
[![Discord](https://img.shields.io/discord/1454265644416765974?label=Discord&logo=discord&color=5865F2)](https://discord.gg/HyKMRp47ka)
[![Support on Boosty](https://img.shields.io/badge/Support-Boosty-orange)](https://boosty.to/fscopilot)

☝️ Join our Discord
☝️ Support here

> 🌐 Visit the official website — [FS Copilot — Shared Cockpit for MSFS 2024](https://fscopilot.com)

# 🛫 FS Copilot

> **This is a combined fork.** It merges [upstream FS Copilot](https://github.com/yury-sch/FsCopilot) (1.2.1, 2026-04-16) with the work found on every public fork as of 2026-10-02, and ships every aircraft profile in the build. See [What this fork adds](#-what-this-fork-adds).


**FS Copilot** is a companion app for **Microsoft Flight Simulator 2024** that lets multiple pilots control the same aircraft together — in real time.
Fly as a real crew. 👨‍✈️👩‍✈️

![FS Copilot](https://raw.githubusercontent.com/yury-sch/FsCopilot/refs/heads/main/preview.png)

## 🔀 What this fork adds

Merged from the public forks of FS Copilot (each fork's own commits are kept, with their authors):

| Source | What it brings |
| --- | --- |
| [xray447](https://github.com/xray447/FsCopilot) `new_master` | Server address, username and peer ID in a login window (saved to `%LOCALAPPDATA%\FsCopilot\config.json`); packet-loss readout per peer with Reset Stats; iniBuilds A380 MFD/ND pointer and click sync (`Definitions/screens.yaml`); one-script relay server setup for Debian |
| [Johnsmz13](https://github.com/Johnsmz13/FsCopilot) `main` | Command-line network choice: `--server <host>`, `--p2p` (direct only), bare `--relay` (relay only) |
| [xiprox](https://github.com/xiprox/FsCopilot) `pointer-forwarding` ([upstream PR #35](https://github.com/yury-sch/FsCopilot/pull/35)) | Pointer gesture sync for panels the element path cannot reach (opt in per profile with `pointer:`), sync-state overlays, acknowledged replay after a reconnect, a peer that left told apart from a peer that was lost |
| xiprox `ahead-traffic-atc` | Share AI traffic and ATC audio with the crew (ATC & Traffic card); relay protocol v2 |
| xiprox `dev-var-replay`, `ahead-modules`, `ahead-debug-symbols`, `ahead-devex` | Variable replay in dev mode; GTNXi, RDR1150XL and KFC 150 modules; debug symbols in Debug builds |
| xiprox `ahead-pointer-forwarding` | VCockpit.js fix for multi-instrument panels; the profile ignore list applied to inbound input; the Synaptic A220 profile |
| xiprox `ahead-record` | Research and build notes under `record/` |
| [LocatedInSpace](https://github.com/LocatedInSpace/FsCopilot) `main` | PA-28-236 Dakota autopilot rework, kept in `Definitions/experimental/` (its author marked it as probably not working) |

[3617luke](https://github.com/3617luke/FsCopilot) carries two of Yury's commits that upstream already has. The other forks have no commits of their own.

**Profiles:** `Definitions/` holds all 81 profiles from the FS Copilot profile server (2026-10-02), the seven FSS Boeing 727 profiles the server no longer serves, and the fork profiles above.

**Who you can fly with:** FS Copilot refuses a peer whose packet set differs from its own, and every fork above adds packets. This build only connects to other people running this build, not stock FS Copilot or a single fork.

**Relay:** the default server is upstream's `p2p.fscopilot.com`. Direct links work through it. This build speaks xiprox's relay protocol v2, which that server does not serve, so a crew that cannot link directly needs a v2 relay entered in the login window (or `--server <host>`). xiprox runs one at `fscrelay.ihsan.dev`. `FsCopilot.Discovery` in this repository builds one.

**Updates:** the update check reads this repository's releases.

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
