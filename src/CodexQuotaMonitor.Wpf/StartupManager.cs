using Microsoft.Win32;

namespace CodexQuotaMonitor.Wpf;

public static class StartupManager
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "CodexQuotaMonitor";

    public static string BuildRunValue(string exePath) => $"\"{exePath}\"";

    public static bool IsEnabled(string valueName = RunValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(valueName) is not null;
    }

    public static bool SetEnabled(bool enabled, SimpleLogger? logger = null, string valueName = RunValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                logger?.Warning($"unable to open registry key {RunKeyPath}");
                return false;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    logger?.Warning("startup registration failed: process path is unavailable");
                    return false;
                }

                key.SetValue(valueName, BuildRunValue(exe));
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to {(enabled ? "enable" : "disable")} startup registration: {ex.Message}");
            return false;
        }
    }
}