# Shatter · separate AR billboard / tumble trial

## Status and scope

Native/portable CI and published-subset/browser checks complete; **art approval remains open**. Default-off geometry experiment (`ShatterBillboardForReview`), separate from the default-off V1 material flag. No preset/menu/default promotion. Melt/Bounce/release particles and settings remain unchanged.

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

Native C#/HLSL execution, publication size, source/published-pixel verification, browser checks and published-image inspection are complete (results below). These are scoped technical checks, not a universal visibility or art certificate. Physical camera/MIDI/GPU, dense/cap/near-clip transitions, every palette/tier/delay, bloom/tint/spill/recording, realtime cadence and user art approval remain open. V1's muted late Stage material and subtle facet are not solved by AR orientation alone. Settings remains last.

## Verified outcome · separate default-off geometry candidate

- Native source **`3b6cd7bbabfed6095f0462fa86d94573d5d8fbb3`**. [CI 38796781439](https://github.com/lxmtuu/PianoPath/actions/runs/38796781439) **full success**: static/portable C# tests, native render and geometry/publication validators, WPF verification, packaging. CI publication **`29fad9dca153dd2a3d7a804ae1b4a0275992fe5a`**. Earlier run 38796042842 was superseded for audit-memory/browser-QA housekeeping; it is not cited as a successful final run.
- **624 static / 720 native motion crops** passed. All **156 active after statics** have nonzero off-delta, with minimum **17 pixels at max ΔRGB > 4** in this dataset; **52 expired pairs** equal off. No candidate raster-empty whitelist. All before/after raw states, ordered selected identities/counts, actual intrinsic RGBA and non-body packets match. Sixteen Stage static before/after controls (12 active / 4 expired) remain pixel-identical.
- Actual uploaded AR corners and clip weights agree with the independently reconstructed Stage-clock expectation: maximum static error **0.000015257301**, maximum motion error **0.000015256339**, below .0003. Established camera/projection routines are reused; this is independent reconstruction of the tumble/packet expectation, not a separate camera-calibration implementation or a final screen-angle equivalence claim.
- **64 frozen V1 before full-frame controls** match the published prior V1 hashes. The original V1 dataset still reproduces both old raster-empty cases; only the separate after candidate fixes them. No rewriting V1 history or silently promoting either flag.
- Local published-subset verification: **22 source hashes, 12 exact native panes, 480 decoded motion halves**, all 60 frames retained per track. Full static originals and off/motion crop images were verified in CI, **not independently re-read locally**; original full-resolution motion readback hashes are metadata, while retained motion images are crops. Nominal motion record `File` identifiers are not paths to stored full-frame images; the actual stored crop path is `motion/{track}/{variant}/{frame:03}.png` in CI artifacts.
- **Real headless Chromium**: 480 BGRA halves match native digests; all four reference/near-overhead firm/soft modes, frame 0/11/12/13/59 status labels, pedal jump, step/seek, play/pause, no page errors and static-page 390px mobile overflow check pass. Both sides are labelled material V1, and the direct player page explicitly says geometry prototype/default-off. No held/release-emitter claim. Prior V1 and Dust V2 players still pass the shared browser checker. This is decoded-image/controller QA, not realtime FPS or physical-GPU/camera acceptance.
- **104 local Python contract tests**, source checker (**328 C# / 2 XAML / 119 resources / 161 named elements**), Python/JavaScript syntax and diff checks pass. Portable C# and native execution were Windows/Linux CI, not locally substituted render evidence. The extra viewer-label contract test was run locally after the native source commit; the CI source contains 103 Python tests.
- Ten prior material families, including frozen Shatter V1, retain **13,776 historical native pixel/frame digest fields**: Wisps/Flames V3 320 each; Embers V1/V2, Splash V1, Fireworks V1, Confetti V1, Dust V1/V2 and Shatter V1 1,642 each. Provenance changes are excluded from this pixel comparison.
- All four newly compacted stills match their pre-change decoded BGRA, dimensions and original display/resolution chunks exactly **after CI regeneration**, not just immediately after compaction. Publication is **1,139,741 bytes / 1.08694 MiB**, below 1.4 MiB; cumulative changed-file payload approximately **127.47 MiB**, below 128 MiB. No quantisation/resampling/frame loss.

### Formerly empty sample · near-overhead / direction (90°,90°) / pitch 61 / .1833s

Both versions still pack **10 physical body quads**; material, world half extents and raw states are unchanged.

| Palette / version | RGB L1 off-delta | Pixels max ΔRGB > 4 |
|---|---|---|
| Prism / before V1 | 0 | 0 |
| Prism / after billboard | 5,069 | 35 |
| Indigo / before V1 | 0 | 0 |
| Indigo / after billboard | 3,235 | 35 |

Same-scene Prism pitch-61 mid sample: Stage before/after is **18,879 L1 / 77 pixels** on both sides; reference AR is **12,768 / 56 → 13,237 / 60**. L1 is absolute RGB off-difference including darkening, **not luminance, a quality score or equalized cross-mode brightness**. Facing the camera increases projected coverage naturally; this is not a world-size/density gain.

### Native inspection and remaining art limits

Inspected the two six-pane native sheets at 6× nearest-neighbour, sixteen same-time pairs across each of the four native motion tracks (64 paired samples), and the actual Chromium player screenshot. Stage controls are visually and hash-identical. Reference AR chips now change their local orientation with the Stage age/phase clock rather than staying velocity-aligned. Near-overhead now displays the shards where the old plane mostly disappeared, including the black-key mid sample.

The geometry recovery does **not** make every shard cleanly legible: near-overhead's configured outward direction moves fragments across the photographed keys, and pale overlapping fragments can merge into bright/white marks on already bright keys. Early birth clustering remains. Facets remain subtle at native pixel size; no polished reflective-glass claim. Stage's faint late material is unchanged. No tint/opacity/density/size boost was added to conceal these limits.

Keep both flags default-off/review-only pending art approval and real camera/GPU checks. The two demonstrated geometry holes are resolved **in this candidate and matrix**, not universally at all poses/tiers/near-clip/cap transitions. Local clocks are shared; full Stage/AR component representation, perspective, final pixels and perceived brightness still differ. Melt/Bounce/release redesigns and settings remain subsequent work.

Evidence: [early six-pane native comparison](previews/shatter-billboard-v1/early.png), [black-key mid comparison](previews/shatter-billboard-v1/mid.png), [gzip manifest](evidence/shatter-billboard-v1/manifest.json.gz). Before left / same material + billboard right; Stage top / reference AR middle / near-overhead bottom.

```bash
PYTHONPATH=/path/to/pillow python3 tools/serve_shatter_billboard.py \
  --output /tmp/shatter-billboard-review --port 8778
REVIEW_SHATTER_BILLBOARD=1 CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8778
```
