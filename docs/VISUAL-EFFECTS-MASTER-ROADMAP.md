# PianoPath — Visual Effects Master Roadmap

> **Mục đích:** lộ trình tái thiết kế art direction, chức năng và đường render của toàn bộ hiệu ứng PianoPath; chuẩn bị để cùng một effect chạy trên sân khấu GPU, bản ghi và AR overlay đàn thật.  
> **Ngày lập:** 2026-10-06  
> **Trạng thái:** roadmap đã chốt; Phase 0 chưa đóng. Đã bổ sung WPF `Spotlights` và regression assertion; còn chờ build/`--verify` trên Windows/.NET.  
> **Tài liệu nền:** [`EFFECTS-REDESIGN.md`](EFFECTS-REDESIGN.md), [`AR-OVERLAY-ROADMAP.md`](AR-OVERLAY-ROADMAP.md), [`SETTINGS-WIRING-AUDIT.md`](SETTINGS-WIRING-AUDIT.md), [`UI-SHADER-REVIEW.md`](UI-SHADER-REVIEW.md).

---

## 1. Tóm tắt quyết định

PianoPath **đã có một danh mục rộng**, gồm 87 mục trong `Stage/Effects/EffectCatalog.cs`, engine Direct3D 11/HLSL, shader HDR/bloom, preset, MIDI/live play, ghi hình và nền tảng camera/calibration AR. Việc tiếp theo không nên là thêm thật nhiều tên effect hoặc viết lại engine từ đầu. Mục tiêu là đưa những gì đang có thành **một hệ hiệu ứng có ngôn ngữ hình ảnh thống nhất, phản ứng đúng với âm nhạc, được kiểm chứng trên từng đường render và có thể chiếu lên đàn thật**.

### Kết quả cuối cần đạt

1. **Đẹp nhưng đọc nốt dễ:** người xem nhận ra cao độ, thời điểm chạm phím và tay chơi trước khi bị hạt, bloom hoặc nền làm phân tán.
2. **Mỗi cài đặt có chức năng thật:** thay đổi intensity/size/speed/amount phải làm thay đổi output đo được; không có toggle “trang trí”.
3. **Một ngữ nghĩa effect, nhiều output:** sân khấu thường, live MIDI, phát MIDI, ghi video và AR dùng cùng định nghĩa sự kiện/điểm neo; khác nhau ở phép chiếu và giới hạn thiết bị.
4. **Mở rộng mà không nhân mã:** Fire/Ice/Galaxy… là recipe/preset, không phải các renderer đặc biệt riêng.
5. **Làm theo lát dọc:** một effect chỉ được coi là hoàn tất khi có thiết kế → trigger thật → render → cài đặt/lưu preset → test → kiểm tra ảnh/hiệu năng. Chỉ khi qua gate mới chuyển sang effect kế tiếp.

### Luật không thương lượng

- **Không làm lại toàn bộ cùng lúc.** Giữ engine D3D11 hiện tại; refactor qua từng lát dọc.
- **Không tăng số lượng trước khi audit.** “87 mục trong catalog” không đồng nghĩa “87 thuật toán độc lập” hoặc “87 mục đã được chứng minh chạy giống nhau trên GPU, software fallback, AR và video”.
- **AR được tính từ thiết kế ban đầu.** Mỗi effect phải biết nó neo vào phím, nốt, đường chạm hay nền; cần hỗ trợ AR, hạ chất lượng có giải thích, hoặc được đánh dấu là chỉ dành cho stage. Không được âm thầm biến mất khi bật AR.
- **MIDI là nguồn sự thật.** Note-on/off, velocity, pedal và tempo quyết định hiệu ứng; camera chỉ cung cấp vị trí/khung nền/mặt nạ tay.
- Lấy cảm hứng từ chuẩn hoàn thiện của các piano visualizer như Keysight/SeeMusic ở **mức độ mượt, sáng rõ, trình diễn và trải nghiệm**, không sao chép nhận diện thương hiệu, asset hay preset độc quyền.

---

## 2. Hiện trạng trong repository và khoảng cách cần xử lý

