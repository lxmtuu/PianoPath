# Audit độ phủ hiệu ứng — Phase 0

> **Phạm vi:** đối chiếu inventory với settings, UI, software/WPF, GPU, kiểm thử và AR.<br>
> **Ngày:** 2026-10-06 · **Trạng thái:** Phase 0 gate đạt trên Windows CI run [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020), commit `68bcc466`; chỉ xác nhận WARP/synthetic inputs, không xác nhận phần cứng GPU/camera.
> **Lộ trình tổng:** [`VISUAL-EFFECTS-MASTER-ROADMAP.md`](VISUAL-EFFECTS-MASTER-ROADMAP.md) · **Inventory thiết kế cũ:** [`EFFECTS-REDESIGN.md`](EFFECTS-REDESIGN.md).

## 1. Phương pháp và giới hạn xác minh

Đã đối chiếu các tệp:

- `Stage/Effects/EffectCatalog.cs`
- `Stage/PianoVisualSettings.cs`, `Stage/VisualPresets.cs`
- `Ui/MainWindow.Settings.cs`
- `Stage/PianoStage.cs`, `Stage/PianoStage.Gpu.cs`
- `Gpu/GpuLook.cs`, `Gpu/GpuStageSimulation.cs`, `Gpu/GpuStageSimulation.Effects.cs`, `Gpu/GpuStageSimulation.Ambient.cs`, `Gpu/GpuArLayer.cs`
- Các hàm `VerifyImpactFx`, `VerifyFallingFx`, `VerifyHoldFx`, `VerifyReleaseFx`, `VerifyAmbientFx`, `VerifySmartFx`, `VerifyThemes`, `VerifyGpuStage`.

**Đã chạy cục bộ:** `python3 tools/check_sources.py` — PASS; checker rà 179 file C#, XAML/resource, README/link, preset, localization, project XML và các kiểm tra tĩnh khác. `git diff --check` cũng PASS.
**Windows CI:** run [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020) trên commit `68bcc466` — static, portable xUnit, Release build, `--verify`, `--bench`/`--bench-ar`, installer, README previews và preset gallery đều PASS. CI dùng WARP cùng AR/camera tổng hợp; benchmark JSON được lưu thành artifact của run. Sandbox không có `dotnet`, webcam hoặc GPU phần cứng, nên không tuyên bố đo performance/phép chiếu trên thiết bị thật. Các bảng dưới đây vẫn phân biệt code-path đã verify với parity/capability chưa chứng minh.

## 2. Kết luận nhanh

| Hạng mục | Kết quả audit |
|---|---|
| Số mục trong catalog | 87; 86 mang `Available`, `falling.color-gradient` hiện được đánh dấu `Planned` vì ColorMode=Gradient chỉ trải màu theo pitch, chưa hòa hai màu dọc thân nốt. |
| CI gate hiện tại | Run [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020), commit `68bcc466`: static, xUnit, Release build, verification, bench, installer, previews/gallery đều PASS; dùng WARP/synthetic input. |
| Phân loại | 40 lifecycle + 31 ambient + 9 modulators + 7 themes; các nhóm có alias/chia sẻ kênh, không phải 87 renderer riêng. |
| Nguồn metadata runtime | `EffectCatalog` không được UI/renderer dùng làm nguồn options hay capability trong app hiện tại; tham chiếu ngoài chính tệp catalog chủ yếu là verification/docs. UI và settings khai báo riêng trong `PianoVisualSettings`/`MainWindow.Settings`. |
| “Available” có chặn UI không? | Không. Runtime không đọc `EffectStatus` để lọc options; hiện có 86 `Available`, 1 `Planned`. `Planned` chỉ đúng metadata trong inventory, không tự ẩn một control UI tương ứng. |
| Test độ phủ | Có kiểm chứng theo **nhóm** và một vài style mẫu, không có bằng chứng tự động cho từng option/ID hoặc parity pixel giữa mọi renderer. |
| AR | Capability đã được gán rõ cho pilot typed: trong 24 family, 6 lớp screen-space (4 ambient + shooting stars + petals) là `Native` theo WARP captured-frame check; 18 lớp gắn nốt/phím hoặc backdrop bị `Unsupported` và bị ẩn khi overlay bật. Ba note materials vẫn là `Fallback` `ar.note.flat-quad`. `Native` ở đây chỉ có nghĩa là được composited trên ảnh camera, **không** phải chiếu qua homography/world-space. |
| Recording | Slice verification theo family đã được thêm: dự kiến đọc 24 GPU `GpuRecordingTap` frame (640×360) và 22 WPF PNG write/read; `falling.shimmer` và `atmosphere.shooting-stars` giữ WPF `Unsupported`. **Kết quả Windows CI cho diff pixel/read-back trên snapshot này còn pending**, nên `Recording=Native` trong source chỉ được chấp nhận nếu các gate đó PASS. Parameter-level `Recording` bindings vẫn `Unverified`; recording cost vẫn `Unmeasured`. |
| Audio-reactive | `AudioReactive` hiện dùng energy/onset envelope trong stage; chưa phải lựa chọn spectrum FFT từ PCM đã xác minh. |
| Lỗi renderer cụ thể | `AmbientLight="Spotlights"` từng có GPU path nhưng thiếu WPF path; preset `Concert Gold` chọn giá trị này. Đã thêm shaft/pool; regression kiểm tra tắt, bounds và hơn 1.000 pixel raster PASS trong Windows CI run `37467081020`. Chưa đo parity pixel với GPU thật. |

