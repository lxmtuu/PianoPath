# Dust V1 — soft pigment motes in Stage / AR

## Status

Native CI and visual review pending. **Default-off, review-only.** Previous candidates are not promoted. Accepted Wisps/Flames stay frozen; auxiliary morph/release follows; settings last.

## Material contract

- Internal `DustArtForReview` is not bound to settings, presets or menu. Eligibility additionally requires actual family `Dust`, non-wisp primitive 3. Tag 29; one sprite / one AR quad per selected physical particle, unchanged order.
- No changes to emitter/RNG, cap, selection, positions, size, velocity, drift, gravity, drag or lifetime. Stage keeps its old kind-0 square (half extent `size × 2.4 × 1.8`); AR retains its original `size × 2.4` square, including projection clamps. The diagnostic restores only candidate tags and intended particle RGBA, not any position/axis/weight/phase.
- Shared bounded, birth-hue-derived linear pigment. Actual RGBA is identity-linked before canonicalisation and compared between Stage and three AR poses. Dust retains its own soft amplitude `.35 × clamp(birthGlow × ParticleGlow,0,1) × (1-age/life)`. This unifies old Stage RGB fading and AR alpha fading; it is an intentional material/opacity change, **not** a promise of unchanged tail brightness.
- A shared smooth Gaussian surface with a compact radial cutoff; no white-hot centre, grain animation, screen-time flicker, glitter or additional age fade. Uses raw particle-size coordinates, so different old quad extents do not arbitrarily stretch the surface. Radius ends at 2.2 raw-size units, fitting inside both unchanged quads. AR's existing world-size clamp can still affect projected apparent size.
- This is a soft material, not new 3D depth geometry, a new depth-sorting model or a new motion style. Overlapping bursts remain possible because emission/spread is deliberately untouched. Legacy Stage's white contribution could dominate low-energy dust; candidate may be substantially dimmer, especially on camera backgrounds. No visibility improvement is assumed before native inspection.

## Verification plan

192 native statics (off/classic/candidate × two palettes × two pitches × four views × burst/held/released/expired), 64 paired raw/ordered geometry/count guards; 720 original motion crops (Stage/reference AR × burst/held-release × 60 frames × off/classic/candidate). Candidate actual particle RGBA parity is checked against Stage at all static AR poses. Active candidate statics must contribute a nonzero native off-delta; motion must contribute at least six nonblank candidate frames, not merely differ from classic by disappearing. This is a nonblank guard, not proof of good visibility. Motion preserves physical release before step 12; pedal must not keep the emitter running.

Publication cap **2.5 MiB**: two exact native ROI quartets, four lossless BGRA XOR+gzip 60-frame bundles, gzip metadata. No quantisation, resampling or frame dropping. Full original/off frames stay artifact-only. No new PNG compaction is planned: existing snapshot headroom must be measured again after publication under the cumulative 128 MiB cap.

Pending: Windows/native/portable CI, published source/pixel checks, real Chromium decoding/controller tests, native visual inspection, and comparison with seven frozen family histories. Local contract tests are not native evidence. Real camera/MIDI, physical GPU performance, all palettes/tiers/delay, bloom/tint/spill/recording, realtime playback cadence and user art acceptance remain separate open gates.
