# Thanh damper — phân tích ánh sáng chuyển động và đề xuất

> **Ngày:** 2026-10-19 · **Nhánh:** `arena/efa69378-pianopath` · **Loại:** phân tích + prototype
> **Cập nhật cùng ngày:** **D0–D3 đã vào mã sản phẩm** — CC64 liên tục tới hình, đường cong cơ học
> `smoothstep(0,25; 0,50)`, một bộ làm mượt 55/75 ms dùng chung, lõi sáng chạy theo lift, vạch/halo stage
> nhích lên cùng quy ước, **một ô LED + một pin theo từng phím**, và lõi **thức dậy qua bloom** khi pedal xuống
> sâu. Các số đo ở §1–§4 mô tả **trước** thay đổi, trừ khi ghi rõ "sau D0–D3".
> **Xem trước:** [damper.html](prototypes/particle-motion-lab/damper.html) — A/B *hiện tại ↔ đề xuất* trên cùng một
> cử chỉ pedal.
> **Liên quan:** [PARTICLE-MOTION-SEEMUSIC-REFERENCE.md](PARTICLE-MOTION-SEEMUSIC-REFERENCE.md) (ngữ pháp hạt),
> [AR-OVERLAY-ROADMAP.md](AR-OVERLAY-ROADMAP.md) §14.3 (phần tử `Dampers`).

## 0. Tóm tắt

1. PianoPath có **ba** cách vẽ cùng một sự kiện "pedal xuống": dải damper AR, vạch sáng mép sau phím trên stage
   WPF, và hào quang (halo) + độ sáng phím trên stage GPU. Ba cách **không cùng đồng hồ**, không cùng hình học,
   và hai trong ba mặc định **tắt**.
2. Dải AR **từng không có chuyển động dọc theo thanh**: nó chỉ dày lên 9,0 → 23,8 px (góc 45°, ở 1280×720) và
   đậm lên 0,18 → 0,50 alpha, còn lõi sáng bị ghim ở mép phím. **Đã sửa (D1):** lõi chạy 0 → **8,2 px** ở 45°
   và 0 → **6,6 px** ở 90°, giữ một đường tiếp xúc mờ tại mép phím.
3. Đường MIDI thật **từng làm mất half-pedal trước khi tới hình**: CC64 liên tục chỉ đi vào âm thanh; phần hình
   chỉ nhận một `bool` ở ngưỡng 64. **Đã sửa (D0 bước 1–2):** `PedalLevelChanged` giờ cũng đưa mức 0..1 tới
   `PianoStage.SetSustainLevel`, và cả dải AR lẫn Pedal Glow dùng chung `ArDampers.LiftFromPedal`.
4. Ba tốc độ **đã thống nhất (D0 bước 3)**: cả WPF, GPU và dải AR dùng chung `ArDampers.Smooth` với
   **τ = 55 ms khi xuống, 75 ms khi lên**. WPF và GPU vẫn khác nhau về độ rộng vạch (12 → 19,2 px so với hình
   học cố định) — khác biệt có chủ ý.
5. Đề xuất D0–D3: đưa CC64 liên tục vào hình, hiệu chỉnh theo đúng hành trình cơ học (chết 0–25%, đầy ở 50%),
   thống nhất một bộ làm mượt, cho **lõi sáng chạy** theo độ nhấc, và thêm **đèn damper theo từng phím** để thanh
   damper thật sự "động" theo ngón tay chứ không chỉ sáng lên.
   **D0–D3 đã xong**: lõi chỉ vượt ngưỡng bloom 1,8 từ ~42% hành trình pedal, và dải thở ±2% khi pedal giữ.

SeeMusic có một **thanh đèn LED theo từng phím** ở mép sau bàn phím (thấy rõ trong ảnh marketing), nhưng
tài liệu của họ **không gọi nó là damper** và không mô tả nó phản ứng pedal (xem §5). PianoPath thì ngược lại:
một thanh **damper** đơn sắc, phản ứng pedal. Hai ngữ pháp khác nhau — và chỗ giao thoa là đề xuất chính của
tài liệu này.

## 1. "Thanh damper" ở đây là gì

Trong repo có ba thứ dễ bị gọi chung là "thanh damper":

