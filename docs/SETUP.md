# Set-up guide

This guide takes you from nothing to everything working, one step at a time. It takes about 30 minutes, most of it spent on optional extras.

**Contents**
1. [What you need](#1-what-you-need)
2. [Install Cat Bridge on Windows](#2-install-cat-bridge-on-windows)
3. [Let Windows share what the island needs](#3-let-windows-share-what-the-island-needs)
4. [Set up Tailscale on both devices](#4-set-up-tailscale-on-both-devices)
5. [Install the iPhone app](#5-install-the-iphone-app)
6. [Pair the iPhone with the laptop](#6-pair-the-iphone-with-the-laptop)
7. [Alerts on your iPhone with Bark](#7-alerts-on-your-iphone-with-bark-optional)
8. [Your iPhone's notifications on the laptop](#8-your-iphones-notifications-on-the-laptop-optional)
9. [Battery, charging and Focus from the iPhone](#9-battery-charging-and-focus-from-the-iphone-optional)
10. [Face sign-in on Windows (Cat Face)](#10-face-sign-in-on-windows-cat-face-optional)
11. [Unlock the laptop with your iPhone's Face ID](#11-unlock-the-laptop-with-your-iphones-face-id-optional)
12. [Waking the laptop and keeping it reachable](#12-waking-the-laptop-and-keeping-it-reachable-optional)
13. [Let the island replace the taskbar](#13-let-the-island-replace-the-taskbar-optional)
14. [Updates](#14-updates)
15. [Checklist](#15-checklist)

---

## 1. What you need

| | Required | Notes |
|---|---|---|
| **Windows PC** | Windows 10 version 2004 or later, or Windows 11, 64-bit | The installer adds the .NET 8 Desktop Runtime if it's missing |
| **iPhone** | iOS 17 or later | iOS 27 or later to forward *any* app's notifications (step 8) |
| **Tailscale account** | Free | [tailscale.com](https://tailscale.com). Sign in with Google, Microsoft, Apple or GitHub |
| **Apple ID** | A free one works | Used only by Sideloadly to sign the app for your own phone |
| **USB cable** | For the first iPhone install | After that Sideloadly can refresh over Wi-Fi |
| A webcam | Only for Cat Face sign-in | Any normal laptop camera |

You do **not** need a Mac, a paid Apple Developer account, a server, or a port opened on your router.

---

## 2. Install Cat Bridge on Windows

1. Open the [latest release](https://github.com/hamzaibnzakir/cat-bridge/releases/latest) and download **DynamicIsland-Setup.exe**.
2. Run it.
   - If **Windows protected your PC** appears, click **More info → Run anyway**. It shows because the installer isn't code-signed yet; the file's SHA-256 is listed on the release page if you want to check it.
   - No administrator rights are needed. It installs for your user only, in `%LocalAppData%\Programs\Cat Bridge`.
   - If the .NET 8 Desktop Runtime is missing, the installer downloads and installs it first.
3. Leave **Start with Windows** ticked and finish. The island appears at the top of your screen, and the cat drops onto your taskbar.
4. Open **Cat Center**, the settings app: press **Ctrl + Alt + C**, or double-click the cat icon in the tray.

> **Prefer no installer?** Download **DynamicIsland-windows.zip** instead, unzip the whole folder somewhere permanent (keep the `models` folder and the DLLs next to the exe), and run `DynamicIsland.exe`. Turn on *Start with Windows* from the tray menu. The zip version can't update itself.

**Try it now:** play a song in Spotify, YouTube or any app. The pill shows the album art and lyrics. Rest your pointer on the island for a moment to open it.

---

## 3. Let Windows share what the island needs

Some features read things Windows keeps private until you say yes. Each is optional; the feature just stays quiet without it.

| Setting | Where | What it enables |
|---|---|---|
| **Notification access** | Cat Center → **Notifications** → *Open settings*, or Windows Settings → Privacy & security → **Notifications** → let apps access your notifications | Windows notifications on the island and banners. Restart Cat Bridge after turning it on. |
| **Camera for desktop apps** | Settings → Privacy & security → **Camera** → *Let desktop apps access your camera* | Cat Face sign-in |
| **Location** | Settings → Privacy & security → **Location** → on, and *Let desktop apps access your location* | Find my laptop's map |
| **OCR language** | Settings → Time & language → Language & region → your language → **Language options** → *Optical character recognition* | Copy text from the screen (English is usually installed already) |

---

## 4. Set up Tailscale on both devices

Tailscale gives each of your devices a private address (it starts with **100.**) that works from anywhere: home Wi-Fi, mobile data, a café. Only devices signed in to your account can use it, and everything is encrypted.

1. **On the laptop:** install Tailscale from [tailscale.com/download/windows](https://tailscale.com/download/windows) and sign in.
2. **On the iPhone:** install **Tailscale** from the App Store, sign in to the **same account**, and allow the VPN configuration when iOS asks.
3. Turn Tailscale **on** on both devices. In the iPhone app you should see your laptop in the list.
4. In Cat Center → **iPhone**, the **Tailscale address** row now shows the laptop's 100.x.x.x address. Cat Bridge picks it up by itself, even if Tailscale connects after Cat Bridge starts.

**Recommended in the Tailscale admin console** ([login.tailscale.com/admin/machines](https://login.tailscale.com/admin/machines)):
- On the laptop, choose **⋯ → Disable key expiry**, so it never drops off your network after 180 days.
- Optional: turn on **MagicDNS** and Cat Bridge can use the laptop's name (`your-laptop.tailnet-name.ts.net`) instead of its number.

> On the iPhone, keep Tailscale switched on (or turn on **VPN On Demand** in Tailscale's settings) so the app can reach the laptop when you're away from home.

---

## 5. Install the iPhone app

A free Apple ID can install your own apps on your own phone. Sideloadly does the signing on Windows, so you don't need a Mac.

1. Download **CatBridge.ipa** from the [latest release](https://github.com/hamzaibnzakir/cat-bridge/releases/latest).
2. On the laptop, install **[Sideloadly](https://sideloadly.io)**. If it asks, also install **iTunes and iCloud from apple.com** (not the Microsoft Store versions); Sideloadly needs their drivers.
3. Plug the iPhone into the laptop with a cable, unlock it, and tap **Trust** if asked.
4. In Sideloadly:
   - Drag **CatBridge.ipa** into the window.
   - Pick your iPhone under *iDevice*.
   - Type your Apple ID email under *Apple account*.
   - Press **Start**, then enter your Apple ID password and any 2-factor code.
5. **On the iPhone, turn on Developer Mode:** Settings → Privacy & Security → **Developer Mode** → on. The phone restarts; after it restarts, tap **Turn On**.
   (The Developer Mode switch only appears after the first sideloaded app is installed.)
6. **Trust your Apple ID:** Settings → General → **VPN & Device Management** → your Apple ID → **Trust**.
7. Open **Cat Bridge**. Allow notifications if you'd like the app to tell you when files arrive.

### The 7-day rule
Apps signed with a **free** Apple ID stop opening after **7 days**. Your pairing and settings are kept, so you only need to re-sign:
- **Easiest:** in Sideloadly, tick **Automatic refresh** (with *Auto-start* in its tray menu). It re-signs the app over Wi-Fi when the iPhone and laptop are on the same network.
- **Manual:** run steps 3–4 again. It installs over the old copy.
- A paid Apple Developer account ($99 a year) makes it last a year.

### Building the app yourself
Every push builds a fresh unsigned `.ipa` on GitHub's Macs: **Actions → Build iPhone app → the latest run → Artifacts → CatBridge-unsigned-ipa**. See [ios/README.md](../ios/README.md).

---

## 6. Pair the iPhone with the laptop

1. On the laptop open **Cat Center → iPhone**.
2. Press **Allow Tailscale devices** and approve the Windows prompt (one time, needs an admin).
   This adds a Windows Firewall rule that lets **only** Tailscale addresses (100.64.0.0/10) reach Cat Bridge's port, 47800. Your Wi-Fi network and the internet still can't.
3. A **QR code** is shown on the same page. On the iPhone, open the **Camera** app and point it at the code, then tap the banner. It opens Cat Bridge and pairs.
   You can also scan from inside Cat Bridge (**Scan pairing code** on first launch, or **Settings → Scan a new code**), or press **Copy pairing link** on the laptop and send it to yourself.
4. Cat Bridge's **Laptop** tab now shows your laptop's battery and name. The status dot turns green.

The QR code holds the laptop's Tailscale address, the port, and a long random **pairing key**. The key is stored in the iPhone's Keychain and is shared with the Share extension and Shortcuts actions, so they never ask for it.

> **Lost your phone, or want to unpair everything?** Cat Center → iPhone → **New key**. Every phone that paired before has to scan again.

**Quick test:** on the iPhone tap **Meow**. The cat on the laptop meows.

---

## 7. Alerts on your iPhone with Bark (optional)

The Cat Bridge app is sideloaded, so it can't receive push notifications on its own (Apple only allows that with a paid developer account). Instead, the laptop sends alerts through **Bark**, a free, open-source notification app.

1. Install **[Bark](https://apps.apple.com/app/bark/id1403753865)** from the App Store and open it. Allow notifications.
2. Bark shows a URL like `https://api.day.app/AbCdEfGh12345/…`. The part after `api.day.app/` is your **key** (`AbCdEfGh12345`). Copy it.
3. On the laptop: Cat Center → **iPhone** → **Bark** → paste the key → **Send a test**. Your iPhone should get "Meow from your laptop".

### Turn on encryption (recommended)
Without encryption, the Bark server can see the text of your alerts. With it, the server only passes on scrambled data that only your iPhone can read.

1. In Bark: **Settings → Encryption Settings**.
2. Choose **AES128** and mode **CBC**. Enter a **16-character key** and a **16-character IV** (any letters and numbers; Bark can generate them). Save.
3. In Cat Center → iPhone → Bark, paste the same **key** and **IV** into the encryption fields → **Apply** → **Send a test**.

### What you'll get
| When | Alerts |
|---|---|
| **Always** | Laptop battery low; a server you monitor goes down or comes back |
| **When you've been away from the laptop for 2+ minutes** | Timer done, fully charged, Windows notifications (each type can be turned off) |
| **When you ask** | **Ring my iPhone** (rings for 30 seconds), clipboard and links sent from the laptop |

> You can also run your own [bark-server](https://github.com/Finb/bark-server) and set its address in `BarkServer`.

---

## 8. Your iPhone's notifications on the laptop (optional)

This makes notifications from WhatsApp, Shopify, Mail or any other app on your iPhone drop in on the laptop like on a Mac. It uses a Shortcuts automation with Cat Bridge's **Send to Laptop** action.

**iOS 27 or later** (any app):
1. Open **Shortcuts** → **Automation** → **+**.
2. Choose the **Notification** trigger, tap **App**, and pick the apps you want (for example WhatsApp and Shopify).
3. Choose **Run Immediately** and turn off *Notify When Run*.
4. Tap **New Blank Automation**, search for **Send to Laptop** and add it.
5. Fill in the fields from the *Notification* variable:
   - **App** → the *App* variable (or type a name)
   - **Title** → *Notification* → tap it → *Title*
   - **Body** → *Notification* → tap it → *Body*
6. Tap **Done**. The next notification from those apps appears on the laptop.

**iOS 17 to 26:** iOS has no notification trigger before 27. You can still use the **Message** trigger (when you get a message) and the **Email** trigger with **Send to Laptop**, or run Send to Laptop from any shortcut you like.

Shopify, WhatsApp and everything else are shown the same way: as ordinary banners with the app's name.

---

## 9. Battery, charging and Focus from the iPhone (optional)

The laptop's island and Cat Center can show your iPhone's battery and follow its Focus. iOS only shares these through Shortcuts, so add a few small automations. Each one is: **Automation → + → trigger → Run Immediately → add Update Laptop**.

| Trigger | What to set in **Update Laptop** |
|---|---|
| **Battery Level** → *Rises above* 20% (and another for *Falls below* 20%, and so on) | Battery: *Battery Level* (from *Get Battery Level*) |
| **Charger** → *Is Connected* | Charging: on |
| **Charger** → *Is Disconnected* | Charging: off |
| **Focus** → *Work* (or any Focus) → *When Turning On* | Focus: on |
| **Focus** → same → *When Turning Off* | Focus: off |

When your iPhone's Focus turns on, the laptop goes into focus too and holds banners. In Cat Center → **Focus** you can also have it start a Pomodoro timer.

### Laptop focus turning on your iPhone's Focus
iOS doesn't let apps switch Focus, so the laptop asks a shortcut to do it, through Bark (needs step 7):
1. In Shortcuts, make a shortcut named exactly **Cat Focus On** with the action **Set Focus → Work → On**.
2. Make another named **Cat Focus Off** with **Set Focus → Work → Off**.
3. When you start focus on the laptop, a Bark notification arrives; tap it and it runs **Cat Focus On**. The names can be changed in Cat Center → Focus.

---

## 10. Face sign-in on Windows (Cat Face) (optional)

Sign in to Windows by looking at your normal webcam, with a "turn your head" check so a photo doesn't work.

### Save your face
1. Cat Center → **Face unlock** → **Set up my face**.
2. Look at the cat on screen, then turn your head slightly to each side when asked. It takes about 10 seconds.
3. Press **Test face unlock** to try the real check: it recognises you, asks you to turn left or right (random each time), then checks you again at the centre.

### Turn it on for the sign-in screen
1. Cat Center → Face unlock → **Use on Windows sign-in screen**.
2. Type your **Windows password** once. This is your account password, not your PIN. If you sign in with a Microsoft account, it's that account's password. It's checked with Windows before anything is saved.
3. Press **Turn on** and approve the Windows prompt. This installs the sign-in tile to `C:\Program Files\DynamicIsland\FaceSignIn`, where only administrators can change it.
4. Lock the PC (**Win + L**). Under **Sign-in options**, choose the **cat**. From then on Windows remembers it and starts the camera as soon as the screen appears.

- Two failed tries, a timeout or a camera problem → a password box appears in the tile. **Use my password instead** is always there.
- Windows' own PIN and password options are never removed, so you can't be locked out.
- Changed your Windows password? The tile asks for it once, then remembers it.
- **To turn it off:** the same window, or run **Turn off Cat Face sign-in.cmd** from the app folder.

> **How secure is it?** Good against someone sitting at your PC or holding up a photo. Not as strong as Windows Hello with an infrared camera: a good video or a realistic mask could fool a normal webcam. Keep a strong password.

---

## 11. Unlock the laptop with your iPhone's Face ID (optional)

Needs step 10's sign-in tile turned on and the iPhone paired (step 6).

1. On the iPhone: Cat Bridge → **Settings** → **Unlock laptop with Face ID** → **Set up**. Look at your phone.
2. On the laptop, Cat Center shows the request with a **6-digit code**. Check it matches the code on your iPhone, then press **Allow** and approve the Windows prompt.
3. Lock the laptop (**Win + L**). Then either:
   - on the iPhone, tap **Unlock laptop** on the Laptop tab and look at your phone, or
   - on the laptop's lock screen, choose the cat and press **Unlock with my iPhone**, then tap the alert that arrives on your phone (needs Bark).

It works while you're signed in and the PC is locked. After a restart, sign in once with Cat Face or your password.

> **After each Cat Bridge update** you may see **Update Cat Face on the sign-in screen**. Click it and approve the prompt so the sign-in tile matches the new version.

---

## 12. Waking the laptop and keeping it reachable (optional)

**Wake** on the iPhone does two things:
1. Over Tailscale, it turns the laptop's screen on. This works whenever the laptop is awake enough to be on your network.
2. If the laptop is asleep and your iPhone is on the **same Wi-Fi**, it also sends a **Wake-on-LAN** packet.

### Keep it reachable while plugged in
Cat Center → **iPhone** → **Stay reachable while plugged in**. While on power, Windows won't go to sleep (the screen still turns off), so Wake, Unlock and Find always work. On battery, Windows sleeps normally.

### Turn on Wake-on-LAN (for waking from sleep)
1. **Device Manager** → *Network adapters* → your adapter → **Properties**:
   - **Power Management:** tick *Allow this device to wake the computer* and *Only allow a magic packet to wake the computer*.
   - **Advanced:** set *Wake on Magic Packet* to **Enabled** (the name varies by adapter).
2. **BIOS/UEFI:** turn on **Wake on LAN** (on Dell: *Power Management → Wake on LAN/WLAN*).

> Many laptops can only be woken this way over a **wired** connection, for example through a dock with Ethernet. Wi-Fi wake from sleep often isn't supported. If yours doesn't wake, use **Stay reachable while plugged in** instead.

---

## 13. Let the island replace the taskbar (optional)

1. Cat Center → **Apps and taskbar** → **Hide the Windows taskbar**.
2. The island's home now starts with a **dock** of your pinned and open apps. **Ctrl + Alt + A** opens app search.
3. The taskbar comes back when you turn this off, when Cat Bridge exits, and even if it crashes. If it ever stays hidden, run **Restore taskbar.cmd** from the app folder.

The Start button, Alt + Tab and Win + D all keep working. See [FEATURES.md → The island as your taskbar](FEATURES.md#the-island-as-your-taskbar) for everything the dock does.

---

## 14. Updates

**Windows:** Cat Bridge checks GitHub Releases for new versions. In Cat Center → **General → Updates**, choose:
- **Automatic**: installs new versions quietly and restarts by itself.
- **Ask me** (default): tells you a new version is out. Cat Center → General → Updates then shows **Install**, **What's new** and **Skip this version**.
- **Off**.

**Check now** checks immediately. Downloads are verified against the release's SHA-256 before they run.

**iPhone:** download the new **CatBridge.ipa** from the release and install it with Sideloadly over the old one. Your pairing is kept.

---

## 15. Checklist

- [ ] Island at the top of the screen, cat on the taskbar
- [ ] Notification access allowed (Cat Center → Notifications shows "On")
- [ ] Tailscale on and signed in to the same account on both devices
- [ ] Firewall rule added (**Allow Tailscale devices**)
- [ ] iPhone app installed, Developer Mode on, Apple ID trusted
- [ ] Paired: the Laptop tab shows the laptop's battery
- [ ] Bark test arrives on the iPhone (encryption on)
- [ ] Notification automation: a WhatsApp or Shopify message pops up on the laptop
- [ ] Update Laptop automations: iPhone battery shows on the island
- [ ] Cat Face works on the lock screen
- [ ] **Unlock laptop** works from the iPhone
- [ ] Sideloadly automatic refresh is on

Something not working? See **[TROUBLESHOOTING.md](TROUBLESHOOTING.md)**.