### Các option thực sự có trong settings

- `FallingTrail`: 7 style hoạt động — Glow, Sparkles, Speed Lines, Blur, Ribbon, Rainbow, Stream.
- `ImpactWave`: Ring, Shockwave, Ripple; cộng `None`.
- `ImpactBurst`: Embers, Splash, Fireworks, Confetti, Dust.
- `ImpactMorph`: Shatter, Melt, Absorb, Bounce, Morph; cộng `None`.
- `ImpactFlashStyle`: Flash, Lightning, Plasma.
- `ReleaseEffect`: Fade, Float Up, Dissolve, Smoke, Snap Back, Echo Rings.
- Bốn ambient slot có tổng 20 style hoạt động: Energy 4, Nature 7, Light 4, Cosmic 5. Một số mục trong catalog nằm ở setting khác, không nằm trong slot có tên ambient.
- `AmbientLight` còn có `Spotlights` (ngoài 7 tên trong `EffectCatalog.AmbientLight`). GPU có render; WPF shaft/pool và regression assertions (zero amount, bounds, raster pixels) đã PASS trên Windows CI run `37467081020`. Choice này vẫn là control ngoài 87 catalog IDs; xem P1 gap bên dưới.
- Petals, Stars/Shooting Stars, Bloom, Note Glow, Rainbow Trail, hit-line pulse và keyboard light là các control hoạt động riêng, không phải tất cả đều là option của bốn ambient slot.

## 3. Ký hiệu trong bảng

- **Choice** — người dùng chọn được style trực tiếp trong UI.
- **Toggle/value** — chức năng bật bằng toggle, màu hoặc slider riêng.
- **Shared/alias** — tên catalog dùng chung một renderer/setting với mục khác; không phải thuật toán độc lập.
- **Implicit** — do dữ liệu âm nhạc/setting chung điều khiển, không có toggle riêng.
- **Preset** — tổ hợp cài đặt trong `VisualPresets`; không có code renderer riêng.
- **Sampled test** — `--verify` có kiểm tra nhóm, nhưng chưa kiểm từng giá trị trong dòng.
- **AR typed pilot** — 24 effect families đã có support state riêng: 6 screen-space composite `Native`, 18 unprojected families `Unsupported`; note materials giữ flat-quad fallback. Điều này chưa phủ 87 descriptive IDs và không tạo surface/world-space projection. Xem §6.

Đường stage thông thường dùng `PianoStage` cho software/WPF và `GpuLook` + `GpuStageSimulation*` cho GPU. Có chung settings/ý nghĩa không có nghĩa hai ảnh pixel giống nhau; parity cần test riêng. Các mục `Verify*Fx` bên dưới hiện nằm trong `Diagnostics/VerificationSuite.cs` và chỉ chạy khi có môi trường Windows/.NET tương ứng.

## 4. Ma trận inventory 87 mục

### 4.1 Falling — 10 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `falling.glow-trail` | `FallingTrail="Glow"`; `DrawFallingTrail` / `AddNoteTrail`. | Choice; test nhóm falling có chọn `Sparkles`, chưa test riêng Glow. AR: `Unsupported` trên camera composite; không được project qua calibration. |
| `falling.motion-blur` | `FallingTrail="Blur"`. | Choice; cùng control/renderer trail; chưa test riêng option Blur. AR: `Unsupported` trên camera composite; không được project qua calibration. |
| `falling.sparkle-tail` | `FallingTrail="Sparkles"`. | Choice; được chọn trong `VerifyFallingFx`, nhưng test kết hợp pulse/ghost và không chứng minh parity GPU/WPF. |
| `falling.color-gradient` | Không có control/render pass head-to-tail; `ColorMode="Gradient"`/`Palette` → `NoteColorCore` chỉ trải màu theo pitch. | Catalog entry đã được đổi sang `Planned`; giữ nghĩa head-to-tail, không đánh đồng với gradient low-to-high của bàn phím. Cần setting/màu/renderer/test riêng ở Phase 4. |
| `falling.pulsing` | `FallingPulse` + `FallingPulseRate`. | Toggle/value; `VerifyFallingFx` vẽ chung với trail/ghost, chưa cô lập phép đo pulse. |
| `falling.ribbon-twist` | `FallingTrail="Ribbon"`. | Choice; cùng renderer trail với Glow/Blur; “twist” là tên style, không có điều khiển riêng. AR: `Unsupported` trên camera composite; không được project qua calibration. |
| `falling.particle-stream` | `FallingTrail="Stream"`. | Choice; chưa test riêng option Stream. |
| `falling.ghost-notes` | `FallingGhost` + `FallingGhostAmount`. | Toggle/value; được vẽ cùng falling sample, chưa assert riêng số/alpha ghost. |
| `falling.speed-lines` | `FallingTrail="Speed Lines"`. | Choice; chưa test riêng option Speed Lines. |
| `falling.rainbow-shift` | `FallingTrail="Rainbow"` đồng thời tô hue thân nốt; tốc độ hue dẫn xuất từ trail intensity. | Shared/alias: trail và body shift ghép vào một lựa chọn; không có speed control riêng. Chưa test riêng. |

