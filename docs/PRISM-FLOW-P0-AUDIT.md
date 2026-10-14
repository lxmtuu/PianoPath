# Prism Flow P0 — Inventory và chuẩn bị baseline Live Stage / AR

> Ngày: 2026-10-14. Trạng thái: **đã có baseline 78 ảnh WARP và full verification PASS trong CI; P0 vẫn còn các gate rộng hơn**.
> Kế hoạch: [Prism Flow Design Roadmap](PRISM-FLOW-DESIGN-ROADMAP.md).
> Chưa thay renderer, shader, preset, schema settings, capability hay menu UI. Đã có bộ baseline P0 78 ảnh Stage/AR trên WARP; chưa có số đo FPS/GPU phần cứng hoặc sign-off của người dùng. Xem §9.

## 1. Đầu ra đã thực hiện

- Bản đồ event → simulation → render/output, kèm điểm cần xác minh.
- Inventory các lựa chọn nhấn/giữ/thả và FX liên kết từ `PianoVisualSettings`, `GpuLook` và simulation runtime.
- Generator không dependency: `tools/make_prism_flow_fixture.py`, xuất MIDI format 1 và manifest 28 giây; dữ liệu đầu ra nằm trong `tools/out/` đã được Git ignore.
- Bảy Python tests kiểm tra input fixture và pin hai file nhỏ trong `docs/samples/prism-flow-p0` theo generator. Đây **không** phải test parser hoặc renderer của ứng dụng.
- `Diagnostics/PrismFlowBaseline.cs` consume manifest, xác minh hash và đối chiếu mọi note với **MidiReader của sản phẩm**; cursor pedal tách khỏi note duration. Portable xUnit có 10 case mới cho reader/cursor/invalid input; đã pass trong portable test job của Windows-CI workflow (job chạy Linux); sandbox vẫn không có SDK.
- `Diagnostics/PrismFlowVisualReview.cs` chuẩn bị 78 ảnh WARP stage/AR (3 control presets × 12 checkpoint × 2 mode, thêm 6 phase-FX-off controls), PNG readback/round-trip, manifest và HTML. Đã build và render PASS trên Windows trong run `38595904297`; 78 PNG qua round-trip, kiểm hình học và effect-off controls. Không phải chứng nhận parity hoặc hardware.
- Đưa tests/generate/check vào static CI; không cần Windows để kiểm input. Không thay thế portable xUnit hay Windows `--verify` đang có.

## 2. Sơ đồ đường dữ liệu hiện tại

```text
Live MIDI callback
  → MainWindow.ForwardMidiToGpu: LiveNote + Impact
  → UI replay đặt SuppressGpuForward để không queue lần hai
Song / pressed state / sustain / visual settings
  → PianoStage.Gpu → GpuStageFeed (state, event queues, immutable GpuLook)
  → GpuRenderLoop: simulation.Step một lần trong vòng render chính
      → Live Stage: note/key/sprite instances
      → AR: GpuArLayer.Build + BuildStageEffects/BuildStageParticles
            → projected quads theo calibration/pose và quality gate
      → embedded/window/recording/alpha output lấy cùng simulation
```

Điểm tựa nguồn:

- `Ui/MainWindow.Gpu.cs`: `ForwardMidiToGpu`; `Ui/MainWindow.xaml.cs`: replay/suppression.
- `Stage/PianoStage.Gpu.cs`: `AttachGpu`, `PublishGpuLook`, `GpuForward`.
- `Gpu/GpuStageFeed.cs`: look snapshot, state, event queue và sustain level.
- `Gpu/GpuRenderLoop.cs`: `Run` (vòng render chính), các đường offline `Step` riêng.
- `Gpu/GpuStageSimulation.cs`: `Step`, `Impact`, `Clear`, held emitters, particle pool.
- `Gpu/GpuArLayer.cs`: `Build`; `Gpu/GpuArLayer.Effects.cs`: `BuildStageEffects`, `BuildStageParticles`, `TryAddProjectedParticle`.

**Giới hạn kết luận:** một lần Step trong vòng render chính là bằng chứng cấu trúc, không chứng minh mọi tình huống nhiều output, reconnect hoặc chuyển mode đã đúng. AR overlay hiện có thể ẩn layer stage-space; không coi đây là hỗ trợ hai look độc lập cùng lúc. Cần test các topology thực sự ứng dụng cho phép.

## 3. Inventory bắt buộc

Ký hiệu `Có code / Chưa đo`: có nhánh nguồn cho chức năng; chưa có evidence mới về hình ảnh, parity, parameter, AR thật hay hiệu năng. Trạng thái capability chính thức vẫn theo contracts/audit hiện hành, không thay bằng bảng này.

