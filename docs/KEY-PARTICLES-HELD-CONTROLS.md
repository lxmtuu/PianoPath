# Held particles — lighting, palette, chord and pose controls

## Status

Implemented diagnostic follow-up to the [v2 sampled review](KEY-PARTICLES-HELD-ART-REVIEW.md), **not a material v3**. Local Python/static checks pass; fresh Windows build, portable .NET tests, WARP captures and visual inspection are pending the new CI run. Prior v2 success remains `38645329801`; it must not be used as evidence for these new controls.

Wisps/Flames stay opt-in/default off. No user-facing control or menu redesign. All other press/held families and the broader roadmap remain open; menu work is still last.

## Fixed factorial plan

| Axis | Values |
|---|---|
| Held family | Wisps, Flames; existing v2 shader and emitter recipes |
| Note colour | Existing Prism; fixed warm amber `#FF9A4D`; fixed cool indigo `#666DFF` |
| Physical-held input | White C4 (60); black C♯4 (61); simultaneous 60/61/64/67 chord |
| View | Live Stage; reference AR; oblique AR; near-overhead AR |
| Independent factors | Neither; key light only; particles only; both |

**72 scenes × 4 variants = 288 full 1280×720 frames.** All are active/held at 36 steps of 1/60 s (0.6 s), seed `20261014`, velocity 0.9, pedal off, beat envelope zero, no imported song or release. Each factor combination replays the same input prefix with a fresh feed/simulation. AR is Ultra, FOV 70°, delay zero, Fit, unmirrored. No temporal playback comparison is implied by static controls.

Fixed colours are deliberately not luminance-matched: they probe contrast under the same material, not a fair brightness ranking. Existing Prism settings stay intact; fixed palettes only change the Note colour route. Both key and held-particle colours still use the normal production colour resolver.

## What “key-light off” actually means

- **AR:** `GpuArInput.KeyLight=0` disables both calibrated key tint and projected neighbouring-key spill; on is 0.6. Other geometry and simulated particle colours/state are untouched.
- **Stage:** neither `KeyLighting=0` (spill only) nor `ShaderKeyLight=0` (changes static ivory shading) isolates the whole pressed-key contribution. A default-off, verification-only renderer seam zeroes packed real-key `Emit.W` and `Base.Y`, after building keys and without modifying the simulation. This removes pressed-key tint/emission/spill while preserving base lighting, depression, geometry, note colour and particles. Felt is excluded. The seam has no settings/preset/menu binding.

This tests the **grouped key-light contribution**, not tint versus spill separately. Bloom/composition remain production behaviour; factor deltas need not be additive.

## Pose and synthetic background contract

Reference AR uses the existing synthetic camera image unchanged. Two new key planes are projected from the physical Standard88 width/depth ratio, FOV 70°, depth 7.2 key-depth units: 55° tilt / −6° roll (oblique), 7° tilt / 0° roll (near-overhead). Their projected corners drive both calibration and a nearest-neighbour planar warp of the reference camera image; out-of-source pixels are dark grey.

These are **calibration-matched planar synthetic fixtures**, not newly lit 3D keyboard photography or real-camera evidence. The camera plane and particle projection must agree, but the fixture cannot certify realistic key lighting, occlusion, hand overlap or exposure. Portable tests pin the physical pose normals, corner mapping, deterministic warp and unchanged reference pixels.

## Guards, evidence and review

- WARP required; original active keyboard layout restored in `finally`.
- Correct on/off particle counts; zero Stage-space geometry in AR; AR key tint/spill absent when off.
- Key lighting cannot change particle counts; particle factor cannot change key/spill counts. Family particle-off frames must match exactly.
- Windows payload checks preserve key shape/press/shadow/RGB/felt under the Stage light-off mask.
- All full PNGs and quartet PNGs round-trip to the original BGRA bytes before hashes are written.
- Manifest records exact input/seed/time, calibration corners, warped-camera hashes, raw BGRA SHA256 and 18 source-file hashes (text normalized to UTF-8/LF; PNG bytes unchanged), plus source revision.
- CI independently decodes **all 288 full frames and 72 quartet sheets** with Pillow, checks BGRA hashes and verifies that every sheet quadrant is its exact native crop. Source hashes and run revision must match before publication.
- Four explicit comparisons per scene: particles without light; particles with light; light without particles; light with particles. Display-RGB delta/footprint numbers are diagnostic measurements, not perceptual quality, linear decomposition or automatic art approval.

Full frames, manifest and standalone HTML viewer stay in the `phase1-visual-review` CI artifact under `held-particle-controls/`. Git receives only 72 native crop quartets in `docs/previews/held-particle-controls/` and JSON evidence in `docs/evidence/held-particle-controls/manifest.json`. The image-only preview rule remains intact; no HTML/base64 payloads or 288-frame dataset in Git.

Each quartet is 768×720: **top-left neither / top-right light only / bottom-left particles only / bottom-right combined**. Each pane is a 384×360 native crop. Full scenes must still be inspected before conclusions about total visibility; the crops are not a substitute for full-scene, temporal or real-camera review.

### Reproduce / inspect

On Windows, the existing `--verify --verify-prism-only --verify-review-out=<folder>` route renders these controls alongside the existing reviews. Then:

```sh
python -m pip install Pillow==11.3.0
python tools/review_held_particle_controls.py <folder>/held-particle-controls --source-root .
```

For a published subset, put the committed quartet PNGs in `<review>/sheets/`, the committed evidence at `<review>/manifest.json`, and use `--sheets-only`. That mode verifies all sheet hashes and the manifest plan, **not** unavailable full-frame hashes/crop reconstruction; the console explicitly says so. The viewer is self-contained, selection-generation guarded, decode-gated and reports image errors instead of showing a stale labelled capture.

Next gate: wait for the new CI, inspect actual paired controls at white/black roots and across chord/colour/pose cases, and only then decide whether a material/lighting change is warranted. No alpha reduction or default enablement is justified by this implementation alone.
