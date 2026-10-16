# Settings P6 · Migration WPF sau khi duyệt layout

**Người dùng đã duyệt:** “tớ thấy thuận rồi”. Triển khai tiếp từ [prototype/bản đồ](SETTINGS-LAYOUT-PROPOSAL.md), không thiết kế lại renderer.

## Phần đã đưa vào source app

- 12 trang WPF thật: Overview, Notes & color, Press, Hold, Release, Lighting, Stage & background, Camera & AR, Recording, Sound & MIDI, Practice & history, Application.
- `SettingsPages` vẫn là nguồn danh mục; các tên trang cũ được resolve qua `Canonical`/`IndexOf`/`SectionOf`. CLI cũ vẫn hợp lệ; thêm `press`, `hold`, `wisps`, `flames`, `release`, `lighting`, `overview`.
- Chín page builder vẫn nguyên nội dung. `ArrangeApprovedSettingsLayout` chuyển **chính Border/row/host cũ**, không clone settings/control/handler. Bốn host cũ chỉ còn là vùng dựng tạm collapsed; card được chuyển ra trang thật trước khi UI sẵn sàng.
- Gộp sáu quick-switch trùng property vào owner chi tiết; menu/play inline switches vẫn đồng bộ model qua đường cũ. ShowNotes chuyển về hình dáng nốt; ShowBackground về nền; light beams về Ánh sáng; AR key tint về Ánh sáng. Spark/emitter/physics có một owner ở Nhấn, Wisps/Flames/Hold FX ở Giữ, Release FX ở Thả.
- Physics và các tham số ít dùng nằm trong Expander **Nâng cao**. Search mở phần có kết quả và trả lại trạng thái mở/đóng trước search khi xóa query. Deep link theo card mở phần Nâng cao cần thiết, xóa bộ lọc cản đường và scroll/pulse card như cũ.
- Audio/MIDI và Practice/History dùng lại nguyên các control XAML, named hosts và event handlers. Search theo card nghiệp vụ: có kết quả thì hiện cả card, không lọc rời rạc label khỏi control. Device-derived lists, MIDI track mute/colors, history chart/ghost, theme/language chips vẫn dùng logic cũ.
- Search khớp English + Vietnamese kể cả ngôn ngữ không đang chọn; normalize dấu và `đ`. Caption highlights vẫn theo ngôn ngữ hiển thị. Preset/share-code workflow trong Overview cũng có thể tìm.
- Caption choice xuống hàng riêng, label wrap; sidebar wrap tên dài, cả 12 trang dùng ScrollViewer. Đơn vị góc dùng `°`, vẫn parse được typed values theo đường cũ. Không giảm minimum window của toàn app hay thay kích thước scene.
- Badge phân biệt Shared / Stage only / AR only ở các editor được phân loại. Bộ chọn Stage/AR trên header **chỉ chọn ngữ cảnh cài đặt trong phiên**, chỉ nhấn mạnh/giảm nhấn badge theo ngữ cảnh; không bật/tắt output AR, không đổi camera, không disable editor, không CopyFrom/MarkModified/ApplyVisualSettings. AR output thật vẫn bật bằng control ArEnabled cũ. Đây không phải hai bản preset độc lập.
- Reset nhóm có xác nhận, đi qua `CopyFrom` trên bản sao current settings để giữ cùng ranh giới machine-local như Apply preset, gồm nguồn camera/video, view/calibration-related options, audio device/gain/latency, language, FPS/VSync và background path. Các nút calibration reset/import/export thật vẫn riêng biệt và giữ nguyên handler.

## Tương thích đường vào cũ

| Tên cũ | Trang thật mới |
| --- | --- |
| Style | Overview |
| Theme / General | Application |
| Notes | Notes & color |
| Particles | Press |
| Keyboard / Background | Stage & background |
| Camera / Camera & FX | Camera & AR |
| Audio / MIDI | Sound & MIDI |
| Practice / History | Practice & history |
| Recording | Recording |

Chevron của layer resolve theo owner/card đã chuyển, không giữ đích cũ sai nhóm (ví dụ Light beams). Các inline More links của Flame → Hold, Embers → Press, Halo → Lighting được cập nhật riêng. `JumpToCard` vẫn dùng các title key cũ. Preset/property/theme/language IDs trong file lưu không đổi.

## Kiểm tra và giới hạn

- `tools/check_sources.py`: PASS (XAML/resource/event handlers/catalogue/localization/syntax guards).
- `tools/check_settings_layout_migration.py`: PASS — chín builder và các handler nghiệp vụ được chọn nguyên vẹn so với source đã duyệt; mọi XAML name/event cũ và unnamed business actions được giữ; có alias routes, reparent/dedup, Advanced/search và reset boundary; không sửa Gpu/Stage/Camera/Audio/Video.
- `tools/check_settings_layout_inventory.py`: PASS cho snapshot **trước migration**. Prototype và TSV cố định source `3914aac`, không tự đổi theo UI mới; generator đọc Git object này để giữ bằng chứng bố cục đã duyệt. Chạy các audit này cần commit gốc có trong local Git history.
- Contract trong `VerificationSuite` đã cập nhật cho 12 trang, canonical aliases, một owner/property, card/row/page agreement và context không sửa model. **Chỉ được compile trong app-build; không thực thi native suite.**
- Biên dịch/package Windows: **SUCCESS** tại source `3072caf707b56265a6dcf2ea31c79a67353952fe`, [run 38861088955](https://github.com/lxmtuu/PianoPath/actions/runs/38861088955). Static 26 s; self-contained win-x64 publish/package 4 m 39 s. `test`, full `build`, `keyboard-light`, `key-ensemble`, `ar-budget` và Python fixture lane đều skipped.
- **[Tải build Settings WPF](https://github.com/lxmtuu/PianoPath/actions/runs/38861088955/artifacts/12029109241)**: private artifact `keyflow-app-win-x64`, **70,988,011 bytes**, tạo `2026-10-16T06:30:18Z`, chưa expired khi truy vấn; workflow retention 7 ngày (API expires `2026-10-23T06:25:10Z`). Giải nén toàn bộ rồi chạy `PianoPath.exe`; không cần cài .NET, không phải installer/public release, build chưa ký. `BUILD-INFO.txt` chứa source SHA và SHA256 executable. Không tải ZIP/dataset vào repo.
- Không chạy native/runtime/CPU/FPS/long-motion checks, không lấy screenshot app hay chứng nhận WPF layout trên Windows. Static guards và compile không chứng minh runtime behavior, focus/scroll/DPI hoặc visual acceptance.

## Lệnh source-only

```sh
python3 tools/check_settings_layout_migration.py
python3 tools/check_settings_layout_inventory.py
python3 tools/check_sources.py
```

Nguồn renderer, model/defaults, preset JSON và pipeline export/recording không bị sửa trong đợt này. Build mới, khi có, chỉ thay lớp Settings của app và các đường điều hướng liên quan.
