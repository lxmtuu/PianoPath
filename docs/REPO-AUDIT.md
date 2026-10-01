# Rà soát toàn kho — 2026‑09

Ghi lại **kết quả rà soát toàn bộ mã nguồn** (đợt 2026‑09) và những hạn chế còn lại, để lần sau
không phải đọc lại từ đầu. Đây là tài liệu *trạng thái*, không phải kế hoạch: kế hoạch nằm ở
`docs/ROADMAP.md`, kiến trúc đa ngôn ngữ ở `docs/LOCALIZATION.md`, ảnh giao diện ở
`docs/LOCALIZATION.md` §10.

## 1. Phương pháp

Rà soát chạy bằng ba lớp đã có của repo, không thêm công cụ mới:

| Lớp | Chạy ở đâu | Nhìn thấy gì |
|---|---|---|
| `tools/check_sources.py` | mọi máy, ~2 s (`build.yml` chạy nó ở job `static` trên `ubuntu-latest` **trước** khi job Windows được xếp lịch) | Cân bằng ngoặc/chuỗi C#, XML hợp lệ, tham chiếu resource, khoá chuỗi in ra, bảng tham số dòng lệnh, ảnh/anchor của **cả hai** README, tên trợ năng của nút chỉ có glyph |
| `--verify` (`Diagnostics/VerificationSuite.cs`) | Windows (CI) | Toán shader, MIDI, SoundFont, AVI, **MP4**, cài đặt, dock, ngôn ngữ, lịch sử, hồ sơ, trợ năng, high contrast, các lớp hiệu ứng |
| Ảnh CI render | `build.yml` | **Hai bộ ảnh** (danh sách chủ đề ở `$shots`) do chính `PianoPath.exe` chụp: `docs/previews/vi/` (bản README này, `--lang=vi`) và `docs/previews/en/` (`README.en.md`, `--lang=en`), cộng gallery preset `docs/previews/presets.jpg` (một bước riêng của workflow) và ảnh mẫu `docs/samples/stage-backdrop.png` do script Python sinh |
Một việc chỉ được coi là "xong" khi **cả ba** lớp nhìn thấy nó (luật 0 của `docs/ROADMAP.md`).

## 2. Đã sửa trong đợt này

