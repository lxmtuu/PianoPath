# Fireworks V1 — multicolour radial spark candidate

## Status

Native candidate verified in full Windows CI and real headless Chromium. **Default-off, review-only; not user art/camera/hardware acceptance.** Embers V2 and Splash V1 remain unaccepted; accepted Wisps/Flames are not reopened. Settings stays last.

## Actual family and material boundaries

Fireworks uses the existing **dot/streak** primitives: burst mixes compact dots and needles, held emits radial streaks. It is **not** `Shape.Shard`, the auxiliary Glass/morph primitive. This pass does not modify Glass or pretend that the family has two component draws: each physical Fireworks dot/streak retains **one Stage sprite / one selected AR quad**.

- Internal `FireworkArtForReview`, additionally gated by the actual `ImpactBurst == "Fireworks"` recipe. No preset/menu/default/recording exposure.
- Tags 26 (streak) / 27 (dot). Shared bounded, hue-preserving intrinsic linear RGB plus one lifetime alpha from the existing common resolver. Native birth hue/random palette mixes are retained, including pale births; no blanket white-core replacement or HDR boost.
- Tapered, broad-shouldered coloured streak with a narrow same-hue facet; compact round sparks. Derivative-integrated coverage remains **inside unchanged dot/streak quads**. No minimum screen size, new burst, new random sample, extra packet, extent/axis/velocity change.
- Emission, radial directions, drag/gravity, life, speed, density, RNG, cap, eligibility and ordering are frozen. AR substitutes material only after the old alpha/projection tests. Baseline is shipping classic, **not Embers V2**.
- Shared intrinsic RGBA is not equal composited colour/apparent brightness across backgrounds, projection and additive/over composition. No new motion dynamics or additional temporal twinkle claimed.

## Verification contract

192 native static captures / 64 classic-candidate raw-state, actual ordered packet geometry/count pairs: Prism and Indigo base palettes, pitches 60/61, Stage plus reference/oblique/near-overhead AR, burst/held/released/expired. The recipe itself retains random multicolour mixes; "Indigo base" does not force every particle to be Indigo.

720 original motion crops: four Stage/reference AR × burst/held-release tracks, 60 frames at exact 1/30 second. Physical release before step 12 with pedal on; pedal does not keep the emitter active. Actual candidate RGBA is identity-linked to raw particles before cross-output canonicalisation; production ordering is never changed to make hashes match. Normalize only intended material kind/RGBA, preserving nonparticle packets and all particle geometry.

Bounded publication ≤2.5 MiB: two native ROI quartets, four lossless XOR+gzip BGRA frame bundles (60 frames / manifest 30 FPS each), gzip manifest. Original fulls/off-motion frames remain artifact-only. Cumulative snapshot payload must be measured after publication and stay below 128 MiB without deleting accepted evidence.

Local Python tests use explicitly synthetic contract pixels, not native evidence. C#/HLSL execution requires Windows CI (no local .NET/renderer). After publication: verify all 19 source hashes, 8 static panes and 480 motion halves; run the real Chromium controller/pixel checks, inspect native images/60-frame contact sheets, and compare saved historical Wisps/Flames/Embers/Splash digests. No art or camera/hardware acceptance may be inferred from test success.

## Open acceptance

Full native CI, published-pixel/browser checks and historical digest comparison now pass (details below); native inspection is recorded, not an art sign-off. Real camera/MIDI, physical GPU performance, all palettes/tiers/delay, bloom/tint/spill, recording and user art approval remain separate open gates.


## Publication retry

Run `38698943750` / renderer `a0141ad` passed static/portable tests, Windows compilation and native capture generation, then **failed the Fireworks publication step**. It is not full CI success. Signed runner logs were unavailable through the permitted log route, so the exact original publisher error is not claimed. The retry surfaces validator output and byte-budget failures as explicit GitHub annotations.

Fireworks' radial crop can be larger than the earlier families. The publisher now uses full-frame **BGRA XOR against the preceding frame + deterministic gzip** instead of separately deflated APNG rectangles, preserving all 60 native frames, ordering and 30-FPS manifest timestamps. The player reconstructs the same PNG frame endpoints. Full original APNGs are not a required deliverable; original static/motion PNGs stay in the artifact. Exact decompressed length/encoding and both halves of every reconstructed frame are validated; truncation/extra-data mutation tests are included. No frame dropping, palette quantisation, resampling or size/density changes. A fresh full native run and actual published-byte measurement remain required.


Run `38700608820` / `f156a97` confirms the publisher failure reason: **2,132,918 bytes exceeded 0.8 MiB**. Native generation and full original pixel validation passed; full CI did not. XOR encoding is pixel-exact but radial ROIs still carry substantial native camera background data. The limit is now 2.5 MiB, paired with explicit lossless compaction of twelve existing opaque native still PNGs (RGBA → RGB, identical decoded BGRA). No images/frames deleted, recoloured, resampled or replaced; animation and nonopaque inputs are rejected. The compactor verifies dimensions and every decoded pixel before writing and reports original native hashes. CI repeats compaction after regenerating previews. This reclaims space instead of weakening the raw-pixel/frame tests; snapshot size still must be measured after publication. Future families remain subject to the same finite budget.


