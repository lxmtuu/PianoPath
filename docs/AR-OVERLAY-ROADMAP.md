# Roadmap: AR piano overlay & live play

Tài liệu này là lộ trình **đầy đủ** để đưa Keyflow từ "webcam picture-in-picture" lên **AR piano overlay**: camera
quay đàn thật làm nền, nốt rơi và hiệu ứng được vẽ **đúng lên từng phím của cây đàn thật**, phản ứng tức thì
với MIDI khi chơi trực tiếp (live play), và quay/phát lại được. Nó bổ sung cho `docs/ROADMAP.md` (lộ trình chung);
trạng thái hiện tại nằm ở `README.md`.

> **Trạng thái:** bản thảo để chốt hướng đi (2026‑10‑05). Chưa có dòng code nào của lộ trình này được viết; mọi
> con số "hiện có" bên dưới đã đối chiếu với mã nguồn ở commit `2133a19`.
>
> **Tiến độ (2026‑10‑05):** AR‑0.1 (cả hai bước), AR‑0.2 và AR‑0.5 (Python + C#) đã xong; `Homography` đã có sớm cho AR‑1.3. AR‑0.3 và 0.4 cũng đã xong về mã (CI xanh) nhưng **chưa ai thử trên webcam thật** — việc đầu tiên khi có máy là mở camera và đọc dòng trạng thái xem có ra `720p · 60 fps · MJPG` không. AR‑0 như vậy chỉ còn việc thử máy thật. Môi trường làm việc **không có .NET SDK** nên mọi thay đổi C# được xác nhận bằng CI Windows (job `build`, `test`). Tất cả đã qua CI Windows + job `test` (run `37238149495`): bản dựng, `--verify`, ngân sách khung hình, bộ ảnh preset; khung C# khớp PNG Python từng điểm ảnh.

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
| `ArCalibration` | `Camera/ArCalibration.cs` | Dữ liệu: 4 góc (hoặc 6 điểm), layout, camera id + cỡ khung, ngày; đọc/ghi `ar-calibration.json` (bỏ qua tệp hỏng như `PracticeHistory`) |
| `KeyboardDetector` | `Camera/KeyboardDetector.cs` | Dò bàn phím tự động bằng profile 1 chiều (AR‑2) |
| `LatencyProbe` | `Camera/LatencyProbe.cs` | Ước lượng độ trễ camera bằng tương quan chéo năng lượng chuyển động ↔ thời điểm Note On (AR‑3) |
| `HandMask` | `Camera/HandMask.cs` | Mặt nạ da độ phân giải đầy đủ để che hiệu ứng (AR‑4) |

## 4. Các giai đoạn

Ký hiệu: **Phụ thuộc** = phải xong trước. **Kiểm chứng** = lớp bảo đảm theo luật §0 của roadmap chung. Ước lượng theo kiểu của `docs/ROADMAP.md` §6 (một người, đã quen repo).

### AR‑0 — Nền móng (không thấy được gì mới, nhưng mở khoá mọi thứ)

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 0.1 | **`KeyboardLayout` thay hằng số** — ✅ **xong (a) và (b)** | **(a) ✅ xong:** `Stage/KeyboardLayout.cs` — một nơi duy nhất cho hình học (khoảng phím 88/76/61/49, `CenterX`, `Span`, `WhitesBelow`, `PitchAt` theo toạ độ, `Fitting` đoán cỡ đàn từ khoảng nốt đã chơi); thuần, không WPF, được **link vào project test**. `PianoStage` đã lấy `IsBlack`, `BlackKeyOffset`, `WhitesBelow`, `KeyCenters` từ `KeyboardLayout.Standard88` bằng đúng công thức cũ. **(b) ✅ xong (CI Windows xanh, gồm `--verify` và bộ ảnh preset):** cho `PianoShaderScene` và `GpuStageSimulation` (`GpuSceneLayout.Lane`, các vòng `FirstPitch..KeyCount`, `/ 52f`) dùng layout đang chọn thay vì hằng số, đổi `PianoStage.FirstPitch/KeyCount` từ `const` thành giá trị của layout, và sửa `HandTracker.KeyPitch` sang `PitchAt` | (a): `KeyboardLayoutTests` (xUnit, mọi máy) — 88 phím trùng **từng tâm phím** với công thức cũ viết lại trong test, 4 cỡ đều tile kín chiều rộng, phím đen nằm giữa hai phím trắng kề, `PitchAt` quay vòng đúng cho mọi phím; và `VerifyKeyboardLayout` trong `--verify` so layout với `PianoStage`/`PianoShaderScene`. (b): **ảnh stage mặc định không đổi** so với trước (so pixel, CI render) |
| 0.2 | **Cài đặt layout** — ✅ **xong** (thuộc tính tên `KeyboardSize`; `CopyFrom` bỏ qua nó nên preset không đổi cỡ đàn; gợi ý cỡ vừa hiện dưới dòng chọn, tính từ nốt đã chơi trên đàn thật, không tự đổi) | `PianoVisualSettings.KeyboardLayout` ("88 / 76 / 61 / 49"), dòng ở trang Keyboard, có đúng một chỗ tiêu thụ (luật "không chức năng chết"), cập nhật `docs/SETTINGS-WIRING-AUDIT.md`. Layout nên **tự đoán từ MIDI** (nốt thấp nhất/cao nhất đã nghe) và gợi ý, không tự đổi | `--verify` + `check_sources.py` |
| 0.3 | **Camera mode selection** — ✅ **xong, chưa thử trên webcam thật** (luật chọn là `CameraModes.Choose`, thuần, có test: gần 720p nhất → tốc độ cao nhất tới 60 → gần 16:9 → MJPG>NV12>YUY2; định dạng nén lạ như H264 để cuối). Lớp Windows `CameraFrameReader.Modes.cs` liệt kê native type, chọn rồi ép camera sang mode đó trước khi đổi RGB32; mọi lỗi lùi về mode mặc định kèm câu giải thích trên dòng trạng thái | Liệt kê `IMFMediaType` gốc của thiết bị (độ phân giải × fps × định dạng); chọn 1280×720@60 MJPG nếu có, ngược lại 30 fps; để Media Foundation giải mã sang RGB32 (đã có) hoặc NV12. Hiển thị mode thật trong status ("720p · 60 fps · MJPG") | `VerifyCameraOverlay` mở rộng: mode do video file báo vẫn đúng; câu báo lỗi khi thiết bị không có mode yêu cầu |
| 0.4 | **Bỏ trần 30 fps cứng** — ✅ **xong, chưa thử trên webcam thật** (camera trực tiếp đọc theo nhịp riêng, không `Thread.Sleep`; tệp video được giữ theo dấu thời gian của chính nó bằng `FramePacer`; `CameraFrameRing` 3 ô thay buffer dùng chung — sửa luôn lỗi vẽ đọc buffer đang bị ghi; mỗi khung có `LastFrameTicks` (QPC) và `LastSampleTime` cho AR‑3; bộ theo dõi bàn tay chạy tối đa 30 lần/giây) | `CameraLoop` đọc theo nhịp thật của camera (không `Thread.Sleep`), khung mới ghi dấu thời gian (`QueryPerformanceCounter` hoặc `MFSampleExtension`/sample time); ring buffer 3 khung thay cho một buffer có lock | `VerifyCameraOverlay`: đọc 30 khung từ AVI do app ghi, dấu thời gian tăng đều |
| 0.5 | **Khung tổng hợp để test** — ✅ **xong (Python + C#)** | `tools/make_ar_sample.py` vẽ bàn phím từ trên xuống bằng đúng hình học `KeyboardLayout` qua một homography biết trước (phối cảnh, ánh sáng tối góc, nhiễu cảm biến), đủ 4 cỡ (`--keys 88\|76\|61\|49`), deterministic; kèm `ar-keyboard-topdown.json` (4 góc, homography, tâm mặt trên từng phím) làm ground truth. Không dùng ảnh bên thứ ba. **Bộ vẽ C#:** `tests/PianoPath.Tests/SyntheticKeyboardFrame.cs` vẽ lại đúng khung đó cho cả bốn cỡ đàn và mọi góc camera, dùng thẳng `KeyboardLayout`; `SyntheticKeyboardFrameTests` buộc nó khớp PNG Python từng điểm ảnh, `HomographyTests` buộc `Camera/Homography.cs` khớp ground truth (`homography`, 88 `keyTopCentres`). Đã có `Homography` thuần (DLT 4 điểm, nghịch đảo, hợp, từ chối 4 điểm thẳng hàng) dùng cho AR‑1.3 | `scan_generated_assets` của checker (kích thước, 8‑bit RGB, JSON mô tả đúng ảnh); `make_ar_sample.py --check` tự đối chiếu số với layout |

**Phụ thuộc:** không. **Ước lượng:** 1–1,5 tuần. **Rủi ro:** 0.1 chạm nhiều file (stage, GPU, sheet) → làm **một PR riêng, không kèm tính năng**, và dùng ảnh README do CI render làm bằng chứng không hồi quy.

### AR‑1 — AR MVP: camera làm nền, nốt rơi lên đàn thật (căn chỉnh thủ công)

| # | Việc | Chi tiết | Kiểm chứng |
|---|---|---|---|
| 1.1 | **Camera là texture GPU** — 🟡 **phần engine xong** (`GpuCameraTexture`, `GpuStageFeed.Camera`, `ArView` ánh xạ ảnh↔màn hình; ảnh camera được ghép **dưới ánh sáng** ở bước composite nên không bị tone‑map; `VerifyArCameraLayer` đọc lại điểm ảnh trên WARP: fit/fill/mirror/độ mờ/bỏ lớp/chroma). **Nối vào app xong** (xem 1.5: engine là người lấy khung duy nhất khi AR bật; camera đọc không lật, lật lúc vẽ). **Còn:** hai hướng chiều ảnh (camera nhìn từ phía người chơi) | `GpuStageFeed` nhận khung mới (con trỏ ring buffer); `GpuStageRenderer` giữ `ID3D11Texture2D` dynamic (`D3D11_USAGE_DYNAMIC`, `Map(WRITE_DISCARD)`), vẽ **một quad toàn màn hình trước mọi lớp**, có "cover" hoặc "fit". Chuyển nền sân khấu về trong suốt khi AR bật (đã có `TransparentBackdrop`, tái dùng). 1080p BGRA ≈ 8 MB/khung → ~500 MB/s ở 60 fps, chấp nhận được; NV12 + shader đổi màu là tối ưu về sau | `VerifyGpuStage`: khung camera tổng hợp màu đỏ → pixel nền của khung GPU đọc lại là đỏ; tắt AR → như cũ |
| 1.2 | **Wizard Calibrate 4 điểm** — 🟡 **phần logic xong** (`CalibrationQuad.Check/Order`, `ArCalibration`, `CalibrationEditor`; còn phần vẽ/giao diện) | Người dùng bấm 4 góc **vùng phím** trên hình camera (trái‑trên, phải‑trên, phải‑dưới, trái‑dưới của dải từ phím thấp nhất đến cao nhất, thấy ở mép trên phím trắng). Kéo được từng điểm, phím mũi tên chỉnh 1 px, phóng to vòng tròn kính lúp quanh điểm. Đồ hoạ **lưới phím xem trước** vẽ chồng ngay khi kéo để mắt người tự chỉnh | `VerifyArCalibration`: nhập 4 điểm → `Homography` → chiếu tâm từng phím về ảnh → khớp tới 0,5 px với cách vẽ ảnh tổng hợp |
| 1.3 | **`Homography` + áp vào GPU** — ✅ **xong phần nốt** (khác kế hoạch: homography áp **trên CPU** trong `ArProjection`, chính xác kiểu double, có cắt ở đường chân trời; GPU nhận 4 góc + trọng số w mỗi góc và nội suy đúng phối cảnh. `ArNotes` đặt nốt theo thời gian: mép đầu chạm mép sau phím ở giờ bắt đầu, trượt qua phím khi đang kêu; `VerifyArCameraLayer` đọc lại điểm ảnh trên WARP). Đa giác phím sáng là 1.4 | Ma trận vào constant buffer; vertex shader của lớp phím/nốt biến đổi toạ độ "không gian bàn phím" (x theo `KeyboardLayout`, y 0 = mép trên phím, y > 0 hướng lên) sang toạ độ màn hình. Mặc định chế độ **image‑space**: nốt rơi theo phương **thẳng đứng của ảnh** từ mép trên bàn phím lên, x ánh xạ qua homography chỉ ở hàng phím (nốt hơi hội tụ theo phối cảnh). Chế độ **plane** đưa sang AR‑6 | `--snapshot` ra `docs/previews/*/ar-overlay.png` từ khung tổng hợp (CI render, theo luật ảnh README) |
| 1.4 | **Phím thật sáng khi chơi** — ✅ **xong** (khác kế hoạch: không multiply/additive riêng mà **phủ màu có độ trong suốt** lên phím trong ảnh, nên phím trắng ngả màu còn phím tối sáng lên, một cách vẽ cho cả hai; độ sáng lấy từ glow của mô phỏng sân khấu nên lên/tắt mượt như phím sân khấu. `ArKeys` chia mỗi phím thành phần nhìn thấy từ trên: phím trắng = mặt trước + thân giữa hai phím đen, phím đen = một hình chữ nhật; các hình của cả bàn phím lát kín vùng phím đúng một lần, kiểm trên 4 cỡ đàn) | Với mỗi phím đang giữ (đã có `GpuStageFeed` held keys), vẽ **đa giác phím** (qua `KeyboardLayout` + homography) với glow cộng (additive) cho phím tối, **nhân (multiply) + viền sáng** cho phím trắng để không cháy sáng; phím đen vẽ sau phím trắng | `VerifyGpuStage`: giữ phím C4 → vùng pixel quanh toạ độ chiếu của C4 sáng hơn phím kề; phím kề không đổi |
| 1.5 | **Cài đặt & trang AR** — 🟡 **phần mô hình xong** (5 giá trị `ArEnabled/ArMode/ArOpacity/ArLookahead/ArKeyLight` trong `PianoVisualSettings`, `ArOptions` thuần đổi chúng thành độ mờ, kiểu nốt, độ sáng phím; kẹp giá trị, NaN → mặc định; mode lạ → Learn; **preset/mã chia sẻ không đổi chúng**, nhưng mã chia sẻ vẫn mang chúng như mọi cài đặt khác. Perform ở bước này chỉ tắt nốt sắp tới; hiệu ứng bay lên là AR‑6.2). **Công tắc đã nối vào luồng vẽ** (`ArPlan` quyết định theo công tắc, engine GPU, camera mở, calibration, nền chroma: không có calibration thì camera là nền và stage như cũ; có calibration thì engine vẽ nốt + phím lên camera, nốt/phím/hiệu ứng ở vị trí phím ảo của stage tự lùi (`GpuLook.From(overCamera)`), engine là người đọc duy nhất của ring camera, camera đọc ảnh không lật; theo dõi bàn tay tạm dừng khi AR dùng camera — chung sống với AR là AR‑5). **Thẻ AR OVERLAY trong Camera & FX xong** (công tắc, Mode, độ đặc, nhìn trước, độ ngả màu phím, RESET CALIBRATION, dòng trạng thái theo `ArPlan`; chuỗi tiếng Việt; test qua dock). **Còn:** nút CALIBRATE và wizard 4 điểm (1.2, phần giao diện) Trang mới **AR** trong dock (hoặc thẻ trong Camera & FX): công tắc **AR overlay**, **Mode** (Learn / Perform), **Calibrate**, **Reset calibration**, opacity lớp AR, nốt chỉ bắt đầu hiện cách phím bao nhiêu giây (`Lookahead`), Mirror (đã có). Mọi dòng có tooltip + khoá chuỗi tiếng Việt (`tools/add_string.py`) | `check_sources.py` (khoá chuỗi), `SETTINGS-WIRING-AUDIT.md`, `VerifyDockAccessibility` |
| 1.6 | **Lưu calibration** — ✅ **xong phần lưu và nối** (`ArCalibrationStore` + `ArCalibrationSession`: cửa sổ báo camera nào đang mở, cỡ ảnh, cỡ đàn trong cài đặt; phiên tìm calibration đã lưu, báo `Ready` / `NotCalibrated` / `Outdated` (cùng camera nhưng đổi độ phân giải hoặc cỡ đàn) / `NoSource`; lưu, xoá, ảnh chụp bất biến cho luồng vẽ đọc; khoá theo ảnh gốc của camera, **không** theo Mirror). Còn nút bấm ở trang AR | `ar-calibration.json` trong thư mục cài đặt, khoá `(camera.Link, width×height, số phím)` — thêm số phím so với kế hoạch đầu vì vùng phím phụ thuộc cỡ đàn. Mở lại camera cũ → tự nạp; đổi độ phân giải → báo cần căn lại. Preset/mã chia sẻ không mang | `VerifyArCalibration`: round‑trip, tệp hỏng bị bỏ qua, preset apply không đổi calibration, mã chia sẻ không chứa nó |
| 1.7 | **Ghi hình AR** | Vì camera nay nằm trong GPU pass, `GpuRecordingTap` (đúng cỡ, đúng fps) ghi luôn nền camera + lớp AR vào AVI/MP4. Tiếng vẫn do `IAudioTrack` | `VerifyMp4Recorder`/tap: khung ghi có pixel nền camera |

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
| 3.1 | **Visual sync offset** | Slider **Camera delay compensation** (0–300 ms, mặc định 0). Cách bù: **trì hoãn phần hiệu ứng theo MIDI** (phím sáng, hạt) cho khớp video — **không** trễ âm thanh (tiếng phải ra ngay). Nốt rơi trong Learn mode tính theo vị trí bài nên dịch trước theo offset (nốt chạm phím khi *tay trên video* chạm phím) | `VerifyGpuStage`: offset 100 ms → hit xuất hiện đúng 100 ms sau trên đồng hồ GPU; tiếng không bị trễ |
| 3.2 | **Đo tự động** (`LatencyProbe`) | Người chơi bấm ~20 nốt bất kỳ. App tính **năng lượng chuyển động** (|khung − khung trước|) trong dải phím theo thời gian và **tương quan chéo** với chuỗi thời điểm Note On → độ trễ tốt nhất ∈ [0, 400 ms]. Báo độ tin cậy; thấp thì đề nghị thử lại | test tổng hợp: chuỗi Note On ngẫu nhiên + chuyển động trễ 83 ms + nhiễu → ước lượng ±1 khung |
| 3.3 | **Đo độ trễ của chính app** | `--bench-ar`: nối vào `--bench` hiện có, thêm nguồn camera tổng hợp 60 fps; báo p95 thời gian khung với pass camera + AR; perf gate trong CI giữ giới hạn như đã có | `--bench` JSON có mục `ar` |
| 3.4 | **Ghi hình giữ đồng bộ** | Khung camera ghi theo **dấu thời gian đã bù**, không theo thời điểm render; video + tiếng lệch ≤ 1 khung trong tệp MP4 | `VerifyMp4Recorder` mốc thời gian |

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
| 6.2 | **Perform mode** | Không có nốt tương lai. Mỗi nốt vừa chơi **bay lên** khỏi phím (vệt, hạt, flash theo vận tốc MIDI) bằng các hiệu ứng có sẵn của `EffectCatalog` (kết hợp theo "combo themes") — chọn một **preset AR** riêng, ví dụ "Neon Hands", "Sakura Stage". Hiệu ứng nặng (blur toàn khung) bị tắt tự động trong AR để giữ 60 fps | preset trong `presets/` qua `VerifyCommunityPresets` |
| 6.3 | **Chế độ mặt phẳng (plane / 3D)** | Thay image‑space bằng nốt đứng trong **một mặt phẳng vuông góc với bàn phím** tại mép sau phím: cần ước lượng tư thế (PnP) từ homography + FOV gần đúng (slider **Camera FOV**, mặc định 70° ngang, hiệu chỉnh bằng 2 nốt ở xa). Làm sau cùng vì rủi ro thẩm mỹ cao và nằm sát vùng "3D camera toàn phần" trong danh sách *không làm* — chỉ làm nếu 1–5 cho phản hồi tốt | `VerifyArCalibration` PnP trên dữ liệu tổng hợp; lùi về image‑space khi FOV sai |
| 6.4 | **Cửa sổ AR cho OBS / máy chiếu** | Mở rộng "OPEN GPU STAGE WINDOW": cửa sổ có camera + AR đã ghép, F11, chọn màn hình; tuỳ chọn nền trong (alpha) để OBS dùng Window Capture với Allow Transparency | `VerifyGpuStage` swap chain |
| 6.5 | **Webcam ảo** *(kéo lên v1.3 theo quyết định §8.5: vừa quay vừa stream/call)* | Windows 11 có `MFCreateVirtualCamera`: đẩy khung AR đã ghép thành camera ảo để Zoom/Meet/Teams chọn được, không cần OBS. **Chỉ Windows 11 22H2+** (Windows 10 dùng OBS Virtual Camera qua 6.4), cần đăng ký COM media source nên có thể cần ký mã. Khung ra phải đi cùng luồng với ghi hình để quay và stream cùng lúc không tốn gấp đôi GPU | `Mf.VirtualCameraProbe` SKIP khi không có API |
| 6.6 | **Xuất sidecar thông tin** | Cùng với MP4, ghi `take.ar.json` (layout, calibration, offset) để dựng lại/ghép lại trong công cụ khác | round‑trip |

**Phụ thuộc:** 6.1–6.2 sau AR‑3; 6.3 sau AR‑2; 6.4 sau AR‑1; 6.5 sau 6.4. Vì cần **vừa quay vừa stream**, 6.4/6.5 vào v1.3 và nên bắt đầu ngay khi AR‑1 xong. **Ước lượng:** 3–4 tuần.

## 5. Bản đồ phiên bản (đã chỉnh theo §8)

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
