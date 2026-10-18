# Thanh damper — phân tích ánh sáng chuyển động và đề xuất

> **Ngày:** 2026-10-19 · **Nhánh:** `arena/efa69378-pianopath` · **Loại:** phân tích + prototype, **không sửa mã sản phẩm**
> **Xem trước:** [damper.html](prototypes/particle-motion-lab/damper.html) — A/B *hiện tại ↔ đề xuất* trên cùng một
> cử chỉ pedal.
> **Liên quan:** [PARTICLE-MOTION-SEEMUSIC-REFERENCE.md](PARTICLE-MOTION-SEEMUSIC-REFERENCE.md) (ngữ pháp hạt),
> [AR-OVERLAY-ROADMAP.md](AR-OVERLAY-ROADMAP.md) §14.3 (phần tử `Dampers`).

## 0. Tóm tắt

1. PianoPath có **ba** cách vẽ cùng một sự kiện "pedal xuống": dải damper AR, vạch sáng mép sau phím trên stage
   WPF, và hào quang (halo) + độ sáng phím trên stage GPU. Ba cách **không cùng đồng hồ**, không cùng hình học,
   và hai trong ba mặc định **tắt**.
2. Dải AR **không có chuyển động dọc theo thanh**. Nó chỉ dày lên 9,0 → 23,8 px (góc 45°, ở 1280×720) và đậm lên
   0,18 → 0,50 alpha. **Lõi sáng bị ghim ở mép phím**, nên mắt thấy "bật sáng", không thấy "nhấc lên".
3. Đường MIDI thật **làm mất half-pedal trước khi tới hình**: CC64 liên tục chỉ đi vào âm thanh; phần hình chỉ
   nhận một `bool` ở ngưỡng 64. Vì vậy `SustainLevel` 0..1 mà `ArDampers` và tài liệu mô tả **không bao giờ có
   giá trị trung gian** trong ứng dụng đang chạy.
4. Ba tốc độ: WPF đổi **trong một frame**; GPU làm mượt **τ = 100 ms**; dải AR đổi **trong một frame**. WPF và
   GPU còn khác nhau về độ rộng vạch (12 → 19,2 px so với hình học cố định).
5. Đề xuất D0–D3: đưa CC64 liên tục vào hình, hiệu chỉnh theo đúng hành trình cơ học (chết 0–25%, đầy ở 50%),
   thống nhất một bộ làm mượt, cho **lõi sáng chạy** theo độ nhấc, và thêm **đèn damper theo từng phím** để thanh
   damper thật sự "động" theo ngón tay chứ không chỉ sáng lên.

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
| Lõi sáng | ghim ở `uv.y = 1` (mép phím); halo loe về phía sau | `StageShaders.hlsl:974-983` |
| Thời gian | **không làm mượt**; theo `SustainLevel` từng frame | `GpuArLayer.cs:134-139` |
| Mặc định | **bật** | `PianoVisualSettings.cs:518` |

Shader: `feather = 1 − uv.y`, `core = exp(−feather²·38)·2,1`, `halo = exp(−feather·5)·0,55`. Điểm sáng nhất luôn
nằm sát mép phím; khi pedal xuống, quad chỉ **dài thêm về phía fallboard**. Vì vậy "chuyển động" của dải là
**độ dày và độ đậm**, không phải một điểm sáng chạy.

### 2.2 Stage WPF

- **Vạch `glowPen`** tại `top + 1`: alpha `(30 + KeyLighting·0,95) · PedalBoost`, độ rộng
  `(5 + BloomSize/10) · PedalBoost` (`PianoStage.cs:3013`). Với mặc định `KeyLighting = 32`, `BloomSize = 70`:
  **12 → 19,2 px**, alpha **60 → 97** khi `PedalBoost` = 1,6.
- **Halo rộng** `DrawImpactLine` tại `keyTop`: dải cao `(6 + BloomSize/5)·6` = **120 px**, alpha
  `(24 + BloomIntensity·0,75) · intensity` = **76 → 121**, cộng spill 60 px xuống mặt phím
  (`PianoStage.cs:2894-2910`).
- `PedalBoost()` = `1 + PedalGlowIntensity/100` khi pedal xuống, **không nội suy** (`PianoStage.cs:1689`).
  Mặc định `PedalGlow = false`, `PedalGlowIntensity = 60` (`PianoVisualSettings.cs:318-319`).