| Nhóm | Lựa chọn / công tắc | Điểm thực thi | Trạng thái P0 / bước kế |
|---|---|---|---|
| Burst | Embers | `Impact`; shape dot/streak | Có code / Chưa đo; review màu, lõi, hướng và density hai mode |
| Burst | Splash | `Impact`; shape droplet, gravity riêng | Có code / Chưa đo; review glint/giọt, không ép thành neon |
| Burst | Fireworks | `Impact`; hướng tỏa và hue variation | Có code / Chưa đo; tách khỏi ambient Fireworks |
| Burst | Confetti | `Impact`; shape confetti và drag | Có code / Chưa đo; giữ đọc như giấy, kiểm rotation/alpha |
| Burst | Dust | `Impact`; shape dust | Có code / Chưa đo; kiểm opacity/softness trên camera |
| Burst zone | `ZoneSplit` → `ImpactBurst="Zone"` trong `GpuLook.From` | Chọn Embers/Splash theo pitch ở impact/hold | Có code / Chưa đo; nhánh runtime không phải style thứ sáu trong array UI |
| Held burst family | Embers / Splash / Fireworks / Confetti / Dust / Zone | Vòng continuous emitters trong `Step` | Có code / Chưa đo; kiểm nhấn → giữ → thả cho từng style |
| Held wisps | `ShowWisps`, WispAmount/Speed/Height/Width/Turbulence/Glow | Budget riêng; shape dot có cờ Wisp | Có code / Chưa đo; noise hiện chưa là curl field 2D dùng chung |
| Held flames | `ShowFlame`, FlameIntensity/Height/ColorMode | Heat + flame budget, shape flame | Có code / Chưa đo; có heat tail khi không còn active |
| Impact morph | None / Shatter / Melt / Absorb / Bounce / Morph | `SpawnImpactMorph` | Có code / Chưa đo; shard/droplet/streak hoặc ring/star, không gọi tất cả là particle |
| Impact wave | None / Ring / Shockwave / Ripple; `ShowImpactRings` | Ring list + stage/AR wave build | Có code / Chưa đo; review trên mặt phím, tách khỏi hạt |
| Impact flash | Flash / Lightning / Plasma; `ShowImpactFlash` | Flash list, `AddFlashSprites`, AR flashes | Có code / Chưa đo; kiểm flash risk/reduced motion |
| Hold accents | HoldBar, HoldBreath, HoldVibration, HoldElectricArc | Shared look + stage effects / projected AR paths | Có code / Chưa đo; test riêng từng modifier và tổ hợp |
| Key/hit accents | KeyLighting, KeyGlowRadius, Halo, HaloPulse, PedalGlow | Key colors/spill, hit line, AR note/impact lights | Có code / Chưa đo; không giả định hit line screen-space có nghĩa vật lý giống AR |
| Release | Fade | Không spawn trong `SpawnReleaseFx` | Hành vi trung tính; test không sinh hạt mới |
| Release particles | Float Up / Dissolve / Smoke | Droplet/dust particles với Origin=1 | Có code / Chưa đo; kiểm note-off/pedal/camera contrast |
| Release rings | Snap Back / Echo Rings | Rings với Origin=1 | Có code / Chưa đo; không gọi là particle |
| Falling liên quan | None / Glow / Sparkles / Speed Lines / Blur / Ribbon / Rainbow / Stream | Note trails, projected song/live effects | Kiểm regression/capability theo đường; không mặc định toàn bộ projected như nhau |

Mỗi hàng chứa nhiều lựa chọn phải được tách thành **case riêng cho từng lựa chọn** khi viết capture runner; bảng nhóm không thay ma trận nghiệm thu. Cần bao gồm intensity=0, toggle off, min/default/max hợp lệ và lifecycle. Trước khi giữ nguyên/nâng cấp style, lưu ảnh/clip baseline riêng của style đó.

## 4. Khoảng cách và rủi ro cần xử lý trước art pass

### P0-01 — Physical-held khác sounding/sustain

Trong `GpuStageSimulation.Step`, `_active` được tổng hợp từ khoảng Start/End của bài và `input.Pressed`. Held sparks/wisps kiểm `_active`; flame còn dùng `_heat`. `SetSustainLevel` truyền sustain nhưng không tự chứng minh `_active` có nghĩa sounding state.

Trong `Midi/MidiFileReader.cs`, reader đọc channel messages nhưng chỉ tạo note intervals từ note-on/off; `MidiSong` chưa có controller timeline. CC64 trong file **không** trở thành pedal timeline cho harness bằng cách đơn giản mở MIDI.

