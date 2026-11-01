using System.Windows;
using HAWinKiosk.Mqtt;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "HA-WinKiosk-SingleInstance";
    private Mutex? _singleInstanceMutex;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, _) => KioskWindow.RestoreTaskbar();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KioskWindow.RestoreTaskbar();

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("HA WinKiosk is already running.", "HA WinKiosk", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        var previewWebViewError = e.Args.Any(a =>
            string.Equals(a, "--preview-webview-error", StringComparison.OrdinalIgnoreCase));

        var appSettings = SettingsManager.Load();
        ThemePalette.ApplyAppWide(UiThemeHelper.ResolveEffectiveDark(appSettings.Kiosk.UiTheme));
        var showSettingsFirst = !SettingsManager.SettingsExists || string.IsNullOrWhiteSpace(appSettings.Kiosk.Url?.Trim());

        var kiosk = new KioskWindow(showSettingsFirst, previewWebViewError);
        kiosk.Show();
        if (!previewWebViewError)
            AutoUpdateService.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