- Felt strip (`DrawFelt`) **không** đổi theo pedal: nó là vật liệu, không phải đèn.

### 2.3 Stage GPU

- `_pedalBoost` chạy về `1 + PedalGlowIntensity` với `1 − exp(−frameDt · 10)` (`GpuStageSimulation.cs:321-322`),
  tức **τ = 100 ms**; `GlowBoost = _pedalBoost · EnergyBoost` (`:170`).
- `GlowBoost` nhân vào halo tại `hitY` (`:1013`) và vào halo từng phím đang kêu (`:1007`).
- Độ sáng phím: `KeyB.y = ShaderEmissive · 1,7 · PedalBoost` (`GpuStageRenderer.cs:751`).
- Hình học halo **không đổi**; felt box không đổi màu (`GpuStageSimulation.cs:785-792`).
- Mặc định `PedalGlow = false` → stage không phản ứng pedal.

### 2.4 Bảng so sánh ba đường

| | Dải AR | WPF stage | GPU stage |
|---|---|---|---|
| Hình học đổi | dày 0,06 → 0,16 key-depth | vạch 12 → 19,2 px; halo 120 px không đổi | không đổi |
| Độ sáng đổi | alpha 0,18 → 0,50 (2,78×) | +60% (alpha 60 → 97, halo 76 → 121) | ×1,6 |
| Làm mượt | **không** | **không** | **τ = 100 ms** |
| Nguồn mức pedal | `SustainLevel` (0/1 trên thực tế) | `bool` | `bool` |
| Mặc định bật | **có** | **không** (`PedalGlow = false`) | **không** |
| Có theo từng phím | không | không (halo theo phím là của nốt) | không |

## 3. Chuyển động đo được

### 3.1 Không gian

Từ calibration 1280×720 trong `ArDampersTests` (homography unit-square → quad), chiếu dải tại `u = 0,5`:

| Pedal lift | Độ dày (45°) | Độ dày (90° overhead) | Alpha |
|---|---|---|---|
| 0,00 | 9,0 px | 7,2 px | 0,18 |
| 0,25 | 12,7 px | 10,2 px | 0,26 |
| 0,50 | 16,4 px | 13,2 px | 0,34 |
| 0,75 | 20,1 px | 16,2 px | 0,42 |
| 1,00 | 23,8 px | 19,2 px | 0,50 |

Tổng dịch chuyển của **mép sau**: 14,8 px (45°) / 12,0 px (90°). Nhưng **lõi sáng không dịch** — nó vẫn ở
mép phím. Mắt nhận ra độ dày tăng chứ không nhận ra một đường sáng đi lên.

### 3.2 Ánh sáng

Alpha tăng **2,78×**; lõi HDR `2,1 + 0,55` nhân với alpha nên cường độ điểm sáng cũng tăng 2,78×, đủ vượt
ngưỡng bloom. Ở 45°, dải rộng 1144 px; ở 90°, 1200 px. WPF tăng **1,6×** (không phải 2,78×), vì `PedalBoost`
bị giới hạn bởi `PedalGlowIntensity`; halo rộng chỉ tăng alpha, không tăng chiều cao.

### 3.3 Thời gian

| Đường | 0 → 100% | Ghi chú |
|---|---|---|
| WPF | **1 frame** (16,7 ms) | `PedalBoost()` đổi tức thì |
| GPU | τ = 100 ms; 63% sau 100 ms, 86% sau 200 ms, 95% sau 300 ms | `1 − exp(−10·dt)` |
| Dải AR | **1 frame** | không nội suy; mức vào là 0/1 |

Nếu pedal được nhấn bằng CC64 nhảy 0 → 127, mép sau dải AR đi 14,8 px trong 16,7 ms = **886 px/s** (45°),
719 px/s (90°). Vạch WPF nở 7,2 px trong 16,7 ms = **431 px/s**. Đây là *pop*, không phải *chuyển động*.

### 3.4 Dọc theo thanh

Hiện tại: **không có**. Dải phủ `u` 0..1 với cùng độ dày/alpha ở mọi phím; **trên thanh** không có đèn theo
phím, không có sóng chạy, không có nhịp thở khi pedal giữ. Ba đường đều là "một thanh, một độ sáng".

## 4. Lỗ hổng half-pedal

Đường tín hiệu thật:

