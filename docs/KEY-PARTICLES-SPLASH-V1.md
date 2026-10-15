# Splash V1 — coloured liquid bead candidate

## Status

Implementation complete enough for native CI; **native/visual results pending**, default-off/review-only. Embers V2 remains unaccepted; accepted Wisps/Flames are not reopened. Settings remains last.

## Material / boundaries

- New internal `SplashArtForReview`, never exposed through presets/menu/default settings. Tags 22–24 retain Stage halo/body/glint component quads; tag 25 shades the existing AR bead quad.
- Shared bounded hue-preserving intrinsic tint and a single `pow(1-age/life,.8)` alpha envelope. No pure-white specular replacement or HDR boost. The underlying tint resolver is reused from Embers but the Splash flag/envelope is separate.
- Coloured rounded bead, pinched upper shoulder and restrained meniscus/ridge, integrated derivative AA inside unchanged quads. Stage's existing offset glint remains tinted; halo restrained. AR composes its own bead on the existing quad. This is not a claim of identical compositing or apparent size/brightness across modes.
- **No change to positions, extents, velocity, emission, lifetime, RNG, caps, ordering or legacy alpha eligibility.** Stage creates the exact old three packets first, then rewrites only their material tags/colour. AR substitutes after old projection/eligibility checks.
- The drop still uses existing axis/fall-stretch: no new velocity-aligned rotation is claimed. Altering geometry is not hidden inside this appearance pass.

## Native contract

192 static captures / 64 classic-candidate state/ordered-packet geometry pairs; 720 original motion crops. Prism/Indigo, pitches 60/61, Stage plus three AR poses; burst/held/released/expired. Four 60-frame burst and held–release tracks; pedal enabled at step 12, never used to keep the held emitter alive.

Stage deliberately retains three draws per physical bead, AR one. `PackedParticles` counts the **actual body packet**, not all three component draws. Full ordered packet hash preserves every component position/extent/order/nonparticle payload; only intended material tags and component RGBA are normalized. Candidate actual body RGBA is identity-linked to the selected raw particle and compared across modes; it does not compare the auxiliary halo/glint to AR.

Published subset limited to 0.8 MiB (two ROI quartets, four lossless 60-frame APNGs, gzip metadata); original static/off-motion images remain CI-artifact-only. Existing approved JSON manifests are compacted without changing parsed data to make room below the cumulative snapshot limit; no frames or accepted deliverables deleted.

## Pending gates

- Windows build, portable C# tests, full native generation/geometry/body-colour checks and bounded publication.
- Verify 19 source hashes, 8 published panes and 480 native motion halves after publication; inspect native burst/held and motion before any art verdict.
- Compare saved historical Wisps/Flames/Embers digest manifests; no claim of frozen pixels until checked.
- Real camera, hardware/perf, live MIDI, arbitrary palettes/tiers/delay, bloom/tint/spill and recording remain unverified. HTTP/JavaScript checks cannot certify browser playback.

Local Python validator tests use explicitly synthetic contract pixels, never claimed as native evidence. No local .NET SDK/Windows renderer is available; C# and HLSL require CI.
