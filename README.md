# HA WinKiosk

Home Assistant Windows Kiosk – An open-source Windows webpage kiosk designed for integration with Home Assistant. Prevents access to the typical Windows UI without pin access and publishes MQTT commands and sensors to Home Assistant. Configurable gestures for reload, clear cache, send MQTT message, and more.

## Requirements

- Windows 10/11
- [.NET 8 Runtime (Desktop)](https://dotnet.microsoft.com/download/dotnet/8.0) (the installer will install this automatically if not already present)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 11)

## Quick Start

1. Download the .exe file from the latest release and double click/open once downloaded to install. It will automatically install .NET 8 if needed.
2. On first run, Settings will open. Enter your kiosk webpage URL, MQTT host, port, username, password, and other details as needed.
3. Click Save & Back to Kiosk – the fullscreen kiosk will load your HA dashboard or chosen URL.
4. Click the gear button to open Settings (If you've disabled show settings button use a configured gesture with action set to `settings`, or MQTT `opensettings`). Use Exit to Windows in Settings to quit the app.

## Kiosk Lockdown

- WebView zoom is blocked (pinch zoom and Ctrl+wheel zoom).
- WebView back/forward swipe navigation is blocked.
- Kiosk window keeps itself topmost and fullscreen, hides the Windows taskbar while running, and restores the taskbar when the app exits.
- Windows key, context-menu key, Alt+F4, Alt+Tab, F11, F12, Ctrl+Esc, and Ctrl+Shift+Esc are intercepted while the kiosk is running.
- For strict OS-level shell lockdown (edge-swipe shell gestures, task switching, etc.), use Windows Assigned Access / Kiosk mode.

## MQTT and Home Assistant

With MQTT configured, the app publishes MQTT payloads that show up in Integrations > MQTT in Home Assistant. In the app Settings, under MQTT, turn Sensors and Commands on or off for which buttons and sensors are discovered.

Entity IDs in HA include your device name (sanitized). Names below match the name field in discovery.

| Name in HA | Type | Description |
| --- | --- | --- |
| Shutdown | Button | Shut down Windows |
| Restart | Button | Restart Windows |
| System sleep | Button | Suspend the PC (S3 sleep) |
| Monitor sleep | Button | Turn the display off |
| Monitor wake | Button | Turn the display on |
| Refresh kiosk | Button | Reload the page in the kiosk |
| Clear kiosk cache | Button | Clear kiosk cache (passwords & settings kept), then reload kiosk |
| Open settings | Button | Open this app’s Settings screen (no PIN; use only on a trusted broker) |
| Close settings | Button | Close Settings and return to the kiosk |
| Run Windows updates | Button | Starts a Windows Update scan/download/install run; app schedules restart if Windows Update reports reboot required |
| Battery level | Sensor | Remaining battery % (`unavailable` on desktops without a battery) |
| Session state | Sensor | PC session state |
| Last Active | Sensor | Seconds since last input (updates every 1 second, ignoring the update interval) |
| Updates pending | Sensor | Count of available Windows updates (`unavailable` if query fails) |
| Monitor orientation | Select | Default rotation for the primary display |
| Monitor brightness | Number | Brightness % (0–100) |

## Settings

Settings are stored at `%APPDATA%\HA-WinKiosk\settings.yaml`. They can be edited either in YAML or in the UI settings.

```yaml
kiosk:
  url: "http://homeassistant.local:8123"   # full URL of the page to show (e.g. Home Assistant)
  pin: ""                             # optional; required to open Settings when pin protection is on
  pinHint: ""                         # optional reminder shown on the PIN screen
  pinResetQuestion: ""                # optional question shown in the PIN dialog when using Forgot PIN
  pinResetAnswer: ""                  # optional answer for Forgot PIN (case-insensitive match)
  pinProtectionDisabled: false        # true = no PIN gate
  showSettingsButton: true            # false = hide gear; open Settings via gesture action=Settings or MQTT only
  uiTheme: auto                       # auto (follow Windows app light/dark) | light | dark
  gestures:
    swipeAction: reload               # disabled | reload | clearcache_reload | settings | mqtt
    swipeDirection: down              # down | up | left | right (used when swipeAction set)
    swipeMqttTopic: "swipe"           # final segment used only when corresponding action is mqtt
    swipeHoldAction: clearcache_reload
    swipeHoldDirection: down          # down | up | left | right (used when swipeHoldAction set)
    swipeHoldMs: 1000                 # swipe-and-hold threshold in milliseconds (used when swipeHoldAction set)
    swipeHoldMqttTopic: "swipe_hold"
    pinchAction: disabled
    pinchMqttTopic: "pinch"
    tripleTapAction: disabled
    quadrupleTapAction: settings
    tripleTapLocation: top-left       # top-left | top-right | bottom-right | bottom-left | anywhere (used when tripleTapAction set)
    tripleTapMqttTopic: "triple_tap"
    quadrupleTapLocation: top-left    # top-left | top-right | bottom-right | bottom-left | anywhere (used when quadrupleTapAction set)
    quadrupleTapMqttTopic: "quadruple_tap"

mqtt:
  host: "192.168.1.?"
  port: 1883
  username: ""
  password: ""
  deviceName: "kiosk"     # used in HA entity IDs (sanitized)
  discoveryPrefix: "homeassistant"    # Home Assistant MQTT discovery prefix

sensors:
  enabled: 
  - battery
  - sessionstate
  - last_active
  - updates_pending

  updateIntervalSeconds: 30           # minimum 5s for battery/session; last_active sensor is always 1s

commands:
  enabled:
    - shutdown
    - restart
    - sleep
    - monitorsleep
    - monitorwake
    - refresh
    - clearcache
    - opensettings
    - closesettings
    - windowsupdate

screenBrightness:
  defaultPercent: 100

screenOrientation:
  default: landscape                  # landscape | portrait | landscape_flipped | portrait_flipped

autoStart:
  enabled: true                       # Auto start on boot
```

## Windows Installer (Normal Install)

This creates a proper installed app (Start menu + Windows search entry + Apps uninstall entry), includes a "Launch after install" checkbox, and removes `%APPDATA%\HA-WinKiosk` config on uninstall.

Build installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Build-Installer.ps1
```
- Publish GitHub release tags as semantic versions (for example `v1.2.3`).
- Keep installer filename as `HAWinKiosk-Setup.exe` (or another `*Setup*.exe`).
- Bump app version in `src\HAWinKiosk\HAWinKiosk.csproj` (`<Version>...</Version>`) before release.

Output:

- `installer\output\HAWinKiosk-Setup.exe`

## Automatic App Updates (GitHub Releases)

 The app checks daily at 3:00 AM local device time for any updates. If a newer version is found, it downloads the installer, silently replaces the old app, and relaunches the new version.



## Credits

[HASS.Agent 2.0](https://github.com/hass-agent/HASS.Agent) (hass-agent, forked from LAB02-Research) was heavily leaned on for implementation of sensors and commands, and in the case of monitor wake/sleep, directly mirrored. Go check out this amazing program if you're just looking for sensors and commands without a kiosk!
