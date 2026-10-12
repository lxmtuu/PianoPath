# Roadmap: AR piano overlay & live play

Tài liệu này là lộ trình **đầy đủ** để đưa Keyflow từ "webcam picture-in-picture" lên **AR piano overlay**: camera
quay đàn thật làm nền, nốt rơi và hiệu ứng được vẽ **đúng lên từng phím của cây đàn thật**, phản ứng tức thì
với MIDI khi chơi trực tiếp (live play), và quay/phát lại được. Nó bổ sung cho `docs/ROADMAP.md` (lộ trình chung);
trạng thái hiện tại nằm ở `README.md`.

> **Trạng thái (2026‑10‑10):** AR‑0, AR‑1 và AR‑S1…S4 hoàn tất về mã; Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), commit `95c5aee`, đã PASS static, Release build, full `--verify`/WARP và portable tests. WARP xác nhận các đường synthetic/calibrated, không phải thử webcam thật. AR‑S4 vẫn **Unverified trên webcam thật**; S5 có selected Standard/Ultra, impact/release/pose/direction và hold-modifier WARP checks đã PASS, còn family/parameter matrix vẫn `Unverified`. S6.2 Geometry+Glow readback cũng PASS trên WARP, nhưng OBS blend thực tế, GPU phần cứng, camera và performance chưa được xác nhận. Calibrate giữ gizmo Move/Rotate/Scale cùng thanh trượt số; calibration v5 lưu translation, uniform scale, pitch/yaw/roll, FOV và baseline; v1–v4 vẫn migrate như mô tả dưới đây. Ambient petals/sao băng vẫn phẳng screen-space. Nguyên nhân lỗi webcam lịch sử chưa xác định; các giới hạn cũ về pose 3D không còn là yêu cầu hiện hành — xem §14.7, §14.10 và §14.11.
>
> **AR‑S4 — mã xong, kiểm tra phần cứng còn mở:** bật **Notes in perspective**, đặt **Minimum length, share of width** ở 100–200%, kiểm tra nốt ngắn trong Learn; trong Perform, không có nốt bài hát tương lai: key tint độc lập, rising MIDI note objects có toggle riêng, và live press bars vẫn có toggle riêng mặc định tắt. Không có webcam/GPU thật ở sandbox nên không thể đóng mục *Unverified* thay chủ repo.
>
> **Mục tiêu hình ảnh mới (2026‑10‑10; hai ảnh tham chiếu do chủ repo gửi):** ảnh A là camera cận cảnh bàn phím/người chơi với cue sáng theo hàng phím và các cột impact/hạt MIDI mọc từ đúng phím; ảnh B là các nốt màu dạng khối nổi/rơi theo phối cảnh tới đúng vùng phím. Đây là đích về hình học, chuyển động và cảm giác AR, không phải cam kết sao chép pixel hay màu chính xác. Chủ repo đã chốt: khi MIDI im chỉ thấy phím vật lý qua camera — **không** bàn phím ảo tĩnh. Hiệu ứng live phải được chiếu theo pose 3D; ambient petals/meteors vẫn phẳng trên khung hình và không được dùng làm hiệu ứng bàn phím. Thứ tự gate S6.2 rồi S5 đã được giữ; cả hai có WARP readback PASS trên CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), còn OBS/webcam/hardware chưa xác minh.
>
> **Cập nhật code Calibrate (2026‑10‑10; CI-verified):** thêm reset dạng circular-arrow cho từng control (orientation, FOV, pitch/yaw/roll, position X/Y/Z, scale, tool, note tilt/azimuth); đổi Rotate gizmo sang hit-test theo cung vòng đã chiếu và trục Euler cố định để tránh góc polar quanh tâm màn hình làm vòng trôi. `CalibrationWizardTests` và `VerifyCalibrationWindow` đã PASS trong Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330); AR‑S4 vẫn *Unverified* cho tới khi thử webcam thật.
>
> **Ưu tiên stream:** AR‑S6 được code trước AR‑S5. **S6.1 alpha-effects** có đầu ra Direct3D BGRA trong suốt; WARP pixel/readback tests PASS trong CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330). **S6.2 geometry+glow** render một scene rồi xuất đồng bộ Geometry và bloom-only Glow qua hai output; WARP readback xác nhận hai lớp khác nhau và trong suốt trên cùng CI. Manual blend Add/Screen trong OBS thật vẫn Unverified. S5 được mở rộng sau gate S6.2; các nhánh WARP đại diện cho impact lights, Ultra effects, release, pose/direction và hold modifiers đã PASS, nhưng family/parameter coverage vẫn chưa đầy đủ. Stencil-channel vẫn còn. **Cánh hoa/sao băng và các lớp ambient vẫn phẳng trên khung hình, không chiếu phối cảnh.** S6 không phải đầu ra Zoom/Meet trực tiếp — virtual camera vẫn thuộc S9.
>
> Sandbox hiện tại **không có .NET SDK, webcam hay GPU thật**; Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) đã chạy static, Release build, full `--verify`/WARP và portable xUnit PASS. WARP chỉ xác nhận synthetic code paths; AR‑S4 vẫn *Unverified* cho tới khi chủ repo thử webcam thật. §11 ghi nhận thử nghiệm camera trước đây; §12–§13 đối chiếu thiết kế/lịch sử; §14 là bản thiết kế lại và thứ tự AR‑S0…S9. §4/§5/§13 giữ lại để tra cứu lịch sử.
>
> **Cập nhật 2026‑10‑09:** AR‑1.1–1.7 đã xong (PR #55: camera texture, homography phẳng, calibrate theo **khung nhìn**, nốt bài hát, phím sáng và vệt cho phím người chơi tự bấm trong Learn/Perform). §14 là thiết kế hiện hành từ 2026‑10‑09. **Cập nhật 2026‑10‑10:** hoàn thiện UI gizmo Move/Rotate/Scale, translation + uniform scale và lưu calibration v5; thêm mũi tên hướng nốt chiếu lên camera preview cùng phép căn screen-up/chiều sâu. CI build/portable tests/full `--verify`/WARP PASS trong run [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330); AR‑S4 webcam vẫn *Unverified*. Đây là cập nhật yêu cầu hiện hành, không bị giới hạn bởi các ghi chép cũ ở §4–§13.
> **AR note diagnosis + Perform MIDI stream, 2026‑10‑10 (local changes; verification pending):** the previous camera note path passed sRGB note colours to a shader expecting linear RGB and drew a generic flat quad rather than Live Stage's SDF/material body; that explains the colour and missing sheen in the screenshot. AR song/live notes now share `ShadeNoteMaterial`, use the stage's `ToLinear` conversion, and emit shimmer inside each calibrated pitch patch. Released 3D live bars are retained until their `Reach / FallSpeed` travel limit instead of being culled at the 2D stage edge. In Perform, a new toggle enables a distinct calibrated 3D note for each MIDI Note On; the note moves from onset, freezes its hold-derived length at Note Off, then continues for a fixed three-second reach (independent of Learn's lookahead). It uses note width/fade velocity graphs and the shared note material; optional live press bars remain an independent, default-off toggle. Geometry/unit, settings/UI, and WARP readback regressions are added, but this sandbox has no .NET SDK/Windows toolchain, so they are **not yet run** and nothing from this change has been pushed. No `Native` family claim is added from source-only coverage. Webcam, OBS, physical GPU and hardware performance remain unverified.

## 0. Mục tiêu, và thế nào là "xong"

| | |
|---|---|
| **Người dùng cần gì** | Đặt webcam/điện thoại nhìn vào bàn phím, bấm **Calibrate** một lần, rồi thấy nốt rơi xuống đúng phím thật; chơi MIDI thì phím thật "sáng" ngay; ghi lại hoặc đưa vào OBS/Zoom. |
| **Hai chế độ dùng** | **Learn** — nốt sắp tới rơi vào phím thật, có wait mode, chấm điểm, chỉ ngón. **Perform** — chơi tự do: phím sáng; mỗi MIDI Note On có thể tạo một nốt 3D riêng bay lên, tiếp tục sau Note Off; live press bars là toggle riêng, mặc định tắt. |
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
| **Căn chỉnh thuộc về máy, không thuộc về preset** | Calibration lưu theo (camera, độ phân giải), giống GPU FPS/VSync: áp preset **không** đổi nó; mã chia sẻ preset (`KEYFLOW-LOOK-2`, đọc được cả mã bản 1) **không** mang nó (giống đường dẫn ảnh nền) |
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

> ⚠ **Từ 2026‑10‑09, bản có hiệu lực là §14** (thiết kế lại theo tham chiếu Keysight; cập nhật pose 3D tự do ngày
> 2026‑10‑09 tại §14.7, §14.10–§14.11). Các mục AR‑0…AR‑6 dưới đây là lịch sử; những giới hạn camera/rotation ở đó
> không được dùng để thu hẹp yêu cầu hoặc thiết kế hiện hành.

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
| 1.4 | **Phím thật sáng khi chơi** — ✅ **xong** (khác kế hoạch: không multiply/additive riêng mà **phủ màu có độ trong suốt** lên phím trong ảnh, nên phím trắng ngả màu còn phím tối sáng lên, một cách vẽ cho cả hai; độ sáng lấy từ glow của mô phỏng sân khấu nên lên/tắt mượt như phím sân khấu. `ArKeys` chia mỗi phím thành phần nhìn thấy từ trên: phím trắng = mặt trước + thân giữa hai phím đen, phím đen = một hình chữ nhật; các hình của cả bàn phím lát kín vùng phím đúng một lần, kiểm trên 4 cỡ đàn; **và từ 2026‑10‑08** mỗi phím người chơi bấm còn kéo theo một **vệt** của chính phím đó — `ArNotes.BuildLive` đặt vệt từ mép sau phím chạy ra theo trục `v` của bàn phím, dài dần khi giữ (tối đa theo AR Reach cố định 3 giây, độc lập Learn Lookahead), mờ dần rồi tắt sau khi nhả; vệt là dấu của tay người chơi nên có ở **cả Learn và Perform**, và `GpuStageSimulation.LivePresses` là nguồn dữ liệu) | Với mỗi phím đang giữ (đã có `GpuStageFeed` held keys), vẽ **đa giác phím** (qua `KeyboardLayout` + homography) với glow cộng (additive) cho phím tối, **nhân (multiply) + viền sáng** cho phím trắng để không cháy sáng; phím đen vẽ sau phím trắng | `VerifyGpuStage`: giữ phím C4 → vùng pixel quanh toạ độ chiếu của C4 sáng hơn phím kề; phím kề không đổi |
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
| 6.2 | **Perform mode** | Không có nốt tương lai. Mỗi MIDI Note On có thể tạo một nốt 3D riêng chạy lên theo hướng AR, đi tới Reach cố định 3 giây độc lập Learn lookahead; Note Off cố định độ dài note nhưng không dừng đường chạy. Live press bars giữ toggle riêng mặc định tắt và dùng AR Reach cố định 3 giây, độc lập Learn Lookahead; không vẽ falling-note trails phẳng qua camera. Hiệu ứng nặng (blur toàn khung) bị tắt tự động trong AR để giữ 60 fps. AR projected-note path gọi chung shader `ShadeNoteMaterial` với Live Stage và chuyển màu sRGB sang linear; falling trails, ghost copies và landing glow của nốt Learn được dựng bằng calibrated 3D patches thay vì sprite phẳng; `Gem`/`CrystalOutline` now pass through the shared shader; new WARP style-path assertions await CI, and full material-setting parity is not verified. **Cập nhật S5 trước đó (selected WARP cases PASS; full matrix remains Unverified):** `GpuLook.From(overCamera: true)` giữ cấu hình + simulation state của Live Stage nhưng `BuildSprites` vẫn chặn sprite stage-space; `GpuArLayer` chiếu các effect state có pitch-tagged theo pose 3D đã calibration. Lần sửa hiện tại thêm stream rising MIDI note riêng, option, pose-projected note trails/ghosts/landing glow và regression tests hình học/WARP; chúng chưa chạy trong sandbox. Standard/Ultra và Lite vẫn giữ gate cho transient families; rising notes là note objects có pose và được giữ ở mọi tier. Full style/parameter matrix, webcam thật và sign-off vẫn còn mở. | `VerifyArNoteProjection` trên WARP; portable geometry tests; presets qua `VerifyCommunityPresets` |
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
| 6 | `ArDelay` (3.1) là **một số cho cả máy**, còn độ trễ phụ thuộc camera (webcam USB ≈ 60–150 ms, OBS Virtual Camera/điện thoại làm webcam khác hẳn) | ✅ **đã làm (AR‑3.5, 2026‑10‑11)** | `Camera/ArDelayStore.cs` giữ độ trễ theo `camera.Link` trong `ar-delay.json` cạnh cài đặt (cùng khoá với file calibration; kẹp theo dải của thanh trượt; tệp hỏng thì quên); `Camera/ArDelaySession.cs` là nửa thuần — rồi một camera thì ghi giá trị thanh trượt vào nó, mở lại thì nạp lại độ trễ của chính nó. Dây nối ở `ApplyArSource` (nạp khi đổi camera, cùng chỗ calibration đổi), `VisualSlider_ValueChanged` và nhánh không-slider của `SetArDelay` (kéo thanh trượt hay chấp nhận MEASURE DELAY đều ghi vào camera đang mở). Kiểm chứng: `ArDelayStoreTests` + `ArDelaySessionTests` (project xUnit, chạy cả Linux lẫn Windows) |
| 7 | Webcam ảo của chính Keyflow (6.5) sẽ hiện trong danh sách camera của chính nó ⇒ vòng lặp hình | 🟡 **ràng buộc cho 6.5** | Khi làm 6.5: nhận diện tên "Keyflow Virtual Camera" trong `CameraRanking.IsVirtual`, **không bao giờ** để "Tự động" chọn nó và ẩn/đánh dấu nó khỏi danh sách nguồn. Thêm test vào `CameraRankingTests` cùng PR |
| 8 | Mở camera chạy trên luồng riêng nhưng `StartCameraOverlay` vẫn **chờ** (tối đa 20 giây) ở luồng UI; tự mở lại khi cắm/rút camera làm cửa sổ khựng 1–3 giây | 🟡 chấp nhận được, ghi lại | Cùng chi phí như trước nhưng giờ có thể tự xảy ra. Nếu gây khó chịu: chuyển sang mở bất đồng bộ (luồng báo về UI bằng `Dispatcher`) |
| 9 | Vòng đọc danh sách 3 giây gọi `MFEnumDeviceSources` đều đặn khi camera đang chạy | 🟡 **chưa đo trên máy thật** | Đã chặn khi ghi/đo/đánh dấu. Nếu thấy rớt khung ở 60 fps: nới chu kỳ hoặc chỉ đọc khi cửa sổ Cài đặt mở |
| 10 | 1.1/1.6: engine là bên duy nhất lấy khung khi AR bật; calibration và `ApplyArSource` dùng `reader.Source` | ✅ ổn | Sau khi lùi sang camera khác, `Source` là camera **thực sự** đã mở nên calibration tìm đúng bản ghi (hoặc báo `NotCalibrated`) |
| 11 | 3.4 ghi hình (âm bắt đầu muộn d) và 1.7 (`CameraAgeMeter`) | ✅ ổn | Không chạm vào đường ghi; đổi camera giữa lúc ghi đã bị chặn (#4) nên d lấy lúc bắt đầu vẫn đúng suốt bản ghi |
| 12 | 2b (camera từ phía người chơi, điện thoại làm webcam) | ✅ ổn | Điện thoại làm webcam (Iriun, DroidCam, Camo, EpocCam) xếp vào "camera màu"; chọn tay trong danh sách luôn được |

**Còn phải thử trên phần cứng thật** (không có trong CI): Camera đa mode (720p60 vs lùi mode mặc định), bật/tắt OBS Virtual Camera khi app đang mở, hai webcam cùng loại (hậu tố `(2)`), tên lấy từ registry, độ khựng khi tự mở lại, p95 của cảnh `ar` trên card thật.

## 12. Hai ảnh mẫu của chủ repo ↔ từng cài đặt hiện có (2026‑10‑06)

Chủ repo đưa hai ảnh cần đạt: **(A)** nhìn thẳng từ trên xuống, nốt/khói/hạt bay lên từ các phím thật, tay nằm trên phím; **(B)** nhìn từ phía người chơi lên một cây đàn grand, nốt và chùm sáng **đứng** trong không khí trước đàn, có phối cảnh. Kết luận sau khi đọc `ArPlan`, `GpuLook`, `GpuArLayer`, `ArProjection`, `StageShaders.hlsl`: **làm được cả hai với webcam, không cần nền tảng hay thư viện mới**, nhưng chúng cần hai khối việc khác nhau.

**Đường render hiện tại (đọc từ mã, không phải đoán):**

1. `ArPlan.Decide` chỉ cấp `GpuArInput` khi AR bật, Direct3D hoạt động, camera mở, nền không phải chroma và calibration `Ready`; `GpuStageRenderer` còn kiểm camera view hợp lệ + kích thước ảnh khớp calibration.
2. `GpuLook.From(overCamera: true)` vẫn giấu nốt/phím stage-space và `GpuStageSimulation.BuildSprites` thoát trước mọi sprite không hiệu chỉnh; các setting Live Stage và pitch-tagged simulation state được giữ cho lớp AR. Vì vậy camera không nhận bàn phím ảo tĩnh hoặc falling-note trails 2D lệch đàn.
3. Song notes và live bars đi qua `GpuArLayer`/`Ar3DNoteProjection`; notes-in-perspective và note direction độc lập với pose, live effects key-anchored theo pose ngay cả khi note perspective tắt. AR note quads dùng chung `ShadeNoteMaterial` và chuyển màu như Live Stage; regression mới sample đủ sáu style trên calibrated quads và Gem/Crystal trên flat AR quads. Các assertion mới đang chờ CI; parameter/value matrix đầy đủ, webcam và parity phần cứng vẫn Unverified.
4. S5 chiếu impact lights + note-light spill ở Standard; Ultra thêm ring/morph/flash, burst/continuous particles, release FX, hold arcs và keyed beams từ cùng pitch-tagged `GpuStageSimulation`, không chiếu sprite stage-space phẳng. Selected paths/variants đã PASS build/WARP/`--verify` trong CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330); các họ/style/parameter chưa đủ coverage để đổi 14 family khỏi `Unverified`, và webcam thật vẫn cần thử.
5. Ambient petals, meteors và shooting stars vẫn được render flat/screen-space, không theo keyboard pose. `HandTracker` vẫn chỉ đọc một bàn tay bằng luật màu da YCbCr; chưa có mặt nạ để che hiệu ứng.

**Kiến trúc đang dùng:** giữ pinhole `ArCameraPose` + FOV và full keyboard-local transform; dùng một AR quad pass chiếu CPU-side các billboard/key-plane ribbons theo camera pose. Không mở lại nhánh scene→image flat homography làm gate, và không chiếu `VsSprite` stage-space lên camera. Pose của piano, hướng notes và ambient screen-space là ba đường độc lập.

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

**Còn nợ kỹ thuật đã biết:** `ArDelay` đã lưu theo từng camera (AR‑3.5, 2026‑10‑11), không còn toàn cục; mở camera chặn luồng giao diện tới 20 s; `StartCameraOverlay` đồng bộ; AR không áp `NoteStyle` và các hiệu ứng (§12) — ma trận *cảnh→ảnh* đã có (**AR‑S4a**, `Camera/ArSceneProjection.cs`, kiểm bằng `ArSceneProjectionTests` + `VerifyArSceneProjection`) và đã nối xuống GPU cho pass nốt (**AR‑S4b**: nhánh AR trong `VsNote`, `w` làm clip w, **mặc định tắt** — `GpuArInput.ProjectedNotes`, kiểm bằng `GpuArMapTests` + `VerifyArNoteProjection` đọc lại điểm ảnh trên WARP); còn thiếu nhánh AR trong `VsSprite`, shape/truncate của nốt, min‑scale đúng nghĩa và công tắc nối pass nốt đó vào giao diện.

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
| 1 | Hai họ phần tử: Scene (tĩnh) và Effect (sinh theo nốt) | Catalogue AR tường minh: mỗi phần tử có `ArElement` (họ), bậc chất lượng và pass vẽ | ✅ `ArElementCatalog` / `ArQualityTier`; các phần tử vẫn hoàn thiện theo slice |
| 2 | **Biến graph** cho mọi tham số | `Stage/GraphVariable.cs`: `Flat\|Graph` + `Start/Stop/Min/Max/Curve`, `Sample(input 0..1)` | ✅ AR‑S2: hai graph velocity cho độ rộng và độ hiện của nốt/vệt; graph cho hiệu ứng AR còn thiếu |
| 3 | **Simulation** dùng chung cho mọi hiệu ứng | Hợp đồng `PianoSimulation` cho activity/damping/pedal/notes-per-second; `GpuStageSimulation` giữ state render và transient MIDI | ✅ AR‑S1; S5 hiện lưu riêng impact gần đây để chiếu effect AR theo pose |
| 4 | **Thứ tự theo độ cao Z**, mặc định là cho 2D | Pose camera pinhole + mặt phím keyboard-local; project bốn góc của note/effect thay vì dựa vào một mặt phẳng màn hình | 🟡 `ArCameraPose` / `Ar3DNoteProjection` đang hoạt động cho note, live bar và S5 transient; chưa có catalogue độ cao chung |
| 5 | Nốt: mọc theo mảnh, min-scale, truncate, size graph | Port vào pass note AR, giữ min-length theo bề rộng phím chiếu và hướng 3D độc lập | 🟡 `Ar3DNoteProjection.Build`; shared Live Stage note-material shader đã nối vào AR, regression WARP đang được mở rộng cho sáu style; min-scale còn thiếu |
| 6 | Keypress: phủ phím + fade hai chiều | Key tint luôn độc lập; live bars là toggle riêng, chiếu theo pose/hướng note khi bật | ✅ **hoàn tất 2026‑10‑12**: tint và projected bars có; **fade hai chiều ✅ (2026‑10‑11)**: `Bar fade follows lifetime` mờ vệt theo vòng đời — xuôi khi giữ, ngược lúc nhả; **coverage ✅ (2026‑10‑12)**: thanh trượt `Bar coverage on the keys` 25–300 % (đầu vệt đi vào thân phím, 200 % = phủ toàn bộ) sau chevron `MORE · LIVE BAR COVERAGE`; **màu theo velocity ✅ (đã có từ 2.0.0, đo lại 2026‑10‑12)**: strength thật của vệt đi qua tint `VelocityColor` của look (`LiveNoteColor`), kiểm bằng WARP; **ánh sáng một cú bấm, đúng một lần ✅ (2026‑10‑12)**: vệt live ở hướng rơi xuống từng bắn thêm một cú impact khi đầu vệt rơi chạm hàng phím — ánh lặp đôi dù tay không nhấn thêm; nay ngón tay là nguồn sáng duy nhất, kiểm bằng block WARP mới trong `PianoStage_GpuCore` (2,8 s sau bấm rơi xuống vẫn không có impact/ring/flash nào mới); **đánh bóng đánh sáng ✅ (2026‑10‑12)**: flash impact nở vào ~55 ms và tắt dần êm (`FlashEnvelope`), sáng/kích thước theo velocity rõ rệt hơn — dải nhạc động rộng gấp ~3 (`ImpactDynamic`/`ImpactSizeBoost`/`FlashStyleStrength`), tint phím giữ lâu "thở" nhẹ qua `HoldBreathAmp`/`KeyBreathMultiplier` (đọc chung buffer `KeyColors` nên áp đồng thờ lên Live Stage lẫn AR); các đường cong khoá bằng xUnit `GpuStageSimulationLightCurvesTests` |
| 7 | Impact light: đèn ở hàng phím, sáng theo velocity, kéo dài theo pedal | Blob ánh sáng projected tại phím MIDI vừa đánh, có strength/lifetime độc lập với live bars | 🟡 S5 Standard impact/note-light output có WARP counter/pixel sample PASS; graph/pedal sustain và camera tuning chưa hoàn tất |
| 8 | Note light: hắt sáng xuống; nghiêng thì lệch | `spill` của sân khấu 2D chưa đủ; AR cần project vùng spill trên mặt phím đã calibration | 🟡 2D spill còn trong stage; spill riêng cho camera AR chưa có |
| 9 | Màu theo **kênh MIDI** = tách hai tay | Thêm chế độ `Channel` (mỗi tay một màu) + `Activity`/`Notes‑sec` | 🟡 hiện có màu theo track và velocity tint; MIDI-channel mode còn thiếu |
| 10 | Trên ảnh camera chỉ dùng được material **emissive/alpha** | AR dùng shared `ShadeNoteMaterial` trên camera-facing quads; hình học vẫn khác mesh stage. | 🟡 WARP regression mới sample sáu style trên pose 3D và Gem/Crystal trên flat AR path (CI đang chờ); full parameter/value matrix, webcam và hardware chưa xác minh |
| 11 | Mức đồ hoạ (Low→Ultra): bậc thấp **bỏ đèn, nốt tự phát sáng** | `AR‑Lite / AR‑Standard / AR‑Ultra` với catalogue gate rõ ràng | ✅ AR‑S3 quality tiers và UI đã có; Lite giữ flat quads |
| 12 | Đầu ra: stencil/alpha, tách glow, render-to-video | Alpha-effects cho OBS; geometry direct-scene + glow/bloom riêng; stencil/render-to-video về sau | 🟡 S6.1/S6.2 WARP alpha + geometry/glow readback PASS; manual OBS blend, stencil và render-to-video chưa xác minh |
| 13 | Căn chỉnh: ảnh alignment, phát hiện xê dịch, khuyến nghị góc máy, fisheye | Bốn điểm + FOV + pose 3D có thể lưu, mở lại và chỉnh trong Calibrate; drift/fisheye vẫn còn | 🟡 pose/gizmo hiện có; thử webcam thật và tự dò drift còn thiếu |

### 14.2 Kiến trúc 5 tầng

```
MIDI ─► PianoSimulation ──► Element spawner ──► Effect elements (nốt, keypress, impact,
         (activity, damping,      │               note light, pulse, hạt)
          pedal, notes/s)         │  tham số = GraphVariable.Sample(input)
                                  ▼
    Colour modes (Static/Channel/Velocity/Activity/…)   ArCameraPose: keyboard-local 3D → camera pixels
                                  │                     (pinhole FOV + calibrated plane correction)
    Scene elements (camera, phím, damper, octave lines, light bars, HUD)
                                  ▼
                    AR passes ─► Output modes (composite / alpha / Geometry+Glow / stencil / render)
```

| Tầng | Nội dung | Hôm nay |
|---|---|---|
| 1. Simulation | activity có floor/ceiling, tắt dần theo pitch + damping, 3 pedal, notes/sec, total | ✅ `Stage/PianoSimulation` (AR‑S1) — `GpuStageSimulation` lo glow/heat/spill và lịch sử impact AR |
| 2. Tham số | `GraphVariable` + catalogue đầu vào (velocity, tiến trình, pitch, notes/sec, activity, damping) | 🟡 AR‑S2: hai graph velocity có; graph cho tham số AR khác còn thiếu |
| 3. Phần tử | Scene + Effect (§14.3), quality gate và pass vẽ; độ cao 3D theo từng phần tử: bảng cm đã khóa (`ArElementHeights`, 2026‑10‑12) — impact 0,2 cm, pulse ≥ 0,3 cm | 🟡 `ArElementCatalog`/`ArQualityTier` đã có; catalogue chưa đầy đủ |
| 4. Chiếu | Pose pinhole + hiệu chỉnh mặt phẳng; từng góc của note/effect đi qua saved keyboard pose/FOV | 🟡 active 3D note/live-bar/S5 transient pass; selected note/S5 routes pass WARP; không phụ thuộc `ArSceneProjection` horizon |
| 5. Đầu ra | composite / alpha-effects / geometry+glow / stencil / render / camera ảo | 🟡 S6.1 alpha và S6.2 Geometry+Glow có WARP readback PASS; stencil/render chưa có, manual OBS validation còn thiếu |

**Độ cao (analog của Z-axis).** Mọi phần tử AR có `Height` tính bằng cm trên mặt phẳng phím. `ArProjection` hiện đã có
homography + trọng số `w`; thêm độ cao thì một điểm ở `Height` được vẽ lệch theo pháp tuyến của mặt phẳng trong ảnh và
nhân tỉ lệ cục bộ (độ lớn tỉ lệ với khoảng cách trong ảnh) — đúng cách Keysight xếp tầng, nhưng trên một mặt phẳng thay
vì một cảnh 3D. Bảng mặc định dự kiến (cm): phím 0 · damper 0.1 · nốt 0.1 · keypress 0.15 · impact glow 0.2 ·
note-light spill 0.05 · pulse 0.3 · hạt 0.4 · HUD 0 (vẽ cuối, không qua homography). Đây là **số khởi đầu cho 2D**;
bài học AR của Keysight là phải chỉnh lại theo góc máy — nên mỗi khung nhìn (45°/90°) có bộ độ cao trong preset AR.
Cập nhật 2026‑10‑12: bảng cm đã khóa trong `Camera/ArElementHeights.cs` (1 đơn vị chiều sâu phím = 15 cm, qua
`ArCameraPose.KeyboardDepthMillimetres`) — impact flare nổi đúng 0,2 cm trên phím bị đánh, pulse được bảo đảm không
bao giờ xuống dưới 0,3 cm (`Lifted` chỉ nâng, không hạ phần tử đang bay cao); key tint, note-light spill, damper và
HUD cố tình giữ 0 vì vật lý của chúng là dính mặt phẳng. xUnit `ArElementHeightsTests` khóa bảng số và chứng minh
ở khung 90° độ nâng đi dọc trục nhìn (dưới mức pixel) trong khi ở 45° đọc được ngược mép sau phím — vì vậy WARP
diff cố ý không gating hai số này.

### 14.3 Catalogue phần tử AR

| Phần tử | Họ | Hôm nay | Việc cần | Pass |
|---|---|---|---|---|
| `Piano keys` (thân phím) | Scene | ✅ `ArKeys.Patches` có hình học trắng = mặt + thân, đen = một hình; patch chỉ phát khi key có glow MIDI; lúc im không vẽ bàn phím ảo tĩnh, theo quyết định của chủ repo | giữ active-key tint độc lập với live bars; không phủ geometry khi MIDI im | `VsArQuad` |
| `Dampers` (dải mép sau phím) | Scene | ✅ 2026‑10‑12: vạch phát sáng mềm ôm mép sau phím (v=0), vươn theo sustain 0..1, shader `PsArQuad` kind 11, bloom quanh lõi; WARP đo lift ở pose 45° lẫn `overhead90`, Lite không vẽ. Cùng ngày: toggle bật/tắt (`ArDamperStrip`, mặc định bật) và màu vạch **Theo look (halo) / Tự chọn** (`ArDamperStripColorMode` + `ArDamperStripColor`, thuộc look; WARP sample pixel màu custom tại tâm vạch) | — | `VsArQuad` |
| `Octave lines` | Scene | 🟡 2026‑10‑12: vạch trắng mờ (alpha .22) đứng ở mọi phím C có phím hai bên, theo `KeyboardLayout` — hiện chỉ vẽ trong chế độ Alignment check | overlay thường trực riêng nếu cần | `VsArQuad` |
| `Light bars` (viền/hắt mép) | Scene | ❌ | gradient quanh mép phím; **không** đổ bóng (bài học AR) | `VsArQuad` |
| `Overlay / HUD` | Scene | ❌ | đếm nốt, hiển thị pedal, tên nốt, watermark; vẽ sau cùng, **không** qua homography | pass riêng |
| `Note objects` | Effect | 🟡 `Ar3DNoteProjection.Build` chiếu billboard camera-facing qua pose; Lite/off còn giữ flat route | shape × trắng/đen, min-scale, truncate, size graph, mọc theo mảnh | `GpuArLayer` → `VsArQuad` qua pose 3D |
| `Keypresses` | Effect | ✅ active-key tint + toggle live bars riêng; bars có route 3D khi bật; fade hai chiều (graph theo vòng đời) ✅ 2026‑10‑11 | coverage slider ✅ 2026‑10‑12 (Clean→Full là một thanh trượt liên tục), màu theo velocity ✅ (`VelocityColor` của look, theo strength thật, đo WARP 2026‑10‑12); giữ toggle độc lập với key tint | `GpuArLayer` → `VsArQuad` |
| `Impact lights` | Effect | 🟡 S5 WARP: MIDI impact cue/decay và neighboring-key spill có counter/pixel sample tại Standard+; tâm flare nổi 0,2 cm theo `ArElementHeights` (2026‑10‑12) | tiếp: spill theo graph/candle flicker và kiểm tra màu/độ sáng ngoài camera thật | `GpuArLayer` → `VsArQuad`, bốn góc chiếu từ pose 3D |
| `Note lights` (spill) | Effect | 🟡 `spill` của sân khấu 2D; chưa có spill camera-space | hắt sáng trên mặt phẳng đã calibration, bán kính theo graph | pass AR chưa có |
| `Pulses` | Effect | 🟡 S5 WARP: Ultra pulse/ring có counter + selected pixel variant checks | tune bán kính/lifetime theo velocity và kiểm tra dưới pose nghiêng | `GpuArLayer` → `VsArQuad`, bốn góc 3D |
| `Particles` | Effect | 🟡 S5 WARP: projected particle aggregate/counters pass ở Ultra; từng shape/physics vẫn cần matrix riêng | mở rộng catalogue shape/physics sau khi pixel/benchmark xác nhận | `GpuArLayer` → `VsArQuad` billboards từ pose 3D |

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
- **AR‑6.2 ✅ (2026‑10‑11)**: graph thứ ba của AR — độ mờ của vệt nhấn trực tiếp theo vòng đời, input là quãng vệt đã vươn 0..1 — đi xuôi khi giữ phím và đi ngược lại suốt pha nhả (`Camera/ArLiveFade.cs`, bật bằng `Bar fade follows lifetime`, mặc định tắt; cùng định dạng 1 toggle + 2 thanh trượt). Pha nhả vẫn kết hợp với dim sẵn có nên vệt biến mất trọn vẹn ở cả flat lẫn route 3D. Kiểm chứng: `ArLiveFadeTests` (thuần) + fact mới trong `ArNotesTests`/`Ar3DNoteProjectionTests` + `--verify` (`VerifyArGraphVariable`); mặc định tắt nên vẻ ngoài cũ đứng nguyên.
- **AR‑S3 ✅ (2026‑10‑09)**: `ArElementCatalog` định nghĩa 11 phần tử (họ scene/effect, pass vẽ, tier tối thiểu); `ArQualityTier` Lite/Standard/Ultra với thứ tự cắt tất định (hạt → pulse → light bars → HUD → note light → damper → octave lines → impact lights), phím/nốt/vệt không bao giờ bị cắt; picker **Quality** trong thẻ AR, mặc định Standard (nâng cấp không mất ánh phím), thuộc máy nên áp look không đổi; `ArLookGuard` giữ nguyên sáu style đã biết cho shared shader; tên style không biết hoặc chưa được port vẫn dùng `Neon` fail-closed
(một style thêm vào trang cài đặt trước khi có biến thể AR sẽ bị loại khỏi AR chứ không được đưa cho một pass chưa từng nghe tên nó).

### 14.6 Đầu ra (output modes)

| Mode | Dùng khi | Cần gì | Kiểm chứng |
|---|---|---|---|
| `composite` (đang có) | chơi/xem tại chỗ, quay | như hiện tại | `VerifyArCameraLayer` |
| `alpha-effects` | OBS/stream: lớp AR **trong suốt** để OBS ghép lên nguồn camera riêng | swap chain có alpha + "Allow Transparency"; scale 100 | ảnh PNG alpha đọc lại điểm ảnh |
| `geometry+glow` | ghép glow kiểu cộng dồn cho đẹp (như guide mask mode) | ✅ code: render scene một lần, xuất hai lớp alpha-premultiplied đồng bộ (Geometry không bloom; Glow chỉ lấy bloom), hai cửa sổ riêng; vẫn cần kiểm tra OBS/blend thật | WARP readback PASS trong CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330): Geometry giữ thân nốt đặc, Glow có bloom riêng và nền trong suốt; manual OBS blend vẫn Unverified |
| `stencil-kênh` | ai buộc phải key/chroma (tương thích cách Keysight) | stencil nhị phân nhét vào kênh blue + shader OBS (tài liệu hoá) | kiểm bằng ảnh mẫu + hướng dẫn |
| `render-to-video` | làm video | profile (độ phân giải/fps/downsample), audio, fade/watermark, bước thời gian cố định | so khung live ↔ render |
| `virtual camera` / `OBS window` | stream/call (AR‑6.4/6.5 giữ nguyên) | dùng lại các mode trên | như cũ |

Chroma key cũ (nền xanh) giữ làm đường lui nhưng **không khuyến khích** — đúng lịch sử của Keysight: bloom và hạt nhỏ
làm artifact.

### 14.7 Căn chỉnh: giữ gì, thêm gì

- **Đã có (2026‑10‑10)**: wizard đặt 4 góc, lưu khung nhìn + horizontal FOV; Calibrate có gizmo Move/Rotate/Scale kiểu Unity và các núm số translation/scale đồng đều/pitch/yaw/roll. Transform tuyệt đối áp dụng từ baseline, chỉnh tay một góc thì baseline được rebase. Version 5 lưu FOV, translation, scale đồng đều, pitch/yaw/roll và baseline chưa biến đổi; v1–v4 tiếp tục đọc với translation 0 và scale 1. v3–v4 giữ FOV đã lưu, v1–v2 dùng FOV 70°; v1–v3 dùng góc 0°, còn v4 giữ rotation. Calibration vẫn thuộc máy.
- **Hướng nốt độc lập**: hai điều khiển 3D tilt/azimuth trong AR settings định hướng note/live bars mà không xoay pose bàn phím. Pose và note direction được lưu trong settings cục bộ; preset không ghi đè chúng theo quy tắc local-only hiện tại.
- AR‑3.5 ✅ (2026‑10‑11): độ trễ camera lưu theo định danh camera trong `ar-delay.json` cạnh cài đặt; thuộc máy như calibration, preset/mã chia sẻ không mang nó.
- **Thêm 1 — overlay kiểm tra**: ✅ 2026‑10‑12. Toggle **Alignment check** trong thẻ KEYBOARD ALIGNMENT vẽ lại khung
  4 cạnh của vùng phím đã lưu cùng vạch quãng tám (trắng .5/.22) ngay trên ảnh camera đang chạy — pack sau cùng để
  đọc trên mọi hiệu ứng, mọi quality tier, chỉ khi đã calibrated; camera xê dịch đọc được ngay, đúng analog của
  "ảnh alignment" bên Keysight. Hình học thuần trong `Camera/ArAlignmentGuides.cs` (xUnit `ArAlignmentGuidesTests`),
  `PianoVisualSettings.ArAlignmentCheck` thuộc máy cùng nhánh `ArViewProfileId`/calibration nên preset/mã chia sẻ
  không mang, còn `VerifyArNoteProjection` có WARP block riêng (`ArAlignmentGuideCount` > 4 ở Standard lẫn Lite,
  pixel bật/tắt đổi rõ, tắt là về 0). Cùng ngày: màu khung chọn được — 5 màu tương phản cao White/Cyan/Yellow/
  Magenta/Lime (`ArAlignmentGuideColor`, thuộc máy theo chế độ kiểm tra; bảng ánh xạ thuần `ArAlignmentGuides.ColorFor`,
  WARP kiểm recolour không đổi hình học).
- **Thêm 2 — đo trôi**: ✅ 2026‑10‑12. Toggle **Canh khung camera có trôi** (thẻ KEYBOARD ALIGNMENT, mặc định bật,
  thuộc máy cùng nhánh calibration): cột ghim là lưới sáng của vùng phím lúc bắt đầu canh (region của
  `MotionEnergy.KeysRegion`); cứ ~2 s so khung đang chạy với cột ghim qua `Camera/ArDriftMeter.cs` — quét dịch
  thô‑rồi‑mịn trên ô 3 pixel, bù độ sáng trung bình, trung bình cắt đuôi 10 % để tay chơi/bóng đổ không tính (cứt ít thôi: trôi thật rải sai số khắp trường); trôi quá
  ngưỡng (~0,5 % chiều rộng ảnh) trong 3 lần đo liền → dòng trạng thái AR nhắc «mở Calibrate và đánh dấu lại».
  Calibration **không bao giờ tự bị sửa**; canh tạm nghỉ khi đang đo độ trễ (đều dùng cột sọc `FrameTap` của
  `CameraFrameRing`). Logic thuần khóa bằng xUnit `ArDriftMeterTests` (frame tổng hợp: trôi 9 px đo ±3, sáng ±18 %
  và bàn tay quét qua không báo động) và `--verify` (`VerifyArDriftWatch`).
- **Thêm 3 — xuất/nhập bản sao căn chỉnh**: ✅ 2026‑10‑12. Hai nút **EXPORT CALIBRATION** / **IMPORT CALIBRATION**
  (thẻ KEYBOARD ALIGNMENT). Xuất gom **mọi** calibration của camera đang dùng (mọi khung nhìn, kích thước ảnh và cỡ
  phím) vào một tệp JSON `keyflow-ar-calibration` v1 (`Camera/ArCalibrationArchive.cs`, thuần), kèm ảnh PNG chụp
  frame hiện tại đặt cạnh tệp để người dùng nhận ra đó là camera nào (ảnh chỉ để nhìn — phục hồi không cần). Nhập khôi
  phục các hàng về
  **đúng cameraLink trong tệp** (rỗng thì về camera đang dùng), mỗi hàng đi qua lại `ArCalibrationStore.Set` nên qua
  đủ kiểm tra như calibrate mới — hàng hỏng bị đếm và bỏ qua, không làm hỏng dữ liệu; tệp lạ version mới hơn bị từ
  chối bằng lỗi rõ ràng. Không thêm key `PianoVisualSettings` nào nên preset/mã chia sẻ không đụng tới. xUnit
  `ArCalibrationArchiveTests` + `--verify` (`VerifyArCalibrationArchive`).
- **Thêm 4 — hướng dẫn ngay trong cửa sổ**: ✅ 2026‑10‑12. Cửa sổ Calibrate in hai câu mẹo dựng cảnh (máy qua vai
  nhìn dọc phím; tránh ống quá rộng/fisheye; tay ra ngoài khung khi đánh dấu) — phần chữ thường trực — và thêm trạm
  quan sát sống `Camera/CalibrationAdvice.cs`: lobe da Y′CbCr quét ảnh still đang hiển thị (chung bước mẫu 3
  pixel), tay da chiếm quá 3 % là dòng note nhắc khẽ — khắp ảnh trước khi đủ 4 góc, chỉ trong quad sau khi đủ
  (point-in-quad trên cùng bước mẫu; cánh tay ngoài vùng không tính). Là lời khuyên, không phải cửa chặn; gỗ sồi
  nắng có thể mượn màu da. xUnit `CalibrationAdviceTests` + `--verify` (`VerifyCalibrationAdvice`).
- **Thêm 5 — preset AR theo khung nhìn**: ✅ 2026‑10‑13. `Camera/ArViewPresets.cs` (thuần) tay bảng theo id khung nhìn: camera nghiêng (45°/legacy/tự do) giữ độ cao catalogue (impact 0,2 cm, pulse 0,3 cm) và đủ hiệu ứng; `overhead90` thu lift còn 0,05 cm / 0,1 cm và giấu hold arc + light beam (quad đứng ép thành sợi nhiễu khi nhìn thẳng xuống). `GpuArInput` nhận `ViewProfileId` (mặc định Legacy → nghiêng, mọi call-site cũ giữ nguyên pixel), layer dùng preset cho impact/rings/beams/arcs; MainWindow feed id thật. Không thêm key `PianoVisualSettings` nào (id vốn machine-local). xUnit `ArViewPresetTests` (bảng số, chuẩn hóa id, units/Lifted) + `--verify` (overhead: beams/arcs=0; lights/pulses/particles >0 trên WARP).

### 14.8 Ngân sách hiệu năng: AR‑Lite / AR‑Standard / AR‑Ultra

| Bậc | Có gì | Ghi chú |
|---|---|---|
| `AR‑Lite` | phím, keypress, nốt phẳng tự phát sáng (đúng thứ đang có) | chạy được trên GPU yếu; là mức mặc định khi máy không theo kịp |
| `AR‑Standard` | + shape nốt/min-scale, impact glow, spill | mục tiêu 60 fps ở 1080p |
| `AR‑Ultra` | + pulse, hạt, note light đầy đủ, light bars | cần card thật; `--bench-ar` làm cổng |

Thứ tự cắt khi vượt ngân sách (tất định, test được) ✅ 2026‑10‑13 qua `Camera/ArQualityBudget.cs` + `GpuArLayer`/`GpuStageRenderer` (`ArBudgetTierOverride` làm seam cho verify): **hạt → pulse → note light → light bars → style nốt → impact glow**;
không bao giờ cắt phím/nốt/keypress. Nối vào `GpuAdaptiveQuality` sẵn có.

### 14.9 Cài đặt, preset, mã chia sẻ

- Thẻ AR hiện có **Notes in perspective**, hai núm **Note direction tilt / azimuth** ngay sau toggle và trước **Minimum length, share of width**; hai núm này độc lập pose keyboard. **Live press bars** mặc định tắt và không điều khiển key tint. Giữ View / Mode / Delay / Calibrate; ambient flowers/stars luôn screen-space.
- ✅ 2026‑10‑13 — Mã chia sẻ mang look + graph: **`KEYFLOW-LOOK-2`** ra đời (đọc được mã bản 1 nguyên từng bit), `docs/SETTINGS-WIRING-AUDIT.md` đã cập nhật. Payload mang kèm mọi graph AR (độ rộng/fade nốt theo velocity, fade vệt nhấn theo lifetime); khóa bằng `ShareCodeEnvelopeTests` (6 fact, phần vận chuyển thuần tách ra `Stage/ShareCodeEnvelope.cs`) + `VerifyPresetShareCodes` mở rộng.
- Máy‑cục‑bộ vẫn là: calibration (góc + FOV), keyboard 3D pose, hướng note 3D, khung nhìn, alignment check (2026‑10‑12), delay, màn hình xuất, camera. Calibration version 5 lưu FOV + translation + scale đồng đều + pitch/yaw/roll + baseline; v1–v4 đọc tiếp với translation 0, scale 1 (v3/v4 giữ FOV, v1/v2 dùng 70°; v1–v3 mặc định góc 0°, v4 giữ rotation). Settings mới mặc định live bars off.

### 14.10 Thứ tự thi công (AR‑S0…AR‑S9)

| Slice | Việc | Phụ thuộc | Kiểm chứng | Ánh xạ bản cũ |
|---|---|---|---|---|
| **AR‑S0** ✅ | nền AR‑0/AR‑1: layout, khung mẫu, camera texture, homography, 4 điểm, khung nhìn, vệt phím tự bấm | — | CI xanh (PR #55) | AR‑0, AR‑1.1–1.7 |
| **AR‑S1** ✅ | `PianoSimulation`: activity (floor/ceiling), tắt dần theo pitch + damping, 3 pedal, notes/sec, total | — | test thuần (`PianoSimulationTests`, 11 fact) + `--verify` đọc số (`VerifyPianoSimulation`) | mở rộng AR‑3 |
| **AR‑S2** ✅ | `GraphVariable` + 2 graph đầu của AR: độ rộng và độ hiện của nốt/vệt theo velocity (bảng cài đặt: 2 công tắc + 4 thanh trượt, curve chưa mở) | S1 | test thuần (`GraphVariableTests` 7 fact, `ArGraphTests` 6 fact) + `--verify` (`VerifyArGraphVariable`) | mới |
| **AR‑S3** ✅ | Catalogue phần tử + 3 bậc chất lượng (Lite/Standard/Ultra, Lite giữ nốt/vệt phẳng, Standard thêm ánh phím + impact/note light, Ultra thêm pulse/hạt), `ArLookGuard` (sáu style đã biết pass-through; tên lạ → Neon) | S2 | test thuần (`ArElementCatalogTests` 7 fact, `ArQualityTests` 3 fact, `ArLookGuardTests` 3 fact) + `--verify` (`VerifyArQualityTiers`); picker "Quality" trong thẻ AR; presets regenerated | mới (bài học randomizer) |
| **AR‑S4** (code/build/synthetic WARP checks PASS; real webcam *Unverified*) | **3D keyboard object transform**: `ArCameraPose` estimates a pinhole camera from the 4 marked corners + horizontal FOV; Calibrate has a Unity-style Move/Rotate/Scale gizmo plus numeric translation, uniform-scale and pitch/yaw/roll controls. Transform sliders are absolute against the original baseline; manual corner edits rebase. File v5 stores FOV, translation, uniform scale, all three pose angles and the untransformed baseline; v1–v4 still load with translation 0 and scale 1 (v3/v4 keep saved FOV, v1/v2 use 70°; v4 keeps rotation, v1–v3 default it to 0°). **Active note route**: `GpuArLayer` → `Ar3DNoteProjection.Build` creates camera-facing quads from keyboard-local 3D and projects them with the pose. Note travel has its own preview arrow, 3D tilt/azimuth controls, and Screen-up / key-depth aiming; these do not rotate or move the keyboard and are saved with stage settings. The active renderer does not route AR notes through stage-space `VsNote` and does not gate them on the legacy `ArSceneProjection` horizon. **Notes in perspective** uses this route except on Lite; off/Lite keep the legacy flat overlay. Same-key clipping and minimum length as a share of projected key width stay in the 3D builder. **Live press bars** are an independent, default-off toggle (key tint stays on); when projected they use the same pose/direction. Stage-space falling-note trails are suppressed whenever the full-screen camera is active, including before calibration, rather than left as misaligned streaks. Ambient petals, shooting stars and other ambient sprites remain flat screen-space. **S5 selected WARP paths (2026‑10‑10; broader coverage remains unverified):** Standard projects MIDI impact lights + neighboring-key spill; Ultra additionally projects the configured Live Stage ring/morph/flash channels, burst and continuous particle families, release effects, held-key arcs and beams. All use pitch-tagged simulation state and the saved 3D keyboard pose; stage-space sprites remain suppressed and ambient stays flat. `VerifyArNoteProjection` now has WARP assertions for these channels, pose movement, release and note-direction independence, and build/full `--verify`/WARP pass in CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330); broad effect-family/value/parameter coverage is not claimed. This patch adds WARP samples for all six note-style branches on calibrated quads plus Gem/Crystal differences on the flat AR path; those new assertions await CI, and no full material-setting parity is claimed. **2026‑10‑10 calibration usability patch (CI-verified in run [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330)):** each adjustable slider/discrete choice now has a circular-arrow reset; rotation rings use the closest point on the projected perspective arc and the saved Euler component axis instead of a screen-centre polar angle. | S1–S3 | `CalibrationWizardTests` and `ArCameraPose`/`Ar3DNoteProjectionTests` cover independent rotation/translation/uniform scale, baseline reload/migration and screen-up direction solving; `ArCalibrationStoreTests` + `ArCalibrationSessionTests` cover v5 full-transform round-trip and v1–v4 migration; `VerifyCalibrationWindow` now also checks all three projected rotation rings under a compound pose, per-control resets, note-direction independence, save/reopen/FOV/start-over; WARP `VerifyArNoteProjection` checks the active AR-quad render route, full object transform, screen-space ambient, synchronized S6.2 geometry/glow readback, and the S5 Standard/Ultra Live Stage light, ring, particle, flash, hold, beam and release samples, plus separate live-bar hold-toggle/representative 0↔100 parameter deltas. Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) passed build, full `--verify`/WARP and portable tests for these synthetic paths. A real webcam trial is still required before S4 leaves **Unverified**; the sandbox itself still has no .NET SDK/camera/hardware. During that trial, record the ambient choice (effects on/off) in the PR description; ambient remains flat and is not a perspective test. | **AR‑6.2** |
| **AR‑S6** (S6.1/S6.2 WARP checks ✅; manual OBS gate *Unverified*) | **S6.1 alpha-effects**: render target alpha, readback BGRA premultiplied, cửa sổ WPF borderless trong suốt cho OBS; bỏ camera/nền, giữ nốt + ánh sáng. **S6.2**: render một scene rồi xuất Geometry (direct scene đã tone-map, không bloom) và Glow (bloom-only) qua một staging-ring pair và mailbox atomically publish chung serial, rồi hiện ở hai cửa sổ; nút riêng trong AR settings. Stencil-channel vẫn chưa làm. | S4 | Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) passes synchronized Geometry/Glow WARP readback; real OBS Add/Screen composition has not been confirmed. Keep the WARP regression suite in CI. | **AR‑6.4** |
| **AR‑S5** (selected Live Stage paths WARP-verified; family/parameter matrix *Unverified*) | Standard: MIDI impact lights + key-light spill. Ultra: configured impact rings/morphs/flashes, burst + continuous particle families, hold arcs, keyed beams and release effects, projected from the same pitch-tagged simulation through the calibrated 3D pose. No stage-space sprite/trail is placed over camera; ambient remains flat. AR quads use the same `ShadeNoteMaterial` as Live Stage. New WARP checks sample all six style branches and Gem/Crystal flat-quad output; this patch is awaiting CI, while full family/value material parity remains Unverified. | S4 + S6.2 | Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) passes build + full `--verify`/WARP for selected paths; next: expand style/parameter coverage, benchmark on physical hardware, and test real webcam/OBS. Do not call S4 webcam verification complete. | AR‑6.2/6.3 |
| **AR‑S7** ✅ 2026‑10‑13 (khung + giọng + watermark + UI) | Render-to-video AR: profile, fps, downsample, audio, watermark. **Nửa khung**: `Video/ArVideoRenderProfile.cs` (thuần, xUnit `VideoRenderProfileTests`) lập kế hoạch geometry/fps/đếm khung với mọi clamp (cạnh chẵn cho AVI, trần 60 fps của writer, trần cạnh dài); `Video/BgraPack.cs` (thuần, `BgraPackTests`) đóng gói bottom-up BGR + box 2×2; `Video/ArRenderToVideo.cs` quét offline một feed-scripted tick nhất quyết từng bước định với seed cố định, downsample và ghi thẳng vào AVI/PNG hiện có. Verify `VerifyArRenderToVideo`: AVI đọc lại với đúng camera-stripe + đèn phím ở đúng tick, PNG folder 6 khung + manifest, tick impact bằng trực tiếp render kép cùng-dt-cùng-seed từng byte, hai sweep giống nhau → byte-identical. Giọng (✅ 2026‑10‑13): `Video/ArVideoAudio.cs` (`ArVideoNoteEvent` / `ArVideoAudio` / `ArVideoMixdown`, thuần, xUnit `SongMixdownTests`) mix piano theo đúng tick của video (sự kiện phát với khung hình nó, các block 44.1 kHz pace theo từng tick nên tổng mẫu khớp sample-exact kể cả ở fps lẻ), engine `Run` nhận hook `audio` tuỳ chọn và file `.wav` nằm cạnh AVI/PNG (convention `take.avi → take.wav`, thêm `-i take.wav` vào lệnh ghép), verify lại điểm sàng SFZ: âm lớn các tick giữ, gần-im ở onset, 22050 mẫu cho 6 khung 12 fps. Watermark (✅ 2026‑10‑13): dải «KEYFLOW» giờ do GPU tự in (sprite kind 16 + texture `MarkTex` t8) với placement thuần `Video/ArVideoWatermark.cs` (xUnit `VideoWatermarkTests`); toggle **Watermark** sẵn có nay bật/tắt dải này một cách nhất quán trên live stage, bản ghi GPU và take offline (lớp WPF software vẫn tự in khi không có GPU). Verify so byte hai take giống nhau mọi thứ trừ toggle: đúng vùng strip sáng lên, bên ngoài nguyên từng byte. UI (✅ 2026‑10‑13): thẻ **AR RENDER‑TO‑VIDEO** trên trang Recording (`Ui/MainWindow.ArExport.cs`) — nút RENDER AR TAKE… xuất bài hát đang mở qua bản calibration hiện có trên file camera video, offline và xác định: profile từ lựa chọn OUTPUT (nắp resolution + fps), engine sweep + watermark kind‑16 + mix voice theo tick (script `Video/ArVideoExportScript.cs`, xUnit `SongExportScriptTests`), ra AVI + `.wav` cạnh nhau kèm dòng ffmpeg ghép; chuỗi giao diện mới đăng ký đầy đủ EN/VI. Verify (`VerifyArRenderToVideo`) chạy trọn đường với FILE VIDEO THẬT: MP4 do `Mp4Recorder` của app tự ghi từ fixture thuần `Camera/CameraTestVideo.cs` (xUnit `CameraTestVideoTests`), decode lại bằng `CameraFrameReader`, dải sáng bên trái tăng đúng từng tick qua engine; mặt card được assert trên cửa sổ verify (dòng trạng thái “Ready when…”). | S6 | so khung live ↔ render | AR‑6.6 |
| **AR‑S8** | HUD: đếm nốt, pedal, tên nốt, octave lines, light bars | S3 | `--verify` + ảnh | mới (overlay/scene) |
| **AR‑S9** | AR‑2 dò tự động, AR‑2b, AR‑4 tay, AR‑5 ngón, AR‑6.5 webcam ảo | như bản cũ | như bản cũ | AR‑2/2b/4/5/6.5 |