### 4.2 Impact — 16 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `impact.burst` | `ImpactBurst` + `ShowEmbers` + particle physics/response; `Impact()` phát burst. | Shared family; không phải lựa chọn thứ sáu ngoài 5 `ImpactBurst` styles. Test sample Confetti, không phải từng style. |
| `impact.ember-burst` | `ImpactBurst="Embers"` trong cùng burst engine. | Shared/alias với `impact.burst`; style Embers là default. |
| `impact.ripple-ring` | `ShowImpactRings` + `ImpactWave="Ring"` + `RingSize`/intensity. | Choice; `VerifyImpactFx` kiểm geometry với Shockwave, không kiểm riêng Ring. |
| `impact.shockwave` | `ImpactWave="Shockwave"`. | Choice; geometry/decay được `VerifyImpactFx` kiểm. AR: `Unsupported` trên camera composite; không được project qua calibration. |
| `impact.flash` | `ShowImpactFlash` + `ImpactFlashStyle="Flash"` + intensity. | Choice/toggle; `VerifyImpactFx` kiểm flash geometry/decay, chưa kiểm từng style. |
| `impact.key-glow` | `KeyLighting`, `KeyGlowRadius`, `PressedKeyColorMode` và key state. | Shared/alias: không có “impact key glow” selector riêng; AR vẽ key patches cơ bản nhưng không có cùng pass impact. |
| `impact.splash` | `ImpactBurst="Splash"`; kind droplet/physics riêng. | Choice; chưa có test riêng cho kiểu Splash. |
| `impact.firework` | `ImpactBurst="Fireworks"`; `AmbientEnergy="Fireworks"` là đường khác. | Shared giữa burst theo nốt và layer ambient; chưa test riêng cả hai đường. |
| `impact.confetti-pop` | `ImpactBurst="Confetti"`. | Choice; được chọn trong `VerifyFallingFx` cùng Shatter/Lightning, chưa có ảnh/test riêng. |
| `impact.dust-cloud` | `ImpactBurst="Dust"`. | Choice; chưa test riêng option Dust. |
| `impact.shatter` | `ImpactMorph="Shatter"` → shard particles. | Choice; sample test gọi cùng Confetti burst; chưa kiểm riêng morphology/lifetime. |
| `impact.bounce` | `ImpactMorph="Bounce"` → streak particles + ring. | Choice; chưa test riêng option Bounce. |
| `impact.absorb` | `ImpactMorph="Absorb"` → ring co vào điểm hit. | Choice; chưa test riêng option Absorb. |
| `impact.melt` | `ImpactMorph="Melt"` → droplet particles. | Choice; chưa test riêng option Melt. |
| `impact.lightning-strike` | `ImpactFlashStyle="Lightning"`. | Choice; WPF verify sample kiểm một geometry tia sét; GPU parity và AR chưa được xác nhận. |
| `impact.note-morph` | `ImpactMorph="Morph"`; UI gọi là “Star morph”. | Shared/alias: catalog mô tả nhiều shape (“star, heart…”) nhưng hiện setting chỉ có một style Morph/Star, không có shape picker. |

