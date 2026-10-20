# Particle motion review — hiện trạng, số đo và hướng nâng cấp

> **Ngày:** 2026-10-19 · **Nhánh:** `arena/efa69378-pianopath` · **Loại:** đánh giá thiết kế + prototype; **W0 đã vào mã ngày 2026-10-20** (xem §4.3), phần còn lại chưa động vào
> **Phạm vi:** chuyển động của hạt trên sân khấu GPU (`Gpu/GpuStageSimulation*.cs`), đường WPF (`Stage/PianoStage.cs`)
> và bản sao dùng cho AR (`Gpu/GpuArLayer.Effects.cs`). Không đánh giá lại shader vật liệu, bloom hay preset.
> **Kèm theo:** [Particle Motion Lab](prototypes/particle-motion-lab/index.html) — bản mô phỏng lại công thức
> trong repo, chạy bằng trình duyệt, để nhìn A/B trước khi động vào mã.
> **Xem thêm:** [PARTICLE-MOTION-SEEMUSIC-REFERENCE.md](PARTICLE-MOTION-SEEMUSIC-REFERENCE.md) — SeeMusic có
> những kiểu chuyển động hạt nào, và PianoPath còn thiếu gì so với nó.

## 1. Kết luận ngắn

Chuyển động hạt hiện tại **không hỏng**, nhưng nó đang ở mức "vật lý của một điểm sáng", chưa phải
"vật lý của vật liệu". Ba điểm yếu lớn nhất, xếp theo mức ảnh hưởng tới cảm giác khi xem:

1. **Sustain đọc thành "nổ lặp lại" chứ không phải "dòng chảy".** Emitter giữ phím dùng chung nón phát
   của cú nhấn (`ParticleSpread` = ±65°). Đo trên lab: các hạt *held* của một phím trải rộng **55 px**
   trong khi bề rộng một phím chỉ **10,9 px** — một dòng suối không thể rộng bằng 5 phím.
2. **Không có trường gió dùng được.** `VectorField` là *gia tốc* cực nhỏ rồi bị drag nuốt ngay: ở mặc định 36,
   một tia lửa chỉ trôi **3,6 px** trong toàn bộ 0,9 s sống (0,4% bề ngang sân khấu). Phải kéo slider lên ~1000
   mới thấy 45 px. Vì thế hạt rơi theo đường cong riêng lẻ, không thành *luồng*.
3. **Ba đường render mô tả cùng một preset theo ba cách khác nhau.** Wisp: GPU cộng dao động *lúc vẽ*
   (biên độ 16,4 px, 5,75 rad/s), WPF cộng *vào vận tốc* (4,9 px, 6,85 rad/s) → lệch **10,6 px** sau 0,8 s.
   Riêng công thức biên độ đó được chép **4 lần** ở 4 file.

Ngoài ra có một lỗi hợp đồng rõ ràng: **"Physics time factor" ở đường WPF làm chậm cả sân khấu**, không chỉ hạt
(mục 6.1).

## 2. Cách đo

| Thành phần | Giá trị |
|---|---|
| Công cụ | `docs/prototypes/particle-motion-lab/lab.js` — mô phỏng lại integrator trong `GpuStageSimulation.Step()` |
| Seed | `20261019`; mỗi panel nhận **cùng chuỗi phát** (cùng thời điểm, góc, tốc độ, life) nên khác biệt chỉ do mô hình |
| Bước thời gian | 1/120 s (đo thêm 30 / 60 / 144 / 240 fps) |
| Thang | 960×540, bàn phím chiếm 20,5% chiều cao, `HitY` = 429,3 px, lane = 10,9 px |
| Kiểm tra tự động | `node tools/check_particle_motion_lab.cjs` — 36 assertions, gồm cả kiểm tra lab *không* làm hai mô hình trùng nhau |

