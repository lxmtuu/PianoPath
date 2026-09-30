# Tái cấu trúc điều hướng dock: từ danh sách phẳng sang ba nhóm theo mục đích
> **Cập nhật sau tài liệu này:** dock đã có trang thứ 12 (**General**, nhóm thứ tư **APP**) để chứa bộ chọn ngôn ngữ và trang thứ 13 (**History**, nhóm **SESSION**) để chứa lịch sử luyện tập; mọi con số "11 trang / ba nhóm" ở dưới là trạng thái tại thời điểm rà soát. Kiến trúc điều hướng không đổi: `Ui/SettingsPages.cs` vẫn là nguồn sự thật duy nhất và `tools/check_sources.py` vẫn chứng minh XAML khớp danh mục.


Tài liệu này ghi lại đợt rà soát **cách sắp xếp chức năng** của Keyflow và những thay đổi đi kèm
(bố cục dock, định danh giao diện, thẻ phím tắt, đường ảnh README). Phần *chức năng* của từng trang
cài đặt vẫn được đối chiếu ở `docs/SETTINGS-WIRING-AUDIT.md`; shader đổ bóng nằm ở
`docs/UI-SHADER-REVIEW.md`.

> Môi trường: sandbox rà soát **không có .NET SDK** và WPF chỉ biên dịch trên Windows. Vì vậy mọi
> thay đổi ở đây đều được kiểm bằng ba lớp: `tools/check_sources.py` (tĩnh, chạy mọi máy, CI chạy
> trước bước build), `VerificationSuite` (`--verify`, chạy trên runner Windows), và ảnh do chính ứng
> dụng render trong CI (`--snapshot`).

## 1. Kết quả rà soát (các vấn đề đã sửa)

