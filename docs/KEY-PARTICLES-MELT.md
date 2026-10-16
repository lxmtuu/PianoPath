# Melt material + Stage front-layer trial (default-off)

Status: native render/validation passed; **review-only, not art accepted or a default change**.

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

Rather than exempting a blank sample or enlarging the halo, the follow-up isolates a front-layer experiment. Each of the 16 Stage candidate statics has a same-material .6-depth counterpart with identical raw/normalized packet/identity-linked RGBA hashes. These controls can be blank; the actual front-layer candidate cannot. This is a deliberate depth-policy change, not a claim of unchanged full clip-space geometry or universal visual acceptance. The follow-up passed native verification; results below.

## Native outcome and published review

Source **`871950ddca2fdb1a0dadb8aae47bd1ebf232d97a`**, [Windows CI 38805236341](https://github.com/lxmtuu/PianoPath/actions/runs/38805236341). Publication **`f676689bf8107d2a74dde2f6db4d64d54dfb44e2`**.

- **Full CI succeeded**, including native render/original-image validation, portable C# tests, full verification, WPF/previews and distributable packaging. This is software-WARP proof, not physical-GPU/FPS acceptance.
- **192 main statics + 16 same-material Stage depth controls; 720 native motion crops.** All **48 active candidate pairs** contribute off-delta, minimum RGB L1 **1,039**, minimum **8 pixels** with max-channel delta >4. All 16 expired main pairs are off-identical. There are no blank exceptions for the actual candidate.
- All **12 active material-only Stage controls are raster-empty**, while all 12 corresponding front-layer candidates contribute signal. The other four material-only controls are expired. Actual raw population, normalized ordered packets and linked body RGBA match each front-layer/control pair: the isolated difference is the documented depth policy/tag, not larger geometry or more particles.
- Example Prism/p60 Stage: early classic 17 pixels >4 → candidate 53; mid classic 0 → 51; late classic 0 → 34. These are visibility measurements against the same off control, not a quality score.
- Stage retains three existing components, AR one body quad. Body colour/alpha matches across Stage and all three aligned AR views. Final scene brightness/compositing is not equal, and AR does not adopt Stage's depth change.
- Native mixed-route checks passed: primary Splash with the same shape/origin remains unchanged alongside tagged Melt; source tags for Float Up/Dissolve and other routes remain zero. Builds do not advance time, spawn or consume RNG.
- **15,950 prior pixel/frame digest fields** across 11 review families (13 manifests) match the pre-Melt freeze, including Wisps/Flames and both Shatter trials. Source/revision metadata is allowed to advance; decoded images/frames are not.

### Storage and reproducibility

The publication is **293,863 bytes (0.280 MiB)**, below the 1.3 MiB CI cap. Fourteen additional allowlisted opaque stills reclaim **1,181,781 bytes** without changing dimensions, decoded BGRA or original display/resolution chunks; exact comparison against local pre-recompression copies passed. Cumulative changed-file payload after publication is **133,190,637 bytes (127.0205 MiB)** versus base `a52ca16`, before this report update.

Run the local published-subset verifier/player:

```sh
python tools/serve_melt_particle_art.py --output /path/to/cache/melt-review --port 8779
# Optional browser QA; dependencies and Chromium runtime live outside the repository.
REVIEW_SINGLE_IMPACT=1 REVIEW_FAMILY=melt node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8779
```

Local checks passed: **113 Python tests**, static source validation and JavaScript syntax; actual headless Chromium **153** checked **480 decoded BGRA frame halves**, all four soft/firm Stage/AR modes, frame 0/11/12/13/59 labels and pedal/seek/step/play controls, no page errors, and mobile overflow. The published subset also verifies source hashes, eight static panes and lossless frame bundles. Original full/off images, 16 depth-control PNGs, oblique/overhead statics and full crop provenance were checked in Windows CI, **not re-downloaded or locally re-rendered**.

### Art status / remaining limits

**Review-only and art-open; neither flag is enabled by default.** The Stage occlusion issue is technically addressed by a deliberate front-layer option, not by making the old material-only trial look successful. The native low-resolution crops show small yellow-green specks; the lower-heavy silhouette/meniscus is not consistently legible at normal size. On bright keys the common press lighting can dominate and the AR classic/candidate difference is subtle; a positive off-delta does not establish that Melt reads as viscous fluid. The large shared bright region in paired images must not be mistaken for a newly added Melt core.

The viewer uses nearest-neighbour magnification and exact native crops, not exposure adjustments or generated art. Storage lane `held` is a **single soft .45 press**, not a held emitter; `burst` is a single firm .9 press. Pedal on at motion frame 12 is a control, not a release burst. Native motion covers Stage and AR reference only; the other two AR poses have static checks. Realtime cadence, actual cameras/physical GPUs, nonzero camera delay, other tiers/palettes/keyboard layouts and readability under ordinary scene lighting remain open.

Next roadmap item is **Bounce**. Float Up/Dissolve/Smoke follow; broad art review then settings UI last. This report is not default-promotion approval.

Post-publication follow-up: the static preview-folder checker needed an explicit allowlist entry for the four newly published Melt BGRA bundles. This is now covered by its existing contract test, including rejection of an unplanned overhead track and a manifest archive in the preview folder. Static checking and all 113 Python tests were rerun successfully **after** publication, along with Chromium QA on the final viewer copy. The documentation/viewer/checker-only follow-up changes no native-render or portable C# source relative to `871950d`; the successful native run above remains its evidence source.
