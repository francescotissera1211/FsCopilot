# WASM pointer log

    Purpose:  What the probes found, and what it changed.
    Order:    newest first

Same entry format as `record/pointer-forwarding/docs/build/log.md`.

---

## 2026-09-24 — Read out of the A350's binary: the press position comes from the draw data, not the click

    Question:  why the A350 ignores the coordinates, and whether a mod to its module could
               make it take them
    Expected:  That the coordinates might not reach the module at all.
    Found:     They reach it and it throws them away. inibuilds-A350.wasm is a Community
               install, unencrypted, unstripped and unoptimised, so tools/wasmdis.mjs reads
               it as near-source. The A380 is streamed in encrypted .fsarchive files and
               cannot be read.

               The module imports nothing that reads the mouse. Input arrives two ways:

               1. <gauge>_mouse_callback(x, y, flags), one per display. All ten call
                  GaugeInstance::MouseCallback<G>, which writes {type, x*2, y} into ONE slot
                  shared by every display (memory 1421660). LEFTSINGLE is type 1, LEFTDRAG 2,
                  LEFTRELEASE 3, wheel 4/5, anything else (MOVE) 0, LEAVE 0 at (0,0).
               2. The per-frame draw callback's sGaugeDrawData, whose mx/my is the sim's
                  own cursor position on that display, -1 when the cursor is elsewhere.

               GaugeInstance::DoCallback, per display per frame:

                 if draw.mx >= 0 && draw.my >= 0:
                     gauge.pos = (draw.mx, draw.my)
                     gauge.MouseEvent(slot)       vtable slot 7
                     slot = {0, 0, 0}
                 (else the slot is left alone)

               FMGS::MouseEvent reads only slot.type. If this pilot's KCCU cursor is not on
               this screen (variables 1559/1445, 1560/1446 for FO), a press calls
               SwitchCaptainCursor(2) / SwitchFOCursor(2) and nothing else. Otherwise the
               cursor is set from gauge.pos, and type 1-5 set press, drag, release and wheel
               flags there. Slot x and y are written and never read.

               That accounts for every A350 result. 2026-08-30's IRS press: the event was
               carried out at draw.mx/my, the real cursor. The latched press: with no display
               under the real cursor the gate stays shut and the event waits in the slot until
               it opens. 2026-09-23's "nothing, never later": nothing clickable under the
               cursor, and any real mouse event overwrites the shared slot before the gate
               opens.
    Changed:   A mod is possible in principle, and it is two changes to DoCallback: take the
               position from the slot rather than draw.mx/my when the slot holds a press, and
               let that press through when the real cursor is not on the display. Neither is
               written or tested. Three things stand in the way before it can be:

               - The slot does not say which display an event is for. A peer press consumed
                 by whichever display draws first could land on the wrong one. The ten
                 MouseCallback<G> functions each know their display and would have to record
                 it.
               - The units of slot x/y against draw.mx/my are unknown. The x*2 is unexplained,
                 and harmless only because nothing reads it.
               - The KCCU cursor gate: the receiving pilot's cursor may be on another screen,
                 where a press only moves the cursor.

               Distribution would be a patch applied to the user's own copy, checked against
               the A350 version's hash, not a copy of iniBuilds' binary.
    Affects:   none yet
    Evidence:  tools/wasmdis.mjs on inibuilds-A350.wasm (68 952 481 bytes, 2026-09-24):
               MFD_mouse_callback, GaugeInstance::MouseCallback<MFD>,
               GaugeInstance::DoCallback, FMGS::MouseEvent

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
               positions, including the same display and the sim in the background. The
               enter wrapper was not tried on it. p07's same-tick run on 2026-08-30 sent OVER
               and ENTER, but with the real cursor on the MFD, whose own moves compete.

               Nothing fired later in any trial, on either aircraft, and nothing stuck.
               The latched press recorded on 2026-08-30 did not reproduce. In the A350's
               same-display case, nothing pressed rather than the control under the cursor.
               Whether the A350 changed since then, or the cursor was not on a control, is
               not known.
    Changed:   WASM displays are per aircraft, not closed. Where the module uses the
               coordinates, a replay wrapped in enter and leave drives it with the receiving
               pilot's cursor anywhere. The A380 is reachable; the A350 is not yet.

               Opt-in cannot key on the instrument identifier, which is `WasmInstrument` on
               both. It has to name the module, e.g. `inibuilds-A380.wasm`, or the module and
               gauge.
    Open:      Whether the LEAVE disturbs the receiving pilot's own hover when their cursor
               is on the target display, or on another one. Whether a held press and a drag
               survive the wrapper. The A350's same-display result against 2026-08-30.
    Affects:   pointer-forwarding 07-wasm-surface, 03-scope, 06-open-questions (Q05)
    Evidence:  results/p01-wasm-away-2026-09-23.ndjson — rows 1-3 and 8-15 A380,
               rows 4-7 A350, rows 16-17 A380 with the enter wrapper
