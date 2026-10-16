# Settings P6 · Bố cục tương tác để duyệt

**Trạng thái: người dùng đã duyệt bố cục (“tớ thấy thuận rồi”).** Tài liệu này giữ nguyên phạm vi/bằng chứng của bước prototype; triển khai control thật và kết quả biên dịch được theo dõi tại [Settings WPF migration](SETTINGS-WPF-MIGRATION.md).

Bản này không thay `Ui/`, `Stage/`, `Gpu/`, `Camera/`, persisted settings, preset của app hay renderer. Không có Windows build mới cho giai đoạn này. Build tích hợp hiệu ứng trước đó vẫn là build đã giao; xem [báo cáo tích hợp](PARTICLE-EFFECTS-APP-INTEGRATION.md).

## 1. Bố cục đề xuất

Navigation chia ba khu vực:

- **Thiết kế hiệu ứng:** Tổng quan → **Nốt & màu → Nhấn phím → Giữ phím → Thả phím → Ánh sáng**.
- **Không gian:** Sân khấu & nền; Camera & AR.
- **Phiên & ứng dụng:** Ghi hình; Âm thanh & MIDI; Luyện tập & lịch sử; Ứng dụng.

Các điều khiển phổ biến ở ngoài; physics và tham số ít dùng ở **Nâng cao**. Tìm kiếm VI/EN trên tên hiển thị, mô tả, tên property và trang cũ; tìm được cả phần Nâng cao. Thẻ có nguồn trang/card/property để đối chiếu. Slider có đơn vị; phần góc dùng độ trong **đề xuất này**, không sửa formatter của app.

**Live Stage / AR mode** chỉ đổi ngữ cảnh xem và trạng thái khả dụng của control. Không tạo hai bản preset, không thay giá trị chung hay calibration. Nhãn `Dùng chung / Stage / AR` là đề xuất phân loại UI, **không phải chứng nhận mới về renderer hoặc mọi capability của từng mode**. Cần đối chiếu consumer khi nối WPF.

Các tham số spark/emitter/physics hiện dùng chung nhấn và giữ: đặt chủ sở hữu ở Nhấn phím, có liên kết từ Giữ phím. Không tạo hai slider nhìn như độc lập nhưng thực chất dùng cùng một property. Wisps, Flames và Hold FX ở Giữ phím; impact/rings/landing glow ở Nhấn phím; release ở Thả phím.

## 2. Bản đồ 13 trang cũ → nhóm mới

| Trang hiện tại | Đích đề xuất | Nội dung không được bỏ khi migration |
| --- | --- | --- |
| Style | Tổng quan + lối tắt đến các nhóm hiệu ứng/nền | Danh sách preset, apply/save-as/delete/import/export/reset, share code, layer switches, HUD/score/watermark |
| Theme | Ứng dụng; look/preset vẫn tìm được từ Tổng quan | Theme chips, motion/backdrop, tạo/sửa/xóa user theme, 3 quick looks |
| Notes | Nốt & màu; Nhấn; Giữ; Thả; Ánh sáng | Color/per-track palette, shape/material, motion/falling FX, impact/rings, Hold FX, release, modulators |
| Particles | Nhấn; Giữ; Tổng quan | Upgraded effects opt-in, sparks/emitter/physics, Wisps, Flames |
| Keyboard | Sân khấu & nền; Ánh sáng | Keyboard size, keys/style/labels/felt, key response, GPU shading |
| Background | Sân khấu & nền; Ánh sáng | Ảnh nền + clear, atmosphere, ambient families, hit-line halo |
| Camera & FX | Camera & AR; Ánh sáng | Camera list/source/video, picture-in-picture, AR, calibration + backup, 3D paths, live bars, delay/quality, transparent/multi-pass outputs, hand tracking, post-FX |
| Audio | Âm thanh & MIDI | Salamander setup/load, reverb/resonance/impulse, piano mute, output/ASIO, latency, gain/status |
| MIDI | Âm thanh & MIDI | Input/output/refresh, selected track, per-track mute/color, metronome's existing disabled state |
| Practice | Luyện tập & lịch sử | Practice mode, tempo, auto-tempo + miss threshold, loop A/B/clear, shortcuts |
| History | Luyện tập & lịch sử | Chart, best/latest ghost, run list/best/summary/empty state, HTML export/clear |
| Recording | Ghi hình | Recording options, capture info, start/stop |
| General | Ứng dụng | Language, graphics/FPS/VSync/status/window, accessibility, updates/release, profile import/export |

`#old/Style` … `#old/General` trong **prototype** đi đến đích chính; bảng đối chiếu dẫn đến các đích phụ. Các route WPF thực tế (`SettingsPages`, dock, menu quick cards, play dialog và `JumpToCard`) **chưa bị sửa**, không phải đã migration xong. Việc đổi 13 trang thành 12 nhóm là đề xuất cần duyệt, không phải cắt một chức năng.

