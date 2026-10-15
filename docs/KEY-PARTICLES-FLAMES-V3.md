# Flames v3 — material candidate, Stage + AR

## Trạng thái

**Đang triển khai; chưa có Windows/native capture cho source này, chưa đánh giá artwork.** Tiếp nối hướng Wisps được người dùng chấp thuận ở [Wisps v3](KEY-PARTICLES-WISPS-V3.md); không mở lại Wisps làm điều kiện chặn Flames. Candidate vẫn review-only/default-off, không binding settings/preset/menu.

## Phạm vi

- Tag Flames 4 → 6; shared Stage/AR material có mantle màu, inner body, một tongue phụ thuôn. Giữ quad, base anchoring, CPU alpha/lifetime/progress/phase và RGB payload.
- Không đổi emitter, birth recipe, RNG, heat/pedal behaviour, số lượng, cap/packing, projection hoặc tint/spill. Normal fixture intensity 70 có 83 samples trong baseline; không nâng mật độ để che khác biệt material.
- Không đổi đường v2 hoặc Wisps v3 đã được chấp thuận. CI phải so lại toàn bộ 192 retained-v2 và 64 accepted-Wisps pixel hashes, quartet hashes/metrics, cùng 240 accepted-Wisps motion hashes trước khi publish.

## Kế hoạch kiểm chứng — chưa phải kết quả capture

- Hai palette × Stage/ba AR poses × tám particle/tint/spill states: 64 fulls và 16 native quartets. Pair cùng seed/input/camera; raw/eligible/packed hashes và actual quad geometry phải bằng v2, off pixels không đổi.
- Prism chord 60/61/64/67, Stage/reference AR, 4 × 60 motion frames ở 30 FPS; held 0…11, physical release trước step 12/pedal on, first released sample 13/30 s, cuối clip phải hết tail/hội tụ.
- Full PNGs, bốn APNGs và self-contained static viewer giữ trong CI artifact. Git chỉ thêm tám active quartets, hai fulls, hai lossless paired APNGs, hai manifests. Mỗi paired APNG đặt v2/v3 trong cùng frame; kiểm riêng BGRA hash từng nửa với native manifest. Không nhận autoplay độc lập là frame-synchronized comparison.
- Python contract tests có dữ liệu giả để kiểm validator/packaging; không phải ảnh GPU hay chứng nhận chất lượng. Local không có .NET/Windows GPU; phải chờ Windows CI và đọc ảnh mới kết luận.

Settings menu vẫn là bước cuối, sau các press/held families còn lại. Chưa promote Flames hay Wisps thành default, chưa hardware/real-camera/all-condition sign-off.