**Giới hạn phải nói rõ:** đây là **bản mô phỏng lại**, không phải ảnh chụp từ ứng dụng. Sandbox không có
Windows, Direct3D hay `dotnet`, nên **không có** build, không có `--verify`, không có ảnh WARP, không đo FPS
trên GPU thật. Mọi số dưới đây là số của *công thức đã đọc trong mã*, không phải của *pixel đã render*.

## 3. Đường đi của một hạt hôm nay

Mỗi frame, `GpuStageSimulation.Step()` chạy đúng một vòng tích phân cho mọi hạt:

```text
p.X += p.Vx * dt;  p.Y += p.Vy * dt;
p.Vx += VectorField * sin(Y / FieldScale + time) * 14 * dt      // "gió"
p.Vy += (Gravity * Grav - 38 * heat / Mass) * dt                // trọng lực + lực nổi
p.Vx += sin(Y * .04 + Phase + time * 3.2) * 14 * (1 - age/life) // curl
p.Vx *= exp(-Drag * DragK * dt); p.Vy *= exp(...)               // drag
```

Ba nhánh rẽ trước đó: `Wisp` (bay thẳng + gió riêng), `Flame` (bay thẳng, **không** có trọng lực/drag/gió),
và mọi hình còn lại. Tốc độ, góc, life, size, màu đều là hằng số lúc sinh; **sau khi sinh không còn
trường nào thay đổi theo tuổi hạt trừ lực nổi và fade tuyến tính**.

| Thành phần cần cho chuyển động tự nhiên | Hiện trạng |
|---|---|
| Trường gió liên tục, nhìn thấy được | Có công thức, vô hình ở mặc định (3,6 px) |
| Xoáy cục bộ (curl) khác nhau theo từng hạt | Một sin dùng chung theo `Y`; hạt cạnh nhau gần như trôi cùng nhau |
| Đường cong sống (vào/ra, phồng/thu) | Gần như tuyến tính; chỉ droplet và flame có đổi size |
| Trễ sinh giữa các hạt trong một cú nổ | Không có: cả burst sinh trong cùng một frame, cùng tuổi |
| Xoay riêng của hạt (confetti, shard) | Không có trục quay; chỉ lắc vị trí |
| Va chạm (mặt phím, sàn) | Không có; hạt xuyên qua phím |
| Nhiệt độ như một biến trạng thái | Chỉ có `(1 - age/life)` dùng cho lực nổi |
| `Mass` | Chỉ dùng cho lực nổi; không có quán tính |
| Đồng bộ GPU ↔ WPF ↔ AR | **Đã sửa 2026-10-20:** wisp dùng chung một đường cong và slow-motion chỉ còn tác dụng lên hạt (W0) |

## 4. Vấn đề cụ thể (có dẫn chứng)

### 4.1 Emitter giữ phím dùng chung nón phát của cú nhấn — P0

`GpuStageSimulation.cs:349-390`: `angle = -π/2 + (Rand()-.5) * ParticleSpread * π` và `speed = ParticleVelocity * …`
giống hệt burst. Với `Spread` mặc định 0,72 → ±65°. Đo: độ lệch chuẩn theo trục x của hạt held trên một phím
là **55,2 px** (lab, t = 6,0 s), tức dòng chảy rộng gấp 5 lần phím. Người xem thấy một ngọn lửa phụt mỗi frame
chứ không thấy dòng khí bốc lên.

**Sửa:** mỗi emitter có `SpreadScale` riêng (held ≈ 0,22 → ±14°), `SpeedScale` ≈ 0,45 và một bias hướng lên.
Đây là thay đổi một dòng trong mỗi nhánh nhưng là khác biệt lớn nhất về cảm giác.

### 4.2 Trường gió không dùng được — P0

| `VectorField` | 36 (mặc định) | 100 | 300 | 1000 (max) |
|---|---|---|---|---|
| Trôi ngang của một tia lửa, 0,9 s (hiện tại) | 3,6 px | 6,4 px | 15,1 px | 45,5 px |
| Cùng phép đo với mô hình đề xuất (mục 5) | 19,2 px | 59,6 px | 172 px | 390 px |

