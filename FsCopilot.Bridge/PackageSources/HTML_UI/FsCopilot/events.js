const nativeInputSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;

class HtmlEvents extends Emitter {
    /**
     * @param {HTMLElement} root instrument root element; screen coordinates are relative to it
     * @param {string} instrument instrument identifier (used to namespace element ids)
     */
    constructor(root, instrument) {
        super();

        this._root = root || document.body;
        this._instrument = instrument || '';

        this._id2El = new Map();
        this._el2Id = new Map();

        this._watched = {
            input: new WeakSet(),
            keypress: new WeakSet(),
            keydown: new WeakSet()
        };

        this._lastMove = 0;
        this._moveIntervalMs = 40; // ~25 Hz is enough for a remote pointer

        // Mouse moves are only worth sharing for the screens listed by the host app (a session wide
        // cursor); clicks/inputs are always captured.
        this.capturePointer = false;

        this._onMouseDown = (ev) => {
            if (ev.selfEmit || ev.button !== 0) return;
            if (!this._owns(ev.target)) return;
            this.dispatchEvent('emit', this._event('mousedown', ev));
        };
        this._onMouse = (ev) => {
            if (ev.selfEmit || ev.button !== 0) return;
            if (!this._owns(ev.target)) return;
            this.dispatchEvent('emit', this._event('mouseup', ev));
        };
        this._onMouseMove = (ev) => {
            if (ev.selfEmit || !this.capturePointer) return;
            if (!this._owns(ev.target)) return;

            const now = Date.now();
            if (now - this._lastMove < this._moveIntervalMs) return;
            this._lastMove = now;
            this.dispatchEvent('emit', this._event('mousemove', ev));
        };
        this._onInput = (ev) => {
            if (ev.selfEmit) return;
            this.dispatchEvent('emit', {type: 'input', id: this._el2Id.get(ev.target), value: ev.target.value});
        }
        this._onKeypress = (ev) => {
            console.log('keypress', ev.keyCode);
            if (ev.selfEmit) return;
            this.dispatchEvent('emit', {type: 'keypress', id: this._el2Id.get(ev.target), value: ev.keyCode});
        };
        this._onKeydown = (ev) => {
            if (ev.selfEmit) return;
            this.dispatchEvent('emit', {type: 'keydown', id: this._el2Id.get(ev.target), value: ev.keyCode});
        };

        document.addEventListener('mousedown', this._onMouseDown, false);
        document.addEventListener('mouseup', this._onMouse, false);
        document.addEventListener('mousemove', this._onMouseMove, false);
        document.querySelectorAll('*').forEach(el => this._initElement(el));

        new MutationObserver(muts => {
            muts.forEach(m => m.addedNodes.forEach(n => {
                if (n.nodeType === 1) {
                    this._initElement(n);
                    n.querySelectorAll('*').forEach(el => this._initElement(el));
                }
            }));
        }).observe(document.documentElement, {childList:true,subtree:true});
    }

    /**
     * Entrypoint used to replay events back into the DOM
     * (simulating user input on a specific element by id).
     *
     * @param {'mouseup'|'input'|'keypress'|'keydown'} type
     * @param {string} id
     * @param {string|number} [value]
     */
    dispatch(type, id, value) {
        const el = this._id2El.get(id);
        if (!el) return;
        let evt;
        switch (type) {
            case 'mouseup':
                ['mousedown', 'mouseup', 'click']
                    .forEach(evType => {
                        evt = new MouseEvent(evType, {bubbles: true, cancelable: true});
                        evt.selfEmit = true;
                        el.dispatchEvent(evt);
                    });
                break;
            case 'input':
                nativeInputSetter.call(el, value);
                evt = new InputEvent('input', {bubbles: true, data: value})
                evt.selfEmit = true;
                el.dispatchEvent(evt);
                break;
            case 'keypress':
                evt = new KeyboardEvent('keypress', {bubbles: true, keyCode: value});
                evt.selfEmit = true;
                el.dispatchEvent(evt);
                break;
            case 'keydown':
                evt = new KeyboardEvent('keydown', {bubbles: true, keyCode: value});
                evt.selfEmit = true;
                el.dispatchEvent(evt);
                break;
        }
    }

    /** True when the element belongs to the instrument this instance is attached to. */
    _owns(target) {
        if (!this._root) return false;
        if (this._root === target) return true;
        return !!target && typeof target.nodeType === 'number' && this._root.contains(target);
    }

    _event(type, ev) {
        const p = HtmlEvents.point(this._root, ev.clientX, ev.clientY);

        let el = ev.target;
        while (el && !this._el2Id.get(el)) el = el.parentNode;

        return {
            type,
            id: el ? this._el2Id.get(el) : undefined,
            x: p.x,
            y: p.y
        };
    }

    /** Normalized [0..1] page coordinates of a DOM event, relative to the screen root. */
    point(ev) {
        return HtmlEvents.point(this._root, ev.clientX, ev.clientY);
    }

