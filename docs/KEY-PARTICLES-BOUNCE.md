# Bounce material + AR tangent-billboard trial (default-off)

Status: native verification pending; **not art accepted or default promoted**.

The candidate rounds the leading end of the existing directional streak and tapers its tail, with bounded birth-hue pigment and one CPU-owned lifetime alpha shared between Stage and aligned AR. The existing neutral-white third of Bounce births remains neutral; there is no newly generated white-hot core, temporal noise, geometry, collision or extra rebound. This is an appearance trial for the existing upward impact spray, not a physics redesign.

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
