/**
 * Remote pilot pointer overlay.
 *
 * Draws where the other pilot is pointing on this screen and pulses on their clicks, so a shared
 * cockpit feels like a real crew looking at the same displays. Positions arrive from the host app
 * (the peer sends {"type":"interact", ... x, y, from}); this class never listens to local input.
 *
 * Kept in hook.js on purpose: VCockpit.js is a copy of the stock simulator file and should stay
 * as close to the original as possible.
 */
class RemotePointer {
    /**
     * @param {HTMLElement} root screen root element the pointer is drawn into
     */
    constructor(root) {
        this._root = root;
        this._el = null;
        this._label = null;
        this._ring = null;
        this._hideTimer = null;
        this._hideDelayMs = 2500;
    }

    /**
     * @param {string} label name of the pilot the pointer belongs to
     * @param {number} x normalized [0..1] position inside the screen
     * @param {number} y normalized [0..1] position inside the screen
     * @param {string} [type] mousemove, mousedown, mouseup (clicks are animated)
     */
    update(label, x, y, type) {
        if (typeof x !== 'number' || typeof y !== 'number') return;

        this._ensure();

        const r = this._root.getBoundingClientRect();
        this._el.style.left = Math.round(x * (r.width || 1)) + 'px';
        this._el.style.top = Math.round(y * (r.height || 1)) + 'px';
        this._el.style.display = 'block';

        if (label && this._label.textContent !== label) this._label.textContent = label;

        if (type === 'mousedown' || type === 'mouseup' || type === 'click') this._pulse();

        if (this._hideTimer) clearTimeout(this._hideTimer);
        this._hideTimer = setTimeout(() => { this._el.style.display = 'none'; }, this._hideDelayMs);
    }

    hide() {
        if (this._hideTimer) clearTimeout(this._hideTimer);
        if (this._el) this._el.style.display = 'none';
    }

    _ensure() {
        if (this._el) return;

        const el = document.createElement('div');
        el.style.cssText = 'position:absolute;left:0;top:0;width:0;height:0;' +
            'display:none;pointer-events:none;z-index:2147483647;';

        // cursor arrow: dark outline + bright fill so it stays visible on any display
        el.innerHTML =
            '<svg width="26" height="26" viewBox="0 0 24 24" style="position:absolute;left:-3px;top:-2px;">' +
            '<path d="M4 2 L4 19 L8.6 14.6 L11.6 21.5 L14.2 20.3 L11.2 13.6 L17.4 13.2 Z" ' +
            'fill="#ffd640" stroke="#101010" stroke-width="1.6" stroke-linejoin="round"/>' +
            '</svg>';

        const ring = document.createElement('div');
        ring.style.cssText = 'position:absolute;left:-14px;top:-14px;width:28px;height:28px;' +
            'border-radius:50%;border:2px solid #ffd640;opacity:0;transform:scale(0.3);';

        const label = document.createElement('div');
        label.style.cssText = 'position:absolute;left:16px;top:16px;padding:1px 5px;white-space:nowrap;' +
            'border-radius:3px;background:rgba(0,0,0,0.65);color:#ffd640;font:11px/1.4 sans-serif;';

        el.appendChild(ring);
        el.appendChild(label);

        this._ring = ring;
        this._label = label;
        this._el = el;
        this._root.appendChild(el);
    }

    /** Short click feedback: an expanding ring around the pointer. */
    _pulse() {
        const ring = this._ring;
        ring.style.transition = 'none';
        ring.style.transform = 'scale(0.3)';
        ring.style.opacity = '0.95';

        setTimeout(() => {
            ring.style.transition = 'transform 260ms ease-out, opacity 260ms ease-out';
            ring.style.transform = 'scale(1.6)';
            ring.style.opacity = '0';
        }, 16);
    }
}

