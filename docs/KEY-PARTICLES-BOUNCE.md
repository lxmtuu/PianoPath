# Bounce material + AR tangent-billboard trial (default-off)

Status: full native CI and browser checks passed; **review-only, not art accepted or default promoted**.

The candidate rounds the leading end of the existing directional streak and tapers its tail, with bounded birth-hue pigment and one CPU-owned lifetime alpha shared between Stage and aligned AR. The existing neutral-white third of Bounce births remains neutral; there is no newly generated white-hot core, temporal noise, extra particles, collision or extra rebound. The follow-up AR-only billboard intentionally changes body corner geometry, as documented below. This is an appearance trial for the existing upward impact spray, not a physics redesign.

An explicit CPU-only `MaterialRoute=2` separates Bounce from primary Ember/Firework streaks with the same shape/origin. Melt retains route 1; other sources remain zero. The extra metadata remains ignored by historical JSON but is explicitly included in new Bounce raw-state hashes. Native mixed-route guards compare actual ordered Stage/AR packets and read-only simulation snapshots.

- Preserve all birth/RNG/velocity/gravity/drag/density/lifetime/caps/selection and packing order.
- Keep one body per particle in each output, exact existing extents and Stage axes/depth .6, and AR depth-off policy. A separately flagged AR-only camera-tangent orientation follows Stage Vx/Vy; it does not change per-output extent magnitudes or the projected center. It is an intentional body-geometry change, not a claim of unchanged AR corners.
- Preserve Bounce's companion ring (channel/gain/white colour/geometry/lifetime) and all other packets. Existing trials/default settings stay unchanged.
- Require 192 main statics, 64 body-off native controls, 64 material-only legacy-axis controls and 720 motion crops. All 48 active candidate statics must differ from the body-off control, not merely the effect-off scene: ring light cannot replace body visibility. All 16 expired cases must be off-identical. No blank-body exceptions.
- Body-off controls zero **only** accepted kind-39 body RGBA immediately before observation/upload, without changing packets/counts/simulation/ring. The normalized geometry/non-body hash must match the actual candidate.
- Motion has two single-press velocities (.9 firm/.45 soft), never a held emitter. Pedal at f12 is a control. At least three active motion frames must still contribute off-delta after f10 (30 Hz), beyond the companion ring's .3-second lifetime.

Static matrix: two palettes, white/black keys, Stage and three production-aligned AR poses, samples f2/f10/f26/f89 at 60 Hz. Motion: Stage and AR reference, 60 exact frames each, lossless common native crops and BGRA XOR/gzip paired tracks. Publication is capped at 0.9 MiB; still-image storage reclamation must preserve decoded BGRA, dimensions and display chunks, never alter animations.

A successful WARP capture is technical evidence, not fluid/rebound readability, physical-GPU/real-camera/FPS acceptance, all tiers/palettes, nonzero camera delay or final art approval. Settings UI remains last. Float Up/Dissolve/Smoke follow Bounce on the roadmap.

## Native finding and isolated follow-up

