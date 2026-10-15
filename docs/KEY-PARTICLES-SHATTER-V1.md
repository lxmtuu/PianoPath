# Auxiliary particles · Shatter V1

## Scope and status

Native/portable CI and published-subset/browser checks complete; **visual approval remains open**, including an inherited AR raster hole (see below). **Default-off, review-only.** First auxiliary material pass: **ImpactMorph Shatter only**. Other morphs/releases and prior candidates remain unchanged; no art approval/promotion. Settings remains last.

| Auxiliary route | Existing primitive | Current scope |
|---|---|---|
| Shatter | shard 6, impact origin 0 | This facet + same-hue glint prototype |
| Melt | droplet 5, impact origin 0 | Baseline, later material pass |
| Bounce | streak 1, impact origin 0 + ring | Baseline, later material pass |
| Float Up | droplet 5, release origin 1 | Baseline, later material pass |
| Dissolve | droplet 5, release origin 1 | Baseline, later material pass |
| Smoke | dust 3, release origin 1 | Baseline, later material pass |

Wisps/Flames directions stay frozen. Absorb/Morph and Snap Back/Echo Rings are non-particle ring/flash channels; this work does not falsely classify them as covered. Held bar/electric arcs are likewise not changed here.

## Material / geometry contract

Internal `ShatterArtForReview` plus selected `ImpactMorph == "Shatter"`, shape 6, non-wisp, origin 0; no settings/preset/menu exposure. A bounded linear birth-hue pigment and single lifetime alpha, with a clipped wedge/faceted plane. Stage's glint adopts the body's hue and .45× alpha rather than a separately whitened hot line. White/pale native births may remain pale; no promise that overlapping fragments can never look white.

**Keep the two production representations:** Stage retains its kind-8 ellipse body + kind-7 line glint; AR retains one kind-2 projected body quad. New tags 31/32 copy the exact old VS extent, line padding/offset, axis, depth and softness payload. Material edits happen after original builders/cutoffs, never changing their eligibility/packing. No merging Stage components, no extra AR quad. Ordered packet hash normalisation restores intended tags and particle RGBA only. Physical body count is not confused with component count; strict packet hashes retain every component's geometry/order.

Actual Stage **body RGBA** is identity-linked and compared with AR bodies at three static poses. Stage's extra glint is a separate, explicitly derived payload, not misleadingly asserted identical to AR's single packet. Stage body identity selection mirrors its existing alpha > .004 / radius eligibility rather than assuming every living raw particle always packed. No particle state/RNG, count cap, density, speed, gravity, drag, selection order or lifetime change.

**Known orientation limitation:** legacy Stage shards tumble using age/phase; AR uses a velocity-aligned axis. That geometry is preserved, not silently changed to force material hashes to agree. Facet/RGBA are shared in local coordinates; this does **not** claim synchronized spin, perspective, final pixels or complete Stage/AR geometric equivalence. A separate orientation decision would require new geometry/packing review before promotion.

## Honest input/evidence plan

192 statics: off/classic/candidate × two palettes × two pitches × four views × samples 2/10/26/89 at 60 Hz (early .05s / mid .1833s / late .45s / expired 1.5s). Every active sample is one impact .9 at f0, no held or release births. Four motion tracks: Stage/reference AR × firm .9 / soft .45 single impact × 60 frames at 30 Hz × off/classic/candidate = 720 crops. Pedal turns on at frame 12 as an input control, **not a release event**.

Storage lane names `burst/held/released/expired` and track suffix `held` are inherited codec slots only: `burst=early/firm, held=mid/soft, released=late`. They do not mean actual held/released Shatter emission. Viewer labels and live status must show this; do not cite held–release coverage for this pass. Metadata declares actual sample frames and velocities, and validators reject the old burst/held fixture timeline. Existing auxiliary lifecycle tests continue to cover the production route's input policy separately.

Publication cap **1.5 MiB**, four lossless 60-frame BGRA XOR+gzip tracks, two native static quartets and gzip metadata. Full original/off frames remain artifact-only. No quantisation, resampling, frame loss or planned additional PNG compaction. Cumulative session payload must stay below 128 MiB.

Completed native/source/browser/lineage checks are recorded below. These are scoped technical checks, not whole-pose visual approval. Other palettes/tiers/delay, dense chords, live preset switches, real camera/MIDI/GPU, bloom/tint/spill/recording, realtime cadence and user art approval remain open.

## Native diagnosis: inherited AR raster-empty pair

