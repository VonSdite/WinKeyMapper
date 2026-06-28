using System;
using System.Windows.Forms;

namespace KeyMapper.Core;

/// <summary>
/// 系统托盘图标（WinForms NotifyIcon）。图标由 <see cref="AppIcon"/> 绘制。
/// 关闭主窗口即收起到托盘；左键双击显示窗口，右键菜单「显示主窗口 / 退出」。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _ni;

    public event Action? ShowRequested;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        _ni = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Visible = true,
            Text = "键盘按键映射工具",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => QuitRequested?.Invoke());
        _ni.ContextMenuStrip = menu;
        _ni.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void SetTooltip(string text) =>
        _ni.Text = text.Length > 63 ? text[..63] : text;

    public void Show() => _ni.Visible = true;
    public void Hide() => _ni.Visible = false;

    public void Dispose()
    {
        _ni.Visible = false;
        _ni.Dispose();
    }
}
