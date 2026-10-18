#!/usr/bin/env node
/*
 * Headless checks for the damper-bar light prototype.
 *
 * The prototype contrasts today's boolean, un-eased pedal light with a
 * continuous, mechanically shaped one. These checks pin the numbers the
 * analysis quotes: two visible levels today, many proposed, a dead zone to
 * 25% travel, full lift at 50%, asymmetric smoothing, a core that travels,
 * and per-key damper ticks that survive a pedal release while a key is held.
 *
 * Usage: node tools/check_damper_light.cjs
 */
'use strict';

const path = require('path');
const fs = require('fs');

let failures = 0;
function check(name, condition, detail) {
  if (condition) console.log(`  PASS  ${name}`);
  else {
    failures++;
    console.log(`  FAIL  ${name}${detail ? ' - ' + detail : ''}`);
  }
}

function makeCtx() {
  return {
    calls: 0,
    save() {}, restore() {}, translate() {}, rotate() {}, scale() {},
    beginPath() {}, closePath() {}, moveTo() {}, lineTo() {}, quadraticCurveTo() {},
    fill() {}, stroke() {}, ellipse() {}, arc() {},
    fillRect() { this.calls++; }, clearRect() {}, fillText() {},
    createLinearGradient() { return { addColorStop() {} }; },
    createRadialGradient() { return { addColorStop() {} }; },
    setLineDash() {},
    fillStyle: '', strokeStyle: '', lineWidth: 1, font: '', globalAlpha: 1
  };
}

const REPO = path.join(__dirname, '..');
const D = require(path.join(REPO, 'docs', 'prototypes', 'particle-motion-lab', 'damper.js'));

function run(engine, seconds, dt = 1 / 60) {
  for (let i = 0; i < Math.round(seconds / dt); i++) D.step(engine, dt);
  return engine;
}

console.log('damper light model');
{
  const m = D.measure();

  check('today exposes only two pedal levels', m.currentLevels === 2, `${m.currentLevels}`);
  check(`proposed exposes a continuous range (${m.proposedLevels})`, m.proposedLevels >= 20);

  const dead = [0, .05, .12, .2, .25].every(cc => D.smoothstep(D.CURVE.free, D.CURVE.full, cc) === 0);
  check('no light below a quarter of pedal travel', dead);
  check('full lift at half travel (the audio catch point)',
    D.smoothstep(D.CURVE.free, D.CURVE.full, .5) === 1);

  let monotonic = true, previous = -1;
  for (let cc = 0; cc <= 1.0001; cc += .01) {
    const lift = D.smoothstep(D.CURVE.free, D.CURVE.full, cc);
    if (lift < previous - 1e-9) monotonic = false;
    previous = lift;
  }
  check('lift never goes backwards as the pedal goes down', monotonic);

  check(`10-90% rise is a real ramp (${Math.round(m.rise * 1000)} ms)`,
    m.rise >= .09 && m.rise <= .16);
  check(`10-90% release is a little slower (${Math.round(m.fall * 1000)} ms)`,
    m.fall >= .13 && m.fall <= .22);

  check(`one 60 fps frame moves at most ${m.maxStep.toFixed(2)} of the target`,
    m.maxStep < .3 && m.maxRelease < .3);

  const up = D.shape(0, true), down = D.shape(1, true);
  check(`strip thickens ${up.depthPx.toFixed(1)} -> ${down.depthPx.toFixed(1)} px`,
    Math.abs(up.depthPx - 7.2) < .01 && Math.abs(down.depthPx - 19.2) < .01);
  check('alpha runs .18 -> .50',
    Math.abs(up.alpha - .18) < 1e-9 && Math.abs(down.alpha - .50) < 1e-9);

  check(`hot core travels ${m.coreTravel45.toFixed(1)} px at 45 deg`,
    m.coreTravel45 > 7.5 && m.coreTravel45 < 9);
  check(`hot core travels ${m.coreTravel90.toFixed(1)} px at 90 deg`,
    m.coreTravel90 > 6 && m.coreTravel90 < 7.5);
  check('the core is pinned today',
    D.shape(1, false).coreOffset === 0);
}