| Thành phần | Nơi vẽ | Là gì |
|---|---|---|
| **Felt strip** | `Stage/PianoStage.cs:3079` (`DrawFelt`), GPU `BuildKeys` | Dải nhung đỏ trên mép sau phím, mô phỏng thanh damper thật. Tùy chọn `ShowKeyFelt`, mặc định **tắt** |
| **Vạch sáng mép sau phím** | WPF `PianoStage.cs:3013` (`glowPen`), GPU `GpuStageSimulation.cs:1013` | Đường phát sáng chạy hết chiều ngang bàn phím, tại `keyTop` |
| **Dải damper AR** | `Camera/ArDampers.cs`, `Gpu/GpuArLayer.cs:134`, shader kind 11 | Dải sáng nằm *sau* mép phím trong ảnh camera, dày lên theo sustain |

Tài liệu này gọi chung nhóm này là **damper light**; "thanh damper" là phần nhìn thấy của nó.

## 2. Hiện trạng — từng đường vẽ

### 2.1 Dải AR (`ArDampers` + shader kind 11)

| Thuộc tính | Giá trị | Nguồn |
|---|---|---|
| Độ dày khi pedal lên | `0,06` key-depth | `ArDampers.BaseDepth` |
| Độ dày khi pedal xuống hết | `0,16` key-depth | `BaseDepth + LiftDepth` |
| Alpha khi lên / xuống | `0,18` → `0,50` | `AlphaBase`, `AlphaLift` |
| Màu | màu halo của look, hoặc màu tự chọn `#8FE8FF` | `GpuLook.cs:371-373` |
| Hình dạng | một quad phủ **toàn bộ** phím, `u` 0..1 | `TryPatch` |
| Ô theo phím (D2) | 88 ô rộng `0,70` phím trắng (`0,46` phím đen), alpha `0,05+0,42v`, mép `0,16+0,54v`; màu mix 22% về pitch | `GpuArLayer.cs`, shader kind 12 |
| Pin theo phím (D2) | cao `15·v` px, rộng 2,4 px, alpha `0,18+0,58v`, crossbar 5,4×2 px | `ArDampers.TickHeight/TickWidth` |
| Lõi sáng | chạy từ `uv.y = 1` tới `uv.y ≈ 0,656` theo lift; đường tiếp xúc mờ ở mép phím | `StageShaders.hlsl:972-989` |
| Thời gian | **55 ms xuống / 75 ms lên**, dùng chung với stage | `ArDampers.Smooth`, `GpuStageSimulation.cs:322` |
| Mặc định | **bật** | `PianoVisualSettings.cs:518` |

Shader: `edge = 1 − uv.y`, `core = exp(−(uv.y − corePos)²·38)·2,1` với `corePos = ArDampers.CorePositionFor(lift)`,
`halo = exp(−edge·5)·0,55`, `contact = exp(−edge²·90)·0,25`. Khi pedal xuống, quad **dài thêm về phía fallboard**
và lõi **chạy theo**; mép phím vẫn còn một đường tiếp xúc mờ để dải không rời khỏi phím.

### 2.2 Stage WPF

- **Vạch `glowPen`** tại `top + 1`: alpha `(30 + KeyLighting·0,95) · PedalBoost`, độ rộng
  `(5 + BloomSize/10) · PedalBoost` (`PianoStage.cs:3013`). Với mặc định `KeyLighting = 32`, `BloomSize = 70`:
  **12 → 19,2 px**, alpha **60 → 97** khi `PedalBoost` = 1,6.
- **Halo rộng** `DrawImpactLine` tại `keyTop`: dải cao `(6 + BloomSize/5)·6` = **120 px**, alpha
  `(24 + BloomIntensity·0,75) · intensity` = **76 → 121**, cộng spill 60 px xuống mặt phím
  (`PianoStage.cs:2894-2910`).
- `PedalBoost()` = `1 + PedalGlowIntensity/100 · lift` khi pedal xuống (`PianoStage.cs:1721`); `lift` được làm mượt
  trong `Advance` bằng `ArDampers.Smooth` (55/75 ms). Vạch `glowPen` dịch lên `6·lift` DIP (`PianoStage.cs:3046`)
  để cùng quy ước với lõi AR. Mặc định `PedalGlow = false`, `PedalGlowIntensity = 60`
  (`PianoVisualSettings.cs:318-319`).
- Felt strip (`DrawFelt`) **không** đổi theo pedal: nó là vật liệu, không phải đèn.

### 2.3 Stage GPU

