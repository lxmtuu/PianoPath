# Offline video — preserve images, not only timeline slots

2026-10-15. Fix implemented; Windows build/runtime rerun pending.

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

Local static/Python/diff checks pass. Windows/.NET tests run in CI; update source/run/verdict after the rerun. The fix addresses the proven offline/live policy mismatch; it does **not yet prove** the root cause of the previous binary's 5-frame result, or accept the held-particle artwork.
