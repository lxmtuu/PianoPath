/*
 * Damper-bar light model for the design prototype.
 *
 * Two readings of the same pedal gesture:
 *   current  - CC64 collapses to a boolean at 64, no easing, AR strip grows but its
 *              hot core stays pinned to the key edge (Camera/ArDampers.cs,
 *              Gpu/GpuArLayer.cs:134, StageShaders.hlsl:974).
 *   proposed - CC64 stays continuous, dead zone to 25% travel, full lift at 50%,
 *              asymmetric smoothing (55 ms down / 75 ms up), a core that travels
 *              with the lift, and one damper tick per key.
 *
 * This is a design model: numbers are PianoPath's, not a capture of the app.
 */
(function (global) {
  'use strict';

  var AR = { baseDepth: .06, liftDepth: .10, alphaBase: .18, alphaLift: .32, coreTravel: .55 };
  var CURVE = { free: .25, full: .50 };
  var TAU = { attack: .055, release: .075, tickUp: .030, tickDown: .040 };
  var CELL = {
    whiteShare: .70, blackShare: .46,
    bodyBase: .05, bodyLift: .42, rimBase: .16, rimLift: .54,
    tickHeight: 15, tickWidth: 2.4, tickBase: .18, tickLift: .58
  };
  var KEY_DEPTH_PX = 120;          // drawing scale: 0.06 depth = 7.2 px, 0.16 = 19.2 px
  var DURATION = 10.4;
  var FIRST = 21, LAST = 108;
  var BLACK = { 1: 1, 3: 1, 6: 1, 8: 1, 10: 1 };

  var clamp = function (v, a, b) { return v < a ? a : v > b ? b : v; };
  var smoothstep = function (e0, e1, x) {
    var t = clamp((x - e0) / (e1 - e0), 0, 1);
    return t * t * (3 - 2 * t);
  };
  var approach = function (x, target, dt, tau) {
    return x + (target - x) * (1 - Math.exp(-dt / tau));
  };
  var isBlack = function (p) { return BLACK[((p % 12) + 12) % 12] === 1; };

  // ------------------------------------------------------------------ gesture
  var GESTURE = [
    { t: 0.0, cc: 0 }, { t: 1.0, cc: 0 },
    { t: 2.0, cc: .35 }, { t: 2.9, cc: .35 },      // half-pedal: audio sustains, current light stays off
    { t: 3.3, cc: 1 }, { t: 4.5, cc: 1 },          // full pedal under a chord
    { t: 5.2, cc: 0 }, { t: 5.7, cc: 0 },          // release
    { t: 6.2, cc: 0 },                             // staccato with no pedal
    { t: 6.5, cc: 1 }, { t: 7.6, cc: 1 },          // pedal under an arpeggio
    { t: 8.3, cc: 0 }, { t: DURATION, cc: 0 }      // release; one note is still held
  ];

  var NOTES = [
    { start: 1.30, end: 1.70, pitch: 55 },
    { start: 3.30, end: 4.40, pitch: 60 }, { start: 3.30, end: 4.40, pitch: 64 },
    { start: 3.30, end: 4.40, pitch: 67 },
    { start: 5.75, end: 5.86, pitch: 60 }, { start: 5.93, end: 6.04, pitch: 62 },
    { start: 6.11, end: 6.22, pitch: 64 }, { start: 6.29, end: 6.40, pitch: 65 },
    { start: 6.60, end: 6.74, pitch: 67 }, { start: 6.82, end: 6.96, pitch: 72 },
    { start: 7.04, end: 7.18, pitch: 76 }, { start: 7.26, end: 7.40, pitch: 79 },
    { start: 7.52, end: 8.80, pitch: 84 }           // held across the pedal release
  ];

  function sampleGesture(t) {
    t = ((t % DURATION) + DURATION) % DURATION;
    for (var i = 0; i < GESTURE.length - 1; i++) {
      var a = GESTURE[i], b = GESTURE[i + 1];
      if (t >= a.t && t <= b.t) {
        var u = b.t === a.t ? 0 : (t - a.t) / (b.t - a.t);
        return a.cc + (b.cc - a.cc) * u;
      }
    }
    return 0;
  }

  function heldAt(t, notes) {
    var held = {};
    for (var i = 0; i < notes.length; i++) {
      var n = notes[i];
      if (t >= n.start && t < n.end) held[n.pitch] = true;
    }
    return held;
  }

  // ------------------------------------------------------------------ engine
  function createEngine(options) {
    options = options || {};
    var engine = {
      t: options.t || 0,
      cc: sampleGesture(options.t || 0),
      manual: false,
      tapAt: null,
      level: 0,
      lift: 0,
      held: {},
      tick: {},
      pulse: { current: 0, proposed: 0 },
      notes: (options.notes || NOTES).map(function (n) { return { start: n.start, end: n.end, pitch: n.pitch }; })
    };
    for (var p = FIRST; p <= LAST; p++) engine.tick[p] = 0;
    engine.held = heldAt(engine.t, engine.notes);
    engine.lift = smoothstep(CURVE.free, CURVE.full, engine.cc);
    engine.level = engine.cc >= .5 ? 1 : 0;
    return engine;
  }

  function step(engine, dt) {
    dt = clamp(dt || 0, 0, .05);
    var prevLevel = engine.level, prevLift = engine.lift;

    if (engine.tapAt !== null) {
      var age = engine.t - engine.tapAt;
      engine.cc = age < .12 ? 1 : 0;
      if (age > .8) engine.tapAt = null;
      if (!engine.manual) engine.t += dt;
    } else if (engine.manual) {
      // cc is driven by the slider; time stands still so the note pattern stays put.
    } else {
      engine.t += dt;
      if (engine.t >= DURATION) {
        engine.t -= DURATION;
        engine.lift = 0;
        for (var q = FIRST; q <= LAST; q++) engine.tick[q] = 0;
      }
      engine.cc = sampleGesture(engine.t);
    }

    engine.level = engine.cc >= .5 ? 1 : 0;
    var target = smoothstep(CURVE.free, CURVE.full, engine.cc);
    var tau = target > engine.lift ? TAU.attack : TAU.release;
    engine.lift = approach(engine.lift, target, dt, tau);
    engine.held = heldAt(engine.t, engine.notes);

    for (var p = FIRST; p <= LAST; p++) {
      var tickTarget = Math.max(engine.held[p] ? 1 : 0, engine.lift);
      var tickTau = tickTarget > engine.tick[p] ? TAU.tickUp : TAU.tickDown;
      engine.tick[p] = approach(engine.tick[p], tickTarget, dt, tickTau);
    }

    engine.pulse.current = Math.max(engine.pulse.current, Math.abs(engine.level - prevLevel));
    engine.pulse.proposed = Math.max(engine.pulse.proposed, Math.abs(engine.lift - prevLift));
  }

  // ------------------------------------------------------------------ geometry
  function shape(lift, continuous) {
    var l = continuous ? clamp(lift, 0, 1) : (lift >= .5 ? 1 : 0);
    return {
      lift: l,
      depth: AR.baseDepth + AR.liftDepth * l,
      depthPx: (AR.baseDepth + AR.liftDepth * l) * KEY_DEPTH_PX,
      alpha: AR.alphaBase + AR.alphaLift * l,
      coreOffset: continuous ? AR.coreTravel * l * AR.liftDepth * KEY_DEPTH_PX : 0
    };
  }

  function measure() {
    var distinct = function (fn) {
      var seen = {};
      for (var cc = 0; cc <= 1.0001; cc += .005) seen[fn(cc).toFixed(4)] = true;
      return Object.keys(seen).length;
    };
    var currentLevels = distinct(function (cc) { return cc >= .5 ? 1 : 0; });
    var proposedLevels = distinct(function (cc) { return smoothstep(CURVE.free, CURVE.full, cc); });

    function response(from, to) {
      var x = from, t = 0, t10 = null, t90 = null;
      var tau = to > from ? TAU.attack : TAU.release;
      for (var i = 0; i < 120; i++) {
        x = approach(x, to, 1 / 60, tau);
        t += 1 / 60;
        if (t10 === null && Math.abs(x - from) >= .1) t10 = t;
        if (t90 === null && Math.abs(x - from) >= .9) t90 = t;
      }
      return { rise: t90 === null ? null : t90 - t10, settle: t };
    }
    var up = response(0, 1), down = response(1, 0);

    var maxStep = 1 - Math.exp(-(1 / 60) / TAU.attack);
    var maxRelease = 1 - Math.exp(-(1 / 60) / TAU.release);

    return {
      currentLevels: currentLevels,
      proposedLevels: proposedLevels,
      rise: up.rise,
      fall: down.rise,
      maxStep: maxStep,
      maxRelease: maxRelease,
      coreTravel45: AR.coreTravel * AR.liftDepth * (23.8 / .16),
      coreTravel90: AR.coreTravel * AR.liftDepth * KEY_DEPTH_PX,
      depthUp: AR.baseDepth * KEY_DEPTH_PX,
      depthDown: (AR.baseDepth + AR.liftDepth) * KEY_DEPTH_PX
    };
  }

  // ------------------------------------------------------------------ render
  var COLORS = {
    current: [255, 184, 107],
    proposed: [143, 216, 255],
    contact: [210, 225, 255]
  };

  function rgba(c, a) { return 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + clamp(a, 0, 1).toFixed(3) + ')'; }

  function pitchColor(pitch) {
    var h = ((pitch - FIRST) * 4.1 % 360 + 360) % 360 / 360;
    var s = .78, l = .62;
    var q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    var hk = function (t) {
      t = (t % 1 + 1) % 1;
      if (t < 1 / 6) return p + (q - p) * 6 * t;
      if (t < .5) return q;
      if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
      return p;
    };
    return [Math.round(hk(h + 1 / 3) * 255), Math.round(hk(h) * 255), Math.round(hk(h - 1 / 3) * 255)];
  }

  function mixColor(a, b, t) {
    return [Math.round(a[0] + (b[0] - a[0]) * t), Math.round(a[1] + (b[1] - a[1]) * t), Math.round(a[2] + (b[2] - a[2]) * t)];
  }

  function keyboardGeometry(w, h) {
    var pad = 18, top = h - 92, bottom = h - 8;
    var whites = [];
    for (var p = FIRST; p <= LAST; p++) if (!isBlack(p)) whites.push(p);
    var whiteW = (w - pad * 2) / whites.length;
    return { pad: pad, top: top, bottom: bottom, whites: whites, whiteW: whiteW, height: bottom - top };
  }

  function keyX(g, p) {
    if (isBlack(p)) {
      var whitesBefore = 0;
      for (var q = FIRST; q < p; q++) if (!isBlack(q)) whitesBefore++;
      var base = g.pad + whitesBefore * g.whiteW;
      return base - g.whiteW * .16;
    }
    var index = 0;
    for (var w = FIRST; w < p; w++) if (!isBlack(w)) index++;
    return g.pad + index * g.whiteW;
  }

  function drawStrip(ctx, x0, x1, front, depth, alpha, coreOffset, color) {
    var sigma = Math.max(1.4, depth * .22);
    var coreY = front - Math.max(1.5, coreOffset);

    // soft bed
    for (var i = 0; i <= Math.ceil(depth); i++) {
      var y = front - i;
      var d = Math.abs(y - coreY);
      var gain = Math.exp(-(d * d) / (2 * sigma * sigma));
      ctx.fillStyle = rgba(color, alpha * (.18 + .82 * gain));
      ctx.fillRect(x0, y - 1, x1 - x0, 1.6);
    }

    // contact line: the rail never stops touching the key edge
    ctx.fillStyle = rgba(COLORS.contact, alpha * .30);
    ctx.fillRect(x0, front - 1.8, x1 - x0, 1.2);

    // hot core
    ctx.fillStyle = rgba(color, Math.min(1, alpha * 1.9));
    ctx.fillRect(x0, coreY - 1.1, x1 - x0, 2.2);
  }

  function drawCells(ctx, g, engine, color) {
    var s = shape(engine.lift, true);
    var front = g.top, depth = s.depthPx;
    for (var p = FIRST; p <= LAST; p++) {
      var v = engine.tick[p];
      if (v < .02) continue;
      var w = isBlack(p) ? g.whiteW * CELL.blackShare : g.whiteW * CELL.whiteShare;
      var x = keyX(g, p) + (isBlack(p) ? g.whiteW * .5 - w * .5 : (g.whiteW - w) * .5);
      var tint = mixColor(color, pitchColor(p), .22);
      ctx.fillStyle = rgba(tint, CELL.bodyBase + CELL.bodyLift * v);
      ctx.fillRect(x, front - depth, w, depth);
      ctx.fillStyle = rgba(tint, CELL.rimBase + CELL.rimLift * v);
      ctx.fillRect(x, front - depth, w, 2);
    }
  }

  function drawTicks(ctx, g, engine, color) {
    var base = g.top - shape(engine.lift, true).depthPx;
    for (var p = FIRST; p <= LAST; p++) {
      var v = engine.tick[p];
      if (v < .02) continue;
      var h = CELL.tickHeight * v;
      var w = CELL.tickWidth;
      var x = keyX(g, p) + (isBlack(p) ? g.whiteW * .5 - w * .5 : (g.whiteW - w) * .5);
      ctx.fillStyle = rgba(color, CELL.tickBase + CELL.tickLift * v);
      ctx.fillRect(x, base - h, w, h);
      ctx.fillRect(x - 1.5, base - h - 2, w + 3, 2);
    }
  }

  function drawInset(ctx, w, s, color, mode) {
    var boxW = 300, boxH = 104, x = w - boxW - 14, y = 14;
    ctx.fillStyle = 'rgba(4,7,12,.72)';
    ctx.strokeStyle = 'rgba(120,140,170,.22)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x + 8, y); ctx.lineTo(x + boxW - 8, y);
    ctx.quadraticCurveTo(x + boxW, y, x + boxW, y + 8);
    ctx.lineTo(x + boxW, y + boxH - 8);
    ctx.quadraticCurveTo(x + boxW, y + boxH, x + boxW - 8, y + boxH);
    ctx.lineTo(x + 8, y + boxH);
    ctx.quadraticCurveTo(x, y + boxH, x, y + boxH - 8);
    ctx.lineTo(x, y + 8);
    ctx.quadraticCurveTo(x, y, x + 8, y);
    ctx.closePath(); ctx.fill(); ctx.stroke();

    ctx.fillStyle = rgba(color, .85);
    ctx.font = '600 10px Inter, system-ui, sans-serif';
    ctx.fillText('MẶT CẮT DẢI ×2,2', x + 12, y + 18);
    ctx.fillStyle = 'rgba(160,174,194,.7)';
    ctx.font = '10px Inter, system-ui, sans-serif';
    ctx.fillText(mode === 'proposed' ? 'lõi chạy' : 'lõi ghim', x + boxW - 62, y + 18);

    var front = y + 76;
    var depth = s.depthPx * 2.2;
    drawStrip(ctx, x + 12, x + boxW - 12, front, depth, s.alpha, s.coreOffset * 2.2, color);

    ctx.fillStyle = 'rgba(160,174,194,.55)';
    ctx.font = '9.5px Inter, system-ui, sans-serif';
    ctx.fillText('mép phím', x + 12, front + 12);
    ctx.fillText((s.depthPx).toFixed(1) + ' px thật', x + boxW - 70, front + 12);
  }

  function drawKeys(ctx, g) {
    var i, p, x;
    ctx.fillStyle = '#0a0e16';
    ctx.fillRect(g.pad, g.top, g.whites.length * g.whiteW, g.height);
    for (i = 0; i < g.whites.length; i++) {
      p = g.whites[i]; x = g.pad + i * g.whiteW;
      ctx.fillStyle = 'rgba(205,215,230,.90)';
      ctx.fillRect(x + .5, g.top + 1, g.whiteW - 1.5, g.height - 1);
      ctx.fillStyle = 'rgba(15,20,32,.22)';
      ctx.fillRect(x + .5, g.top + 1, g.whiteW - 1.5, 3);
    }
    for (p = FIRST; p <= LAST; p++) {
      if (!isBlack(p)) continue;
      x = keyX(g, p);
      ctx.fillStyle = 'rgba(12,17,27,.96)';
      ctx.fillRect(x, g.top + 1, g.whiteW * .62, g.height * .62);
    }
  }

  function drawPanel(ctx, engine, mode, w, h) {
    var continuous = mode === 'proposed';
    var color = continuous ? COLORS.proposed : COLORS.current;
    var s = shape(continuous ? engine.lift : engine.cc, continuous);
    var g = keyboardGeometry(w, h);

    var bg = ctx.createLinearGradient(0, 0, 0, h);
    bg.addColorStop(0, '#070a12'); bg.addColorStop(.62, '#0b101c'); bg.addColorStop(1, '#05070c');
    ctx.fillStyle = bg; ctx.fillRect(0, 0, w, h);

    var x0 = g.pad, x1 = g.pad + g.whites.length * g.whiteW;
    drawStrip(ctx, x0, x1, g.top, s.depthPx, s.alpha, s.coreOffset, color);
    if (continuous) { drawCells(ctx, g, engine, color); drawTicks(ctx, g, engine, color); }
    drawKeys(ctx, g);
    drawInset(ctx, w, s, color, mode);

    ctx.fillStyle = continuous ? 'rgba(143,216,255,.92)' : 'rgba(255,184,107,.92)';
    ctx.font = '600 12px Inter, system-ui, sans-serif';
    ctx.fillText(continuous ? 'ĐỀ XUẤT' : 'HIỆN TẠI', 18, 24);
    ctx.fillStyle = 'rgba(170,181,198,.75)';
    ctx.font = '11px Inter, system-ui, sans-serif';
    ctx.fillText(continuous
      ? 'lift ' + s.lift.toFixed(2) + ' · dày ' + s.depthPx.toFixed(1) + ' px · lõi +' + s.coreOffset.toFixed(1) + ' px'
      : 'level ' + s.lift.toFixed(0) + ' · dày ' + s.depthPx.toFixed(1) + ' px · lõi ghim', 18, 42);
    if (!continuous) {
      ctx.fillStyle = 'rgba(255,184,107,.75)';
      ctx.fillText('CC64 ' + Math.round(engine.cc * 127) + ' → chỉ có 0/1', 18, 59);
    } else {
      ctx.fillText('CC64 ' + Math.round(engine.cc * 127) + ' → lift liên tục', 18, 59);
    }
    return s;
  }

  function buildTrace() {
    var engine = createEngine();
    var trace = { cc: [], current: [], lift: [] };
    var steps = Math.round(DURATION * 60);
    for (var i = 0; i <= steps; i++) {
      trace.cc.push(engine.cc);
      trace.current.push(engine.level);
      trace.lift.push(engine.lift);
      step(engine, 1 / 60);
    }
    return trace;
  }

  function drawGraph(ctx, engine, trace, w, h) {
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = '#0a0e16'; ctx.fillRect(0, 0, w, h);
    var left = 34, top = 10, right = w - 14, bottom = h - 20;
    var x = function (t) { return left + (right - left) * (t / DURATION); };
    var y = function (v) { return bottom - (bottom - top) * v; };

    // half-pedal window
    ctx.fillStyle = 'rgba(143,216,255,.055)';
    ctx.fillRect(left, y(CURVE.full), right - left, y(CURVE.free) - y(CURVE.full));
    ctx.strokeStyle = 'rgba(143,216,255,.14)'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(left, y(CURVE.free)); ctx.lineTo(right, y(CURVE.free));
    ctx.moveTo(left, y(CURVE.full)); ctx.lineTo(right, y(CURVE.full)); ctx.stroke();

    // grid
    ctx.strokeStyle = 'rgba(160,175,200,.10)';
    for (var g = 0; g <= 1; g += .25) {
      ctx.beginPath(); ctx.moveTo(left, y(g)); ctx.lineTo(right, y(g)); ctx.stroke();
    }
    ctx.fillStyle = 'rgba(150,163,182,.65)'; ctx.font = '10px Inter, system-ui, sans-serif';
    ctx.fillText('1', 16, y(1) + 3); ctx.fillText('0', 16, y(0) + 3);
    ctx.fillText('CC64 0', left, h - 5);
    ctx.fillText('10,4 s', right - 34, h - 5);

    var drawCurve = function (values, color, width, dash) {
      ctx.strokeStyle = color; ctx.lineWidth = width; ctx.setLineDash(dash || []);
      ctx.beginPath();
      for (var i = 0; i < values.length; i++) {
        var px = x(i / (values.length - 1) * DURATION), py = y(values[i]);
        if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
      }
      ctx.stroke(); ctx.setLineDash([]);
    };

    drawCurve(trace.cc, 'rgba(150,163,182,.35)', 1, [3, 3]);
    drawCurve(trace.current, 'rgba(255,184,107,.9)', 1.6);
    drawCurve(trace.lift, 'rgba(143,216,255,.95)', 2);

    var px = x(engine.t);
    ctx.strokeStyle = 'rgba(255,255,255,.45)'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(px, top); ctx.lineTo(px, bottom); ctx.stroke();
    ctx.fillStyle = 'rgba(255,184,107,.9)'; ctx.fillRect(px - 1, y(engine.level) - 2, 3, 4);
    ctx.fillStyle = 'rgba(143,216,255,.95)'; ctx.fillRect(px - 1, y(engine.lift) - 2, 3, 4);
  }

  // ------------------------------------------------------------------ api
  var api = {
    AR: AR, CURVE: CURVE, TAU: TAU, CELL: CELL, DURATION: DURATION, KEY_DEPTH_PX: KEY_DEPTH_PX,
    GESTURE: GESTURE, NOTES: NOTES,
    createEngine: createEngine, step: step, shape: shape, measure: measure,
    sampleGesture: sampleGesture, heldAt: heldAt, smoothstep: smoothstep, isBlack: isBlack,
    drawPanel: drawPanel, drawGraph: drawGraph, buildTrace: buildTrace,
    pitchColor: pitchColor, mixColor: mixColor
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  global.DamperLight = api;
})(typeof window !== 'undefined' ? window : globalThis);
