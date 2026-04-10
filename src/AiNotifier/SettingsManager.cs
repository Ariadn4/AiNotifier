using System.IO;
using System.Text.Json;

namespace AiNotifier;

public class AppSettings
{
    // Master on/off switch (does not change individual alert/nudge settings)
    public bool MasterEnabled { get; set; } = true;

    // Alert type toggles (persistent config, independent of MasterEnabled)
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
    public bool NudgeEnabled { get; set; } = true;
    public bool ProjectBubbleEnabled { get; set; } = true;
    public int NudgeCooldownMinutes { get; set; }  // 0 = 无冷却
    public int NudgeTriggerMode { get; set; }      // 0 = AI思考时, 1 = 冷却到期时
    public int NudgeOrderMode { get; set; }        // 0 = 随机, 1 = 顺序
    public int NudgeStaySeconds { get; set; } = 10; // 碎碎念停留时长
    public List<string>? CustomNudgeMessages { get; set; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }

    // Language (null = auto-detect from system, "zh-CN" or "en" = explicit)
    public string? Language { get; set; }

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

                bool needSave = false;

                // Migrate legacy SoundId → StopSoundId
                if (settings.SoundId != null)
                {
                    settings.StopSoundId = settings.SoundId;
                    settings.StopCustomSoundPath = settings.CustomSoundPath;
                    settings.SoundId = null;
                    settings.CustomSoundPath = null;
                    needSave = true;
                }

                // Migrate legacy Bubble* → Nudge*
                if (settings.ExtensionData != null)
                {
                    var ext = settings.ExtensionData;
                    if (ext.TryGetValue("BubbleEnabled", out var be))
                    { settings.NudgeEnabled = be.GetBoolean(); needSave = true; }
                    if (ext.TryGetValue("BubbleCooldownMinutes", out var bcm))
                    { settings.NudgeCooldownMinutes = bcm.GetInt32(); needSave = true; }
                    if (ext.TryGetValue("BubbleTriggerMode", out var btm))
                    { settings.NudgeTriggerMode = btm.GetInt32(); needSave = true; }
                    if (ext.TryGetValue("BubbleOrderMode", out var bom))
                    { settings.NudgeOrderMode = bom.GetInt32(); needSave = true; }
                    if (ext.TryGetValue("BubbleStaySeconds", out var bss))
                    { settings.NudgeStaySeconds = bss.GetInt32(); needSave = true; }
                    if (ext.TryGetValue("CustomBubbleMessages", out var cbm))
                    {
                        settings.CustomNudgeMessages = cbm.Deserialize<List<string>>(JsonOptions);
                        needSave = true;
                    }
                    if (needSave)
                        settings.ExtensionData = null;
                }

                if (needSave) Save(settings);

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
