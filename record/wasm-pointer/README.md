# WASM pointer

Whether a press sent from the other pilot can land on a WASM-rendered display — the A350,
A380, PMDG and every other `WasmInstrument` panel — at the point it was sent to.

Pointer forwarding closed this surface as unreachable (Q05, `record/pointer-forwarding/docs/07-wasm-surface.md`):
on the A350, `WASM_MOUSE_DOWN` pressed whatever the real cursor was over, 120 px from the
point we sent. In every one of those runs the real cursor was on the same display.

xray447's commit `5defed4` on `xray447/FsCopilot` (A380, 2026-09-21) streams moves, then
sends a mousedown, and reports that it works. It was tested with the real cursor on FS
Copilot's Develop window, not in the cockpit. So the open case is: **what does the sim do
with the point we send when its own raycast hits no display?** If it presses there, WASM
displays can be synced whenever the receiving pilot's cursor is elsewhere, behind a gate
that holds the press while it is not.

**Status, 2026-09-24:** the A380 is reachable; the A350 not yet. The A380 presses at the
coordinates once `WASM_MOUSE_OVER`/`WASM_MOUSE_ENTER` tell the sim the cursor is on that
display. The A350 pressed nothing without the enter wrapper, and has not been tried with it. See [docs/log.md](docs/log.md).

| File | What it is |
| --- | --- |
| [docs/log.md](docs/log.md) | What the probes found |
| `probes/` | One file per question |
| `results/` | Raw probe output |

## Running p01

In a flight with the aircraft loaded and DevMode's inspector up on `127.0.0.1:19999`, from
this folder:

```bash
node probes/p01-wasm-away.mjs
```

It is guided. Every step waits for **Page Down**, read globally so it works with the sim in front
and does not move the mouse, and checks where the real cursor is before it fires. It uses
pointer-forwarding's inspector driver rather than a copy.
