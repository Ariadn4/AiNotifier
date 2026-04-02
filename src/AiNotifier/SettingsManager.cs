using System.IO;
using System.Text.Json;

namespace AiNotifier;

public class AppSettings
{
    // Alert type toggles
    public bool StopAlertEnabled { get; set; } = true;
    public bool NotificationAlertEnabled { get; set; } = true;

    // Per-type sound settings
    public string StopSoundId { get; set; } = "alert-1";
    public string? StopCustomSoundPath { get; set; }
    public string NotificationSoundId { get; set; } = "alert-3";
    public string? NotificationCustomSoundPath { get; set; }

    // Shared sound settings
    public double Volume { get; set; } = 0.6;
    public bool GradualVolume { get; set; }

    // Legacy fields for migration (kept for backward compat on load)
    public string? SoundId { get; set; }
    public string? CustomSoundPath { get; set; }

    public bool ShortMode { get; set; }
    public int AlertTimeoutSeconds { get; set; } = 60;
    public bool BubbleEnabled { get; set; } = true;
    public bool ProjectBubbleEnabled { get; set; } = true;
    public int BubbleCooldownMinutes { get; set; }  // 0 = 无冷却
    public List<string>? CustomBubbleMessages { get; set; }

    // Window position (null = use default)
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}

public static class SettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiNotifier");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();

                // Migrate legacy SoundId → StopSoundId
                if (settings.SoundId != null)
                {
                    settings.StopSoundId = settings.SoundId;
                    settings.StopCustomSoundPath = settings.CustomSoundPath;
                    settings.SoundId = null;
                    settings.CustomSoundPath = null;
                    Save(settings);
                }

                return settings;
            }
        }
        catch { /* ignore corrupt settings */ }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore save errors */ }
    }
}
