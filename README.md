# <img src="light_logo.png" alt="HA WinKiosk logo" width="36" /> HA WinKiosk <img src="light_logo.png" alt="HA WinKiosk logo" width="36" />
Home Assistant Windows Kiosk - An open-source Windows webpage kiosk designed for integration with Home Assistant. Prevents access to the typical Windows UI without pin access and publishes MQTT commands and sensors to Home Assistant. Configurable gestures for reload, clear cache, send MQTT message, and more.

## Quick Start

1. Download the .exe file from the latest release and once downloaded double click/open to install. It will automatically install .NET 8 if needed.
2. On first run, Settings will open. Enter your kiosk webpage URL, MQTT host, port, username, password, and other details as needed.
3. Click Save & Back to Kiosk - the fullscreen kiosk will load your HA dashboard or chosen URL.
4. Click the gear button to open Settings (If you've disabled the settings button use a configured gesture with action set to `settings`, or MQTT `opensettings`). Use Exit to Windows in Settings to quit the app.

**I recommend checking out [my setup](docs/my_setup.md) if you want to sleep/wake your kiosk, use autologin, have troubles with the app not starting, or have a Surface Pro 3. Please check this out before making an issue!**
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

Instead of continuing, I've optimised this kiosk app to work with [voice-satellite-card-integration](https://github.com/jxlarrea/voice-satellite-card-integration) by jxlarrea. Mic access, input device, output device, and volume can be changed from the config section of HA WinKiosk's settings. If you want to customise the Voice Assist appearance to match this app better, you can follow the intergration instructions for skins and extra css.

## MQTT and Home Assistant

With MQTT configured, the app publishes MQTT payloads that show up in Integrations > MQTT in Home Assistant. In the app Settings, under MQTT, you can choose which sensors and commands are exposed.

### MQTT entities & services

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
| Run Windows updates | Button | Starts a Windows Update scan/download/install run. If  `Respect active hours` is on, it will wait till inactive hours before restarting. If it's off, it will restart within 30 seconds. |
| PowerShell commands | Button(s) | One HA button per named PowerShell row when the master toggle is on |
| Update sensors | Button | Immediately publish all enabled sensor values (entity is auto-created when any sensor is enabled) |
| Battery level | Sensor | Remaining battery % (not available on PCs without a battery) |
| Last Active | Sensor | Seconds since last input (updates every 1 second, ignoring the update interval) |
| Windows updates pending | Number | Count of available Windows updates |
| CPU usage | Sensor | Processor load % |
| Memory usage | Sensor | Physical memory used % |
| Monitor state | Binary sensor | Whether the display is on (`on`/`off`) |
| Current URL | Sensor | WebView address currently shown in the kiosk |
| Release info | Sensor | App version and breaking changes (always published) |
| Camera | Camera | Optional MQTT camera when Camera stream mode is HA camera entity |
| Monitor brightness | Number | Brightness % (1-100 by default; 0-100 when `Allow 0% brightness` is on)|

When the command Navigate is enabled in Settings, the kiosk listens on `{discoveryPrefix}/{device}/nav` (payload = HA path).

```yaml
service: mqtt.publish
data:
  topic: "homeassistant/kiosk/nav"
  payload: "/lovelace/default_view"
```

### Camera
Camera stream settings appear under MQTT -> Sensors when a camera is present, and the camera can be chosen under Config.

| UI Name | Values | Notes |
| --- | --- | --- |
| Camera stream | `off` / `ha` / `mjpeg` | Off, HA MQTT camera entity, or LAN MJPEG for an NVR |
| FPS | `1..15` | MJPEG mode only. Note that higher means more network traffic. |
| Port | integer | MMJPEG mode only. Listen port (default 8081) |

| Mode | Meaning |
| --- | --- |
| Off | Camera(s) are not in use |
| MJPEG stream for NVR | MJPEG camera stream on a configurable port at `http://<kiosk-ip>:<port>`, ready for any NVR or software that can ingest HTTP MJPEG (for example, Frigate). Make sure to set a static IP on the kiosk so the URL doesn't change. |
| HA camera entity | Camera feed is sent to Home Assistant alongside any other MQTT sensors or commands. **EXPECT LAG** - I haven't found a way to fix this yet (feel free to make an issue if you know a way) |


## Settings

Settings are stored under `%APPDATA%\HA-WinKiosk\`:

| File | Contents |
| --- | --- |
| `settings.yaml` | Kiosk URL, gestures, MQTT, PIN hint, and most other options |
| `pin-secrets.json` | Settings PIN, verification question, and reset answer (base64-encoded; not editable in YAML) |

Settings can be edited in the UI or by editing `settings.yaml`. PIN-related secrets are set in the UI only and are written to `pin-secrets.json` automatically. On upgrade, any legacy PIN fields in `settings.yaml` are migrated into that file and removed from the YAML.

Below is a key of what all the options are, and below that is an example `settings.yaml`.

![Settings](https://github.com/user-attachments/assets/e09a5801-a07b-45db-98b0-c6f4b96e4eb7)

### All Options

#### Config

| UI Name | Values | Notes |
| --- | --- | --- |
| Kiosk URL | URL | Kiosk page URL |
| Ignore HTTPS cert warnings | `true`/`false` | Auto-allow invalid/self-signed TLS certs for the kiosk URL |
| Do Not Disturb | `true`/`false` | Suppress toast notifications while the kiosk is running |
| Beta updates | `true`/`false` | When `true`, automatic updates may install GitHub prereleases; when `false`, only stable releases |
| Theme | `auto`/`light`/`dark` | UI theme mode |
| Brightness (%) | `0..100` | Screen brightness |
| Allow 0% brightness | `true`/`false` | When `false` (default), local slider and HA brightness entity minimum is 1% |
| Start when Windows starts | `true`/`false` | Launch app at sign-in |
| Respect active hours | `true`/`false` | When on, a required reboot after Windows Update is deferred to outside active hours; when off, reboot is scheduled in 30 seconds |
| Playback device | string (MMDevice ID) | Empty keeps Windows default playback device |
| Input device | string (MMDevice ID) | Empty uses Windows default capture device |
| Camera device | string (video device ID) | Empty uses the first available camera |
| Volume (%) | `0..100` | Master volume for the playback device |
| Settings PIN | string | PIN required when PIN protection is enabled. UI only; stored in `pin-secrets.json` |
| PIN hint | string | Hint shown on PIN prompt |
| Verification question | string | Forgot-PIN verification question. UI only; stored in `pin-secrets.json` |
| PIN reset answer | string | Forgot-PIN verification answer. UI only; stored in `pin-secrets.json` |
| PIN protection | `true`/`false` | `false` disables PIN gate |

##### Buttons

| UI Name | Values | Notes |
| --- | --- | --- |
| Settings button | `true`/`false` | Show/hide gear button |
| Custom button | `true`/`false` | Floating action button to the left of the gear (or alone top-right if gear is hidden) |
| Custom button icon | `mdi:name` | Material Design Icon id; default `mdi:button-pointer` |
| Custom button action | same as gestures (no Disabled), plus `Bring up keyboard` | Dispatches like gesture actions; off/on is the Custom button toggle; keyboard opens TabTip/`osk` |
| Custom button MQTT topic | string | Topic suffix when action is MQTT message |

#### Gestures

Each enabled gesture has an action dropdown with the following possible values:

```Disabled, Reload, Clear cache and reload, Settings, MQTT message, MQTT reconnect```

When action is `MQTT message`, make sure to set an MQTT topic suffix for that gesture.

##### Options

| Gesture | Options |
| --- | --- |
| Single tap | Location: `Top left`/`Top right`/`Bottom right`/`Bottom left`/`Anywhere` |
| Double tap | Location: same |
| Triple tap | Location: same |
| Quadruple tap | Location: same |
| Quintuple tap | Location: same |
| Swipe | Direction: `Down`/`Left`/`Right` |
| Swipe and hold | Direction: `Down`/`Left`/`Right`<br>Hold threshold (ms): integer |
| Two-finger swipe (touch devices only) | Direction: `Down`/`Left`/`Right` |
| Two-finger swipe and hold (touch devices only) | Direction: `Down`/`Left`/`Right`<br>Hold threshold (ms): integer |
| Pinch | - |
| Zoom | Direction: `Any`/`In`/`Out` |

#### MQTT

Enable or disable each sensor or command in this section of settings. They are listed under [MQTT entities & services](#mqtt-entities--services).

| UI Name | Values | Notes |
| --- | --- | --- |
| MQTT IP | hostname/IP | Broker host |
| MQTT port | integer | Broker port |
| MQTT username | string | Broker username |
| MQTT password | string | Broker password |
| Device name | string | Base ID in MQTT entities/topics |
| Discovery prefix | string | MQTT discovery prefix for Home Assistant (default `homeassistant`) |
| PowerShell commands | name + command rows | Master toggle still uses enabled slug `powershellcommand`; each row becomes its own HA button (`ps_` + sanitized name) |


### Example `settings.yaml`

```yaml
config:
  url: "http://homeassistant.local:8123"
  ignoreCertificateErrors: false
  doNotDisturb: true
  pinHint: ""
  pinProtectionDisabled: false
  showSettingsButton: true
  customButton:
    enabled: false
    icon: "mdi:button-pointer"
    action: reload
    mqttTopic: ""
  uiTheme: auto
  betaUpdates: false
  playbackDeviceId: ""
  inputDeviceId: ""
  cameraDeviceId: ""
  volumePercent: 100
  brightnessPercent: 100
  allowZeroBrightness: false
  autoStartEnabled: true
  windowsUpdateRespectActiveHours: true

gestures:
  singleTapAction: disabled
  singleTapLocation: top-left
  singleTapMqttTopic: "single_tap"
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
      - cpu
      - memory
      - monitor_on
      - current_url
      - last_active
      - updates_pending
    cameraStream:
      mode: off
      fps: 5
      port: 8081
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
      - nav
    powerShellCommands:
      - name: "Restart Explorer"
        command: "Stop-Process -Name explorer -Force"
```
## Autostart and updates

The app checks daily at 3:00 AM local device time for any updates. If a newer version is found, it downloads the installer, silently replaces the old app, and relaunches the new version.

If beta updates are enabled, it will download the latest update, even if it is a pre-release. If disabled, it will download the latest stable release.

When Start on boot is enabled in Settings, the app adds itself to the current-user Run key and opens immediately. The exception for this is if Windows Smart App Control decides that it's not safe to open (even if it has opened before). To solve this, read [my setup](docs/my_setup.md) docs. 

## License

This project uses the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html). See [LICENSE](LICENSE) for the full legal text. In short: you can use, change, and share it freely. If you distribute a modified version, you must offer it under the same license and share the source too, so the work (and its derivatives) stay open. You cannot take this code, tweak it, and ship it as a closed product.

## Credits

[HASS.Agent 2.0](https://github.com/hass-agent/HASS.Agent) (hass-agent, forked from LAB02-Research) was heavily leaned on for implementation of sensors and commands, and in the case of monitor wake/sleep, directly mirrored. Check out this program if you're just looking for sensors and commands without a kiosk!

[Browser Mod 2](https://github.com/thomasloven/hass-browser_mod) was referenced and inspiration for the navigation function.

## About
This is my first Windows app (super excited that I finally made one). I use it for my own setup and it's been really helpful. Please report an issue if something doesn't work, I'll try my best to fix it.

Contributions/PRs welcome. 

If this app helps you out, consider supporting me [here](https://kattcrazy.nz/product/support-me/) :)
