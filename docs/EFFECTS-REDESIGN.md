# Thiết kế lại hệ thống Effects (v1: Impact phase hoàn thiện)

Tài liệu này là bản thiết kế chính thức cho toàn bộ hệ thống hiệu ứng của Keyflow:
tổng hợp 87 effects theo yêu cầu, sắp xếp theo kiến trúc logic duy nhất, và roadmap
triển khai từng phase — **làm đến đâu hoàn thiện đến đó** (settings + slider + renderer
+ preset + kiểm thử), không hiện UI cho effect chưa có logic thật.

Bảng tổng hợp trong code nằm ở `Stage/Effects/EffectCatalog.cs` (nguồn duy nhất —
single source of truth); tài liệu này giải thích *vì sao* kiến trúc lại như vậy và
*làm gì tiếp theo*.

---

## 1. Vì sao phải thiết kế lại?

87 effects không thể là 87 toggle rời rạc: UI sẽ loạn, renderer sẽ rối, và người dùng
không bao giờ học hết. Quan sát vòng đời thật của một nốt nhạc cho thấy mọi effect
đều thuộc đúng một trong 4 nhóm vai trò — đó là chìa khóa của thiết kế này.

## 2. Kiến trúc logic: 4 nguyên tắc

```
                        ┌──────────────────────────────────────┐
                        │  SMART MODULATORS (9)                │
                        │  velocity / pitch / octave / zone /  │
                        │  pedal / tempo / audio  →  chỉ NHÂN  │
                        │  tham số (size, màu, cường độ)       │
                        └───────────────┬──────────────────────┘
                                        │ modulate (không vẽ)
        ┌───────────────────────────────┼───────────────────────────────┐
        │ NOTE PIPELINE (40)            │                               │ AMBIENT (31)
        │                               │                               │
        │  Falling (10) → Impact (16)   │                               │  Particle & Energy (9)
        │  → Hold (8) → Release (6)     │                               │  Nature (9) · Light (7)
        │  mỗi nốt đi qua đúng 4 phase  │                               │  Cosmic (6) · toàn sân khấu
        └───────────────────────────────┴───────────────────────────────┘
                                        │
                        ┌───────────────┴──────────────────────┐
                        │  COMBO THEMES (7) = preset graphs    │
                        │  Fire / Ice / Galaxy / Sakura /      │
                        │  Electric / Ocean / Retro            │
                        └──────────────────────────────────────┘
```

1. **Một nốt, 4 phase sống.** Nốt sinh ra (Falling), chạm phím (Impact), được giữ
   (Hold), rồi chết (Release). Effect của nốt thuộc đúng 1 phase → UI có đúng 1 card
   mỗi phase thay vì 40 toggle rời.
2. **Ambient là lớp toàn sân khấu.** 4 họ Particle & Energy, Nature, Light & Color,
   Cosmic không bám theo nốt; mỗi họ là 1 khe layer độc lập (bật/tắt riêng).
3. **Smart effects là modulators, không phải renderer.** Velocity, cao độ, quãng 8,
   vùng phím, pedal, tempo, FFT chỉ biến đổi tham số của effect khác — không vẽ gì.
4. **Theme là preset graph.** Fire/Ice/Galaxy… không phải code mới; mỗi theme là một
   bộ cố định (falling, impact, hold, release, ambient, modulators).

### 2.1 Chuẩn tham số: 4 slider dùng chung

Mọi rendered effect đều chỉnh bằng đúng 4 slider quen thuộc (học 1 lần, dùng mọi nơi):

| Slider | Ý nghĩa | Miền |
|---|---|---|
| Intensity | Độ mạnh / độ sáng / alpha chung | 0–100 (wave đến 150) |
| Size | Bán kính, độ rộng, độ lan | 0–100 |
| Speed | Tốc độ animation (nghịch với lifetime) | 0–100 |
| Amount | Số lượng particle (chỉ effect dạng hạt) | 0–100+ |

