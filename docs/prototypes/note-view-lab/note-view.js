/*
 * Note-view lab — a browser sketch of the three note paths the audit measured:
 * WPF/GPU stage, the flat AR overlay, and the 3D AR path.
 *
 * The geometry is copied from the product formulas, not approximated:
 *   stage:  lane = W/88, width = lane * NoteWidth/100,
 *           height = clamp(duration*speed - gap, 16, .9*hitY),
 *           bottom = hitY - (start - position)*speed
 *   flat:   width = key span * WidthShare, head at v=0, tail clipped at reach
 *   3D:     the same, plus min-aspect and the next same-pitch cut
 *
 * The projection is deliberately schematic: AR is mapped with one key depth
 * equal to the keyboard's height, not through a camera calibration. Colours
 * and glow are simplified. The point is the numbers and the timing.
 */
(function (global) {
  'use strict';

  var FIRST = 21, KEYS = 88, BLACK_SHARE = .52;

  var DEFAULTS = {
    noteWidth: 80, noteFall: 550, fallSpeed: 258, noteGap: 2, noteMinLength: 16,
    keyboardScale: 100, arFall: 1.5, arLookahead: 3, arShare: .86,
    arGap: .015, arMinLength: .06, keyboardAspect: 8.4, releaseLife: .35
  };

  var MODELS = { STAGE: 'stage', FLAT: 'flat', AR3D: '3d' };

  var SCENE_NOTES = [
    { pitch: 60, start: 1.5, duration: .5 },
    { pitch: 64, start: 3.0, duration: 2.0 },
    { pitch: 67, start: 5.5, duration: 4.0 },
    { pitch: 61, start: 5.5, duration: 2.0 },
    { pitch: 72, start: 8.5, duration: 6.0 },
    { pitch: 66, start: 9.5, duration: 1.5 },
    { pitch: 60, start: 12.5, duration: 2.0 },
    { pitch: 61, start: 12.5, duration: 2.0 },
    { pitch: 64, start: 12.5, duration: 2.0 },
    { pitch: 66, start: 12.5, duration: 2.0 }
  ];

  function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }
  function isBlack(pitch) { return pitch % 12 === 1 || pitch % 12 === 3 || pitch % 12 === 6 || pitch % 12 === 8 || pitch % 12 === 10; }
  function blackOffset(pitch) {
    switch (pitch % 12) {
      case 1: return -.08;
      case 3: return .08;
      case 6: return -.10;
      case 8: return 0;
      case 10: return .10;
      default: return 0;
    }
  }

  function makeKeyboard(first, count) {
    first = first == null ? FIRST : first;
    count = count == null ? KEYS : count;
    var whites = [], whitesBelow = [];
    for (var p = 0; p < 128; p++) {
      whitesBelow[p] = whites.length;
      if (p >= first && p < first + count && !isBlack(p)) whites.push(p);
    }
    var whiteWidth = 1 / whites.length;
    var centers = [], spans = [];
    for (var q = 0; q < 128; q++) {
      centers[q] = isBlack(q)
        ? (whitesBelow[q] + blackOffset(q)) * whiteWidth
        : (whitesBelow[q] + .5) * whiteWidth;
      if (isBlack(q)) {
        var half = whiteWidth * BLACK_SHARE / 2;
        spans[q] = { left: centers[q] - half, right: centers[q] + half, width: whiteWidth * BLACK_SHARE };
      } else {
        var left = whitesBelow[q] * whiteWidth;
        spans[q] = { left: left, right: left + whiteWidth, width: whiteWidth };
      }
    }
    return {
      first: first, count: count, whites: whites, whiteWidth: whiteWidth,
      center: function (pitch) { return centers[clamp(pitch | 0, 0, 127)]; },
      span: function (pitch) { return spans[clamp(pitch | 0, 0, 127)]; }
    };
  }

  var KEYBOARD = makeKeyboard(FIRST, KEYS);

  function layoutFor(state) {
    var height = state.height || 1080;
    var width = state.width || height * 16 / 9;
    var scale = state.keyboardScale == null ? DEFAULTS.keyboardScale : state.keyboardScale;
    var keyboard = clamp(Math.max(110, Math.min(228, height * .205)) * (scale / 100), 80, 300);
    var speed = DEFAULTS.fallSpeed * (state.noteFall || DEFAULTS.noteFall) / 550;
    return {
      width: width, height: height, keyboardHeight: keyboard, hitY: height - keyboard,
      lane: width / KEYS, whiteWidth: width / KEYBOARD.whites.length,
      blackWidth: width / KEYBOARD.whites.length * BLACK_SHARE,
      speed: speed, lookahead: (height - keyboard) / speed
    };
  }

  function noteWidthPixels(pitch, pct, mode, layout) {
    pct = pct == null ? DEFAULTS.noteWidth : pct;
    var laneWidth = layout.lane * pct / 100;
    if (mode === 'span') return KEYBOARD.span(pitch).width * layout.width * pct / 100;
    if (mode === 'clamped') return Math.min(laneWidth, .98 * layout.blackWidth);
    return laneWidth;
  }

  function stageNote(note, position, state) {
    var L = state.layout || layoutFor(state);
    var speed = L.speed;
    var gap = state.noteGap == null ? DEFAULTS.noteGap : state.noteGap;
    var minLength = state.noteMinLength == null ? DEFAULTS.noteMinLength : state.noteMinLength;
    var height = clamp(note.duration * speed - gap, minLength, L.hitY * .9);
    var bottom = L.hitY - (note.start - position) * speed;
    var top = bottom - height;
    var width = noteWidthPixels(note.pitch, state.widthPct, state.widthMode, L);
    return {
      x: KEYBOARD.center(note.pitch) * L.width - width / 2,
      top: top, bottom: bottom, width: width, height: height,
      visible: top <= L.hitY && bottom >= 0,
      sounding: position >= note.start && position < note.start + note.duration,
      barSeconds: (height + gap) / speed,
      clipped: Math.abs(height - L.hitY * .9) < 1e-9,
      headY: Math.min(bottom, L.hitY)
    };
  }

  function arReach(state) {
    return (state.arLookahead == null ? DEFAULTS.arLookahead : state.arLookahead)
      * (state.arFall == null ? DEFAULTS.arFall : state.arFall);
  }

  function arNote(note, position, state, nextSame) {
    var L = state.layout || layoutFor(state);
    var fall = state.arFall == null ? DEFAULTS.arFall : state.arFall;
    var reach = arReach(state);
    var share = state.arShare == null ? DEFAULTS.arShare : state.arShare;
    var depth = L.keyboardHeight;
    var distance = (note.start - position) * fall;     // key depths above the keys
    if (distance > reach + 1e-9) return null;
    var natural = Math.max(note.duration * fall - DEFAULTS.arGap, DEFAULTS.arMinLength);
    var nearDistance = Math.max(distance, 0);
    var tailDistance = Math.min(distance + natural, reach);
    var widthDepth = KEYBOARD.span(note.pitch).width * DEFAULTS.keyboardAspect * share;
    var minAspect = state.minAspect || 0;
    if (state.model === MODELS.AR3D) {
      tailDistance = Math.max(distance + natural, nearDistance + minAspect * widthDepth);
      if (nextSame != null) {
        var nextHead = (nextSame.start - position) * fall;
        tailDistance = Math.min(tailDistance, Math.max(nearDistance, nextHead));
      }
      tailDistance = Math.min(tailDistance, reach);
    }
    if (!(tailDistance > nearDistance + 1e-9)) return null;
    var width = widthDepth * depth;
    return {
      x: KEYBOARD.center(note.pitch) * L.width - width / 2,
      width: width,
      yFar: L.hitY - tailDistance * depth,
      yNear: L.hitY - nearDistance * depth,
      height: (tailDistance - nearDistance) * depth,
      nearDistance: nearDistance, tailDistance: tailDistance,
      visible: true,
      sounding: position >= note.start && position < note.start + note.duration,
      barSeconds: (tailDistance - nearDistance) / fall,
      clipped: tailDistance + 1e-9 >= reach
    };
  }

  function nextSamePitch(index) {
    var note = SCENE_NOTES[index];
    for (var i = index + 1; i < SCENE_NOTES.length; i++)
      if (SCENE_NOTES[i].pitch === note.pitch) return SCENE_NOTES[i];
    return null;
  }

  function longestStageSeconds(layout, state) {
    var gap = state && state.noteGap != null ? state.noteGap : DEFAULTS.noteGap;
    return (layout.hitY * .9 + gap) / layout.speed;
  }

  function statsFor(height, state) {
    state = state || {};
    var L = layoutFor({ height: height, width: height * 16 / 9, noteFall: state.noteFall, keyboardScale: state.keyboardScale });
    var widthPct = state.widthPct == null ? DEFAULTS.noteWidth : state.widthPct;
    var mode = state.widthMode || 'lane';
    var notePx = noteWidthPixels(60, widthPct, mode, L);
    var arPx = KEYBOARD.span(60).width * L.width * (state.arShare == null ? DEFAULTS.arShare : state.arShare);
    return {
      height: height, width: L.width, hitY: L.hitY, keyboardHeight: L.keyboardHeight,
      lane: L.lane, notePx: notePx,
      pctWhite: notePx / L.whiteWidth * 100,
      pctBlack: notePx / L.blackWidth * 100,
      arPx: arPx, ratio: arPx / notePx,
      lookahead: L.lookahead,
      longest: longestStageSeconds(L, state),
      arSeconds: arReach(state) / (state.arFall == null ? DEFAULTS.arFall : state.arFall)
    };
  }

  function noteText(pitch) {
    var names = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
    return names[pitch % 12] + (Math.floor(pitch / 12) - 1);
  }

  function noteColor(pitch, alpha) {
    var hue = ((pitch - FIRST) * 360 / 87 + 188) % 360;
    return 'hsla(' + hue.toFixed(1) + ',72%,60%,' + alpha + ')';
  }

  // --------------------------------------------------------------- drawing
  function roundedRect(ctx, x, y, w, h, r) {
    r = Math.max(0, Math.min(r, w / 2, h / 2));
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x + r, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
  }

  function drawKeyboard(ctx, L, model) {
    var top = L.hitY;
    var h = L.keyboardHeight;
    ctx.save();
    ctx.fillStyle = model === MODELS.STAGE ? '#e7ecf5' : 'rgba(214,222,236,.72)';
    for (var i = 0; i < KEYBOARD.whites.length; i++) {
      var pitch = KEYBOARD.whites[i];
      var span = KEYBOARD.span(pitch);
      ctx.fillRect(span.left * L.width, top, span.width * L.width - .6, h);
    }
    ctx.fillStyle = model === MODELS.STAGE ? '#111827' : 'rgba(10,14,22,.88)';
    for (var p = FIRST; p < FIRST + KEYS; p++) {
      if (!isBlack(p)) continue;
      var b = KEYBOARD.span(p);
      ctx.fillRect(b.left * L.width, top, b.width * L.width, h * .62);
    }
    ctx.strokeStyle = 'rgba(120,210,255,.35)';
    ctx.lineWidth = Math.max(1, L.height * .0012);
    ctx.beginPath(); ctx.moveTo(0, top); ctx.lineTo(L.width, top); ctx.stroke();
    ctx.restore();
  }

  function drawStage(ctx, L, state, position) {
    ctx.fillStyle = '#070b13'; ctx.fillRect(0, 0, L.width, L.height);
    var bg = ctx.createLinearGradient(0, 0, 0, L.hitY);
    bg.addColorStop(0, '#0a1020'); bg.addColorStop(1, '#131d31');
    ctx.fillStyle = bg; ctx.fillRect(0, 0, L.width, L.hitY);

    // One-second lanes: a quiet ruler for the roll.
    ctx.strokeStyle = 'rgba(120,160,220,.08)'; ctx.lineWidth = 1;
    for (var s = 0; s < 12; s++) {
      var y = L.hitY - ((s + (position % 1)) * L.speed);
      if (y < 0) break;
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(L.width, y); ctx.stroke();
    }

    drawKeyboard(ctx, L, MODELS.STAGE);

    ctx.save();
    ctx.beginPath(); ctx.rect(0, 0, L.width, L.hitY); ctx.clip();
    for (var i = 0; i < SCENE_NOTES.length; i++) {
      var n = stageNote(SCENE_NOTES[i], position, state);
      if (!n.visible) continue;
      var alpha = n.sounding ? .92 : 1;
      var grad = ctx.createLinearGradient(0, n.top, 0, n.bottom);
      grad.addColorStop(0, noteColor(SCENE_NOTES[i].pitch, .45 * alpha));
      grad.addColorStop(1, noteColor(SCENE_NOTES[i].pitch, .95 * alpha));
      ctx.fillStyle = grad;
      roundedRect(ctx, n.x, n.top, n.width, n.height, Math.min(n.width * .35, n.height * .2));
      ctx.fill();
      ctx.strokeStyle = 'rgba(255,255,255,.42)'; ctx.lineWidth = .8; ctx.stroke();
      ctx.fillStyle = 'rgba(255,255,255,.88)';
      ctx.fillRect(n.x + 1, n.headY - Math.max(1, n.height * .012), n.width - 2, Math.max(1, n.height * .012));
    }
    ctx.restore();
  }

  function drawAr(ctx, L, state, position, model) {
    ctx.fillStyle = '#0b0a11'; ctx.fillRect(0, 0, L.width, L.height);
    var bg = ctx.createRadialGradient(L.width * .5, L.height * .28, 10, L.width * .5, L.height * .3, L.width * .7);
    bg.addColorStop(0, model === MODELS.AR3D ? '#1b1730' : '#171522');
    bg.addColorStop(1, '#07070b');
    ctx.fillStyle = bg; ctx.fillRect(0, 0, L.width, L.height);

    // A soft camera-like band behind the keys, so the two AR panels read as overlays.
    var band = ctx.createLinearGradient(0, L.hitY - L.keyboardHeight * .5, 0, L.hitY);
    band.addColorStop(0, 'rgba(90,120,180,.05)'); band.addColorStop(1, 'rgba(90,120,180,.12)');
    ctx.fillStyle = band; ctx.fillRect(0, 0, L.width, L.hitY);

    drawKeyboard(ctx, L, model);

    var hitGlow = ctx.createLinearGradient(0, L.hitY - 8, 0, L.hitY + 4);
    hitGlow.addColorStop(0, 'rgba(100,220,255,0)');
    hitGlow.addColorStop(1, 'rgba(120,230,255,.24)');
    ctx.fillStyle = hitGlow; ctx.fillRect(0, L.hitY - 8, L.width, 12);

    ctx.save();
    ctx.beginPath(); ctx.rect(0, 0, L.width, L.hitY); ctx.clip();
    for (var i = 0; i < SCENE_NOTES.length; i++) {
      var note = SCENE_NOTES[i];
      var n = arNote(note, position, state, model === MODELS.AR3D ? nextSamePitch(i) : null);
      if (!n || n.yNear < 0 || n.yFar > L.hitY) continue;
      var cx = n.x + n.width / 2;

      if (model === MODELS.AR3D && state.effects) {
        // Ghosts: the same note lifted a little, fading out, as the 3D path adds them.
        for (var g = 1; g <= 3; g++) {
          var gh = Math.min(n.height, L.hitY);
          var gy = n.yFar - g * (8 + gh * .12);
          ctx.fillStyle = noteColor(note.pitch, .12 / g);
          roundedRect(ctx, n.x, gy, n.width, gh, n.width * .3); ctx.fill();
        }
        if (n.nearDistance < .65) {
          var t = 1 - n.nearDistance / .65;
          var glow = ctx.createRadialGradient(cx, L.hitY, 0, cx, L.hitY, n.width * 1.8);
          glow.addColorStop(0, 'rgba(160,235,255,' + (.34 * t).toFixed(3) + ')');
          glow.addColorStop(1, 'rgba(160,235,255,0)');
          ctx.fillStyle = glow;
          ctx.fillRect(cx - n.width * 1.8, L.hitY - n.width * 1.8, n.width * 3.6, n.width * 3.6);
        }
      }

      var grad = ctx.createLinearGradient(0, n.yFar, 0, n.yNear);
      grad.addColorStop(0, noteColor(note.pitch, n.sounding ? .55 : .7));
      grad.addColorStop(1, noteColor(note.pitch, n.sounding ? .98 : .9));
      ctx.fillStyle = grad;
      roundedRect(ctx, n.x, n.yFar, n.width, n.height, Math.min(n.width * .32, n.height * .3));
      ctx.fill();
      ctx.strokeStyle = 'rgba(255,255,255,.5)'; ctx.lineWidth = .9; ctx.stroke();
      ctx.fillStyle = 'rgba(255,255,255,.92)';
      ctx.fillRect(n.x + 1, n.yNear - Math.max(1, n.width * .12), n.width - 2, Math.max(1, n.width * .12));

      if (model === MODELS.AR3D && state.effects && n.width >= 11 && n.height >= 15) {
        var fs = Math.max(10, n.width * .62);
        var label = noteText(note.pitch);
        ctx.font = fs + 'px system-ui, sans-serif';
        var tw = ctx.measureText(label).width;
        var ly = Math.min(n.yNear - 4, Math.max(fs + 10, n.yFar + fs + 10));
        ctx.fillStyle = 'rgba(8,12,20,.74)';
        roundedRect(ctx, cx - tw / 2 - 4, ly - fs - 4, tw + 8, fs + 6, 4);
        ctx.fill();
        ctx.fillStyle = 'rgba(255,255,255,.94)';
        ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
        ctx.fillText(label, cx, ly);
        ctx.textAlign = 'start'; ctx.textBaseline = 'alphabetic';
      }
    }
    ctx.restore();
  }

  function drawScene(ctx, x, y, w, h, model, state) {
    var L = state.layout || layoutFor(state);
    ctx.save();
    ctx.beginPath(); ctx.rect(x, y, w, h); ctx.clip();
    ctx.translate(x, y);
    ctx.scale(w / L.width, h / L.height);
    if (model === MODELS.STAGE) drawStage(ctx, L, state, state.position);
    else drawAr(ctx, L, state, state.position, model);
    ctx.restore();
  }

  function releaseInfo() {
    return { flat: DEFAULTS.releaseLife, projected: DEFAULTS.arLookahead, ratio: DEFAULTS.arLookahead / DEFAULTS.releaseLife };
  }

  var api = {
    DEFAULTS: DEFAULTS, MODELS: MODELS, SCENE_NOTES: SCENE_NOTES, KEYBOARD: KEYBOARD,
    makeKeyboard: makeKeyboard, isBlack: isBlack, layoutFor: layoutFor,
    noteWidthPixels: noteWidthPixels, stageNote: stageNote, arNote: arNote,
    arReach: arReach, longestStageSeconds: longestStageSeconds, statsFor: statsFor,
    noteText: noteText, noteColor: noteColor, drawScene: drawScene, releaseInfo: releaseInfo
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  global.NoteViewLab = api;

  // ------------------------------------------------------------------- UI
  if (typeof document === 'undefined' || !document.getElementById('note-view-app')) return;

  var state = {
    height: 1080, width: 1920, keyboardScale: 100,
    widthPct: 80, widthMode: 'lane', minAspect: 0, effects: true,
    position: 0, playing: true, loop: 18
  };
  var canvases = { stage: null, flat: null, '3d': null };
  var statsNodes = { stage: null, flat: null, '3d': null };
  var lastFrame = 0;

  function setupCanvas(id) {
    var canvas = document.getElementById(id);
    if (!canvas) return null;
    var dpr = Math.min(2, global.devicePixelRatio || 1);
    var cssWidth = canvas.clientWidth || 900;
    var cssHeight = cssWidth * 9 / 16;
    canvas.width = Math.round(cssWidth * dpr);
    canvas.height = Math.round(cssHeight * dpr);
    canvas.style.height = cssHeight + 'px';
    var ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return { canvas: canvas, ctx: ctx, cssWidth: cssWidth, cssHeight: cssHeight };
  }

  function resize() {
    canvases.stage = setupCanvas('stage-canvas');
    canvases.flat = setupCanvas('flat-canvas');
    canvases['3d'] = setupCanvas('ar3d-canvas');
    render();
  }

  function applyResolution(height) {
    state.height = height;
    state.width = height * 16 / 9;
    var buttons = document.querySelectorAll('[data-res]');
    for (var i = 0; i < buttons.length; i++)
      buttons[i].setAttribute('aria-pressed', String(+buttons[i].getAttribute('data-res') === height));
    updateStats();
    render();
  }

  function clearStats() {
    ['stage', 'flat', '3d'].forEach(function (k) {
      var node = document.getElementById(k + '-stats');
      if (node) node.innerHTML = '';
    });
  }

  function updateStats() {
    var s = statsFor(state.height, state);
    var L = layoutFor(state);
    var n = stageNote({ pitch: 60, start: 0, duration: 4 }, 0, { layout: L, widthPct: state.widthPct, widthMode: state.widthMode });
    var ar4 = arNote({ pitch: 60, start: 0, duration: 4 }, 0, { layout: L, model: MODELS.FLAT, arShare: DEFAULTS.arShare });
    var ar4d = arNote({ pitch: 60, start: 0, duration: 4 }, 0, { layout: L, model: MODELS.AR3D, arShare: DEFAULTS.arShare, minAspect: state.minAspect });

    document.getElementById('stage-stats').innerHTML =
      '<b>' + n.width.toFixed(2) + ' px</b> · ' + s.pctWhite.toFixed(1) + '% phím trắng / ' +
      s.pctBlack.toFixed(1) + '% phím đen · lookahead ' + s.lookahead.toFixed(2) + ' s · thanh 4 s → ' +
      n.barSeconds.toFixed(2) + ' s' + (n.clipped ? ' <em>(bị cắt)</em>' : '');
    document.getElementById('flat-stats').innerHTML =
      '<b>' + s.arPx.toFixed(2) + ' px</b> · ' + (DEFAULTS.arShare * 100).toFixed(0) + '% phím · reach ' +
      s.arSeconds.toFixed(1) + ' s · thanh 4 s → ' + (ar4 ? ar4.barSeconds.toFixed(2) : '0') + ' s · live-bar release ' +
      DEFAULTS.releaseLife.toFixed(2) + ' s';
    document.getElementById('ar3d-stats').innerHTML =
      '<b>' + Math.round(s.arPx) + ' px</b> · min-aspect ' + (state.minAspect * 100).toFixed(0) + '% · reach ' +
      s.arSeconds.toFixed(1) + ' s · thanh 4 s → ' + (ar4d ? ar4d.barSeconds.toFixed(2) : '0') + ' s · live-bar release ' +
      DEFAULTS.arLookahead.toFixed(1) + ' s';

    var rows = '';
    [900, 1080, 1440].forEach(function (h) {
      var r = statsFor(h, state);
      rows += '<tr' + (h === state.height ? ' class="on"' : '') + '><td>' + h + 'p</td><td>' + r.notePx.toFixed(2) +
        '</td><td>' + r.pctWhite.toFixed(1) + '%</td><td>' + r.pctBlack.toFixed(1) + '%</td><td>' +
        r.arPx.toFixed(2) + '</td><td>' + r.lookahead.toFixed(2) + ' s</td><td>' + r.longest.toFixed(2) +
        '</td><td>' + r.arSeconds.toFixed(2) + '</td></tr>';
    });
    document.getElementById('measure-rows').innerHTML = rows;
  }

  function render() {
    var L = layoutFor(state);
    state.layout = L;
    if (canvases.stage) drawScene(canvases.stage.ctx, 0, 0, canvases.stage.cssWidth, canvases.stage.cssHeight, MODELS.STAGE, state);
    if (canvases.flat) drawScene(canvases.flat.ctx, 0, 0, canvases.flat.cssWidth, canvases.flat.cssHeight, MODELS.FLAT, state);
    if (canvases['3d']) drawScene(canvases['3d'].ctx, 0, 0, canvases['3d'].cssWidth, canvases['3d'].cssHeight, MODELS.AR3D, state);
  }

  function tick(now) {
    var dt = lastFrame ? Math.min(.05, (now - lastFrame) / 1000) : 0;
    lastFrame = now;
    if (state.playing) {
      state.position += dt;
      if (state.position > state.loop) state.position = 0;
      var slider = document.getElementById('time');
      if (slider) slider.value = state.position.toFixed(2);
      var label = document.getElementById('time-label');
      if (label) label.textContent = state.position.toFixed(1) + ' s';
    }
    render();
    global.requestAnimationFrame(tick);
  }

  function init() {
    resize();
    applyResolution(1080);
    updateStats();

    var play = document.getElementById('play');
    if (play) play.addEventListener('click', function () {
      state.playing = !state.playing;
      play.textContent = state.playing ? 'Tạm dừng' : 'Chạy';
      play.setAttribute('aria-pressed', String(state.playing));
    });

    var slider = document.getElementById('time');
    if (slider) {
      slider.max = state.loop;
      slider.addEventListener('input', function () { state.position = +slider.value; document.getElementById('time-label').textContent = state.position.toFixed(1) + ' s'; render(); });
    }

    var res = document.querySelectorAll('[data-res]');
    for (var i = 0; i < res.length; i++) res[i].addEventListener('click', function (e) {
      applyResolution(+e.currentTarget.getAttribute('data-res'));
    });

    var width = document.getElementById('note-width');
    if (width) width.addEventListener('input', function () {
      state.widthPct = +width.value;
      document.getElementById('note-width-label').textContent = width.value + '%';
      updateStats(); render();
    });

    var mode = document.getElementById('width-mode');
    if (mode) mode.addEventListener('change', function () { state.widthMode = mode.value; updateStats(); render(); });

    var minAspect = document.getElementById('min-aspect');
    if (minAspect) minAspect.addEventListener('input', function () {
      state.minAspect = +minAspect.value / 100;
      document.getElementById('min-aspect-label').textContent = minAspect.value + '%';
      updateStats(); render();
    });

    var effects = document.getElementById('ar-effects');
    if (effects) effects.addEventListener('change', function () { state.effects = effects.checked; render(); });

    global.addEventListener('resize', resize);
    global.requestAnimationFrame(tick);
  }

  init();
})(typeof window !== 'undefined' ? window : globalThis);
