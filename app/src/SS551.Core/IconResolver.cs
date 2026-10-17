using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SS551.Core;

/// <summary>
/// Turns a freedesktop icon name ("firefox") into a file, searching the
/// current icon theme, the themes it inherits, hicolor and pixmaps.
/// </summary>
public sealed partial class IconResolver
{
    private readonly List<Dictionary<string, (string Path, int Score)>> _themes = [];
    private readonly Dictionary<string, string> _pixmaps = new(StringComparer.Ordinal);

    private IconResolver()
    {
    }

    private static readonly Lazy<IconResolver> Shared = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Index built once per process (walking the theme folders takes a moment).</summary>
    public static IconResolver Default => Shared.Value;

    /// <summary>Returns a PNG/SVG file for the icon, or null if none is found.</summary>
    public string? Resolve(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        if (Path.IsPathRooted(icon))
            return File.Exists(icon) ? icon : null;
        foreach (var theme in _themes)
        {
            if (theme.TryGetValue(icon, out var hit))
                return hit.Path;
        }
        return _pixmaps.GetValueOrDefault(icon);
    }

    private static IconResolver Build()
    {
        var resolver = new IconResolver();
        var roots = IconRoots().ToList();
        foreach (var theme in ThemeChain(CurrentTheme(), roots))
            resolver._themes.Add(IndexTheme(theme, roots));
        foreach (var root in DesktopApps.DataDirs().Select(d => Path.Combine(d, "pixmaps")).Append("/usr/share/pixmaps"))
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var file in Directory.EnumerateFiles(root))
            {
                if (IsImage(file))
                    resolver._pixmaps.TryAdd(Path.GetFileNameWithoutExtension(file), file);
            }
        }
        return resolver;
    }

    private static IEnumerable<string> IconRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".icons");
        foreach (var d in DesktopApps.DataDirs())
            yield return Path.Combine(d, "icons");
    }

    private static string CurrentTheme()
    {
        try
        {
            var psi = new ProcessStartInfo("gsettings", ["get", "org.gnome.desktop.interface", "icon-theme"])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd().Trim().Trim('\'') ?? "";
            p?.WaitForExit(2000);
            return output.Length > 0 ? output : "hicolor";
        }
        catch (Exception)
        {
            return "hicolor";
        }
    }

    /// <summary>The theme, its Inherits= chain, then hicolor (always last).</summary>
    private static IEnumerable<string> ThemeChain(string theme, List<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([theme]);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t == "hicolor" || !seen.Add(t))
                continue;
            yield return t;
            foreach (var root in roots)
            {
                var index = Path.Combine(root, t, "index.theme");
                if (!File.Exists(index))
                    continue;
                var inherits = File.ReadLines(index).FirstOrDefault(l => l.StartsWith("Inherits=", StringComparison.Ordinal));
                foreach (var parent in inherits?[9..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
                    queue.Enqueue(parent);
                break;
            }
        }
        yield return "hicolor";
    }

    private static Dictionary<string, (string Path, int Score)> IndexTheme(string theme, List<string> roots)
    {
        var index = new Dictionary<string, (string Path, int Score)>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var dir = Path.Combine(root, theme);
            if (!Directory.Exists(dir))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                }).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var file in files)
            {
                if (!IsImage(file))
                    continue;
                var name = Path.GetFileNameWithoutExtension(file);
                var score = Score(file);
                if (!index.TryGetValue(name, out var best) || score > best.Score)
                    index[name] = (file, score);
            }
        }
        return index;
    }

    /// <summary>Prefers full-colour app icons that are at least key-sized.</summary>
    private static int Score(string file)
    {
        var score = file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? 400
            : SizeDir().Match(file) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 32;
        if (score > 512)
            score = 512 - score / 100; // huge bitmaps: fine, but no better than svg
        if (file.Contains("symbolic", StringComparison.Ordinal))
            score -= 1000;
        if (file.Contains("/apps/", StringComparison.Ordinal))
            score += 50;
        return score;
    }

    private static bool IsImage(string file) =>
        file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"/(\d+)x\d+(@\d+x?)?/")]
    private static partial Regex SizeDir();
}