| Việc | Bằng chứng kiểm chứng |
|---|---|
| **Trợ năng**: mọi nút chỉ có glyph (`A`, `B`, `×`, `↺`, các nút icon, chevron của hộp thoại Play) có tên cho trình đọc màn hình, lấy từ tooltip đã dịch; mọi hàng sinh tự động của dock (slider, ô số, combo, swatch màu, ô màu) được đặt tên theo nhãn của hàng; **Tab** ở lại trong dock (`KeyboardNavigation.TabNavigation="Cycle"` tại `Ui/MainWindow.xaml`) và đi qua từng trang theo đúng thứ tự các hàng được in; ở cửa sổ 1080×700 mọi hàng vẫn nằm trong cột cuộn và mọi điều khiển vẫn nằm trong thẻ của hàng | `VerifyAccessibility` + `VerifyDockAccessibility` + luật `scan_accessible_names` (checker) |
| **Bộ cài song ngữ**: `installer/Languages/Vietnamese.isl` ghi đè 108 câu của wizard (đúng `[Messages]`/`[CustomMessages]`, giữ nguyên mọi placeholder, lưu UTF‑8 có BOM) và `installer/Keyflow.iss` khai báo hai mục `[Languages]` + `[LangOptions] vietnamese.*` (`$041e`); `build.yml` biên dịch bộ cài trên `publish\win-x64` giả mỗi lần push, `release.yml` biên dịch từ bản publish thật rồi đính kèm bộ cài | `scan_installer` (checker) + `tools/inno_messages.py` + bước *Build the installer* trong cả hai workflow |
| **High contrast**: khi Windows bật, khung giao diện vẽ bằng `SystemColors` **và vẽ lại ngay khi cửa sổ nhận thông báo của Windows** (`SystemParameters.StaticPropertyChanged` → `ShellThemeManager.OnSystemParametersChanged`); theme người dùng chọn vẫn nằm trong file cài đặt và tự quay lại khi tắt | `VerifyAccessibility` (nhánh `ShellThemeManager.ForceHighContrast`, kiểm luôn việc thông báo tự vẽ lại) |
| **Undo / redo**: 32 ảnh chụp JSON, `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y`, một lần kéo slider là **một** bước (commit ở nhịp idle của `_settingsSaveTimer`), khôi phục qua `CopyFrom` + `RefreshSettingControls`; nhập hồ sơ mở một lịch sử mới nên `Ctrl+Z` không lùi qua nó | `VerifySettingsHistory` |
| **Hồ sơ cài đặt**: `Keyflow.profile.json` (`Profile/SettingsProfile.cs`) gom cài đặt sân khấu + ngôn ngữ + theme; nhập/xuất ở trang General; kéo‑thả `.json` (hồ sơ), `.mid`/`.midi` (mở bài), ảnh (đặt nền) vào cửa sổ; ngôn ngữ không có trong build rơi về `en` | `VerifySettingsProfile` |
| **Điều hướng phiên Play**: nút Play trên header và menu đều mở cùng một hộp thoại; Back/Escape quay về nguồn, nút Settings và từng chevron mở đúng dock page rồi có đường quay lại Play; CTA đổi theo trạng thái bài; tab focus được giữ trong hộp thoại, ô tìm thư viện ẩn khi không có bài | `VerifyEmbersShell` + `tools/check_sources.py` + hai README song ngữ |
| **Tìm kiếm trong dock**: từ đồng nghĩa theo setting (`SearchSynonyms`), khớp theo token (giao), tên setting là lưới an toàn cuối, phần khớp được tô accent bằng `Run` | `VerifySettingsDock` + `VerifyLanguageSwitching` |
| **Tài liệu song ngữ**: `README.en.md` bản dịch 1‑1, hai bản trỏ nhau, `scan_readme` quét cả hai, bảng tham số dòng lệnh kiểm riêng từng bản | `tools/check_sources.py` |
| **Thẻ F1 và Escape**: `Ctrl+Z / Ctrl+Shift+Z` được thêm vào thẻ phím tắt; Escape ưu tiên lớp đang thấy (help → Play → dock → menu → sân khấu), xoá nội dung ô tìm kiếm trước khi rời nếu nó đang focus | `VerifyEmbersShell` + `VerifySettingsHistory` + `scan_readme`/checker |
| **Đọc MusicXML** (P1 #6): `Midi/MusicXmlReader.cs` đọc `.musicxml`/`.xml`/`.mxl` (nén qua `META-INF/container.xml`), giữ con trỏ theo `divisions` trong từng `part`/`measure`, cộng thời gian trôi của bè vào từng nốt nên ô nhịp thứ hai không quay về 0, lưới phách sinh theo ô nhịp + tempo đang hiệu lực; `HandSplitFromStaves` chỉ nhận điểm chia khi hai tay tách hẳn và mỗi tay chiếm ≥10% số nốt; `Ui/MainWindow.xaml.cs` mở bài theo phần mở rộng qua `LoadSong` (dùng chung với MIDI), `Ui/MainWindow.Library.cs` áp điểm chia qua slider dưới cùng công tắc `InferHandSplit` và nhớ theo tệp | `VerifyMusicXmlImport` |
| **Đường tiếng của bản ghi** (P1 #1): `Audio/WavWriter.cs` chỉ biết PCM 16-bit (đúng thứ `waveOut` phát) và vá hai trường kích thước khi `Dispose`; tap nằm ở `PianoAudioEngine` — cùng một chỗ gọi cho khối đưa ra thiết bị và khối render khi máy không có thiết bị, nên WAV không thể lệch khỏi thứ đã nghe; phần tự render theo đồng hồ video nằm ở `MainWindow.PumpAudioTrack`, và writer chỉ được chạm dưới một khoá vì tap chạy trên luồng âm thanh; không nạp SoundFont thì `BeginAudioTrack()` trả về false và không có tệp rỗng nào được ghi | `VerifyRecordingAudioTrack` |
| **Thư viện theo thư mục** (P1 #3): `Library/SongFolderIndex.cs` quét thư mục bằng BFS có trần (`MaxDepth = 3`, `MaxFiles = 500`) và bỏ qua thư mục không đọc được; mỗi tệp đọc bằng **chính** `MidiReader`/`MusicXmlReader` nên thư viện không thể nói khác lúc mở bài; cache theo `Size` + `ModifiedUtc` nên lần quét sau không đọc lại tệp không đổi và **thẻ đi theo mục đã cache**; tệp hỏng bị bỏ khỏi danh sách chứ không làm hỏng lượt quét; `library-index.json` là dữ liệu dẫn xuất (xoá được, hỏng thì quên) nên không nằm trong `PianoVisualSettings`; `SongFolderWatcher` chỉ bật cờ trên luồng của watcher, `MainWindow` quét lại bằng `DispatcherTimer` trên luồng UI | `VerifySongFolderLibrary` |
| **Bóng luyện tập + biểu đồ** (P1 #4): `PracticeRun.Ghost` lưu nốt đã chấm điểm (`PracticePoint`), app gom trong `RecordPracticeNote(hit, pitch, at)` và ghi khi `Stop` qua `RecordPracticeRunIfScored`, `MainWindow.ResetScore` xoá ghost nên mỗi lượt bắt đầu từ 0; `PracticeHistory.Daily(days, now)` gom theo **ngày địa phương** và luôn trả đủ số ngày (ngày trống = 0), `AverageAccuracy`/`ChartCaption` dùng chung cho dock lẫn báo cáo HTML; `Practice/PracticeChart.cs` thuần hình học (`BarHeight`, `PitchWindow`, `Point`) nên kiểm được bằng số; trang History vẽ `Rectangle`/`Ellipse` từ chính mô hình đó; dòng ghi trước khi có ghost (thiếu hẳn thuộc tính) nạp ra `null` chứ không hỏng tệp | `VerifyPracticeGhostAndChart` |
| **Lớp khuông nhạc** (P1 #6): `Stage/SheetLayer.cs` giữ hình học thuần — `Step` đếm bậc theo ký hiệu khoa học (C4 = 28, C-1 = 0) nên hằng số dòng dưới cùng của hai khuông là giá trị của chính hàm đó, `Place` chọn khuông theo điểm chia tay rồi trả bậc tương đối, `LedgerLines` chỉ sinh dòng kẻ phụ khi nốt ra ngoài khuông, `NoteX`/`WindowStart` đặt playhead ở một phần tư chiều rộng; `PianoStage.DrawSheet` truyền nốt + `_beats`/`_beatsPerBar` + điểm chia tay nên vạch nhịp khớp lưới phách của bài (rỗng với buổi diễn live, khi đó không có vạch nhịp). Khoá nhạc là glyph 𝄞/𝄢 khi font cài có, không thì chữ G/F: chữ nhạc là ký hiệu, không phải câu chữ trong bảng dịch. **Hoá biểu** ở `Stage/MusicKey.cs`: tông suy ra bằng tương quan Krumhansl–Kessler trên thời lượng vang của từng cao độ (dưới ngưỡng khớp 0,5 thì viết theo C major), `Spell` viết cao độ theo tông đó, `AccidentalPlan` quyết định dấu thật sự đặt cạnh từng nốt theo ô nhịp của chính khuông nó nằm trên, và `SheetLayer` vẽ hoá biểu ở đầu cả hai khuông rồi đẩy mọi thứ viết theo thời gian vào phần còn lại của dải. Ảnh render của `VerifySheetLayer` đếm mực trong khoảng giữa khoá và nốt để chứng minh hoá biểu thật sự được vẽ | `VerifySheetLayer` |
| **Chuỗi PNG alpha** (P1 #2): `Video/IFrameRecorder.cs` là giao kèo chung (AVI giữ đường BGR có stride và giới hạn 2 GB, chuỗi PNG nhận buffer BGRA đóng gói chặt, `HasAlpha=true`, `IsNearSizeLimit=false`); `PianoStage.TransparentBackdrop` chỉ bỏ bốn lớp tô đục nền/vignette nên alpha chở đúng thứ sân khấu vẽ; `RecordVideo_Click` chọn bộ ghi theo `RecordingFormat`, thư mục khung nằm trong thư mục con có nhãn thời gian, và `StopVideoRecording` trả sân khấu về chế độ nền bình thường | `VerifyPngSequenceRecorder` |
| **Theme tự tạo** (P2 Preset): `Theme/UserShellTheme.cs` giữ năm màu gốc + họ nền động, `UserShellThemes.Build` suy ra hai mươi token (kẹp bề mặt vào dải tối đọc được, các lớp sáng dần theo thứ tự), `Theme/UserThemeStore.cs` lưu `<cài đặt>/themes/*.json` và `ShellThemes.Everything`/`Find` gộp theme người dùng với ba theme có sẵn nên cả hai hàng chip và phần nạp cài đặt đều thấy; `Ui/ThemeStudioWindow.cs` là xưởng có dải xem trước và `TryBuild` từ chối tên rỗng/màu không phải hex | `VerifyUserShellThemes` |
| **Kệ preset cộng đồng** (P2 Preset): `presets/*.json` là preset đầy đủ (phong bì `Version/Thumbnail/Description/Settings`), `tools/make_presets.py` sinh từ giá trị mặc định của `PianoVisualSettings` nên không bao giờ thiếu khoá, `PianoPath.csproj` nhúng vào assembly, `Stage/CommunityPresets.cs` đọc và đánh dấu `Community`; danh sách in nhãn COMMUNITY, `CommunityPresets.NameConflict` chặn lưu đè tên có sẵn/cộng đồng, `Delete` từ chối vì không có tệp trong thư mục người dùng | `VerifyCommunityPresets` + `scan_preset_shelf` trong `tools/check_sources.py` |
| **Ảnh xem trước trong file preset** (P2 Preset): `Stage/PresetThumbnail.cs` render chính `PianoStage` ở 192×112 (khung hình cố định: vài nốt hai tay + ba cú impact) rồi lưu base64 PNG trong file preset dạng phong bì `{Version, Thumbnail, Settings}`; `LoadUserPresets`/`Import` đọc lại, tệp JSON trần của bản cũ vẫn nạp với `Thumbnail = ""`, và `Decode` từ chối mọi thứ không phải PNG đúng cỡ nên ảnh hỏng chỉ làm mất ảnh. Danh sách preset dùng ảnh đã lưu khi có, không thì vẽ miniature như trước | `VerifyPresetThumbnails` |
| **Mã chia sẻ diện mạo** (P2 Preset): `Stage/VisualPresetShare.cs` gói toàn bộ cài đặt thành `KEYFLOW-LOOK-1:<base64url của JSON nén gzip>`, **bỏ đường dẫn ảnh nền** (diện mạo dùng ảnh rơi về nền màu) và `Clamp()` lại khi giải mã; trang Style có ô mã + **COPY CODE** (vào cả clipboard, lỗi clipboard thì mã vẫn nằm trong ô) và **APPLY CODE** (đọc từ ô, đi qua `CommitHistory` như mọi thay đổi cài đặt) | `VerifyPresetShareCodes` (dữ liệu thuần) + `VerifyPresetSharing` (trong app) |
| **Lớp phủ camera** (P1 #8, nửa camera): `Camera/MediaFoundation.cs` khai báo tay phần Media Foundation cần dùng — `mfplat.dll` cho `MFStartup`/`MFCreateAttributes`/`MFCreateMediaType`, **`mf.dll`** cho `MFEnumDeviceSources`/`MFCreateDeviceSource` (đặt sai ở `mfplat.dll` thì máy nào cũng báo "entry point not found", CI đã bắt được), `mfreadwrite.dll` cho `MFCreateSourceReaderFromURL`/`FromMediaSource`; interface khai theo **đúng thứ tự vtable** vì mọi interface kế thừa `IMFAttributes`. `Camera/CameraFrameReader.cs`: liệt kê camera (`IMFActivate` → tên + symbolic link, driver không trả lời thì bỏ qua chứ không hỏng cả danh sách), mở camera hoặc tệp video thành source reader **RGB32** có `MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING`, đọc khung trên luồng riêng, lật khung theo stride âm và chuyển thành top-down BGRA **đục** (nền tảng WPF vẽ bằng premultiplied alpha), `Dispose` + `MFShutdown()` khi cửa sổ đóng; `Camera/CameraOverlay.cs` giữ hình học thuần — `Place` theo bốn góc có inset 2,5%, `CopyFrame`, `IsKeyed` (khoảng cách RGB tối đa 220 ở ngưỡng 100, xanh lá phải trội hơn đỏ/xanh dương 12 đơn vị), `ApplyKey` xoá cả màu lẫn alpha, `OpacityFactor`; đọc tệp tin vòng lặp bằng cách **mở lại** reader thay vì `SetCurrentPosition` (PROPVARIANT truyền theo giá trị rất dễ sai kích thước). Dock: thẻ **WEBCAM OVERLAY** ở trang Camera & FX (`ShowCameraOverlay`/`CameraSourceLink`/`CameraVideoPath`/`CameraCorner`/`CameraSize`/`CameraOpacity`/`CameraMirror`/`CameraKeyTolerance`, tất cả qua `Clamp()`); cửa sổ bơm khung bằng `WriteableBitmap` 30 fps và gọi `MFShutdown()` lúc đóng. | `VerifyCameraOverlay` + `VerifyCameraOverlayDock` (góc/khung/key/opacity, câu trả lời cho tệp thiếu, clip AVI do app tự ghi được đọc lại, hàng trong dock, và pixel sân khấu render lại đúng ô của góc đã chọn) |
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
* MusicXML, khuông nhạc và hoá biểu đã có (điểm chia tay đọc từ `<staff>`, tông suy ra từ chính bài), nay đã **nối đuôi nốt ngắn theo phách** (`SheetLayer.Beams`/`Flags`) và **viết dấu nghỉ cho tay đang im lặng** (`SheetLayer.Rests`/`Rest`, phần tính trước gom trong `Plan` và giữ giữa các khung hình), và **nối nốt vắt qua vạch nhịp bằng dấu luyến** (`SheetLayer.Ties`/`TieUnder`: nốt được luyến không ghi lại dấu hoá, dấu hoá đi theo dấu luyến sang ô nhịp mới), và **ghép các nốt cùng lúc thành một cột hợp âm chung một đuôi** (`SheetLayer.Chords`, đuôi nối đi qua hợp âm, phần tính trước thêm cả dòng đọc từng tay qua `Streams`), và **nối đường cong khi dòng nhạc đổi tay** (`SheetLayer.Slurs`: nốt tay này dứt đúng chỗ nốt tay kia bắt đầu, trong cùng khoảng chờ của dấu luyến, hai nốt cách nhau dưới một quãng tám và không nốt nào thuộc hợp âm), nhưng khuông vẫn chưa phải bản khắc nhạc đầy đủ (còn dấu nghỉ nhiều tầng và ghép phách trong MusicXML), đồng thời MIDI format 2 (các chuỗi độc lập, chạy lần lượt) và SMPTE time division (tick là thời gian tuyệt đối, tempo chỉ ảnh hưởng lưới phách) nay đã đọc được qua `Midi/MidiFileReader.cs`. Lịch sử luyện tập, ghost/biểu đồ 14 ngày và thư viện theo thư mục đã xong (xem các dòng ở trên); phần còn lại của suy luận chia tay là sửa theo khoảng nghỉ/repeat và chỉ áp dụng khi hai tay tách nhau rõ (chồng lấn thì giữ điểm người dùng chọn).
* REC ghi được **AVI phần hình** (MJPEG hoặc RGB không nén, trần 2 GB, tiếng ở WAV cạnh đó), **chuỗi PNG 32-bit có alpha**, và **MP4 (H.264 + AAC)** — MP4 mang luôn tiếng trong tệp qua Media Foundation sink writer (`Video/Mp4Recorder.cs` + `Camera/MediaFoundation.Encode.cs`, khung đổi sang NV12 trong `Video/Nv12Frame.cs`, audio đi qua `Audio/IAudioTrack.cs`; `VerifyMp4Recorder` kiểm cả một tệp thật, đi kèm là **phép thử bộ mã hoá** `Mf.EncodeSinkProbe` (chạy trước bản ghi: đưa mô tả luồng H.264 cho bộ ghi rồi dừng — máy không có bộ mã hoá thì biết ngay, khỏi chờ vô ích) và **phép thử đường ống** `Mf.EncodeAviProbe` + `--encode-probe` ghi ba khung vào AVI không nén (không cần bộ mã hoá; chỉ chạy trong tiến trình con riêng khi lượt chạy không ra được tệp MP4 nào, và luôn sau bản ghi, nên không thể chặn bản ghi) để tách lỗi của app khỏi lỗi của máy, và việc mã hoá chạy trong **tiến trình con** (`--encode-take`) nên bộ mã hoá có làm sập tiến trình thì lượt kiểm chứng vẫn còn kết luận, máy thiếu bộ mã hoá thì ghi SKIP nêu lý do). **Hạn chế đã biết**: trên runner CI, Media Foundation **treo trong `IMFSample::SetSampleTime`** — gọi mãi không trả lời — nên ở đó chưa từng có tệp MP4 thật nào ra đời (bản ghi bị bỏ sau 60 giây và ghi SKIP kèm đúng bước cuối); vì vậy cửa sổ mở bộ ghi qua `HangGuard`/`Mp4Recorder.TryOpen` với hạn 10 giây rồi **tự lùi về AVI** kèm câu báo, và **một tệp MP4 thật vẫn đang chờ một máy Windows bình thường**. **Theo dõi bàn tay** cũng đã có (`Camera/HandTracker.cs` + thẻ HAND TRACKING ở trang Camera & FX): luật màu da YCbCr với độ nhạy riêng, lưới 32×24 và nhóm ô liền kề lớn nhất cho ra tâm/khổ bàn tay theo tỉ lệ khung, biên dạng cột cho ra **số ngón** (đường lòng bàn tay = độ cao phổ biến nhất, ngón = đoạn vượt qua đường giữa), `KeyPitch` biến vị trí ngang thành phím và sân khấu vẽ dải sáng trên phím đó kèm chấm vị trí + chấm mỗi ngón; `VerifyHandTracking` + `VerifyHandTrackingDock` kiểm từ luật màu, khung hình tự vẽ, biên dạng ngón đến đường đi thật qua pump và mực vẽ trên sân khấu. **Hạn chế**: đây là thị giác cổ điển chứ không phải mô hình học máy — nền trùng màu da hoặc ánh sáng quá ấm sẽ làm nó lẫn, và nó theo *một* bàn tay (nhóm lớn nhất). Chưa có WebM/VP9 ghi trực tiếp.
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

## 5. Đợt rà soát lần hai — 2026‑09‑30

Lần thứ hai đọc toàn kho theo đúng ba lớp ở §1, lần này **kiểm cả chính bộ kiểm**: các luật tĩnh được
tiêm lỗi thật vào một bản sao repo để xem chúng có bắt không (11/11 mutation bị bắt, xem bảng dưới),
rồi soi những chỗ mà ba lớp không nhìn tới.

### 5.1 Đã sửa

| Việc | Bằng chứng kiểm chứng |
|---|---|
| **Chín khoá chết trong hai bảng chuỗi**: `STAGE ATMOSPHERE` (bản mới là `ATMOSPHERE`), ba dạng `Next recording: …` mà chỉ dạng `… · {4}. {5}` còn được in (`Ui/MainWindow.Settings.cs:1746`), khoá bộ lọc `MIDI files (*.mid;*.midi)…` (bộ lọc thật nay có cả MusicXML), `Frames per second of the AVI file.` (nay là `… of the recording.`), `Optional recital layer…`, `Quick switches… Detailed controls live on the other pages.` và câu ghi chú "keyboard is baked once…" — tất cả là dấu vết của những lần đổi chữ: khoá mới được thêm, khoá cũ không ai xoá, và mỗi khoá như vậy vẫn bắt dịch giả mang theo một bản dịch vô ích. Đã xoá khỏi **cả hai** bảng (mỗi bảng 1246 → 1237 dòng, inventory 1225 → 1216 khoá) | đối chiếu từng khoá: văn bản thô của nó không xuất hiện ở bất kỳ tệp nào ngoài hai bảng (grep + script so khớp chính xác sau khi giải mã `\n`/`&amp;`), và bản chữ mới của mỗi câu đều đã là khoá sống |
| **Luật checker mới `scan_dead_keys(cs_files, xaml_files)`** trong `tools/check_sources.py`: mọi khoá của inventory phải khớp **chính xác** một literal trong nguồn ngoài `Localization/` (đã giải mã escape C# và entity XML) — nếu không thì báo lỗi kèm hướng xử lý; ba khoá đi qua giá trị chứ không qua literal (`Acoustic`, `Imperial`, `Obsidian` — `BackdropStyle` được tra bằng `ToString()` ở `Ui/ThemeStudioWindow.cs:72`) nằm trong danh sách miễn có tên trong hàm. So khớp chính xác chứ không phải "chứa": `Next recording: … · {4}.` là tiền tố của khoá sống `… · {4}. {5}` nên phép thử chuỗi con sẽ để nó sống mãi | `python3 tools/check_sources.py` xanh sau khi thêm (inventory 1216, "…and against the sources that hold them"); mutation test: chèn lại khoá chết vào **đúng vị trí ordinal trong cả hai bảng** → bị bắt; chèn một khoá chết là **tiền tố** của khoá sống → bị bắt; khoá tra qua tên enum → không báo nhầm |
| **`Audio/WavWriter.cs` giữ handle khi ghi lỗi**: `Append` đặt `_closed = true` khi đĩa đầy, và `Dispose` cũ thoát ngay theo cờ đó — nên tệp WAV không bao giờ được đóng, handle (và khoá ghi) nằm lại trong tiến trình tới lúc thoát. Nay `Dispose` luôn đóng stream (cờ `_disposed`), còn `_sizesUnreliable` nói rằng khối lỗi có thể đã vào đĩa một phần nên hai trường kích thước giữ nguyên như đã ghi thay vì khai thừa mẫu | `VerifyRecordingAudioTrack` giữ nguyên hợp đồng (mở xong `IsClosed == false`, `Dispose` vá đủ hai trường, ghi sau khi đóng bị bỏ qua); `IsClosed` nay đúng nghĩa "file đã đóng" |
| **`tools/build_installer.ps1` không nối `-SourceDir` tới ISCC**: tham số này chỉ dùng cho `Test-Path` và các tệp stub, còn ISCC luôn biên dịch theo mặc định `..\publish\win-x64` của `installer/Keyflow.iss` — chạy `-SourceDir ..\publish\win-arm64` thì kiểm một nơi, đóng gói một nẻo, mà vẫn "thành công". Nay đường dẫn được giải thành tuyệt đối và truyền bằng `/DSourceDir=`, nên nơi kiểm và nơi biên dịch là một. Phép kiểm "ISCC có ghi tệp không" cũng không còn bị một `Keyflow-Setup-*.exe` cũ trong `installer\Output` đánh lừa: nó so với danh sách tệp có trước khi chạy | không chạy được pwsh/Inno Setup trong sandbox này, nên phần PowerShell được soát tay (cân bằng ngoặc/chuỗi bằng script, đọc lại toàn bộ) và sẽ do bước *Build the installer* của `build.yml` chạy trên Windows xác nhận |
| **Tài liệu lệch về ảnh preview**: `README.md`, `README.en.md`, `docs/LOCALIZATION.md` §10, `docs/REPO-AUDIT.md` §1 và `docs/ROADMAP.md` §4 đều còn nói CI render **8 ảnh**/**"nine subjects"**, trong khi `$shots` và hai thư mục `docs/previews/{vi,en}` đã có **11** mục mỗi bộ (thêm `theme-dock`/`language-dock`/`stage-gpu-storm`… theo thời gian). Đã sửa thành mô tả đúng — hai bộ, mỗi bộ ghim `--lang` của chính nó — và các câu mô tả quy trình nay **không mang con số dẫn xuất** ("22 ảnh", "11 cảnh") nữa, để lần thêm một cảnh sau không phải sửa lại lần nữa; bản gộp với PR #26 giữ phần nội dung mới hơn của main (bước **gallery preset** `presets.jpg`, hành vi nhánh chỉ nhận pull request → `Previews not committed`) và liệt kê các cảnh theo tên. | `tools/check_sources.py` (anchor + liên kết ảnh của cả hai README) vẫn xanh; `ls docs/previews/{en,vi} \| wc -l` = 11 và `grep -c "Name = '" build.yml` = 11 |

### 5.2 Đã kiểm, không thấy vấn đề

* **Bộ kiểm tĩnh thật sự bắt lỗi** (mutation test trên bản sao repo): xoá một dòng dịch tiếng Việt, đổi
  placeholder, in một chuỗi không có trong bảng, gãy anchor README, đổi tên một switch dòng lệnh chỉ ở
  mã nguồn, thêm `{StaticResource}` trỏ vào khoá không tồn tại, gỡ tooltip của nút chỉ có glyph, sửa một
  khoá trong `presets/*.json`, thêm token theme không có mặc định trong `App.xaml`, làm mất một dấu xuống
  dòng trong bản dịch — **tất cả đều đỏ**, mỗi lần kèm đúng thông báo chỉ vào chỗ sai.
* **Không có TODO/FIXME/HACK**, không có bí mật/credential nào trong kho, không có tệp nhị phân bị
  commit ngoài con trỏ LFS 134 byte của `Assets/ConcertGrand.sf2` (đúng như thiết kế: app tự phát hiện
  con trỏ LFS và chuyển sang chế độ im lặng).
* **JSON preset hợp lệ** cả ba tệp và đúng mọi khoá của `PianoVisualSettings`; `python3 tools/*.py` biên
  dịch sạch; các phiên bản action trong workflow (`checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7`,
  `action-gh-release@v3`) đều là bản mới nhất hiện có.
* **I/O không ném vào mặt người dùng**: `SongFolderIndex`/`SongLibrary`/`PracticeHistory`/`SettingsProfile`/
  `UserThemeStore` đều ghi kiểu "tmp rồi move", bỏ qua dòng hỏng, coi tệp hỏng là dữ liệu rỗng; `MidiFileReader`
  kiểm độ dài header/khối trước khi đọc, `MusicXmlReader` từ chối tệp không có nốt.

### 5.3 Ghi nhận, chưa sửa (nhỏ, có chủ đích)

* **`HangGuard` để lại luồng chạy mãi**: thiết kế đã ghi rõ (không giết luồng đang trong lời gọi gốc), và
  `ManualResetEventSlim.Set()` sau `Dispose()` là an toàn theo tài liệu .NET (đã đối chiếu mã nguồn
  runtime), nên luồng đến muộn không làm sập tiến trình — đúng như tài liệu của repo nói.
* **`CameraStatus`** (`Ui/MainWindow.xaml.cs:800`) được luồng camera ghi (`:911`, `:913`) và luồng UI đọc:
  ghi chuỗi là nguyên tử nên chỉ có thể thấy giá trị cũ trong tích tắc, không hỏng dữ liệu. Muốn tuyệt
  đối thì cho nó chạy qua dispatcher, nhưng `CameraStatus` chỉ là dòng trạng thái.
* **`UserThemeStore.Save`** sửa `Name` trên chính đối tượng người gọi truyền vào (không copy trước); hiện
  mọi lời gọi đều dùng giá trị trả về nên chưa thành lỗi.
* **Script PowerShell không có lớp kiểm tĩnh**: `publish.ps1` chỉ chạy khi cắt bản phát hành, và
  `tools/build_installer.ps1` chỉ được CI biên dịch chứ không được "đọc" bằng công cụ nào — đây là vùng
  mù duy nhất còn lại của bộ kiểm.

## 6. Đợt rà soát lần ba — 2026‑10‑01 (trước khi vào P3)

Lần này đọc lại toàn kho **để xác nhận trạng thái trước khi bắt đầu `docs/ROADMAP.md` §4 (P3)**, nên
trọng tâm là đối chiếu từng câu "đã xong / chưa làm" trong roadmap với bằng chứng lấy được ngay lúc rà
soát — không sửa tính năng nào. Sandbox này **không có .NET SDK** (`dotnet: command not found`), nên
lớp biên dịch/`--verify` được đọc từ lần chạy CI mới nhất chứ không chạy lại tại đây.

### 6.1 Ba lớp kiểm chứng, số liệu đo được

| Lớp | Kết quả lần này |
|---|---|
| `python3 tools/check_sources.py` | **xanh**, exit 0, ~4 s: "checked 79 C# files and 2 XAML files, 99 resource keys, 149 named elements … 1166 keys the sources print (1266 in the inventory)" |
| `--verify` (CI, lần chạy `build` mới nhất trên `main`: run **36831576802**, 2026‑10‑01T07:39Z) | **xanh**: `static` ✅ + `build` ✅ cả 15 bước; nhật ký 300 dòng, mã thoát 0, **"Verification passed: 1407 assertions, 3 skipped check group(s)"** |
| Ảnh CI render | ⚠️ **lệch 3 merge** — xem §6.2 |

Ba nhóm SKIP của `--verify` đều là `Assets/ConcertGrand.sf2` còn là con trỏ Git LFS (workflow checkout
`lfs: false`), đúng như thiết kế.

### 6.2 Vấn đề thật tìm thấy (chưa sửa)

| Vấn đề | Bằng chứng |
|---|---|
| **Ảnh `docs/previews/` đã lệch giao diện 3 merge.** Commit cuối cùng chạm `docs/previews` là `de75f93` (2026‑09‑30T23:13:17Z, "Refresh the README previews from CI"); sau đó `main` đã nhận `1a5d50a` *Add contextual quick adjustment flow*, `0503ec2` *Close dock before opening quick adjustments* và `9b7fb57` *Redesign GPU atmosphere and hit-line effects (#31)* — cả ba đều đổi những thứ README đang chụp (menu/Play có Quick Adjust, preset Galaxy Voyage + Electric Storm). Ảnh trong README vì thế **vẫn xanh ở checker** nhưng không còn là ảnh của build hiện tại | `gh api repos/…/commits?path=docs/previews` → mục mới nhất là `de75f93`; `gh api repos/…/commits?sha=main&per_page=6` → ba commit kể trên nằm sau nó; `scan_readme` (`tools/check_sources.py:703`) chỉ kiểm **tồn tại** + **có tên trong `$shots`**, không có phép kiểm nào so ngày của ảnh với ngày của nguồn |
| **CI không còn tự commit ảnh được nữa.** Annotation của chính lần chạy xanh ở trên: `warning :: Previews not committed :: The branch only accepts pull requests…`. Nguyên nhân đã đo được: repo có **hai ruleset áp cho `~ALL`** — `24265624` *Protect Branch* (deletion, non_fast_forward) và `24233546` *Protect Main Branch* (deletion, non_fast_forward, **pull_request** với `require_code_owner_review: true` + `require_extra_approval_for_unattributed_changes: true`) — và **cả hai có `bypass_actors: null`**, tức không có ai được vượt, kể cả `GITHUB_TOKEN` của workflow. Đây chính là mục "còn mở" của P3 #1, nay đã xác nhận là nguyên nhân trực tiếp của §6.2 dòng trên | `gh api repos/lxmtuu/PianoPath/rulesets/{24265624,24233546}`; `gh api repos/…/check-runs/110269233500/annotations` |
| **10 cảnh báo nullable trong bản build Release.** Tất cả là `Dereference of a possibly null reference.` (CS8602) và tất cả nằm trong **`Diagnostics/VerificationSuite.cs`** — dòng 4100, 4101, 4148, 4153, 4199, 4275, 4279, 4288, 4348, tức khối kiểm Quick Adjust mới của `VerifyEmbersShell` (4079–4372). Kiểu mẫu giống nhau: `(Dictionary<string, ComboBox>)Field(window, "_quickAdjustChoices")` rồi giải tham chiếu kết quả. GitHub chỉ hiện tối đa 10 annotation mỗi mức nên **số thật ≥ 10**; đây là lần đầu một đợt rà soát ghi nhận cảnh báo biên dịch (các đợt trước chỉ soi TODO/FIXME) | `gh api repos/…/check-runs/110269233500/annotations` → 10 mục cùng một thông báo; đọc `sed -n 4096,4104p Diagnostics/VerificationSuite.cs` |
| **`docs/previews/presets.jpg` cũng cũ theo.** Gallery dựng từ chính `$shots`-era preset look; `9b7fb57` đổi atmosphere + hit-line của GPU nên cột "GPU (WARP)" của Galaxy Voyage / Electric Storm trong ảnh không còn là look hiện tại | cùng bằng chứng ngày commit ở dòng 1 của bảng này; `presets.jpg` nằm trong `docs/previews/` nên cùng một commit `de75f93` |

### 6.3 Đối chiếu roadmap P3 — từng dòng, bằng chứng lấy tại chỗ

| P3 | Roadmap nói | Đo lại lần này |
|---|---|---|
| #1 CI phân lớp | ✅ xong, còn mở việc commit ảnh | **đúng**: `build.yml` có job `static` (`ubuntu-latest`, chỉ `check_sources.py`) và job `build` đặt `needs: static`. Phần "còn mở" vẫn mở và đã đo được nguyên nhân (§6.2) |
| #2 Tách project test | ⬜ chưa làm | **đúng**: không có `tests/`, không `*.sln`, không tham chiếu xUnit/NUnit/MSTest nào. Số liệu roadmap hơi cũ: `VerificationSuite` nay là **56** hàm `Verify*` trong **4 888** dòng (`VerificationSuite.cs` 4 654 + `.Gpu.cs` 234), không phải "55 hàm trong ~4 400 dòng". Bốn thư mục **không** tham chiếu WPF/`System.Drawing`/Vortice — `Audio/`, `Library/`, `Midi/`, `Profile/` — tổng **2 474** dòng, đủ để link nguồn vào một project `net10.0` thường. Ghi chú thêm cho việc tách: `Practice/PracticeChart.cs` dùng `System.Windows.Point`/`Rect` (WindowsBase) nên **không** nằm trong nhóm đó dù là hình học thuần |
| #3 Perf gate | ⬜ chưa làm | **đúng**: không có `--bench` ở bất kỳ đâu; 19 switch dòng lệnh đang có là `--background-image= --compact --encode-probe= --encode-take= --gpu --lang= --menu --play-chord --play-dialog --play-preview --preset= --settings-dir= --settings-tab= --shortcuts --show-settings --snapshot --software --verify --verify-log=`. `GpuRenderLoop.Fps` (`Gpu/GpuRenderLoop.cs:63`, tính ở `:208` theo cửa sổ 0,5 s) là số đo duy nhất, đúng như roadmap mô tả — không có thời gian từng khung, không có phân vị |
| #4 Giảm cỡ bản tải | ⬜ chưa làm | **đúng**: `release.yml` checkout `lfs: true` và đóng gói nguyên thư mục publish; `publish.ps1` không có chế độ *lite* (chỉ `SelfContained`/`FrameworkDependent`), và nó **từ chối** publish khi SF2 còn là con trỏ LFS trừ khi có `-AllowLfsPointer` |
| #5 Cập nhật & đóng gói | 🟡 một phần | **đúng**: `release.yml` đính `publish/*.zip` + `Keyflow-Setup-*.exe`, không có `SHA256SUMS`/`GetFileHash` ở workflow hay script nào, không `CHANGELOG*`, không `Microsoft.WindowsAppSDK`/MSIX/winget manifest |
| #6 Nhánh portability | ⬜ chưa làm | **đúng**: interface tự viết trong repo chỉ có `Audio/IAudioTrack.cs:15` và `Video/IFrameRecorder.cs:17` (phần còn lại là interface COM của Media Foundation); không có `IStageRenderer`, không `IAudioSink` |
| #7 Kiểm chứng đường phát hành | ⬜ chưa làm | **đúng**: `git tag -l` rỗng, `gh release list` rỗng → `release.yml` chưa từng chạy; `<Version>0.4.0</Version>` vẫn ở `PianoPath.csproj` trong khi phạm vi v0.5–v0.8 đã vào |

### 6.4 Đã kiểm, không thấy vấn đề

* Không có `TODO`/`FIXME`/`HACK`/`XXX` trong bất kỳ tệp `.cs`/`.py`/`.ps1`/`.xaml`/`.yml` nào.
* `catch { }` rỗng có tồn tại nhưng **đều có chủ đích** và đều là best‑effort dọn dẹp: `Marshal.ReleaseComObject`
  (`Camera/CameraFrameReader.cs:405`, `Camera/MediaFoundation.Encode.cs:252,308`), `Flush` của source reader
  (`CameraFrameReader.cs:213,226`), `_stream.Dispose()` trong `finally` của `Audio/WavWriter.cs:185`, và ghi
  log trong tiến trình con (`Diagnostics/EncodeProbeAttempt.cs:32,33,52,61`). Không chỗ nào nuốt lỗi nghiệp vụ.
* Bộ kiểm tĩnh vẫn là lớp duy nhất chạy được trên sandbox không có .NET, và nó vẫn xanh — nên mọi thay đổi
  ở đợt P3 sắp tới đều có ít nhất một lớp kiểm chứng chạy được tại đây.

### 6.5 Không kiểm được tại sandbox này (nói rõ, không đoán)

* **Không biên dịch được C#**: `dotnet: command not found`. Mọi kết luận về build/`--verify` ở trên là đọc
  từ lần chạy CI 36831576802, không phải chạy lại.
* **Không tải được artifact/log của lần chạy**: `gh run view --log` và `gh run download` đều đứt ở bước tải
  blob (`…blob.core.windows.net… EOF`), nên 300 dòng nhật ký `--verify` chỉ đọc được qua annotation của
  check-run, không đọc được toàn văn.
* **Không chạy được PowerShell/Inno Setup** (`publish.ps1`, `tools/build_installer.ps1`) — đúng như ghi nhận
  ở §5.3, đây vẫn là vùng mù duy nhất của bộ kiểm.

### 6.6 Đã sửa trong đợt này

| Việc | Bằng chứng kiểm chứng |
|---|---|
| **Bước `Report preview drift` trong `build.yml`** — vá đúng lỗ hổng ở §6.2 dòng 1: `scan_readme` chỉ chứng minh ảnh *tồn tại* và *có tên trong `$shots`*, nên ảnh còn đúng hay không thì không lớp nào nhìn thấy. Hai bước render vốn đã ghi đè `docs/previews` bằng ảnh của chính build đó, nên `git status --porcelain -- docs/previews` **chính là** bảng đối chiếu: danh sách rỗng nghĩa là ảnh đã commit là ảnh của build này, còn mỗi đường dẫn listed là một ảnh README không còn khớp với code bên cạnh nó. Bước chạy `if: always()` **và** chỉ khi cả `steps.render-previews` lẫn `steps.render-gallery` đều `success` (hai `id` mới thêm), để một lần render hỏng không bị đọc nhầm thành "ảnh vẫn đúng"; nó chạy cả ở pull request, nơi bước commit bị bỏ qua và trước đây không có tín hiệu nào. Cảnh báo chứ không phải lỗi, có chủ đích: ruleset đang chặn token của workflow nên biến nó thành đỏ sẽ sơn đỏ mọi pull request vì một việc chỉ người (hoặc một luật bypass) giải quyết được | `bash -n` sạch; **chạy thật đoạn script** (trích từ YAML bằng `yaml.safe_load`) trong một repo git tạm ở ba trạng thái: (1) không lệch → in `docs/previews matches what this build rendered.`, exit 0, summary rỗng; (2) hai tệp đã commit bị ghi đè → `::warning title=Previews are stale::2 file(s)…` và summary liệt kê đúng hai đường dẫn; (3) thêm một tệp chưa có trong repo và một tên **có khoảng trắng** → bắt đủ 4 tệp, danh sách phân tách `, ` đúng (lỗi `paste -sd', '` luân phiên dấu phẩy/dấu cách đã sửa thành `paste -sd, - \| sed 's/,/, /g'`). `python3 tools/check_sources.py` chạy lại sau khi sửa workflow: exit 0 |
| **Hai bản README** (`README.md` + `README.en.md`) mô tả thêm bước mới trong bảng workflow, giữ hai bản 1‑1 | `tools/check_sources.py` xanh (bảng tham số/ảnh/anchor của cả hai bản) |
| **Mười cảnh báo CS8602 trong `Diagnostics/VerificationSuite.cs`** — nguyên nhân gốc không phải từng chỗ giải tham chiếu mà là **một câu `Assert` thừa**: `Assert(menu is not null && play is not null && quickAdjust is not null, …)` ở đầu `VerifyEmbersShell`. Tám biến cục bộ vừa được lấy bằng `(T)window.FindName(…)!` (đã not-null), nhưng câu null-test đứng **một mình** khiến trình biên dịch gộp cả nhánh "điều kiện sai" — vì không có gì nói với nó rằng `Assert` sai thì ném — nên từ đó về sau `menu`/`play`/`quickAdjust` là maybe-null. Hai dòng kế tiếp chữa bằng `menu!`/`play!`; từ dòng 4100 thì quên → cảnh báo. Cách chữa: thêm helper **`Element<T>(root, name)`** trả về **not-null** và ném ngay tại chỗ tra cứu kèm tên phần tử bị thiếu, thay cho cả `FindName(…)!` lẫn câu `Assert` thừa; nhờ vậy trạng thái dòng không bao giờ bị hạ xuống maybe-null và không cần `!` nào nữa. Số assertion của `--verify` giảm đúng **một** (1407 → 1406) vì bỏ câu `Assert` thừa; phần kiểm "phần tử có tồn tại" vẫn còn, và còn rõ hơn (báo tên phần tử thay vì trỏ vào dòng thứ hai mươi dùng nó) | bằng chứng cho chẩn đoán, đọc ngay trong tệp: (a) dòng 366 tác giả **đã gặp đúng lỗi này** và chữa bằng `if (baked is null) return;` kèm ghi chú *"Non-Nullable copy so the local helper below does not have to re-prove the null state"*; (b) dòng 957 và 3714 viết `Assert(x is not null && x.Foo)` — dereference **trong cùng biểu thức** nên được thu hẹp đúng, và **không** lần chạy CI nào báo cảnh báo ở đó; (c) dòng 3322 dùng `wav!.Append(…)` để thu hẹp lại sau một câu `Assert(wav is not null && …)` — cùng một mẹo `!`, xác nhận chẩn đoán. Đã quét tự động toàn tệp (script tìm mọi `Assert(<var> is not null…)` rồi dò chỗ `<var>.` ở các câu sau trong cùng hàm): đúng **3** ứng viên, và cả ba đều đã được giải thích — `wav` (đã có `wav!`), `chips` (báo nhầm vì khớp chữ trong chuỗi của câu `Assert` khác), và chính dòng chú thích XML mới thêm. `python3 tools/check_sources.py`: exit 0. **Phần biên dịch thật do CI Windows xác nhận** — sandbox không có .NET SDK nên không tự chạy được |

### 6.7 Hai việc người dùng đã chọn nhưng sandbox này **không làm được**

Cả hai đều do mạng của sandbox, không phải do repo — ghi lại để lần sau không thử lại vô ích:

* **Tải artifact `keyflow-previews` để commit ảnh mới**: artifact vẫn còn (`id 11148505196`, 7 052 863 byte, `expired=false`) nhưng API trả 302 sang `productionresultssa10.blob.core.windows.net`, và host đó **không nối được** — `gh run download` ba lần đều `EOF`, `curl -sSL` báo `OpenSSL SSL_connect: SSL_ERROR_SYSCALL`. `raw.githubusercontent.com` và `objects.githubusercontent.com` cũng 000; chỉ `api.github.com`, `pypi.org` và `registry.npmjs.org` là thông. **Hệ quả: `docs/previews/` vẫn lệch ba merge** — ảnh chỉ render được trên Windows nên không có đường nào làm mới chúng từ đây.
* **Cài .NET 10 SDK**: `dot.net`, `builds.dotnet.microsoft.com`, `dotnetcli.azureedge.net` và `api.nuget.org` đều trả `000`. Không có SDK thì không build được project test `net10.0` của P3 #2 tại đây, dù về nguyên tắc nó chạy được trên Linux.

Hai lệnh để làm mới ảnh từ máy của bạn (artifact của run 36831576802 vẫn còn):

```powershell
gh run download 36831576802 -n keyflow-previews -D docs/previews   # hai bộ vi/ + en/ và presets.jpg
git add docs/previews; git commit -m "Refresh the README previews from CI artifact 36831576802"
```

### 6.8 CI đã xác nhận (run 36834861621, nhánh `arena/01a0f671-pianopath`, commit `dcc943c`)

Sandbox không có .NET SDK nên **chính runner Windows của repo là lớp kiểm chứng** cho hai thay đổi trên:

| Đo trên CI | Kết quả |
|---|---|
| **Mười cảnh báo CS8602** | **còn 0**. `gh api …/check-runs/110279800651/annotations` không còn mục nào khớp "null reference" (trước đó: 10, và bị GitHub cắt ở 10). Chẩn đoán "một câu `Assert` thừa là nguyên nhân gốc" vậy là đúng — không phải sửa từng chỗ giải tham chiếu |
| **`Element<T>` chạy thật** | `Verification passed: **1406** assertions, 3 skipped check group(s)`, `PianoPath.exe exited with code 0 after 300 log line(s)`. Đúng **1407 → 1406** như đã tính (bỏ một câu `Assert` thừa); `VerifyEmbersShell` vẫn chạy qua nên cả tám lần tra cứu phần tử đều tìm thấy phần tử của chúng |
| **Bước `Report preview drift` chạy thật** | `! 23 file(s) under docs/previews differ from what this build rendered (…)` — nêu đủ tên 23 tệp (11 `en/` + 11 `vi/` + `presets.jpg`) |

### 6.9 Một lỗi thiết kế do chính lần CI đó lộ ra, đã sửa

Con số **23/23** là manh mối: cả `shortcuts.png` lẫn `language-dock.png` — hai cảnh mà ba commit gần đây
không đụng tới — cũng khác bản đã commit. Đối chiếu lịch sử: repo có **349 commit thì 106 là preview
refresh**, tức gần như lần push nào cũng ra diff. Kết luận: **ảnh render không ổn định theo byte giữa các
lần chạy**, nên `git status` trên `docs/previews` *luôn* khác rỗng ngay sau khi render. Bước drift vì thế
**không được đứng trước** bước commit — nếu không nó sẽ kêu oan ở mọi lần chạy, kể cả lần mà bước commit
vừa ghi ảnh mới thành công ngay sau đó (đúng cái bệnh "cảnh báo mà không ai đọc" mà nó sinh ra để chữa).

Đã chuyển `Report preview drift` xuống **sau** `Commit refreshed previews`; thứ tự đó *chính là* phép kiểm:
refresh mà ghi được thì cây làm việc sạch và bước này nói "khớp", còn push bị từ chối (GH013) hoặc bước
commit bị bỏ qua (pull request) thì tệp vẫn bẩn và độ lệch là thật, chưa ai giải quyết. Kiểm lại bằng cách
chạy đúng đoạn script trong repo git tạm: cây sạch → `docs/previews matches what this build rendered, or the
refresh above just committed it.`; hai tệp bẩn → `::warning title=Previews are stale::2 file(s) … and were not
committed`. `python3 tools/check_sources.py`: exit 0.
