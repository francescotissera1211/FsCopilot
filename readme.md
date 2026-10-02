
[![Discord](https://img.shields.io/discord/1454265644416765974?label=Discord&logo=discord&color=5865F2)](https://discord.gg/HyKMRp47ka)
[![Support on Boosty](https://img.shields.io/badge/Support-Boosty-orange)](https://boosty.to/fscopilot)

☝️ Join our Discord
☝️ Support here

> 🌐 Visit the official website — [FS Copilot — Shared Cockpit for MSFS 2024](https://fscopilot.com)

# 🛫 FS Copilot

> **This is a combined fork.** It merges [upstream FS Copilot](https://github.com/yury-sch/FsCopilot) 1.2.1 with the work from every public fork, and ships every aircraft profile in the build. Download it from [Releases](../../releases), and read [This fork: everything in one build](#-this-fork-everything-in-one-build) before you fly.


**FS Copilot** is a companion app for **Microsoft Flight Simulator 2024** that lets multiple pilots control the same aircraft together — in real time.
Fly as a real crew. 👨‍✈️👩‍✈️

![FS Copilot](https://raw.githubusercontent.com/yury-sch/FsCopilot/refs/heads/main/preview.png)

## 🔀 This fork: everything in one build

FS Copilot's public forks each added something, and none of it reached upstream. This fork merges all of them on top of upstream 1.2.1, keeps every fork author's commits, fixes what didn't work together, and ships every aircraft profile it could find. Every fork branch with work of its own is archived here under `forks/<owner>/<branch>`, in case the original goes away.

### Download and install

1. Download `FsCopilot_v<version>.zip` from this repository's [Releases](../../releases) and unzip it anywhere, for example next to your other add-ons.
2. Run `FsCopilot.exe`.
3. The first window is **Connection settings**. Everything is filled in for you, including your peer ID: 8 random letters made on first run and kept from then on. Press **Enter** or choose **Save settings and connect**. See [Settings](#settings).
4. On first run the **Setup** window offers **Install**. It copies the `fscopilot-bridge` package into your MSFS Community folder (and removes YourControls if it finds it, because the two conflict). Then choose **Continue**.
5. Upgrading from stock FS Copilot: close it, run this build, and choose **Install** when Setup asks. The bridge in your Community folder is replaced.

### Who you can fly with

Everyone in the session must run **this build**. FS Copilot compares the list of network messages on both sides before it links, and refuses a peer whose list differs. Every fork added messages, so this build cannot pair with stock FS Copilot or with any single fork, and they cannot pair with it. Send your crew the same release.

### Connecting: client code, direct links and the relay

- Your **client code** is your peer ID (8 letters, shown at the top of the main window). To fly together, one pilot gives their code to the other, who types it into the **Code to join** box and chooses **Join**. With three or more pilots, everyone joins the same person; once linked, the app introduces the rest to each other.
- The code is only an address. It is not itself a direct connection. When you choose Join, the app tries a **direct** link first for about 4 seconds: both apps ask the server to introduce them, then punch through each other's router (UDP hole punching, with UPnP / NAT-PMP port mapping where the router allows it). A direct link is the fastest, and nothing passes through a server once it is up.
- If the direct attempt fails, the app falls back to the **relay**. Both apps keep a connection to the server, and the server forwards every packet between them. A direct link fails when a router or ISP won't allow hole punching: carrier-grade NAT (common on mobile, satellite and some fibre ISPs), symmetric NAT, strict firewalls, or a server that does not introduce the two peers.
- The relay carries everything the direct link carries: cockpit sync, pointer sync, traffic and ATC audio. It only adds latency, roughly the round trip to the server (about 100 ms from Europe to `p2p.fscopilot.com`). The connection list on the main window shows `direct` or `relay` for each peer, with its ping and packet loss.
- **Two relay protocols, both handled automatically.** This build speaks xiprox's relay protocol v2, which keeps traffic and ATC audio apart from control messages so they survive the relay. Upstream's server `p2p.fscopilot.com` speaks v1. The app recognises a v1 relay from its first reply and switches to v1 for the session, so the default server works without changing anything. A v2 relay (for example xiprox's `fscrelay.ihsan.dev`, or one you host) can be entered in Connection settings.
- **Which server?** Keep the default, `p2p.fscopilot.com`. On 2026-10-02 both servers were tested with this build's own network code, sending 100 packets of every kind at small and full size through each relay:
  - **`p2p.fscopilot.com` (default):** every kind and size arrived, using the automatic v1 fallback.
  - **`fscrelay.ihsan.dev` (xiprox's server, also up):** everything arrived except the largest unreliable packets, which were 1 byte over the limit. That was a budget bug in the traffic/ATC code, now fixed, and it never affected real traffic batches (1008 bytes at most) or voice frames (about 60 bytes).

  Upstream's is the official, long-running server and needs nothing set. A direct link between two copies on *one* computer can't be tested (both servers behaved the same there), so direct links are best judged on a real two-house session; the relay takes over whenever one fails.
- **Connection type** in Connection settings: **Automatic** (direct, then relay) is right for nearly everyone. Choose **Relay only** if joins keep taking several seconds or failing on your network, or **Direct link only** to keep traffic off the server.

### Features added by this fork

**Connection settings window** (xray447, extended here): choose the server, your display name, a fixed peer ID that stays the same between sessions so your crew can keep your code, the connection type, and whether to share ground vehicles.

**Packet loss per peer** (xray447): each peer in the onboard list shows `LOSS: n%` beside its ping. **Reset Stats** clears the counters.

**Pointer sync for panels the normal sync can't reach** (xiprox, upstream PR #35): some glass displays draw with SVG or rebuild their element tree, so the usual "which button was pressed" sync can't find the button on the other side. For instruments listed under `pointer:` in the aircraft profile, the app sends *where* the pointer went instead (presses, holds and drags, as fractions of the display), and the other side replays the gesture at the same spot, one at a time and at the pilot's pace.
- Turn it on in a profile:
  ```yaml
  pointer:
    - DisplayUnits                      # every panel of this instrument
    - DisplayUnits|config=Default       # or one panel of several, by its URL query
  ```
  The Synaptic A220 profile uses it for its display units.
- While a session is starting, a display is locked under a **connecting** overlay. If the link drops mid-session it shows **degraded**, and **Take Control** gets you going again. While a peer's gesture replays it shows **replaying**. A red warning means the panel lost the app. Gestures that weren't acknowledged are resent when a peer reconnects, so the two displays can't drift apart.
- The panels talk to the app over a local WebSocket on the first free port between 9020 and 9024. If all five are taken, the main window says "Panel channel unavailable". Connections from web pages are refused.

**Shared pointer and clicks for iniBuilds A380 displays** (xray447): the A380's MFDs, NDs and SD are WASM displays, which can't be synced element by element. `Definitions/screens.yaml` lists the displays whose pointer and presses are shared. The other pilot's pointer appears as a yellow arrow with their name. Displays are matched by gauge name and order (`DU#2`), not by panel number, because the simulator renumbers panels between sessions. By default the press is shared and the simulator generates the click itself; `clicks: true` shares clicks as well.

**Share AI traffic** (xiprox): one pilot (the host) shares the AI traffic in their sim, and everyone else sees the same aircraft in the same places.
- In the **ATC & Traffic** card, turn on **Share AI traffic**. You must be in a session with the simulator running ("Waiting for the simulator" means it isn't connected yet).
- Only one pilot hosts traffic at a time. The others see "shared by <name>", and their toggle is replaced. If two turn it on at once, the app picks one and tells the other.
- Receivers should switch off their own AI traffic (the card warns "Detected existing AI traffic in your sim" when it finds some), and everyone should use the same traffic pack and scenery.
- Ground vehicles aren't shared by default. Start the app with `--traffic-ground` to include them.

**Share ATC audio** (xiprox): one pilot's ATC app is heard by the whole crew.
- Supported apps are found automatically: BeyondATC, SayIntentions, Pilot2ATC, PF3, FSHud and VoxATC. **Other…** lists every program currently making sound, so you can pick any app.
- The host turns on **Share ATC audio** and picks the app from **ATC audio source app**. Only that program's sound is captured (Windows 10 version 2004 or later), compressed with Opus and streamed. The status reads "Capturing", or "Capturing — muted in the volume mixer" if Windows has that app muted.
- Receivers get a **Mute ATC** button and an **ATC volume** slider. Your choices are saved in `settings.json` beside the exe.

**Developer tools** (xiprox, xray447): start with `--dev` for the Develop window. It records and replays a session's physics, controls and variables (variables-only traces leave the aircraft alone), browses the profile, and lists every glass display with its group and number. "Peer move" and "Peer click" pretend to be the other pilot on one machine.

**Fixes carried here:**
- Every instrument loads on panels that hold several (VCockpit.js; xiprox).
- A profile's `ignore:` list now also protects you from input arriving from the peer.
- A failed join gives back your role.
- A peer that is still handshaking is not shown as connected.
- A peer that left is told apart from one whose link was lost.

### Settings

Everything is set in windows; the command line is only for one-off overrides. Connection settings opens at every start, and the **Settings** button beside your code in the main window opens it any time. Saving there restarts FS Copilot, which leaves any session, because the server, ID and connection type are fixed when the network starts. Escape closes it without saving.

| Where | Setting | Saved in |
| --- | --- | --- |
| Connection settings (at every start, or the Settings button) | Server address, username, peer ID (generated for you; **New ID** makes another, or type exactly 8 letters or digits), connection type (Automatic / Direct only / Relay only), share ground vehicles when hosting traffic | `%LOCALAPPDATA%\FsCopilot\config.json` |
| Main window, ATC & Traffic, while you share ATC | ATC audio source app | `settings.json` beside `FsCopilot.exe` |
| Main window, ATC & Traffic, while you receive ATC | Mute and volume of the received ATC audio | `settings.json` beside `FsCopilot.exe` |

Connection settings checks the fields when you save and says what is wrong ("Peer ID must be exactly 8 letters or digits"), instead of greying the button out. If `config.json` can't be written, it says so and stays open. Changes take effect the next time you press Save, which happens at every start.

### Accessibility

Built for screen readers and tested on 2026-10-02 with Windows UI Automation (the interface NVDA, JAWS and Narrator use):

- **Every control has a short name, and a short hint where one helps.** Every button, box, switch, list, slider and picker is named; hints are a few words (NVDA: NVDA+Tab reads them). Decorative icons and images are hidden.
- **Headings.** Each window title is heading level 1, and the cards (Connection, ATC & Traffic, Onboard) are level 2. Press H in browse mode to jump between them.
- **Spoken updates.** These are spoken as they happen:
  - joining, joined, and why a join failed;
  - who joined or left, and over which link;
  - who has control, by name, whenever it changes hands: "You have control" or "Alpha has control";
  - errors, such as the sim not running or the bridge missing;
  - a newer aircraft profile being available;
  - ATC and traffic sharing starting or stopping;
  - Setup progress;
  - copying your code.

  They use UI Automation notification events, which NVDA, JAWS and Narrator read. Avalonia, the UI toolkit, doesn't raise live-region events on Windows, so they would otherwise stay silent.
- **Text fields speak as you edit.** Left and Right say the character at the cursor, Home and End too, Ctrl+Left/Right the word, Backspace the deleted character, Delete the new one at the cursor, Up and Down the whole field, and Shift selection what it selects. Avalonia gives text boxes no UI Automation Text pattern (still true in 12.1), so a screen reader cannot follow the cursor itself; FS Copilot speaks it instead.
- **Keyboard.**
  - Focus starts in a useful place: the first field in Connection settings, the action button in Setup, the client code box in the main window, and Open GitHub in the update message.
  - Enter submits Connection settings and joins from the code box. Escape closes the update message.
  - Tab follows reading order.
- **Onboard list.** Each pilot is one list item, read as "name, code, direct or relay, quality", with ping and loss as its description. Arrow keys move between pilots. The rows update in place, so the reader doesn't lose its spot as ping changes.
- **Readable codes.** Codes are spelled out ("Q F Q Y E C T D"). **Copy code** puts yours on the clipboard to paste into a message.
- **Readable values.** The ATC volume slider reads as a percentage. The mute button is named for what it will do ("Mute ATC" or "Unmute ATC").

### Command-line options

| Option | What it does |
| --- | --- |
| `--server <host>` | Use this server (matchmaking and relay) for this run instead of the one in Connection settings. |
| `--relay <host>` | The same, xiprox's spelling. |
| `--relay` (no host after it) | Relay only for this run (Johnsmz13); the same as Connection type "Relay only". |
| `--no-direct` | Relay only (xiprox's spelling, keeps both transports loaded). |
| `--p2p` | Direct links only for this run; the same as Connection type "Direct link only". |
| `--peer-id <id>` | Use this peer ID for this run instead of the saved one. |
| `--traffic-ground` | Share ground vehicles for this run; the same as the Connection settings check box. |
| `--traffic-offset <nm>,<deg>` / `--traffic-shadow <m>` | Test aids: place received traffic away from the originals so two copies can run against one sim. |
| `--dev` | Open the Develop window instead of a session. |
| `--debug` | Verbose log (`log` beside the exe). |

### Aircraft profiles

`Definitions/` ships 92 aircraft profiles, so nothing has to be downloaded first:
- **81 from the FS Copilot profile server** (2026-10-02).
- **Seven FSS Boeing 727 profiles** the server no longer serves.
- **Synaptic A220**, with pointer sync.
- **iniBuilds A350, iniBuilds A400M and A2A PA-24 Comanche**, from xiprox's profile collection.

The app still checks the profile server and offers **Download** when your aircraft's profile has a newer version.

Five served profiles include modules under misspelled names (TBM 850, A330, Beluga, Cessna 414AW, Citation CJ3+), so parts of them were skipped. Small alias files in `Definitions/modules/` point those names at the real modules. The profiles themselves are unchanged, so profile updates keep working. Three included modules (`transponder`, `radios`, `AS_G1000_NXi_ALT_MOD`) don't exist anywhere yet.

`Definitions/experimental/` holds alternatives that aren't loaded: LocatedInSpace's PA-28 Dakota autopilot rework (its author marked it as probably not working) and degroat-c's YourControls conversion of the PMDG 737-800. To try one, copy it over the file of the same name in `Definitions/`.

### Running your own server

`FsCopilot.Discovery` is the matchmaking server (UDP 3480) and the relay (UDP 3600), and this repository's copy serves relay protocol v1 and v2 side by side. On Debian 13, `FsCopilot.Discovery/for_debian_13_run.sh` installs .NET and starts it (xray447). Point the app at it with Connection settings or `--server <host>`.

### Where everything came from

| Source | Brought |
| --- | --- |
| [xray447](https://github.com/xray447/FsCopilot) `new_master` | Connection settings window, packet loss, iniBuilds A380 display sync, Debian server script, WASM version fix |
| [Johnsmz13](https://github.com/Johnsmz13/FsCopilot) `main` | `--server`, `--p2p`, `--relay` |
| [xiprox](https://github.com/xiprox/FsCopilot) `pointer-forwarding` (upstream PR #35), `ahead-*`, `dev-var-replay` | Pointer sync, traffic and ATC sharing, relay protocol v2, var replay, GTNXi / RDR1150XL / KFC 150 modules, fixes, research notes in `record/` |
| [xiprox/fsc-editor](https://github.com/xiprox/fsc-editor) corpus | A350, A400M, PA-24 profiles, newer A220 profile |
| [LocatedInSpace](https://github.com/LocatedInSpace/FsCopilot) `main` | Experimental PA-28 Dakota rework |
| [degroat-c/pmdg737-fscopilot](https://github.com/degroat-c/pmdg737-fscopilot) | Experimental PMDG 737-800 conversion |
| This fork | v1 relay fallback, the relay packet budget fix, connection type and ground-vehicle settings, the accessibility work, module aliases, bundled profiles, update check pointed at this repository |

[3617luke](https://github.com/3617luke/FsCopilot) holds two of Yury's commits that upstream already has. harrycollin, art-drobanov, coisasgamer4, kpolkowski, grzegorzkibitz, demendet, lLeolau and code-dev1324's FsCopilot-V2 have no changes of their own.

**Updates:** the app's update check reads this repository's releases.

## 💡 How It Works

FS Copilot synchronizes the aircraft position, control surfaces, and cockpit switches between both pilots.
So when one pilot turns right, the other pilot’s aircraft turns right as well. It’s like you’re both flying the same plane together.

## 🚀 Quick start
1. Launch **FS Copilot**.
   On the first run, it copies the required files into your *Community* folder automatically.
2. Start Microsoft Flight Simulator and choose a supported aircraft.
3. Enter your partner’s session code and select Join.
4. Enjoy shared flight 🛫

## ✈️ Which aircraft are supported?
You can find the list of supported aircraft directly on https://fscopilot.com/.

The list is always up to date and updates automatically.

Additionally, join to the ⁠[Discord](https://discord.gg/HyKMRp47ka) server.
Community members often share new or custom profiles there, so your aircraft might already be supported even if it’s not included by default. 

## ⚙️ Compatibility

FS Copilot is built for **Microsoft Flight Simulator 2024** but aims to remain compatible with MSFS 2020.

However, development is primarily focused on MSFS 2024, so some features may not behave exactly as intended in the 2020 version.

## 🧩 For Developers

FS Copilot is built with **.NET 9 (C#)** and designed for modular extensibility.
It includes a flexible networking layer using **peer-to-peer UDP connection by hole punching**,
allowing direct low-latency connections without external servers.

Each aircraft is defined via YAML templates that describe variable mappings,
event bindings, and transformation logic.
These templates support embedded **JavaScript expressions** for dynamic data handling
— enabling complex synchronization behavior right inside the config.

For example:

```yaml
- get: L:AS1000_PFD_SelectedNavIndex # NAV 1/2
  set: (>B:AS1000_PFD_1_NAV_Khz_Button_Push)
- get: L:PFD_CDI_Source # CDI
  set: "value < 3 ? `${value} (>K:AP_NAV_SELECT_SET)` : '(>K:TOGGLE_GPS_DRIVES_NAV1)'"
  skp: H:AS1000_PFD_SOFTKEYS_6
- get: A:KOHLSMAN SETTING MB:0, Millibars # BARO
  set: "`${value * 16} 0 (>K:KOHLSMAN_SET)`"
- get: Z:AUDIO_Knob_Selector_1 # MIC
  set: |
    switch (value) {
        case   0: return '(>H:KMA28_TRANSMISSION_KNOB_COM3)'
        case  20: return '(>H:KMA28_TRANSMISSION_KNOB_COM2)'
        case  40: return '(>H:KMA28_TRANSMISSION_KNOB_COM1)'
        case  60: return '(>H:KMA28_TRANSMISSION_KNOB_COM1_2)'
        case  80: return '(>H:KMA28_TRANSMISSION_KNOB_COM2_1)'
        case 100: return '(>H:KMA28_TRANSMISSION_KNOB_TEL)'
        default: return ''
    }
```

You can find a detailed documentation here:
👉 [FS Copilot — Definitions Guide](https://github.com/yury-sch/FsCopilot/wiki)

## 🤝 Acknowledgements

This project was inspired by the ideas explored in [YourControls](https://github.com/Sequal32/yourcontrols).

Several good concepts and approaches originated there and helped shape the early direction of this work.

*The core of this project has been written entirely from scratch*, features a distinct architecture, and is implemented in a different programming language.

## 🧑‍💻 Author

**FS Copilot** is created by aviation and MSFS enthusiast **Yury Sсherbakov.**
Born from the idea that flying together should be as easy as sitting next to your co-pilot.

> “Fly together. Control together.” ✈️
