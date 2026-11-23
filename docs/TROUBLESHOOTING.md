# Troubleshooting

Find your problem below. If it isn't here, check the [log files](#log-files) and [open an issue](https://github.com/hamzaibnzakir/cat-bridge/issues) with what you tried. **Never paste your pairing key, Bark key or settings.json into an issue.**

- [Installing](#installing)
- [The iPhone can't reach the laptop](#the-iphone-cant-reach-the-laptop)
- [The iPhone app](#the-iphone-app)
- [Alerts and notifications](#alerts-and-notifications)
- [Unlock and Cat Face](#unlock-and-cat-face)
- [The island and the taskbar](#the-island-and-the-taskbar)
- [Other features](#other-features)
- [Log files](#log-files)
- [Uninstalling](#uninstalling)

---

## Installing

**"Windows protected your PC" when running the installer.**
The installer isn't code-signed yet, so SmartScreen doesn't recognise it. Click **More info → Run anyway**. You can compare the file's SHA-256 with the one on the release page (`Get-FileHash .\DynamicIsland-Setup.exe` in PowerShell).

**"You must install .NET Desktop Runtime" when starting the zip version.**
Install the **.NET 8 Desktop Runtime (x64)** from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0). The installer version does this for you.

**Sideloadly says it can't find the device, or fails with a pairing error.**
Install **iTunes and iCloud from apple.com** (not the Microsoft Store versions), unlock the phone, tap **Trust**, and try another cable or port.

**Sideloadly asks for an app-specific password or 2-factor code.**
Enter the code your Apple devices show. Sideloadly signs in to Apple only to make the certificate for your own phone.

---

## The iPhone can't reach the laptop

Work through these in order:

1. **Tailscale is on, on both devices, and signed in to the same account.** In the Tailscale app on the iPhone, the laptop should be listed and show as connected.
2. **Cat Bridge is running on the laptop.** The cat should be on the taskbar and the island at the top.
3. **The firewall rule exists.** Cat Center → iPhone → **Windows Firewall** should say it's allowed. If not, press **Allow Tailscale devices**.
4. **The address is right.** Cat Center → iPhone → **Tailscale address** should show a 100.x.x.x address. If Tailscale gave the laptop a new address, scan the QR code again (iPhone app → Settings → **Scan a new code**).
5. **The key is right.** If someone pressed **New key**, every phone has to scan again. The app says *Not paired* or *Unauthorized*.
6. **Restart the bridge:** Cat Center → iPhone → **Restart bridge**.
7. **Is the laptop asleep?** A sleeping laptop isn't on the network. Turn on **Stay reachable while plugged in**, or see [Wake-on-LAN](SETUP.md#12-waking-the-laptop-and-keeping-it-reachable-optional).

**Test from another Tailscale device** (or the laptop itself):
```powershell
curl http://100.x.y.z:47800/api/hello
```
Any answer, even *This phone isn't paired*, means the bridge is reachable and the problem is the key. No answer means Tailscale, the firewall or the bridge.

**Port 47800 is already used by something else.**
Change it in Cat Center → iPhone → **Port** → **Apply**, then press **Allow Tailscale devices** again and re-scan the QR code.

**Corporate or school laptop.**
Group Policy may block the firewall rule or Tailscale. Ask your IT team, or use a personal PC.

---

## The iPhone app

**"App Transport Security policy requires the use of a secure connection".**
You have an app version older than 2.0.1. Install the latest **CatBridge.ipa** from the releases page.

**The app won't open: "Unable to verify app" or "no longer available".**
The 7-day free certificate expired. Re-install with Sideloadly (your pairing is kept), and turn on Sideloadly's automatic refresh. See [The 7-day rule](SETUP.md#the-7-day-rule).

**"Untrusted Developer".**
Settings → General → **VPN & Device Management** → your Apple ID → **Trust**.

**There's no Developer Mode switch.**
It only appears after the first sideloaded app is installed. Install with Sideloadly first, then check Settings → Privacy & Security.

**The share sheet asks for the pairing link.**
Open the Cat Bridge app once; it shares its pairing with the extension. If it still asks, copy the link from Cat Center → iPhone → **Copy pairing link**, and paste it.

**Shortcuts actions say they're not paired.**
Open the app once after installing or updating, then run the shortcut again.

**The trackpad doesn't type into something.**
Windows doesn't let any app send input to the lock screen or to apps running as administrator. Use **Unlock laptop** for the lock screen.

---

## Alerts and notifications

**The Bark test doesn't arrive.**
- Check the key: it's only the part after `api.day.app/`, with no slashes.
- If you turned on encryption, the **key**, **IV**, **AES128** and **CBC** must match exactly in both Bark and Cat Center. Try with encryption off to narrow it down.
- Notifications for **Bark** must be allowed in iPhone Settings, and Focus may be hiding them.

**iPhone notifications don't appear on the laptop.**
- In the automation, make sure **Run Immediately** is chosen and the trigger lists the right apps.
- Run the automation's **Send to Laptop** by hand from Shortcuts to check it works.
- Notification triggers need iOS 27 or later. On older iOS use Message or Email triggers ([SETUP step 8](SETUP.md#8-your-iphones-notifications-on-the-laptop-optional)).
- During focus, banners wait in Alerts and show at the end.

**Windows notifications don't appear on the island.**
Cat Center → Notifications → **Open settings** (or Windows Settings → Privacy & security → Notifications), then restart Cat Bridge.

**Laptop alerts don't reach the iPhone while I'm at the laptop.**
Timers, *fully charged* and forwarded Windows notifications are only sent when you've been away for 2 minutes or more, so your phone doesn't buzz while you're using the laptop. Battery low and server alerts always go through.

---

## Unlock and Cat Face

**"The lock screen isn't ready."**
The sign-in tile only listens while the lock screen is showing it. Tap a key or the screen on the laptop so the lock screen appears, and make sure Cat Face is turned on for the sign-in screen (Cat Center → Face unlock).

**Unlock with iPhone worked before, but not after an update.**
Accept **Update Cat Face on the sign-in screen** when Cat Bridge shows it (Cat Center → Face unlock has the same button). The tile in `C:\Program Files` is updated separately because it needs admin rights.

**Unlock with iPhone doesn't work after a restart.**
That's by design: after a restart, sign in once with Cat Face or your password. Then it works whenever the PC is locked.

**My phone isn't listed, or was removed.**
Set it up again from the iPhone app (Settings → Unlock laptop with Face ID → **Set up**) and press **Allow** on the laptop after checking the 6-digit code.

**Cat Face asks me to turn the wrong way.**
Turn on `FaceTurnFlip` in settings.json (some cameras mirror the image).

**Cat Face doesn't recognise me.**
- Re-do **Set up my face** in the light you usually sit in.
- If the laptop has more than one camera, set `CameraIndex` to 1.
- *Let desktop apps access your camera* must be on in Windows Settings.

**Cat Face says my password is wrong after I changed it.**
Type the new password in the tile once; it remembers it.

**I want to remove Cat Face from the sign-in screen.**
Cat Center → Face unlock → turn it off, or run **Turn off Cat Face sign-in.cmd** in the app folder. As a last resort, from an administrator command prompt:
```cmd
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{7a4a5c8b-1a84-498f-9bf5-d0fcffc94a1e}" /f
```
Windows' own PIN and password sign-in always stays available, so you can never be locked out.

---

## The island and the taskbar

**The taskbar stayed hidden.**
Run **Restore taskbar.cmd** in the app folder (`%LocalAppData%\Programs\Cat Bridge`). Starting and exiting Cat Bridge also restores it.

**The island is covered by another window.**
It puts itself back on top every 2 seconds. If a full-screen app covers it, that's *Hide in full screen* doing its job.

**No lyrics.**
The song isn't in [LRCLIB](https://lrclib.net), or the app reports the title oddly. YouTube titles like *Artist - Song (Official Video)* are cleaned up automatically.

**GPU shows n/a.**
Windows' GPU performance counters aren't available. Update your Intel or NVIDIA drivers.

**Tray icons aren't in the island.**
Windows only gives tray icons to Explorer. Use the **Tray icons** button on the Apps page, which shows the real taskbar for a moment.

---

## Other features

**Find my laptop shows no location.**
Turn on Windows Settings → Privacy & security → **Location** and *Let desktop apps access your location*. Without Wi-Fi nearby, location can be rough or missing.

**Ctrl + Alt + T copies nothing, or gibberish.**
Install the OCR language for the text: Settings → Time & language → Language & region → the language → **Language options** → *Optical character recognition*. Select a larger area with clear text.

**A shortcut key does nothing.**
Another app may have taken it first. Close the other app or change its shortcut, then restart Cat Bridge.

**AirPods or Bluetooth battery isn't shown.**
Not every device reports its battery to Windows. If Windows Settings → Bluetooth & devices doesn't show a percentage for it, Cat Bridge can't either.

**Downloads aren't shown.**
Only downloads to your **Downloads** folder are watched, from Edge, Chrome, Firefox, Brave and Opera.

**Updates fail.**
Check you're online and can open github.com. The error shows in Cat Center → General → Updates. You can always download the installer from the releases page and run it over the old version.

---

## Log files

| Log | Where |
|---|---|
| The app | `%AppData%\DynamicIsland\log.txt` (paste `%AppData%\DynamicIsland` into the File Explorer address bar) |
| The sign-in tile | `C:\ProgramData\DynamicIsland\FaceSignIn\provider.log` |
| The face check on the sign-in screen | `C:\ProgramData\DynamicIsland\FaceSignIn\helper.log` |

Logs never contain your password, pairing key or face data. Read them before sharing anyway.

---

## Uninstalling

**Windows:** Settings → Apps → Installed apps → **Cat Bridge** → Uninstall.
- It closes the app, restores the taskbar, and removes Cat Face from the sign-in screen (one admin prompt) if you turned it on.
- At the end it asks whether to also delete your settings, saved face and notification history. Choose **No** if you might install it again.
- Optional clean-up: run *Delete my face data* first; delete `C:\ProgramData\DynamicIsland` as an administrator; remove the **Dynamic Island Cat Bridge** firewall rule in *Windows Defender Firewall with Advanced Security*.

**iPhone:** delete the Cat Bridge app. Its pairing key is removed from the Keychain along with it.
