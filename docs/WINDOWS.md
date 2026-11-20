# Dynamic Island for Windows: the full guide

An always-on Dynamic Island for the Dell Precision 5540 (any Windows 10 2004+ / 11 PC), redesigned around a cat, plus a little desktop cat that keeps you company.

## The island
- **At rest** it's a black notch with cat ears. The cat in the middle naps, and opens its eyes as your cursor reaches for it.
- **While music plays** the pill shows the album art, the current lyric line and a visualizer that follows the real audio.
- **Live activities**: one-line pop-ups for notifications, volume and brightness, the charger, Wi-Fi, the camera or mic turning on, and timers.
- **Open it** (rest the cursor on it for 0.3 s, or click) to get a **widget home**: Now playing, System, Battery, Sound, Alerts, Focus timer and Shelf. Tap a widget for its full page; the back arrow returns home. You choose which widgets show, and their order, with `widgets` in settings.

## The desktop cat
- It lives on top of your taskbar. It wanders, sits, swishes its tail and naps.
- **It never gets in your way.** It never takes keyboard focus. Clicks pass straight through it, and it fades when your cursor passes over.
- **To interact**, rest your cursor on it for a moment. It purrs (hearts!), and then you can:
  - **Click it** for a menu: Focus 25 min, Play / pause, Open island, Nap 30 min (do not disturb).
  - **Drag it** anywhere. It dangles from the scruff, and when you let go it drops back to the taskbar.
  - **Drop files on it** to save them to the island's Shelf.
- **Useful on its own:**
  - Stretch reminders after 50 minutes of unbroken work (`breakReminderMinutes`, 0 = off).
  - It cheers when a focus timer ends and perks up when a notification arrives.
  - It lies down and asks for the charger when the battery is low.
  - It falls asleep when you step away and says welcome back when you return.
- **Colours**: ginger, black, white or grey (tray → Cat colour). The island's cat matches.
- It hides during full-screen games, videos and presentations.

Hermes features (prompt, queue, Command Center) are parked for now. Set `"hermesEnabled": true` to bring them back.

## Cat Center (the desktop app)
Double-click the tray icon, press **Ctrl + Alt + C**, or use **Cat Center** on the island's Apps page. Every setting is here, so you never need to edit settings.json:
- **Home:** laptop and iPhone battery, Shopify today, notifications, Cat Bridge status, open apps, quick actions.
- **Apps and taskbar**, **Notifications**, **Shopify**, **iPhone**, **Island** (widgets, order, colour, timing), **Desktop cat**, **Files and clipboard**, **Face unlock**, **Servers**, **General**.
- The window is resizable. On narrow widths the sidebar shrinks to icons, tiles reflow and text wraps, so nothing overlaps.
- **Two-finger scrolling** is smooth everywhere (Cat Center and the island). Sideways two-finger swipes scroll sideways strips and dismiss banners.

## The island as your taskbar
Cat Center → **Apps and taskbar** → **Hide the Windows taskbar**. The island then does the taskbar's job:
- **Dock:** the top of the island's home shows your pinned apps and every open app. A dot marks running apps; a longer accent bar marks the one in front.
  - **Click** an app to switch to it. Click the front app to minimise it. With several windows, clicks cycle through them.
  - **Hover** for a live preview of the window.
  - **Right-click** for its windows, New window, Pin/Unpin, Move left/right, Minimise all and Close.
  - **Middle-click** opens a new window.
