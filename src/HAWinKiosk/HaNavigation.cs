using Microsoft.Web.WebView2.Core;

namespace HAWinKiosk;

/// <summary>Browser Mod-style in-app navigation for Home Assistant SPA routes.</summary>
public static class HaNavigation
{
    public static async Task NavigateHaPathAsync(
        CoreWebView2? webView,
        string kioskBaseUrl,
        string path,
        Action<string> fullNavigate,
        CancellationToken cancellationToken = default)
    {
        if (webView == null || string.IsNullOrWhiteSpace(path))
            return;

        var trimmed = path.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs)
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            fullNavigate(trimmed);
            return;
        }

        var haPath = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        var escaped = System.Text.Json.JsonSerializer.Serialize(haPath);
        var script =
            "(function(){try{var path=" + escaped + ";history.pushState(null,\"\",path);"
            + "window.dispatchEvent(new CustomEvent(\"location-changed\"));return true;}catch(e){return false;}})()";

        try
        {
            var result = await webView.ExecuteScriptAsync(script);
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                return;
        }
        catch
        {
            // fall through to full navigation
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullUrl = BuildFullUrl(kioskBaseUrl, haPath);
        fullNavigate(fullUrl);
    }

    internal static string BuildFullUrl(string kioskBaseUrl, string haPath)
    {
        if (Uri.TryCreate(kioskBaseUrl, UriKind.Absolute, out var baseUri))
        {
            var builder = new UriBuilder(baseUri.Scheme, baseUri.Host, baseUri.Port, haPath);
            return builder.Uri.ToString();
        }

        return haPath;
    }
}
