# Fireworks V1 — multicolour radial spark candidate

## Status

Implementation prepared for native CI. **Native/visual result pending; default-off, review-only.** Embers V2 and Splash V1 remain unaccepted; accepted Wisps/Flames are not reopened. Settings stays last.

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

Bounded publication ≤0.8 MiB: two native ROI quartets, four lossless XOR+gzip BGRA frame bundles (60 frames / manifest 30 FPS each), gzip manifest. Original fulls/off-motion frames remain artifact-only. Cumulative snapshot payload must be measured after publication and stay below 128 MiB without deleting accepted evidence.

Local Python tests use explicitly synthetic contract pixels, not native evidence. C#/HLSL execution requires Windows CI (no local .NET/renderer). After publication: verify all 19 source hashes, 8 static panes and 480 motion halves; run the real Chromium controller/pixel checks, inspect native images/60-frame contact sheets, and compare saved historical Wisps/Flames/Embers/Splash digests. No art or camera/hardware acceptance may be inferred from test success.

## Pending

Full native CI, published-pixel/browser checks, native visual inspection and historical digest comparison. Real camera/MIDI, physical GPU performance, all palettes/tiers/delay, bloom/tint/spill, recording and user art approval remain separate open gates.


## Publication retry

Run `38698943750` / renderer `a0141ad` passed static/portable tests, Windows compilation and native capture generation, then **failed the Fireworks publication step**. It is not full CI success. Signed runner logs were unavailable through the permitted log route, so the exact original publisher error is not claimed. The retry surfaces validator output and byte-budget failures as explicit GitHub annotations.

Fireworks' radial crop can be larger than the earlier families. The publisher now uses full-frame **BGRA XOR against the preceding frame + deterministic gzip** instead of separately deflated APNG rectangles, preserving all 60 native frames, ordering and 30-FPS manifest timestamps. The player reconstructs the same PNG frame endpoints. Full original APNGs are not a required deliverable; original static/motion PNGs stay in the artifact. Exact decompressed length/encoding and both halves of every reconstructed frame are validated; truncation/extra-data mutation tests are included. No frame dropping, palette quantisation, resampling or size/density changes. A fresh full native run and actual published-byte measurement remain required.
