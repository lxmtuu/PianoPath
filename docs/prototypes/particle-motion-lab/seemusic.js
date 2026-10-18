/*
 * SeeMusic-style motion playground.
 *
 * This file implements PianoPath's own reading of the motion grammar that
 * SeeMusic exposes through its documented particle presets and settings:
 * Particles / Streaklets / Smoke as independent layers, and the presets
 * "explode", "fast playing", "high count", "high turbulence", "long
 * streaklets", "straight" and "twirl", plus wind and magnetism.
 *
 * It is NOT a reverse engineering of SeeMusic's algorithms: SeeMusic is a
 * closed app and was not run for this prototype. Numbers here are ours.
 * The point is to make each *kind of motion* visible and tunable so the
 * product decision can be made by eye, not by description.
 *
 * Layer model (documented):
 *   - Particles, Streaklets and Smoke can be toggled independently.
 * Settings reused from the public documentation:
 *   - size, count, speed, turbulence, wind, magnetism, direction, colour.
 *
 * Rendering is a deliberately simple 2D canvas; no bloom, no HDR.
 */
(function (global) {
  'use strict';

  var lab = global.ParticleLab;
  var TAU = Math.PI * 2;
  var clamp = function (v, a, b) { return v < a ? a : v > b ? b : v; };
  var lerp = function (a, b, t) { return a + (b - a) * t; };

  function mulberry32(seed) {
    var a = seed >>> 0;
    return function () {
      a = (a + 0x6D2B79F5) >>> 0;
      var t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // ---------------------------------------------------------------- presets
  // Each preset is our own recipe; the names are the documented SeeMusic
  // preset names, so the user can recognise what each is *for*.
  var PRESETS = {
    'Default': {
      note: 'Hạt nhỏ bám theo phím, mật độ vừa, sống ngắn. Bản "sạch" để nhìn nốt.',
      particles: { style: 'puff', density: 20, speed: 170, size: 1.5, life: .7, gravity: 260, drag: 1.8, spread: .9 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'Explode': {
      note: 'Hạt bắn ngang ra hai bên khỏi phím, gần như không rơi: cảm giác "vỗ" mặt phím.',
      particles: { style: 'sideways', density: 34, speed: 340, size: 1.4, life: .55, gravity: 40, drag: 2.6, spread: .35 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'Fast playing': {
      note: 'Nốt ngắn bùng mạnh hơn hẳn nốt dài. Đây là preset làm câu staccato lấp lánh.',
      particles: { style: 'puff', density: 26, speed: 230, size: 1.2, life: .5, gravity: 220, drag: 2.2, spread: 1.1, shortNoteBoost: 2.4 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'High count': {
      note: 'Rất nhiều hạt nhỏ, tốc độ thấp: một đám bụi lấp lánh thay vì vài tia rõ.',
      particles: { style: 'puff', density: 90, speed: 150, size: .9, life: .9, gravity: 200, drag: 1.4, spread: 1.2 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'High turbulence': {
      note: 'Cùng mật độ nhưng trường xoáy mạnh: các tia bẻ cong và quấn vào nhau.',
      particles: { style: 'puff', density: 30, speed: 200, size: 1.4, life: 1.0, gravity: 180, drag: 1.6, spread: 1.0, turbulence: 1 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'Long streaklets': {
      note: 'Bỏ hạt điểm, chỉ còn vệt dài: tốc độ của hạt chính là độ dài vệt.',
      particles: { style: 'puff', density: 0, speed: 0, size: 0, life: 0, gravity: 0, drag: 0, spread: 0 },
      streaklets: { on: true, density: 26, speed: 330, life: 1.1, width: 1.6 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'Straight': {
      note: 'Không nhiễu, không gió: hạt bay thẳng, đều. Dùng khi cần "sạch" tuyệt đối.',
      particles: { style: 'puff', density: 22, speed: 190, size: 1.4, life: .7, gravity: 260, drag: 1.8, spread: .8, turbulence: 0 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'Twirl': {
      note: 'Lực tiếp tuyến quanh phím làm hạt xoáy tròn thay vì toả thẳng.',
      particles: { style: 'puff', density: 30, speed: 170, size: 1.5, life: 1.1, gravity: 120, drag: 1.2, spread: .9, twirl: 1 },
      streaklets: { on: true, density: 8, speed: 260, life: .9, width: 1.2 },
      smoke: { on: false, density: 0, speed: 0, life: 0 }
    },
    'Smoke': {
      note: 'Lớp khói riêng: hạt lớn, mờ, nổi lên, sống lâu — chiều sâu chứ không phải điểm nhấn.',
      particles: { style: 'puff', density: 8, speed: 120, size: 1.0, life: .6, gravity: 200, drag: 2, spread: .8 },
      streaklets: { on: false, density: 0, speed: 0, life: 0 },
      smoke: { on: true, density: 7, speed: 38, life: 2.4, size: 18 }
    }
  };

  var PRESET_ORDER = ['Default', 'Explode', 'Fast playing', 'High count', 'High turbulence', 'Long streaklets', 'Straight', 'Twirl', 'Smoke'];

  // ---------------------------------------------------------------- scenario
  var NOTES = [
    { start: .35, end: .60, pitch: 52, strength: .75 },
    { start: .68, end: .84, pitch: 55, strength: .65 },   // short, staccato
    { start: .92, end: 1.06, pitch: 59, strength: .8 },   // short
    { start: 1.14, end: 1.28, pitch: 62, strength: .7 },  // short
    { start: 1.40, end: 2.10, pitch: 67, strength: .9 },
    { start: 2.25, end: 3.20, pitch: 48, strength: .8 },
    { start: 2.25, end: 3.20, pitch: 55, strength: .8 },
    { start: 2.25, end: 3.20, pitch: 64, strength: .8 },
    { start: 3.40, end: 3.58, pitch: 72, strength: .95 },
    { start: 3.66, end: 3.82, pitch: 74, strength: .9 },
    { start: 3.90, end: 4.05, pitch: 76, strength: .85 },
    { start: 4.20, end: 5.60, pitch: 60, strength: .85 },
    { start: 4.20, end: 5.60, pitch: 67, strength: .85 }
  ];
  var LOOP = 6.2;

  function buildSchedule(seed) {
    var rnd = mulberry32(seed);
    return { notes: NOTES.map(function (n) { return Object.assign({}, n); }), seed: seed, rnd: rnd };
  }

  // ---------------------------------------------------------------- state
  function createEngine(options) {
    options = options || {};
    return {
      preset: options.preset || 'Default',
      particlesOn: true,
      streakletsOn: true,
      smokeOn: true,
      density: options.density == null ? 1 : options.density,
      speed: options.speed == null ? 1 : options.speed,
      turbulence: options.turbulence == null ? 1 : options.turbulence,
      wind: options.wind == null ? 0 : options.wind,
      magnetism: options.magnetism == null ? 0 : options.magnetism,
      clock: 0,
      rnd: mulberry32(options.seed || 20261019),
      particles: [],
      nextNote: 0,
      noteAliases: [],
      activeAnchors: []
    };
  }

  function presetOf(engine) { return PRESETS[engine.preset] || PRESETS.Default; }

  // ---------------------------------------------------------------- colours
  var spriteCache = new Map();
  function glowSprite(color, soft) {
    var key = color[0] + ',' + color[1] + ',' + color[2] + ',' + (soft ? 's' : 'h');
    var hit = spriteCache.get(key);
    if (hit) return hit;
    if (spriteCache.size > 300) spriteCache.clear();
    var c = document.createElement('canvas');
    c.width = c.height = 64;
    var g = c.getContext('2d');
    var grd = g.createRadialGradient(32, 32, 0, 32, 32, 32);
    if (soft) {
      grd.addColorStop(0, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',.55)');
      grd.addColorStop(.45, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',.20)');
      grd.addColorStop(1, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',0)');
    } else {
      grd.addColorStop(0, 'rgba(255,255,255,.95)');
      grd.addColorStop(.2, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',.8)');
      grd.addColorStop(.5, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',.22)');
      grd.addColorStop(1, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',0)');
    }
    g.fillStyle = grd;
    g.fillRect(0, 0, 64, 64);
    spriteCache.set(key, c);
    return c;
  }

  // ---------------------------------------------------------------- motion
  function field(x, y, t, seed) {
    var s = 90;
    var a = Math.sin(y / s + t * .9 + seed * TAU);
    var b = Math.cos(x / (s * 1.6) - t * .7 + seed * 4.1);
    var c = Math.cos(y / (s * .5) + t * .4 + seed * 2.7);
    return { x: a * b + .4 * c, y: (c * a - .3 * b) * .6 };
  }

  function anchorOf(notes, clock) {
    var list = [];
    for (var i = 0; i < notes.length; i++) {
      var n = notes[i];
      if (clock >= n.start && clock <= n.end) list.push(n);
    }
    return list;
  }

  function spawnBurst(engine, note, x, y) {
    var p = presetOf(engine).particles;
    var short = (note.end - note.start) < .25;
    var count = Math.round(p.density * engine.density * (p.shortNoteBoost && short ? p.shortNoteBoost : 1) * (.6 + note.strength * .6));
    var color = lab.noteColor(note.pitch);
    var style = p.style;
    for (var i = 0; i < count; i++) {
      var angle;
      if (style === 'sideways') {
        // Explode: a flat fan along the keybed, left and right.
        var side = engine.rnd() < .5 ? -1 : 1;
        angle = (side < 0 ? Math.PI : 0) + (engine.rnd() - .5) * p.spread;
      } else {
        angle = -Math.PI / 2 + (engine.rnd() - .5) * 2 * Math.PI * p.spread * .8;
      }
      var speed = p.speed * engine.speed * (.45 + engine.rnd() * .9);
      engine.particles.push({
        layer: 'particle',
        x: x + (engine.rnd() - .5) * 8, y: y - 1 - engine.rnd() * 2,
        vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
        age: 0, life: p.life * (.6 + engine.rnd() * .8),
        size: p.size * (.6 + engine.rnd() * .9),
        grav: p.gravity, drag: p.drag,
        color: engine.rnd() < .18 ? [255, 255, 255] : color,
        seed: engine.rnd(), pitch: note.pitch, anchorX: x, anchorY: y,
        trail: null
      });
    }
  }

  function spawnStreaklet(engine, note, x, y) {
    var s = presetOf(engine).streaklets;
    if (!s.on || s.density <= 0) return;
    var count = Math.max(1, Math.round(s.density * engine.density * .25));
    var color = lab.noteColor(note.pitch);
    for (var i = 0; i < count; i++) {
      var angle = -Math.PI / 2 + (engine.rnd() - .5) * 1.5;
      var speed = s.speed * engine.speed * (.6 + engine.rnd() * .8);
      engine.particles.push({
        layer: 'streaklet',
        x: x + (engine.rnd() - .5) * 6, y: y - 1,
        vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
        age: 0, life: s.life * (.7 + engine.rnd() * .6),
        size: (s.width || 1.4) * (.7 + engine.rnd() * .6),
        grav: 160, drag: 1.1,
        color: color, seed: engine.rnd(), pitch: note.pitch,
        anchorX: x, anchorY: y,
        trail: [[x, y]]
      });
    }
  }

  function spawnSmoke(engine, note, x, y) {
    var s = presetOf(engine).smoke;
    if (!s.on || s.density <= 0) return;
    var count = Math.max(1, Math.round(s.density * engine.density * .18));
    var color = lab.noteColor(note.pitch);
    for (var i = 0; i < count; i++) {
      engine.particles.push({
        layer: 'smoke',
        x: x + (engine.rnd() - .5) * 14, y: y - 2,
        vx: (engine.rnd() - .5) * 16, vy: -s.speed * (.4 + engine.rnd() * .7) * engine.speed,
        age: 0, life: s.life * (.6 + engine.rnd() * .7),
        size: (s.size || 16) * (.5 + engine.rnd() * .8),
        grav: -26, drag: .5,
        color: engine.rnd() < .3 ? [220, 230, 255] : color,
        seed: engine.rnd(), pitch: note.pitch,
        anchorX: x, anchorY: y, trail: null
      });
    }
  }

  function emitAt(engine, note, x, y) {
    spawnBurst(engine, note, x, y);
    spawnStreaklet(engine, note, x, y);
    spawnSmoke(engine, note, x, y);
  }

  function step(engine, dt) {
    dt = clamp(dt, 0, .05);
    engine.clock += dt;
    if (engine.clock > LOOP) {
      engine.clock -= LOOP;
      engine.nextNote = 0;
      engine.particles.length = 0;
      engine.rnd = mulberry32(20261019 + Math.floor(engine.clock));
    }

    // note-ons
    var notes = NOTES;
    while (engine.nextNote < notes.length && notes[engine.nextNote].start <= engine.clock) {
      var n = notes[engine.nextNote++];
      emitAt(engine, n, lab.keyCenter(n.pitch), lab.HIT_Y - 1);
    }

    // sustained emission from held notes (one pulse every 40 ms)
    var anchors = anchorOf(notes, engine.clock);
    engine.activeAnchors = anchors.map(function (a) { return { x: lab.keyCenter(a.pitch), y: lab.HIT_Y - 1, pitch: a.pitch }; });
    if (Math.floor(engine.clock / .04) !== Math.floor((engine.clock - dt) / .04)) {
      for (var a = 0; a < anchors.length; a++) {
        var note = anchors[a];
        var x = lab.keyCenter(note.pitch), y = lab.HIT_Y - 1;
        spawnSmoke(engine, note, x, y);
        spawnStreaklet(engine, note, x, y);
      }
    }

    var preset = presetOf(engine);
    var turb = (preset.particles.turbulence == null ? 1 : preset.particles.turbulence) * engine.turbulence;
    var twirl = preset.particles.twirl ? 1 : 0;

    for (var i = engine.particles.length - 1; i >= 0; i--) {
      var p = engine.particles[i];
      if (p.layer === 'particle' && !engine.particlesOn) { engine.particles.splice(i, 1); continue; }
      if (p.layer === 'streaklet' && !engine.streakletsOn) { engine.particles.splice(i, 1); continue; }
      if (p.layer === 'smoke' && !engine.smokeOn) { engine.particles.splice(i, 1); continue; }

      p.age += dt;
      if (p.age >= p.life) { engine.particles.splice(i, 1); continue; }

      var u = p.age / p.life;

      // wind (global, horizontal) + curl turbulence
      var f = field(p.x, p.y, engine.clock, p.seed);
      var turbAmp = p.layer === 'smoke' ? 26 : 90;
      p.vx += (f.x * turbAmp * turb + engine.wind * 60) * dt;
      p.vy += f.y * turbAmp * .55 * turb * dt;

      // twirl: tangential force around its own key anchor
      if (twirl && p.layer === 'particle') {
        var dx = p.x - p.anchorX, dy = p.y - p.anchorY;
        var r = Math.sqrt(dx * dx + dy * dy) + 24;
        var w = 900 / r;
        p.vx += (-dy / r) * w * dt * 1.2;
        p.vy += (dx / r) * w * dt * 1.2;
      }

      // magnetism: pull toward (or push from) the other sounding notes
      if (engine.magnetism !== 0 && p.layer !== 'smoke' && engine.activeAnchors.length > 1) {
        var best = null, bestD = 1e9;
        for (var k = 0; k < engine.activeAnchors.length; k++) {
          var an = engine.activeAnchors[k];
          if (an.pitch === p.pitch) continue;
          var ddx = an.x - p.x, ddy = an.y - p.y;
          var d2 = ddx * ddx + ddy * ddy;
          if (d2 < bestD) { bestD = d2; best = { dx: ddx, dy: ddy, d: Math.sqrt(d2) }; }
        }
        if (best) {
          // Magnetism: a key-relative attraction, strong enough to read at 100%,
          // weak enough that the particle still leaves its own key first.
          var pull = engine.magnetism * 2400 / (1 + best.d / 140);
          p.vx += (best.dx / (best.d + 1e-3)) * pull * dt;
          p.vy += (best.dy / (best.d + 1e-3)) * pull * dt * .6;
        }
      }

      // forces
      var heat = clamp(1 - u, 0, 1);
      p.vy += (p.grav - (p.layer === 'smoke' ? 0 : 30 * heat)) * dt;
      if (p.layer === 'smoke') p.vy += -20 * dt;
      var damp = Math.exp(-p.drag * dt);
      p.vx *= damp; p.vy *= damp;

      p.x += p.vx * dt;
      p.y += p.vy * dt;

      if (p.layer === 'streaklet') {
        p.trail.push([p.x, p.y]);
        if (p.trail.length > 26) p.trail.shift();
      }
      if (p.layer === 'smoke') p.size += (28 - p.size) * dt * .5;
    }
  }

  // ---------------------------------------------------------------- drawing
  function draw(ctx, engine) {
    var parts = engine.particles;

    // smoke first, on its own (alpha) pass: it is atmosphere, not light
    ctx.save();
    ctx.globalCompositeOperation = 'source-over';
    for (var s = 0; s < parts.length; s++) {
      var sm = parts[s];
      if (sm.layer !== 'smoke') continue;
      var su = sm.age / sm.life;
      var a = Math.sin(Math.PI * clamp(su, 0, 1)) * .30;
      var tex = glowSprite(sm.color, true);
      ctx.globalAlpha = a;
      ctx.drawImage(tex, sm.x - sm.size, sm.y - sm.size, sm.size * 2, sm.size * 2);
    }
    ctx.restore();

    // particles + streaklets are light: additive
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (var i = 0; i < parts.length; i++) {
      var p = parts[i];
      var u = p.age / p.life;
      if (p.layer === 'smoke') continue;

      if (p.layer === 'streaklet') {
        var fade = Math.pow(clamp(1 - u, 0, 1), 1.2);
        if (p.trail.length > 1) {
          ctx.strokeStyle = 'rgba(' + p.color[0] + ',' + p.color[1] + ',' + p.color[2] + ',' + (fade * .8).toFixed(3) + ')';
          ctx.lineWidth = p.size;
          ctx.lineCap = 'round';
          ctx.beginPath();
          ctx.moveTo(p.trail[0][0], p.trail[0][1]);
          for (var t = 1; t < p.trail.length; t++) ctx.lineTo(p.trail[t][0], p.trail[t][1]);
          ctx.stroke();
        }
        var st = glowSprite(p.color, false);
        ctx.globalAlpha = fade * .8;
        ctx.drawImage(st, p.x - p.size * 3, p.y - p.size * 3, p.size * 6, p.size * 6);
        continue;
      }

      var alpha = Math.pow(clamp(1 - u, 0, 1), 1.3);
      var size = p.size * 2.6 * (1 - .3 * u);
      var tex2 = glowSprite(p.color, false);
      ctx.globalAlpha = alpha;
      ctx.drawImage(tex2, p.x - size, p.y - size, size * 2, size * 2);
    }
    ctx.restore();
  }

  // ---------------------------------------------------------------- harness
  function createLab(options) {
    var engine = createEngine(options);
    return {
      engine: engine,
      preset: function (name) { engine.preset = name; },
      step: step,
      draw: draw,
      reset: function () {
        engine.clock = 0; engine.nextNote = 0; engine.particles.length = 0;
        engine.rnd = mulberry32(options && options.seed || 20261019);
      }
    };
  }

  var api = {
    PRESETS: PRESETS, PRESET_ORDER: PRESET_ORDER, NOTES: NOTES, LOOP: LOOP,
    createEngine: createEngine, createLab: createLab, step: step, draw: draw,
    field: field, clamp: clamp
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  global.SeeMusicLab = api;
})(typeof window !== 'undefined' ? window : globalThis);
