# Rà soát dự án & đợt nâng cấp giao diện / shader đổ bóng

Tài liệu này là kết quả của việc rà soát toàn bộ kho `Keyflow · Piano VFX Studio` và đợt nâng cấp
để giao diện tiệm cận các MIDI visualizer hiện đại (Keysight, Piano VFX, Embers), đồng thời thay
cách vẽ bàn phím phẳng bằng một **shader đổ bóng thời gian thực** mô phỏng đúng mô hình shading
mà Unreal Engine dùng mặc định.

> Lưu ý môi trường: sandbox này **không có .NET SDK và WPF chỉ biên dịch trên Windows**, nên mã C#
> không thể biên dịch/chạy tại đây. Thay vào đó dự án có hai lớp kiểm chứng:
> 1. `tools/check_sources.py` — kiểm tra cân bằng ngoặc/dấu nháy mọi file C#, và tính hợp lệ XML +
>    mọi tham chiếu `StaticResource` trong XAML (chạy được ở mọi máy).
> 2. `VerificationSuite` (chạy bằng `PianoPath.exe --verify` trong CI trên Windows) — thêm mục
>    `VerifyShaderPipeline` kiểm tra thật đầu ra pixel của shader và `VerifyShadedStage` kiểm tra
>    công tắc shader đổi đúng thứ stage vẽ.
> Ngoài ra `tools/shader_preview.py` là bản **port Python 1-1** của shader để xem trước thuật toán
> và tạo ảnh minh hoạ trong `tools/out/` (không commit). Nó kiểm chứng toán học & bố cục, không phải
> là bản build của ứng dụng.

---

## 1. Hiện trạng trước khi nâng cấp (kết quả rà soát)

Kiến trúc hiện tại đã khá chỉn chu: `PianoStage` vẽ toàn bộ sân khấu bằng `DrawingContext` (vector),
hệ cài đặt khai báo 10 trang, preset, tìm kiếm, và `VerificationSuite` chạy trong CI. Bàn phím cũ vẽ
bằng các `RoundedRectangle` + gradient dọc — nhanh nhưng **phẳng**: không có bóng đổ thật, không có
occlusion giữa các phím, không có specular theo góc nhìn, nên kém "vật lý" so với Keysight/Piano VFX.

Những điểm đã rà soát và giữ nguyên vì chúng tốt và được test bao:
- `MidiReader`, `SoundFontSynthesizer`, `PianoAudioEngine`, reverb — có test dày, không đụng.
- Hệ preset / JSON round-trip / clamp — giữ nguyên contract (mọi giá trị mới đều được `Clamp()`).
- `CaptureStageBgr` cho quay video — giữ nguyên.

---

## 2. Shader đổ bóng kiểu Unreal (phần trọng tâm)

### 2.1 Cách tiếp cận
WPF không biên dịch HLSL lúc chạy, nên shader được viết bằng C# như một **software shader** chạy đa
luồng (`Parallel.For`) rồi đóng gói thành `BitmapSource`. Để giữ 60 fps, bàn phím được **bake một
lần và cache** (nền tĩnh), sau đó mỗi phím đang kêu chỉ vẽ lại một **tile overlay** nhỏ — không bao
giờ shade lại cả 88 phím mỗi khung hình.

### 2.2 Mô hình shading (bê đúng công thức của UE)
- **BRDF Cook–Torrance**: phân bố GGX (`DistributionGgx`), hình học Smith (`GeometrySmith`),
  fresnel Schlick (`FresnelSchlick`). `F0 = Specular * 0.16` để khớp cách UE map input Specular 0.5 → F0 0.08.
- **Bóng mềm (penumbra thật)**: key-light là một *softbox*; mỗi sample bóng lấy lệch trên mặt đèn rồi
  bắn tia che (`IsShadowed`) — độ rộng softbox = độ mềm bóng, đúng như area light trong UE.
