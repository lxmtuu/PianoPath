# Offline video — preserve images, not only timeline slots

2026-10-15. Fix verified: full build/test/benchmark/installer pipeline PASS. Artwork review remains separate.

## Failure and bounded diagnosis

[Run 38634465675](https://github.com/lxmtuu/PianoPath/actions/runs/38634465675), source `892f1d0`, passed portable/build and isolated held-art captures/clip assembly, but full verification failed in `VerifyArRenderToVideo`: decoded **5 of 12** AVI frames, stream ended; at least six content frames required. This was not a capture/material failure. No AVI binary was preserved in that run, so the exact failed file cannot establish whether the writer or decoder discarded each missing frame.

A concrete production defect is visible in code: `ArRenderToVideo.Run` submitted offline frames through the live `WriteFrame` API. AVI/MP4/WebM live queues retain four waiting images; under pressure they merge later inputs into repeats of the tail image. AVI can represent repeats with empty samples, and a decoder may deliver fewer images than accepted timeline slots. Offline export must not depend on encoder speed or lose changing camera/input frames.

## Fix

- New `IFrameRecorder.WriteOfflineFrame`: synchronous writers keep their direct write; all three asynchronous writers apply bounded backpressure instead of coalescing. The render sweep uses it for every frame.
- Queue remains bounded at four pending images, with buffer copies on acceptance. Encoder dequeue/error/close wakes waiters. A wedged encoder fails after a 30-second capacity wait rather than allocating unbounded memory or waiting forever.
- Live REC keeps its existing nonblocking/repeat policy. Alpha, frame packing, timestamps, audio timing, render timestep and particle materials are unchanged.
- AVI finalization now reports a worker error or a ten-second drain timeout instead of silently claiming completion.
- **No test threshold lowered**, no extra padding ticks or sleep/retry workaround. Decoder end-of-stream behavior is not changed without evidence.

## Regression evidence required

- Portable: bounded full queue, twelve ordered images, wakeup on worker failure, closed recorder, bounded timeout; RIFF inventory distinguishes 12 timeline slots/5 payloads, 12 distinct images, duplicates and truncation. No GPU/codec proof inferred from these tests.
- Windows: rapid 32-frame offline AVI burst must finalize **32 nonempty distinct payloads** through the interface dispatch, despite the four-image queue. Native RGB/MJPEG path remains exercised.
- AR take: actual `movi` payloads must be **12/12 nonempty and distinct**, not an accepted counter or `idx1` token count; the camera-file take also requires 12 nonempty payloads. Existing minimum-six decoded frames and ordered light/camera probes remain intact.
- `AviFrameInventory` is a small structural diagnostic for the app's single-stream RIFF AVI output, not a replacement for Media Foundation decoding.
- If AR readback still fails, preserve `keyflow-arvideo-failure-*.avi` with the CI verification-log artifact and include payload inventory in the failure. This distinguishes truncated/missing content from decoder delivery before another targeted fix.

Local static/Python/diff checks pass. [Run `38636257464`](https://github.com/lxmtuu/PianoPath/actions/runs/38636257464), source `21e8f25`, passed static/portable/Windows build/isolated render; its full suite passed the rapid AVI and AR recording checks, reporting **12 samples / 12 nonempty / 12 distinct payloads**. It then failed later at `VerifySettingsDock`: the JSON-only `HeldParticleArt` flag had not been explicitly classified. Follow-up adds exactly that prototype exception (default-off/no-row assertions retained; remove at P6) and replaces blocking task waits in the new tests with async awaits. The complete successful follow-up is recorded below. The fix addresses the proven offline/live policy mismatch. The original failed AVI was not retained, so its precise missing-frame history is not independently established; this does not accept the held-particle artwork.

## Successful full rerun

- [Run `38637127055`](https://github.com/lxmtuu/PianoPath/actions/runs/38637127055), source **`87ca500`**, finished **SUCCESS**; preview auto-commit **`b17a691`**.
- Static/Python checks, portable tests, Windows build, isolated render/capture + APNG assembly, full verification, frame-budget measurement, installer build, galleries/previews and distributable publication steps all passed.
- Full verification: **6,047 assertions / 1 skipped check group**, exit code 0. Both offline AVI payload and retained minimum-six decoder/content checks passed, as did the explicit JSON-only settings classification.
- The new tests' blocking task waits were replaced with async waits; their previous xUnit1031 annotations are absent in this run.
- The benchmark is on **Microsoft Basic Render Driver (software/WARP)**, not a hardware-performance certificate. Shader compilation reported 1,536.64 ms; no new hardware claim.
- Held-art evidence is now available in `docs/previews/held-particle-art`: 8 active images, 8 lossless APNG crops; static/motion manifests are in `docs/evidence/held-particle-art`. Full source capture set remains in the `phase1-visual-review` artifact. Human art/motion review is still open.


### Evidence storage follow-up

After fetching `b17a691`, a fresh local source check exposed the image-only preview rule: the two generated JSON manifests must not live in `docs/previews`. They are moved to `docs/evidence/held-particle-art`, and the workflow stages that evidence directory explicitly. The C#/shader code is unchanged from successful run `38637127055`; **[Run `38640821342`](https://github.com/lxmtuu/PianoPath/actions/runs/38640821342), source `29aefb7`, completed SUCCESS** with the storage correction: static/portable/Windows build/full verification (**6,047 assertions / 1 skipped group**), captures/APNG assembly, WARP benchmark, installer, galleries and distributable publication all passed. This prevents the preview auto-commit itself from breaking the next static check.

Independent local artifact integrity check: decoded all **480 APNG frames** (8 × 60) and all **8 committed active images** using Pillow, converted to raw BGRA and compared SHA-256 against the capture manifests; every hash matched. This proves lossless frame/image preservation, not visual artwork approval. After the final CI run, a fresh checkout/source check also passes with JSON evidence outside the image-only directory.
