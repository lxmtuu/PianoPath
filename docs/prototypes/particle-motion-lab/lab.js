/*
 * Particle Motion Lab - a browser re-implementation of the Live Stage particle
 * integrator in Gpu/GpuStageSimulation.cs, used only for design review.
 *
 * It is NOT a capture from the application and it does not ship in the app.
 * Emission is pre-computed once per scenario and both panels replay the same
 * descriptors, so a difference between the two panels is a difference in the
 * motion model, never in the random draw.
 *
 * Ported constants (defaults):
 *   GpuLook.Gravity 290, Drag .26, VectorField .36, FieldScale 100,
 *   ParticleVelocity 210, ParticleSpread .72, ParticleLife .9 ...
 * Integrator: GpuStageSimulation.Step() "---- integrate ----".
 */
(function (global) {
  'use strict';

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

  // ------------------------------------------------------------------ look
  var DEFAULT_LOOK = {
    gravity: 290, drag: 26, vectorField: 36, fieldScale: 100, evolutionSpeed: 1,
    particleAmount: 34, particleResponse: 55, particleVelocity: 210, particleSpeed: 1,
    particleRandomness: .38, particleSpread: .72, particleLife: .9, particleLifeRandomness: .45,
    particleSize: 1.6, particleSizeRandomness: .8, particleGlow: .85, emitterSize: .24, spiral: .3,
    wispAmount: 45, wispSpeed: 170, wispHeight: .55, wispWidth: .4, wispTurbulence: .55, wispGlow: .8,
    flameIntensity: .7, flameHeight: .6,
    physicsTimeFactor: 1
  };

  // Per-family multipliers, copied from GpuStageSimulation.Impact() and the held emitter.
  var FAMILY = {
    Embers: { shape: 'dot', grav: 1, drag: 1, speed: 1, life: 1, size: 1 },
    Splash: { shape: 'droplet', grav: 1.6, drag: 1, speed: .82, life: 1.1, size: 1 },
    Fireworks: { shape: 'dot', grav: 1, drag: 1, speed: 1.25, life: 1.5, size: .9 },
    Confetti: { shape: 'confetti', grav: .32, drag: 3.2, speed: .7, life: 2.2, size: 1.3 },
    Dust: { shape: 'dust', grav: -.12, drag: 2.4, speed: .35, life: 2.4, size: 2.2 }
  };
  var HELD = {
    Embers: { shape: 'streak', grav: .55, drag: 1.2, speed: 1, life: 1, size: 1 },
    Splash: { shape: 'droplet', grav: 1.35, drag: .85, speed: .86, life: 1.08, size: 1 },
    Fireworks: { shape: 'streak', grav: 1, drag: 1, speed: 1.15, life: 1.22, size: .9 },
    Confetti: { shape: 'confetti', grav: .38, drag: 2.8, speed: .68, life: 1.75, size: 1.25 },
    Dust: { shape: 'dust', grav: -.1, drag: 2.2, speed: .3, life: 1.65, size: 1.8 }
  };

  // ------------------------------------------------------------------ stage
  var STAGE_W = 960, STAGE_H = 540;
  var KEYBOARD_FRACTION = .205;
  var HIT_Y = STAGE_H * (1 - KEYBOARD_FRACTION);
  var FIRST_PITCH = 21, KEY_COUNT = 88, WHITE_COUNT = 52;
  var WHITE_W = STAGE_W / WHITE_COUNT;

  function isBlack(p) { var m = p % 12; return m === 1 || m === 3 || m === 6 || m === 8 || m === 10; }
  var CENTER = (function () {
    var out = {}, white = 0;
    for (var p = FIRST_PITCH; p < FIRST_PITCH + KEY_COUNT; p++) {
      if (isBlack(p)) out[p] = white * WHITE_W;
      else { out[p] = (white + .5) * WHITE_W; white++; }
    }
    return out;
  })();
  function keyCenter(p) {
    p = clamp(p, FIRST_PITCH, FIRST_PITCH + KEY_COUNT - 1);
    return CENTER[p] | 0;
  }

  // ------------------------------------------------------------------ colour
  function hslToRgb(h, s, l) {
    h = ((h % 360) + 360) % 360 / 360;
    var r, g, b;
    if (s === 0) { r = g = b = l; }
    else {
      var q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
      var hk = function (t) {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1 / 6) return p + (q - p) * 6 * t;
        if (t < 1 / 2) return q;
        if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
        return p;
      };
      r = hk(h + 1 / 3); g = hk(h); b = hk(h - 1 / 3);
    }
    return [Math.round(r * 255), Math.round(g * 255), Math.round(b * 255)];
  }
  function noteColor(pitch) { return hslToRgb((pitch - FIRST_PITCH) * 4.1, .78, .62); }
  function hueColor(h) { return hslToRgb(h, .85, .6); }
  function mix(a, b, t) {
    return [Math.round(lerp(a[0], b[0], t)), Math.round(lerp(a[1], b[1], t)), Math.round(lerp(a[2], b[2], t))];
  }
  var WHITE = [255, 255, 255];

  // ------------------------------------------------------------------ recipes
  function makeParticle(o) {
    return {
      shape: o.shape, x: o.x, y: o.y, vx: o.vx, vy: o.vy, life: o.life, size: o.size,
      mass: o.mass || 1.5, grav: o.grav, dragK: o.dragK, color: o.color, phase: o.phase,
      kind: o.kind, pitch: o.pitch === undefined ? -1 : o.pitch, // burst | held | wisp | flame
      spin: o.spin || 0, depth: o.depth || 0, fieldSeed: o.fieldSeed || 0, delay: o.delay || 0,
      age: 0, angle: 0, bounced: false
    };
  }

  function burstRecipes(rnd, look, family, pitch, strength, x, y) {
    var f = FAMILY[family];
    var amount = clamp(Math.floor(look.particleAmount * strength * look.particleResponse / 55 * 2.2), 0, 300);
    var list = [];
    var base = noteColor(pitch);
    for (var i = 0; i < amount; i++) {
      var shape = f.shape, grav = f.grav, drag = f.drag, speedScale = f.speed,
          lifeScale = f.life, sizeScale = f.size;
      var c = i % 4 === 0 ? mix(base, WHITE, .72)
            : mix(base, hueColor(pitch * 4.1 + (rnd() - .5) * 28), .18);
      if (family === 'Splash') c = mix(mix(base, [148, 209, 255], .46), WHITE, rnd() * .24);
      if (family === 'Fireworks') c = mix(base, hueColor(rnd() * 360), .64);
      if (family === 'Confetti') c = rnd() < .2 ? WHITE : mix(base, hueColor(rnd() * 360), .54);
      if (family === 'Dust') c = mix([168, 156, 140], base, .34);

      var needle = i % 3 !== 0 && shape === 'dot';
      if (needle) shape = 'streak';
      var angle = -Math.PI / 2 + (rnd() - .5) * look.particleSpread * Math.PI;
      angle += Math.sin(i * .37 + rnd() * 0) * look.spiral * .3; // scenario clock is zero per event
      if (family === 'Fireworks') angle = rnd() * TAU;

      var speedVar = 1 + (rnd() - .5) * 2 * look.particleRandomness;
      var lifeVar = 1 + (rnd() - .5) * 2 * look.particleLifeRandomness;
      var sizeVar = 1 + (rnd() - .5) * 2 * look.particleSizeRandomness;
      var speed = look.particleVelocity * Math.max(.08, speedVar) * look.particleSpeed * strength * speedScale;
      var life = Math.max(.05, look.particleLife * lifeVar * lifeScale);
      list.push(makeParticle({
        shape: shape, kind: 'burst', pitch: pitch,
        x: x + (rnd() - .5) * look.emitterSize * (STAGE_W / KEY_COUNT) * 2, y: y,
        vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed - (needle ? 35 : 15),
        life: life, mass: needle ? .6 : 1.5, phase: rnd() * TAU,
        size: Math.max(.2, look.particleSize * sizeVar * sizeScale),
        color: c, grav: grav, dragK: drag,
        spin: (rnd() - .5) * 14, depth: rnd(), fieldSeed: rnd(), delay: rnd() * .055
      }));
    }
    return list;
  }

  function heldRecipe(rnd, look, family, pitch, x, y) {
    var f = HELD[family];
    var base = noteColor(pitch);
    var c = family === 'Embers' ? (rnd() < .22 ? WHITE : mix(base, WHITE, .24))
          : family === 'Splash' ? mix(base, [148, 209, 255], .48)
          : family === 'Fireworks' ? mix(base, hueColor(rnd() * 360), .56)
          : family === 'Confetti' ? mix(base, hueColor(rnd() * 360), .52)
          : mix([184, 171, 153], base, .38);
    var angle = -Math.PI / 2 + (rnd() - .5) * look.particleSpread * Math.PI;
    if (family === 'Fireworks') angle = rnd() * TAU;
    var speedVar = 1 + (rnd() - .5) * 2 * look.particleRandomness;
    var lifeVar = 1 + (rnd() - .5) * 2 * look.particleLifeRandomness;
    var sizeVar = 1 + (rnd() - .5) * 2 * look.particleSizeRandomness;
    var speed = look.particleVelocity * Math.max(.08, speedVar) * look.particleSpeed * f.speed;
    return makeParticle({
      shape: f.shape, kind: 'held', pitch: pitch,
      x: x + (rnd() - .5) * (STAGE_W / KEY_COUNT) * (.3 + look.emitterSize), y: y,
      vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
      life: Math.max(.05, look.particleLife * lifeVar * f.life),
      size: Math.max(.2, look.particleSize * sizeVar * f.size),
      mass: .7, phase: rnd() * TAU, color: c, grav: f.grav, dragK: f.drag,
      spin: (rnd() - .5) * 12, depth: rnd(), fieldSeed: rnd(), delay: rnd() * .04
    });
  }

  function wispRecipe(rnd, look, pitch, x, y, t) {
    var base = noteColor(pitch);
    var life = .35 + look.wispHeight * 1.9;
    var strand = rnd() < .5 ? 0 : 1;
    return makeParticle({
      shape: 'dot', kind: 'wisp', pitch: pitch,
      x: x + (rnd() - .5) * (STAGE_W / KEY_COUNT) * .25, y: y - 2 - rnd() * 3,
      vx: (rnd() - .5) * 8, vy: -look.wispSpeed * (.75 + rnd() * .5),
      life: life * (.7 + rnd() * .6),
      phase: strand * Math.PI + t * 1.7 + (rnd() - .5) * .5,
      size: .6 + rnd() * 1.2, color: rnd() < .2 ? mix(base, WHITE, .6) : base,
      grav: 0, dragK: 0, mass: 1.5,
      spin: 0, depth: rnd(), fieldSeed: rnd(), delay: rnd() * .05
    });
  }

  function flameRecipe(rnd, look, pitch, x, y) {
    return makeParticle({
      shape: 'flame', kind: 'flame', pitch: pitch,
      x: x + (rnd() - .5) * (STAGE_W / KEY_COUNT) * .7, y: y,
      vx: (rnd() - .5) * 14, vy: -(50 + 150 * look.flameHeight) * (.6 + rnd() * .6),
      life: .22 + look.flameHeight * .45 * (.6 + rnd() * .6),
      size: (STAGE_W / KEY_COUNT) * (.35 + rnd() * .3),
      color: [255, 140, 38], grav: 1, dragK: 0, mass: 1.5,
      phase: rnd() * TAU, spin: 0, depth: rnd(), fieldSeed: rnd(), delay: rnd() * .05
    });
  }

  // ------------------------------------------------------------------ scenario
  // A short, deterministic routine: single hits, a chord and a sustained chord
  // so impact, held and wisp emitters all appear in one loop.
  function buildScenario(seed, look, family) {
    var rnd = mulberry32(seed);
    var notes = [
      { start: .12, end: .55, pitch: 48, strength: .70 },
      { start: .55, end: 1.05, pitch: 55, strength: .82 },
      { start: 1.05, end: 1.55, pitch: 60, strength: .90 },
      { start: 1.55, end: 2.05, pitch: 64, strength: .78 },
      { start: 2.05, end: 2.55, pitch: 67, strength: .95 },
      { start: 2.55, end: 3.05, pitch: 72, strength: .72 },
      { start: 3.25, end: 3.85, pitch: 48, strength: .85 },
      { start: 3.25, end: 3.85, pitch: 55, strength: .85 },
      { start: 3.25, end: 3.85, pitch: 64, strength: .85 },
      { start: 3.25, end: 3.85, pitch: 72, strength: .85 },
      { start: 4.30, end: 6.90, pitch: 52, strength: .80 },
      { start: 4.30, end: 6.90, pitch: 59, strength: .80 },
      { start: 4.30, end: 6.90, pitch: 67, strength: .80 },
      { start: 4.30, end: 6.90, pitch: 74, strength: .80 }
    ];
    var duration = 8.0;

    var impacts = [], ticks = [];
    var tickDt = 1 / 120;
    var sparkCredit = {}, wispCredit = {};
    for (var n = 0; n < notes.length; n++) {
      var note = notes[n];
      impacts.push({
        t: note.start, pitch: note.pitch,
        list: burstRecipes(rnd, look, family, note.pitch, note.strength, keyCenter(note.pitch), HIT_Y - 1)
      });
      sparkCredit[note.pitch] = 0; wispCredit[note.pitch] = 0;
    }

    var steps = Math.round(duration / tickDt);
    for (var s = 0; s < steps; s++) {
      var t = s * tickDt;
      var batch = [];
      for (var k = 0; k < notes.length; k++) {
        var nt = notes[k];
        if (t < nt.start || t >= nt.end) continue;
        var x = keyCenter(nt.pitch), y = HIT_Y - 1;
        // held emitter: ParticleAmount * 1.3 births per second per sounding key
        sparkCredit[nt.pitch] += look.particleAmount * 1.3 * tickDt;
        while (sparkCredit[nt.pitch] >= 1) { sparkCredit[nt.pitch] -= 1; batch.push(heldRecipe(rnd, look, family, nt.pitch, x, y)); }
        // wisps: WispAmount * 4 births per second per sounding key
        wispCredit[nt.pitch] += look.wispAmount * 4 * tickDt;
        while (wispCredit[nt.pitch] >= 1) { wispCredit[nt.pitch] -= 1; batch.push(wispRecipe(rnd, look, nt.pitch, x, y, t)); }
        // flames: 70 * intensity * heat (~1 while held) per second per sounding key
        var heat = Math.min(1, (t - nt.start) * 7);
        var flameCredit = 70 * look.flameIntensity * heat * tickDt;
        // deterministic draw of a fractional birth for the flame emitter
        var flameRnd = mulberry32((nt.pitch * 7919 + s * 104729) >>> 0)();
        if (flameRnd < flameCredit) batch.push(flameRecipe(rnd, look, nt.pitch, x, y));
      }
      if (batch.length) ticks.push({ t: t, list: batch });
    }
    return { duration: duration, notes: notes, impacts: impacts, ticks: ticks };
  }

  // ------------------------------------------------------------------ models
  var MODEL = { GPU: 'gpu', WPF: 'wpf', V2: 'v2', FLOW: 'flow' };

  function v2Flags(flags) {
    return Object.assign({
      curlField: true, dragCurve: true, lifeCurves: true, spin: true, diffusion: true,
      stagger: true, depth: true, floor: false, flamePhysics: true, wispInSim: true
    }, flags || {});
  }

  // W1 (PARTICLE-MOTION-REVIEW §7): the four flow-shape components of §5 rows 1-4. They are motion only -
  // the emitter schedule is still shared with the other panels, so a difference between them is the model.
  var FLOW_TAU = .45;        // s, wind entrainment time constant (row 2)
  var FLOW_VERTICAL = .12;   // the wind bends the rise; it does not replace it (row 2)
  var FLOW_DIFFUSION = 46;   // px/s^2 at Diffusion 55 (row 4)
  var HELD_SPREAD = .22;     // rad cone half-angle for the sustained emitter (row 1)
  var HELD_SPEED = .45;      // fraction of the burst speed (row 1)

  function flowFlags(flags) {
    return Object.assign({ sustain: true, wind: true, curl: true, diffusion: 55 }, flags || {});
  }

  function diffusionAmount(flags) {
    if (flags.diffusion === false || flags.diffusion === 0) return 0;
    return typeof flags.diffusion === 'number' ? flags.diffusion : 55;
  }

  // A divergence-free 2-D wind (curl of a stream function) with a slow upward draught over the keyboard: the
  // field the column is entrained by. Amplitude follows the Vector field slider so the existing control keeps
  // meaning.  psi = A sin(kx x + w) cos(ky y - .6w)
  function flowWind(look, x, y, t) {
    var s = Math.max(30, look.fieldScale * 2.1), kx = 1 / s, ky = 1 / (s * 1.6);
    var w = t * (.22 + look.evolutionSpeed * .33);
    var a = kx * x + w, b = ky * y - w * .6;
    var amp = 88 * (look.vectorField / 36) * (s / 1.6);
    return {
      x: -amp * ky * Math.sin(a) * Math.sin(b),
      y: -amp * kx * Math.cos(a) * Math.cos(b) - 26
    };
  }

  // Small-scale curl, offset per particle by its own fieldSeed: neighbours share the eddy, so the stream
  // twists together instead of every mote wandering on its own (row 3).
  function flowCurl(x, y, t, seed) {
    var s = 34, kx = 1 / s, ky = 1 / (s * 1.25);
    var a = kx * x + t * .9 + seed * TAU, b = ky * y - t * .66 + seed * 2.4;
    var amp = 520;
    return { x: -amp * ky * Math.sin(a) * Math.sin(b), y: -amp * kx * Math.cos(a) * Math.cos(b) };
  }

  function fieldV1(look, x, y, t) {
    return look.vectorField / 100 * Math.sin(y / Math.max(1, look.fieldScale) + t * look.evolutionSpeed) * 14;
  }

  // v2: a coherent 2-D curl field used as a wind the particle is entrained by,
  // instead of a single sine used as a tiny acceleration.
  function windV2(look, x, y, t, seed) {
    var s = Math.max(12, look.fieldScale);
    var sp = t * look.evolutionSpeed * .55;
    var a = Math.sin(y / s + sp + seed * TAU);
    var b = Math.cos(x / (s * 1.7) - sp * .7 + seed * 3.1);
    var c = Math.cos(y / (s * .55) + sp * .43 + seed * 2.2);
    var mag = look.vectorField / 1000 * 700;
    return {
      x: (a * b + .35 * c) * mag,
      y: (c * a - .30 * b) * mag * .42
    };
  }

  function dragCoefficient(look, flags) {
    if (!flags.dragCurve) return look.drag / 100;
    var d = clamp(look.drag / 100, 0, 1);
    return 2.2 * Math.pow(d, 1.8);
  }

  function stepPanel(panel, dt, look) {
    var model = panel.model, flags = panel.flags;
    var v2 = model === MODEL.V2;
    var origin = panel.origin; // 'gpu' | 'wpf' | null
    var wispInSim = v2 && flags.wispInSim;
    var dragBase = dragCoefficient(look, flags);

    for (var i = panel.particles.length - 1; i >= 0; i--) {
      var p = panel.particles[i];
      p.age += dt;
      if (p.age >= p.life) { panel.particles.splice(i, 1); continue; }
      if (p.age < 0) continue; // staggered birth

      if (model === MODEL.FLOW) {
        // W1: advect the particle with a coherent wind, add its own eddy, and let it widen as it ages.
        if (flags.wind) {
          var fw = flowWind(look, p.x, p.y, panel.clock);
          var kEntrain = 1 - Math.exp(-dt / FLOW_TAU);
          p.vx += (fw.x - p.vx) * kEntrain;
          p.vy += (fw.y - p.vy) * kEntrain * FLOW_VERTICAL;
        }
        if (flags.curl) {
          var fc = flowCurl(p.x, p.y, panel.clock, p.fieldSeed);
          p.vx += fc.x * dt; p.vy += fc.y * dt;
        }
        var diff = diffusionAmount(flags);
        if (diff > 0) p.vx += (p.fieldSeed - .5) * FLOW_DIFFUSION * (diff / 55) * dt;
        if (p.kind === 'wisp') p.vy *= Math.exp(-.35 * dt);
        if (p.kind === 'flame') p.size *= 1 + .9 * dt;   // widens as it cools, ~1.4x over its life
        p.x += p.vx * dt; p.y += p.vy * dt;
        continue;
      }

      if (p.kind === 'wisp') {
        if (v2) {
          var w = windV2(look, p.x, p.y, panel.clock, p.fieldSeed);
          var wig = Math.sin(p.age * (3 + look.wispTurbulence * 5) + p.phase) * (8 + look.wispTurbulence * 46);
          p.x += (p.vx + w.x * .28 + (flags.diffusion ? (p.fieldSeed - .5) * 30 : 0)) * dt;
          p.x += wig * dt * (wispInSim ? 1 : 0);
          p.y += (p.vy + w.y * .10) * dt;
          p.vy *= Math.exp(-.35 * dt);
        } else if (origin === 'wpf') {
          var turb = look.wispTurbulence;
          p.x += (p.vx + Math.sin(p.age * (3 + turb * 7) + p.phase) * (8 + turb * 46) * Math.min(1, p.age * 2)) * dt;
          p.y += p.vy * dt;
          p.x += fieldV1(look, p.x, p.y, panel.clock) * dt * 6;
          p.vy *= Math.exp(-.35 * dt);
        } else {
          p.y += p.vy * dt;
          p.x += (p.vx + fieldV1(look, p.x, p.y, panel.clock) * 6) * dt;
          p.vy *= Math.exp(-.35 * dt);
        }
        continue;
      }

      if (p.kind === 'flame' && !(v2 && flags.flamePhysics)) {
        // Current GPU path: constant rise, no gravity, no drag, no wind.
        p.x += p.vx * dt; p.y += p.vy * dt;
        p.vx += Math.sin(p.age * 11 + p.phase) * 40 * dt;
        continue;
      }

      if (p.kind === 'flame') {
        var wf = windV2(look, p.x, p.y, panel.clock, p.fieldSeed);
        p.x += p.vx * dt; p.y += p.vy * dt;
        p.vx += (Math.sin(p.age * 11 + p.phase) * 55 + wf.x * .25) * dt;
        p.vy += (-30 + wf.y * .1) * dt;         // buoyancy, decaying with age
        p.vx *= Math.exp(-1.7 * dt);
        p.vy *= Math.exp(-.9 * dt);
        p.size += 46 * dt;                        // a flame widens as it cools
        continue;
      }

      // Common integration path (burst + held).
      p.x += p.vx * dt;
      p.y += p.vy * dt;

      if (v2) {
        var wind = windV2(look, p.x, p.y, panel.clock, p.fieldSeed);
        var entrain = 2.2;
        p.vx += wind.x * (p.kind === 'held' ? .75 : 1) * entrain * dt * 1.6;
        p.vy += wind.y * entrain * dt * .9;
        // Turbulent diffusion: each particle keeps a small personal drift, so a
        // coherent column widens with age instead of staying a rigid rod.
        if (flags.diffusion) p.vx += (p.fieldSeed - .5) * 46 * dt;
      } else {
        p.vx += fieldV1(look, p.x, p.y, panel.clock) * dt;
      }

      var heat = clamp(1 - p.age / p.life, 0, 1);
      p.vy += (look.gravity * p.grav - 38 * heat / Math.max(.2, p.mass)) * dt;
      if (p.shape === 'confetti') p.vx += Math.sin(p.age * 9 + p.phase) * 60 * dt;
      if (!v2) p.vx += Math.sin(p.y * .04 + p.phase + panel.clock * 3.2) * 14 * heat * dt;

      var k = v2 && flags.dragCurve ? dragBase * p.dragK : look.drag / 100 * p.dragK;
      var damp = Math.exp(-k * dt);
      p.vx *= damp; p.vy *= damp;

      if (v2 && flags.spin && (p.shape === 'confetti' || p.shape === 'shard')) p.angle += p.spin * dt;

      if (v2 && flags.floor && p.vy > 0 && p.y > HIT_Y && !p.bounced &&
          (p.shape === 'dot' || p.shape === 'streak' || p.shape === 'droplet' || p.shape === 'shard')) {
        p.y = HIT_Y;
        p.vy *= -.32; p.vx *= .78; p.bounced = true;
      }
    }

    // spawn whatever the pre-computed schedule has produced by now
    while (panel.nextImpact < panel.scenario.impacts.length &&
           panel.scenario.impacts[panel.nextImpact].t <= panel.clock) {
      var ev = panel.scenario.impacts[panel.nextImpact++];
      for (var b = 0; b < ev.list.length; b++) panel.particles.push(cloneParticle(ev.list[b], v2, flags, model, look));
    }
    while (panel.nextTick < panel.scenario.ticks.length &&
           panel.scenario.ticks[panel.nextTick].t <= panel.clock) {
      var tick = panel.scenario.ticks[panel.nextTick++];
      for (var c = 0; c < tick.list.length; c++) panel.particles.push(cloneParticle(tick.list[c], v2, flags, model, look));
    }
  }

  function cloneParticle(src, v2, flags, model, look) {
    var p = {
      shape: src.shape, kind: src.kind, x: src.x, y: src.y, vx: src.vx, vy: src.vy,
      life: src.life, size: src.size, mass: src.mass, grav: src.grav, dragK: src.dragK,
      color: src.color, phase: src.phase, spin: src.spin, depth: src.depth, pitch: src.pitch,
      fieldSeed: src.fieldSeed, age: 0, angle: src.phase, bounced: false
    };
    if (v2 && flags.stagger) p.age = -src.delay;
    if (v2 && flags.depth) {
      var s = 1 - .12 * src.depth;
      p.vx *= s; p.vy *= s;
      p.size *= 1 - .30 * src.depth;
    }
    if (model === MODEL.FLOW && flags.sustain !== false && src.kind === 'held' && look) {
      // Row 1: the sustained emitter is a narrow, slow, upward cone. Same birth, different aim - the burst
      // emitter that draws the impact shells keeps its wide fan, which is the whole point of splitting them.
      var speed = Math.sqrt(src.vx * src.vx + src.vy * src.vy) * HELD_SPEED;
      var off = Math.atan2(src.vx, -src.vy) * (HELD_SPREAD / Math.max(.01, look.particleSpread));
      p.vx = Math.sin(off) * speed;
      p.vy = -Math.cos(off) * speed;
    }
    return p;
  }

  function makePanel(model) {
    return {
      model: model, origin: model === MODEL.WPF ? 'wpf' : model === MODEL.GPU ? 'gpu' : null,
      flags: model === MODEL.FLOW ? flowFlags() : v2Flags(), particles: [], clock: 0, nextImpact: 0, nextTick: 0,
      scenario: null, sprites: 0
    };
  }

  function resetPanel(panel, scenario) {
    panel.particles = []; panel.clock = 0; panel.nextImpact = 0; panel.nextTick = 0;
    panel.scenario = scenario; panel.sprites = 0;
  }

  // ------------------------------------------------------------------ curves
  function sizeOverLife(p, u) {
    switch (p.shape) {
      case 'droplet': return .7 + .5 * (1 - u);
      case 'dust': return 1 + .9 * u;
      case 'confetti': return 1;
      case 'streak': return 1 - .35 * u;
      case 'flame': return .6 + Math.sin(Math.min(1, u * 1.4) * Math.PI) * .7;
      default: return 1 - .45 * u;
    }
  }

  function alphaOverLife(p, u) {
    switch (p.shape) {
      case 'dust': return Math.pow(Math.sin(Math.PI * clamp(u, 0, 1)), .8) * .92;
      case 'confetti': return Math.min(1, (1 - u) * 3.2);
      case 'droplet': return Math.pow(clamp(1 - u, 0, 1), .8);
      case 'flame': return clamp(1 - u, 0, 1);
      default: return Math.pow(clamp(1 - u, 0, 1), 1.35);
    }
  }

  // ------------------------------------------------------------------ sprites
  var glowCache = new Map();
  function glowSprite(color) {
    var key = color[0] + ',' + color[1] + ',' + color[2];
    var hit = glowCache.get(key);
    if (hit) return hit;
    if (glowCache.size > 400) glowCache.clear();
    var c = document.createElement('canvas');
    c.width = c.height = 64;
    var g = c.getContext('2d');
    var grd = g.createRadialGradient(32, 32, 0, 32, 32, 32);
    grd.addColorStop(0, 'rgba(255,255,255,.95)');
    grd.addColorStop(.18, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',.85)');
    grd.addColorStop(.45, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',.28)');
    grd.addColorStop(1, 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',0)');
    g.fillStyle = grd;
    g.fillRect(0, 0, 64, 64);
    glowCache.set(key, c);
    return c;
  }

  var cssCache = new Map();
  function css(color, alpha) {
    var key = color[0] + ',' + color[1] + ',' + color[2] + ',' + Math.round(alpha * 100);
    var hit = cssCache.get(key);
    if (hit) return hit;
    if (cssCache.size > 2000) cssCache.clear();
    var v = 'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',' + alpha.toFixed(3) + ')';
    cssCache.set(key, v);
    return v;
  }

  function drawPanel(ctx, panel, look) {
    var flow = panel.model === MODEL.FLOW;
    var v2 = panel.model === MODEL.V2 || flow;
    var flags = panel.flags;
    var wispOffset = !(v2 && flags.wispInSim) && !flow;
    panel.sprites = 0;

    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (var i = 0; i < panel.particles.length; i++) {
      var p = panel.particles[i];
      if (p.age < 0) continue;
      var u = clamp(p.age / p.life, 0, 1);
      var alpha = v2 && flags.lifeCurves ? alphaOverLife(p, u) : clamp(1 - u, 0, 1);
      var size = p.size * (v2 && flags.lifeCurves ? sizeOverLife(p, u) : 1);
      var x = p.x, y = p.y;
      if (p.kind === 'wisp') {
        if (wispOffset) {
          var amp = (5 + look.wispTurbulence * 26) * (.45 + look.wispWidth) * Math.min(1, p.age * 1.6);
          x += Math.sin(p.age * (3 + look.wispTurbulence * 5) + p.phase) * amp;
        }
        alpha = Math.min(1, u * 6) * (1 - u);
        size *= 1.6;
      }
      if (alpha <= .004) continue;
      var glow = v2 && flags.depth ? (1 - .3 * p.depth) : 1;
      var a = clamp(alpha * glow, 0, 1);

      switch (p.shape) {
        case 'streak': {
          var speed = Math.sqrt(p.vx * p.vx + p.vy * p.vy);
          var ang = speed > 1e-3 ? Math.atan2(p.vy, p.vx) : -Math.PI / 2;
          var stretch = clamp(speed * .018, .5, 7);
          var tex = glowSprite(p.color);
          ctx.save();
          ctx.translate(x, y);
          ctx.rotate(ang);
          ctx.globalAlpha = a;
          ctx.drawImage(tex, -size * 2.6, -size * 1.15, size * 2.6 * (1 + stretch * .5), size * 2.3);
          ctx.restore();
          panel.sprites++;
          break;
        }
        case 'droplet': {
          var dtex = glowSprite(p.color);
          ctx.globalAlpha = a * .5;
          ctx.drawImage(dtex, x - size * 2.6, y - size * 2.6, size * 5.2, size * 5.2);
          panel.sprites++;
          ctx.globalAlpha = a;
          ctx.fillStyle = css(p.color, .85);
          ctx.beginPath();
          ctx.ellipse(x, y, size * .62, size * .62 * (1 + clamp(Math.abs(p.vy) / 700, 0, 1.2)), 0, 0, TAU);
          ctx.fill();
          ctx.fillStyle = 'rgba(255,255,255,.75)';
          ctx.beginPath();
          ctx.arc(x - size * .18, y - size * .22, size * .16, 0, TAU);
          ctx.fill();
          panel.sprites += 2;
          break;
        }
        case 'confetti': {
          var ang2 = v2 && flags.spin ? p.angle : p.phase + p.age * 7;
          var flip = Math.cos(p.age * (v2 ? 5.5 : 7) + p.phase);
          var w = size * 2.4, h = Math.max(1, size * 1.5 * Math.abs(flip));
          ctx.save();
          ctx.translate(x, y);
          ctx.rotate(ang2);
          ctx.globalAlpha = a;
          ctx.fillStyle = css(p.color, .95);
          ctx.fillRect(-w / 2, -h / 2, w, h);
          ctx.restore();
          panel.sprites++;
          break;
        }
        case 'flame': {
          var t = u;
          var fire = t < .25 ? mix([255, 242, 204], [255, 199, 77], t / .25)
                   : t < .6 ? mix([255, 199, 77], [255, 97, 20], (t - .25) / .35)
                   : mix([255, 97, 20], [115, 8, 3], (t - .6) / .4);
          var ftex = glowSprite(fire);
          var fs = size * (.6 + Math.sin(Math.min(1, u * 1.4) * Math.PI) * .7);
          ctx.globalAlpha = a * .9;
          ctx.drawImage(ftex, x - fs * 1.5, y - fs * 1.5, fs * 3, fs * 3);
          panel.sprites++;
          break;
        }
        case 'shard': {
          var sang = p.angle + p.age * 7.3;
          ctx.globalAlpha = a;
          ctx.fillStyle = css(p.color, .9);
          ctx.save();
          ctx.translate(x, y);
          ctx.rotate(sang);
          ctx.fillRect(-size * 1.2, -size * .2, size * 2.4, size * .4);
          ctx.restore();
          panel.sprites++;
          break;
        }
        default: {
          var tex2 = glowSprite(p.color);
          var gain = p.kind === 'wisp' ? (.6 + .8 * 1.6) : 1;
          var s2 = size * 1.6;
          ctx.globalAlpha = a * gain;
          ctx.drawImage(tex2, x - s2, y - s2, s2 * 2, s2 * 2);
          panel.sprites++;
          break;
        }
      }
    }
    ctx.restore();
  }

  // ------------------------------------------------------------------ scene
  function paintScene(ctx, look) {
    var bg = ctx.createLinearGradient(0, 0, 0, STAGE_H);
    bg.addColorStop(0, '#070a12');
    bg.addColorStop(.62, '#0b1020');
    bg.addColorStop(1, '#121a2c');
    ctx.fillStyle = bg;
    ctx.fillRect(0, 0, STAGE_W, STAGE_H);

    // soft halo behind the hit line
    var halo = ctx.createRadialGradient(STAGE_W / 2, HIT_Y, 10, STAGE_W / 2, HIT_Y, STAGE_W * .55);
    halo.addColorStop(0, 'rgba(120,109,241,.20)');
    halo.addColorStop(1, 'rgba(120,109,241,0)');
    ctx.fillStyle = halo;
    ctx.fillRect(0, 0, STAGE_W, HIT_Y + 60);

    // keyboard: white keys, then black keys
    var kbTop = HIT_Y;
    var kbH = STAGE_H - HIT_Y;
    ctx.fillStyle = '#0d1220';
    ctx.fillRect(0, kbTop, STAGE_W, kbH);
    for (var w = 0; w < WHITE_COUNT; w++) {
      var g = ctx.createLinearGradient(0, kbTop, 0, STAGE_H);
      g.addColorStop(0, '#e8edf7');
      g.addColorStop(.18, '#cfd7e6');
      g.addColorStop(1, '#6f7a8d');
      ctx.fillStyle = g;
      ctx.fillRect(w * WHITE_W + .6, kbTop, WHITE_W - 1.2, kbH);
    }
    for (var p = FIRST_PITCH; p < FIRST_PITCH + KEY_COUNT; p++) {
      if (!isBlack(p)) continue;
      var bx = keyCenter(p);
      var bw = WHITE_W * .52, bh = kbH * .62;
      var bg2 = ctx.createLinearGradient(0, kbTop, 0, kbTop + bh);
      bg2.addColorStop(0, '#1b2436');
      bg2.addColorStop(1, '#05070c');
      ctx.fillStyle = bg2;
      ctx.fillRect(bx - bw / 2, kbTop, bw, bh);
    }

    // hit line filament
    var line = ctx.createLinearGradient(0, 0, STAGE_W, 0);
    line.addColorStop(0, 'rgba(155,131,255,0)');
    line.addColorStop(.2, 'rgba(155,131,255,.55)');
    line.addColorStop(.5, 'rgba(220,232,255,.9)');
    line.addColorStop(.8, 'rgba(155,131,255,.55)');
    line.addColorStop(1, 'rgba(155,131,255,0)');
    ctx.fillStyle = line;
    ctx.fillRect(0, kbTop - 1, STAGE_W, 2);
  }

  // Simple note roll: enough context for the eye to judge "does the effect
  // compete with the notes", without pretending to be the real note renderer.
  function drawNotes(ctx, scenario, clock, look) {
    var speed = 258;                  // GpuLook.SongFallSpeed default
    var noteWidth = (STAGE_W / KEY_COUNT) * .8;
    ctx.save();
    for (var i = 0; i < scenario.notes.length; i++) {
      var note = scenario.notes[i];
      var bottom = HIT_Y - (note.start - clock) * speed;
      var top = bottom - (note.end - note.start) * speed;
      if (top > HIT_Y || bottom < 0) continue;
      var sounding = note.start <= clock && note.end > clock;
      var c = noteColor(note.pitch);
      var opacity = sounding ? .95 : .62;
      var x = keyCenter(note.pitch) - noteWidth / 2;
      var b = Math.min(bottom, HIT_Y + 2), t = Math.max(top, -40);
      if (b - t < 1) continue;
      var grad = ctx.createLinearGradient(0, t, 0, b);
      grad.addColorStop(0, 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + (opacity * .55).toFixed(3) + ')');
      grad.addColorStop(1, 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + opacity.toFixed(3) + ')');
      ctx.fillStyle = grad;
      ctx.fillRect(x, t, noteWidth, b - t);
      // bright head
      ctx.fillStyle = 'rgba(255,255,255,' + (opacity * .55).toFixed(3) + ')';
      ctx.fillRect(x, b - 2.5, noteWidth, 1.6);
      // sounding key glow
      if (sounding) {
        var glow = ctx.createRadialGradient(keyCenter(note.pitch), HIT_Y, 1, keyCenter(note.pitch), HIT_Y, noteWidth * 2.4);
        glow.addColorStop(0, 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',.55)');
        glow.addColorStop(1, 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',0)');
        ctx.fillStyle = glow;
        ctx.fillRect(keyCenter(note.pitch) - noteWidth * 2.4, HIT_Y - noteWidth * 2.4, noteWidth * 4.8, noteWidth * 2.4);
      }
    }
    ctx.restore();
  }

  function render(ctx, panel, look) {
    paintScene(ctx, look);
    drawNotes(ctx, panel.scenario, panel.clock, look);
    drawPanel(ctx, panel, look);
  }

  // ------------------------------------------------------------------ harness
  function createLab(options) {
    options = options || {};
    var lab = {
      w: STAGE_W, h: STAGE_H, hitY: HIT_Y,
      look: Object.assign({}, DEFAULT_LOOK),
      family: options.family || 'Embers',
      mode: options.mode || 'ab', // 'ab' = current vs v2, 'parity' = GPU vs WPF
      panels: [makePanel(MODEL.GPU), options.mode === 'flow' ? makePanel(MODEL.FLOW) : makePanel(MODEL.V2)],
      playing: true, loop: 0, speed: 1,
      scenario: null
    };
    lab.rebuild = function () {
      lab.scenario = buildScenario(options.seed || 20261019, lab.look, lab.family);
      lab.panels[0].model = MODEL.GPU;
      lab.panels[0].origin = 'gpu';
      lab.panels[1].model = lab.mode === 'parity' ? MODEL.WPF : lab.mode === 'flow' ? MODEL.FLOW : MODEL.V2;
      lab.panels[1].origin = lab.mode === 'parity' ? 'wpf' : null;
      resetPanel(lab.panels[0], lab.scenario);
      resetPanel(lab.panels[1], lab.scenario);
    };
    lab.flags = function (name, value) { lab.panels[1].flags[name] = value; };
    lab.step = function (dt) {
      dt = clamp(dt, 0, .05) * lab.speed;
      if (!lab.scenario) lab.rebuild();
      for (var i = 0; i < lab.panels.length; i++) {
        var panel = lab.panels[i];
        panel.clock += dt;
        stepPanel(panel, dt, lab.look);
      }
      if (lab.panels[0].clock >= lab.scenario.duration) {
        if (lab.playing) { lab.rebuild(); lab.loop++; }
      }
    };
    lab.rebuild();
    return lab;
  }

  var api = {
    TAU: TAU, STAGE_W: STAGE_W, STAGE_H: STAGE_H, HIT_Y: HIT_Y,
    DEFAULT_LOOK: DEFAULT_LOOK, FAMILY: FAMILY, HELD: HELD, MODEL: MODEL,
    keyCenter: keyCenter, noteColor: noteColor,
    buildScenario: buildScenario, makePanel: makePanel, resetPanel: resetPanel,
    stepPanel: stepPanel, drawPanel: drawPanel, render: render, paintScene: paintScene, drawNotes: drawNotes,
    createLab: createLab, v2Flags: v2Flags, flowFlags: flowFlags, cloneParticle: cloneParticle,
    flowWind: flowWind, flowCurl: flowCurl, diffusionAmount: diffusionAmount,
    FLOW_TAU: FLOW_TAU, FLOW_VERTICAL: FLOW_VERTICAL, FLOW_DIFFUSION: FLOW_DIFFUSION,
    HELD_SPREAD: HELD_SPREAD, HELD_SPEED: HELD_SPEED,
    sizeOverLife: sizeOverLife, alphaOverLife: alphaOverLife
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  global.ParticleLab = api;
})(typeof window !== 'undefined' ? window : globalThis);