### 4.3 Hold — 8 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `hold.flame-pillar` | `ShowFlame`, `FlameIntensity`, `FlameHeight`, `FlameColorMode`. | Toggle/value cũ; `VerifyHoldFx` không assert riêng flame. |
| `hold.sustain-particles` | `ShowWisps` + Wisp amount/speed/height/width/turbulence/glow. | Shared/alias với lớp wisps; chưa có kiểm chứng riêng theo Note On/off. |
| `hold.energy-column` | `ShowLightBeams` + `BeamIntensity`; GPU/WPF key beam paths. | Shared/alias; không có `HoldColumnStyle` selector riêng. |
| `hold.bar` | `HoldBar` + `HoldBarIntensity`. | Toggle/value; được bật trong `VerifyHoldFx` và kết hợp vẽ sounding note. |
| `hold.breathing-glow` | `HoldBreath` + `HoldBreathRate` / `BreathFactor`. | Toggle/value; được bật trong test nhóm, chưa assert riêng chu kỳ/tần số. |
| `hold.vibration` | `HoldVibration` + `HoldVibrationAmount`. | Toggle/value; được bật trong test nhóm, chưa assert displacement/giới hạn riêng. |
| `hold.color-cycle` | `HoldColorCycle` + speed. | Toggle/value; được bật trong test nhóm, chưa test màu theo thời gian riêng. |
| `hold.electric-arc` | `HoldElectricArc` + intensity; GPU procedural arc/WPF `DrawElectricArcs`. | Test kiểm geometry giữa hai phím. AR: `Unsupported` trên camera composite; không được project qua calibration. |

### 4.4 Release — 6 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `release.fade-out` | `ReleaseEffect="Fade"`; không spawn thêm hình, dùng fade của note. | Mặc định; không có hiệu ứng phụ riêng để test. |
| `release.float-up` | `ReleaseEffect="Float Up"` → mote bay lên. | Choice; `VerifyReleaseFx` hiện chỉ chạy Echo Rings. |
| `release.dissolve` | `ReleaseEffect="Dissolve"` → hạt tan. | Choice; chưa test riêng. |
| `release.smoke-puff` | `ReleaseEffect="Smoke"` → dust/smoke particles. | Choice; chưa test riêng. |
| `release.snap-back` | `ReleaseEffect="Snap Back"` → ring/co rút. | Choice; chưa test riêng. |
| `release.echo-rings` | `ReleaseEffect="Echo Rings"` → rings. | Choice; verify song note-end + live release + seek ngược + decay. |

### 4.5 Ambient — Particle & Energy (9)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `ambient.embers` | `ImpactBurst="Embers"` / `ShowEmbers`. | Không phải `AmbientEnergy` choice; event-based burst. Alias. |
| `ambient.fire` | `ShowFlame` / flame hold path. | Không có ambient-slot `Fire`; đây là lớp theo phím/nốt. Alias. |
| `ambient.explosion` | `ImpactBurst` + velocity/particle response. | Event-based burst, không phải ambient slot. Alias. |
| `ambient.sparkles` | `ShowStars` (starfield) hoặc `FallingTrail="Sparkles"`. | Hai đường khác nhau; không có `AmbientEnergy="Sparkles"`. Alias. |
| `ambient.lightning` | `AmbientEnergy="Lightning Storm"`. | Choice ambient có thật; `VerifyAmbientFx` chọn Fireworks, chưa chọn Lightning Storm. |
| `ambient.laser` | `AmbientEnergy="Laser Beams"`. | Choice ambient có thật; chưa test riêng. |
| `ambient.plasma` | `ImpactFlashStyle="Plasma"`. | Impact flash cục bộ, không có ambient plasma orb selector. Alias/semantic mismatch. |
| `ambient.confetti` | `AmbientEnergy="Confetti Rain"` hoặc `ImpactBurst="Confetti"`. | Hai trigger khác nhau; catalog chưa phân biệt layer và impact. |
| `ambient.firework` | `AmbientEnergy="Fireworks"` hoặc `ImpactBurst="Fireworks"`. | Hai trigger khác nhau; catalog chưa phân biệt layer và impact. |

### 4.6 Ambient — Nature & Elements (9)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `ambient.petals` | `ShowPetals` + `PetalAmount` + `PetalColor`. | Có renderer riêng, nhưng không nằm trong `AmbientNature` choice. |
| `ambient.ripple` | `ImpactWave="Ripple"`. | Effect impact tại phím, không phải ambient layer tự chạy. |
| `ambient.rain` | `AmbientNature="Rain"`. | Choice thật; chưa test riêng. |
| `ambient.snow` | `AmbientNature="Snow"`. | Choice thật; được chọn trong `VerifyAmbientFx` như một phần composite. |
| `ambient.smoke` | `AmbientNature="Smoke"` và `ReleaseEffect="Smoke"`. | Hai trigger/renderer context khác nhau; tên catalog chưa tách. |
| `ambient.leaves` | `AmbientNature="Leaves"`. | Choice thật; chưa test riêng. |
| `ambient.butterflies` | `AmbientNature="Butterflies"`. | Choice thật; chưa test riêng. |
| `ambient.aurora` | `AmbientNature="Aurora"`; `BackgroundMotion="Aurora"` là shader nền riêng. | Hai đường Aurora khác nhau, cần mô tả capability/trigger. |
| `ambient.dust` | `AmbientNature="Dust"` hoặc `ImpactBurst="Dust"`. | Ambient nature và impact burst cùng tên nhưng trigger/physics khác. |

