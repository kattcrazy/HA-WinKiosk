# Tips and Tricks
Things everyone needs to know to have a successful kiosk setup on a Windows device, especially a Surface Pro 3.

## Surface Pro 3 wake up after monitorsleep
Big thank you to [NexGen3D](https://community.home-assistant.io/t/windows-10-kiosk-app/562484/9) on the Home Assistant Community Forums for this one!

In Regedit...

#### Part 1: Power
`HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Power`
Add the following 32bit Dword if not there already: `PlatformAoAcOverride` and set its value to `0` 

#### Part 2: Passwordless
`HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device`
Change `DevicePasswordLessBuildVersion` from `2` to `0`

Regedit will automatically save your changes so you can now close the window. 

## Monitorwake & autologin
Make sure the user you want to autologin has a password set as it won't work without one. 

#### Part 1: Netplwiz
In netplwiz, there is a checkbox. Uncheck it. If already unchecked, check and uncheck.
<img width="449" height="117" alt="image" src="https://github.com/user-attachments/assets/3b4a811d-b0b5-4c78-902c-a5bc93d41a70" />
Press "apply" and then enter the password as instructed. Then press ok to close the window.

#### Part 2: HA WinKiosk
In HA WinKiosk settings, at the bottom of the list of possible sensors and commands there is a powershell command option. Enable this. 
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
      milliseconds: 500
  - action: button.press
    metadata: {}
    data: {}
    target:
      entity_id: button.[your kiosk name]_powershell_command
```
This effectively wakes up the kiosk from its monitorsleep (will not work with systemsleep or shutdown), waits 500 milliseconds, and presses the enter key to bypass the lockscreen.