## Verified native candidate

- [CI **38702753168**](https://github.com/lxmtuu/PianoPath/actions/runs/38702753168) **SUCCESS**, source `58c96bd862dacf9108d7adea928f41ad147e354d`; preview publication `97a5ffc`. Renderer/material code is unchanged from `a0141ad`; retries repaired publication/budget, not emission or visuals. Static/portable tests, Windows build, full native verification, frame budget, galleries and package passed. The two earlier runs remain failed, not reclassified as successes.
- **192 static captures / 64 paired geometry/state/count guards / 720 native motion crops** passed. Actual candidate particle RGBA, linked to raw identity, matches Stage and all three AR poses for the static matrix. This is intrinsic parity, not equal final pixels or perceived brightness.
- Local published subset passes **19 source hashes, 8 native static panes, 480 exact motion halves**. The four frame bundles retain all 60 frames at manifest 30 FPS; no resampling, quantisation or frame dropping. Full original/off frames remain artifact-only.
- **Real headless Chromium**: all 480 decoded native BGRA halves match the manifest; four mode release/step/seek controls and play/pause work, no page errors, 390px mobile overflow check passed. This is actual browser decoding/control QA, not a mocked DOM, realtime FPS claim or native WPF GUI test.
- **67 local Python tests** pass (the successful CI source had 66; the extra local test covers preservation of PNG colour-management/resolution metadata). Source checker, Python/JavaScript syntax and diff checks pass. C#/HLSL execution was on Windows CI, not local simulation.
- Historical digest comparison, excluding source provenance: **342 static + 240 motion each for Wisps and Flames, 3,466 Embers V1, 4,378 Embers V2, 4,378 Splash V1**, all unchanged. Twelve compacted stills preserve every decoded native BGRA pixel and original sRGB/gamma/physical-resolution chunks. The compactor now preserves colour-management chunks directly and rejects animation/nonopaque inputs; this post-CI Python-only guard was tested locally, with no renderer/shader change.
- Published Fireworks payload **2.034 MiB**, below 2.5 MiB; cumulative session changed-file payload approximately **127.44 MiB**, below 128 MiB. Twelve stills reclaim approximately **2.2 MB** of encoding bytes; accepted images/frames were not deleted or artistically modified.

### Same-scene native signal · Prism / pitch 60

| View / phase | Packed classic / V1 | RGB L1 off-delta classic → V1 | Pixels max ΔRGB > 4 classic → V1 |
|---|---|---|---|
| Stage / burst | 35 / 35 | 51,277 → 46,523 | 125 → 140 |
| Stage / held | 14 / 14 | 22,066 → 18,425 | 74 → 94 |
| AR reference / burst | 35 / 35 | 7,528 → 10,852 | 77 → 134 |
| AR reference / held | 14 / 14 | 10,878 → 12,610 | 70 → 131 |

L1 is absolute RGB difference from matched off (including darkening), **not luminance or a quality score**. Compare versions within the same scene only. Stage loses some peak/total energy while the thresholded coloured footprint grows; AR reference has more thresholded pixels without more particles. Do not claim a universal brightness increase.

### Inspection / remaining risks

Inspected native burst/held quartets at nearest-neighbour enlargement, 60-frame contact-sheet overviews for all four tracks, and the real Chromium player screenshot/control run. V1 trades some white Stage peaks for broader coloured bodies; AR reference streaks read more as coloured connected slivers instead of isolated bright dots. Some old Stage peaks become dimmer, and early overlapping bursts can still merge into a bright cluster. Larger motion ROIs include substantial keyboard/background and can make one-pixel detail hard to read at fit scale. Contact-sheet overview and browser hash checks are not full aesthetic/cadence acceptance for every frame/pose.

Simulation life/directions are unchanged, but the unified alpha encoding intentionally changes perceived tail energy. No new radial dynamics, emitter policy, temporal glitter, camera calibration or minimum size is introduced. Real camera/MIDI, every palette/tier/delay, bloom/tint/spill, recording, physical GPU performance and **user art approval remain open**. Embers/Splash are still review-only; accepted Wisps/Flames remain frozen; Confetti/Dust and auxiliary art remain pending, settings last.

Evidence: [burst quartet](previews/fireworks-art-v1/burst.png), [held quartet](previews/fireworks-art-v1/held.png), [gzip manifest](evidence/fireworks-art-v1/manifest.json.gz). Classic left / V1 right; Stage above / AR reference below.

```bash
# Pillow required; verify native subset and serve on 0.0.0.0.
python3 tools/serve_firework_particle_art.py --output /tmp/fireworks-review --port 8773
# Real browser QA with externally installed puppeteer-core and Chrome:
CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8773
```
