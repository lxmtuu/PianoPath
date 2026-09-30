# Roadmap: các hướng cập nhật và cải thiện của Keyflow

Tài liệu này gom **những việc đáng làm tiếp theo**, xếp theo thứ tự nên làm, kèm ghi chú kỹ thuật để
một đóng góp mới không phải đoán lại từ đầu. Trạng thái hiện tại nằm ở `README.md`; giới hạn hiện tại ở
mục *Giới hạn hiện tại* của README; kiến trúc đa ngôn ngữ ở `docs/LOCALIZATION.md`; catalogue hiệu ứng
ở `docs/EFFECTS-REDESIGN.md`.

## 0. Ba luật của mọi thay đổi

| Luật | Ý nghĩa trong repo này |
|---|---|
| **Không có chức năng "chết"** | Mọi switch/slider/combo sinh ra từ `Ui/MainWindow.Settings.cs` phải có đúng một chỗ tiêu thụ nó trong `Stage/`, `Audio/` hoặc `Ui/`. `docs/SETTINGS-WIRING-AUDIT.md` là bảng đối chiếu; `--verify` asserts từng kênh |
| **Mỗi tính năng mang theo một lớp kiểm chứng** | Ba lớp đã có: `tools/check_sources.py` (tĩnh, mọi máy), `VerificationSuite` (`--verify`, Windows), và ảnh README do CI render. Tính năng mới phải được một trong ba lớp nhìn thấy, nếu không nó sẽ lệch trong im lặng |
| **Văn bản hiển thị không bao giờ là logic** | Id, giá trị lưu, tên theme, tên preset stays English; chỉ caption dịch. Đây là điều làm `docs/LOCALIZATION.md` đơn giản và làm mục *accessibility* bên dưới khả thi |

## 1. P0 — đóng những gì đang mở (ngắn hạn, rủi ro thấp)

> **Trạng thái 2026‑09:** cả bảy mục P0 đã xong và **mỗi mục có lớp kiểm chứng riêng** (`tools/check_sources.py`,
> `--verify`, hoặc cả hai). Mục nào xong thì dòng tương ứng ở bảng dưới được ghi lại kèm bằng chứng.
> Việc còn mở nằm ở P1 (§2).

