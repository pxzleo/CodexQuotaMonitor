using System.Text;
using System.Text.Json;
using System.Windows;

namespace CodexQuotaMonitor.Wpf;

public partial class App : System.Windows.Application
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    private SingleInstanceGuard? _instanceGuard;
    private SimpleLogger? _logger;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths paths;
        CliOptions options;
        AppSettings settings;
        try
        {
            options = CliOptions.Parse(e.Args);
            paths = AppPaths.Discover();
            _logger = new SimpleLogger(paths.LogPath);
            settings = SettingsStore.ApplyCliOverrides(SettingsStore.Load(paths.SettingsPath, _logger), options);
        }
        catch (Exception ex)
        {
            AttachConsoleIfPossible();
            Console.Error.WriteLine($"startup failed: {ex.Message}");
            Shutdown(2);
            return;
        }

        try
        {
            _logger.Info($"startup root={paths.RootDirectory} check={options.Check} once={options.Once}");

            if (options.Check)
            {
                AttachConsoleIfPossible();
                PrintCheck(paths, options, settings);
                Shutdown(0);
                return;
            }

            if (options.Once)
            {
                AttachConsoleIfPossible();
                var exitCode = PrintOnce(paths, options);
                Shutdown(exitCode);
                return;
            }

            SettingsStore.EnsureDefault(paths.SettingsPath, _logger);
            _instanceGuard = new SingleInstanceGuard();
            if (!_instanceGuard.Acquire(Constants.MutexName))
            {
                _logger.Info("second instance detected; activating existing window");
                if (!ActivateExistingWindow(settings))
                {
                    _logger.Warning("second instance detected, but no existing overlay window was found");
                    System.Windows.MessageBox.Show(
                        "Codex 用量监控原生版已经在运行，但没有找到可激活的浮窗。\n\n请在任务管理器中结束 CodexQuotaMonitor.Wpf.exe 后重新启动。",
                        Constants.AppName,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
                Shutdown(0);
                return;
            }

            var window = new MainWindow(paths, options, settings, _logger);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.Error("startup failed", ex);
            AttachConsoleIfPossible();
            Console.Error.WriteLine($"startup failed: {ex.Message}");
            Shutdown(2);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _instanceGuard?.Dispose();
        _logger?.Info($"exit code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private static void AttachConsoleIfPossible()
    {
        try
        {
            NativeMethods.AttachConsole(AttachParentProcess);
            Console.OutputEncoding = Encoding.UTF8;
            Console.Error.WriteLine();
        }
        catch
        {
            // Console attachment is best effort for a WPF executable.
        }
    }

    private static void PrintCheck(AppPaths paths, CliOptions options, AppSettings settings)
    {
        var codexHome = paths.ResolveCodexHome(options.CodexHome);
        var codexExe = CodexExeFinder.Find(options.CodexExe) ?? "";
        Console.WriteLine($"root={paths.RootDirectory}");
        Console.WriteLine($"settings={paths.SettingsPath}");
        Console.WriteLine($"log={paths.LogPath}");
        Console.WriteLine($"codex_home={codexHome}");
        Console.WriteLine($"codex_exe={(string.IsNullOrWhiteSpace(codexExe) ? "not found" : codexExe)}");
        Console.WriteLine($"quota_interval={settings.QuotaInterval}");
        Console.WriteLine($"tray={(!settings.NoTray)}");
        var physicalWidth = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 0;
        var dipWidth = (int)SystemParameters.PrimaryScreenWidth;
        var dpiScale = physicalWidth > 0 && dipWidth > 0 ? (double)physicalWidth / dipWidth : 1.0;
        var placement = CodexQuotaMonitor.Wpf.MainWindow.ResolvePlacement(settings.PlacementEdge, settings.PlacementOffset, settings.PlacementOffset2, settings.WindowWidth, Constants.DefaultHeight, 0, dpiScale);
        Console.WriteLine($"placement={placement.X},{placement.Y},{placement.Width}x{placement.Height}");
        Console.WriteLine($"placement_edge={settings.PlacementEdge} placement_offset={settings.PlacementOffset} placement_offset2={settings.PlacementOffset2}");
        if (NativeMethods.TryGetTaskbarRect(out var edge, out var rect))
        {
            Console.WriteLine($"taskbar=edge:{edge} rect:{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
        }
        else
        {
            Console.WriteLine("taskbar=unavailable; using lower-left fallback");
        }
    }

    private static int PrintOnce(AppPaths paths, CliOptions options)
    {
        var logger = new SimpleLogger(paths.LogPath);
        var codexHome = paths.ResolveCodexHome(options.CodexHome);
        var quotaReader = new QuotaReader(codexHome, options.CodexExe, logger);
        var quota = Task.Run(() => quotaReader.ReadAsync()).GetAwaiter().GetResult();
        var payload = new
        {
            quota = new
            {
                quota.Error,
                quota.LimitId,
                quota.LimitName,
                quota.PlanType,
                fiveHour = quota.FiveHour,
                weekly = quota.Weekly,
                quota.RateLimitReachedType
            }
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return quota.Error is null ? 0 : 2;
    }

    private static bool ActivateExistingWindow(AppSettings settings)
    {
        foreach (var hwnd in NativeMethods.FindWindowsByTitlePrefix(Constants.WindowTitlePrefix))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.ApplyOverlayStyles(hwnd);
            NativeMethods.EnableFrostedBackdrop(hwnd);
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            var scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var baseHeightPhysical = Constants.DefaultHeight;
            if (NativeMethods.TryGetTaskbarRect(out var edge, out var taskbar))
            {
                baseHeightPhysical = edge is 0u or 2u ? Constants.DefaultHeight : taskbar.Height;
            }
            var extraPhysical = 0;
            if (NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                extraPhysical = Math.Max(0, rect.Height - baseHeightPhysical);
            }
            var placement = CodexQuotaMonitor.Wpf.MainWindow.ResolvePlacement(
                settings.PlacementEdge,
                settings.PlacementOffset,
                settings.PlacementOffset2,
                settings.WindowWidth,
                (int)Math.Round(baseHeightPhysical / scale),
                (int)Math.Round(extraPhysical / scale),
                scale);
            NativeMethods.SetTopmostPosition(hwnd,
                (int)Math.Round(placement.X * scale),
                (int)Math.Round(placement.Y * scale),
                (int)Math.Round(placement.Width * scale),
                (int)Math.Round(placement.Height * scale));
            NativeMethods.SetTopmostNoActivate(hwnd);
            return true;
        }
        return false;
    }

}