Chỉ thêm màu (Color) hoặc lựa chọn (Choice) khi effect thật sự cần. Mọi slider đều có
ô nhập số + nút reset theo chuẩn dock hiện có, và đều được `Clamp()` + tìm kiếm.

### 2.2 Kênh renderer (channels)

Thay vì mỗi effect một đường vẽ riêng, renderer tổ chức theo **kênh** — nhiều effect
dùng chung một kênh với tham số khác nhau:

| Kênh | Vai trò | Ví dụ effect |
|---|---|---|
| `falling.trail` | Vệt kéo sau nốt | Glow Trail, Sparkle Tail, Speed Lines |
| `falling.body` | Thân nốt | Motion Blur, Pulsing, Rainbow Shift |
| `impact.burst` | Hạt nổ tại điểm chạm | Burst, Ember Burst, Confetti, Firework, Splash |
| `impact.wave` | Sóng lan từ phím | Ripple Ring, Shockwave, Echo Rings, Water Ripple |
| `impact.flash` | Chớp sáng | Flash, Lightning Strike, Plasma |
| `impact.morph` | Nốt biến hình | Shatter, Bounce, Absorb, Melt, Note Morph |
| `hold.column` | Cột dựng từ phím giữ | Flame Pillar, Energy Column, Sustain Particles |
| `hold.glow` | Phím phát sáng khi giữ | Breathing Glow, Color Cycle, Key Press Glow |
| `hold.link` | Nối các phím giữ | Electric Arc |
| `release` | Hậu kỳ khi thả | Fade Out, Float Up, Dissolve, Smoke Puff, Snap Back |
| `ambient.*` | 4 lớp nền độc lập | Stars, Petals, Aurora, Galaxy… |
| `mod.*` | Modulators | velocity/pitch/zone/tempo/audio/pedal |
| `theme` | Preset graph | 7 combo themes |

## 3. Tổng hợp toàn bộ 87 effects

Trạng thái: ✅ = đã hoàn thiện (settings + renderer + kiểm thử), 🔜 = đã chốt kênh,
làm theo roadmap. Chi tiết trong `EffectCatalog.cs`.

### 3.1 Falling — nốt đang rơi (10)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Glow Trail | Vệt sáng mờ | falling.trail | ✅ v2 |
| Motion Blur | Mờ chuyển động | falling.body | ✅ v2 |
| Sparkle Tail | Đuôi tia sáng | falling.trail | ✅ v2 |
| Color Gradient | Chuyển sắc đầu–đuôi | falling.body | ✅ có sẵn |
| Pulsing | Nhấp nháy | falling.body | ✅ v2 |
| Ribbon Twist | Dải lụa xoắn | falling.body | ✅ v2 |
| Particle Stream | Dòng hạt | falling.trail | ✅ v2 |
| Ghost Notes | Bóng ma | falling.trail | ✅ v2 |
| Speed Lines | Vệt tốc độ | falling.trail | ✅ v2 |
| Rainbow Shift | Cầu vồng trượt | falling.body | ✅ v2 |

### 3.2 Impact — nốt chạm phím (16) · ✅ v1 HOÀN THIỆN kênh burst/wave/flash

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Burst / Explosion | Nổ hạt | impact.burst | ✅ sparks engine |
| Ember Burst | Than hồng bắn | impact.burst | ✅ blackbody cooling |
| Ripple Ring | Vòng sóng | impact.wave | ✅ `ImpactWave=Ring` |
| Shockwave | Sóng xung kích | impact.wave | ✅ `ImpactWave=Shockwave` (mới) |
| Flash | Chớp sáng | impact.flash | ✅ `ShowImpactFlash` (mới) |
| Key Press Glow | Phím rực sáng | hold.glow | ✅ KeyLighting |
| Splash | Bắn tung tóe | impact.burst | ✅ v2 |
| Firework | Pháo hoa mini | impact.burst | ✅ v2 |
| Confetti Pop | Giấy màu | impact.burst | ✅ v2 |
| Dust Cloud | Đám bụi | impact.burst | ✅ v2 |
| Shatter / Break | Vỡ kính | impact.morph | ✅ v2 |
| Bounce | Nảy lên | impact.morph | ✅ v2 |
| Absorb | Phím hút nốt | impact.morph | ✅ v2 |
| Melt | Tan chảy | impact.morph | ✅ v2 |
| Lightning Strike | Sét đánh | impact.flash | ✅ v2 |
| Note Morph | Biến hình | impact.morph | ✅ v2 |

