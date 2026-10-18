#!/usr/bin/env node
/*
 * Headless checks for docs/prototypes/note-view-lab.
 *
 * The lab is a sketch, but its geometry has to stay the geometry the audit
 * measured. These checks pin the numbers the document quotes and exercise the
 * draw path with a recording 2D-context stub.
 *
 * Usage: node tools/check_note_view_lab.cjs
 */
'use strict';

const path = require('path');

let failures = 0;
function check(name, ok, detail) {
  if (ok) console.log(`  PASS  ${name}`);
  else { failures++; console.log(`  FAIL  ${name}${detail ? ' - ' + detail : ''}`); }
}
function near(a, b, eps) { return Math.abs(a - b) <= (eps == null ? 1e-6 : eps); }

const LAB = require(path.join(__dirname, '..', 'docs', 'prototypes', 'note-view-lab', 'note-view.js'));
const D = LAB.DEFAULTS;
const M = LAB.MODELS;

function state(over) {
  return Object.assign({
    height: 1080, width: 1920, keyboardScale: 100, widthPct: 80, widthMode: 'lane',
    minAspect: 0, effects: true, arFall: D.arFall, arLookahead: D.arLookahead, arShare: D.arShare
  }, over || {});
}

console.log('keyboard geometry');
const K = LAB.KEYBOARD;
check('88 keys, 52 white, first A0', K.count === 88 && K.whites.length === 52 && K.first === 21);
check('white key is 1/52 of the width', near(K.span(60).width, 1 / 52));
check('black key is 0.52 of a white key', near(K.span(61).width, .52 / 52));
check('C# sits right of C and left of D', K.center(61) > K.center(60) && K.center(61) < K.center(62));
check('pitch names', LAB.noteText(60) === 'C4' && LAB.noteText(61) === 'C#4');

console.log('layout at 1920x1080');
const L = LAB.layoutFor(state());
check('keyboard 221.4 px, hit 858.6 px', near(L.keyboardHeight, 221.4) && near(L.hitY, 858.6));
check('lane 21.818, white 36.923, black 19.2',
  near(L.lane, 1920 / 88) && near(L.whiteWidth, 1920 / 52) && near(L.blackWidth, 1920 / 52 * .52));
check('lookahead 3.328 s', near(L.lookahead, 858.6 / 258));

console.log('note width: lane vs key span');
const nw = LAB.noteWidthPixels(60, 80, 'lane', L);
check('stage note 17.455 px at 80%', near(nw, 17.454545, 1e-5));
check('47.27% of a white key', near(nw / L.whiteWidth * 100, 47.2727, 1e-3));
check('90.91% of a black key', near(nw / L.blackWidth * 100, 90.9091, 1e-3));
const ar = K.span(60).width * 1920 * .86;
check('AR note 31.754 px', near(ar, 31.753846, 1e-5));
check('AR is 1.819x the stage note', near(ar / nw, 1.819, 1e-3));
check('span mode white 29.538 px at 80%', near(LAB.noteWidthPixels(60, 80, 'span', L), 29.53846, 1e-4));
check('span mode black 15.36 px at 80%', near(LAB.noteWidthPixels(61, 80, 'span', L), 15.36, 1e-9));
check('clamp leaves 80% unchanged', near(LAB.noteWidthPixels(60, 80, 'clamped', L), nw));
check('clamp stops 100% at 98% of the black key',
  near(LAB.noteWidthPixels(60, 100, 'clamped', L), .98 * L.blackWidth));

console.log('bar length: the 0.9*hiY clamp');
const st = LAB.statsFor(1080, state());
check('longest stage bar at 1080p is 3.003 s', near(st.longest, 3.00287, 1e-4));
check('longest stage bar at 900p is 2.504 s', near(LAB.statsFor(900, state()).longest, 2.50368, 1e-4));
check('longest stage bar at 1440p is 4.236 s', near(LAB.statsFor(1440, state()).longest, 4.23566, 1e-4));
const n4 = LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, 0, Object.assign(state(), { layout: L }));
check('4 s note at 1080p is clamped to 3.003 s', near(n4.barSeconds, 3.00287, 1e-4) && n4.clipped);
check('4 s note at 1440p keeps 4.000 s',
  near(LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, 0, Object.assign(state({ height: 1440, width: 2560 }), { layout: LAB.layoutFor(state({ height: 1440, width: 2560 })) })).barSeconds, 4, 1e-9));
check('4 s note at 900p is clamped to 2.504 s',
  near(LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, 0, Object.assign(state({ height: 900, width: 1600 }), { layout: LAB.layoutFor(state({ height: 900, width: 1600 })) })).barSeconds, 2.50368, 1e-4));
const topAtOnset = L.hitY - n4.height;
check('clamped bar keeps a 10% gap at the top', near(topAtOnset, L.hitY * .1, 1e-6));

