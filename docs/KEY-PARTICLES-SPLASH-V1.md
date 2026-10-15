# Splash V1 — coloured liquid bead candidate

## Status

Native candidate verified in full Windows CI and real headless Chromium; **default-off/review-only, not user art acceptance**. Embers V2 remains unaccepted; accepted Wisps/Flames are not reopened. Settings remains last.

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

## Verification scope / open acceptance

- Windows build, portable C# tests, full native generation/geometry/body-colour checks and bounded publication: **PASS**, run below.
- Published verification of 19 source hashes, 8 panes and 480 native motion halves: **PASS**. Native static and 60-frame contact-sheet inspection complete; no automatic art verdict.
- Saved historical Wisps/Flames/Embers digest comparison: **PASS**, unchanged digests below.
- Real camera, hardware/perf, live MIDI, arbitrary palettes/tiers/delay, bloom/tint/spill and recording remain unverified. HTTP/JavaScript checks cannot certify browser playback.

Local Python validator tests use explicitly synthetic contract pixels, never claimed as native evidence. No local .NET SDK/Windows renderer is available; C# and HLSL require CI.


## Browser QA method

`tools/check_burst_particle_review_browser.cjs` uses **real headless Chromium**, not a mock DOM: decodes all 240 paired PNGs into canvas and compares 480 native BGRA hashes, then operates four modes, release/next/previous/seek and play/pause, checks page errors and a 390px mobile layout. Optional screenshot. Install browser dependencies outside the checkout; no generated binaries belong in Git. A system Chrome can be selected via `CHROME_BIN`; otherwise `@sparticuz/chromium` is supported (its bundled native libraries may require `LD_LIBRARY_PATH`). This is browser decoding/control QA, **not** realtime FPS, live camera, physical GPU or art acceptance. Passed on the published Splash page (and smoke-tested first on Embers V2).


## Native evidence and observed result

- [CI **38694214635**](https://github.com/lxmtuu/PianoPath/actions/runs/38694214635) **SUCCESS**, renderer source `ba78d827998a46696a7bec4ad16cb5a4091b8742`; preview publication `e53da10`. Static/portable tests, Windows build, full native suite, bounded Splash publisher, frame budget, galleries and package passed.
- **59 Python tests**, source checker (313 C# / 2 XAML), Python/JavaScript syntax and diff checks passed locally. C# and HLSL execution were checked on Windows CI, not simulated locally.
- Native matrix **192 statics / 720 motion crops** passed raw state, actual ordered component-packet geometry, population and identity-linked body-colour guards. Local subset verification passes **19 source hashes / 8 published static panes / 480 motion halves**. Full originals remain artifact-only; this subset cannot independently reverify every original/off frame.
- **Real headless Chromium** passed all 480 decoded native BGRA halves, release/step/seek controls for four modes, play/pause, no page errors, and 390px mobile overflow. This tests actual browser decoding/controller behaviour, not guaranteed 30-FPS playback or native WPF GUI interaction.
- Against saved pre-Splash manifests, unchanged SHA256 digest leaves: **342 static + 240 motion each for Wisps/Flames**, **3,466 Embers V1**, **4,378 Embers V2** (pixel/state/packet digests, excluding source provenance). Approved artwork was not reopened. Existing metadata compaction saved **1,145,572 bytes**, with parsed JSON unchanged before renderer provenance refresh.
- Splash committed subset **0.369 MiB**, below 0.8 MiB. Cumulative session changed-file payload approximately **127.11 MiB**, below 128 MiB. No accepted frames removed.

### Matched native signal · Prism / pitch 60

| View / phase | Body packets classic / candidate | RGB L1 off-delta classic → candidate | Pixels max ΔRGB > 4 classic → candidate |
|---|---|---|---|
| Stage / burst | 35 / 35 | 19,964 → 9,631 | 39 → 17 |
| Stage / held | 14 / 14 | 22,529 → 14,636 | 147 → 49 |
| AR reference / burst | 35 / 35 | 1,357 → 1,310 | 10 → 12 |
| AR reference / held | 14 / 14 | 6,500 → 6,501 | 30 → 38 |

Do **not** read this as a universal brightness/contrast increase. Stage deliberately loses much of its broad halo and white glint; its off-delta footprint/energy falls. Reference AR held has nearly unchanged absolute RGB delta but a larger thresholded footprint. L1 includes darkening, not just luminance; compare versions within the same scene only, never normalise Stage against AR with this table.

Inspected burst/held quartets at nearest-neighbour enlargement and all 60-frame contact sheets for all four published tracks, plus a real Chromium player screenshot/control run. Candidate Stage shows more discrete coloured bodies rather than diffuse glow; reference AR remains small and pixel-limited. Fine liquid meniscus/shoulder detail is not reliably readable in every subpixel drop; early burst still overlaps near the key. These remain **open art/readability limitations**, not test failures silently relabelled as final polish. Geometry/orientation and emitter timing were intentionally retained; no new velocity rotation or motion dynamics claimed.

Evidence: [burst](previews/splash-art-v1/burst.png), [held](previews/splash-art-v1/held.png), [gzip manifest](evidence/splash-art-v1/manifest.json.gz). **Classic left / candidate right; Stage above / AR below.**

```bash
# Requires Pillow. Native subset is verified before serving on 0.0.0.0.
python3 tools/serve_splash_particle_art.py --output /tmp/splash-review --port 8772
# Real browser QA, using externally installed puppeteer-core and Chrome:
CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8772
```

Next families: Fireworks, Confetti, Dust, then remaining auxiliary morph/release art. Embers V2/Splash V1 remain review-only; menu redesign remains last, consistent between Stage and AR.