    static point(root, clientX, clientY) {
        const r = (root || document.body).getBoundingClientRect();
        const w = r.width || 1;
        const h = r.height || 1;
        const x = (clientX - r.left) / w;
        const y = (clientY - r.top) / h;
        return {x: Math.min(Math.max(x, 0), 1), y: Math.min(Math.max(y, 0), 1)};
    }

    /** Resolves a normalized screen point into a client point plus the element hit at it. */
    elementAt(x, y) {
        const r = this._root.getBoundingClientRect();
        const cx = r.left + x * (r.width || 1);
        const cy = r.top + y * (r.height || 1);

        const el = document.elementFromPoint(cx, cy);
        return {el: el && this._owns(el) ? el : this._root, cx, cy};
    }

    /** Discovery helper: describes what is under a normalized screen point. */
    hitTest(x, y) {
        const {el, cx, cy} = this.elementAt(x, y);
        const r = el.getBoundingClientRect();

        let nearest = el;
        while (nearest && !this._el2Id.get(nearest)) nearest = nearest.parentNode;

        const cls = (el.getAttribute && el.getAttribute('class')) || '';
        const text = (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 40);

        return {
            x,
            y,
            px: Math.round(cx),
            py: Math.round(cy),
            tag: el.tagName,
            cls,
            text,
            id: nearest ? this._el2Id.get(nearest) : undefined,
            rect: [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)]
        };
    }

    /**
     * Entrypoint used to replay pointer events back into the DOM by normalized screen coordinates.
     * Required for screens that resolve hits by position (SVG/canvas style displays): the pointer has
     * to hover the hot spot first, so 'mouseup' always replays a mousemove before the click.
     *
     * @param {'mousemove'|'mousedown'|'mouseup'|'click'} type
     * @param {number} x normalized [0..1]
     * @param {number} y normalized [0..1]
     * @returns {HTMLElement} the element the event was dispatched to
     */
    dispatchAt(type, x, y) {
        const {el, cx, cy} = this.elementAt(x, y);

        const make = (evType, buttons) => {
            const evt = new MouseEvent(evType, {
                bubbles: true,
                cancelable: true,
                view: window,
                clientX: cx,
                clientY: cy,
                screenX: Math.round(cx),
                screenY: Math.round(cy),
                button: 0,
                buttons
            });
            evt.selfEmit = true;
            return evt;
        };

        switch (type) {
            case 'mousemove':
                el.dispatchEvent(make('mousemove', 0));
                break;
            case 'mousedown':
                // hover the hot spot first, then press: the simulator generates the mouseup itself
                el.dispatchEvent(make('mousemove', 0));
                el.dispatchEvent(make('mousedown', 1));
                break;
            default:
                el.dispatchEvent(make('mousemove', 0));
                el.dispatchEvent(make('mousedown', 1));
                el.dispatchEvent(make('mouseup', 0));
                el.dispatchEvent(make('click', 0));
                break;
        }

        return el;
    }

    /** Discovery helper: size of this screen and of the page hosting it. */
    describe() {
        const r = this._root.getBoundingClientRect();
        return {
            rect: [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)],
            page: [window.innerWidth, window.innerHeight],
            elements: document.querySelectorAll('*').length
        };
    }

    _initElement(el) {
        if (!(el instanceof HTMLElement)) return;
        this._assignId(el);
        this._attachListeners(el);
    }

    _assignId(el) {
        if (this._el2Id.has(el)) return;

        const pathParts = [];
        let node = el;
        while (node && node.nodeType === 1) {
            let index = 0;
            let sib = node;
            while ((sib = sib.previousElementSibling)) index++;
            pathParts.push(node.tagName + ':' + index);
            node = node.parentElement;
        }
        pathParts.reverse();

        // const attrs = el.getAttributeNames()
        //     .filter(name => name !== 'id')
        //     .sort()
        //     .map(name => `${name}=${el.getAttribute(name)}`)
        //     .join('|');

        // const str = pathParts.join('/') + '|' + attrs;
        const str = pathParts.join('/');
        let hash = 0;
        for (let i = 0; i < str.length; i++) {
            hash = ((hash << 5) - hash) + str.charCodeAt(i);
            hash |= 0;
        }

        let id = 'fsc_' + this._instrument.replace(/[^a-zA-Z0-9]/g, '') + '_' + Math.abs(hash);
        while (this._id2El.has(id)) {
            id += '1';
        }

        // el.id = id;
        this._el2Id.set(el, id);
        this._id2El.set(id, el);
    }

    _attachListeners(el) {
        const store = window.fscListeners;

        const has = (type) => {
            if (!store) return false;
            const set = store.get(el);
            return !!set && set.has(type);
        };

        const subscribe = (type, handler) => {
            const bucket = this._watched[type];
            if (!bucket || bucket.has(el)) return;
            bucket.add(el);
            el.addEventListener(type, handler, false);
        };

        if (el instanceof HTMLInputElement) {
            subscribe('input', this._onInput);
            return;
        }

        if (has('keypress')) subscribe('keypress', this._onKeypress);
        if (has('keydown')) subscribe('keydown', this._onKeydown);
    }
}
