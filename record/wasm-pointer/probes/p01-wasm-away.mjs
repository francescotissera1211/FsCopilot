/*
 * P01 — Does a WASM gauge honour injected coordinates when the real cursor is
 * NOT on that display?
 *
 * Q05 closed negative on the A350: WASM_MOUSE_DOWN pressed whatever the real
 * cursor hovered, 120 px from the injected point. But in every p07 run the real
 * cursor was on the same display. xray447's A380 commit replays streamed moves
 * then a mousedown and reports that it works, tested with the real cursor on
 * the Develop window, not in the cockpit. This asks whether the sim falls back
 * to the injected point when its own raycast hits no display.
 *
 *   node record/wasm-pointer/probes/p01-wasm-away.mjs
 *
 * A guided session. Nothing is timed: every step waits for Page Down, read
 * globally so it works with the sim in front and does not move the mouse.
 * Before firing, the step checks where the real cursor is — the foreground
 * window from Windows, the hovered display from the panels — and refuses to
 * fire if that is not what the step asked for.
 *
 * Trials, per aircraft:
 *   1  real cursor on another control, same display     control: p07 repeated
 *   3  real cursor on another display
 *   4  real cursor on the cockpit, off every display
 *   5  sim in the background
 * each with xray447's replay (10 moves at 20 Hz, then move + mousedown, no
 * mouseup). Where that presses the target, the same case is repeated with a
 * bare mousedown and with a full move/down/up/click.
 *
 * Nothing is suppressed. Coherent.call is wrapped pass-through for one
 * synchronous dispatch and restored in `finally`; the panel listeners only
 * read. Every trial appends a line to results/p01-wasm-away-<date>.ndjson.
 */

import { mkdirSync, appendFileSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, join } from "node:path"
import { spawn } from "node:child_process"
import { createInterface } from "node:readline"

import { listPages, Inspector } from "../../pointer-forwarding/probes/lib/inspector.mjs"

const HERE = dirname(fileURLToPath(import.meta.url))
const RESULTS = join(HERE, "..", "results")
const SIM = /FlightSimulator/i

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// ---- terminal ---------------------------------------------------------------

const rl = createInterface({ input: process.stdin, output: process.stdout })
const lines = []
let lineWaiter = null
rl.on("line", (l) => { if (lineWaiter) { const w = lineWaiter; lineWaiter = null; w(l) } else lines.push(l) })

function ask(prompt) {
  process.stdout.write(prompt)
  lines.length = 0
  return new Promise((r) => { lineWaiter = r })
}

async function choose(prompt, keys) {
  for (;;) {
    const a = (await ask(`${prompt} `)).trim().toLowerCase()
    if (keys.includes(a)) return a
    console.log(`  type one of: ${keys.join(" ")}`)
  }
}

// ---- Page Down, read globally ---------------------------------------------

const PS = `
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WP01 {
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int v);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
[Console]::Out.WriteLine("READY")
$was = $false
while ($true) {
  $now = ([WP01]::GetAsyncKeyState(0x22) -band 0x8001) -ne 0
  if ($now -and -not $was) {
    $procId = 0
    [void][WP01]::GetWindowThreadProcessId([WP01]::GetForegroundWindow(), [ref]$procId)
    $name = "?"
    try { $name = (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch {}
    [Console]::Out.WriteLine("KEY " + $name)
  }
  $was = $now
  Start-Sleep -Milliseconds 20
}`

