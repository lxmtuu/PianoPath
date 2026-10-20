# Audit độ phủ hiệu ứng — Phase 0

> **Phạm vi:** đối chiếu inventory với settings, UI, software/WPF, GPU, kiểm thử và AR.<br>
> **Ngày audit gốc:** 2026-10-06 · **Cập nhật AR-S5:** 2026-10-10 · **Đối chiếu lại trạng thái repo:** 2026-10-16 · **Đối chiếu bằng chứng CI:** 2026-10-20 (xem hai khối ngay dưới). Phase 0 gate đạt trên Windows CI run [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020), commit `68bcc466`; CI chỉ xác nhận WARP/synthetic inputs, không xác nhận phần cứng GPU/camera. S5 build, full `--verify`/WARP và portable tests PASS trên Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), commit `95c5aee` (PR check [38014567555](https://github.com/lxmtuu/PianoPath/actions/runs/38014567555)). WARP đọc lại các nhánh Standard/Ultra, impact variants, release/pose/direction và bốn hold modifiers cùng giá trị tham số đại diện. Đây là synthetic/WARP evidence, không xác nhận webcam/GPU thật, OBS hay performance; 14 family S5 và binding AR vẫn `Unverified` cho tới khi có ma trận style/value/lifecycle đầy đủ.
>
> **Delta local 2026-10-10 — ✅ đã merge và đã được CI chạy (đối chiếu 2026-10-16):** setting `ArRisingMidiNotes`/“MIDI notes rise in Perform” (mỗi Note On là note object 3D riêng, Note Off đóng băng độ dài, object tiếp tục tới reach; live press bars vẫn độc lập và tắt mặc định) **không còn nằm ở working tree**. Đo tại chỗ trên HEAD `18b10d8`: setting ở `Stage/PianoVisualSettings.cs` (4 chỗ), toggle + mô tả ở `Ui/MainWindow.Settings.cs:647`, nhãn đã dịch ở `Localization/Strings.English.cs:798`, đường vẽ ở `Gpu/GpuArLayer.cs` + `Gpu/GpuStageSimulation.cs`, `Camera/ArElementCatalog.cs` đã được **link vào project test** (`PianoPath.Tests.csproj:74`), và `Diagnostics/VerificationSuite.cs` (12 lần) + `.Ar.cs` (2 lần) assert nó. Lần CI xanh trên `main` run [38900310173](https://github.com/lxmtuu/PianoPath/actions/runs/38900310173) ⇒ **nay là bằng chứng test/WARP**, không còn là delta chưa chạy. Điều **không** đổi: grep `RisingMidi` trong `Stage/Effects/*.cs` cho **0 kết quả**, tức không có trạng thái mới nào trong `EffectContractCatalog` — 14 family S5 và binding AR vẫn `Unverified` đúng như bảng dưới.

> **Delta 2026-10-20 — H3, dọn nợ "chờ chạy":** Windows CI run [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913) trên commit `40157bd` xanh **cả 4 job** (`static`, `test`, `keyboard-light`, `build`); bước `Verification suite` báo **8027 assertion**, 1 nhóm skip, và `Build` có 48 bước ✔ / 2 skip có chủ ý / 0 lỗi biên dịch. Mọi assertion từng được ghi là "thêm ở local branch, chưa chạy" trong tài liệu này — note-colour/material/shimmer, trail/ghost/landing glow, atlas label, parity Lite/Standard/Ultra, tuổi 0 của nốt Perform, hướng Learn, held-bar default/release, sáu style branch và Gem/Crystal trên AR quad — **đã chạy và PASS** trên WARP trong run đó (và đã PASS từ run [39197798138](https://github.com/lxmtuu/PianoPath/actions/runs/39197798138) trước khi N9 được thêm). **Đính chính:** các bản trước trích `39173459709` như bằng chứng PASS; run đó thực ra **bị huỷ** bởi lượt push sau, nên nay thay bằng `39294579913`. Điều **không** đổi: 14 family S5 và mọi binding tham số AR vẫn `Unverified`, và phần còn thiếu là **coverage** (ma trận style/value/lifecycle, webcam thật, GPU thật, performance) — không còn là "chưa chạy ở đâu cả".
>
> **Lộ trình tổng:** [`VISUAL-EFFECTS-MASTER-ROADMAP.md`](VISUAL-EFFECTS-MASTER-ROADMAP.md) · **Inventory thiết kế cũ:** [`EFFECTS-REDESIGN.md`](EFFECTS-REDESIGN.md).

## 1. Phương pháp và giới hạn xác minh

Đã đối chiếu các tệp:

- `Stage/Effects/EffectCatalog.cs`
- `Stage/PianoVisualSettings.cs`, `Stage/VisualPresets.cs`
- `Ui/MainWindow.Settings.cs`
- `Stage/PianoStage.cs`, `Stage/PianoStage.Gpu.cs`
- `Gpu/GpuLook.cs`, `Gpu/GpuStageSimulation.cs`, `Gpu/GpuStageSimulation.Effects.cs`, `Gpu/GpuStageSimulation.Ambient.cs`, `Gpu/GpuArLayer.cs`, `Gpu/GpuArLayer.Effects.cs`
- Các hàm `VerifyImpactFx`, `VerifyFallingFx`, `VerifyHoldFx`, `VerifyReleaseFx`, `VerifyAmbientFx`, `VerifySmartFx`, `VerifyThemes`, `VerifyGpuStage`, `VerifyArOverlayExclusions`, `VerifyArNoteProjection`.

**Đã chạy cục bộ:** `python3 tools/check_sources.py` — PASS; lần chạy **2026-10-16** rà **349 file C#**, 2 XAML, 119 resource key, 174 named element, 1482 khoá chuỗi nguồn in ra (1653 trong inventory), resource/localization, README/link, preset, project XML, trang phát hành công khai và các kiểm tra tĩnh khác, exit 0. Chạy lại **2026-10-20** (cùng ngày với run `39294579913`): **359 file C#**, 2 XAML, 119 resource key, 176 named element, 1485 khoá chuỗi nguồn in ra (1656 trong inventory) — vẫn là **số của ngày ghi**. *(Con số "219 file" của lần audit gốc là ảnh chụp 2026-10-06; các con số đếm trong toàn bộ tài liệu này — kể cả `docs/REPO-AUDIT.md` — đều là **số của ngày ghi**, không phải số hiện hành.)*
**Windows CI:** baseline Phase 0 run [37467081020](https://github.com/lxmtuu/PianoPath/actions/runs/37467081020) trên commit `68bcc466` đã PASS. Lát Recording theo family sau đó PASS trong run [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649), commit `ffcc340`: static, portable xUnit, Release build và full `--verify`; verifier đọc riêng 24 family frames từ `GpuRecordingTap` ở 640×360 và ghi/đọc 22 cặp neutral/effect qua PNG recorder rồi so pixel. `falling.shimmer` và `atmosphere.shooting-stars` vẫn WPF `Unsupported`. CI dùng WARP/synthetic scenes, không phải GPU/camera thật; không có kết luận về phần cứng hay performance. S5 + hold-modifier readback mới PASS trong [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) trên `95c5aee`; coverage chỉ theo nhánh/giá trị đại diện. Family-level Recording capture không chứng minh từng style/setting binding: Recording bindings vẫn `Unverified`, chi phí vẫn `Unmeasured`.

## 2. Kết luận nhanh

**S5 WARP:** build, full `--verify` (gồm `VerifyArNoteProjection`) và portable tests PASS trên [Windows CI run 38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), commit `95c5aee`; PR check [38014567555](https://github.com/lxmtuu/PianoPath/actions/runs/38014567555) cũng PASS. Đây chỉ là coverage tổng hợp/WARP; webcam, OBS, GPU vật lý và performance vẫn chưa xác minh. Bốn hold toggle trên live bar có pixel check riêng, so sánh giá trị tham số đại diện 0/100; đây chưa phải ma trận đầy đủ.

| Hạng mục | Kết quả audit |
|---|---|
| Số mục trong catalog | 87; 86 mang `Available`, `falling.color-gradient` hiện được đánh dấu `Planned` vì ColorMode=Gradient chỉ trải màu theo pitch, chưa hòa hai màu dọc thân nốt. |
| CI gate hiện tại | Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), commit `95c5aee`: static, portable tests, Release build, full `--verify`/WARP, installer và previews/gallery đều PASS; dùng synthetic/WARP input, không xác nhận hardware. CI frame-budget step cũng chạy trên WARP; số liệu đó không phải benchmark phần cứng/AR. AR/S5 performance trên GPU thật vẫn chưa được xác minh. |
| Phân loại | 40 lifecycle + 31 ambient + 9 modulators + 7 themes; các nhóm có alias/chia sẻ kênh, không phải 87 renderer riêng. |
| Nguồn metadata runtime | `EffectCatalog` không được UI/renderer dùng làm nguồn options hay capability trong app hiện tại; tham chiếu ngoài chính tệp catalog chủ yếu là verification/docs. UI và settings khai báo riêng trong `PianoVisualSettings`/`MainWindow.Settings`. |
| “Available” có chặn UI không? | Không. Runtime không đọc `EffectStatus` để lọc options; hiện có 86 `Available`, 1 `Planned`. `Planned` chỉ đúng metadata trong inventory, không tự ẩn một control UI tương ứng. |
| Test độ phủ | Có kiểm chứng theo **nhóm** và một vài style mẫu, không có bằng chứng tự động cho từng option/ID hoặc parity pixel giữa mọi renderer. |
| AR | Trong 24 family typed: 6 lớp screen-space (4 ambient + shooting stars + petals) là `Native` theo WARP captured-frame check; 17 family giữ `Unverified` — 14 family S5 có calibrated 3D projection đã được sample bằng WARP/full `--verify` trên CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) nhưng chưa phủ mọi style/value/parameter/lifecycle, cộng `falling.trail`/`falling.pulse`/`falling.shimmer` (xem đính chính 2026-10-20 bên dưới: chúng chạy trên quad nốt của AR, không phải trên note roll sân khấu); chỉ backdrop `atmosphere.stars` là `Unsupported` vì `Stars()` chỉ được gọi trong `PsBackground`, mà pass nền bị bỏ khi camera làm nền. Ba note materials pass qua shared `ShadeNoteMaterial` trên camera-facing quads; AR capability và per-setting bindings giờ là `Unverified` (không phải `Fallback`/`Native`). Regression mới sample sáu style trên calibrated quads và Gem/Crystal trên flat AR path, đã PASS trong Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913); full parameter/value matrix và hardware chưa xác minh. Ambient vẫn không qua calibration; không nâng S5 lên `Native` chỉ từ các mẫu hiện có. |
| Recording | Family-capture slice PASS trong Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649), commit `ffcc340`: 24 family fixtures đọc frame 640×360 qua GPU `GpuRecordingTap`; 22 WPF-native family được ghi qua `PngSequenceRecorder`, decode PNG thật và pass pixel-diff. Chỉ `falling.shimmer` và `atmosphere.shooting-stars` là WPF `Unsupported`. Đây là bằng chứng family-presence; parameter-level `Recording` bindings vẫn `Unverified`, recording cost vẫn `Unmeasured`. |
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
- **AR typed pilot** — 24 effect families đã có support state riêng: 6 screen-space composite `Native`, 17 family `Unverified` (14 S5 3D-projection có sample WARP PASS trong run [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) nhưng chưa đủ ma trận; cộng trail/pulse/shimmer vốn vẽ trên quad nốt AR — xem đính chính 2026-10-20), chỉ `atmosphere.stars` `Unsupported`; note materials route qua shared shader trên camera-facing quads nhưng capability/bindings vẫn `Unverified` pending AR-specific matrix. Điều này chưa phủ 87 descriptive IDs. Ambient petals/meteors vẫn flat screen-space. Xem §6.

Đường stage thông thường dùng `PianoStage` cho software/WPF và `GpuLook` + `GpuStageSimulation*` cho GPU. Có chung settings/ý nghĩa không có nghĩa hai ảnh pixel giống nhau; parity cần test riêng. Các mục `Verify*Fx` bên dưới hiện nằm trong `Diagnostics/VerificationSuite.cs` và chỉ chạy khi có môi trường Windows/.NET tương ứng.

## 4. Ma trận inventory 87 mục

> **Cập nhật 2026-10-21 — N12(b):** cột "Độ phủ / lưu ý" bên dưới ghi trạng thái **lúc audit Phase 0**. Từ đợt
> N12(b), từng giá trị của **11 trục option** đã có ảnh riêng trên WPF **và** D3D/WARP ở cùng cảnh, cùng seed và
> cùng thời điểm (`--verify-option-matrix-only`, 128 ảnh, `phase1-visual-review/option-matrix/`). Các dòng còn ghi
> "chưa test riêng option X" vẫn đúng ở mức **mỹ thuật** và ở phần **AR/pose còn lại**, nhưng không còn đúng ở mức
> "renderer có rẽ nhánh theo option hay không" — xem §4.11 và `docs/NOTE-EFFECTS-ACTION-PLAN.md` N12. Từ
> 2026-10-21, N12(d) phần 1 chụp thêm năm trục hiệu ứng qua **ba pose AR đã hiệu chỉnh** (30 ảnh WARP, cổng ≥12 px
> so với ảnh tắt cùng pose), nên câu "option đi qua đường chiếu AR" cũng đã có bằng chứng cho năm trục đó.

### 4.1 Falling — 10 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `falling.glow-trail` | `FallingTrail="Glow"`; `DrawFallingTrail` / `AddNoteTrail`. | Choice; test nhóm falling có chọn `Sparkles`, chưa test riêng Glow. AR: chỉ vẽ trên đường projected (cần pose đã calibration); đường flat không có trail, nên family vẫn `Unverified`. |
| `falling.motion-blur` | `FallingTrail="Blur"`. | Choice; cùng control/renderer trail; chưa test riêng option Blur. AR: như `Glow`, chỉ trên đường projected; `Unverified`. |
| `falling.sparkle-tail` | `FallingTrail="Sparkles"`. | Choice; được chọn trong `VerifyFallingFx`, nhưng test kết hợp pulse/ghost và không chứng minh parity GPU/WPF. |
| `falling.color-gradient` | Không có control/render pass head-to-tail; `ColorMode="Gradient"`/`Palette` → `NoteColorCore` chỉ trải màu theo pitch. | Catalog entry đã được đổi sang `Planned`; giữ nghĩa head-to-tail, không đánh đồng với gradient low-to-high của bàn phím. Cần setting/màu/renderer/test riêng ở Phase 4. |
| `falling.pulsing` | `FallingPulse` + `FallingPulseRate`. | Toggle/value; `VerifyFallingFx` vẽ chung với trail/ghost, chưa cô lập phép đo pulse. |
| `falling.ribbon-twist` | `FallingTrail="Ribbon"`. | Choice; cùng renderer trail với Glow/Blur; “twist” là tên style, không có điều khiển riêng. AR: như `Glow`, chỉ trên đường projected; `Unverified`. |
| `falling.particle-stream` | `FallingTrail="Stream"`. | Choice; chưa test riêng option Stream. |
| `falling.ghost-notes` | `FallingGhost` + `FallingGhostAmount`. | Toggle/value; được vẽ cùng falling sample, chưa assert riêng số/alpha ghost. |
| `falling.speed-lines` | `FallingTrail="Speed Lines"`. | Choice; chưa test riêng option Speed Lines. |
| `falling.rainbow-shift` | `FallingTrail="Rainbow"` đồng thời tô hue thân nốt; tốc độ hue dẫn xuất từ trail intensity. | Shared/alias: trail và body shift ghép vào một lựa chọn; không có speed control riêng. Chưa test riêng. |

### 4.2 Impact — 16 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `impact.burst` | `ImpactBurst` + `ShowEmbers` + particle physics/response; `Impact()` phát burst. | Shared family; không phải lựa chọn thứ sáu ngoài 5 `ImpactBurst` styles. Test sample Confetti, không phải từng style. AR S5 Ultra có projected WARP sample trong `VerifyArNoteProjection`; chưa cô lập đủ năm burst styles/physics values nên family/binding vẫn `Unverified`. |
| `impact.ember-burst` | `ImpactBurst="Embers"` trong cùng burst engine. | Shared/alias với `impact.burst`; style Embers là default. AR S5 Ultra có sample burst/particle counters trên WARP, nhưng Embers chưa được cô lập khỏi cấu hình burst khác; family/binding vẫn `Unverified`. |
| `impact.ripple-ring` | `ShowImpactRings` + `ImpactWave="Ring"` + `RingSize`/intensity. | Choice; `VerifyImpactFx` kiểm geometry với Shockwave, không kiểm riêng Ring. WARP sample pixel checks cover Ripple baseline plus Shockwave/Absorb comparisons at representative settings; Ring and the full size/intensity matrix remain untested, so `impact.wave` stays `Unverified`. |
| `impact.shockwave` | `ImpactWave="Shockwave"`. | Choice; geometry/decay được `VerifyImpactFx` kiểm. AR S5 Ultra có projected shockwave; `VerifyArNoteProjection` now has a WARP pixel comparison for a Shockwave configuration; other choices/ranges and all per-parameter bindings remain `Unverified`. |
| `impact.flash` | `ShowImpactFlash` + `ImpactFlashStyle="Flash"` + intensity. | Choice/toggle; `VerifyImpactFx` kiểm flash geometry/decay, chưa kiểm từng style. The S5 WARP fixture uses Lightning and compares Plasma plus Flash/Morph variants; it does not cover every style/intensity, so family/bindings remain `Unverified`. |
| `impact.key-glow` | `KeyLighting`, `KeyGlowRadius`, `PressedKeyColorMode` và key state. | Shared/alias: không có “impact key glow” selector riêng. S5 WARP checks Standard impact/note-light counters and neighboring-key spill; key tint is a separate pass. Full family/parameter support remains `Unverified`. |
| `impact.splash` | `ImpactBurst="Splash"`; kind droplet/physics riêng. | Choice; chưa có test riêng cho kiểu Splash. S5 projects particle state by pitch/pose in Ultra, but the Splash choice is not isolated by the WARP fixture and remains `Unverified`. |
| `impact.firework` | `ImpactBurst="Fireworks"`; `AmbientEnergy="Fireworks"` là đường khác. | Shared giữa burst theo nốt và layer ambient; chưa test riêng cả hai đường. Burst theo nốt có AR S5 Ultra projection code; ambient fireworks vẫn là screen-space. |
| `impact.confetti-pop` | `ImpactBurst="Confetti"`. | Choice; được chọn trong `VerifyFallingFx` cùng Shatter/Lightning, chưa có ảnh/test riêng. Confetti is enabled in the mixed Ultra WARP fixture and aggregate particle/pixel assertions pass; the burst choice is not isolated from the other hit channels. |
| `impact.dust-cloud` | `ImpactBurst="Dust"`. | Choice; chưa test riêng option Dust. AR S5 projects particles by pose, but Dust has no isolated WARP style assertion and remains `Unverified`. |
| `impact.shatter` | `ImpactMorph="Shatter"` → shard particles. | Choice; sample test gọi cùng Confetti burst; chưa kiểm riêng morphology/lifetime. Shatter is configured in the mixed Ultra WARP fixture; its morphology/lifetime is not isolated, so its style/binding remains `Unverified`. |
| `impact.bounce` | `ImpactMorph="Bounce"` → streak particles + ring. | Choice; chưa test riêng option Bounce. Bounce has 3D particle/ring code, but no isolated WARP style comparison; it remains `Unverified`. |
| `impact.absorb` | `ImpactMorph="Absorb"` → ring co vào điểm hit. | Choice; A WARP pixel comparison exercises the Absorb ring configuration; full morphology/intensity and family bindings remain `Unverified`. |
| `impact.melt` | `ImpactMorph="Melt"` → droplet particles. | Choice; Melt has 3D particle projection code, but no isolated WARP style assertion; it remains `Unverified`. |
| `impact.lightning-strike` | `ImpactFlashStyle="Lightning"`. | Choice; WPF verify sample kiểm một geometry tia sét; The mixed Ultra WARP fixture configures Lightning, while Plasma/Flash comparisons cover other branches; Lightning style/parameter binding is not independently verified. |
| `impact.note-morph` | `ImpactMorph="Morph"`; UI gọi là “Star morph”. | Shared/alias: catalog mô tả nhiều shape (“star, heart…”) nhưng hiện setting chỉ có một style Morph/Star, không có shape picker. A WARP pixel comparison exercises the Morph branch (combined with a Flash variant); the full morphology/intensity matrix remains `Unverified` in `impact.morph`. |

### 4.3 Hold — 8 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `hold.flame-pillar` | `ShowFlame`, `FlameIntensity`, `FlameHeight`, `FlameColorMode`. | Toggle/value cũ; `VerifyHoldFx` không assert riêng flame. Flame is enabled in the Ultra WARP fixture, but its particle counter is aggregated with other emitters; flame-specific settings remain `Unverified`. |
| `hold.sustain-particles` | `ShowWisps` + Wisp amount/speed/height/width/turbulence/glow. | Shared/alias với lớp wisps; chưa có kiểm chứng riêng theo Note On/off. Wisp projection is present in the Ultra aggregate WARP fixture; its trigger/amount/speed/shape bindings are not isolated and remain `Unverified`. |
| `hold.energy-column` | `ShowLightBeams` + `BeamIntensity`; GPU/WPF key beam paths. | Shared/alias; không có `HoldColumnStyle` selector riêng. The Ultra WARP fixture asserts keyed-beam instances at its configured value; beam intensity/quality variants and family bindings remain `Unverified`. |
| `hold.bar` | `HoldBar` + `HoldBarIntensity`. | Toggle/value; được bật trong `VerifyHoldFx` và kết hợp vẽ sounding note. The passing WARP fixture independently toggles HoldBar on projected Standard live bars and compares intensity 0 vs 100; intermediate values, other tiers and the overall family remain `Unverified`. |
| `hold.breathing-glow` | `HoldBreath` + `HoldBreathRate` / `BreathFactor`. | Toggle/value; được bật trong test nhóm, chưa assert riêng chu kỳ/tần số. The passing WARP fixture independently toggles HoldBreath and compares rate 0 vs 100 on projected Standard live bars; intermediate rates and the full family remain `Unverified`. |
| `hold.vibration` | `HoldVibration` + `HoldVibrationAmount`. | Toggle/value; được bật trong test nhóm, chưa assert displacement/giới hạn riêng. The passing WARP fixture independently toggles HoldVibration and compares amount 0 vs 100 on projected Standard live bars; intermediate amounts and the full family remain `Unverified`. |
| `hold.color-cycle` | `HoldColorCycle` + speed. | Toggle/value; được bật trong test nhóm, chưa test màu theo thời gian riêng. WARP fixture independently toggles HoldColorCycle and compares speed 0 vs 100 on projected Standard live bars; intermediate values and the full family remain `Unverified`. |
| `hold.electric-arc` | `HoldElectricArc` + intensity; GPU procedural arc/WPF `DrawElectricArcs`. | Test kiểm geometry giữa hai phím. The Ultra WARP fixture counts projected hold-arc segments, but does not isolate arc intensity/flicker settings; family/parameter support remains `Unverified`. |

### 4.4 Release — 6 mục

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `release.fade-out` | `ReleaseEffect="Fade"`; không spawn thêm hình, dùng fade của note. | Mặc định; không có hiệu ứng phụ riêng để test. S5 chiếu transient release tường minh cho các style khác; Fade không phát hình học riêng. |
| `release.float-up` | `ReleaseEffect="Float Up"` → mote bay lên. | Choice; `VerifyReleaseFx` hiện chỉ chạy Echo Rings. A Dissolve release is read back on WARP with active-release/particle counters and visible pixels; Float Up and its settings remain unverified. |
| `release.dissolve` | `ReleaseEffect="Dissolve"` → hạt tan. | Choice; The Dissolve release WARP assertion passes; other release styles/intensity/lifecycle values are not covered, so family/binding remains `Unverified`. |
| `release.smoke-puff` | `ReleaseEffect="Smoke"` → dust/smoke particles. | Choice; Smoke release has projection code but no isolated WARP style assertion; it remains `Unverified`. |
| `release.snap-back` | `ReleaseEffect="Snap Back"` → ring/co rút. | Choice; Snap Back has release-ring projection code but no isolated WARP style assertion; it remains `Unverified`. |
| `release.echo-rings` | `ReleaseEffect="Echo Rings"` → rings. | Choice; verify song note-end + live release + seek ngược + decay. Echo Rings is present in the 3D code, but the current S5 WARP fixture selects Dissolve rather than Echo Rings; AR style coverage remains `Unverified`. |

### 4.5 Ambient — Particle & Energy (9)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `ambient.embers` | `ImpactBurst="Embers"` / `ShowEmbers`. | Không phải `AmbientEnergy` choice; event-based burst. Alias; the mixed Ultra WARP fixture samples the projected particle channel but does not isolate the Embers alias; remains `Unverified`. |
| `ambient.fire` | `ShowFlame` / flame hold path. | Không có ambient-slot `Fire`; đây là lớp theo phím/nốt. Alias; Flame is enabled in the mixed Ultra WARP fixture but not isolated by style/parameter; remains `Unverified`. |
| `ambient.explosion` | `ImpactBurst` + velocity/particle response. | Event-based burst, không phải ambient slot. Alias; the mixed Ultra WARP fixture samples pitch-tagged particles but does not isolate Explosion/velocity-response values; remains `Unverified`. |
| `ambient.sparkles` | `ShowStars` (starfield) hoặc `FallingTrail="Sparkles"`. | Hai đường khác nhau; không có `AmbientEnergy="Sparkles"`. Alias. |
| `ambient.lightning` | `AmbientEnergy="Lightning Storm"`. | Choice ambient có thật; `VerifyAmbientFx` chọn Fireworks, chưa chọn Lightning Storm. |
| `ambient.laser` | `AmbientEnergy="Laser Beams"`. | Choice ambient có thật; chưa test riêng. |
| `ambient.plasma` | `ImpactFlashStyle="Plasma"`. | Impact flash cục bộ, không có ambient plasma orb selector. Alias/semantic mismatch; Plasma is included in a passing Ultra WARP pixel comparison, but full flash family/parameter coverage remains `Unverified`. |
| `ambient.confetti` | `AmbientEnergy="Confetti Rain"` hoặc `ImpactBurst="Confetti"`. | Hai trigger khác nhau; catalog chưa phân biệt layer và impact. Ambient rain remains screen-space; the note-triggered Confetti burst is sampled in the mixed Ultra WARP fixture, not as a separate style contract. |
| `ambient.firework` | `AmbientEnergy="Fireworks"` hoặc `ImpactBurst="Fireworks"`. | Hai trigger khác nhau; catalog chưa phân biệt layer và impact. Ambient fireworks remain screen-space; the note-triggered Fireworks burst has projection code but no isolated S5 WARP style assertion. |

### 4.6 Ambient — Nature & Elements (9)

| ID | Mapping runtime hiện tại | Độ phủ / lưu ý |
|---|---|---|
| `ambient.petals` | `ShowPetals` + `PetalAmount` + `PetalColor`. | Có renderer riêng, nhưng không nằm trong `AmbientNature` choice. |
| `ambient.ripple` | `ImpactWave="Ripple"`. | Effect impact tại phím, không phải ambient layer tự chạy. Ripple is present in the mixed Ultra WARP impact comparison; it remains key-anchored, not ambient perspective projection, and the full wave parameter matrix is `Unverified`. |
| `ambient.rain` | `AmbientNature="Rain"`. | Choice thật; chưa test riêng. |
| `ambient.snow` | `AmbientNature="Snow"`. | Choice thật; được chọn trong `VerifyAmbientFx` như một phần composite. |
| `ambient.smoke` | `AmbientNature="Smoke"` và `ReleaseEffect="Smoke"`. | Hai trigger/renderer context khác nhau; tên catalog chưa tách. Nature smoke remains screen-space; the S5 release WARP case selects Dissolve, so Smoke release style remains `Unverified`. |
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

### 4.11 Ma trận option N12(b) — 11 trục, 128 ảnh

Trước đợt này, mỗi family chỉ được kiểm bằng **một** style chọn tay (Confetti cho burst, Shatter cho morph,
Sparkles cho trail…), nên câu "chưa test riêng option X" trong §4.1–4.8 là đúng: chưa có ảnh nào chứng minh
chọn giá trị khác thì **có** pixel khác. Ma trận này bịt đúng khoảng đó, và **chỉ** khoảng đó.

| Trục setting | Giá trị | Cảnh / mẫu | Kết luận sau N12(b) |
|---|---|---|---|
| `NoteStyle` | 6 | nốt 60 giữ 2,0 s · mẫu 1,5 s | 6/6 có ảnh riêng; 5 giá trị khác baseline `Solid` và khác nhau từng cặp |
| `FallingTrail` | 8 | nốt 60 giữ 2,0 s · mẫu 1,5 s | 7/7 style có ảnh; baseline `None` không vẽ trail |
| `ImpactWave` | 4 | `Impact(60, 0,756)` · mẫu +0,117 s | Ring / Shockwave / Ripple khác baseline `None` và khác nhau |
| `ImpactBurst` | 5 | `Impact(60, 0,756)` · mẫu +0,117 s | 5/5 style; baseline = `ShowEmbers=false` |
| `ImpactMorph` | 6 | `Impact(60, 0,756)` · mẫu +0,117 s | Shatter / Melt / Absorb / Bounce / Morph khác `None` và khác nhau |
| `ImpactFlashStyle` | 3 | `Impact(60, 0,756)` · mẫu +0,117 s | Flash / Lightning / Plasma; baseline = `ShowImpactFlash=false` |
| `ReleaseEffect` | 6 | press khung 0, release khung 8 · mẫu 0,267 s | 5/5 hiệu ứng khác baseline `Fade` và khác nhau |
| `AmbientEnergy` | 5 | không input · mẫu 1,5 s | 4/4 có ảnh; baseline `None` |
| `AmbientNature` | 8 | không input · mẫu 1,5 s | 7/7 có ảnh; baseline `None` |
| `AmbientLight` | 5 | không input · mẫu 1,5 s | 4/4 có ảnh, gồm `Spotlights` (control ngoài 87 ID); baseline `None` |
| `AmbientCosmic` | 6 | không input · mẫu 1,5 s | 5/5 có ảnh; baseline `None` |

Cách chạy, cách đọc artifact và phần ghim: `docs/OPTION-MATRIX-N12B.md`.

Tổng: **62 giá trị**, trong đó **9 giá trị chính là trạng thái tắt** (nên baseline của chúng không chụp lại), cộng
**11 baseline** ⇒ **64 cảnh × 2 renderer = 128 ảnh** 960×540, kèm `manifest.json` (sha256 từng
ảnh, số pixel đổi so với baseline, bảng so từng cặp, counter hạt/ambient/trail của GPU) và `index.html`.

Bốn điều kiện làm lượt chạy **đỏ**, tất cả đều là bất biến của renderer chứ không phải ý thích:

1. một giá trị đổi **<12 px** so với baseline của chính nó ⇒ option được settings nhận nhưng không vẽ;
2. hai giá trị trong cùng trục cho ra **cùng một khung hình** ⇒ hai tên, một hình;
3. baseline của các family **chung một cảnh** khác nhau **dù chỉ một bit** ⇒ renderer không tất định, hoặc option
   này rò rỉ sang cảnh của option khác (mọi family đều tắt trong baseline, nên chúng phải ra cùng ảnh);
4. thiếu ảnh, thiếu hash, hoặc số ảnh khác 128.

**Lượt chạy đầu tiên tìm ra một lỗi thật (2026-10-21).** CI của chính ma trận đỏ ở `impact-morph/Shatter`:
**0 px** trên WPF. Nguyên nhân không nằm ở ma trận — stage **phần mềm** vẽ hạt/ring/flash qua ba danh sách
dùng chung, mà mỗi danh sách chỉ được vẽ khi công tắc của lớp đó bật (`ShowEmbers`, `ShowImpactRings`,
`ShowImpactFlash`). Vì vậy Shatter/Melt/Bounce (hạt), Absorb (ring) và Morph (flash) **biến mất** nếu người
dùng tắt Sparks/Rings/Flash, còn stage GPU vẫn vẽ chúng: vừa là lỗi parity WPF↔GPU, vừa là một option "được
settings nhận nhưng không vẽ". Bản sửa nằm ở sản phẩm, không ở harness: `Stage/Effects/EffectChannel.cs` gắn
chủ sở hữu cho từng hạt/ring/flash (`Layer`/`Morph`/`Release`); ba đường vẽ chỉ ẩn hiệu ứng `Layer` khi công
tắc của chính nó tắt; ring lấy độ sáng qua `KeyRingChannelGain` như GPU (ring morph theo slider morph, ring
release mang sẵn intensity lúc sinh). Morph và release là hai family **không có công tắc riêng**, nên chúng
được chụp với **mọi** công tắc lớp tắt — mỗi khung của hai family đó đồng thời là bằng chứng "option tự vẽ".
`tools/check_sources.py` ghim cả bốn đường vẽ, phép chia độ sáng và 6+6 điểm sinh hiệu ứng, để lỗi này không
quay lại mà không cần Windows.

**Không** chứng nhận: mỹ thuật/duyệt hình, AR (chưa có pose/camera), đường MIDI/CC64 thật, các quality tier khác,
FPS phần cứng, hay thay đổi mặc định. `tools/check_sources.py` (`scan_option_matrix`) ghim danh sách giá trị của
plan **bằng đúng** mảng trong `PianoVisualSettings`, nên thêm một style mà quên capture là làn tĩnh đỏ ngay.

## 5. Khoảng trống ưu tiên

### P0 — quyết định đã chốt và yêu cầu trước typed runtime registry

1. **Quyết định nguồn dữ liệu — đã chốt cho Phase 0:** các khai báo settings/default/normalize và danh sách lựa chọn UI hiện có tiếp tục là nguồn runtime; `EffectCatalog` được giữ đúng vai trò inventory/metadata cho audit/diagnostics, không lọc UI và không chứng minh capability. Chỉ đưa một typed registry vào runtime sau khi Phase 1 chuẩn hóa ID/alias/capability; không nối `EffectStatus` vội.
2. **Đã sửa và verify WPF path `AmbientLight=Spotlights`.** Renderer ba shaft/pool theo màu, tốc độ và activity cùng kiểm tra zero-intensity, bounds và pixel raster đã PASS trong Windows CI run `37467081020`. Phạm vi kết luận chỉ là WPF/WARP test; parity pixel trên GPU thật chưa được đo.
3. **AR và Recording capability theo typed family — cập nhật theo bằng chứng WARP.** `EffectContractCatalog` ghi trạng thái riêng cho 24 effect families: sáu ambient pass-through là `Native` chỉ trong screen-space camera composite; 14 family S5 đã qua selected WARP readback trong CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330), gồm Standard/Ultra, các impact variant, Dissolve release, pose/direction và bốn hold modifier với giá trị tham số 0/100. Family/parameter AR vẫn `Unverified` vì chưa phủ mọi choice, range, quality/lifecycle; ba family falling (`falling.trail`, `falling.pulse`, `falling.shimmer`) cũng ở `Unverified` vì chúng vẽ trên quad nốt của AR, và chỉ backdrop `atmosphere.stars` còn `Unsupported`. Ba note materials dùng shared `ShadeNoteMaterial` trên camera-facing geometry; contract state và mọi AR setting bindings hiện là `Unverified`, không được nâng thành `Native` từ code alone. WARP checks mới sample sáu style branches trên calibrated quads và Gem/Crystal trên flat AR path, PASS trong Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913) ngày 2026-10-20; full parameter/value matrix và hardware vẫn chưa xác minh. `VerifyArOverlayCapturedFrames` vẫn là bằng chứng riêng cho ambient/suppression; S5 đã được xác minh bởi các case cũ trong `VerifyArNoteProjection`; các case mới cũng đã chạy trong CI 39294579913. AR typed pilot chưa phủ toàn bộ 87 ID descriptive; ambient không qua calibration. Recording family capture PASS trong Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649): 24 fixtures có GPU tap pixel difference riêng; 22 WPF-native fixtures còn được ghi/đọc PNG và pass pixel-diff. Hai GPU-only families không bị suy diễn sang WPF. Recording evidence **không** chứng minh từng parameter binding, mọi style/value hay recording cost: binding vẫn `Unverified`, chi phí vẫn `Unmeasured`.
4. **Đã chốt nghĩa `falling.color-gradient`:** giữ yêu cầu chuyển màu đầu–đuôi; `ColorMode="Gradient"` hiện tại là gradient theo pitch và không thay thế được. Entry đã chuyển sang `Planned`; renderer/settings/test dọc thân nốt được đưa vào Phase 4, không làm lẫn hai hành vi.

