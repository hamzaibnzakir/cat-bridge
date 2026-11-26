# Changelog

## 2.3.0
- **Your iPhone is now a mini laptop.** The Remote tab shows the laptop's screen live on top, with the trackpad and keys underneath. Tap the screen to click that spot, hold for a right-click, and pinch or double-tap to zoom. Choose Sharp, Smooth or Data saver. Frames are only sent when the screen changes.
- **You always know when you're being watched:** a purple dot on the island and a pop-up while your iPhone views the screen. Turn it off in Cat Center → iPhone → *Let my iPhone see the screen*.
- **New documentation:** a step-by-step [set-up guide](docs/SETUP.md), a [guide to every feature](docs/FEATURES.md), [troubleshooting](docs/TROUBLESHOOTING.md), and [how it works](docs/HOW-IT-WORKS.md) (architecture, security, the bridge API, every setting). They replace `docs/WINDOWS.md`. The Windows zip now includes the set-up, features and troubleshooting guides.

## 2.2.0
- **Unlock with your iPhone:** Face ID on the iPhone unlocks the locked laptop. Set it up in the iPhone app (Settings), then press Allow in Cat Center after checking the 6-digit code. Or press "Unlock with my iPhone" on the lock screen and tap the alert on your phone.
- **Wake from the iPhone:** the screen turns on from anywhere over Tailscale. Wake-on-LAN works on the same Wi-Fi. A new "stay reachable while plugged in" option keeps the laptop awake.
- **iPhone trackpad and keyboard:** a new Remote tab with the trackpad, typing, shortcut keys, volume and play controls.
- **Share sheet:** "Send to Laptop" from Safari, Photos, Files or any app.
- **Find my laptop:** map location, a loud alarm, and lost mode (a message on screen and on the sign-in tile).
- **Copy text from anything on screen** (Ctrl + Alt + T) and **screenshots to the shelf** (Ctrl + Alt + S, plus Win + Shift + S).
- **AirPods and Bluetooth battery** and **download progress** in the pill.
- **One focus for both devices:** quiet banners during focus, follow your iPhone's Focus, tap the iPhone to turn its Focus on. Pomodoro and weekly stats in Cat Center and on the iPhone.
- Shopify is now an ordinary notification: the separate page, widget and sale cards are gone.
- After updating, Cat Bridge offers to refresh the Cat Face sign-in tile (one admin prompt).

## 2.1.0
- **Windows installer:** `DynamicIsland-Setup.exe` installs Cat Bridge for your account (no admin needed). It adds Start menu shortcuts for Cat Bridge and Cat Center, and can start with Windows. It also installs .NET 8 for you if it's missing.
- **Automatic updates** from GitHub Releases, in Cat Center → General → Updates (choose Automatic, Ask me or Off). Downloads are checked against GitHub's SHA-256 before installing, and the app restarts by itself.
- **Clean uninstall:** puts the taskbar back, removes Cat Face sign-in (one admin prompt), and asks before deleting your settings.
- The app now has its own icon.

## 2.0.1
- iPhone: fixed "App Transport Security policy requires a secure connection" when connecting to the laptop's Tailscale address.

## 2.0.0
- **Cat Center:** a desktop app for every setting, with a responsive layout and smooth two-finger scrolling.
- **Island taskbar:** hide the Windows taskbar. The island adds a dock with live previews, an app launcher with search, background apps, and an attention glow.
- **Mac-style banners** for iPhone and Windows notifications, plus **Shopify sale cards** with a daily total.
- **Native iPhone app** (SwiftUI) over Tailscale with Shortcuts actions. Laptop alerts arrive via Bark.
- **Sign-in screen summary** on the Cat Face tile.

## 1.x
- 1.7: notifications both ways (Web Push PWA).
- 1.6: Cat Bridge over Tailscale.
- 1.5: Cat Face on the Windows sign-in screen.
- 1.4: face unlock.
- 1.3: cat redesign and desktop cat.
- 1.0–1.2: Dynamic Island with lyrics, mixer, notifications, shelf and monitors.
