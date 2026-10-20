## 🌐 FS Copilot Discovery Server

This directory contains the **Discovery (rendezvous) service** used by FS Copilot to establish **peer-to-peer UDP hole-punch connections** between clients.

### ⚙️ Overview

The server runs two services:

- **Matchmaking (STUN), UDP 3480.** It registers clients by their peer ID (the client code), keeps their public and local endpoints, and introduces two peers to each other so they can hole-punch a direct link. Once a direct link is up, the server carries nothing.
- **Relay, UDP 3600.** When a direct link can't be made, both clients connect here and the relay forwards every packet between linked peers. It serves relay protocol v1 (upstream's, which `p2p.fscopilot.com` speaks) and v2 (xiprox's, with a frame-type byte so unreliable traffic and ATC packets aren't mistaken for control messages) side by side, and never links a v1 client to a v2 one. Clients of this combined build speak v2 and fall back to v1 by themselves.

Clients in IP direct mode use neither: they connect to each other's address.

On Debian 13, `for_debian_13_run.sh` (xray447) installs .NET and starts the server. Open UDP 3480 and 3600 in the host's firewall.

---

### 🧰 Developer Setup

To build and run the Discovery service:

1. Make sure you have **.NET 9 SDK** installed.
2. Open this directory in your IDE (Visual Studio / Rider / VS Code).
3. Run the project in **Development** mode:
   ```bash
   dotnet run --project FsCopilot.Discovery