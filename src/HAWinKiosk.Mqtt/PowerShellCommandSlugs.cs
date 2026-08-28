using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk.Mqtt;

/// <summary>Builds unique MQTT command slugs for PowerShell command rows.</summary>
public static class PowerShellCommandSlugs
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HA-WinKiosk",
        "mqtt-ps-button-slugs.json");

    public readonly record struct Entry(string Slug, string DisplayName, string Command);

    public static List<Entry> Build(IReadOnlyList<PowerShellCommandItem>? items)
    {
        var list = items ?? Array.Empty<PowerShellCommandItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Entry>(list.Count);

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var display = string.IsNullOrWhiteSpace(item.Name)
                ? $"PowerShell {i + 1}"
                : item.Name.Trim();
            var baseSlug = "ps_" + SanitizeName(display);
            if (baseSlug is "ps_" or "ps")
                baseSlug = $"ps_{i + 1}";

            var slug = baseSlug;
            var n = 2;
            while (!used.Add(slug))
                slug = $"{baseSlug}_{n++}";

            result.Add(new Entry(slug, display, item.Command ?? ""));
        }

        return result;
    }

    public static HashSet<string> LoadCachedSlugs()
    {
        try
        {
            if (!File.Exists(CachePath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var json = File.ReadAllText(CachePath);
            var arr = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return arr.Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveCachedSlugs(IEnumerable<string> slugs)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var arr = slugs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            File.WriteAllText(CachePath, JsonSerializer.Serialize(arr));
        }
        catch
        {
            // Best-effort cache for clearing stale HA discovery.
        }
    }

    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
            else if (c is ' ' or '-' or '_')
                sb.Append('_');
        }

        var s = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        return s;
    }
}