Slider có 0…1000 nhưng 90% hành trình nằm dưới ngưỡng nhìn thấy. Nguyên nhân: `VectorField` là *gia tốc*
(`* 14`), rồi bị `exp(-0.26·dt)` hãm; hạt không bao giờ đạt được vận tốc gió.

**Sửa:** đổi sang **trường vận tốc** (hạt bị cuốn về phía gió với hằng số thời gian ~0,45 s), giữ nguyên
thang số cho preset cũ nhưng đổi hệ số trong `GpuLook` để 36 ≈ 15-20 px. Preset lưu số 36 vẫn hợp lệ.

### 4.3 Wisp: bốn bản sao của một công thức, hai cách hiểu — P0

*Số dòng dưới đây là hiện trạng lúc đo (2026-10-19), trước W0; mã đã đổi, xem đoạn "Sửa — đã làm" cuối mục.*

```text
GpuStageSimulation.cs:849-851   (đường art v3)   amp = (5 + turb*26) * (.45 + width) → cộng lúc VẼ
GpuStageSimulation.cs:969-971   (đường classic)  cùng công thức                    → cộng lúc VẼ
GpuArLayer.Effects.cs:465                        cùng công thức                    → cộng lúc VẼ
Stage/PianoStage.cs:620-630                      sin(age*(3+turb*7)) * (8+turb*46) → cộng vào VẬN TỐC
```

Đo trên cùng một hạt, cùng seed: sau 0,8 s GPU vẽ lệch **10,6 px** so với WPF; biên độ 16,4 px so với 4,9 px;
tần số 5,75 so với 6,85 rad/s. Vì dao động của GPU nằm ngoài mô phỏng, nó còn **không kéo theo hạt khác**
và **không có trong trạng thái AR** — AR phải tự tính lại, nên đó là bản sao thứ tư. Bản sao thứ năm nằm ở
`Diagnostics/ArParticleAlignmentReview.cs:73`, tức **chính công cụ đo** AR — nó so sản phẩm với số học của nó,
nên có thể tiếp tục xanh trong khi renderer lệch.

**Sửa — đã làm (W0, 2026-10-20):** một `static class WispMotion` (`Stage/WispMotion.cs`, cùng kiểu với
`StageLightCurves`) trả về offset ngang đã gồm dao động, và cả bốn chỗ đều gọi nó:
`GpuStageSimulation` ×2, `GpuArLayer.Effects`, `ArParticleAlignmentReview`. WPF **không còn cộng sin vào vận
tốc** nữa — nó cộng đúng offset đó lúc vẽ, nên đứng yên trong slow-motion và trùng GPU theo từng pixel.
Hai lớp khoá lại: `WispMotionTests` (8 fact, hình dạng đường cong) và `tools/check_sources.py`
(`scan_wisp_motion_contract`) — không file nào khác được nhân một giá trị turbulence với hằng số, và cả bốn
nơi vẽ phải gọi `WispMotion.Offset`. `--verify` đọc sprite thật của simulation và so từng hạt wisp với đường
cong, nên một bản sao mới sẽ đỏ ngay trên CI thay vì lệch dần trong video.

### 4.4 Flame không có vật lý — P1

`GpuStageSimulation.cs:451-455`: flame chỉ `Y += Vx·dt` với `Vy` hằng, một sin đẩy `Vx`, không trọng lực,
không drag, không gió, size không đổi. Kết quả: lửa "dán" vào phím trong khi tia lửa cạnh nó cong và tắt dần.
Ngọn lửa thật cần: nổi lên (gia tốc âm), nở ra khi nguội, tắt dần theo nhiệt độ, và bị gió bẻ cong.

### 4.5 Đường cong sống gần như tuyến tính — P1

