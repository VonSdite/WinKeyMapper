using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace KeyMapper.Core;

/// <summary>当前用户的 Windows 开机启动项。</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyMapper";
    private const string TrayArgument = "--tray";

    public static bool IsEnabled()
    {
        string? value = CurrentValue();
        return IsCurrentAppValue(value);
    }

    public static void EnsureTrayArgument()
    {
        string? value = CurrentValue();
        if (IsCurrentAppValue(value) &&
            !value!.Contains(TrayArgument, StringComparison.OrdinalIgnoreCase))
        {
            SetEnabled(true);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ??
                        Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (key is null)
            throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");

        if (enabled)
        {
            string appPath = AppPath;
            if (string.IsNullOrWhiteSpace(appPath))
                throw new InvalidOperationException("无法确定当前程序路径。");

            key.SetValue(ValueName, $"{Quote(appPath)} {TrayArgument}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string AppPath =>
        Environment.ProcessPath ??
        Process.GetCurrentProcess().MainModule?.FileName ??
        string.Empty;

    private static string? CurrentValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) as string;
    }

    private static bool IsCurrentAppValue(string? value)
    {
        string appPath = AppPath;
        return !string.IsNullOrWhiteSpace(value) &&
               !string.IsNullOrWhiteSpace(appPath) &&
               value.Contains(appPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string path) => $"\"{path}\"";
}
