/*
 Experimental weather sync for FS Copilot.

 Talks to the sim's JS_LISTENER_WEATHER view listener (the same one the in-game weather panel uses).
 Weather.js from fs-base-ui is NOT included on purpose: it touches globals that may not exist in an
 instrument view, and a load failure there must never break the cockpit hook. The raw listener API is used
 instead:
   trigger ASK_WEATHER_LIST                    -> on 'SetWeatherList'
   on 'UpdatePreset'                           -> current preset data
   call 'UpdateTempWeatherPreset' (preset)     -> apply a full custom preset
   trigger CUSTOM_WEATHER_PRESET_CHANGED (id)  -> select an existing preset (fallback)

 Only one instrument instance (the elected leader) talks to the listener. Everything is wrapped so a failure
 is reported to the app and never throws into the instrument.

 Bus protocol (type 'wx'), payloads base64-chunked to fit the 512-byte bus:
   app -> js: {op:'get'} | {op:'chunk', k:'apply', s, i, n, d}
   js -> app: {op:'hello', v} | {op:'log', m} | {op:'result', ok, how, err}
              {op:'chunk', k:'state'|'diag', s, i, n, d}
*/
const FSC_WX_VERSION = 1;
const FSC_WX_CHUNK = 300;
const FSC_WX_BEAT_MS = 2000;
const FSC_WX_STALE_S = 6;

class FscWeather {
    constructor(bus) {
        this.bus = bus;
        this.id = Math.floor(Math.random() * 1000000) + 1;
        this.leader = false;
        this.listener = null;
        this.latestPreset = null;
        this.latestList = null;
        this.diagSent = false;
        this.seq = 0;
        this.incoming = new Map();

        bus.addEventListener('message', msg => {
            try { this.onMessage(msg); } catch (e) { this.log('message error: ' + e); }
        });
        setInterval(() => {
            try { this.elect(); } catch (e) { console.error('[FsCopilot] [Weather] elect', e); }
        }, FSC_WX_BEAT_MS);
    }

    now() { return (Date.now() / 1000) % 100000; }

    elect() {
        const leader = SimVar.GetSimVarValue('L:FSC_WX_LEADER', 'number');
        const beat = SimVar.GetSimVarValue('L:FSC_WX_BEAT', 'number');
        const now = this.now();
        if (leader === this.id) {
            SimVar.SetSimVarValue('L:FSC_WX_BEAT', 'number', now);
            if (!this.leader) this.becomeLeader();
            return;
        }
        this.leader = false;
        if (!leader || now - beat > FSC_WX_STALE_S || now < beat) {
            SimVar.SetSimVarValue('L:FSC_WX_LEADER', 'number', this.id);
            SimVar.SetSimVarValue('L:FSC_WX_BEAT', 'number', now);
        }
    }

    becomeLeader() {
        this.leader = true;
        if (!this.listener) {
            try {
                this.listener = RegisterViewListener('JS_LISTENER_WEATHER', () => this.log('weather listener registered'));
                this.listener.on('UpdatePreset', data => { this.latestPreset = data; });
                this.listener.on('SetWeatherList', data => { this.latestList = data; });
            } catch (e) {
                this.listener = null;
                this.log('cannot register weather listener: ' + e);
            }
        }
        this.send({op: 'hello', v: FSC_WX_VERSION, ok: !!this.listener});
    }

    onMessage(msg) {
        if (!msg || msg.type !== 'wx' || !this.leader) return;
        if (msg.op === 'get') this.report();
        if (msg.op === 'chunk' && msg.k === 'apply') {
            const json = this.collect(msg);
            if (json != null) this.apply(json);
        }
    }

    report() {
        if (!this.listener) {
            this.send({op: 'hello', v: FSC_WX_VERSION, ok: false});
            return;
        }
        this.listener.trigger('ASK_WEATHER_LIST');
        setTimeout(() => {
            try {
                if (!this.diagSent) {
                    this.diagSent = true;
                    this.sendChunked('diag', JSON.stringify({
                        updatePreset: this.describe(this.latestPreset),
                        list: this.describe(this.latestList)
                    }));
                }
                const current = this.current();
                this.sendChunked('state', JSON.stringify({
                    source: current.source,
                    preset: current.preset
                }));
            } catch (e) { this.log('report error: ' + e); }
        }, 1500);
    }

    current() {
        if (this.latestPreset) return {source: 'UpdatePreset', preset: this.latestPreset};
        const list = this.presetArray(this.latestList);
        if (list) {
            const idx = this.selectedIndex(this.latestList);
            if (idx != null && list[idx]) return {source: 'list[' + idx + ']', preset: list[idx]};
        }
        return {source: 'none', preset: null};
    }