`alpha = 1 - u` cho hầu hết hình, size hằng số trừ droplet/flame. Hệ quả: hạt **hiện ra đột ngột** ở frame đầu
(alpha 1 ngay lập tức) rồi tắt đều — mắt đọc đây là "bụi bật", không phải "vật bay". Ba thay đổi nhỏ:

- fade-in `smoothstep(0, 0.06 s, age)` — mất hẳn cảm giác pop;
- fade-out `(1-u)^1.4…1.8` tuỳ họ — tàn dư ngắn hơn, sân khấu sạch hơn khi nghỉ;
- size ramp theo họ (dot co lại, dust nở ra) — đây là thứ tạo cảm giác khối lượng.

### 4.6 Burst là một lớp vỏ đồng tuổi — P1

Mọi hạt của một cú nổ sinh trong cùng frame, cùng `age = 0`, cùng vận tốc theo một phân bố. Mắt thấy một
**vòng tròn nở ra** chứ không thấy một **đám** có chiều sâu. Trễ sinh 0-60 ms + biến thiên tốc độ theo seed
làm đám hạt có "khối".

### 4.7 Không có va chạm — P2

`Particle` không có khái niệm giới hạn. Tia lửa bay xuống xuyên qua phím và mất hút dưới `HitY`
(WPF có cùng hành vi). Splash và shard chỉ cần **một** điều kiện `if (y > HitY)` là có nảy, và điều đó miễn phí.

### 4.8 Hiệu năng: 1,4 triệu bước hạt mỗi giây trên luồng render — P2

Ở 6.000 hạt và 240 fps: **1,44 M** bước/s ⇒ khoảng **2,9 M `sin`** + **1,44 M `exp`** mỗi giây, làm trên
luồng đang chuẩn bị lệnh GPU (`MathF.Sin`, `MathF.Exp` không vector hoá). Đo được điều tốt: chất lượng
*không* phụ thuộc frame rate (xem 6.3), nên **không cần** đầu tư vào substepping ở mặc định.
Ngân sách hiện tại còn rất rộng (đỉnh 1.592 hạt so với trần 24.000), nghĩa là chi phí thật nằm ở **số phép
toán mỗi hạt**, không ở số hạt.

## 5. Đề xuất: Motion v2

Chín thành phần, mỗi thành phần bật/tắt độc lập trong lab để review riêng. Không thành phần nào đổi hình học
nốt, thời điểm MIDI hay preset đã lưu.

| # | Thành phần | Công thức / hành vi | Tham số mới | Ảnh hưởng |
|---|---|---|---|---|
| 1 | Emitter riêng cho sustain | nón hẹp, thiên lên, vận tốc thấp | `HeldSpread` (0,22), `HeldSpeed` (0,45) | Dòng chảy thay vì pháo hoa liên tục |
| 2 | Trường gió cuốn (velocity field) | `v += (wind − v) * (1 − e^(−dt/τ))`, τ ≈ 0,45 s | tái dùng `VectorField`, `FieldScale` | Hạt bay *cùng nhau* như không khí |
| 3 | Curl 2D theo seed từng hạt | 2 sin/cos lệch tần, mỗi hạt một `fieldSeed` | có sẵn `Phase` | Xoáy cục bộ, hết "cột cứng" |
| 4 | Khuếch tán rối | `vx += (seed − 0,5) * 46 * dt` | `Diffusion` (0-100) | Dòng hẹp nở dần theo tuổi |
| 5 | Đường cong sống theo họ | bảng `LifeCurve{FadeIn, FadeOutExp, Size0, Size1, Glow0, Glow1}` | dữ liệu, không slider | Hết pop-in, tàn dư sạch |
| 6 | Trễ sinh + trục quay | `age0 = −Random(0, 60 ms)`, `angle += spin·dt` | `Stagger`, `Spin` | Burst có khối; confetti lật thật |
| 7 | Nhiệt độ như biến trạng thái | `T` giảm theo hằng số riêng họ; buoyancy, màu, glow, drag đọc `T` | `CoolingRate` mỗi họ | Lửa/khói/embers dùng chung một lõi |
| 8 | Va chạm nhẹ | `if (y > HitY && vy > 0) vy *= −0,32; vx *= 0,78` | `FloorBounce` (0-100) | Splash/shard nảy trên phím |
| 9 | Slow-motion chỉ cho hạt | WPF: tách `particleDt` khỏi `frameDt` | không có | Đồng bộ hợp đồng đã ghi trong `EFFECTS-REDESIGN.md` §8 |