- **Attention:** an app that wants you (its taskbar button would flash) glows orange, and the island shows a pop-up you can click.
- **Apps page** (the grid button in the dock, or **Ctrl + Alt + A**):
  - Search every installed app, Store apps included. Type and press Enter.
  - Pinned and recent apps.
  - All open windows with minimise and close.
  - **Running in the background:** apps with no window (Discord, Tailscale, OneDrive…) with **Open** and **Quit** (and **Force quit** if one won't close).
  - Windows buttons: Start, Task view, Desktop, Alerts, Quick settings, **Tray icons** (shows the real taskbar for a moment), Show/Hide taskbar.
- **Safety:** the taskbar comes back when you switch this off, when Dynamic Island exits, and even if it crashes. If it ever stays hidden, run **Restore taskbar.cmd** from the app folder. The Windows key, Alt + Tab and Win + D all keep working.
- **Limit:** the small tray icons themselves can't be drawn inside the island, because Windows only gives them to Explorer. That's what the **Tray icons** button is for.

## Mac-style notifications and Shopify
- **Banners:** iPhone notifications (and, if you like, Windows ones) drop in at the top right like on a Mac, or under the island.
  - They stack and pause while you hover.
  - They close with ×, a drag or a two-finger swipe to the right.
  - Hover shows actions: **Copy**, **Open**, and **Copy code 482913** when a message contains a one-time code.
- **Shopify:** order notifications become a green sale card with the amount, the order number and the customer.
  - A cha-ching sound plays and the desktop cat celebrates.
  - A running total for today shows in multiple currencies ($, ₦, €, £, Rp…).
  - The same order arriving twice (Phone Link and Cat Bridge) is counted once.
  - There's a **Shopify today** widget on the island and a Shopify page in Cat Center.
- **Sign-in screen:** the Cat Face tile shows a one-line summary, e.g. "Shopify: 4 orders · $312 today · 3 new notifications". Choose Off, Counts or Details in Cat Center → Notifications.
  - Windows doesn't let other apps put widgets on the lock-screen picture itself, so it appears on the sign-in tile.
  - After updating, open **Face unlock → Use on sign-in screen** once and press Turn on again, so the new tile is installed.

## Cat Bridge (iPhone ↔ laptop over Tailscale)
A native iPhone app, **Cat Bridge**, talks to this laptop directly over Tailscale, anywhere in the world. No Safari, no Tailscale Serve.

**How it connects**
- The laptop listens on its **Tailscale address (100.x.x.x)** and on 127.0.0.1, never on your Wi-Fi or the internet. It picks the address up by itself when Tailscale connects.
- Traffic is encrypted by Tailscale (WireGuard), and every request needs your pairing key.
- Cat Center → iPhone → **Allow Tailscale devices** adds a Windows Firewall rule that only accepts 100.x addresses (one admin prompt).
- Port: 47800 (change it in Cat Center → iPhone). **New key** unpairs every phone.

**Install the iPhone app** (no Mac needed)
1. The app's source is in `ios/` of this repo. Every push to GitHub builds `CatBridge-unsigned-ipa` on GitHub's Mac machines (Actions tab → latest run → Artifacts).
2. On this laptop, install [Sideloadly](https://sideloadly.io), plug in the iPhone, drop in the .ipa and sign in with your Apple ID.
3. On the iPhone: Settings → Privacy & Security → **Developer Mode** on. Then Settings → General → VPN & Device Management → trust your Apple ID.
4. With a free Apple ID the app must be re-installed every 7 days. Sideloadly can refresh it automatically when the phone is on the same Wi-Fi.
5. Pair: Cat Center → iPhone shows a QR code. Scan it with the iPhone Camera (or inside the app).

**What the app does:**
- Laptop battery, music controls, focus timer, Find laptop and Lock.
- Clipboard both ways, send photos, videos and files, and download files from the shelf.
- Open links on the laptop.
- The laptop's notifications, your Shopify total, and what the laptop sent you.

**Shortcuts actions** (no URLs or keys to type):
- **Send to Laptop**: your iPhone's notifications onto the island.
- **Update Laptop**: battery, charging and Focus.
- **Copy to Laptop Clipboard**, **Open on Laptop**, **Send File to Laptop**, **Find My Laptop**, **Lock Laptop**, **Start Laptop Focus Timer**.

**Your iPhone's notifications on the laptop** (e.g. Shopify)
Shortcuts → Automation → **App Notification** → pick Shopify (or WhatsApp, Messages…) → Run Immediately → add **Send to Laptop** and fill App, Title and Body from the notification. iOS lets each automation watch one app.

**Alerts on your iPhone (Bark)**
A sideloaded app can't receive push notifications without a paid developer account, so the laptop sends pop-ups through the free [Bark](https://apps.apple.com/app/bark/id1403753865) app.
1. Paste your Bark key in Cat Center → iPhone and press **Send a test**.
2. Recommended: turn on Bark's AES-128-CBC encryption and paste the key and IV too. Then the Bark server only relays scrambled text.
3. You can also point it at your own bark-server on your VPS.

What arrives:
- **Always:** laptop battery low, and servers going down or coming back.
- **When you've been away for 2+ minutes:** timers, fully charged, and your Windows notifications.
- **On demand:** clipboard and links, and **Ring my iPhone** (rings for 30 s).

## Face unlock (step 1 of 2: set-up and test)
Tray → Face unlock → **Set up my face**: look at the cat, then turn slightly to each side. It takes about 10 seconds.
Then use **Test face unlock** to run the real check:
1. **Recognition.** Your face is matched against your saved template. In testing, the same person scored 0.83–0.98, other people ≤ 0.32, and the match threshold is 0.42.
2. **Liveness.** The cat asks you to turn your head left or right (random each time). A flat photo can't do that.
3. **Back to centre** and a final identity check, so a different face can't be swapped in.
4. **Two failed tries → password.** On the real sign-in screen (step 2), this is where your password box appears.

**How secure is it?** It stops casual attempts, such as someone sitting at your PC or holding up a photo of you. It is not as strong as Windows Hello with an infrared camera: a good video of you turning your head, or a realistic mask, could fool a normal webcam. Keep a strong Windows password.

**Privacy:** what's saved is 11 sets of numbers describing your face, not a photo. They're encrypted with Windows DPAPI and stored in `C:\ProgramData\DynamicIsland\FaceId`, readable only by you, Administrators and SYSTEM. Nothing is uploaded. Tray → Face unlock → Delete my face data removes it.

### On the Windows sign-in and lock screens
Tray → Face unlock → **Use on Windows sign-in screen…**, type your Windows password once (the one you sign in with, not your PIN), then press **Turn on** and approve the Windows permission prompt. Then lock your PC (Win + L) or restart, and choose the **cat** under *Sign-in options*. After you've used it once, Windows remembers it and starts the camera as soon as the screen appears.

- **Two failed tries**, a timeout or a camera problem: a password box appears right in the tile. You can also click **Use my password instead** at any time.
- **You can't be locked out.** Windows' own password and PIN options are never hidden or changed.
- **Changed your Windows password?** The tile asks you to type it once, then remembers the new one.
- **How it works:** the sign-in screen loads a small tile (`DynamicIsland.FaceSignIn.dll`). The camera and face check run in a separate process (`FaceHelper.exe`), so if anything crashes there, the sign-in screen keeps working. Both are installed to `C:\Program Files\DynamicIsland\FaceSignIn`, where only administrators can change them, because Windows runs them as SYSTEM.
- **Your password** is checked with Windows first, then encrypted (DPAPI, machine scope) in a file only SYSTEM and Administrators can open. It's used only after your face passes.
- **To turn it off:** use the same window, or run `Turn off Cat Face sign-in.cmd` in the app folder. As a last resort, from an administrator command prompt run `reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{7a4a5c8b-1a84-498f-9bf5-d0fcffc94a1e}" /f`
- **Logs:** `C:\ProgramData\DynamicIsland\FaceSignIn\provider.log` (the tile) and `helper.log` (the face check).
- **Needs:** .NET Framework 4.8 (built into Windows 10/11) and the .NET 8 Desktop Runtime.

Tuning (settings.json): `faceMatchThreshold` (default 0.42; higher is stricter), `cameraIndex` (default 0), `faceTurnFlip` (set true if it always asks you to turn the opposite way to how it reads you).

## Run it
1. Install the **.NET 8 Desktop Runtime (x64)** if you don't already have it. Windows will prompt with a download link on first launch if it's missing.
2. Unzip the whole **DynamicIsland** folder somewhere permanent (for example `C:\Brainbox\apps\DynamicIsland`) and run `DynamicIsland.exe`. Keep the `models` folder and the DLLs next to it.
3. Right-click the tray icon and choose **Start with Windows** to keep it always on.
4. For face unlock, make sure *Settings → Privacy & security → Camera → Let desktop apps access your camera* is on.

## Controls
| Action | What it does |
|---|---|
| Rest the pointer on it (0.3 s) | Opens the island; it shrinks back to the notch about 0.5 s after you move away |
| Click the pill | Opens it right away (clicking never pins it) |
| Pin button (top-right of the open island) | Keeps it open; releases itself after 60 s away (`pinTimeoutSeconds`) |
| `Ctrl + Alt + Space` | Pin or unpin the expanded view |
| `Ctrl + Alt + H` | Hide or show the island |
| Swipe left / right on the island | Previous / next track |
| Scroll on the island | Volume up / down |
| Middle-click | Play / pause |
| Drag files onto the island | Add them to the Shelf |
| `Ctrl + Alt + A` | Search and open apps (the island's Apps page) |
| `Ctrl + Alt + C` | Open Cat Center |
| Tray icon | Double-click for Cat Center; right-click for pin, hide, iPhone, face unlock, exit |

## Settings
Settings live in `%AppData%\DynamicIsland\settings.json`. Open it from the tray menu, edit it, then choose **Reload settings**.

```json
{
  "accent": "#FFB37A",
  "petEnabled": true,
  "petColor": "ginger",
  "breakReminderMinutes": 50,
  "widgets": ["media", "system", "battery", "sound", "alerts", "timer", "shelf"],
  "hermesEnabled": false,
  "lyricsInPill": true,
  "showVolumeOsd": true,
  "hideInFullscreen": false,
  "topOffset": 6,
  "hoverDelayMs": 300,
  "collapseDelayMs": 450,
  "pinTimeoutSeconds": 60,
  "brainboxStatusUrl": "http://localhost:8787/api/status",
  "brainboxToken": "",
  "hermesQueuePath": "C:\\Brainbox\\automation\\queue",
  "gestures": true,
  "privacyDots": true,
  "showBrightnessOsd": true,
  "notifications": true,
  "notificationsSuppressNative": false,
  "clipboardHistory": true,
  "clipboardAutoPaste": true,
  "monitorIntervalSeconds": 30,
  "monitors": [
    { "name": "Firecrawl tunnel", "type": "tcp", "host": "127.0.0.1", "port": 3002 },
    { "name": "VPS SSH", "type": "tcp", "host": "YOUR.VPS.IP", "port": 22 },
    { "name": "Command Center API", "type": "http", "url": "http://localhost:8787/health" }
  ]
}
```

- `monitors` is checked every `monitorIntervalSeconds`. `tcp` checks that a port accepts connections and `http` checks for a non-5xx response. You get a toast when something goes down and again when it comes back. Entries with an empty host are skipped.
- **Hermes prompt**: each prompt is written to `<hermesQueuePath>\pending\island-<timestamp>.json` as `{ id, created, source: "dynamic-island", status: "pending", instruction }`. It's written to a temp file first and then moved, so Hermes never reads a half-written task. Change the field name in `SubmitPrompt` if your Hermes runner expects something other than `instruction`.
- **Notifications**: Windows must allow apps to read your notifications. If the Alerts tab says access is off, use its button to open the setting, turn it on, then restart the island. `notificationsSuppressNative: true` also removes each one from Windows' Notification Center once the island has shown it.
- **Clipboard**: history is kept in memory only and never saved to disk. Content that password managers mark as private is skipped. `clipboardAutoPaste: true` pastes into the app you were using when you click a clip.

- `brainboxStatusUrl` can be any JSON endpoint, such as your VPS status API through the SSH tunnel. Top-level fields are shown as rows. Values like `running`, `ok` or `healthy` get a green dot, `down` or `failed` red, and `degraded` or `idle` yellow. `{ "workers": { "status": "running" } }` and `[{ "name": "Redis", "status": "up" }]` both work. Nothing is faked: with no URL the panel says "Not connected", and when the endpoint can't be reached it says "Unavailable".
- `brainboxToken` is sent as `Authorization: Bearer …`. It is stored only in your AppData, never in the code.
- `hideInFullscreen: true` hides the island during full-screen games, videos and presentations.

## Where the data comes from
- **Media**: Windows' media session API, which covers Spotify, Chrome/Edge (YouTube, SoundCloud), Apple Music, VLC and more.
- **Lyrics**: [LRCLIB](https://lrclib.net), a free service that needs no API key. YouTube titles like "Artist - Song (Official Video)" are cleaned up automatically.
- **Stats**: Windows performance counters. Battery comes from Win32 power status and Wi-Fi from the Windows connection profile.
- **Visualizer and volume**: Windows Core Audio, via NAudio.

## Credits
Face models: [YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet) (MIT) and [SFace](https://github.com/opencv/opencv_zoo/tree/main/models/face_recognition_sface) (Apache 2.0) from the OpenCV model zoo; licenses are in `models/`. Uses [OpenCvSharp](https://github.com/shimat/opencvsharp) (Apache 2.0).

The notifications listener, per-app mixer, output switching, brightness safety rule (never write an unvalidated DDC/CI brightness), gestures and adaptive idle polling were ported in spirit from [PILLAR](https://github.com/warpirate/pillar-dynamic-island-for-windows) (MIT, © 2026 warpirate). No code was copied; PILLAR is Tauri/Rust/React and this is C#/WPF.

## Build from source
```powershell
cd windows
pwsh ./build.ps1      # → windows/dist/DynamicIsland-windows.zip
```
The 37 MB face model is downloaded from the OpenCV model zoo on the first build.
For a version that doesn't need the .NET runtime installed, use `--self-contained true`. The exe is larger, about 70 MB.

## Troubleshooting
- Errors are logged to `%AppData%\DynamicIsland\log.txt`.
- **No lyrics**: the track isn't in LRCLIB, or the media app reports the title oddly.
- **GPU shows n/a**: the GPU performance counters aren't available. Update your Intel or NVIDIA drivers.
- **Island covered by another always-on-top window**: it re-asserts topmost every 2 seconds, so it reappears on its own.