- **Contact AO**: tia bán cầu ngắn (`TraceOcclusion`) làm tối khe hở giữa các phím và gầm fallboard
  (tương đương SSAO/DFAO).
- **IBL môi trường**: ambient lấy theo gradient sky/ground + phản xạ môi trường dọc vector phản xạ
  (`Reflect`) nhân fresnel — cho ngà/đen mun độ bóng đúng chất sơn mài.
- **Đèn màu theo nốt**: mỗi phím đang kêu là một *area light* màu nốt, hắt màu sang phím lân cận và
  lên giường phím (`LitReach`).
- **Tonemap ACES filmic** (tonemapper mặc định của UE) + chỉnh saturation/contrast + **dither Bayer**
  chống banding khi ghi sRGB.

### 2.3 Camera
Camera *pinhole trụ*: tuyến tính theo trục X để **mỗi phím luôn rơi đúng cột pixel của nốt đang rơi**
(giữ contract của `KeyCenters`), nhưng vẫn phối cảnh thật theo chiều sâu để phím xa nhỏ dần và bóng
đen dài ra. `ShaderCameraTilt` điều khiển độ thấp/cao của máy quay.

### 2.4 Cache & ngân sách khung hình
- `PianoShaderScene.Signature()` băm mọi tham số shader; chỉ khi signature đổi (hoặc đổi kích thước)
  mới bake lại → kéo một slider không liên quan **không** gây bake lại (được test bao).
- Tile overlay chỉ sơn lại phím của nó + giường phím xung quanh, **không** sơn đè lên mặt phím khác
  (tránh xung đột giữa các tile khi hai phím kề cùng kêu).
- Ngân sách tối đa 6 tile bake/khung; khi hết, phím dùng tile màu gần nhất trước đó để không nhấp nháy.

---

## 3. Giao diện (chrome) kiểu visualizer hiện đại

- **Bảng màu trung tính gần-đen** với một accent violet→cyan duy nhất, để sân khấu neon là thứ sáng
  nhất màn hình (công thức của Keysight/Piano VFX). Thêm `PanelChromeBrush` (mặt nâng nhiều lớp),
  `TopSheenBrush` (cạnh trên bắt sáng như bevel 3D), `HairlineBrush`, `SoftShadow`.
- **Icon vector thuần Geometry** (không font icon, không asset ngoài) cho mọi nút: logo, open, settings,
  caption (min/max/close), play/pause/restart, menu, rec, và 10 icon điều hướng.
- **Header**: logo + brand + chip PRO, khối bài hát, chip LOOK ở giữa, caption buttons kiểu Windows.
- **Footer/transport**: nút play tròn gradient, restart, readout thời gian (font Consolas), 3 chip số liệu
  ACCURACY/SCORE/STREAK, pedal pills, thanh seek + progress.
- **Dock cài đặt**: cột điều hướng rộng hơn với icon + nhãn + marker accent, header "STAGE DESIGN",
  ô tìm kiếm có icon kính lúp.
- Nút được thêm lớp sheen + trạng thái hover/press; tab điều hướng giữ icon khi được chọn.

---

## 4. Cài đặt mới (trang Keyboard → "RAY-TRACED SHADING")

`ShadingQuality` (Off/Fast/Balanced/Cinematic), `ShaderKeyLight`, `ShaderShadows`,
`ShaderAmbientOcclusion`, `ShaderGloss`, `ShaderRimLight`, `ShaderEmissive`, `ShaderExposure`,
`ShaderCameraTilt`, `ShaderFilmic`. Tất cả được `Clamp()` và nằm trong preset JSON nên round-trip an toàn.
Các preset gán mức shader riêng (Inferno/Ice Crystal = Cinematic, Classic Roll = Fast, Green Screen = Off).

---

## 5. Cách tự kiểm chứng

