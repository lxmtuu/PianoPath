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
| Kiểm tra tự động | `node tools/check_particle_motion_lab.cjs` — 59 assertions, gồm cả kiểm tra lab *không* làm hai mô hình trùng nhau |

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

**Một nửa đã sửa (2026-10-20):** bản WPF trước đây **không** vẽ hạt nào — nó vẽ một chùm ellipse dán `keyTop`
(≈43 × 99 px, tâm cách phím 35–41 px, chỉ sway/flicker tại chỗ). Nay `Stage/FlameMotion.cs` giữ đúng luật của
engine và bản phần mềm bắn hạt theo luật đó: 49 hạt/s/phím ở mặc định (70 × 0,7), bay lên 84–168 px/s, sống
0,382–0,544 s, size 5,1–9,4 px, nguội trắng → vàng → cam → đỏ thẫm. Vật lý *của engine* (gia tốc nổi, nở ra
khi nguội) vẫn là W2 vì nó đổi pixel đã ghim trong `flames-v3`.

**Candidate W2 (2026-10-21):** `FlameMotion.StepV3` giữ nguyên sinh/tuổi/phát xạ, chỉ đổi *đường bay*: nổi có
gia tốc 30 px/s², drag 1,7/s ngang và 0,9/s dọc, size nở 0,9/s (≈1,57× sau nửa giây). Engine gọi đúng hàm đó
khi `GpuLook.FlameMotionV3ForReview` bật, và cờ này **chỉ** do review chuyển động bật — nên ảnh tĩnh `flames-v3`
(vẫn phải trùng geometry với v2 để đo ánh sáng) và mọi frame đang ship giữ nguyên đường bay cũ. Số đo: một hạt
mặc định lên 59,3 px trong 0,5 giây thay vì 70 px khi trôi đều, chao ngang bị chặn ở 23,5 px/s (40 / 1,7) thay
vì cộng dồn suốt đời hạt. Lab đổi nhánh `flamePhysics` sang đúng bốn số đó; `FlameMotionTests` (+4 fact) và
`check_particle_motion_lab.cjs` §7b cùng đọc lại hằng số từ mã C#, nên lab, engine và test không thể lệch nhau.
Chưa bật cho người dùng: `UpgradedParticleEffects` vẫn chỉ chọn vật liệu, chưa chọn chuyển động.

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

### 5.1 Bốn thành phần đầu đã dựng thành mô hình `flow` và **đo được**

`docs/prototypes/particle-motion-lab/lab.js` có thêm `MODEL.FLOW`, cùng chế độ **GPU ↔ flow (W1)** trên trang.
Nó giữ nguyên tiền đề của lab: hai panel nhận **cùng một chuỗi phát**, nên khác biệt nhìn thấy chỉ đến từ mô
hình. Bốn thành phần đầu của bảng trên được cài đúng như công thức đã ghi:

| # | Trong `lab.js` | Hằng số |
|---|---|---|
| 1 | nón hẹp, chậm, thiên lên cho emitter sustain (không đụng chùm impact) | `HELD_SPREAD .22`, `HELD_SPEED .45` |
| 2 | `v += (wind − v)(1 − e^(−dt/τ))`, wind là curl của một hàm dòng **không phân kỳ** + gió nâng nhẹ | `FLOW_TAU .45`; chiều dọc chỉ nhận 12% (`FLOW_VERTICAL`) để gió **bẻ** dòng chứ không **dập** dòng |
| 3 | curl nhỏ, lệch pha theo `fieldSeed` từng hạt | biên độ 520 px²/s |
| 4 | `vx += (seed − 0,5)·46·dt`, nhân theo slider `Diffusion`/55 | `FLOW_DIFFUSION 46` — đúng hằng số trong bảng |

**Số đo** (`node tools/check_particle_motion_lab.cjs` §7; một dòng wisp 1,2 s ở một phím, 120 hạt, đo tại
t = 1,2 s; "phím" = bề rộng phím trắng; độ rộng là p10–p90 của vị trí **lúc vẽ** trong một lát dày 20 px):

| Đại lượng | GPU đang ship | flow (W1) |
|---|---|---|
| Độ rộng lát 30–50 px trên phím | **0,62 phím** | **0,14 phím** |
| Độ rộng lát 70–90 px | **1,06 phím** | **0,41 phím** |
| Độ rộng lát 110–130 px | **1,87 phím** | **0,49 phím** |
| "Cong như một khối" (local spread, thấp = tốt) | 0,47 | **0,29** |
| Độ cao lớn nhất còn lại | 195 px | 173 px (−11%) |
| Tâm dòng lệch khỏi phím | −0,79 phím | −0,30 phím |
| Tắt `Diffusion` → lát giữa | — | 0,21 phím (≈½) |
| Tắt gió → local spread | — | 0,54 (mất tính khối) |
| Emitter sustain so với chùm impact | 1,00× nón / 1,00× tốc độ | 0,31× / 0,45× |
| Flame: độ cao / size trung bình | 82 px / 5,20 | 76 px / 6,47 |

