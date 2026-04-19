/* EcoFlow Data Flow — sequence-style diagram, animated packets. */
(() => {
  const stage = document.getElementById('stage');
  const canvas = document.getElementById('canvas');

  function fit() {
    const s = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
    stage.style.transform = `scale(${s})`;
  }
  window.addEventListener('resize', fit); fit();

  const state = {
    theme: localStorage.getItem('ef_theme') || 'dark',
    speed: parseFloat(localStorage.getItem('ef_speed') || '1'),
    detail: localStorage.getItem('ef_detail') || 'normal',
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
    user: { kind: 'actor', title: 'User', sub: 'Operator', tag: 'LoginView',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="10" cy="7" r="3"/><path d="M3 17c1.5-3 4.2-4.5 7-4.5s5.5 1.5 7 4.5"/></svg>' },
    app: { kind: 'actor', title: 'App', sub: 'Avalonia client', tag: 'EcoFlowClient.cs',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="2.5" y="3.5" width="15" height="11" rx="1"/><path d="M7 17.5h6M9 14.5v3M5 6h10M5 9h6"/></svg>' },
    rest: { kind: 'service', accent: 'rest', title: 'REST API', sub: 'api.ecoflow.com', tag: 'https',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M4 8c1-3 4-4.5 7-3.5c1.5-0.5 3.5 0 4.5 1.5c2 0.5 3 3 2 5s-2.5 2.5-4 2.5H5.5C3 13.5 2.5 10 4 8z"/></svg>' },
    broker: { kind: 'service', accent: 'mqtt', title: 'MQTT Broker', sub: 'TLS · 8883', tag: 'MQTT 3.1.1',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M3 14c0-3 2-5 5-5"/><path d="M3 17c0-5 3.5-8.5 8.5-8.5"/><path d="M3 20c0-7 5.5-12 12-12"/><circle cx="4" cy="16" r="0.8" fill="currentColor"/></svg>' },
    device: { kind: 'actor', accent: 'mqtt', title: 'Device', sub: 'DELTA 3 / Max', tag: 'telemetry',
      icon: '<svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="3.5" y="4.5" width="13" height="11" rx="1"/><rect x="6" y="7" width="8" height="3" rx="0.5"/><circle cx="7" cy="12.5" r="0.6" fill="currentColor"/><circle cx="10" cy="12.5" r="0.6" fill="currentColor"/><circle cx="13" cy="12.5" r="0.6" fill="currentColor"/></svg>' },
  };

  function makeNode(key, { x, y, w = 220 }) {
    const meta = NODES[key];
    const n = document.createElement('div');
    n.className = 'node' + (meta.accent === 'rest' ? ' rest-accent' : meta.accent === 'mqtt' ? ' mqtt-accent' : '');
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
        <marker id="arrRest" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--rest)"/></marker>
        <marker id="arrMqtt" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--mqtt)"/></marker>
        <marker id="arrInk"  viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--ink-dim)"/></marker>
      </defs>`;
    return svg;
  }

  /* ========== BUILD ========== */
  function build() {
    canvas.innerHTML = '';

    // 5 actor columns
    const cols = [
      { key: 'user',   x: 200 },
      { key: 'app',    x: 580 },
      { key: 'rest',   x: 960 },
      { key: 'broker', x: 1340 },
      { key: 'device', x: 1720 },
    ];
    const headerY = 210;
    const lifelineTop = 340;
    const lifelineBottom = 970;

    // Render column node headers
    cols.forEach(c => canvas.appendChild(makeNode(c.key, { x: c.x, y: headerY, w: 220 })));

    const svg = makeSVG();
    canvas.appendChild(svg);

    // lifelines
    cols.forEach(c => {
      svg.appendChild(el('line', {
        x1: c.x, y1: lifelineTop, x2: c.x, y2: lifelineBottom,
        stroke: 'var(--rule-2)', 'stroke-width': 1, 'stroke-dasharray': '4 6'
      }));
    });

    // Phase bands (reference lines + labels)
    const phases = [
      { y: 360, label: 'PHASE 1 · LOGIN',         color: 'rest' },
      { y: 460, label: 'PHASE 2 · DEVICE LIST',   color: 'rest' },
      { y: 550, label: 'PHASE 3 · MQTT CREDS',    color: 'rest' },
      { y: 650, label: 'PHASE 4 · CONNECT + WAKE', color: 'mqtt' },
      { y: 830, label: 'PHASE 5 · STREAM',        color: 'mqtt' },
    ];
    phases.forEach(ph => {
      const band = div('phase-band ' + ph.color);
      band.style.top = ph.y + 'px'; band.style.height = '1px';
      const lbl = div('phase-label'); lbl.style.position='absolute'; lbl.style.left='16px'; lbl.style.top='6px';
      lbl.style.fontFamily = 'JetBrains Mono, monospace';
      lbl.style.fontSize = '11px'; lbl.style.letterSpacing = '0.22em'; lbl.style.textTransform = 'uppercase';
      lbl.style.color = ph.color === 'rest' ? 'var(--rest)' : 'var(--mqtt)';
      lbl.textContent = ph.label;
      band.appendChild(lbl);
      canvas.appendChild(band);
    });

    const xOf = Object.fromEntries(cols.map(c => [c.key, c.x]));

    function hop(from, to, y, kind, text, { num, dashed = false, deep = false, dur = 0.55, loop = false, stagger } = {}) {
      const x1 = xOf[from], x2 = xOf[to];
      const arrowEnd = x2 > x1 ? x2 - 8 : x2 + 8;
      const startX  = x2 > x1 ? x1 + 4 : x1 - 4;
      const d = `M ${startX} ${y} L ${arrowEnd} ${y}`;
      const id = 'w' + Math.random().toString(36).slice(2, 9);
      const p = el('path', { d });
      p.classList.add(kind === 'rest' ? 'rest-path' : kind === 'mqtt' ? 'mqtt-path' : 'neutral-path');
      if (dashed) p.classList.add('dashed');
      if (deep) p.classList.add('detail-only', 'detail-deep-only');
      p.setAttribute('marker-end', kind === 'rest' ? 'url(#arrRest)' : kind === 'mqtt' ? 'url(#arrMqtt)' : 'url(#arrInk)');
      p.dataset.id = id;
      svg.appendChild(p);

      const lbl = div('wire-label ' + (kind === 'rest' ? 'rest' : kind === 'mqtt' ? 'mqtt' : ''));
      if (deep) lbl.classList.add('detail-only', 'detail-deep-only');
      lbl.innerHTML = (num ? `<span class="wire-num">${num}</span>` : '') + text;
      lbl.style.left = ((x1 + x2) / 2) + 'px';
      lbl.style.top = (y - 20) + 'px';
      lbl.style.transform = 'translateX(-50%)';
      canvas.appendChild(lbl);

      return { id, kind, dur, deep, loop, stagger };
    }

    const schedule = [];
    let t = 0;
    function step(h, { gap = 0.75 } = {}) {
      schedule.push({ ...h, start: t });
      if (!h.loop) t += gap;
    }

    // PHASE 1 · LOGIN
    step(hop('user', 'app',  395, 'neutral', 'email · password',          { num: '00' }));
    step(hop('app',  'rest', 420, 'rest',    'POST /auth/login',          { num: '01' }));
    step(hop('rest', 'app',  445, 'rest',    'token · userId',            { num: '02', dashed: true }));

    // PHASE 2 · DEVICE LIST
    step(hop('app',  'rest', 495, 'rest',    'GET /app/user/device/list', { num: '03' }));
    step(hop('rest', 'app',  520, 'rest',    '[ { sn, deviceName }, … ]', { num: '04', dashed: true }));

    // PHASE 3 · MQTT CREDS
    step(hop('app',  'rest', 585, 'rest',    'GET /iot-auth/app/certification', { num: '05' }));
    step(hop('rest', 'app',  615, 'rest',    'host · port · certAccount · certPassword', { num: '06', dashed: true }));

    // PHASE 4 · CONNECT + WAKE
    step(hop('app',    'broker', 685, 'mqtt', 'CONNECT · TLS 8883',                         { num: '07' }));
    step(hop('broker', 'app',    715, 'mqtt', 'CONNACK',                                    { num: '08', dashed: true }));
    step(hop('app',    'broker', 745, 'mqtt', 'SUBSCRIBE /app/device/property/<sn>',        { num: '09' }));
    step(hop('app',    'broker', 775, 'mqtt', 'PUBLISH wake → /app/<userId>/<sn>/thing/property/get', { num: '10' }));
    step(hop('broker', 'device', 805, 'mqtt', 'wake',                                       { num: '11', deep: true }));

    // PHASE 5 · STREAM (loop)
    step(hop('device', 'broker', 870, 'mqtt', 'PUBLISH protobuf · QoS 0',                   { num: '12', loop: true, stagger: 1.8, dur: 0.75 }), { gap: 0 });
    step(hop('broker', 'app',    910, 'mqtt', '→ /app/device/property/<sn>',                { num: '13', loop: true, stagger: 1.8, dur: 0.75 }), { gap: 0 });
    step(hop('device', 'broker', 945, 'mqtt', '…',                                           { loop: true, stagger: 1.8, dur: 0.75 }), { gap: 0 });

    // Decode callout next to app column, beside streaming phase
    const decode = div('');
    decode.style.cssText = 'position:absolute;left:60px;top:840px;width:260px;color:var(--ink-faint);font-family:JetBrains Mono,monospace;font-size:10.5px;line-height:1.5;padding:12px 14px;border:1px solid var(--rule-2);border-radius:3px;background:var(--card);';
    decode.innerHTML = `<div style="color:var(--ink);font-weight:600;font-size:11px;margin-bottom:6px;letter-spacing:0.1em;">DECODE PROTOBUF  →</div>parseOuter(envelope) → if<br/>encType=1 &amp; src≠32, XOR-decrypt<br/>pdata with (seq &amp; 0xFF) → dispatch<br/>by (cmdFunc, cmdId) → merge into<br/>DeviceState under SyncLock.`;
    canvas.appendChild(decode);

    // FSM strip — bottom-right
    const fsm = div('fsm-strip');
    fsm.style.cssText = 'right:60px; top:1000px;';
    fsm.innerHTML = `<span>FSM</span><span class="fsm-arrow">›</span><b>Idle</b><span class="fsm-arrow">→</span><b>Connecting</b><span class="fsm-arrow">→</span><b>Authenticating</b><span class="fsm-arrow">→</span><span style="color:var(--mqtt);font-weight:600;">Streaming</span>`;
    canvas.appendChild(fsm);

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
  let cycleMax = 10;

  function startPackets(root, svg, specs) {
    if (rafId) cancelAnimationFrame(rafId);
    timeline = [];
    requestAnimationFrame(() => {
      const paths = {};
      svg.querySelectorAll('path[data-id]').forEach(p => { paths[p.dataset.id] = p; });
      specs.forEach(s => {
        const p = paths[s.id];
        if (!p) return;
        try { timeline.push({ spec: s, pathEl: p, len: p.getTotalLength() }); } catch(e) {}
      });
      cycleMax = Math.max(...timeline.map(t => t.spec.start + t.spec.dur + 1.5), 12);
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
    d.className = 'packet ' + (kind === 'rest' ? 'rest' : kind === 'mqtt' ? 'mqtt' : '');
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

  bindSeg('themeSeg', 'theme', v => { state.theme = v; localStorage.setItem('ef_theme', v); document.documentElement.setAttribute('data-theme', v); });
  bindSeg('detailSeg', 'detail', v => { state.detail = v; localStorage.setItem('ef_detail', v); updateDetail(); });

  const speedRange = document.getElementById('speedRange');
  const speedLabel = document.getElementById('speedLabel');
  speedRange.value = state.speed;
  speedLabel.textContent = Number(state.speed).toFixed(2) + '×';
  speedRange.addEventListener('input', () => {
    state.speed = parseFloat(speedRange.value);
    localStorage.setItem('ef_speed', String(state.speed));
    speedLabel.textContent = state.speed.toFixed(2) + '×';
  });

  document.querySelectorAll('#themeSeg button').forEach(b => b.classList.toggle('on', b.dataset.theme === state.theme));
  document.querySelectorAll('#detailSeg button').forEach(b => b.classList.toggle('on', b.dataset.detail === state.detail));

  build();
})();
