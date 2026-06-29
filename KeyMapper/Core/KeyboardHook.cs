using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KeyMapper.Core;

/// <summary>
/// 底层键盘钩子（WH_KEYBOARD_LL）：改键 + 按键捕获。
/// 钩子运行在安装它的线程（UI 线程）上，借 WPF 消息循环驱动，无需额外依赖。
///
/// 改键策略：吞掉源键事件，用 SendInput 按扫描码注入目标键。注入时附带
/// KEYEVENTF_EXTENDEDKEY（旧实现扫描码恒为 0，导致依赖扫描码的程序收不到目标键）。
/// 注入事件带 LLKHF_INJECTED 标志，回调中跳过，避免循环。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const int VK_PAUSE = 0x13;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    // 联合体必须包含全部三个成员，使 Marshal.SizeOf<INPUT>() 与原生 sizeof(INPUT) 一致
    // （x64 = 40，x86 = 28）。曾因只放 KEYBDINPUT 导致 cbSize 偏小，SendInput 静默失败。
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>目标键的预计算信息，供回调零查找地注入。</summary>
    private readonly record struct Target(int Vk, int Scan, bool Extended);

    private readonly LowLevelKeyboardProc _proc; // 持有委托防止 GC 回收
    private IntPtr _hook = IntPtr.Zero;
    private readonly int _inputSize = Marshal.SizeOf<INPUT>();
    private readonly INPUT[] _inputBuf = new INPUT[1];

    private readonly Dictionary<int, Target> _remap = new(); // srcVk -> 目标键
    private readonly HashSet<int> _downTarget = new();       // 当前因改键「按下」的目标键
    private bool _remapping;
    private bool _capturing;
    private int _capturedVk; // 刚捕获的源键，用于吞掉其 keyup
    private bool _pausedWasRemapping; // Pause 前的状态，供 Resume 恢复

    public bool IsInstalled => _hook != IntPtr.Zero;
    public bool IsRemapping => _remapping;

    /// <summary>捕获到按键时触发（捕获模式下按下的第一个物理键）。未收录键为 null。</summary>
    public event EventHandler<KeyDef?>? KeyCaptured;

    public KeyboardHook()
    {
        _proc = HookCallback;
        // 守卫：INPUT 托管大小必须与原生 sizeof(INPUT) 一致，否则 SendInput 静默失败。
        int expected = IntPtr.Size == 8 ? 40 : 28;
        if (Marshal.SizeOf<INPUT>() != expected)
            throw new InvalidOperationException(
                $"INPUT 结构大小 {Marshal.SizeOf<INPUT>()} 与原生 sizeof(INPUT)={expected} 不符，SendInput 将失败。");
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>设置改键表（srcVk -> dstVk）。会先释放当前按住的目标键以防卡键。</summary>
    public void SetRemap(IReadOnlyDictionary<int, int> srcToDst)
    {
        ReleaseAllHeld();
        _remap.Clear();
        foreach (var (s, d) in srcToDst)
        {
            _remap[s] = new Target(d, (int)MapVirtualKey((uint)d, MAPVK_VK_TO_VSC), IsExtendedVk(d));
        }
        _remapping = _remap.Count > 0;
    }

    /// <summary>停止改键并释放按住的键。</summary>
    public void Stop()
    {
        ReleaseAllHeld();
        _remap.Clear();
        _remapping = false;
    }

    /// <summary>临时暂停改键（如打开映射对话框时，避免在搜索框里打出被改的键）。</summary>
    public void Pause()
    {
        _pausedWasRemapping = _remapping;
        ReleaseAllHeld();
        _remapping = false;
    }

    /// <summary>恢复 Pause 前的改键状态。</summary>
    public void Resume() => _remapping = _pausedWasRemapping;

    public void BeginCapture()
    {
        _capturedVk = 0;
        _capturing = true;
    }

    public void CancelCapture()
    {
        _capturing = false;
        _capturedVk = 0;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (k.flags & LLKHF_INJECTED) != 0;
            int msg = wParam.ToInt32();
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            // 捕获：吞掉第一个物理按键的 down，并随后吞掉其 up，避免向应用泄漏半截事件
            if (!injected && _capturing && down)
            {
                _capturing = false;
                _capturedVk = (int)k.vkCode;
                KeyCaptured?.Invoke(this, Keys.ByVk(_capturedVk));
                return (IntPtr)1;
            }
            if (!injected && up && _capturedVk != 0 && (int)k.vkCode == _capturedVk)
            {
                _capturedVk = 0;
                return (IntPtr)1;
            }

            // 改键：吞掉源键，注入目标键
            if (!injected && _remapping && _remap.TryGetValue((int)k.vkCode, out var t))
            {
                if (down)
                {
                    _downTarget.Add(t.Vk);
                    SendKey(t, down: true);
                    return (IntPtr)1;
                }
                if (up)
                {
                    _downTarget.Remove(t.Vk);
                    SendKey(t, down: false);
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void SendKey(Target t, bool down)
    {
        bool useScancode = t.Scan != 0 && t.Vk != VK_PAUSE;
        _inputBuf[0].type = INPUT_KEYBOARD;
        _inputBuf[0].u.ki = new KEYBDINPUT
        {
            wVk = useScancode ? (ushort)0 : (ushort)t.Vk,
            wScan = (ushort)t.Scan,
            dwFlags = (useScancode ? KEYEVENTF_SCANCODE : 0u)
                | (down ? 0u : KEYEVENTF_KEYUP)
                | (t.Extended ? KEYEVENTF_EXTENDEDKEY : 0u),
            time = 0u,
            dwExtraInfo = IntPtr.Zero,
        };
        SendInput(1, _inputBuf, _inputSize);
    }

    /// <summary>停止/切换改键时，对仍按住的目标键补发 keyup，防止目标键卡住。</summary>
    private void ReleaseAllHeld()
    {
        foreach (var vk in _downTarget)
            SendKey(new Target(vk, (int)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC), IsExtendedVk(vk)), down: false);
        _downTarget.Clear();
    }

    /// <summary>判断虚拟键码是否为扩展键（决定 KEYEVENTF_EXTENDEDKEY）。</summary>
    private static bool IsExtendedVk(int vk) => vk switch
    {
        0xA3 or 0xA5 or 0x5B or 0x5C => true,   // 右Ctrl / 右Alt / 左Win / 右Win
        0x24 or 0x23 or 0x21 or 0x22 => true,   // Home / End / PageUp / PageDown
        0x26 or 0x28 or 0x25 or 0x27 => true,   // 上 / 下 / 左 / 右
        0x2D or 0x2E or 0x2C or 0x6F => true,   // Insert / Delete / PrintScreen / 小键盘 /
        _ => false,
    };

    public void Dispose()
    {
        Stop();
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