| Mảng | Đang có | Hướng xử lý |
|---|---|---|
| Sân khấu | D3D11 là đường render mặc định; HLSL, `GpuStageSimulation`, HDR/bloom, loop dựng riêng; có đường software/WPF và `--snapshot`/`--bench`. | Giữ làm nền. Trước khi chỉnh look, chụp và đo baseline để không phá preset/đường ghi. |
| Nốt và FX | `PianoStage.cs`, `GpuStageSimulation.cs`, `GpuStageSimulation.Effects.cs`, `GpuStageSimulation.Ambient.cs`; có falling/impact/hold/release, particle, ambient, modulator, background motion, keyboard shading. | Kiểm tra parity từng effect giữa GPU và đường fallback; thống nhất trigger, time, tọa độ, màu và giới hạn particle. |
| Cài đặt/UI | `PianoVisualSettings.cs`; trang được dựng trong `Ui/MainWindow.Settings.cs`; nhiều control đã có `Clamp()`, preset và lưu JSON. | Đối chiếu mỗi control với đúng đoạn renderer tiêu thụ nó; tinh gọn UI để người mới không phải chỉnh hàng chục slider. |
| Danh mục | `EffectCatalog.cs` ghi 87 mục; 86 `Available`, còn `falling.color-gradient` `Planned` cho tới khi có gradient head-to-tail riêng. Một số tên là alias/chia sẻ kênh. | Phase 0 audit đã đối chiếu ID với settings/UI/backend/test; `Available` không chứng minh từng option, parity hoặc AR. Danh sách alias/gap nằm ở [`EFFECT-COVERAGE-AUDIT.md`](EFFECT-COVERAGE-AUDIT.md). |
| Nối danh mục với ứng dụng | Audit Phase 0 xác nhận `EffectCatalog` chưa làm runtime source; settings/default/normalize và lựa chọn UI vẫn là dữ liệu chạy thật. | Quyết định Phase 0: giữ `EffectCatalog` là inventory/metadata, không gắn `EffectStatus` vào UI. Sau khi Phase 1 chuẩn hóa ID/alias/capability, thiết kế typed registry nếu cần; tránh đưa alias mơ hồ vào runtime. Chi tiết: [`EFFECT-COVERAGE-AUDIT.md`](EFFECT-COVERAGE-AUDIT.md). |
| AR | Đã có camera texture trên GPU, ring buffer/timestamp, calibration/homography, key patches và note quads có perspective correction; Learn/Perform, delay và recording có verify. | Phase 0 baseline: `GpuArLayer` chỉ chiếu key/note geometry; một số ambient còn overlay screen-space, không qua calibration; stage-local lifecycle effects chưa có AR capability riêng. Xem [`EFFECT-COVERAGE-AUDIT.md`](EFFECT-COVERAGE-AUDIT.md) và `AR-OVERLAY-ROADMAP.md` §12. |
| Audio-reactive | Đã có khối FFT/audio trong repository, nhưng đường “Audio Reactive” của visualizer chưa nên được gọi là spectrum/FFT thật cho đến khi xác nhận lấy PCM, smoothing và latency. | Tách phân tích âm thanh khỏi audio callback/UI; triển khai band/envelope thật ở phase riêng và test không làm nghẽn playback. |
| Preset & chia sẻ | Có preset built-in/user, JSON và mã chia sẻ; hiệu ứng mới cần giữ tương thích file cũ. | Dùng ID ổn định, version/schema migration; preset chỉ mô tả look, không mang camera/calibration/thiết lập riêng máy. |

**Lưu ý quan trọng:** catalog hiện cộng 40 effect theo vòng đời nốt + 31 mục ambient + 9 modulator + 7 theme = **87 mục**. Đây là 87 mục sản phẩm trong danh mục, không phải 87 renderer độc lập. Ví dụ: Glow/Neon, Rainbow Trail, Ribbon, Flash, Fire/Embers và Water Ripple xuất hiện ở nhiều vai trò hoặc dùng chung kênh. Roadmap sẽ chuẩn hóa ngữ nghĩa nhưng **không xóa tên/preset cũ một cách phá tương thích**.

---

## 3. Art direction: “PianoPath Concert FX”

### 3.1 Ngôn ngữ hình ảnh

- **Sân khấu là sân khấu, không phải bảng đèn hiệu ứng:** nền tối sâu, dải phím và đường chạm là điểm neo thị giác; ánh sáng có lý do từ nốt nhạc.
- **Một tiêu điểm tại một thời điểm:** nốt đang tới/chạm phím là foreground; effect impact là khoảnh khắc ngắn; ambience tạo chiều sâu chứ không tranh điểm nhìn.
- **Vật liệu có tính cách riêng:** Neon = lõi + viền phát sáng; Glass = truyền sáng/refraction tiết chế; Fire = nhiệt/ember; Water = ripple/caustic mềm; Paper/Confetti = hình rõ, không phát sáng quá mức; Dust/Smoke = thể tích mờ có chiều sâu.
- **Chuyển động bám vào nhạc:** chuyển động theo pitch, velocity, beat, pedal và thời lượng nốt; random chỉ tạo biến thiên tự nhiên, không làm mất khả năng lặp lại một cảnh.
- **Cùng palette, không đồng nghĩa cùng một gradient:** màu nốt phải ổn định theo hand/track/pitch khi người dùng chọn; ánh sáng, hạt và bloom dẫn xuất từ màu đó.

### 3.2 Ba hướng look làm mốc thiết kế

Đây là ba **mốc art direction** để kiểm thử hệ thống, không bắt buộc đổi tên theme hiện có:

| Look | Mục đích | Quy tắc hình ảnh |
|---|---|---|
| **Concert / Cinematic** | Video biểu diễn, đàn grand, nhạc cổ điển. | Đen/indigo/amber, tương phản có kiểm soát, phím có vật liệu, bloom mềm, ambience ít nhưng sâu. |
| **Neon / Electronic** | Cover hiện đại, EDM, stream. | Màu bão hòa theo tay/track, viền rõ, trail và energy có nhịp, tránh phủ neon lên mọi pixel. |
| **Clean / Learning** | Học nốt, tutorial, đọc bản nhạc. | Nốt/phím/nhãn rõ nhất; ambient tắt hoặc rất nhẹ; không có flash toàn màn hình, tối thiểu chuyển động phụ. |

Mỗi preset phải có **ảnh preview deterministically render** trên cùng một đoạn MIDI/chord, để so chất lượng và không chỉ dựa vào mô tả bằng chữ.

### 3.3 Quy tắc tạo hình và hậu kỳ

