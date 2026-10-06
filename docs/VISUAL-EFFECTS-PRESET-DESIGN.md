# PianoPath — art direction cho preset đã ship

> **Ngày:** 2026-10-06  
> **Phạm vi:** 14 preset built-in và 3 preset community trong `presets/`; không sửa preset cá nhân.  
> **Tình trạng:** recipe, mô tả và default wiring đã được cập nhật trong source. Chưa coi look là đạt cho tới khi Windows build tạo gallery từ renderer thật, kiểm tra GPU/WPF và qua các gate ở cuối tài liệu.

## 1. Quyết định thiết kế

Dùng ba vai trò đã chọn: **Concert** (sân khấu biểu diễn), **Neon** (creator/live) và **Learning** (đọc nốt/luyện tập). Tên preset cũ được giữ để không làm hỏng tên đã lưu; tên không đồng nghĩa mọi preset phải dùng chung một theme hay một bộ hiệu ứng.

- **Một tâm điểm:** note body, pitch, onset và hit-line luôn rõ hơn ambience. Mỗi recipe chỉ chọn có chủ đích material, trail, impact, hold/release và nền.
- **Giảm chồng hiệu ứng:** preset mặc định không bật full-screen flash; particle, bloom và ambient được hạ; motion nền tắt hoặc chậm. Flash còn lại là hiệu ứng cục bộ tại phím và cường độ thấp, không phải strobe toàn khung.
- **Màu ổn định:** hand/track/pitch giữ nguyên trong cảnh; hiệu ứng không tự đổi palette của note nguồn.
- **Learning ưu tiên chức năng:** nốt đặc, chia màu tay trái/phải, nhãn C/quãng tám hữu ích; không có hạt va chạm hay lớp nền chuyển động.
- **Utility không trang trí:** `Classic Roll` dành cho thời lượng xem dài; `Green Screen` giữ nền chroma thuần, không bloom/ánh màu tràn.

## 2. Danh mục built-in

Thứ tự picker bắt đầu bằng default rồi tới hai hướng creator/learning, sau đó các look tiện ích và chủ đề. Các lớp bên dưới là cấu hình recipe, chưa phải xác nhận mọi backend render giống nhau.

| Preset (tên ổn định) | Vai trò | Vật liệu / màu chính | Lớp được chủ động giữ |
|---|---|---|---|
| **Concert Gold** *(default mới)* | Concert | Nốt đặc xanh thủy tinh / đồng thau, bàn phím satin | Glow trail ngắn, bụi/ring nhỏ, hold và pedal glow nhẹ, một lớp spotlight mềm |
| **Neon Violet** | Neon | Nốt neon tím → cyan, edge sắc | Nebula chậm, shimmer/trail ngắn, burst/va chạm cục bộ cường độ thấp |
| **Two Hands** | Learning | Nốt đặc xanh lam / hồng phấn, split tại C4 | Nhãn C, cue nốt sắp chạm nhẹ; ambience và particle tắt |
| **Classic Roll** | Utility / performance | Solid theo màu track, cạnh vuông mềm | Không trail, burst, ring, flash, bloom hay motion nền |
| **Green Screen** | Utility / production | Nền `#00FF00`, nốt hai tay đục và tương phản | Tắt halo, bloom, key-light, particle và mọi ambience |
| **Inferno** | Concert / thematic | Nốt ember đỏ-hổ-phách | Haze/lửa thưa, hit ring và flash cục bộ thấp; không phủ toàn màn |
| **Ice Crystal** | Concert / thematic | Glass cyan-trắng, viền gọn | Prism/tuyết thưa, sparkle/ripple, refraction có giới hạn |
| **Sakura Nocturne** | Concert / thematic | Glass hồng phấn / ivory trên nền chàm | Cánh hoa thưa, ribbon ngắn và hit accent nhẹ |
| **Moonlight Sonata** | Concert | Glass xanh bạc, độ bão hòa thấp | Starfield thưa, aurora rất chậm, glow mềm |
| **Aurora Rainbow** | Neon / visualizer | Cầu vồng ổn định theo cao độ | Aurora nhẹ, hit-line Spectrum tiết chế; không cycling màu nhanh |
| **Ocean Depths** | Concert / thematic | Glass xanh ngọc | Ocean Flow chậm, mưa thưa và ripple bám vào phím |
| **Galaxy Voyage** | Neon / thematic | Tím → cyan | Vệt thiên thạch ngắn, tinh vân/va chạm thưa, plasma hit cục bộ |
| **Electric Storm** | Neon / thematic | Cyan-trắng | Speed line ngắn, sét ở phím, hồ quang khi giữ; cường độ chớp bị giới hạn |
| **Retro Arcade** | Neon / playful | Note vuông, bảng màu track giới hạn | Retro Grid tĩnh/chậm, bounce/confetti gọn, bloom thấp |

