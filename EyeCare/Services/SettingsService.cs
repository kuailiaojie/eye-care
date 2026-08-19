using System;
using System.IO;
using System.Text.Json;
using EyeCare.Models;

namespace EyeCare.Services;

/// <summary>
/// 负责加载 / 保存设置到本地 JSON 文件。
/// 文件路径：%LOCALAPPDATA%\EyeCare\settings.json
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EyeCare");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public AppSettings Data { get; private set; } = new();

    /// <summary>设置发生变化时触发。</summary>
    public event Action? SettingsChanged;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded is not null)
                {
                    // 1.0 was the old default and made enabling brightness appear to do nothing.
                    // Migrate untouched legacy settings to a useful visible starting value.
                    if (!loaded.BrightnessEnabled && loaded.Brightness >= 0.999)
                        loaded.Brightness = 0.85;
                    Data = loaded;
                }
            }
        }
        catch
        {
            // 加载失败时使用默认值，不崩溃。
            Data = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Data, _jsonOptions);
            File.WriteAllText(SettingsPath, json);
            SettingsChanged?.Invoke();
        }
        catch
        {
            // 保存失败不阻塞应用。
        }
    }
}