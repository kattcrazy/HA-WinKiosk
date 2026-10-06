# Further Instructions
This document contains instructions and tips on how to implement some automations or features you're wanting for your kiosk. This guide assumes you've connected this to Home Assistant via MQTT, are alright making basic automations, and editing Windows settings. Setting locations are based on Windows 11.

## Best Windows settings (highly reccomended)
Set the following Windows settings.

#### Windows Security > App & Browser control > Smart app control
Turn this off! I know this does expose your device, but unfortunately this is the only way I have found to stop it from randomly preventing HA WinKiosk from opening.

#### Apps > Startup
Make sure that HA WinKiosk is enabled. If you don't see it here, make sure you've enabled the start on boot / auto start setting in the app.

#### System > Power
Power/screen off timeout: Never

#### Accounts > Sign-in Options
When should windows require you to sign in again? Never

#### Windows Updates
Active hours: Set this to something reasonable

Automatically finish setting up after updating: On

Notify when a restart is required: Off

#### Time & Language > Date & Time
Make sure that the time is correct. If not, fix it. HA WinKiosk relys on this for its 3am update check.

#### Apps > Installed Apps
Remove any apps that will not be used, for example 'calculator', 'notepad', etc. 

## Wake up after monitorsleep (Surface Pro 3, but may work for other devices)
Big thank you to [NexGen3D](https://community.home-assistant.io/t/windows-10-kiosk-app/562484/9) on the Home Assistant Community Forums for this one!

In Regedit...

<details>
  <summary>How to use regedit?</summary>

  Regedit can be opened like any other app by pressing the windows button then searching by name.

  Once you've opened it, you'll see the following or similar:

  <img width="666" height="375" alt="#363636" src="https://github.com/user-attachments/assets/34e4c8c0-25c0-4938-92b5-5e107db19c22" />

  To make a new key:

  <img width="522" height="278" alt="image" src="https://github.com/user-attachments/assets/5837263b-0068-4f87-91c1-389bfc49b8fa" />


</details>

#### Part 1: Power
Path: `HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Power`

Add the following 32bit Dword if not there already: `PlatformAoAcOverride` and set its value to `0` 

#### Part 2: Passwordless
Path: `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device`

Change `DevicePasswordLessBuildVersion` from `2` to `0`

Regedit will automatically save your changes so you can now close the window. 

## Automatically log in after being on lock screen (Surface Pro 3, but may work for other devices)
Make sure the user you want to autologin has a password set as it won't work without one. 

#### Part 1: Netplwiz
In netplwiz, there is a checkbox. Uncheck it. If already unchecked, check and uncheck.
<img width="449" height="117" alt="image" src="https://github.com/user-attachments/assets/3b4a811d-b0b5-4c78-902c-a5bc93d41a70" />

Press "apply" and then enter the password as instructed. Then press ok to close the window.

#### Part 2: HA WinKiosk
In HA WinKiosk settings in the MQTT section, enable the PowerShell commands toggle.
Add a name and this command: `(New-Object -ComObject WScript.Shell).SendKeys("{ENTER}")`.
Now press Save & Back to Kiosk.

#### Part 3: Home Assistant
In Home Assistant, you'll see a button for that PowerShell command.
Put it into an automation along with the monitorwake command like this, replacing `[your kiosk name]` with your kiosk device name and `[your powershell command entity here]` with the PowerShell button entity from HA.

```
alias: Turn on Kiosk
triggers:
  - at: "07:00:00"
    trigger: time
conditions: []
actions:

  - action: button.press
    metadata: {}
    target:
      entity_id: button.[your kiosk name]_monitor_wake
    data: {}
  - delay:
      hours: 0
      minutes: 0
      seconds: 0
      milliseconds: 700
  - action: button.press
    metadata: {}
    data: {}
    target:
      entity_id: [your powershell command entity here]
  - delay:
      hours: 0
      minutes: 0
      seconds: 0
      milliseconds: 700
  - action: button.press
    metadata: {}
    data: {}
    target:
      entity_id: [your powershell command entity here]
```
This effectively wakes up the kiosk from its monitorsleep (will not work with systemsleep or shutdown), waits 700 milliseconds, presses the enter key to bypass the lockscreen, then repeats the last 2 steps to ensure it worked.

## Automatic Windows updates
In Home Assistant, you'll see 'Run windows updates' as a MQTT button.
Make a Home Assistant automation like this, replacing the time interval with your chosen interval and `[your kiosk name]`  with your kiosk's name. I reccomend not setting the time to 3am or just before as that is when HA WinKiosk updates itself. 

```
alias: Update Kiosk
triggers:
  - at: "04:00:00"
    trigger: time
    weekday:
      - sat
conditions: []
actions:
  - action: button.press
    metadata: {}
    target:
      entity_id: button.[your kiosk name]_run_windows_updates
    data: {}

```

This triggers Windows to check and run updates, and restart if required either outside of your active hours or in 30 seconds, depending on your settings in HA WinKiosk. 

## Memory & Cache Refresh
In Home Assistant, you'll see 'Refresh Kiosk' as a MQTT button (if you've set up MQTT).

Make a Home Assistant automation like this, replacing the time interval with your chosen interval and `[your kiosk name]`  with your kiosk's name. I reccomend not setting the time to 3am or just before as that is when HA WinKiosk updates itself. 

```
alias: Update Kiosk
triggers:
  - trigger: time
    at: "05:00:00"
conditions: []
actions:
  - action: button.press
    metadata: {}
    target:
      entity_id: button.[your kiosk name]_refresh_kiosk
    data: {}

```

This refreshes the kiosk webpage to prevent memory buildup. If prefered, you could trigger the button `[your kiosk name]`_clear_kiosk_cache instead.

<!--

work in progress

### Schedule disabling & enabling of touchscreen to avoid false clicks during times when the screen needs to stay off (Surface Pro 3, but may work for other devices)

powershell.exe

-NoProfile -ExecutionPolicy Bypass -Command "Enable-PnpDevice -InstanceId 'HID\NTRG0001&COL02\5&63F74D3&0&0001' -Confirm:$false"

-NoProfile -ExecutionPolicy Bypass -Command "Disable-PnpDevice -InstanceId 'HID\NTRG0001&COL02\5&63F74D3&0&0001' -Confirm:$false"

highest privileges, system

no conditions
-->
