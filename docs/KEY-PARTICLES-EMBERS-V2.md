# Embers v2 — shared material energy / AR contrast

## Status

Implementation pending native CI; no new native success or art acceptance claimed yet. Follows [v1](KEY-PARTICLES-EMBERS-V1.md), which reduced white cores but made AR too faint. Approved Wisps/Flames and production note-aligned AR geometry remain unchanged. Settings menu stays last.

## Changes and boundaries

- V1 retained different Stage/AR CPU gains and lifetime encoding: Stage attenuated RGB while AR attenuated alpha. V1 then compressed RGB again in the shader. V2 resolves **one intrinsic linear tint and one lifetime alpha** in `SparkParticleAppearance.TryColor` for both outputs. Alpha = `(1-age/life) * clamp(glow*ParticleGlow,0,1)`; there is no second age fade in the shader.
- Hue retained, dominant tint capped at 0.95; near-black inputs are not promoted to bright colours. No white-core substitution or HDR amplification. Layer opacity still belongs to AR composition.
- Broader coverage **inside the existing dot/streak quad**, using integrated subpixel coverage; no minimum screen-size, extent/velocity/centre/projector change. V1 material/recipe remains available and unchanged.
- Internal default-off `SparkArtV2ForReview`; tags 20/21. No presets, public setting, menu, WPF fallback or recording-default change. Applies only to dot/streak primitives; other families are not marked complete.
- Same emitter, seed, density, lifetime, RNG, packing order/cap. Legacy AR eligibility is evaluated before substituting v2 material, so material energy cannot admit extra particles.

## Verification contract

Same 192-static / 720-motion matrix as v1, but **left = v1, right = v2**. Off is matched in time. Static palettes Prism/Indigo, pitches 60/61, Stage + three AR poses, burst/held/released/expired. Four synchronized 60-frame motion tracks cover Stage/reference AR × burst/held-release, with pedal at step 12.

V1's whole-packet equality cannot apply to deliberately changed colour/alpha. V2 instead checks raw state, actual packet geometry/order/nonparticle payload and selected count unchanged, then separately hashes actual packed particle colours. V2 actual colour/alpha hashes must match Stage and all three AR views at each static palette/pitch/phase. Portable tests check finite/bounded hue and exactly one lifetime envelope, black/zero-glow and invalid inputs.

Native signal metrics (RGB absolute off-difference and >4-level footprint) are recorded per static sample; compare only versions within the same scene. They are not cross-mode quality scores or a substitute for inspection on real cameras.

Full original statics and motion crops stay in the CI artifact. Commit only two native ROI quartets, four paired APNGs and gzip-compressed metadata (lossless JSON, not lossy pixels), at most 0.7 MiB. Opaque APNG frames may encode RGB rather than RGBA; decoded native BGRA hashes must remain identical, including repeated tail frames at exact 1/30 s. V1 and all approved deliverables remain present; no deletion to make room.

Full CI, published-pixel checks, manual native inspection and hardware/real-camera acceptance remain pending at this point.