let keyWaiter = null
function startKeys() {
  const ps = spawn("powershell.exe",
    ["-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Buffer.from(PS, "utf16le").toString("base64")],
    { stdio: ["ignore", "pipe", "ignore"] })
  process.on("exit", () => { try { ps.kill() } catch { /* gone */ } })
  const out = createInterface({ input: ps.stdout })
  return new Promise((ready, fail) => {
    ps.on("error", fail)
    out.on("line", (l) => {
      if (l === "READY") return ready()
      if (l.startsWith("KEY ") && keyWaiter) { const w = keyWaiter; keyWaiter = null; w({ foreground: l.slice(4) }) }
    })
  })
}

/** Wait for Page Down. Presses while nobody is waiting are dropped. */
function pageDown() { return new Promise((r) => { keyWaiter = r }) }

// ---- panels -----------------------------------------------------------------

// Chrome 49 inside: var and function only.
const ARM = `
(function () {
  var all = document.querySelectorAll("wasm-sim-canvas")
  if (!all.length) return null
  if (!window.__WP01) {
    var S = window.__WP01 = { d: {}, downs: [] }
    function canvasOf(ev) {
      var t = ev.target
      return t && t.closest ? t.closest("wasm-sim-canvas") : (t && t.tagName === "WASM-SIM-CANVAS" ? t : null)
    }
    function seen(ev) {
      if (ev.__wp01) return
      var c = canvasOf(ev)
      if (!c) return
      var g = String(c.m_wasmInstrumentGUid)
      var d = S.d[g] || (S.d[g] = { over: false })
      var leaving = ev.type === "mouseleave" || (ev.type === "mouseout" && !(ev.relatedTarget && c.contains(ev.relatedTarget)))
      if (leaving) { d.over = false; d.t = Date.now(); return }
      d.over = true; d.t = Date.now(); d.x = ev.clientX; d.y = ev.clientY
      if (ev.type === "mousedown") S.downs.push({ guid: g, t: d.t })
    }
    var types = ["mousemove", "mouseover", "mouseout", "mouseleave", "mousedown"]
    for (var i = 0; i < types.length; i++) document.addEventListener(types[i], seen, true)
  }
  var out = []
  for (var j = 0; j < all.length; j++) {
    var inst = all[j].closest("wasm-instrument")
    var u = inst ? String(inst.getAttribute("url") || "") : ""
    var g = /wasm_gauge=([^&]+)/i.exec(u), m = /wasm_module=([^&]+)/i.exec(u)
    out.push({ guid: String(all[j].m_wasmInstrumentGUid), gauge: g ? g[1] : "", module: m ? m[1].split(/[\\\\/]/).pop() : "" })
  }
  return out
})()`

const STATE = `(function(){ var S = window.__WP01; return S ? { d: S.d, downs: S.downs } : null })()`

function fireSource(seq, x, y, guid) {
  return `
(function () {
  var X = ${x}, Y = ${y}, GUID = ${JSON.stringify(guid)}, SEQ = ${JSON.stringify(seq)}
  var canvas = null, all = document.querySelectorAll("wasm-sim-canvas")
  for (var i = 0; i < all.length; i++) if (String(all[i].m_wasmInstrumentGUid) === GUID) canvas = all[i]
  if (!canvas) return { error: "the display is gone (panel reloaded?) — restart p01" }
  var target = canvas.querySelector("img") || canvas
  var calls = []
  function step(fn) {
    var native = Coherent.call
    Coherent.call = function (name) {
      if (typeof name === "string" && name.indexOf("WASM_") === 0)
        calls.push(name + "(" + Array.prototype.slice.call(arguments, 1).join(",") + ")")
      return native.apply(this, arguments)
    }
    try { fn() } finally { Coherent.call = native }
  }
  function send(type, x, y, buttons) {
    var ev = new MouseEvent(type, { bubbles: true, cancelable: true, view: window,
      clientX: x, clientY: y, screenX: x, screenY: y, button: 0, buttons: buttons })
    ev.__wp01 = true
    target.dispatchEvent(ev)
  }
  if (SEQ === "down") { step(function () { send("mousedown", X, Y, 1) }); return { calls: calls } }
  if (SEQ === "up") { step(function () { send("mouseup", X, Y, 0) }); return { calls: calls } }
  if (SEQ === "click") {
    step(function () { send("mousemove", X, Y, 0); send("mousedown", X, Y, 1); send("mouseup", X, Y, 0); send("click", X, Y, 0) })
    return { calls: calls }
  }
  // mouseenter/mouseleave do not bubble, and WasmSimCanvas binds them on the canvas itself
  function onCanvas(type) { var hold = target; target = canvas; send(type, X, Y, 0); target = hold }
  function press() { send("mousemove", X, Y, 0); send("mousedown", X, Y, 1); send("mouseup", X, Y, 0); send("click", X, Y, 0) }
  if (SEQ === "enter") {
    step(function () { send("mouseover", X, Y, 0); onCanvas("mouseenter"); press(); onCanvas("mouseleave") })
    return { calls: calls }
  }
  if (SEQ === "entertrail") {
    // enter, then 10 moves at 20 Hz from 120 px up-left, then press and leave: for a module
    // whose cursor has to travel to the point rather than jump
    var trail = window.__WP01_FIRE = { done: false, calls: calls }
    step(function () { send("mouseover", X - 120, Y - 120, 0); onCanvas("mouseenter") })
    var k = 0
    var t = setInterval(function () {
      try {
        k++
        var f = k / 10
        step(function () { send("mousemove", Math.round(X - 120 * (1 - f)), Math.round(Y - 120 * (1 - f)), 0) })
        if (k < 10) return
        clearInterval(t)
        step(function () { press(); onCanvas("mouseleave") })
        trail.done = true
      } catch (e) { clearInterval(t); trail.error = String(e); trail.done = true }
    }, 50)
    return { pending: true }
  }
  // xray: the inspector cannot await a Promise, so finish into a global
  var out = window.__WP01_FIRE = { done: false, calls: calls }
  var n = 0
  var timer = setInterval(function () {
    try {
      n++
      var f = n / 10
      step(function () { send("mousemove", Math.round(X - 120 * (1 - f)), Math.round(Y - 120 * (1 - f)), 0) })
      if (n < 10) return
      clearInterval(timer)
      step(function () { send("mousemove", X, Y, 0); send("mousedown", X, Y, 1) })
      out.done = true
    } catch (e) { clearInterval(timer); out.error = String(e); out.done = true }
  }, 50)
  return { pending: true }
})()`
}

/** One inspector connection per WASM page, held for the whole session. */
class Panels {
  async open() {
    const pages = (await listPages()).filter((p) => /WasmInstrument/i.test(String(p.title || "")))
    this.pages = []
    this.displays = new Map()
    for (const p of pages) {
      const insp = await new Inspector(p.id).open()
      const found = await insp.evaluate(ARM)
      if (!found) { insp.close(); continue }
      this.pages.push({ page: p, insp })
      for (const d of found) this.displays.set(d.guid, { ...d, page: p, insp })
    }
    return this.displays.size
  }

  /** Where the panels last saw the real cursor, and every real mousedown. */
  async state() {
    const d = {}, downs = []
    for (const { insp } of this.pages) {
      const s = await insp.evaluate(STATE)
      if (!s) continue
      Object.assign(d, s.d)
      downs.push(...s.downs)
    }
    const over = Object.entries(d).filter(([, v]) => v.over).sort((a, b) => b[1].t - a[1].t)[0]
    return { hovered: over ? { guid: over[0], ...over[1] } : null, downs }
  }

  name(guid) {
    const d = this.displays.get(guid)
    return d ? `${d.module.replace(/\.wasm$/i, "")} ${d.gauge} (${d.page.title.split(" - ")[0]})` : `display ${guid}`
  }

  async fire(seq, mark) {
    const insp = this.displays.get(mark.guid).insp
    let r = await insp.evaluate(fireSource(seq, mark.x, mark.y, mark.guid))
    for (let k = 0; r && r.pending && k < 40; k++) {
      await sleep(100)
      const f = await insp.evaluate("window.__WP01_FIRE")
      if (f && f.done) r = f
    }
    if (!r || r.pending) throw new Error("the sequence did not finish within 4 s")
    if (r.error) throw new Error(r.error)
    return r.calls
  }

  close() { for (const { insp } of this.pages) insp.close() }
}

// ---- the session ------------------------------------------------------------

const CASES = {
  1: { say: "Mouse on ANOTHER control, same display.",
       sim: true, check: (h, m) => h && h.guid === m.guid, want: "same display" },
  3: { say: "Mouse on ANOTHER display.",
       sim: true, check: (h, m) => h && h.guid !== m.guid, want: "another display" },
  4: { say: "Mouse on the cockpit, off every display.",
       sim: true, check: (h) => !h, want: "no display" },
  5: { say: "Click this terminal.",
       sim: false, check: () => true, want: "anywhere" }
}

const ANSWERS = {
  b: "target pressed", u: "control under my mouse pressed", n: "nothing", o: "something else"
}

async function waitForPosition(panels, kase, mark) {
  const c = CASES[kase]
  for (;;) {
    console.log(`\n  ${c.say}`)
    console.log(c.sim ? "  Alt+Tab to the sim (no click), then Page Down."
                      : "  Then Page Down.")
    const before = (await panels.state()).downs.length
    const key = await pageDown()
    const s = await panels.state()

    if (s.downs.length > before) {
      console.log(`  ✗ You clicked ${panels.name(s.downs[s.downs.length - 1].guid)}.`)
      await ask("  Reset it, then Enter. ")
      continue
    }
    const inSim = SIM.test(key.foreground)
    if (c.sim && !inSim) { console.log("  ✗ Sim not in front."); continue }
    if (!c.sim && inSim) { console.log("  ✗ Sim still in front."); continue }
    if (!c.check(s.hovered, mark)) {
      const where = s.hovered ? panels.name(s.hovered.guid) : "no display"
      console.log(`  ✗ Mouse is on ${where}, needs ${c.want}.`)
      const go = await choose("  Fire anyway? [y/n]", ["y", "n"])
      if (go === "n") continue
      return { foreground: key.foreground, hovered: s.hovered, overridden: true }
    }
    return { foreground: key.foreground, hovered: s.hovered, overridden: false }
  }
}

async function trial(panels, mark, kase, seq, file, label) {
  console.log(`\n── ${label}`)
  const pos = await waitForPosition(panels, kase, mark)
  const calls = await panels.fire(seq, mark)
  console.log(`  Fired.`)

  const a = await choose("  Result? [b] target  [u] under mouse  [n] nothing  [o] other", Object.keys(ANSWERS))
  let other = ""
  if (a === "o") other = (await ask("  What? ")).trim()

  const later = await choose("  Wave over the display. Anything fire by itself? [y/n]", ["y", "n"])

  let stuck = null
  if (seq !== "click") {
    stuck = await choose("  Stuck? [y/n]", ["y", "n"])
    if (stuck === "y") { await panels.fire("up", mark); console.log("  Released.") }
  }
  await ask("  Reset the display, then Enter. ")

  const row = {
    t: new Date().toISOString(), case: kase, seq,
    aircraft: panels.displays.get(mark.guid).module, target: { ...mark, display: panels.name(mark.guid) },
    foreground: pos.foreground, hovered: pos.hovered ? panels.name(pos.hovered.guid) : null, overridden: pos.overridden,
    outcome: ANSWERS[a] + (other ? `: ${other}` : ""), pressedLater: later === "y", stuck: stuck === null ? null : stuck === "y", calls
  }
  appendFileSync(file, JSON.stringify(row) + "\n")
  return a
}

async function main() {
  console.log("P01 WASM pointer")
    const panels = new Panels()
  const n = await panels.open().catch(() => 0)
  if (!n) throw new Error("no WASM displays found. Load the A350 or A380 into a flight (DevMode on) and run this again.")
  await startKeys()
  console.log(`${n} displays found.`)

  mkdirSync(RESULTS, { recursive: true })
  const file = join(RESULTS, `p01-wasm-away-${new Date().toISOString().slice(0, 10)}.ndjson`)

  let mark
  for (;;) {
    console.log("\n── Target")
    console.log("  Hover a control that visibly reacts (no click), then Page Down.")
    const key = await pageDown()
    const s = await panels.state()
    if (!SIM.test(key.foreground)) { console.log(`  ✗ Sim not in front.`); continue }
    if (!s.hovered) {
      console.log("  ✗ No display under the mouse. Wiggle, then Page Down.")
      continue
    }
    mark = { guid: s.hovered.guid, x: s.hovered.x, y: s.hovered.y }
    const ok = await choose(`  ${panels.name(mark.guid)}. Right display? [y/n]`, ["y", "n"])
    if (ok === "y") break
  }

  // --only 3,4 --seq enter: just those positions, one sequence each
  const at = process.argv.indexOf("--only")
  if (at !== -1) {
    const only = process.argv[at + 1].split(",").map(Number)
    const s = process.argv.indexOf("--seq")
    const seq = s !== -1 ? process.argv[s + 1] : "enter"
    for (const [i, kase] of only.entries())
      await trial(panels, mark, kase, seq, file, `Position ${i + 1} of ${only.length}: ${CASES[kase].say}`)
    console.log(`\nDone: ${file}`)
    panels.close()
    process.exit(0)
  }

  const order = [1, 3, 4, 5]
  for (const [i, kase] of order.entries()) {
    const where = `Position ${i + 1} of ${order.length}`
    const a = await trial(panels, mark, kase, "xray", file, `${where}, sequence 1 of 3`)
    if (a === "b") {
      await trial(panels, mark, kase, "down", file, `${where} again, sequence 2 of 3`)
      await trial(panels, mark, kase, "click", file, `${where} again, sequence 3 of 3`)
    }
  }

  console.log(`\nDone: ${file}`)
  console.log("Next aircraft: run again.")
  panels.close()
  process.exit(0)
}

process.on("SIGINT", () => { console.log("\nStopped. Trials so far are saved."); process.exit(0) })

main().catch((e) => { console.error("\n" + e.message + "\n"); process.exit(1) })
