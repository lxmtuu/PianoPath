#!/usr/bin/env node
/*
 * Headless smoke test for docs/prototypes/particle-motion-lab.
 *
 * The lab is a design prototype, not product code, but a prototype that
 * throws in the browser is worse than no prototype. This script exercises
 * the model (spawn schedule, integrator, life curves) and the draw path with
 * a recording 2D-context stub, and it fails if the two models stop being
 * clearly different where the review claims they are.
 *
 * Usage: node tools/check_particle_motion_lab.cjs
 */
'use strict';

const path = require('path');

let failures = 0;
function check(name, condition, detail) {
  if (condition) {
    console.log(`  PASS  ${name}`);
  } else {
    failures++;
    console.log(`  FAIL  ${name}${detail ? ' - ' + detail : ''}`);
  }
}

// ---------------------------------------------------------------- fake DOM
function makeCtx() {
  const ctx = {
    calls: 0,
    save() {}, restore() {}, translate() {}, rotate() {},
    beginPath() {}, ellipse() {}, arc() {}, fill() {},
    fillRect() { this.calls++; },
    drawImage() { this.calls++; },
    createLinearGradient() { return { addColorStop() {} }; },
    createRadialGradient() { return { addColorStop() {} }; },
    fillStyle: '', globalAlpha: 1, globalCompositeOperation: 'source-over'
  };
  return ctx;
}

global.document = {
  createElement(tag) {
    if (tag !== 'canvas') throw new Error('unexpected element ' + tag);
    return {
      width: 0, height: 0,
      getContext() { return makeCtx(); }
    };
  }
};

const PROTOTYPE = path.join(__dirname, '..', 'docs', 'prototypes', 'particle-motion-lab');
const lab = require(path.join(PROTOTYPE, 'lab.js'));
const seeMusic = require(path.join(PROTOTYPE, 'seemusic.js'));

const LOOK = Object.assign({}, lab.DEFAULT_LOOK);
const STEP = 1 / 120;

function scenarioFor(family, look) {
  return lab.buildScenario(20261019, look || LOOK, family);
}

function freshPanel(model, scenario) {
  const panel = lab.makePanel(model);
  lab.resetPanel(panel, scenario || { duration: 0, impacts: [], ticks: [] });
  return panel;
}

function finitePosition(p) {
  return Number.isFinite(p.x) && Number.isFinite(p.y) && Number.isFinite(p.age);
}

// ---------------------------------------------------------------- 1. scenario
console.log('scenario');
{
  const families = Object.keys(lab.FAMILY);
  for (const family of families) {
    const s = scenarioFor(family);
    const bad = [];
    s.impacts.concat(s.ticks).forEach(group => {
      (group.list || []).forEach(p => { if (!finitePosition(p)) bad.push(p); });
    });
    check(`${family}: ${s.impacts.length} impacts, ${s.ticks.length} emitter ticks, all finite`,
      s.impacts.length === s.notes.length && s.ticks.length > 0 && bad.length === 0);
  }

  const s = scenarioFor('Embers');
  const windowStart = 4.5, windowEnd = 5.5;
  const heldPerPitch = new Map();
  for (const tick of s.ticks) {
    if (tick.t < windowStart || tick.t >= windowEnd) continue;
    for (const p of tick.list) {
      if (p.kind !== 'held') continue;
      heldPerPitch.set(p.pitch, (heldPerPitch.get(p.pitch) || 0) + 1);
    }
  }
  const expected = LOOK.particleAmount * 1.3 * (windowEnd - windowStart);
  const counts = [...heldPerPitch.values()];
  check(`held emitter rate ~ ${expected.toFixed(0)}/key/s (got ${counts.join(', ')})`,
    counts.length === 4 && counts.every(c => Math.abs(c - expected) <= 2));
}

// ---------------------------------------------------------------- 2. determinism
console.log('determinism');
{
  const run = () => {
    const s = scenarioFor('Confetti');
    const panel = freshPanel(lab.MODEL.V2, s);
    for (let i = 0; i < 600; i++) { panel.clock += STEP; lab.stepPanel(panel, STEP, LOOK); }
    return panel.particles.map(p => `${p.x.toFixed(5)},${p.y.toFixed(5)},${p.age.toFixed(5)}`).join('|');
  };
  const a = run(), b = run();
  check('same seed + same dt -> identical particle state', a === b, `${a.length} vs ${b.length}`);
}