console.log('per-key damper ticks');
{
  const notes = [{ start: 0, end: 2, pitch: 60 }];
  const engine = D.createEngine({ notes });
  engine.manual = true;

  run(engine, .5);
  check('with the pedal up only the held key lifts',
    engine.tick[60] > .9 && engine.tick[64] < .05,
    `60:${engine.tick[60].toFixed(2)} 64:${engine.tick[64].toFixed(2)}`);

  engine.cc = 1;
  run(engine, .4);
  check('full pedal lifts every key',
    engine.tick[21] > .9 && engine.tick[64] > .9 && engine.tick[108] > .9);

  engine.cc = 0;
  run(engine, .05);
  check('the comb does not snap down in one frame',
    engine.tick[64] > .5, engine.tick[64].toFixed(2));

  run(engine, .6);
  check('released keys fall within the felt time',
    engine.tick[64] < .05 && engine.tick[60] > .9,
    `60:${engine.tick[60].toFixed(2)} 64:${engine.tick[64].toFixed(2)}`);

  engine.t = 2.5;
  run(engine, .3);
  check('a released held-key tick falls too', engine.tick[60] < .1, engine.tick[60].toFixed(2));
}

console.log('per-key cells (SeeMusic-style)');
{
  const a = D.pitchColor(21), b = D.pitchColor(108), c = D.pitchColor(60);
  check('pitch colours stay in range and are deterministic',
    [a, b, c].every(v => v.length === 3 && v.every(x => x >= 0 && x <= 255 && x === Math.round(x)))
    && D.pitchColor(60).join() === c.join());
  check('different pitches get different tints', a.join() !== b.join(), `${a} / ${b}`);
  check('the cell tint is the light colour mixed 22% toward the pitch',
    D.mixColor([0, 0, 0], [100, 200, 50], .22).join() === '22,44,11');

  const styles = [];
  const ctx = makeCtx();
  const rect = ctx.fillRect.bind(ctx);
  ctx.fillRect = function (x, y, w, h) { styles.push(this.fillStyle); rect(x, y, w, h); };

  const down = run(Object.assign(D.createEngine(), { cc: 1, manual: true }), .4);
  const up = run(Object.assign(D.createEngine(), { manual: true }), .4);
  const base = styles.length;
  D.drawPanel(ctx, up, 'proposed', 960, 250);
  const upRects = styles.length - base;
  D.drawPanel(ctx, down, 'proposed', 960, 250);
  const downRects = styles.length - base - upRects;

  check('pedal down paints a cell for every key', downRects - upRects >= 176,
    `${upRects} up / ${downRects} down`);
  const alphas = styles.map(v => { const m = String(v).match(/,([0-9.]+)\)$/); return m ? +m[1] : NaN; });
  check('cell body and top edge get their own alphas',
    alphas.some(a => a > .46 && a < .48) && alphas.some(a => a > .69 && a < .71));
  check('cells only appear where a damper is up', down.tick[108] > .9 && up.tick[108] < .02);
}

console.log('determinism and draw path');
{
  const a = D.createEngine(), b = D.createEngine();
  for (let i = 0; i < 240; i++) { D.step(a, 1 / 60); D.step(b, 1 / 60); }
  const sample = e => [e.cc, e.lift, e.tick[60], e.tick[72]].map(v => v.toFixed(9)).join(',');
  check('two runs with the same input agree frame for frame', sample(a) === sample(b));

  const ctx = makeCtx();
  let threw = null;
  try {
    D.drawPanel(ctx, a, 'current', 960, 250);
    D.drawPanel(ctx, a, 'proposed', 960, 250);
    D.drawGraph(ctx, a, D.buildTrace(), 960, 150);
  } catch (error) { threw = error; }
  check('both panels and the graph draw without throwing', threw === null, threw && threw.message);
  check('the draw path emits geometry', ctx.calls > 100, `${ctx.calls} fills`);

  const page = path.join(REPO, 'docs', 'prototypes', 'particle-motion-lab', 'damper.html');
  const html = fs.readFileSync(page, 'utf8');
  check('the page loads the model', /src="damper\.js"/.test(html) && /DamperLight/.test(html));
}

console.log('');
if (failures) {
  console.log(`${failures} check(s) failed`);
  process.exit(1);
}
console.log('all checks passed');