**Hành động:** chốt semantics từng emitter (physical-held / sounding / heat tail). Harness tương lai cần đọc manifest và inject sustain qua API đúng, hoặc mở rộng song controller model bằng một thay đổi có tests riêng. Không kéo dài tất cả note duration giả để che khác biệt này.

### P0-02 — Camera delay chưa chứng minh đồng bộ toàn bộ FX

`GpuArLayer.Build` dùng `input.Position - DelaySeconds` cho song notes. Vòng render chính dùng `ArKeyHistory` cho màu phím trễ. `BuildStageParticles` lấy particle snapshot hiện tại của simulation; chưa thấy cùng một history offset được áp dụng tại đường này.

**Hành động:** đo cùng onset ở notes/key tint/burst/held/release trong AR với delay=0 và khác 0. Chọn chính sách timeline rõ ràng, không delay audio/Live Stage để chữa AR. Đây là rủi ro cần test, chưa kết luận mọi camera đều lệch.

### P0-03 — Clock, seed và reset chưa đủ cho replay so sánh FPS

`Step` clamp dt ở 0.05 s, simulation time tiến theo frame dt; constructor có seeded Random nhưng số lần lấy random phụ thuộc emission/update. `Clear` dọn particles/lists và vài key arrays; không thấy reset `_sparkBudget/_wispBudget/_flameBudget`, `_time`, Random hoặc mọi scan/hold state trong chính hàm này.

**Hành động:** viết test reset/seek khi đang phát và khi chạm cap trước khi thay đổi semantics. Phân biệt soft clear với full deterministic reset; fixed seed không tự bảo đảm cùng ảnh sau seek hoặc ở mọi FPS. Chưa sửa `Clear` trong P0 vì cần audit callers trước.

### P0-04 — AR quality/projection làm thay đổi số lượng và hình dạng

`BuildStageEffects` có `ArQualityBudget` gate; `BuildStageParticles` có cap projected particles và thứ tự ưu tiên Origin, còn geometry đi qua pose/view profile. Một case hiện đúng trên Ultra không chứng minh Standard/Lite hoặc góc top-down có cùng output.

**Hành động:** ma trận quality × pose × style × lifecycle; giải thích cắt giảm trên UI. Không tăng capability từ Unverified lên Native chỉ vì code projection tồn tại.

### P0-05 — Không gian stage và layout AR không trùng tuyệt đối

Simulation lấy layout/aspect của output chính; các output khác dựng layout riêng. AR lấy layout/calibration/pose thật rồi chuyển particle theo helper projection.

**Hành động:** kiểm resize/aspect, 88/76/61/49 phím, black-key offsets, mirror/crop và đổi primary output. Chốt đơn vị flow trước khi thêm trường xoáy; không thêm lực theo pixel màn hình rồi áp y nguyên vào AR.

## 5. Fixture 28 giây và cách sử dụng

```bash
python3 -m unittest discover -s tools/tests -v
python3 tools/make_prism_flow_fixture.py
python3 tools/make_prism_flow_fixture.py --check
python3 tools/check_sources.py
```

File được tạo mặc định: `tools/out/prism-flow-p0/baseline.mid` và `manifest.json`. Hai input nhỏ (~4.9 KB tổng) được lưu ở `docs/samples/prism-flow-p0` để xUnit/Windows diagnostics consume cùng dữ liệu mà không phụ thuộc Python lúc chạy; Python tests giữ chúng khớp generator. Cùng generator sinh cùng bytes; manifest có SHA-256 MIDI, notes, CC64, checkpoint, FPS cần thử và seed đề xuất. Capture harness mới truyền seed vào `GpuRenderLoop.RenderFrames`. Consumer/product-reader tests và capture harness đã PASS trong CI run `38595904297`; phạm vi evidence giới hạn theo cấu hình được ghi ở dưới.

| Khoảng thời gian (s) | Cảnh / mục đích |
|---|---|
| 0–1 | Rest đầu, kiểm nền sạch |
| 1–2.5 | Phím thấp nhất 21 / cao nhất 108; kiểm boundary |
| 3–5 | Giữ cặp phím trắng/đen, hai channel |
| 6–7.0625 | Nốt 62.5 ms lặp, velocity 24/48/80/112/127 |
| 9–10.5 | Chord 10 nốt, hai channel/track |
| 12–16 | CC64 down ở 12, nhấn 12.25–13, tay nhả nhưng pedal giữ đến 16 |
| 18–21 | Cùng pitch 60 hai channel, thời lượng khác nhau |
| 23–24 | Nốt đen riêng để xem release |
| 24–28 | Tail + rest cuối; manifest có checkpoint 27 s |

