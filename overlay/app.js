(() => {
  // /live : one URL for OBS. The app tells us which overlay to show over the WebSocket and we rebuild the
  // page in place - the socket, the browser source and OBS all stay as they are, so switching in the app
  // shows up in OBS at once without a refresh.
  const live = location.pathname === '/live';
  const LOOKS = { classic: ['white', 'outline'], glass: ['white', 'glass'], dark: ['amber', 'solid'], neon: ['blue', 'neon'], paper: ['amber', 'paper'] };
  const themes = { amber: '#C99A3B', white: '#FFFFFF', green: '#4CBE5A', pink: '#FF5FA2', blue: '#4DA3FF', olive: '#8C9A5B' };

  function el(tag, cls) { const d = document.createElement(tag); if (cls) d.className = cls; return d; }
  const root = document.getElementById('root');
  const status = el('div', 'status'); document.body.appendChild(status);

  // ---------- keyboard layouts (vk = Windows virtual-key code; vks = any of) ----------
  const K = (l, vk, w, cls) => ({ l, vks: Array.isArray(vk) ? vk : [vk], w: w || 1, cls: cls || '' });
  const G = w => ({ gap: true, w });
  const SHIFT = [160, 161, 16], CTRL = [162, 163, 17], ALT = [164, 165, 18];
  const WASD = [
    [K('1', 49), K('2', 50), K('3', 51), K('4', 52), K('5', 53)],
    [K('Tab', 9, 1.5, 'sm'), K('Q', 81), K('W', 87), K('E', 69), K('R', 82), K('T', 84)],
    [K('Caps', 20, 1.75, 'sm'), K('A', 65), K('S', 83), K('D', 68), K('F', 70), K('G', 71)],
    [K('Shift', SHIFT, 2.25, 'sm'), K('Z', 90), K('X', 88), K('C', 67), K('V', 86), K('B', 66)],
    [K('Ctrl', CTRL, 1.25, 'sm'), K('Alt', ALT, 1.25, 'sm'), K('Space', 32, 4.75, 'sm')],
  ];
  const WASD_MINI = [
    [G(1), K('W', 87)],
    [K('A', 65), K('S', 83), K('D', 68)],
    [K('Ctrl', CTRL, 1, 'sm'), K('Shift', SHIFT, 1, 'sm'), K('Space', 32, 1, 'sm')],
  ];
  const FULL = [
    [K('Esc', 27, 1, 'sm'), G(1), K('F1', 112, 1, 'sm'), K('F2', 113, 1, 'sm'), K('F3', 114, 1, 'sm'), K('F4', 115, 1, 'sm'), G(.5), K('F5', 116, 1, 'sm'), K('F6', 117, 1, 'sm'), K('F7', 118, 1, 'sm'), K('F8', 119, 1, 'sm'), G(.5), K('F9', 120, 1, 'sm'), K('F10', 121, 1, 'sm'), K('F11', 122, 1, 'sm'), K('F12', 123, 1, 'sm')],
    [K('`', 192), K('1', 49), K('2', 50), K('3', 51), K('4', 52), K('5', 53), K('6', 54), K('7', 55), K('8', 56), K('9', 57), K('0', 48), K('-', 189), K('=', 187), K('Bksp', 8, 2, 'sm')],
    [K('Tab', 9, 1.5, 'sm'), K('Q', 81), K('W', 87), K('E', 69), K('R', 82), K('T', 84), K('Y', 89), K('U', 85), K('I', 73), K('O', 79), K('P', 80), K('[', 219), K(']', 221), K('\\', 220, 1.5)],
    [K('Caps', 20, 1.75, 'sm'), K('A', 65), K('S', 83), K('D', 68), K('F', 70), K('G', 71), K('H', 72), K('J', 74), K('K', 75), K('L', 76), K(';', 186), K("'", 222), K('Enter', 13, 2.25, 'sm')],
    [K('Shift', SHIFT, 2.25, 'sm'), K('Z', 90), K('X', 88), K('C', 67), K('V', 86), K('B', 66), K('N', 78), K('M', 77), K(',', 188), K('.', 190), K('/', 191), K('Shift', 161, 2.75, 'sm')],
    [K('Ctrl', 162, 1.25, 'sm'), K('Win', 91, 1.25, 'sm'), K('Alt', 164, 1.25, 'sm'), K('Space', 32, 6.25, 'sm'), K('Alt', 165, 1.25, 'sm'), K('Win', 92, 1.25, 'sm'), K('Menu', 93, 1.25, 'sm'), K('Ctrl', 163, 1.25, 'sm')],
  ];
  const ARROWS = [[G(1), K('▲', 38)], [K('◀', 37), K('▼', 40), K('▶', 39)]];
  const NUMPAD = [
    [K('Num', 144, 1, 'sm'), K('/', 111), K('*', 106), K('-', 109)],
    [K('7', 103), K('8', 104), K('9', 105), K('+', 107, 1, 'tall')],
    [K('4', 100), K('5', 101), K('6', 102), G(1)],
    [K('1', 97), K('2', 98), K('3', 99), K('Ent', 13, 1, 'sm tall')],
    [K('0', 96, 2), K('.', 110), G(1)],
  ];

  // key names for ?keys= (case-insensitive) and for the history strip
  const NAMES = { space: 32, shift: [160, 161, 16], lshift: 160, rshift: 161, ctrl: [162, 163, 17], lctrl: 162, rctrl: 163, alt: [164, 165, 18], lalt: 164, ralt: 165,
    tab: 9, enter: 13, return: 13, esc: 27, escape: 27, bksp: 8, backspace: 8, caps: 20, capslock: 20, win: [91, 92], menu: 93, del: 46, delete: 46, ins: 45, insert: 45,
    home: 36, end: 35, pgup: 33, pageup: 33, pgdn: 34, pagedown: 34, up: 38, down: 40, left: 37, right: 39, '`': 192, '-': 189, '=': 187, '[': 219, ']': 221, '\\': 220, ';': 186, "'": 222, ',': 188, '.': 190, '/': 191,
    num: 144, numlock: 144, 'num/': 111, 'num*': 106, 'num-': 109, 'num+': 107, 'num.': 110, lmb: 1, rmb: 2, mmb: 4, m4: 5, m5: 6, prtsc: 44, scroll: 145, pause: 19 };
  for (let i = 0; i < 26; i++) NAMES[String.fromCharCode(97 + i)] = 65 + i;
  for (let i = 0; i < 10; i++) { NAMES[String(i)] = 48 + i; NAMES['num' + i] = 96 + i; }
  for (let i = 1; i <= 24; i++) NAMES['f' + i] = 111 + i;
  const VKNAME = {};
  for (const [n, v] of Object.entries(NAMES)) for (const vk of [].concat(v)) if (!VKNAME[vk] || n.length < VKNAME[vk].length) VKNAME[vk] = n;
  const pretty = n => ({ up: '▲', down: '▼', left: '◀', right: '▶', space: 'Space', shift: 'Shift', ctrl: 'Ctrl', alt: 'Alt', enter: 'Enter', tab: 'Tab', esc: 'Esc', caps: 'Caps', bksp: 'Bksp', win: 'Win', del: 'Del', ins: 'Ins', home: 'Home', end: 'End', pgup: 'PgUp', pgdn: 'PgDn', lmb: 'LMB', rmb: 'RMB', mmb: 'MMB', m4: 'M4', m5: 'M5', num: 'Num' })[n] || (n.length === 1 ? n.toUpperCase() : n[0].toUpperCase() + n.slice(1));
  const keyName = vk => VKNAME[vk] ? pretty(VKNAME[vk]) : 'vk' + vk;
  function customLayout(spec) {
    return spec.split('|').map(row => row.split(',').map(t => t.trim()).filter(Boolean).map(t => {
      const [name, w] = t.split(':');
      const vk = NAMES[name.toLowerCase()];
      if (vk === undefined) return K(name, -1, parseFloat(w) || 1, 'sm');
      const label = pretty(name.toLowerCase());
      return K(label, vk, parseFloat(w) || (label.length > 2 ? 1.5 : 1), label.length > 2 ? 'sm' : '');
    }));
  }

  // Labels follow the pad: the capture side tags the state "xbox", "ps" or "hid"; ?pad=ps|xbox overrides.
  const LABELS = {
    xbox: { A: 'A', B: 'B', X: 'X', Y: 'Y', LB: 'LB', RB: 'RB', LT: 'LT', RT: 'RT', BACK: 'BACK', START: 'START', LS: 'LS', RS: 'RS', GUIDE: 'Guide' },
    ps: { A: '✕', B: '○', X: '□', Y: '△', LB: 'L1', RB: 'R1', LT: 'L2', RT: 'R2', BACK: 'CREATE', START: 'OPTIONS', LS: 'L3', RS: 'R3', GUIDE: 'PS' },
    ps4: { A: '✕', B: '○', X: '□', Y: '△', LB: 'L1', RB: 'R1', LT: 'L2', RT: 'R2', BACK: 'SHARE', START: 'OPTIONS', LS: 'L3', RS: 'R3', GUIDE: 'PS' },
  };
  const modelFor = kind => kind === 'ps4' ? 'dualshock4.glb' : kind === 'ps' ? 'dualsense.glb' : 'xbox.glb';   // ?model=auto: bundled model for the pad in use
  const PADBITS = [[0x1000, 'A'], [0x2000, 'B'], [0x4000, 'X'], [0x8000, 'Y'], [0x100, 'LB'], [0x200, 'RB'], [1, '▲'], [2, '▼'], [4, '◀'], [8, '▶'], [0x10, 'START'], [0x20, 'BACK'], [0x40, 'LS'], [0x80, 'RS'], [0x400, 'GUIDE']];
  const MBIT = { 1: 1, 2: 2, 4: 4, 5: 8, 6: 16 };   // VK_LBUTTON.. VK_XBUTTON2 -> bit in the mouse mask

  // ---------- per-overlay state: everything build() creates and apply() drives ----------
  let current = null;                       // the query string the page is built from
  let q, fit = false, overlay = 'none', theme = 'amber', style = 'solid', padForce = '', withHistory = false, histMax = 8, histTtl = 4000, accent = themes.amber;
  let keyEls = [], mouse = null, pad = null, pad3d = null, autoModel = false, hist = null, padKind = 'xbox', demoTimer = null;
  const L = () => LABELS[padKind] || LABELS.xbox;

  function buildKeyboard(layout) {
    const kb = el('div', 'kb');
    for (const row of layout) {
      const r = el('div', 'row');
      for (const k of row) {
        const d = el('div', 'key' + (k.gap ? ' gap' : '') + (k.cls ? ' ' + k.cls : ''));
        d.style.width = `calc(var(--u) * ${k.w} + var(--gap) * ${k.w - 1})`;
        if (!k.gap) { d.textContent = k.l; keyEls.push({ el: d, vks: k.vks }); }
        r.appendChild(d);
      }
      kb.appendChild(r);
    }
    return kb;
  }

  function buildMouse() {
    const m = el('div', 'mouse');
    m.innerHTML = `<div class="body"><div class="btn l"></div><div class="btn r"></div><div class="wheel"><div class="arr up">▲</div><div class="arr dn">▼</div></div>
      <div class="pad"><div class="dot"></div></div></div><div class="side x2"></div><div class="side x1"></div>`;
    mouse = { l: m.querySelector('.l'), r: m.querySelector('.r'), w: m.querySelector('.wheel'), up: m.querySelector('.up'), dn: m.querySelector('.dn'), x1: m.querySelector('.x1'), x2: m.querySelector('.x2'), dot: m.querySelector('.dot'), vx: 0, vy: 0, wheelUntil: 0, wheelDir: 0 };
    return m;
  }

  function buildPad() {
    const p = el('div', 'pad');
    const B = (cls, x, y, id) => `<div class="b ${cls}" style="left:${x}px;top:${y}px" data-b="${id}"></div>`;
    p.innerHTML = `<div class="shell"></div>
      ${B('bump', 60, 14, 'LB')}${B('bump', 270, 14, 'RB')}
      <div class="trig" style="left:73px;top:-8px"><i id="lt"></i><span data-t="LT"></span></div><div class="trig" style="left:283px;top:-8px"><i id="rt"></i><span data-t="RT"></span></div>
      <div class="stick" id="ls" style="left:48px;top:78px"><i></i></div>
      <div class="stick" id="rs" style="left:232px;top:150px"><i></i></div>
      ${B('dp', 116, 158, 'UP')}${B('dp', 88, 186, 'LEFT')}${B('dp', 144, 186, 'RIGHT')}${B('dp', 116, 214, 'DOWN')}
      ${B('face', 322, 72, 'Y')}${B('face', 286, 108, 'X')}${B('face', 358, 108, 'B')}${B('face', 322, 144, 'A')}
      ${B('small', 168, 104, 'BACK')}${B('small', 218, 104, 'START')}
      <div class="none" id="nopad">no controller</div>`;
    const g = s => p.querySelector(s);
    const by = t => p.querySelector(`[data-b="${t}"]`);
    pad = { el: p, LB: by('LB'), RB: by('RB'), lt: g('#lt'), rt: g('#rt'), ltl: p.querySelector('[data-t=LT]'), rtl: p.querySelector('[data-t=RT]'), ls: g('#ls'), rs: g('#rs'),
      up: by('UP'), left: by('LEFT'), right: by('RIGHT'), down: by('DOWN'), A: by('A'), B: by('B'), X: by('X'), Y: by('Y'),
      back: by('BACK'), start: by('START'), none: g('#nopad'), kind: '' };
    pad.up.textContent = '▲'; pad.down.textContent = '▼'; pad.left.textContent = '◀'; pad.right.textContent = '▶';
    relabelPad();
    return p;
  }
  function relabelPad() {
    if (!pad || pad.kind === padKind) return;
    pad.kind = padKind;
    const l = L();
    for (const k of ['A', 'B', 'X', 'Y', 'LB', 'RB']) pad[k].textContent = l[k];
    pad.back.textContent = l.BACK; pad.start.textContent = l.START; pad.ltl.textContent = l.LT; pad.rtl.textContent = l.RT;
    pad.el.classList.toggle('ps', padKind === 'ps' || padKind === 'ps4');
  }

  // fit=1 / live: scale the overlay to the browser source instead of a fixed scale
  function fitToWindow() {
    if (!fit || !root.firstElementChild) return;
    const w = root.offsetWidth, h = root.offsetHeight;
    if (!w || !h) return;
    root.style.transform = `scale(${Math.min(window.innerWidth / w, window.innerHeight / h)})`;
  }
  window.addEventListener('resize', fitToWindow);

  // ---------- build (and rebuild) the page from a query string ----------
  function build(search) {
    current = search;
    q = new URLSearchParams(search);
    // tear down whatever the previous build made
    if (demoTimer) { clearInterval(demoTimer); demoTimer = null; }
    if (pad3d && pad3d.dispose) { try { pad3d.dispose(); } catch (e) { } }
    pad3d = null; pad = null; mouse = null; hist = null; keyEls = []; autoModel = false;
    root.innerHTML = ''; root.className = ''; root.style.transform = '';
    for (const c of [...document.body.classList]) if (c.startsWith('s-')) document.body.classList.remove(c);
    document.documentElement.style.removeProperty('--pressed-text');

    fit = live || q.get('fit') === '1';
    overlay = q.get('overlay') || (live ? 'none' : 'wasd');
    // ready-made looks (?look=): theme + style in one word. Explicit theme=/style= still win.
    const look = LOOKS[q.get('look') || ''] || null;
    theme = q.get('theme') || (look ? look[0] : 'amber');
    const scale = parseFloat(q.get('scale') || '1');
    const plate = q.get('plate') === '1';
    style = q.get('style') || (look ? look[1] : theme === 'white' ? 'outline' : 'solid');   // solid | outline | glass | neon | paper
    padForce = q.get('pad') || '';                                          // ps | xbox: force button labels
    withHistory = q.get('history') === '1' || overlay === 'history';
    histMax = Math.max(1, Math.min(20, parseInt(q.get('hist') || '8')));
    histTtl = Math.max(500, parseFloat(q.get('histttl') || '4') * 1000);
    document.body.classList.add('s-' + style);
    accent = q.get('color') ? '#' + q.get('color').replace('#', '') : (themes[theme] || themes.amber);
    document.documentElement.style.setProperty('--accent', accent);
    if (theme === 'white') document.documentElement.style.setProperty('--pressed-text', '#0B0E10');
    padKind = padForce === 'ps' || padForce === 'ps4' ? padForce : (state.g && typeof state.g[7] === 'string' && (state.g[7] === 'ps' || state.g[7] === 'ps4') ? state.g[7] : 'xbox');

    root.style.transform = `scale(${scale})`;
    if (plate) root.classList.add('plate');
    const main = el('div', 'main');

    if (overlay === 'wasd') { main.appendChild(buildKeyboard(WASD)); main.appendChild(buildMouse()); }
    else if (overlay === 'wasd-mini') { main.appendChild(buildKeyboard(WASD_MINI)); }
    else if (overlay === 'arrows') { main.appendChild(buildKeyboard(ARROWS)); main.appendChild(buildMouse()); }
    else if (overlay === 'keyboard') { main.appendChild(buildKeyboard(FULL)); main.appendChild(buildKeyboard(ARROWS)); }
    else if (overlay === 'keyboard+mouse') { main.appendChild(buildKeyboard(FULL)); main.appendChild(buildKeyboard(ARROWS)); main.appendChild(buildMouse()); }
    else if (overlay === 'numpad') { main.appendChild(buildKeyboard(NUMPAD)); }
    else if (overlay === 'mouse') { main.appendChild(buildMouse()); }
    else if (overlay === 'custom') { main.appendChild(buildKeyboard(customLayout(q.get('keys') || 'W,A,S,D'))); if (q.get('mouse') === '1') main.appendChild(buildMouse()); }
    else if (overlay === 'controller') { main.appendChild(buildPad()); }
    else if (overlay === 'controller3d') {
      let ok = false;
      const m = q.get('model') || '';
      autoModel = m === 'auto';
      try { if (window.THREE && window.Pad3D) { pad3d = Pad3D.create(main, { accent, theme, style, size: parseInt(q.get('size') || '480'), spin: q.get('spin') === '1', mono: q.get('mono') === '1', model: autoModel ? modelFor(padKind) : m }); ok = !!pad3d; } } catch (e) { console.error(e); }
      if (!ok) main.appendChild(buildPad());   // no WebGL: fall back to the flat pad
    }
    else if (overlay === 'all') { main.appendChild(buildKeyboard(WASD)); main.appendChild(buildMouse()); main.appendChild(buildPad()); }
    if (main.childElementCount) root.appendChild(main);

    // history strip: one chip per press, newest on the right, fading out after histTtl
    if (withHistory) {
      hist = { el: el('div', 'hist'), items: [], prevKeys: new Set(), prevMb: 0, prevBt: 0, prevLt: false, prevRt: false };
      hist.el.style.setProperty('--n', histMax);
      root.appendChild(hist.el);
      root.classList.add('col');
    }

    status.textContent = live && overlay === 'none' ? 'waiting for the app…' : '';
    if (fit) { root.style.transform = 'scale(1)'; fitToWindow(); setTimeout(fitToWindow, 300); }
    if (live) { try { history.replaceState(null, '', '/live' + (search ? '?' + search : '')); } catch (e) { } }

    // demo mode (?demo=1): fake input so you can position the source in OBS
    if (q.get('demo') === '1') {
      let t = 0;
      const seq = [[87], [87, 65], [65], [83], [83, 68], [68], [32], [160, 87], [87], [], [38], [38, 39], [39], [], [104], [100], [102], [], [81], [69], [82], [70]];
      demoTimer = setInterval(() => {
        t++;
        const k = seq[Math.floor(t / 6) % seq.length];
        const mb = (t % 24 < 6 ? 1 : 0) | (t % 40 > 33 ? 2 : 0) | (t % 60 > 55 ? 8 : 0);
        const a = t / 20;
        const dx = Math.round(Math.cos(a) * 12), dy = Math.round(Math.sin(a) * 12);
        const w = t % 30 === 0 ? 1 : t % 30 === 15 ? -1 : 0;
        const bt = (t % 24 < 6 ? 0x1000 : 0) | (t % 48 > 40 ? 0x100 : 0) | (t % 36 > 30 ? 0x2 : 0) | (t % 90 > 84 ? 0x2000 : 0);
        const g = [bt, Math.round((Math.sin(a) + 1) * 127), t % 50 > 30 ? 255 : 0, Math.cos(a).toFixed(2), Math.sin(a).toFixed(2), 0, 0, padForce || (q.get('demo_pad') || 'xbox')];
        apply({ k, m: [mb, dx, dy, w], g });
      }, 50);
      status.textContent = '';
    } else if (state.k) apply(state);   // show the last known state straight away on the new overlay
  }

  function histPush(labels) {
    if (!labels.length) return;
    const chip = el('div', 'chip');
    chip.textContent = labels.join('+');
    hist.el.appendChild(chip);
    hist.items.push({ el: chip, t: performance.now() });
    while (hist.items.length > histMax) hist.items.shift().el.remove();
  }
  function histUpdate(s) {
    const labels = [];
    const down = new Set(s.k || []);
    const mods = [], plain = [];
    for (const vk of down) if (!hist.prevKeys.has(vk)) { if (vk === 16 || vk === 17 || vk === 18) continue; ([160, 161, 162, 163, 164, 165].includes(vk) ? mods : plain).push(keyName(vk)); }
    // a modifier pressed with a key in the same tick reads as one chord; held modifiers prefix new keys
    const held = [...down].filter(vk => [160, 161, 162, 163, 164, 165].includes(vk) && hist.prevKeys.has(vk)).map(keyName);
    if (plain.length) labels.push(...plain.map(p => [...held, ...mods, p].join('+'))); else labels.push(...mods);
    const mb = (s.m || [0])[0];
    for (const [bit, n] of [[1, 'LMB'], [2, 'RMB'], [4, 'MMB'], [8, 'M4'], [16, 'M5']]) if ((mb & bit) && !(hist.prevMb & bit)) labels.push(n);
    const w = (s.m || [0, 0, 0, 0])[3];
    if (w > 0) labels.push('Wheel ▲'); else if (w < 0) labels.push('Wheel ▼');
    const g = s.g, bt = g ? g[0] : 0, l = L();
    for (const [bit, n] of PADBITS) if ((bt & bit) && !(hist.prevBt & bit)) labels.push(l[n] || n);
    const lt = g ? g[1] > 128 : false, rt = g ? g[2] > 128 : false;
    if (lt && !hist.prevLt) labels.push(l.LT); if (rt && !hist.prevRt) labels.push(l.RT);
    hist.prevKeys = down; hist.prevMb = mb; hist.prevBt = bt; hist.prevLt = lt; hist.prevRt = rt;
    histPush(labels);
  }

  // ---------- state ----------
  let state = { k: [], m: [0, 0, 0, 0], g: 0 };
  function apply(s) {
    state = s;
    if (!padForce && s.g && typeof s.g[7] === 'string') { const k = s.g[7] === 'ps' || s.g[7] === 'ps4' ? s.g[7] : 'xbox'; if (k !== padKind) { padKind = k; relabelPad(); if (pad3d && autoModel) pad3d.setModel(modelFor(k)); } }
    const down = new Set(s.k || []);
    const mb = (s.m || [0])[0];
    for (const k of keyEls) k.el.classList.toggle('on', k.vks.some(v => v <= 6 ? !!(mb & MBIT[v]) : down.has(v)));   // vk 1..6 = mouse buttons, from the mouse mask
    if (mouse) {
      const [b, dx, dy, w] = s.m || [0, 0, 0, 0];
      mouse.l.classList.toggle('on', !!(b & 1)); mouse.r.classList.toggle('on', !!(b & 2));
      mouse.w.classList.toggle('on', !!(b & 4)); mouse.x1.classList.toggle('on', !!(b & 8)); mouse.x2.classList.toggle('on', !!(b & 16));
      mouse.vx = mouse.vx * 0.6 + dx * 0.4; mouse.vy = mouse.vy * 0.6 + dy * 0.4;
      if (w) { mouse.wheelDir = w > 0 ? 1 : -1; mouse.wheelUntil = performance.now() + 120; }
    }
    if (pad3d) { pad3d.update(s.g); if (!hist) status.textContent = s.g ? '' : 'no controller'; }
    if (pad) {
      const g = s.g;
      pad.none.style.display = g ? 'none' : 'block';
      const bt = g ? g[0] : 0;
      const on = (e, m) => e.classList.toggle('on', !!(bt & m));
      on(pad.up, 1); on(pad.down, 2); on(pad.left, 4); on(pad.right, 8); on(pad.start, 0x10); on(pad.back, 0x20);
      on(pad.ls, 0x40); on(pad.rs, 0x80); on(pad.LB, 0x100); on(pad.RB, 0x200);
      on(pad.A, 0x1000); on(pad.B, 0x2000); on(pad.X, 0x4000); on(pad.Y, 0x8000);
      pad.lt.style.width = (g ? g[1] / 255 * 100 : 0) + '%'; pad.rt.style.width = (g ? g[2] / 255 * 100 : 0) + '%';
      const dz = v => Math.abs(v) < 0.08 ? 0 : v;
      const sx = e => e.querySelector('i');
      sx(pad.ls).style.transform = g ? `translate(${dz(g[3]) * 18}px, ${-dz(g[4]) * 18}px)` : '';
      sx(pad.rs).style.transform = g ? `translate(${dz(g[5]) * 18}px, ${-dz(g[6]) * 18}px)` : '';
    }
    if (hist) histUpdate(s);
  }

  // smooth mouse dot + wheel flash + history fade on animation frames
  function frame() {
    if (mouse) {
      mouse.vx *= 0.85; mouse.vy *= 0.85;
      const max = 26, len = Math.hypot(mouse.vx, mouse.vy), k = len > 0 ? Math.min(len * 1.2, max) / len : 0;
      mouse.dot.style.transform = `translate(${mouse.vx * k}px, ${mouse.vy * k}px)`;
      const flash = performance.now() < mouse.wheelUntil;
      mouse.up.classList.toggle('on', flash && mouse.wheelDir > 0);
      mouse.dn.classList.toggle('on', flash && mouse.wheelDir < 0);
    }
    if (hist) {
      const now = performance.now();
      for (const it of hist.items) { const age = now - it.t; it.el.style.opacity = age < histTtl * 0.6 ? 1 : Math.max(0, 1 - (age - histTtl * 0.6) / (histTtl * 0.4)); }
      while (hist.items.length && now - hist.items[0].t > histTtl) hist.items.shift().el.remove();
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  // first build from the page's own URL (the /live page starts empty and waits for the app's config)
  build(location.search.slice(1));
  if (q.get('demo') === '1') return;

  // ---------- websocket ----------
  // One connection for the life of the page. The app pushes {"cfg": "<query>"} on connect and whenever the
  // selection changes; the live page rebuilds itself from it. Fixed-URL pages ignore cfg.
  let lastMsg = 0;
  function connect() {
    const ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws');
    ws.onmessage = e => {
      let d; try { d = JSON.parse(e.data); } catch { return; }
      if (typeof d.cfg === 'string') {
        if (live && d.cfg && d.cfg !== current) build(d.cfg);
        return;
      }
      lastMsg = performance.now(); apply(d);
    };
    ws.onclose = () => { status.textContent = 'reconnecting…'; setTimeout(connect, 1000); };
    ws.onopen = () => { status.textContent = ''; };
  }
  connect();
  setInterval(() => { if (performance.now() - lastMsg > 3000) status.textContent = 'waiting for input…'; else if (status.textContent === 'waiting for input…') status.textContent = ''; }, 1000);
})();
