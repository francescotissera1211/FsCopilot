// Minimal WASM disassembler for reading a few functions out of a large unstripped module.
//   node wasmdis.mjs <file.wasm> find <regex>        list functions whose name matches
//   node wasmdis.mjs <file.wasm> dis <name|index>    disassemble one function
//   node wasmdis.mjs <file.wasm> grep <regex>        functions with a matching instruction line
//   node wasmdis.mjs <file.wasm> callers <name>      functions that call it directly
//
// Written for inibuilds-A350.wasm, which ships unstripped and unoptimised, so the `name`
// section gives every function and the bodies read almost like source. MVP opcodes plus
// the 0xFC bulk-memory group; SIMD is not decoded and stops the listing with `??`.
import { readFileSync } from "node:fs"

const [file, mode, arg] = process.argv.slice(2)
const b = readFileSync(file)
let p = 8
const u = () => { let r = 0n, s = 0n, x; do { x = b[p++]; r |= BigInt(x & 0x7f) << s; s += 7n } while (x & 0x80); return Number(r) }
const s32 = () => { let r = 0n, s = 0n, x; do { x = b[p++]; r |= BigInt(x & 0x7f) << s; s += 7n } while (x & 0x80); if (s < 64n && (x & 0x40)) r |= -1n << s; return BigInt.asIntN(64, r) }
const str = () => { const n = u(); const t = b.toString("utf8", p, p + n); p += n; return t }

const types = [], funcTypes = [], names = new Map(), bodies = []
let nImportFuncs = 0
const globalsNames = new Map()
while (p < b.length) {
  const id = b[p++], size = u(), end = p + size
  if (id === 1) { const n = u(); for (let i = 0; i < n; i++) { p++; const np = u(); const ps = []; for (let j = 0; j < np; j++) ps.push(b[p++]); const nr = u(); const rs = []; for (let j = 0; j < nr; j++) rs.push(b[p++]); types.push({ ps, rs }) } }
  else if (id === 2) { const n = u(); for (let i = 0; i < n; i++) { const m = str(), f = str(), k = b[p++]
      if (k === 0) { funcTypes.push(u()); names.set(nImportFuncs, `${m}.${f}`); nImportFuncs++ }
      else if (k === 1) { p++; const fl = u(); u(); if (fl & 1) u() } else if (k === 2) { const fl = u(); u(); if (fl & 1) u() } else if (k === 3) p += 2 } }
  else if (id === 3) { const n = u(); for (let i = 0; i < n; i++) funcTypes.push(u()) }
  else if (id === 10) { const n = u(); for (let i = 0; i < n; i++) { const sz = u(); bodies.push({ start: p, end: p + sz }); p += sz } }
  else if (id === 0) { const nm = str(); if (nm === "name") { while (p < end) { const sub = b[p++], ssz = u(), send = p + ssz
        if (sub === 1) { const n = u(); for (let i = 0; i < n; i++) { const idx = u(); names.set(idx, str()) } }
        else if (sub === 7) { const n = u(); for (let i = 0; i < n; i++) { const idx = u(); globalsNames.set(idx, str()) } }
        p = send } } }
  p = end
}
const T = { 0x7f: "i32", 0x7e: "i64", 0x7d: "f32", 0x7c: "f64", 0x7b: "v128", 0x70: "funcref", 0x6f: "externref" }
const nameOf = (i) => names.get(i) || `f${i}`
const sig = (i) => { const t = types[funcTypes[i]]; return `(${t.ps.map((x) => T[x]).join(",")}) -> (${t.rs.map((x) => T[x]).join(",")})` }

// opcode tables
const NOARG = {}
;"unreachable nop".split(" ").forEach((n, i) => (NOARG[i] = n))
Object.assign(NOARG, { 0x05: "else", 0x0b: "end", 0x0f: "return", 0x1a: "drop", 0x1b: "select" })
const NUM = ("i32.eqz i32.eq i32.ne i32.lt_s i32.lt_u i32.gt_s i32.gt_u i32.le_s i32.le_u i32.ge_s i32.ge_u " +
  "i64.eqz i64.eq i64.ne i64.lt_s i64.lt_u i64.gt_s i64.gt_u i64.le_s i64.le_u i64.ge_s i64.ge_u " +
  "f32.eq f32.ne f32.lt f32.gt f32.le f32.ge f64.eq f64.ne f64.lt f64.gt f64.le f64.ge " +
  "i32.clz i32.ctz i32.popcnt i32.add i32.sub i32.mul i32.div_s i32.div_u i32.rem_s i32.rem_u i32.and i32.or i32.xor i32.shl i32.shr_s i32.shr_u i32.rotl i32.rotr " +
  "i64.clz i64.ctz i64.popcnt i64.add i64.sub i64.mul i64.div_s i64.div_u i64.rem_s i64.rem_u i64.and i64.or i64.xor i64.shl i64.shr_s i64.shr_u i64.rotl i64.rotr " +
  "f32.abs f32.neg f32.ceil f32.floor f32.trunc f32.nearest f32.sqrt f32.add f32.sub f32.mul f32.div f32.min f32.max f32.copysign " +
  "f64.abs f64.neg f64.ceil f64.floor f64.trunc f64.nearest f64.sqrt f64.add f64.sub f64.mul f64.div f64.min f64.max f64.copysign " +
  "i32.wrap_i64 i32.trunc_f32_s i32.trunc_f32_u i32.trunc_f64_s i32.trunc_f64_u i64.extend_i32_s i64.extend_i32_u i64.trunc_f32_s i64.trunc_f32_u i64.trunc_f64_s i64.trunc_f64_u " +
  "f32.convert_i32_s f32.convert_i32_u f32.convert_i64_s f32.convert_i64_u f32.demote_f64 f64.convert_i32_s f64.convert_i32_u f64.convert_i64_s f64.convert_i64_u f64.promote_f32 " +
  "i32.reinterpret_f32 i64.reinterpret_f64 f32.reinterpret_i32 f64.reinterpret_i64 i32.extend8_s i32.extend16_s i64.extend8_s i64.extend16_s i64.extend32_s").split(" ")