## 3. Inventory từ source: phạm vi và giới hạn

Nguồn chính:

- [`SettingsPages.cs`](../Ui/SettingsPages.cs): danh mục/trật tự 13 trang.
- [`MainWindow.Settings.cs`](../Ui/MainWindow.Settings.cs): standard row builders, ButtonRow, custom Register hosts, lựa chọn động và dependent visibility.
- [`MainWindow.xaml`](../Ui/MainWindow.xaml): static controls/actions, business hosts và footer Save/Reset.
- [`PianoVisualSettings.cs`](../Stage/PianoVisualSettings.cs): defaults và `CopyFrom` giữ cấu hình máy.
- Vietnamese string table và `ArOptions` để lấy caption, constant range.

Kết quả trích xuất hiện tại, được kiểm tra tự động:

- **242 lần xuất hiện standard row**, gồm các layer quick-switch trùng property; **236 property duy nhất** có standard row.
- Model có **254 property**. **18 property ngoài bộ trích xuất standard row**, **không có nghĩa là 18 control bị thiếu hay 18 setting không dùng**. Trong đó có audio/practice của XAML, Language/ShellTheme chips, TrackColors palette, đường dẫn chọn file và metadata/legacy/internal fields.
- **29 action references:** toàn bộ ButtonRow tuples trong Build*Page, kể cả lambda/caption có điều kiện, cộng Save/Reset footer.
- **12 custom-host references:** Register/status/chips, TrackPaletteRow và BuildCameraList trong các page builders.
- **40 static XAML control/host references**, giữ option/range/event và trạng thái disabled gốc khi có. Chín generated-page hosts được đại diện bởi các row đã trích xuất, không đếm lại trong 40 mục này.

Bản kê chi tiết có property, old page/card, new route, basic/advanced, đề xuất scope, file/dòng source, visibility condition, ranges/options/handlers:

**[`SETTINGS-LAYOUT-INVENTORY.tsv`](SETTINGS-LAYOUT-INVENTORY.tsv)** — 341 bản ghi + SHA256 nguồn; có cả row occurrences, không làm mất dấu quick switch khi gộp UI.

`inventory.json` trong server có metadata đầy đủ hơn, defaults và các entry đã gộp theo property; link mở trực tiếp ở sidebar.

### Rà thủ công các nội dung tạo động

| Host / control đặc biệt | Source cần giữ khi chuyển WPF | Hành vi / nội dung đã xác định |
| --- | --- | --- |
| Theme chips | `MainWindow.Settings.cs:RefreshThemeChips`, `MainWindow.Menu.cs:ThemeChip_Click` | Catalogue built-in + user themes, active chip, mô tả theme, theme id không đổi theo ngôn ngữ |
| Language chips | `MainWindow.Settings.cs:RefreshLanguageChips / LanguageChoices / LanguageChip_Click` | Follow Windows + mọi bundled language, active caption/blurb; VI/EN của prototype không ghi property Language của app |
| Camera list | `MainWindow.Cameras.cs:BuildCameraList / RebuildCameraList / CameraList_SelectionChanged` | Windows cameras/virtual cameras, Automatic, selected/running/error states, refresh, wheel đi theo page |
| TrackPaletteRow | `MainWindow.Settings.cs:TrackPaletteRow / TrackPaletteButton_Click` | 8 swatches; color dialog; chia sẻ TrackColors với MIDI track rows |
| TrackListHost | `MainWindow.Settings.cs:RebuildTrackList / TrackMute_Changed` | Song track name + note count, swatch, mute checkbox; empty state khi chưa mở MIDI |
| PracticeHistoryHost | `MainWindow.History.cs:RefreshPracticeHistory` | Latest runs, song/time/accuracy/hits/misses/streak, best/summary/empty states; không có nút ghost riêng bị bỏ |
| HistoryChartHost | `MainWindow.History.cs:BuildPracticeChart` | Daily bars, tooltips, caption, empty days |
| HistoryGhostHost | `MainWindow.History.cs:BuildPracticeGhost / AddGhostRow` | Only/best/latest take captions, hit/miss dots, time/pitch tooltips, shared axes, empty/no-ghost case |
| Audio output / reverb / MIDI pickers | Named controls trong `MainWindow.xaml`; handler cùng tên trong window partials | Device-derived options và status không được giả lập bằng thiết bị mẫu; giữ disabled/enabled transitions của app |
| Calibration và backup | `MainWindow.Settings.cs:CalibrateAr / ResetArCalibration_Click`; AR export/import handlers | Không đổi camera/view/keyboard/profile association; không tái dùng góc của profile khác; giữ source/frame guards và cancel path |

