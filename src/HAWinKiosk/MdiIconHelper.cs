using Material.Icons;
using Material.Icons.WPF;

namespace HAWinKiosk;

internal static class MdiIconHelper
{
    public const string DefaultMdiName = "mdi:button-pointer";

    public static MaterialIconKind ResolveKind(string? mdiName)
    {
        if (TryParse(mdiName, out var kind))
            return kind;
        return MaterialIconKind.ButtonPointer;
    }

    public static bool TryParse(string? raw, out MaterialIconKind kind)
    {
        kind = MaterialIconKind.ButtonPointer;
        var s = (raw ?? "").Trim();
        if (s.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase))
            s = s[4..];
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var pascal = string.Concat(
            s.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Length == 0
                    ? ""
                    : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

        return Enum.TryParse(pascal, ignoreCase: true, out kind);
    }

    public static void ApplyTo(MaterialIcon icon, string? mdiName)
    {
        icon.Kind = ResolveKind(mdiName);
    }
}