```
MIDI CC64 (0..127)
  ├─ PedalLevelChanged ──► _audio.ControlChange(64, value)      (âm thanh, liên tục)
  └─ PedalChanged (>=64) ─► SetPedalState(bool)                 (hình, nhị phân)
                              └─ Stage.SetSustainPedal(bool)
                                   ├─ _simulation.SetSustain(down ? 1 : 0)
                                   └─ _gpu.SetSustain(down)     → _sustainLevel ∈ {0,1}
```

`Ui/MainWindow.xaml.cs:129-130`; `Stage/PianoStage.cs:256-262`; `Gpu/GpuStageFeed.cs:113-117`.

Hệ quả:

- `ArDampers.ClampLevel` và `GpuStageFeed.SetSustainLevel` **hỗ trợ** 0..1, test cũng kiểm tra, nhưng **không có
  caller nào** truyền giá trị trung gian trong ứng dụng thật.
- Ngưỡng hình là **64/127 = 0,504**; mô hình âm thanh có vùng tự do `< 0,25` và catch `>= 0,5`
  (`SfzSynthesizer.cs:31-32,152-159`). Trong khoảng CC64 **0,25–0,50**, âm thanh đã ngân một phần nhưng đèn
  damper **vẫn tắt hoàn toàn**.
- UI ghi "level 0..1" (`MainWindow.Settings.cs:629`) và "half-pedalling shows" (`GpuStageFeed.cs:25`), trong khi
  hình chỉ có hai trạng thái. Đây là **lỗi hợp đồng**, cùng loại với lỗi `PhysicsTimeFactor` đã ghi ở
  [PARTICLE-MOTION-REVIEW.md](PARTICLE-MOTION-REVIEW.md).

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
- PianoPath có thứ ngược lại: **một** thanh damper đơn sắc, **nhấc theo pedal**, không chia theo phím.
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
| 2 | **Core travel** — lõi sáng đi lên | **không** (ghim ở mép phím) | có (D1) |
| 3 | **Brightness swell** — sáng lên | có, nhưng bật/tắt | có, có làm mượt |
| 4 | **Width swell** — vạch WPF rộng ra | có (12 → 19,2 px) | giữ, cùng thời gian với AR |
| 5 | **Release fall** — buông pedal | tức thì | rơi nhanh hơn lúc nhấn (D0) |
| 6 | **Per-key cells** — 88 ô sáng theo phím (kiểu SeeMusic) | **không** | có (D2) |
| 7 | **Damper heads** — tick nhô lên theo từng phím | **không** | có (D2, tùy chọn) |
| 8 | **Sweep along bar** — sóng chạy ngang | **không** | tùy chọn, không ưu tiên |
| 9 | **Idle breath** — thở khi giữ pedal | **không** | tùy chọn, rất nhẹ |
| 10 | **Màu theo phím** — pitch/track tint | **không** (một màu halo) | tùy chọn, mix thấp |

## 7. Đề xuất D0–D3

### D0 — Sự thật trước (bắt buộc)

1. **Nối CC64 liên tục vào hình**: `PedalLevelChanged` cập nhật `PianoSimulation.Sustain` và
   `GpuStageFeed.SetSustainLevel`; `PedalChanged` chỉ còn là edge cho toggle UI.
2. **Đường cong cơ học**:
   ```
   raw   = clamp(cc64 / 127, 0, 1)
   lift  = smoothstep(0.25, 0.50, raw)
   ```
   Không đèn dưới 25% hành trình; đầy ở 50% — đúng bằng ngưỡng `DamperCatch` của âm thanh. Khoảng 25–50% là
   vùng half-pedal nhìn thấy được.
3. **Một bộ làm mượt dùng chung** (AR, WPF, GPU):
   ```
   τ_nhấn  = 55 ms   (10–90% ≈ 121 ms)
   τ_nhả   = 75 ms   (10–90% ≈ 165 ms)
   x += (target − x) · (1 − exp(−dt / τ))
   ```
   Nhả chậm hơn nhấn một chút vì bàn đạp có đà; nhưng không có overshoot (felt không nảy).
4. **Cùng một giá trị `lift` cho depth và alpha**, thay vì mỗi nơi tự tính.

### D1 — Cho thanh damper thật sự chuyển động

