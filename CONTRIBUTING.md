# Contributing to Cat Bridge

Thanks for helping! A few things keep the project easy to work on.

## Getting set up
- **Windows app:** you need the .NET 8 SDK on Windows. Run `cd windows && pwsh ./build.ps1`. The first build downloads the 37 MB face model.
- **iPhone app:** you need a Mac with Xcode 16 and `brew install xcodegen`. Run `cd ios && xcodegen generate`. No Mac? Push a branch and let `ios.yml` build it.
- **Core client:** run `cd ios/Packages/CatBridgeCore && swift test`. It works on macOS and Linux. Set `CATBRIDGE_LIVE` to a pairing link to test against a running laptop.

## Pull requests
1. Branch from `main`: `feature/…` or `fix/…`.
2. Keep each PR to one change, and describe what you tested and on which hardware.
3. Both CI workflows must pass.
4. Add UI text in plain, friendly English; the app talks like a helpful cat, not a manual.

## Things to be careful with
- **`windows/FaceSignIn`** runs inside the Windows sign-in screen as SYSTEM. Never block or filter Windows' own sign-in options. Keep the password fallback. Test on a VM or spare account first.
- **The bridge** must only listen on Tailscale and loopback addresses, and must check the pairing key on every API call.
- **Never commit** secrets, pairing keys, Bark keys, signing certificates or `.ipa` files.

## Reporting bugs
Use the bug report template. Include your Windows build, iOS version, and the relevant lines from `%AppData%\DynamicIsland\log.txt`.
