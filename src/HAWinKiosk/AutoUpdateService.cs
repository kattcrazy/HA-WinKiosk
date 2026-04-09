using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using HAWinKiosk.Mqtt;

namespace HAWinKiosk;

public static class AutoUpdateService
{
    private const string GitHubOwner = "kattcrazy";
    private const string GitHubRepo = "HA-WinKiosk";
    private const int UpdateHourLocal = 3;
    private static readonly HttpClient Http = BuildHttpClient();
    private static readonly SemaphoreSlim CheckGate = new(1, 1);

    public static void Start()
    {
        _ = Task.Run(RunSchedulerLoopAsync);
    }

    public static async Task<bool> CheckAndApplyNowAsync()
    {
        return await CheckAndApplyAsync();
    }

    private static async Task RunSchedulerLoopAsync()
    {
        while (true)
        {
            try
            {
                var delay = GetDelayUntilNextUpdateWindow();
                await Task.Delay(delay);
                _ = await CheckAndApplyAsync();
            }
            catch
            {
                // Never crash app from update scheduler.
            }
            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }

    private static TimeSpan GetDelayUntilNextUpdateWindow()
    {
        var now = DateTimeOffset.Now;
        var next = new DateTimeOffset(
            now.Year, now.Month, now.Day,
            UpdateHourLocal, 0, 0, now.Offset);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    private static async Task<bool> CheckAndApplyAsync()
    {
        if (!await CheckGate.WaitAsync(0)) return false;
        try
        {
            var betaUpdates = SettingsManager.Load().Kiosk.BetaUpdates;
            var latest = betaUpdates
                ? await GetBestReleaseIncludingPrereleasesAsync(GitHubOwner, GitHubRepo)
                : await GetLatestStableReleaseAsync(GitHubOwner, GitHubRepo);
            if (latest == null) return false;

            var currentVersion = GetCurrentVersion();
            if (latest.Version <= currentVersion) return false;

            var tempDir = Path.Combine(Path.GetTempPath(), "HA-WinKiosk", "updates");
            Directory.CreateDirectory(tempDir);
            var installerPath = Path.Combine(tempDir, latest.AssetName);
            await DownloadFileAsync(latest.DownloadUrl, installerPath);

            // Inno upgrade uses stable AppId, so old version is uninstalled/replaced automatically.
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true
            };
            Process.Start(psi);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });
            return true;
        }
        catch
        {
            // best-effort updater; never crash kiosk
            return false;
        }
        finally
        {
            CheckGate.Release();
        }
    }

    private static async Task DownloadFileAsync(string url, string path)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var inStream = await resp.Content.ReadAsStreamAsync();
        await using var outStream = File.Create(path);
        await inStream.CopyToAsync(outStream);
    }

    private static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(0, 0, 0, 0);
    }

    private static async Task<LatestRelease?> GetLatestStableReleaseAsync(string owner, string repo)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        using var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return TryBuildLatestReleaseFromRoot(doc.RootElement);
    }

    private static async Task<LatestRelease?> GetBestReleaseIncludingPrereleasesAsync(string owner, string repo)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=100";
        using var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array) return null;

        LatestRelease? best = null;
        DateTimeOffset bestPublished = DateTimeOffset.MinValue;

        foreach (var el in root.EnumerateArray())
        {
            if (el.TryGetProperty("draft", out var draftEl) && draftEl.ValueKind == JsonValueKind.True)
                continue;

            var rel = TryBuildLatestReleaseFromRoot(el);
            if (rel == null) continue;

            if (!el.TryGetProperty("published_at", out var pubEl)
                || !DateTimeOffset.TryParse(pubEl.GetString(), out var publishedAt))
                publishedAt = DateTimeOffset.MinValue;

            if (best == null
                || rel.Version > best.Version
                || (rel.Version == best.Version && publishedAt > bestPublished))
            {
                best = rel;
                bestPublished = publishedAt;
            }
        }

        return best;
    }

    private static LatestRelease? TryBuildLatestReleaseFromRoot(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
        var tag = tagEl.GetString() ?? "";
        if (!TryParseVersion(tag, out var version)) return null;

        if (!root.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assetsEl.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) continue;
            var download = asset.TryGetProperty("browser_download_url", out var d) ? (d.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(download)) continue;
            return new LatestRelease(version, name, download);
        }
        return null;
    }

    private static bool TryParseVersion(string raw, out Version version)
    {
        var s = (raw ?? "").Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s, out version!);
    }

    private static HttpClient BuildHttpClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("HA-WinKiosk-Updater");
        c.Timeout = TimeSpan.FromSeconds(20);
        return c;
    }

    private sealed record LatestRelease(Version Version, string AssetName, string DownloadUrl);
}
