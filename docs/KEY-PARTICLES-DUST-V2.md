# Dust V2 — recover low birth-glow held visibility without changing burst

## Status

Full Windows/native and published-pixel/browser checks pass; native inspection recorded, not user art approval. **Default-off, review-only.** V1 stays frozen as its own evidence/checkpoint; no previous candidate or preset is promoted. Dust visibility is the current priority, then auxiliary morph/release, settings last.

## Deliberately narrow change

V1's held dust has birth glow .38, versus full-glow 1 for impact births. V1 applies `.35 × clamp(glow × ParticleGlow,0,1) × (1-age/life)`; its Stage held capture was too faint. V2 replaces only the **low birth-glow response**, not the global intensity:

`response = 1 - (1-glow)^3`, for `0 < glow < 1`.

Alpha becomes `.35 × clamp(response × ParticleGlow,0,1) × (1-age/life)`. At glow .38 and gain .65 this is about **2.004× V1 intrinsic alpha**, not a promise of twice the perceived brightness. Exactly zero, full-glow and overdriven births stay on the **exact V1 arithmetic path**. Zero ParticleGlow still yields zero alpha; alpha remains bounded by .35. RGB/hue, linear lifetime fade, Gaussian footprint/cutoff and premultiplication stay unchanged. No white core, noise, extra opacity fade or temporal glitter.

Both outputs use the same resolver. Internal `DustArtV2ForReview` defaults false and can only accompany the existing review flag. New tag 30 uses the exact tag-29 shader/VS path: no new size, shape, positions, counts, selection, packing order, RNG, velocity, gravity, drag or lifetime. Current base-family/non-wisp Dust gate remains; Zone/live-switch coverage is not claimed. No new geometry/depth model or additional layer.

## Comparison and native gates

**Left is actual Dust V1, right V2**, not original legacy Dust. Original legacy ↔ V1 evidence stays separate. The new manifest declares this comparator and requires schema 2 with actual identity-linked RGBA hashes. Source/pixel verification also compares every baseline static/full motion frame, normalized packet/raw hash, count and colour hash against the separately generated V1 artifact. Motion crops may differ in bounds, so lineage uses full-frame hashes rather than comparing different crop hashes.

192 statics / 64 paired state/ordered geometry/count guards and actual RGBA parity across Stage/three static AR poses; 720 native motion crops. **Every full-glow burst pair must be pixel-identical**, including all 60 frames in both motion views; no generic requirement to make unchanged bursts look different. Held/released active statics must differ and contribute nonzero off-delta, held motion must have at least six changed and nonblank frames. Empty/expired outputs remain equal. These are technical gates, not art approval.

Publication cap **1.75 MiB**: two native quartets + four lossless BGRA XOR+gzip 60-frame tracks + gzip metadata, preserving all native frames/pixels. Cumulative changed-file payload must remain below 128 MiB. Full originals/off stay artifact-only. No planned additional compaction or frame deletion.

## Verified native result