- `_pedalBoost = 1 + PedalGlowIntensity · SustainLift` (không còn bộ lọc riêng), với `SustainLift` được làm mượt
  55/75 ms trong `Step` (`GpuStageSimulation.cs:322-325`); `GlowBoost = _pedalBoost · EnergyBoost` (`:170`).
  Nửa pedal cho nửa độ sáng, và dải AR đọc đúng cùng `SustainLift`.
- `GlowBoost` nhân vào halo tại `hitY` (`:1013`) và vào halo từng phím đang kêu (`:1007`).
- Độ sáng phím: `KeyB.y = ShaderEmissive · 1,7 · PedalBoost` (`GpuStageRenderer.cs:751`).
- Halo tại `hitY` dịch lên `5 · lift` DIP khi Pedal Glow bật (`GpuStageSimulation.cs:1014`); felt box không đổi màu
  (`GpuStageSimulation.cs:785-792`).
- Mặc định `PedalGlow = false` → stage không phản ứng pedal.

### 2.4 Bảng so sánh ba đường

| | Dải AR | WPF stage | GPU stage |
|---|---|---|---|
| Hình học đổi | dày 0,06 → 0,16 key-depth; lõi chạy 8,2/6,6 px | vạch 12 → 19,2 px, dịch lên 6 px; halo 120 px không đổi | halo dịch lên 5 px |
| Độ sáng đổi | alpha 0,18 → 0,50 (2,78×) | +60% (alpha 60 → 97, halo 76 → 121) | ×1,6 |
| Làm mượt | **55/75 ms** | **55/75 ms** | **55/75 ms** |
| Nguồn mức pedal | lift 0..1 (sau D0) | lift 0..1 (sau D0) | lift 0..1 (sau D0) |
| Mặc định bật | **có** | **không** (`PedalGlow = false`) | **không** |
| Có theo từng phím | **có** (ô + pin, D2; thanh nền vẫn đều) | không (halo theo phím là của nốt) | không |

## 3. Chuyển động đo được

### 3.1 Không gian

Từ calibration 1280×720 trong `ArDampersTests` (homography unit-square → quad), chiếu dải tại `u = 0,5`:

| Pedal lift | Độ dày (45°) | Độ dày (90° overhead) | Alpha | Lõi dịch 45° / 90° |
|---|---|---|---|---|
| 0,00 | 9,0 px | 7,2 px | 0,18 | 0 / 0 |
| 0,25 | 12,7 px | 10,2 px | 0,26 | 2,0 / 1,7 px |
| 0,50 | 16,4 px | 13,2 px | 0,34 | 4,1 / 3,3 px |
| 0,75 | 20,1 px | 16,2 px | 0,42 | 6,1 / 5,0 px |
| 1,00 | 23,8 px | 19,2 px | 0,50 | 8,2 / 6,6 px |

Tổng dịch chuyển của **mép sau**: 14,8 px (45°) / 12,0 px (90°). Trước D1, lõi sáng không dịch — nó vẫn ở
mép phím, nên mắt chỉ nhận ra độ dày tăng. Sau D1, lõi dịch 55% phần dày thêm, tức **8,2 / 6,6 px** ở full
pedal, đủ để đọc thành một đường sáng đi lên.

### 3.2 Ánh sáng

Alpha tăng **2,78×**; lõi HDR `2,1 + 0,55` nhân với alpha nên cường độ điểm sáng cũng tăng 2,78×, đủ vượt
ngưỡng bloom. Ở 45°, dải rộng 1144 px; ở 90°, 1200 px. WPF tăng **1,6×** (không phải 2,78×), vì `PedalBoost`
bị giới hạn bởi `PedalGlowIntensity`; halo rộng chỉ tăng alpha, không tăng chiều cao.

### 3.3 Thời gian

**Trước D0:** WPF đổi trong 1 frame, GPU τ = 100 ms, dải AR đổi trong 1 frame. CC64 nhảy 0 → 127 làm mép sau
dải AR đi 14,8 px trong 16,7 ms = **886 px/s** (45°), 719 px/s (90°) — một cú *pop*.

**Sau D0 (bước 3):** cả ba dùng `ArDampers.Smooth` với **τ = 55 ms xuống / 75 ms lên** (10–90% ≈ 121 / 165 ms).
Với CC64 nhảy 0 → 127, tốc độ ban đầu của mép sau là `14,8 / 0,055` ≈ **269 px/s** (45°), giảm 3,3×; không còn
overshoot. Bảng đo thời gian chi tiết nằm trong `damper.js`/`check_damper_light.cjs`.

### 3.4 Dọc theo thanh

