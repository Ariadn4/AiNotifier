using Microsoft.Win32;

namespace AiNotifier;

public static class AutoStartManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AiNotifier";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                var value = key?.GetValue(AppName) as string;
                return value != null && value == Environment.ProcessPath;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.SetValue(AppName, Environment.ProcessPath ?? "");
        }
        catch
        {
            // Silently fail if registry write is denied
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // Silently fail if registry write is denied
        }
    }
}