Run [38787357450](https://github.com/lxmtuu/PianoPath/actions/runs/38787357450), source `7d4e645`, isolated `prism-ar-near-overhead-p61-held`: 10 raw particles / 10 actual body packets, **classic signal 0, classic pixel hash equals off**, candidate likewise blank. This is not candidate-only dimming, and packed body/RGBA parity is not visible-render proof.

Pin the same near-overhead / black-key / mid sample in Prism and Indigo explicitly. Both classic and candidate must equal off exactly, every other active static must show a candidate off-delta and a classic/candidate material delta. The manifest must disclose exactly these two IDs; unexpected new blank pairs, a candidate disappearing where classic is visible, or any changed pinned case fail validation. No global blank allowance, altered particles/geometry, brighter mask, skipped samples or substitute imagery. The 64 state/packet pairs are **46 visible active + 2 disclosed raster-empty + 16 expired**; the two empty cases are not material acceptance. Run 38788019460 confirmed exactly these two cases in the native dataset, including Indigo.

A separate AR shard orientation/billboard decision is required before promotion. Do not describe the whole three-pose matrix as visually passing merely because the state/packet guards pass.

## Verified result · Shatter V1, not promoted

- Native source **`650c2963620ec8d599de9e220eba86b834f8e86e`**; [Windows/portable CI 38788019460](https://github.com/lxmtuu/PianoPath/actions/runs/38788019460) **full success**, including native rendering, publication validator, WPF verification and package path. CI publication **`be1cc0baedc84e6dda736d35ff558ab52b477c17`**. Failed discovery runs above are not presented as successful runs.
- **192 statics / 64 paired state+ordered-component guards / 720 motion crops**. Static pairs are **46 visible active, 2 explicitly pinned classic+candidate raster-empty, 16 expired**. Actual identity-linked body RGBA matches Stage and all three static AR poses; this does not certify a visible body at the two empty samples, final pixel parity, glint parity or synchronized spin.
- Local publication verification: **21 source hashes, 8 exact static panes, 480 exact motion halves**, all 60 frames retained per track. Full original/off frames were verified by CI; they are artifact-only and were **not** independently re-read locally. Restricted Actions log download failed; no claim of having locally read the full verification-log artifact.
- **Real headless Chromium**: all 480 decoded BGRA halves match native digests; all four mode controls, frame 0/11/12/13/59 status semantics, pedal jump, step/seek, play/pause, no page errors and static-page 390px overflow check pass. Soft/firm labels and metadata verified; no held/release-emitter claims. The same shared browser checker also still passes the prior Dust V2 viewer. This is decoded-image/control QA, not realtime FPS or physical GPU evidence.
- **98 Python tests**, source checks (326 C# / 2 XAML / 119 resources / 161 named elements), Python/JavaScript syntax and diff checks pass. C# compilation/tests and HLSL execution were CI, not local Linux rendering.
- **Nine previous families unchanged:** Wisps/Flames V3 have 320 historical native pixel/frame digest fields each; Embers V1/V2, Splash V1, Fireworks V1, Confetti V1, Dust V1/V2 have 1,642 each. **12,134 prior native pixel/frame digest fields unchanged**, excluding provenance hashes. No previous candidate/default/preset promotion.
- Publication **996,474 bytes / 0.9503 MiB**, below 1.5 MiB. Cumulative changed-file payload approximately **127.00 MiB**, below 128 MiB. No new quantisation, downsampling, frame dropping or extra still compaction was introduced for Shatter. The existing CI PNG compactor continues its established lossless handling.

### Same-scene native signal · Prism / pitch 60

All rows have **10 classic / 10 candidate physical body packets**. Stage also retains its existing glint components. Storage phases below mean early/mid/late single-impact samples, **not held/release births**.

| View / sample | RGB L1 off-delta classic → V1 | Pixels max ΔRGB > 4 classic → V1 |
|---|---|---|
| Stage / early .05s | 22,121 → 18,340 | 61 → 62 |
| Stage / mid .1833s | 25,466 → 18,199 | 79 → 69 |
| Stage / late .45s | 14,888 → 10,818 | 61 → 69 |
| AR reference / early .05s | 6,697 → 9,051 | 18 → 46 |
| AR reference / mid .1833s | 6,323 → 12,552 | 13 → 54 |
| AR reference / late .45s | 4,003 → 5,750 | 15 → 49 |

L1 is absolute RGB difference from the same off scene, including darkening; **not luminance, an art score, or a cross-mode brightness measure**. Candidate Stage loses off-delta energy while reference AR gains covered/different pixels. Do not call this equalized brightness or a global contrast improvement.

### Native inspection / next decision

Inspected both static quartets at 6× nearest-neighbour, sixteen same-time samples across each of four native motion tracks (64 paired samples), and the actual Chromium player screenshot. Stage's harsh silvery line is reduced and adopts the body hue; pieces read more muted/olive-green in this Prism fixture, and late Stage fragments remain faint. AR reference shows more elongated angular chips than the classic point-like marks. Birth pigment is still pale; early fragments overlap near the key. The small two-plane facet is not reliably legible at native AR resolution, and this pass does **not** establish a polished reflective-glass look.

Most importantly, Stage retains age/phase tumble while AR follows velocity, and the two near-overhead black-key mid samples remain entirely raster-empty in both versions. **Keep Shatter default-off/review-only. A separate AR orientation/billboard review is needed before calling Shatter synchronized or promoting it.** Do not enlarge sprites, raise density/brightness, drop the two samples or weaken candidate-only disappearance guards to hide the baseline hole.

The inherited fields `staticReleaseFrame=21` and motion `releaseFrame=12` are **pedal-toggle storage fields only** in this producer, not physical note releases. Every track has just one f0 impact; soft motion uses .45 and firm uses .9. No held or release particles were redesigned or validated here. Melt/Bounce/Float Up/Dissolve/Smoke remain baseline; settings stays last. Real camera/MIDI, every palette/tier/delay, dense chords, bloom/tint/spill/recording, actual cadence and user art approval remain open.

Evidence: [early quartet](previews/shatter-art-v1/burst.png), [mid quartet](previews/shatter-art-v1/held.png), [manifest](evidence/shatter-art-v1/manifest.json.gz). Classic left / candidate right; Stage above / reference AR below.

```bash
PYTHONPATH=/path/to/pillow python3 tools/serve_shatter_particle_art.py \
  --output /tmp/shatter-review --port 8777
REVIEW_SINGLE_IMPACT=1 CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8777
```