### 3.3 Hold — giữ nốt (8)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Flame Pillar | Cột lửa | hold.column | ✅ flames |
| Sustain Particles | Hạt bay liên tục | hold.column | ✅ wisps |
| Energy Column | Cột năng lượng | hold.column | ✅ light beams |
| Hold Bar | Thanh giữ dài | hold.bar | ✅ v3 |
| Breathing Glow | Phím thở sáng | hold.glow | ✅ v3 |
| Vibration | Rung nhẹ | hold.glow | ✅ v3 |
| Color Cycle | Đổi màu liên tục | hold.glow | ✅ v3 |
| Electric Arc | Tia điện nối phím | hold.link | ✅ v3 |

### 3.4 Release — thả nốt (6)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Fade Out | Mờ dần | release | ✅ trails |
| Float Up | Bay lên | release | ✅ v4 |
| Dissolve | Tan thành hạt | release | ✅ v4 |
| Smoke Puff | Puff khói | release | ✅ v4 |
| Snap Back | Co rút | release | ✅ v4 |
| Echo Rings | Vòng sóng dội | release | ✅ v4 |

### 3.5 Ambient — Particle & Energy (9)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Embers | Than hồng bay | impact.burst | ✅ |
| Fire / Flames | Ngọn lửa | hold.column | ✅ |
| Explosion / Burst | Vụ nổ hạt | impact.burst | ✅ |
| Sparkles / Stars | Tia sáng lấp lánh | ambient.light | ✅ star field |
| Lightning / Electric | Tia sét | ambient.particles | ✅ v5 |
| Laser Beams | Tia laser | hold.column | ✅ v5 |
| Plasma | Quả cầu plasma | impact.flash | ✅ v2 (flash style) |
| Confetti | Giấy màu | ambient.particles | ✅ v5 |
| Firework | Pháo hoa | ambient.particles | ✅ v5 |

### 3.6 Ambient — Nature & Elements (9)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Petals / Cherry Blossom | Cánh hoa anh đào | ambient.nature | ✅ motes |
| Water Ripple | Gợn sóng nước | impact.wave | ✅ v5 |
| Rain / Droplets | Mưa | ambient.nature | ✅ v5 |
| Snow / Ice | Tuyết / Băng | ambient.nature | ✅ v5 |
| Smoke / Fog | Khói / Sương | ambient.nature | ✅ v5 |
| Wind / Leaves | Lá bay | ambient.nature | ✅ v5 |
| Butterflies | Đàn bướm | impact.burst | ✅ v5 |
| Aurora | Cực quang | ambient.light | ✅ v5 |
| Dust Cloud | Mây bụi | ambient.nature | ✅ v5 |

### 3.7 Ambient — Light & Color (7)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Glow / Neon | Neon phát sáng | falling.body | ✅ |
| Bloom | Hào quang | ambient.light | ✅ |
| Flash | Chớp trắng | impact.flash | ✅ |
| Color Splash / Paint | Bắn màu sơn | impact.burst | ✅ v5 |
| Rainbow Trail | Dải cầu vồng | falling.trail | ✅ v2 (trail style) |
| Gradient Wave | Sóng gradient | ambient.light | ✅ v5 |
| Prism / Crystal | Lăng kính | ambient.light | ✅ v5 |

### 3.8 Ambient — Cosmic & Abstract (6)