**Trước D2: không có.** Dải phủ `u` 0..1 với cùng độ dày/alpha ở mọi phím; **trên thanh** không có đèn theo
phím, không có sóng chạy, không có nhịp thở khi pedal giữ — cả ba đường đều là "một thanh, một độ sáng".

**Từ D2:** mỗi phím có một ô riêng trong dải, nên dọc thanh đã có biến thiên: phím đang giữ sáng 1, phím
không giữ chỉ sáng theo pedal, phím đen có ô hẹp hơn, và mỗi ô có một pin cao theo damper của nó. Độ dày/alpha
của **thanh nền** vẫn đều — đó là chủ ý: thanh là "pedal đang ở đâu", ô là "phím nào đang kêu".
Sóng chạy ngang vẫn chưa làm; nhịp thở đã có từ D3 (±2% ở 0,7 Hz, tắt theo Reduced Motion).

## 4. Lỗ hổng half-pedal

Đường tín hiệu thật:

```
MIDI CC64 (0..127)
  ├─ PedalLevelChanged ──► _audio.ControlChange(64, value)       (âm thanh, liên tục)
  │                     └► Stage.SetSustainLevel(value / 127)   (hình, liên tục — D0)
  └─ PedalChanged (>=64) ─► SetPedalState(bool)                  (edge cho toggle UI)
                              └─ Stage.SetSustainPedal(bool)
                                   ├─ lift = smoothstep(.25, .50, raw)
                                   ├─ _simulation.SetSustain(lift)
                                   └─ _gpu.SetSustainLevel(down, raw)
```

`Ui/MainWindow.xaml.cs:129-136`; `Stage/PianoStage.cs:256-296`; `Gpu/GpuStageFeed.cs:113-120`.

Trước D0, phần hình chỉ nhận `bool`; **hiện tại** cả ba đường đi qua cùng một `lift`. Hệ quả còn lại là
nhịp làm mượt (bước 3 của D0), không còn là mất half-pedal.

Hệ quả trước D0:

- **Trước D0**, `ArDampers.ClampLevel` và `GpuStageFeed.SetSustainLevel` hỗ trợ 0..1 nhưng **không có caller nào**
  truyền giá trị trung gian trong ứng dụng thật; giờ `MainWindow` gọi `Stage.SetSustainLevel` từ CC64.
- **Trước D0**, ngưỡng hình là **64/127 = 0,504**, trong khi âm thanh có vùng tự do `< 0,25` và catch `>= 0,5`;
  khoảng CC64 **0,25–0,50** đã ngân một phần nhưng đèn damper tắt hoàn toàn. Nay `LiftFromPedal` cho đúng vùng
  chết 0–25% và đầy ở 50%, khớp mô hình âm thanh.
- UI ghi "level 0..1" (`MainWindow.Settings.cs:629`) và "half-pedalling shows" (`GpuStageFeed.cs:25`); trước D0
  hình chỉ có hai trạng thái. Đây từng là **lỗi hợp đồng**, cùng loại với lỗi `PhysicsTimeFactor` đã ghi ở
  [PARTICLE-MOTION-REVIEW.md](PARTICLE-MOTION-REVIEW.md); D0 bước 1–2 đã đóng phần mất half-pedal.

## 5. SeeMusic: thanh LED theo phím, không phải damper rail

Tài liệu chính thức **không** dùng từ damper cho phần hình. Đã đọc transcript đầy đủ hai video hướng dẫn:

- **2023** (`l8lboohrSUY`): Effect Settings gồm Note, Keyboard, Particle, **Light**, Saber, Texture, Glow,
  Background. Light Settings chỉ có **scale/opacity của đèn bàn phím**, light rays, bright center, và chọn
  above/below video. Không nhắc pedal hay damper.
- **2020** (`NiPJyWJgIs4`): particle settings, "add light to video", render — không nhắc damper.
- Trang Features / Support / forum: nói về sustain **như dữ liệu MIDI** (ghi, phát, half-pedal trong âm thanh),
  không mô tả một thanh damper phát sáng.
- **Ảnh marketing** trên `seemusicapp.com/seemusic`, truy cập 2026-10-19 — nguồn duy nhất cho thấy thanh sáng
  ở mép sau bàn phím. Ba ảnh cho thấy:

