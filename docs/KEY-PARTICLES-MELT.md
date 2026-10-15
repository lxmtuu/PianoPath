# Melt material trial (default-off)

Status: implementation and native verification in progress; **not art accepted or a default change**.

- Shared bounded same-hue body RGBA and slow lifetime alpha in Live Stage / aligned AR.
- Viscous lower shoulder/meniscus inside unchanged droplet extents; no white-hot core or time noise.
- Stage keeps the existing halo, body and offset glint; AR keeps one body quad. Their compositing is not claimed identical.
- Explicit CPU-only material provenance separates Melt from same-shape/origin primary Splash and release droplets. JSON ignores this extra metadata for historical state hashes; Melt diagnostics hash it explicitly.
- No emission, RNG, physics, density, lifetime, caps, selection, ordered packing or XY geometry changes. **Separate default-off Stage front-layer trial** changes only vertex depth from .6 to .01 for tagged Melt components (kinds 36–38). Material-only kinds 33–35 retain .6. AR keeps its existing depth-off pass and body kind 33.
- Default-off review flag, no settings changes. Wisps/Flames and prior trials remain controls.

Native review is required before any visual conclusion: isolated firm/soft single impacts, 192 statics plus 16 material-only Stage depth controls and 720 motion crops; off/classic/candidate, white/black keys, two palettes, Stage and three aligned AR views. All active candidate statics must contribute off-delta; **no blank-pair exemptions**. Actual normalized geometry/state hashes and particle-linked body colour hashes must match, with mixed primary Splash/Melt isolation checked in the native suite.

The publication uses lossless native crops and full 60-frame BGRA XOR/gzip tracks. Additional allowlisted opaque PNG stills are recompressed without changing dimensions, decoded BGRA or display metadata to reclaim storage, never quantized or resampled.

## Native finding motivating the depth trial

The first real Windows/WARP run [38804570184](https://github.com/lxmtuu/PianoPath/actions/runs/38804570184), source `ca92cc0`, failed the strict active-signal guard on `prism-live-stage-p60-burst`: 8 raw particles and 8 body packets, classic RGB L1 signal 1,135, material-only candidate **zero**. The native build, portable tests and mixed-route lifecycle assertions had passed. Stage particle sprites depth-read at .6 while key meshes are in front of them; the old large halo can leak outside the key silhouette even when the small bead is occluded.

Rather than exempting a blank sample or enlarging the halo, the follow-up isolates a front-layer experiment. Each of the 16 Stage candidate statics has a same-material .6-depth counterpart with identical raw/normalized packet/identity-linked RGBA hashes. These controls can be blank; the actual front-layer candidate cannot. This is a deliberate depth-policy change, not a claim of unchanged full clip-space geometry or universal visual acceptance. Native verification of the follow-up is pending.
