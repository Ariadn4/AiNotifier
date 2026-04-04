using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiNotifier;

public static class HookManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private const string SidExtract = "SID=$(sed -n 's/.*\"session_id\" *: *\"\\([^\"]*\\)\".*/\\1/p')";

    private const string StopCommand = SidExtract + " && curl -s -G \"http://localhost:19836/stop\" --data-urlencode \"sid=$SID\"";
    private const string StopCommandWithCwd = SidExtract + " && curl -s -G \"http://localhost:19836/stop\" --data-urlencode \"cwd=$CLAUDE_PROJECT_DIR\" --data-urlencode \"sid=$SID\"";
    private const string NotifyCommand = SidExtract + " && curl -s -G \"http://localhost:19836/notify\" --data-urlencode \"sid=$SID\"";
    private const string NotifyCommandWithCwd = SidExtract + " && curl -s -G \"http://localhost:19836/notify\" --data-urlencode \"cwd=$CLAUDE_PROJECT_DIR\" --data-urlencode \"sid=$SID\"";
    private const string NudgeCommand = SidExtract + " && curl -s -G \"http://localhost:19836/start\" --data-urlencode \"sid=$SID\"";
    private const string NotifyUrl = "localhost:19836";

    private static string UserHome =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ClaudeSettingsPath =>
        Path.Combine(UserHome, ".claude", "settings.json");

    public static bool IsClaudeCodeBound()
    {
        try
        {
            var path = ClaudeSettingsPath;
            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json);
            var stopHooks = root?["hooks"]?["Stop"]?.AsArray();
            if (stopHooks == null) return false;

            return HasNotifyHook(stopHooks);
        }
        catch
        {
            return false;
        }
    }

    public static void BindClaudeCode(bool projectBubbleEnabled = true)
    {
        try
        {
            var path = ClaudeSettingsPath;
            JsonNode root;

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                root = JsonNode.Parse(json) ?? new JsonObject();
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                root = new JsonObject();
            }

            if (root["hooks"] == null)
                root["hooks"] = new JsonObject();

            var hooks = root["hooks"]!.AsObject();
            var stopCmd = projectBubbleEnabled ? StopCommandWithCwd : StopCommand;
            var stopEntry = CreateHookEntry(stopCmd);

            if (hooks["Stop"] == null)
            {
                hooks["Stop"] = new JsonArray { stopEntry };
            }
            else
            {
                var stopArray = hooks["Stop"]!.AsArray();
                if (!HasNotifyHook(stopArray))
                    stopArray.Add(stopEntry);
            }

            // Add Notification hook
            var notifyCmd = projectBubbleEnabled ? NotifyCommandWithCwd : NotifyCommand;
            var notificationEntry = CreateHookEntry(notifyCmd);
            if (hooks["Notification"] == null)
            {
                hooks["Notification"] = new JsonArray { notificationEntry };
            }
            else
            {
                var notifArray = hooks["Notification"]!.AsArray();
                if (!HasNotifyHook(notifArray))
                    notifArray.Add(notificationEntry);
            }

            // Add UserPromptSubmit hook for nudge
            var nudgeEntry = CreateHookEntry(NudgeCommand);
            if (hooks["UserPromptSubmit"] == null)
            {
                hooks["UserPromptSubmit"] = new JsonArray { nudgeEntry };
            }
            else
            {
                var preToolArray = hooks["UserPromptSubmit"]!.AsArray();
                if (!HasNotifyHook(preToolArray))
                    preToolArray.Add(nudgeEntry);
            }

            File.WriteAllText(path, root.ToJsonString(JsonOptions));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(LocalizationService.Instance.Get("Error_ClaudeConfig", ex.Message), ex);
        }
    }

    public static void RebindIfNeeded(bool projectBubbleEnabled)
    {
        if (!IsClaudeCodeBound()) return;
        UnbindClaudeCode();
        BindClaudeCode(projectBubbleEnabled);
    }

    public static void UnbindClaudeCode()
    {
        try
        {
            var path = ClaudeSettingsPath;
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json);
            var stopArray = root?["hooks"]?["Stop"]?.AsArray();
            if (stopArray == null) return;

            RemoveNotifyHooks(stopArray);

            if (stopArray.Count == 0)
                root!["hooks"]!.AsObject().Remove("Stop");

            // Remove Notification hook
            var notifArray = root?["hooks"]?["Notification"]?.AsArray();
            if (notifArray != null)
            {
                RemoveNotifyHooks(notifArray);
                if (notifArray.Count == 0)
                    root!["hooks"]!.AsObject().Remove("Notification");
            }

            // Remove UserPromptSubmit hook
            var preToolArray = root?["hooks"]?["UserPromptSubmit"]?.AsArray();
            if (preToolArray != null)
            {
                RemoveNotifyHooks(preToolArray);
                if (preToolArray.Count == 0)
                    root!["hooks"]!.AsObject().Remove("UserPromptSubmit");
            }

            if (root!["hooks"]!.AsObject().Count == 0)
                root.AsObject().Remove("hooks");

            File.WriteAllText(path, root.ToJsonString(JsonOptions));
        }
        catch
        {
        }
    }

    private static JsonObject CreateHookEntry(string? command = null) => new()
    {
        ["hooks"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "command",
                ["command"] = command ?? NotifyCommand
            }
        }
    };

    private static bool HasNotifyHook(JsonArray hookArray)
    {
        return hookArray.Any(entry =>
            entry?["hooks"]?.AsArray().Any(h =>
                h?["command"]?.GetValue<string>().Contains(NotifyUrl) == true) == true);
    }

    private static void RemoveNotifyHooks(JsonArray hookArray)
    {
        for (int i = hookArray.Count - 1; i >= 0; i--)
        {
            var innerHooks = hookArray[i]?["hooks"]?.AsArray();
            if (innerHooks != null && innerHooks.Any(h =>
                h?["command"]?.GetValue<string>().Contains(NotifyUrl) == true))
            {
                hookArray.RemoveAt(i);
            }
        }
    }
}