| Ảnh | Thanh sáng | Diễn giải |
|---|---|---|
| Ảnh bàn phím trắng-cyan | Một vạch sáng liền, chỗ phím đang chơi sáng hơn | Đèn theo phím + bloom làm liền mạch |
| Ảnh hai tay đỏ/xanh | Thanh đổi màu theo tay/track (đỏ trái, xanh phải) | Màu theo nốt/track, không phải theo pedal |
| Ảnh desktop cầu vồng | Thanh cầu vồng suốt 88 phím | Màu theo pitch, có thể cả khi không chơi |

Kết luận có bằng chứng:

- Đây là **keyboard lights** (có thể có lớp saber phủ lên; tài liệu không tách bạch trong ảnh) — 88 ô sáng,
  mỗi ô theo phím/nốt, màu theo pitch hoặc track, bloom biến chuỗi ô thành một thanh liền. Nó **không nhấc
  lên** và không được mô tả là phản ứng pedal.
- PianoPath (trước D2) có thứ ngược lại: **một** thanh damper đơn sắc, **nhấc theo pedal**, không chia theo
  phím. Từ D2 nó có thêm ô theo phím, nhưng vẫn **nhấc theo pedal** — đó là chỗ hai ngữ pháp gặp nhau.
- Hai ngữ pháp trả lời hai câu hỏi khác nhau: SeeMusic trả lời "phím nào đang kêu"; PianoPath trả lời "pedal
  đang ở đâu". Cả hai đều đúng — và có thể **cộng lại**.

Vì vậy phần D2 dưới đây không phải "bắt chước SeeMusic" mà là **hợp nhất**: giữ chuyển động nhấc của damper,
thêm cách chia ô theo phím của LED. Nếu cậu có một clip SeeMusic cụ thể muốn đối chiếu, gửi timestamp/ảnh.

> Giới hạn: SeeMusic cũng có **3D scene** với piano model và camera (trang Features). Nếu model 3D của họ có
> damper động thì tài liệu công khai không mô tả; ảnh marketing chỉ cho thấy thanh LED 2D. Mình không suy đoán
> thêm.

## 6. Từ vựng chuyển động cho một thanh damper

| # | Chuyển động | Hiện tại | Đề xuất |
|---|---|---|---|
| 1 | **Lift** — dải dày lên | có (AR, 14,8 px) | giữ, nhưng theo đường cong cơ học |
| 2 | **Core travel** — lõi sáng đi lên | **không** (ghim ở mép phím) | **có** (D1, đã vào mã) |
| 3 | **Brightness swell** — sáng lên | có, nhưng bật/tắt | có, có làm mượt |
| 4 | **Width swell** — vạch WPF rộng ra | có (12 → 19,2 px) | giữ, cùng thời gian với AR |
| 5 | **Release fall** — buông pedal | tức thì | rơi **chậm hơn** lúc nhấn một chút (D0: 75 vs 55 ms) |
| 6 | **Per-key cells** — 88 ô sáng theo phím (kiểu SeeMusic) | **không** | **có** (D2, đã vào mã) |
| 7 | **Damper heads** — pin nhô lên theo từng phím | **không** | **có** (D2, đã vào mã) |
| 8 | **Sweep along bar** — sóng chạy ngang | **không** | tùy chọn, không ưu tiên |
| 9 | **Idle breath** — thở khi giữ pedal | **không** | tùy chọn, rất nhẹ |
| 10 | **Màu theo phím** — pitch/track tint | **không** (một màu halo) | tùy chọn, mix thấp |

## 7. Đề xuất D0–D3

### D0 — Sự thật trước (bắt buộc)

1. **Nối CC64 liên tục vào hình** — ✅ **đã xong**: `PedalLevelChanged` gọi `Stage.SetSustainLevel`;
   `PedalChanged` vẫn là edge cho toggle UI. `PianoSimulation.Sustain` và `GpuStageFeed.SetSustainLevel`
   nhận mức đã hiệu chỉnh.
2. **Đường cong cơ học**:
   ```
   raw   = clamp(cc64 / 127, 0, 1)
   lift  = smoothstep(0.25, 0.50, raw)
   ```
   ✅ **đã xong:** `ArDampers.LiftFromPedal`, dùng bởi `PianoStage`, `GpuStageFeed` và test.
   Không đèn dưới 25% hành trình; đầy ở 50% — đúng bằng ngưỡng `DamperCatch` của âm thanh. Khoảng 25–50% là
   vùng half-pedal nhìn thấy được.