Thứ tự gate stream được giữ: **S6.2 Geometry + Glow WARP readback trước S5**; CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) xác nhận readback này và S5 selected paths. S5 family/parameter support vẫn *Unverified* pending full matrix; manual OBS blend, webcam thật và hardware/performance chưa xác minh. AR‑S4 webcam vẫn *Unverified*.
Ước lượng thô: S1–S3 ≈ 3 tuần, S4–S5 ≈ 3–4 tuần, S6–S8 ≈ 3 tuần, S9 như bản cũ. Mọi slice vẫn theo luật §0:
toán thuần + test khung tổng hợp trước, máy thật để **Unverified** cho tới khi chủ repo thử.

### 14.11 Những gì **không** đổi (ràng buộc của chủ repo)

- **Pose 3D tự do là yêu cầu hiện hành**: pitch/yaw/roll của mặt phím và tilt/azimuth của hướng nốt được chỉnh riêng; không tái dựng mesh vật lý đầy đủ của đàn — chỉ mặt phím đã calibration và các billboard note/live-bar được dựng/project. Không dùng giới hạn flat-only cũ để thu hẹp yêu cầu này.
- Độ trễ camera lưu theo từng camera (AR‑3.5, 2026‑10‑11) và thuộc máy; preset/mã chia sẻ không đổi nó.
- Calibration thuộc máy (camera + khung nhìn), preset/mã chia sẻ không đổi nó.
- MIDI là sự thật; không nhận phím từ hình/âm thanh; không model nặng trong gói mặc định.
- Không âm thanh; không telemetry; mọi thứ kiểm chứng bằng khung tổng hợp.