// ---------------------------------------------------------------- 3. stability
console.log('integrator stability (8 s, every family, both models)');
{
  let maxAlive = 0;
  for (const family of Object.keys(lab.FAMILY)) {
    for (const model of [lab.MODEL.GPU, lab.MODEL.V2]) {
      const s = scenarioFor(family);
      const panel = freshPanel(model, s);
      let bad = 0;
      for (let i = 0; i < 960; i++) {
        panel.clock += STEP;
        lab.stepPanel(panel, STEP, LOOK);
        maxAlive = Math.max(maxAlive, panel.particles.length);
        for (const p of panel.particles) if (!finitePosition(p)) bad++;
        if (bad) break;
      }
      check(`${family} / ${model}: finite positions, ${panel.particles.length} alive at t=8s`, bad === 0);
    }
  }
  check(`peak population stays bounded (${maxAlive})`, maxAlive < 6000);
}

// ---------------------------------------------------------------- 4. speed of the controls
console.log('control response');
{
  const singleParticle = () => ({
    shape: 'streak', kind: 'burst', x: 480, y: lab.HIT_Y - 1, vx: 0, vy: -210,
    life: .9, size: 1.6, mass: .6, grav: 1, dragK: 1, color: [255, 255, 255],
    phase: 0, spin: 0, depth: 0, fieldSeed: .25, delay: 0
  });

  function apex(model, look, flags) {
    const panel = freshPanel(model);
    panel.flags = lab.v2Flags(flags);
    panel.particles.push(lab.cloneParticle(singleParticle(), model === lab.MODEL.V2, panel.flags));
    let minY = panel.particles[0].y;
    for (let i = 0; i < 120; i++) {
      panel.clock += STEP;
      lab.stepPanel(panel, STEP, look);
      if (panel.particles[0]) minY = Math.min(minY, panel.particles[0].y);
    }
    return panel.particles[0] ? panel.particles[0].y : minY;
  }

  const low = Object.assign({}, LOOK, { drag: 0 });
  const high = Object.assign({}, LOOK, { drag: 100 });
  const v1Delta = Math.abs(apex(lab.MODEL.GPU, low) - apex(lab.MODEL.GPU, high));
  const v2Delta = Math.abs(apex(lab.MODEL.V2, low) - apex(lab.MODEL.V2, high));
  check(`drag 0 -> 100 moves the end point further in v2 (${v1Delta.toFixed(1)} px -> ${v2Delta.toFixed(1)} px)`,
    v2Delta > v1Delta * 1.5);

  function drift(model, look, flags) {
    const panel = freshPanel(model);
    panel.flags = lab.v2Flags(flags);
    panel.particles.push(lab.cloneParticle(singleParticle(), model === lab.MODEL.V2, panel.flags));
    const x0 = panel.particles[0].x;
    let peak = 0;
    for (let i = 0; i < 108; i++) {
      panel.clock += STEP;
      lab.stepPanel(panel, STEP, look);
      if (panel.particles[0]) peak = Math.max(peak, Math.abs(panel.particles[0].x - x0));
    }
    return peak;
  }

  const v1Drift = drift(lab.MODEL.GPU, LOOK);
  const v2Drift = drift(lab.MODEL.V2, LOOK);
  check(`default vector field now moves an ember (${v1Drift.toFixed(1)} px -> ${v2Drift.toFixed(1)} px)`,
    v2Drift > v1Drift * 3 && v2Drift < 60);
}

// ---------------------------------------------------------------- 5. v2 features
console.log('v2 features');
{
  const s = scenarioFor('Embers');
  const panel = freshPanel(lab.MODEL.V2, s);
  panel.flags = lab.v2Flags({ stagger: true });
  for (let i = 0; i < 30; i++) { panel.clock += STEP; lab.stepPanel(panel, STEP, LOOK); }
  check('staggered birth leaves some particles not yet visible',
    panel.particles.some(p => p.age < 0));

  check('dot shrinks and fades faster than linear by mid-life',
    lab.alphaOverLife({ shape: 'dot' }, .5) < .42 && lab.sizeOverLife({ shape: 'dot' }, .5) < .82);
  check('dust grows and peaks its alpha at mid-life',
    lab.alphaOverLife({ shape: 'dust' }, .5) > .85 && lab.sizeOverLife({ shape: 'dust' }, .5) > 1.4);

  const noSpin = freshPanel(lab.MODEL.V2, s);
  noSpin.flags = lab.v2Flags({ spin: false });
  check('spin flag reaches the panel flags', noSpin.flags.spin === false);
}

