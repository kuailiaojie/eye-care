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

    private System.Threading.Timer? _saveDebounceTimer;
    private readonly object _saveLock = new();

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

    /// <summary>
    /// 请求保存设置。立即触发 SettingsChanged 通知前端，并以 400ms 防抖写入磁盘，
    /// 避免拖动滑块时产生高频磁盘 I/O。
    /// </summary>
    public void Save()
    {
        SettingsChanged?.Invoke();

        lock (_saveLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new System.Threading.Timer(_ => SaveImmediate(), null, 400, System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>立即同步写入磁盘（在程序退出前调用）。</summary>
    public void SaveImmediate()
    {
        lock (_saveLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(Data, _jsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 保存失败不阻塞应用。
            }
        }
    }
}