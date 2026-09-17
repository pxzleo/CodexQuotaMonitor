using System.Text.Json;
using CodexQuotaMonitor.Wpf;

var tests = new (string Name, Action Body)[]
{
    ("quota JSON-RPC response parsing", TestQuotaParsing),
    ("weekly-only quota is not mislabeled as 5H", TestWeeklyOnlyQuotaParsing),
    ("settings defaults, JSON load, CLI override, corrupt fallback", TestSettings),
    ("formatting helpers", TestFormatting),
    ("taskbar overlay placement", TestTaskbarPlacement),
    ("quota history prune, save, load", TestQuotaHistory),
    ("argument handling", TestArguments)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
        return 1;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed");
return 0;

static void TestQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "test-limit",
            "limitName": "Test Limit",
            "planType": "plus",
            "primary": {
              "usedPercent": 57.25,
              "windowDurationMins": 300,
              "resetsAt": 4102444800
            },
            "secondary": {
              "usedPercent": 21,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            }
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal(null, snapshot.Error, "quota error");
    Equal("test-limit", snapshot.LimitId, "limit id");
    Near(42.75, snapshot.FiveHour!.RemainingPercent!.Value, 0.001, "5h remaining");
    Near(79.0, snapshot.Weekly!.RemainingPercent!.Value, 0.001, "weekly remaining");
}

static void TestWeeklyOnlyQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "codex",
            "primary": {
              "usedPercent": 25,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            },
            "secondary": null
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal<LimitWindow?>(null, snapshot.FiveHour, "disabled 5h window");
    Near(75.0, snapshot.Weekly!.RemainingPercent!.Value, 0.001, "weekly-only remaining");
    Equal(10080, snapshot.Weekly.WindowMins, "weekly-only duration");
}

static void TestSettings()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "codex-quota-native-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var path = Path.Combine(tempDir, "settings.json");
    try
    {
        var defaults = SettingsStore.Load(path);
        Equal(180, defaults.QuotaInterval, "default quota interval");
        Equal(86400, defaults.TrendWindowSeconds, "default trend window");
        Equal(Constants.PlacementTaskbar, defaults.PlacementEdge, "default placement edge");
        Equal(0, defaults.PlacementOffset, "default placement offset");
        Equal(0, defaults.PlacementOffset2, "default placement offset2");

        File.WriteAllText(path, """
            {
              "quota_interval": 60,
              "no_tray": true,
              "window_width": 336,
              "red_threshold": 10,
              "amber_threshold": 25,
              "trend_window_seconds": 900,
              "placement_edge": "right",
              "placement_offset": 420,
              "placement_offset2": 130
            }
            """);
        var loaded = SettingsStore.Load(path);
        Equal(60, loaded.QuotaInterval, "loaded quota interval");
        Equal(true, loaded.NoTray, "loaded no tray");
        Equal(260, loaded.WindowWidth, "floating-card width migration");
        Equal(900, loaded.TrendWindowSeconds, "loaded trend window");
        Equal(Constants.PlacementRight, loaded.PlacementEdge, "loaded placement edge");
        Equal(420, loaded.PlacementOffset, "loaded placement offset");
        Equal(130, loaded.PlacementOffset2, "loaded placement offset2");

        File.WriteAllText(path, """
            {
              "placement_edge": "middle"
            }
            """);
        var invalidEdge = SettingsStore.Load(path);
        Equal(Constants.PlacementTaskbar, invalidEdge.PlacementEdge, "invalid placement edge normalized");

        File.WriteAllText(path, """
            {
              "trend_window_seconds": 1234
            }
            """);
        var invalidWindow = SettingsStore.Load(path);
        Equal(86400, invalidWindow.TrendWindowSeconds, "invalid trend window normalized");

        var cli = CliOptions.Parse(["--quota-interval", "300", "--tray"]);
        var merged = SettingsStore.ApplyCliOverrides(loaded, cli);
        Equal(300, merged.QuotaInterval, "cli quota override");
        Equal(false, merged.NoTray, "cli tray override");

        File.WriteAllText(path, "{ broken json");
        var fallback = SettingsStore.Load(path);
        Equal(180, fallback.QuotaInterval, "corrupt JSON fallback");
        Equal(86400, fallback.TrendWindowSeconds, "corrupt JSON trend fallback");
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

static void TestFormatting()
{
    Equal("abc", Formatting.Truncate("abc", 10), "truncate short");
    Equal("abcdefg...", Formatting.Truncate("abcdefghijk", 10), "truncate long");
    Equal("--", Formatting.RemainingText(null), "remaining missing");
    Equal("43", Formatting.RemainingText(42.75), "remaining percent");
    Equal("24H", QuotaTrendBlock.WindowLabel(TimeSpan.FromHours(24)), "24h window label");
    Equal("6H", QuotaTrendBlock.WindowLabel(TimeSpan.FromHours(6)), "6h window label");
    Equal("1H", QuotaTrendBlock.WindowLabel(TimeSpan.FromHours(1)), "1h window label");
    Equal("15M", QuotaTrendBlock.WindowLabel(TimeSpan.FromMinutes(15)), "15min window label");
}

static void TestTaskbarPlacement()
{
    var bottomRect = new NativeMethods.RECT { Left = 0, Top = 1032, Right = 1920, Bottom = 1080 };
    var bottom = TaskbarPlacementCalculator.Compute(3, bottomRect, 260, 0, 1920, 1080);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), bottom, "bottom taskbar placement");

    var bottomExpanded = TaskbarPlacementCalculator.Compute(3, bottomRect, 260, 120, 1920, 1080);
    Equal(new TaskbarPlacement(0, 912, 260, 168), bottomExpanded, "expanded bottom taskbar placement");

    var topRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 40 };
    var top = TaskbarPlacementCalculator.Compute(1, topRect, 260, 0, 1920, 1080);
    Equal(new TaskbarPlacement(0, 0, 260, 40), top, "top taskbar height");

    var topExpanded = TaskbarPlacementCalculator.Compute(1, topRect, 260, 120, 1920, 1080);
    Equal(new TaskbarPlacement(0, 40, 260, 160), topExpanded, "expanded top taskbar placement");

    var verticalRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 48, Bottom = 1080 };
    var vertical = TaskbarPlacementCalculator.Compute(0, verticalRect, 260, 0, 1920, 1080);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), vertical, "vertical taskbar fallback");

    var verticalExpanded = TaskbarPlacementCalculator.Compute(0, verticalRect, 260, 120, 1920, 1080);
    Equal(new TaskbarPlacement(0, 912, 260, 168), verticalExpanded, "expanded vertical taskbar fallback");

    var bounds = new System.Drawing.Rectangle(0, 0, 1920, 1080);
    Equal(new TaskbarPlacement(0, 300, 260, 48), TaskbarPlacementCalculator.EdgePlacement(Constants.PlacementLeft, 0, 300, 260, 48, bounds), "left edge placement");
    Equal(new TaskbarPlacement(1660, 300, 260, 48), TaskbarPlacementCalculator.EdgePlacement(Constants.PlacementRight, 0, 300, 260, 48, bounds), "right edge placement");
    Equal(new TaskbarPlacement(500, 0, 260, 48), TaskbarPlacementCalculator.EdgePlacement(Constants.PlacementTop, 500, 0, 260, 48, bounds), "top edge placement");
    Equal(new TaskbarPlacement(500, 1032, 260, 48), TaskbarPlacementCalculator.EdgePlacement(Constants.PlacementBottom, 500, 0, 260, 48, bounds), "bottom edge placement");
    Equal(new TaskbarPlacement(1660, 1032, 260, 48), TaskbarPlacementCalculator.EdgePlacement(Constants.PlacementRight, 0, 99999, 260, 48, bounds), "right edge offset clamp");
    Equal(new TaskbarPlacement(700, 500, 260, 48), TaskbarPlacementCalculator.EdgePlacement(Constants.PlacementFree, 700, 500, 260, 48, bounds), "free placement");

    Equal(Constants.PlacementLeft, TaskbarPlacementCalculator.ResolveSnapEdge(0, 400, 260, 448, 1920, 1080, 48), "snap left edge");
    Equal(Constants.PlacementRight, TaskbarPlacementCalculator.ResolveSnapEdge(1660, 400, 1920, 448, 1920, 1080, 48), "snap right edge");
    Equal(Constants.PlacementTop, TaskbarPlacementCalculator.ResolveSnapEdge(800, 10, 1060, 58, 1920, 1080, 48), "snap top edge");
    Equal(Constants.PlacementBottom, TaskbarPlacementCalculator.ResolveSnapEdge(800, 1022, 1060, 1080, 1920, 1080, 48), "snap bottom edge");
    Equal(Constants.PlacementFree, TaskbarPlacementCalculator.ResolveSnapEdge(800, 400, 1060, 448, 1920, 1080, 48), "center stays free");
    Equal(Constants.PlacementFree, TaskbarPlacementCalculator.ResolveSnapEdge(49, 400, 309, 448, 1920, 1080, 48), "just outside snap distance");
}

