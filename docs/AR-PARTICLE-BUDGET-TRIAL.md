# AR fixed-cap selection trial — default off

## Status and evidence

The ensemble review exposed existing AR starvation: a dense held chord could use all 192 particle slots for impact/morph, leaving no held particles selected. This trial changes **selection policy**, not particle artwork. It is deliberately separate from the approved held/release materials and stays **default off**. No Settings, presets, density, brightness, physics, Stage depth, or shipping default was promoted.

- Native source: `4b47a75313023f7cbffca3f413556821a541b063`.
- [Focused Windows CI 38849028643](https://github.com/lxmtuu/PianoPath/actions/runs/38849028643): **SUCCESS** — source checks, portable .NET tests, Release build, 904 WARP captures, full original PNG/inventory validation, bounded publication.
- Evidence publication: `9a0555c`; [manifest](evidence/ar-particle-budget-v1/manifest.json.gz), six static sheets and two lossless temporal bundles in `docs/previews/ar-particle-budget-v1`.
- [Native comparison viewer](https://8784-isuyvot335g1n340b9fei.e2b.app/): 24 exact static crop panes; 64 exact temporal halves (two recipes × 16 paired frames). Real Chromium checked decoded BGRA digests, six recipe/pose choices, hold/release boundary, seek/step/play/pause, rapid selection/decode races and 390 px layout.
- Local Python: **150 tests PASS, 43.162 s**; static/source and whitespace checks pass.
- **Full regression:** [normal CI 38849867407](https://github.com/lxmtuu/PianoPath/actions/runs/38849867407), source `ebfcba8`, is **SUCCESS**: static checks, portable tests, full Windows build/regression and keyboard-light lane all passed. Publications: `ff1863b` (keyboard-light) and `bf4301c` (full previews). The focused budget lane was intentionally skipped in this normal run; its separate success is recorded above.

The first focused run, [38848552261](https://github.com/lxmtuu/PianoPath/actions/runs/38848552261), passed native rendering and all original pixel/inventory checks, then failed only the final publication-size gate (2,234,531 bytes versus an underestimated 1,100,000). The successful retry uses a measured 2,400,000-byte ceiling after reclaiming storage losslessly; no frames, precision, alpha, camera inputs or assertions were removed. Final published payload is **2,234,822 bytes**.

## Visual acceptance — 2026-10-16

The user reviewed the paired viewer and replied **“hợp lí rồi”**, accepting the displayed held/impact/release balance. Preserve this selection/artwork direction for subsequent hardening. This is visual acceptance of the shown native subset, not authorization to enable the review flag by default or certification of all poses, full lifecycle, live MIDI/camera, hardware or frame-time performance. The review flag remains unchanged by this acceptance record; this is not a default-promotion change. Settings redesign remains last.

**User follow-up:** “không cần kiểm, tớ kiểm rồi” — the user reports having tested this and explicitly declines the additional checks proposed by the assistant. Accept the user’s testing as the project acceptance decision for this step; do not rerun or block progress on the proposed CPU/runtime/longer-motion checks. Test devices, measurements and exact coverage were not specified, so this does not create independently measured FPS or universal hardware claims.

## Policy and isolation

`GpuLook.ArFairParticleBudgetForReview` has a private setter and defaults to `false`; only the explicit internal review clone can enable it. With the flag false and no inventory observer, the original capped loop remains the execution path.

The review path evaluates the existing lifetime, key-layout, alpha and projection gates, retaining the exact projected quad for every eligible particle. It then:

1. Keeps the full identity selection when eligibility is at or below 192.
2. Under pressure, cycles origins `0 / 1 / 2` (impact/morph, release, held), then active MIDI pitches within each origin, taking each bucket's next **legacy-order** particle. Empty shares are borrowed; it is not a rigid 64/64/64 partition.
3. Sorts selected indices back into the original eligibility order, preserving legacy relative compositing order **among the selected particles**.
4. Uploads only those unchanged quads, never more than 192.

Selection is deterministic for the same ordered input and uses no RNG. This is **not material-only**: particle identities and geometry/compositing intentionally differ under pressure. The algorithm is not age-persistent/hysteretic; preserving order does not prove frame-to-frame identity stability or absence of shimmer.

Stage does not use this allocation path. The trial does not alter emitter births, seeds, trajectories, lifetime, individual quad positions/scales, material tags, RGBA or the non-particle packet sequence. It does not enlarge the shipping AR cap.

## Native matrix and guards

840 static captures = five ensemble recipes × Prism/Indigo × four views (Live Stage, AR reference, oblique, near-overhead) × seven checkpoints × three modes:

- **fast:** real default flag-off/no-inventory path;
- **legacy:** full eligibility inventory, original first-192 selection;
- **fair:** identical inventory, trial selector.

Another 64 captures = the first two recipes × f29–44 inclusive × legacy/fair, AR reference/Prism. Replay is seeded `20261016`, 30 Hz. Same ensemble input: 60 on f0/off f12; chord 48/55/60/61/64/67/72/73 on f18/off f36; pedal f24–53. Lighting/materials remain the ensemble `mixed`/Key light 100 setup: held v3, release material, current Stage depth, shipping classic impact/morph.

Native C# assertions and the independent Python selector/inventory verifier establish:

- 280 static fast/legacy pairs have identical packet and pixel hashes.
- 140 fast Stage/AR-reference captures still match the frozen ensemble source `79930e52cb3773f02d9e8e59be2c111236917227` exactly; that baseline was not relabelled or refreshed.
- All 70 Stage legacy/fair pairs and all 96 under-cap static AR pairs remain packet/pixel identical.
- Legacy/fair raw state hashes/counts, key packets, non-particle quads and full eligibility inventories match.
- Selected indices obey cap/subset/order and the declared allocation policy. Actual uploaded particle quads equal the corresponding eligible payloads exactly, including RGBA. Full inventories and original 640×360 PNGs remain in the CI artifact, not Git.
- All **114 pressure cases** in the static AR matrix cover every eligible role/pitch group in this fixture. Legacy misses groups in 96 of those cases; trial misses none. All 114 pressure images differ, as expected for changed identities.

Twenty-five source/input hashes guard the diagnostic and render dependencies. This is not a claim that every transitive dependency or every possible keyboard/scene was tested. Raw population is not eligibility; eligibility and selection still do not prove a visible raster contribution for each particle.

## Concrete result and tradeoff

At **f33, Prism, AR reference**, all legacy cases select `[192, 0, 0]` (impact/morph, release, held). Trial results:

| Recipe | Eligible | Trial impact/morph | Trial release | Trial held |
| --- | ---: | ---: | ---: | ---: |
| Embers / Wisps / Shatter / Float Up | 1026 | 93 | 6 | 93 |
| Splash / Flames / Melt / Dissolve | 588 | 96 | 1 | 95 |
| Fireworks / Wisps / Bounce / Smoke | 1002 | 94 | 4 | 94 |
| Confetti / Flames / Shatter / Float Up | 648 | 93 | 6 | 93 |
| Dust / Wisps / Melt / Dissolve | 1058 | 96 | 0 | 96 |

Every eligible held pitch of the eight-note chord is selected. That also holds in all **30 f33 combinations** (five recipes × two palettes × three AR poses), not just the displayed reference crop. The small release populations at f33 are survivors from the earlier single-note release, not a premature chord Note Off.

At f10, selection and pixels remain exactly unchanged: held counts 54/23/54/23/54 across the five recipes, total particle counts 96/63/98/65/94. This is not a density boost.

At f38, the allocation depends on remaining eligible populations. Embers/Wisps keeps role totals `[30,52,110]` in both modes but changes which pitches/particles receive them; **same role counts do not mean same selected identities or pixels**. Splash/Flames changes `[127,65,0]` to `[64,64,64]`. The benefit is no longer excluding eligible held/release groups; the cost is fewer impact/morph slots under pressure. Equal role opportunity is a reviewable design choice, not an objectively optimal priority policy.

The viewer deliberately exposes both f33/f38 and f29–44 with synchronized halves. It uses exact fixed crops, not full-frame playback; particles/notes outside the crop are omitted. Near-overhead and oblique crops are explicitly separate from the motion section, which always uses AR reference. Motion playback preserves every native frame without interpolation; browser decode cadence is not an app frame-time measurement.

## Cost and remaining acceptance

The old path could stop projecting after 192 accepted particles. The trial scans/projects **all** eligible particles and allocates selection queues/arrays. The recorded `DrawMilliseconds` includes diagnostic replay, setup, packet construction, WARP rendering and readback; it is **not an isolated selector benchmark or real-app FPS**. No performance improvement is claimed. These are known implementation costs, not a request for additional testing: the user has tested this step and declined further checks.

Selected held quads now exist across pitches in this matrix, but this is not proof that every body is visually strong enough at every pose. The comparison reduces impact density; Wisps remains subtle in dense scenes. No isolated held-body-off raster control was added here. Visual balance is accepted and the user reports completing their own testing; additional assistant-led runtime/longer-motion checks are waived at the user’s request. The automated evidence does not independently establish sustained device load, universal hardware/driver coverage or flicker-free motion.

The approved artwork remains the baseline. Settings redesign is still the **last** roadmap stage and was not started. No default/preset/depth promotion follows automatically from the green diagnostic.

## Reproduce

On Windows with .NET 10 and the repository samples:

```powershell
$env:GITHUB_SHA = git rev-parse HEAD
dotnet build PianoPath.csproj --configuration Release
Start-Process -FilePath 'bin/Release/net10.0-windows/PianoPath.exe' -ArgumentList '--verify','--verify-ar-budget-only','--verify-review-out=<directory>','--verify-log=<log>' -Wait
python tools/review_ar_particle_budget.py <directory> --source-root . --expected-revision $env:GITHUB_SHA
```

For the checked-in subset (Pillow required), with matching source guards:

```sh
python tools/serve_ar_particle_budget.py --output <cache-directory> --verify-only
python tools/serve_ar_particle_budget.py --output <cache-directory> --port 8784
node tools/check_ar_particle_budget_browser.cjs http://127.0.0.1:8784
```

Artifact-only originals/inventories use the repository's effective seven-day retention, despite the requested fourteen-day upload setting. The checked-in subset is bounded; generated full frames and decoded viewer caches stay outside Git. To keep the cumulative snapshot bounded, 55 explicitly allowlisted, already-changed opaque native stills were compacted losslessly, saving **3,592,868 bytes** with exact before/after decoded BGRA hashes. No APNG/animation or source camera image was compacted. Historical evidence comparisons still pass: **69,964 pixel/frame/state/geometry digest fields, 16 families / 18 manifests unchanged**, excluding source/revision metadata.