| # | Việc | Ghi chú kỹ thuật | Cách kiểm chứng | Trạng thái |
|---|---|---|---|---|
| 1 | ~~**Hoàn tất đa ngôn ngữ**~~ — đã xong: chip phím tắt F1 (nhãn mô tả) đã qua `Loc`, `UnknownKeys` giờ là lỗi trong `--verify` | Xem `docs/LOCALIZATION.md` §9; `VisualPreset` đã tách "tên đã lưu" và nhãn hiển thị qua `DisplayName` | `tools/check_sources.py` + `--verify` | ✅ xong |
| 2 | ~~**Song ngữ cho tài liệu**~~ — đã xong: `README.md` (tiếng Việt) + `README.en.md` (tiếng Anh), hai bản trỏ nhau | `README.md` giữ tiếng Việt; thêm `README.en.md` dịch 1‑1, mỗi ảnh dùng lại từ `docs/previews/`. `scan_readme` trong checker quét **cả hai** tệp | `python tools/check_sources.py` | ✅ xong |
| 3 | ~~**Ngôn ngữ cho bộ cài**~~ — đã xong: `installer\Languages\Vietnamese.isl` là bản dịch **một phần** (108 câu, liệt kê sau `compiler:Default.isl`) + `vietnamese.LanguageName/LanguageID=$041e/LanguageCodePage` trong `Keyflow.iss`; `build.yml` biên dịch bộ cài trên stub `publish\win-x64` mỗi lần push, `release.yml` biên dịch từ chính thư mục đã publish và đính kèm bộ cài vào release | `tools/check_sources.py` (`scan_installer`: tên câu, phân đoạn, placeholder, BOM, `[Languages]`/`[LangOptions]`) + `tools/inno_messages.py` sinh danh sách tên hợp lệ + `tools/build_installer.ps1` biến mọi cảnh báo lạ của ISCC thành lỗi | ✅ xong |
| 4 | ~~**Accessibility**~~ — đã xong cả hai bước: (1) `AutomationProperties.Name` cho nút chỉ có glyph, tên dịch được cho mọi hàng sinh tự động, `KeyboardNavigation.TabNavigation="Cycle"` trong dock, palette `SystemColors` khi Windows bật high contrast; (2) `VerifyDockAccessibility` đo dock ở 1080×700 — mỗi trang giữ mọi hàng trong cột cuộn và mọi điều khiển trong thẻ của hàng (một từ dài hơn ở ngôn ngữ khác không đẩy được slider ra ngoài), và Tab đi qua trang theo đúng thứ tự các hàng được in | `VerifyAccessibility` + `VerifyDockAccessibility` trong `--verify`, cộng luật `scan_accessible_names` trong checker (nút chỉ có glyph phải có ToolTip hoặc tên) | ✅ xong |
| 5 | ~~**Undo / redo cho bàn thiết kế**~~ — đã xong: stack 32 ảnh JSON, `Ctrl+Z`/`Ctrl+Shift+Z`/`Ctrl+Y`, commit khi điều khiển đứng yên nên một lần kéo là một bước, khôi phục qua `CopyFrom` + `RefreshSettingControls` | `VerifySettingsHistory` trong `--verify`: 3 thay đổi → 3 undo → JSON bằng nhau, control chạy theo, kéo liên tục là một bước | ✅ xong |
| 6 | ~~**Hồ sơ cài đặt**~~ — đã xong: `Keyflow.profile.json` (`Profile/SettingsProfile.cs`) gom cài đặt sân khấu + `Language` + `ShellTheme`; nhập/xuất ở trang General, kéo‑thả `.json`/`.mid`/ảnh vào cửa sổ | `VerifySettingsProfile` trong `--verify`: round‑trip tệp, từ chối JSON lạ, ngôn ngữ không có trong build → `en` | ✅ xong |
| 7 | ~~**Tìm kiếm thông minh hơn trong dock**~~ — đã xong: `SearchSynonyms` theo từng setting, khớp theo token (giao), tên setting là lưới an toàn, phần khớp được tô accent bằng `Run` | `--verify`: `tempo` ra *Fall speed*, `tốc độ` (tiếng Việt) ra cùng hàng, query rỗng trả lại nhãn thường | ✅ xong |

## 2. P1 — giá trị thật cho người làm video và người luyện đàn