1. **Nốt:** thân có mặt chính dễ đọc, mép/đầu sáng vừa đủ, gradient không che phân biệt note; chiều rộng khớp hình học bàn phím, chuyển động mượt ở 60–240 FPS.
2. **Trail:** nối với thân nốt, chạy theo hướng chuyển động; khi tăng length phải kéo dài trail thật, không chỉ tăng alpha.
3. **Impact:** neo vào đúng tâm phím/pitch; envelope attack nhanh → peak → decay; không kéo dài để che nốt kế tiếp.
4. **Particle:** mỗi họ có vận tốc, trọng lực, drag, lifetime, shape và màu riêng; random có seed; có trần số hạt và giảm tải theo quality.
5. **Bloom:** tổng hợp trong linear light/HDR, đa kích thước nếu cần; giữ chi tiết nốt và phím, không dùng glow để che aliasing hoặc sai hình học.
6. **Keyboard:** giữ shading vật liệu hiện tại (GGX/softbox/ACES ở `Stage/Shading/*`) nhưng ánh sáng phím phải phản ứng từ cùng nguồn màu/velocity của nốt.
7. **Tương phản:** có profile cho color-blind/đọc nhãn; màu không phải tín hiệu duy nhất—có thể phân biệt bằng viền, pattern hoặc hand split.
8. **Flash safety:** không dùng strobe toàn khung hình làm mặc định; có tùy chọn **Reduce flashes / Reduced motion**; flash tác động cục bộ tại phím, ngắn và có trần cường độ.
9. **Không “effect soup”:** preset mặc định chỉ bật vài lớp có chủ đích; cho phép tắt từng layer; có master intensity/quality để giảm nhanh độ dày thị giác.

### 3.4 Các lớp render chuẩn

**Stage thường:**

```text
Backplate (solid / image / chroma)
  → Atmosphere / procedural background
  → Sheet, guide lanes, hit line
  → Falling notes + material + trails
  → Impact / hold / release effects + ambient particles
  → Keyboard surface + per-key light/shadow
  → labels / watermark / UI chrome
```

**AR overlay:**

```text
Timestamped camera frame
  → camera image / color conversion
  → AR projector: keyboard-space effect geometry → image-space
  → overlay render (note, trail, impact, key glow, particles)
  → hand occlusion mask (tay thật nằm trước effect)
  → composite camera + overlay + output/recording
```

Stage, record và AR phải tiêu thụ cùng effect event. Không xây một “bộ hiệu ứng AR” thứ hai với ý nghĩa khác.

---

## 4. Chuẩn chức năng: một effect được định nghĩa ra sao?

Mỗi effect mới hoặc effect được tái thiết kế phải có đủ hợp đồng sau. Nếu thiếu một mục, effect vẫn là **Draft**, chưa đưa lên UI/preset mặc định.

| Trường hợp đồng | Cần mô tả |
|---|---|
| ID ổn định | ID không đổi theo cách viết tên; dùng trong JSON/preset/migration. |
| Vai trò | `falling`, `impact`, `hold`, `release`, `ambient`, `keyboard`, `background` hoặc `modulator`. |
| Trigger | Note On, note đang rơi, key held, Note Off, pedal, beat, FFT band hoặc thời gian nền. |
| Anchor/tọa độ | Tâm phím, thân nốt, hit line, vùng phím, stage background, mặt phẳng phím AR hay không gian 3D. |
| Vòng đời | Attack/hold/decay/lifetime, cách pause/seek/stop/reset; hành vi khi note chồng cùng pitch. |
| Tham số | Default, miền/clamp, đơn vị, tác dụng nhìn thấy được, liên kết UI, preset và reset. Chỉ hiện tham số thật sự có ý nghĩa. |
| Điều chế | Có nhận velocity/pitch/hand/track/pedal/beat/audio không; công thức và giới hạn. |
| Render | Channel, material/blend, pass HLSL, cost class, trần instance/particle và quality fallback. |
| Capability | GPU stage / software fallback / recording / AR surface / AR image-space / AR world-space; fallback khi không hỗ trợ. |
| Accessibility | Có flash không, reduced-motion behavior, color-independent cue và mức an toàn. |
| Kiểm chứng | Unit test, `--verify`, preview/golden render, trigger thật và benchmark cần thiết. |

### Mô hình dữ liệu đề nghị

Không cần triển khai đúng tên lớp ngay, nhưng runtime cần ngữ nghĩa tương đương:

```text
MIDI file / live MIDI
       → timestamp + note state (pitch, velocity, track/hand, start/end, pedal, beat)
       → effect event (stable ID, anchor, normalized keyboard coordinate, lifetime, seed)
       → effect simulation (song clock / monotonic clock, độc lập UI thread)
       → render passes (GPU stage / AR projector / recording)
```

- Dùng `KeyboardLayout` làm nguồn hình học duy nhất cho 49/61/76/88 phím.
- Tọa độ logic ưu tiên **keyboard-space chuẩn hóa** (x theo tâm/phần rộng phím; y=0 là mép phím/hit line; khoảng cách nốt có ý nghĩa độc lập độ phân giải). Chuyển sang pixel chỉ ở renderer cuối.
- Phân biệt **song time** (phát MIDI/seek/tempo map) với **wall-clock** (live MIDI/camera); một khung nặng không được làm lệch âm thanh hoặc đầu vào MIDI.
- Effect dùng thời gian/seed xác định để `--snapshot`, video và test cho kết quả tái lập; live play vẫn được phép có biến thiên seed theo event.
- GPU/render thread không gọi ngược vào UI hoặc chặn audio callback. Audio analysis nếu cần chạy riêng và đưa kết quả đã làm mượt sang render loop.

---

## 5. Danh sách hiệu ứng mục tiêu