class Hook {
    constructor(instrument) {
        const hookId = Math.floor(Math.random() * 100000);
        const id = instrument.instrumentIdentifier;
        // document.title = 'FS Copilot Hook - ' + id;
        SimVar.SetSimVarValue('L:FSC_HOOK', 'number', hookId);

        const bus = new Bus();

        // FS Copilot: per page identity. The aircraft uses the very same instrument identifier
        // ("WasmInstrument") for every display panel, so the panel name has to be part of the
        // address, otherwise DU2/DU4/DU6/DU7/DU8 cannot be told apart nor targeted individually.
        const panel = (typeof globalPanelData !== 'undefined' && globalPanelData && globalPanelData.sName)
            ? String(globalPanelData.sName)
            : String(document.title || '').split(' - ')[0];

        // Wasm displays carry their gauge name in the instrument url, e.g.
        // html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html
        //     ?wasm_module=SimObjects\Airplanes\inibuilds-a380\common\panel\inibuilds-A380.wasm&wasm_gauge=RUD
        const url = String(instrument.getAttribute('Url') || '');
        const gauge = String((url.match(/wasm_gauge=([^&]+)/i) || [])[1] || '');
        const module = String((url.match(/wasm_module=([^&]+)/i) || [])[1] || '').split(/[\\/]/).pop();
        const guid = String(instrument.getAttribute('Guid') || '');

        // Creation time: panel names (VCockpit53) are not stable across reloads or add-ons, so the
        // host app derives the display order from this timestamp instead (same clock for every page).
        const created = Date.now();

        // e.g. "VCockpit92:WasmInstrument:RUD"
        const screen = [panel, id, gauge].filter(p => !!p).join(':');

        // Accepts an exact screen address, a bare instrument identifier, the gauge name, or "*".
        const matches = (target) => !target || target === '*' || target === screen || target === id ||
            (!!gauge && target === gauge);

        // Screens whose pointer is shared session wide (pushed by the host app once the aircraft
        // profile is loaded): {"type":"screens","list":["VCockpit53", ...],"clicks":false}.
        const config = window.fscScreens || (window.fscScreens = {keys: [], clicks: false});

        const isShared = () => {
            if (!config.keys || !config.keys.length) return false;

            const address = screen.toLowerCase();
            for (let i = 0; i < config.keys.length; i++) {
                const key = String(config.keys[i] || '').toLowerCase();
                if (key && address.indexOf(key) >= 0) return true;
            }
            return false;
        };

        // The comm bus payload is carried through a fixed 512 byte WASM buffer (str_msg),
        // anything longer is dropped on the way to the host app.
        const send = (msg) => {
            if (JSON.stringify(msg).length < 500) {
                bus.send(msg);
                return;
            }

            msg.detail = String(msg.detail || '').slice(0, 120);
            msg.title = String(msg.title || '').slice(0, 24);
            if (JSON.stringify(msg).length < 500) {
                bus.send(msg);
                return;
            }

            msg.detail = String(msg.detail).slice(0, 64);
            msg.title = String(msg.title).slice(0, 12);
            bus.send(msg);
        };

        const interact = instrument.onInteractionEvent;
        instrument.onInteractionEvent = (_args) => {
            interact.call(instrument, _args);
            if (SimVar.GetSimVarValue('L:FSC_HOOK', 'number') != hookId) return;
            bus.send({type: 'hevent', name: _args[0]});
        }

        /* FS Copilot: pointer (mousemove/mousedown/mouseup) bridge for DU style screens.
           Unlike the original implementation this runs for non-interactive instruments too,
           because displays like e.g. WasmInstrument panels never set isInteractive. */
        const events = new HtmlEvents(instrument, screen);
        events.capturePointer = isShared();

        const pointer = new RemotePointer(instrument);

        events.addEventListener('emit', ev => bus.send({
            type: 'interact',
            instrument: screen,
            event: ev.type,
            id: ev.id,
            value: ev.value,
            x: ev.x,
            y: ev.y
        }));

        // Discovery mode: report what this instrument/page looks like, so the host app can
        // address the right screen (DU2, DU4, ...) and know its size/origin.
        const info = () => {
            const d = events.describe();
            const r = d.rect;

            // Fields the simulator exposes for this panel: helps to find a stable display id.
            const keys = (typeof globalPanelData !== 'undefined' && globalPanelData)
                ? Object.keys(globalPanelData).join(',')
                : '';

            const cfg = (typeof globalPanelData !== 'undefined' && globalPanelData && globalPanelData.sConfigFile)
                ? String(globalPanelData.sConfigFile).split('\\').pop()
                : '';

            send({
                type: 'screen',
                action: 'info',
                instrument: screen,
                title: String(document.title || ''),
                gauge: gauge,
                wasm: module,
                guid: guid,
                t: created,
                w: r[2],
                h: r[3],
                elements: d.elements,
                interactive: !!instrument.isInteractive,
                detail: ('page=' + d.page[0] + 'x' + d.page[1] + (cfg ? ' cfg=' + cfg : '')).slice(0, 120),
                keys: keys.slice(0, 96)
            });
        };

        // the panel is not laid out right after the instrument has been created
        setTimeout(info, 3000);

        // Hovering a display in the cockpit tells the host app which screen the pilot is pointing at
        // (the simulator dispatches mouseenter on the instrument, see OnMouseEnter in VCockpit.js):
        // that is how a display can be labelled without trusting the panel number.
        instrument.addEventListener('mouseenter', () => send({
            type: 'screen',
            action: 'focus',
            instrument: screen,
            title: String(document.title || ''),
            t: created
        }));

        bus.addEventListener('message', msg => {
            if (msg.type === 'screens') {
                config.keys = Array.isArray(msg.list) ? msg.list : [];
                config.clicks = msg.clicks === true;
                events.capturePointer = isShared();
                return;
            }

            if (msg.type === 'interact') {
                if (!matches(msg.instrument)) return;

                // Glass displays (a Wasm gauge) never accept a replayed click: the simulator
                // generates mouseup/click itself from the shared press. Guarded here as well so an
                // older peer build cannot inject them either.
                const glass = !!gauge;
                if (glass && !config.clicks && (msg.event === 'mouseup' || msg.event === 'click')) {
                    console.log(`[FsCopilot] [Hook] ${msg.event} ignored for glass display ${screen}`);
                    return;
                }

                const byPoint = typeof msg.x === 'number' && typeof msg.y === 'number' &&
                    (msg.event === 'mousemove' || msg.event === 'mousedown' || msg.event === 'mouseup' || msg.event === 'click');

                if (byPoint) {
                    pointer.update(msg.from || 'Co-pilot', msg.x, msg.y, msg.event);
                    events.dispatchAt(msg.event, msg.x, msg.y);
                }
                else events.dispatch(msg.event, msg.id, msg.value);
                return;
            }

            if (msg.type !== 'screen') return;
            if (!matches(msg.instrument)) return;

            switch (msg.action) {
                case 'info':
                    info();
                    break;

                case 'probe': {
                    const hit = events.hitTest(msg.x, msg.y);
                    send({
                        type: 'screen',
                        action: 'hit',
                        instrument: screen,
                        x: hit.x,
                        y: hit.y,
                        detail: hit.tag +
                            (hit.cls ? '.' + String(hit.cls).slice(0, 40) : '') +
                            ' px=' + hit.px + ',' + hit.py +
                            ' rect=' + hit.rect.join(',') +
                            (hit.text ? ' text="' + hit.text + '"' : '')
                    });
                    break;
                }

                case 'move':
                case 'click': {
                    const el = events.dispatchAt(msg.action === 'click' ? 'mouseup' : 'mousemove', msg.x, msg.y);
                    const hit = events.hitTest(msg.x, msg.y);
                    send({
                        type: 'screen',
                        action: msg.action === 'click' ? 'clicked' : 'moved',
                        instrument: screen,
                        x: hit.x,
                        y: hit.y,
                        detail: el.tagName + ' px=' + hit.px + ',' + hit.py + ' rect=' + hit.rect.join(',')
                    });
                    break;
                }
            }
        });
    }
}
