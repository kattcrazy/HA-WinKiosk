# My Setup
How I've set up my Surface Pro 3, in the form of a tutorial.

## 1. Windows Settings
Set the following Windows settings.

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

## 2. Wake up after monitorsleep
Big thank you to [NexGen3D](https://community.home-assistant.io/t/windows-10-kiosk-app/562484/9) on the Home Assistant Community Forums for this one!

In Regedit...

#### Part 1: Power
`HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Power`

Add the following 32bit Dword if not there already: `PlatformAoAcOverride` and set its value to `0` 

#### Part 2: Passwordless
`HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device`

Change `DevicePasswordLessBuildVersion` from `2` to `0`

Regedit will automatically save your changes so you can now close the window. 

## 3. Monitorwake & autologin
Make sure the user you want to autologin has a password set as it won't work without one. 

#### Part 1: Netplwiz
In netplwiz, there is a checkbox. Uncheck it. If already unchecked, check and uncheck.
<img width="449" height="117" alt="image" src="https://github.com/user-attachments/assets/3b4a811d-b0b5-4c78-902c-a5bc93d41a70" />

Press "apply" and then enter the password as instructed. Then press ok to close the window.

#### Part 2: HA WinKiosk
In HA WinKiosk settings in the MQTT section, there is a powershell command toggle. Enable this. 
In the new input box below it, put `(New-Object -ComObject WScript.Shell).SendKeys("{ENTER}")`. 
Now press Save & Back to Kiosk.

#### Part 3: Home Assistant
In Home Assistant, you'll see the powershell command as a new MQTT button (if you've set up MQTT).
Put it into an automation along with the monitorwake command like this, replacing `[your kiosk name]` with the name of your kiosk device that you put in HA WinKiosk settings.

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
      entity_id: button.[your kiosk name]_powershell_command
  - delay:
      hours: 0
      minutes: 0
      seconds: 0
      milliseconds: 700
  - action: button.press
    metadata: {}
    data: {}
    target:
      entity_id: button.[your kiosk name]_powershell_command
```
This effectively wakes up the kiosk from its monitorsleep (will not work with systemsleep or shutdown), waits 700 milliseconds, presses the enter key to bypass the lockscreen, then repeats the last 2 steps to ensure it worked.

## 4. Longterm management
Make sure the user you want to autologin has a password set as it won't work without one. 

#### Updates
In Home Assistant, you'll see 'Run windows updates' as a MQTT button (if you've set up MQTT).
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

#### Memory Refresh
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