5. **Lõi sáng chạy**: trong shader kind 11, đặt lõi tại
   ```
   corePos = 1 − 0,55 · lift        // uv.y: 1 = mép phím, 0 = phía fallboard
   core    = exp(−(uv.y − corePos)² · 38) · 2,1
   ```
   Ở 45°, lõi đi **8,1 px**; ở 90°, **6,6 px**. Giữ thêm một **đường tiếp xúc mờ** tại `uv.y = 1` (gain ~0,25)
   để dải vẫn dính vào phím.
6. **WPF/GPU theo cùng quy ước**: vạch WPF dịch lên `6 · lift` DIP; halo GPU dịch lên `5 · lift` DIP. Đây là
   thay đổi nhỏ nhưng làm ba đường đọc giống nhau.

### D2 — Hai lớp trên cùng một thanh

**Lớp 1 — ô LED theo phím (kiểu SeeMusic).** Chia dải thành 88 ô, mỗi ô sáng theo trạng thái damper của phím
đó; bloom của sản phẩm sẽ làm các ô liền lại thành thanh, đúng như ảnh SeeMusic. Ô rộng 0,70 bề rộng phím
trắng (phím đen 0,46), thân alpha `0,05 + 0,42·v`, cạnh trên 2 px alpha `0,16 + 0,54·v`; ở `v = 1` đo được
**0,470 / 0,700**. Màu = màu halo mix **22%** về màu pitch. Đây là lớp trả lời "phím nào đang kêu / đang được
giữ".

**Lớp 2 — đầu damper (tùy chọn).** Một **tick** nhô lên 0 → 15 px cho mỗi phím, đọc như đầu damper thật:

7. **Trạng thái damper của mỗi phím** đã có sẵn trong mô hình: damper rời dây khi
   `held || sostenutoCaught || sustain > ngưỡng`. Tick:
   ```
   tickTarget[p] = max(held[p] ? 1 : 0, caught[p] ? 1 : 0, lift)
   tick += (tickTarget − tick) · (1 − exp(−dt / τ_tick))
   τ_tick = 30 ms lên, 40 ms xuống
   ```
   - Pedal lên: chỉ phím đang giữ sáng — một **lược sáng chạy theo ngón tay**.
   - Pedal xuống: cả 88 phím sáng.
   - Buông pedal: tất cả rơi trong ~150 ms, trừ phím còn giữ (đúng vật lý).
8. **Chi phí**: 88 quad (ô LED) + 88 quad (tick) thêm vào cùng `PsArQuad`, không thêm draw call; ở Lite vẫn bị
   cắt cùng `Dampers`. WPF: 88 `DrawLine` + 88 rect mỗi frame, có thể gộp thành một `StreamGeometry`.

### D3 — Tinh chỉnh (tùy chọn)

9. **Bloom threshold**: giữ lõi pedal-up dưới ngưỡng bloom, pedal-down vượt ngưỡng — ánh sáng "sống dậy" mềm
   thay vì pop.
10. **Idle breath** rất nhẹ (±2% alpha, 0,7 Hz) chỉ khi pedal giữ; tắt được.
11. **Màu**: giữ màu halo; tick có thể lấy màu nốt ở cường độ thấp để không rối.

## 8. Tiêu chí nghiệm thu

| Tiêu chí | Cách đo | Ngưỡng |
|---|---|---|
| Half-pedal nhìn thấy | quét CC64 0..127, đếm số mức `lift` phân biệt | **≥ 10** (hiện tại: 2) |
| Vùng chết | CC64 ≤ 32 | `lift = 0` |
| Đầy | CC64 ≥ 64 | `lift = 1` |
| Thời gian nhấn | 10–90% | 121 ± 25 ms |
| Thời gian nhả | 10–90% | 165 ± 30 ms |
| Lệch ba đường | cùng một cử chỉ | ≤ 1 frame |
| Lõi chạy | dịch chuyển tại 45° / 90° | ≥ 8 px / ≥ 6 px, đơn điệu |
| Ô theo phím | giữ phím rồi buông pedal | ô phím giữ vẫn sáng; ô khác tắt ≤ 200 ms |
| Tick theo phím | giữ phím rồi buông pedal | tick phím giữ vẫn cao; phím khác rơi ≤ 200 ms |
| Không hồi quy | pedal lên | dải giống ảnh cũ (sai khác ≤ 1 px / 1 alpha) |
| Tất định | chạy lại cùng input | cùng kết quả từng frame |

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

Kiểm tra headless (**28** assertion: mô hình, half-pedal, tick, ô LED, tất định, đường vẽ):

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
