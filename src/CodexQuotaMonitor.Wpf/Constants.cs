namespace CodexQuotaMonitor.Wpf;

public static class Constants
{
    public const string AppName = "Codex Quota Monitor Native";
    public const string WindowTitlePrefix = "Codex Quota Monitor Native";
    public const string MutexName = "Local\\CodexQuotaMonitorNative";
    public const int DefaultQuotaIntervalSeconds = 180;
    public const int DefaultWidth = 260;
    public const int DefaultHeight = 48;
    public const int CurvePanelHeight = 120;
    public const int DefaultTrendWindowSeconds = 24 * 60 * 60;
    public const string PlacementTaskbar = "taskbar";
    public const string PlacementLeft = "left";
    public const string PlacementRight = "right";
    public const string PlacementTop = "top";
    public const string PlacementBottom = "bottom";
    public const string PlacementFree = "free";
    public static readonly string[] PlacementEdges = [PlacementTaskbar, PlacementLeft, PlacementRight, PlacementTop, PlacementBottom, PlacementFree];
    public const int EdgeSnapDistance = 48;
    public const double DefaultRedThreshold = 15.0;
    public const double DefaultAmberThreshold = 30.0;

    public static readonly (string Label, int Seconds)[] TrendWindowOptions =
    [
        ("24h", 24 * 60 * 60),
        ("6h", 6 * 60 * 60),
        ("1h", 60 * 60),
        ("15min", 15 * 60)
    ];
}