// ---------------------------------------------------------------- 6. wisp parity
// W0 (2026-10-20) shipped the shared curve: the product's stage, AR quads and alignment review all call
// Stage/WispMotion.cs, and the software stage no longer integrates the sway into velocity. The lab keeps
// the pre-W0 WPF integrator on purpose — it is the "before" picture the review measured — so this section
// now checks (a) the lab still reproduces that historical 10.6 px gap, and (b) the curve the lab draws as
// "current GPU" is the curve the product actually ships.
console.log('wisp parity (lab keeps the pre-W0 WPF sway as the "before" picture)');
{
  function wispDrawnX(model, seconds) {
    const panel = freshPanel(model);
    const src = {
      shape: 'dot', kind: 'wisp', x: 400, y: lab.HIT_Y - 2, vx: 0, vy: -LOOK.wispSpeed,
      life: 1.6, size: 1, mass: 1.5, grav: 0, dragK: 0, color: [255, 255, 255],
      phase: .3, spin: 0, depth: 0, fieldSeed: .5, delay: 0
    };
    panel.particles.push(lab.cloneParticle(src, false, lab.v2Flags()));
    for (let i = 0; i < Math.round(seconds / STEP); i++) {
      panel.clock += STEP;
      lab.stepPanel(panel, STEP, LOOK);
    }
    const p = panel.particles[0];
    if (!p) return null;
    const amp = (5 + LOOK.wispTurbulence * 26) * (.45 + LOOK.wispWidth) * Math.min(1, p.age * 1.6);
    const offset = model === lab.MODEL.GPU ? Math.sin(p.age * (3 + LOOK.wispTurbulence * 5) + p.phase) * amp : 0;
    return p.x + offset;
  }
  const gpu = wispDrawnX(lab.MODEL.GPU, .8);
  const wpf = wispDrawnX(lab.MODEL.WPF, .8);
  check(`the pre-W0 WPF integrator still disagrees with the GPU draw offset by > 6 px (${(gpu - wpf).toFixed(1)} px)`,
    gpu !== null && wpf !== null && Math.abs(gpu - wpf) > 6);

  // The product has one curve now; the lab's "current GPU" column must be that curve, not a stale copy.
  const fs = require('fs');
  const curve = fs.readFileSync(path.join(__dirname, '..', 'Stage', 'WispMotion.cs'), 'utf8');
  const numbers = [
    ['amplitude grows with turbulence at 26 px/unit', /T\(turbulence\) \* 26/],
    ['amplitude grows with width from a .45 px base', /\.45f \+ W\(width\)/],
    ['frequency is 3 + 5 rad/s per unit of turbulence', /3 \+ T\(turbulence\) \* 5/],
    ['the sway fades in over 1/1.6 s', /\* 1\.6f/],
  ];
  for (const [what, pattern] of numbers) check(`WispMotion.cs still carries ${what}`, pattern.test(curve));
  check('WispMotion.cs clamps turbulence and width instead of extrapolating',
    /Math\.Clamp\(turbulence, 0, 1\)/.test(curve) && /Math\.Clamp\(width, 0, 1\)/.test(curve));
}

// ---------------------------------------------------------------- 7. draw path
console.log('draw path');
{
  const s = scenarioFor('Splash');
  const panel = freshPanel(lab.MODEL.V2, s);
  for (let i = 0; i < 240; i++) { panel.clock += STEP; lab.stepPanel(panel, STEP, LOOK); }
  const ctx = makeCtx();
  let threw = null;
  try {
    lab.paintScene(ctx, LOOK);
    lab.drawPanel(ctx, panel, LOOK);
  } catch (error) { threw = error; }
  check(`draws ${panel.particles.length} particles without throwing`,
    threw === null && panel.sprites > 0, threw && threw.message);
}

