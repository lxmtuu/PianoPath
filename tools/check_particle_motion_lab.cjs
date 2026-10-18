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

const lab = require(path.join(__dirname, '..', 'docs', 'prototypes', 'particle-motion-lab', 'lab.js'));

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
console.log('wisp parity (GPU draw offset vs WPF integrated sway)');
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
  check(`the two shipping paths disagree by > 6 px on the same wisp (${(gpu - wpf).toFixed(1)} px)`,
    gpu !== null && wpf !== null && Math.abs(gpu - wpf) > 6);
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

// ---------------------------------------------------------------- summary
console.log('');
if (failures) {
  console.log(`${failures} check(s) failed`);
  process.exit(1);
}
console.log('all checks passed');
