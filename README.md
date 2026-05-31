# <img src="light_logo.png" alt="HA WinKiosk logo" width="36" /> HA WinKiosk <img src="light_logo.png" alt="HA WinKiosk logo" width="36" />
Home Assistant Windows Kiosk – An open-source Windows webpage kiosk designed for integration with Home Assistant. Prevents access to the typical Windows UI without pin access and publishes MQTT commands and sensors to Home Assistant. Configurable gestures for reload, clear cache, send MQTT message, and more.

## Quick Start

1. Download the .exe file from the latest release and once downloaded double click/open to install. It will automatically install .NET 8 if needed.
2. On first run, Settings will open. Enter your kiosk webpage URL, MQTT host, port, username, password, and other details as needed.
3. Click Save & Back to Kiosk – the fullscreen kiosk will load your HA dashboard or chosen URL.
4. Click the gear button to open Settings (If you've disabled show settings button use a configured gesture with action set to `settings`, or MQTT `opensettings`). Use Exit to Windows in Settings to quit the app.

**I recommend checking out [my setup](my_setup.md) if you want to sleep/wake your kiosk, use autologin, have troubles with the app not starting, or have a Surface Pro 3. Please check this out before making an issue!**
## Requirements

- Windows 10/11 (I would be interested to know if this works on previous versions)
- [.NET 8 Runtime (Desktop)](https://dotnet.microsoft.com/download/dotnet/8.0) (the installer will install this automatically if not already present)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 11)

## Kiosk Lockdown

- WebView zoom is blocked (pinch zoom and Ctrl+wheel zoom).
- WebView back/forward swipe navigation is blocked.
- Kiosk window keeps itself topmost and fullscreen, hides the Windows taskbar while running, and restores the taskbar when the app exits.
- Windows key, context-menu key, Alt+F4, Alt+Tab, F11, F12, Ctrl+Esc, and Ctrl+Shift+Esc are intercepted while the kiosk is running. A limitation of running inside Windows Explorer is that the start menu will still come up on windows key/swipe up from bottom.

<img width="1250" height="703" alt="image (21)" src="https://github.com/user-attachments/assets/76113480-37a0-43c6-8385-49aeda76daed" />

## Voice Assist

While I previously attempted to add native openwakeword and wyoming services support with releases [v3.9.3-beta](https://github.com/kattcrazy/HA-WinKiosk/releases/tag/v3.9.3-beta) to [v3.10.9-beta](https://github.com/kattcrazy/HA-WinKiosk/releases/tag/v3.10.9-beta) I have discovered a wonderful intergration that does this just as well, if not better, with a lot less work. 

Instead of continuing, I've optimised this kiosk app to work with [voice-satellite-card-integration](https://github.com/jxlarrea/voice-satellite-card-integration) by jxlarrea.

Mic access, input device, output device, and volume can be changed from the config section of settings. If you want to customise the Voice Assist appearance to match this app better, you can follow the intergration instructions for skins and extra css. I have prewritten some css styles below if you wish to use them.

<details>
  <summary>Styles</summary>

```
/* --- GLOBAL BAR STYLING --- */
#voice-satellite-ui .vs-activity-bar {
  height: 4px !important;
  border-radius: 4px !important;
  transition: all 0.3s ease !important;
}

/* --- LIGHT THEME --- */
#voice-satellite-ui.vs-light {
  --vs-text-user-color: #000000;
  --vs-text-assistant-color: #29b6f6;
}

#voice-satellite-ui.vs-light .vs-text-user {
  color: var(--vs-text-user-color) !important;
}

#voice-satellite-ui.vs-light .vs-text-assistant {
  color: var(--vs-text-assistant-color) !important;
}

#voice-satellite-ui.vs-light .vs-activity-bar {
  background: #ffffff !important;
  box-shadow: 0 0 15px 4px rgba(79, 195, 247, 0.8) !important;
}

/* --- DARK THEME --- */
#voice-satellite-ui.vs-dark {
  --vs-text-user-color: #ffffff;
  --vs-text-assistant-color: #03a9f4;
}

  color: var(--vs-text-user-color) !important;
}

#voice-satellite-ui.vs-dark .vs-text-assistant {
  color: var(--vs-text-assistant-color) !important;
}

#voice-satellite-ui.vs-dark .vs-activity-bar {
  background: #222222 !important;
  box-shadow: 0 0 20px 6px rgba(3, 169, 244, 0.7) !important;
}

```
</details>

## MQTT and Home Assistant

With MQTT configured, the app publishes MQTT payloads that show up in Integrations > MQTT in Home Assistant. In the app Settings, under MQTT, turn Sensors and Commands on or off for which buttons and sensors are discovered.

Entity IDs in HA include your device name (sanitized). Names below match the name field in discovery.

### MQTT entities

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
| Monitor brightness | Number | Brightness % (0–100). Entity ID suffix: `{device}_monitor_brightness`. |

## Settings

Settings are stored at `%APPDATA%\HA-WinKiosk\settings.yaml`. Settings can be edited either in YAML or in the UI. Older settings layouts are read once and migrated to the new layout the next time settings are saved.

Below is a key of what all the options are, and below that is an example `settings.yaml`.

![Settings](https://github.com/user-attachments/assets/e09a5801-a07b-45db-98b0-c6f4b96e4eb7)

### All Options

#### Config

| UI Name | Values | Notes | Published to HA via MQTT? |
| --- | --- | --- | --- |
| Kiosk URL | URL | Kiosk page URL | No |
| Ignore HTTPS cert warnings | `true`/`false` | Auto-allow invalid/self-signed TLS certs for the kiosk URL | No |
| Do Not Disturb | `true`/`false` | Suppress toast notifications while the kiosk is running | No |
| Beta updates | `true`/`false` | When `true`, automatic updates may install GitHub prereleases; when `false`, only stable releases | No |
| Show settings button | `true`/`false` | Show/hide gear button | No |
| Theme | `auto` \| `light` \| `dark` | UI theme mode | No |
| Brightness (%) | `0..100` | Screen brightness. | Yes (`number.{device}_monitor_brightness`) |
| Start when Windows starts | `true`/`false` | Launch app at sign-in. | No |
| Playback device | string (MMDevice ID) | Empty keeps Windows default playback device. | No |
| Input device | string (MMDevice ID) | Empty uses Windows default capture device. | No |
| Volume (%) | `0..100` | Master volume for the playback device. | No |
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
| Discovery prefix | string | MQTT discovery prefix for Home Assistant (default `homeassistant`) | No |
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
| Respect active hours | `On`/`Off` | Toggle under Run Windows updates. When on, reboot (if required) is deferred to outside active hours; when off, reboot is scheduled in 30 seconds. | No |
| Command: PowerShell command | `On`/`Off` | Exposes custom PowerShell MQTT button | Yes |
| PowerShell command text | string | Command text for powershellcommand MQTT command | Yes |
| (YAML only) `mqtt.sensors.updateIntervalSeconds` | integer ≥ 5 | How often `battery` and `updates_pending` refresh. `last_active` still updates every 1 second when enabled. | No |

### Example `settings.yaml`

```yaml
config:
  url: "http://homeassistant.local:8123"
  ignoreCertificateErrors: false
  doNotDisturb: true
  pin: ""
  pinHint: ""
  pinResetQuestion: ""
  pinResetAnswer: ""
  pinProtectionDisabled: false
  showSettingsButton: true
  uiTheme: auto
  betaUpdates: false
  playbackDeviceId: ""
  inputDeviceId: ""
  volumePercent: 100
  brightnessPercent: 100
  autoStartEnabled: true

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
    windowsUpdateRespectActiveHours: true
    powerShellCommand: ""
```

## Autostart and updates

The app checks daily at 3:00 AM local device time for any updates. If a newer version is found, it downloads the installer, silently replaces the old app, and relaunches the new version.

If beta updates are enabled, it will download the latest update, even if it is a pre-release. If disabled, it will download the latest stable release.

When **Start on boot** is enabled in Settings, the app adds itself to the current-user Run key (same approach as [HASS Agent 2](https://github.com/hass-agent/HASS.Agent)). The exception for this is if Windows Smart App Control decides that it's not safe to open (even if it has opened before). To solve this, read [my setup](my_setup.md) docs. 

## License

This project uses the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html). See [LICENSE](LICENSE) for the full legal text. In short: you can use, change, and share it freely. If you distribute a modified version, you must offer it under the same license and share the source too, so the work (and its derivatives) stay open. You cannot take this code, tweak it, and ship it as a closed product.

## Credits

[HASS.Agent 2.0](https://github.com/hass-agent/HASS.Agent) (hass-agent, forked from LAB02-Research) was heavily leaned on for implementation of sensors and commands, and in the case of monitor wake/sleep, directly mirrored. Go check out this amazing program if you're just looking for sensors and commands without a kiosk!

## About
This is my first Windows app (super excited that I finally made one). I use it for my own setup and it's been really helpful. Please report an issue if something doesn't work, I'll try my best to fix it.

Contributions/PRs welcome. 

If this app helps you out, consider supporting me [here](https://kattcrazy.nz/product/support-me/) :)