### 4.7 Ambient — Light & Color (7)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `ambient.glow` | `NoteStyle="Neon"`, `NoteGlow`, `NoteEdge`; key light có setting riêng. | Vật liệu/note rendering, không phải ambient slot. Alias. |
| `ambient.bloom` | `BloomIntensity` + `BloomSize` / GPU compositor. | Hậu kỳ toàn scene, không phải `AmbientLight` style. |
| `ambient.flash` | `ShowImpactFlash` + `ImpactFlashStyle`. | Impact-local flash, không phải ambient slot. |
| `ambient.splash` | `AmbientLight="Color Splash"`. | Choice ambient có thật; chưa test riêng. |
| `ambient.rainbow-trail` | `FallingTrail="Rainbow"`. | Falling trail/body hue; không phải lớp ambient. Alias. |
| `ambient.gradient-wave` | `AmbientLight="Gradient Wave"`. | Choice thật; test sample AmbientLight hiện dùng Prism. |
| `ambient.prism` | `AmbientLight="Prism"`. | Choice thật; `VerifyAmbientFx` chọn Prism ở WPF layer. |

### 4.8 Ambient — Cosmic & Abstract (6)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `ambient.galaxy` | `AmbientCosmic="Galaxy"`; `BackgroundMotion="Nebula"` là đường nền riêng. | `VerifyAmbientFx`/`VerifyGpuStage` có composite sample; chưa kiểm từng mode/pixel parity. |
| `ambient.black-hole` | `AmbientCosmic="Black Hole"`. | Choice thật; chưa test riêng. |
| `ambient.matrix` | `AmbientCosmic="Matrix Rain"`. | Choice thật; chưa test riêng. |
| `ambient.geometry` | `AmbientCosmic="Geometric"`. | Choice thật; chưa test riêng. |
| `ambient.fractal` | `AmbientCosmic="Fractal"`. | Choice thật; chưa test riêng. |
| `ambient.ribbon` | `FallingTrail="Ribbon"`. | Alias với `falling.ribbon-twist`; không phải cosmic ambient. |

### 4.9 Smart modulators (9)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `mod.velocity-size` | `Impact(pitch, strength)`, `ParticleResponse`, intensity/physics. | Implicit: không có toggle “Velocity Size”; `VerifySmartFx` so particle count hit nhẹ/mạnh. |
| `mod.velocity-map` | Cùng event strength điều chỉnh burst/wave/flash/key response. | Implicit/shared với velocity-size; không có curve/amount riêng cho brightness. |
| `mod.key-color` | `ColorMode`, `Palette`, hand/track/pitch color qua `NoteColorCore`. | Color modes là lựa chọn loại trừ; không có modulator ID/toggle riêng. |
| `mod.velocity-color` | `VelocityColor` + `VelocityColorAmount`, note/burst tint. | Toggle/value; verify source có assert UI và sample rendering gián tiếp, chưa đo curve. |
| `mod.octave-color` | `OctaveColor` + `OctaveColorBlend`. | Toggle/value; `VerifySmartFx` kiểm hai octave khác màu. |
| `mod.pedal-glow` | `PedalGlow` + intensity, `SetSustainPedal`. | Toggle/value; cần test assert chênh sáng có/không pedal, không chỉ gọi hàm. |
| `mod.tempo-sync` | `TempoSync` + amount, `PulseBeat`/beat pulse. | Toggle/value; cần test đổi tempo/seek/beat phase và ảnh before/after riêng. |
| `mod.audio-reactive` | `AudioReactive` + amount; `_energyLevel` tăng theo note onset/energy. | Chưa phải PCM FFT band; verify hiện bật chung boost nhưng không chứng minh spectrum hoặc audio latency. |
| `mod.zone-split` | `ZoneSplit` + pitch/amount; recolor nốt và ép burst bass/treble. | Toggle/value; `VerifySmartFx` assert bass=embers, treble=splash. |

### 4.10 Theme graphs (7)

| ID | Preset/settings graph | Độ phủ / lưu ý |
|---|---|---|
| `theme.fire` | `Inferno`. | `VerifyThemes` kiểm vài setting đại diện; không có renderer riêng. |
| `theme.ice` | `Ice Crystal`. | `VerifyThemes` kiểm burst/wave/snow. |
| `theme.galaxy` | `Galaxy Voyage`. | `VerifyThemes` kiểm galaxy/plasma/rainbow. |
| `theme.sakura` | `Sakura Nocturne`. | `VerifyThemes` kiểm petals/confetti/float-up. |
| `theme.electric` | `Electric Storm`. | `VerifyThemes` kiểm lightning/arc/storm. |
| `theme.ocean` | `Ocean Depths`. | `VerifyThemes` kiểm splash/ripple/rain. |
| `theme.retro` | `Retro Arcade`. | `VerifyThemes` kiểm confetti/bounce/snap/square note. |