### P1 — độ rõ của sản phẩm/kiểm chứng

5. Phân biệt event-triggered `Embers/Fire/Explosion/Plasma/Water Ripple` với ambient chạy nền, đồng thời giữ alias cũ khi migrate preset.
6. `Spotlights` là setting/UI choice bổ sung ngoài 87 ID hiện tại; WPF path và regression đã PASS trên CI. Trong Phase 2 ID normalization, quyết định có thêm ID explicit hay gộp vào Ambient Light capability; đồng thời lập mapping/verification cho `ShowStars`, `ShowPetals`, Shooting Stars, note landing glow, note shimmer, halo pulse và background motion — các control tồn tại nhưng không thuộc inventory 87 hiện tại.
7. Mở rộng verify từ “một mẫu mỗi nhóm” sang từng Choice value tối thiểu: spawn/draw/decay, màu/size/speed/amount, disabled, seek/reset, preset round-trip. **Đã làm một nửa (N12(b), 2026-10-21):** 11 trục setting dạng chuỗi có ảnh riêng cho **mọi** giá trị trên WPF và GPU/WARP (128 ảnh, §4.11); phần **giá trị số** (amount/speed/intensity/size), seek/reset và round-trip preset vẫn còn.
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
| `VerifyArNoteProjection` | Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) PASS on the earlier source revision: calibrated Standard/Ultra, selected impact variants, Dissolve release, transformed pose/direction, and four hold modifiers with representative 0↔100 parameter pixel deltas. The current local branch adds note-colour/material/shimmer, released-bar lifetime, Lite/Standard/Ultra pixel parity for note and live-bar geometry, age-zero first-frame Perform onset/direction, and held-bar default-opacity WARP assertions; all of them pass in Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913) (2026-10-20). | Only sampled styles/values; not all 14 families/parameter bindings. Those assertions ran on WARP in 39294579913; the remaining gap is per-value/per-binding coverage, and real webcam, OBS, physical GPU and performance are not verified. |
| `VerifyEffectFamilyGpuRecordingCaptures` / `VerifyEffectFamilyWpfRecordingCaptures` | Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649) PASS: 24/24 GPU tap families có changed-pixel evidence; 22/22 WPF-native families ghi PNG rồi decode/read back và pass pixel-diff. Hai WPF-only exclusions được assert chính xác. | Chỉ family-level presence trên WARP + WPF software-stage fixture; mọi `parameter.Bindings.Recording` vẫn `Unverified`, mọi `Cost.Recording` vẫn `Unmeasured`; không chứng nhận GPU phần cứng. |
| `--verify-option-matrix-only` (N12(b)) | Ma trận option: 11 trục setting × mọi giá trị (62) + 11 baseline = 64 cảnh × WPF/GPU‑WARP = **128 ảnh 960×540**, cùng cảnh/cùng seed; mỗi giá trị đổi ≥12 px so với baseline, mọi cặp trong một trục khác ảnh, baseline của các family chung một cảnh trùng nhau **từng bit**; `manifest.json` (sha256, số pixel đổi, bảng so cặp, counter GPU) và `index.html` trong `phase1-visual-review/option-matrix`. Đường tắt và `--verify` đầy đủ đều chạy nó; `tools/check_sources.py` ghim danh sách giá trị vào `PianoVisualSettings`. | Đã phủ thêm AR/pose phần 1 (N12(d), `--verify-option-pose-only`): 5 trục hiệu ứng × 3 pose × tắt/bật = **30 ảnh WARP 1280×720**, artifact `option-pose-n12d`. Chưa phủ: phần còn lại của AR/pose (nốt, ambient), các giá trị **số** (amount/speed/size/intensity), smart modulator, theme graph, GPU phần cứng, duyệt mỹ thuật hay thay đổi mặc định. |
| `tests/PianoPath.Tests` | Portable test project PASS cùng Windows build/`--verify` trong run [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330). | Không có project test riêng cho toàn bộ lifecycle effect renderer. |

