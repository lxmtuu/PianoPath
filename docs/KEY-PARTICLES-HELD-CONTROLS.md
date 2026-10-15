# Held particles — lighting, palette, chord and pose controls

## Status

Implemented diagnostic follow-up to the [v2 sampled review](KEY-PARTICLES-HELD-ART-REVIEW.md), **not a material v3**. Initial control run [38657264149](https://github.com/lxmtuu/PianoPath/actions/runs/38657264149), source `a387776`, completed SUCCESS: Windows/portable/static, 288 captures + independent pixel/crop/source validation, full verification **6,056 assertions / 1 skipped group**, benchmark, installer and publication. Preview `d751ad4` contains 72 native quartet PNGs. All earlier preview files were byte-unchanged.

Inspection found cached AR counters in the Stage records (image data was valid). Source `edce79d` records inactive AR counts as zero and rejects mode-counter leakage in the review validator; it also stages 16 actual full-scene samples. Final rerun [38659928167](https://github.com/lxmtuu/PianoPath/actions/runs/38659928167), source **`edce79d`**, completed **SUCCESS**, again **6,056 assertions / 1 skipped group**: static, portable .NET, Windows build, capture/pixel/source validation, full verification, WARP benchmark, installer, galleries and publication. Final evidence/16 full samples landed as **`0a3214e`**. All 288 frame pixel hashes, all 72 quartet hashes and every comparison metric match the initial run; only inactive Stage-record AR counters/source metadata changed. All 72 quartet PNG files remain byte-identical to `d751ad4`.

Independent local check: **72/72 quartet raw BGRA hashes, 16/16 full-sample raw BGRA hashes and 18/18 canonical source hashes match**; every Stage AR counter is zero. All 288 originals were independently checked in CI; only the 16 published originals were available locally. Earlier v2 assets and all other preview files stayed byte-unchanged. Local Python tests: **25 PASS**, including the new inactive-mode regression guard; static source/diff checks PASS.

Wisps/Flames stay opt-in/default off. No user-facing control or menu redesign. All other press/held families and the broader roadmap remain open; menu work is still last.

## Fixed factorial plan

| Axis | Values |
|---|---|
| Held family | Wisps, Flames; existing v2 shader and emitter recipes |
| Note colour | Existing Prism; fixed warm amber `#FF9A4D`; fixed cool indigo `#666DFF` |
| Physical-held input | White C4 (60); black C♯4 (61); simultaneous 60/61/64/67 chord |
| View | Live Stage; reference AR; oblique AR; near-overhead AR |
| Independent factors | Neither; key light only; particles only; both |

**72 scenes × 4 variants = 288 full 1280×720 frames.** All are active/held at 36 steps of 1/60 s (0.6 s), seed `20261014`, velocity 0.9, pedal off, beat envelope zero, no imported song or release. Each factor combination replays the same input prefix with a fresh feed/simulation. AR is Ultra, FOV 70°, delay zero, Fit, unmirrored. No temporal playback comparison is implied by static controls.

Fixed colours are deliberately not luminance-matched: they probe contrast under the same material, not a fair brightness ranking. Existing Prism settings stay intact; fixed palettes only change the Note colour route. Both key and held-particle colours still use the normal production colour resolver.

## What “key-light off” actually means

- **AR:** `GpuArInput.KeyLight=0` disables both calibrated key tint and projected neighbouring-key spill; on is 0.6. Other geometry and simulated particle colours/state are untouched.
- **Stage:** neither `KeyLighting=0` (spill only) nor `ShaderKeyLight=0` (changes static ivory shading) isolates the whole pressed-key contribution. A default-off, verification-only renderer seam zeroes packed real-key `Emit.W` and `Base.Y`, after building keys and without modifying the simulation. This removes pressed-key tint/emission/spill while preserving base lighting, depression, geometry, note colour and particles. Felt is excluded. The seam has no settings/preset/menu binding.

This tests the **grouped key-light contribution**, not tint versus spill separately. Bloom/composition remain production behaviour; factor deltas need not be additive.

## Pose and synthetic background contract

Reference AR uses the existing synthetic camera image unchanged. Two new key planes are projected from the physical Standard88 width/depth ratio, FOV 70°, depth 7.2 key-depth units: 55° tilt / −6° roll (oblique), 7° tilt / 0° roll (near-overhead). Their projected corners drive both calibration and a nearest-neighbour planar warp of the reference camera image; out-of-source pixels are dark grey.

These are **calibration-matched planar synthetic fixtures**, not newly lit 3D keyboard photography or real-camera evidence. The camera plane and particle projection must agree, but the fixture cannot certify realistic key lighting, occlusion, hand overlap or exposure. Portable tests pin the physical pose normals, corner mapping, deterministic warp and unchanged reference pixels.

## Guards, evidence and review

- WARP required; original active keyboard layout restored in `finally`.
- Correct on/off particle counts; zero Stage-space geometry in AR; AR key tint/spill absent when off.
- Key lighting cannot change particle counts; particle factor cannot change key/spill counts. Family particle-off frames must match exactly.
- Windows payload checks preserve key shape/press/shadow/RGB/felt under the Stage light-off mask.
- All full PNGs and quartet PNGs round-trip to the original BGRA bytes before hashes are written.
- Manifest records exact input/seed/time, calibration corners, warped-camera hashes, raw BGRA SHA256 and 18 source-file hashes (text normalized to UTF-8/LF; PNG bytes unchanged), plus source revision.
- CI independently decodes **all 288 full frames and 72 quartet sheets** with Pillow, checks BGRA hashes and verifies that every sheet quadrant is its exact native crop. Source hashes and run revision must match before publication.
- Four explicit comparisons per scene: particles without light; particles with light; light without particles; light with particles. Display-RGB delta/footprint numbers are diagnostic measurements, not perceptual quality, linear decomposition or automatic art approval.

Full frames, manifest and standalone HTML viewer stay in the `phase1-visual-review` CI artifact under `held-particle-controls/`. Git receives 72 native crop quartets in `docs/previews/held-particle-controls/`, 16 selected combined full frames in `docs/previews/held-particle-controls-scenes/`, and JSON evidence in `docs/evidence/held-particle-controls/manifest.json`. The image-only preview rule remains intact; no HTML/base64 payloads or complete 288-frame dataset in Git.

Each quartet is 768×720: **top-left neither / top-right light only / bottom-left particles only / bottom-right combined**. Each pane is a 384×360 native crop. Full scenes must still be inspected before conclusions about total visibility; the crops are not a substitute for full-scene, temporal or real-camera review.

### Reproduce / inspect

On Windows, the existing `--verify --verify-prism-only --verify-review-out=<folder>` route renders these controls alongside the existing reviews. Then:

```sh
python -m pip install Pillow==11.3.0
python tools/review_held_particle_controls.py <folder>/held-particle-controls --source-root .
```

For a published subset, put the committed quartet PNGs in `<review>/sheets/`, the committed evidence at `<review>/manifest.json`, and use `--sheets-only`. That mode verifies all sheet hashes and the manifest plan, **not** unavailable full-frame hashes/crop reconstruction; the console explicitly says so. The viewer is self-contained, selection-generation guarded, decode-gated and reports image errors instead of showing a stale labelled capture.

Next gate: a targeted contrast/budget follow-up, retaining the distinctions below before any lighting/material decision. No alpha reduction or default enablement is justified by the capture harness alone.


## Nhận xét từ ảnh đối chứng thực tế

Đã mở sáu overview 50% (toàn bộ 72 quartet), rồi đọc native chín quartet sau: `wisps-prism-chord-ar-reference`, `wisps-indigo-chord-ar-oblique`, `flames-amber-chord-ar-near-overhead`, `wisps-prism-white-ar-reference`, `wisps-prism-black-ar-reference`, `wisps-indigo-chord-live-stage`, `flames-prism-white-ar-reference`, `flames-indigo-chord-ar-oblique`, `wisps-prism-chord-ar-near-overhead`. Đây là kiểm static samples ở 0,6 s, không phải xem native toàn bộ 288 ảnh hoặc playback.

- **Vùng sáng kéo dài dọc phím AR và quầng ngang ở chân phím có ngay trong light-only.** Bật hạt không phải điều kiện tạo ra các mảng này. Có cơ sở tách vấn đề key tint/spill khỏi material của hạt; chưa tách tint và spill thành hai tham số đối chứng riêng, chưa quy mọi pixel sáng cho lighting.
- **Wisps không tự hết vấn đề contrast khi tắt key light.** Với Prism và phím trắng, root vẫn ngả gần trắng/vàng và hòa vào ivory; gần overhead trông như cụm nét sáng rất ngắn. Indigo phân biệt màu rõ hơn trong các crop, nhưng trên Stage nền đen các sợi vẫn nhỏ/mảnh. Vì vậy giảm alpha toàn cục sẽ có nguy cơ làm Stage khó đọc hơn mà chưa giải quyết chân hạt trên phím sáng.
- **Flames nhận ra được ở particles-only**, nhất là amber và indigo/violet; hình nhỏ, thuôn. Trong combined, dải màu phím vẫn chiếm nhiều diện tích hơn thân lửa. Đây chưa phải bằng chứng cần tăng sáng hoặc density.
- **Chord là một phép kiểm budget nữa, không chỉ kiểm material.** Ở fixture Prism, Wisps có **304 Stage sprites nhưng 192 projected AR particles** trong cả ba pose và cả hai trạng thái light; Flames **83/83**. Code hiện có `MaxProjectedParticles=192` trong `GpuArLayer.Effects.cs`, với ưu tiên burst/release rồi continuous. Không dùng số lượng này để tuyên bố Stage/AR density parity; vòng này chưa thay cap, thứ tự ưu tiên hay emitter.
- **Không thấy mất hoàn toàn billboard ở các pose được đọc**, nhưng kích thước/phối cảnh và độ tách khỏi nền thay đổi rõ. Jaggies ở mép phím của background mới đã hiện trong neither, là giới hạn nearest-neighbour fixture, không nên sửa shader hạt để bù chúng.

### Guard định lượng, không chấm điểm thị giác

Ví dụ chord/Prism, số pixel full-frame có chênh RGB >4 do **particle factor**:

| Mode | Wisps light-off / light-on | Flames light-off / light-on |
|---|---:|---:|
| Stage | 2,610 / 2,610 | 2,529 / 2,529 |
| AR reference | 934 / 794 | 1,752 / 1,701 |
| AR oblique | 1,063 / 841 | 1,903 / 1,772 |
| AR near-overhead | 607 / 508 | 1,211 / 1,174 |

Đây là footprint của phép so cặp, không phải brightness hoặc tỷ lệ đóng góp năng lượng. Tất cả 144 particle comparisons trong manifest đầu có signal >4; không suy ra đủ rõ ở cảnh thật hoặc đạt chuẩn artwork.

**Hướng tiếp theo:** giữ shader v2/default-off. Trước material v3, kiểm full scene và làm một lát nhỏ về key tint/spill contrast cùng budget/độ phủ theo pitch của chord (có control mật độ dưới cap); không tự tăng cap hay giảm alpha. Sau đó mới tiếp tục art/motion của các family còn lại, hardware/real-camera/tier/delay/output gates; settings menu vẫn cuối cùng.


### Full-scene follow-up sau final CI

Đã mở hai overview 40% chứa đủ 16 full-scene representatives, rồi xem native 1280×720 bốn ảnh: Wisps/Prism/chord/AR reference, Wisps/Indigo/chord/Stage, Flames/Prism/chord/AR near-overhead, Wisps/Prism/chord/AR oblique. Trong fixture cô lập này, Wisps Indigo còn đọc được thành các sợi trên Stage nền đen; ở AR Prism, key tint/spill nổi rõ hơn các nét Wisps rất nhỏ gần mép phím, đặc biệt khi nghiêng. Flames gần overhead vẫn có thân nhỏ phía trên phím, không biến thành một mảng full-screen. Không suy ra độ rõ với bài hát dày, camera thật, moving exposure hay motion từ các ảnh này.

Bộ đếm cached đã được sửa ở **capture metadata**, không đổi renderer policy, shader, emitter hoặc AR cap. Node VM fake-DOM smoke kiểm 72 lựa chọn, decode lỗi và decode cũ trả về muộn (không phải browser/GPU visual test); source/static checks và 25 Python tests PASS. Review viewer có thể mở riêng full-frame combined ở 16 scene đã công bố; artifact vẫn giữ đủ 288 ảnh của bốn variants.
