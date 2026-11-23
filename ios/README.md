# Cat Bridge for iPhone

The iPhone half of Cat Bridge: a native SwiftUI app that talks to your Windows laptop directly over **Tailscale**. There's no web app, no server in between, and no Mac needed to build or install it.

- **Using it?** Installing and pairing are covered in [docs/SETUP.md](../docs/SETUP.md#5-install-the-iphone-app), and every screen is explained in [docs/FEATURES.md](../docs/FEATURES.md#on-the-iphone).
- **This page** is for building and working on the app.

## What's in the app
| Tab or part | What it does |
|---|---|
| **Laptop** | Battery and focus rings, now playing, Focus 25, Unlock with Face ID, Wake, Find, Lock, Meow, clipboard, open a link |
| **Remote** | Trackpad (move, tap, two-finger scroll and right-click, hold to drag) and keyboard, over a WebSocket |
| **Alerts** | The laptop's notifications and what it sent you |
| **Files** | Send photos, videos and files to the laptop; download from its Shelf |
| **Settings** | Pairing, Face ID unlock set-up, Bark and Shortcuts guides |
| **ShareExtension** | **Send to Laptop** in the share sheet |
| **App Intents** | Send to Laptop, Update Laptop, Copy to Laptop Clipboard, Open on Laptop, Send File to Laptop, Find My Laptop, Lock Laptop, Start Laptop Focus Timer |

## Layout
```
ios/
├── project.yml                 XcodeGen project (app + share extension)
├── CatBridge/
│   ├── App/                    App entry, Face ID unlock key (Secure Enclave)
│   ├── Shared/                 Pairing store (shared Keychain group, also used by the extension)
│   ├── Design/                 Theme and the drawn cat
│   ├── Features/               Screens: Home, Remote, Find, Alerts, Files, Settings, QR scanner
│   └── Intents/                Shortcuts actions
├── ShareExtension/             "Send to Laptop"
├── scripts/                    package-unsigned-ipa.sh (used by CI)
└── Packages/CatBridgeCore/     Networking, models, pairing links, unlock message format + tests
```

## Build without a Mac
Every push to `main` runs **Build iPhone app** (`.github/workflows/ios.yml`) on GitHub's macOS runners and uploads **CatBridge-unsigned-ipa** (Actions → the latest run → Artifacts). Releases attach it as **CatBridge.ipa**. Install it with [Sideloadly](https://sideloadly.io), which signs it with your Apple ID.

## Build on a Mac
```bash
brew install xcodegen
cd ios && xcodegen generate && open CatBridge.xcodeproj
```
Pick your team under *Signing & Capabilities* for both targets. The shared Keychain group is `<TEAM ID>.app.catbridge.shared`, worked out at run time from the app's own team prefix, so the app and extension share the pairing whatever Apple ID signs them.

## Tests
`CatBridgeCore` builds and tests on Linux and macOS:
```bash
cd ios/Packages/CatBridgeCore && swift test
# against a real laptop (copy the link from Cat Center → iPhone → Copy pairing link):
CATBRIDGE_LIVE="catbridge://pair?host=100.x.y.z&port=47800&key=…" swift test
```

## How it talks to the laptop
Plain HTTP to `http://<tailscale-ip>:47800` inside Tailscale's WireGuard tunnel, with the pairing key in `X-Cat-Key`. `Info.plist` allows this with an `NSExceptionDomains` entry for `ts.net` and `NSAllowsArbitraryLoads`. Don't add `NSAllowsLocalNetworking`, because it makes iOS ignore the other exceptions. The full API is in [docs/HOW-IT-WORKS.md](../docs/HOW-IT-WORKS.md#the-bridge-api).
