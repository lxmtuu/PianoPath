# Confetti V1 — paper surface and synchronized material rotation

## Status

Full native CI and published-pixel/browser review passed; not user art approval. **Default-off, review-only.** Previous Embers/Splash/Fireworks candidates are not silently promoted; accepted Wisps/Flames remain frozen. Settings stays last.

## Material and rotation contract

- New internal `ConfettiArtForReview`, additionally gated to `ImpactBurst == "Confetti"` and non-wisp shape 2. Tag 28; no preset/menu/default exposure.
- Preserve positions, size, axes, velocity, density, spawn credit/RNG, lifetime, cap, selection and packet order. Exactly one Stage sprite / one AR quad per selected paper particle. Old Stage square quads and AR rectangular quads are unchanged.
- Shared clipped-corner rectangular paper with a same-hue fold, bounded intrinsic tint. Shape is expressed in raw-particle-size units to retain material aspect despite different quad aspect ratios. The polygon fits inside a unit-radius circle (corner vectors .94/.27 and .66/.55), hence inside the narrow old AR quad at every angle. It occupies less surface than the old luminous square; a universal brightness increase is **not** the goal or a guaranteed result.
- Preserve Confetti's legacy opacity `min(1,(1-age/life)*1.5)`, including its existing independence from ParticleGlow. No new HDR/white highlight or temporal brightness pulse; pale/white native births remain pale/white. RGB is shared through the bounded birth-tint resolver.
- Existing Stage geometry rotates by **age in seconds × 7 + birth phase**. Legacy AR uses **normalised age × 7 + phase**, a different angular clock. Candidate AR now receives the actual cosine/sine material basis matching Stage, and inverse-rotates the surface inside the unchanged projected quad. No geometry rotation, second rotation on Stage or new simulation dynamics is introduced.

## Native verification

192 statics / 64 paired raw-state, physical geometry/order/count guards: two base palettes, two pitches, four views and burst/held/released/expired. 720 original motion crops: Stage/reference AR × burst/held-release × 60 frames × off/classic/candidate. Physical release before step 12, pedal on but no sustained emitter.

`MaterialHash` links **actual packed RGBA and actual orientation cosine/sine** to each raw particle before diagnostic canonicalisation. It must match Stage and all three AR poses for candidate statics. Stage ordered packet normalisation restores tag 28→3 and zeroes intended particle RGBA only. AR normalisation restores tag 28→2 and the two explicitly changed material lanes to the raw particle's original progress/shape; all actual corner positions, weights, order, phase and nonparticle payload stay strict. The producer does not reorder packets to make hashes match.

Publication limit **3.0 MiB**: two native ROI quartets, four lossless 60-frame BGRA XOR+gzip bundles and gzip metadata. Full originals/off frames remain artifact-only. Cumulative payload must remain under 128 MiB; sixteen more explicitly allowlisted opaque legacy stills are compacted RGB losslessly, preserving dimensions, every decoded BGRA pixel and sRGB/gamma/physical-resolution chunks. No accepted image/frame deleted, recoloured or resampled; animation/nonopaque input rejected.

## Verified native candidate

