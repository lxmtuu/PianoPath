# Roadmap: AR piano overlay & live play

Tài liệu này là lộ trình **đầy đủ** để đưa Keyflow từ "webcam picture-in-picture" lên **AR piano overlay**: camera
quay đàn thật làm nền, nốt rơi và hiệu ứng được vẽ **đúng lên từng phím của cây đàn thật**, phản ứng tức thì
với MIDI khi chơi trực tiếp (live play), và quay/phát lại được. Nó bổ sung cho `docs/ROADMAP.md` (lộ trình chung);
trạng thái hiện tại nằm ở `README.md`.

> **Trạng thái (2026‑10‑09):** AR‑0, AR‑1, AR‑3.1–3.4 đã làm xong về mã và qua CI Windows (PR #53). Đã vào `main` thêm:
> **khung nhìn** của camera (mặc định / 45° / 90° từ trên xuống, `ar-calibration.json` bản 2 — tệp bản 1 đọc lên thành khung
> nhìn mặc định), **vệt cho phím người chơi tự bấm**, và ba lát cắt **AR‑S1…S3** của bản thiết kế lại ở §14 (`PianoSimulation`,
> `GraphVariable`, `ArElementCatalog` + ba bậc chất lượng + `ArLookGuard`). **AR‑S4a** — ma trận *cảnh→ảnh*
> (`Camera/ArSceneProjection.cs`) — đã vào, và **AR‑S4b** đã nối nó xuống GPU cho chính pass nốt của sân khấu (nhánh AR
> trong `VsNote`, **mặc định tắt**); phần còn của S4b (nhánh `VsSprite`, shape/truncate, công tắc giao diện) là việc kế
> tiếp, và cho tới khi đó lớp phủ vẫn vẽ quad phẳng: `NoteStyle` và các hiệu ứng sân khấu **chưa** có tác dụng trong AR.
> **Chưa phần nào được thử trên webcam hay đàn thật**; lần thử thật đầu tiên (chỉ phần đọc camera) đã lộ ra hai lỗi và một
> khoảng trống, xem §11. AR‑2, 2b, 4, 5, 6 chưa bắt đầu. §12 đối chiếu hai ảnh mẫu của chủ repo với cài đặt hiện có;
> §13 là bảng đối chiếu tiến độ; §14 là bản thiết kế lại và thứ tự thi công AR‑S0…S9.
> Môi trường làm việc **không có .NET SDK, không có webcam, không có GPU thật** nên mọi thay đổi C# được xác nhận bằng CI
> Windows (`build`: `--verify` và ngân sách khung hình trên WARP; `test`: xUnit trên Linux). Các con số "hiện có" ở §1 được
> đối chiếu với mã ở commit `2133a19` (trước khi bắt đầu).
>
> **Cập nhật 2026‑10‑09:** AR‑1.1–1.7 đã xong và CI xanh (PR #55: camera là texture, homography phẳng, 4 điểm
> calibrate theo **khung nhìn**, nốt của bài, phím sáng, và **vệt cho từng phím người chơi tự bấm** ở cả Learn lẫn
> Perform — `ArNotes.BuildLive`, `GpuStageSimulation.LivePresses`). **§14 là bản thiết kế lại toàn bộ hệ thống AR
> "piano live play"** (tham chiếu Keysight), có hiệu lực từ 2026‑10‑09; §4/§5/§13 giữ lại để đối chiếu lịch sử.

## 0. Mục tiêu, và thế nào là "xong"

| | |
|---|---|
| **Người dùng cần gì** | Đặt webcam/điện thoại nhìn vào bàn phím, bấm **Calibrate** một lần, rồi thấy nốt rơi xuống đúng phím thật; chơi MIDI thì phím thật "sáng" ngay; ghi lại hoặc đưa vào OBS/Zoom. |
| **Hai chế độ dùng** | **Learn** — nốt sắp tới rơi vào phím thật, có wait mode, chấm điểm, chỉ ngón. **Perform** — chơi tự do: phím sáng, hạt, vệt nốt vừa chơi bay lên, dùng để quay/stream. |
| **Tiêu chí hoàn thành toàn lộ trình** | (1) Calibrate ≤ 30 giây, sai số căn chỉnh ≤ 1/4 bề rộng một phím trắng ở giữa đàn; (2) từ lúc bấm phím tới lúc phím ảo sáng trên màn hình, **độ trễ do app gây ra** ≤ 1 khung ở 60 FPS (không tính độ trễ camera, cái đó được đo và bù — xem AR‑3); (3) 49/61/76/88 phím đều chạy; (4) chạy 60 FPS ổn định ở 1080p trong `--bench`; (5) mọi bước có lớp kiểm chứng **không cần camera thật** (khung tổng hợp). |

## 1. Hiện trạng (đã có, tái sử dụng được)

| Có sẵn | Ở đâu | Dùng cho AR thế nào |
|---|---|---|
| Đọc camera/video qua Media Foundation, luồng riêng, BGRA top‑down, mirror | `Camera/CameraFrameReader.cs`, `CameraOverlay.CopyFrame` | Nguồn khung hình |
| Key màu xanh lá, vị trí 4 góc, độ mờ | `Camera/CameraOverlay.cs` | Giữ cho chế độ PiP cũ |
| Hand tracking cổ điển (YCbCr, lưới 32×24, đếm ngón, đổi X → phím) | `Camera/HandTracker.cs` | Nền để nâng cấp (AR‑5) |
| Stage GPU D3D11, vòng lặp riêng, HDR + bloom, ghi hình đúng cỡ, MIDI vào thẳng luồng GPU | `Gpu/*` | Nơi ghép camera + nốt (AR‑1) |
| Ghi MP4 H.264 + AAC, PNG alpha, cửa sổ GPU thứ hai (OBS/máy chiếu) | `Video/*`, `Gpu/SwapChainHost.cs` | Xuất kết quả AR |
| Practice: wait mode, chấm điểm, ghost, auto tempo, lịch sử | `Practice/*` | Chế độ Learn |
| Mẫu "tải tuỳ chọn một lần, ghim hash, resume" | downloader Salamander | Cách đưa model ML tuỳ chọn (AR‑5) |

**Những chỗ hiện đang chặn AR** (đã xác nhận trong code):

1. Camera chỉ là lớp WPF `DrawImage` vẽ **trên** khung GPU (`PianoStage.DrawCameraOverlay`), pace bằng `DispatcherTimer` + `Thread.Sleep` ở **30 FPS** cố định (`CameraFrameReader.FrameRate = 30`). AR cần camera là **một texture trong pass GPU**.
2. Hình học phím **đã đúng thật** (52 phím trắng đều nhau, phím đen lệch theo từng nhóm) nhưng **nằm ở ba nơi** — `PianoStage`, `PianoShaderScene`, `GpuStageSimulation` (cứng `52`, `88`, `Lane = width / 88`) — và `FirstPitch = 21`, `KeyCount = 88` là **hằng số `const`** nên chưa thể có đàn 61 phím. `HandTracker.KeyPitch` còn giả định 88 phím chia đều theo chiều ngang (sai so với hình học thật: phím đen và phím trắng không đều). *(Bản thảo đầu của tài liệu này nói "88 làn bằng nhau" cho cả vị trí nốt — không đúng: chỉ cỡ hạt/tia dùng làn đều; vị trí nốt đã dùng `KeyCenterOf`.)*
3. Không có phép biến đổi hình học nào giữa khung camera và sân khấu (chỉ `Place()` chữ nhật theo góc).
4. `SetCurrentMediaType` ép RGB32 (xem `CameraFrameReader` quanh dòng 292); chưa chọn độ phân giải/fps/định dạng gốc (MJPG, NV12) của camera.
5. Không có bù độ trễ camera: phím ảo sáng ngay theo MIDI, còn video của phím thật tới muộn ~60–150 ms → trông lệch.
6. Overlay luôn nằm trên mọi thứ, **bàn tay che phím nhưng hiệu ứng vẫn vẽ đè lên tay**.

## 2. Nguyên tắc thiết kế riêng cho AR

Giữ nguyên **ba luật** của `docs/ROADMAP.md` §0 và thêm bốn điều:

| Nguyên tắc | Hệ quả |
|---|---|
| **Toán thuần, kiểm chứng bằng khung tổng hợp** | Mọi thuật toán (hình học phím, homography, dò bàn phím, đo độ trễ) viết thành hàm thuần như `CameraOverlay`/`HandTracker`, test bằng ảnh do chính repo vẽ. Không test nào cần webcam |
| **Không thêm dependency nặng ở lõi** | Cùng triết lý "không model, không tải về" của hand tracking. OpenCV/ONNX chỉ vào dưới dạng **tuỳ chọn tải một lần** (AR‑5), không bao giờ trong gói mặc định |
| **Căn chỉnh thuộc về máy, không thuộc về preset** | Calibration lưu theo (camera, độ phân giải), giống GPU FPS/VSync: áp preset **không** đổi nó; mã chia sẻ preset (`KEYFLOW-LOOK-1`) **không** mang nó (giống đường dẫn ảnh nền) |
| **MIDI là sự thật, camera là phụ** | Biết phím nào đang bấm là việc của MIDI. Camera chỉ trả lời "phím nằm ở đâu" và "tay ở đâu". Vì vậy nhận diện phím bấm từ hình/âm thanh nằm trong mục *không làm* (§9), nhất quán với `docs/ROADMAP.md` §5 |

## 3. Kiến trúc đề xuất

```
Camera thread ──► CameraFrameReader (chọn mode: MJPG/NV12 720p–1080p @30/60)
                      │  khung + dấu thời gian đồng hồ hệ thống
                      ▼
              CameraFeed (ring buffer 3 khung, lock-free) ──► HandTracker / KeyboardDetector (luồng phân tích riêng)
                      │
                      ▼
   GpuStageFeed ── camera texture (dynamic, upload mỗi khung mới)
                      │
   MIDI callback ──►  GPU render loop:  [1 camera quad] → [2 lớp AR: phím sáng, nốt, hạt, trong không gian bàn phím]
                                        → [3 mặt nạ che tay] → bloom/tonemap → màn hình / tap ghi hình
                      ▲
   KeyboardCalibration (homography 3×3 + KeyboardGeometry) ── uniform cho shader
```

Các khối mới (mỗi khối một tệp, hàm thuần, có test):

| Khối | Tệp dự kiến | Việc |
|---|---|---|
| `KeyboardLayout` | `Stage/KeyboardLayout.cs` | Dải phím (49/61/76/88 → `FirstPitch`, `KeyCount`), **hình học thật**: pitch → (x tâm, bề rộng, là phím đen/trắng) theo đơn vị bàn phím chuẩn hoá 0‑1 |
| `Homography` | `Camera/Homography.cs` | Giải 4 điểm → ma trận 3×3 (DLT, 8 ẩn), nghịch đảo, áp điểm; thêm tinh chỉnh bình phương tối thiểu khi > 4 điểm |
| `ArCalibration` | `Camera/ArCalibration.cs` | Dữ liệu: 4 góc (hoặc 6 điểm), layout, camera id + khung nhìn + cỡ khung, ngày; đọc/ghi `ar-calibration.json` bản 2 (bỏ qua tệp hỏng như `PracticeHistory`); tệp bản 1 đọc lên thành khung nhìn mặc định |
| `KeyboardDetector` | `Camera/KeyboardDetector.cs` | Dò bàn phím tự động bằng profile 1 chiều (AR‑2) |
| `LatencyProbe` | `Camera/LatencyProbe.cs` | Ước lượng độ trễ camera bằng tương quan chéo năng lượng chuyển động ↔ thời điểm Note On (AR‑3) |
| `HandMask` | `Camera/HandMask.cs` | Mặt nạ da độ phân giải đầy đủ để che hiệu ứng (AR‑4) |

## 4. Các giai đoạn

> ⚠ **Từ 2026‑10‑09, bản có hiệu lực là §14** (thiết kế lại theo tham chiếu Keysight, gồm ánh xạ mã cũ ↔ mới ở
> §14.10). Các mục AR‑0…AR‑6 dưới đây giữ nguyên để tra cứu lịch sử và đối chiếu tiến độ.

Ký hiệu: **Phụ thuộc** = phải xong trước. **Kiểm chứng** = lớp bảo đảm theo luật §0 của roadmap chung. Ước lượng theo kiểu của `docs/ROADMAP.md` §6 (một người, đã quen repo).

### AR‑0 — Nền móng (không thấy được gì mới, nhưng mở khoá mọi thứ)

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 0.1 | **`KeyboardLayout` thay hằng số** — ✅ **xong (a) và (b)** | **(a) ✅ xong:** `Stage/KeyboardLayout.cs` — một nơi duy nhất cho hình học (khoảng phím 88/76/61/49, `CenterX`, `Span`, `WhitesBelow`, `PitchAt` theo toạ độ, `Fitting` đoán cỡ đàn từ khoảng nốt đã chơi); thuần, không WPF, được **link vào project test**. `PianoStage` đã lấy `IsBlack`, `BlackKeyOffset`, `WhitesBelow`, `KeyCenters` từ `KeyboardLayout.Standard88` bằng đúng công thức cũ. **(b) ✅ xong (CI Windows xanh, gồm `--verify` và bộ ảnh preset):** cho `PianoShaderScene` và `GpuStageSimulation` (`GpuSceneLayout.Lane`, các vòng `FirstPitch..KeyCount`, `/ 52f`) dùng layout đang chọn thay vì hằng số, đổi `PianoStage.FirstPitch/KeyCount` từ `const` thành giá trị của layout, và sửa `HandTracker.KeyPitch` sang `PitchAt` | (a): `KeyboardLayoutTests` (xUnit, mọi máy) — 88 phím trùng **từng tâm phím** với công thức cũ viết lại trong test, 4 cỡ đều tile kín chiều rộng, phím đen nằm giữa hai phím trắng kề, `PitchAt` quay vòng đúng cho mọi phím; và `VerifyKeyboardLayout` trong `--verify` so layout với `PianoStage`/`PianoShaderScene`. (b): **ảnh stage mặc định không đổi** so với trước (so pixel, CI render) |
| 0.2 | **Cài đặt layout** — ✅ **xong** (thuộc tính tên `KeyboardSize`; `CopyFrom` bỏ qua nó nên preset không đổi cỡ đàn; gợi ý cỡ vừa hiện dưới dòng chọn, tính từ nốt đã chơi trên đàn thật, không tự đổi) | `PianoVisualSettings.KeyboardLayout` ("88 / 76 / 61 / 49"), dòng ở trang Keyboard, có đúng một chỗ tiêu thụ (luật "không chức năng chết"), cập nhật `docs/SETTINGS-WIRING-AUDIT.md`. Layout nên **tự đoán từ MIDI** (nốt thấp nhất/cao nhất đã nghe) và gợi ý, không tự đổi | `--verify` + `check_sources.py` |
| 0.3 | **Camera mode selection** — ✅ **xong; lần thử webcam thật đầu tiên (2026‑10‑05) tìm ra và đã sửa 3 lỗi của đường đọc camera, xem §11** (luật chọn là `CameraModes.Choose`, thuần, có test: gần 720p nhất → tốc độ cao nhất tới 60 → gần 16:9 → MJPG>NV12>YUY2; định dạng nén lạ như H264 để cuối). Lớp Windows `CameraFrameReader.Modes.cs` liệt kê native type, chọn rồi ép camera sang mode đó trước khi đổi RGB32; mọi lỗi lùi về mode mặc định kèm câu giải thích trên dòng trạng thái | Liệt kê `IMFMediaType` gốc của thiết bị (độ phân giải × fps × định dạng); chọn 1280×720@60 MJPG nếu có, ngược lại 30 fps; để Media Foundation giải mã sang RGB32 (đã có) hoặc NV12. Hiển thị mode thật trong status ("720p · 60 fps · MJPG") | `VerifyCameraOverlay` mở rộng: mode do video file báo vẫn đúng; câu báo lỗi khi thiết bị không có mode yêu cầu |
| 0.4 | **Bỏ trần 30 fps cứng** — ✅ **xong; luồng đọc nay tự mở/đọc/đóng reader trên một luồng (§11); chưa đo 60 fps trên webcam thật** (camera trực tiếp đọc theo nhịp riêng, không `Thread.Sleep`; tệp video được giữ theo dấu thời gian của chính nó bằng `FramePacer`; `CameraFrameRing` 3 ô thay buffer dùng chung — sửa luôn lỗi vẽ đọc buffer đang bị ghi; mỗi khung có `LastFrameTicks` (QPC) và `LastSampleTime` cho AR‑3; bộ theo dõi bàn tay chạy tối đa 30 lần/giây) | `CameraLoop` đọc theo nhịp thật của camera (không `Thread.Sleep`), khung mới ghi dấu thời gian (`QueryPerformanceCounter` hoặc `MFSampleExtension`/sample time); ring buffer 3 khung thay cho một buffer có lock | `VerifyCameraOverlay`: đọc 30 khung từ AVI do app ghi, dấu thời gian tăng đều |
| 0.5 | **Khung tổng hợp để test** — ✅ **xong (Python + C#)** | `tools/make_ar_sample.py` vẽ bàn phím từ trên xuống bằng đúng hình học `KeyboardLayout` qua một homography biết trước (phối cảnh, ánh sáng tối góc, nhiễu cảm biến), đủ 4 cỡ (`--keys 88\|76\|61\|49`), deterministic; kèm `ar-keyboard-topdown.json` (4 góc, homography, tâm mặt trên từng phím) làm ground truth. Không dùng ảnh bên thứ ba. **Bộ vẽ C#:** `tests/PianoPath.Tests/SyntheticKeyboardFrame.cs` vẽ lại đúng khung đó cho cả bốn cỡ đàn và mọi góc camera, dùng thẳng `KeyboardLayout`; `SyntheticKeyboardFrameTests` buộc nó khớp PNG Python từng điểm ảnh, `HomographyTests` buộc `Camera/Homography.cs` khớp ground truth (`homography`, 88 `keyTopCentres`). Đã có `Homography` thuần (DLT 4 điểm, nghịch đảo, hợp, từ chối 4 điểm thẳng hàng) dùng cho AR‑1.3 | `scan_generated_assets` của checker (kích thước, 8‑bit RGB, JSON mô tả đúng ảnh); `make_ar_sample.py --check` tự đối chiếu số với layout |
| 0.6 | **Tìm, gọi tên và chọn camera** — ✅ **xong, CI xanh; chưa thử trên máy có nhiều camera** (không nằm trong kế hoạch gốc; sinh ra từ lần thử webcam thật, §11). `CameraRanking` (camera màu trước, ảo sau, hồng ngoại cuối — "camera đầu tiên" trên laptop Windows Hello thường là camera hồng ngoại), `CameraNames` (tên thật, không bao giờ "Camera 1": tên MF → bản ghi thiết bị trong registry → mã VID/PID), `CameraListing` (các dòng + so sánh hai lần đọc). `OpenDevice` thử lần lượt các camera khác khi camera được chọn không mở được. Danh sách `CAMERAS ON THIS MACHINE` trên trang Camera & FX. Danh sách tự đọc lại khi Windows báo đổi thiết bị, mỗi 3 giây (camera ảo như OBS không gây thông báo nào), và khi cửa sổ được đưa lên trước; **bị giữ lại** khi đang ghi, đang đo độ trễ hoặc đang đánh dấu góc phím | Camera là **của máy** nên `CopyFrom` bỏ qua `CameraSourceLink`/`CameraVideoPath` và mã chia sẻ/preset lưu xoá chúng; calibration khoá theo `camera.Link` + **khung nhìn**, nên camera khác — hay cùng camera ở góc khác — ⇒ cần đánh dấu lại phím (trạng thái `NotCalibrated`, không phải lỗi) | `CameraRankingTests`, `CameraNamesTests`, `CameraListingTests`; `check_sources.py` ghim GUID `ReaderAdvancedKey`; `VerifyKeyboardLayout` (preset giữ camera, mã chia sẻ không mang camera) |

**Phụ thuộc:** không. **Ước lượng:** 1–1,5 tuần. **Rủi ro:** 0.1 chạm nhiều file (stage, GPU, sheet) → làm **một PR riêng, không kèm tính năng**, và dùng ảnh README do CI render làm bằng chứng không hồi quy.

### AR‑1 — AR MVP: camera làm nền, nốt rơi lên đàn thật (căn chỉnh thủ công)

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 1.1 | **Camera là texture GPU** — ✅ **xong (chưa thử với camera thật)** (`GpuCameraTexture`, `GpuStageFeed.Camera`, `ArView` ánh xạ ảnh↔màn hình; ảnh camera được ghép **dưới ánh sáng** ở bước composite nên không bị tone‑map; `VerifyArCameraLayer` đọc lại điểm ảnh trên WARP: fit/fill/mirror/độ mờ/bỏ lớp/chroma). **Nối vào app xong** (xem 1.5: engine là người lấy khung duy nhất khi AR bật; camera đọc không lật, lật lúc vẽ). Hai hướng chiều ảnh: đã xong bằng ô "Đàn trong ảnh" (xem 1.5); camera hẳn ở phía người chơi là AR‑2b | `GpuStageFeed` nhận khung mới (con trỏ ring buffer); `GpuStageRenderer` giữ `ID3D11Texture2D` dynamic (`D3D11_USAGE_DYNAMIC`, `Map(WRITE_DISCARD)`), vẽ **một quad toàn màn hình trước mọi lớp**, có "cover" hoặc "fit". Chuyển nền sân khấu về trong suốt khi AR bật (đã có `TransparentBackdrop`, tái dùng). 1080p BGRA ≈ 8 MB/khung → ~500 MB/s ở 60 fps, chấp nhận được; NV12 + shader đổi màu là tối ưu về sau | `VerifyGpuStage`: khung camera tổng hợp màu đỏ → pixel nền của khung GPU đọc lại là đỏ; tắt AR → như cũ |
| 1.2 | **Wizard Calibrate 4 điểm** — ✅ **xong về mã; chưa ai nhìn cửa sổ bằng ảnh camera thật** (`CalibrationQuad.Check/Order`, `ArCalibration`, `CalibrationEditor`, `CalibrationWizard`, `CalibrationWindow`, chi tiết ở 1.5) | Người dùng bấm 4 góc **vùng phím** trên hình camera (trái‑trên, phải‑trên, phải‑dưới, trái‑dưới của dải từ phím thấp nhất đến cao nhất, thấy ở mép trên phím trắng). Kéo được từng điểm, phím mũi tên chỉnh 1 px, phóng to vòng tròn kính lúp quanh điểm. Đồ hoạ **lưới phím xem trước** vẽ chồng ngay khi kéo để mắt người tự chỉnh | `VerifyArCalibration`: nhập 4 điểm → `Homography` → chiếu tâm từng phím về ảnh → khớp tới 0,5 px với cách vẽ ảnh tổng hợp |
| 1.3 | **`Homography` + áp vào GPU** — ✅ **xong phần nốt** (khác kế hoạch: homography áp **trên CPU** trong `ArProjection`, chính xác kiểu double, có cắt ở đường chân trời; GPU nhận 4 góc + trọng số w mỗi góc và nội suy đúng phối cảnh. `ArNotes` đặt nốt theo thời gian: mép đầu chạm mép sau phím ở giờ bắt đầu, trượt qua phím khi đang kêu; `VerifyArCameraLayer` đọc lại điểm ảnh trên WARP). Đa giác phím sáng là 1.4 | Ma trận vào constant buffer; vertex shader của lớp phím/nốt biến đổi toạ độ "không gian bàn phím" (x theo `KeyboardLayout`, y 0 = mép trên phím, y > 0 hướng lên) sang toạ độ màn hình. Mặc định chế độ **image‑space**: nốt rơi theo phương **thẳng đứng của ảnh** từ mép trên bàn phím lên, x ánh xạ qua homography chỉ ở hàng phím (nốt hơi hội tụ theo phối cảnh). Chế độ **plane** đưa sang AR‑6 | `--snapshot` ra `docs/previews/*/ar-overlay.png` từ khung tổng hợp (CI render, theo luật ảnh README) |
| 1.4 | **Phím thật sáng khi chơi** — ✅ **xong** (khác kế hoạch: không multiply/additive riêng mà **phủ màu có độ trong suốt** lên phím trong ảnh, nên phím trắng ngả màu còn phím tối sáng lên, một cách vẽ cho cả hai; độ sáng lấy từ glow của mô phỏng sân khấu nên lên/tắt mượt như phím sân khấu. `ArKeys` chia mỗi phím thành phần nhìn thấy từ trên: phím trắng = mặt trước + thân giữa hai phím đen, phím đen = một hình chữ nhật; các hình của cả bàn phím lát kín vùng phím đúng một lần, kiểm trên 4 cỡ đàn; **và từ 2026‑10‑08** mỗi phím người chơi bấm còn kéo theo một **vệt** của chính phím đó — `ArNotes.BuildLive` đặt vệt từ mép sau phím chạy ra theo trục `v` của bàn phím, dài dần khi giữ (tối đa bằng Lookahead), mờ dần rồi tắt sau khi nhả; vệt là dấu của tay người chơi nên có ở **cả Learn và Perform**, và `GpuStageSimulation.LivePresses` là nguồn dữ liệu) | Với mỗi phím đang giữ (đã có `GpuStageFeed` held keys), vẽ **đa giác phím** (qua `KeyboardLayout` + homography) với glow cộng (additive) cho phím tối, **nhân (multiply) + viền sáng** cho phím trắng để không cháy sáng; phím đen vẽ sau phím trắng | `VerifyGpuStage`: giữ phím C4 → vùng pixel quanh toạ độ chiếu của C4 sáng hơn phím kề; phím kề không đổi |
| 1.5 | **Cài đặt & trang AR** — ✅ **xong về mã (mô hình, công tắc, thẻ, cửa sổ căn chỉnh); chưa thử trên máy thật** (5 giá trị `ArEnabled/ArMode/ArOpacity/ArLookahead/ArKeyLight` trong `PianoVisualSettings`, `ArOptions` thuần đổi chúng thành độ mờ, kiểu nốt, độ sáng phím; kẹp giá trị, NaN → mặc định; mode lạ → Learn; **preset/mã chia sẻ không đổi chúng**, nhưng mã chia sẻ vẫn mang chúng như mọi cài đặt khác. Perform ở bước này không vẽ nốt sắp tới nhưng **có vệt phẳng cho mỗi phím vừa bấm** — lát cắt đầu của AR‑6.2; hạt/flash theo vận tốc vẫn là AR‑6.2). **Công tắc đã nối vào luồng vẽ** (`ArPlan` quyết định theo công tắc, engine GPU, camera mở, calibration, nền chroma: không có calibration thì camera là nền và stage như cũ; có calibration thì engine vẽ nốt + phím lên camera, nốt/phím/hiệu ứng ở vị trí phím ảo của stage tự lùi (`GpuLook.From(overCamera)`), engine là người đọc duy nhất của ring camera, camera đọc ảnh không lật; theo dõi bàn tay tạm dừng khi AR dùng camera — chung sống với AR là AR‑5). **Thẻ AR OVERLAY trong Camera & FX xong** (công tắc, Mode, độ đặc, nhìn trước, độ ngả màu phím, RESET CALIBRATION, dòng trạng thái theo `ArPlan`; chuỗi tiếng Việt; test qua dock). **Cửa sổ căn chỉnh + nút CALIBRATE xong** (`CalibrationWizard` thuần: bấm/kéo/mũi tên/Tab/Undo, kính lúp 36 px, lưới phím xem trước, Done khoá khi tứ giác sai; `CalibrationWindow` vẽ ảnh tĩnh của camera đúng chiều như stage — lật khi Mirror — kèm tên phím thấp nhất/cao nhất để thấy lưới chạy ngược; ảnh lấy bằng `CameraFrameRing.TryPeek` không làm phiền engine, luôn là ảnh camera giao, chưa lật; không lưu khi huỷ hoặc khi camera đổi nguồn/kích thước lúc cửa sổ đang mở; test logic + `VerifyCalibrationWindow` trên cửa sổ thật, vẽ lại và đọc điểm ảnh). **Đường ảnh → calibration đã lưu được kiểm** (`CalibrationFromPictureTests`: ảnh bàn phím qua ring như luồng camera, wizard bấm lệch 2 px rồi kéo về góc thật, ở 3 cỡ cửa sổ, lật/không lật, 4 cỡ đàn; lưới xem trước của cả 88/76/61/49 phím phải nằm đúng màu phím trên điểm ảnh; lưu rồi phiên mới tìm lại đúng, khoá khác cỡ đàn thì không; ảnh mẫu `docs/samples` khớp 88 tâm phím của JSON trong 0,5 px; góc lệch 14×22 px bị lưới báo sai). **Ảnh lật ✅:** cửa sổ calibrate có ô **“Đàn trong ảnh”** (4 lựa chọn: âm trầm trái/phải × mép xa trên/dưới, tính theo ảnh *đang hiển thị*; mặc định = đàn thẳng trong ảnh camera giao, và mở lại đúng lựa chọn đã dùng từ calibration đã lưu). Góc vẫn lưu theo thứ tự back-left, back-right, front-right, front-left nên định dạng lưu không đổi; `CalibrationQuad.CheckAnyWinding` nhận cả chiều ngược (camera phía bên kia đàn), còn `Check` nghiêm vẫn dùng cho editor. Test: 4 cỡ đàn × 8 cách ảnh bị lật/Mirror, kiểm trên điểm ảnh; chọn sai thì lưới báo sai, chọn đúng thì lưới đúng ngay không cần bấm lại. Dòng trạng thái của thẻ AR khi đang chạy có thêm “app giữ mỗi khung hình N ms” (theo bước 5 ms, từ `CameraAgeMeter`); chọn xong ô “Đàn trong ảnh” thì phím mũi tên/Tab trả về cho các góc. **Còn:** thử camera thật. — 1.7 Trang mới **AR** trong dock (hoặc thẻ trong Camera & FX): công tắc **AR overlay**, **Mode** (Learn / Perform), **Calibrate**, **Reset calibration**, opacity lớp AR, nốt chỉ bắt đầu hiện cách phím bao nhiêu giây (`Lookahead`), Mirror (đã có). Mọi dòng có tooltip + khoá chuỗi tiếng Việt (`tools/add_string.py`) | `check_sources.py` (khoá chuỗi), `SETTINGS-WIRING-AUDIT.md`, `VerifyDockAccessibility` |
| 1.6 | **Lưu calibration** — ✅ **xong phần lưu và nối** (`ArCalibrationStore` + `ArCalibrationSession`: cửa sổ báo camera nào đang mở, cỡ ảnh, cỡ đàn trong cài đặt; phiên tìm calibration đã lưu, báo `Ready` / `NotCalibrated` / `Outdated` (cùng camera nhưng đổi độ phân giải hoặc cỡ đàn) / `NoSource`; lưu, xoá, ảnh chụp bất biến cho luồng vẽ đọc; khoá theo ảnh gốc của camera, **không** theo Mirror). Nút CALIBRATE và RESET CALIBRATION ở thẻ AR (xem 1.5) | `ar-calibration.json` trong thư mục cài đặt, khoá `(camera.Link, khung nhìn, width×height, số phím)` — thêm số phím so với kế hoạch đầu vì vùng phím phụ thuộc cỡ đàn, và thêm **khung nhìn** (mặc định / 45° / 90° từ trên xuống) vì bốn góc đánh dấu ở một góc camera không còn nghĩa gì khi quay camera sang góc khác; khung nhìn chưa đánh dấu là `NotCalibrated` chứ không dùng lại vùng phím của góc cũ. Mở lại camera cũ → tự nạp; đổi độ phân giải → báo cần căn lại. Preset/mã chia sẻ không mang | `VerifyArCalibration`: round‑trip, tệp hỏng bị bỏ qua, preset apply không đổi calibration, mã chia sẻ không chứa nó |
| 1.7 | **Ghi hình AR** — ✅ **đã kiểm** (`VerifyArRecording`, WARP trên CI: 3 bản ghi — đúng khổ sân khấu, khổ vuông có viền, ảnh Mirror — đọc lại từ khung do luồng render tự ghi: có ảnh camera, phím đang giữ được tô đúng chỗ trong từng khung, chỗ khác không đổi. Thêm `CameraAgeMeter`: đo ảnh camera nằm trong app bao lâu từ lúc reader nhận tới lúc được vẽ lần đầu, cho vòng render và cho từng bản ghi; **chưa** gồm độ trễ của chính camera/USB — việc của AR‑3) | Vì camera nay nằm trong GPU pass, `GpuRecordingTap` (đúng cỡ, đúng fps) ghi luôn nền camera + lớp AR vào AVI/MP4. Tiếng vẫn do `IAudioTrack` | `VerifyMp4Recorder`/tap: khung ghi có pixel nền camera |

**Phụ thuộc:** AR‑0.1, 0.3. **Ước lượng:** 2–3 tuần. **Cột mốc:** quay được video "nốt rơi lên đàn thật" bằng hai bước (Calibrate → REC). **Rủi ro chính:** vertex shader phải xử lý được homography khi 4 điểm gần suy biến — hàm `Homography` trả `null` + câu báo lỗi, wizard không cho "Done" khi tứ giác lồi sai/diện tích quá nhỏ.

### AR‑2 — Calibrate tự động và giữ căn chỉnh

Mục tiêu: người mới không phải click 4 điểm; và nếu cây đàn/camera bị chạm vào thì app **biết** thay vì lệch trong im lặng.

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 2.1 | **Dò mép bàn phím** | Cổ điển, không model: (a) ước lượng hướng chính bằng gradient trên dải sáng‑tối (phím trắng sáng, khe & phím đen tối); (b) lấy profile cường độ dọc theo đường qua phím đen → **nhịp 2‑3 phím đen** (C♯D♯ | F♯G♯A♯) cho vị trí quãng tám; (c) 2 đường biên trên/dưới bằng gradient mạnh nhất; (d) đề xuất 4 góc, **người dùng xác nhận/kéo chỉnh** (không bao giờ im lặng tin mù) | `VerifyKeyboardDetector`: trên ảnh tổng hợp (nhiều góc nghiêng ±25°, xoay ±10°, sáng/tối, nhiễu), góc dò được lệch ≤ 1% chiều rộng ảnh; ảnh không có đàn trả "không thấy" thay vì đoán |
| 2.2 | **Đăng ký cao độ** | Khi thấy được ≥ 2 nhóm phím đen, ghép mẫu với `KeyboardLayout` để biết đầu trái là nốt nào; nếu chỉ thấy một phần (đàn 61 phím cắt khung) → hỏi người dùng "phím đầu tiên là C?" hoặc **chơi một phím để chốt** (MIDI cho biết pitch, vị trí bấm trong ảnh cho biết phím) | test: lệch một quãng tám bị phát hiện bằng MIDI note |
| 2.3 | **Tinh chỉnh bằng MIDI** | "Chơi C2, C4, C6" khi tay đã ở trên phím: dùng 3 điểm tay (HandTracker) làm ràng buộc thêm cho homography → bình phương tối thiểu | test với tay tổng hợp |
| 2.4 | **Giám sát trôi (drift watch)** | Mỗi giây so khớp lại **mẫu phím đen dự đoán** với khung hiện tại trên dải hẹp; sai lệch trung bình > 0,5 phím trắng hoặc tương quan thấp → huy hiệu "Căn chỉnh có thể đã lệch · Calibrate lại". Không tự sửa khi đang chơi | test: dịch ảnh tổng hợp 2% → báo; nhiễu nhỏ → không báo |
| 2.5 | **Nhiều camera/góc đặt** | Lưu **nhiều hồ sơ** (ví dụ "trên đầu", "trước mặt", "điện thoại"), chọn theo camera đang mở | round‑trip |

**Phụ thuộc:** AR‑1. **Ước lượng:** 2–3 tuần. **Rủi ro:** góc nhìn từ phía người chơi nghiêng mạnh làm phím đen bị phím trắng che → vẫn cần đường lùi thủ công 4 điểm (AR‑1.2 giữ nguyên, 2.x chỉ là gợi ý).

### AR‑2b — Camera từ phía người chơi

Làm **sau khi** góc từ trên xuống đã ổn định (quyết định §8.4). Cùng `Homography` và wizard 4 điểm của AR‑1, nhưng bàn phím bị
nhìn nghiêng nên phát sinh các vấn đề riêng:

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 2b.1 | **Ảnh mẫu từ phía người chơi** | `make_ar_sample.py --view player`: cùng bàn phím nhìn nghiêng 25–40°, phím đen che một phần phím trắng, đầu xa nhỏ hơn đầu gần | `check_sources` (ảnh + JSON) |
| 2b.2 | **Hình học phím theo độ cao** | Mép trên phím đen cao hơn mép phím trắng: vẽ glow phím đen ở **mặt trên của nó**, không ở mặt phẳng phím trắng; thêm tham số `Key height` (mm) vào calibration | test chiếu: tâm phím đen đúng trên ảnh nghiêng |
| 2b.3 | **Dò tự động cho góc nghiêng** | Profile 1 chiều dọc theo mép trước phím trắng (vẫn thấy rõ) thay cho đường qua phím đen (bị che) | `VerifyKeyboardDetector` thêm bộ ảnh nghiêng |
| 2b.4 | **Nốt rơi theo hướng nhìn** | Nốt rơi theo phương thẳng đứng của ảnh nhưng đáy nốt bám mép sau phím; có chế độ plane (AR‑6.3) nếu cần | ảnh `--snapshot` |

**Phụ thuộc:** AR‑1, AR‑2.1. **Ước lượng:** 1,5–2 tuần.

### AR‑3 — Đồng bộ thời gian (cái làm AR "cảm giác đúng")

Camera luôn trễ ~60–150 ms so với thực, trong khi hiệu ứng theo MIDI thì ngay lập tức. Phím ảo sáng **trước** khi ngón tay trên video chạm phím. Phải bù.

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 3.1 | **Visual sync offset** — ✅ **xong, CI xanh** (`ArDelay` 0–300 ms, mặc định 0, slider “Camera delay” trên thẻ AR, không vào preset. Hiểu như sau: màn hình là cái nhìn *trễ d* về thế giới, nên cả lớp AR được vẽ theo thế giới của d trước — phím bấm sáng muộn d (`ArKeyHistory`: lịch sử độ sáng phím của vòng render, hỏi tại `now − d`, nội suy), nốt tới muộn d (vị trí bài trừ d) — để nốt chạm phím đúng lúc tay trong video chạm phím nếu người chơi bấm đúng nhịp tiếng. Tiếng không bị trễ. Cần cân nhắc: người tập bấm theo *màn hình* sẽ thấy nốt chạm phím muộn d so với tiếng — mặc định 0 để không ép ai) | Slider **Camera delay compensation** (0–300 ms, mặc định 0). Cách bù: **trì hoãn phần hiệu ứng theo MIDI** (phím sáng, hạt) cho khớp video — **không** trễ âm thanh (tiếng phải ra ngay). Nốt rơi trong Learn mode tính theo vị trí bài nên dịch trước theo offset (nốt chạm phím khi *tay trên video* chạm phím) | `VerifyGpuStage`: offset 100 ms → hit xuất hiện đúng 100 ms sau trên đồng hồ GPU; tiếng không bị trễ |
| 3.2 | **Đo tự động** (`LatencyProbe`) — ✅ **xong, CI xanh** (còn thử với webcam + đàn thật). Gồm: `LatencyProbe` thuần (gộp hợp âm, trung bình năng lượng theo trễ 0–400 ms, tin khi thắng ≥99% của 100 lần dịch vòng, hai nửa nốt chẵn/lẻ lệch ≤60 ms, không có đỉnh thứ hai ≥60%); `MotionEnergy`/`LatencyRecorder` (sai khác độ sáng trong hộp quanh 4 góc phím, lấy mẫu thưa); `CameraFrameRing.FrameTap` (đo trên luồng đọc camera, không giành khung với luồng vẽ); `LatencyMeasurement` (20 lần bấm + 0,6 s chờ hình về, tối đa 90 s; gợi ý = trễ đo + tuổi khung trong app, cắt ở 300 ms); nút **MEASURE DELAY** trên thẻ AR (giờ bấm lấy ở luồng MIDI, đặt thanh Camera delay, báo lý do khi thất bại); `VerifyArLatency` chạy đồng hồ thật + luồng thật. Cần đàn MIDI, camera sống và AR đã đánh dấu phím | Người chơi bấm ~20 nốt bất kỳ. App tính **năng lượng chuyển động** (|khung − khung trước|) trong dải phím theo thời gian và **tương quan chéo** với chuỗi thời điểm Note On → độ trễ tốt nhất ∈ [0, 400 ms]. Báo độ tin cậy; thấp thì đề nghị thử lại | test tổng hợp: chuỗi Note On ngẫu nhiên + chuyển động trễ 83 ms + nhiễu → ước lượng ±1 khung |
| 3.3 | **Đo độ trễ của chính app** — ✅ **xong, CI xanh** (run 37308483738; trên WARP: cảnh `ar` p95 153,6 ms, 107 hình camera, app giữ mỗi hình 144 ms trung bình — bị chi phối bởi thời gian một khung của WARP, nên số này chỉ có nghĩa trên card thật; chưa có số đo card thật) (`--bench-ar` thêm cảnh `ar` 1080p: look mặc định, bỏ nốt/phím của sân khấu, camera tổng hợp 1280×720 @60 fps qua lớp camera + nốt rơi + phím sáng; JSON có mục `camera` {số hình, tuổi trung bình/tệ nhất}; CI chạy `--bench=90 --bench-ar`; cảnh mới so với baseline cũ ⇒ "Unknown", không phải lỗi). | `--bench-ar`: nối vào `--bench` hiện có, thêm nguồn camera tổng hợp 60 fps; báo p95 thời gian khung với pass camera + AR; perf gate trong CI giữ giới hạn như đã có | `--bench` JSON có mục `ar` |
| 3.4 | **Ghi hình giữ đồng bộ** — ✅ **xong, CI xanh** (run 37311491748). Hiểu như sau: mỗi khung của bản ghi đã là một bức ảnh nhất quán của thế giới cách đây d (camera + lớp AR vẽ theo thế giới đó, 3.1), nên **không dịch hình** mà **dịch tiếng**: khung không thể có dấu thời gian âm, vì vậy track âm thanh bắt đầu muộn d (d = `Camera delay`, chỉ khi lớp camera nằm trong bản ghi; tiếng chèn d giây im lặng ở đầu, ở cả MP4 lẫn WAV). `RecordingSync` thuần: `LeadInFrames` (làm tròn khung mẫu, tối đa 300 ms), `Begin`, `SilenceDue` (đồng hồ bù cho máy không có thiết bị âm thanh, cộng cả đoạn chờ, không trôi), `Skew`. Test: tiếng bấm đầu tiên nằm đúng ở khung mẫu `d·44100` của WAV; mô hình bấm phím ngẫu nhiên ở 24/30/60 fps cho độ lệch tiếng–hình ∈ [−1 mẫu, 1 khung + 1 mẫu]; mô phỏng đồng hồ có jitter không trôi quá 1 mẫu. Không đo được bằng cách giải mã MP4 trong CI (encoder là mã native, `VerifyMp4Take` chạy ở tiến trình con và có thể SKIP), nên độ lệch được chứng minh ở mức dấu thời gian. Giới hạn: d lấy lúc **bắt đầu ghi** (đổi slider giữa chừng không áp dụng); tiếng dài hơn hình đúng d giây ở cuối tệp | Khung camera ghi theo **dấu thời gian đã bù**, không theo thời điểm render; video + tiếng lệch ≤ 1 khung trong tệp MP4 | `VerifyMp4Recorder` mốc thời gian |

**Phụ thuộc:** AR‑1 (không cần AR‑2). **Ước lượng:** 1,5–2 tuần.

### AR‑4 — Độ chân thực: che tay, ánh sáng, bóng

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 4.1 | **Che hiệu ứng bằng bàn tay** | `HandMask` ở độ phân giải ~1/4 khung (YCbCr như hiện tại, + làm mịn & giãn biên 2 px, theo thời gian EMA để khỏi nhấp nháy) upload thành texture R8; shader lớp AR nhân alpha với `1 − mask` trong vùng bàn phím → phím sáng nằm **dưới** ngón tay. Dùng chung quy tắc da và thanh trượt **Skin sensitivity** hiện có (không thêm cài đặt trùng) | test: tay tổng hợp phủ phím E4 → pixel E4 trong lớp AR bị che, phím kề không |
| 4.2 | **Thích ứng màu nền** | Đo độ sáng trung bình từng phím trong ROI để chọn additive/multiply (phím trắng vs phím đen) và độ sáng glow; chống cháy sáng bằng ACES đã có | test: nền trắng → glow không vượt ngưỡng |
| 4.3 | **Phím nhấn "chìm"** | Phím đang giữ vẽ bóng tối nhẹ ở mép và dịch nhẹ xuống 1‑2 px (đã có "key press depth" cho 3D keyboard — tái dùng tham số) | ảnh `--snapshot` |
| 4.4 | **Độ phơi sáng** | Gợi ý trong status khi khung quá tối/ngược sáng (trung bình luma, % cháy sáng) thay vì để người dùng đoán vì sao dò sai | test luma tổng hợp |

**Phụ thuộc:** AR‑1. **Ước lượng:** 2 tuần. **Rủi ro:** cổ điển da‑màu kém khi ánh sáng vàng/người da tối → nút nâng cấp lên mô hình ở AR‑5 là đường lùi; đây là lý do 4.1 phải đi qua **một giao diện** (`IHandSource` trả về mặt nạ + điểm) để thay được.

### AR‑5 — Bàn tay và ngón: từ "blob da" lên theo dõi từng ngón

Hiện `HandTracker` chỉ biết **một** bàn tay lớn nhất, ngón chỉ đếm. Learn mode cần biết **ngón nào ở phím nào** và hướng dẫn **ngón nào nên bấm**.

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 5.1 | **Hai tay** | Tách 2 nhóm da lớn nhất, gán trái/phải theo vị trí x (+ hướng cổ tay); mỗi tay một marker, khớp với màu nốt theo tay đã có (`PerHand`) | test tổng hợp: hai blob |
| 5.2 | **Đầu ngón (cổ điển)** | Đỉnh lồi của đường viền mặt nạ ở độ phân giải đầy đủ + lõm giữa các ngón; trả `Fingertips[]` (x,y) → qua homography nghịch → **phím dưới từng đầu ngón** | test: bàn tay vẽ tay (5 ngón), từng đầu ngón rơi đúng phím |
| 5.3 | **Chỉ ngón (fingering) trong bài** | Đọc `<fingering>` của MusicXML (hiện **chưa** đọc), cho MIDI thì cho phép nhập/lưu ngón tay theo bài hoặc sinh gợi ý bằng thuật toán quy hoạch động đơn giản (chi phí chuyển tay); vẽ số ngón 1–5 trên nốt rơi và trên **phím thật** cạnh nốt kế | `VerifyMusicXml`: đọc đúng `<fingering>`; test DP trên giai điệu cho trước |
| 5.4 | **Gợi ý tư thế / ngón sai** (Learn) | So đầu ngón dự kiến (ngón nào) với vị trí thật khi MIDI Note On tới: "ngón 3 đang ở E4, nốt cần là F4" — **chỉ gợi ý nhẹ**, không chấm điểm vì tính không chắc | test hợp nhất |
| 5.5 | **Mô hình ML tuỳ chọn** *(đã duyệt, §8.2)* | Tải một lần (mẫu downloader Salamander: manifest ghim hash, resume, huỷ an toàn, xác minh SHA‑256) một mô hình hand landmark (ví dụ MediaPipe Hands, giấy phép Apache‑2.0) + **ONNX Runtime/DirectML**; ra 21 điểm/tay → cùng `IHandSource`. Mặc định **tắt**, gói cài không tăng dung lượng, thuật toán cổ điển luôn là đường lùi | `VerifyHandModelDownload` (hash, resume, huỷ an toàn); phần độ chính xác của mô hình không có ảnh thật để kiểm trong CI nên chỉ kiểm giao diện `IHandSource` bằng một nguồn giả, và ghi rõ điều đó trong tài liệu |

**Phụ thuộc:** AR‑1 (5.1–5.4), AR‑4.1 (giao diện `IHandSource`). **Ước lượng:** 3–4 tuần (5.5 riêng 1–2 tuần, có thể tách PR/phiên bản). **Rủi ro:** ONNX Runtime là dependency native (~20‑30 MB); đã được duyệt (§8.2) với điều kiện giữ tuỳ chọn và đường lùi cổ điển.

### AR‑6 — Hoàn thiện Learn/Perform và đầu ra

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 6.1 | **Learn mode đầy đủ** | Wait mode/chấm điểm/ghost hiện có chạy trên đàn thật: nốt kế tiếp **nhấp nháy trên phím thật**, nốt đúng/sai đổi màu viền phím, vòng "sắp tới" (landing glow đã có trên GPU, ánh xạ sang không gian phím AR), đếm nhịp bằng quầng quanh phím | `VerifyPracticeGhostAndChart` + test AR ánh xạ |
| 6.2 | **Perform mode** | Không có nốt tương lai. Mỗi nốt vừa chơi **bay lên** khỏi phím (vệt, hạt, flash theo vận tốc MIDI) bằng các hiệu ứng có sẵn của `EffectCatalog` (kết hợp theo "combo themes") — chọn một **preset AR** riêng, ví dụ "Neon Hands", "Sakura Stage". Hiệu ứng nặng (blur toàn khung) bị tắt tự động trong AR để giữ 60 fps. **Điều kiện đã biết (§12):** hiện `GpuLook.From(overCamera: true)` tắt cả hạt, wisp, lửa, vòng sóng, flash, chùm sáng, quầng; và lớp AR vẽ nốt bằng một shader phẳng riêng nên `NoteStyle`/glow/roundness/hướng `Up` không có tác dụng (kể cả `Gem` và `CrystalOutline`; hai material mới chỉ thuộc stage WPF/GPU, không tuyên bố hỗ trợ AR). 6.2 vì vậy gồm việc đưa **chính** các pass của sân khấu qua một phép chiếu cảnh→ảnh (§12), không phải chỉ bật lại các cờ. **Đã làm 2026‑10‑08 (lát cắt đầu):** vệt phẳng cho mỗi phím người chơi bấm, ở cả hai chế độ (`ArNotes.BuildLive` + `GpuArLayer.Build`, đọc lại điểm ảnh trong `VerifyArCameraLayer`); phần hạt/wisp/lửa/flash theo vận tốc và preset AR vẫn còn | preset trong `presets/` qua `VerifyCommunityPresets` |
| 6.3 | **Chế độ mặt phẳng (plane / 3D)** | Thay image‑space bằng nốt đứng trong **một mặt phẳng vuông góc với bàn phím** tại mép sau phím: cần ước lượng tư thế (PnP) từ homography + FOV gần đúng (slider **Camera FOV**, mặc định 70° ngang, hiệu chỉnh bằng 2 nốt ở xa). Làm sau cùng vì rủi ro thẩm mỹ cao. **Cập nhật 2026‑10‑06:** chủ repo đưa ảnh mẫu cần đạt (nốt và chùm sáng đứng trước một cây đàn grand, nhìn từ phía người chơi) ⇒ 6.3 là **đích**, không còn là "chỉ làm nếu…"; vẫn làm sau 2b và 6.2, vì dùng chung phép chiếu cảnh→ảnh (§12). Vẫn không dựng lại đàn 3D | `VerifyArCalibration` PnP trên dữ liệu tổng hợp; lùi về image‑space khi FOV sai |
| 6.4 | **Cửa sổ AR cho OBS / máy chiếu** | Mở rộng "OPEN GPU STAGE WINDOW": cửa sổ có camera + AR đã ghép, F11, chọn màn hình; tuỳ chọn nền trong (alpha) để OBS dùng Window Capture với Allow Transparency | `VerifyGpuStage` swap chain |
| 6.5 | **Webcam ảo** *(kéo lên v1.3 theo quyết định §8.5: vừa quay vừa stream/call)* | Windows 11 có `MFCreateVirtualCamera`: đẩy khung AR đã ghép thành camera ảo để Zoom/Meet/Teams chọn được, không cần OBS. **Chỉ Windows 11 22H2+** (Windows 10 dùng OBS Virtual Camera qua 6.4), cần đăng ký COM media source nên có thể cần ký mã. Khung ra phải đi cùng luồng với ghi hình để quay và stream cùng lúc không tốn gấp đôi GPU | `Mf.VirtualCameraProbe` SKIP khi không có API |
| 6.6 | **Xuất sidecar thông tin** | Cùng với MP4, ghi `take.ar.json` (layout, calibration, offset) để dựng lại/ghép lại trong công cụ khác | round‑trip |

**Phụ thuộc:** 6.1–6.2 sau AR‑3; 6.3 sau AR‑2; 6.4 sau AR‑1; 6.5 sau 6.4. Vì cần **vừa quay vừa stream**, 6.4/6.5 vào v1.3 và nên bắt đầu ngay khi AR‑1 xong. **Ước lượng:** 3–4 tuần.

## 5. Bản đồ phiên bản (đã chỉnh theo §8)

> Bảng này tạm giữ nguyên; §14.10 xếp lại thứ tự thi công và sẽ cập nhật bảng này khi chốt các slice mới.

| Đợt | Nội dung | Độ lớn | Giá trị người dùng thấy |
|---|---|---|---|
| **v1.1** | AR‑0 + AR‑1 (góc từ trên xuống, đủ 4 cỡ đàn) | 3–4,5 tuần | Calibrate 4 điểm, nốt rơi lên đàn thật, quay được |
| **v1.2** | AR‑3 + AR‑2.1–2.2 | 3–4 tuần | Bù độ trễ tự động, dò đàn tự động |
| **v1.3** | AR‑6.1, 6.2 (Learn/Perform) + **AR‑6.4 (OBS) + AR‑6.5 (webcam ảo)** + AR‑4 | 5–7 tuần | Che tay, preset AR, **stream/call được ngay** |
| **v1.4** | **AR‑2b (camera từ phía người chơi)** + AR‑2.3–2.5 + AR‑5.1–5.4 | 4–5 tuần | Góc nhìn thứ hai, hai tay, ngón tay |
| **v1.5** | AR‑5.5 (ML ONNX tuỳ chọn) + AR‑6.3 (mặt phẳng 3D) | 3–5 tuần | Theo dõi tay chuẩn hơn, chế độ 3D |

Tổng: **khoảng 16–22 tuần** cho một người. Mỗi đợt kết thúc phải cập nhật `CHANGELOG.md` + `CHANGELOG.en.md`
(cùng số phiên bản với `<Version>`, theo `scan_release_version`), README song ngữ và `docs/SETTINGS-WIRING-AUDIT.md`.

## 6. Lớp kiểm chứng (tóm tắt, theo luật "mỗi tính năng một lớp")

| Lớp | Thêm gì |
|---|---|
| `tools/check_sources.py` | Quét khoá chuỗi mới (tiếng Anh + Việt), dòng cài đặt mới có tooltip/tên accessible; `docs/samples/ar-*` khớp `tools/make_ar_sample.py` |
| `tests/PianoPath.Tests` (xUnit, `net10.0`, chạy mọi máy) | `KeyboardLayout`, `Homography`, `LatencyProbe`, `KeyboardDetector` (khung tổng hợp), `HandMask` — toàn bộ logic thuần |
| `--verify` (Windows) | `VerifyArCalibration`, `VerifyKeyboardDetector`, `VerifyArOverlayGpu` (pixel đọc lại trên WARP), `VerifyLatencyProbe`, mở rộng `VerifyCameraOverlay`, `VerifyGpuStage` |
| `--bench` | Thêm cảnh AR: p95 khung ≤ ngưỡng đã đặt cho 1080p |
| Ảnh README do CI | `ar-overlay.png`, `ar-calibration.png` từ khung tổng hợp (không dùng ảnh người dùng/bên thứ ba) |

## 7. Rủi ro tổng và cách giảm

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Sai lệch phối cảnh làm người dùng thấy "nốt không trúng phím" và bỏ tính năng | Cao | Calibrate thủ công luôn có; lưới xem trước; drift watch (2.4); mặc định image‑space đơn giản |
| Độ trễ camera làm cảm giác lệch | Cao | AR‑3 sớm, không để sau mọi thứ đẹp; hiển thị độ trễ đã đo |
| Dò tự động thất bại ở góc nhìn xấu/ánh sáng xấu | Trung | Chỉ **gợi ý**, người dùng xác nhận; thông báo nguyên nhân (4.4) |
| Sửa `KeyCount` chạm nhiều file, dễ hồi quy | Trung | AR‑0.1 một PR riêng + so pixel ảnh mặc định |
| CI không có camera, không có GPU thật | Trung | Mọi thứ test bằng khung tổng hợp trên WARP (đã là cách repo làm) |
| Windows virtual camera / ONNX là dependency và rủi ro phát hành | Trung | Đẩy sang v1.5+, tuỳ chọn, SKIP khi thiếu |
| Hiệu năng ở 1080p60 + bloom + camera | Trung | Upload dynamic texture, thang camera về 720p khi bench vượt ngân sách, tắt blur nặng trong AR |
| Quyền riêng tư (camera, ảnh tay) | Thấp | Giữ nguyên: offline, không telemetry, không lưu khung hình; calibration chỉ lưu số |

## 8. Quyết định của chủ repo (2026‑10‑05)

Năm câu hỏi của bản thảo đầu đã được trả lời; mọi mục khác trong tài liệu này đã được chỉnh theo đó.

| # | Câu hỏi | Quyết định | Hệ quả cho lộ trình |
|---|---|---|---|
| 1 | Phạm vi nền tảng | **Chỉ Windows + webcam** (kể cả điện thoại làm webcam) | Kính AR/XR nằm lại trong §9; không có client thứ hai |
| 2 | ML tuỳ chọn (ONNX) cho AR‑5.5 | **Có** | AR‑5.5 là việc **trong phạm vi**, không còn "nếu được duyệt". Vẫn tuỳ chọn, tải một lần, mặc định tắt, thuật toán cổ điển là đường lùi |
| 3 | Cỡ đàn | **Đủ 49 / 61 / 76 / 88 phím** | AR‑0.1/0.2 hỗ trợ cả bốn ngay từ đầu; mọi test AR chạy trên cả bốn cỡ (`tools/make_ar_sample.py --keys N`) |
| 4 | Góc đặt camera | **Làm từ trên xuống trước**, sau đó đến từ phía người chơi, làm đầy đủ cả hai | AR‑1/AR‑2 tối ưu và kiểm chứng cho góc từ trên xuống. Góc từ phía người chơi (phím đen bị che, phối cảnh mạnh) là **AR‑2b**, sau khi từ trên xuống ổn định, và dùng chung homography + wizard 4 điểm |
| 5 | Mục đích video | **Vừa quay vừa stream/call** | **Webcam ảo (AR‑6.5) và cửa sổ OBS (AR‑6.4) được kéo lên v1.3**, cùng đợt với Learn/Perform — không để cuối. Ghi hình + đầu ra trực tiếp dùng chung một luồng khung đã ghép |

**Còn lại cần chốt khi tới lúc:** ngưỡng độ trễ cho phép khi stream/call (gợi ý ≤ 100 ms từ camera tới khung ra), và có cần
ký mã bộ cài để đăng ký webcam ảo không (Windows có thể đòi hỏi khi đăng ký COM media source).

## 9. Những việc đã cân nhắc và **không** làm (hoặc chưa)

| Việc | Lý do |
|---|---|
| Nhận phím bấm **chỉ từ hình ảnh** hoặc **nhận dạng nốt từ âm thanh** cho đàn cơ không có MIDI | Mâu thuẫn nguyên tắc "MIDI là sự thật" và mục *không làm* "transcription" của roadmap chung; độ chính xác thấp, trễ cao. Khuyên dùng cảm biến/cổng MIDI cho đàn cơ |
| Ứng dụng kính AR / XR (Quest passthrough, HoloLens, Vision Pro) | Là sản phẩm khác (client riêng, Unity/WebXR); Keyflow là app WPF Windows. Hướng đi sau này **nếu cần**: phát luồng nốt/phím qua WebSocket cục bộ để client XR vẽ — đưa vào nghiên cứu, không cam kết |
| Mô hình piano 3D, quỹ đạo camera tự do | Đã nằm trong *không làm* của `docs/ROADMAP.md` §5; AR‑6.3 chỉ là mặt phẳng nốt, không phải dựng lại đàn |
| Tự nhận diện hãng/loại đàn | Không cần: layout + calibration đã đủ |
| Lưu khung hình camera để huấn luyện | Phá nguyên tắc offline/không telemetry |
| Ứng dụng di động riêng | Ngoài phạm vi; điện thoại làm webcam đã giải quyết nhu cầu |

## 10. Việc nên làm **ngay tuần đầu** (nếu chốt hướng đi)

1. PR `AR‑0.1` (KeyboardLayout, không đổi hành vi) — thấp rủi ro, mở khoá tất cả.
2. PR `AR‑0.5` (`tools/make_ar_sample.py` + khung tổng hợp) — để mọi PR sau có bằng chứng.
3. Spike 1–2 ngày: upload texture camera 1080p60 vào `GpuStageRenderer`, đo `--bench` — **xác nhận ngân sách hiệu năng** trước khi cam kết AR‑1.
4. Spike nhỏ: `Homography` + vẽ lưới phím lên khung tổng hợp để xem trực quan sai số.

---

*Mọi mục chỉ xong khi: code + `check_sources.py`/`--verify` cập nhật + tài liệu (README song ngữ hoặc `docs/*`) viết lại +
ảnh `docs/previews/` phản ánh đúng UI mới (CI lo phần ảnh) — cùng định nghĩa "xong" với `docs/ROADMAP.md`.*

## 11. Rà soát chéo sau lần thử webcam thật đầu tiên (2026‑10‑05)

Lần đầu chạy trên webcam thật (CI chỉ chạy WARP, không có camera) lộ ra ba lỗi và một khoảng trống. Bảng này đối chiếu mọi thay đổi camera với các điều đã hứa trong lộ trình.

| # | Phát hiện | Trạng thái | Ghi chú |
|---|---|---|---|
| 1 | `ReaderAdvancedKey` mang GUID của `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS`, nên reader không bao giờ chèn bộ đổi màu ⇒ mọi camera YUY2/NV12/MJPEG lỗi `0xC00D5212` khi xin RGB32 | ✅ đã sửa | GUID đúng `0F81DA2C-…`; `check_sources.py` ghim giá trị; nếu camera không cho khung 32‑bit ở mode đã chọn thì dùng mode mặc định của nó và báo `ModeNote` (đúng như 0.3 đã hứa) |
| 2 | Reader tạo trên luồng UI (STA), đọc trên luồng camera ⇒ lần đọc đầu lỗi `E_NOINTERFACE` | ✅ đã sửa | Một luồng duy nhất mở, đọc và đóng reader (`Flush`/`Dispose` không còn gọi từ luồng UI). Cùng đường cho video tệp |
| 3 | `CopyFrom`/mã chia sẻ mang `CameraSourceLink`, `CameraVideoPath` ⇒ áp một preset đặt camera về "Tự động" và bỏ video tệp; mã chia sẻ mang đường dẫn thiết bị của máy khác. Trái nguyên tắc §2 "căn chỉnh thuộc về máy" | ✅ đã sửa | Hai khoá thành thuộc tính của máy (như `KeyboardSize`, `Ar*`, ảnh nền) |
| 4 | Tự mở lại camera khi danh sách đổi có thể xảy ra **giữa lúc ghi, đo độ trễ (3.2) hoặc đánh dấu góc phím (1.2)** | ✅ đã sửa | Giữ việc đọc danh sách trong ba trạng thái đó; kết quả đọc muộn bị bỏ. 1.2/3.2 vốn đã chịu được camera bị đổi (`SameSource`, đo tự bỏ) nhưng người dùng sẽ mất công vô ích |
| 5 | Calibration khoá theo `camera.Link`; camera ảo không có link | ✅ ổn | Camera không có link lấy `name:<tên>` làm định danh nên vẫn chọn, nhớ và lưu calibration được. Link thật của camera vật lý không đổi so với trước |
| 6 | `ArDelay` (3.1) là **một số cho cả máy**, còn độ trễ phụ thuộc camera (webcam USB ≈ 60–150 ms, OBS Virtual Camera/điện thoại làm webcam khác hẳn) | 🔴 **mở** — đề xuất **AR‑3.5** | Lưu `ArDelay` theo `camera.Link` (như calibration), tự nạp khi đổi camera, nút MEASURE DELAY ghi vào đúng camera. Chưa làm vì đổi mô hình lưu trữ; cần chủ repo duyệt |
| 7 | Webcam ảo của chính Keyflow (6.5) sẽ hiện trong danh sách camera của chính nó ⇒ vòng lặp hình | 🟡 **ràng buộc cho 6.5** | Khi làm 6.5: nhận diện tên "Keyflow Virtual Camera" trong `CameraRanking.IsVirtual`, **không bao giờ** để "Tự động" chọn nó và ẩn/đánh dấu nó khỏi danh sách nguồn. Thêm test vào `CameraRankingTests` cùng PR |
| 8 | Mở camera chạy trên luồng riêng nhưng `StartCameraOverlay` vẫn **chờ** (tối đa 20 giây) ở luồng UI; tự mở lại khi cắm/rút camera làm cửa sổ khựng 1–3 giây | 🟡 chấp nhận được, ghi lại | Cùng chi phí như trước nhưng giờ có thể tự xảy ra. Nếu gây khó chịu: chuyển sang mở bất đồng bộ (luồng báo về UI bằng `Dispatcher`) |
| 9 | Vòng đọc danh sách 3 giây gọi `MFEnumDeviceSources` đều đặn khi camera đang chạy | 🟡 **chưa đo trên máy thật** | Đã chặn khi ghi/đo/đánh dấu. Nếu thấy rớt khung ở 60 fps: nới chu kỳ hoặc chỉ đọc khi cửa sổ Cài đặt mở |
| 10 | 1.1/1.6: engine là bên duy nhất lấy khung khi AR bật; calibration và `ApplyArSource` dùng `reader.Source` | ✅ ổn | Sau khi lùi sang camera khác, `Source` là camera **thực sự** đã mở nên calibration tìm đúng bản ghi (hoặc báo `NotCalibrated`) |
| 11 | 3.4 ghi hình (âm bắt đầu muộn d) và 1.7 (`CameraAgeMeter`) | ✅ ổn | Không chạm vào đường ghi; đổi camera giữa lúc ghi đã bị chặn (#4) nên d lấy lúc bắt đầu vẫn đúng suốt bản ghi |
| 12 | 2b (camera từ phía người chơi, điện thoại làm webcam) | ✅ ổn | Điện thoại làm webcam (Iriun, DroidCam, Camo, EpocCam) xếp vào "camera màu"; chọn tay trong danh sách luôn được |

**Còn phải thử trên phần cứng thật** (không có trong CI): Camera đa mode (720p60 vs lùi mode mặc định), bật/tắt OBS Virtual Camera khi app đang mở, hai webcam cùng loại (hậu tố `(2)`), tên lấy từ registry, độ khựng khi tự mở lại, p95 của cảnh `ar` trên card thật.

## 12. Hai ảnh mẫu của chủ repo ↔ từng cài đặt hiện có (2026‑10‑06)

Chủ repo đưa hai ảnh cần đạt: **(A)** nhìn thẳng từ trên xuống, nốt/khói/hạt bay lên từ các phím thật, tay nằm trên phím; **(B)** nhìn từ phía người chơi lên một cây đàn grand, nốt và chùm sáng **đứng** trong không khí trước đàn, có phối cảnh. Kết luận sau khi đọc `ArPlan`, `GpuLook`, `GpuArLayer`, `ArProjection`, `StageShaders.hlsl`: **làm được cả hai với webcam, không cần nền tảng hay thư viện mới**, nhưng chúng cần hai khối việc khác nhau.

**Điều đang chặn (đọc từ mã, không phải đoán):**

1. Khi AR hoạt động, `ArPlan.HideStageLayers` ⇒ `GpuLook.From(overCamera: true)` **tắt** `ShowNotes`, `ShowKeys`, `ShowEmbers`, `ShowWisps`, `ShowFlame`, `ShowImpactRings`, `ShowImpactFlash`, `ShowHalo`, `ShowLightBeams`. Chỉ còn nốt phẳng và màu phím của `GpuArLayer`.
2. Nốt AR vẽ bằng `VsArQuad/PsArQuad`, một shader riêng chỉ có viền, đầu sáng và màu: **`NoteStyle` (Solid/Neon/Glass/Fire/Gem/CrystalOutline), `Notes3D`, `NoteGlow`, `NoteRoundness`, `NoteEdge`, vệt, bóng mờ, `NoteDirection = Up` đều không có tác dụng trong AR.** `Gem` và `CrystalOutline` chưa được port; AR hiện là fallback quad phẳng, không được tính là hỗ trợ material. Màu (`ColorMode`, `Palette`, theo tay/track) có tác dụng vì đi qua `look.NoteColor`.
3. `ArOptions.Mode = Perform` nghĩa là "không vẽ nốt sắp tới", và **từ 2026‑10‑08** mỗi phím bạn bấm kéo theo một **vệt phẳng** mọc ra từ phím theo đúng phương bàn phím trong ảnh (`ArNotes.BuildLive`, vẽ trong `GpuArLayer`) — lát cắt đầu của (A); vệt/hạt/flash theo vận tốc MIDI vẫn là AR‑6.2.
4. Mọi hiệu ứng của sân khấu sống trong **không gian cảnh** (x theo phím, y so với đường chạm). Muốn đặt chúng lên đàn thật phải qua một phép chiếu cảnh→ảnh.
5. `HandTracker` chỉ đọc **một** bàn tay, bằng luật màu da YCbCr; chưa có mặt nạ để che hiệu ứng.

**Hướng làm đề xuất (một kiến trúc cho cả A và B):** thêm vào `Frame` cbuffer một ma trận 3×3 *cảnh→ảnh* và một cờ AR; `VsNote` và `VsSprite` biến đổi vị trí qua ma trận đó và truyền trọng số `w` để nội suy đúng phối cảnh (như `VsArQuad` đang làm). Khi đó **chính** nốt, hạt, wisp, lửa, vòng sóng, chùm sáng của sân khấu chạy trong AR, nên mọi thanh trượt trang Notes/Particles/Effects có tác dụng mà không viết lại từng hiệu ứng. Ma trận (A) là homography mặt phím đã có; ma trận (B) là homography của **mặt phẳng đứng** ở mép sau phím, suy ra từ tư thế camera.

| Thành phần | Có sẵn | Còn thiếu cho (A) | Còn thiếu cho (B) |
|---|---|---|---|
| Hình camera + khung AR + calibration 4 điểm | ✅ AR‑0/1 | — | calibration cho góc nghiêng (2b.1–2b.3), chiều cao phím |
| Nốt theo màu của look | ✅ | — | — |
| Kiểu nốt, glow, bo góc, vệt | ✅ trên sân khấu, ❌ trong AR | đưa `PsNote` qua phép chiếu | như (A) |
| Hạt, wisp, lửa, vòng sóng, flash, chùm sáng | ✅ trên sân khấu, ❌ trong AR | phép chiếu cho `VsSprite`; bỏ cờ `overCamera` từng loại | như (A) + chiếu lên mặt đứng |
| Nốt/hạt **bay lên** | ✅ `NoteDirection=Up` trên sân khấu | Perform mode (6.2) | như (A) |
| Che tay | ❌ | mặt nạ da (4.1); chỉ cần ở vùng phím | **cần hơn (A)**: tay nằm giữa camera và mặt phẳng nốt, nên che cả phần dưới của nốt/chùm sáng |
| Tư thế camera (PnP) + FOV | ❌ | không cần (mặt phẳng nằm) | **bắt buộc** (6.3): `K⁻¹H` ⇒ `r1, r2, t`, FOV mặc định 70°, thanh trượt **Camera FOV** |
| Độ trễ camera | ✅ AR‑3 | — | — |
| Quay MP4 có lớp camera | ✅ AR‑3.4 | — | — |
| Webcam ảo / OBS để stream | ❌ (6.4/6.5) | — | — |

**Rủi ro riêng:**
- (A) Tay dưới ánh sáng màu: ảnh mẫu có tay nhuộm xanh/tím do chính hiệu ứng toả sáng. Luật da YCbCr sẽ **hỏng** ở đây (màu da lệch khỏi cửa sổ chroma), nên mặt nạ có thể nhấp nháy. Đường lùi là mô hình ONNX (5.5) đã duyệt; trước đó, cho nốt/hiệu ứng chỉ nằm **sau** mép sau phím (không chồng lên tay) là đủ cho hầu hết khung hình.
- (B) FOV sai làm phương thẳng đứng của nốt lệch: nốt "nghiêng" so với đàn. Cần thanh trượt FOV có xem trước và lùi về image‑space khi giá trị vô lý.
- (B) Chỗ đặt camera: phải thấy toàn dãy phím từ phía sau/trên đầu người chơi mà không bị đầu và vai che. Đây là vấn đề lắp đặt, phần mềm không giải quyết được.
- (B) Phím nhỏ trong ảnh nên sai số 1–2 px ở góc xa thành lệch cả phím: calibration phải có phóng to khi chấm góc và (sau này) tinh chỉnh bằng MIDI (2.3).
- Cả hai: p95 khung hình của cảnh `ar` mới chỉ đo trên WARP (≈154 ms); cần đo trên card thật trước khi thêm cả bộ hạt vào AR.

**Thứ tự đề xuất:** (1) thử máy thật AR‑1/3 → (2) phép chiếu cảnh→ảnh cho `VsNote`/`VsSprite` + Perform mode = **ảnh A** → (3) mặt nạ tay 4.1 → (4) 2b + PnP + mặt phẳng đứng = **ảnh B** → (5) webcam ảo 6.5 xen vào bất cứ lúc nào sau (2) vì cần stream được. Thứ tự này khác bản đồ §5 (nơi 6.3 ở v1.5): nếu chủ repo đồng ý, (2) và (3) kéo lên v1.2–v1.3, (4) vào v1.4.

## 13. Đối chiếu tiến độ với lộ trình (2026‑10‑08)

| Hạng mục | Mã | CI | Máy thật | Ghi chú |
|---|---|---|---|---|
| AR‑0.1 `KeyboardLayout`, 0.2 cỡ đàn, 0.5 khung mẫu | ✅ | ✅ | không cần | 49/61/76/88 phím |
| AR‑0.3 chọn mode, 0.4 nhịp đọc riêng | ✅ | ✅ | 🟡 hai lỗi đã sửa, chưa xác nhận lại trên máy; chưa đo 60 fps | hai lỗi thật đã sửa (§11) |
| AR‑0.6 danh sách, tên, hot‑plug camera (ngoài kế hoạch) | ✅ | ✅ | ❌ chưa thử | OBS tự hiện, hai webcam trùng tên |
| AR‑1.1 camera là texture GPU, 1.3 homography, 1.4 phím sáng | ✅ | ✅ (WARP đọc lại điểm ảnh) | 🟡 đã thấy vệt phím tự bấm khi thử máy thật (báo của chủ repo 2026‑10‑08), còn phải xác nhận lại | vệt phẳng cho phím tự bấm là lát cắt đầu của AR‑6.2 |
| AR‑1.2 cửa sổ 4 điểm, 1.5 thẻ AR, 1.6 lưu | ✅ | ✅ | ❌ chưa nhìn bằng ảnh thật | thêm **khung nhìn** mặc định/45°/90°: mỗi góc camera một vùng phím riêng, `ar-calibration.json` bản 2, tệp bản 1 đọc lên thành khung nhìn mặc định |
| AR‑1.7 ghi hình AR | ✅ | ✅ (AVI/WARP) | ❌ MP4 thật | `VerifyMp4Take` mong manh trên CI |
| AR‑3.1 độ trễ cố định, 3.2 đo tự động | ✅ | ✅ | ❌ cần webcam + đàn MIDI | |
| AR‑3.3 đo của chính app | ✅ | ✅ (WARP p95 ≈ 154 ms) | ❌ cần card thật | mục tiêu < 16 ms |
| AR‑3.4 ghi hình đồng bộ | ✅ | ✅ | ❌ | |
| AR‑2 dò tự động, 2b phía người chơi | ❌ | | | ảnh B phụ thuộc 2b |
| AR‑4 che tay, ánh sáng | ❌ | | | |
| AR‑5 tay và ngón, ONNX | ❌ | | | |
| AR‑6.1 Learn đầy đủ, 6.2 Perform, 6.3 mặt phẳng | 🟡 6.2 mới có vệt phẳng cho phím tự bấm | | | ảnh A cần 6.2, ảnh B cần 6.3 (§12) |
| AR‑6.4 cửa sổ OBS, 6.5 webcam ảo, 6.6 sidecar | ❌ | | | stream/call là ưu tiên của chủ repo |
| Tài liệu người dùng (README, CHANGELOG song ngữ) | ✅ mục **Unreleased** | | | README chưa có mục tả AR, chỉ có cờ `--bench-ar`; viết khi AR ra bản |

**Còn nợ kỹ thuật đã biết:** `ArDelay` toàn cục (đề xuất AR‑3.5, cần chủ repo duyệt theo §14.11); mở camera chặn luồng giao diện tới 20 s; `StartCameraOverlay` đồng bộ; AR không áp `NoteStyle` và các hiệu ứng (§12) — ma trận *cảnh→ảnh* đã có (**AR‑S4a**, `Camera/ArSceneProjection.cs`, kiểm bằng `ArSceneProjectionTests` + `VerifyArSceneProjection`) và đã nối xuống GPU cho pass nốt (**AR‑S4b**: nhánh AR trong `VsNote`, `w` làm clip w, **mặc định tắt** — `GpuArInput.ProjectedNotes`, kiểm bằng `GpuArMapTests` + `VerifyArNoteProjection` đọc lại điểm ảnh trên WARP); còn thiếu nhánh AR trong `VsSprite`, shape/truncate của nốt, min‑scale đúng nghĩa và công tắc nối pass nốt đó vào giao diện.

**Từ 2026‑10‑09:** thiết kế lại toàn bộ hệ thống AR — xem §14. Mục AR‑6.2 trong bảng trên ("vệt phẳng cho phím tự bấm") là **AR‑S0** của bản mới; phần style nốt/hiệu ứng chuyển thành AR‑S4/AR‑S5.

## 14. Thiết kế lại AR OVERLAY — "piano live play" (2026‑10‑09, tham chiếu Keysight)

### 14.0 Vì sao Keysight, và những gì đã đọc

[Keysight](https://egglyberts.live/keysight/wiki/) (Egglyberts, Unreal Engine) là visualiser MIDI "live play" được dùng và
tài liệu hoá kỹ nhất hiện có: nó đã đi hết không gian thiết kế mà mình đang bước vào — phần tử, tham số theo vận tốc,
tầng Z, chế độ trong suốt cho OBS, căn chỉnh AR bằng tay, render ra video. Mình **không** bắt chước engine của nó
(UE4, PBR, 3D thật, đàn 3D, không âm thanh); mình lấy **kiến trúc ý tưởng** rồi ánh xạ lên AR phẳng `flat-quad` của
Keyflow. Ghi chú trung thực: nhiều trang của wiki còn là **stub** (`pulses`, `particles`, `piano`, `dampers`, `backdrop`,
`overlay`, `light bars`, `octave lines`, hướng dẫn hiệu năng, time wizard, video background vẫn "UNDER CONSTRUCTION"), nên
phần chi tiết của các phần tử đó lấy từ trang intro/effect/gallery chứ không phải từ trang riêng.

| Trang (đọc 2026‑10‑09) | Lấy gì |
|---|---|
| [`guide_basic_intro`](https://egglyberts.live/keysight/wiki/guide_basic_intro/) | Lối dùng: chuẩn / live / "generic" / **AR** / máy tạo hạt; bốn giới hạn (không âm thanh, một instance, khó học, không nền video) |
| [`docu_effect_intro`](https://egglyberts.live/keysight/wiki/docu_effect_intro/) | Hai họ phần tử: **Scene** (tĩnh) và **Effect** (sinh ra khi có nốt); effect chia theo ý nghĩa: *indicator / emphasis / velocity* |
| [`docu_effect_note`](https://egglyberts.live/keysight/wiki/docu_effect_note/) | Nốt: 5 shape × trắng/đen, mọc từ 0 theo 3 mảnh, đường thẳng theo pitch, Z-height riêng, **minimum note scaling**, **new note truncates**, **key-size graph theo velocity**, pitch bend, sustain kéo dài |
| [`docu_effect_keypress`](https://egglyberts.live/keysight/wiki/docu_effect_keypress/) | Phủ lên phím đang giữ: coverage (Clean/Inset/Full), animation (alpha/ngang/dọc), lifetime, **graph fade-over-lifetime chạy trái→phải khi bấm và phải→trái khi nhả** |
| [`docu_effect_impact`](https://egglyberts.live/keysight/wiki/docu_effect_impact/) | Đèn điểm ở hàng phím cho biết phím nào đang xuống/kêu: sáng theo velocity (không qua floor/ceiling), candle flicker, Z-height/radius/falloff, kéo dài khi có sustain |
| [`docu_effect_note_light`](https://egglyberts.live/keysight/wiki/docu_effect_note_light/) | Đèn chữ nhật vô hình trên nốt → **hắt sáng xuống**; cảnh báo **lệch khi nhìn nghiêng** vì độ cao bị phản chiếu |
| [`docu_concept_graph`](https://egglyberts.live/keysight/wiki/docu_concept_graph/) | **Biến graph**: `Mode(Flat/Graph) × Start/Stop/Min/Max/Curve`; đầu vào luôn 0..1 (velocity đã chuẩn hoá, hay tỉ lệ của một giá trị như "lifetime"); kẹp ngoài khoảng, curve −1..1 |
| [`docu_feat_simulation`](https://egglyberts.live/keysight/wiki/docu_feat_simulation/) | **"Activity"**: velocity 0–127 → 0..1 qua **floor/ceiling**; tắt dần theo thời gian với thời gian tắt khác nhau theo pitch và theo **damping**; sustain 0..1 **không nhị phân**; sostenuto/soft; notes/sec; total activity |
| [`docu_concept_layout`](https://egglyberts.live/keysight/wiki/docu_concept_layout/) | Ba trục X/Y/Z, gốc ở giữa E/F, đơn vị cm; **Note direction** bottom-up (live) vs top-down (delay); "key length 1.00 = đúng kích thước thật"; note speed graph |
| [`docu_concept_layers`](https://egglyberts.live/keysight/wiki/docu_concept_layers/) | **Cao độ Z quyết định thứ tự vẽ**; chống z-fighting; xếp hạng pulse/particle theo `Z×1000`, mới nhất lên trên; bảng độ cao (pulse/particle 0.10, light bar 1.25–1.30, nốt 0.1) |
| [`docu_concept_colour`](https://egglyberts.live/keysight/wiki/docu_concept_colour/) | **Màu động**: Static (đổi ô theo từng nốt), **Channel** (theo kênh MIDI → tách hai tay), Velocity, Gradient, Key series, Cycling, Notes/sec, Activity, Random |
| [`docu_concept_material`](https://egglyberts.live/keysight/wiki/docu_concept_material/) | Bốn mode material (Basic/Advanced/Blended/Transparent); **self-emissive** độc lập với ánh sáng; chỉ `Transparent` mới bán trong suốt, và nó hoàn toàn emissive |
| [`docu_feat_graphics`](https://egglyberts.live/keysight/wiki/docu_feat_graphics/) | Resolution scale; **mask mode ép scale = 100**; max framerate vs v-sync (v-sync mượt hơn khi capture); preset thấp **bỏ đèn, nốt tự phát sáng** |
| [`docu_feat_mask_mode`](https://egglyberts.live/keysight/wiki/docu_feat_mask_mode/) + [`guide_adv_mask_mode`](https://egglyberts.live/keysight/wiki/guide_adv_mask_mode/) | Trong suốt: **stencil nhị phân nhét vào kênh blue** (×0.495, +0.5), shader OBS giải mã; tách **geometry/glow** hai lớp rồi cộng dồn; lịch sử: chroma key → artifact vì bloom/hạt |
| [`docu_feat_render`](https://egglyberts.live/keysight/wiki/docu_feat_render/) | Render-to-video: profile, fps 10–240, downsample 4×/16×, audio, fade/watermark, **bước thời gian cố định**, frame → ffmpeg; **particle rendering mode** vì khung hình bị hỏng khi vừa mô phỏng vừa xuất |
| [`guide_adv_ar`](https://egglyberts.live/keysight/wiki/guide_adv_ar/) | Quy trình căn AR: **góc máy** (qua vai, tránh tay nằm trong vùng hiệu ứng), ảnh alignment đúng tỉ lệ, preset tham chiếu, FoV, **Z-height của effect phải chỉnh lại cho AR (mặc định là cho 2D)**, tắt planar shadow, **backup ảnh alignment để thấy camera xê dịch**, "preset mặc định cho randomizer" |
| [`docu_feat_midi`](https://egglyberts.live/keysight/wiki/docu_feat_midi/) | Chỉ hiểu **kênh** (không track), CC 64/66/67, pitch bend, note test giả lập (Q / turbo) |

### 14.1 Mười ba bài học → hệ quả cho Keyflow

| # | Bài học từ Keysight | Hệ quả ở Keyflow | Hôm nay |
|---|---|---|---|
| 1 | Hai họ phần tử: Scene (tĩnh) và Effect (sinh theo nốt) | Catalogue AR tường minh: mỗi phần tử có `ArElement` (họ), độ cao, cờ AR, pass vẽ | ❌ chưa có catalogue |
| 2 | **Biến graph** cho mọi tham số | `Stage/GraphVariable.cs`: `Flat\|Graph` + `Start/Stop/Min/Max/Curve`, `Sample(input 0..1)` | ✅ AR‑S2 (2026‑10‑09): `GraphVariable` + 2 graph đầu tiên của AR (độ rộng, độ hiện của nốt/vệt theo velocity) |
| 3 | **Simulation** dùng chung cho mọi hiệu ứng | Một hợp đồng `PianoSimulation`: `Activity(pitch)` (floor/ceiling + tắt dần), `Damping`, `Pedals`, `NotesPerSecond`, `TotalActivity` | ✅ `Stage/PianoSimulation` (AR‑S1, 2026‑10‑09): `Activity`, `NotesPerSecond`, `TotalActivity`, `Sustain` (0..1, damping), `SetSostenuto` bắt cạnh, `SetSoft` chỉ ghi nhận |
| 4 | **Thứ tự theo độ cao Z**, mặc định là cho 2D | Bảng thứ tự vẽ + **độ cao cm** cho từng phần tử AR; mặc định AR khác mặc định 2D | ❌ mọi thứ cùng một mặt phẳng |
| 5 | Nốt: mọc theo mảnh, min-scale, truncate, size graph | Port vào pass nốt AR thay vì quad phẳng cứng | ❌ |
| 6 | Keypress: phủ phím + fade hai chiều | Vệt phẳng hiện tại là lát đầu; thêm coverage + fade graph, giữ `ReleaseLife` | 🟡 (vệt 2026‑10‑08 + phím sáng) |
| 7 | Impact light: đèn ở hàng phím, sáng theo velocity, kéo dài theo pedal | Nâng key-glow/landing-glow thành blob sáng theo velocity | 🟡 |
| 8 | Note light: hắt sáng xuống; nghiêng thì lệch | `spill` hiện có; trong AR vẽ **trên mặt phẳng** chứ không phản chiếu | 🟡 |
| 9 | Màu theo **kênh MIDI** = tách hai tay | Thêm chế độ `Channel` (mỗi tay một màu) + `Activity`/`Notes‑sec` | 🟡 (màu theo track + velocity tint) |
| 10 | Trên ảnh camera chỉ dùng được material **emissive/alpha** | Bộ "AR material": flat emissive, gradient, texture, additive glow; `Gem`/`CrystalOutline` cần biến thể AR hoặc bị loại | ❌ |
| 11 | Mức đồ hoạ (Low→Ultra): bậc thấp **bỏ đèn, nốt tự phát sáng** | `AR‑Lite / AR‑Standard / AR‑Ultra`; bậc Lite = quad phẳng tự phát sáng (đúng thứ đang có) | ❌ chưa có tên/khái niệm |
| 12 | Đầu ra: stencil/alpha, tách glow, render-to-video | Thay chroma bằng alpha/stencil thật; tách geometry/glow cho OBS; render profile AR | ❌ (chỉ composite) |
| 13 | Căn chỉnh: ảnh alignment, phát hiện xê dịch, khuyến nghị góc máy, fisheye | Giữ 4 điểm + khung nhìn; thêm overlay kiểm tra + đo trôi + hướng dẫn | 🟡 (4 điểm + khung nhìn ✅) |

### 14.2 Kiến trúc 5 tầng

```
MIDI ─► PianoSimulation ──► Element spawner ──► Effect elements (nốt, keypress, impact,
         (activity, damping,      │               note light, pulse, hạt)
          pedal, notes/s)         │  tham số = GraphVariable.Sample(input)
                                  ▼
    Colour modes (Static/Channel/Velocity/Activity/…)   ArProjection: cảnh → ảnh
                                  │                     (homography + độ cao + w)
    Scene elements (camera, phím, damper, octave lines, light bars, HUD)
                                  ▼
                    AR passes ─► Output modes (composite / alpha / glow / stencil / render)
```

| Tầng | Nội dung | Hôm nay |
|---|---|---|
| 1. Simulation | activity có floor/ceiling, tắt dần theo pitch + damping, 3 pedal, notes/sec, total | ✅ `Stage/PianoSimulation` (AR‑S1) — `GpuStageSimulation` vẫn lo glow/heat/spill |
| 2. Tham số | `GraphVariable` + catalogue đầu vào (velocity, tiến trình, pitch, notes/sec, activity, damping) | 🟡 AR‑S2: `GraphVariable` + velocity (độ rộng/độ hiện nốt); các đầu vào còn lại ở AR‑S3/S5 |
| 3. Phần tử | Scene + Effect (§14.3), mỗi cái có độ cao cm + cờ AR | chưa có catalogue |
| 4. Chiếu | homography cảnh→ảnh trong **chính** pass của sân khấu (§12), cộng độ cao → scale/phối cảnh | chưa (§12 là đề xuất) |
| 5. Đầu ra | composite / alpha-effects / geometry+glow / stencil / render / camera ảo | chỉ composite + ghi hình |

**Độ cao (analog của Z-axis).** Mọi phần tử AR có `Height` tính bằng cm trên mặt phẳng phím. `ArProjection` hiện đã có
homography + trọng số `w`; thêm độ cao thì một điểm ở `Height` được vẽ lệch theo pháp tuyến của mặt phẳng trong ảnh và
nhân tỉ lệ cục bộ (độ lớn tỉ lệ với khoảng cách trong ảnh) — đúng cách Keysight xếp tầng, nhưng trên một mặt phẳng thay
vì một cảnh 3D. Bảng mặc định dự kiến (cm): phím 0 · damper 0.1 · nốt 0.1 · keypress 0.15 · impact glow 0.2 ·
note-light spill 0.05 · pulse 0.3 · hạt 0.4 · HUD 0 (vẽ cuối, không qua homography). Đây là **số khởi đầu cho 2D**;
bài học AR của Keysight là phải chỉnh lại theo góc máy — nên mỗi khung nhìn (45°/90°) có bộ độ cao trong preset AR.

### 14.3 Catalogue phần tử AR

| Phần tử | Họ | Hôm nay | Việc cần | Pass |
|---|---|---|---|---|
| `Piano keys` (thân phím) | Scene | ✅ `ArKeys.Patches` (trắng = mặt + thân, đen = một hình) | thêm animation nhấn (giữ ngắn) | `VsArQuad` |
| `Dampers` (dải mép sau phím) | Scene | ❌ | dải mỏng nhích lên theo damping 0..1 (sustain 0..1 chứ không nhị phân) | `VsArQuad` |
| `Octave lines` | Scene | ❌ | vạch mờ mỗi quãng tám, theo dõi `KeyboardLayout` | `VsArQuad` |
| `Light bars` (viền/hắt mép) | Scene | ❌ | gradient quanh mép phím; **không** đổ bóng (bài học AR) | `VsArQuad` |
| `Overlay / HUD` | Scene | ❌ | đếm nốt, hiển thị pedal, tên nốt, watermark; vẽ sau cùng, **không** qua homography | pass riêng |
| `Note objects` | Effect | 🟡 quad phẳng (`ArNotes.Build`) | shape × trắng/đen, min-scale, truncate, size graph, mọc theo mảnh | `VsNote` qua projection (S4) |
| `Keypresses` | Effect | 🟡 phím sáng + vệt (`ArLiveBar`) | coverage, fade graph hai chiều, màu theo velocity | `VsArQuad`/`VsSprite` |
| `Impact lights` | Effect | 🟡 glow phím | blob ở mép phím theo velocity, "đèn" kéo dài khi sustain, candle flicker tuỳ chọn | `VsSprite` |
| `Note lights` (spill) | Effect | 🟡 `spill` của sân khấu | hắt sáng trên mặt phẳng, bán kính theo graph | `VsSprite` |
| `Pulses` | Effect | ❌ (rings của sân khấu bị tắt trong AR) | vòng tại điểm chạm, ripple, "hit ring" | `VsSprite` |
| `Particles` | Effect | ❌ | hạt nổi trên mặt phẳng (bụi, tia, mưa như "Raindrops" trong gallery) | pass hạt qua projection |

### 14.4 An toàn trên ảnh camera (AR safety)

- **Không** bóng đổ / planar shadow trong AR (bài học light bar của Keysight).
- **Không** blur toàn khung (đã bị tắt trong AR từ trước; giữ nguyên và ghi vào catalogue).
- **Độ cao phải theo khung nhìn**: mặc định 2D thường sai khi camera nghiêng; mỗi khung nhìn có bộ độ cao riêng.
- **Tay**: ưu tiên góc máy *qua vai* để tay nằm ngoài vùng hiệu ứng; nếu không được thì dùng mặt nạ tay (AR‑4).
- **Fisheye**: homography phẳng không uốn được; phải khử méo trước khi calibrate (cảnh báo + hướng dẫn ngay trong cửa
  sổ calibrate, kèm khuyến nghị góc máy như trên).
- **Material**: chỉ emissive/alpha trên ảnh camera; reflection/PBR chỉ có nghĩa trong ảnh dựng hoàn toàn (render).
- **Alpha/stencil mode**: khoá `Resolution scale = 100` (bài học mask mode) và phải giữ pixel không bị resample.

### 14.5 Tham số hoá: `GraphVariable`

```csharp
internal readonly record struct GraphVariable(bool Graph, double Start, double Stop, double Min, double Max, double Curve)
{
    internal double Sample(double input);   // kẹp ngoài [Start, Stop]; curve −1..1 giữa hai mốc
}
```

- **Đầu vào** luôn 0..1: velocity đã chuẩn hoá qua **floor/ceiling** của simulation; tiến trình của một effect (0→1);
  vị trí pitch trên đàn; notes/sec; activity; damping. Không có đầu vào nào khác — đúng luật của Keysight.
- **Dùng cho**: độ sáng, kích thước nốt, độ dài vệt, bán kính spill, số hạt, lifetime pulse, độ cao (Y displacement),
  tốc độ. Mỗi tham số là flat hoặc graph, mặc định flat để không phá preset cũ.
- **Lưu**: graph thuộc *look* → đi theo preset và mã chia sẻ; độ cao/khung nhìn thuộc máy như calibration.
- **Kiểm chứng**: test thuần cho `Sample` (kẹp, curve, NaN) — ✅ AR‑S2; `--verify` đọc lại pixel cho vài graph mẫu — đợi AR‑S4 (phép chiếu vào pass nốt).
- **UI (AR‑S2)**: mỗi graph chỉ 2 tay cầm (mềm ↔ mạnh) + công tắc bật/tắt; `Curve`/`Start`/`Stop` chưa mở ra giao diện — đúng như §14.9 đã hứa (không vẽ đồ thị trong bảng cài đặt).
- **AR‑S3 ✅ (2026‑10‑09)**: `ArElementCatalog` định nghĩa 11 phần tử (họ scene/effect, pass vẽ, tier tối thiểu); `ArQualityTier` Lite/Standard/Ultra với thứ tự cắt tất định (hạt → pulse → light bars → HUD → note light → damper → octave lines → impact lights), phím/nốt/vệt không bao giờ bị cắt; picker **Quality** trong thẻ AR, mặc định Standard (nâng cấp không mất ánh phím), thuộc máy nên áp look không đổi; `ArLookGuard` chuyển `Gem`/`CrystalOutline` — và mọi tên style app không biết — về `Neon` phẳng thay vì vẽ sai
(một style thêm vào trang cài đặt trước khi có biến thể AR sẽ bị loại khỏi AR chứ không được đưa cho một pass chưa từng nghe tên nó).

### 14.6 Đầu ra (output modes)

| Mode | Dùng khi | Cần gì | Kiểm chứng |
|---|---|---|---|
| `composite` (đang có) | chơi/xem tại chỗ, quay | như hiện tại | `VerifyArCameraLayer` |
| `alpha-effects` | OBS/stream: lớp AR **trong suốt** để OBS ghép lên nguồn camera riêng | swap chain có alpha + "Allow Transparency"; scale 100 | ảnh PNG alpha đọc lại điểm ảnh |
| `geometry+glow` | ghép glow kiểu cộng dồn cho đẹp (như guide mask mode) | tách pass sáng viền/đầu nốt thành lớp riêng | hai ảnh PNG, so pixel |
| `stencil-kênh` | ai buộc phải key/chroma (tương thích cách Keysight) | stencil nhị phân nhét vào kênh blue + shader OBS (tài liệu hoá) | kiểm bằng ảnh mẫu + hướng dẫn |
| `render-to-video` | làm video | profile (độ phân giải/fps/downsample), audio, fade/watermark, bước thời gian cố định | so khung live ↔ render |
| `virtual camera` / `OBS window` | stream/call (AR‑6.4/6.5 giữ nguyên) | dùng lại các mode trên | như cũ |

Chroma key cũ (nền xanh) giữ làm đường lui nhưng **không khuyến khích** — đúng lịch sử của Keysight: bloom và hạt nhỏ
làm artifact.

### 14.7 Căn chỉnh: giữ gì, thêm gì

- **Giữ**: 4 điểm + khung nhìn (đã xong), calibration thuộc máy, `ArDelay` toàn cục (AR‑3.5 vẫn cần chủ repo duyệt).
- **Thêm 1 — overlay kiểm tra**: vẽ lưới phím + 4 góc đã lưu lên ảnh camera đang chạy (nút "ALIGNMENT CHECK"), để thấy
  ngay camera có bị xê dịch — analog của "ảnh alignment" bên Keysight.
- **Thêm 2 — đo trôi**: so mép hàng phím trong khung mới với calibration; lệch quá ngưỡng → nhắc đánh dấu lại
  (không tự sửa).
- **Thêm 3 — xuất/nhập bản sao căn chỉnh** (JSON + ảnh chụp) để backup trước khi chỉnh chân máy.
- **Thêm 4 — hướng dẫn ngay trong cửa sổ**: góc máy qua vai, tránh fisheye, tay ngoài vùng.
- **Thêm 5 — preset AR theo khung nhìn**: độ cao/phần tử khác nhau cho 45° và 90° (đi cùng `ArViewProfileId`).

### 14.8 Ngân sách hiệu năng: AR‑Lite / AR‑Standard / AR‑Ultra

| Bậc | Có gì | Ghi chú |
|---|---|---|
| `AR‑Lite` | phím, keypress, nốt phẳng tự phát sáng (đúng thứ đang có) | chạy được trên GPU yếu; là mức mặc định khi máy không theo kịp |
| `AR‑Standard` | + shape nốt/min-scale, impact glow, spill | mục tiêu 60 fps ở 1080p |
| `AR‑Ultra` | + pulse, hạt, note light đầy đủ, light bars | cần card thật; `--bench-ar` làm cổng |

Thứ tự cắt khi vượt ngân sách (tất định, test được): **hạt → pulse → note light → light bars → style nốt → impact glow**;
không bao giờ cắt phím/nốt/keypress. Nối vào `GpuAdaptiveQuality` sẵn có.

### 14.9 Cài đặt, preset, mã chia sẻ

- Thẻ AR thêm mục **Look** (bật/tắt từng phần tử + graph), **Output** (mode, màn hình), **Quality** (3 bậc); giữ
  View / Mode / Delay / Calibrate.
- Mã chia sẻ mang look + graph ⇒ cần **`KEYFLOW-LOOK-2`** (đọc được mã bản 1); `docs/SETTINGS-WIRING-AUDIT.md` cập nhật.
- Máy‑cục‑bộ vẫn là: calibration, khung nhìn, delay, màn hình xuất, camera.

### 14.10 Thứ tự thi công (AR‑S0…AR‑S9)

| Slice | Việc | Phụ thuộc | Kiểm chứng | Ánh xạ bản cũ |
|---|---|---|---|---|
| **AR‑S0** ✅ | nền AR‑0/AR‑1: layout, khung mẫu, camera texture, homography, 4 điểm, khung nhìn, vệt phím tự bấm | — | CI xanh (PR #55) | AR‑0, AR‑1.1–1.7 |
| **AR‑S1** ✅ | `PianoSimulation`: activity (floor/ceiling), tắt dần theo pitch + damping, 3 pedal, notes/sec, total | — | test thuần (`PianoSimulationTests`, 11 fact) + `--verify` đọc số (`VerifyPianoSimulation`) | mở rộng AR‑3 |
| **AR‑S2** ✅ | `GraphVariable` + 2 graph đầu của AR: độ rộng và độ hiện của nốt/vệt theo velocity (bảng cài đặt: 2 công tắc + 4 thanh trượt, curve chưa mở) | S1 | test thuần (`GraphVariableTests` 7 fact, `ArGraphTests` 6 fact) + `--verify` (`VerifyArGraphVariable`) | mới |
| **AR‑S3** ✅ | Catalogue phần tử + 3 bậc chất lượng (Lite/Standard/Ultra, Lite giữ nốt/vệt phẳng, Standard thêm ánh phím + impact/note light, Ultra thêm pulse/hạt), `ArLookGuard` (Gem/CrystalOutline → Neon) | S2 | test thuần (`ArElementCatalogTests` 7 fact, `ArQualityTests` 3 fact, `ArLookGuardTests` 3 fact) + `--verify` (`VerifyArQualityTiers`); picker "Quality" trong thẻ AR; presets regenerated | mới (bài học randomizer) |
| **AR‑S4** ✅ (code, 2026‑10‑09; máy thật còn *Unverified*) | **S4a ✅ (2026‑10‑09)**: `Camera/ArSceneProjection.cs` — ma trận 3×3 *cảnh→ảnh* (homography của calibration kẹp giữa ánh xạ affine của scene và của `ArView`, nên một ma trận duy nhất), `Project`/`WeightAt`/`ScaleAt` (= √\|det M\|/w^1,5, số shader cần để đổi cỡ nốt từ DIP scene sang pixel), và `TryProjectRect` hỏi lại `ArProjection` nên hai đường không thể lệch nhau; `w` vẫn = 1 ở góc sau‑trái của phím nên `MinW` giữ nguyên nghĩa. **S4b ✅ (2026‑10‑09, nhánh `VsNote`)**: ma trận vào cbuffer `Frame` (3 hàng + 1 float4 cờ: cờ AR, √\|det M\|, min‑scale px) và nhánh AR trong `VsNote` — chiếu 4 góc của rect qua ma trận, `w` làm clip w, `Local`/`Half` × `ScaleAt` (sàn min‑scale px) để SDF đo bằng pixel thật; **mặc định tắt** (`GpuArInput.ProjectedNotes`), bật lên thì lớp AR thôi giữ note/vệt phẳng và để chính pass nốt vẽ qua phép chiếu. ****Phần còn lại (đã làm)**: nhánh AR trong `VsSprite` — hạt, flare, beam, shape đi qua ma trận, nhóm bị cắt nguyên khi bán kính đĩa chạm chân trời; truncate nốt tại chân trời bằng đúng lát cắt của `ArProjection` (`ArSceneProjection.TruncateToPicture`, Perform bỏ nốt bài hát, vệt phím vẫn vẽ); công tắc **Notes in perspective** (`ArNotesInPerspective`, mặc định tắt) trong thẻ AR. **Min‑scale đúng nghĩa Keysight (2026‑10‑09)**: núm **Minimum note length** (`ArNoteMinScale`, 0–200 % độ rộng nốt, mặc định 0) trong thẻ AR, chỉ hiện khi bật Notes in perspective; `Camera/ArNoteMinimum.cs` kéo nốt ngắn lên tới `minAspect × width`, lớn dần từ đầu nốt (mép chạm phím), và nốt cùng phím đến sau cắt đà của nốt trước ("new note truncates existing note"). Lite không chiếu nốt theo phối cảnh (`ArOptions.NotesProjected`). Sàn magnification `MinScalePx` vẫn là tầng kỹ thuật riêng, mặc định 0. **Shape**: nốt trên sân khấu đã có Gem/Crystal/Neon/Fire nên khi chiếu theo phối cảnh đạt parity qua projection. **Chưa làm**: 5 hình 3D của Keysight (ba mảnh trắng/đen), size graph, nốt mọc theo mảnh. | S1–S3 | test thuần (`ArSceneProjectionTests`, 6 fact: khớp `ArProjection` từng góc ở 4 cách lật/fit, 4 cỡ đàn khớp polygon phím, `w` = 1 ở góc sau‑trái + ma trận đúng là phép ghép, `ScaleAt` = Jacobian, cắt ở chân trời, từ chối đầu vào hỏng) + `--verify` (`VerifyArSceneProjection`: khớp trên chính `GpuSceneLayout` mà renderer dựng, ba cỡ output, lật và không lật); S4b: test thuần `GpuArMapTests` (đóng gói rows/flags vào cbuffer) + `VerifyArNoteProjection` đọc lại điểm ảnh trên WARP — mặc định tắt thì khung không đổi một pixel dù cả bản đồ đã nằm trong cbuffer, bật lên thì từng nốt nằm trong patch của `ArProjection` cho đúng rect đó, có cả ảnh gương; phần còn lại: `ArNoteMinimumTests` (6 fact: kéo theo đầu nốt, cắt bởi nốt cùng phím, không đổi khi đủ dài, min 0 hoặc width hỏng) và `ArOptions`/`PianoVisualSettings` (clamp, Lite không chiếu, look không ghi đè) + `VerifyArNoteProjection` đọc lại điểm ảnh: Perform bỏ ≥500 pixel nốt, min 100 % làm nốt ngắn dài hơn, sprite trên phím được vẽ qua bản đồ, sprite quá chân trời bị cắt nguyên (khung byte‑identical) | **AR‑6.2** |
| **AR‑S5** | Độ cao + thứ tự vẽ; note light/impact light đúng phối cảnh; pulse + hạt trong AR | S4 | pixel + `--bench-ar` | AR‑6.2/6.3 |
| **AR‑S6** | Đầu ra: alpha-effects, tách geometry/glow, stencil-kênh | S4 | ảnh PNG alpha đọc lại | **AR‑6.4** |
| **AR‑S7** | Render-to-video AR: profile, fps, downsample, audio, watermark | S6 | so khung live ↔ render | AR‑6.6 |
| **AR‑S8** | HUD: đếm nốt, pedal, tên nốt, octave lines, light bars | S3 | `--verify` + ảnh | mới (overlay/scene) |
| **AR‑S9** | AR‑2 dò tự động, AR‑2b, AR‑4 tay, AR‑5 ngón, AR‑6.5 webcam ảo | như bản cũ | như bản cũ | AR‑2/2b/4/5/6.5 |

Ưu tiên stream/call của chủ repo (AR‑6.4/6.5) được giữ: **S6 chen ngay sau S4**, không đợi hết S5.
Ước lượng thô: S1–S3 ≈ 3 tuần, S4–S5 ≈ 3–4 tuần, S6–S8 ≈ 3 tuần, S9 như bản cũ. Mọi slice vẫn theo luật §0:
toán thuần + test khung tổng hợp trước, máy thật để **Unverified** cho tới khi chủ repo thử.

### 14.11 Những gì **không** đổi (ràng buộc của chủ repo)

- Phép chiếu phẳng `flat-quad`; **không** camera 3D tự do, không dựng lại đàn 3D.
- `ArDelay` vẫn toàn cục (AR‑3.5 chỉ làm khi chủ repo duyệt).
- Calibration thuộc máy (camera + khung nhìn), preset/mã chia sẻ không đổi nó.
- MIDI là sự thật; không nhận phím từ hình/âm thanh; không model nặng trong gói mặc định.
- Không âm thanh; không telemetry; mọi thứ kiểm chứng bằng khung tổng hợp.

### 14.12 Rủi ro mới

| Rủi ro | Giảm |
|---|---|
| Khối lượng 9 slice (ước 9–11 tuần một người) | làm đúng thứ tự; S1–S3 là logic thuần, rẻ; S4 mới là slice rủi ro |
| Biên tập graph làm UI phức tạp | chỉ 2 handle + 1 curve; mặc định flat; không vẽ đồ thị trong bảng cài đặt |
| Port `Gem`/`CrystalOutline` sang AR dễ vỡ | mỗi style một biến thể emissive; style chưa port thì bị loại khỏi AR chứ không vẽ sai |
| Alpha mode không chạy trên mọi GPU/OBS | giữ stencil-kênh làm đường lui; tài liệu hoá cả hai |
| Hai hệ shader cho cùng hiệu ứng | bắt buộc dùng lại pass sân khấu qua projection (S4), không viết lại hiệu ứng cho AR |
| Độ cao sai theo góc máy | preset AR theo khung nhìn + overlay kiểm tra + ảnh mẫu trong `--verify` |