| Effect | Tiếng Việt | Kênh | Trạng thái |
|---|---|---|---|
| Galaxy / Nebula | Tinh vân | ambient.cosmic | ✅ v5 |
| Black Hole | Hố đen | ambient.cosmic | ✅ v5 |
| Matrix Rain | Mưa ký tự | ambient.cosmic | ✅ v5 |
| Geometric Shapes | Hình khối | ambient.cosmic | ✅ v5 |
| Fractal | Hoa văn fractal | ambient.cosmic | ✅ v5 |
| Ribbon / Trail | Dải lụa | falling.trail | ✅ v2 (trail style) |

### 3.9 Smart modulators (9) — chỉ nhân tham số, không vẽ

| Effect | Tiếng Việt | Điều chế | Trạng thái |
|---|---|---|---|
| Velocity Size | Lực → Kích thước | burst amount, wave/flash size | ✅ |
| Velocity Mapping | Lực → Cường độ | burst/wave/flash brightness | ✅ |
| Key Color Mapping | Mỗi nốt một màu | NoteColor | ✅ ColorMode |
| Velocity Color | Lực → Màu (nhẹ=xanh, mạnh=đỏ) | note + burst color | ✅ v6 |
| Octave Color | Mỗi quãng 8 một màu | NoteColor | ✅ v6 |
| Pedal Glow | Phím sáng theo pedal | hold.glow khi sustain | ✅ v6 |
| Tempo Sync | Nháy theo nhịp BPM | pulse theo beat clock | ✅ v6 |
| Audio Reactive | Nhảy theo FFT | spectrum → intensity | ✅ v6 |
| Zone Split FX | Bass=lửa, Treble=băng | effect theo vùng phím | ✅ v6 |

### 3.10 Combo themes (7) — preset graphs

| Theme | Preset | Falling → Impact → Hold → Release | Trạng thái |
|---|---|---|---|
| Fire (Lửa) | Inferno | Vệt glow → ember + shockwave + bounce → cột lửa + hold bar → khói (+ pháo hoa) | ✅ v7 |
| Ice (Băng) | Ice Crystal | Vệt sparkle → splash + ripple + absorb → rung nhẹ → tan hạt (+ tuyết) | ✅ v7 |
| Galaxy (Vũ trụ) | Galaxy Voyage | Vệt rainbow → firework + shockwave + plasma → thở → tan hạt (+ thiên hà) | ✅ v7 |
| Sakura (Hoa) | Sakura Nocturne | Vệt ribbon → confetti cánh hoa + flash → thở → bay lên (+ bướm) | ✅ v7 |
| Electric (Điện) | Electric Storm | Vệt speed lines → ember + shockwave + sét → arc điện → snap (+ bão sét) | ✅ v7 |
| Ocean (Đại dương) | Ocean Depths | Vệt glow → splash + ripple → cột nước → bay lên (+ mưa) | ✅ v7 |
| Retro / 8-bit | Retro Arcade | Ghost + pulse → confetti + ring + bounce → — → snap (+ hình học) | ✅ v7 |

## 4. Mapping legacy → kiến trúc mới

Không đập bỏ: engine hiện có khớp hoàn toàn vào các kênh mới, chứng tỏ thiết kế đúng.

