> ⚠️ **Disclaimer: this is an unofficial build that merges several forks.**
> It is not FS Copilot as its author, Yury Scherbakov, publishes it. It combines
> [upstream FS Copilot](https://github.com/yury-sch/FsCopilot) 1.2.1 with work from eight other
> developers' forks: xray447, SamiSaleh98, Johnsmz13, xiprox, LocatedInSpace, degroat-c, ragerin and
> GrainLin-0507. It also adds fixes and screen-reader access of its own. None of those authors made,
> reviewed or support this combination.
>
> - **Who did what, how it was merged and why:** see [CREDITS.md](CREDITS.md).
> - **Problems with this build:** report them [here](../../issues), not to upstream or to the fork
>   authors.
> - **Downloads:** [Releases](../../releases).
> - **Licence:** MIT, as upstream is, with every author's copyright and commits kept.

[![Discord](https://img.shields.io/discord/1454265644416765974?label=Discord&logo=discord&color=5865F2)](https://discord.gg/HyKMRp47ka)
[![Support on Boosty](https://img.shields.io/badge/Support-Boosty-orange)](https://boosty.to/fscopilot)

☝️ FS Copilot's own Discord and Boosty, run by its author for the original app. This combined build isn't supported there.

> 🌐 Visit the official website — [FS Copilot — Shared Cockpit for MSFS 2024](https://fscopilot.com)

# 🛫 FS Copilot

**FS Copilot** is a companion app for **Microsoft Flight Simulator 2024** that lets multiple pilots control the same aircraft together — in real time.
Fly as a real crew. 👨‍✈️👩‍✈️

![FS Copilot](https://raw.githubusercontent.com/yury-sch/FsCopilot/refs/heads/main/preview.png)

## 🔀 This fork: everything in one build

FS Copilot's public forks each added something, and none of it reached upstream. This fork merges all of them on top of upstream 1.2.1. It keeps every fork author's commits, fixes what didn't work together, and ships every aircraft profile it could find. Every fork branch with work of its own is archived here under `forks/<owner>/<branch>`, in case the original goes away. [CREDITS.md](CREDITS.md) says who made each part, how it was merged, what was left out and why.

### Download and install

1. Download `FsCopilot-v<version>-win-x64.zip` from this repository's [Releases](../../releases). Unzip it anywhere, for example next to your other add-ons. Releases up to 1.4.3 were named `FsCopilot_v<version>.zip`.
2. Run `FsCopilot.exe`.
3. The first window is **Connection settings**. Everything is filled in for you, including your peer ID: 8 random letters made on first run and kept from then on. Press **Enter** or choose **Save settings and connect**. See [Settings](#settings).
4. On first run the **Setup** window offers **Install**. It copies the `fscopilot-bridge` package into your MSFS Community folder. If it finds YourControls there, it moves YourControls to the Recycle Bin, because the two conflict. Then choose **Continue**.
5. Upgrading: close FS Copilot and run the new build. When Setup asks, choose **Install**, which replaces the bridge in your Community folder. This matters from 1.5.0 on: the bridge gained a weather module.

### Who you can fly with

Everyone in the session must run **the same release of this build**. Before two copies link, FS Copilot compares their lists of network messages, and those lists include the app's version number. A copy whose list differs is refused. So this build can't pair with stock FS Copilot, with any single fork, or with an older release of itself. Send your crew the same release.

### Connecting: client code, direct links and the relay

- Your **client code** is your peer ID (8 letters, shown at the top of the main window). To fly together, one pilot gives their code to the other. The other pilot types it into the **Code to join** box and chooses **Join**. With three or more pilots, everyone joins the same person; once linked, the app introduces the rest to each other.
- The code is only an address. It is not itself a direct connection. When you choose Join, the app first tries a **direct** link for about 4 seconds:
  - both apps ask the server to introduce them;
  - then they punch through each other's router (UDP hole punching), with UPnP or NAT-PMP port mapping where the router allows it.

  A direct link is the fastest, and once it is up nothing passes through a server.
- If the direct attempt fails, the app falls back to the **relay**. Both apps keep a connection to the server, and the server forwards every packet between them. A direct link fails when a router or ISP won't allow hole punching. That happens with:
  - carrier-grade NAT, common on mobile, satellite and some fibre ISPs;
  - symmetric NAT;
  - strict firewalls;
  - a server that does not introduce the two peers.
- The relay carries everything the direct link carries: cockpit sync, pointer sync, traffic and ATC audio. It only adds latency, roughly the round trip to the server (about 100 ms from Europe to `p2p.fscopilot.com`). The connection list on the main window shows `direct` or `relay` for each peer, with its ping and packet loss.
- **Two relay protocols, both handled automatically.** This build speaks xiprox's relay protocol v2, which keeps traffic and ATC audio apart from control messages so they survive the relay. Upstream's server `p2p.fscopilot.com` speaks v1. The app recognises a v1 relay from its first reply and switches to v1 for the session, so the default server works without changing anything. A v2 relay can be entered in Connection settings, for example xiprox's `fscrelay.ihsan.dev` or one you host.
- **Which server?** Keep the default, `p2p.fscopilot.com`. On 2026-10-02 both servers were tested with this build's own network code, sending 100 packets of every kind, at small and at full size, through each relay:
  - **`p2p.fscopilot.com` (default):** every kind and size arrived, using the automatic v1 fallback.
  - **`fscrelay.ihsan.dev` (xiprox's server, also up):** everything arrived except the largest unreliable packets, which were 1 byte over the limit. That was a budget bug in the traffic and ATC code, now fixed. It never affected real traffic batches (1008 bytes at most) or voice frames (about 60 bytes).

  Upstream's is the official, long-running server and needs nothing set. A direct link between two copies on *one* computer can't be tested, because both servers behaved the same there. Direct links are best judged on a real two-house session; the relay takes over whenever one fails.
- **Connection type** in Connection settings:
  - **Automatic** (direct, then relay) is right for nearly everyone.
  - **Relay only** if joins keep taking several seconds or failing on your network.
  - **Direct only** to keep traffic off the server.
  - **IP direct, no server**: see the next section.

### IP direct: connect by address, no server (GrainLin-0507)

This mode skips the server entirely: no introductions and no relay. It is for crews who share a network, for example:
- the same LAN;
- a virtual LAN such as ZeroTier, Tailscale, Radmin VPN, n2n or WireGuard;
- a router port forward.

It also helps pilots behind symmetric NAT, whose direct links always fail and whose relay may be far away.

1. Every pilot sets **Connection type** to **IP direct, no server** in Connection settings and saves. Leave **Direct port** at 38510 unless something else uses it. It is UDP, and every pilot must use the same port.
2. The first time, Windows asks once for administrator rights. FS Copilot uses them to add one inbound firewall rule, **FS Copilot Direct**: UDP, for this `FsCopilot.exe` only. The app itself keeps running without administrator rights. If you decline, or the rule can't be added, the main window says so and shows a **Fix firewall** button that asks again. If you move the folder, the rule no longer matches, and the app asks again.
3. The main window shows **Listening on UDP port …, your IP: …**. Give one of those addresses to your partner. On a virtual LAN, give them the virtual LAN's address.
4. Your partner types it into **Partner IP to join** (the box that is **Code to join** in the other modes) and chooses **Join**. A port is only needed if yours isn't the default: `10.0.0.2:38600`, or `[fd00::2]:38600` for IPv6. Host names work too.
5. With three or more pilots, each one joins the same pilot once. The apps exchange addresses and link everyone to everyone.

Everything works over IP direct, as over any other link: cockpit sync, pointer sync, traffic, ATC audio, time and weather. `--direct` and `--direct-port <port>` set this mode for one run without changing your settings.

To remove the firewall rule later, open Command Prompt as administrator in the FS Copilot folder and run `FsCopilot.exe --uninstall-firewall`. You can also delete the rule named **FS Copilot Direct** in Windows Defender Firewall's advanced settings.

### Features added by this fork

**Connection settings window** (xray447, extended here). In it you choose:
- the server;
- your display name;
- a fixed peer ID that stays the same between sessions, so your crew can keep your code;
- the connection type, and the IP direct port;
- whether to share ground vehicles.

**Time, pause and sim rate sync** (ragerin):
- **Time:** the pilot whose session it is (the one the others joined) sets the zulu date and time. A joiner's sim is moved to it at load-in (to within 5 seconds) and corrected whenever it drifts more than 30 seconds. If MSFS keeps refusing the change, the sync log says to check that the flight doesn't use live or real time.
- **Pause:** pausing, including the Esc menu, pauses every pilot, and unpausing resumes everyone.
- **Sim rate:** held at 1x while you are connected.
- **Lost link:** if the link to the other pilot is lost, your sim pauses so you can reconnect. If the other pilot leaves on purpose, it doesn't pause.

**Weather sync** (ragerin): the host's weather preset is selected for everyone, and live weather is matched to live weather. Only presets can be synced, because the full weather data isn't available to instruments. It needs the bridge from 1.5.0 or later, which carries the new `weather.js`. If the bridge doesn't answer, the sync log says so and weather is left alone.

**Aircraft state sync** (ragerin): this works for every aircraft, whatever its profile:
- fuel in each tank, and the flaps, follow the pilot in control;
- payload stations, chocks and covers sync both ways ("Other pilot placed the pitot covers").

Anything the aircraft's own profile already syncs is skipped, so nothing is driven twice.

**Full sync at load-in** (ragerin): when a pilot joins or an aircraft loads, the joiner gets the whole cockpit at once instead of waiting for each switch to change. That includes every variable in the profile, plus time, weather and aircraft state.

**Sync log** (ragerin, made accessible here): a list on the main window of what the sync did and why, newest first. Examples are "Time in sync with host" and "Weather now matches the host". Each entry is one list item, read as "Warning: message, 16:43". Warnings and errors are also spoken as they happen; routine entries wait in the list.

**Profile notes** (SamiSaleh98, upstream PR #36): a profile can carry instructions for the crew. They appear in a "Profile Notes" card when that aircraft loads, and "Profile notes available." is spoken. Notes from included modules follow the profile's own. Without this, a profile with a `notes:` section would not load at all, in stock FS Copilot too.
  ```yaml
  notes:
    - "Make sure the MCDU configs are the same for both pilots"
    - "Request GPU from the MCDU rather than from the EFB"
  ```

**Packet loss per peer** (xray447): each peer in the onboard list shows its packet loss beside its ping. **Reset Stats** clears the counters.

**Pointer sync for panels the normal sync can't reach** (xiprox, upstream PR #35). Some glass displays draw with SVG or rebuild their element tree, so the usual "which button was pressed" sync can't find the button on the other side. For instruments listed under `pointer:` in the aircraft profile, the app sends *where* the pointer went instead: presses, holds and drags, as fractions of the display. The other side replays each gesture at the same spot, one at a time and at the pilot's pace.
- Turn it on in a profile:
  ```yaml
  pointer:
    - DisplayUnits                      # every panel of this instrument
    - DisplayUnits|config=Default       # or one panel of several, by its URL query
  ```
  The Synaptic A220 profile uses it for its display units.
- The display shows an overlay for each state of the link:
  - **connecting:** while a session is starting, the display is locked under this overlay;
  - **degraded:** the link dropped mid-session, and **Take Control** gets you going again;
  - **replaying:** a peer's gesture is being replayed;
  - **red warning:** the panel lost the app.

  Gestures that weren't acknowledged are resent when a peer reconnects, so the two displays can't drift apart.
- The panels talk to the app over a local WebSocket on the first free port between 9020 and 9024. If all five are taken, the main window says "Panel channel unavailable". Connections from web pages are refused.

**Shared pointer and clicks for iniBuilds A380 displays** (xray447). The A380's MFDs, NDs and SD are WASM displays, which can't be synced element by element. `Definitions/screens.yaml` lists the displays whose pointer and presses are shared. The other pilot's pointer appears as a yellow arrow with their name. Displays are matched by gauge name and order (`DU#2`), not by panel number, because the simulator renumbers panels between sessions. By default the press is shared and the simulator generates the click itself; `clicks: true` shares clicks as well.

**Share AI traffic** (xiprox): one pilot (the host) shares the AI traffic in their sim, and everyone else sees the same aircraft in the same places.
- In the **ATC & Traffic** card, turn on **Share AI traffic**. You must be in a session with the simulator running ("Waiting for the simulator" means it isn't connected yet).
- Only one pilot hosts traffic at a time. The others see "shared by <name>", and their toggle is replaced. If two turn it on at once, the app picks one and tells the other.
- Receivers should switch off their own AI traffic; the card warns "Detected existing AI traffic in your sim" when it finds some. Everyone should use the same traffic pack and scenery.
- Ground vehicles aren't shared by default. Turn on **Share ground vehicles** in Connection settings, or start the app with `--traffic-ground`.

**Share ATC audio** (xiprox): one pilot's ATC app is heard by the whole crew.
- Supported apps are found automatically: BeyondATC, SayIntentions, Pilot2ATC, PF3, FSHud and VoxATC. **Other…** lists every program currently making sound, so you can pick any app.
- The host turns on **Share ATC audio** and picks the app from **ATC audio source app**. Only that program's sound is captured (Windows 10 version 2004 or later), compressed with Opus and streamed. The status reads "Capturing", or "Capturing — muted in the volume mixer" if Windows has that app muted.
- Receivers get a **Mute ATC** button and an **ATC volume** slider. Your choices are saved in `settings.json` beside the exe.

**Developer tools** (xiprox, xray447): start with `--dev` for the Develop window. It can:
- record and replay a session's physics, controls and variables (a variables-only trace leaves the aircraft alone);
- browse the profile;
- list every glass display with its group and number.

"Peer move" and "Peer click" pretend to be the other pilot on one machine.

**Fixes carried here:**
- Every instrument loads on panels that hold several (VCockpit.js; xiprox).
- A profile's `ignore:` list now also protects you from input arriving from the peer.
- A failed join gives back your role.
- A peer that is still handshaking is not shown as connected, and isn't offered traffic or ATC (xiprox).
- A pilot who left is told apart from one whose link was lost, in the main window and in the sync log.
- Connection requests carry one digest of the network messages. They used to carry a hash of each message, a list that no longer fit in one packet once every fork's messages were in, so direct joins failed.
- Setup reports a bridge that couldn't be copied (for example, because MSFS had it open), instead of saying "Setup complete".

### Settings

Everything is set in windows; the command line is only for one-off overrides. Connection settings opens at every start, and the **Settings** button beside your code in the main window opens it any time. The server, ID and connection type are fixed when the network starts, so saving there restarts FS Copilot, which leaves any session. Escape closes it without saving.

| Where | Setting | Saved in |
| --- | --- | --- |
| Connection settings (at every start, or the Settings button) | Server address; username; peer ID (generated for you; **New ID** makes another, or type exactly 8 letters or digits); connection type (Automatic / Direct only / Relay only / IP direct, no server); direct port (IP direct only, 1024–65535, default 38510); share ground vehicles when hosting traffic | `%LOCALAPPDATA%\FsCopilot\config.json` |
| Main window, ATC & Traffic, while you share ATC | ATC audio source app | `settings.json` beside `FsCopilot.exe` |
| Main window, ATC & Traffic, while you receive ATC | Mute and volume of the received ATC audio | `settings.json` beside `FsCopilot.exe` |

Connection settings checks the fields when you save and says what is wrong, instead of greying the button out. For example: "Peer ID must be exactly 8 letters or digits". If `config.json` can't be written, it says so and stays open. Changes take effect the next time you press Save, which happens at every start.

### Accessibility

Built for screen readers, and tested with Windows UI Automation (the interface NVDA, JAWS and Narrator use): every window on 2026-10-02, and Connection settings and the main window in every connection type again on 2026-10-20, with no unnamed control.

- **Every control has a short name, and a short hint where one helps.** Every button, box, switch, list, slider and picker is named. Hints are a few words; in NVDA, NVDA+Tab reads them. Decorative icons and images are hidden.
- **Headings.** Each window title is heading level 1. The cards (Connection, ATC & Traffic, Onboard, Sync log) are level 2. Press H in browse mode to jump between them.
- **Spoken updates.** These are spoken as they happen:
  - joining, joined, and why a join failed;
  - who joined or left, and over which link;
  - who has control, by name, whenever it changes hands: "You have control" or "Alpha has control";
  - errors, such as the sim not running or the bridge missing;
  - sync warnings and errors, such as a lost link, a refused time change, or weather sync being unavailable;
  - a newer aircraft profile being available;
  - ATC and traffic sharing starting or stopping;
  - Setup progress;
  - the firewall check in IP direct mode, when you choose Fix firewall;
  - copying your code.

  They use UI Automation notification events, which NVDA, JAWS and Narrator read. Avalonia, the UI toolkit, doesn't raise live-region events on Windows, so they would otherwise stay silent.
- **Text fields speak as you edit.** Avalonia gives text boxes no UI Automation Text pattern (still true in 12.1), so a screen reader can't follow the cursor itself. FS Copilot speaks it instead:

  | Key | Spoken |
  | --- | --- |
  | Left, Right, Home, End | the character at the cursor |
  | Ctrl+Left, Ctrl+Right | the word |
  | Backspace | the deleted character |
  | Delete | the new character at the cursor |
  | Up, Down | the whole field |
  | Shift selection | what it selects |
- **Keyboard.**
  - Focus starts in a useful place:
    - the first field in Connection settings;
    - the action button in Setup;
    - the join box in the main window;
    - Open GitHub in the update message.
  - Enter submits Connection settings and joins from the join box. Escape closes the update message.
  - Tab follows reading order.
- **Onboard list.** Each pilot is one list item, read as "name, code, direct or relay, quality", with ping and loss as its description. Arrow keys move between pilots. The rows update in place, so the reader doesn't lose its place as ping changes.
- **Sync log.** One list item per entry, newest first, read as "severity: message, time". Info entries say no severity.
- **Readable codes.** Codes are spelled out ("Q F Q Y E C T D"); an IP address is read as it is. **Copy code** puts yours on the clipboard to paste into a message.
- **Readable values.** The ATC volume slider reads as a percentage. The mute button is named for what it will do ("Mute ATC" or "Unmute ATC").

### Command-line options

| Option | What it does |
| --- | --- |
| `--server <host>` | Use this server (matchmaking and relay) for this run instead of the one in Connection settings. |
| `--relay <host>` | The same, xiprox's spelling. |
| `--relay` (no host after it) | Relay only for this run (Johnsmz13); the same as Connection type "Relay only". |
| `--no-direct` | Relay only (xiprox's spelling, keeps both transports loaded). |
| `--p2p` | Direct links only for this run; the same as Connection type "Direct only". |
| `--direct` | IP direct for this run (GrainLin-0507); the same as Connection type "IP direct, no server". |
| `--direct-port <port>` | The UDP port for IP direct, for this run. |
| `--peer-id <id>` | Use this peer ID for this run instead of the saved one. |
| `--traffic-ground` | Share ground vehicles for this run; the same as the Connection settings check box. |
| `--traffic-offset <nm>,<deg>` / `--traffic-shadow <m>` | Test aids: place received traffic away from the originals, so two copies can run against one sim. |
| `--dev` | Open the Develop window instead of a session. |
| `--debug` | Verbose log (`log` beside the exe). |
| `--install-firewall` / `--uninstall-firewall` | Add or remove the IP direct firewall rule, then exit. Must run as administrator. The app starts `--install-firewall` itself when it needs the rule. |

The environment variable `FSCOPILOT_SKIP_FIREWALL=1` skips the firewall check in IP direct mode. It is meant for automated tests, so they don't raise an administrator prompt.

### Aircraft profiles

`Definitions/` ships 96 aircraft profiles, so nothing has to be downloaded first:
- **85 from the FS Copilot profile server** (2026-10-20). Five were new that day: Asobo Bonanza G36, Bluemesh Latécoère 631, Douglas DC-3, JF PA-28 Arrow and the WBSim C152 Fairing. Six were updated, among them DKgolfnut's hand-built PMDG DC-6.
- **Seven FSS Boeing 727 profiles** the server no longer serves.
- **Synaptic A220**, with pointer sync.
- **iniBuilds A350, iniBuilds A400M and A2A PA-24 Comanche**, from xiprox's profile collection.

Each profile's header names or links its author, and those headers are unchanged. The app still checks the profile server and offers **Download** when your aircraft's profile has a newer version.

Five served profiles include modules under misspelled names (TBM 850, A330, Beluga, Cessna 414AW and Citation CJ3+), so parts of them were skipped. Small alias files in `Definitions/modules/` point those names at the real modules. The profiles themselves are unchanged, so profile updates keep working. Three included modules (`transponder`, `radios`, `AS_G1000_NXi_ALT_MOD`) don't exist anywhere yet.

`Definitions/experimental/` holds alternatives that aren't loaded:
- LocatedInSpace's PA-28 Dakota autopilot rework, which its author marked as probably not working;
- degroat-c's YourControls conversion of the PMDG 737-800;
- the earlier, unverified PMDG DC-6 conversion.

To try one, copy it over the file of the same name in `Definitions/`.

### Running your own server

`FsCopilot.Discovery` is the matchmaking server (UDP 3480) and the relay (UDP 3600). This repository's copy serves relay protocol v1 and v2 side by side. On Debian 13, `FsCopilot.Discovery/for_debian_13_run.sh` installs .NET and starts it (xray447). Point the app at it with Connection settings or `--server <host>`. IP direct mode needs no server.

### Where everything came from

In short; [CREDITS.md](CREDITS.md) has the who, what, how and why for each.

| Source | Brought |
| --- | --- |
| [Yury Scherbakov](https://github.com/yury-sch/FsCopilot) (upstream) 1.2.1 | FS Copilot itself, the bridge and its WASM module, the profile server and the default server |
| [xray447](https://github.com/xray447/FsCopilot) `new_master` | Connection settings window, packet loss, iniBuilds A380 display sync, Debian server script, WASM version fix |
| [SamiSaleh98](https://github.com/SamiSaleh98/FsCopilot) `feature/profile-notes` (upstream PR #36) | Profile notes |
| [Johnsmz13](https://github.com/Johnsmz13/FsCopilot) `main` | `--server`, `--p2p`, `--relay` |
| [xiprox](https://github.com/xiprox/FsCopilot) `pointer-forwarding` (upstream PR #35), `ahead-*`, `dev-var-replay`, `main` | Pointer sync, traffic and ATC sharing, relay protocol v2, var replay, GTNXi / RDR1150XL / KFC 150 modules, fixes, research notes in `record/` |
| [xiprox/fsc-editor](https://github.com/xiprox/fsc-editor) corpus | A350, A400M, PA-24 profiles, newer A220 profile |
| [LocatedInSpace](https://github.com/LocatedInSpace/FsCopilot) `main` | Experimental PA-28 Dakota rework |
| [degroat-c/pmdg737-fscopilot](https://github.com/degroat-c/pmdg737-fscopilot) | Experimental PMDG 737-800 conversion |
| [ragerin](https://github.com/ragerin/FsCopilot) `claude/environment-sync` | Time, pause and sim rate sync, weather preset sync, aircraft state sync, full sync at load-in, sync log, the GitHub Actions build |
| [GrainLin-0507](https://github.com/GrainLin-0507/FsCopilot-IP-direct-connect) `main` | IP direct mode with address exchange, the firewall helper, the major.minor bridge version check |
| Profile authors | Every profile in `Definitions/`; each file's header credits its author |
| This fork | The merges, the accessibility work, the v1 relay fallback and relay packet budget fix, connection type and ground-vehicle settings, IP direct in Connection settings, the schema digest, module aliases, bundled profiles, tests, update check pointed at this repository |

[3617luke](https://github.com/3617luke/FsCopilot) holds two of Yury's commits that upstream already has. The forks by harrycollin, art-drobanov, coisasgamer4, kpolkowski, grzegorzkibitz, demendet and lLeolau, and code-dev1324's FsCopilot-V2, have no changes of their own.

**Updates:** the app's update check reads this repository's releases.

### Building and testing

```bash
dotnet test tests/FsCopilot.Tests/FsCopilot.Tests.csproj -c Release
dotnet publish FsCopilot/FsCopilot.csproj -c Release -r win-x64 -o publish
```

`tests/` checks what can be checked without a simulator. That covers:
- address parsing for IP direct;
- the schema digest;
- the packet decoders' limits;
- firewall rule parsing;
- the connection types;
- the spoken text of the text echo, the onboard list and the sync log;
- that every bundled profile loads.

The GitHub Actions workflow (from ragerin) runs the tests and builds the app. It also takes the bridge's WASM module from upstream's 1.2.1 release, adds this repository's bridge scripts, and checks the module's version. A `v*` tag publishes a release. On a fork, enable Actions in the repository settings first.


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

This combined build is put together by [francescotissera1211](https://github.com/francescotissera1211) from the work of the people in [CREDITS.md](CREDITS.md).

> “Fly together. Control together.” ✈️