```powershell
# 1. Kiểm tra tĩnh (mọi máy, không cần SDK)
python tools/check_sources.py

# 2. Xem trước thuật toán shader (port Python) + ảnh minh hoạ
python tools/shader_preview.py 780 180 0.6   # xuất tools/out/keyboard-unlit.png & keyboard-lit.png

# 3. Trên Windows: biên dịch rồi chạy bộ kiểm chứng trong ứng dụng
dotnet run -c Release -- --verify
```

Mục `VerificationSuite` mới sẽ in `PASS shader pipeline: ...` và `PASS settings dock: ...` nếu bóng/occlusion/
tonemap/overlay tile hoạt động đúng; CI chặn build khi có `FAIL`.

---

## 6. Rủi ro & giới hạn đã biết

- Shader là **software** (CPU đa luồng) nên lần bake đầu ở Cinematic trên máy yếu có thể tốn vài chục ms;
  đã có chế độ Fast/Off và cache để giảm. Đây là đánh đổi để chạy được trên WPF thuần.
- Vì không biên dịch được trong sandbox, mọi thay đổi C# đã được soát cú pháp bằng
  `tools/check_sources.py` và logic bằng port Python; bước biên dịch thật vẫn cần CI/Windows.
- Tile overlay không hắt màu lên *mặt* phím lân cận (chỉ lên giường phím) để tránh xung đột cache; phần
  loang màu trên phím vẫn có từ lớp glow sẵn có của stage.

---

## 7. Đợt tái cấu trúc shell theo Embers (25 ảnh tham chiếu)

Sau khi rà từng ảnh chụp Embers 2.3, Keyflow được bổ sung một "shell" đúng mô hình đó, trong khi
giữ nguyên stage + dock hiện có làm phần "Design" chi tiết:

| Ảnh Embers | Thành phần | Keyflow tương ứng |
|---|---|---|
| 1. Main menu (Play/Design/Showcase/Settings/About/Exit trên nền nebula) | `MainMenuOverlay` trong `MainWindow.xaml` | Card logo + `Play` / `Design` + liên kết `Settings` / `About` / `Exit`; mở khi khởi động thường; nút HOME trên header để quay lại; `--verify`/`--snapshot`/`--show-settings` bỏ qua menu để CI/chụp ảnh không đổi. |
| 2. Styles grid (thumbnail bàn phím + nốt màu) | `PresetThumbnail()` trong `MainWindow.Settings.cs` | Mỗi preset trong danh sách Style có thumbnail mini render thật (nền tối, phím trắng/đen, vạch nốt theo palette của preset + vạch halo), cache theo tên+màu. |
| 3–6. Style editor: section Notes / Keys / Embers, nhóm ADVANCED, nút Save, tab BACK | Dock 10 trang hiện có | Các section tương đương đã có (Notes/Keyboard/Particles...); chevron trong Play dialog deep-link đúng trang. |
| 7–11. Play dialog: MIDI File / Live Play, 2 card Left/Right Hand viền màu, Speed + nút reset, danh sách OPTIONS (Camera, Background, Notes, Embers, Halo, Flame, Keys, Extras) mỗi hàng toggle + chevron, nút Play lớn | `PlayDialogOverlay` + `MainWindow.Menu.cs` | Đúng từng hàng: toggle ánh xạ 1-1 vào setting thật (`ShowBackground/ShowNotes/ShowEmbers/ShowHalo/ShowFlame/ShowKeys`, Extras = `ShowCounter/ShowWatermark`) nên dock và dialog không lệch nhau; chevron mở đúng tab dock; card tay biên màu theo `LeftHandColor/RightHandColor` (hoặc gradient nốt); Speed = `NoteFallSpeed` kèm reset. |
| 12–13. Settings dialog (Display/Audio/MIDI/Other, chọn SoundFont) | Menu → Settings | Mở dock tại trang Audio (SoundFont, MIDI input/output, prevent overlaps...). |
| 14–25. Camera (Parallax/Zoom/Saturation/Contrast/Bloom + ADVANCED), Background (Select Image/Dim/Guide/Gradient), Notes (3D Notes), Halo (Halo Color + color picker HSV/HEX), Flame (Plasma/Rays), Keys (Lighting/Overhang/Animate), Extras (Counter/Watermark) | Các trang dock | Đủ hết thông số tương đương đã có sẵn; color picker HSV/HEX là `ColorPickerWindow`. |