## 5. Khoảng trống ưu tiên

### P0 — quyết định đã chốt và yêu cầu trước typed runtime registry

1. **Quyết định nguồn dữ liệu — đã chốt cho Phase 0:** các khai báo settings/default/normalize và danh sách lựa chọn UI hiện có tiếp tục là nguồn runtime; `EffectCatalog` được giữ đúng vai trò inventory/metadata cho audit/diagnostics, không lọc UI và không chứng minh capability. Chỉ đưa một typed registry vào runtime sau khi Phase 1 chuẩn hóa ID/alias/capability; không nối `EffectStatus` vội.
2. **Đã sửa và verify WPF path `AmbientLight=Spotlights`.** Renderer ba shaft/pool theo màu, tốc độ và activity cùng kiểm tra zero-intensity, bounds và pixel raster đã PASS trong Windows CI run `37467081020`. Phạm vi kết luận chỉ là WPF/WARP test; parity pixel trên GPU thật chưa được đo.
3. **AR và Recording capability theo typed family — phạm vi đã được xác định; capture slice mới chờ Windows CI.** `EffectContractCatalog` ghi trạng thái riêng cho 24 effect families: sáu ambient pass-through là `Native` chỉ trong screen-space camera composite; 18 stage-anchored/backdrop families là `Unsupported` trên AR overlay vì không được chiếu lên keyboard thật. Ba note materials giữ `Fallback` `ar.note.flat-quad`. `VerifyArOverlayCapturedFrames` đọc lại các lớp pass-through và suppression trên WARP. Phạm vi AR chưa bao phủ toàn bộ 87 ID descriptive và chưa có surface/world-space geometry. Recording: source hiện có fixture GPU tap riêng cho 24 family, PNG write/read cho 22 WPF-native; kết quả pixel diff/read-back của Windows CI chưa chạy trên snapshot này. Không suy rộng sang hai WPF-Unsupported family. Kể cả khi family-level capture PASS, điều đó **không** chứng minh từng parameter binding, mọi style/value hay recording cost: bindings vẫn `Unverified`, chi phí vẫn `Unmeasured`.
4. **Đã chốt nghĩa `falling.color-gradient`:** giữ yêu cầu chuyển màu đầu–đuôi; `ColorMode="Gradient"` hiện tại là gradient theo pitch và không thay thế được. Entry đã chuyển sang `Planned`; renderer/settings/test dọc thân nốt được đưa vào Phase 4, không làm lẫn hai hành vi.

### P1 — độ rõ của sản phẩm/kiểm chứng

5. Phân biệt event-triggered `Embers/Fire/Explosion/Plasma/Water Ripple` với ambient chạy nền, đồng thời giữ alias cũ khi migrate preset.
6. `Spotlights` là setting/UI choice bổ sung ngoài 87 ID hiện tại; WPF path và regression đã PASS trên CI. Trong Phase 2 ID normalization, quyết định có thêm ID explicit hay gộp vào Ambient Light capability; đồng thời lập mapping/verification cho `ShowStars`, `ShowPetals`, Shooting Stars, note landing glow, note shimmer, halo pulse và background motion — các control tồn tại nhưng không thuộc inventory 87 hiện tại.
7. Mở rộng verify từ “một mẫu mỗi nhóm” sang từng Choice value tối thiểu: spawn/draw/decay, màu/size/speed/amount, disabled, seek/reset, preset round-trip.
8. Kiểm tra pixel parity GPU/software trên tập scene chung; hiện có thể khẳng định cùng settings có đường code ở cả hai, nhưng chưa có bằng chứng mọi style render giống/đạt chất lượng tương đương.
9. Tách `AudioReactive` envelope hiện tại khỏi mục tiêu FFT thật; chỉ quảng bá low/mid/high spectrum sau khi đường PCM → analyzer → smoothing → render đã được kiểm thử.

## 6. Ma trận đường kiểm chứng hiện tại