- [CI **38777928321**](https://github.com/lxmtuu/PianoPath/actions/runs/38777928321) **SUCCESS**; source `fd17d0cf10e9728497bc8c9bf10d1d7dfe727cb3`, publication `80e1a86`. Static/portable tests, Windows build, full native capture generation and publication validation, verification suite, frame budget, galleries and package passed. No failed native run is reclassified as success.
- **192 static captures / 64 paired raw-state, ordered geometry/selection/count guards / 720 native motion crops** pass on Windows WARP. Actual candidate intrinsic RGBA, identity-linked before canonicalisation, matches Stage and all three static AR poses. This is intrinsic parity, not equal composited appearance.
- **All 16 burst static pairs and all 120 burst motion pairs are pixel-identical to V1.** Held/released changes and nonblank guards pass. The separately generated V1 baseline/off lane also matches V2 baseline/off in **128 static records and 480 full motion records** (pixels, raw/normalized ordered packets, actual RGBA and counts); full-frame hashes are used for lineage even when ROI bounds differ.
- Local published subset validation passes **19 source hashes, 8 native panes, 480 exact native motion halves**, plus baseline lineage. Four tracks retain all 60 frames at manifest 30 FPS. Full original/off frames were validated in CI, not downloaded/revalidated locally.
- **Real headless Chromium 153** confirms all 480 decoded BGRA halves; release/step/seek for all four modes, play/pause, no page errors, and 390px static-page mobile overflow check pass. Player explicitly labels **V1 left / V2 right**, not legacy classic. Not native WPF GUI testing, realtime playback/FPS, camera or physical GPU acceptance.
- **89 local Python tests**, source checker (323 C# / 2 XAML / 119 resources / 161 named elements), Python/JavaScript syntax and diff checks pass. C#/HLSL execution was Windows CI, not synthetic local render evidence.
- Eight historical families match the frozen pre-V2 native pixel/frame digests: **320 each for Wisps/Flames**, **1,642 each for Embers V1/V2, Splash V1, Fireworks V1, Confetti V1 and Dust V1** — **10,492 fields unchanged**, excluding changed source provenance. These are digest fields, not distinct capture counts. V1's original legacy comparison is preserved separately.
- Published V2 payload **407,061 bytes / 0.3882 MiB**, below 1.75 MiB. Cumulative session changed-file payload approximately **125.98 MiB**, below 128 MiB. No additional PNG compaction, resampling, quantisation, deleted imagery or dropped frames.

### Same-scene native signal · Prism / pitch 60

**This table compares V1 → V2, not legacy → V2.**

| View / phase | Packed V1 / V2 | RGB L1 off-delta V1 → V2 | Pixels max ΔRGB > 4 V1 → V2 |
|---|---|---|---|
| Stage / burst | 35 / 35 | 21,897 → 21,897 | 50 → 50 |
| Stage / held | 14 / 14 | 12,446 → 22,577 | 140 → 161 |
| AR reference / burst | 35 / 35 | 3,356 → 3,356 | 43 → 43 |
| AR reference / held | 14 / 14 | 6,617 → 11,876 | 124 → 141 |

RGB L1 is **absolute** off-difference including darkening/occlusion, not emitted energy, luminance, a visibility score or a cross-mode brightness metric. Approximately 2.004× intrinsic alpha does not mean exactly twice final brightness. Do not choose a global gain from L1 alone.

### Visual inspection / limitations

Inspected both native static quartets at 8× nearest-neighbour enlargement, sixteen same-time samples through each of the four 60-frame tracks and the real Chromium screenshot/control run. Held V2's warm plume/motes are more discernible than V1 against both sampled backgrounds. The surface/colour stays soft; no new sharp white core or temporal brightness oscillation was introduced. The early/full-glow cloud/arc is unchanged, not redesigned or magically separated into fine individual grains.

Stage remains deliberately subdued, much less white-hot than the original legacy Dust. Late tails and small/low-contrast patches can still be hard to see; this does **not** prove legibility on every background or at native small size. AR held is more present and may increase cloud opacity/occlusion over bright keys; real-camera readability must be checked separately. Longer *perceived* visibility is expected from increased alpha, but raw lifetime, death times and physical release/emission are unchanged.

Real GPU/camera/MIDI, dense chords, Zone/live switches, every palette/tier/delay, bloom/tint/spill/recording, realtime cadence and **user art approval remain open**. Motion here covers reference AR / Stage only; oblique and near-overhead checks are static. No prior candidates/defaults are promoted. Auxiliary morph/release is next after this Dust review checkpoint; settings stays last.

Evidence: [V1/V2 burst quartet](previews/dust-art-v2/burst.png), [V1/V2 held quartet](previews/dust-art-v2/held.png), [gzip manifest](evidence/dust-art-v2/manifest.json.gz).

```bash
# Pillow required; validate native pixels/source/lineage and serve on 0.0.0.0.
python3 tools/serve_dust_particle_art_v2.py --output /tmp/dust-v2-review --port 8776
CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8776
```