Ba điều đọc được từ bảng, và chúng **đổi cách hiểu cổng nghiệm thu**:

1. **Cổng "≤ 1,5 phím" không phải hiện trạng.** Đo đúng cách (vị trí lúc vẽ, p10–p90) thì dòng wisp đang ship
   đã rộng **1,87 phím** ở một phần ba trên. Nghĩa là 1,5 phím là mục tiêu *thu hẹp*, không phải "giữ nguyên".
   W1 đạt 0,49 phím ở cùng lát — dưới cổng gần 3 lần.
2. **Khác biệt không chỉ là hẹp hơn.** local spread 0,29 so với 0,47: dòng mới cong **như một khối**, còn dòng
   cũ là hiệu ứng sway ngẫu nhiên từng hạt. Đó chính là cảm giác "flow" mà P3b hứa, và là thứ không thể đo
   bằng độ rộng.
3. **Hai thành phần làm hai việc khác nhau.** Tắt diffusion → lát giữa hẹp còn một nửa; tắt gió → dòng mất tính
   khối (0,54). Vì vậy không thể bỏ thành phần nào mà vẫn giữ hình.

**Hệ quả hợp đồng (quan trọng):** mô hình flow chạy **bên trong integrator**, không phải offset lúc vẽ như W0.
Khuếch tán là quá trình có lịch sử — không thể tính lại tại thời điểm vẽ. Vì vậy muốn đưa vào sản phẩm thì GPU,
WPF và AR phải chạy **cùng một** integrator (AR đọc vị trí CPU nên điều này khả thi), và mọi frame của
`wisps-v3`/`flames-v3` sẽ đổi. Đó là lý do W1 dừng ở mức mô hình + số đo: cần một lần re-baseline có chủ đích.

