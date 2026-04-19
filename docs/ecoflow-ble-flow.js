/* EcoFlow BLE Flow — sequence-style diagram, animated packets. */
(() => {
  const stage = document.getElementById('stage');
  const canvas = document.getElementById('canvas');

  function fit() {
    const s = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
    stage.style.transform = `scale(${s})`;
  }
  window.addEventListener('resize', fit); fit();

  const state = {
    theme: localStorage.getItem('ef_ble_theme') || 'dark',
    speed: parseFloat(localStorage.getItem('ef_ble_speed') || '1'),
    detail: localStorage.getItem('ef_ble_detail') || 'normal',
  };
  document.documentElement.setAttribute('data-theme', state.theme);

  const svgNS = 'http://www.w3.org/2000/svg';
  const el = (tag, attrs = {}) => {
    const e = document.createElementNS(svgNS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    return e;
  };
  const div = (cls, html = '') => { const d = document.createElement('div'); d.className = cls; d.innerHTML = html; return d; };

  const NODES = {
    app: { kind: 'actor', title: 'App', sub: 'Avalonia client', tag: 'BleMonitor.cs',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="2.5" y="3.5" width="15" height="11" rx="1"/><path d="M7 17.5h6M9 14.5v3M5 6h10M5 9h6"/></svg>' },
    adapter: { kind: 'platform', accent: 'ble', title: 'BLE Adapter', sub: 'IBleAdapter · host radio', tag: 'CoreBT · WinRT · BlueZ',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M7 4l6 6-6 6V4zM7 10l6-6 M7 10l6 6"/></svg>' },
    device: { kind: 'actor', accent: 'ble', title: 'Device', sub: 'DELTA 3 / Max', tag: 'GATT peripheral',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="3.5" y="4.5" width="13" height="11" rx="1"/><rect x="6" y="7" width="8" height="3" rx="0.5"/><circle cx="7" cy="12.5" r="0.6" fill="currentColor"/><circle cx="10" cy="12.5" r="0.6" fill="currentColor"/><circle cx="13" cy="12.5" r="0.6" fill="currentColor"/></svg>' },
  };

  function makeNode(key, { x, y, w = 240 }) {
    const meta = NODES[key];
    const n = document.createElement('div');
    n.className = 'node' + (meta.accent === 'ble' ? ' ble-accent' : '');
    n.style.left = (x - w/2) + 'px'; n.style.top = y + 'px'; n.style.width = w + 'px';
    n.innerHTML = `<div class="node-head"><span>${meta.kind}</span><span class="node-icon">${meta.icon}</span></div>
      <div class="node-title">${meta.title}</div>
      <div class="node-sub">${meta.sub}</div>
      <div style="margin-top:10px;"><span class="node-tag">${meta.tag}</span></div>`;
    return n;
  }

  function makeSVG() {
    const svg = el('svg', { class: 'wires', viewBox: '0 0 1920 1080', width: 1920, height: 1080 });
    svg.setAttribute('preserveAspectRatio', 'none');
    svg.innerHTML = `
      <defs>
        <marker id="arrBle" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--ble)"/></marker>
        <marker id="arrCrypt" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--crypt)"/></marker>
        <marker id="arrInk"  viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--ink-dim)"/></marker>
      </defs>`;
    return svg;
  }

  function build() {
    canvas.innerHTML = '';

    // 3 actor columns — spaced for clarity
    const cols = [
      { key: 'app',     x: 360 },
      { key: 'adapter', x: 960 },
      { key: 'device',  x: 1560 },
    ];
    const headerY = 210;
    const lifelineTop = 340;
    const lifelineBottom = 980;

    cols.forEach(c => canvas.appendChild(makeNode(c.key, { x: c.x, y: headerY, w: 240 })));

    const svg = makeSVG();
    canvas.appendChild(svg);

    cols.forEach(c => {
      svg.appendChild(el('line', {
        x1: c.x, y1: lifelineTop, x2: c.x, y2: lifelineBottom,
        stroke: 'var(--rule-2)', 'stroke-width': 1, 'stroke-dasharray': '4 6'
      }));
    });

    const phases = [
      { y: 360, label: 'PHASE 1 · IDENTITY BOOTSTRAP',      color: 'ble' },
      { y: 420, label: 'PHASE 2 · ADVERTISEMENT SCAN',      color: 'ble' },
      { y: 510, label: 'PHASE 3 · GATT CONNECT + SUBSCRIBE', color: 'ble' },
      { y: 620, label: 'PHASE 4 · ECDH HANDSHAKE  (Type 7)', color: 'crypt' },
      { y: 820, label: 'PHASE 5 · AUTH',                     color: 'crypt' },
      { y: 900, label: 'PHASE 6 · STREAM (encrypted)',       color: 'crypt' },
    ];
    phases.forEach(ph => {
      const band = div('phase-band ' + ph.color);
      band.style.top = ph.y + 'px'; band.style.height = '1px';
      const lbl = div('phase-label'); lbl.style.position='absolute'; lbl.style.left='16px'; lbl.style.top='6px';
      lbl.style.fontFamily = 'JetBrains Mono, monospace';
      lbl.style.fontSize = '11px'; lbl.style.letterSpacing = '0.22em'; lbl.style.textTransform = 'uppercase';
      lbl.style.color = ph.color === 'ble' ? 'var(--ble)' : 'var(--crypt)';
      lbl.textContent = ph.label;
      band.appendChild(lbl);
      canvas.appendChild(band);
    });

    const xOf = Object.fromEntries(cols.map(c => [c.key, c.x]));

    function hop(from, to, y, kind, text, { num, dashed = false, deep = false, dur = 0.55, loop = false, stagger } = {}) {
      const x1 = xOf[from], x2 = xOf[to];
      let d;
      if (from === to) {
        // self-hop: small rightward loop at this column
        const r = 26;
        d = `M ${x1 + 4} ${y} C ${x1 + r + 40} ${y - r}, ${x1 + r + 40} ${y + r}, ${x1 + 4} ${y + 2}`;
      } else {
        const arrowEnd = x2 > x1 ? x2 - 8 : x2 + 8;
        const startX  = x2 > x1 ? x1 + 4 : x1 - 4;
        d = `M ${startX} ${y} L ${arrowEnd} ${y}`;
      }
      const id = 'w' + Math.random().toString(36).slice(2, 9);
      const p = el('path', { d });
      p.classList.add(kind === 'ble' ? 'ble-path' : kind === 'crypt' ? 'crypt-path' : 'neutral-path');
      if (dashed) p.classList.add('dashed');
      if (deep) p.classList.add('detail-only', 'detail-deep-only');
      p.setAttribute('marker-end', kind === 'ble' ? 'url(#arrBle)' : kind === 'crypt' ? 'url(#arrCrypt)' : 'url(#arrInk)');
      p.dataset.id = id;
      svg.appendChild(p);

      const lbl = div('wire-label ' + (kind === 'ble' ? 'ble' : kind === 'crypt' ? 'crypt' : ''));
      if (deep) lbl.classList.add('detail-only', 'detail-deep-only');
      lbl.innerHTML = (num ? `<span class="wire-num">${num}</span>` : '') + text;
      if (from === to) {
        lbl.style.left = (x1 + 80) + 'px';
        lbl.style.top  = (y - 8) + 'px';
      } else {
        lbl.style.left = ((x1 + x2) / 2) + 'px';
        lbl.style.top  = (y - 20) + 'px';
        lbl.style.transform = 'translateX(-50%)';
      }
      canvas.appendChild(lbl);

      return { id, kind, dur, deep, loop, stagger };
    }

    const schedule = [];
    let t = 0;
    function step(h, { gap = 0.7 } = {}) {
      schedule.push({ ...h, start: t });
      if (!h.loop) t += gap;
    }

    // PHASE 1 — identity bootstrap (local only, app-side)
    step(hop('app', 'app', 395, 'neutral', 'resolve userId  ·  CloudUserId  ∥  LocalUserId (GUID)', { num: '00' }));

    // PHASE 2 — advertisement scan
    step(hop('app',    'adapter', 450, 'ble', 'StartScanAsync()',                { num: '01' }));
    step(hop('device', 'adapter', 475, 'ble', 'adv · manufacturerId = 46517',    { num: '02' }));
    step(hop('adapter','app',     495, 'ble', 'AdvertisementReceived → sn · encType · protoVer', { num: '03', dashed: true }));

    // PHASE 3 — connect + subscribe
    step(hop('app',    'adapter', 545, 'ble', 'ConnectAsync(deviceAddress)',     { num: '04' }));
    step(hop('adapter','device',  565, 'ble', 'GATT connect',                    { num: '05' }));
    step(hop('app',    'adapter', 585, 'ble', 'Subscribe notify · 0x0001 → 0x6e400001 fallback', { num: '06' }));
    step(hop('adapter','app',     605, 'ble', 'notifications ready',             { num: '07', dashed: true }));

    // PHASE 4 — ECDH handshake
    step(hop('app',    'device',  655, 'crypt', '[0x01 0x00  X Y]   local pubkey (SECP160r1)',   { num: '08' }));
    step(hop('device', 'app',     685, 'crypt', '[status  ecdhType  devicePub (40B)]',            { num: '09', dashed: true }));
    step(hop('app',    'app',     715, 'crypt', 'compute shared secret · set initial AES-128',    { num: '10', deep: true }));
    step(hop('app',    'device',  740, 'crypt', '[0x02]   session-key request',                   { num: '11' }));
    step(hop('device', 'app',     765, 'crypt', '[0x02 ciphertext] → decrypt → srand(16) · seed(2)', { num: '12', dashed: true }));
    step(hop('app',    'app',     790, 'crypt', 'derive sessionKey = MD5(keydata[pos..] ‖ srand)',{ num: '13', deep: true }));

    // PHASE 5 — auth
    step(hop('app', 'device', 850, 'crypt', 'probe (cmdSet=0x35 cmdId=0x89)',                     { num: '14', deep: true }));
    step(hop('app', 'device', 875, 'crypt', 'auth  MD5(userId + sn)  (cmdSet=0x35 cmdId=0x86)',   { num: '15' }));

    // PHASE 6 — telemetry stream (loop)
    step(hop('device', 'adapter', 935, 'crypt', 'notify  5A5A frames (encrypted)',                { num: '16', loop: true, stagger: 1.8, dur: 0.7 }), { gap: 0 });
    step(hop('adapter','app',     960, 'crypt', 'BleTransport.OnNotification',                    { num: '17', loop: true, stagger: 1.8, dur: 0.7 }), { gap: 0 });

    // Decode callout (left side)
    const decode = div('');
    decode.style.cssText = 'position:absolute;left:40px;top:700px;width:300px;color:var(--ink-faint);font-family:JetBrains Mono,monospace;font-size:10.5px;line-height:1.5;padding:12px 14px;border:1px solid var(--rule-2);border-radius:3px;background:var(--card);';
    decode.innerHTML = `<div style="color:var(--ink);font-weight:600;font-size:11px;margin-bottom:6px;letter-spacing:0.1em;">DECODE PIPELINE  →</div>
      buffer += notify bytes →<br/>
      find <span style="color:var(--ble);">0x5A 0x5A</span> · CRC16-Modbus →<br/>
      AES-128-CBC decrypt (PKCS7) →<br/>
      parse <span style="color:var(--ble);">0xAA</span> packet · CRC8 header →<br/>
      XOR-decrypt payload if seq[0]≠0 →<br/>
      dispatch (Src, CmdSet, CmdId) →<br/>
      BmsData · DisplayData · EmsData.`;
    canvas.appendChild(decode);

    // Right-side callout — crypto key facts
    const cryptNote = div('');
    cryptNote.style.cssText = 'position:absolute;right:40px;top:460px;width:300px;color:var(--ink-faint);font-family:JetBrains Mono,monospace;font-size:10.5px;line-height:1.5;padding:12px 14px;border:1px solid var(--rule-2);border-radius:3px;background:var(--card);';
    cryptNote.innerHTML = `<div style="color:var(--ink);font-weight:600;font-size:11px;margin-bottom:6px;letter-spacing:0.1em;">ENCRYPTION TYPE  (from adv)</div>
      <span style="color:var(--crypt);">Type 1</span> · AES-256-CBC · stateless<br/>
      &nbsp;&nbsp;key = MD5(sn) ‖ MD5(sn)<br/>
      &nbsp;&nbsp;iv&nbsp; = MD5(reverse(sn))<br/><br/>
      <span style="color:var(--crypt);">Type 7</span> · AES-128-CBC · ECDH<br/>
      &nbsp;&nbsp;curve = SECP160r1 · keydata 65 280 B<br/>
      &nbsp;&nbsp;one handshake per connection`;
    canvas.appendChild(cryptNote);

    // FSM strip — bottom-right
    const fsm = div('fsm-strip');
    fsm.style.cssText = 'right:60px; top:1000px;';
    fsm.innerHTML = `<span>FSM</span><span class="fsm-arrow">›</span><b>Idle</b><span class="fsm-arrow">→</span><b>Scanning</b><span class="fsm-arrow">→</span><b>Connecting</b><span class="fsm-arrow">→</span><b>Authenticating</b><span class="fsm-arrow">→</span><span style="color:var(--crypt);font-weight:600;">Streaming</span>`;
    canvas.appendChild(fsm);

    // Note: no EcoFlow servers
    const noServer = div('');
    noServer.style.cssText = 'position:absolute;left:40px;top:1000px;font-family:JetBrains Mono,monospace;font-size:11px;color:var(--ink-faint);letter-spacing:0.08em;';
    noServer.innerHTML = '<span style="color:var(--ble);">◦</span>  NO ECOFLOW SERVERS · LOCAL ONLY · userId is the only identity bit carried over';
    canvas.appendChild(noServer);

    // time axis label
    const axis = div('');
    axis.style.cssText = 'position:absolute;left:20px;top:600px;color:var(--ink-faint);font-family:JetBrains Mono,monospace;font-size:10px;letter-spacing:0.3em;writing-mode:vertical-rl;transform:rotate(180deg);';
    axis.textContent = 'TIME  ↓';
    canvas.appendChild(axis);

    startPackets(canvas, svg, schedule);
    updateDetail();
  }

  /* ========== PACKETS ========== */
  let rafId = null;
  let timeline = [];
  let cycleMax = 12;

  function startPackets(root, svg, specs) {
    if (rafId) cancelAnimationFrame(rafId);
    timeline = [];
    requestAnimationFrame(() => {
      const paths = {};
      svg.querySelectorAll('path[data-id]').forEach(p => { paths[p.dataset.id] = p; });
      specs.forEach(s => {
        const p = paths[s.id];
        if (!p) return;
        try {
          const len = p.getTotalLength();
          // self-loop (same column) needs a tiny fake path — draw packet at midpoint
          timeline.push({ spec: s, pathEl: p, len: Math.max(len, 1) });
        } catch(e) {}
      });
      cycleMax = Math.max(...timeline.map(t => t.spec.start + t.spec.dur + 1.5), 14);
      let lastT = performance.now();
      let accum = 0;
      function frame(now) {
        const dt = (now - lastT) / 1000 * state.speed;
        lastT = now;
        accum += dt;
        const t = accum % cycleMax;
        root.querySelectorAll('.packet').forEach(d => d.remove());
        timeline.forEach(tl => {
          const s = tl.spec;
          if (s.deep && state.detail !== 'deep') return;
          if (s.loop) {
            const stagger = s.stagger || 1.5;
            for (let ts = s.start; ts <= t + 0.001; ts += stagger) {
              if (t - ts >= 0 && t - ts < s.dur) drawPacket(tl, (t - ts) / s.dur);
            }
          } else if (t >= s.start && t - s.start < s.dur) {
            drawPacket(tl, (t - s.start) / s.dur);
          }
        });
        rafId = requestAnimationFrame(frame);
      }
      rafId = requestAnimationFrame(frame);
    });
  }

  function drawPacket(tl, frac) {
    const f = Math.max(0, Math.min(1, frac));
    const pt = tl.pathEl.getPointAtLength(tl.len * f);
    const d = document.createElement('div');
    const kind = tl.spec.kind;
    d.className = 'packet ' + (kind === 'ble' ? 'ble' : kind === 'crypt' ? 'crypt' : '');
    if (kind === 'neutral') { d.style.background = 'var(--ink-dim)'; d.style.color = 'var(--ink-dim)'; }
    d.style.left = pt.x + 'px';
    d.style.top = pt.y + 'px';
    canvas.appendChild(d);
  }

  /* ========== TWEAKS ========== */
  function updateDetail() {
    document.body.classList.remove('detail-simple','detail-normal','detail-deep','detail-hide-on-simple');
    document.body.classList.add('detail-' + state.detail);
    if (state.detail === 'simple') document.body.classList.add('detail-hide-on-simple');
    document.querySelectorAll('.detail-deep-only').forEach(e => { e.style.display = state.detail === 'deep' ? '' : 'none'; });
  }

  const tweaks = document.getElementById('tweaks');
  window.addEventListener('message', (ev) => {
    const d = ev.data || {};
    if (d.type === '__activate_edit_mode') tweaks.classList.add('open');
    if (d.type === '__deactivate_edit_mode') tweaks.classList.remove('open');
  });
  window.parent && window.parent.postMessage({ type: '__edit_mode_available' }, '*');

  function bindSeg(id, key, onChange) {
    const seg = document.getElementById(id);
    seg.querySelectorAll('button').forEach(b => {
      b.addEventListener('click', () => {
        seg.querySelectorAll('button').forEach(x => x.classList.remove('on'));
        b.classList.add('on');
        onChange(b.dataset[key]);
      });
    });
  }

  bindSeg('themeSeg', 'theme', v => { state.theme = v; localStorage.setItem('ef_ble_theme', v); document.documentElement.setAttribute('data-theme', v); });
  bindSeg('detailSeg', 'detail', v => { state.detail = v; localStorage.setItem('ef_ble_detail', v); updateDetail(); });

  const speedRange = document.getElementById('speedRange');
  const speedLabel = document.getElementById('speedLabel');
  speedRange.value = state.speed;
  speedLabel.textContent = Number(state.speed).toFixed(2) + '×';
  speedRange.addEventListener('input', () => {
    state.speed = parseFloat(speedRange.value);
    localStorage.setItem('ef_ble_speed', String(state.speed));
    speedLabel.textContent = state.speed.toFixed(2) + '×';
  });

  document.querySelectorAll('#themeSeg button').forEach(b => b.classList.toggle('on', b.dataset.theme === state.theme));
  document.querySelectorAll('#detailSeg button').forEach(b => b.classList.toggle('on', b.dataset.detail === state.detail));

  build();
})();
