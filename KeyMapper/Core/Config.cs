using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KeyMapper.Core;

public sealed class Mapping
{
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class AppConfig
{
    public List<Mapping> Mappings { get; set; } = new();

    /// <summary>上次关闭时改键是否开启，用于下次启动恢复开关状态。</summary>
    public bool Active { get; set; } = true;
}

/// <summary>配置持久化：存于 %APPDATA%\KeyMapper\config.json。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyMapper");

    public static string FilePath => Path.Combine(Dir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppConfig();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOpts));
        }
        catch
        {
            // 写入失败不致崩溃
        }
    }
}
