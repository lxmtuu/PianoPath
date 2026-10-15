# Embers v1 — material candidate, Live Stage + AR

## Trạng thái

Đang triển khai và chờ native CI; **chưa tuyên bố render thành công, visual sign-off hoặc promote**. Tiếp tục sau Wisps/Flames đã duyệt và sửa hướng AR. Không mở lại artwork đã duyệt; settings menu vẫn để cuối.

## Hướng thiết kế và ranh giới

- Tia có thân thuôn theo vận tốc, đầu đầy hơn đuôi; chấm ember là hạt nhỏ có mặt cắt góc, không phải đốm tròn có lõi trắng lớn.
- Chất liệu chung `ShadeSparkArt` cho GPU Live Stage và AR: giữ sắc đầu vào, quầng gọn, pixel-box coverage cho tia dưới một pixel; không dùng thời gian toàn cục để tạo chớp giả hoặc fade lifetime lần hai.
- `SparkArtForReview` internal/default-off. Chỉ đổi discriminator dot/streak → 19/18; CPU positions, extents, axes, color/alpha, age/progress và thứ tự packet giữ nguyên. Không binding preset/settings/menu, không đổi WPF fallback hoặc recording defaults.
- Không chỉnh emitter, velocity, RNG, density, lifetime, cap 192 hay burst/release priority. Không dùng minimum screen-size hoặc sửa projector để làm candidate to hơn. AR dùng **production note-aligned geometry**, không legacy axes của các review artwork trước.
- Primitive dot/streak còn được các channel khác dùng chung; cờ nội bộ chọn material primitive, **không phải đã hoàn tất Fireworks/Bounce hay các family khác**. Lát review hiện tại chỉ chạy recipe Embers; các family khác phải có review riêng.
- CPU legacy Stage/AR còn khác gain/alpha/extent; dùng cùng shader không tự đóng parity về độ sáng/kích thước. Lát này giữ nguyên những khác biệt đó để tách material khỏi geometry/recipe.

## Bằng chứng được chuẩn bị

- **192 static full frames:** 2 palettes (Prism/Indigo) × 4 views (Stage/reference/oblique/near-overhead) × white/black (60/61) × burst/held/released/expired × off/classic/art.
- Burst: một impact, không held, sample 0,05 s. Held: không impact, sample 0,60 s. Released: nhả trước step 21 ở 60 FPS, pedal on, sample 0,60 s. Expired: cùng release timeline, sample 2,50 s. Mọi control cùng timeline/seed, không dùng off của phase khác.
- Tint/spill masked; bloom off ở cả hai version để đọc thân hạt, không phải thay default sản phẩm. Đổi góc AR theo fixture: reference 0°/0°, oblique 45°/−45°, near-overhead 90°/90°.
- So raw simulation hash và **actual complete renderer packets**, chỉ chuẩn hoá material tag về legacy: vị trí/axes/color/alpha/order/count phải giống nhau. Off/expired bằng nhau; active static phải có pixel delta. Stage-space geometry không được lọt lên camera.
- **720 native motion crops:** Stage/reference AR × burst/held-then-release × off/classic/art × 60 frames/30 FPS. Release trước step 12, pedal on. Crop chung là union mọi off-difference ở cả 60 frames, thêm padding, không resample; crop không nhất thiết cùng kích thước giữa hai mode.
- APNG delta encoding dùng SOURCE rectangles, đúng **1/30 s**, giữ cả frame lặp ở đuôi. Hai version nằm trong cùng frame; decoder phải kiểm từng half hash bằng crop gốc. Tối thiểu sáu frame có khác biệt trong mỗi track; không ép mọi hạt sắp tắt phải còn thay đổi pixel sau lượng tử hoá.
- Git chỉ publish hai native crop quartets và bốn paired APNGs cùng manifest, tổng tối đa 1,4 MiB. Full statics và original motion crops nằm trong CI artifact. Không xóa các deliverable đã duyệt để lấy chỗ.

Đây là WARP/synthetic-camera review, chưa camera thật, mọi palette/tier/delay/recording hoặc hiệu năng GPU thật. Static/motion validator tests là contract synthetic, không thay native evidence.
