# Float Up / Dissolve / Smoke — physical-release material trials

**User-tested and visually accepted for all three reviewed candidates (2026-10-16).** The implementation remains default-off/review-only; settings/menu/default presets have not been promoted or redesigned.

## Scope and appearance

- **Float Up:** soft buoyant beads, quieter same-hue halo and glint; retains the original upward motion and slow alpha envelope.
- **Dissolve:** restrained fine-grain coverage rather than another white liquid sparkle; retains original spread, drag and lifetime.
- **Smoke:** a soft two-lobe veil inside the existing dust square, preserving the original grey-dominant birth tint and subdued alpha.
- CPU-owned bounded linear-light body RGBA is shared between Live Stage and aligned AR. Shader coverage is shared, but full compositing/brightness is not claimed identical: droplet Stage retains its existing halo/body/offset-glint packets, while AR retains one body quad; Smoke retains one packet per output.
- CPU-only provenance routes **3/4/5**, origin 1, distinguish the three release tails from primary Splash/Dust, Melt and Bounce. The source tag follows the particle after changing the selected release setting. Historical JSON ignores the metadata; this producer hashes it explicitly.
- No additional births, RNG draws, size, velocities, gravity, drag, lifespan, caps, selection, packet order, Stage XY geometry or AR corners/weights are introduced by the material flag.

Two independent default-off flags: `ReleaseArtForReview` changes pigment/coverage, while `ReleaseFrontLayerForReview` changes **only tagged Stage sprite depth from .6 to .01**. Material kinds 40–46 retain .6; kinds 47–53 are the front-layer trial. AR keeps its existing axes and depth-off pass. This is a deliberate Stage depth-policy experiment, **not** an unchanged-geometry/material-only claim. Each Stage candidate has a same-material .6-depth control with identical raw state, normalized ordered packet and body-RGBA hashes.

## Real lifecycle, not relabelled impact/held tracks

Every capture uses actual `LiveNote` down/up events, never `Impact`:

- Note On at f0, velocity .9; physical key held before release.
- Statics: Note Off before step **f21**; samples **f23/f31/f47/f110** at 60 Hz, offsets **2/10/26/89** from release. Main samples use pedal on after release, with matched pedal-off controls.
- Pre-release: f20 candidate/off pairs must have no raw/packed particles and identical native pixels.
- Motion: Note Off before step **f12** at 30 Hz; both lanes use the same .9 press and physical release. Historical storage name `held` means **pedal on**, `burst` means **pedal off**, not a held emitter or impact. Each lane keeps all 60 exact frames.
- Full-sequence local validation additionally requires no births/pixel changes before f12, a release birth at f12, non-increasing raw population thereafter, expiration by f59 even under pedal, and identical state/body material/ordered packets between pedal lanes. Whole-scene pixel equality between pedal states is measured separately, not assumed from state equality.

Static matrix per family: two palettes, white/black keys, Stage plus AR reference/oblique/near-overhead. **192 main + 16 depth + 64 pedal + 32 pre-release images = 304 statics** per family; **720 native motion crops** per family, Stage/AR reference only. Group: **912 statics + 2,160 motion crops**. All 48 active main candidates per family must contribute off-delta, with no blank exemptions; 16 expired main triples must be identical. These are strict fixture requirements, not universal readability or hardware acceptance.

## Native outcome

