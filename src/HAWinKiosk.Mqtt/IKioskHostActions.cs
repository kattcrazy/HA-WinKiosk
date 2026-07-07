namespace HAWinKiosk.Mqtt;

/// <summary>Host (WPF) actions invoked from MQTT command handlers (WebView reload, cache, open/close Settings).</summary>
public interface IKioskHostActions
{
    void ReloadWebView();
    Task ClearBrowsingCacheAsync(CancellationToken cancellationToken = default);
    void OpenSettings();
    void CloseSettings();

    /// <summary>Called after MQTT persisted remote changes to <c>settings.yaml</c> (reload UI state / reinject WebView script).</summary>
    void NotifySettingsChangedFromMqtt();

    Task NavigateHaPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Current WebView URL, or null if not available.</summary>
    string? GetCurrentUrl();
}
