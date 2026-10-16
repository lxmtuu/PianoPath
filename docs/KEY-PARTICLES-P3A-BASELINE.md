# P3a — bounded emission and current-family inventory

Status (2026-10-15): source `08c83b5`, [CI 38609015755](https://github.com/lxmtuu/PianoPath/actions/runs/38609015755) **SUCCESS** (static, portable tests, Windows build, lifecycle diagnostics, 70 inventory captures, full suite **5,120 assertions / one skipped group**, previews and packaging). This is a lifecycle foundation and current-shape baseline, **not the completed particle redesign**.

[Vietnamese visual review and image links](KEY-PARTICLES-P3A-REVIEW.md) · [Dated pixel/counter evidence](KEY-PARTICLES-P3A-EVIDENCE.json).

## What changes

- GPU sparks, wisps and flames share a pure `KeyEmissionBudget` helper. Each key/emitter holds only a fraction in `[0,1)`. Whole births blocked by the 24,000-particle pool are discarded, never deferred into a catch-up spray.
- Disabled/inactive/off-layout sources discard credit. Sparks/wisps follow the existing key-active state: a pressed snapshot **or a currently active song interval**, not pedal level alone. Flames intentionally retain their cooling-heat emission tail. No MIDI/sounding-state ownership redesign is implied.
- Soft clear drops particles, live transients, emission credits, key hold/spill/colour, energy/pedal/activity, release-scan cursor and note-render caches. It preserves the session animation clock, RNG stream and ambient field. A repeatable restart uses a **fresh simulation with an explicit seed**; soft clear is not a rewind.
- WPF fallback soft clear also drops wisp credit and transient beat/energy. Its clock and wisp rate remain physics-scaled; this is **not fallback parity**.
- Non-finite elapsed time cannot poison the GPU animation clock. Existing family shape IDs, profile coefficients, colour mixes, shader gains and palette/satin controls are retained.

## Tests and capture design

| Layer | Checks | Limit |
|---|---|---|
| Portable xUnit | Fractional schedule, capacity recovery, partial capacity, off/retrigger, invalid numbers, independent keys, 30/60/120/144 FPS totals | Pure helper, not product execution |
| Shipping simulation diagnostics | Five burst/held families, white/black keys, two Zone routes, actual full pool, release/pedal distinction, toggles, heat tail, soft clear/release cursor, seeded draw/no-draw replay | Explicit feed states; not OS MIDI routing |
| Stage/AR builders | Repeated output builds cannot age/spawn particles; later burst must preserve seeded RNG replay | Shared state does not mean equal projected shading |
| WARP PNG capture | Seven routes × five phases × two modes = **70 frames**; visible held-vs-off deltas, no Stage-space leak, expired tails, lossless PNG readback | AR Ultra, synthetic camera, Fit, no mirror, delay zero |

Five phases: emitter-off control and held at 0.60 s; one isolated impact at 0.05 s; released at 0.60 s after the held input ends at 0.35 s; expired tail at 2.50 s. Release/tail fixtures explicitly keep the pedal down. The isolated impact does **not** also set a held key; the held fixture does **not** enqueue an impact.

The capture manifest records the exact settings, seed, raw-BGRA pixel hashes, renderer counters and held-vs-off deltas. Full evidence is written to `phase1-visual-review/key-particles-p3a/{manifest.json,index.html}` in CI. Twenty representative press/held images (five families × two modes × two phases) are staged under `docs/previews/prism-flow-p3a` by the existing preview-commit workflow; Zone/release/tail/off remain in artifacts.

## Deliberately still open

1. **Art pass across every press/held particle channel:** five burst families, wisps/flames, particle-producing impact morphs, then their shared Stage/AR presentation. This baseline does not complete that work or create a new flow emitter.
2. Stage/AR size/gain/glow formulas still differ, notably Dust and handling of per-particle `Glow`. A common birth stream is not a visual-parity certificate.
3. [Frame-epoch follow-up](KEY-PARTICLES-FRAME-EPOCH.md) adds stamped snapshots, frame-fenced dequeues and an AR-key history boundary; [fresh CI 38617785565](https://github.com/lxmtuu/PianoPath/actions/runs/38617785565) passed at source `1b6ac51` (5,245 full-suite assertions / one skipped group). This does not create a full restart policy or solve OS MIDI/sounding ownership.
4. WPF has different physics/time semantics; descending live-trail touchdown deserves a separate audit. Recording, all quality tiers, nonzero AR delay, density/motion clips and physical-camera checks are not certified here.
5. P4 hit line, P5 real-device budgets and **P6 settings-menu redesign last**, consistent/readable/compact in both modes, remain required.

Keep the opt-in `Prism Flow Preview` palette/satin recipe as the particle-off control. No built-in recipe or material identity is replaced by this slice.