First Windows/WARP run [38816833025](https://github.com/lxmtuu/PianoPath/actions/runs/38816833025), source `6616b1d`, passed compilation, portable tests and lifecycle/mixed-route checks, but failed at `prism-ar-near-overhead-p60-burst`: **11 raw particles, 11 bodies, zero body-only raster signal**. The effect-off comparison was not blank because it included the companion ring; this is exactly why the body-off control is mandatory. Full logs were unavailable through the restricted artifact host; this failure comes from the job's native annotation.

The follow-up keeps the pure material path as 64 legacy-axis controls and adds a separate default-off `BounceBillboardForReview` flag. Its actual AR corners and perspective weights are checked independently against the Stage velocity orientation in a note-aligned camera tangent plane (max error .0003). Classic vs material-only must have identical normalized full geometry; material-only vs billboard must preserve raw state, selected order, body RGBA, all non-body packets and extents. All 16 Stage controls must remain pixel-identical. Body-off retains the new geometry and companion ring, and cannot be blank-exempted. Three AR poses/directions are sampled; this is not all-direction or real-camera acceptance.

Fifteen further opaque native Wisps/Flames stills were recompressed losslessly to reclaim 587,531 bytes. APNGs were deliberately rejected and left untouched. Dimensions, decoded BGRA and original display chunks are invariant; historical particle pixel/frame hashes must still match after native publication.

## Verified native outcome

Source **`3a6ff74ddfd4af60b81622174fb009113e72df5e`**; [Windows CI **38817700956**](https://github.com/lxmtuu/PianoPath/actions/runs/38817700956) **fully succeeded**, including portable C# tests, native original validation, the full verification suite, WPF/previews, packaging and distributable publication. Native evidence publication: **`4a5fc07`**.

| Check | Measured result |
|---|---|
| Native static matrix | 192 main images + 64 body-off + 64 same-material legacy-axis controls = **320 statics** |
| Native motion | **720 crops**, four seeded tracks with 60 frames per off/classic/candidate variant |
| Active body visibility | **48/48** positive body-only deltas, minimum RGB L1 **1,618**, minimum **21 pixels** with max-channel delta >4 |
| Expired output | **16/16** main pairs off-identical, no body contribution |
| Pure-material near-overhead | All **12 active** legacy-axis controls are body-empty; the corresponding tangent candidates are not |
| Named recovery | Prism / AR near-overhead / p60 / f2: **11 unchanged bodies**, material-only body signal 0 → tangent body signal **2,988 / 21 pixels >4** |
| Actual uploaded corner/weight audit | Max error **0.0000152771**, below **0.0003**, independent Stage-velocity reconstruction |
| Stage isolation | All **16** geometry-control/candidate pairs pixel-identical; no Stage billboard/depth/extent change |
| After-ring motion visibility | Frames f10 onward with body off-delta: Stage firm **10**, Stage soft **8**, AR reference firm **10**, AR reference soft **8** |
| Historical controls | **17,608** pixel/frame digest fields across **12 families / 14 manifests** unchanged, including Melt and both Shatter trials |

The native mixed-route check also passed for Bounce + primary Embers and Bounce + primary Fireworks: primary packets and the companion ring unchanged, ordered counts/identities unchanged, only tagged body material and the explicit AR-body geometry differ, Stage unaffected by the geometry flag, and repeated builds do not advance time or RNG. All candidate/control pairs preserve state, linked body RGBA, selected order and non-body packets. Classic vs material-only preserves normalized full geometry; material-only vs tangent intentionally does not preserve active AR body corners. Facing the camera changes projected area/foreshortening, **not nominal half-extents or particle size settings**.

### Published review and local checks

- Native publication **927,802 bytes (0.8848 MiB)**, below the **0.9 MiB** CI cap. The paired AR frame bundles are exact native BGRA/XOR/gzip, not quantized video.
- Cumulative changed-file payload after publication, before the final report update: **133,256,197 bytes (127.0830 MiB)** versus base `a52ca16`, below 128 MiB.
- The 15 losslessly compacted stills were checked against Git revision `ee64074` for identical dimensions, decoded BGRA and display/resolution chunks. Their pixels also remain in the historical digest freeze. Animated PNG files were rejected and left alone.
- **123 Python tests** pass, including body-off/geometry/timeline tamper cases, bounds/encoding round trips and exact preview-bundle allowlisting. Static source and JavaScript syntax checks pass after publication.
- Real headless Chromium **153** verifies **480 decoded native BGRA halves**, all four soft/firm Stage/AR modes, labels at frames 0/11/12/13/59, pedal/seek/step/play/pause controls, no page errors and mobile overflow.
- Local publication verification covers source hashes, eight static panes and the 480 paired motion halves. Original full/off PNGs, 64 body-off PNGs, 64 legacy-axis PNGs, oblique/overhead statics and original crop derivation were validated **in CI**, not downloaded/re-rendered locally.

Run the native viewer:

```sh
python tools/serve_bounce_particle_art.py --output /path/to/cache/bounce-review --port 8780
REVIEW_SINGLE_IMPACT=1 REVIEW_FAMILY=bounce node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8780
```

### Art status and limits

**Both new flags remain default-off; art remains open.** The native images show a quieter, more continuous directional stroke in AR and less white-hot body energy. At compact resolution the rounded-head profile is still subtle, streaks overlap in the initial cluster, and the late tail is faint. The unchanged white-born third of the spray, white companion ring and common press lighting still draw attention; do not identify those shared highlights as a new Bounce core. Positive body-only pixels establish rendering, not an approved elastic/bouncing impression.

This is the existing upward impact spray with a new body surface and separate AR orientation option, **not extra collision/rebound physics**. Storage lane `held` is one soft .45 press, not a held emitter. Pedal at frame 12 is not a release effect. Motion covers Stage and AR reference only; two additional AR poses have static checks. Real cameras/physical GPUs, nonzero delay, arbitrary note directions, other quality tiers/palettes/layouts, realtime FPS and legibility in a normal combined scene are not accepted by this test.

Next: **Float Up / Dissolve / Smoke**, then combined art review; **settings UI last**. Native-source files are unchanged in the final documentation/viewer-only follow-up, so the successful source revision above remains the evidence source.