### Xử lí đồ họa ánh sáng "như Embers"
- Đường lửa **halo** trên mép phím = `ShowHalo` + `DrawImpactLine` (toggle riêng trong Play dialog).
- **Keys lighting** = `KeyLighting` + phát xạ của shader ray-trace (phím kêu tự phát sáng và hắt sáng).
- **Embers** = emitter sparks/wisps với đầy đủ Velocity/Spread/Response/Gravity/Drag/Vector Field đã có.
- **Bloom / saturation / contrast / parallax / zoom** ở trang Camera; **Dim / Guide / Gradient** ở Background.
- Khác biệt đã biết: Embers cho mỗi tay một *style document* độc lập; Keyflow hiện hỗ trợ tách màu
  hai tay (ColorMode PerHand) chứ chưa tách toàn bộ style — card Left/Right trong Play dialog hiện
  mở trang Notes để chỉnh, đây là giới hạn được ghi nhận chứ không giả vờ có.

## 8. Đợt nâng cấp "concert shell" (giao diện hoà nhạc / Your Lie in April)

- **Ba giao diện** trong `Theme/ShellTheme.cs`: *Sakura Nocturne* (mặc định — đêm chàm, hồng hoa
  anh đào #FF7BAC và vàng ấm), *Concert Noir* (violet #8B5CFF → cyan #25D0FF, bản gốc), *Velvet Gold*
  (nhung đỏ + đồng thau). Mọi token màu (Accent, Glow, Petal, Panel, Control, Track, Popup…) được
  `ShellThemeManager` ghi đè vào `Application.Resources`; XAML đọc bằng `DynamicResource` nên cả
  header, dock, menu, hộp thoại và thanh trượt đổi màu trong một khung hình, không cần mở lại app.
- **Backdrop động** (`Theme/ChromeBackdrop.cs`): hoa anh đào rơi, quầng cực quang hay nếp nhung tuỳ
  theme, mật độ theo `BackdropDensity`, biên độ theo `ChromeMotion` (Off/Calm/Full). Dùng cho nền
  main menu và sau lưng dock cài đặt.
- **Đồng hồ khung hình dùng chung** (`Ui/FrameClock.cs`): `CompositionTarget.Rendering` là nguồn duy
  nhất cho mọi animation; đếm yêu cầu (`Acquire`/`Release`) nên khi sân khấu đứng yên thì WPF không
  phải vẽ thêm khung nào. Sân khấu (nốt rơi, tia lửa, petal) và backdrop chạy cùng nhịp
  vsync, hết hiện tượng lệch nhịp giữa hai `DispatcherTimer` khác chu kỳ.
- **Chuyển động giao diện** (`Ui/ChromeMotion.cs`): fade/slide/pop/cascade/pulse dùng chung một bộ
  easing, tôn trọng `SystemParameters.ClientAreaAnimation`; `--snapshot`/`--show-settings` gọi
  `DisableChromeMotion()` để ảnh chụp luôn tất định.
- **Lớp sân khấu mới**: `DrawPetals` (petal bay theo hàm của thời gian, không tích luỹ sai số;
  số lượng = `PetalAmount` × tỉ lệ bề rộng, trần 150). Lớp đèn quét `DrawSpotlights` đã xóa trong
  đợt effects-redesign v1 (xem `docs/EFFECTS-REDESIGN.md`).
- **Hiệu năng**: bỏ `DispatcherTimer` 16 ms của sân khấu, nhãn thống kê/thanh thời gian chỉ cập nhật
  khi giá trị đổi, pen lưới guide và mọi brush/petal được cache; `HasActiveEffects` bao gồm
  cả lớp petal và impact wave/flash nên chỉ chạy khung hình khi thật sự có gì chuyển động.