**Về slider:** đề xuất *không* thêm control nào ở màn hình mặc định. Năm tham số mới (1, 4, 6, 8 và độ
lạnh của họ) nên nằm sau **Nâng cao**, đúng nguyên tắc "control phổ biến trước" của
[PRISM-FLOW-DESIGN-ROADMAP](PRISM-FLOW-DESIGN-ROADMAP.md) §5 (Quy tắc thiết kế UI). Bốn control cũ (`Velocity`, `Spread`, `Drag`,
`Vector field`) giữ nguyên nhãn, chỉ đổi độ nhạy để slider có tác dụng ở nửa dưới hành trình.

**Về "Motion profile":** ba look Concert / Neon / Learning trong
[VISUAL-EFFECTS-DESIGN-BIBLE](VISUAL-EFFECTS-DESIGN-BIBLE.md) §3.2 hiện chỉ khác nhau ở *tham số* hạt.
Có thể gom thành một profile dữ liệu (như `PrismFlowPalette`) — `Concert` = ít hạt, gió chậm, fade dài;
`Neon` = nhiều hạt, curl mạnh, fade ngắn; `Learning` = tắt. Một control thay cho mười slider.

## 6. Đồng bộ giữa các đường render

### 6.1 "Physics time factor" của WPF làm chậm cả sân khấu — lỗi hợp đồng

Trước W0, `Stage/PianoStage.cs:602`: `var dt = Math.Clamp(seconds, 0, .05) * PhysicsTimeFactor / 100;` rồi dùng `dt` cho
**mọi thứ**: `_elapsed` (nền động), `_simulation.Advance`, nhiệt phím, sparks, rings, flashes và **live note
bars** (`trail.Age`). Trong khi đó đường GPU cố ý tách ra (`GpuStageSimulation.cs:241-244`):

> *"The physics slow-motion control belongs to particles; the stage clock, notes and ambient layers must keep
> their real tempo so changing it cannot slow the whole composition."*

và [EFFECTS-REDESIGN](EFFECTS-REDESIGN.md) §8 ghi đúng như vậy. Nghĩa là preset có `PhysicsTimeFactor = 10`
cho ra **hai trải nghiệm khác nhau** tuỳ máy có GPU hay không, và bản WPF vi phạm chính tài liệu của repo.

**Đã sửa (W0, 2026-10-20).** `Stage/StageClock.cs` giữ phép chia hai đồng hồ ở một chỗ; `Advance` lấy
`particleDt` cho **sparks** (tuổi, vận tốc, gió, drag, curl), còn `frameSeconds` cho `_elapsed`, `_simulation`,
`_beatPulse`/`_energyLevel`, nhiệt phím, ngân sách phát wisp, rings, flashes và **live bars**. Ở mặc định 100
hai đồng hồ là một (`x × 1.0 ≡ x`), nên mọi frame đã được chấp nhận — preview và bằng chứng WARP — không đổi
một bit. Ba lớp khoá: `StageClockTests` (5 fact, gồm "100 phải trùng khít", không phải "xấp xỉ"), guard
`scan_stage_clock_contract` trong `check_sources.py` (live bar/ring/flash mà ăn `particleDt` là đỏ), và
`VerifyStageClock` trong `--verify` dựng **hai `PianoStage` thật** ở 100% và 40%: thanh live bar phải đi đúng
quãng đường như nhau, còn wisp phải chậm hẳn — 0,4 lần là mốc lý thuyết, test chấp nhận 0,3–0,6 vì drag làm
quãng đường hụt ít hơn thời gian.

