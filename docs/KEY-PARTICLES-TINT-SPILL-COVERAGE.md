# Tint / spill và độ phủ hợp âm dưới AR cap

## Trạng thái

Lát chẩn đoán tiếp theo sau [held controls](KEY-PARTICLES-HELD-CONTROLS.md). **Đã triển khai harness; Windows build/capture/review mới đang chờ CI.** Không dùng thành công của run `38659928167` làm bằng chứng cho code mới.

Không đổi shader v2, material/lifetime/clock/RNG, production recipes, cap 192 hoặc thứ tự chọn AR particles. Wisps/Flames vẫn experimental/default-off; không có settings/menu binding mới. Mọi burst/held family còn lại vẫn thuộc roadmap, menu vẫn cuối cùng.

## Ma trận giới hạn

- Load: Wisps normal (`WispAmount=32`), Wisps low (`8`, chỉ fixture), Flames normal (giữ intensity 70, không thay intensity để giả lập density).
- Palette: Prism và Indigo; warm Amber vẫn có trong bộ controls trước.
- Cùng hợp âm MIDI 60/61/64/67, velocity 0,9, giữ 36 bước × 1/60 s, seed 20261014, pedal/beat off.
- Stage và AR reference/oblique/near-overhead; cùng camera fixture và calibration của bộ trước. AR Ultra/Fit/70°/delay 0, không mirror.
- Ba factor độc lập: particles, key tint, neighbouring-key spill (2×2×2).

**24 cảnh × 8 variants = 192 full frames**, 48 native quartet sheets. Mỗi cảnh có hai sheets (particles off/on), mỗi sheet: trên trái neither, trên phải tint, dưới trái spill, dưới phải both. Mỗi pane 384×360 pixel native.

### Phạm vi “tint”

Ở Stage, “tint” là **pressed-key tint + emission group** (`Emit.W`), spill là `Base.Y`; không phải phép tách tint khỏi emission. Giữ nguyên static ivory illumination, RGB, độ nhấn, shadow và felt. Ở AR, tắt riêng calibrated key patches hoặc `BuildProjectedNoteLights`; KeyLight vẫn dùng 0,6 khi một trong hai bật. Đây là seams verification-only, mặc định không kích hoạt; không thêm setting cho người dùng.

Bốn trạng thái tương đương light-group của normal loads phải giữ **đúng raw pixel hash** với 288-frame controls vừa render trong cùng run. Nhánh product mặc định vẫn phải qua toàn bộ baseline, full verification và benchmark hiện có.

## Đo độ phủ thực sự, không suy từ số sprite tổng

Observers mặc định null, thông báo **sau khi** Stage sprite / AR particle đã được pack thành công, không tham gia selection và không thay simulation. Một snapshot read-only ở frame cuối lấy cả live pool:

1. **Raw:** live particle pool theo pitch.
2. **Eligible:** cùng điều kiện resolve surface và alpha >0,003 của Stage. Đây không phải danh sách nguyên nhân AR rejection; pose/near-plane/cap vẫn là các bước khác.
3. **Packed:** callback thực tế của active renderer. Tổng phải khớp StageSpriteCount hoặc ArParticleFxCount, inactive-mode counters phải zero.

Mỗi nhóm giữ rõ cả bốn pitch (kể cả zero), count, histogram tuổi tuyệt đối [0;0,2), [0,2;0,4), [0,4;+∞), min/max/mean age và mean normalized progress. Sorted hashes của (pitch,age,life) kiểm ánh sáng/view không đổi population; histogram/count không phải on-screen area, occlusion hoặc perceptual visibility.

Guard bắt buộc:

- Tint/spill không thay candidate/packed population; particles/spill không thay tint packing và particles/tint không thay spill packing.
- Live pool không đổi theo view hoặc light factors trong cùng family/load/palette/input.
- Low Wisps và Flames: eligible <192, đủ bốn pitch, packed sample hash bằng eligible hash. Không lặng lẽ chấp nhận particle/pitch bị mất ở sub-cap.
- Normal Wisps: fixture phải thực sự vượt cap; AR phải pack 192, không sửa cap cho tests xanh.
- Particle-off pixels phải giống giữa family/load tương ứng.
- Native key-mask và AR-build tests kiểm riêng bốn tổ hợp tint/spill, đồng thời kiểm geometry/state/particle preservation.

## Evidence và giới hạn

PNG round trip tại encoder; manifest ghi source revision, 19 canonical source hashes, camera/pixel hashes, factor flags, per-pitch age summaries. Python kiểm schema/factor pairing, coverage arithmetic/cross-view invariants, source hashes; decode đủ 192 full images + 48 sheets và so native crop trong CI. Schema-1 viewer tiếp tục đọc evidence cũ có 18 source hashes hoặc code mới có 19.

Đủ full frames, manifest và self-contained viewer ở artifact `phase1-visual-review/held-light-isolation/`. Git chỉ lưu manifest tại `docs/evidence/held-light-isolation/` và 48 sheets + **4 low-Wisps/Prism combined full scenes** tại `docs/previews/held-light-isolation/`. Normal full-scene equivalents đã có trong bộ trước; không commit thêm cả dataset hoặc HTML/base64 lớn.

```sh
python -m pip install Pillow==11.3.0
python tools/review_held_light_isolation.py <review>/held-light-isolation --source-root .
```

Subset published có thể dùng `--sheets-only` khi đặt sheets trong thư mục `sheets/` cùng manifest. Chế độ này không chứng nhận full frames không có tại chỗ, và ghi rõ giới hạn đó.

**Gate còn chờ:** CI mới và đọc ảnh thật (tint-only/spill-only, low vs normal ở cùng pose), phân bổ/age bias theo pitch và full-scene low density. Chưa kết luận cần thay tint/spill strength, alpha, material, emitter hay cap. Synthetic planar camera, static 0,6 s không thay được temporal, real-camera, delay/tier, recording/alpha/WPF hoặc hardware tests.
