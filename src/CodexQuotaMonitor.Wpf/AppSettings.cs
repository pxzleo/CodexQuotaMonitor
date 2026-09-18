using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaMonitor.Wpf;

public sealed class AppSettings
{
    [JsonPropertyName("quota_interval")]
    public int QuotaInterval { get; set; } = Constants.DefaultQuotaIntervalSeconds;

    [JsonPropertyName("no_tray")]
    public bool NoTray { get; set; }

    [JsonPropertyName("window_width")]
    public int WindowWidth { get; set; } = Constants.DefaultWidth;

    [JsonPropertyName("trend_window_seconds")]
    public int TrendWindowSeconds { get; set; } = Constants.DefaultTrendWindowSeconds;

    [JsonPropertyName("placement_edge")]
    public string PlacementEdge { get; set; } = Constants.PlacementTaskbar;

    [JsonPropertyName("placement_offset")]
    public int PlacementOffset { get; set; }

    [JsonPropertyName("placement_offset2")]
    public int PlacementOffset2 { get; set; }

    [JsonPropertyName("startup")]
    public bool Startup { get; set; }

    [JsonPropertyName("red_threshold")]
    public double RedThreshold { get; set; } = Constants.DefaultRedThreshold;

    [JsonPropertyName("amber_threshold")]
    public double AmberThreshold { get; set; } = Constants.DefaultAmberThreshold;

    public AppSettings Clone() => new()
    {
        QuotaInterval = QuotaInterval,
        NoTray = NoTray,
        WindowWidth = WindowWidth,
        TrendWindowSeconds = TrendWindowSeconds,
        PlacementEdge = PlacementEdge,
        PlacementOffset = PlacementOffset,
        PlacementOffset2 = PlacementOffset2,
        Startup = Startup,
        RedThreshold = RedThreshold,
        AmberThreshold = AmberThreshold
    };

    public void Normalize()
    {
        QuotaInterval = Math.Max(30, QuotaInterval);
        WindowWidth = WindowWidth is < 220 or > 300
            ? Constants.DefaultWidth
            : WindowWidth;
        TrendWindowSeconds = Constants.TrendWindowOptions.Any(option => option.Seconds == TrendWindowSeconds)
            ? TrendWindowSeconds
            : Constants.DefaultTrendWindowSeconds;
        PlacementEdge = Constants.PlacementEdges.Contains(PlacementEdge)
            ? PlacementEdge
            : Constants.PlacementTaskbar;
        PlacementOffset = Math.Max(0, PlacementOffset);
        PlacementOffset2 = Math.Max(0, PlacementOffset2);
        RedThreshold = Math.Clamp(RedThreshold, 0.0, 100.0);
        AmberThreshold = Math.Clamp(AmberThreshold, RedThreshold, 100.0);
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static AppSettings Load(string path, SimpleLogger? logger = null)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                           ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to load settings from {path}: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings, SimpleLogger? logger = null)
    {
        try
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to save settings to {path}: {ex.Message}");
        }
    }

    public static AppSettings ApplyCliOverrides(AppSettings settings, CliOptions options)
    {
        var merged = settings.Clone();
        if (options.QuotaInterval.HasValue)
        {
            merged.QuotaInterval = options.QuotaInterval.Value;
        }
        if (options.NoTray.HasValue)
        {
            merged.NoTray = options.NoTray.Value;
        }
        merged.Normalize();
        return merged;
    }

    public static void EnsureDefault(string path, SimpleLogger? logger = null)
    {
        if (!File.Exists(path))
        {
            Save(path, new AppSettings(), logger);
        }
    }
}