### 14.12 Rủi ro mới

| Rủi ro | Giảm |
|---|---|
| Khối lượng 9 slice (ước 9–11 tuần một người) | làm đúng thứ tự; S1–S3 là logic thuần, rẻ; S4 mới là slice rủi ro |
| Biên tập graph làm UI phức tạp | chỉ 2 handle + 1 curve; mặc định flat; không vẽ đồ thị trong bảng cài đặt |
| Note material trên AR khác hình học stage | shared shader giữ các style đã biết trên camera-facing quads; WARP chỉ sample style path, chưa xác minh toàn bộ parameter/value matrix hoặc hardware |
| Alpha mode không chạy trên mọi GPU/OBS | giữ stencil-kênh làm đường lui; tài liệu hoá cả hai |
| Render path của AR note | Dùng pose camera 3D + một AR quad pass có kiểm chứng; không mở lại nhánh scene-space note shader cũ. Chia sẻ note-state/color ở mức có thể, nhưng projection không được phụ thuộc vào ma trận flat/horizon. |
| Độ cao sai theo góc máy | preset AR theo khung nhìn + overlay kiểm tra + ảnh mẫu trong `--verify` |

### 14.13 Đích hình ảnh từ ảnh tham chiếu của chủ repo (2026‑10‑10)

Hai ảnh gửi ngày 2026‑10‑10 được ghi nhận là **mục tiêu trực quan**, không được sao chép vào repo và không phải yêu cầu khớp pixel:

- **A — Perform/live:** camera cận cảnh cây đàn thật và tay người chơi; dải sáng chạy theo hàng phím, điểm nhấn/hạt dựng thành vệt cột tại các phím MIDI vừa bấm. Đây là hiệu ứng AR gắn với phím và vận tốc, không phải các hạt sân khấu bị chiếu phẳng sai vị trí.
- **B — Learn/notes:** các khối nốt nhiều màu nổi trong không gian trước cây đàn, có phối cảnh/hướng chuyển động về đúng vùng phím sau khi Calibrate; phải theo pose 3D tự do và hướng nốt riêng.

**Đối chiếu đường render hiện tại (đã lần theo cả cờ lẫn renderer):**

1. `ArPlan.Decide` chỉ cấp `GpuArInput` khi AR bật, Direct3D hoạt động, nền không phải chroma, camera mở và calibration `Ready`; `GpuStageRenderer` chỉ build AR quads khi camera view hợp lệ và kích thước frame trùng calibration. Nhánh note-perspective cần thêm `CameraPose` hợp lệ.
2. `GpuArLayer.Build` thoát sớm khi opacity ≤ 0. Key tint chỉ được tạo khi key-color từ MIDI có glow > `MinKeyGlow` và `KeyLight > 0`; Lite ép `KeyLight = 0`. `ArKeys.Patches` là hình học cho các vùng phím được tô, **không** có nghĩa là một bàn phím tĩnh được vẽ khi không có MIDI.
3. Bài hát chỉ vẽ nốt khi `ShowNotes` (Learn), `input.Playing`, danh sách MIDI không rỗng, nốt còn trong lookahead/reach và phép chiếu tạo được patch. Perform chủ ý đặt `ShowNotes = false`. Live press bars cần `LivePressTrails = true` và dữ liệu MIDI đang/ vừa giữ phím; toggle này độc lập với key tint.
4. Dù calibration đang Ready hay ở NeedsCalibration/Outdated, khi camera thực sự là background, `GpuStageRenderer` chặn stage-space notes/trails, guide lanes, virtual key bodies và sprites; chỉ calibrated `GpuArLayer` mới được phép thêm cue theo pose, vì vậy không có bàn phím ảo tĩnh hay stage-space overlay leak trước calibration. `BuildAmbient` nhận rõ camera-active state để không phụ thuộc vào việc note pass đã xóa trail list; ambient screen-space vẫn giữ nguyên. Khi Ready, `ArPlan.HideStageLayers` bật `ArOverlayActive`; S5 giữ cấu hình + pitch-tagged simulation state để `GpuArLayer` chiếu effect theo pose 3D: Standard impact/note-light spill; Ultra ring/morph/flash, particle families, release FX, hold arcs và beams. Ambient petals/meteors giữ screen-space. S5 selected assertions pass build/WARP/`--verify` trong CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), nhưng 14 family/parameter states vẫn *Unverified* pending full matrix; webcam thật vẫn cần thử. S6.2 Geometry + Glow WARP readback pass, còn OBS gate chưa xác minh.

