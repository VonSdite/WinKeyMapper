using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace KeyMapper.Core;

/// <summary>当前用户的 Windows 开机启动项。</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyMapper";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        string? value = key?.GetValue(ValueName) as string;
        string appPath = AppPath;
        return !string.IsNullOrWhiteSpace(value) &&
               !string.IsNullOrWhiteSpace(appPath) &&
               value.Contains(appPath, StringComparison.OrdinalIgnoreCase);
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

            key.SetValue(ValueName, Quote(appPath), RegistryValueKind.String);
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

    private static string Quote(string path) => $"\"{path}\"";
}
