using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EyeCare.Services;

/// <summary>
/// 每日健康用眼统计数据。
/// </summary>
public class DailyHealthRecord
{
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    /// <summary>屏幕使用总时长（分钟）。</summary>
    public int ScreenMinutes { get; set; }
    /// <summary>开启蓝光过滤的时长（分钟）。</summary>
    public int FilterMinutes { get; set; }
    /// <summary>按时完成的休息次数。</summary>
    public int CompletedBreaks { get; set; }
    /// <summary>跳过的休息次数。</summary>
    public int SkippedBreaks { get; set; }
}

/// <summary>
/// 持久化数据根对象。
/// </summary>
public class HealthStatsData
{
    public Dictionary<string, DailyHealthRecord> Records { get; set; } = new();
}

/// <summary>
/// 本地健康用眼统计存储（纯本地 JSON，零联网，保护隐私）。
/// </summary>
public class EyeHealthStatsStore
{
    private static readonly string StatsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EyeCare", "stats.json");

    private HealthStatsData _data = new();
    private readonly object _lock = new();

    public event Action? StatsUpdated;

    public EyeHealthStatsStore()
    {
        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(StatsFilePath))
                {
                    string json = File.ReadAllText(StatsFilePath);
                    _data = JsonSerializer.Deserialize<HealthStatsData>(json) ?? new();
                }
            }
            catch
            {
                _data = new();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatsFilePath)!);
                string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StatsFilePath, json);
            }
            catch
            {
            }
        }
        StatsUpdated?.Invoke();
    }

    private DailyHealthRecord GetOrCreateToday()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!_data.Records.TryGetValue(today, out var record))
        {
            record = new DailyHealthRecord { Date = today };
            _data.Records[today] = record;
        }
        return record;
    }

    public DailyHealthRecord GetTodayRecord()
    {
        lock (_lock)
        {
            return GetOrCreateToday();
        }
    }

    public void RecordScreenMinute(bool filterActive)
    {
        lock (_lock)
        {
            var r = GetOrCreateToday();
            r.ScreenMinutes++;
            if (filterActive) r.FilterMinutes++;
        }
        Save();
    }

    public void RecordCompletedBreak()
    {
        lock (_lock)
        {
            var r = GetOrCreateToday();
            r.CompletedBreaks++;
        }
        Save();
    }

    public void RecordSkippedBreak()
    {
        lock (_lock)
        {
            var r = GetOrCreateToday();
            r.SkippedBreaks++;
        }
        Save();
    }

    /// <summary>
    /// 计算连续健康用眼打卡天数（每天屏幕使用 >= 15 分钟且完成休息 >= 1 次）。
    /// </summary>
    public int CalculateStreak()
    {
        lock (_lock)
        {
            int streak = 0;
            DateTime cur = DateTime.Today;

            while (true)
            {
                string key = cur.ToString("yyyy-MM-dd");
                if (_data.Records.TryGetValue(key, out var r) && r.ScreenMinutes >= 15 && r.CompletedBreaks >= 1)
                {
                    streak++;
                    cur = cur.AddDays(-1);
                }
                else if (cur == DateTime.Today)
                {
                    // 今天如果还在进行中（尚未达到 15 分钟 + 1 次休息），不截断历史连续打卡，继续从昨天往前统计
                    cur = cur.AddDays(-1);
                }
                else
                {
                    break;
                }
            }
            return streak;
        }
    }

    /// <summary>
    /// 获取最近 7 天的统计数据（按日期从旧到新升序排列）。
    /// </summary>
    public List<DailyHealthRecord> GetLast7Days()
    {
        lock (_lock)
        {
            var list = new List<DailyHealthRecord>();
            for (int i = 6; i >= 0; i--)
            {
                var dt = DateTime.Today.AddDays(-i);
                string key = dt.ToString("yyyy-MM-dd");
                if (_data.Records.TryGetValue(key, out var r))
                {
                    list.Add(r);
                }
                else
                {
                    list.Add(new DailyHealthRecord { Date = key });
                }
            }
            return list;
        }
    }
}