## 3. Community shelf

`tools/make_presets.py` là nguồn sự thật. Generator tạo settings đầy đủ trên một visual baseline tĩnh, rồi recipe chỉ bật các lớp của nó; model default tương lai không được âm thầm thêm flash/particle vào cả shelf.

| Preset | Vai trò | Recipe |
|---|---|---|
| **Ember Rain** | Concert / elemental | Note Fire, haze ember và mưa thưa; splash/ripple ở phím, flash tắt |
| **Lo-Fi Study** | Learning / long session | Nốt đục muted, glow và particle gần bằng 0; aurora chậm; ambient và flash tắt |
| **Sunset Drive** | Neon / synthwave | Coral–amber neon, speed-line ngắn, Retro Grid, Fireworks/ring nhỏ; flash tắt |

Gallery CI dùng `--preset` cho cả built-in lẫn community và render mỗi recipe qua WPF/software cùng GPU/WARP. Ảnh của hai đường render là bằng chứng kiểm tra, không thay thế đo phần cứng.

## 4. Default, compatibility và dữ liệu người dùng

- `Concert Gold` là preset đầu picker, `DefaultPresetName`, look cho thư mục settings mới và target của thao tác **Reset**.
- Store chỉ tạo `Concert Gold` khi chưa có tệp hoặc tệp hiện tại không đọc được. Tệp hợp lệ hiện có tiếp tục đi qua migration/clamp bình thường; không tự áp look mới, không đổi palette hay preset name của người dùng.
- Built-in names được giữ nguyên; các thuộc tính preset vẫn là những trường đã có trong `PianoVisualSettings`. Không thêm field/schema mới và không thay format JSON.
- Community files được cập nhật từ generator; local user presets, thumbnails, preset import và file người dùng tự tạo không bị rewrite.
- Chọn lại một preset built-in/community là thao tác chủ động áp recipe mới; mọi cài đặt cá nhân đang lưu vẫn không bị chạm cho tới khi người dùng chọn/reset/save.

## 5. Cổng kiểm chứng bắt buộc

1. `python3 tools/check_sources.py` pass: locale, gallery đủ 17 look, generator và preset JSON đồng bộ.
2. Windows build pass; `--verify` xác nhận tên, clamp, store first-run/reset, saved-look preservation và từng profile community.
3. CI render đủ 17 look trên software/WPF và GPU/WARP. Review từng ảnh ở 100% và 50%, tối thiểu scene 720p/1080p, hợp âm dày, note giữ/pedal và đoạn nghỉ.
4. Chroma green kiểm tra edge/bloom/halo không spill; Learning kiểm tra nhãn và tương phản; Neon/Concert kiểm tra nhiều hit nhanh không hòa thành một quầng.
5. Chạy frame-budget gate; không suy diễn hiệu năng GPU thật từ WARP. Hardware gallery/bench còn thiếu phải ghi “chưa xác nhận”.

Chưa qua Windows render, xem ảnh, test xUnit/`--verify` và đo thiết bị thật thì chỉ xác nhận **recipe/code đã cấu hình**, không tuyên bố “preset đã hoàn thiện thị giác”.