// ---------------------------------------------------------------- 8. see-style playground
console.log('see-style motion playground');
{
  const presets = seeMusic.PRESET_ORDER;
  check(`nine documented-style presets (${presets.length})`, presets.length === 9,
    presets.join(', '));
  check('every preset exists in the recipe table',
    presets.every(name => seeMusic.PRESETS[name]));

  function run(preset, seconds, mutate) {
    const engine = seeMusic.createEngine({ seed: 20261019, preset });
    if (mutate) mutate(engine);
    const dt = 1 / 120;
    for (let i = 0; i < Math.round(seconds / dt); i++) seeMusic.step(engine, dt);
    return engine;
  }

  // Sample through the whole loop: short-lived presets are empty at some instants by design.
  let bad = 0;
  const peaks = [];
  for (const preset of presets) {
    let peak = 0;
    const engine = seeMusic.createEngine({ seed: 20261019, preset });
    for (let i = 0; i < Math.round(5.8 / (1 / 120)); i++) {
      seeMusic.step(engine, 1 / 120);
      peak = Math.max(peak, engine.particles.length);
      for (const p of engine.particles) {
        if (![p.x, p.y, p.age].every(Number.isFinite)) bad++;
      }
    }
    peaks.push(`${preset}:${peak}`);
  }
  check(`all presets stay finite and emit (${peaks.join(' ')})`, bad === 0);

  const straight = run('Straight', 2.0);
  const twirl = run('Twirl', 2.0);
  function spread(engine) {
    const xs = engine.particles.filter(p => p.layer === 'particle').map(p => p.x);
    if (xs.length < 4) return 0;
    const mean = xs.reduce((a, b) => a + b, 0) / xs.length;
    return Math.sqrt(xs.reduce((a, b) => a + (b - mean) ** 2, 0) / xs.length);
  }
  check(`twirl widens the cloud against straight (${spread(straight).toFixed(1)} px -> ${spread(twirl).toFixed(1)} px)`,
    spread(twirl) > spread(straight) * 1.15);

  const noParticles = run('Default', 2.0, e => { e.particlesOn = false; });
  check('the Particles layer can be switched off on its own',
    noParticles.particles.every(p => p.layer !== 'particle'));

  const streakOnly = run('Long streaklets', 2.0);
  check('Long streaklets really removes the small particles',
    streakOnly.particles.every(p => p.layer !== 'particle') &&
    streakOnly.particles.some(p => p.layer === 'streaklet'));

  // Magnetism needs two or more keys sounding at once, so sample inside the chord.
  const still = run('Default', 2.6, e => { e.magnetism = 0; });
  const pulled = run('Default', 2.6, e => { e.magnetism = 1; });
  // Magnetism means "particles of one key curve toward the other sounding keys",
  // so measure how close a particle gets to a *different* sounding key.
  function meanDistanceToOtherKey(engine) {
    const anchors = engine.activeAnchors || [];
    const parts = engine.particles.filter(p => p.layer === 'particle');
    if (!parts.length || anchors.length < 2) return 0;
    let total = 0, n = 0;
    for (const p of parts) {
      let best = Infinity;
      for (const a of anchors) if (a.pitch !== p.pitch) best = Math.min(best, Math.abs(a.x - p.x));
      if (Number.isFinite(best)) { total += best; n++; }
    }
    return n ? total / n : 0;
  }
  const dStill = meanDistanceToOtherKey(still), dPulled = meanDistanceToOtherKey(pulled);
  check(`magnetism curves particles toward the other keys (${dStill.toFixed(1)} -> ${dPulled.toFixed(1)} px)`,
    dStill > 0 && dPulled < dStill * .9);

  function meanDrift(engine) {
    return engine.particles
      .filter(p => p.layer === 'particle')
      .reduce((a, p) => a + Math.abs(p.x - p.anchorX), 0) / Math.max(1, engine.particles.length);
  }
  const fast = run('Default', 1.0, e => { e.speed = 2; });
  const slow = run('Default', 1.0, e => { e.speed = .5; });
  check(`speed changes how far a particle travels (${meanDrift(slow).toFixed(1)} -> ${meanDrift(fast).toFixed(1)} px)`,
    meanDrift(fast) > meanDrift(slow) * 1.5);

  const a = run('High turbulence', 2.0);
  const b = run('High turbulence', 2.0);
  const sig = e => e.particles.map(p => `${p.x.toFixed(4)},${p.y.toFixed(4)}`).join('|');
  check('the playground is deterministic for a seed', sig(a) === sig(b));

  const ctx = makeCtx();
  let drawThrew = null;
  try { seeMusic.draw(ctx, run('Smoke', 3.0)); } catch (error) { drawThrew = error; }
  check('draws every layer without throwing', drawThrew === null, drawThrew && drawThrew.message);
}

// ---------------------------------------------------------------- summary
console.log('');
if (failures) {
  console.log(`${failures} check(s) failed`);
  process.exit(1);
}
console.log('all checks passed');
