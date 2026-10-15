# Melt material trial (default-off)

Status: implementation and native verification in progress; **not art accepted or a default change**.

- Shared bounded same-hue body RGBA and slow lifetime alpha in Live Stage / aligned AR.
- Viscous lower shoulder/meniscus inside unchanged droplet extents; no white-hot core or time noise.
- Stage keeps the existing halo, body and offset glint; AR keeps one body quad. Their compositing is not claimed identical.
- Explicit CPU-only material provenance separates Melt from same-shape/origin primary Splash and release droplets. JSON ignores this extra metadata for historical state hashes; Melt diagnostics hash it explicitly.
- No emission, RNG, physics, density, lifetime, caps, selection, ordered packing, depth or geometry changes.
- Default-off review flag, no settings changes. Wisps/Flames and prior trials remain controls.

Native review is required before any visual conclusion: isolated firm/soft single impacts, 192 statics and 720 motion crops; off/classic/candidate, white/black keys, two palettes, Stage and three aligned AR views. All active candidate statics must contribute off-delta; **no blank-pair exemptions**. Actual normalized geometry/state hashes and particle-linked body colour hashes must match, with mixed primary Splash/Melt isolation checked in the native suite.

The publication uses lossless native crops and full 60-frame BGRA XOR/gzip tracks. Additional allowlisted opaque PNG stills are recompressed without changing dimensions, decoded BGRA or display metadata to reclaim storage, never quantized or resampled.