Danh sách bên dưới giữ nguyên 87 mục hiện hữu để không mất chức năng/preset. Phần mô tả là **hành vi sản phẩm cần bảo đảm**, không khẳng định mọi renderer hiện tại đã có cùng chất lượng hay AR support.

### 5.1 Vòng đời nốt — Falling (10)

| Effect | Hành vi cần thấy |
|---|---|
| Glow Trail | Quầng sáng mềm kéo theo thân nốt; độ dài và cường độ chỉnh độc lập. |
| Motion Blur | Mờ định hướng theo vector rơi/đi lên, không làm mờ toàn scene. |
| Sparkle Tail | Tia lấp lánh nhỏ rời trail, có lifetime ngắn và giới hạn mật độ. |
| Color Gradient | Chuyển sắc đầu–đuôi giúp đọc hướng và chuyển động của nốt. |
| Pulsing | Nốt thở sáng/tối; tốc độ có thể sync beat hoặc chạy tự do. |
| Ribbon Twist | Dải lụa xoắn theo hành trình; chuyển động mượt, không đổi x/pitch. |
| Particle Stream | Dòng hạt xuất phát từ thân nốt, có hướng/velocity nhất quán. |
| Ghost Notes | Echo mờ của nốt dẫn đường; khoảng cách echo gắn với thời gian, không nhân bản note logic. |
| Speed Lines | Các vệt tốc độ ngắn theo vận tốc nốt; tự giảm khi density cao. |
| Rainbow Shift | Hue dịch chuyển liên tục nhưng vẫn giữ tương phản với nền và hand split. |

### 5.2 Vòng đời nốt — Impact (16)

| Effect | Hành vi cần thấy |
|---|---|
| Burst / Explosion | Cụm hạt bung từ đúng tâm phím, kích thước/cường độ theo velocity. |
| Ember Burst | Tia lửa nóng nguội dần theo màu/nhiệt, có trọng lực và drag riêng. |
| Ripple Ring | Vòng rỗng lan ra như cộng hưởng âm học. |
| Shockwave | Sóng đặc có viền sáng chạy ra ngoài; nhanh hơn và nặng hơn Ripple. |
| Flash | Flare cục bộ ngắn tại điểm chạm; không flash cả màn hình. |
| Key Press Glow | Phím sáng lên khi impact rồi chuyển mượt sang trạng thái hold. |
| Splash | Giọt nước bắn ra theo quỹ đạo, khác shape và physics với ember. |
| Firework | Tia/hoa nhỏ bung thành nhiều nhánh, có fade và giới hạn số lần nổ. |
| Confetti Pop | Miếng giấy màu rõ hình, quay và rơi; không dùng bloom quá mạnh. |
| Dust Cloud | Mây bụi mềm bốc lên, alpha phân lớp và tan tự nhiên. |
| Shatter / Break | Note vỡ thành shard tại hit line, mảnh không xuyên qua khu vực che tay trong AR. |
| Bounce | Thân nốt bật khỏi phím một lần rồi tiêu biến. |
| Absorb | Thân nốt co/hút vào tâm phím; key glow nhận lại màu năng lượng. |
| Melt | Note mềm/chảy thành giọt; cần giữ silhouette đủ lâu để thấy điểm kết thúc nốt. |
| Lightning Strike | Tia sét đi từ một điểm neo tới phím đúng pitch, không chọn ngẫu nhiên phím khác. |
| Note Morph | Nốt biến thành hình/biểu tượng đã chọn; shape phải đọc được ở kích thước nhỏ. |

### 5.3 Vòng đời nốt — Hold (8)

| Effect | Hành vi cần thấy |
|---|---|
| Flame Pillar | Cột lửa tồn tại trong lúc phím/nốt được giữ; chiều cao theo intensity. |
| Sustain Particles | Hạt/wisp phát liên tục từ phím đang giữ, có trần emitter. |
| Energy Column | Cột sáng thẳng và ổn định; phù hợp AR surface với điểm neo đúng phím. |
| Hold Bar | Thân note đang kêu có lớp nhấn/viền nóng theo thời lượng giữ. |
| Breathing Glow | Nốt và phím sáng theo chu kỳ mềm; không làm thay đổi timing âm nhạc. |
| Vibration | Rung rất nhỏ trên thân nốt/viền; mặc định không làm lệch vị trí pitch. |
| Color Cycle | Chuyển màu khi giữ; tùy chọn khóa màu gốc để không ảnh hưởng bài học. |
| Electric Arc | Hồ quang nối các phím/nốt đang cùng hoạt động; chọn cặp gần/âm nhạc thay vì nối ngẫu nhiên. |

### 5.4 Vòng đời nốt — Release (6)

| Effect | Hành vi cần thấy |
|---|---|
| Fade Out | Mặc định an toàn: giảm opacity/energy mượt tại Note Off. |
| Float Up | Mote sáng bay lên từ phím sau khi thả. |
| Dissolve | Thân nốt tan thành các hạt/pixel rồi biến mất. |
| Smoke Puff | Một cụm khói nhỏ tại điểm thả, ngắn và không che nốt lân cận. |
| Snap Back | Note thu nhanh về điểm neo phím, có easing nhưng không giật. |
| Echo Rings | Một vài vòng nhỏ dội ra ở release, giảm dần về alpha/size. |

### 5.5 Ambient — Particle & Energy (9)

> Trong catalog hiện tại, nhóm này có một số tên thực chất là event/hold effect (ví dụ Fire/Explosion/Embers). Khi chuẩn hóa, giữ tên/API cũ nhưng tách **trigger theo nốt** khỏi **lớp nền chạy liên tục**.