| Cũ (v0) | Kênh mới | Ghi chú |
|---|---|---|
| `ShowEmbers` + sparks physics | `impact.burst` | Giữ nguyên toàn bộ physics |
| `ShowImpactRings` + `RingSize` | `impact.wave` | + `ImpactWave` (Ring/Shockwave/None), + `ImpactWaveIntensity` |
| (mới) | `impact.flash` | `ShowImpactFlash` + `ImpactFlashIntensity` |
| `ShowFlame` + flames | `hold.column` | = Flame Pillar |
| `ShowWisps` + wisps | `hold.column` | = Sustain Particles |
| `ShowLightBeams` | `hold.column` | = Energy Column |
| Stars / Petals-motes | `ambient.light` / `ambient.nature` | Giữ nguyên |
| `ShowSpotlights` / `SpotlightIntensity` | — | **ĐÃ XÓA** toàn bộ (model, renderer, UI, preset, test, docs) |
| (mới v2) `FallingTrail` + Intensity/Length | `falling.trail` | 7 kiểu vệt: Glow/Sparkles/Speed Lines/Blur/Ribbon/Rainbow/Stream |
| (mới v2) `FallingPulse` / `FallingGhost` | `falling.body` / `falling.trail` | Pulsing + Ghost Notes cho nốt đang bay |
| (mới v2) `ImpactBurst` | `impact.burst` | Embers/Splash/Fireworks/Confetti/Dust |
| (mới v2) `ImpactMorph` | `impact.morph` | Shatter/Melt/Absorb/Bounce/Star Morph |
| (mới v2) `ImpactFlashStyle` | `impact.flash` | Flash/Lightning/Plasma |
| (mới v3) `HoldBar` + Intensity | `hold.bar` | Thanh đang kêu rực sáng + viền nóng khi giữ |
| (mới v3) `HoldBreath` + Rate | `hold.glow` | Phím + nốt "thở" (chỉ scale bán kính/glow, không phá cache brush) |
| (mới v3) `HoldVibration` / `HoldColorCycle` | `hold.glow` | Rung nhẹ + xoay màu nốt đang giữ |
| (mới v3) `HoldElectricArc` + Intensity | `hold.link` | Tia điện nối tối đa 6 cặp phím giữ |
| (mới v4) `ReleaseEffect` + Intensity | `release` | Fade/Float Up/Dissolve/Smoke/Snap Back/Echo Rings cho live + MIDI note-end |
| (mới v5) `AmbientEnergy` + Amount/Speed | `ambient.particles` | Storm/Lasers/Confetti Rain/Fireworks (procedural, không particle list) |
| (mới v5) `AmbientNature` + Amount/Speed | `ambient.nature` | Rain/Snow/Smoke/Leaves/Butterflies/Dust/Aurora |
| (mới v5) `AmbientLight` + Amount/Speed/Color | `ambient.light` | Gradient Wave/Prism/Color Splash |
| (mới v5) `AmbientCosmic` + Amount/Speed | `ambient.cosmic` | Galaxy/Black Hole/Matrix/Geometric/Fractal |
| (mới v5) `ImpactWave` += Ripple | `impact.wave` | Gợn sóng nước từ điểm chạm |
| (mới v6) `VelocityColor`/`OctaveColor`/`ZoneSplit` | `mod.*` | Lực→màu, quãng 8→màu, bass=lửa/treble=băng (+ tint nốt) |
| (mới v6) `PedalGlow`/`TempoSync`/`AudioReactive` | `mod.*` | Pedal thật, beat thật từ tempo map, envelope năng lượng |
| (mới v6) nối dây host | — | MIDI/live velocity thật → `Impact`; pedal → `SetSustainPedal`; beat → `PulseBeat` |
| (mới v7) 7 combo themes | `theme` | Inferno/Ice Crystal/Sakura Nocturne nâng cấp + Galaxy Voyage/Electric Storm/Ocean Depths/Retro Arcade mới |
| `ParticleResponse` / strength | `mod.velocity` | Mở rộng sang wave/flash (size + brightness theo lực nhấn) |

File JSON/preset cũ có key `ShowSpotlights` vẫn đọc được (parser bỏ qua key lạ).

## 5. Các phase đã hoàn thiện

### 5.1 Phase 1 (v1) — Impact

- **Settings** (`PianoVisualSettings`): `ImpactWave` (None/Ring/Shockwave),
  `ImpactWaveIntensity` (0–150), `ShowImpactFlash`, `ImpactFlashIntensity` (0–100);
  tất cả `Clamp()`, round-trip JSON, preset-safe. Mặc định giữ nguyên look cũ
  (Ring 100, Flash tắt) nên preset hiện có không đổi một pixel.
