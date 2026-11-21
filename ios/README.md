# Cat Bridge for iPhone

The iPhone half of Dynamic Island for Windows: a native SwiftUI app that talks to your laptop's island directly over **Tailscale**. No Safari, no web app.

## What it does
- **Laptop at a glance:** battery, what's playing (play/pause, next, previous), focus timer.
- **Remote:** Find my laptop (the desktop cat meows), Lock laptop, start/stop a focus timer.
- **Clipboard both ways**, and **open links** on the laptop.
- **Files:** send photos, videos and any file to the laptop (they land in `Downloads\Cat Bridge` and on the island's shelf), and download files from the shelf.
- **Alerts:** the laptop's notifications and anything the laptop sent you.
- **Unlock the laptop with Face ID** (Secure Enclave key), **wake it**, **find it** on a map, ring it, or put it in lost mode.
- **Remote:** the iPhone becomes the laptop's trackpad and keyboard.
- **Share sheet:** "Send to Laptop" from any app.
- **Focus:** today's focus time, the week and your streak.
- **Shortcuts actions** that run in the background, with no URLs or keys to type:
  - **Send to Laptop**: put an iPhone notification on the laptop's island (use it in an *App Notification* automation for Shopify, WhatsApp…)
  - **Update Laptop**: battery, charging and Focus
  - **Copy to Laptop Clipboard**, **Open on Laptop**, **Send File to Laptop**, **Find My Laptop**, **Lock Laptop**, **Start Laptop Focus Timer**

## Build (no Mac needed)
Every push to `main` runs **Build iPhone app** on GitHub's Mac machines and uploads **CatBridge-unsigned-ipa** (Actions tab → latest run → Artifacts). The core networking code is also tested on Linux.

## Install with Sideloadly (Windows)
1. Download the .ipa artifact and unzip it.
2. Install [Sideloadly](https://sideloadly.io), plug in your iPhone, drop the .ipa in, enter your Apple ID and press Start.
3. On the iPhone: **Settings → Privacy & Security → Developer Mode** → on (restart when asked). Then **Settings → General → VPN & Device Management** → trust your Apple ID.
4. A free Apple ID keeps sideloaded apps for **7 days**. Re-install (your pairing is kept), or turn on Sideloadly's auto-refresh with the phone on the same Wi-Fi.

## Pair
On the laptop: **Cat Center → iPhone**. Scan the QR code with the iPhone Camera (it opens Cat Bridge) or inside the app. The code holds the laptop's Tailscale address, the port and the pairing key. The key is kept in the iPhone Keychain.

## Alerts on a locked iPhone
Push notifications need a paid Apple Developer account, so the laptop sends pop-ups through the free **Bark** app instead (Cat Center → iPhone → Bark). With Bark's AES encryption turned on, only your iPhone can read them.

## Develop
```bash
cd ios && brew install xcodegen && xcodegen generate && open CatBridge.xcodeproj
cd ios/Packages/CatBridgeCore && swift test        # unit tests
CATBRIDGE_LIVE="catbridge://pair?host=100.x.y.z&port=47800&key=…" swift test   # against your laptop
```
- `Packages/CatBridgeCore`: models, the HTTP client and pairing links (Foundation only, so it's testable anywhere).
- `CatBridge/`: the SwiftUI app and its App Intents.