| Nhóm verify | Bằng chứng trong mã | Chưa chứng minh |
|---|---|---|
| `VerifyImpactFx` | UI wave/flash; Shockwave + Flash sinh geometry và decay. | Mọi wave/burst/morph/flash value, GPU/AR parity. |
| `VerifyFallingFx` | UI trail/pulse/ghost; Sparkles + Confetti/Shatter/Lightning sample; trail vẽ ra ngoài note; inventory assertion (87 total, 86 Available, head-to-tail Planned) PASS trong run `37467081020`. | Gradient head-to-tail chưa có renderer; 7 trail options còn lại chưa được verify riêng; GPU parity chưa được chứng minh. |
| `VerifyHoldFx` | UI các toggle; sounding note và Electric Arc geometry. | Flame/Wisps/Energy Column, từng modulation rate/amount và mọi renderer. |
| `VerifyReleaseFx` | Echo Rings live + MIDI note-end + seek ngược + decay. | 5 release styles còn lại và GPU parity. |
| `VerifyAmbientFx` | Bốn selector; sample Fireworks/Snow/Prism/Galaxy, 4 pass có geometry; WPF `Spotlights` zero-intensity, drawing-bounds và rasterized-pixel assertions cùng test Ripple PASS trong run `37467081020`. | Mọi ambient choice khác và parity pixel GPU/WPF vẫn chưa được chứng minh. |
| `VerifySmartFx` | Octave recolor; velocity particle count; bass/treble zone; halo render sau khi bật boosts. | Đo riêng pedal/tempo/audio boost; FFT/latency/curve. |
| `VerifyThemes` | Bảy preset graph có một số setting đại diện. | Render thumbnail/ảnh và settings đầy đủ của từng theme. |
| `VerifyGpuStage` | Một bundle nhiều setting; WARP render, ambient count, recording tap. | Ma trận từng effect/option và GPU thật. |
| `tests/PianoPath.Tests` | Project test thuần cho AR/layout/audio/math PASS trong run `37467081020`. | Không có project test riêng cho toàn bộ lifecycle effect renderer. |

### AR capability baseline (source audit)

| Capability | Hiện trạng có thể khẳng định từ code | Ranh giới / chưa được chứng minh |
|---|---|---|
| Điều kiện chạy | `ArPlan.Decide` yêu cầu GPU hoạt động, camera/video source và calibration hợp lệ; software fallback trả `NeedsGpu`. Chroma-green background loại trừ camera AR. | Không có đường AR dựa trên software renderer. |
| Camera & mapping | Camera ring → GPU texture; Fit/Fill, mirror, opacity; `ArView` và homography dùng chung cho nền camera và các điểm chiếu. | Kiểm tra trên camera/webcam thật tùy thiết bị; verify dùng WARP và khung synthetic. |
| Phím thật | `GpuArLayer` lấy glow/color từ simulation rồi gọi `ArKeys.Patches` để chiếu phần nhìn thấy của key trắng/đen thành quad; có `KeyLight`/opacity/delay. | Là light overlay trên ảnh, không phải dựng lại phím 3D hay che khuất theo bàn tay tự động. |
| Nốt Learn | `ArNotes.Build` tạo note quad theo calibration, màu `GpuLook.NoteColor`, trạng thái played/missed và fade; `ArNoteStyle` điều khiển fall speed/reach/length/gap/width; `ArOptions` điều khiển Learn/Perform, lookahead, opacity và delay. | Đây là hình học AR riêng, không lấy trọn `NoteStyle`/material của stage (3D, glass/refraction, note shimmer, trails, note labels). |
| Stage lifecycle effects | Khi overlay Active, `GpuLook.From(..., overCamera: true)` ẩn notes/keys, disable emitter/impact/release channels; `BuildNotes` bỏ các falling/hold effect và `GpuStageSimulation.BuildSprites` bỏ cả sprite transients đã ở giữa đời. | 18 trong 24 effect families hiện là `Unsupported` trên AR overlay; chúng chưa được project lên keyboard thật. Đây là suppression, không phải tuyên bố renderer AR cho các FX đó. |
| Ambient / screen-space | GPU `BuildAmbient` composited lên ảnh camera: bốn ambient slots, shooting stars và petals đã được bật riêng, render và đọc lại thành frame WARP. Các families này mang capability `Native` riêng cho chế độ screen-space overlay. | Không perspective-project qua calibration và không phải surface/world-space AR; sao nền và key beams bị loại vì camera thay backdrop và stage-local key-light sai vị trí. Guide lanes chưa thuộc family typed này. |
| Verify hiện có | `VerifyArCameraLayer`, `VerifyArOverlayExclusions`, `VerifyArOverlayCapturedFrames`, `VerifyArRecording`, `VerifyArDelay` và `VerifyArLatency`; captured-frame test kiểm riêng sáu lớp pass-through, sao/beams bị loại, và transients in-flight bị ẩn. Note materials có read-back riêng qua WPF PNG file + GPU `GpuRecordingTap`. Slice mới thêm fixture capture cho 24 non-material family qua GPU tap và 22 WPF-native family qua PNG write/read; cần Windows CI PASS để xác nhận output pixel thực tế. | `Gem`/`CrystalOutline` trên phím thật vẫn là `ar.note.flat-quad`. Family-level Recording `Native` chỉ khẳng định frame đại diện của từng family đi qua đường ghi GPU; không phủ mọi style/value, parameter binding, độ phân giải dài hạn hay phần cứng thật. Binding recording của từng parameter vẫn `Unverified`, chi phí ghi vẫn `Unmeasured`; CrystalOutline/NoteRefraction còn khác biệt WPF/GPU. |