3. **Một bộ làm mượt dùng chung** (AR, WPF, GPU):
   ```
   τ_nhấn  = 55 ms   (10–90% ≈ 121 ms)
   τ_nhả   = 75 ms   (10–90% ≈ 165 ms)
   x += (target − x) · (1 − exp(−dt / τ))
   ```
   Nhả chậm hơn nhấn một chút vì bàn đạp có đà; nhưng không có overshoot (felt không nảy).
   ✅ **đã xong:** `ArDampers.Smooth` dùng bởi `PianoStage.Advance`, `GpuStageSimulation.Step` và dải AR; test
   ghim `1 − e⁻¹` ở 55 ms và `e⁻¹` ở 75 ms.
4. **Cùng một giá trị `lift` cho depth và alpha** — ✅ **đã có** (`DepthFor`/`AlphaFor` cùng nhận `lift`, và
   cả hai đường lấy cùng `SustainLevel`).

### D1 — Cho thanh damper thật sự chuyển động

5. **Lõi sáng chạy**: trong shader kind 11, đặt lõi tại
   ```
   corePos = ArDampers.CorePositionFor(lift)   // 1 − 0,55·(0,10·lift)/(0,06 + 0,10·lift)
   core    = exp(−(uv.y − corePos)² · 38) · 2,1 // corePos đi qua Misc.w
   ```
   Ở 45°, lõi đi **8,2 px**; ở 90°, **6,6 px** (0,55 × phần dày thêm). Giữ thêm một **đường tiếp xúc mờ**
   tại `uv.y = 1` (gain 0,25) để dải vẫn dính vào phím. ✅ **đã xong** trong shader kind 11.
6. **WPF/GPU theo cùng quy ước**: vạch WPF dịch lên `6 · lift` DIP; halo GPU dịch lên `5 · lift` DIP. ✅ **đã
   xong**; cả hai chỉ dịch khi Pedal Glow bật, để preset không có pedal glow giữ nguyên hình.

### D2 — Hai lớp trên cùng một thanh ✅ **đã xong**

**Lớp 1 — ô LED theo phím (kiểu SeeMusic).** Mỗi phím có một ô trong dải, sáng theo trạng thái damper của
chính phím đó: phím đang giữ, hoặc cả bàn khi pedal nhấc. Ô rộng **0,70** bề rộng phím trắng (phím đen **0,46**,
tức `0,46/0,52` bề rộng phím đen để giữ đúng tỉ lệ), thân alpha `0,05 + 0,42·v`, dải mép sau 2 px alpha
`0,16 + 0,54·v`; ở `v = 1` là **0,470 / 0,700**. Màu = màu halo mix **22%** về màu pitch. Đây là lớp trả lời
"phím nào đang kêu / đang được giữ".

**Lớp 2 — đầu damper.** Mỗi ô có một **pin** nhô lên `0 → 15 px`, rộng 2,4 px, alpha `0,18 + 0,58·v`, cộng một
**crossbar** 5,4 × 2 px ở đầu pin — đúng hình đầu damper trong prototype.

7. **Cách vẽ trong sản phẩm.** Ô và pin đi cùng `PsArQuad` với kind **12**, không thêm draw call. Ô mang
   `Misc = (rimAlpha, 0, 12, 0)`; shader vẽ thân phẳng và dải 2 px ở `uv.y = 0` (mép sau) bằng `fwidth` —
   đúng 2 px ở mọi tỉ lệ chiếu — rồi composite `rim over body` đúng như prototype vẽ hai hình chữ nhật.
   Pin và crossbar mang `Misc = (0, 1, 12, 0)` để shader bỏ dải mép; chúng là hình chữ nhật màn hình
   2,4 × `15·v` và 5,4 × 2 px đặt tại tâm mép sau của ô.
8. **Trạng thái damper theo phím** (D2):
   ```
   tickTarget[p] = held[p] ? 1 : lift        // ArDampers.TickTarget
   tick += (tickTarget − tick) · (1 − exp(−dt/τ_tick))
   τ_tick = 30 ms lên, 40 ms xuống            // nhanh hơn thanh 55/75 ms
   ```
   Pedal lên: chỉ phím đang giữ sáng — một **lược sáng chạy theo ngón tay**. Pedal xuống: cả bàn sáng. Buông
   pedal: các phím không giữ rơi hết trong ≈ 150 ms; phím còn giữ vẫn sáng. Sostenuto **chưa** nối vào hình
   (mô hình âm thanh đã có trong `PianoSimulation`); khi cần chỉ phải mở rộng `TickTarget`.