### 6.2 Wisp — xem 4.3

### 6.3 Những thứ **không** cần sửa (đã đo)

| Nghi vấn | Kết quả đo | Kết luận |
|---|---|---|
| Tích phân phụ thuộc frame rate | Lệch trung bình `Y` < 0,5 px giữa 30 / 60 / 144 / 240 fps | Không cần substepping mặc định |
| Drag vô dụng | Biên độ nâng 90 → 60 px khi Drag 0 → 100 | Có tác dụng; chỉ dải hữu ích bị nén ở cuối |
| Rò rỉ pool / NaN | 8 s × 5 họ × 2 mô hình: không NaN, đỉnh 1.592 hạt | Ổn định |
| Tất định theo seed | Cùng seed + cùng dt → trạng thái trùng khít | Ổn định |

### 6.4 Bản sao công thức nên gom lại

Ngoài wisp, biên độ `(5 + turb*26)` còn xuất hiện trong `Diagnostics/ArParticleAlignmentReview.cs:73`.
Bốn chỗ, một công thức. Nên có một nơi duy nhất — vừa sửa lỗi 4.3, vừa là điều kiện để viết test so sánh
GPU/WPF bằng số thay vì bằng mắt.

## 7. Lộ trình đề xuất (bốn lát cắt, mỗi lát qua cổng riêng)

| Lát | Nội dung | Vì sao trước | Cổng |
|---|---|---|---|
| **W0 · Hợp đồng** | **Xong 2026-10-20:** gom `WispMotion` (5 bản sao → 1), WPF bỏ dao động khỏi vận tốc, tách `particleDt` khỏi `frameDt` (`StageClock`), guard trong `check_sources` + `--verify`. **Chuyển sang W2:** vật lý flame — nó **đổi pixel**, mà bằng chứng `flames-v3` đang ghim đúng hình flame hiện tại, nên phải làm cùng lúc với một lần re-baseline có chủ đích, không phải như một refactor | Sửa lỗi, không đổi nghệ thuật; tạo nền để so sánh được | Test so sánh vị trí wisp GPU/WPF; slow-motion không đổi tốc độ live bar |
| **W1 · Hình dáng dòng chảy** | Emitter sustain riêng (thành phần 1), gió cuốn (thành phần 2), curl theo seed (thành phần 3), khuếch tán (thành phần 4) | 80% cảm giác "flow" mà roadmap P3b hứa; không thêm hạt | Ảnh/clip 3 checkpoint (nhấn/giữ/pedal) ở 100% và 50%; độ rộng dòng ≤ 1,5 phím |
| **W2 · Vật liệu** | Life curve theo họ (thành phần 5), trễ sinh + spin (thành phần 6), nhiệt độ (thành phần 7) | Làm đám hạt có khối lượng và "chín" | Bảng `LifeCurve` cho 5 họ × 3 look; review ảnh tĩnh + clip |
| **W3 · Tương tác & tải** | Va chạm (thành phần 8), bảng `exp`/`sin` nếu đo thấy cần; **cân nhắc** `EmissionScale` cho adaptive | Chỉ làm sau khi hình đã chốt | Benchmark `--bench` trên GPU thật (ngoài sandbox này) |

`GpuAdaptiveQualityProfile` hiện chỉ hạ bloom và ambient density; master roadmap §11.6 ghi rõ *"particle bursts …
không đổi"* theo tier. Vì vậy hạ mật độ hạt theo tải **không phải** một dòng thêm vào enum — nó là thay đổi hợp
đồng đã có test, cần cập nhật tài liệu và kiểm thử cùng lúc. Đó là lý do W3 để sau, không gộp vào W1.