| Effect | Hành vi cần thấy |
|---|---|
| Embers | Than hồng thưa bay lên từ vùng phím vừa chơi hoặc làm nền tùy chế độ. |
| Fire / Flames | Lửa gắn với phím/nốt giữ; không chạy liên tục khi không có nguồn. |
| Explosion / Burst | Biến thể burst mạnh theo nốt/velocity, không phải layer nền liên tục. |
| Sparkles / Stars | Trường sao nền có mật độ/số lượng giới hạn. |
| Lightning / Electric | Các tia điện chạy theo vùng hoặc nhịp, có điểm đầu/cuối rõ. |
| Laser Beams | Tia quét sân khấu hoặc phát từ hit point; intensity/speed độc lập. |
| Plasma | Quả cầu/halo năng lượng cục bộ, có lõi và viền chứ không chỉ là flash khác màu. |
| Confetti | Mưa giấy theo sự kiện/preset lễ hội; không bật mặc định khi học. |
| Firework | Pháo hoa nền/đúng nhịp, có rate limit tránh spam ở hợp âm dày. |

### 5.6 Ambient — Nature & Elements (9)

| Effect | Hành vi cần thấy |
|---|---|
| Petals / Cherry Blossom | Cánh hoa có xoay, drift theo gió; không che hit line lâu. |
| Water Ripple | Ripple nước neo theo phím/hit point; phân biệt với ring âm học bằng chất liệu. |
| Rain / Droplets | Mưa theo chiều sâu/tốc độ; có thể tăng cường theo beat hoặc velocity. |
| Snow / Ice | Tuyết nền; tùy chọn frost/key highlight chỉ khi không gây mất tương phản. |
| Smoke / Fog | Sương/khói có vùng, độ sâu và turbulence; cắt alpha khi che nốt. |
| Wind / Leaves | Lá bay có quỹ đạo không đồng nhất nhưng chuyển động tổng thể cùng một hướng gió. |
| Butterflies | Chuyển động mềm, thưa; chỉ dùng trong preset, không nên là default. |
| Aurora / Northern Lights | Dải sáng nền procedural, không đè tương phản lên thân nốt. |
| Dust Cloud | Bụi mịn treo trong không khí; mật độ thay đổi theo quality. |

### 5.7 Ambient — Light & Color (7)

| Effect | Hành vi cần thấy |
|---|---|
| Glow / Neon | Lớp phát sáng vật liệu của note/key; không phải một particle system riêng. |
| Bloom | Hậu kỳ HDR dùng chung, có master control và chất lượng thấp/cao. |
| Flash | Flare màu/trắng cục bộ; alias với impact flash cần hợp nhất ở metadata. |
| Color Splash / Paint | Đốm màu/tán sắc xuất hiện theo hit, tan mềm. |
| Rainbow Trail | Dải nhiều màu theo đường melody; cần giới hạn bão hòa và độ dày. |
| Gradient Wave | Sóng màu ngang qua scene; giữ vùng notes dễ đọc. |
| Prism / Crystal | Tách màu/refraction có kiểm soát; ở quality thấp chuyển thành gradient/edge tint. |

### 5.8 Ambient — Cosmic & Abstract (6)

| Effect | Hành vi cần thấy |
|---|---|
| Galaxy / Nebula | Trường mây vũ trụ procedural, chuyển động chậm sau notes. |
| Black Hole | Vortex/hút particle theo điểm tâm; không bẻ méo tọa độ nốt. |
| Matrix Rain | Cột ký tự chạy; chỉ trong preset/game-style, có lựa chọn mật độ thấp. |
| Geometric Shapes | Hình khối/đường hình học trôi/đổi hướng theo beat. |
| Fractal | Pattern procedural có detail/quality giới hạn để tránh tốn GPU. |
| Ribbon / Trail | Dải lụa theo melody; là style/alias của falling ribbon khi gắn nốt. |

### 5.9 Smart modulators (9) — dữ liệu điều khiển, không tự vẽ

| Modulator | Input → output |
|---|---|
| Velocity Size | Lực phím → kích thước burst/wave/nốt trong giới hạn. |
| Velocity Mapping | Lực phím → cường độ/độ sáng; đường cong có thể chọn linear hoặc mềm. |
| Key Color Mapping | Pitch/hand/track → màu ổn định cho mỗi nốt. |
| Velocity Color | Nhẹ → màu mát, mạnh → màu ấm; hỗ trợ palette an toàn cho mù màu. |
| Octave Color | Quãng tám → hue/nhóm màu riêng; có blend để không làm mất màu tay. |
| Pedal Glow | Sustain pedal → key glow/cộng hưởng; note release vẫn tôn trọng MIDI timing. |
| Tempo Sync | Tempo map/beat phase → nhịp pulse/energy; xử lý đúng khi đổi tempo/seek. |
| Audio Reactive | PCM envelope/FFT band → ambient/bloom/particle; phải có smoothing và latency budget. |
| Zone Split FX | Bass/treble hoặc vùng do người dùng chọn → effect/palette khác nhau. |

### 5.10 Combo themes (7) — recipe/preset graph

