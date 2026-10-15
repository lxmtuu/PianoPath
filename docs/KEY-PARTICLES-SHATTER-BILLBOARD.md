# Shatter · separate AR billboard / tumble trial

## Status and scope

Implementation ready for native CI; **native and browser outcomes pending**. Default-off geometry experiment (`ShatterBillboardForReview`), separate from the default-off V1 material flag. No preset/menu/default promotion. Melt/Bounce/release particles and settings remain unchanged.

The V1 material pass preserved Stage age/phase tumble but AR velocity-plane orientation. Two near-overhead black-key mid samples packed ten bodies yet rendered no pixels in either V1/classic. This pass addresses the geometry explicitly rather than brightening pigment, changing particle size/density/lifetime, hiding the samples or weakening the V1 disclosure.

## Geometry contract

Only non-wisp, shape-6, impact-origin-0 particles with selected `ImpactMorph == "Shatter"`, the new review flag, and non-historical AR axes take the new branch. Production/default flags remain false. Stage's exact float clock is `phase + age * 7.3f`; Stage and AR share it. AR rotates right/down in the existing note-aligned camera-tangent basis: `x = right*cos - up*sin`, `y = -right*sin - up*cos`. Both axes remain tangent to the camera ray, rather than collapsing the particle plane edge-on.

This synchronizes local clock/rotation, **not identical screen angles or final pixels under different projection/mirroring**. AR half extents remain `size*1.2`, `size*.42` with the original world-size clamps. Centers, raw simulation, RNG, velocities, birth/lifetime/alpha/colour, ordering, counts/caps and non-body geometry are not intentionally changed. Changing the billboard naturally changes its projected footprint; this is not a geometry-preserving material tweak. No extra AR glint/quad. Stage retains its existing body + glint and must remain pixel-identical.

A geometry change can affect projectability near the clip plane or at the cap; the sub-cap native matrix must preserve actual ordered selected identities/counts, but is not universal near-clip/cap coverage. Do not claim untouched geometry or complete production parity from this trial.

## Evidence plan

- **624 native statics**: two palettes × two pitches × (Stage control once + three AR poses × four explicit NoteDirections) × four samples × off/before/after. Samples 2/10/26/89 at 60 Hz. Both before and after use exactly V1 pigment/facet/glint; only the new AR billboard flag differs.
- **156 active pairs**, no raster-empty candidate exemptions; **52 expired pairs**. Stage pixel equality, before/after raw state, ordered selected identities, actual material payloads and non-body packets must match. Actual AR corners and clip weights must agree with an independently reconstructed Stage-clock projection within .0003 pixel/weight units. World-size constants are not increased.
- **64 frozen V1 full-frame before controls** must reproduce the prior published V1 pixel hashes, including both old empty cases. V1's own 192/720 dataset remains independently regenerated, with the two original empty samples still disclosed.
- **720 native motion crops**: reference AR pitch 60 / near-overhead AR pitch 61 × firm .9 / soft .45 f0 impact × 60 frames at 30 Hz × off/before/after. Pedal toggles at f12 only; no held/release emitter. Four tracks retain all frames losslessly. Static pedal toggles at f21.
- Publication target: two six-pane native sheets (Stage / reference / near-overhead), four XOR BGRA+gzip paired tracks, gzip metadata, **cap 1.4 MiB**. Full originals/off variants stay in CI artifacts. No quantisation, resampling or frame dropping. Browser decoding/controller/label QA and native inspection follow publication.
- Ten frozen material families (including Shatter V1) will be checked for unchanged prior pixel/frame digests.

## Bounded storage housekeeping

Cumulative session payload was approximately 127.00 MiB before this pass. Reclaimed **716,961 bytes** by adding four already-existing opaque native AR stills (Smoke/Dissolve × white/black key) to the established PNG compactor allowlist. Decoded BGRA pixels, dimensions and original display/resolution chunks are preserved exactly; no alpha-bearing/animated input is rewritten. This is storage-only, not a Smoke/Dissolve redesign. Pre-compaction copies are retained locally for post-CI pixel/chunk comparison.

## Acceptance still open

Native C#/HLSL execution, publication-size result, independent source/pixel verification, browser checks and actual image inspection are pending. Physical camera/MIDI/GPU, dense/cap/near-clip transitions, every palette/tier/delay, bloom/tint/spill/recording, realtime cadence and user art approval remain open. V1's muted late Stage material and subtle facet are not solved by AR orientation alone. Settings remains last.
