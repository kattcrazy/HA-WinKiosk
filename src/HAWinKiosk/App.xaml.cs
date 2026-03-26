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
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("HA WinKiosk is already running.", "HA WinKiosk", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        var appSettings = SettingsManager.Load();
        var showSettingsFirst = !SettingsManager.SettingsExists || string.IsNullOrWhiteSpace(appSettings.Kiosk.Url?.Trim());

        var kiosk = new KioskWindow(showSettingsFirst);
        kiosk.Show();
        AutoUpdateService.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
