using System.Text.Json.Nodes;

namespace ImmichDrive.Classes.Settings;

/// <summary>
/// The settings file's schema migrations for the kit's JsonSettingsStore (CONF-3). Each step
/// upgrades the JSON by one version, in place; a file written before versions existed is version 0.
/// Steps are never removed or reordered once shipped. WinUI-free and NLog-free so the portable test
/// harness can run them.
/// </summary>
public static class SettingsMigrations
{
    /// <summary>The steps, in order: index n upgrades version n to n + 1.</summary>
    public static IReadOnlyList<Action<JsonObject>> Steps { get; } =
    [
        ToVersion1,
    ];

    /// <summary>
    /// 0 to 1: the first versioned file. The settings window's four position and size numbers become
    /// one <c>SettingsWindowPlacement</c>, and the stored start-at-sign-in flag is dropped because
    /// Windows owns that state. Every other field, including a legacy <c>ApiKey</c>, is kept as written.
    /// </summary>
    private static void ToVersion1(JsonObject root)
    {
        int width = TakeInt(root, "SettingsWindowWidth");
        int height = TakeInt(root, "SettingsWindowHeight");
        int x = TakeInt(root, "SettingsWindowX");
        int y = TakeInt(root, "SettingsWindowY");

        if (width > 0 && height > 0)
        {
            root["SettingsWindowPlacement"] = new JsonObject
            {
                ["X"] = x,
                ["Y"] = y,
                ["Width"] = width,
                ["Height"] = height,
            };
        }

        root.Remove("Startup");
    }

    /// <summary>Removes a numeric field and returns its value, or 0 when absent or not a number.</summary>
    private static int TakeInt(JsonObject root, string name)
    {
        if (!root.Remove(name, out var node) || node is not JsonValue value)
        {
            return 0;
        }

        return value.TryGetValue(out int number) ? number : 0;
    }
}
