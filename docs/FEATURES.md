# Everything Cat Bridge does

A guide to every feature on the laptop and the iPhone, and how to use it. To install and set things up, see **[SETUP.md](SETUP.md)** first.

**On the laptop**
- [The island](#the-island)
- [Controls](#controls)
- [The island as your taskbar](#the-island-as-your-taskbar)
- [Mac-style notifications](#mac-style-notifications)
- [The desktop cat](#the-desktop-cat)
- [Focus](#focus)
- [Copy text from the screen and screenshots](#copy-text-from-the-screen-and-screenshots)
- [Shelf and clipboard](#shelf-and-clipboard)
- [Bluetooth batteries and downloads](#bluetooth-batteries-and-downloads)
- [Server monitors](#server-monitors)
- [Cat Face sign-in](#cat-face-sign-in)
- [Cat Center](#cat-center)

**On the iPhone**
- [The Laptop tab](#the-laptop-tab)
- [Unlock with Face ID](#unlock-the-laptop-with-face-id)
- [Wake the laptop](#wake-the-laptop)
- [The Remote tab: your mini laptop](#the-remote-tab-your-mini-laptop)
- [Find my laptop](#find-my-laptop)
- [The Alerts tab](#the-alerts-tab)
- [The Files tab](#the-files-tab)
- [Share sheet](#share-sheet)
- [Shortcuts actions](#shortcuts-actions)
- [Settings tab](#settings-tab)

---

# On the laptop

## The island

A black pill at the top-centre of your screen, always on top, with a cat in it.

- **At rest** it's a small notch with cat ears. The cat naps, and opens its eyes as your pointer comes near.
- **While music plays** the pill widens to show the album art, the current **lyric line** (synced to the song) and a visualizer that moves with the real audio. This works with Spotify, YouTube and SoundCloud in Chrome or Edge, Apple Music, VLC, and anything else that shows up in Windows' media controls.
- **Live activities** are short pop-ups for things that just happened:
  - volume and brightness changes
  - charger plugged in or out
  - Wi-Fi connecting
  - **camera or microphone** turning on, with privacy dots while they're in use
  - timers ending, downloads finishing, Bluetooth devices connecting
  - new notifications
- **Open it** by resting your pointer on it for a moment, or clicking it. You get the **home**: a row of widgets you can tap for a full page, with a back arrow to return.

**Widgets** (choose which show, and their order, in Cat Center → Island):

| Widget | What it shows |
|---|---|
| **Now playing** | Artwork, title, artist, the lyric, play/pause, next, previous, a seek bar |
| **iPhone** | Your phone's battery and charging, its Focus, and buttons to ring it or send it your clipboard |
| **System** | CPU, memory, GPU, disk, network speed |
| **Battery** | Laptop battery, time remaining, power mode, plus Bluetooth device batteries |
| **Sound** | Output device switcher and a **per-app volume mixer** |
| **Alerts** | Recent notifications from Windows and your iPhone |
| **Timer** | Focus/Pomodoro timer and today's total |
| **Shelf** | Files and clips parked for later (see [Shelf](#shelf-and-clipboard)) |
| **Servers** | Status of anything you monitor (see [Server monitors](#server-monitors)) |

It hides during full-screen games, videos and presentations if you turn on *Hide in full screen*.

## Controls

| Action | What it does |
|---|---|
| Rest the pointer on the island | Opens it. It shrinks back shortly after you move away |
| Click the pill | Opens it right away |
| Pin button (top-right when open) | Keeps it open. It releases itself after 60 seconds away |
| Swipe left or right on the island (two fingers) | Previous or next track |
| Scroll on the island | Volume up or down |
| Middle-click | Play or pause |
| Drag files onto the island | Put them on the Shelf |
| `Ctrl + Alt + Space` | Open and pin, or unpin |
| `Ctrl + Alt + A` | Search and open apps |
| `Ctrl + Alt + C` | Open Cat Center |
| `Ctrl + Alt + T` | Copy text from anything on screen |
| `Ctrl + Alt + S` | Screenshot to the Shelf |
| `Ctrl + Alt + H` | Hide or show the island |
| Tray icon | Double-click for Cat Center; right-click for pin, hide, iPhone, face unlock, settings, exit |

## The island as your taskbar

Turn on Cat Center → **Apps and taskbar** → **Hide the Windows taskbar**, and the island takes over the taskbar's job.

**The dock** sits at the top of the island's home. It shows your pinned apps and every open app. A dot marks apps that are running; a longer accent bar marks the one in front.
- **Click** an app to switch to it. Click the app that's already in front to minimise it. If it has several windows, clicking cycles through them.
- **Hover** for a **live preview** of the window.
- **Right-click** for its list of windows, *New window*, *Pin* or *Unpin*, *Move left* or *right*, *Minimise all* and *Close*.
- **Middle-click** opens a new window of that app.

**Attention:** when an app wants you (its taskbar button would normally flash), its icon glows orange and the island shows a pop-up you can click.

**The Apps page** (the grid button in the dock, or `Ctrl + Alt + A`):
- **Search** every installed app, Microsoft Store apps included. Type and press Enter.
- **Pinned** and **recent** apps.
- **Open windows**, with minimise and close buttons.
- **Running in the background:** apps with no window, like Discord, Tailscale or OneDrive, with **Open** and **Quit** (and **Force quit** if one won't close).
- **Windows buttons:** Start, Task view, Desktop, Notification centre, Quick settings, **Tray icons** (shows the real taskbar for a moment), and Show or Hide taskbar.

**Safe by design:**
- The taskbar comes back when you switch this off, when Cat Bridge exits, and even if it crashes. If it ever stays hidden, run **Restore taskbar.cmd** from the app folder.
- The Windows key, Alt + Tab and Win + D keep working.
- Windows only gives the small tray icons to Explorer, so they can't be drawn inside the island. That's what the **Tray icons** button is for.

## Mac-style notifications

- **Banners** drop in at the top-right of the screen like on a Mac (or under the island; choose in Cat Center → Notifications). Your iPhone's notifications always show; Windows notifications can too.
  - They stack, and stay put while your pointer is on them.
  - Close one with ×, by dragging it, or with a two-finger swipe to the right.
  - Hover to see actions: **Copy**, **Open**, and **Copy code 482913** when the message contains a one-time code.
- **Every app is treated the same:** WhatsApp, Shopify, Mail, Messages… each banner shows the app's name, the title and the text.
- **During focus,** banners wait quietly in Alerts, and you get one summary when focus ends.
- **On the sign-in screen,** the Cat Face tile shows a short summary such as *3 new notifications (WhatsApp, Mail)*. Choose **Off**, **Counts** or **Details** in Cat Center → Notifications.
- **Hide Windows' own pop-ups** (optional): *Suppress native notifications* removes each one from Windows' notification centre after the island has shown it.

## The desktop cat

A small cat that lives on top of your taskbar line.
- It wanders, sits, swishes its tail and naps.
- **It never gets in your way.** It never takes keyboard focus, clicks pass straight through it, and it fades when your pointer passes over.
- **To play with it,** rest your pointer on it for a moment. It purrs (hearts!), and then you can:
  - **Click** it for a menu: *Focus 25 min*, *Play / pause*, *Open island*, *Nap 30 min* (do not disturb).
  - **Drag** it anywhere. It dangles by the scruff and drops back to the taskbar when you let go.
  - **Drop files** on it to put them on the Shelf.
- **What it does on its own:**
  - Reminds you to stretch after 50 minutes of unbroken work (change or turn off in Cat Center → Desktop cat).
  - Cheers when a focus timer ends, and perks up when a notification arrives.
  - Lies down and asks for the charger when the battery is low.
  - Falls asleep when you step away, and says welcome back when you return.
  - **Meows** when your iPhone taps *Meow*, and rings loudly for *Find my laptop*.
- **Colours:** ginger, black, white or grey. The island's cat matches.

## Focus

- **Start focus** from the island's Timer widget, the cat's menu, Cat Center, or the iPhone app (*Focus 25*).
- While focused, banners wait quietly and the cat stays calm. You get a summary at the end.
- **Your iPhone's Focus** can put the laptop in focus too, and can start a Pomodoro timer (needs the *Update Laptop* automation, [SETUP step 9](SETUP.md#9-battery-charging-and-focus-from-the-iphone-optional)).
- **Starting focus on the laptop** can turn on your iPhone's Focus, via Bark and your *Cat Focus On* shortcut.
- **Stats:** Cat Center → Focus and the iPhone's Laptop tab show today's focus time, the week, and your streak.

## Copy text from the screen and screenshots

- **`Ctrl + Alt + T`**: the screen dims; drag a box over anything, such as a paused video, an image, a PDF or an app that won't let you select text. The text is copied. It uses Windows' built-in OCR and works offline.
- **`Ctrl + Alt + S`**: drag a box to take a screenshot. It's saved to `Pictures\Screenshots\Cat Bridge`, copied to the clipboard, and put on the Shelf.
- Screenshots you take with **Win + Shift + S** land on the Shelf too.

## Shelf and clipboard

- **The Shelf** is a parking spot for files. Drop files on the island or the cat, take a screenshot, or send files from your iPhone, and they wait there. Drag them back out to any app. Your iPhone can download them from its **Files** tab.
- **Clipboard history** keeps your recent copies (in memory only, never saved to disk). Content that password managers mark as private is skipped. Click a clip and it pastes into the app you were using.
- **Clipboard to the iPhone:** the iPhone widget's *Send clipboard* button, or Cat Center → iPhone → **Send clipboard**, sends it via Bark.

## Bluetooth batteries and downloads

- **Bluetooth:** when AirPods or other Bluetooth devices connect, the pill shows their battery. You get a warning at 15%. Their levels also show in the Battery widget.
- **Downloads:** while a browser downloads to your Downloads folder, the pill shows progress and speed, with a pop-up when it finishes. You can also have finished downloads put on the Shelf.

## Server monitors

The **Servers** widget checks things you care about every 30 seconds:
- **tcp** checks that a host and port accept connections (for example SSH on your VPS).
- **http** checks that a URL answers without a server error.

You get a banner (and a Bark alert on your iPhone) when something goes down, and again when it comes back. Add them in Cat Center → **Servers**. A status URL that returns JSON can also be shown as rows of green, yellow and red dots; see [HOW-IT-WORKS.md](HOW-IT-WORKS.md#settings-reference).

## Cat Face sign-in

Face recognition for the real Windows lock and sign-in screens, using a normal webcam. Set-up is in [SETUP step 10](SETUP.md#10-face-sign-in-on-windows-cat-face-optional).

**How a sign-in works:**
1. **Recognition.** The camera finds your face and compares it with your saved one. In testing, the same person scored 0.83–0.98 and other people 0.32 or lower; the match threshold is 0.42.
2. **Liveness.** The cat asks you to turn your head left or right (random each time). A flat photo can't do that.
3. **Back to centre,** and a final identity check, so a different face can't be swapped in during the turn.
4. **Signed in.** Windows receives your password, which is only released after your face passes.
5. **Two failed tries** (or a timeout or camera problem): a password box appears in the tile.

**Also on the tile:**
- **Unlock with my iPhone**: sends an alert to your phone; approve it with Face ID.
- A one-line notification summary, if you turned that on.
- Your **lost mode** message, if lost mode is on.

**Privacy:** what's saved is 11 sets of numbers describing your face, not photos. They're encrypted with Windows DPAPI in `C:\ProgramData\DynamicIsland\FaceId`, readable only by you, Administrators and SYSTEM. Nothing is uploaded. **Delete my face data** in Cat Center removes them.

**Security:** good against someone sitting at your PC or holding up a photo. A normal webcam can't match Windows Hello's infrared camera: a good video of you turning your head, or a realistic mask, could fool it. Keep a strong password.

## Cat Center

The settings app. Open it with **Ctrl + Alt + C**, by double-clicking the tray icon, or from the island's Apps page. Every setting is here, so you never have to edit a file.

| Page | What's on it |
|---|---|
| **Home** | Laptop, iPhone and Bluetooth batteries; recent notifications; Cat Bridge status; open apps; quick actions |
| **Apps and taskbar** | Hide the taskbar, dock previews, pinned apps |
| **Notifications** | Notification access, banner position and duration, which banners show, lock-screen summary |
| **iPhone** | Tailscale address, port, firewall, pairing QR code, *Stay reachable*, *Let my iPhone see the screen*, Find my laptop (location, lost mode, alarm test), Bark set-up and encryption, unlock requests |
| **Focus** | Pomodoro length, silence banners, follow the iPhone's Focus, the Focus shortcut names, stats |
| **Island** | Widgets and their order, accent colour, lyrics, pop-ups, timings, gestures |
| **Desktop cat** | On or off, colour, stretch reminders |
| **Files and clipboard** | Shelf, screenshots, downloads, clipboard history |
| **Face unlock** | Set up, test, sign-in screen, delete face data |
| **Servers** | Monitors and the status URL |
| **General** | Start with Windows, updates, the settings file |

The window can be resized freely. On narrow widths the sidebar shrinks to icons, tiles reflow and text wraps, so nothing overlaps. Two-finger scrolling is smooth everywhere.

---

# On the iPhone

The **Cat Bridge** app has five tabs: **Laptop**, **Remote**, **Alerts**, **Files** and **Settings**. It talks to the laptop directly over Tailscale, so it works anywhere your phone has internet.

## The Laptop tab

- **Status:** the laptop's name, whether it's reachable, and when it was last seen.
- **Rings** for the laptop's battery (and charging) and today's focus.
- **Now playing** on the laptop, with play/pause, next and previous.
- **Action tiles:**

| Tile | What it does |
|---|---|
| **Focus 25** / **Stop** | Starts or stops a 25-minute focus timer on the laptop |
| **Unlock laptop** | Face ID on the phone, then the laptop unlocks |
| **Wake laptop** | Turns the laptop's screen on |
| **Find laptop** | Opens [Find my laptop](#find-my-laptop) |
| **Lock laptop** | Locks Windows straight away |
| **Meow** | The desktop cat meows. Handy for checking it's connected |

- **Focus card:** today's focus time, this week, and your streak.
- **Clipboard:** send your iPhone's clipboard to the laptop, or fetch the laptop's.
- **Open a link** on the laptop.

## Unlock the laptop with Face ID

Tap **Unlock laptop**, look at your phone, and the locked laptop unlocks. You can also start from the laptop: on the lock screen choose the cat, press **Unlock with my iPhone**, and approve the alert on your phone.

What happens behind the scenes:
1. The laptop's lock screen makes a one-time random challenge.
2. Your iPhone signs it with a key kept in its **Secure Enclave**. The key can't be copied off the phone, and only works after Face ID.
3. The lock screen checks the signature itself against your registered phones, which only Administrators can change, and then signs you in.

It works while you're signed in and the PC is locked. After a restart, sign in once with Cat Face or your password. Set-up: [SETUP step 11](SETUP.md#11-unlock-the-laptop-with-your-iphones-face-id-optional).

## Wake the laptop

**Wake laptop** turns the laptop's screen on over Tailscale. If the laptop is asleep and you're on the same Wi-Fi, it also sends a Wake-on-LAN packet. Keep the laptop reachable with *Stay reachable while plugged in*. See [SETUP step 12](SETUP.md#12-waking-the-laptop-and-keeping-it-reachable-optional).

## The Remote tab: your mini laptop

The Remote tab turns your iPhone into a small laptop: **the laptop's screen, live, on top**, and the trackpad and keys underneath.

**The live screen**
- Shows the laptop's main screen as it happens, pointer included. It works anywhere over Tailscale.
- **Tap** the picture to click exactly that spot. **Hold** for a right-click there.
- **Pinch** or **double-tap** to zoom in on small text; drag to look around while zoomed. Double-tap again to zoom out.
- **Quality** (the sliders button, top left):
  - **Sharp**: the clearest picture, for Wi-Fi.
  - **Smooth**: more frames per second.
  - **Data saver**: for mobile data.
- The status line shows the frame rate. The laptop only sends a new picture when something on screen changes, so a still screen uses almost no data.
- **Hide the screen** with the display button (top right) to use only the trackpad.
- If the laptop is locked, it says so, with an **Unlock with Face ID** button.
- **On the laptop**, while your iPhone is watching:
  - a **purple dot** shows on the island
  - a pop-up appears when viewing starts
  
  To turn screen viewing off, use Cat Center → iPhone → **Let my iPhone see the screen**.

**The trackpad and keyboard**

| Gesture | What it does |
|---|---|
| Move one finger | Moves the pointer |
| Tap | Left click (tap twice quickly to double-click) |
| Two-finger tap | Right click |
| Two-finger drag | Scroll (up, down and sideways) |
| Hold, then move | Drag (click and hold) |

- **Left click** and **Right click** buttons under the pad.
- **Keyboard:** tap the keyboard button and type with the iPhone keyboard; text appears on the laptop.
- **Keys:** Esc, Tab, Enter, Backspace, the arrows, the Windows key, F5, and shortcuts: **Copy**, **Paste**, **Undo**, **Switch app** (Alt + Tab), **Desktop** (Win + D), **Zoom +** and **Zoom −**, volume and play/pause.
- **Pointer speed:** Slow, Normal, Fast or Very fast (the sliders button).

> Windows doesn't let any app type into the lock screen, or into apps running as administrator. Use **Unlock laptop** for the lock screen.

## Find my laptop

- **Map:** where the laptop is, how accurate that is, and when it was seen, plus its battery, Wi-Fi and internet address. **Directions** opens Apple Maps. Needs Location turned on in Windows.
- **Make it ring:** plays a loud sound at full volume, even if the laptop is muted, until you press **Stop the sound**.
- **Lost mode:** locks the laptop and shows your message (for example *Please call 0803 000 0000*) on its screen and on the sign-in tile. Turn it off from the same screen.

## The Alerts tab

- **On the laptop:** recent notifications from the laptop.
- **Sent to me:** everything the laptop has sent you: clipboard, links, alerts.

## The Files tab

- **Send to laptop:** pick **Photos and videos** or any file. They land in `Downloads\Cat Bridge` on the laptop and on the island's Shelf.
- **On the laptop's shelf:** the files on the Shelf. Tap one to download it to your iPhone.

## Share sheet

In Safari, Photos, Files or almost any app, tap **Share → Send to Laptop**:
- **Links** open in the laptop's browser.
- **Text** goes to the laptop's clipboard.
- **Photos, videos and files** land in `Downloads\Cat Bridge` and on the Shelf.

If it asks for the pairing link, open the Cat Bridge app once (it shares the pairing with the extension), or press **Copy pairing link** in Cat Center → iPhone and paste it.

## Shortcuts actions

Cat Bridge adds these actions to the **Shortcuts** app. They run in the background and never ask for addresses or keys.

| Action | What it does | Good for |
|---|---|---|
| **Send to Laptop** | Shows a notification (app, title, body) on the laptop | A *Notification* automation for WhatsApp, Shopify, Mail… |
| **Update Laptop** | Sends your battery level, charging state and Focus | *Battery Level*, *Charger* and *Focus* automations |
| **Copy to Laptop Clipboard** | Puts text on the laptop's clipboard | A shortcut on your Home Screen or Back Tap |
| **Open on Laptop** | Opens a link in the laptop's browser | |
| **Send File to Laptop** | Sends a file or photo | |
| **Find My Laptop** | Makes the laptop ring | Ask Siri *"Find my laptop"* |
| **Lock Laptop** | Locks the laptop | Lock it when you leave home (*Leave* automation) |
| **Start Laptop Focus Timer** | Starts a focus timer for the minutes you choose | |

Examples are in [SETUP steps 8 and 9](SETUP.md#8-your-iphones-notifications-on-the-laptop-optional).

## Settings tab

- **Laptop:** which laptop you're paired with, its address, **Scan a new code**, and unpair.
- **Unlock laptop with Face ID:** set up or remove this phone.
- **Bark guide:** how to get laptop alerts on your iPhone.
- **Shortcuts guides:** step-by-step for the automations above.
- **About:** version and links.