| # | Vấn đề | Vì sao đáng sửa | Cách xử lý |
|---|---|---|---|
| 1 | **Dock 11 trang nằm phẳng** dưới một tiêu đề cứng "STAGE DESIGN", trong đó chỉ 7 trang đầu thật sự thuộc nhóm thiết kế sân khấu | Nhóm Audio / MIDI / Practice / Recording bị đặt sai nhãn; càng thêm trang càng khó tìm | `Ui/SettingsPages.cs` giữ **danh mục ba nhóm** (STAGE DESIGN / SOUND & INPUT / SESSION); nhãn nhóm in từ attached property `SettingsPages.Section` trên trang đầu của mỗi nhóm, template dock không còn tiêu đề cứng |
| 2 | **Id giao diện lệch tên hiển thị**: `sakura` mang tên "Concert Grand", `noir`, `velvet` | File lưu, preset và log đọc ra một đằng, người dùng thấy một nẻo; tên theme cũ đã bị gỡ nhưng id thì không | Id chuẩn hoá thành slug khớp tên: `concert-grand` / `concert-noir` / `velvet-gold`; bảng `ShellThemes.LegacyIds` giữ tương thích (`sakura`, `sakura-nocturne`, "Sakura Nocturne" → `concert-grand`; `noir` → `concert-noir`; `velvet` → `velvet-gold`) và `PianoVisualSettings.ApplyMigrations` viết lại id khi nạp file cũ |
| 3 | **Token màu "Petal"** trong theme được dùng cho *bụi acoustic* của backdrop | Tên token mô tả sai thứ nó vẽ | Đổi thành `Mote` / `MoteAlt` và khoá resource `MoteBrush`; preset/thẻ đọc cùng một bảng màu |
| 4 | **Mọi phím tắt chỉ có trong README**: app tự ẩn toàn bộ giao diện sau 2,8 giây, người mới dễ mất thanh công cụ và không biết `Esc`/`F11`/các phím đàn | Cửa vào duy nhất của tài liệu là trang GitHub | Thẻ **Keyboard & Shortcuts (F1)** dựng tại chỗ từ bảng dữ liệu trong `Ui/MainWindow.Shortcuts.cs`, mở bằng `F1` hoặc link trong menu, đóng bằng `Esc`, click nền hoặc nút ✕ |
| 5 | **Ảnh README đã lệch**: menu hiển thị "Sakura Nocturne" và nhãn nút cũ ("Play"/"Design") trong khi code hiện tại là "Concert Grand" và "Perform & Play"/"Stage Design Studio" | Ảnh là ấn tượng đầu tiên về sản phẩm | Ảnh chuyển vào `docs/previews/`, **do ứng dụng render trong CI** mỗi lần build và upload thành artifact `keyflow-previews`; README nêu rõ hai cách làm mới |
| 6 | **CI không chạy kiểm tra tĩnh** dù tài liệu nói có | Tài liệu sai và lỗi cú pháp/XAML chỉ lộ ra trên máy Windows | `build.yml` chạy `python tools/check_sources.py` trước `dotnet restore` |
| 7 | **Nút đóng tròn của hộp thoại Play vẽ ra vòng tròn rỗng** (không có dấu ✕): template `RoundGlyphButtonStyle` tô icon bằng `Fill`, nhưng `IconClose`/`IconMinimize` là hình **nét hở** nên `Fill` không vẽ gì | Lỗi im lặng, chỉ lộ ra khi nhìn ảnh render thật | Mọi template vẽ icon từ `Tag` giờ tô **cả `Fill` và `Stroke`** (`GlyphButtonStyle`, `IconTextButtonStyle`, `RoundGlyphButtonStyle`, `CaptionButtonStyle`, `CaptionCloseStyle`, nav item), kèm quy tắc mới trong `check_sources.py`: icon nào là hình đặc hay nét hở cũng hiện |
| 8 | **Không có gì bảo đảm danh mục trang, XAML và tài liệu khớp nhau** | Một trang thêm vào chỉ ở XAML sẽ lặng lẽ lệch khỏi tìm kiếm, menu `--settings-tab` và thẻ nhóm | Bốn kiểm tra tự động: runtime (`VerificationSuite`), tĩnh (`check_sources.py`: danh mục ↔ tab strip, theme token ↔ `App.xaml`, template icon ↔ luật Fill+Stroke) |

## 2. Vì sao là ba nhóm này

| Nhóm | Trang | Câu hỏi người dùng đang hỏi |
|---|---|---|
| **STAGE DESIGN** | Style, Theme, Notes, Particles, Keyboard, Background, Camera & FX | "Sân khấu trông thế nào?" |
| **SOUND & INPUT** | Audio, MIDI | "Tiếng đàn và đường MIDI đến từ đâu?" |
| **SESSION** | Practice, Recording | "Buổi tập này chạy và được ghi lại ra sao?" |

Ba nhóm này cũng là cách thẻ Play dialog và menu nói chuyện với dock: mọi chevron trong dialog deep-link
tới một trang, còn nút **OPEN DESIGN** mở nhóm thiết kế. Tìm kiếm trong dock tự nhảy sang trang đầu tiên
có kết quả, nên người dùng không cần nhớ trang nào nằm ở nhóm nào.

## 3. Nguồn sự thật duy nhất cho danh mục trang

```csharp
internal static readonly SettingsSection[] Sections =
[
    new(DesignSection,  [Style, Theme, Notes, Particles, Keyboard, Background, Camera]),  // STAGE DESIGN
    new(SoundSection,   [Audio, Midi]),                                                    // SOUND & INPUT
    new(SessionSection, [Practice, Recording]),                                            // SESSION
];

internal static readonly string[] Order = [.. Sections.SelectMany(section => section.Pages)];
```

- `Order` **suy ra** từ `Sections`, nên không có trang nào tồn tại ngoài một nhóm.
- Tab strip trong `Ui/MainWindow.xaml` gắn `local:SettingsPages.Section="…"` cho **trang đầu** mỗi nhóm;
  template `SettingsNavItemStyle` in nhãn đó và tự thu gọn khi thuộc tính chưa được đặt.
