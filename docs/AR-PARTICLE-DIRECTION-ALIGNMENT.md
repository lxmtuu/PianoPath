# AR particle direction alignment

## Status

Implemented and native-verified. [Full CI 38681528570](https://github.com/lxmtuu/PianoPath/actions/runs/38681528570) **SUCCESS** for source `a537b7ff60552e42c35505a33d53f9c5ea51b37c`: static checks, portable tests, Windows compilation/full verification suite, frame-budget check and publication all passed. Native evidence was published by commit `671d38c`.

This is the geometry fix requested after approval of Wisps/Flames artwork, not another artwork revision. New geometry still awaits the user's visual review; no real-camera/hardware or all-condition acceptance is claimed.

## Decision

- Projected notes use `target - NoteDirection.Vector * distance`. Particle rise must therefore follow **minus** that vector, not the vector itself.
- Rotate the entire particle render frame rigidly with a continuous tilt/azimuth basis (no shortest-arc roll flip at the +Z pole): rise, lateral jitter/turbulence and directional sprite velocity. Do not rotate only the rendered flame while leaving its centres on the old -Z path. Preserve the existing emitter anchor at keyboard depth 0.12 and the existing rise clamp.
- For held surfaces, align the camera-facing long axis with the projected outward note tangent. Use the note projector's back-to-front fallback if travel points directly into the lens. Face the camera without going edge-on; keep the existing material, CPU colour/alpha/progress/phase, sizes and lifecycle.
- Classic compact particles use the aligned face; streak/shard velocity and spin bases rotate with the same frame. No emitter, RNG, age, heat/pedal, cap/priority or Stage renderer change. Individual lateral motion remains intentional, not every particle forced onto one line.
- Production defaults to alignment; no settings toggle. An internal `LegacyParticleAxesForReview` seam exists only to reproduce historical material fixtures. Those old captures are explicitly labelled `legacyParticleAxesForReview=true`; frozen geometry is NOT evidence of new production alignment.

## Verified evidence

- Portable rigid-frame tests: 49 tilt/azimuth pairs, including lateral travel, reverse travel and near-180° rotations; correct outward sign, orthogonality, preserved velocity magnitude.
- Projection tests: three calibrated poses × 49 directions × mirror/Fit/Fill; finite camera-facing quads and axis matching the actual note patch at the same centre; ray-aligned fallback and invalid inputs.
- Native WARP: 96 full frames = two held families × three poses × four directions × hold/release samples × legacy/aligned. Same raw simulation hash, intrinsic surface payload, selected population and note count in every pair; actual packed long-axis error at most one degree for aligned captures.
- Preserve accepted material fixtures through explicit legacy review input, not by refreshing their old pixel baselines to the new geometry. New aligned captures also show rising MIDI notes so the comparison includes notes and particles together.
- Publish only two bounded native quartets: rows Wisps/Flames, columns legacy/aligned. Full 96-frame originals stay in the CI artifact. No new default artwork enablement or all-pose real-camera/hardware sign-off.

## Regression contract update

Run `38680618244` (source `a7124aa`) passed portable tests and all 96 native alignment captures/publication checks, then failed the old full-suite assertion requiring every key effect, including particles, to ignore note direction. This was the previous product behaviour, now intentionally superseded by the user's request. The replacement does not simply remove the check: it partitions actual renderer packets, requires particle geometry to change with raw state/selection/intrinsic payload unchanged, requires every nonparticle key-effect packet to stay equal, and separately retains the old pixel-independence test behind the explicit review-only legacy input. The replacement passed the full Windows verification suite in `38681528570`; the earlier failed run is not overall approval.

## Native results and publication

All **96 original native frames / 48 paired state–intrinsic–count guards** passed in CI, with a rising MIDI note present in every capture. The actual packed held-surface axis (not just an intended CPU basis) was compared to the outward note tangent projected at the particle centre. Maximum aligned error:

| Calibrated fixture | Maximum aligned axis error |
| --- | ---: |
| Reference | 0.000453° |
| Oblique | 0.000528° |
| Near-overhead | 0.000361° |

These are geometry measurements over the specified matrix, not perceptual accuracy of curved shader tongues, motion or a real camera. The enforced tolerance is 1°. Intrinsic material payload, raw simulation, selected particle count and note count match between legacy/aligned captures. Nonparticle key effects remain fixed to the keyboard, independently checked with actual renderer packets. Individual lateral drift/turbulence remains intentional.

Only **two lossless 1280×720 quartets / eight native 640×360 panes** are committed:

- [Default, hold, reference camera](previews/ar-particle-alignment/default.png).
- [45° / −45°, release + pedal, oblique camera](previews/ar-particle-alignment/diagonal-release.png).
- [96-capture matrix, source hashes and published-pane hashes](evidence/ar-particle-alignment/manifest.json).

Rows: Wisps / Flames. Columns: historical geometry / corrected production geometry. Compare within one row and scene; the two scenes also differ in camera and phase. Native source pixels are copied unchanged, without contrast/exposure retouching. The full 96 originals remain in the CI artifact, not in Git. Locally, the eight published panes and 22 source hashes were independently checked; **the published subset does not re-verify the other 88 originals**.

Historical material fixtures explicitly retain legacy axes. Independent comparison with pre-fix evidence confirmed **both accepted Wisps and Flames** retain their 64 full-frame hashes, 16 quartet hashes, 240 motion-frame hashes and coverage/geometry metrics. Their old artwork viewers must not be used to assess the new production path. Live Stage, material shaders, emitter/lifetime/RNG, cap 192 and burst/release packing priorities are unchanged. Age-representative packing remains a separate future experiment; candidate artwork is not enabled by this fix.

## Local static comparison

With Pillow installed:

```sh
python3 tools/serve_ar_particle_alignment_review.py --output /home/user/ar-particle-alignment-review --port 8769
```

The viewer verifies the published pixels and source hashes before serving on `0.0.0.0`; it offers fit, native and 2× nearest-neighbour display. Source is `a537b7f`, not an interactive web rewrite of the native renderer. HTTP/pixel checks and mock-DOM tests cover the viewer's selection/zoom/stale/error states, **not real-browser, continuous-motion or hardware acceptance**. Review changes after the native CI are documentation/static-viewer only.
