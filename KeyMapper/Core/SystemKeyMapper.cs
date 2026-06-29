using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace KeyMapper.Core;

public readonly record struct ElevatedCommandResult(bool Started, bool Cancelled, int ExitCode);

public enum SystemMapClearResult
{
    Cleared,
    NoOwnedMap,
    CurrentMapChanged,
}

public static class SystemKeyMapper
{
    public const string ApplyArgument = "--apply-system-map";
    public const string ClearArgument = "--clear-system-map";
    public const int SuccessExitCode = 0;
    public const int ErrorExitCode = 1;
    public const int NoMappingsExitCode = 2;
    public const int NoOwnedMapExitCode = 3;
    public const int CurrentMapChangedExitCode = 4;

    private const string KeyboardLayoutKeyPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
    private const string ScancodeMapValueName = "Scancode Map";
    private const string AppliedMapValueName = "KeyMapper Applied Scancode Map";
    private const string BackupMapValueName = "KeyMapper Backup Scancode Map";
    private const string BackupPresentValueName = "KeyMapper Backup Scancode Map Present";
    private const uint MAPVK_VK_TO_VSC = 0;
    private const int VK_PAUSE = 0x13;
    private const ushort ExtendedPrefix = 0xE000;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public static bool TryRunStartupCommand(string[] args, out int exitCode)
    {
        int applyIndex = Array.FindIndex(args, IsApplyArgument);
        if (applyIndex >= 0)
        {
            exitCode = RunApplyCommand(ConfigPathArg(args, applyIndex));
            return true;
        }

        if (args.Any(IsClearArgument))
        {
            exitCode = RunClearCommand();
            return true;
        }

        exitCode = SuccessExitCode;
        return false;
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool HasSystemMap()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyboardLayoutKeyPath, false);
            return key?.GetValue(ScancodeMapValueName) is byte[];
        }
        catch
        {
            return false;
        }
    }

    public static int CountConfigMappings(AppConfig config) => BuildEntries(config).Count;

    public static int ApplyConfig(AppConfig config)
    {
        var entries = BuildEntries(config);
        if (entries.Count == 0)
            return 0;

        byte[] nextValue = BuildRegistryValue(entries);
        using var key = Registry.LocalMachine.CreateSubKey(KeyboardLayoutKeyPath, true)
            ?? throw new InvalidOperationException("Cannot open the keyboard layout registry key.");
        byte[]? currentValue = ReadBinaryValue(key, ScancodeMapValueName);
        byte[]? appliedValue = ReadBinaryValue(key, AppliedMapValueName);

        if (!BytesEqual(currentValue, appliedValue))
            SaveBackup(key, currentValue);

        key.SetValue(ScancodeMapValueName, nextValue, RegistryValueKind.Binary);
        key.SetValue(AppliedMapValueName, nextValue, RegistryValueKind.Binary);
        return entries.Count;
    }

    public static SystemMapClearResult Clear()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyboardLayoutKeyPath, true);
        if (key == null)
            return SystemMapClearResult.Cleared;

        byte[]? currentValue = ReadBinaryValue(key, ScancodeMapValueName);
        byte[]? appliedValue = ReadBinaryValue(key, AppliedMapValueName);
        if (appliedValue == null)
            return currentValue == null ? SystemMapClearResult.Cleared : SystemMapClearResult.NoOwnedMap;

        if (!BytesEqual(currentValue, appliedValue))
            return SystemMapClearResult.CurrentMapChanged;

        if (BackupWasPresent(key))
        {
            byte[]? backupValue = ReadBinaryValue(key, BackupMapValueName);
            if (backupValue == null)
                return SystemMapClearResult.CurrentMapChanged;

            key.SetValue(ScancodeMapValueName, backupValue, RegistryValueKind.Binary);
        }
        else
        {
            key.DeleteValue(ScancodeMapValueName, false);
        }

        ClearMetadata(key);
        return SystemMapClearResult.Cleared;
    }

    public static ElevatedCommandResult RunElevatedAndWait(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = CurrentExecutablePath(),
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return new ElevatedCommandResult(false, false, ErrorExitCode);

            process.WaitForExit();
            return new ElevatedCommandResult(true, false, process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ElevatedCommandResult(false, true, ErrorExitCode);
        }
    }

    public static string ApplyArgumentsForConfig(string configPath) =>
        $"{ApplyArgument} {Quote(configPath)}";

    private static int RunApplyCommand(string? configPath)
    {
        try
        {
            var config = string.IsNullOrWhiteSpace(configPath)
                ? ConfigStore.Load()
                : ConfigStore.LoadFrom(configPath);
            return ApplyConfig(config) > 0 ? SuccessExitCode : NoMappingsExitCode;
        }
        catch
        {
            return ErrorExitCode;
        }
    }

    private static int RunClearCommand()
    {
        try
        {
            return Clear() switch
            {
                SystemMapClearResult.Cleared => SuccessExitCode,
                SystemMapClearResult.NoOwnedMap => NoOwnedMapExitCode,
                SystemMapClearResult.CurrentMapChanged => CurrentMapChangedExitCode,
                _ => ErrorExitCode,
            };
        }
        catch
        {
            return ErrorExitCode;
        }
    }

    private static Dictionary<ushort, ushort> BuildEntries(AppConfig config)
    {
        var entries = new Dictionary<ushort, ushort>();
        foreach (var mapping in config.Mappings)
        {
            if (!mapping.Enabled)
                continue;

            var source = Keys.ById(mapping.SourceId);
            var target = Keys.ById(mapping.TargetId);
            if (source == null || target == null || source.Vk == target.Vk)
                continue;

            ushort sourceScan = ToRegistryScancode(source);
            ushort targetScan = ToRegistryScancode(target);
            if (sourceScan != targetScan)
                entries[sourceScan] = targetScan;
        }

        return entries;
    }

    private static ushort ToRegistryScancode(KeyDef key)
    {
        if (key.Vk == VK_PAUSE)
            throw new NotSupportedException("系统级映射不支持 Pause/Break。");

        uint scan = MapVirtualKey((uint)key.Vk, MAPVK_VK_TO_VSC);
        if (scan == 0)
            throw new NotSupportedException($"系统级映射不支持 {key.Display}。");

        return (ushort)((key.IsExtended ? ExtendedPrefix : 0) | (ushort)(scan & 0xFF));
    }

    private static byte[] BuildRegistryValue(Dictionary<ushort, ushort> entries)
    {
        byte[] bytes = new byte[12 + entries.Count * 4 + 4];
        WriteUInt32(bytes, 8, (uint)entries.Count + 1);

        int offset = 12;
        foreach (var (source, target) in entries)
        {
            WriteUInt32(bytes, offset, target | ((uint)source << 16));
            offset += 4;
        }

        return bytes;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static byte[]? ReadBinaryValue(RegistryKey key, string valueName) =>
        key.GetValue(valueName) as byte[];

    private static void SaveBackup(RegistryKey key, byte[]? currentValue)
    {
        if (currentValue == null)
        {
            key.SetValue(BackupPresentValueName, 0, RegistryValueKind.DWord);
            key.DeleteValue(BackupMapValueName, false);
            return;
        }

        key.SetValue(BackupPresentValueName, 1, RegistryValueKind.DWord);
        key.SetValue(BackupMapValueName, currentValue, RegistryValueKind.Binary);
    }

    private static bool BackupWasPresent(RegistryKey key) =>
        key.GetValue(BackupPresentValueName) is int value && value != 0;

    private static void ClearMetadata(RegistryKey key)
    {
        key.DeleteValue(AppliedMapValueName, false);
        key.DeleteValue(BackupMapValueName, false);
        key.DeleteValue(BackupPresentValueName, false);
    }

    private static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left == null || right == null)
            return left == right;
        if (left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static bool IsApplyArgument(string arg) =>
        string.Equals(arg, ApplyArgument, StringComparison.OrdinalIgnoreCase);

    private static bool IsClearArgument(string arg) =>
        string.Equals(arg, ClearArgument, StringComparison.OrdinalIgnoreCase);

    private static string? ConfigPathArg(string[] args, int applyIndex) =>
        applyIndex + 1 < args.Length && !args[applyIndex + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[applyIndex + 1]
            : null;

    private static string CurrentExecutablePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Cannot locate the current executable.");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