- [CI **38761082847**](https://github.com/lxmtuu/PianoPath/actions/runs/38761082847) **SUCCESS**, native source `bcdd74837d190a480f594cef5ee652d29be951ac`, preview publication `2fe5933`. Static/portable tests, Windows build, full native generation, publication validation, frame budget, galleries and package passed. The first run `38760931898` failed the preview-folder source guard before native execution; it is not a native success. The narrow allowlist fix admits exactly eight existing/new native track paths, not arbitrary gzip files.
- **192 static captures / 64 paired raw-state, geometry/order/count guards / 720 motion crops** passed on native Windows WARP. Actual candidate RGBA **and rotation basis**, linked to raw identities, match Stage and all three AR poses in the static matrix. Final composited pixels need not match across modes.
- Local published-subset validation: **19 source hashes, 8 static panes, 480 exact motion halves** pass. Every published track contains all 60 native frames at manifest 30 FPS. Full original/off frames are artifact-only, not locally revalidated. Downloading the separate verification-log artifact was blocked by the restricted artifact host; no claim of locally reading that log.
- **Real headless Chromium 153**: all 480 decoded native BGRA halves match the manifest; all four mode release/step/seek controls, play/pause, no page errors and 390px static-page mobile overflow check pass. This is decoded-image/control QA, not realtime cadence, native WPF GUI, camera or physical GPU acceptance.
- **73 Python tests**, source checker (319 C# / 2 XAML / 119 resources / 161 named elements), syntax/diff checks pass. Local tests are contracts; C#/HLSL execution was Windows CI, not a synthetic stand-in.
- Compared freshly regenerated evidence with six frozen historical families: **320 pixel/frame digest fields each for Wisps and Flames** (64 static fulls + 16 sheets + 240 motion frames), **1,642 each for Embers V1/V2, Splash V1 and Fireworks V1** (192 statics + 720 crop hashes + 720 full-frame hashes + 2 sheets + 8 panes), unchanged. These are pixel digest comparisons, excluding changed source provenance, not inflated counts of distinct captures.
- All sixteen newly compacted opaque stills match the pre-change `cbe6e7b` decoded pixels, dimensions and original display chunks exactly. Reclaimed **2,868,162 bytes**, with no animation/frame loss, quantisation or accepted-art changes.
- Published family **431,408 bytes / 0.4114 MiB**, below 3.0 MiB; cumulative session changed-file payload approximately **124.94 MiB**, below 128 MiB.

### Same-scene native signal · Prism / pitch 60

| View / phase | Packed classic / V1 | RGB L1 off-delta classic → V1 | Pixels max ΔRGB > 4 classic → V1 |
|---|---|---|---|
| Stage / burst | 35 / 35 | 13,258 → 9,833 | 24 → 24 |
| Stage / held | 14 / 14 | 40,256 → 24,942 | 90 → 94 |
| AR reference / burst | 35 / 35 | 3,144 → 2,534 | 17 → 17 |
| AR reference / held | 14 / 14 | 21,950 → 15,022 | 78 → 80 |

L1 is absolute RGB off-difference including darkening, not luminance, a quality score or a cross-mode brightness metric. Candidate paper deliberately covers less area and loses substantial energy; a few more thresholded held pixels do not mean brighter or universally more legible.

### Inspection and remaining risks

Inspected both native static quartets at 8× nearest-neighbour enlargement, sixteen same-time samples across each of the four 60-frame motion tracks, and the actual Chromium player screenshot. Held candidate paper is narrower and less square/luminous; colours and angular silhouettes remain distinguishable against Stage/AR backgrounds. The subtle same-hue fold/clipped corners can be subpixel and are **not reliably readable** at small AR size. Candidate tails become dimmer; native-size visibility still needs user review.

Burst still overlaps into a curved bright/multicolour band, not cleanly separated falling paper. It is a retained emission/trajectory limitation, not something shader colour/rotation alone solved. No density/spread/lifetime/size changes were made to hide it. The intrinsic angle clock is synchronized, not identical screen-space orientation after AR perspective. Static oblique/near-overhead guards pass, but their motion is not published here.

Real camera/MIDI, every palette/tier/delay, bloom/tint/spill/recording, actual playback cadence, physical GPU performance and **user art approval remain open**. No local .NET/Windows renderer is available. Previously accepted Wisps/Flames remain frozen, other candidates remain review-only, Dust/auxiliary art follows and settings stays last.

Evidence: [burst quartet](previews/confetti-art-v1/burst.png), [held quartet](previews/confetti-art-v1/held.png), [gzip manifest](evidence/confetti-art-v1/manifest.json.gz). Classic left / V1 right; Stage above / reference AR below.

```bash
# Pillow required; validate native subset and serve on 0.0.0.0.
python3 tools/serve_confetti_particle_art.py --output /tmp/confetti-review --port 8774
CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8774
```
