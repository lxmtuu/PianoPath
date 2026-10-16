# Embers v2 — shared material energy / AR contrast

## Status

Native candidate verified in full Windows CI; **review-only/default-off, not visually accepted or promoted**. Follows [v1](KEY-PARTICLES-EMBERS-V1.md), which reduced white cores but made AR too faint. Approved Wisps/Flames and production note-aligned AR geometry remain unchanged. Settings menu stays last.

## Changes and boundaries

- V1 retained different Stage/AR CPU gains and lifetime encoding: Stage attenuated RGB while AR attenuated alpha. V1 then compressed RGB again in the shader. V2 resolves **one intrinsic linear tint and one lifetime alpha** in `SparkParticleAppearance.TryColor` for both outputs. Alpha = `(1-age/life) * clamp(glow*ParticleGlow,0,1)`; there is no second age fade in the shader.
- Hue retained, dominant tint capped at 0.95; near-black inputs are not promoted to bright colours. No white-core substitution or HDR amplification. Layer opacity still belongs to AR composition.
- Broader coverage **inside the existing dot/streak quad**, using integrated subpixel coverage; no minimum screen-size, extent/velocity/centre/projector change. V1 material/recipe remains available and unchanged.
- Internal default-off `SparkArtV2ForReview`; tags 20/21. No presets, public setting, menu, WPF fallback or recording-default change. Applies only to dot/streak primitives; other families are not marked complete.
- Same emitter, seed, density, lifetime, RNG, packing order/cap. Legacy AR eligibility is evaluated before substituting v2 material, so material energy cannot admit extra particles.

## Verification contract

Same 192-static / 720-motion matrix as v1, but **left = v1, right = v2**. Off is matched in time. Static palettes Prism/Indigo, pitches 60/61, Stage + three AR poses, burst/held/released/expired. Four synchronized 60-frame motion tracks cover Stage/reference AR × burst/held-release, with pedal at step 12.

V1's whole-packet equality cannot apply to deliberately changed colour/alpha. V2 instead checks raw state, actual packet geometry/order/nonparticle payload and selected count unchanged, then separately hashes actual packed particle colours. V2 actual colour/alpha hashes must match Stage and all three AR views at each static palette/pitch/phase. Portable tests check finite/bounded hue and exactly one lifetime envelope, black/zero-glow and invalid inputs.

Native signal metrics (RGB absolute off-difference and >4-level footprint) are recorded per static sample; compare only versions within the same scene. They are not cross-mode quality scores or a substitute for inspection on real cameras.

Full original statics and motion crops stay in the CI artifact. Commit only two native ROI quartets, four paired APNGs and gzip-compressed metadata (lossless JSON, not lossy pixels), at most 0.7 MiB. Opaque APNG frames may encode RGB rather than RGBA; decoded native BGRA hashes must remain identical, including repeated tail frames at exact 1/30 s. V1 and all approved deliverables remain present; no deletion to make room.

Full CI and published-pixel checks now pass; native static/frame inspection is recorded below. Real-camera/hardware checks, browser playback cadence and user art acceptance remain open.

## First native run / comparison-order correction

Run `38689015067` for source `c9d0a09` passed static checks, portable tests and Windows compilation; native v2 static generation passed within-output raw/geometry/population guards, then failed the cross-output colour hash at `prism-ar-reference-p60-burst`. The initial verifier incorrectly compared ordered colour lists: Stage uses ascending simulation slots, AR intentionally packs newest-first / origin priority. It is **not overall CI success**.

The replacement joins each actual packed colour/alpha to its actual raw particle identity, then canonicalises those identity–colour pairs only inside diagnostics. The full ordered packet geometry test between v1/v2 remains strict; production ordering/cap/selection is not touched. This preserves per-particle identity rather than merely comparing an unlabelled colour histogram. The corrected comparison passed in the fresh full native run below.

## Verified candidate and published evidence