| Theme | Recipe định hướng |
|---|---|
| Fire / Inferno | Glow trail → ember burst + shockwave → flame/hold bar → smoke; nền ember haze nhẹ. |
| Ice / Crystal | Sparkle trail → splash/ripple/shatter → frozen/glass hold → dissolve; snow thưa. |
| Galaxy / Voyage | Rainbow/meteor trail → firework + plasma + shockwave → breathing glow → stardust; nebula nền. |
| Sakura / Nocturne | Ribbon → petal/confetti impact → soft breathing glow → float-up petals; rose/indigo. |
| Electric / Storm | Speed lines → lightning + shockwave → arcs → snap; bão điện chỉ chạy nền ở preset. |
| Ocean / Depths | Glass drop → splash/ripple → water column → foam/dissolve; ocean flow nền. |
| Retro / Arcade | Pixel/ghost notes → 8-bit burst/bounce → blink → snap; matrix/geometric theo preset. |

Theme chỉ là một đồ thị thiết lập các effect đã có. Chọn theme không được ghi đè camera, calibration, keyboard size, audio device hoặc thiết lập riêng máy.

### 5.11 Hệ hình ảnh bổ trợ ngoài con số 87

Những thứ dưới đây là “visual system” quan trọng nhưng không cộng vào số 87:

- **Note material:** Solid / Neon / Glass / Fire; roundness, edge, tint, head glow, shimmer.
- **Màu nốt:** Gradient, PerHand, PerTrack, RainbowPitch, RainbowTime; track/hand colors.
- **Background motion:** Aurora, Nebula, Prism, Ember Haze, Ocean Flow, Retro Grid; có None và amount/speed/color.
- **Hit-line motion:** Pulse, Sweep, Twin Comets, Spectrum, Electric Arc, Ripple.
- **Bàn phím:** Classic/Studio/Glass, nhãn phím, key light, shadow/contact occlusion, vật liệu và nốt phím đang sáng.
- **Stage layout:** guide lanes, sheet layer, hit line, background image/chroma, vignette, watermark và recording output.

Các mục này cũng phải đi qua cùng rule: preset preview, visibility, tooltip/localization, storage/migration, capability và test.

---

## 6. Hướng AR: cùng effect, đúng không gian, có fallback rõ

### 6.1 Ba tầng không gian

| Tầng | Mô tả | Phép chiếu cần | Ví dụ |
|---|---|---|---|
| **AR Surface** | Effect nằm trên/phía trên mặt phẳng phím; ưu tiên ra mắt đầu tiên. | `KeyboardLayout` + calibration/homography hiện có. | Key glow, note bar trên phím, ripple, burst, nhãn phím. |
| **AR Image-space** | Effect bay từ phím lên trong ảnh; vẫn là overlay 2D được neo theo dải phím. | Homography + hướng bay trong ảnh; cần giữ đúng timestamp/AR view. | Falling/rising note, ribbon, trail, hạt nhỏ. |
| **AR World-space** | Cột/đường sáng có chiều sâu thật, thay đổi phối cảnh theo camera. | Pose/FOV/camera calibration và sau đó occlusion/depth. Homography mặt phím đơn thuần không đủ vì không chiếu được độ cao z. | Cột sáng đứng trước đàn, beam 3D, bóng tiếp xúc/parallax. |

### 6.2 Hành vi Learn và Perform

- **Learn:** nốt tương lai trượt tới đúng phím; có lookahead; hit/target sáng rõ; có thể giảm ambient/flash để hỗ trợ học.
- **Perform:** không vẽ nốt tương lai; note-on live tạo nốt/vệt bay khỏi phím theo velocity và release; key light khớp với MIDI. Một preset riêng có thể tối ưu cho quay/stream.
- **Hand occlusion:** camera thật → mask/độ tin cậy → effect phía sau bàn tay bị che. Nếu chưa có mask chất lượng, hạ effect xuống sau vùng phím thay vì vẽ xuyên qua tay.
- **Latency:** delay/age gắn với camera source, không giả định một số dùng chung cho mọi webcam; đo bằng timestamp, hiệu chỉnh camera overlay nhưng **không bao giờ trì hoãn âm thanh**.
- **Preset:** lưu visual recipe và lựa chọn AR look; calibration, nguồn camera và delay theo camera là dữ liệu máy, không xuất trong preset/chia sẻ.

### 6.3 Ma trận hỗ trợ cần công khai

Mỗi effect hiển thị một capability badge/trạng thái:

- **Stage + Recording:** chạy trên sân khấu GPU và được lấy từ cùng frame compositor khi ghi.
- **AR Surface:** vẽ lên đàn thật qua keyboard coordinate/homography.
- **AR Image-space:** bay/di chuyển trong ảnh nhưng vẫn neo vào keyboard.
- **AR World-space:** cần projection 3D/camera pose/occlusion.
- **Stage-only:** không phù hợp với camera thật; nếu chọn trong AR thì hiện lý do và fallback cụ thể, không âm thầm mất hiệu ứng.

**Ưu tiên AR MVP:** key glow, note body, falling/rising trail, impact ring/burst, hold glow, release particles. Background cosmic/fullscreen shader và motion blur nặng có thể giảm/tắt ở AR quality thấp; danh sách cần được nói rõ trong UI.

---

## 7. Trải nghiệm chỉnh sửa: Visual Effects Studio

UI nên đi từ “mở nhiều trang cài đặt và dò slider” sang ba độ sâu thao tác:

1. **Quick Adjust:** chọn preset; master intensity/particle density/quality; màu tay; bật/tắt ambient; Reduced Motion/Reduce Flash.
2. **Effects Studio:** nhóm theo Falling / Impact / Hold / Release / Ambient / Keyboard / AR; tìm kiếm theo tên/từ khóa/tác dụng; bật/tắt effect, kéo thứ tự layer nếu phù hợp.
3. **Inspector:** chỉ hiện thông số hợp lệ của effect đang chọn; preview thay đổi ngay; có reset riêng effect, compare A/B và tooltip giải thích bằng ngôn ngữ thường.

