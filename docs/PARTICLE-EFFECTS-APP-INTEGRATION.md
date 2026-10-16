# Accepted particle effects — application integration

## Scope

The user selected **“Đưa hiệu ứng đã duyệt vào app”** after accepting the AR allocation balance and reporting that their own testing was complete. This implementation connects those accepted effects to normal application settings; it does not start the final Settings redesign or promote every experimental effect.

The persisted opt-in is `PianoVisualSettings.UpgradedParticleEffects` (default `false`). Old files without the property and existing presets retain their old look. This is a real application control, not a diagnostic launch flag.

## How to use

On the **Particles** page, enable **Upgraded particle effects** / **Hiệu ứng hạt nâng cấp**. It applies to both GPU Live Stage and AR, including the GPU look snapshot used by recording/export. No need to run `--verify` or edit JSON.

Alternatively apply either embedded community preset:

- **Prism Flow - Wisps**: Embers + Wisps + Shatter + Float Up.
- **Prism Flow - Flames**: Splash + Flames + Melt + Dissolve.

These use the accepted ensemble recipe settings. Preset application retains the existing machine-local camera/calibration and AR configuration through the normal `CopyFrom` path; it does not turn AR on, change MIDI/audio routing or pick a different graphics backend. The existing `Prism Flow Preview` palette-only preset remains particle-off.

The option changes **appearance and AR selection**, not channel enablement: existing sparks/Wisps/Flames/release switches, intensities and styles still decide what is emitted. For example, choose Smoke in the existing release control to use the approved Smoke material. Turning the option off restores classic rendering/selection, or the older held-v2 material if the user's existing `HeldParticleArt` option was already true. The underlying legacy setting is not overwritten.

## Runtime wiring

`GpuLook.From` snapshots the persisted option, enables the shared held-material path when opted in and exposes four effective policies:

| Policy | Effect |
| --- | --- |
| `UseWispArtV3` | Approved Wisps v3 surface in Stage/AR |
| `UseFlameArtV3` | Approved Flames v3 surface in Stage/AR |
| `UseReleaseArt` | Approved Float Up/Dissolve/Smoke material routes |
| `UseFairArParticleBudget` | Accepted role/pitch allocation, still capped at 192, AR only |

The existing independent diagnostic opt-ins are preserved. Ordinary application settings do not set those private flags. Stage and AR packers read the same effective material policies; AR reads the effective allocation policy. A setting change publishes a new immutable look, not a mutable settings reference to the render thread. Persistence, clone, presets and share codes carry the new Boolean through the existing settings serializer/copy path.

No births, RNG, trajectories, source artwork, alpha, key-light curve, cap, shader or depth values were changed in this integration. Burst/morph materials remain shipping classic; separate Shatter/Melt/Bounce depth/billboard experiments remain off. WPF/software rendering stays classic, as stated in the control tooltip and preset descriptions. This is not a claim of software/GPU visual parity.

Only a small card/control was added to the existing Particles page, with VI/EN labels, search metadata, accessibility name and the normal setting-change/save machinery. Navigation, page grouping, camera controls and the later Settings redesign are untouched.

## Verification status

The earlier trial evidence remains frozen at its recorded source; see [AR allocation trial](AR-PARTICLE-BUDGET-TRIAL.md). It must not be relabelled as evidence generated from this integration.

The initial integration commit `9f6511f` did not create a Windows build. The subsequent compile/package-only run [38856662037](https://github.com/lxmtuu/PianoPath/actions/runs/38856662037) is **SUCCESS** at source `f5d201e064b95f279d38693ed90a0355a0dadffa`: static source checks and self-contained Windows x64 publishing passed. Native capture, Python/.NET test suites, performance measurement and the full runtime regression were explicitly skipped, in accordance with the user's request. Build success is not a new runtime/visual acceptance claim.

[Download the private integration artifact](https://github.com/lxmtuu/PianoPath/actions/runs/38856662037/artifacts/12027432990): `keyflow-app-win-x64`, 70,953,281 compressed bytes, seven-day retention. Extract it and run `PianoPath.exe`; the .NET runtime is bundled. `BUILD-INFO.txt` records the source SHA and executable SHA-256. This is an unsigned branch build, not an installer or a public release; no release tag was created.

Future verification contracts cover old-file defaults, JSON/clone/share/apply persistence, both output snapshots, turning the option off, snapshot immutability, shelf availability, exclusion of unselected experimental flags, continuous export replay and planned scene time. These runtime/test contracts have not been executed in this step.

Source-only checks (`tools/check_sources.py`) and whitespace inspection were used for implementation hygiene; they do not constitute a new native/runtime regression or a Windows build.

## Offline AR export follow-up

A source audit found that `ArRenderToVideo.Run` restarted `GpuStageSimulation` for every output frame, discarding particle ages, emitter credit, RNG progression and release history. It now owns one `GpuOfflineFrameSequence` per take, reusing its GPU target/readback resources and advancing the same simulation throughout. Independent snapshot helpers are unchanged. Explicit caller-issued transient clears are still honored.

The export UI now publishes deterministic song positions through `plan.PositionAt(tick)`, rather than dividing by the requested rate when the actual plan uses a different rate. Audio was already using the plan rate. Onset events now queue visual impacts even for silent exports; song intervals and the existing song-release scanner own held/release state, avoiding duplicate live Note Off effects. Slow export frames are split into <=50 ms simulation steps so the engine's per-step clamp does not silently lose elapsed time.

The existing half-requested-rate camera-file planning policy, decoder polling/rewind behavior, audio routing and output formats are not redesigned here. Camera-source resampling and universal audiovisual sync are not claimed. Live rendering, particle artwork, cap and preset settings are unchanged by this follow-up. Future verification contracts were updated to compare continuous replay (not an isolated third frame) and to check accumulated scene time and planned-rate positions. No new native/performance run was requested.