| # | Việc | Ghi chú kỹ thuật |
|---|---|---|
| 1 | **Xuất MP4 có âm thanh** | *Đã xong nửa đường tiếng*: công tắc **Record audio** (`PianoVisualSettings.RecordAudio`, mặc định bật) + `Audio/WavWriter.cs` ghi WAV stereo 16-bit cạnh video (`take.wav`, hoặc `audio.wav` trong thư mục khung PNG); `PianoAudioEngine.SetTap` nhận đúng các khối PCM mà engine render (máy không có thiết bị ra âm thanh thì `PumpTapBlock()` render theo đồng hồ ghi hình nên WAV vẫn đúng độ dài), hộp thoại kết thúc in độ dài + dòng ffmpeg ghép MP4, và `VerifyRecordingAudioTrack` kiểm từ header tới đường đi thật. *Còn lại*: **tự ghi MP4** (Media Foundation/MFEncoder hoặc sidecar FFmpeg) thay vì đưa lệnh mux cho người dùng. Hướng đi gốc: Nút REC hiện chỉ ghi AVI phần hình. Hướng đi: (a) `MFEncoder`/Media Foundation qua P/Invoke (không thêm dependency, H.264 + AAC); (b) FFmpeg sidecar (chất lượng cao, nhưng phải tải ~90 MB và chịu giấy phép). PCM của `PianoAudioEngine` đã sẵn trong bộ đệm → ghi song song ra WAV rồi mux. Bắt buộc export **theo khung hình thời gian thực ảo**, không theo đồng hồ tường thuật, nếu không video sẽ tụt frame khi CPU bận |
| 2 | ~~**Chuỗi PNG / nền trong (WebM‑VP9 alpha)**~~ — đã xong phần PNG32: `Video/IFrameRecorder.cs` + `Video/PngSequenceRecorder.cs` ghi `frame-000001.png` (PNG 8-bit RGBA) vào thư mục người dùng chọn, khung lặp ghi đủ bản, `sequence.json` kèm pattern và **dòng lệnh ffmpeg** (`libvpx-vp9` + `yuva420p`) để dựng lại thành WebM alpha; `PianoStage.TransparentBackdrop` bỏ các lớp tô đục (nền, màu nền, ảnh, gradient, vignette) nhưng vẫn vẽ mọi lớp diện mạo bật, và trang Recording có **Format** + **Transparent background**; `VerifyPngSequenceRecorder` kiểm định dạng RGBA, alpha, manifest, từ chối tham số sai và một lần chụp thật. *Còn lại*: WebM/VP9 ghi trực tiếp (cần sidecar ffmpeg) và đường tiếng — cùng lúc với mục #1 |
| 3 | **Thư viện bài** | *Đã có lát cắt v0.5*: hộp thoại Play giữ danh sách **RECENT** (`Library/SongLibrary.cs` + `library.json` trong thư mục cài đặt; mới nhất trước, tối đa 12 bài, khoá theo đường dẫn) lưu số nốt, số track, tempo, điểm chia tay, tốc độ rơi, tempo phát và preset của từng bài, mở lại là khôi phục qua chính các slider. ✅ **xong cả hai phần**: `Library/SongFolderIndex.cs` lập chỉ mục một thư mục (BFS tối đa 3 tầng/500 tệp, đọc bằng chính `MidiReader`/`MusicXmlReader`, cache theo kích thước + giờ sửa trong `library-index.json`), **thẻ** theo từng bài (tối đa 8, cắt khoảng trắng/viết thường), tìm kiếm khớp tiêu đề–tên tệp–thẻ theo từng từ; `Library/SongFolderWatcher.cs` + `DispatcherTimer` giữ danh sách theo đĩa; hộp thoại Play có mục **LIBRARY** với CHOOSE FOLDER/RESCAN, ô tìm kiếm và chip thẻ. Kiểm chứng: `VerifySongFolderLibrary` |
| 4 | **Lịch sử luyện tập** | ✅ đã xong cả ba phần: `Practice/PracticeHistory.cs` ghi **mỗi lượt chơi một dòng JSON** vào `%LOCALAPPDATA%\Keyflow\history\practice.jsonl` (append, đọc lại bỏ qua dòng hỏng), trang **History** trong dock in các lượt gần nhất kèm lượt tốt nhất của bài, **EXPORT HTML** xuất báo cáo (bảng lượt chơi + bảng tổng hợp theo bài, có BOM UTF-8) và **CLEAR HISTORY** xoá. **Ghost + biểu đồ**: mỗi lượt chơi giữ danh sách nốt đã chấm điểm (`PracticePoint`: vị trí trong bài, cao độ, trúng/trượt; trần `GhostCapacity = 256`), app gom ngay lúc chấm điểm và ghi khi dừng transport; `PracticeHistory.Daily(14)` gom theo ngày địa phương (ngày không luyện vẫn là hàng 0) và `GhostPair` lấy lượt tốt nhất + mới nhất của bài đang mở; `Practice/PracticeChart.cs` giữ hình học thuần, trang History vẽ hai cột và hai hàng chấm, báo cáo HTML thêm bảng theo ngày; `VerifyPracticeGhostAndChart` kiểm cả mô hình lẫn hình vẽ trong dock |
| 5 | **Tempo luyện tập tự động** | ✅ đã xong: công tắc **Auto practice tempo** + slider **Misses before slowing down** (1–6, mặc định 3) ở trang Practice (`PracticeAutoTempo`/`PracticeMissThreshold` trong `Stage/PianoVisualSettings.cs`); sai liên tiếp quá ngưỡng → mỗi bước giảm 5% (sàn 50%), đúng liên tiếp 4 nốt → tăng 2% và không vượt 100%; mọi bước đi qua chính `TempoSlider` nên nhãn, metronome và file cài đặt theo đường thường. Kiểm chứng: `VerifyPracticeTempo` |
| 6 | **MusicXML + khuông nhạc** | ✅ **xong cả hai nửa**: `Midi/MusicXmlReader.cs` parse `musicxml`/`xml`/`mxl` (XML thuần + `ZipArchive`, không thêm thư viện) → nốt theo giây, lưới phách theo ô nhịp và tempo từng đoạn, tên bè, hợp âm/`backup`/`forward`, và **chia tay đọc thẳng từ `<staff>`** (khuông 2 = tay trái; hai `part` = hai tay); giá trị đó đi qua slider và được nhớ trong `library.json`; `VerifyMusicXmlImport` kiểm toàn bộ. **Lớp khuông nhạc** (`Stage/SheetLayer.cs`, công tắc **Sheet music** ở thẻ LAYERS) vẽ khuông đôi trên sân khấu cạnh piano roll: nốt đặt theo cao độ viết, khuông theo điểm chia tay, dòng kẻ phụ, vạch nhịp theo lưới phách của bài, vòng sáng theo playhead; `VerifySheetLayer` kiểm hình học lẫn lúc sân khấu vẽ thật. **Hoá biểu** đã có: `Stage/MusicKey.cs` suy ra tông của bài bằng tương quan Krumhansl–Kessler trên thời lượng vang của từng cao độ, `SheetLayer` vẽ hoá biểu ở đầu cả hai khuông, viết cao độ theo tông đó và chỉ đặt dấu hoá khi ô nhịp thật sự cần (dấu giữ đến hết ô nhịp, dấu hoàn lấy lại nốt tự nhiên); `VerifySheetLayer` kiểm cả phần này. *Còn lại*: nối đuôi (beaming), ghép phách và dấu luyến/nghỉ, tức là khắc nhạc đầy đủ thay vì lớp đọc nốt |
| 7 | **Suy luận chia tay** | ✅ đã xong (không cần MusicXML): `Midi/HandSplit.cs` gom hai cụm một chiều theo **thời lượng vang** của từng cao độ, chỉ nhận khi giữa hai tay có **ít nhất 5 semitone trống** và mỗi tay chiếm ≥10% thời lượng, rồi chốt ứng viên gần nốt giữa C4 trong khoảng trống (bài một tay hoặc chồng lấn giữ nguyên lựa chọn của người dùng); công tắc **Infer hand split from the song** ở Notes → Color, kết quả ghi vào `library.json` kèm cờ `SplitInferred` nên mở lại dùng đúng giá trị đã nhớ thay vì đo lại. Học từ thẻ `<staff>` của MusicXML sẽ làm cùng lúc với mục #6 |
| 8 | **Webcam / bàn tay** | *Đã xong nửa camera*: `Camera/MediaFoundation.cs` khai báo tay phần Media Foundation cần dùng (`mfplat.dll` cho `MFStartup`/`MFCreateAttributes`/`MFCreateMediaType`, `mf.dll` cho `MFEnumDeviceSources`/`MFCreateDeviceSource`, `mfreadwrite.dll` cho source reader — **không** phải `mfplat.dll`, đúng lỗi CI đã bắt được) và `Camera/CameraFrameReader.cs` mở camera hoặc tệp video thành source reader **RGB32** rồi đọc khung trên luồng riêng, luôn trả khung top-down BGRA và trả mọi lỗi thành câu; `Camera/CameraOverlay.cs` giữ hình học thuần (vị trí bốn góc, stride âm = hàng bottom-up, mirror, key xanh theo ngưỡng 0–100 so với `ChromaGreen`, hệ số opacity). Trang **Camera & FX** có thẻ **WEBCAM OVERLAY**: công tắc + chọn camera (đọc lại bằng **REFRESH CAMERAS**) + **USE CAMERA**/**CHOOSE VIDEO…** + **Corner**/**Size**/**Opacity**/**Mirror**/**Key tolerance**, kèm dòng trạng thái nói rõ đang mở gì hoặc vì sao không chạy được; cửa sổ bơm khung lên `PianoStage` qua `WriteableBitmap` ở 30 fps và gỡ reader + `MFShutdown()` khi đóng. Mặc định **tắt**, góc mặc định **dưới‑trái** như ghi chú cũ. Kiểm chứng: `VerifyCameraOverlay` + `VerifyCameraOverlayDock` trong `--verify`. *Còn lại*: **theo dõi bàn tay** (nhận diện ngón/độ cao bàn tay kiểu AI) — lớp phủ này chỉ là hình, không suy diễn gì từ khung hình |

## 3. P2 — sân khấu và âm thanh

| Nhóm | Việc |
|---|---|
| **Âm thanh** | `.sf3` (sample nén), modulators + instrument generator đầy đủ, convolution reverb (IR do người dùng nạp), release/abort samples + **sympathetic resonance** (dây cùng bậc rung khi pedal sustain), `damper`/`sostenuto` ảnh hưởng thật tới tail, micro‑tuning (Just, Werckmeister, thang do người dùng đặt), `keyoff` velocity |
| **Kết xuất** | Đường GPU (Direct3D11) cho keyboard + particles, giữ software shader làm fallback và làm ảnh CI tất định; depth of field; bloom HDR thực; motion blur từ velocity buffer; particle compute; camera keyframe (dolly/crane theo khuông nhạc) |
| **Hiệu ứng** | Hoàn thành các mục `EffectStatus.Planned` trong `Stage/Effects/EffectCatalog.cs` theo đúng phase; beat‑synced combo (xung theo lưới phách); audio‑reactive **bằng FFT thật** trên PCM của engine (hiện mới chỉ dựa trên biên độ); mỗi hiệu ứng một "shape" riêng (`falling.glow-trail` và `falling.ribbon-twist` đang chia sẻ hình) |
| **Preset** | ✅ **import/export qua một chuỗi base64** (`Stage/VisualPresetShare.cs`: gzip + base64url sau tiền tố `KEYFLOW-LOOK-1:`, bỏ đường dẫn ảnh nền, giới hạn kích thước, `Clamp()` khi giải mã; nút COPY CODE / APPLY CODE ở trang Style). ✅ **thumbnail render sẵn trong file preset** (`Stage/PresetThumbnail.cs` render chính sân khấu ở 192×112; file preset là phong bì `Version/Thumbnail/Settings`, tệp JSON trần của bản cũ vẫn nạp, preset không ảnh thì danh sách vẽ miniature). ✅ **kho preset cộng đồng** (`presets/` trong repo: mỗi tệp là một preset đầy đủ, `tools/make_presets.py` sinh, `PianoPath.csproj` nhúng, `Stage/CommunityPresets.cs` đọc, nhãn COMMUNITY trong danh sách, không xoá/ghi đè được; `tools/check_sources.py` soi từng khoá còn `VerifyCommunityPresets` soi trên app đang chạy). ✅ **user shell theme** (`Theme/UserShellTheme.cs` + `Ui/ThemeStudioWindow.cs`: năm màu gốc suy ra hai mươi token, lưu trong `<cài đặt>/themes/*.json`, hiện ở cả hai hàng chip, CREATE/EDIT/DELETE ở trang Theme; `VerifyUserShellThemes`). |

## 4. P3 — kỹ thuật và phát hành

| # | Việc | Ghi chú |
|---|---|---|
| 1 | **CI phân lớp** | Job `static` chạy trên `ubuntu-latest` (chỉ `check_sources.py`, ~10 s) rồi tới job Windows build/verify; render **hai** bộ ảnh, một `--lang=en` một `--lang=vi`, để ảnh README luôn là ảnh của đúng build |
| 2 | **Tách bài kiểm ra một project test** | `VerificationSuite` là exe `--verify` — tốt cho CI nhưng không đo được độ phủ và khó chạy lẻ. Chuyển các `Verify*` thuần tính toán (MIDI, SoundFont, shader math, settings JSON) sang `tests/PianoPath.Tests` (xUnit) chạy trên mọi OS, giữ phần WPF trong `--verify` |
| 3 | **Perf gate** | Ngân sách khung hình: p95 < 8 ms ở 1080p preset *Classic Roll*, < 16 ms ở *Cinematic*. `--snapshot` đã in FPS; thêm `--bench=<frames>` xuất JSON để CI so với ngưỡng và in `NOTE` khi vượt |
| 4 | **Giảm cỡ bản tải** | SoundFont 113 MiB là ~1/3 tổng tải. Phương án: build *lite* (không SF2, lần chạy đầu tải có checksum + hiện giấy phép CC BY), build *full* như hiện tại; Inno/ZIP tự chọn |
| 5 | **Cập nhật & đóng gói** | MSIX (`Microsoft.WindowsAppSDK`) cho Store + `winget`; auto‑update đọc `releases/latest` (so `AssemblyInformationalVersion`), tải file ZIP/MSIX và kiểm SHA‑256; ký mã (Azure Trusted Signing) để Windows SmartScreen hết chặn bản không dấu |
| 6 | **Nhánh portability** | Tách `IStageRenderer` (WPF `D3DImage` hoặc Skia) và `IAudioSink` (WASAPI/WaveOut) khỏi `MainWindow`; chỉ khi hai tầng này sạch thì câu chuyện Linux/macOS mới có nghĩa. Không cam kết lịch |

## 5. Những việc đã cân nhắc và **không** làm

| Việc | Lý do không làm |
|---|---|
| Port sang Linux/macOS ở kiến trúc hiện tại | Đổi `MainWindow` sang Avalonia/Uno là viết lại UI; giá trị nhỏ so với việc giữ một sân khấu WPF + WinMM hoàn thiện |
| Hệ plugin / scripting trong app | Bề mặt bảo mật + API không ổn định; preset JSON + Green Screen + OBS đã phủ nhu cầu tuỳ biến |
| Nhận dạng nốt từ audio, transcription AI trong app | Lĩnh vực riêng, khối lượng model lớn; người dùng có MIDI file là đầu vào chuẩn nhất |
| Tài khoản, cloud sync, leaderboard | Dự án MIT, offline, không telemetry — giữ nguyên |
| 3D camera toàn phần (quỹ đạo, mô hình piano 3D) | Chi phí kết xuất và rủi ro lệch thẩm mỹ so với piano roll phẳng + parallax; đường đáng giá hơn là shader keyboard như hiện tại |
| Nhận screenshot của người dùng làm ảnh README | Ảnh chụp lệch ngay lần đổi UI kế tiếp và mang artwork bên thứ ba; tính năng nào cần ảnh thì CI render lấy (xem `docs/LOCALIZATION.md` §10) |
| `.resx` / `ResourceManager` cho bản dịch | Xem `docs/LOCALIZATION.md` §7 — bảng `Dictionary` tĩnh cho đúng thứ dự án cần với ít máy móc hơn |

## 6. Khối lượng ước đoán

| Đợt | Nội dung | Độ lớn |
|---|---|---|
| **v0.5** | P0 cả bảng (1–7) + thư viện bài ở mức "recent + metadata per bài" | 1–2 tuần |
| **v0.6** | Xuất MP4/alpha + audio mux, audio‑reactive FFT, undo/redo | 2–3 tuần |
| **v0.7** | MusicXML + khuông nhạc, suy luận chia tay, lịch sử luyện tập | 3–4 tuần |
| **v0.8** | Preset chia sẻ + kho preset trong repo, user shell theme, perf gate + tách test project | 2–3 tuần |
| **v1.0** | Đường GPU, MSIX + auto‑update + ký mã, tài liệu song ngữ đầy đủ, API cài đặt đóng băng (schema version + migration) | 4–6 tuần |

Một mục chỉ xong khi: code + `check_sources.py`/`--verify` cập nhật + tài liệu (README hoặc `docs/*`)
viết lại + ảnh trong `docs/previews/` phản ánh đúng UI mới (CI lo phần ảnh).
