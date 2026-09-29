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

| # | Việc | Ghi chú kỹ thuật | Cách kiểm chứng |
|---|---|---|---|
| 1 | **Hoàn tất đa ngôn ngữ** — chip phím tắt F1 qua `Loc.T` (năm nhãn mô tả, không phải phím thật), `--verify` chặn thêm `UnknownKeys` thành lỗi thay vì `NOTE` | Xem `docs/LOCALIZATION.md` §9; `VisualPreset` đã tách "tên đã lưu" và nhãn hiển thị qua `DisplayName` | `tools/check_sources.py` + `--verify` |
| 2 | **Song ngữ cho tài liệu** | `README.md` giữ tiếng Việt; thêm `README.en.md` dịch 1‑1, mỗi ảnh dùng lại từ `docs/previews/`. `scan_readme` trong checker quét **cả hai** tệp | `python tools/check_sources.py` |
| 3 | **Ngôn ngữ cho bộ cài** | `installer/Keyflow.iss` khai một tệp `.isl`; thêm `installer\Languages\Vietnamese.isl` rồi chọn ngôn ngữ theo `Language` đã lưu hoặc theo Windows | Build `release.yml` thêm bước ISCC để ảnh installer cũng được kiểm |
| 4 | **Accessibility** | `AutomationProperties.Name` cho mọi nút chỉ có glyph (⟳, A/B, các nút icon), `KeyboardNavigation.TabNavigation` trong dock, font scale không vỡ layout (`--compact` 1080×700 là một case test), màu theo `SystemParameters.HighContrast` qua `ShellThemeManager` | Thêm `VerifyAccessibility` vào `--verify`: mỗi control có `AutomationProperties.Name` hoặc text, và không token màu nào bị thiếu khi bật high contrast |
| 5 | **Undo / redo cho bàn thiết kế** | `PianoVisualSettings.Clone()` đã có; giữ stack 32 ảnh JSON (`ToJson`), `Ctrl+Z` / `Ctrl+Shift+Z`, chạy qua đúng `ApplyVisualSettings` | `--verify`: đổi 3 setting → undo 3 lần → JSON trước/sau bằng nhau |
| 6 | **Hồ sơ cài đặt** (import/export một tệp) | `Keyflow.profile.json` gom `visual-settings.json` + preset đang dùng + `Language` + `ShellTheme`; cho phép kéo‑thả tệp vào cửa sổ | `--verify`: round‑trip tệp hồ sơ, kể cả khi `Language` là ngôn ngữ chưa có trong build → rơi về `en` |
| 7 | **Tìm kiếm thông minh hơn trong dock** | `SettingRow.SearchKeys` đã có; thêm bảng từ đồng nghĩa (mỗi key một dòng `"speed tempo"`), tokenize theo dấu cách, highlight phần khớp bằng `Run` | `--verify`: query `tempo` ra slider *Fall speed*, query `nốt rơi` (tiếng Việt) ra cùng hàng |

## 2. P1 — giá trị thật cho người làm video và người luyện đàn

| # | Việc | Ghi chú kỹ thuật |
|---|---|---|
| 1 | **Xuất MP4 có âm thanh** | Nút REC hiện chỉ ghi AVI phần hình. Hướng đi: (a) `MFEncoder`/Media Foundation qua P/Invoke (không thêm dependency, H.264 + AAC); (b) FFmpeg sidecar (chất lượng cao, nhưng phải tải ~90 MB và chịu giấy phép). PCM của `PianoAudioEngine` đã sẵn trong bộ đệm → ghi song song ra WAV rồi mux. Bắt buộc export **theo khung hình thời gian thực ảo**, không theo đồng hồ tường thuật, nếu không video sẽ tụt frame khi CPU bận |
| 2 | **Chuỗi PNG / nền trong (WebM‑VP9 alpha)** | Để ghép lớp vào Premiere/Resolve/OBS. `RenderTargetBitmap` đã có; chỉ cần encoder alpha (PNG32; VP9 cần sidecar) và cùng đồng hồ frame‑exact ở trên |
| 3 | **Thư viện bài** | Theo dõi một thư mục, chỉ mục các tệp `mid` (tên, tempo, số nốt, số track, duration), tìm kiếm, tag, và metadata per bài (tempo offset, hand split, preset đã dùng) — hiện mọi thứ này chỉ sống trong phiên |
| 4 | **Lịch sử luyện tập** | SQLite (hoặc JSON line) tại `%LOCALAPPDATA%\Keyflow\history\`: accuracy/streak/miss theo bài theo ngày, so sánh hai lần chạy ("ghost"), export báo cáo HTML. Bảng điểm đã đếm đủ hit/miss/streak — thứ còn thiếu chỉ là nơi ghi lại |
| 5 | **Tempo luyện tập tự động** | "Slow‑down curve": sai > N nốt → giảm 5%, đúng liên tiếp → tăng 2% về 100%. Có sẵn `PracticeMode` và lưới phách của metronome nên chỉ cần một vòng điều khiển trên tốc độ phát |
| 6 | **MusicXML + khuông nhạc** | Parse `musicxml` / `mxl` (XML thuần, không cần thư viện ngoài) → khuông `treble`/`bass`, chia tay theo thẻ `<staff>`. Render cạnh piano roll: sân khấu giữ nguyên, thêm lớp sheet. Đây là thứ điểm chia tay cố định hiện tại không thay thế được |
| 7 | **Suy luận chia tay** | Heuristics: nốt dưới median − k cho tay trái, sửa theo khoảng nghỉ và repeat; cho phép học từ thẻ `<staff>` của MusicXML khi có. Lưu kết quả vào metadata bài để không đổi mỗi lần mở |
| 8 | **Webcam / bàn tay** | Lớp phủ góc dưới‑trái. Không cần AI: chroma key từ camera (nền xanh) reuse đúng đường `ChromaGreen` + một `VideoCapture`. Chỉ đáng làm sau khi (1) có pipeline frame‑exact |

## 3. P2 — sân khấu và âm thanh

| Nhóm | Việc |
|---|---|
| **Âm thanh** | `.sf3` (sample nén), modulators + instrument generator đầy đủ, convolution reverb (IR do người dùng nạp), release/abort samples + **sympathetic resonance** (dây cùng bậc rung khi pedal sustain), `damper`/`sostenuto` ảnh hưởng thật tới tail, micro‑tuning (Just, Werckmeister, thang do người dùng đặt), `keyoff` velocity |
| **Kết xuất** | Đường GPU (Direct3D11) cho keyboard + particles, giữ software shader làm fallback và làm ảnh CI tất định; depth of field; bloom HDR thực; motion blur từ velocity buffer; particle compute; camera keyframe (dolly/crane theo khuông nhạc) |
| **Hiệu ứng** | Hoàn thành các mục `EffectStatus.Planned` trong `Stage/Effects/EffectCatalog.cs` theo đúng phase; beat‑synced combo (xung theo lưới phách); audio‑reactive **bằng FFT thật** trên PCM của engine (hiện mới chỉ dựa trên biên độ); mỗi hiệu ứng một "shape" riêng (`falling.glow-trail` và `falling.ribbon-twist` đang chia sẻ hình) |
| **Preset** | Thumbnail render sẵn trong file preset; import/export qua một chuỗi base64 (dễ gửi trong chat); kho preset cộng đồng = một folder `presets/` trong repo với JSON đã được `--verify` kiểm (mọi key tồn tại, mọi giá trị trong khoảng); user shell theme (bảng màu tự tạo, lưu `theme.json`) |

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