**Đã vào sản phẩm (2026-10-22), và đây là cách nó giữ được bước re-baseline:** luật bốn phần nằm trong
`Stage/ParticleFlow.cs` — gió không phân kỳ (`Wind`), xoáy riêng từng hạt (`Curl`), khuếch tán (`Diffusion`), và
một `Step` ghép chúng — với đúng hằng số của lab (`EntrainSeconds` 0,45 s; `VerticalShare` 0,12; `DiffusionRate` 46
px/s²). Cả `GpuStageSimulation` (wisp + lửa) và `PianoStage` (hai danh sách rời) gọi **cùng** hàm đó; AR đọc vị trí
CPU nên đi theo miễn phí. Nó nằm sau slider **Coherent flow** (`ParticleFlow`, 0–100) **mặc định 0**: ở 0 hai
integrator chạy đúng số học cũ, nên `wisps-v3`/`flames-v3` không đổi một byte và lượt CI vẫn xanh. Bật lên là look
mới; lật **mặc định** mới là lần re-baseline — và đó là việc phải chạy trên máy có GPU/WPF thật, vì pipeline bằng
chứng so hash với bản đã chấp nhận và không cho phép tự nhận bản mới (`review_flames_v3.retained`). Bản vào sản phẩm xanh ở [run 39799609388](https://github.com/lxmtuu/PianoPath/actions/runs/39799609388): `static`, `test`, `keyboard-light`, `build` đều qua, và bước `Validate and stage Flames v3` vẫn khớp đúng hash wisps/flames đã chấp nhận — mặc định 0 không đổi một pixel.

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

### 6.5 Mật độ phát của emitter giữ phím — lệch 4× và thiếu một kênh

Hai đường render đã luôn đồng ý **cái gì** phát ra từ một phím giữ (ember theo họ burst, wisp, flame) nhưng
không đồng ý **bao nhiêu**:

| Kênh | Engine (`GpuStageSimulation.Step`) | WPF trước 2026-10-20 |
|---|---|---|
| Ember khi giữ phím | `ParticleAmount × 1,3`/giây/phím | **không có kênh nào** |
| Wisp | `WispAmount × 4`/giây/phím (bốn, vì hai sợi xoắn vào nhau) | `WispAmount` — **một phần tư** |
| Flame | `70 × intensity × heat`/giây/phím | cùng luật (W2, 2026-10-20) |

Hệ quả đúng như ta thấy trong ảnh: cùng một preset, máy có GPU cho một phím phun ember + hai sợi khói xoắn,
máy không GPU cho một phím trơ với chút hạt lác đác.

**Đã sửa:** `Stage/HeldEmission.cs` giữ hai mật độ ở một chỗ; `SpawnWisps` và `SpawnHeldSparks` (mới) gọi nó và
dùng chung `KeyEmissionBudget.Take`, nên cách tích luỹ credit, ngưỡng `1e-9` và cách **bỏ** hạt khi pool đầy đều
giống engine. Engine **giữ nguyên** biểu thức `float` của nó (`look.WispAmount * 4f`,
`look.ParticleAmount * 1.3f`) để frame đã ghim không đổi bit; guard `scan_held_emission_parity` bắt cả hai phía.

Hai quyết định đi kèm:

- **Trần khác nhau, mật độ giống nhau.** `WpfParticleCap = 2.600` so với 24.000 của GPU vẫn đúng — đó là ngân
  sách thời gian khung hình, và `EffectBudget` đã ghi đúng như vậy. Cái sai là phát **ít đi** ngay từ đầu.
- **Không mô phỏng hạt phần mềm khi GPU đang vẽ.** `Advance` trước đây vẫn sinh và tích phân wisp/spark/flame dù
  `OnRender` đã giao cả sân khấu cho `RenderGpuFrame`. Với mật độ mới, một hợp âm bốn phím sẽ là ~800 hạt vô hình
  mỗi giây trên luồng UI. Nay phần phát bị bỏ và credit bị xoá, nên đổi engine giữa chừng không xả backlog.

Đo trong `--verify` (`VerifyWpfEmissionParity`): một giây giữ phím ở mặc định sinh **180 wisp / 44 ember /
45 flame mote**, đúng ba mật độ của engine; tắt từng kênh thì bằng 0.

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
| **W0 · Hợp đồng** | **Xong 2026-10-20:** gom `WispMotion` (5 bản sao → 1), WPF bỏ dao động khỏi vận tốc, tách `particleDt` khỏi `frameDt` (`StageClock`), và **mật độ phát** của emitter giữ phím (`HeldEmission` — trước đó WPF phát 1/4 wisp và không có ember), guard trong `check_sources` + `--verify`. **Chuyển sang W2:** vật lý flame — nó **đổi pixel**, mà bằng chứng `flames-v3` đang ghim đúng hình flame hiện tại, nên phải làm cùng lúc với một lần re-baseline có chủ đích, không phải như một refactor | Sửa lỗi, không đổi nghệ thuật; tạo nền để so sánh được | Test so sánh vị trí wisp GPU/WPF; slow-motion không đổi tốc độ live bar |
| **W1 · Hình dáng dòng chảy** | **Mô hình + số đo xong 2026-10-20:** bốn thành phần (emitter sustain, gió cuốn, curl theo seed, khuếch tán) chạy trong lab dưới `MODEL.FLOW`, có chế độ so sánh trên trang và 12 assertion đo trong `check_particle_motion_lab.cjs` §7. **Đã vào sản phẩm 2026-10-22 sau slider `ParticleFlow` mặc định 0:** `Stage/ParticleFlow.cs` là luật duy nhất, `GpuStageSimulation` + `PianoStage` cùng gọi, `scan_particle_flow_contract` ghim hình dạng đó và `ParticleFlowTests` ghim bốn tính chất; còn lại là lật mặc định = re-baseline `wisps-v3`/`flames-v3` trên máy có GPU thật, cùng loại với W2 | 80% cảm giác "flow" mà roadmap P3b hứa; không thêm hạt | **Đo được:** độ rộng 0,14 / 0,41 / 0,49 phím ở ba lát (cổng 1,5 — dòng đang ship đo 1,87), local spread 0,29 so với 0,47, mất 11% độ cao. Còn thiếu ảnh/clip 3 checkpoint trên máy có GPU |
| **W2 · Vật liệu** | **Nửa đầu xong 2026-10-20:** bản phần mềm vẽ flame bằng đúng luật hạt của engine (`FlameMotion`, guard + 5 fact + `VerifyWpfFlameParity`) thay cho chùm ellipse dán phím. **Candidate 2026-10-21:** vật lý của chính engine (nổi có gia tốc, drag, nở khi nguội) nằm ở `FlameMotion.StepV3` và chỉ bật trong clip đối chứng `flames-v3/motion`, nên ảnh tĩnh v2/v3 vẫn đo ánh sáng trên cùng geometry. **Còn lại:** chốt candidate (đưa vào preset/`UpgradedParticleEffects`) và màu theo nhiệt độ nếu review đòi | Làm đám hạt có khối lượng và "chín" | Bảng `LifeCurve` cho 5 họ × 3 look; review ảnh tĩnh + clip |
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
| `tools/check_particle_motion_lab.cjs` | 59 assertions headless cho hai prototype, gồm §7b đọc hằng số W2 từ `Stage/FlameMotion.cs` |

Không tệp mã sản phẩm nào bị sửa.
