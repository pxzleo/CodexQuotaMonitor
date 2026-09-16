using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaMonitor.Wpf;

public sealed class QuotaHistory
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24) + TimeSpan.FromMinutes(30);

    private readonly List<TrendSample> _samples = new();

    public IReadOnlyList<TrendSample> Samples => _samples;

    public void AddSample(TrendSample sample)
    {
        _samples.Add(sample);
        Prune();
    }

    public void Prune(DateTimeOffset? reference = null)
    {
        var cutoff = (reference ?? DateTimeOffset.Now) - Retention;
        while (_samples.Count > 0 && _samples[0].At < cutoff)
        {
            _samples.RemoveAt(0);
        }
    }

    public static QuotaHistory Load(string path, SimpleLogger? logger = null)
    {
        var history = new QuotaHistory();
        if (!File.Exists(path))
        {
            return history;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<SampleEntry>>(File.ReadAllText(path));
            if (entries is not null)
            {
                foreach (var entry in entries)
                {
                    if (entry.At is not null)
                    {
                        history._samples.Add(new TrendSample(entry.At.Value, entry.Wk, entry.H5));
                    }
                }
            }
            history.Prune();
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to load quota history from {path}: {ex.Message}");
        }

        return history;
    }

    public void Save(string path, SimpleLogger? logger = null)
    {
        try
        {
            Prune();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var entries = _samples
                .Select(sample => new SampleEntry { At = sample.At, Wk = sample.WeeklyRemaining, H5 = sample.FiveHourRemaining })
                .ToArray();
            File.WriteAllText(path, JsonSerializer.Serialize(entries) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to save quota history to {path}: {ex.Message}");
        }
    }

    private sealed class SampleEntry
    {
        [JsonPropertyName("at")]
        public DateTimeOffset? At { get; set; }

        [JsonPropertyName("wk")]
        public double? Wk { get; set; }

        [JsonPropertyName("h5")]
        public double? H5 { get; set; }
    }
}