# WASM pointer log

    Purpose:  What the probes found, and what it changed.
    Order:    newest first

Same entry format as `record/pointer-forwarding/docs/build/log.md`.

---

## 2026-09-23 — The A380 presses where we say once the display is told the mouse entered it; the A350 does not

    Question:  pointer-forwarding Q05 — reopened, per aircraft. The A380 is reachable.
    Expected:  That the A380 would behave as the A350 did on 2026-08-30: WASM_MOUSE_DOWN
               pressing whatever the real cursor was over. xray447's commit 5defed4 said
               otherwise, but was tested with FS Copilot's Develop window in front.
    Found:     p01 fires a press at a marked target while the real cursor is held somewhere
               else, and checks where the cursor is before firing. The pilot reports what
               pressed.

               A380, target on a DU display, xray447's replay (10 moves, then mousedown):

                 real cursor                        sim in front   result
                 another control, same display      yes            target pressed
                 another display                    yes            nothing
                 cockpit, off every display         yes            nothing
                 anywhere                           no             target pressed

               A bare mousedown and a full move/down/up/click gave the same results as
               xray447's replay wherever it was tried. The streamed moves do nothing.

               So the A380 module uses the coordinates carried by WASM_MOUSE_DOWN, but the
               sim only passes input to the display the real cursor is on, while it has
               focus. xray447 tested in the one state with no gate.

               Adding WASM_MOUSE_OVER and WASM_MOUSE_ENTER before the press, and
               WASM_MOUSE_LEAVE after it, opens the gate. With the real cursor on another
               display, and again on the cockpit, the target pressed:

                 OVER(232,111,77) ENTER(232) MOVE(232,111,77) DOWN(232,111,77)
                 UP(232,111,77) CLICK(232,111,77) LEAVE(232)

               A350, target on the MFD, xray447's replay: nothing pressed in any of the four
               positions, including the same display and the sim in the background. Its module
               ignores the coordinates. p07's same-tick run on 2026-08-30 already sent OVER and
               ENTER, so the enter wrapper does not rescue it.

               Nothing fired later in any trial, on either aircraft, and nothing stuck.
               The latched press recorded on 2026-08-30 did not reproduce. In the A350's
               same-display case, nothing pressed rather than the control under the cursor.
               Whether the A350 changed since then, or the cursor was not on a control, is
               not known.
    Changed:   WASM displays are per aircraft, not closed. Where the module uses the
               coordinates, a replay wrapped in enter and leave drives it with the receiving
               pilot's cursor anywhere. The A380 is reachable; the A350 is not.

               Opt-in cannot key on the instrument identifier, which is `WasmInstrument` on
               both. It has to name the module, e.g. `inibuilds-A380.wasm`, or the module and
               gauge.
    Open:      Whether the LEAVE disturbs the receiving pilot's own hover when their cursor
               is on the target display, or on another one. Whether a held press and a drag
               survive the wrapper. The A350's same-display result against 2026-08-30.
    Affects:   pointer-forwarding 07-wasm-surface, 03-scope, 06-open-questions (Q05)
    Evidence:  results/p01-wasm-away-2026-09-23.ndjson — rows 1-3 and 8-15 A380,
               rows 4-7 A350, rows 16-17 A380 with the enter wrapper
