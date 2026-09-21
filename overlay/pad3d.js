// 3D controller for the overlay. Two sources:
//   built-in : an Xbox-style pad built from primitives (no files needed)
//   model    : a glTF/GLB from the models folder (?model=name.glb), with an optional name.json mapping
// Exposes Pad3D.create(parent, opts) -> { update(g), canvas }
(() => {
  const T = window.THREE;
  const XB = { A: 0x1000, B: 0x2000, X: 0x4000, Y: 0x8000, LB: 0x100, RB: 0x200, UP: 1, DOWN: 2, LEFT: 4, RIGHT: 8, START: 0x10, BACK: 0x20, LS: 0x40, RS: 0x80, GUIDE: 0x400 };
  const dz = v => Math.abs(v) < 0.08 ? 0 : v;

  function create(parent, o) {
    if (!T) return null;
    const W = o.size, H = Math.round(o.size * 0.66);
    let renderer;
    try { renderer = new T.WebGLRenderer({ antialias: true, alpha: true, premultipliedAlpha: false }); } catch { return null; }
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setSize(W, H);
    renderer.setClearColor(0x000000, 0);
    renderer.outputEncoding = T.sRGBEncoding;
    renderer.domElement.className = 'pad3d';
    parent.appendChild(renderer.domElement);

    const scene = new T.Scene();
    const cam = new T.PerspectiveCamera(26, W / H, 0.1, 100);
    cam.position.set(0, 17, 34); cam.lookAt(0, -0.8, 0);
    scene.add(new T.HemisphereLight(0xffffff, 0x2a3138, 0.75));
    const sun = new T.DirectionalLight(0xffffff, 0.85); sun.position.set(8, 14, 12); scene.add(sun);
    const rim = new T.DirectionalLight(0xffffff, 0.35); rim.position.set(-8, 4, -6); scene.add(rim);

    const accent = new T.Color(o.accent);
    const pad = new T.Group(); scene.add(pad);
    pad.rotation.x = -0.72;                      // lie it down like a pad on a desk, seen from above-front

    let g = null, animate = () => { };
    const t0 = performance.now();
    function frame() {
      animate(g ? g[0] : 0, g);
      if (o.spin) pad.rotation.y = Math.sin((performance.now() - t0) / 2500) * 0.35;
      renderer.render(scene, cam);
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);

    let current = null, loading = 0;
    function setModel(name) {
      if (name === current) return;
      current = name;
      const gen = ++loading;
      animate = () => { };
      while (pad.children.length) pad.remove(pad.children[0]);
      pad.rotation.set(-0.72, 0, 0);
      if (!name) { animate = buildProcedural(); return; }
      loadModel(name).then(a => { if (gen === loading) animate = a; }).catch(err => { console.error('model failed, using built-in pad', err); if (gen === loading) { while (pad.children.length) pad.remove(pad.children[0]); animate = buildProcedural(); } });
    }
    setModel(o.model || '');

    return { update(state) { g = state; }, setModel, canvas: renderer.domElement };

    // =====================================================================================
    // built-in pad
    function buildProcedural() {
      const white = o.theme === 'white' || o.style === 'glass';
      const bodyCol = white ? 0xd6d6d6 : 0x23282d, trimCol = white ? 0xb0b0b0 : 0x3a4147, capCol = white ? 0xbdbdbd : 0x151a1e;
      const mat = (c, rough = 0.62) => new T.MeshStandardMaterial({ color: c, roughness: rough, metalness: 0.05 });

      const sh = new T.Shape();
      sh.moveTo(-9.6, 1.6);
      sh.bezierCurveTo(-6, 3.4, 6, 3.4, 9.6, 1.6);
      sh.bezierCurveTo(11.2, 0.6, 11.4, -3, 10.3, -6.4);
      sh.bezierCurveTo(9.6, -8.6, 7, -8.9, 6.1, -6.8);
      sh.bezierCurveTo(5.3, -4.8, 3.6, -3.2, 0, -3.2);
      sh.bezierCurveTo(-3.6, -3.2, -5.3, -4.8, -6.1, -6.8);
      sh.bezierCurveTo(-7, -8.9, -9.6, -8.6, -10.3, -6.4);
      sh.bezierCurveTo(-11.4, -3, -11.2, 0.6, -9.6, 1.6);
      const body = new T.Mesh(new T.ExtrudeGeometry(sh, { depth: 2.4, bevelEnabled: true, bevelThickness: 0.7, bevelSize: 0.7, bevelSegments: 6, curveSegments: 24 }), mat(bodyCol));
      body.name = 'body'; body.position.z = -1.2; pad.add(body);
      const TOP = 1.95;

      const parts = {};
      function cyl(name, r, h, c, x, y, z, seg = 40) { const m = new T.Mesh(new T.CylinderGeometry(r, r, h, seg), mat(c)); m.name = name; m.rotation.x = Math.PI / 2; m.position.set(x, y, z); pad.add(m); return m; }
      function box(name, w, h, d, c, x, y, z) { const m = new T.Mesh(new T.BoxGeometry(w, h, d), mat(c)); m.name = name; m.position.set(x, y, z); pad.add(m); return m; }

      const faceCol = o.mono ? { A: trimCol, B: trimCol, X: trimCol, Y: trimCol } : { A: 0x2fb457, B: 0xd8433a, X: 0x2f7fd8, Y: 0xe3b62f };
      const fc = [6.4, 0.6];
      for (const [n, dx, dy] of [['A', 0, -1.55], ['B', 1.55, 0], ['X', -1.55, 0], ['Y', 0, 1.55]]) {
        const b = cyl('btn_' + n, 0.72, 0.55, faceCol[n], fc[0] + dx, fc[1] + dy, TOP + 0.2);
        b.userData = { z: TOP + 0.2, base: new T.Color(faceCol[n]) }; parts[n] = b;
      }
      function stick(name, x, y) {
        const gr = new T.Group(); gr.name = name; gr.position.set(x, y, TOP); pad.add(gr);
        const ring = new T.Mesh(new T.TorusGeometry(1.45, 0.14, 12, 48), mat(white ? 0xffffff : trimCol)); ring.position.z = 0.12; gr.add(ring);
        const well = new T.Mesh(new T.CylinderGeometry(1.3, 1.3, 0.3, 40), mat(white ? 0x9c9c9c : 0x0e1215)); well.rotation.x = Math.PI / 2; well.position.z = 0.02; gr.add(well);
        const shaft = new T.Mesh(new T.CylinderGeometry(0.42, 0.55, 1.3, 24), mat(capCol)); shaft.rotation.x = Math.PI / 2; shaft.position.z = 0.65; gr.add(shaft);
        const cap = new T.Mesh(new T.CylinderGeometry(1.05, 0.95, 0.45, 40), mat(capCol, 0.7)); cap.rotation.x = Math.PI / 2; cap.position.z = 1.4; gr.add(cap);
        const dish = new T.Mesh(new T.CylinderGeometry(0.8, 0.8, 0.12, 40), mat(white ? 0x9a9a9a : 0x0b0e10)); dish.rotation.x = Math.PI / 2; dish.position.z = 1.62; gr.add(dish);
        gr.userData = { cap };
        return gr;
      }
      parts.LS = stick('stick_L', -6.3, 0.6); parts.RS = stick('stick_R', 3.4, -3.4);
      const dp = new T.Group(); dp.name = 'dpad'; dp.position.set(-3.4, -3.4, TOP + 0.15); pad.add(dp);
      const dh = new T.Mesh(new T.BoxGeometry(2.6, 0.85, 0.35), mat(capCol)); dp.add(dh);
      const dv = new T.Mesh(new T.BoxGeometry(0.85, 2.6, 0.35), mat(capCol)); dp.add(dv);
      parts.BACK = cyl('btn_back', 0.42, 0.3, trimCol, -1.6, 0.6, TOP + 0.05); parts.BACK.userData = { z: TOP + 0.05, base: new T.Color(trimCol) };
      parts.START = cyl('btn_start', 0.42, 0.3, trimCol, 1.6, 0.6, TOP + 0.05); parts.START.userData = { z: TOP + 0.05, base: new T.Color(trimCol) };
      const guide = cyl('btn_guide', 0.75, 0.3, white ? 0xffffff : 0x3a4147, 0, 2.15, TOP + 0.05); guide.material.emissive = accent.clone().multiplyScalar(0.35);
      parts.LB = box('btn_LB', 3.8, 0.9, 1.1, trimCol, -6.2, 3.0, 0.9); parts.LB.userData = { z: 0.9, base: new T.Color(trimCol) };
      parts.RB = box('btn_RB', 3.8, 0.9, 1.1, trimCol, 6.2, 3.0, 0.9); parts.RB.userData = { z: 0.9, base: new T.Color(trimCol) };
      function trigger(name, x) {
        const gr = new T.Group(); gr.name = name; gr.position.set(x, 3.2, -0.4); pad.add(gr);
        const m = new T.Mesh(new T.BoxGeometry(2.6, 1.4, 1.6), mat(capCol)); m.position.set(0, 0.9, -0.6); gr.add(m);
        return gr;
      }
      parts.LT = trigger('trigger_L', -6.2); parts.RT = trigger('trigger_R', 6.2);

      function press(mesh, on) {
        mesh.position.z = mesh.userData.z - (on ? 0.28 : 0);
        mesh.material.emissive = on ? accent.clone().multiplyScalar(0.9) : new T.Color(0x000000);
        mesh.material.color = on && white ? accent.clone() : mesh.userData.base.clone();
      }
      return (bt, g) => {
        for (const n of ['A', 'B', 'X', 'Y', 'LB', 'RB', 'BACK', 'START']) press(parts[n], !!(bt & XB[n]));
        const lx = g ? dz(+g[3]) : 0, ly = g ? dz(+g[4]) : 0, rx = g ? dz(+g[5]) : 0, ry = g ? dz(+g[6]) : 0;
        parts.LS.rotation.set(-ly * 0.45, lx * 0.45, 0); parts.RS.rotation.set(-ry * 0.45, rx * 0.45, 0);
        for (const [s, on] of [[parts.LS, bt & XB.LS], [parts.RS, bt & XB.RS]]) {
          s.position.z = TOP - (on ? 0.25 : 0);
          s.userData.cap.material.emissive = on ? accent.clone().multiplyScalar(0.8) : new T.Color(0);
        }
        const lt = g ? g[1] / 255 : 0, rt = g ? g[2] / 255 : 0;
        parts.LT.rotation.x = lt * 0.7; parts.RT.rotation.x = rt * 0.7;
        parts.LT.children[0].material.emissive = accent.clone().multiplyScalar(lt * 0.8);
        parts.RT.children[0].material.emissive = accent.clone().multiplyScalar(rt * 0.8);
        const dx = (bt & XB.RIGHT ? 1 : 0) - (bt & XB.LEFT ? 1 : 0), dy = (bt & XB.UP ? 1 : 0) - (bt & XB.DOWN ? 1 : 0);
        dp.rotation.set(-dy * 0.22, dx * 0.22, 0);
        dh.material.emissive = dv.material.emissive = (dx || dy) ? accent.clone().multiplyScalar(0.8) : new T.Color(0);
      };
    }

    // =====================================================================================
    // glTF model + mapping json (all keys optional). Two ways to find the moving parts:
    //   nodes  : the model has a node per button -> "nodes": { "A": "Cross", ... } (or the name heuristics below)
    //   parts  : the model is one welded mesh, or split by material only (every Sketchfab download so far) ->
    //            "parts": { "A": { "box": [x0,y0,z0, x1,y1,z1] } | { "disc": [cx,cy, r, z0,z1] }, ... }
    //            in normalised "pad space" (0..1 of the model's bounding box; x = right, y = far edge, z = up),
    //            "select": "shell" picks whole connected shells whose centre is inside, "tri" cuts triangles out.
    // "axes": { "right": "-x", "up": "-z", "far": "y" } says which model axes are which. "width" = fit width in
    // scene units, "tilt" = rest tilt, "press" = press depth as a fraction of model height, "stickTilt", "triggerPull".
    async function loadModel(name) {
      const base = '/models/' + encodeURIComponent(name);
      let map = {};
      try { const r = await fetch(base.replace(/\.gl(b|tf)$/i, '') + '.json'); if (r.ok) map = await r.json(); } catch { }
      const gltf = await new Promise((res, rej) => new T.GLTFLoader().load(base, res, undefined, rej));
      if (map.credit) console.log('model credit:', map.credit);
      if (map.parts) return buildFromParts(gltf, map);
      const named = buildFromNodes(gltf, map);
      return named;
    }

    // ---- part helpers shared by both paths --------------------------------------------------------------------
    function materialsOf(obj) { const ms = []; obj.traverse(x => { if (x.isMesh) ms.push(...[].concat(x.material)); }); return ms; }
    function ownMaterials(obj) {
      obj.traverse(n => {
        if (!n.isMesh || !n.material) return;
        n.material = Array.isArray(n.material) ? n.material.map(m => m.clone()) : n.material.clone();
        for (const m of [].concat(n.material)) m.userData.rest = { emissive: m.emissive ? m.emissive.clone() : null, emissiveMap: m.emissiveMap || null, emissiveIntensity: m.emissiveIntensity ?? 1, color: m.color ? m.color.clone() : null };
      });
    }
    // glow: emissive colour without the model's own emissive texture (which would mask it), restored on release
    function setGlow(obj, k) {
      for (const m of materialsOf(obj)) {
        if (!m.emissive || !m.userData.rest) continue;
        const r = m.userData.rest;
        if (k > 0) {
          m.emissiveMap = null; m.emissiveIntensity = 1;
          if (!m.map && r.color) { m.color = accent.clone(); m.emissive = accent.clone().multiplyScalar(0.35 * k); }   // untextured (plain white) parts: paint them, light glow
          else m.emissive = accent.clone().multiplyScalar(k);
        }
        else { m.emissive = r.emissive ? r.emissive.clone() : new T.Color(0); m.emissiveMap = r.emissiveMap; m.emissiveIntensity = r.emissiveIntensity; if (r.color) m.color = r.color.clone(); }
        m.needsUpdate = true;
      }
    }

    // ---- path 1: welded / material-split meshes, parts picked by position --------------------------------------
    function buildFromParts(gltf, map) {
      const src = gltf.scene; src.updateMatrixWorld(true);
      const ax = spec => { const v = new T.Vector3(); const c = spec.slice(-1); v['xyz'.includes(c) ? c : 'x'] = spec[0] === '-' ? -1 : 1; return v; };
      const axes = Object.assign({ right: 'x', up: 'y', far: '-z' }, map.axes || {});
      const R = ax(axes.right), F = ax(axes.far), U = ax(axes.up);
      const basis = new T.Matrix4().set(R.x, R.y, R.z, 0, F.x, F.y, F.z, 0, U.x, U.y, U.z, 0, 0, 0, 0, 1);   // model -> pad space

      // 1. flatten every primitive into pad space
      const prims = [];
      src.traverse(n => {
        if (!n.isMesh || !n.geometry) return;
        let geo = n.geometry.clone();
        if (!geo.index) { const idx = new Uint32Array(geo.attributes.position.count); for (let i = 0; i < idx.length; i++) idx[i] = i; geo.setIndex(new T.BufferAttribute(idx, 1)); }
        geo.applyMatrix4(new T.Matrix4().multiplyMatrices(basis, n.matrixWorld));
        prims.push({ geo, mat: [].concat(n.material)[0] });
      });
      const bbox = new T.Box3();
      for (const p of prims) { p.geo.computeBoundingBox(); bbox.union(p.geo.boundingBox); }
      const size = bbox.getSize(new T.Vector3()), min = bbox.min.clone();
      const totalTris = prims.reduce((a, p) => a + p.geo.index.count / 3, 0);
      const nx = v => (v - min.x) / size.x, ny = v => (v - min.y) / size.y, nz = v => (v - min.z) / size.z;

      // 2. selectors in normalised pad space
      const inside = (sel, x, y, z) => {
        if (sel.box) { const b = sel.box; return x >= b[0] && x <= b[3] && y >= b[1] && y <= b[4] && z >= b[2] && z <= b[5]; }
        if (sel.disc) { const [cx, cy, r, z0, z1] = sel.disc; return z >= z0 && z <= z1 && Math.hypot(x - cx, (y - cy) * size.y / size.x) <= r; }   // r in units of model width
        return false;
      };
      const partNames = Object.keys(map.parts);

      // 3. per primitive: connected shells (union-find over shared / coincident vertices), then assign triangles
      const partTris = {}; for (const k of partNames) partTris[k] = [];   // [{prim, tris:[i...]}]
      for (const p of prims) {
        const pos = p.geo.attributes.position.array, idx = p.geo.index.array, nv = pos.length / 3, nt = idx.length / 3;
        const parent = new Int32Array(nv); for (let i = 0; i < nv; i++) parent[i] = i;
        const find = i => { while (parent[i] !== i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
        const union = (a, b) => { a = find(a); b = find(b); if (a !== b) parent[a] = b; };
        const res = Math.max(size.x, size.y, size.z) * 1e-5, seen = new Map();
        for (let i = 0; i < nv; i++) {
          const key = Math.round(pos[i * 3] / res) + ',' + Math.round(pos[i * 3 + 1] / res) + ',' + Math.round(pos[i * 3 + 2] / res);
          const j = seen.get(key); if (j === undefined) seen.set(key, i); else union(i, j);
        }
        for (let t = 0; t < nt; t++) { union(idx[t * 3], idx[t * 3 + 1]); union(idx[t * 3 + 2], idx[t * 3]); }
        const shell = new Map();   // root -> { n, cx, cy, cz }
        const triRoot = new Int32Array(nt);
        for (let t = 0; t < nt; t++) {
          const r = find(idx[t * 3]); triRoot[t] = r;
          let s = shell.get(r); if (!s) { s = { n: 0, cx: 0, cy: 0, cz: 0 }; shell.set(r, s); }
          for (let k = 0; k < 3; k++) { const v = idx[t * 3 + k] * 3; s.cx += pos[v]; s.cy += pos[v + 1]; s.cz += pos[v + 2]; }
          s.n++;
        }
        const shellPart = new Map();
        for (const [r, s] of shell) {
          if (s.n > totalTris * 0.2) continue;   // the body is never a button
          const x = nx(s.cx / (3 * s.n)), y = ny(s.cy / (3 * s.n)), z = nz(s.cz / (3 * s.n));
          for (const k of partNames) { const sel = map.parts[k]; if ((sel.select || map.select || 'shell') === 'shell' && inside(sel, x, y, z)) { shellPart.set(r, k); break; } }
        }
        const owner = new Array(nt).fill(null);
        for (let t = 0; t < nt; t++) {
          const byShell = shellPart.get(triRoot[t]);
          if (byShell) { owner[t] = byShell; continue; }
          let cx = 0, cy = 0, cz = 0;
          for (let k = 0; k < 3; k++) { const v = idx[t * 3 + k] * 3; cx += pos[v]; cy += pos[v + 1]; cz += pos[v + 2]; }
          const x = nx(cx / 3), y = ny(cy / 3), z = nz(cz / 3);
          for (const k of partNames) { const sel = map.parts[k]; if ((sel.select || map.select || 'shell') === 'tri' && inside(sel, x, y, z)) { owner[t] = k; break; } }
        }
        const rest = [];
        for (let t = 0; t < nt; t++) { if (owner[t]) partTris[owner[t]].push({ prim: p, t }); else rest.push(t); }
        p.rest = rest;
      }

      // 4. build meshes. holder scales + centres the pad-space model; parts are groups at their pivot
      const holder = new T.Group();
      const s = (map.width || 22) / size.x;
      const centre = bbox.getCenter(new T.Vector3());
      holder.scale.setScalar(s); holder.position.copy(centre).multiplyScalar(-s);
      pad.add(holder);
      pad.rotation.x = map.tilt ?? -0.72;

      // non-indexed copy of a triangle subset, every attribute kept (colour, uv2, tangents...), positions re-based to the pivot
      const subGeometry = (prim, tris, offset) => {
        const g = prim.geo, idx = g.index.array, out = new T.BufferGeometry();
        for (const [name, attr] of Object.entries(g.attributes)) {
          const n = attr.itemSize, src = attr.array, dst = new Float32Array(tris.length * 3 * n);
          let o = 0;
          for (const t of tris) for (let k = 0; k < 3; k++) { const v = idx[t * 3 + k] * n; for (let q = 0; q < n; q++) dst[o + q] = src[v + q]; o += n; }
          if (name === 'position') for (let i = 0; i < dst.length; i += 3) { dst[i] -= offset.x; dst[i + 1] -= offset.y; dst[i + 2] -= offset.z; }
          out.setAttribute(name, new T.BufferAttribute(dst, n, attr.normalized));
        }
        if (!g.attributes.normal) out.computeVertexNormals();
        return out;
      };
      for (const p of prims) { const m = new T.Mesh(subGeometry(p, p.rest, new T.Vector3()), p.mat); m.name = 'body'; holder.add(m); }

      const parts = {};
      for (const k of partNames) {
        const list = partTris[k]; if (!list.length) continue;
        const byPrim = new Map(); for (const { prim, t } of list) { if (!byPrim.has(prim)) byPrim.set(prim, []); byPrim.get(prim).push(t); }
        // pivot from the part's bounding box: centre, or bottom (sticks), or top (triggers)
        const pb = new T.Box3();
        for (const [prim, tris] of byPrim) { const pa = prim.geo.attributes.position.array, idx = prim.geo.index.array; for (const t of tris) for (let q = 0; q < 3; q++) { const v = idx[t * 3 + q] * 3; pb.expandByPoint(new T.Vector3(pa[v], pa[v + 1], pa[v + 2])); } }
        const pivotKind = map.parts[k].pivot || (k === 'LS' || k === 'RS' ? 'bottom' : k === 'LT' || k === 'RT' ? 'top' : 'center');
        const pivot = pb.getCenter(new T.Vector3());
        if (pivotKind === 'bottom') pivot.z = pb.min.z; else if (pivotKind === 'top') { pivot.z = pb.max.z; pivot.y = pb.max.y; }
        const grp = new T.Group(); grp.name = 'part_' + k; grp.position.copy(pivot); grp.userData.pivot = pivot.clone();
        for (const [prim, tris] of byPrim) grp.add(new T.Mesh(subGeometry(prim, tris, pivot), prim.mat));
        ownMaterials(grp);
        holder.add(grp); parts[k] = grp;
        console.log(`part ${k}: ${list.length} tris, pivot`, [nx(pivot.x), ny(pivot.y), nz(pivot.z)].map(v => v.toFixed(3)).join(', '));
      }
      console.log('mapped parts:', Object.keys(parts).join(', ') || 'none', '| missing:', partNames.filter(k => !parts[k]).join(', ') || 'none');

      const pressAmt = (map.press ?? 0.015) * size.z, tiltAmt = map.stickTilt ?? 0.3, pull = map.triggerPull ?? 0.45;
      const press = (k, on) => { const g = parts[k]; if (!g) return; g.position.copy(g.userData.pivot); g.position.z -= on ? pressAmt : 0; setGlow(g, on ? 0.9 : 0); };
      return (bt, g) => {
        for (const k of ['A', 'B', 'X', 'Y', 'LB', 'RB', 'START', 'BACK', 'GUIDE', 'UP', 'DOWN', 'LEFT', 'RIGHT']) press(k, !!(bt & XB[k]));
        const lx = g ? dz(+g[3]) : 0, ly = g ? dz(+g[4]) : 0, rx = g ? dz(+g[5]) : 0, ry = g ? dz(+g[6]) : 0;
        for (const [k, x, y, m] of [['LS', lx, ly, XB.LS], ['RS', rx, ry, XB.RS]]) {
          const n = parts[k]; if (!n) continue;
          n.rotation.set(-y * tiltAmt, x * tiltAmt, 0);              // pad space: lean the top toward the push
          n.position.copy(n.userData.pivot); n.position.z -= (bt & m) ? pressAmt : 0;
          setGlow(n, (bt & m) ? 0.8 : 0);
        }
        const lt = g ? g[1] / 255 : 0, rt = g ? g[2] / 255 : 0;
        for (const [k, v] of [['LT', lt], ['RT', rt]]) { const n = parts[k]; if (!n) continue; n.rotation.x = -v * pull; setGlow(n, v * 0.8); }
        if (parts.DPAD) {
          const dx = (bt & XB.RIGHT ? 1 : 0) - (bt & XB.LEFT ? 1 : 0), dy = (bt & XB.UP ? 1 : 0) - (bt & XB.DOWN ? 1 : 0);
          parts.DPAD.rotation.set(-dy * 0.15, dx * 0.15, 0);
          setGlow(parts.DPAD, (dx || dy) ? 0.8 : 0);
        }
      };
    }

    // ---- path 2: a node per button ------------------------------------------------------------------------------
    function buildFromNodes(gltf, map) {
      const model = gltf.scene;

      // fit: centre and scale to a known width, then optional rotation from the mapping
      if (map.rotation) model.rotation.set(...map.rotation.map(d => d * Math.PI / 180));
      model.updateMatrixWorld(true);
      let box = new T.Box3().setFromObject(model);
      const size = box.getSize(new T.Vector3());
      const s = (map.width || 22) / Math.max(size.x, 1e-6);
      model.scale.setScalar(s);
      model.updateMatrixWorld(true);
      box = new T.Box3().setFromObject(model);
      const c = box.getCenter(new T.Vector3());
      model.position.sub(c);
      // built-in pad lies in XY with +Z up; most controller models are Y-up. Tip the model onto the same plane.
      const holder = new T.Group(); holder.add(model);
      if (map.yUp !== false) holder.rotation.x = Math.PI / 2;
      pad.add(holder);
      pad.rotation.x = map.tilt ?? -0.72;
      ownMaterials(model);

      // resolve nodes
      const all = []; model.traverse(n => { if (n.name) all.push(n); });
      const find = key => {
        const wanted = map.nodes && map.nodes[key];
        const cands = wanted ? [].concat(wanted).map(w => w.toLowerCase()) : heuristics[key];
        if (!cands) return null;
        for (const cand of cands) { const hit = all.find(n => n.name.toLowerCase() === cand); if (hit) return hit; }
        if (!wanted) for (const cand of cands) { const hit = all.find(n => tokens(n.name).includes(cand)); if (hit) return hit; }
        return null;
      };
      const parts = {};
      for (const k of ['A', 'B', 'X', 'Y', 'LB', 'RB', 'LT', 'RT', 'LS', 'RS', 'UP', 'DOWN', 'LEFT', 'RIGHT', 'DPAD', 'START', 'BACK', 'GUIDE']) {
        const n = find(k); if (n) parts[k] = n;
      }
      console.log('model nodes:', all.map(n => n.name).join(', '));
      console.log('mapped:', Object.fromEntries(Object.entries(parts).map(([k, v]) => [k, v.name])));
      if (Object.keys(parts).length === 0) console.warn('no button nodes found - the model will show but nothing will move. Add a name.json with "parts" (see README).');

      // remember rest pose
      const rest = new Map();
      for (const n of Object.values(parts)) rest.set(n, { p: n.position.clone(), r: n.rotation.clone() });
      const pressDir = new T.Vector3(...(map.pressDir || [0, -1, 0])).normalize();
      const pressAmt = (map.press || 0.03) * (size.y * s || 1) / s;   // in model-local units
      const tiltAmt = map.stickTilt ?? 0.35, pull = map.triggerPull ?? 0.5;
      const press = (n, on) => { if (!n) return; const r = rest.get(n); n.position.copy(r.p).addScaledVector(pressDir, on ? pressAmt : 0); setGlow(n, on ? 0.9 : 0); };

      return (bt, g) => {
        for (const k of ['A', 'B', 'X', 'Y', 'LB', 'RB', 'START', 'BACK', 'GUIDE']) press(parts[k], !!(bt & XB[k]));
        const lx = g ? dz(+g[3]) : 0, ly = g ? dz(+g[4]) : 0, rx = g ? dz(+g[5]) : 0, ry = g ? dz(+g[6]) : 0;
        for (const [k, x, y, m] of [['LS', lx, ly, XB.LS], ['RS', rx, ry, XB.RS]]) {
          const n = parts[k]; if (!n) continue; const r = rest.get(n);
          n.rotation.set(r.r.x + y * tiltAmt, r.r.y, r.r.z - x * tiltAmt);   // Y-up model: tilt about X (fwd/back) and Z (left/right)
          n.position.copy(r.p).addScaledVector(pressDir, (bt & m) ? pressAmt : 0);
          setGlow(n, (bt & m) ? 0.8 : 0);
        }
        const lt = g ? g[1] / 255 : 0, rt = g ? g[2] / 255 : 0;
        for (const [k, v] of [['LT', lt], ['RT', rt]]) { const n = parts[k]; if (!n) continue; const r = rest.get(n); n.rotation.x = r.r.x - v * pull; setGlow(n, v * 0.8); }
        const dirs = [['UP', XB.UP], ['DOWN', XB.DOWN], ['LEFT', XB.LEFT], ['RIGHT', XB.RIGHT]];
        let any = false;
        for (const [k, m] of dirs) { if (parts[k]) press(parts[k], !!(bt & m)); if (bt & m) any = true; }
        if (parts.DPAD) {
          const r = rest.get(parts.DPAD);
          const dx = (bt & XB.RIGHT ? 1 : 0) - (bt & XB.LEFT ? 1 : 0), dy = (bt & XB.UP ? 1 : 0) - (bt & XB.DOWN ? 1 : 0);
          parts.DPAD.rotation.set(r.r.x - dy * 0.18, r.r.y, r.r.z - dx * 0.18);
          setGlow(parts.DPAD, any ? 0.8 : 0);
        }
      };
    }

    function tokens(name) { return name.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean); }
  }

  // node-name guesses when no mapping json is present (lowercase; exact name first, then token match)
  const heuristics = {
    A: ['btn_a', 'button_a', 'cross', 'a'], B: ['btn_b', 'button_b', 'circle', 'b'], X: ['btn_x', 'button_x', 'square', 'x'], Y: ['btn_y', 'button_y', 'triangle', 'y'],
    LB: ['btn_lb', 'lb', 'l1', 'bumper_l', 'leftbumper'], RB: ['btn_rb', 'rb', 'r1', 'bumper_r', 'rightbumper'],
    LT: ['trigger_l', 'lt', 'l2', 'lefttrigger'], RT: ['trigger_r', 'rt', 'r2', 'righttrigger'],
    LS: ['stick_l', 'l3', 'lstick', 'leftstick', 'thumb_l', 'analog_l', 'joystick_l'], RS: ['stick_r', 'r3', 'rstick', 'rightstick', 'thumb_r', 'analog_r', 'joystick_r'],
    UP: ['dpad_up', 'd_up', 'up'], DOWN: ['dpad_down', 'd_down', 'down'], LEFT: ['dpad_left', 'd_left', 'left'], RIGHT: ['dpad_right', 'd_right', 'right'],
    DPAD: ['dpad', 'd_pad', 'directional'],
    START: ['btn_start', 'start', 'options', 'menu'], BACK: ['btn_back', 'back', 'share', 'create', 'view', 'select'], GUIDE: ['btn_guide', 'guide', 'ps', 'home', 'xbox'],
  };

  window.Pad3D = { create };
})();