- **Renderer** (`PianoStage`): `Impact()` điều phối 3 kênh burst/wave/flash;
  `DrawShockwave` (lõi nóng + viền sáng), `DrawImpactFlashes` (flare ~180 ms);
  size/brightness theo lực nhấn; trần 64 waves + 32 flashes; `HasActiveEffects`
  và `ClearTransient` bao đủ.
- **UI** (trang Particles): card mới **IMPACT · WAVE & FLASH** — toggle + Choice +
  3 slider, hàng phụ thuộc `VisibleWhen`, tìm kiếm được ("shockwave", "flash").
- **Catalog**: `Stage/Effects/EffectCatalog.cs` — 87 effects, trạng thái
  Available/Planned, kênh renderer cho từng effect.
- **Kiểm thử**: `VerifyImpactFx` — UI phơi đúng control, catalog đủ 6 impact
  Available, hit sinh wave+flash, vẽ geometry thật, tắt dần đúng hạn.
- **Docs**: README + `SETTINGS-WIRING-AUDIT.md` (mục 11) + `UI-SHADER-REVIEW.md`.

### 5.2 Phase 2 (v2) — Falling + Impact còn lại

- **Falling**: `FallingTrail` 7 kiểu (Glow/Sparkles/Speed Lines/Blur/Ribbon/Rainbow/Stream)
  + Intensity/Length; `FallingPulse` + Rate (nhấp nháy khi bay); `FallingGhost` + Amount
  (bóng ma dẫn đường); Rainbow cũng xoay màu thân nốt (Rainbow Shift).
- **Burst**: `ImpactBurst` 5 kiểu — Embers (giữ nguyên physics cũ), Splash (giọt nước),
  Fireworks (vỏ pháo hoa rực rỡ), Confetti (giấy màu tung bay), Dust (mây bụi) — mỗi kiểu
  có trọng lực/ma sát riêng (`Spark.Grav`/`DragK`) và cách vẽ riêng.
- **Morph**: `ImpactMorph` 5 kiểu + Intensity — Shatter (mảnh kính), Melt (giọt sáp),
  Absorb (vòng sóng co vào phím), Bounce (tia nảy + vòng kick), Morph (sao 5 cánh).
- **Flash**: `ImpactFlashStyle` — Flash (cũ), Lightning (sét đánh từ đỉnh sân khấu),
  Plasma (quả cầu năng lượng + tia lửa).
- **UI**: card FALLING FX (trang Notes); Burst style (SPARKS · EMITTER); Note morph +
  Flash style (card IMPACT). **Kiểm thử**: `VerifyFallingFx` + sửa bug `Advance(1.0)`
  ở test v1 (mỗi step bị clamp 50 ms nên phải lặp frame-size steps).

### 5.3 Phase 3 (v3) — Hold

- **Settings**: `HoldBar` + Intensity, `HoldBreath` + Rate, `HoldVibration` + Amount,
  `HoldColorCycle` + Speed, `HoldElectricArc` + Intensity — tất cả mặc định tắt nên
  look hiện có không đổi.
- **Renderer**: nốt đang kêu rung/xoay màu/viền nóng trong `DrawConfiguredNote`;
  `BreathFactor()` điều nhịp glow phím + flare halo + glow nốt; `DrawElectricArcs`
  nối các phím giữ bằng tia sét động (tối đa 6 cặp, không cần particle list mới).
- **UI**: card HOLD FX (trang Notes). **Kiểm thử**: `VerifyHoldFx`.

### 5.4 Phase 4 (v4) — Release

- **Settings**: `ReleaseEffect` 6 kiểu + Intensity, mặc định Fade (không đổi look cũ).
- **Renderer**: `ReleaseLiveNote` phát hiệu ứng tại phím; `ScanReleaseFx` trong `Advance`
  bắt MIDI note-end vừa qua playhead (con trỏ đơn điệu + chặn seek ngược để không bung
  release cũ; trần 24 release/frame). Tái dùng sparks/rings sẵn có, không list mới.