console.log('entry and exit timing');
const appear = LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, -L.lookahead, Object.assign(state(), { layout: L }));
check('stage note enters with bottom at 0', near(appear.bottom, 0, 1e-9));
const before = LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, -L.lookahead - .01, Object.assign(state(), { layout: L }));
check('stage note is absent just before lookahead', !before.visible);
const after = LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, 3.1, Object.assign(state(), { layout: L }));
check('stage 4 s bar is gone 3.1 s after onset', !after.visible);

console.log('AR flat and 3D');
const reach = LAB.arReach(state());
check('AR reach is 4.5 key depths / 3.0 s', near(reach, 4.5) && near(reach / D.arFall, 3));
const flat4 = LAB.arNote({ pitch: 60, start: 0, duration: 4 }, 0, Object.assign(state(), { model: M.FLAT }));
check('flat 4 s bar is cut at 3.000 s', near(flat4.barSeconds, 3, 1e-9) && flat4.clipped);
const flat2 = LAB.arNote({ pitch: 60, start: 0, duration: 2 }, 0, Object.assign(state(), { model: M.FLAT }));
check('flat 2 s bar keeps its 1.99 s natural length', near(flat2.barSeconds, 1.99, 1e-9) && !flat2.clipped);
const three4 = LAB.arNote({ pitch: 60, start: 0, duration: 4 }, 0, Object.assign(state(), { model: M.AR3D }));
check('3D 4 s bar is clamped at 3.000 s', near(three4.barSeconds, 3, 1e-9) && three4.clipped);
const flatLate = LAB.arNote({ pitch: 60, start: 0, duration: 4 }, 4.05, Object.assign(state(), { model: M.FLAT }));
check('flat 4 s bar is gone 4.05 s after onset', flatLate === null);
const next = { pitch: 60, start: 1, duration: 2 };
const cut = LAB.arNote({ pitch: 60, start: 0, duration: 2 }, 0, Object.assign(state(), { model: M.AR3D }), next);
check('next same-pitch note cuts the tail to 1.5 key depths', near(cut.tailDistance, 1.5, 1e-9));
const minState = Object.assign(state(), { model: M.AR3D, minAspect: 2 });
const minNote = LAB.arNote({ pitch: 60, start: 0, duration: .1 }, 0, minState);
check('min-aspect 200% grows a short note to 0.278 key depths', near(minNote.tailDistance, .277777, 1e-4));
const noMin = LAB.arNote({ pitch: 60, start: 0, duration: .1 }, 0, Object.assign(state(), { model: M.AR3D }));
check('without min-aspect the short note stays 0.135 key depths', near(noMin.tailDistance, .135, 1e-9));

console.log('release');
const rel = LAB.releaseInfo();
check('flat release 0.35 s, 3D release 3.0 s, 8.57x', near(rel.flat, .35) && near(rel.projected, 3) && near(rel.ratio, 8.571428, 1e-5));

console.log('time step');
const a = LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, 0, Object.assign(state(), { layout: L }));
const b = LAB.stageNote({ pitch: 60, start: 0, duration: 4 }, 1 / 30, Object.assign(state(), { layout: L }));
check('at 30 fps the head moves 8.6 px per frame', near(b.bottom - a.bottom, 8.6, 1e-9));

console.log('draw path');
function makeCtx() {
  const c = {
    calls: 0,
    save() {}, restore() {}, translate() {}, scale() {}, setTransform() {},
    beginPath() {}, closePath() {}, rect() {}, clip() {},
    moveTo() {}, lineTo() {}, quadraticCurveTo() {}, arc() {},
    fill() { this.calls++; }, stroke() { this.calls++; },
    fillRect() { this.calls++; }, measureText() { return { width: 6 }; },
    fillText() { this.calls++; },
    createLinearGradient() { return { addColorStop() {} }; },
    createRadialGradient() { return { addColorStop() {} }; },
    fillStyle: '', strokeStyle: '', lineWidth: 1, globalAlpha: 1,
    font: '', textAlign: 'start', textBaseline: 'alphabetic'
  };
  return c;
}
for (const model of [M.STAGE, M.FLAT, M.AR3D]) {
  const ctx = makeCtx();
  let ok = true;
  try {
    const s = Object.assign(state(), { position: 7.2, layout: L, model: model });
    LAB.drawScene(ctx, 0, 0, 960, 540, model, s);
  } catch (e) { ok = false; console.log('    ' + e.message); }
  check(`draw path runs for ${model}`, ok);
  check(`draw path paints for ${model}`, ctx.calls > 20, String(ctx.calls));
}
const ctx3 = makeCtx();
LAB.drawScene(ctx3, 0, 0, 960, 540, M.AR3D, Object.assign(state(), { position: 7.2, layout: L, model: M.AR3D, minAspect: 1, effects: true }));
check('3D effects path paints', ctx3.calls > 30, String(ctx3.calls));

console.log('');
if (failures) {
  console.log(`${failures} check(s) failed`);
  process.exit(1);
}
console.log('all note-view checks passed');