**Giới hạn:** đây là đối chiếu source + bản bố cục, không phải C# AST/compiler hay chứng minh mọi nhánh runtime đã được chuyển. Nội dung con sinh từ dữ liệu thật được giữ bằng host/handler reference và bảng rà thủ công, không mô phỏng bằng lịch sử hoặc thiết bị bịa. Trước migration phải đối chiếu từng handler/binding/visibility/reset/deep link với [wiring audit](SETTINGS-WIRING-AUDIT.md) và [navigation audit](DOCK-NAVIGATION-AUDIT.md).

## 4. Có thể bấm gì trong prototype?

- Navigation, Stage/AR context, VI/EN captions và tìm kiếm hai ngôn ngữ không dấu/có dấu.
- Toggle, slider, choice có literal options và color picker có thể sửa **object nháp trong bộ nhớ tab**. Chuyển trang/context/language/search giữ cùng một object.
- Nâng cao mở/đóng; khi tìm kiếm, phần nâng cao có kết quả tự mở.
- Sáu community presets cục bộ: giữ nhóm machine-local theo `CopyFrom` + đường dẫn ảnh nền; tạo baseline mới để thử reset. Không thay danh mục preset tích hợp của app.
- Reset nhóm có xác nhận/hủy; chỉ tác động các property standard-row áp dụng cho ngữ cảnh/kết quả hiện tại, về baseline của preset đã chọn hoặc model defaults.
- Xuất JSON có `kind: settings-layout-prototype`, `notAnAppPreset: true`, hash nguồn và draft values. **Không phải preset để import vào app.**

**Không hoạt động và có ghi rõ:** import/export preset thật, share code, camera/device scan, calibration, audio/MIDI, history/profile, record và output windows. Buttons nghiệp vụ bị disabled hoặc hiển thị dạng tham chiếu. Các choice có options tính toán, per-track palette và custom chips được để tham chiếu; visibility dependencies hiện ghi từ source, **chưa nối điều kiện C# vào UI web**. Color picker nháp cũng không thực thi mọi side effect/binding của app.

Không gọi backend, không localStorage/sessionStorage, không đọc file cài đặt của người dùng. Reload bỏ bản nháp; không có tự lưu vào app. Không có hình render mới hoặc thiết kế lại particle artwork.

## 5. Chạy lại / kiểm tra

Không cần npm để chạy prototype:

```sh
python3 tools/serve_settings_wireframe.py --output /home/user/.cache/settings-wireframe --port 8785
```

Server bind `0.0.0.0`, các tài nguyên dùng URL tương đối; dùng được trong Live Preview. Output sinh ở cache, không đưa dataset/screenshot/browser dependencies vào Git.

Tạo lại bản kê và kiểm tra source/prototype inventory:

```sh
python3 tools/serve_settings_wireframe.py --output /home/user/.cache/settings-wireframe --build-only --audit-output docs/SETTINGS-LAYOUT-INVENTORY.tsv
python3 tools/check_settings_layout_inventory.py
node --check tools/settings_wireframe/app.js
python3 tools/check_sources.py
```

Browser QA dùng `puppeteer-core` + `@sparticuz/chromium` ngoài repo (hoặc `CHROME_BIN`), `NODE_PATH`/library path theo môi trường:

```sh
node tools/check_settings_layout_browser.cjs http://127.0.0.1:8785
```

### Kết quả phiên này

- Source/prototype inventory: **PASS**; tất cả standard builder-property calls trong Build*Page có trong inventory; TSV khớp generator; mapping bao phủ mọi đích trích xuất.
- JS syntax và `tools/check_sources.py`: **PASS**.
- Chromium UI-only: **PASS** — 12 nhóm mới/13 old links, VI/EN search, state giữ qua navigation/context/language, 6 preset giữ machine-local values, reset/cancel theo nhóm, export draft envelope, actions không nối nghiệp vụ, không external requests/storage writes.
- Responsive: **800 / 390 / 320 px × 2 ngôn ngữ × 13 views** (12 nhóm + bản đồ); cả khi mở Advanced không tràn ngang, không trùng control id, không có page/console error.
- **Không chạy lại native/render/runtime/CPU/long-motion checks, không build app**, đúng phạm vi đã thống nhất.

## 6. Sau khi duyệt bố cục

Chỉ sau khi người dùng duyệt layout mới chuyển control thật sang WPF: giữ nguyên property/binding/handler/IDs; một owner cho shared controls; adapter cho deep link/card cũ; visibility, typed values/reset, localization/accessibility và scroll behavior phải được nối lại. Camera/calibration, user presets/themes/profiles, Audio/MIDI và history cần review riêng các dynamic hosts ở trên. Không thay renderer hoặc vật liệu hiệu ứng trong bước sắp xếp menu.