- **UI**: card RELEASE FX (trang Notes). **Kiểm thử**: `VerifyReleaseFx`.

### 5.5 Phase 5 (v5) — Ambient

- **Settings**: 4 khe độc lập `AmbientEnergy`/`AmbientNature`/`AmbientLight`/`AmbientCosmic`
  (mỗi khe Choice + Amount + Speed; Light thêm Color), mặc định None. `ImpactWaves` thêm Ripple.
- **Renderer**: 4 pass procedural sau background, trước notes — dùng `SeededRandom` + `_elapsed`
  nên không cần list mới, không tốn bộ nhớ; `DrawLightning` tách lõi `DrawBolt` dùng chung
  với bão sét. `HasActiveEffects` bao cả 4 khe.
- **UI**: card AMBIENT LAYERS (trang Background); Wave style thêm Ripple.
- **Kiểm thử**: `VerifyAmbientFx` (4 pass vẽ geometry toàn sân khấu + ripple spawn/decay).

### 5.6 Phase 6 (v6) — Smart modulators

- **Settings**: `VelocityColor` + Amount, `OctaveColor` + Blend, `ZoneSplit` + Pitch + Amount,
  `PedalGlow` + Intensity, `TempoSync` + Amount, `AudioReactive` + Amount — đều mặc định tắt.
- **Renderer**: `NoteColor` bọc thêm octave/zone (`NoteColorCore` giữ logic cũ); velocity tint
  cho nốt MIDI/live/burst/wave/flash; zone ép kiểu burst theo vùng phím; `BeatBoost`/
  `EnergyBoost`/`PedalBoost` nhân vào glow nốt + halo + phím sáng.
- **Nối dây thật**: velocity MIDI/live vào `Impact` (thay hằng số .82/.75); pedal sustain
  vào `SetSustainPedal`; beat từ tempo map vào `PulseBeat` (kể cả khi tắt metronome).
  Audio Reactive v1 = envelope năng lượng từ note onset (FFT để tương lai).
- **UI**: card SMART MODULATORS (trang Notes). **Kiểm thử**: `VerifySmartFx`.

### 5.7 Phase 7 (v7) — Combo themes

- **Preset graphs**: 3 preset cũ thành theme (Inferno=Fire, Ice Crystal=Ice,
  Sakura Nocturne=Sakura + bật petals) và 4 preset mới (Galaxy Voyage, Electric Storm,
  Ocean Depths, Retro Arcade) — mỗi preset cố định toàn bộ falling/impact/hold/release/
  ambient/modulators theo đúng bảng §3.10.
- **Không code renderer mới**: themes chứng minh kiến trúc đúng — mọi theme chỉ là tổ hợp
  settings. **Kiểm thử**: `VerifyThemes` assert từng graph.

## 6. Roadmap các phase tiếp theo

| Phase | Scope | Settings mới (dự kiến) | Renderer |
|---|---|---|---|
| ✅ **2 · Falling** | Glow Trail, Sparkle Tail, Speed Lines, Pulsing, Motion Blur (+ impact.morph còn lại: Shatter, Melt…) | `FallingTrail` (Choice) + Intensity/Length; `FallingPulse` + rate | Vệt sau nốt trong `DrawConfiguredNote`; morph khi impact |
| ✅ **3 · Hold** | Hold Bar, Breathing Glow, Color Cycle, Vibration, Electric Arc | `HoldGlow` (Choice) + rate; `HoldBar` toggle; `ElectricArc` toggle | Nhịp thở theo `_elapsed`; arc nối phím trong `_activeKey` |
| ✅ **4 · Release** | Float Up, Dissolve, Smoke Puff, Snap Back, Echo Rings | `ReleaseEffect` (Choice) + Intensity | Hàng đợi release khi `ReleaseLiveNote`/note-end |
| ✅ **5 · Ambient** | 4 khe layer: Energy / Nature / Light / Cosmic | Mỗi khe: Choice + Amount + Speed (+ Color) | Các lớp độc lập sau background, trước notes |
| ✅ **6 · Smart UI** | Velocity Color, Octave Color, Pedal Glow, Zone Split, Tempo Sync, Audio Reactive | Toggle + Amount từng modulator | Móc vào `NoteColor`, `Impact(strength)`, beat clock, FFT |
| ✅ **7 · Themes** | 7 combo themes thành preset có sẵn | (không thêm setting — chỉ preset) | `VisualPresets`: Fire/Ice/Galaxy/Sakura/Electric/Ocean/Retro |

