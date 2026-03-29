# HA WinKiosk <img src="logo.png" alt="HA WinKiosk logo" width="36" />

Home Assistant Windows Kiosk – An open-source Windows webpage kiosk designed for integration with Home Assistant. Prevents access to the typical Windows UI without pin access and publishes MQTT commands and sensors to Home Assistant. Configurable gestures for reload, clear cache, send MQTT message, and more.

## Quick Start

1. Download the .exe file from the latest release and once downloaded double click/open to install. It will automatically install .NET 8 if needed.
2. On first run, Settings will open. Enter your kiosk webpage URL, MQTT host, port, username, password, and other details as needed.
3. Click Save & Back to Kiosk – the fullscreen kiosk will load your HA dashboard or chosen URL.
4. Click the gear button to open Settings (If you've disabled show settings button use a configured gesture with action set to `settings`, or MQTT `opensettings`). Use Exit to Windows in Settings to quit the app.

I reccomend checking out  [Tips and tricks](Tips-and-tricks.md) if you're wanting to sleep/wake your kiosk, use autologin, or have a Surface Pro 3.

## Requirements

- Windows 10/11
- [.NET 8 Runtime (Desktop)](https://dotnet.microsoft.com/download/dotnet/8.0) (the installer will install this automatically if not already present)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 11)

## Kiosk Lockdown

- WebView zoom is blocked (pinch zoom and Ctrl+wheel zoom).
- WebView back/forward swipe navigation is blocked.
- Kiosk window keeps itself topmost and fullscreen, hides the Windows taskbar while running, and restores the taskbar when the app exits.
- Windows key, context-menu key, Alt+F4, Alt+Tab, F11, F12, Ctrl+Esc, and Ctrl+Shift+Esc are intercepted while the kiosk is running. A limitation of running inside Windows Explorer is that the start menu WILL still come up on windows key, and swipe up from bottom.

## MQTT and Home Assistant

With MQTT configured, the app publishes MQTT payloads that show up in Integrations > MQTT in Home Assistant. In the app Settings, under MQTT, turn Sensors and Commands on or off for which buttons and sensors are discovered.

Entity IDs in HA include your device name (sanitized). Names below match the name field in discovery.

<img width="1250" height="703" alt="image (21)" src="https://github.com/user-attachments/assets/76113480-37a0-43c6-8385-49aeda76daed" />

| Name in HA | Type | Description |
| --- | --- | --- |
| Shutdown | Button | Shut down Windows |
| Restart | Button | Restart Windows |
| System sleep | Button | Suspend the PC (S3 sleep) |
| Monitor sleep | Button | Turn the display off |
| Monitor wake | Button | Turn the display on |
| Refresh kiosk | Button | Reload the page in the kiosk |
| Clear kiosk cache | Button | Clear kiosk cache (passwords & settings kept), then reload kiosk |
| Open settings | Button | Open this app’s Settings screen (no PIN) |
| Close settings | Button | Close Settings and return to the kiosk |
| Run Windows updates | Button | Starts a Windows Update scan/download/install run; app schedules restart if Windows Update reports reboot required |
| PowerShell command | Button | Executes configured PowerShell command text from settings |
| Battery level | Sensor | Remaining battery % (`unavailable` on desktops without a battery) |
| Last Active | Sensor | Seconds since last input (updates every 1 second, ignoring the update interval) |
| Updates pending | Number | Count of available Windows updates |
| Monitor brightness | Number | Brightness % (0–100) |

## Settings

Settings are stored at ` %LocalAppData%\Programs\HA WinKiosk`. They can be edited either in YAML or in the UI settings. Below is a key of what all the options are, and below that is an example settings.yaml

![Settings](https://github.com/user-attachments/assets/e09a5801-a07b-45db-98b0-c6f4b96e4eb7)

### All Options

#### Config

| UI Name | Values | Notes | Published to HA via MQTT? |
| --- | --- | --- | --- |
| Kiosk URL | URL | Kiosk page URL | No |
| Show settings button | `true`/`false` | Show/hide gear button | No |
| Theme | `auto` \| `light` \| `dark` | UI theme mode | No |
| Brightness (%) | `0..100` | Startup brightness | Yes (number entity) |
| Start when Windows starts | `true`/`false` | Launch app at sign-in | No |

#### Pin

| UI Name | Values | Notes | Published to HA via MQTT? |
| --- | --- | --- | --- |
| Settings PIN | string | PIN required when PIN protection is enabled. Doesn't have to be numbers. | No |
| PIN hint | string | Hint shown on PIN prompt | No |
| Verification question | string | Forgot-PIN verification question | No |
| PIN reset answer | string | Forgot-PIN verification answer | No |
| PIN protection | `true`/`false` | `false` disables PIN gate | No |

#### Gestures

| UI Name | Values | Notes | Published to HA via MQTT? |
| --- | --- | --- | --- |
| Double tap action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for double tap | Indirect (when MQTT message) |
| Double tap location | `Top left` \| `Top right` \| `Bottom right` \| `Bottom left` \| `Anywhere` | Double-tap screen region | No |
| Double tap MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Triple tap action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for triple tap | Indirect (when MQTT message) |
| Triple tap location | `Top left` \| `Top right` \| `Bottom right` \| `Bottom left` \| `Anywhere` | Triple-tap screen region | No |
| Triple tap MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Quadruple tap action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for quadruple tap | Indirect (when MQTT message) |
| Quadruple tap location | `Top left` \| `Top right` \| `Bottom right` \| `Bottom left` \| `Anywhere` | Quadruple-tap screen region | No |
| Quadruple tap MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Quintuple tap action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for quintuple tap | Indirect (when MQTT message) |
| Quintuple tap location | `Top left` \| `Top right` \| `Bottom right` \| `Bottom left` \| `Anywhere` | Quintuple-tap screen region | No |
| Quintuple tap MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Swipe action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for swipe | Indirect (when MQTT message) |
| Swipe direction | `Down` \| `Left` \| `Right` | Swipe direction filter | No |
| Swipe MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Swipe and hold action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for swipe-and-hold | Indirect (when MQTT message) |
| Swipe and hold direction | `Down` \| `Left` \| `Right` | Swipe-and-hold direction filter | No |
| Swipe and hold threshold (milliseconds) | integer (ms) | Hold threshold for swipe-and-hold | No |
| Swipe and hold MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Two-finger swipe action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Touch devices only | Indirect (when MQTT message) |
| Two-finger swipe direction | `Down` \| `Left` \| `Right` | Touch devices only | No |
| Two-finger swipe MQTT topic | string | Touch devices only | Yes |
| Two-finger swipe and hold action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Touch devices only | Indirect (when MQTT message) |
| Two-finger swipe and hold direction | `Down` \| `Left` \| `Right` | Touch devices only | No |
| Two-finger swipe and hold threshold (milliseconds) | integer (ms) | Hold threshold for two-finger swipe-and-hold | No |
| Two-finger swipe and hold MQTT topic | string | Touch devices only | Yes |
| Pinch action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for pinch-in gesture | Indirect (when MQTT message) |
| Pinch MQTT topic | string | Topic suffix when action is MQTT message | Yes |
| Zoom action | `Disabled` \| `Reload` \| `Clear cache and reload` \| `Settings` \| `MQTT message` | Action for zoom gesture | Indirect (when MQTT message) |
| Zoom direction | `Any` \| `In` \| `Out` | Zoom gesture direction filter | No |
| Zoom MQTT topic | string | Topic suffix when action is MQTT message | Yes |

#### MQTT

| UI Name | Values | Notes | Published to HA via MQTT? |
| --- | --- | --- | --- |
| MQTT IP | hostname/IP | Broker host | No |
| MQTT port | integer | Broker port | No |
| MQTT username | string | Broker username | No |
| MQTT password | string | Broker password | No |
| Device name | string | Base ID in MQTT entities/topics | No |
| Sensor: Battery level | `On`/`Off` | Publishes battery percentage | Yes |
| Sensor: Last Active | `On`/`Off` | Publishes seconds since last input (1s cadence) | Yes |
| Sensor: Updates pending | `On`/`Off` | Publishes Windows update count | Yes |
| Command: Shutdown | `On`/`Off` | Exposes Shutdown MQTT button | Yes |
| Command: Restart | `On`/`Off` | Exposes Restart MQTT button | Yes |
| Command: System sleep | `On`/`Off` | Exposes Sleep MQTT button | Yes |
| Command: Monitor sleep | `On`/`Off` | Exposes Monitor sleep MQTT button | Yes |
| Command: Monitor wake | `On`/`Off` | Exposes Monitor wake MQTT button | Yes |
| Command: Refresh kiosk | `On`/`Off` | Exposes Refresh MQTT button | Yes |
| Command: Clear kiosk cache | `On`/`Off` | Exposes Clear cache MQTT button | Yes |
| Command: Open settings | `On`/`Off` | Exposes Open settings MQTT button | Yes |
| Command: Close settings | `On`/`Off` | Exposes Close settings MQTT button | Yes |
| Command: Run Windows updates | `On`/`Off` | Exposes Windows updates MQTT button | Yes |
| Command: PowerShell command | `On`/`Off` | Exposes custom PowerShell MQTT button | Yes |
| PowerShell command text | string | Command text for powershellcommand MQTT command | Yes |

### Example `settings.yaml`

```yaml
kiosk:
  url: "http://homeassistant.local:8123"
  pin: ""
  pinHint: ""
  pinResetQuestion: ""
  pinResetAnswer: ""
  pinProtectionDisabled: false
  showSettingsButton: true
  uiTheme: auto
  gestures:
    doubleTapAction: disabled
    doubleTapLocation: top-left
    doubleTapMqttTopic: "double_tap"
    tripleTapAction: disabled
    tripleTapLocation: top-left
    tripleTapMqttTopic: "triple_tap"
    quadrupleTapAction: settings
    quadrupleTapLocation: top-left
    quadrupleTapMqttTopic: "quadruple_tap"
    quintupleTapAction: disabled
    quintupleTapLocation: top-left
    quintupleTapMqttTopic: "quintuple_tap"
    swipeAction: reload
    swipeDirection: down
    swipeMqttTopic: "swipe"
    twoFingerSwipeAction: disabled
    twoFingerSwipeDirection: down
    twoFingerSwipeMqttTopic: "two_finger_swipe"
    swipeHoldAction: clearcache_reload
    swipeHoldDirection: down
    swipeHoldMs: 1000
    swipeHoldMqttTopic: "swipe_hold"
    twoFingerSwipeHoldAction: disabled
    twoFingerSwipeHoldDirection: down
    twoFingerSwipeHoldMs: 1000
    twoFingerSwipeHoldMqttTopic: "two_finger_swipe_hold"
    pinchAction: disabled
    pinchMqttTopic: "pinch"
    zoomAction: disabled
    zoomDirection: any
    zoomMqttTopic: "zoom"

mqtt:
  host: "192.168.1.?"
  port: 1883
  username: ""
  password: ""
  deviceName: "kiosk"
  discoveryPrefix: "homeassistant"

sensors:
  enabled:
    - battery
    - last_active
    - updates_pending
  updateIntervalSeconds: 30

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
  powerShellCommand: ""

screenBrightness:
  defaultPercent: 100

autoStart:
  enabled: true
```

## Autostart and updates

The app checks daily at 3:00 AM local device time for any updates. If a newer version is found, it downloads the installer, silently replaces the old app, and relaunches the new version.

 The app will always open upon boot, first using Task Scheduler then falling back to being a Startup app if that fails.

## Credits

[HASS.Agent 2.0](https://github.com/hass-agent/HASS.Agent) (hass-agent, forked from LAB02-Research) was heavily leaned on for implementation of sensors and commands, and in the case of monitor wake/sleep, directly mirrored. Go check out this amazing program if you're just looking for sensors and commands without a kiosk!
