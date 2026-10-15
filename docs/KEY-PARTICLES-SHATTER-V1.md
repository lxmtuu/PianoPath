# Auxiliary particles · Shatter V1

## Scope and status

Native validation in progress; first run exposed a legacy near-overhead raster hole (see below). **Default-off, review-only.** First auxiliary material pass: **ImpactMorph Shatter only**. Other morphs/releases and prior candidates remain unchanged; no art approval/promotion. Settings remains last.

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

Pending: Windows/native/portable CI, source/published-pixel verification, Chromium actual decoding/controller/label QA, native inspection, nine-family historical digest comparison. Other palettes/tiers/delay, dense chords, live preset switches, real camera/MIDI/GPU, bloom/tint/spill/recording, realtime cadence and user art approval remain open.

## Native diagnosis: inherited AR raster-empty pair

Run [38787357450](https://github.com/lxmtuu/PianoPath/actions/runs/38787357450), source `7d4e645`, isolated `prism-ar-near-overhead-p61-held`: 10 raw particles / 10 actual body packets, **classic signal 0, classic pixel hash equals off**, candidate likewise blank. This is not candidate-only dimming, and packed body/RGBA parity is not visible-render proof.

Pin the same near-overhead / black-key / mid sample in Prism and Indigo explicitly. Both classic and candidate must equal off exactly, every other active static must show a candidate off-delta and a classic/candidate material delta. The manifest must disclose exactly these two IDs; unexpected new blank pairs, a candidate disappearing where classic is visible, or any changed pinned case fail validation. No global blank allowance, altered particles/geometry, brighter mask, skipped samples or substitute imagery. The 64 state/packet pairs are **46 visible active + 2 disclosed raster-empty + 16 expired**; the two empty cases are not material acceptance. Native confirmation of the second palette is still pending.

A separate AR shard orientation/billboard decision is required before promotion. Do not describe the whole three-pose matrix as visually passing merely because the state/packet guards pass.
