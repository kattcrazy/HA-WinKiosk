using System.IO;
using System.Text.Json;

namespace HAWinKiosk;

/// <summary>Persists breaking-changes banner dismiss / first-shown time per app version.</summary>
internal static class BreakingChangesNoticeStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HA-WinKiosk",
        "breaking-changes-notice.json");

    private static readonly TimeSpan ShowDuration = TimeSpan.FromHours(24);

    private sealed class NoticeState
    {
        public string Version { get; set; } = "";
        public bool Dismissed { get; set; }
        public DateTime? FirstShownUtc { get; set; }
    }

    public static bool ShouldShow(string version)
    {
        var state = Load();
        if (!string.Equals(state.Version, version, StringComparison.Ordinal))
            return true;

        if (state.Dismissed)
            return false;

        if (state.FirstShownUtc.HasValue
            && DateTime.UtcNow - state.FirstShownUtc.Value >= ShowDuration)
        {
            return false;
        }

        return true;
    }

    public static void RecordFirstShown(string version)
    {
        var state = Load();
        if (!string.Equals(state.Version, version, StringComparison.Ordinal))
        {
            state = new NoticeState { Version = version, FirstShownUtc = DateTime.UtcNow };
        }
        else if (!state.FirstShownUtc.HasValue)
        {
            state.FirstShownUtc = DateTime.UtcNow;
        }

        Save(state);
    }

    public static void Dismiss(string version)
    {
        var state = Load();
        if (!string.Equals(state.Version, version, StringComparison.Ordinal))
        {
            state = new NoticeState { Version = version };
        }

        state.Dismissed = true;
        if (!state.FirstShownUtc.HasValue)
            state.FirstShownUtc = DateTime.UtcNow;

        Save(state);
    }

    private static NoticeState Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new NoticeState();

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<NoticeState>(json) ?? new NoticeState();
        }
        catch
        {
            return new NoticeState();
        }
    }

    private static void Save(NoticeState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(state);
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Best-effort; banner may reappear next launch.
        }
    }
}