### Bố cục đề nghị

```text
┌──────────────────┬──────────────────────────┬────────────────────┐
│ Category + search│ Live preview / test MIDI │ Inspector          │
│ Effect cards     │ Learn / Perform / AR view│ Parameters + reset │
│ AR + cost badges │ A/B + deterministic demo │ Accessibility      │
└──────────────────┴──────────────────────────┴────────────────────┘
```

- Preview có sẵn cảnh test xác định: nốt đơn velocity thấp/cao, hợp âm dày, legato, sustain pedal, tempo chậm/nhanh và live MIDI.
- Card effect có thumbnail, trigger, cost (Low/Medium/High), AR badge và trạng thái on/off; không bắt người dùng hiểu tên kênh renderer.
- Có chế độ **Simple / Advanced**: Simple chỉ show điều khiển thường dùng; Advanced mở shader/physics/cost khi người dùng muốn tinh chỉnh.
- Cho phép nghe/nhìn một preset trên cùng đoạn MIDI, so sánh A/B và reset nhanh về mặc định.
- Tất cả nhãn, tooltip, trạng thái AR và accessibility dịch đủ Tiếng Việt/English; slider có số cụ thể, clamp, reset và keyboard navigation.
- UI không được hứa tính năng chưa có renderer; catalog/capability và UI phải lấy cùng metadata.

---

## 8. Lộ trình thực hiện — mỗi phase là một cổng chất lượng

Ước lượng dưới đây chỉ là **cỡ việc sơ bộ** cho một người đã quen repository; phải cập nhật sau Phase 0 và không tính thời gian thử webcam/GPU thật. Không chuyển phase khi gate chưa đạt.

| Phase | Scope | Cỡ việc | Deliverable chính | Gate để sang phase sau |
|---|---|---:|---|---|
| **0. Audit + baseline** | Đối chiếu toàn bộ 87 mục, settings, UI, GPU, WPF/software, preset, MIDI/live, recording, AR; capture ảnh/perf hiện tại. | S | Ma trận coverage và bug/gap list có bằng chứng. | Không còn mục “Available” chưa biết nó thực sự vẽ ở đâu; default/preset baseline được lưu; xác nhận control nào dead/duplicate. |
| **1. Art direction + UX spec** | Chốt style language, palette/material/motion, thứ tự render, wireframe Visual Effects Studio, 3 preview target. | S–M | Design bible ngắn + wireframe + deterministic sample scene. | Có tiêu chí nhận diện “đẹp/đọc được” và duyệt được ba look mẫu trước khi refactor renderer. |
| **2. Effect contract + compatibility** | Chuẩn hóa ID/phase/trigger/anchor/parameter/capability/cost/accessibility; thống nhất clock, seed và keyboard coordinates; version migration. | M | Metadata/effect schema dùng thật; mapping settings cũ → ID mới. | JSON/preset cũ round-trip; không đổi default; test contract và tọa độ; UI không đọc danh sách riêng rời rạc. |
| **3. Lát dọc tham chiếu (4 phase + AR Surface)** | Hoàn thiện một effect đại diện cho Falling, Impact, Hold, Release; cùng đường MIDI-file/live → preview → GPU → recording → synthetic AR. | M | Bộ mẫu Glow/Ribbon, Ripple/Burst, Hold Glow, Release Dissolve có đủ UI, preset, AR Surface. | Effect kích hoạt đúng event/velocity/pitch, không mất khi ghi, project đúng lên ảnh camera tổng hợp, có `--verify` + perf. Chỉ sau đây mới nhân rộng. |
| **4. Nâng chất lượng 40 note-phase FX** | Rà lần lượt 10 Falling (bao gồm renderer Color Gradient head-to-tail còn Planned), 16 Impact, 8 Hold, 6 Release; chuẩn hóa material/physics/envelope và alias. | M–L | Coverage matrix đã đóng cho toàn bộ lifecycle. | Mỗi effect có test trigger/decay/disabled/overlap; ảnh WARP/software hợp lý; slider tạo khác biệt nhìn thấy; không đổi note timing. |
| **5. Atmosphere + compositor** | 31 mục ambient, background motion, hit-line, keyboard lighting, bloom; tách event-triggered effect khỏi layer nền. | M | Layer stack có giới hạn, master intensity/quality và preset composition. | Ambient không che nốt; density/quality có tác dụng thật; load nặng vẫn qua benchmark; stage và recording cùng frame output. |
| **6. Music-reactive thật** | Nối velocity/pitch/hand/track/pedal/tempo; phân tích PCM envelope + low/mid/high FFT bands có smoothing. | M | Reactive mapping và UI có preview theo beat/âm thanh. | Audio callback không bị chặn; live latency không tăng; test tempo change/seek/pedal; trường hợp không có PCM có fallback ghi rõ. |
| **7. Visual Effects Studio + preset graph** | Triển khai browse/stack/inspector/A-B, thumbnail, recipe preset, save/import/export và migration. | M | UI mới và 7 theme làm từ graph, không code renderer riêng. | Không setting chết; preset cũ mở được; mã chia sẻ không mang calibration/camera; ảnh thumbnail deterministic; có test localization/accessibility. |
| **8. AR hoàn chỉnh** | Đưa các channel tương thích lên homography; sau đó hand mask, latency theo camera, camera phía người chơi/FOV/world-space; Learn/Perform; recording/OBS. | L | Ma trận AR capability thật + overlay surface rồi nâng cấp world-space. | Calibration ≤30 giây, sai số mục tiêu ≤1/4 bề rộng phím ở vùng giữa; 49/61/76/88; app latency hướng tới ≤1 frame ở 60 FPS; thử camera/đàn thật. |
| **9. Hardening + release** | Tối ưu GPU/overdraw, quality tiers, flash safety, regression image, docs và release notes song ngữ. | S–M | FX Studio ổn định, preset gallery và hồ sơ perf. | `--verify`, xUnit, ảnh golden, `--bench`, `--bench-ar`, ghi MP4 và hardware checklist đều đạt; cảnh nào chưa thử thật phải gắn nhãn chưa xác nhận. |