### Recording family capture baseline

Các fixture ở `Diagnostics/EffectRecordingFixtures.cs` được nối vào suite: mỗi family so sánh frame trung tính với frame chỉ bật đúng một họ. Windows CI của snapshot này chưa chạy; mô tả dưới đây là coverage/assertion được thiết kế, chưa phải kết quả PASS:

| Nhóm | Số family | Fixture đại diện | Đường read-back |
|---|---:|---|---|
| Impact | 4 | Shockwave, Lightning, Confetti burst, Shatter | WPF PNG + GPU `GpuRecordingTap` |
| Falling | 3 | Sparkles/ghost, pulse, shimmer | WPF PNG + GPU tap cho trail/pulse; shimmer chỉ GPU tap vì WPF `Unsupported` |
| Hold | 5 | bar, breath, vibration, color cycle, electric arc | WPF PNG + GPU tap |
| Ambient/atmosphere | 8 | Confetti Rain, Snow, Prism, Galaxy, stars, shooting stars, petals, beams | WPF PNG + GPU tap; shooting stars chỉ GPU tap vì WPF `Unsupported` |
| Particles | 3 | sparks, wisps, flames | WPF PNG + GPU tap |
| Release | 1 | MIDI note-off → Echo Rings | WPF PNG + GPU tap |
| **Tổng** | **24** | Mỗi ID đúng một fixture; từng frame effect được đối chiếu với neutral frame cùng scene | **24 GPU tap + 22 WPF PNG** |

Khi chạy trên Windows, GPU fixtures sẽ dùng WARP và đọc frame 640×360 từ đường take off-screen thật; WPF fixtures ghi hai frame 640×360 vào `PngSequenceRecorder`, decode PNG rồi assert khác biệt pixel. Thư mục capture chỉ là fixture tạm trong `%TEMP%` và bị xóa sau verify. Hai family GPU-only không được nâng `WpfStage`; các test này không đo chi phí ghi, không xác minh từng setting/alias/style và không tạo tuyên bố về GPU phần cứng. `Recording=Native` ở typed family chỉ được giữ nếu Windows CI chứng minh frame đại diện của từng family có pixel khác biệt trên output đã đọc lại; nếu capture lỗi, phải hạ đúng family về `Unverified`. Mọi `parameter.Bindings.Recording` tiếp tục `Unverified`, mọi `Cost.Recording` tiếp tục `Unmeasured`.

**Quyết định Phase 0:** catalog giữ vai trò inventory/audit; các setting/default và lựa chọn UI hiện tại là nguồn runtime. Chưa gắn `EffectStatus` để lọc UI. Phase sau chỉ đưa typed registry vào runtime sau khi ID/alias/capability được chuẩn hóa; AR capability phải là thuộc tính explicit, không suy từ `Available`.

## 7. Gate kết thúc Phase 0

Phase 0 chỉ đóng khi:

- [x] Inventory 87 mục được đối chiếu với setting/UI/renderer ở mức source; alias, entry chưa độc lập và control nằm ngoài inventory được ghi rõ.
- [x] Chạy static source checker và `git diff --check`.
- [x] Windows CI [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020), commit `68bcc466`: Release build, `--verify`, portable xUnit và benchmark (`--bench`/`--bench-ar`) đều PASS.
- [x] README previews/preset gallery và bench JSON được sinh/lưu từ run; đây là WARP/synthetic baseline, không phải đo GPU/camera thật.
- [x] Chốt source-of-truth: `EffectCatalog` là inventory/metadata; settings/defaults và UI vẫn là runtime source.
- [x] Có WPF fix và regression assertions cho `Spotlights` software path (bounds, tắt và raster pixels); assertion raster PASS trên Windows CI.
- [x] Chốt nghĩa `falling.color-gradient`: head-to-tail vẫn là yêu cầu riêng, đánh dấu `Planned`; gradient hiện tại là theo pitch.
- [x] Ghi AR capability baseline, ranh giới overlay và alias/entry chưa độc lập.

**Phase 0 gate đã đạt** theo mã nguồn và Windows CI nêu trên. Kết luận này không nâng các giới hạn đã nêu thành capability: kiểm thử hình ảnh chạy trên WARP/synthetic inputs, chưa xác nhận GPU/camera/đàn thật hay AR world-space. Các gap còn lại được chuyển thành backlog Phase 2/4/8; không được mô tả như tính năng đã hoàn tất.
