using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SS551.Core.Model;

namespace SS551.Core;

/// <summary>Loads and saves the profile as JSON in $XDG_CONFIG_HOME/ss551/profile.json.</summary>
public sealed class ProfileStore
{
    public ProfileStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(ConfigDir, "profile.json");
    }

    public string Path { get; }

    public static string ConfigDir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = string.IsNullOrEmpty(xdg)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return System.IO.Path.Combine(root, "ss551");
        }
    }

    public Profile Load()
    {
        if (!File.Exists(Path))
            return Profile.CreateDefault();
        var root = JsonNode.Parse(File.ReadAllText(Path))?.AsObject()
                   ?? throw new JsonException("profile is not a JSON object");
        Migrate(root);
        return (root.Deserialize(ProfileJsonContext.Default.Profile) ?? Profile.CreateDefault()).Normalize();
    }

    public void Save(Profile profile)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        // Write to a temp file first so a crash never leaves a truncated profile.
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, ProfileJsonContext.Default.Profile));
        File.Move(tmp, Path, overwrite: true);
    }

    private static void Migrate(JsonObject root)
    {
        var version = (int?)root["Version"] ?? 1;
        if (version < 2)
            MigrateToV2(root);
        if (version < 3)
            MigrateToV3(root);
        root["Version"] = Profile.CurrentVersion;
    }

    /// <summary>v3 moved the key look (title, icon, colours) into "Look".</summary>
    private static void MigrateToV3(JsonObject root)
    {
        foreach (var key in Objects(root["Pages"]).SelectMany(p => Objects(p["Keys"])))
        {
            var look = new JsonObject();
            foreach (var name in new[] { "Title", "IconPath", "BackgroundColor", "TextColor" })
            {
                if (key[name] is { } value)
                    look[name] = value.DeepClone();
                key.Remove(name);
            }
            key["Look"] = look;
        }
    }

    /// <summary>
    /// Version 1 (no "Version" field) had a single action per key, button
    /// and knob direction; version 2 has macros and long-press stages.
    /// </summary>
    private static void MigrateToV2(JsonObject root)
    {
        foreach (var key in Objects(root["Pages"]).SelectMany(p => Objects(p["Keys"])))
        {
            key["Press"] = PressFrom(key["Action"]);
            key.Remove("Action");
        }

        if (root["Buttons"] is JsonArray buttons)
        {
            for (var i = 0; i < buttons.Count; i++)
                buttons[i] = PressFrom(buttons[i]);
        }

        foreach (var knob in Objects(root["Knobs"]))
        {
            knob["Left"] = MacroFrom(knob["Left"]);
            knob["Right"] = MacroFrom(knob["Right"]);
        }
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? array) =>
        array is JsonArray a ? a.OfType<JsonObject>() : [];

    private static JsonObject PressFrom(JsonNode? action) => new() { ["Actions"] = MacroFrom(action) };

    private static JsonArray MacroFrom(JsonNode? action) =>
        action is JsonObject obj ? [obj.DeepClone()] : [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Profile))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