### Nhịp làm bên trong từng phase

```text
Chốt hành vi + anchor
  → vẽ preview/reference
  → làm đúng 1 lát code nhỏ
  → chạy unit/static/--verify
  → render test frame + nhìn ảnh
  → soi tham số và edge case
  → sửa đến khi gate xanh
  → mới chuyển effect/phase kế tiếp
```

**Không gộp nhiều effect mới rồi mới test cuối.** Với phase 4/5, mỗi effect hoặc một nhóm alias rất nhỏ được kiểm tra độc lập trước khi cập nhật matrix. Nếu ảnh, trigger, save/restore, AR mapping hoặc perf sai thì dừng ngay ở mục đó; không “để sửa sau”.

---

## 9. Kiểm chứng bắt buộc

### Với từng effect

- [ ] Có ID ổn định, trigger/anchor/lifecycle/parameter/capability ghi trong catalog.
- [ ] MIDI playback và live MIDI phản ứng đúng pitch, velocity, onset/off; note chồng cùng pitch không kết thúc nhầm nhau.
- [ ] Bật/tắt thật sự thay đổi hình; intensity 0 không spawn/không còn residual ngoài lifetime đã định.
- [ ] Size/speed/amount/color chỉnh được, có clamp, reset, JSON round-trip và preset.
- [ ] Seek, pause, stop, tempo change, song end và reset không để hạt/nốt ma.
- [ ] Màu/alpha/blend không làm mất thân nốt; flash tuân Reduced Flash.
- [ ] Có deterministic snapshot hoặc geometry assertion; random effect có seed trong test.
- [ ] Có capability rõ ràng cho GPU/fallback/recording/AR; fallback có thông báo hoặc style thay thế.
- [ ] Có cost cap: particles/instances/lifetime/overdraw; không tạo cấp phát vô hạn mỗi frame.

### Theo hệ thống

- **Unit:** `tests/PianoPath.Tests` cho geometry, envelope, mapping, migrations, timing và calibration thuần.
- **Windows integration:** `--verify` cho control wiring, GPU/WARP render, cửa sổ/recording và effect lifecycle.
- **Ảnh:** deterministic `--snapshot`/preset gallery; so sánh before/after, nhất là preset mặc định và chroma green.
- **Hiệu năng:** giữ cổng hiện hữu trong README: `--bench` default scene p95 < 8 ms, heavy scene p95 < 16 ms trên GPU thật; `--bench-ar` giữ mục tiêu p95 < 16 ms. WARP/CI dùng để xác minh đúng logic render, **không** dùng như bằng chứng đạt FPS phần cứng.
- **AR:** khung tổng hợp deterministic trước; sau đó webcam thật/49–88 phím, mirror, camera đổi, hand mask, timestamp, age/delay và video record.
- **Manual visual review:** tối thiểu một cảnh Classical/legato, một hợp âm dày, một bài electronic/velocity rộng, một góc camera overhead và một góc người chơi.

---

## 10. Phase 0 nên làm đầu tiên

Bắt đầu bằng **audit, chưa sửa renderer**. Tạo một bảng cho từng effect với các cột:

```text
ID | Trigger | Control/settings | UI | GPU stage | software/fallback | MIDI file
   | live MIDI | preset/save | recording | AR tier | test | cost/perf | known issue
```

Sau đó:

1. Chụp look mặc định và các preset đại diện trước khi đổi code.
2. Chạy `--verify`, xUnit và benchmark hiện tại; giữ log/ảnh làm mốc.
3. Đối chiếu `EffectCatalog` với `PianoVisualSettings`, `MainWindow.Settings`, `GpuStageSimulation*`, `PianoStage`, preset và `GpuArLayer`; sửa danh sách thành đúng thực tế thay vì đoán theo tên.
4. Tách mục **effect thật**, **modulator**, **theme/preset**, **alias** và **visual-system control**.
5. Ghi rõ điều nào stage đã chạy nhưng AR chưa có; ưu tiên kiểm chứng camera tổng hợp trước khi chạm webcam thật.
6. Chọn bốn effect của Phase 3 làm bộ thử nghiệm tham chiếu. Nếu không thể đi hết UI → GPU → recording → AR synthetic cho bốn effect nhỏ, chưa được mở rộng toàn bộ catalog.

**Kết luận:** bắt đầu bằng coverage matrix và design bible; không bật đồng loạt 87 hiệu ứng, không thay nguyên engine. Nền tảng cần đạt là một effect có cùng hành vi, đúng thời điểm và đúng vị trí trên stage, video và AR — sau đó mới mở rộng từng nhóm có kiểm soát.