NUM.forEach((n, i) => (NOARG[0x45 + i] = n))
const MEM = ("i32.load i64.load f32.load f64.load i32.load8_s i32.load8_u i32.load16_s i32.load16_u i64.load8_s i64.load8_u i64.load16_s i64.load16_u i64.load32_s i64.load32_u " +
  "i32.store i64.store f32.store f64.store i32.store8 i32.store16 i64.store8 i64.store16 i64.store32").split(" ")
const VAR = { 0x20: "local.get", 0x21: "local.set", 0x22: "local.tee", 0x23: "global.get", 0x24: "global.set" }

function dis(fi, out) {
  const body = bodies[fi - nImportFuncs]
  p = body.start
  const nl = u(); const locals = []; for (let i = 0; i < nl; i++) { const c = u(); locals.push(`${c}x${T[b[p++]]}`) }
  const lines = [], calls = []
  let depth = 1
  while (p < body.end) {
    const at = p - body.start, op = b[p++]
    let t
    if (op === 0x02 || op === 0x03 || op === 0x04) { const bt = b[p]; if (bt === 0x40 || T[bt]) p++; else s32(); t = ["block", "loop", "if"][op - 2] }
    else if (op === 0x0c || op === 0x0d) t = (op === 0x0c ? "br " : "br_if ") + u()
    else if (op === 0x0e) { const n = u(); const ls = []; for (let i = 0; i <= n; i++) ls.push(u()); t = "br_table " + ls.join(",") }
    else if (op === 0x10) { const c = u(); calls.push(c); t = "call " + nameOf(c) }
    else if (op === 0x11) { const ti = u(); u(); t = "call_indirect type" + ti }
    else if (op === 0x1c) { const n = u(); p += n; t = "select_t" }
    else if (VAR[op]) { const i = u(); t = VAR[op] + " " + (op >= 0x23 ? (globalsNames.get(i) || "g" + i) : i) }
    else if (op === 0x25 || op === 0x26) { u(); t = op === 0x25 ? "table.get" : "table.set" }
    else if (op >= 0x28 && op <= 0x3e) { const al = u(); const off = u(); t = `${MEM[op - 0x28]} off=${off}` }
    else if (op === 0x3f || op === 0x40) { p++; t = op === 0x3f ? "memory.size" : "memory.grow" }
    else if (op === 0x41) t = "i32.const " + s32()
    else if (op === 0x42) t = "i64.const " + s32()
    else if (op === 0x43) { t = "f32.const " + b.readFloatLE(p); p += 4 }
    else if (op === 0x44) { t = "f64.const " + b.readDoubleLE(p); p += 8 }
    else if (op === 0xd0) { p++; t = "ref.null" }
    else if (op === 0xd1) { t = "ref.is_null" }
    else if (op === 0xd2) { u(); t = "ref.func" }
    else if (op === 0xfc) { const sub = u()
      if (sub <= 7) t = "trunc_sat." + sub
      else if (sub === 8) { u(); p++; t = "memory.init" } else if (sub === 9) { u(); t = "data.drop" }
      else if (sub === 10) { p += 2; t = "memory.copy" } else if (sub === 11) { p++; t = "memory.fill" }
      else { u(); if (sub === 12 || sub === 14) u(); t = "fc." + sub } }
    else if (NOARG[op] !== undefined) t = NOARG[op]
    else { t = `?? 0x${op.toString(16)} — stopping`; lines.push(`${String(at).padStart(6)} ${"  ".repeat(depth)}${t}`); break }
    if (t === "end" || t === "else") depth--
    lines.push(`${String(at).padStart(6)} ${"  ".repeat(Math.max(depth, 0))}${t}`)
    if (/^(block|loop|if)$/.test(t) || t === "else") depth++
  }
  if (out) { out.push(`;; ${nameOf(fi)}  [f${fi}]  ${sig(fi)}  locals ${locals.join(" ")}`); out.push(...lines) }
  return calls
}

const find = (a) => { if (/^\d+$/.test(a)) return Number(a); for (const [i, n] of names) if (n === a) return i; throw new Error("no function " + a) }

if (mode === "find") { const re = new RegExp(arg, "i"); for (const [i, n] of names) if (re.test(n)) console.log(`f${i}\t${sig(i)}\t${n}`) }
else if (mode === "dis") { const out = []; dis(find(arg), out); console.log(out.join("\n")) }
else if (mode === "grep") {
  // functions whose disassembly has a line matching <regex>
  const re = new RegExp(arg)
  for (let i = nImportFuncs; i < nImportFuncs + bodies.length; i++) {
    const out = []
    try { dis(i, out) } catch { continue }
    const hit = out.slice(1).filter((l) => re.test(l))
    if (hit.length) console.log(`${nameOf(i)}  [f${i}]\n   ` + hit.map((h) => h.trim()).join("\n   "))
  }
}
else if (mode === "callers") { const target = find(arg); for (let i = nImportFuncs; i < nImportFuncs + bodies.length; i++) { try { if (dis(i).includes(target)) console.log(`f${i}\t${nameOf(i)}`) } catch { } } }