### AR capability baseline (source audit)

| Capability | Hiện trạng có thể khẳng định từ code | Ranh giới / chưa được chứng minh |
|---|---|---|
| Điều kiện chạy | `ArPlan.Decide` yêu cầu GPU hoạt động, camera/video source và calibration hợp lệ; software fallback trả `NeedsGpu`. Chroma-green background loại trừ camera AR. | Không có đường AR dựa trên software renderer. |
| Camera & mapping | Camera ring → GPU texture; `ArView` điều khiển Fit/Fill + mirror; homography neo calibration trên mặt phím, còn `ArCameraPose` (FOV + full 3D transform) chiếu note/effect billboards ngoài mặt phẳng. | Kiểm tra hình ảnh trên camera/webcam thật tùy thiết bị; verify dùng WARP và khung synthetic. |
| Phím thật | `GpuArLayer` lấy glow/color từ simulation rồi gọi `ArKeys.Patches` để chiếu phần nhìn thấy của key trắng/đen thành quad; có `KeyLight`/opacity/delay. | Là light overlay trên ảnh, không phải dựng lại phím 3D hay che khuất theo bàn tay tự động. |
| Nốt Learn | `Ar3DNoteProjection.Build` tạo projected 3D note quads theo full camera pose + note direction riêng khi Notes in perspective bật; `ArNotes.Build` là fallback flat; màu `GpuLook.NoteColor`, played/missed, fade, fall speed/reach/length/gap/width và min-aspect vẫn được áp dụng. AR và Live Stage giờ dùng chung `ShadeNoteMaterial`; màu AR đi qua cùng `ToLinear`, còn shimmer, các FallingTrail variants, FallingGhost copies, NoteLandingGlow và note-name atlas glyphs được đặt trên calibrated 3D note/key patches thay vì sprite phẳng. WARP assertions mới cho linearized color, Glass/Gem, shimmer, Glow/Stream trail, ghosts, landing glow, note-name atlas glyphs, live-bar release, pixel parity giữa ba manual quality tiers và held-bar defaults PASS trong Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913). |
| MIDI Note On / Perform | `ArRisingMidiNotes` → `ArOptions.ShowsRisingMidiNotes` (Perform only) → `GpuArInput.ShowRisingMidiNotes` → one state per `GpuStageFeed.LiveNote` Note On → `GpuStageSimulation.ArRisingMidiNotes` → `Ar3DNoteProjection.BuildRisingMidiNotes` → shared AR note material. Each same-pitch onset remains a separate object; pitch, velocity width/fade, note colour, hold settings and selected 3D direction are retained. Note Off freezes the held-duration body, not travel; the reach is a fixed three seconds independent of Learn lookahead. Core note objects are pose-projected at Lite/Standard/Ultra when calibrated; this does not loosen transient family tier gates. Geometry/unit tests plus WARP checks for Lite first-frame age-zero onset, outward direction, quality parity and repeated-pitch/enable-disable behavior pass in Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913); no `Native`/family status is claimed. |
| Stage lifecycle effects | `GpuStageRenderer` chặn stage-space notes/trails, guide lanes, virtual key bodies và sprites khi camera thực sự là background, kể cả NeedsCalibration/Outdated; `BuildAmbient` nhận camera-active state riêng nên không dựa vào việc note pass đã xóa trail list. Screen-space ambient vẫn chạy phẳng. Khi calibration Ready, `GpuLook.From(..., overCamera: true)` còn giữ cấu hình + pitch-tagged simulation state để `GpuArLayer` chiếu theo full 3D pose. Code S5 chiếu impact/note lights ở Standard; Ultra thêm rings/morphs/flashes, impact + continuous particle families, release FX, held-key arcs và keyed beams. | Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) PASS build/full `--verify`/WARP: Standard/Ultra hit/hold channels, selected impact variants, Dissolve release, transformed-pose/direction independence và từng HoldBar/Breath/Vibration/ColorCycle toggle với tham số 0/100 trên live bars. 14 family/bindings vẫn `Unverified` vì chưa đủ ma trận để gọi `Native`; webcam thật, OBS, GPU phần cứng và performance chưa xác nhận. Lite/Standard/Ultra giữ giới hạn catalogue; không tuyên bố parity cho `Gem`/`CrystalOutline`. |
| Ambient / screen-space | GPU `BuildAmbient` composited lên ảnh camera: bốn ambient slots, shooting stars và petals đã được bật riêng, render và đọc lại thành frame WARP; sáu family này `Native` riêng cho screen-space overlay. | Không perspective-project qua calibration. Backdrop stars vẫn `Unsupported`; key-anchored beams có counter assertion trên WARP trong S5 Ultra nhưng family/parameter vẫn `Unverified` vì chưa có ma trận riêng; không phải ambient pass-through. Guide lanes chưa thuộc family typed này. |
| Nốt / trail / shimmer trên AR | `GpuArLayer.AddSongNote` áp `FallingPulse` và gọi `AddNoteShimmer` cho quad nốt ở **cả hai** đường flat và projected; trail/ghost/landing glow/note name chỉ được thêm trong `BuildProjectedSongNoteEffects`, tức chỉ khi có `ArCameraPose`. `Stars()` chỉ xuất hiện trong `PsBackground`, còn `GpuStageRenderer` bỏ pass nền khi `cameraActive` hoặc `alphaOutput`. | Ba family falling vì vậy là `Unverified` (có đường vẽ, chưa có ma trận theo giá trị), không phải `Unsupported`; backdrop stars là family duy nhất thật sự không bao giờ vẽ đè camera. Không có chứng minh cho webcam/GPU thật. |
| Verify hiện có | `VerifyArCameraLayer`, `VerifyArOverlayExclusions`, `VerifyArOverlayCapturedFrames`, `VerifyArRecording`, `VerifyArDelay`, `VerifyArLatency` và assertions trong `VerifyArNoteProjection`; Windows CI [38014563330](https://github.com/lxmtuu/PianoPath/actions/runs/38014563330) PASS cho calibrated Standard/Ultra, impact variants, Dissolve release, pose/direction independence và các hold modifier/representative 0↔100 parameter deltas. Các regression mới cho note-color/material/shimmer/trails/ghosts/landing glow/atlas labels, parity geometry/pixel giữa Lite/Standard/Ultra, tuổi 0 của nốt Perform ở frame đầu, hướng đi ngược mũi tên Learn và held-bar defaults/release đều PASS trong Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913). `VerifyArOverlayCapturedFrames` là test riêng cho sáu screen-space families/suppression, không phải bằng chứng cho mọi S5 option. Note materials có read-back riêng qua WPF PNG + GPU `GpuRecordingTap`. Regression WARP sample sáu style branch trên calibrated quads và Gem/Crystal trên flat AR path PASS trong Windows CI [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913) (bản ghi `39173459709` trước đó là run **bị huỷ**, không phải bằng chứng PASS). Recording family slice PASS trong Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649): 24 non-material families qua GPU tap, 22 WPF-native families qua PNG write/read và changed-pixel assertions. | Gem/Crystal vẫn rasterize trên AR camera-facing quads (không phải mesh stage); shared shader paths có mặt, nhưng material capability và setting bindings giữ `Unverified` cho tới khi CI/style-value matrix xác nhận. Family-level Recording `Native` chỉ khẳng định frame đại diện; không phủ mọi style/value, parameter binding, độ phân giải dài hạn hay phần cứng thật. Binding recording từng parameter vẫn `Unverified`, chi phí ghi `Unmeasured`; CrystalOutline/NoteRefraction còn khác biệt WPF/GPU có chủ ý: với Crystal Outline GPU bỏ hẳn tham số (chỉ WPF vẽ fringe + sheen), còn Solid/Satin/Glass/Gem đổi cả hai và chỉ hình viền là WPF-only (NOTE-EFFECTS-ACTION-PLAN §3.6). |

### Đính chính 2026-10-20 — ba family falling không còn `Unsupported` ở AR

Bản audit trước ghi `falling.trail`, `falling.pulse`, `falling.shimmer` là `Unsupported` trên camera composite, dựa trên một sự thật chỉ đúng với note roll của sân khấu: khi camera làm nền, `GpuStageRenderer` bỏ note pass. Nhưng AR có đường vẽ riêng cho nốt, và đường đó **có** hai family sau:

- `GpuArLayer.AddSongNote` gọi pulse cho mọi quad nốt AR (cả flat lẫn projected) và `AddNoteShimmer` cho cùng quad đó. WARP đã có case shimmer (100 so với 45) và trail Glow/Stream, ghost, landing glow, tên nốt trên đường projected; pulse mới chỉ được kiểm tra là còn nguyên setting. Vì chưa phủ nhánh flat và mọi giá trị, trạng thái đúng là `Unverified`, không phải `Unsupported`.
- `falling.trail` chỉ được thêm trong `BuildProjectedSongNoteEffects`, tức là **có** trail/ghost/landing glow/note name khi nốt được chiếu bằng `ArCameraPose`, và **không có** gì trong chế độ flat. Vì vậy trạng thái phụ thuộc đường vẽ, và `Unverified` là mức trung thực.
- Backdrop stars thì khác: `Stars()` chỉ nằm trong `PsBackground`, mà pass đó bị bỏ khi `cameraActive` hoặc khi xuất nền trong suốt. `VerifyArOverlayCapturedFrames` đã khẳng định bật `ShowStars` không đổi một byte nào trên ảnh camera. Gia đình này thật sự không bao giờ vẽ đè camera → vẫn `Unsupported`, và đây là family duy nhất ở trạng thái đó (6 `Native`, 17 `Unverified`, 1 `Unsupported`).

UI phản ánh đúng ranh giới này ở card **3D NOTE PATH**: dòng "The AR overlay leaves out …" liệt kê trail, ghosts, landing glow, tên nốt (chỉ khi đang ở đường flat) và trường sao nền. Shimmer và pulse cố ý không nằm trong danh sách. Test đi kèm: `ArOmittedEffectsTests` (xUnit) và assertion N12 trong `--verify`.

### Recording family capture evidence — Windows CI PASS

Các fixture ở `Diagnostics/EffectRecordingFixtures.cs` được nối vào suite: mỗi family so sánh frame trung tính với frame chỉ bật đúng một họ. Windows CI [37762349649](https://github.com/lxmtuu/PianoPath/actions/runs/37762349649), commit `ffcc340`, PASS toàn bộ các assert pixel/read-back này. Đây là bằng chứng cho frame đại diện của từng family, không phải xác minh mọi setting/style hay chi phí ghi:

| Nhóm | Số family | Fixture đại diện | Đường read-back |
|---|---:|---|---|
| Impact | 4 | Shockwave, Lightning, Confetti burst, Shatter | WPF PNG + GPU `GpuRecordingTap` |
| Falling | 3 | Sparkles/ghost, pulse, shimmer | WPF PNG + GPU tap cho trail/pulse; shimmer chỉ GPU tap vì WPF `Unsupported` |
| Hold | 5 | bar, breath, vibration, color cycle, electric arc | WPF PNG + GPU tap |
| Ambient/atmosphere | 8 | Confetti Rain, Snow, Prism, Galaxy, stars, shooting stars, petals, beams | WPF PNG + GPU tap; shooting stars chỉ GPU tap vì WPF `Unsupported` |
| Particles | 3 | sparks, wisps, flames | WPF PNG + GPU tap |
| Release | 1 | MIDI note-off → Echo Rings | WPF PNG + GPU tap |
| **Tổng** | **24** | Mỗi ID đúng một fixture; từng frame effect được đối chiếu với neutral frame cùng scene | **24 GPU tap + 22 WPF PNG** |

GPU fixtures chạy trên WARP và đọc frame 640×360 từ đường take off-screen thật; mỗi neutral baseline được ghép với frame đã bật một family, và từng fixture phải đạt ngưỡng changed-pixel riêng. WPF fixtures chạy software-stage renderer (không lấy cached GPU preview), ghi hai frame 640×360 vào `PngSequenceRecorder`, decode PNG thật rồi assert tối thiểu 8 pixel có tổng channel delta >24. Thư mục capture chỉ là fixture tạm trong `%TEMP%` và bị xóa sau verify. `falling.shimmer` và `atmosphere.shooting-stars` vẫn WPF `Unsupported`; không nâng sang `Native`. CI xác nhận các output này trên WARP và WPF synthetic fixture, không phải GPU phần cứng hay hiệu năng thực tế. Bằng chứng giữ family-level `Recording=Native` cho 24 family; nó không xác minh từng setting/alias/style hoặc đo chi phí ghi. Mọi `parameter.Bindings.Recording` tiếp tục `Unverified`, mọi `Cost.Recording` tiếp tục `Unmeasured`.

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