**Tiêu chí mục tiêu cho các slice kế tiếp:**

- Learn: nốt màu 3D rơi/đi theo hướng riêng và tới đúng phím dưới pose/FOV đã calibration; đổi góc bàn phím không làm note direction bị xoay theo ngoài ý muốn.
- Perform: không có nốt bài hát tương lai; lúc MIDI im **chỉ thấy phím vật lý từ camera, tuyệt đối không vẽ bàn phím ảo tĩnh**. Khi MIDI kích hoạt mới hiện cue hàng phím/key tint và hiệu ứng live đúng phím theo S5. Giữ key tint độc lập với live bars.
- Thứ tự code đã giữ: S6.2 geometry/glow (hai output đồng bộ) trước S5. WARP readback cho S6.2 và selected S5 paths đã PASS trong CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330). Để sign-off streaming vẫn cần ghép hai cửa sổ trong OBS (Geometry bình thường, Glow Add/Screen), mở rộng style/parameter checks và benchmark trên GPU thật; không suy ra webcam/OBS/performance từ WARP. Ambient petals/meteors luôn screen-space, không nhận perspective. AR-S4 vẫn **Unverified** cho tới khi webcam thật chạy đúng checklist; local sandbox không có .NET SDK/hardware, còn Windows CI đã xác nhận code/WARP.