    presetArray(data) {
        if (!data) return null;
        if (Array.isArray(data)) return data;
        for (const k of Object.keys(data)) if (Array.isArray(data[k])) return data[k];
        return null;
    }

    selectedIndex(data) {
        if (!data || Array.isArray(data)) return null;
        for (const k of Object.keys(data)) {
            if (/select|current|active/i.test(k) && typeof data[k] === 'number') return data[k];
        }
        return null;
    }

    apply(json) {
        let preset;
        try { preset = JSON.parse(json); } catch (e) { this.result(false, 'parse', String(e)); return; }
        if (!this.listener) { this.result(false, 'none', 'no weather listener'); return; }

        let done = false;
        const timer = setTimeout(() => {
            if (done) return;
            done = true;
            this.fallback(preset, 'UpdateTempWeatherPreset timed out');
        }, 5000);

        try {
            this.listener.call('UpdateTempWeatherPreset', preset)
                .then(() => {
                    if (done) return;
                    done = true;
                    clearTimeout(timer);
                    this.result(true, 'temp');
                })
                .catch(err => {
                    if (done) return;
                    done = true;
                    clearTimeout(timer);
                    this.fallback(preset, 'UpdateTempWeatherPreset failed: ' + JSON.stringify(err));
                });
        } catch (e) {
            done = true;
            clearTimeout(timer);
            this.fallback(preset, 'UpdateTempWeatherPreset threw: ' + e);
        }
    }

    // Select a preset with the same name from the local list, if there is one.
    fallback(preset, reason) {
        try {
            const list = this.presetArray(this.latestList) || [];
            const nameKey = Object.keys(preset).find(k => /name/i.test(k) && typeof preset[k] === 'string');
            const idKey = Object.keys(preset).find(k => /^(s|i)?id$/i.test(k));
            const idx = nameKey ? list.findIndex(p => p && p[nameKey] === preset[nameKey]) : -1;
            if (idx < 0) { this.result(false, 'none', reason + '; no matching preset in local list'); return; }
            const id = idKey && list[idx][idKey] !== undefined ? list[idx][idKey] : idx;
            this.listener.trigger('CUSTOM_WEATHER_PRESET_CHANGED', id);
            this.result(true, 'preset', reason + '; selected local preset ' + preset[nameKey]);
        } catch (e) {
            this.result(false, 'none', reason + '; fallback error: ' + e);
        }
    }

    result(ok, how, err) { this.send({op: 'result', ok, how, err: err ? String(err).slice(0, 300) : undefined}); }

    // Structure only (keys, types, array lengths), for diagnosing the payload format from the app log.
    describe(v, depth = 0) {
        if (v === null || v === undefined) return null;
        if (Array.isArray(v)) return {array: v.length, first: v.length ? this.describe(v[0], depth + 1) : null};
        if (typeof v !== 'object') return typeof v === 'string' ? 'string:' + v.slice(0, 40) : typeof v + ':' + v;
        if (depth > 2) return 'object';
        const out = {};
        for (const k of Object.keys(v)) out[k] = this.describe(v[k], depth + 1);
        return out;
    }

    collect(msg) {
        const key = msg.k + ':' + msg.s;
        let entry = this.incoming.get(key);
        if (!entry) {
            entry = {parts: new Array(msg.n), got: 0, at: Date.now()};
            this.incoming.set(key, entry);
        }
        for (const [k, e] of this.incoming) if (Date.now() - e.at > 10000) this.incoming.delete(k);
        if (entry.parts[msg.i] === undefined) { entry.parts[msg.i] = msg.d; entry.got++; }
        if (entry.got < msg.n) return null;
        this.incoming.delete(key);
        return decodeURIComponent(escape(atob(entry.parts.join(''))));
    }

    sendChunked(kind, text) {
        const b64 = btoa(unescape(encodeURIComponent(text)));
        const n = Math.max(1, Math.ceil(b64.length / FSC_WX_CHUNK));
        const s = ++this.seq;
        for (let i = 0; i < n; i++) {
            const d = b64.substr(i * FSC_WX_CHUNK, FSC_WX_CHUNK);
            // Space chunks out so the single-slot client data area isn't overwritten before it's read.
            setTimeout(() => this.send({op: 'chunk', k: kind, s, i, n, d}), i * 40);
        }
    }

    log(m) {
        console.log('[FsCopilot] [Weather] ' + m);
        this.send({op: 'log', m: String(m).slice(0, 380)});
    }

    send(data) {
        try { this.bus.send(Object.assign({type: 'wx'}, data)); }
        catch (e) { console.error('[FsCopilot] [Weather] send', e); }
    }
}
