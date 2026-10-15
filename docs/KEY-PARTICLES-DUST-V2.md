# Dust V2 — recover low birth-glow held visibility without changing burst

## Status

Native/visual checks pending. **Default-off, review-only.** V1 stays frozen as its own evidence/checkpoint; no previous candidate or preset is promoted. Dust visibility is the current priority, then auxiliary morph/release, settings last.

## Deliberately narrow change

V1's held dust has birth glow .38, versus full-glow 1 for impact births. V1 applies `.35 × clamp(glow × ParticleGlow,0,1) × (1-age/life)`; its Stage held capture was too faint. V2 replaces only the **low birth-glow response**, not the global intensity:

`response = 1 - (1-glow)^3`, for `0 < glow < 1`.

Alpha becomes `.35 × clamp(response × ParticleGlow,0,1) × (1-age/life)`. At glow .38 and gain .65 this is about **2.004× V1 intrinsic alpha**, not a promise of twice the perceived brightness. Exactly zero, full-glow and overdriven births stay on the **exact V1 arithmetic path**. Zero ParticleGlow still yields zero alpha; alpha remains bounded by .35. RGB/hue, linear lifetime fade, Gaussian footprint/cutoff and premultiplication stay unchanged. No white core, noise, extra opacity fade or temporal glitter.

Both outputs use the same resolver. Internal `DustArtV2ForReview` defaults false and can only accompany the existing review flag. New tag 30 uses the exact tag-29 shader/VS path: no new size, shape, positions, counts, selection, packing order, RNG, velocity, gravity, drag or lifetime. Current base-family/non-wisp Dust gate remains; Zone/live-switch coverage is not claimed. No new geometry/depth model or additional layer.

## Comparison and native gates

**Left is actual Dust V1, right V2**, not original legacy Dust. Original legacy ↔ V1 evidence stays separate. The new manifest declares this comparator and requires schema 2 with actual identity-linked RGBA hashes. Source/pixel verification also compares every baseline static/full motion frame, normalized packet/raw hash, count and colour hash against the separately generated V1 artifact. Motion crops may differ in bounds, so lineage uses full-frame hashes rather than comparing different crop hashes.

192 statics / 64 paired state/ordered geometry/count guards and actual RGBA parity across Stage/three static AR poses; 720 native motion crops. **Every full-glow burst pair must be pixel-identical**, including all 60 frames in both motion views; no generic requirement to make unchanged bursts look different. Held/released active statics must differ and contribute nonzero off-delta, held motion must have at least six changed and nonblank frames. Empty/expired outputs remain equal. These are technical gates, not art approval.

Publication cap **1.75 MiB**: two native quartets + four lossless BGRA XOR+gzip 60-frame tracks + gzip metadata, preserving all native frames/pixels. Cumulative changed-file payload must remain below 128 MiB. Full originals/off stay artifact-only. No planned additional compaction or frame deletion.

Native Windows, local published subset checks, real Chromium decoding/controls, native inspection and eight-family historical comparisons remain pending. In particular, higher held alpha also affects AR; camera clutter/occlusion cannot be judged from Stage or RGB off-delta alone. Real GPU/camera/MIDI, dense chords, Zone/live switches, all palettes/tiers/delay, bloom/tint/spill/recording, realtime cadence and user approval remain open.