Source **`9cbf1e4579a151934c19c4a4725e340a9075b726`**, [Windows CI **38834302456**](https://github.com/lxmtuu/PianoPath/actions/runs/38834302456): **fully successful**, including portable C# tests, original native render validation, the full regression/lifecycle suite, WPF/previews and distributable publication. Evidence publication **`0a09aa8`**. Follow-up tools/docs below do not change native C#/HLSL source.

| Per-family measurement | Float Up | Dissolve | Smoke |
|---|---:|---:|---:|
| Statics / motion crops | 304 / 720 | 304 / 720 | 304 / 720 |
| Active main candidates with off-delta | 48/48 | 48/48 | 48/48 |
| Minimum native RGB L1 signal | 1,135 | 162 | 1,167 |
| Minimum pixels with max-channel delta >4 | 7 | 4 | 43 |
| Expired main triples off-identical | 16/16 | 16/16 | 16/16 |
| Material-only Stage active controls blank | **0/12** | **0/12** | **0/12** |
| Stage front/material-only pairs pixel-identical | 16/16 | 8/16 | 12/16 |
| Matched pedal-off/on statics pixel-identical | 64/64 | 64/64 | 64/64 |
| Pre-release candidate/off pairs empty and equal | 16/16 | 16/16 | 16/16 |
| Motion first raw birth / last alive frame | 12 / 43 | 12 / 32 | 12 / 48 |
| Motion candidate off-delta frames, Stage / AR reference | 32 / 32 | 21 / 21 | 34 / 37 |

The depth experiment **is not required to avoid blank samples in this release matrix**. Unlike the earlier Melt finding, every material-only Stage control here is already visible. Float Up is pixel-identical at both depths throughout all 16 statics. Dissolve has eight nonempty partial-occlusion differences (for example indigo/p60/late signal 130 at .6 versus 162 at .01); Smoke has four small early differences. This is evidence for a separately reviewable policy difference, **not** a reason to silently promote front-layer rendering or claim a release occlusion failure was fixed.

Actual Stage/AR raw state, ordered normalized geometry and particle-linked intrinsic body RGBA checks passed across all static poses. The native mixed-source guard retains primary Splash and Melt alongside all three release tails while changing the selected release setting; no unrelated packets, counts or AR geometry change, and repeated builds do not advance simulation time or spawn. Eligibility tests additionally reject primary Dust's origin/route and wrong shapes/wisps.

Local full-sequence metadata checks passed for both pedal lanes: no raw/packed particles before f12, an actual birth at f12, no population increase afterwards, expiration before f59, and matched state/RGBA/packet hashes. All 60 full-frame native hashes for off/classic/art match between pedal-on/off lanes in each view, and the corresponding published paired bundles are byte-identical. This equality is **specific to the isolated fixture with tint/spill/bloom/damper strip disabled**, not all pedal-dependent scene lighting.

**55,486 historical native pixel/frame/state/geometry digest fields** across **13 families / 15 manifests** match the pre-release freeze after publication, including Wisps/Flames, Melt, Bounce and both Shatter trials. Source/revision metadata is intentionally allowed to advance.

## Publication, reproducibility and limits

The repository retains two native static paired sheets and four exact paired BGRA XOR/gzip motion bundles per family, with schema-4 provenance manifests under `docs/evidence/release-*-v1`. Full original/off/control PNGs and original crop derivation are verified in Windows CI, not redrawn or replaced with generated imagery. Local verification covers source hashes, eight static panes and 480 decoded native motion halves per family; original full datasets were not downloaded locally.

Combined publication: **883,973 bytes**, consisting of Float Up **272,124**, Dissolve **279,561**, Smoke **332,288** bytes; each below its .65 MiB CI cap. Cumulative changed-file payload versus `a52ca16` after publication, before this report, was **132,617,458 bytes (126.474 MiB)**, below 128 MiB.

**136 Python tests** pass, including lifecycle/pedal tamper cases, exact bundle round trips, cross-family HTTP isolation and the twelve-path release bundle allowlist. Static source and JavaScript syntax checks pass. Real headless Chromium **153** verifies **1,440 native BGRA motion halves** across the three families, all twelve Stage/AR pedal modes, initial post-release f14, frame 0/11/12/13/59 statuses, step/seek/play/pause, no page errors and mobile overflow. The combined three-family gallery, navigation and measured depth limitations were also checked in Chromium. These are decoding/UI checks, not realtime rendering benchmarks.

Native original-image validation ran in CI. The large full artifact and verification logs were not downloaded through the restricted artifact host; the retained publication, native provenance and completed job/step statuses are the available local evidence. No claim is made of locally re-rendering the Windows originals.

Nine explicitly allowlisted opaque native stills reclaim **1,614,686 bytes**, preserving dimensions, decoded BGRA and original colour-management/resolution chunks. No palette quantization, image resizing, exposure adjustment, frame dropping or animation rewriting is used. Historical native digest comparison excludes only source/revision metadata.

Run the combined review (Pillow required):

```sh
python tools/serve_release_particle_art.py --output /path/to/cache/release-review --port 8781
# --verify-only validates/builds all three family pages without starting a server.
REVIEW_RELEASE=1 REVIEW_FAMILY=release-float-up node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8781/float-up
# Repeat with release-dissolve /dissolve and release-smoke /smoke.
```

The gallery links all three effects. The player initially opens f14, after release, not an empty held interval. Its lanes and statuses explicitly distinguish physical key release from independent pedal state; frame seeking/stepping and play/pause preserve exact native frame identity. Playback is not a realtime-FPS benchmark.

## Art status

**User acceptance recorded on 2026-10-16:** “tớ đã test cả ba và thấy ổn”. Float Up, Dissolve and Smoke are accepted visually for the versions the user reviewed. The test environment, camera poses, hardware and whether the user separately compared the Stage depth controls were not specified; this acceptance does not assert broader coverage or select a production depth policy. Both review flags remain default-off, with no runtime/settings change.

The following observations were recorded before user acceptance and remain context, not an unresolved request for that same visual approval. Native sheets show Float Up as smaller, more coloured beads rather than white glints; Dissolve is finer and particularly faint late in its short life; Smoke softens the white puffs, but the two-lobe silhouette is not reliably legible at normal resolution and AR can blend into the common key/camera texture. The large shared bright key region in paired crops is not new Smoke or a new particle core. Positive native off-delta is not proof that the three identities read distinctly enough in a real performance.

The viewer uses exact native pixels with nearest-neighbour enlargement and no colour/exposure edits. Physics/size/counts remain frozen; the accepted versions are retained as the visual baseline rather than enlarged or retuned after acceptance.

Still unverified beyond the user-reported test: palette/tier/layout breadth, all camera directions and motion poses, ordinary-scene lighting coverage, actual cameras/GPU/MIDI devices and nonzero camera delay. Neither software WARP pixels, browser decoding nor the unspecified user test establishes universal coverage. Earlier trials remain frozen controls. Default promotion and the separate Stage depth-policy decision are not authorized by this visual feedback alone; settings UI remains last.
