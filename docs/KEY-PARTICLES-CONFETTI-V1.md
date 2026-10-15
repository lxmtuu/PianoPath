# Confetti V1 — paper surface and synchronized material rotation

## Status

Native CI / visual review pending. **Default-off, review-only.** Previous Embers/Splash/Fireworks candidates are not silently promoted; accepted Wisps/Flames remain frozen. Settings stays last.

## Material and rotation contract

- New internal `ConfettiArtForReview`, additionally gated to `ImpactBurst == "Confetti"` and non-wisp shape 2. Tag 28; no preset/menu/default exposure.
- Preserve positions, size, axes, velocity, density, spawn credit/RNG, lifetime, cap, selection and packet order. Exactly one Stage sprite / one AR quad per selected paper particle. Old Stage square quads and AR rectangular quads are unchanged.
- Shared clipped-corner rectangular paper with a same-hue fold, bounded intrinsic tint. Shape is expressed in raw-particle-size units to retain material aspect despite different quad aspect ratios. The polygon fits inside a unit-radius circle (corner vectors .94/.27 and .66/.55), hence inside the narrow old AR quad at every angle. It occupies less surface than the old luminous square; a universal brightness increase is **not** the goal or a guaranteed result.
- Preserve Confetti's legacy opacity `min(1,(1-age/life)*1.5)`, including its existing independence from ParticleGlow. No new HDR/white highlight or temporal brightness pulse; pale/white native births remain pale/white. RGB is shared through the bounded birth-tint resolver.
- Existing Stage geometry rotates by **age in seconds × 7 + birth phase**. Legacy AR uses **normalised age × 7 + phase**, a different angular clock. Candidate AR now receives the actual cosine/sine material basis matching Stage, and inverse-rotates the surface inside the unchanged projected quad. No geometry rotation, second rotation on Stage or new simulation dynamics is introduced.

## Native verification

192 statics / 64 paired raw-state, physical geometry/order/count guards: two base palettes, two pitches, four views and burst/held/released/expired. 720 original motion crops: Stage/reference AR × burst/held-release × 60 frames × off/classic/candidate. Physical release before step 12, pedal on but no sustained emitter.

`MaterialHash` links **actual packed RGBA and actual orientation cosine/sine** to each raw particle before diagnostic canonicalisation. It must match Stage and all three AR poses for candidate statics. Stage ordered packet normalisation restores tag 28→3 and zeroes intended particle RGBA only. AR normalisation restores tag 28→2 and the two explicitly changed material lanes to the raw particle's original progress/shape; all actual corner positions, weights, order, phase and nonparticle payload stay strict. The producer does not reorder packets to make hashes match.

Publication limit **3.0 MiB**: two native ROI quartets, four lossless 60-frame BGRA XOR+gzip bundles and gzip metadata. Full originals/off frames remain artifact-only. Cumulative payload must remain under 128 MiB; sixteen more explicitly allowlisted opaque legacy stills are compacted RGB losslessly, preserving dimensions, every decoded BGRA pixel and sRGB/gamma/physical-resolution chunks. No accepted image/frame deleted, recoloured or resampled; animation/nonopaque input rejected.

## Pending gates

Windows build and full native/portable CI, source/published-pixel verification, real Chromium pixel/controller checks, native image inspection and saved historical Wisps/Flames/Embers/Splash/Fireworks digest comparisons. Local synthetic contract tests are not native render evidence. Physical GPU/camera, live MIDI, all palette/tier/delay, bloom/tint/spill/recording, actual playback cadence and user art approval remain open. No local .NET/Windows renderer is available.
