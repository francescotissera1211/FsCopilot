# Credits: who made what in this build

> ⚠️ **This is an unofficial build that merges several forks.** It is not FS Copilot as its author
> publishes it, and none of the people below made, reviewed or support *this combination*. Each of
> them wrote their part for their own fork, and this repository put the parts together. Report
> problems with this build here, in [this repository's issues](../../issues), not to upstream or
> to the fork authors.
>
> Everything is MIT licensed, as upstream is. Every author's commits are kept in this repository's
> history under their own name, and each fork's branches are archived here under
> `forks/<owner>/<branch>` in case the original goes away.

## Why this build exists

FS Copilot's public forks each fixed or added something: a settings window, profile notes, AI
traffic and ATC sharing, connecting by IP address, time and weather sync. None of that reached
upstream, and two copies of FS Copilot only link when they have the same network messages, so a
crew had to pick one fork's features and lose the rest. This build merges all of them on one
code base, fixes what broke when they met, and adds screen-reader access throughout. It is
maintained by [francescotissera1211](https://github.com/francescotissera1211), who flies blind with
NVDA.

## How it was merged

- Each fork was merged with `git merge`, never squashed or copied by hand, so `git log` and
  `git blame` show the real author of every line.
- When two forks changed the same thing, both were kept wherever they could work together (for
  example, three different ways to choose the server all work as command-line options). Where they
  conflicted, the merge commit says what was chosen and why.
- What was left out is listed under each source below, with the reason. Nothing that changes what
  the app does was dropped.
- After every merge the build was tested in two copies on one machine, with Windows UI Automation
  (what NVDA, JAWS and Narrator use), and with the test suite in `tests/`.
- Much of this repository's own work, ragerin's environment sync and xiprox's later commits were
  written with the help of Claude, an AI assistant. Those commits say so in their author or
  `Co-Authored-By` line.

## The sources

### Yury Scherbakov: FS Copilot itself

- **Who:** [yury-sch/FsCopilot](https://github.com/yury-sch/FsCopilot), the original author.
- **What:** the whole app this build is made from, at version 1.2.1. That covers the sync engine,
  the YAML profile format, direct links and the relay, the `fscopilot-bridge` Community package
  and its WASM module, the profile server and the default server `p2p.fscopilot.com`.
- **How:** this build's history starts from his. The bridge's WASM module is shipped exactly as
  his 1.2.1 release ships it, because it needs the MSFS SDK to build and no fork changed it.
- **Why:** none of the rest would exist without it. His [Discord](https://discord.gg/HyKMRp47ka),
  [website](https://fscopilot.com) and [Boosty](https://boosty.to/fscopilot) are the place to
  support the project itself.

### xray447

- **Who:** [xray447/FsCopilot](https://github.com/xray447/FsCopilot), branch `new_master`.
- **What:**
  - the Connection settings window (server, name, a fixed peer ID, forced relay);
  - packet loss per peer, with Reset Stats;
  - the shared pointer and presses for the iniBuilds A380's WASM displays (`Definitions/screens.yaml`);
  - the Debian 13 script for running the server (`FsCopilot.Discovery/for_debian_13_run.sh`);
  - a fix for the WASM version check.
- **How:** merged first, so later merges were built on top of it. This build extended the window
  with the connection type, the ground-vehicle switch, IP direct and the direct port.
- **Why:** before this, the server was fixed in the code and a peer ID changed every run, so your
  crew had to learn a new code every flight.

### SamiSaleh98

- **Who:** [SamiSaleh98/FsCopilot](https://github.com/SamiSaleh98/FsCopilot), branch
  `feature/profile-notes`, also upstream's PR #36.
- **What:** profile notes, which are crew instructions inside a profile, shown in a Profile Notes
  card when the aircraft loads.
- **How:** merged as written. This build also reads notes from included modules, and speaks
  "Profile notes available".
- **Why:** some aircraft need both pilots to do something by hand (the same MCDU setup, for
  example), and the profile is the one place everyone reads. Without it, a profile containing
  `notes:` fails to load even in stock FS Copilot.

### Johnsmz13

- **Who:** [Johnsmz13/FsCopilot](https://github.com/Johnsmz13/FsCopilot), branch `main`.
- **What:** the `--server <host>`, `--p2p` and bare `--relay` command-line options.
- **How:** kept alongside xray447's settings window and xiprox's `--relay <host>`. A switch on the
  command line wins for that run.
- **Why:** to choose the server and the link type without rebuilding the app.

### xiprox (İhsan Işık)

- **Who:** [xiprox/FsCopilot](https://github.com/xiprox/FsCopilot). That covers branches
  `pointer-forwarding` (upstream PR #35), the `ahead-*` topic branches, `dev-var-replay` and, since
  then, `main`. Also the profile collection in [xiprox/fsc-editor](https://github.com/xiprox/fsc-editor)
  and the v2 relay `fscrelay.ihsan.dev`.
- **What:**
  - **pointer sync** for glass displays the normal sync can't reach;
  - **AI traffic sharing** and **ATC audio sharing**;
  - **relay protocol v2**;
  - the Develop window's **variable replay**;
  - the GTNXi, RDR1150XL and KFC 150 modules;
  - the multi-instrument VCockpit fix;
  - the research notes in `record/`, including the 2026-09 study of why the A380's WASM displays
    accept forwarded presses and the A350's don't;
  - the A350, A400M, PA-24 and newer A220 profiles;
  - from his newest `main`: traffic and ATC sharing count only peers that have finished connecting.
- **How:** merged branch by branch. His newest `main` commits were cherry-picked onto this build.
  Relay protocol v2 is used when the relay speaks it. This build added a fallback to protocol v1,
  so upstream's default server still relays everything.
- **Left out:**
  - his version-number bump (this build has its own version line);
  - his removal of the A220 profile from the repository (this build ships it);
  - the test-bench hooks for his external two-instance exerciser (`BenchControl` with `--bench`,
    and the `--dev` pointer echo). His code says the bench is kept to one file so that his upstream
    PR can leave it out, and the exerciser it serves isn't in the repository.
- **Why:** most of what makes a shared flight feel shared, such as the same traffic and the same
  ATC, and glass cockpits like the A220's that upstream's sync could not reach.

### LocatedInSpace

- **Who:** [LocatedInSpace/FsCopilot](https://github.com/LocatedInSpace/FsCopilot), branch `main`.
- **What:** a rework of the PA-28-236 Dakota autopilot sync.
- **How:** shipped unloaded in `Definitions/experimental/`, because its author wrote that it
  probably doesn't work. The loaded Dakota profile is the served one, which matches his first
  version.
- **Why:** kept so the work isn't lost and anyone can try it.

### degroat-c

- **Who:** [degroat-c/pmdg737-fscopilot](https://github.com/degroat-c/pmdg737-fscopilot).
- **What:** a conversion of the YourControls PMDG 737NG definitions to an FS Copilot profile.
- **How:** shipped unloaded in `Definitions/experimental/`. The loaded 737-800 profile is the
  newer hand-built V2.1 by Hawks1326 and Av8rChris.
- **Why:** a second, independent take on the 737 to compare with.

### ragerin (Mads H. Jakobsen)

- **Who:** [ragerin/FsCopilot](https://github.com/ragerin/FsCopilot), branch
  `claude/environment-sync` (merged into his `main` by his PRs #1–#3). He wrote it with Claude.
- **What:**
  - **environment sync:** zulu date and time from the session's host, pause (the Esc menu too) in
    both directions, and the sim rate held at 1x;
  - **weather preset sync** through a new bridge script, `weather.js`;
  - **aircraft state sync** for every aircraft: fuel, flaps, payload, chocks and covers;
  - a **full sync on load-in**, so a joiner gets the whole cockpit at once instead of waiting;
  - the **sync log** in the main window;
  - the **GitHub Actions build** that builds, checks the bridge's WASM version and publishes
    releases.
- **How:** merged as one branch. This build made the sync log a screen-reader list (one item per
  entry, its severity said in words, warnings and errors spoken). It also stopped a deliberate
  leave from being treated as a lost connection (which used to pause the sim), and added the test
  step to the build.
- **Left out:** pointing the update check at his repository. This build checks its own releases.
- **Why:** without it, two pilots in one aircraft can be at different times of day, in different
  weather, or one paused while the other flies on.

### GrainLin-0507

- **Who:** [GrainLin-0507/FsCopilot-IP-direct-connect](https://github.com/GrainLin-0507/FsCopilot-IP-direct-connect),
  branch `main`. His README has support links if this part helped you.
- **What:**
  - **IP direct mode:** connect straight to your partner's IP address, with no server;
  - an address exchange that links a crew of three or more to each other from one join;
  - the **one-time firewall rule** (one Windows administrator prompt);
  - a bridge version check that compares major.minor only.
- **How:**
  - His transport (`DirectNetwork.cs`) and firewall helper (`Firewall.cs`) are merged under his
    name. His separate `fscopilot.config.json` was replaced by a Connection type and a Direct port
    in Connection settings, so there is one place to set everything.
  - The firewall helper now removes only *block* rules for this program, never your own allow
    rules.
  - The transport got the leave/lost distinction and the packet channels the other transports use.
- **Left out:** his README and its donation images (they describe his fork, not this one) and the
  separate configuration file (replaced as described).
- **Why:** many players are behind symmetric NAT (NAT4), where hole punching never works and every
  packet goes through an overseas relay. On a shared virtual LAN (ZeroTier, Tailscale, Radmin VPN,
  n2n, WireGuard) or a real one, a direct link to the partner's address is faster and needs no
  server at all.

### Profile authors

The aircraft profiles in `Definitions/` come from the FS Copilot profile server and the forks
above. Each file's header names or links its author, mostly a post in the FS Copilot Discord, and
those headers are kept unchanged. Examples are DKgolfnut (the PMDG DC-6 rewrite), and Hawks1326 and
Av8rChris (the PMDG 737-800 V2.1). The small alias files in `Definitions/modules/` are this
build's: they point misspelled include names at the real modules, without changing the profiles.

### This repository

- **Who:** [francescotissera1211/FsCopilot](https://github.com/francescotissera1211/FsCopilot).
- **What:**
  - the merges themselves;
  - **screen-reader access** throughout: names, hints, headings, spoken updates through UI
    Automation notifications, text fields that speak as you edit, readable codes and lists;
  - the relay protocol v1 fallback, and the fix to the relay packet budget;
  - the connection type and ground-vehicle settings, and IP direct in Connection settings;
  - one schema digest in connection requests: with every fork's messages, the old per-message
    list no longer fit in a connect packet, and direct joins failed;
  - telling a pilot who left from a lost link;
  - the bundled profiles and module aliases;
  - the update check pointed at this repository;
  - the test suite, and the fixes from reviewing the whole merged code.

### Forks with nothing of their own

[3617luke](https://github.com/3617luke/FsCopilot) holds two of Yury's commits that upstream
already has. The forks by harrycollin, art-drobanov, coisasgamer4, kpolkowski, grzegorzkibitz,
demendet and lLeolau, and code-dev1324's FsCopilot-V2, had no changes beyond upstream when
checked (2026-10-20).

### Inspiration

FS Copilot was inspired by [YourControls](https://github.com/Sequal32/yourcontrols), as its author
says in the README.
