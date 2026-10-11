/*
 Weather preset and time control for FS Copilot.

 Talks to the sim's JS_LISTENER_WEATHER view listener (the one the in-game weather and time panels use).
 Weather.js from fs-base-ui is NOT included on purpose: it touches globals that may not exist in an
 instrument view, and a load failure there must never break the cockpit hook. The raw listener API is used:
   trigger ASK_WEATHER_LIST                        -> on 'SetWeatherList': [{index, name, isSelected, live, ...}]
   trigger CUSTOM_WEATHER_PRESET_CHANGED (index)   -> select a preset
   trigger SET_DATE (day, month, year), SET_ZULU_TIME (seconds of day)

 Only the presets are synced (selected by name), not the weather data itself: the list entries are summaries,
 and the full preset data is not available to instruments.

 Only one instrument instance (the elected leader) talks to the listener. Everything is wrapped so a failure is
 reported to the app and never throws into the instrument.

 Bus protocol (type 'wx'), every message fits the 512-byte bus:
   app -> js: {op:'ping'} | {op:'get'} | {op:'select', name, live} | {op:'time', sec, day, month, year}
   js -> app: {op:'hello', v, ok} | {op:'log', m} | {op:'state', name, index, live, count}
              {op:'result', what:'select'|'time', ok, err}
*/
const FSC_WX_VERSION = 2;
const FSC_WX_BEAT_MS = 2000;
const FSC_WX_STALE_S = 6;
const FSC_WX_LIST_WAIT_MS = 2000;
const FSC_WX_VERIFY_MS = 3000;

class FscWeather {
    constructor(bus) {
        this.bus = bus;
        this.id = Math.floor(Math.random() * 1000000) + 1;
        this.leader = false;
        this.listener = null;
        this.list = null;
        this.listWaiters = [];

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
                this.listener.on('SetWeatherList', data => this.onList(data));
            } catch (e) {
                this.listener = null;
                this.log('cannot register weather listener: ' + e);
            }
        }
        this.hello();
    }

    hello() { this.send({op: 'hello', v: FSC_WX_VERSION, ok: !!this.listener}); }

    onMessage(msg) {
        if (!msg || msg.type !== 'wx' || !this.leader) return;
        switch (msg.op) {
            case 'ping': this.hello(); break;
            case 'get': this.report(); break;
            case 'select': this.select(msg.name, !!msg.live); break;
            case 'time': this.setTime(msg); break;
        }
    }

    // ---- preset list ----

    onList(data) {
        let arr = null;
        if (Array.isArray(data)) arr = data;
        else if (data) for (const k of Object.keys(data)) if (Array.isArray(data[k])) { arr = data[k]; break; }
        this.list = arr;
        const waiters = this.listWaiters;
        this.listWaiters = [];
        for (const w of waiters) { try { w(arr); } catch (e) { this.log('list waiter error: ' + e); } }
    }

    // Ask for a fresh list; resolves with the array, or null if the sim doesn't answer in time.
    freshList(callback) {
        let done = false;
        const finish = arr => { if (!done) { done = true; callback(arr); } };
        this.listWaiters.push(finish);
        setTimeout(() => finish(null), FSC_WX_LIST_WAIT_MS);
        this.listener.trigger('ASK_WEATHER_LIST');
    }

    selected(list) {
        if (!list) return null;
        return list.find(p => p && p.isSelected === true) || null;
    }

    report() {
        if (!this.listener) { this.hello(); return; }
        this.freshList(list => {
            const sel = this.selected(list);
            this.send({
                op: 'state',
                name: sel ? String(sel.name) : null,
                index: sel ? sel.index : null,
                live: sel ? !!sel.live : null,
                count: list ? list.length : -1
            });
        });
    }

    findTarget(list, name, live) {
        if (!list) return null;
        return list.find(p => p && String(p.name) === name)
            || (live ? list.find(p => p && p.live === true) : null);
    }

    select(name, live) {
        if (!this.listener) { this.result('select', false, 'no weather listener'); return; }
        this.freshList(list => {
            if (!list) { this.result('select', false, 'sim did not return the preset list'); return; }
            const target = this.findTarget(list, name, live);
            if (!target) { this.result('select', false, 'no local preset named ' + name); return; }
            const cur = this.selected(list);
            if (cur && String(cur.name) === String(target.name)) { this.result('select', true); return; }

            // The event argument is undocumented: try the entry's index first, then its name.
            this.tryArgs(target, [target.index, target.name], 0);
        });
    }

    tryArgs(target, args, i) {
        if (i >= args.length) { this.result('select', false, 'sim ignored the preset change'); return; }
        try { this.listener.trigger('CUSTOM_WEATHER_PRESET_CHANGED', args[i]); }
        catch (e) { this.log('preset change threw: ' + e); }
        setTimeout(() => this.freshList(list => {
            const cur = this.selected(list);
            if (cur && String(cur.name) === String(target.name)) this.result('select', true, 'arg ' + i);
            else this.tryArgs(target, args, i + 1);
        }), FSC_WX_VERIFY_MS);
    }

    // ---- time ----

    setTime(msg) {
        if (!this.listener) { this.result('time', false, 'no weather listener'); return; }
        try {
            this.listener.trigger('SET_DATE', msg.day, msg.month, msg.year);
            this.listener.trigger('SET_ZULU_TIME', msg.sec);
            this.result('time', true);
        } catch (e) {
            this.result('time', false, String(e));
        }
    }

    result(what, ok, err) {
        this.send({op: 'result', what, ok, err: err ? String(err).slice(0, 300) : undefined});
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
