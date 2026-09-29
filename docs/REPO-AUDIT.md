# Rà soát toàn kho — 2026‑09

Ghi lại **kết quả rà soát toàn bộ mã nguồn** (đợt 2026‑09) và những hạn chế còn lại, để lần sau
không phải đọc lại từ đầu. Đây là tài liệu *trạng thái*, không phải kế hoạch: kế hoạch nằm ở
`docs/ROADMAP.md`, kiến trúc đa ngôn ngữ ở `docs/LOCALIZATION.md`, ảnh giao diện ở
`docs/LOCALIZATION.md` §10.

## 1. Phương pháp

Rà soát chạy bằng ba lớp đã có của repo, không thêm công cụ mới:

| Lớp | Chạy ở đâu | Nhìn thấy gì |
|---|---|---|
| `tools/check_sources.py` | mọi máy, ~2 s | Cân bằng ngoặc/chuỗi C#, XML hợp lệ, tham chiếu resource, khoá chuỗi in ra, bảng tham số dòng lệnh, ảnh/anchor của **cả hai** README, tên trợ năng của nút chỉ có glyph |
| `--verify` (`Diagnostics/VerificationSuite.cs`) | Windows (CI) | Toán shader, MIDI, SoundFont, AVI, cài đặt, dock, ngôn ngữ, lịch sử, hồ sơ, trợ năng, high contrast, các lớp hiệu ứng |
| Ảnh CI render | `build.yml` | Tám ảnh trong `docs/previews/` (bảy `--lang=en`, một `--lang=vi`) do chính `PianoPath.exe` chụp, cộng ảnh mẫu `docs/samples/stage-backdrop.png` do script Python sinh |

Một việc chỉ được coi là "xong" khi **cả ba** lớp nhìn thấy nó (luật 0 của `docs/ROADMAP.md`).

## 2. Đã sửa trong đợt này

| Việc | Bằng chứng kiểm chứng |
|---|---|
| **Trợ năng**: mọi nút chỉ có glyph (`A`, `B`, `×`, `↺`, các nút icon, chevron của hộp thoại Play) có tên cho trình đọc màn hình, lấy từ tooltip đã dịch; mọi hàng sinh tự động của dock (slider, ô số, combo, swatch màu, ô màu) được đặt tên theo nhãn của hàng; **Tab** ở lại trong dock (`KeyboardNavigation.TabNavigation="Cycle"` tại `Ui/MainWindow.xaml`) | `VerifyAccessibility` + luật `scan_accessible_names` (checker) |
| **High contrast**: khi Windows bật, khung giao diện vẽ bằng `SystemColors`; theme người dùng chọn vẫn nằm trong file cài đặt và tự quay lại khi tắt | `VerifyAccessibility` (nhánh `ShellThemeManager.ForceHighContrast`) |
| **Undo / redo**: 32 ảnh chụp JSON, `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y`, một lần kéo slider là **một** bước (commit ở nhịp idle của `_settingsSaveTimer`), khôi phục qua `CopyFrom` + `RefreshSettingControls` | `VerifySettingsHistory` |
| **Hồ sơ cài đặt**: `Keyflow.profile.json` (`Profile/SettingsProfile.cs`) gom cài đặt sân khấu + ngôn ngữ + theme; nhập/xuất ở trang General; kéo‑thả `.json` (hồ sơ), `.mid`/`.midi` (mở bài), ảnh (đặt nền) vào cửa sổ; ngôn ngữ không có trong build rơi về `en` | `VerifySettingsProfile` |
| **Tìm kiếm trong dock**: từ đồng nghĩa theo setting (`SearchSynonyms`), khớp theo token (giao), tên setting là lưới an toàn cuối, phần khớp được tô accent bằng `Run` | `VerifySettingsDock` + `VerifyLanguageSwitching` |
| **Tài liệu song ngữ**: `README.en.md` bản dịch 1‑1, hai bản trỏ nhau, `scan_readme` quét cả hai, bảng tham số dòng lệnh kiểm riêng từng bản | `tools/check_sources.py` |
| **Thẻ F1** thêm dòng `Ctrl+Z / Ctrl+Shift+Z` và câu mô tả đi qua `Loc` (thêm khoá vào cả hai bảng) | `scan_readme`/checker + `--verify` (`UnknownKeys` là lỗi) |

## 3. Hạn chế còn lại (có chủ đích hoặc chưa làm)

### 3.1 Việc đang mở trong P0

* **Bộ cài đã có tiếng Việt, nhưng là bản dịch *một phần* — có chủ đích.** `installer/Languages/Vietnamese.isl`
  ghi đè 108 câu mà wizard thật sự hiện (trang welcome/license/thư mục/việc làm thêm/Start Menu/ready/
  tiến trình/kết thúc, các hộp thoại lỗi, trình gỡ cài đặt); ~162 câu còn lại của Inno Setup (các trang
  bộ cài này không dùng, chuỗi shell-extension, trang đĩa/component…) vẫn là tiếng Anh và trình biên dịch
  in ra một cảnh báo `… has not been defined for the "vietnamese" language` cho mỗi câu — đó là hành vi
  đã biết của tệp một phần, không phải lỗi. `tools/build_installer.ps1` chỉ tha đúng loại cảnh báo đó.
* **Trợ năng bước 2**: font scale (case test `--compact` 1080×700) và thứ tự tiêu điểm (tab order)
  theo từng trang dock chưa được assert.

### 3.2 Giới hạn kỹ thuật của sản phẩm (giữ nguyên, đã ghi ở README)

* SoundFont chỉ đọc SF2 **không nén** (không `.sf3`), một phần modulator/filter/FX của đặc tả chưa có.
* Chưa có MusicXML/khuông nhạc, thư viện bài, lịch sử luyện tập; điểm chia tay cố định C4 = 60.
* REC ghi **AVI phần hình** (MJPEG hoặc RGB không nén, trần 2 GB), chưa ghép tiếng, chưa có MP4/alpha.
* Chỉ Windows (WPF + WinMM), cần thiết bị MIDI mà Windows nhìn thấy.
* Chưa có webcam/3D phối cảnh; âm thanh chưa có convolution reverb, sympathetic resonance.
* Chi phí bake shader nằm trên CPU (bake một lần rồi cache — đổi preset nhiều sẽ thấy).

### 3.3 Rủi ro quy trình đã biết

* `build.yml` commit ảnh preview trở lại chính nhánh đã push, nên lần push kế tiếp phải
  `git rebase origin/<nhánh>` trước (đã gặp và xử lý).
* Thư mục `publish/` và ảnh preview là **artifact**, không phải nguồn: đừng commit tay.
* `docs/previews/` chỉ nhận ảnh do `PianoPath.exe` render trong CI; ảnh chụp tay sẽ bị checker báo lệch.

## 4. Cách chạy lại đợt rà soát

```powershell
python tools/check_sources.py          # tĩnh, mọi máy, phải in "no bracket, quote, XML ... problems found"
dotnet run --project PianoPath.csproj -c Release -- --verify --verify-log=verify.log
python tools/inno_messages.py          # chỉ khi Inno Setup lên bản mới: xem lại danh sách tên câu hợp lệ
pwsh tools/build_installer.ps1 -Stub   # cần Inno Setup; biên dịch installer\Keyflow.iss như CI vẫn làm
```

`--verify` trả mã thoát 0/1, ghi log ra `%TEMP%\keyflow-verification.log` (hoặc `--verify-log=`), và
đếm cả `SKIP` (ví dụ SF2 là con trỏ LFS) để không ai nhầm "bỏ qua" với "đã kiểm".