**Cảnh báo quan trọng:** thời lượng EOT là 28 s nhưng UI/app có thể tính length theo last note end, không theo EOT. Runner phải tự tiến đến 28 s; không suy ra rằng bấm Play file sẽ tự capture rest cuối. Những thao tác pause/seek/loop/switch/disconnect không được mã hóa bằng một MIDI giả; nằm trong danh sách manualScenarios để harness/API hoặc người test thực hiện.

Tách hai đường kiểm thử:

1. **MIDI-file ingestion:** mở file bằng app, xác nhận note count/timing/track/velocity bằng product tests. Không tuyên bố pedal hoặc trailing rest đã được consume.
2. **Song-state render harness (đã PASS Windows/WARP):** dùng notes từ product reader, controls + thời gian từ manifest, inject sustain level, bước dt 1/60 từ 0 và render Stage/AR qua D3D/WARP. Có ảnh/manifest, **chưa có clip/motion metrics, live MIDI routing hoặc simulation reset verification**. Không dùng Python renderer thay app.

## 6. Bằng chứng đã có ở lượt P0 này

- Python fixture tests: 7/7 pass tại sandbox (thêm case kiểm committed fixtures ở lượt tiếp theo).
- Generate → `--check`: bytes và manifest khớp.
- `tools/check_sources.py`: pass (C#/XAML/resources/bindings/docs hiện có).
- Không có .NET SDK ở sandbox. Portable xUnit, Windows Release build, capture P0 và full `--verify` đều đã pass ở run `38595904297`, commit `4478f93`.
- D3D/WARP capture 78 ảnh đã pass sau khi sửa camera input và tách capture mode. Chưa có hardware/webcam/OBS evidence mới; không công bố parity/FPS/capability mới.

## 7. Checklist còn lại để đóng P0

- [x] Inventory runtime và sơ đồ luồng dữ liệu bước đầu.
- [x] Input generator tái lập + kiểm cấu trúc độc lập + static CI.
- [x] Viết consumer + product MIDIReader tests và capture harness Windows; **execution còn pending**, checkbox này chỉ xác nhận đã viết code.
- [x] Portable xUnit, Release build, isolated P0 capture và full Windows `--verify` PASS trên run `38595904297`.
- [x] Harness dùng `ar-keyboard-topdown.png` và pose/corners tương ứng trong repo, không thêm ảnh riêng tư; chờ render xác minh.
- [x] Capture ba control Concert Gold / Ice Crystal (Glass) / Neon Violet ở 12 checkpoint, Stage + AR Ultra; thêm sáu phase-FX-off controls.
- [ ] Capture đầy đủ từng family/style, thêm frame tắt riêng bloom và từng nhóm hạt; batch hiện tại không thay thế ma trận này.
- [ ] Kiểm/đo reset, pedal/sounding, delay, multi-output và chuyển mode; chốt hợp đồng semantics.
- [ ] Thu baseline CPU/GPU/frame trên hardware được ghi tên; WARP chỉ chứng minh chức năng.
- [ ] Người dùng duyệt baseline và khoảng trống cần làm; mới chuyển sang P1 palette.

## 8. Chạy phần mới trên Windows và cách đọc artifact

Thêm mode **`--verify-prism-only`**, dùng cùng `--verify --verify-review-out=<dir>`: chỉ kiểm input/cursor P0 và 78 capture trong thư mục con `prism-flow-p0`. Không chạy/chứng nhận full suite. CI chạy mode này trước, rồi vẫn chạy full `--verify` và 48 ảnh Phase 1 độc lập; lỗi bất kỳ mode nào vẫn làm build đỏ. Ordinary `--verify` không bị thêm yêu cầu fixture P0 trong source checkout. Workflow build đang có đã upload toàn bộ thư mục review nên cũng bao gồm output mới này khi chạy thành công.

```powershell
# Từ repository root, .NET 10 và Windows:
dotnet test tests/PianoPath.Tests/PianoPath.Tests.csproj --configuration Release
dotnet build PianoPath.csproj --configuration Release
.\bin\Release\net10.0-windows\PianoPath.exe --verify --verify-prism-only "--verify-review-out=$env:TEMP\keyflow-review" "--verify-log=$env:TEMP\keyflow-prism-verify.log"
```

Mở `$env:TEMP\keyflow-review\prism-flow-p0\index.html`. Manifest ghi thời điểm yêu cầu và thời điểm thực tế làm tròn lên cuối frame 60 FPS, raw-pixel SHA256, counters stage/AR, số pixel đổi ở control và giới hạn bằng chứng. Chọn `Ice Crystal` làm control **vật liệu Glass hiện có**, không đổi tên preset hoặc giả vờ có preset “Glass” độc lập.

- 72 baseline + 6 effect-off, tất cả 1280×720; HTML hiển thị ở chiều rộng 640 nhưng **không phải** downsample 2×2 hay chứng nhận ảnh 50%.
- Mỗi capture tạo cursor/feed/simulation mới rồi tiến tới checkpoint. Đây là replay sạch cho ảnh đối chứng, **không kiểm** `GpuStageSimulation.Clear` khi đang chạy.
- Camera synthetic, AR Ultra, fit, không mirror, delay=0; không đi qua key-history delay của vòng render realtime. Không dùng harness này để kết luận delay hoặc multi-output đã đúng.
- Mỗi capture kiểm PNG decode pixel round-trip và không rỗng; AR kiểm stage-space notes/keys không lọt vào camera. 6 control frame chỉ kiểm nhóm phase FX làm đổi ảnh, không chứng minh từng style/control.
- Captures dùng song-state path (cùng `GpuStageSimulation`/renderer), chưa mô phỏng callback live MIDI. Nâng cấp live-routing/physical-held/sounding cần lát dọc riêng.
- Captures chưa có threshold “đẹp”, chưa visual sign-off, chưa hardware/FPS evidence. Không đổi support contracts hoặc chuẩn hóa mất chênh lệch hiện tại.

## 9. Lịch sử CI và lỗi đã tìm thấy

- [Run 38592847576](https://github.com/lxmtuu/PianoPath/actions/runs/38592847576), commit `366ba13`: static, portable tests và Windows build pass. P0 capture fail ở guard thiếu note geometry. Audit tìm thấy harness dùng lại một `CameraFrameRing` đã bị consume; sau khi capture Stage tháo camera texture, preset AR tiếp theo không có frame mới để upload.
- Commit `23b8b41`: mỗi AR capture có ring mới và frame synthetic mới publish, giữ guard hình học (không xóa assertion); lỗi được thêm preset/mode/counter/camera upload context.
- [Run 38593677798](https://github.com/lxmtuu/PianoPath/actions/runs/38593677798), commit `23b8b41`: static, portable tests và Windows build pass. Suite dừng **trước P0** ở kiểm tra có sẵn `VerifyArRecording`: ảnh camera tuổi 10064 ms vượt ceiling 5000 ms trên runner chậm. Đây không phải bằng chứng fix P0 đã pass/fail; không nới ceiling hoặc đánh dấu pass.
- Tách `--verify-prism-only` để thu baseline độc lập với các kiểm tra device/realtime. Giữ full suite chạy riêng và vẫn fail build nếu có lỗi. Run kế tiếp `38595904297` đã PASS capture P0 và full suite độc lập.
- [Run 38595904297](https://github.com/lxmtuu/PianoPath/actions/runs/38595904297), commit `4478f93`: static, portable tests, Windows Release build, **isolated P0 capture (78 ảnh)** và **full verification suite** đều PASS. Cấu hình ảnh: 1280×720, 60 FPS simulation, WARP, AR Ultra, camera synthetic, delay=0; không phải webcam hay hardware FPS. Toàn workflow đã hoàn tất **success** (build job 19m31s), gồm perf-report, installer, galleries và publish. Full suite báo **3753 assertions, 1 skipped check group**: không đổi skip thành PASS và không suy đoán encoder/device coverage từ trạng thái job xanh.
- API rerun của kết nối GitHub trả permission 403; chạy lại bằng một commit thay đổi thực trên đúng nhánh session, không yêu cầu credential.
- GitHub artifact downloads redirect sang storage host ngoài allowlist sandbox nên chưa tải được ảnh/log thô bằng `gh run download`. Bổ sung sáu ảnh baseline đại diện vào cơ chế preview commit hiện có; toàn bộ 78 ảnh/manifest vẫn ở artifact, không đưa gallery lớn vào Git. Preview chỉ là baseline do app render, không phải Prism Flow look mới hoặc visual sign-off. Đã fetch preview commit `6138271`, xem cả sáu PNG, và dùng Pillow decode lại BGRA32: cả sáu kích thước 1280×720 và SHA256 pixel khớp các annotation P0 của CI.

P0 còn mở. Bước tiếp theo là **chạy tests/Windows capture, review artifact thật**, rồi giải quyết state/timing và bổ sung coverage từng style. Không bắt đầu chỉnh shader trước khi các rủi ro có đường kiểm chứng.