9. **Chi phí**: tối đa **265 quad** (88 ô + 88 pin × 2 quad + 1 dải), không thêm draw call. Ở Lite hoặc khi tắt
   `ShowDamperStrip`, ô và pin biến mất cùng dải. `ArObjectQuadCount` trừ `ArDamperCellCount` +
   `ArDamperTickCount`, nên mọi assert "một quad mỗi nốt" giữ nguyên.
10. **Kiểm**: 4 Fact mới trong `tests/PianoPath.Tests/ArDampersTests.cs` (chiều rộng/alpha, `TickTarget`, độ tụ
    sau 10 frame, và so sánh tốc độ với thanh); `VerificationSuite.Ar` đòi mỗi ô có đúng 2 quad pin, và ô/pin biến mất ở Lite và khi tắt
    strip; `tools/check_damper_light.cjs` (**33** assertion) đối chiếu từng hằng số với `Camera/ArDampers.cs`.
11. **Chưa làm**: stage WPF **chưa** có ô theo phím — D2 mới ở lớp AR, đúng phạm vi của dải damper hiện tại.
    Ngân sách GPU thật chưa đo lại trên máy có card (máy này không build được), nên 265 quad mới chỉ được
    kiểm bằng **số lượng**, không phải bằng thời gian khung.

### D3 — Tinh chỉnh ✅ **đã xong**

12. **Bloom ignition.** Ngưỡng bloom của composite là **1,8** (`Post2.z`; giờ đọc từ `ArDampers.BloomThreshold`
    để shader, renderer và test dùng chung một số). Với D1, kênh sáng nhất của lõi chỉ đạt **0,48** khi pedal
    lên và **1,10** khi pedal xuống — bản thân dải **không bao giờ** chạm bloom. D3 nhân riêng lõi với:
    ```
    ignite = 1 + 1,4 · smoothstep(0,35; 0,95; lift)
    ```
    `= 1` khi pedal lên (hình D1 không đổi), `= 2,4` khi pedal xuống hết. Kênh sáng nhất của lõi thành
    **0,48 → 2,57**. Prefilter có knee mềm 0,36 nên bloom bắt đầu từ ngưỡng hiệu dụng **1,44** tại **lift ≈ 0,65**
    và vượt hẳn 1,8 tại **lift ≈ 0,74** — tức **CC64 ≈ 51–53**, khoảng **40–42% hành trình** pedal, ngay dưới
    điểm catch 50%. Ô và pin **không** tăng HDR: chúng là lớp "phím nào kêu", không phải nguồn bloom.
13. **Idle breath.** `Breath(lift, t) = 1 + 0,02 · engage(lift) · sin(2π · 0,7 · t)`, với `engage` chạy từ lift
    0,02 đến 0,27. Pedal lên ⇒ **đúng 1,000** (không hồi quy); pedal giữ ⇒ alpha của dải, ô và pin cùng dao động
    **±2% ở 0,7 Hz** (chu kỳ 1,43 s — không đủ nhanh để thành nháy). `ReducedMotion` của hệ accessibility tắt hẳn
    nhịp thở; thời gian lấy từ `simulation.Clock` nên tất định và **không** chịu `PhysicsTimeFactor`. Nhịp thở chỉ
    ở lớp AR (video overlay); vạch WPF và halo GPU giữ nguyên, vì chúng là ánh sáng của stage chứ không phải của
    ảnh camera.
14. **Màu.** Giữ màu halo cho dải, ô mix 22% về pitch, **pin giữ màu dải** — 88 pin tô màu nốt sẽ thành tiếng ồn
    thị giác, trong khi mắt đã đọc màu qua ô. Không thêm màu mới, không thêm preset.

Kiểm: 2 Fact mới trong `tests/PianoPath.Tests/ArDampersTests.cs` (ignition + quãng cắt bloom; biên độ, chu kỳ và
việc tắt nhịp thở); cùng 9 Fact của D0–D2, `ArDampersTests` giờ có **11 Fact** cho damper (thông điệp commit
`e048a0d` ghi nhầm "six facts" — con số đúng là 11). Chưa đo trên GPU thật (máy này không build được): quãng cắt
bloom là tính từ đúng công thức shader, không phải ảnh chụp.

## 8. Tiêu chí nghiệm thu

