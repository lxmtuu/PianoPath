# PianoPath — Visual Effects Master Roadmap

> **Mục đích:** lộ trình tái thiết kế art direction, chức năng và đường render của toàn bộ hiệu ứng PianoPath; chuẩn bị để cùng một effect chạy trên sân khấu GPU, bản ghi và AR overlay đàn thật.<br>
> **Ngày lập:** 2026-10-06<br>
> **Trạng thái (2026-10-08):** Phase 0 đã đạt trên WARP/synthetic inputs; art direction Concert / Neon / Learning và 17 preset recipes đã được cập nhật, tương thích preset được giữ nguyên. **Phase 1 fixed-scene visual gate cho ba profile tham chiếu đã được người dùng duyệt** sau khi xem gallery 48 ảnh (run [37576179305](https://github.com/lxmtuu/PianoPath/actions/runs/37576179305)): ba profile × bốn checkpoint × WPF/GPU-WARP × 100%/50%; ảnh 50% là trung bình 2×2 từ capture 100%. Các checkpoint đang chơi/chord/pedal được chấp nhận; cả ba rest đều có WPF note Δ 0 px ở 100% và 50%. Bản tinh chỉnh beam cuối (`ba154e3`) sau đó cũng pass build, `--verify`, tests và tạo gallery mới trong run [37580688870](https://github.com/lxmtuu/PianoPath/actions/runs/37580688870). **Sign-off này là chấp thuận thị giác của người dùng, không tuyên bố pixel-perfect WPF/GPU parity hay chứng nhận GPU phần cứng/FPS/AR.** Mở rộng review toàn bộ 17 preset, chroma/accessibility và hardware gates vẫn là follow-up. Recording family-capture slice đã PASS trong Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649), commit `ffcc340`: 24/24 family frames qua GPU `GpuRecordingTap`, 22/22 WPF-native qua PNG write/read; hai family WPF-only vẫn `Unsupported`. Đây chỉ là family-presence; Recording bindings giữ `Unverified`, cost giữ `Unmeasured`.
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
| AR / Recording | Camera texture, ring/timestamp, calibration/homography, key patches, note quads, Learn/Perform, delay và GPU recording đã có verify. Typed AR pilot hiện ghi 6 ambient families `Native` **chỉ cho screen-space camera composite**, 14 S5 calibrated-3D projection families `Unverified` pending Windows/WARP checks, và 4 unprojected families `Unsupported`; note materials giữ `ar.note.flat-quad` fallback. Recording family-capture slice PASS Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649), commit `ffcc340`: 24/24 family frames đọc từ GPU `GpuRecordingTap`, 22/22 WPF-native family ghi/đọc PNG và pass pixel-diff; `falling.shimmer` và `atmosphere.shooting-stars` tiếp tục WPF `Unsupported`. | S5 projection code/assertions are present but unbuilt/unrun; do not describe the 14 as verified. Ambient petals/meteors remain flat screen-space. Typed pilot chưa phủ trọn 87 ID descriptive inventory. Recording `Native` chỉ là family-frame presence, không phủ từng style/parameter; parameter-level Recording bindings còn `Unverified`, recording cost còn `Unmeasured`. Xem [`EFFECT-COVERAGE-AUDIT.md`](EFFECT-COVERAGE-AUDIT.md) và `AR-OVERLAY-ROADMAP.md` §12. |
| Audio-reactive | Đã có khối FFT/audio trong repository, nhưng đường “Audio Reactive” của visualizer chưa nên được gọi là spectrum/FFT thật cho đến khi xác nhận lấy PCM, smoothing và latency. | Tách phân tích âm thanh khỏi audio callback/UI; triển khai band/envelope thật ở phase riêng và test không làm nghẽn playback. |
| Preset & chia sẻ | Có 14 built-in, 3 community, JSON, thumbnail và mã chia sẻ; recipes Concert/Neon/Learning đã được làm lại, `Concert Gold` là default cho cài đặt mới/reset. | CI `37480841688` xác nhận settings compatibility, build/verify và render đủ 17 preset trên WPF/software + GPU/WARP; gallery đã được tạo và xem tổng quan. Giữ nguyên tên/schema và preset người dùng. Review chi tiết theo fixed scene, 100%/50%, accessibility và hardware vẫn mở; taxonomy version/capability riêng trước khi mở schema. |

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

Mỗi preset phải có **ảnh preview deterministic** trên cùng một đoạn MIDI/chord. Đã chọn Concert Gold làm default, Neon Violet giữ vai trò creator/neon và Two Hands làm learning; ma trận recipe/compatibility chi tiết ở [`VISUAL-EFFECTS-PRESET-DESIGN.md`](VISUAL-EFFECTS-PRESET-DESIGN.md). Recipe đã được cấu hình nhưng chỉ được xem là đạt sau khi xem ảnh từ WPF/GPU thật và kiểm tra trên scene cố định.

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

- **Note material:** Solid / Neon / Glass / Fire / Gem / Crystal Outline; roundness, edge, tint, head glow and shimmer. `Notes3D` remains an independent depth axis; Streak stays in a later trail phase. Gem/Crystal Outline render on WPF + D3D11/WARP, not the current flat-quad AR overlay.
- **Màu nốt:** Gradient, PerHand, PerTrack, RainbowPitch, RainbowTime; track/hand colors.
- **Background motion:** Aurora, Nebula, Prism, Ember Haze, Ocean Flow, Retro Grid; có None và amount/speed/color.
- **Hit-line motion:** Pulse, Sweep, Twin Comets, Spectrum, Electric Arc, Ripple.
- **Bàn phím:** Classic/Studio/Glass, nhãn phím, key light, shadow/contact occlusion, vật liệu và nốt phím đang sáng.
- **Stage layout:** guide lanes, sheet layer, hit line, background image/chroma, vignette, watermark và recording output.

Các mục này cũng phải đi qua cùng rule: preset preview, visibility, tooltip/localization, storage/migration, capability và test.

---


### 5.12 Khoảng trống chức năng nâng cao ngoài inventory 87 mục

Bảng dưới đây là **gap review**, không phải lời khẳng định các tính năng đã có. “Có một phần” chỉ ra bằng chứng hiện tại; mỗi hàng vẫn cần gate riêng trước khi gọi là hoàn tất.

| Năng lực | Hiện trạng trong repo | Phần còn thiếu / hướng thiết kế | Gate cụ thể |
|---|---|---|---|
| Gradient dọc thân nốt | `ColorMode="Gradient"` đã dùng màu theo pitch; catalog ghi `falling.color-gradient` là Planned. | Thêm axis/ID rõ ràng cho head-to-tail gradient, giữ nguyên màu hand/track/pitch; không dùng chung một tên cho hai loại gradient. | Fixture có note dài/ngắn và đảo hướng; assert màu đầu/cuối, GPU/WPF parity, không đổi màu khi resize/seek. |
| MIDI expression nâng cao | Có note-on/off, velocity, track, pitch, sustain pedal và tempo. | Audit CC66 sostenuto, CC67 soft pedal, half-pedal/continuous CC64; pitch bend, channel/poly pressure và MPE chỉ bật nếu input thật cung cấp. Map thành biến thiên cục bộ, opt-in; không giả lập expression thiếu dữ liệu. | File MIDI + thiết bị thật, nhiều channel/voice, note lặp cùng pitch khi pedal giữ; không nhầm note-off, không tăng audio callback time. |
| Ngữ nghĩa nhạc | Có beat/tempo, sheet/track và practice feedback ở các bề mặt hiện tại. | Downbeat/bar accents có thể bật riêng; chord/interval/voice-leading ribbon hoặc nhãn hợp âm là lớp học tùy chọn, không phải ambient mặc định. Không gọi đây là transcription/audio-to-MIDI. | Tempo map đổi giữa bài, hợp âm dày, seek/loop; output tái lập từ MIDI và không che note body/score. |
| Audio-reactive thật | Có setting/envelope và code FFT/audio trong repo; chưa chứng minh mọi đường lấy PCM có latency/smoothing đúng. | Chốt nguồn: synth nội bộ hoặc input thiết bị được người dùng chọn; low/mid/high bands, RMS/envelope, attack/release smoothing, latency và privacy. Chạy analyzer ngoài audio callback. | PCM fixture có tần số đã biết; loopback/input thiết bị; latency p95 đo được, audio callback không block/allocate theo frame. |
| Accessibility và flash safety | Có thể tắt một số effect riêng; chưa có global Reduced Motion/Reduce Flashes/color-vision contract hoàn chỉnh. | Một toggle toàn cục lưu được; giới hạn flash theo diện tích/cường độ/tần suất/thời lượng; high-contrast/shape/label option và palette không dựa riêng vào màu. | Test mọi preset + override global; no full-frame strobe ở default; label/focus/keyboard navigation; kiểm tra nhanh ở 50% preview và kiểm thử accessibility thủ công. |
| Adaptive quality và capability | Có `ShadingQuality`, clamp, D3D11 và WPF fallback; chưa có capability registry đủ để dự đoán từng effect/backend. | Bảng support GPU/WPF/recording/AR, shader/feature-level và resource budget; Auto/Performance/Quality tier. Khi chậm, giảm theo thứ tự ambient → particles → bloom/resolution, **không** đổi pitch, timing, note width hoặc hit geometry. Hysteresis chống nhảy quality liên tục. | Giả lập thiếu shader/device/memory; tier chuyển sau ngưỡng có hysteresis; hiển thị lý do hạ cấp; scene note giữ pixel/geometry invariant. |
| Màu và compositor | GPU stage có HDR/bloom/ACES; WPF/camera/recording là nhiều đường ghép. | Chốt linear/premultiplied alpha, SDR/HDR output, transfer function, exposure, UI/camera color; tránh gamma double-apply, halo viền đen và chroma spill. | Color bars/alpha checker qua từng backend; pixel diff có tolerance ghi rõ; recording và snapshot không đổi gamma so với viewport. |
| Preset taxonomy/compatibility | Có tên built-in/community/user, settings JSON, migration và share code. | Tách ID ổn định khỏi tên dịch; family/tag, min capability, tier/cost, AR class, content vs machine-local; preset diff/preview và migration alias. Không nâng version schema chỉ để đổi recipe. | Tải fixture preset cũ qua phiên bản mới, round-trip không mất field; machine-local camera/device paths không đi theo share; default không override file đã lưu. |
| AR calibration/projection | Có timestamped camera ring, homography và perspective-correct key/note quads. | Thêm lens-distortion/rolling-shutter consideration, crop/letterbox/mirror/exposure, vùng mép, độ tin cậy calibration và recalibration khi camera dịch. | Checkerboard/keyboard target 49/61/76/88 phím; báo reprojection error center/edge; calibration invalid thì overlay dừng/hiển thị lý do thay vì trôi. |
| AR occlusion/depth | Có hand marker/basic hand tracking; `GpuArLayer` chủ yếu chiếu key/note geometry, một số FX vẫn screen-space. | Tách occlusion mask theo người/bàn tay với depth/segmentation fallback; chọn effect nào thực sự cần world-space. Xử lý riêng ambient, tránh vẽ glow xuyên qua bàn tay nếu occlusion bật. Giữ inference on-device/opt-in. | Synthetic occluder trước, sau đó camera thật trong nhiều ánh sáng/tông da; mask latency và edge artifacts đo riêng; tắt module không làm mất note projection. |
| Recording/export creator | Có GPU off-screen take, WPF overlay và audio mux ở một số đường. | Một compositor/timeline deterministic; sync audio-video, drop/duplicate frames report, premultiplied alpha/chroma, render-at-size. Thêm framing 16:9/9:16, safe-area, crop và OBS transparent layer như profile xuất, không hardcode vào preset look. | Clip 10 phút với audio click/flash sync, drift/drop report; 1080p/portrait/transparent; A/V lệch mục tiêu <50 ms và profile scale không đổi timing/note geometry. |
| Transition, preview và curation | Apply preset hiện thay các giá trị tức thì; đã có thumbnail và preset gallery cho look. | A/B và transition crossfade có giới hạn, preview live trên cùng MIDI; preset diff/undo. Giữ màu note/mốc thời gian ổn định khi chuyển. | A/B cùng seed; không rơi settings machine-local; no event loss/crossfade flash; test undo/redo và lưu sau chỉnh sửa. |

Không gom các gap này thành một “advanced FX” phase mơ hồ: capability/accessibility contract đi trước; mỗi renderer/feature có owner, output, cost, fallback và test cụ thể. Dự kiến ghép chúng vào Phase 2–10 trong bảng tại §8.

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
| ✅ **0. Audit + baseline** | Đối chiếu 87 mục với settings, UI, GPU, WPF/software, preset, MIDI/live, recording và AR; chốt ảnh/perf baseline WARP. | S | Ma trận [`EFFECT-COVERAGE-AUDIT.md`](EFFECT-COVERAGE-AUDIT.md), gap list, README previews/preset gallery và bench artifact CI. | **Đạt 2026-10-06:** Windows CI [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020) PASS trên `68bcc466`; baseline là WARP/synthetic, chưa xác nhận phần cứng. |
| ✅ **1. Concert / Neon / Learning + preset refresh** | Hướng đã được người dùng chọn. Tái thiết kế 14 built-in + 3 community; Concert Gold thành fresh-install/reset default; giữ tên/schema/user files. | S–M | [`VISUAL-EFFECTS-DESIGN-BIBLE.md`](VISUAL-EFFECTS-DESIGN-BIBLE.md), [`VISUAL-EFFECTS-PRESET-DESIGN.md`](VISUAL-EFFECTS-PRESET-DESIGN.md), recipe generator, localization, gallery 17 preset và fixed-scene artifact 48 ảnh. | **Đã ký duyệt 2026-10-07:** Windows build + xUnit/`--verify` pass; artifact đủ 48 ảnh WPF/GPU-WARP ở 100%/50%; người dùng duyệt ba profile tham chiếu và các checkpoint readability/chord/pedal/rest. Review mở rộng đủ 17 preset, chroma/accessibility và hardware vẫn là follow-up; sign-off không tuyên bố pixel-perfect parity hay hardware pass. |
| **2. Effect contract + capability + accessibility** | Chuẩn hóa ID/phase/trigger/anchor/clock/seed/parameter/cost/capability/quality fallback; hợp đồng Reduced Motion, Reduce Flashes, contrast; giữ alias/schema cũ. | M | Typed metadata dùng chung cho runtime/UI/tests; ma trận GPU/WPF/recording/AR; preset migration map. | JSON/share cũ round-trip; setting “unsupported” có fallback/message; global accessibility áp mọi preset; không còn control chết. |
| **3. Lát dọc tham chiếu (4 phase + AR Surface)** | Hoàn thiện 1 effect đại diện Falling, Impact, Hold, Release trên MIDI file/live → GPU → WPF/fallback → recording → synthetic AR. | M | Glow/Ribbon, Ripple/Burst, Hold Glow, Release Dissolve cùng trigger/anchor/decay và capability. | Event/pitch/velocity đúng; seek/pause/pedal/overlap sạch; clip không mất frame; AR projection đúng; test + ảnh + cost gate xanh. Chỉ sau đó mới nhân rộng. |
| **4. Note lifecycle + expression** | 40 note-phase FX; gradient head-to-tail; note overlap/voice identity; MIDI pedal/expression thật (CC66/67, half-pedal, pitch/pressure/MPE nếu thiết bị hỗ trợ). | M–L | Coverage matrix + typed mapping; dữ liệu thiếu expression không được giả lập. | Mỗi effect có trigger/decay/disable/overlap test; gradient có geometry assertion; multi-channel MIDI playback/live; note timing không đổi. |
| **5. Atmosphere, material và compositor** | 31 ambient, background motion, hit-line, keyboard light, bloom/color management; cap overdraw/particle; define alpha/HDR/SDR chain. | M | Layer graph thống nhất, master intensity, color contract và Adaptive Quality tiers. | Ambient không che nốt; alpha/color bars qua GPU/WPF/recording; quality hạ theo thứ tự, hysteresis ổn định và giữ note geometry. |
| **6. Musical + audio reactivity** | Xác nhận PCM source; envelope + low/mid/high FFT; beat/bar accents, chord/interval overlay tùy chọn; latency/availability state. | M | Analyzer tách khỏi audio callback, smoothing/latency setting và live preview có nguồn rõ ràng. | Tone fixture bands đúng; hardware loopback test; callback không block; seek/tempo map/pedal không stale; không gọi feature chưa có PCM là FFT reactive. |
| **7. Visual Effects Studio + preset taxonomy** | Browse/stack/inspector/A-B, preview cùng fixture, recipe graph 7 theme, stable IDs/tags/capability, diff/undo/import/export. | M | Studio + compatibility/migration docs; user preset chỉ look, không camera/calibration/device. | Preset cũ mở lại đúng; localization/keyboard/accessibility; thumbnails deterministic; mọi control có consumer/capability; không overwrite user settings. |
| **8. AR nâng cao** | Hoàn thiện homography/crop/mirror/lens/rolling shutter; occlusion mask/depth; world/surface anchor; camera age/latency và Learn/Perform. | L | Per-effect AR support/fallback, calibration confidence, hand occlusion và AR capture. | Calibration ≤30 s; sai số thiết kế ≤1/4 bề rộng phím ở giữa và ≤1/2 ở rìa; latency p95 ≤1 frame ở 60 FPS mục tiêu; thiết bị thật mới được tuyên bố đạt. |
| **9. Recording/export creator** | Fixed-step/deterministic compositor; A/V sync/drop reporting; HDR/SDR/alpha/chroma; 16:9, 9:16 safe frame, crop/OBS profile. | M–L | Một output contract cho viewport, snapshot, video và transparent/chroma export. | Render 10 phút drift mục tiêu <50 ms; frame/drop report; 1080p/portrait/alpha; không đổi timing, colorspace chính xác, audio nghe lại đúng. |
| **10. Hardening + hardware acceptance** | Multi-GPU, iGPU, webcam, driver, permissions, thermal/session soak, accessibility, install/release. | M | Hardware matrix, release report, preset gallery/build artifacts và support/fallback guide. | `--verify`, xUnit, static, golden gallery, `--bench`, `--bench-ar`, MP4, 30-min soak và manual acceptance trên danh sách thiết bị §11; chưa thử thật phải gắn nhãn. |
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
- **Manual visual review:** Phase 1 fixed-scene gate cho Concert Gold / Neon Violet / Two Hands **đã được người dùng ký duyệt 2026-10-07** sau khi xem artifact 48 ảnh [run 37576179305](https://github.com/lxmtuu/PianoPath/actions/runs/37576179305), gồm bốn mốc ở WPF/GPU-WARP, 100%/50%. Readability, held chord, sustain pedal và release/rest được chấp thuận; WPF note Δ ở rest là 0 px ở cả hai kích thước cho cả ba profile. Bước mở rộng tiếp theo là duyệt đủ 17 preset ở 100%/50% và review chroma Green Screen riêng; Reduced Motion/Reduce Flashes, high-contrast và color-vision palettes vẫn cần coverage riêng. WARP không được dùng làm bằng chứng hardware.

---

## 10. Phase 0 baseline — hoàn tất; không chạy lại audit từ đầu

Phase 0 đã hoàn thành trên `68bcc466`; các bước dưới đây là kết quả cần giữ làm nền và chỉ cập nhật khi có bằng chứng mới. Không xem nó là lý do để bỏ qua kiểm tra preset/render mới:

```text
ID | Trigger | Control/settings | UI | GPU stage | software/fallback | MIDI file
   | live MIDI | preset/save | recording | AR tier | test | cost/perf | known issue
```

Các kết quả/artefact cần giữ làm nguồn sự thật:

1. Coverage matrix và alias/gap list nằm trong [`EFFECT-COVERAGE-AUDIT.md`](EFFECT-COVERAGE-AUDIT.md); `EffectCatalog` vẫn là inventory, không phải runtime registry.
2. Windows CI [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020) xác nhận code-path/verify trên WARP và synthetic inputs tại `68bcc466`; không đại diện GPU, webcam hay đàn thật.
3. Catalog đã phân biệt lifecycle FX, ambient, modulator, combo theme và visual-system control; 87 mục không phải 87 renderer độc lập.
4. `GpuArLayer` hiện chủ yếu project note/key geometry; ambient screen-space và stage lifecycle chưa có capability AR đầy đủ. Gap chi tiết ở `AR-OVERLAY-ROADMAP.md` và bảng §5.12.
5. Phase 3 vẫn phải chọn bốn effect đại diện; chưa mở rộng catalog cho tới khi một lát đi hết UI → GPU → recording → AR synthetic với test/ảnh/perf.

### Phase 1 fixed-scene visual gate — signed off (2026-10-07)

Người dùng đã duyệt bộ ảnh Phase 1 trong hội thoại. Artifact gốc [run 37576179305](https://github.com/lxmtuu/PianoPath/actions/runs/37576179305) có đúng 48 capture: Concert Gold / Neon Violet / Two Hands, bốn checkpoint, WPF + GPU-WARP, 1920×1080 và 960×540; ảnh 50% là average 2×2 từ capture 100%. Bảng dưới ghi **WPF note-layer Δ pixels, 100% / 50%**; số này xác nhận lớp nốt xuất hiện/biến mất đúng checkpoint, không phải chỉ số parity giữa renderer:

| Profile | 0.8 s · readability | 4.1 s · held chord | 6.1 s · sustain pedal | 11.25 s · rest |
|---|---:|---:|---:|---:|
| Concert Gold | 76,932 / 23,668 | 76,132 / 23,092 | 79,018 / 23,515 | **0 / 0** |
| Neon Violet | 100,645 / 31,687 | 95,761 / 30,330 | 99,736 / 31,550 | **0 / 0** |
| Two Hands (Learning) | 43,910 / 11,673 | 42,739 / 11,246 | 43,600 / 11,522 | **0 / 0** |

**Tiêu chí được người dùng chấp thuận:** nốt đang chơi còn đọc được sau downsample; held chord/tách tay và sustain pedal rõ; release/rest không có note residue, WPF note Δ bằng 0 ở cả hai kích thước và không thấy flash toàn khung. Crystal Outline giữ material phẳng/lõi trong suốt; verification kiểm tra viền sáng và giới hạn alpha lõi ở frame gốc lẫn bản 2×2. WPF/GPU vẫn có khác biệt về độ sáng/độ mềm của glow; sign-off ghi nhận mức chất lượng chấp nhận được, **không** khẳng định pixel-perfect parity.

Sau bộ ảnh được duyệt, chỉnh sửa beam-core cuối ở commit `ba154e3` đã pass Windows build, xUnit, `--verify` và tạo gallery mới trong [run 37580688870](https://github.com/lxmtuu/PianoPath/actions/runs/37580688870). Kết quả CI/WARP xác nhận code path, không xác nhận GPU phần cứng/FPS, camera hay AR. Mở rộng visual review đủ 17 preset, chroma Green Screen và accessibility là follow-up riêng; Phase 1 reference gate đã đóng.

## 11. Cổng chấp nhận phần cứng và đường fallback (chưa đạt)

Đây là **ma trận cần chạy**, chưa phải báo cáo pass. CI trên WARP chỉ chứng minh có thể dựng/đi qua code path; không chứng minh FPS, nhiệt độ, camera latency, màu màn hình hay AR trên thiết bị người dùng.

### 11.1 Nhóm thiết bị tối thiểu

| Nhóm | Mẫu cần đại diện | Cách đánh giá |
|---|---|---|
| iGPU phổ biến | Intel UHD/Iris Xe hoặc AMD Radeon iGPU có D3D11; driver OEM và driver Windows Update nếu có khác biệt. | GPU stage 1080p, default/heavy presets, shader compile, ảnh và p95; kiểm tra tiết kiệm pin/thermal. |
| dGPU tầm trung | NVIDIA GeForce hoặc AMD Radeon rời, laptop và desktop khi khả thi. | 1080p/1440p, 60/120/144 FPS; bloom/particle load, recording off-screen và chuyển monitor. |
| Fallback/software | WARP/Basic Render; máy không khởi tạo D3D11 hoặc shader capability bị thiếu. | Chỉ kiểm tra correctness/ổn định, không dùng để kết luận FPS. Xác nhận note/keyboard vẫn render và effect giảm cấp có giải thích. |
| Camera | Built-in webcam 720p, USB overhead 1080p 30/60 FPS; camera đổi aspect ratio/mirror/crop và tình huống ánh sáng yếu. | Permission/reconnect, frame age/drop, calibration center/edge, exposure/white-balance drift và video capture. |
| MIDI + audio | Bàn phím MIDI thật hỗ trợ CC64; nếu có, thêm CC66/67, pitch bend/pressure; WASAPI và endpoint audio thật. | MIDI playback/live parity, event order, pedal, latency, audio callback load và AV sync. |

Ghi đủ model GPU/CPU/camera/MIDI, driver/Windows build, độ phân giải màn hình, power mode, renderer thực tế, preset, camera FPS và adapter reported. Không gộp nhiều adapter/driver vào một dòng “đã thử”.

### 11.2 Số đo và hành vi bắt buộc

- **GPU frame budget:** giữ gate hiện có ở 1920×1080 — default p95 `< 8 ms`, heavy p95 `< 16 ms`; AR p95 `< 16 ms`. Ghi p50/p95/p99/max, dropped frame, warm-up/compile riêng. Những con số này là mục tiêu trên GPU thật phù hợp, không áp vào WARP.
- **Fallback:** nếu D3D11/shader không dùng được, app phải khởi động ổn định, nốt/phím/mốc thời gian giữ đúng; effect không hỗ trợ phải được giảm cấp/ẩn có nêu lý do, không được làm hỏng look mà không báo.
- **AR timing/fit:** giữ timestamp và frame age; đo p95 theo camera thật. Mục tiêu overlay latency ≤ một frame ở 60 FPS; reprojection ≤ 1/4 phím ở vùng giữa và ≤ 1/2 phím vùng rìa. Nếu không đạt, cảnh báo calibration/latency và có cách tắt overlay an toàn.
- **Recording:** clip 10 phút với click track/flash marker, log PTS/drop/duplicate; mục tiêu audio-video drift `< 50 ms`, không thay timing nốt hoặc màu do viewport scale. Kiểm tra MP4, snapshot, alpha/chroma và 16:9 + 9:16 output.
- **Ổn định:** 30 phút playback + camera + render; không tăng vô hạn particles/memory, không mất MIDI, không crash khi rút/cắm lại camera, đổi device, seek hoặc đổi quality tier.
- **Accessibility:** duyệt đủ preset với Reduce Motion/Reduce Flashes; không có full-frame flash ở default; text/label, focus, contrast và pattern dự phòng được kiểm tra bằng bàn phím và ở preview 50%.

### 11.3 Cách ra quyết định

1. Windows CI + WARP: **code-path pass**, không phải hardware pass.
2. Mỗi nhóm phần cứng: lưu artifact/metadata và kết quả; đo lại sau thay đổi renderer/compositor/camera.
3. Nếu chỉ một nhóm fail, điều chỉnh capability/quality fallback của nhóm đó trước; không hạ chất lượng mọi GPU để né lỗi.
4. Không đánh dấu Phase 8/9/10 xong khi camera thật, record dài hoặc GPU thật chưa chạy. Ghi “chưa xác nhận” cạnh đúng mục còn thiếu.

### 11.4 Kết quả đo đầu tiên — iGPU AMD (2026-10-07)

Máy người dùng, `--bench` / `--bench --bench-ar` trên bản Release của `bad6dde` (code bench không đổi
tới `30b1296`), adapter reported `AMD Radeon(TM) Graphics`, `warp: false`, Direct3D 11, 1920×1080,
app 1.0.0, shader compile 551–658 ms.

**Metadata máy đo (người dùng cung cấp 2026-10-08):**

| Mục | Giá trị |
|---|---|
| CPU | AMD Ryzen 5 7535HS with Radeon Graphics |
| GPU trong máy | **Hai adapter:** iGPU `AMD Radeon(TM) Graphics` (driver 32.0.21045.5002) — adapter mà ba lượt bench đã chạy; và dGPU `NVIDIA GeForce RTX 4050 Laptop GPU` (driver 32.0.16.1714) — **chưa đo** |
| OS | Microsoft Windows 11 Home Single Language, build 26200 |
| Power scheme | Balanced (`381b4222-…`, `powercfg /getactivescheme`) |

Hai hệ quả phải giữ kèm kết quả bên dưới:

1. **Ba lượt fail của default là kết quả của riêng iGPU**, không phải của cả máy: renderer tạo device
   bằng `D3D11CreateDevice(null, Hardware, …)` (`GpuStageRenderer`) nên bench chạy trên adapter mặc
   định mà Windows gán cho `PianoPath.exe` — ở đây là iGPU. RTX 4050 cùng máy thuộc nhóm "dGPU tầm
   trung" của §11.1 và cần một lượt đo riêng trước khi kết luận gì về nó; lượt đo đó ghim thẳng
   adapter bằng `--bench --bench-adapter="RTX 4050"` (khớp đúng một adapter theo tên, không cần đổi
   thiết lập đồ hoạ của Windows).
2. **Power scheme Balanced trên laptop iGPU** nhất quán với phương sai lớn của heavy giữa các lượt
   (13.13 → 9.01 → 8.26 ms): xung iGPU bị điều phối theo pin/nhiệt. Lượt đo lại nên ghi kèm power
   scheme và, nếu so sánh nghiêm túc, đo ở cùng một scheme.

| Scene | Budget p95 | ba lượt đo (ms) | Kết luận |
|---|---:|---|---|
| default (Concert Gold) | 8 | 9.59 / 11.20 / 11.43 | **Chưa đạt** — fail 3/3 lượt, vượt 20–43% |
| heavy (Galaxy Voyage) | 16 | 13.13 / 9.01 / 8.26 | Đạt — nhưng phương sai lớn (nhiệt/power) |
| ar (camera tổng hợp 720p60) | 16 | 8.84 | Đạt — camera age mean 1.77 ms, worst 15.74 ms (dưới 1 khung 60 fps ≈ 16.7 ms nhưng sát biên) |

Đọc phân phối: mọi scene có chu kỳ 3 khung (2 khung GPU-bound + 1 khung ~1.5 ms do readback-ring
trùng gối trong loop bench, xem `GpuRenderLoop`), nên p95 luôn thuộc cụm khung work; p95 của cụm
work ở default ≈ 11.6 ms. Giả thuyết chi phí có bằng chứng: Concert Gold mặc định
`BloomIntensity 80 / BloomSize 70` đẩy chuỗi bloom (6 cấp FP16, cấp 0 full-res) mạnh hơn Galaxy
Voyage (44/52) — default ít hạt hơn nhưng đuôi p95 cao hơn. Lát tối ưu thuộc Phase 5
(compositor/Adaptive Quality) vì đụng bloom là đụng pixel và kéo theo render lại gallery
fixed-scene để người dùng duyệt lại; không xen ngang các phase hiện tại.

### 11.5 Kết quả dGPU đầu tiên — NVIDIA GeForce RTX 4050 Laptop (2026-10-08)

Cùng máy với §11.4 (Ryzen 5 7535HS, Windows 11 build 26200, Balanced), đo bằng `--bench-ar`.
JSON báo `adapter: NVIDIA GeForce RTX 4050 Laptop GPU`, `warp: false`,
`takenUtc 2026-10-07T18:59:27Z`, shader compile 646 ms. **Lưu ý cơ chế chọn adapter:** lượt đo này
diễn ra khi đường code `--bench-adapter` còn lỗi `E_INVALIDARG` (truyền `DriverType.Hardware` cùng
adapter tường minh — đã sửa sau đó), tức lệnh chạy với cờ không thể tạo device; adapter NVIDIA trong
JSON là do bộ chọn adapter mặc định của OS cho exe, không phải do cờ ghim. Con số vẫn là đo thật
trên RTX 4050 (`adapter` + `warp:false` trong chính báo cáo). Các thống kê dưới đây đã được tính lại
độc lập từ mảng `frameMs` trong JSON và khớp đúng báo cáo của app.

| Scene | Budget p95 | Kết quả |
|---|---:|---|
| ar (camera tổng hợp 720p60) | 16 | **Đạt** — p95 3.25 ms; mean 2.71; median 2.63; max 4.13 ms; camera age mean 1.67 / worst 12.10 ms |

So với iGPU cùng máy (§11.4): ar p95 8.84 → 3.25 ms; camera age worst 15.74 → 12.10 ms. Phân phối
khung đều (2.28–4.13 ms), không còn cụm chu kỳ 3 khung lấn át như trên iGPU — chi phí readback-ring
không còn đáng kể cạnh thời gian khung của dGPU.

**Bổ sung sau bản sửa `eb7b00f`: default và heavy đã đo trên dGPU.** Ba lượt
`--bench --bench-adapter="RTX 4050"`, mã thoát 0 cả ba, `takenUtc` mới từng lượt
(2026-10-07T20:35:05Z / 20:37:12Z / 20:38:49Z) — bằng chứng đầu tiên đường ghim adapter hoạt động
trên phần cứng thật, đúng adapter được yêu cầu.

| Scene | Budget p95 | lượt 1 | lượt 2 | lượt 3 | Kết luận |
|---|---:|---|---|---|---|
| default (Concert Gold) | 8 | 3.32 | 3.29 | 3.39 | **Đạt 3/3** — dư ~2.4× ngân sách |
| heavy (Galaxy Voyage) | 16 | 2.97 | 3.09 | 3.17 | Đạt 3/3 — cực ổn định |

Đối chiếu iGPU §11.4 cùng máy: default 9.59 / 11.20 / 11.43 (fail 3/3, vượt 20–43%);
heavy 13.13 / 9.01 / 8.26 (đạt nhưng phương sai lớn). Trên dGPU cả hai scene nằm gọn dưới 3.4 ms.

Kết luận rút ra:

1. **Default fail là vấn đề của riêng nhóm iGPU, không phải của renderer.** Cùng một scene Concert
   Gold với bloom 80/70 (giả thuyết chi phí của §11.4) chỉ tốn ~3.3 ms p95 trên RTX 4050. Câu hỏi
   Phase 5 do đó thu hẹp: tối ưu bloom default chỉ đáng làm cho iGPU; hướng đúng tinh thần §11.3 là
   chỉnh capability/quality fallback của nhóm iGPU (Adaptive Quality hạ cấp bloom khi vượt ngân sách)
   thay vì hạ chất lượng cho mọi GPU.
2. **Phương sai heavy của iGPU (13.13 → 8.26) nhất quán với giả thuyết pin/nhiệt**: cùng scene trên
   dGPU chạy 2.97–3.17 ms, gần như phẳng; Balanced power scheme chỉ bóp xung iGPU.
3. Cổng phần cứng giờ có đủ dữ liệu cho hai nhóm §11.1 (iGPU phổ biến, dGPU tầm trung) với cả ba
   scene; WARP/fallback đã do CI cover về correctness; camera/AR có số trên cả hai adapter. Còn thiếu:
   nhóm MIDI/audio thật và soak dài trên phần cứng.

### 11.6 Adaptive Quality — implementation slice (2026-10-08)

Đã thêm bộ điều khiển runtime trên các adapter D3D11 phần cứng; đây **không phải** thay default hay hạ chất lượng toàn cục. Telemetry là D3D11 timestamp/disjoint query không chặn (`DONOTFLUSH`) bao quanh các draw scene + bloom + composite của một output chính; `Present`/VSync, readback và sleep nằm ngoài phép đo. Với swap-chain VSync, ngân sách theo mức chậm hơn giữa frame-rate cấu hình và refresh của monitor; WARP/software không bật controller.

- Cửa sổ không chồng lấn 60 mẫu hợp lệ; nếu p95 vượt 90% ngân sách trong 3 cửa sổ liên tiếp, giảm một tier. Thứ tự: bloom 6→4→2 cấp, sau đó ambient density 100→60→30%. Phục hồi một tier khi p95 dưới 75% trong 8 cửa sổ liên tiếp. Khoảng trễ/cooldown được kiểm thử thuần.
- Chỉ bloom pyramid và instance của ambience nền bị thay đổi. Guide lanes và note trails được giữ; particle bursts, note/key geometry, pitch/timing và keyboard geometry không đổi. Tier chỉ áp cho stage đang hiển thị; GPU recording luôn render Full và khung chỉ phục vụ recording không được lấy mẫu thích ứng. Settings/preset/JSON không bị sửa; trạng thái hiển thị tên tier hiện hành.
- `--bench`, note-material probe và deterministic/WARP screenshots luôn chạy Full quality để baseline/golden không tự đổi theo tải. Unit tests kiểm tra budget theo VSync, thứ tự tier, outlier, hysteresis, reset và phép compact ambient giữ nguyên note-trail tail.

**Chưa có kết quả hardware sau thay đổi.** `--bench` hiện vẫn đo baseline Full quality; Windows CI/WARP chỉ có thể xác nhận build/correctness, không chứng minh iGPU nhanh hơn hoặc tier nào đạt 8 ms. Kết quả vật lý của Adaptive Quality vẫn **Unmeasured**; cần đo lại iGPU với tier/runtime state và xem lại fixed-scene gallery trước khi kết luận hình ảnh/performance.
