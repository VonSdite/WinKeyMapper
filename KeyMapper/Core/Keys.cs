using System.Collections.Generic;

namespace KeyMapper.Core;

/// <summary>一个按键的定义：内部 Id、界面显示名、Windows 虚拟键码、是否扩展键、分类。</summary>
public sealed record KeyDef(string Id, string Display, int Vk, bool IsExtended, string Category);

/// <summary>按键目录。Id 用小写，便于配置可读与编辑。</summary>
public static class Keys
{
    public static IReadOnlyList<KeyDef> All { get; }

    private static readonly Dictionary<string, KeyDef> _byId;
    private static readonly Dictionary<int, KeyDef> _byVk;

    static Keys()
    {
        var list = new List<KeyDef>
        {
            // 修饰键
            new("left ctrl", "左 Ctrl", 0xA2, false, "修饰"),
            new("right ctrl", "右 Ctrl", 0xA3, true, "修饰"),
            new("left alt", "左 Alt", 0xA4, false, "修饰"),
            new("right alt", "右 Alt", 0xA5, true, "修饰"),
            new("left shift", "左 Shift", 0xA0, false, "修饰"),
            new("right shift", "右 Shift", 0xA1, false, "修饰"),
            new("left win", "左 Win", 0x5B, true, "修饰"),
            new("right win", "右 Win", 0x5C, true, "修饰"),
            new("caps lock", "Caps Lock", 0x14, false, "修饰"),
            new("scroll lock", "Scroll Lock", 0x91, false, "修饰"),
            new("num lock", "Num Lock", 0x90, false, "修饰"),
            // 导航 / 编辑
            new("home", "Home", 0x24, true, "导航"),
            new("end", "End", 0x23, true, "导航"),
            new("page up", "Page Up", 0x21, true, "导航"),
            new("page down", "Page Down", 0x22, true, "导航"),
            new("up", "上 ↑", 0x26, true, "导航"),
            new("down", "下 ↓", 0x28, true, "导航"),
            new("left", "左 ←", 0x25, true, "导航"),
            new("right", "右 →", 0x27, true, "导航"),
            new("insert", "Insert", 0x2D, true, "导航"),
            new("delete", "Delete", 0x2E, true, "导航"),
            new("backspace", "Backspace", 0x08, false, "导航"),
            new("tab", "Tab", 0x09, false, "导航"),
            new("enter", "Enter", 0x0D, false, "导航"),
            new("space", "Space", 0x20, false, "导航"),
            new("esc", "Esc", 0x1B, false, "导航"),
            new("print screen", "PrtSc", 0x2C, true, "导航"),
            new("pause", "Pause", 0x13, false, "导航"),
        };
        for (int i = 1; i <= 24; i++) // F1..F24（VK_F1 = 0x70）
            list.Add(new KeyDef($"f{i}", $"F{i}", 0x6F + i, false, "功能"));
        for (int i = 0; i <= 9; i++) // 小键盘 0..9
            list.Add(new KeyDef($"numpad {i}", $"小键盘 {i}", 0x60 + i, false, "小键盘"));
        list.Add(new KeyDef("numpad .", "小键盘 .", 0x6E, false, "小键盘"));
        list.Add(new KeyDef("numpad +", "小键盘 +", 0x6B, false, "小键盘"));
        list.Add(new KeyDef("numpad -", "小键盘 -", 0x6D, false, "小键盘"));
        list.Add(new KeyDef("numpad *", "小键盘 *", 0x6A, false, "小键盘"));
        list.Add(new KeyDef("numpad /", "小键盘 /", 0x6F, true, "小键盘"));
        for (char c = 'a'; c <= 'z'; c++) // 字母
            list.Add(new KeyDef($"{c}", $"字母 {char.ToUpper(c)}", 0x41 + (c - 'a'), false, "字母"));
        for (int i = 0; i <= 9; i++) // 数字
            list.Add(new KeyDef($"{i}", $"数字 {i}", 0x30 + i, false, "数字"));

        All = list;
        _byId = new Dictionary<string, KeyDef>(list.Count);
        _byVk = new Dictionary<int, KeyDef>(list.Count);
        foreach (var k in list)
        {
            _byId[k.Id] = k;
            // 同一 Vk 可能被多个定义共享（如扩展/非扩展），保留首个即可
            if (!_byVk.ContainsKey(k.Vk))
                _byVk[k.Vk] = k;
        }
    }

    public static KeyDef? ById(string? id) =>
        id != null && _byId.TryGetValue(id, out var k) ? k : null;

    public static KeyDef? ByVk(int vk) =>
        _byVk.TryGetValue(vk, out var k) ? k : null;
}