Thứ tự này là logic nhất: Impact trước vì engine đã có sẵn một nửa (xong v1); toàn bộ 7 phase đã hoàn thiện (v1–v7), 87/87 effects Available.
Falling/Hold/Release theo vòng đời nốt; Ambient độc lập nên sau; Smart cần các kênh
để điều chế nên gần cuối; Themes cuối vì chúng chỉ là preset trên tất cả.

## 7. Quy ước thêm effect mới (checklist)

1. Thêm vào `EffectCatalog` với đúng kênh; để `Planned` cho đến khi xong.
2. Thêm settings vào `PianoVisualSettings` (+ `Clamp()` + options array nếu là Choice).
3. Vẽ trong `PianoStage` đúng kênh; tôn trọng `HasActiveEffects`/`ClearTransient`.
4. Thêm UI (toggle/Choice/slider + `VisibleWhen` + tooltip) đúng trang.
5. Cập nhật preset liên quan (nếu đổi look mặc định — tránh).
6. Thêm assert vào `VerificationSuite` (spawn → geometry → decay → restore).
7. Cập nhật `EffectCatalog` → `Available` + docs (audit + redesign này).

Nguyên tắc bất biến: **mọi cài đặt hiện trên UI đều phải có logic thật** —
Planned không hiện UI.

## 8. GPU atmosphere, hit line and emitter refinement

GPU adds a second, full-screen atmosphere path beside the instanced ambient shapes:

- `BackgroundMotion` runs in `Gpu/StageShaders.hlsl::BackgroundMotion`, so Aurora, Nebula,
  Prism, Ember Haze, Ocean Flow and Retro Grid are coherent shader fields, not thousands
  of CPU-created sprites. Amount, speed and a palette tint travel through `GpuLook` and
  the `SceneFx` frame constants. None, a hidden background and chroma key suppress it.
- `HaloPulseStyle` selects Pulse, Sweep, Twin Comets, Spectrum, Electric Arc or Ripple.
  The shader modulates the note-aware hit-line filament; `AddHaloPulses` adds sparse,
  style-specific HDR geometry for the moving heads, arcs and rings. Intensity and speed
  are independent controls, and activity/tempo sync only modulate brightness.
- The held-key spark emitter now uses the selected burst family too (Embers, Splash,
  Fireworks, Confetti or Dust), with each family owning its velocity, gravity, drag,
  lifetime, shape and colour response. `ParticleRandomness`, life randomness and size
  randomness control actual independent samples instead of decorative-only sliders.
- The GPU physics-time control scales particle integration and particle ageing only.
  Song time, key transitions, live-note trails, the background shader and hit-line motion
  keep real frame time, so slow-motion sparks do not slow the performance.
- Each built-in and community look chooses a motion profile for its theme; Classic Roll,
  Two Hands and Green Screen deliberately retain a quiet or clean backdrop.

The settings path is covered in `SETTINGS-WIRING-AUDIT.md`. `VerifyGpuStage` checks mapping,
clamps, impact and held-emitter shapes, slow/fast particle ageing with a real-time stage clock,
a WARP render of the procedural sky and active hit line, and a clean chroma frame; the community
preset generator keeps every shipped JSON preset complete.
