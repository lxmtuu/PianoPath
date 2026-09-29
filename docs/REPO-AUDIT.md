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
| **Trợ năng**: mọi nút chỉ có glyph (`A`, `B`, `×`, `↺`, các nút icon, chevron của hộp thoại Play) có tên cho trình đọc màn hình, lấy từ tooltip đã dịch; mọi hàng sinh tự động của dock (slider, ô số, combo, swatch màu, ô màu) được đặt tên theo nhãn của hàng; **Tab** ở lại trong dock (`KeyboardNavigation.TabNavigation="Cycle"` tại `Ui/MainWindow.xaml`) và đi qua từng trang theo đúng thứ tự các hàng được in; ở cửa sổ 1080×700 mọi hàng vẫn nằm trong cột cuộn và mọi điều khiển vẫn nằm trong thẻ của hàng | `VerifyAccessibility` + `VerifyDockAccessibility` + luật `scan_accessible_names` (checker) |
| **Bộ cài song ngữ**: `installer/Languages/Vietnamese.isl` ghi đè 108 câu của wizard (đúng `[Messages]`/`[CustomMessages]`, giữ nguyên mọi placeholder, lưu UTF‑8 có BOM) và `installer/Keyflow.iss` khai báo hai mục `[Languages]` + `[LangOptions] vietnamese.*` (`$041e`); `build.yml` biên dịch bộ cài trên `publish\win-x64` giả mỗi lần push, `release.yml` biên dịch từ bản publish thật rồi đính kèm bộ cài | `scan_installer` (checker) + `tools/inno_messages.py` + bước *Build the installer* trong cả hai workflow |
| **High contrast**: khi Windows bật, khung giao diện vẽ bằng `SystemColors` **và vẽ lại ngay khi cửa sổ nhận thông báo của Windows** (`SystemParameters.StaticPropertyChanged` → `ShellThemeManager.OnSystemParametersChanged`); theme người dùng chọn vẫn nằm trong file cài đặt và tự quay lại khi tắt | `VerifyAccessibility` (nhánh `ShellThemeManager.ForceHighContrast`, kiểm luôn việc thông báo tự vẽ lại) |
| **Undo / redo**: 32 ảnh chụp JSON, `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y`, một lần kéo slider là **một** bước (commit ở nhịp idle của `_settingsSaveTimer`), khôi phục qua `CopyFrom` + `RefreshSettingControls`; nhập hồ sơ mở một lịch sử mới nên `Ctrl+Z` không lùi qua nó | `VerifySettingsHistory` |
| **Hồ sơ cài đặt**: `Keyflow.profile.json` (`Profile/SettingsProfile.cs`) gom cài đặt sân khấu + ngôn ngữ + theme; nhập/xuất ở trang General; kéo‑thả `.json` (hồ sơ), `.mid`/`.midi` (mở bài), ảnh (đặt nền) vào cửa sổ; ngôn ngữ không có trong build rơi về `en` | `VerifySettingsProfile` |
| **Tìm kiếm trong dock**: từ đồng nghĩa theo setting (`SearchSynonyms`), khớp theo token (giao), tên setting là lưới an toàn cuối, phần khớp được tô accent bằng `Run` | `VerifySettingsDock` + `VerifyLanguageSwitching` |
| **Tài liệu song ngữ**: `README.en.md` bản dịch 1‑1, hai bản trỏ nhau, `scan_readme` quét cả hai, bảng tham số dòng lệnh kiểm riêng từng bản | `tools/check_sources.py` |
| **Thẻ F1** thêm dòng `Ctrl+Z / Ctrl+Shift+Z` và câu mô tả đi qua `Loc` (thêm khoá vào cả hai bảng) | `scan_readme`/checker + `--verify` (`UnknownKeys` là lỗi) |
| **Lịch sử luyện tập** (P1#4, phần chính): `Practice/PracticeHistory.cs` append mỗi lượt chơi một dòng JSON vào `history/practice.jsonl` trong thư mục cài đặt (đọc lại bỏ qua dòng hỏng, giữ 200 lượt gần nhất), `Stop()` ghi lượt vừa chơi khi có ít nhất một nốt được chấm và chỉ một lần; trang **History** (nhóm SESSION) in các lượt gần nhất, lượt tốt nhất của bài đang mở, nút **EXPORT HTML** (báo cáo có bảng lượt chơi + bảng tổng hợp theo bài) và **CLEAR HISTORY** | `VerifyPracticeHistory` |
| **Suy luận chia tay** (P1#7): `Midi/HandSplit.cs` gom hai cụm theo thời lượng vang (mỗi tay ≥10% và giữa hai tay có ≥5 semitone trống), chốt ứng viên gần C4 trong khoảng trống, bài một tay hoặc chồng lấn giữ nguyên lựa chọn của người dùng; công tắc ở Notes → Color đưa kết quả qua chính slider **Hand split point** rồi lưu kèm cờ `SplitInferred` trong `library.json`, mở lại dùng giá trị đã nhớ | `VerifyHandSplitInference` (toán) + `VerifyHandSplitInferenceOnSong` (trong app) |
| **Tempo luyện tập tự động** (P1#5): công tắc + ngưỡng ở trang Practice (`PracticeAutoTempo`, `PracticeMissThreshold` 1–6, mặc định 3); sai liên tiếp quá ngưỡng thì mỗi bước giảm 5% (sàn 50%), đúng liên tiếp 4 nốt thì tăng 2% và dừng ở 100%; mỗi bước đi qua `TempoSlider` nên nhãn tempo và đường phát theo đúng nhịp thường; `ResetScore` xoá cả hai chuỗi đếm | `VerifyPracticeTempo` |
| **Thư viện bài** (lát cắt v0.5 của P1#3): hộp thoại Play có mục **RECENT** đọc `library.json` trong thư mục cài đặt (mới nhất trước, tối đa 12 bài, khoá theo đường dẫn, `×` để quên); mỗi dòng lưu số nốt, số track, tempo, điểm chia tay, tốc độ rơi, tempo phát và preset, mở lại là khôi phục qua chính các slider (đi qua đúng đường của người dùng nên vào cả lịch sử undo); tệp hỏng đọc thành rỗng, bài mất khỏi đĩa tự bị loại | `VerifySongLibrary` |

## 3. Hạn chế còn lại (có chủ đích hoặc chưa làm)

### 3.1 Việc đang mở trong P0

* **Không còn mục nào đang mở.** Cả bảy mục P0 đã xong và mỗi mục có lớp kiểm chứng riêng (bảng §2,
  `docs/ROADMAP.md` §1); việc tiếp theo nằm ở P1 của roadmap (xuất MP4/alpha, thư viện bài, lịch sử
  luyện tập…), không phải một mục P0 còn dở.
* **Bộ cài tiếng Việt là bản dịch *một phần* — có chủ đích, không phải việc đang mở.** `installer/Languages/Vietnamese.isl`
  ghi đè 108 câu mà wizard thật sự hiện (trang welcome/license/thư mục/việc làm thêm/Start Menu/ready/
  tiến trình/kết thúc, các hộp thoại lỗi, trình gỡ cài đặt); ~162 câu còn lại của Inno Setup (các trang
  bộ cài này không dùng, chuỗi shell-extension, trang đĩa/component…) vẫn là tiếng Anh và trình biên dịch
  in ra một cảnh báo `… has not been defined for the "vietnamese" language` cho mỗi câu — đó là hành vi
  đã biết của tệp một phần, không phải lỗi. `tools/build_installer.ps1` chỉ tha đúng loại cảnh báo đó.

### 3.2 Giới hạn kỹ thuật của sản phẩm (giữ nguyên, đã ghi ở README)

* SoundFont chỉ đọc SF2 **không nén** (không `.sf3`), một phần modulator/filter/FX của đặc tả chưa có.
* Chưa có MusicXML/khuông nhạc; lịch sử luyện tập mới ở mức ghi theo lượt + báo cáo HTML (chưa có "ghost" so hai lần chạy, chưa có biểu đồ theo ngày); thư viện bài mới ở mức recent + metadata (chưa theo dõi thư mục, tìm kiếm, tag); suy luận chia tay đã có nhưng chỉ áp dụng khi hai tay tách nhau rõ (chồng lấn thì giữ điểm người dùng chọn), chưa đọc được thẻ `<staff>` của MusicXML (sẽ làm cùng mục #6) và chưa sửa theo khoảng nghỉ/repeat.
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
