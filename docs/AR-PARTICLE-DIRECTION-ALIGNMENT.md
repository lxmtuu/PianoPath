# AR particle direction alignment

## Status

Implementation pending native verification. This is a geometry fix requested after approval of Wisps/Flames artwork, not another artwork revision. No new native success or visual acceptance is claimed yet.

## Decision

- Projected notes use `target - NoteDirection.Vector * distance`. Particle rise must therefore follow **minus** that vector, not the vector itself.
- Rotate the entire particle render frame rigidly: rise, lateral jitter/turbulence and directional sprite velocity. Do not rotate only the rendered flame while leaving its centres on the old -Z path. Preserve the existing emitter anchor at keyboard depth 0.12 and the existing rise clamp.
- For held surfaces, align the camera-facing long axis with the projected outward note tangent. Use the note projector's back-to-front fallback if travel points directly into the lens. Face the camera without going edge-on; keep the existing material, CPU colour/alpha/progress/phase, sizes and lifecycle.
- Classic compact particles use the aligned face; streak/shard velocity and spin bases rotate with the same frame. No emitter, RNG, age, heat/pedal, cap/priority or Stage renderer change. Individual lateral motion remains intentional, not every particle forced onto one line.
- Production defaults to alignment; no settings toggle. An internal `LegacyParticleAxesForReview` seam exists only to reproduce historical material fixtures. Those old captures are explicitly labelled `legacyParticleAxesForReview=true`; frozen geometry is NOT evidence of new production alignment.

## Planned evidence

- Portable rigid-frame tests: 49 tilt/azimuth pairs, including lateral travel, reverse travel and near-180° rotations; correct outward sign, orthogonality, preserved velocity magnitude.
- Projection tests: three calibrated poses × 49 directions × mirror/Fit/Fill; finite camera-facing quads and axis matching the actual note patch at the same centre; ray-aligned fallback and invalid inputs.
- Native WARP: 96 full frames = two held families × three poses × four directions × hold/release samples × legacy/aligned. Same raw simulation hash, intrinsic surface payload, selected population and note count in every pair; actual packed long-axis error at most one degree for aligned captures.
- Preserve accepted material fixtures through explicit legacy review input, not by refreshing their old pixel baselines to the new geometry. New aligned captures also show rising MIDI notes so the comparison includes notes and particles together.
- Publish only two bounded native quartets: rows Wisps/Flames, columns legacy/aligned. Full 96-frame originals stay in the CI artifact. No new default artwork enablement or all-pose real-camera/hardware sign-off.
