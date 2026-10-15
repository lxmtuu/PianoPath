# Embers v1 — material candidate, Live Stage + AR

## Trạng thái

Đã có prototype native; [full CI 38684672840](https://github.com/lxmtuu/PianoPath/actions/runs/38684672840) **SUCCESS**, source `d0f95251edafe1107fb2d0d257786dfcaf255901`, evidence commit `1f00ee3`. Static/portable tests, Windows build, native validation, full regression, frame-budget check và publication đều qua. **Chưa visual sign-off hoặc promote; AR reference còn tối/mảnh hơn mong muốn.** Tiếp tục sau Wisps/Flames đã duyệt và sửa hướng AR. Không mở lại artwork đã duyệt; settings menu vẫn để cuối.

## Hướng thiết kế và ranh giới

- Tia có thân thuôn theo vận tốc, đầu đầy hơn đuôi; chấm ember là hạt nhỏ có mặt cắt góc, không phải đốm tròn có lõi trắng lớn.
- Chất liệu chung `ShadeSparkArt` cho GPU Live Stage và AR: giữ sắc đầu vào, quầng gọn, pixel-box coverage cho tia dưới một pixel; không dùng thời gian toàn cục để tạo chớp giả hoặc fade lifetime lần hai.
- `SparkArtForReview` internal/default-off. Chỉ đổi discriminator dot/streak → 19/18; CPU positions, extents, axes, color/alpha, age/progress và thứ tự packet giữ nguyên. Không binding preset/settings/menu, không đổi WPF fallback hoặc recording defaults.
- Không chỉnh emitter, velocity, RNG, density, lifetime, cap 192 hay burst/release priority. Không dùng minimum screen-size hoặc sửa projector để làm candidate to hơn. AR dùng **production note-aligned geometry**, không legacy axes của các review artwork trước.
- Primitive dot/streak còn được các channel khác dùng chung; cờ nội bộ chọn material primitive, **không phải đã hoàn tất Fireworks/Bounce hay các family khác**. Lát review hiện tại chỉ chạy recipe Embers; các family khác phải có review riêng.
- CPU legacy Stage/AR còn khác gain/alpha/extent; dùng cùng shader không tự đóng parity về độ sáng/kích thước. Lát này giữ nguyên những khác biệt đó để tách material khỏi geometry/recipe.

## Bằng chứng đã kiểm trong CI

- **192 static full frames:** 2 palettes (Prism/Indigo) × 4 views (Stage/reference/oblique/near-overhead) × white/black (60/61) × burst/held/released/expired × off/classic/art.
- Burst: một impact, không held, sample 0,05 s. Held: không impact, sample 0,60 s. Released: nhả trước step 21 ở 60 FPS, pedal on, sample 0,60 s. Expired: cùng release timeline, sample 2,50 s. Mọi control cùng timeline/seed, không dùng off của phase khác.
- Tint/spill masked; bloom off ở cả hai version để đọc thân hạt, không phải thay default sản phẩm. Đổi góc AR theo fixture: reference 0°/0°, oblique 45°/−45°, near-overhead 90°/90°.
- So raw simulation hash và **actual complete renderer packets**, chỉ chuẩn hoá material tag về legacy: vị trí/axes/color/alpha/order/count phải giống nhau. Off/expired bằng nhau; active static phải có pixel delta. Stage-space geometry không được lọt lên camera.
- **720 native motion crops:** Stage/reference AR × burst/held-then-release × off/classic/art × 60 frames/30 FPS. Release trước step 12, pedal on. Crop chung là union mọi off-difference ở cả 60 frames, thêm padding, không resample; crop không nhất thiết cùng kích thước giữa hai mode.
- APNG delta encoding dùng SOURCE rectangles, đúng **1/30 s**, giữ cả frame lặp ở đuôi. Hai version nằm trong cùng frame; decoder phải kiểm từng half hash bằng crop gốc. Tối thiểu sáu frame có khác biệt trong mỗi track; không ép mọi hạt sắp tắt phải còn thay đổi pixel sau lượng tử hoá.
- Git chỉ publish hai native crop quartets và bốn paired APNGs cùng manifest, tổng tối đa 1,4 MiB. Full statics và original motion crops nằm trong CI artifact. Không xóa các deliverable đã duyệt để lấy chỗ.

Đây là WARP/synthetic-camera review, chưa camera thật, mọi palette/tier/delay/recording hoặc hiệu năng GPU thật. Static/motion validator tests là contract synthetic, không thay native evidence.

## Published subset / kiểm độc lập

- [Burst crop quartet](previews/embers-art-v1/burst.png), [held crop quartet](previews/embers-art-v1/held.png): Stage trên / AR dưới, classic trái / v1 phải. Đây là crop nguyên pixel, không full scene, không resize/retouch.
- Bốn paired APNGs: [Stage burst](previews/embers-art-v1/live-stage-burst.png), [Stage held/release](previews/embers-art-v1/live-stage-held.png), [AR burst](previews/embers-art-v1/ar-reference-burst.png), [AR held/release](previews/embers-art-v1/ar-reference-held.png).
- [Manifest](evidence/embers-art-v1/manifest.json): 192 static records + 720 motion records, 18 source hashes. Local kiểm tám published crops và 480 native motion halves; **không tải/re-verify đủ 192 original fulls hoặc 240 original off-motion crops**. Native CI kiểm originals.
- So với pre-pass `5855e45`, cả Wisps và Flames giữ nguyên **64 full-frame / 16 quartet / 240 motion hashes mỗi family**, cùng coverage/geometry metrics. AR alignment 96-frame matrix vẫn PASS.
- 53 Python tests với Pillow và source checker PASS; tests fake-image/APNG là synthetic contract, không gọi là native evidence. HTTP kiểm 5 static/index routes + 240 paired-frame routes (480 native half hashes), 4 invalid routes trả 404; mock-DOM kiểm 240 mode/frame states, native aspect, stale/error guards. Không suy ra đã nghiệm thu browser playback.
- Native runtime source giữ nguyên sau run `d0f9525`; follow-up chỉ thêm viewer/documentation và siết validator integer-count/crop/expired-off checks, local 53 tests PASS.

## Quan sát — chưa chốt chất lượng

Đã xem hai static native quartets ở nearest-neighbour, cùng bốn paired temporal samples: Stage/AR held f11 và f18. Không nhận đây là đã xem liên tục mọi frame trong browser.

- Stage: bớt lõi kim trắng, các tia giữ màu hơn; hạt sinh ra màu trắng vẫn có thể trắng/xám vì không đổi birth colour. Burst sớm 0,05 s còn dồn thành cụm, do cùng emission/trajectory; chưa sửa recipe để giấu hiện tượng đó.
- AR reference: giảm quầng trắng nhưng **thân xanh hiện quá nhẹ/tối trên nền camera ở một số samples**. Pixel delta và số hạt đúng không chứng minh độ đọc đã đạt. Không dùng zoom viewer để nhận là đã tăng physical size.
- Phần đuôi chuyển động vẫn theo cùng quỹ đạo và cùng physical release/pedal; material làm thay cảm giác độ sáng/visible fade. Track burst có 20, held có 30 frames khác pixel giữa versions ở mỗi mode; không ép tail đã hết hạt còn phải khác nhau.
- V1 là mốc đối chứng cho lượt cân contrast/material energy tiếp theo, **chưa đóng Embers và chưa chuyển trạng thái Fireworks/Splash/Confetti/Dust thành hoàn tất**. Bước tiếp theo cần cân CPU gain/alpha và pixel coverage giữa Stage/AR, vẫn tách khỏi density/lifetime/RNG/cap/packing. Không mở lại Wisps/Flames.

## Mở viewer

```sh
python -m pip install Pillow==11.3.0
python tools/serve_spark_particle_art.py --output /path/to/embers-review --port 8770
# --verify-only: chỉ kiểm subset, không mở server
```

Viewer phục vụ trên `0.0.0.0`; player tạm dừng/bước từng frame/nhảy tới release 12. Classic và candidate luôn trong cùng PNG, không hai autoplay lệch nhau. Source PNG được ghép lossless delta rectangles, 60 frames kể cả identical tail, đúng 1/30 s; browser tốc độ thực không được bảo đảm. Bản này chưa camera/MIDI thật, tint/spill/bloom-on art acceptance, mọi tier/delay/recording hoặc hardware performance.