- [CI 38689803522](https://github.com/lxmtuu/PianoPath/actions/runs/38689803522) **SUCCESS**, source `f667f6710d4f8d37086cf4b474a6e36362e121f0`: static checks, portable tests, Windows build, full native verification, v2 publisher, frame budget, installer, README/preset/material galleries and distribution. Preview publication `5a39701`.
- Native matrix: **192 static captures / 64 same-state, actual ordered-geometry/population pairs; 720 motion crops**. V2 per-particle actual colour/alpha matches Stage and all three AR poses for every static palette/pitch/phase, after identity-linked canonicalisation. This is intrinsic-material parity, **not equal composited pixels or equal apparent size/brightness across modes**.
- Locally: **54 Python tests**, source checks (310 C# / 2 XAML), `git diff --check`; published subset verifies all **18 source hashes, 8 static panes and 480 motion halves**. All 240 frame endpoints also decode to the manifest's 480 native BGRA half hashes. HTML V1/V2 labels and player JavaScript syntax checked. HTTP/hash/syntax checks are not browser playback acceptance.
- Historical digest comparison against saved `4bbec03` manifests: **3,466 v1 pixel/raw/packet digest leaves unchanged; 342 static + 240 motion digest leaves unchanged for each of accepted Wisps v3 and Flames v3**. Lossless RGB APNG encoding/JSON compaction changes some file bytes, not decoded v1 frames or metadata meaning. No accepted art reopened.
- Committed v2 subset: two native ROI quartets, four exact-1/30-second 60-frame APNG pairs, gzip manifest; approximately **0.543 MiB**, below the 0.7 MiB publication limit. Cumulative changed-file payload against session base: approximately **127.78 MiB**, below 128 MiB; full frame trees remain artifact-only.

### Same-scene native signal (Prism, pitch 60)

| View / phase | Packed v1 / v2 | RGB L1 off-delta v1 → v2 | Pixels with max ΔRGB > 4 v1 → v2 |
|---|---|---|---|
| Stage / burst 0.05 s | 35 / 35 | 28,144 → 37,135 | 69 → 74 |
| Stage / held 0.60 s | 14 / 14 | 34,108 → 42,842 | 154 → 193 |
| AR reference / burst 0.05 s | 35 / 35 | 3,468 → 5,286 | 47 → 46 |
| AR reference / held 0.60 s | 14 / 14 | 4,875 → 11,739 | 80 → 92 |

L1 is absolute RGB difference from the matched off control, including darkening; **not luminance, a quality score, or cross-mode normalisation**. AR held L1 increases about 2.41× in this sample, without more particles. Footprint need not increase monotonically (AR burst drops one thresholded pixel).

### Inspection and remaining limits

Inspected native burst/held quartets at nearest-neighbour enlargement and contact sheets of all 60 frames in each of the four published motion tracks. The v2 reference-AR held filaments are visibly stronger than v1, with more coherent coloured bodies. The early burst still forms a bright overlapping cluster; this pass does not prove the burst silhouette/fineness is final. The unchanged simulation lifetime does not imply an identical perceived fade: replacing the two encodings with one alpha envelope intentionally changes visible tail energy.

Contact-sheet review is **not realtime playback/cadence approval**. Published crops are a subset, not the original full camera frames. No claims for every palette/tier/delay, live MIDI, real camera/exposure, bloom/tint/spill integration, recording or physical GPU performance/acceptance. Stage and AR may still differ perceptually because projection, scale, compositing and backgrounds differ. User art acceptance remains open; all other families and settings-last roadmap remain pending.

Evidence: [burst quartet](previews/embers-art-v2/burst.png), [held quartet](previews/embers-art-v2/held.png), [gzip manifest](evidence/embers-art-v2/manifest.json.gz). Left = **V1**, right = **V2**; Stage above, AR reference below.

```bash
# Requires Pillow; verifies the published native subset before serving.
python3 tools/serve_spark_particle_art.py --version 2 --output /tmp/embers-v2-review --port 8771
# Server binds 0.0.0.0; motion-review.html synchronises the two versions by frame.
```