| Tiêu chí | Cách đo | Ngưỡng |
|---|---|---|
| Half-pedal nhìn thấy | quét CC64 0..127, đếm số mức `lift` phân biệt | **≥ 10** (sau D0: 34) |
| Vùng chết | CC64 ≤ 32 | `lift = 0` |
| Đầy | CC64 ≥ 64 | `lift = 1` |
| Thời gian nhấn | 10–90% | 121 ± 25 ms |
| Thời gian nhả | 10–90% | 165 ± 30 ms |
| Lệch ba đường | cùng một cử chỉ | ≤ 1 frame |
| Lõi chạy | dịch chuyển tại 45° / 90° | ≥ 8 px / ≥ 6 px, đơn điệu |
| Ô theo phím | giữ phím rồi buông pedal | ô phím giữ vẫn sáng; ô khác tắt ≤ 200 ms |
| Tick theo phím | giữ phím rồi buông pedal | tick phím giữ vẫn cao; phím khác rơi ≤ 200 ms |
| Không hồi quy | pedal lên | dải giống ảnh cũ (sai khác ≤ 1 px / 1 alpha) |
| Bloom thức dậy | kênh sáng nhất của lõi tại lift 0 / 1 so với ngưỡng 1,8 | 0,48 < 1,8 < 2,57; cắt tại lift ≈ 0,74 |
| Nhịp thở | biên độ alpha khi pedal giữ | ±2% ở 0,7 Hz; đúng 1 khi pedal lên; tắt khi Reduced Motion |
| Tất định | chạy lại cùng input | cùng kết quả từng frame |

Hai hàng **ô theo phím** và **tick theo phím** giờ có đường kiểm tự động: `VerificationSuite.Ar` đếm ô/pin khi
sustain xuống và đòi chúng về 0 ở Lite và khi tắt strip; thời gian rơi ≤ 200 ms được ghim bằng τ 40 ms
(10–90% ≈ 88 ms, 95% ≈ 120 ms). Các hàng số học (half-pedal, dead zone, thời gian nhấn/nhả, lõi chạy) đã được
ghim bằng test và checker từ D0/D1.

## 9. Prototype

```bash
# mở trực tiếp
docs/prototypes/particle-motion-lab/damper.html
# hoặc qua server đang chạy
http://localhost:8080/damper.html
```

- Hai panel **Hiện tại** và **Đề xuất**, cùng một timeline cử chỉ pedal + nốt; panel đề xuất vẽ cả ô LED
  theo phím lẫn tick đầu damper.
- Đồ thị bên dưới: CC64 (xám), mức hiện tại (hổ phách, bậc thang), `lift` đề xuất (xanh).
- Thanh trượt CC64 để thử tay; nút "Nhấn nhanh" để thấy pop hiện tại.
- HUD in số: số mức phân biệt trong vùng half-pedal, Δ lớn nhất mỗi frame, thời gian lên/xuống, quãng chạy của lõi.
- Canvas 2D **không mô phỏng bloom composite** và không vẽ nhịp thở: D3 được kiểm bằng số trong test, không bằng
  ảnh trong lab.

Kiểm tra headless (**33** assertion: mô hình, half-pedal, tick, ô LED, khớp hằng số với mã sản phẩm, tất định,
đường vẽ):

```bash
node tools/check_damper_light.cjs
```

## 10. Nguồn & độ tin cậy

| Điều | Nguồn | Tin được |
|---|---|---|
| Hằng số `ArDampers`, alpha/depth, shader kind 11 | mã nguồn repo | **cao** |
| Ba đường vẽ, toggle, mặc định, đường tín hiệu CC64 | mã nguồn repo | **cao** |
| Số px 9,0 → 23,8 / 7,2 → 19,2 | homography dựng lại từ quad trong `ArDampersTests` | **trung bình** (tính toán, không phải ảnh chụp) |
| Hành vi SeeMusic | transcript 2023/2020 + trang Features/Support/forum | **cao cho "không có tài liệu"**, **không đủ để nói app không có** |
| Ảnh marketing SeeMusic (thanh LED theo phím) | 3 ảnh trên trang sản phẩm, không phải tài liệu kỹ thuật | **trung bình** (thấy được, nhưng không có thông số) |
| Đề xuất D0–D3 | thiết kế | — |

Điểm cần nói rõ: mình **không chạy SeeMusic** và không xem được khung hình video. Kết luận "SeeMusic không công
bố thanh damper" là về **tài liệu**, không phải về **sản phẩm**. Nếu có clip cụ thể, mình sẽ phân tích bằng hình.