static void TestQuotaHistory()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "codex-quota-native-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var path = Path.Combine(tempDir, "history.json");
    try
    {
        var now = DateTimeOffset.Now;
        var history = new QuotaHistory();
        history.AddSample(new TrendSample(now.AddHours(-30), 90.0, null));
        history.AddSample(new TrendSample(now.AddHours(-24), 80.0, 40.0));
        history.AddSample(new TrendSample(now.AddMinutes(-5), 75.5, null));
        history.Save(path);

        var loaded = QuotaHistory.Load(path);
        Equal(2, loaded.Samples.Count, "pruned old samples");
        Equal(80.0, loaded.Samples[0].WeeklyRemaining, "weekly value round-trip");
        Equal(40.0, loaded.Samples[0].FiveHourRemaining, "5h value round-trip");
        Equal(null, loaded.Samples[1].FiveHourRemaining, "null 5h round-trip");

        var empty = QuotaHistory.Load(Path.Combine(tempDir, "missing.json"));
        Equal(0, empty.Samples.Count, "missing history file");
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

static void TestArguments()
{
    var options = CliOptions.Parse([
        "--check",
        "--codex-home", "C:\\CodexHome\\.codex",
        "--codex-exe", "C:\\Tools\\codex.exe",
        "--quota-interval", "600",
        "--no-tray"
    ]);
    Equal(true, options.Check, "check flag");
    Equal(false, options.Once, "once flag");
    Equal("C:\\CodexHome\\.codex", options.CodexHome, "codex home");
    Equal("C:\\Tools\\codex.exe", options.CodexExe, "codex exe");
    Equal(600, options.QuotaInterval, "quota interval");
    Equal(true, options.NoTray, "no tray");

    var tray = CliOptions.Parse(["--tray"]);
    Equal(false, tray.NoTray, "tray override");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void Near(double expected, double actual, double tolerance, string label)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
