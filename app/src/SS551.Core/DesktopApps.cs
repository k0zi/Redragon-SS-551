using System.Globalization;

namespace SS551.Core;

/// <summary>An installed application, read from its .desktop file.</summary>
public sealed record DesktopApp(string Id, string Name, string? Comment, string? Icon, string DesktopFile);

/// <summary>Lists applications from the XDG application directories (system, user, snap, flatpak).</summary>
public static class DesktopApps
{
    public static IReadOnlyList<DesktopApp> LoadAll()
    {
        // Earlier directories win for the same desktop id (user overrides system).
        var byId = new Dictionary<string, DesktopApp?>(StringComparer.Ordinal);
        foreach (var dir in ApplicationDirs())
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.AllDirectories))
            {
                var id = Path.GetRelativePath(dir, file).Replace('/', '-');
                if (!byId.ContainsKey(id))
                    byId[id] = TryParse(id, file);
            }
        }
        return byId.Values.OfType<DesktopApp>()
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IEnumerable<string> DataDirs()
    {
        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        yield return string.IsNullOrEmpty(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : home;
        var dirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        foreach (var d in (string.IsNullOrEmpty(dirs) ? "/usr/local/share:/usr/share" : dirs).Split(':'))
        {
            if (d.Length > 0)
                yield return d;
        }
    }

    private static IEnumerable<string> ApplicationDirs() =>
        DataDirs().Select(d => Path.Combine(d, "applications")).Distinct();

    private static DesktopApp? TryParse(string id, string file)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMain = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inMain = line == "[Desktop Entry]";
                continue;
            }
            var eq = line.IndexOf('=');
            if (!inMain || eq <= 0 || line.StartsWith('#'))
                continue;
            values.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }

        if (Get("Type") != "Application" || Get("NoDisplay") == "true" || Get("Hidden") == "true")
            return null;
        var name = Get($"Name[{lang}]") ?? Get("Name");
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return new DesktopApp(id, name, Get($"Comment[{lang}]") ?? Get("Comment"), Get("Icon"), file);

        string? Get(string key) => values.TryGetValue(key, out var v) ? v : null;
    }
}