- Thêm một trang = **một dòng** trong `Sections` + **một `TabItem`** trong XAML. Quên một trong hai thì
  `check_sources.py` báo lỗi ngay (thứ tự, tên trang, thiếu/thừa nhãn nhóm), và `--verify` khẳng định lại
  trên ứng dụng thật.

## 4. Thẻ phím tắt (F1)

- Dữ liệu bảng phím tắt nằm trong `ShortcutGroups` (`Ui/MainWindow.Shortcuts.cs`), dựng một lần khi mở
  lần đầu thành ba cột: **Play the stage**, **Move around**, **Session & capture** (12 dòng).
- `F1` mở/đóng, `Esc` đóng trước khi xử lý tới ô tìm kiếm/dock, click nền hoặc nút ✕ cũng đóng.
- Trong lúc thẻ mở, đồng hồ tự ẩn giao diện **không** chạy (`ShortcutsVisible` được kiểm ở
  `CheckChromeIdle` và `Window_MouseMove`), nên thẻ không biến mất khi người dùng đang đọc.
- `VerificationSuite` mở/đóng thẻ bằng chính các hàm của UI và khẳng định có đủ ba nhóm, mỗi nhóm ≥ 4 dòng.

## 5. Ảnh giao diện do CI render

`build.yml` render 6 ảnh sau **từ chính file `PianoPath.exe` vừa build và vừa vượt qua `--verify`**:

| Ảnh | Lệnh |
|---|---|
| `docs/previews/stage-live.png` | `--snapshot … --compact --play-preview` |
| `docs/previews/main-menu.png` | `--snapshot … --compact --menu` |
| `docs/previews/design-dock.png` | `--snapshot … --compact --show-settings --settings-tab=style` |
| `docs/previews/theme-dock.png` | `--snapshot … --compact --show-settings --settings-tab=theme` |
| `docs/previews/play-dialog.png` | `--snapshot … --compact --play-dialog` |
| `docs/previews/shortcuts.png` | `--snapshot … --compact --shortcuts` |

Để ảnh tất định, chế độ chụp tự tắt chuyển động giao diện (`DisableChromeMotion`) và tắt tự ẩn
(`AutoHideChrome = false`), đồng thời có watchdog bảo đảm luôn ghi ra file.

Sau khi render, CI **commit thẳng ảnh mới vào nhánh vừa build** (`Refresh the README previews from
CI [skip ci]`, bỏ qua với pull request và với chính commit đó nên không thể lặp), đồng thời upload
artifact `keyflow-previews`. Muốn lấy ảnh rời hoặc render tại máy:

```powershell
gh run download <run-id> -n keyflow-previews -D docs/previews
```

`check_sources.py` kiểm tra mọi ảnh và liên kết nội bộ trong README, nên không thể xoá/đổi tên ảnh mà
quên cập nhật tài liệu.

## 6. Việc còn lại / rủi ro đã biết

- Thẻ phím tắt liệt kê `A · B · ×` cho vòng lặp A–B: đây là **ba nút trên thanh thời gian**, không phải
  phím tắt toàn cục (các phím chữ đã dành cho bàn phím đàn). Nhãn được viết ở dạng thao tác để không
  gây hiểu nhầm là hotkey; `VerificationSuite` kiểm cấu trúc thẻ, không kiểm nội dung từng dòng.
- Mau sắc mặc định trong `App.xaml` (champagne gold) trùng bảng màu **Concert Grand** để khung đầu tiên
  trước khi `ShellThemeManager` phát theme không bị nháy màu khác.
- Nhóm "SOUND & INPUT" hiện có 2 trang; nếu sau này thêm trang thiết bị (ví dụ Bluetooth MIDI, thiết bị
  vào riêng cho từng tay) thì đó là chỗ nên đặt.