**Không khuyến nghị** chuyển integrator sang compute shader ngay. Nó sẽ phá mô hình "một mô phỏng cho cả
Stage, AR và video" (AR đọc vị trí hạt trên CPU ở `ArParticleAt`, và cap AR 192 là thiết kế có chủ đích).
Nếu vẫn muốn: chỉ bật cho Stage sau một capability flag, giữ mô phỏng CPU cho AR, và cần golden-image test
riêng — tức là một dự án, không phải một lát cắt.

## 8. Tiêu chí nghiệm thu đề xuất

- Mỗi control còn lại phải làm thay đổi **một đại lượng đo được** (xem bảng ở mục 4.2 và 6.3), không chỉ đổi ảnh.
- Cùng scene, seed, `t`: **Stage và AR** phải cho cùng quỹ đạo hạt (sai khác ≤ 0,5 px trước khi chiếu).
- Cùng preset: **GPU và WPF** phải cho cùng *tốc độ* nốt và cùng độ rộng dòng wisp (sai khác ≤ 10%).
- Slow-motion 10%: tốc độ live note bar **không đổi**; chỉ hạt chậm lại.
- Ảnh tĩnh ở `Amount = 0` phải **trùng khít** ảnh không có lớp hạt.
- Với nốt dày (hợp âm 8 nốt, 30 fps): dòng hạt của hai phím cạnh nhau vẫn tách được, không nhoè thành một khối.
- Đo lại `--bench`: không tăng p95 quá **0,3 ms** so với baseline ở cùng số hạt.

## 9. Cách xem prototype

```bash
# không cần cài gì: mở trực tiếp
xdg-open docs/prototypes/particle-motion-lab/index.html      # hoặc double-click trên Windows

# kiểm tra mô hình không lệch và không hỏng
node tools/check_particle_motion_lab.cjs
```

Lab có hai chế độ: **Hiện tại ↔ v2** (cùng chuỗi phát, khác mô hình) và **GPU ↔ WPF** (cùng preset, hai đường
render đang có — cho thấy lệch ở mục 4.3). Cột trái bật/tắt từng thành phần v2, kéo `Drag` / `Vector field` /
`Gravity` / `Turbulence` để thấy slider nào thật sự làm gì; `Space` phát/dừng, `R` chơi lại.

## 10. Câu hỏi cần tác giả quyết

1. **Ưu tiên cảm giác nào?** Concert (chậm, ít, mềm) hay Neon (nhanh, nhiều, sắc)? Tỉ lệ hạt/gió/fade nên
   chọn theo một trong hai trước, đừng làm cả hai cùng lúc (đúng luật của master roadmap §1).
2. **Có chấp nhận đổi cảm giác khi giữ phím không?** W1 đổi sustain rõ nhất; preset cũ sẽ khác đi dù không
   đổi một con số nào. Nếu cần "không ai phản đối", nên đưa sau một toggle như `UpgradedParticleEffects`.
3. **Có cần đúng từng pixel giữa GPU và WPF không?** Nếu chỉ cần "na ná", W0 vẫn nên làm (nó là lỗi hợp đồng),
   nhưng tiêu chí nghiệm thu có thể nới từ px sang %.
4. **Ai chạy benchmark GPU thật?** Mọi kết luận hiệu năng ở đây là suy ra từ mã, không phải từ máy có GPU.

## 11. Tệp đã thêm trong lượt này

| Tệp | Vai trò |
|---|---|
| `docs/PARTICLE-MOTION-REVIEW.md` | Tài liệu này |
| `docs/prototypes/particle-motion-lab/index.html` | Lab A/B chạy bằng trình duyệt |
| `docs/prototypes/particle-motion-lab/lab.js` | Mô hình hiện tại, WPF, và v2; renderer 2D tối giản |
| `docs/prototypes/particle-motion-lab/README.md` | Cách chạy và giới hạn của prototype |
| `tools/check_particle_motion_lab.cjs` | 36 assertions headless cho hai prototype |

Không tệp mã sản phẩm nào bị sửa.
