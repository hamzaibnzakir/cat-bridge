# Changelog

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
