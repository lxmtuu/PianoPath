# Ma trận option N12(b) — mỗi lựa chọn là một khung hình

Tài liệu này mô tả **cách chứng minh** một option trong Settings thật sự vẽ ra thứ khác, chứ không chỉ là
một cái tên trong combo box. Nó là phần (b) của mục **N12** trong
[NOTE-EFFECTS-ACTION-PLAN](NOTE-EFFECTS-ACTION-PLAN.md) và là bằng chứng mà
[EFFECT-COVERAGE-AUDIT](EFFECT-COVERAGE-AUDIT.md) §4.11 trích dẫn.

## 1. Vì sao cần

Trước đợt này, mỗi family chỉ được kiểm bằng **một** style chọn tay: Confetti cho burst, Shatter cho morph,
Sparkles cho trail, Prism cho ambient light… Vì vậy audit chỉ dám ghi "chưa test riêng option X" cho khoảng
ba chục dòng — đúng theo nghĩa "chưa có ảnh nào chứng minh chọn giá trị khác thì có pixel khác". Không có
nghĩa các option đó không chạy; nghĩa là **repository không có bằng chứng**.

N12(b) tạo bằng chứng đó bằng cách giữ mọi thứ cố định và chỉ đổi **một** chuỗi setting.

## 2. Ma trận

11 trục setting dạng chuỗi, mọi giá trị chúng nhận, đúng thứ tự trong `Stage/PianoVisualSettings.cs`:

| Trục | Giá trị | Số giá trị | Cảnh | Mốc chụp |
|---|---|---|---|---|
| `NoteStyle` | Solid, Neon, Glass, Fire, Gem, CrystalOutline | 6 | nốt 60 giữ 2,0 s | 1,5 s |
| `FallingTrail` | None, Glow, Sparkles, Speed Lines, Blur, Ribbon, Rainbow, Stream | 8 | nốt 60 giữ 2,0 s | 1,5 s |
| `ImpactWave` | None, Ring, Shockwave, Ripple | 4 | `Impact(60, 96/127)` ở khung 1 | +0,117 s |
| `ImpactBurst` | Embers, Splash, Fireworks, Confetti, Dust | 5 | `Impact(60, 96/127)` ở khung 1 | +0,117 s |
| `ImpactMorph` | None, Shatter, Melt, Absorb, Bounce, Morph | 6 | `Impact(60, 96/127)` ở khung 1 | +0,117 s |
| `ImpactFlashStyle` | Flash, Lightning, Plasma | 3 | `Impact(60, 96/127)` ở khung 1 | +0,117 s |
| `ReleaseEffect` | Fade, Float Up, Dissolve, Smoke, Snap Back, Echo Rings | 6 | nhấn khung 0, nhả khung 8 | 0,267 s |
| `AmbientEnergy` | None, Lightning Storm, Laser Beams, Confetti Rain, Fireworks | 5 | không input | 1,5 s |
| `AmbientNature` | None, Rain, Snow, Smoke, Leaves, Butterflies, Dust, Aurora | 8 | không input | 1,5 s |
| `AmbientLight` | None, Gradient Wave, Prism, Color Splash, Spotlights | 5 | không input | 1,5 s |
| `AmbientCosmic` | None, Galaxy, Black Hole, Matrix Rain, Geometric, Fractal | 6 | không input | 1,5 s |

**62 giá trị**, trong đó **9 giá trị chính là trạng thái tắt** — baseline của chúng không chụp lại — cộng **11 baseline**
⇒ **64 cảnh**, mỗi cảnh hai renderer (WPF và Direct3D/WARP) ⇒ **128 ảnh 960×540**,
cùng seed `20261021`, cùng 60 fps. 960×540 là cỡ review của harness: một mảnh shard hay một ring vẫn phủ vài chục
pixel, còn 128 PNG thì vẫn là artifact nhỏ — không nhồi vào bundle Phase 1 vốn đã hơn 1 GiB.

Hai family **không có giá trị "off"** — `ImpactBurst` và `ImpactFlashStyle` luôn vẽ một cái gì đó — nên baseline
của chúng tắt **công tắc hiển thị** (`ShowEmbers`, `ShowImpactFlash`) và mọi giá trị được chụp với công tắc bật.

Ngược lại, `ImpactMorph` và `ReleaseEffect` là hai family **không có công tắc riêng**: chúng được chụp với Embers,
Impact rings, Impact flash và wisps đều **tắt**. Nhờ vậy mỗi khung của hai family này không chỉ so với baseline mà còn
chứng minh option **tự vẽ**, không mượn công tắc của lớp khác. Nếu một lần sửa nào bật lại một lớp cho hai family đó,
`Settings(...)` ném lỗi ngay.

Mọi family khác đều tắt trong baseline: `NoteStyle=Solid`, `FallingTrail=None`, `ImpactWave=None`, `ImpactMorph=None`,
`ReleaseEffect=Fade`, bốn lớp ambient `None`. Baseline cũng tắt các lớp có thể thêm kênh thứ hai (`ShowFlame`,
`ShowWisps`, `ShowHalo`, `ShowKeyFelt`, `ShowPetals`, `ShowStars`, `NoteLandingGlow`, `NoteShimmer`, `ShootingStars`,
`BackgroundMotion`, `KeyLabels`) và **không** đọc từ preset — nên sửa preset không làm đổi ma trận.

## 3. Bốn bất biến làm lượt chạy đỏ

Mỗi ảnh được so với baseline của **chính family đó** (cùng cảnh, cùng input, chỉ khác một chuỗi setting):

1. **Option phải vẽ.** Mỗi giá trị phải đổi **≥ 12 px** so với baseline trên **cả hai** renderer. Không đổi
   nghĩa là settings nhận giá trị nhưng renderer không rẽ nhánh.
2. **Hai giá trị không được là một hình.** Mọi cặp giá trị trong cùng một trục phải khác ảnh nhau (dung sai
   ±2/255 mỗi kênh màu, bỏ alpha). Đây là điều bắt được "hai tên, một hình".
3. **Tắt hết thì phải ra cùng một cảnh, từng bit.** Baseline của các family **chung một cảnh** (2 family ở
   `Falling`, 4 ở `Impact`, 4 ở `Ambient`) phải trùng sha256. Nếu lệch, hoặc renderer không tất định, hoặc
   option này rò rỉ sang cảnh khác — cả hai đều là lỗi thật. GPU đã có tiền lệ `VerifyDeterministicGpuFrame`
   (khung trùng byte-for-byte), và preview WPF cũng đã được CI so không drift.
4. **Đủ ảnh và đọc lại được.** Đúng 128 ảnh, mỗi ảnh có trong manifest, và hash trong manifest phải **khớp lại**
   khi giải mã chính tệp PNG đó — hash được tính từ pixel **đọc lại từ tệp**, không phải từ buffer trong RAM, nên
   một tệp cụt hay bị ghi lại không thể nằm dưới một hash mô tả ảnh khác. (PNG lưu alpha thẳng còn stage phần mềm
   trả BGRA đã nhân alpha, nên so byte-by-byte giữa buffer và tệp là sai kỳ vọng; so hash-của-tệp là đúng thứ
   người xem mở.)

## 4. Chạy và đọc kết quả

```powershell
# chỉ ma trận (không chạy toàn suite)
PianoPath.exe --verify --verify-option-matrix-only --verify-review-out=<thư-mục> --verify-log=<tệp.log>

# hoặc chạy cùng lượt đầy đủ; --verify có --verify-review-out cũng chạy ma trận và lát AR/pose
PianoPath.exe --verify --verify-review-out=<thư-mục>

# chỉ lát AR/pose N12(d) — §8
PianoPath.exe --verify --verify-option-pose-only --verify-review-out=<thư-mục> --verify-log=<tệp.log>

# chỉ ma trận trục số N12(e) — §9
PianoPath.exe --verify --verify-option-numeric-only --verify-review-out=<thư-mục> --verify-log=<tệp.log>

# chỉ round-trip preset N12(f) — §10
PianoPath.exe --verify --verify-preset-roundtrip-only --verify-review-out=<thư-mục> --verify-log=<tệp.log>
```

Kết quả nằm trong `<thư-mục>/option-matrix/`:

- `index.html` — contact sheet, mỗi trục một hàng, baseline đứng đầu; dòng dưới ảnh ghi `+N px vs baseline`.
- `manifest.json` — `families` (trục, giá trị, cảnh, số khung, mốc chụp), `captures` (sha256 **của pixel đọc lại
  từ tệp**, số pixel đổi,
  counter GPU: sprite/ambient/note-trail/note/key), `comparisons` (từng cặp), `baselines` (hash + số pixel sáng).
- `<trục>/neutral-{wpf,gpu-warp}.png` và `<trục>/<giá-trị>-{wpf,gpu-warp}.png`.

Trong CI, `option-matrix/` có artifact riêng tên **`option-matrix-n12b`** (bước *Verification suite* sinh ra, artifact
được upload ngay sau bundle Phase 1), và bundle `phase1-visual-review` **loại trừ** thư mục này để giữ nguyên cỡ cũ.
Chạy trên nhánh push thì cả hai chỉ lên artifact, không vào Git.

## 5. Ghim để ma trận không lặng lẽ cũ đi

- `tools/check_sources.py` → `scan_effect_channel_ownership`: `Stage/Effects/EffectChannel.cs` phải giữ
  `Layer=0/Morph=1/Release=2` (trùng số kênh ring của GPU), ba đường vẽ phải hỏi `OwnsItsDraw`, ring phải đi qua
  `KeyRingChannelGain.Resolve(ring.Channel, …)`, và đúng **6** điểm sinh morph + **6** điểm sinh release phải mang tag.
- `tools/check_sources.py` → `scan_option_matrix`: danh sách giá trị trong `Diagnostics/OptionMatrixPlan.cs`
  phải **bằng đúng, đúng thứ tự** mảng trong `Stage/PianoVisualSettings.cs`; mỗi trục phải có nhánh trong
  `OptionMatrixReview`; `--verify-option-matrix-only` phải còn trong suite. Thêm một style mà quên capture ⇒
  làn tĩnh đỏ ngay, không cần chờ Windows.
- `tests/PianoPath.Tests/OptionMatrixPlanTests.cs`: ghim 11 trục / 62 giá trị / 64 cảnh / 128 ảnh, quy tắc
  "baseline không chụp hai lần", slug tên tệp, và ngữ nghĩa `ChangedPixels`/`NonBlackPixels`.
- `Diagnostics/VerificationSuite.cs`: assert `captures == 128` (và `30` cho lát AR/pose) cùng dòng `PASS` nêu rõ
  phạm vi.
- `OptionNumericPlanTests`: chín trục, ba điểm **tăng dần và phân biệt**, nằm trong khoảng slider, có ít nhất một
  đầu; hai trục width phải giữ đúng cặp 30/45/100 và hai cảnh `Falling`/`Held`; mỗi trục phải arrange qua một
  family mà ma trận §2 đã chụp.
- `scan_option_numeric` (`tools/check_sources.py`): đọc **đầu mút slider từ `SliderRow`** trong
  `Ui/MainWindow.Settings.cs` và khoảng `Clamp` trong `PianoVisualSettings`, so với khoảng của plan; đòi ba điểm
  tăng dần, có đầu mút — trừ những trục nằm trong **`OptionNumericPlan.EndpointExempt`** (duy nhất `note-speed`,
  lý do viết ngay cạnh tập đó); scan đối chiếu cả hai chiều nên một miễn trừ cũ cũng đỏ. Nó cũng đòi review vẫn gọi
  đúng hai hàm render của ma trận phẳng, vẫn `Clamp()` rồi đọc lại giá trị.
- `OptionMatrixPlanTests`: lát AR/pose chỉ được chọn **giá trị sản phẩm** (không phải `None`/`Fade`), phủ đúng
  hai kênh morph và kênh release, và không có công tắc riêng — tức cùng điều kiện "tự vẽ" như ma trận phẳng.

## 6. Không chứng nhận gì

- **Không** phải duyệt mỹ thuật: ảnh để người xem, không có điểm chất lượng tự động.
- **Không** phủ AR ngoài lát N12(d) ở §8: ba pose đã hiệu chỉnh × năm trục hiệu ứng được chiếu vào ảnh
  camera; phần còn lại của ma trận (nốt, ambient) vẫn chỉ có bằng chứng ở sân khấu phẳng.
- **Không** phủ **giá trị số**: amount/speed/size/intensity/curve, seek/reset, round-trip preset.
- **Không** phủ smart modulator (`VelocityColor`, `OctaveColor`, `PedalGlow`, `TempoSync`, `AudioReactive`,
  `ZoneSplit`) hay theme graph.
- **Không** phải hiệu năng: WARP trên runner, không phải GPU thật, không đo FPS.
- **Không** đổi mặc định, preset hay RNG. Ma trận **không** sửa mã vẽ: nó chỉ đọc settings và gọi API công khai của
  stage. Lỗi vẽ mà nó bắt được (mục 7) là lỗi của sản phẩm và được sửa riêng trong `Stage/`, có test.

## 7. Lượt chạy đầu tiên đã bắt được gì

CI của ma trận (run `39486496425`, commit `d087476`) đỏ ở family thứ năm, ngay giá trị đầu tiên của nó:

```
FAIL: impact-morph/Shatter moved 0 pixels on WPF (floor 12): the option is accepted by the settings class
but does not draw.
```

Không phải ma trận làm sai. Stage **phần mềm** giữ hạt, ring và flash trong ba danh sách dùng chung, và cả ba
chỉ được vẽ khi công tắc của lớp sở hữu bật:

| Danh sách | Điều kiện vẽ cũ | Hệ quả |
|---|---|---|
| `_sparks` | `ShowEmbers \|\| ShowWisps` | Shatter, Melt, Bounce biến mất khi tắt Sparks |
| `_rings` | `ShowImpactRings` | Absorb, Snap Back, Echo Rings biến mất khi tắt Impact rings |
| `_flashes` | `ShowImpactFlash` | Morph (ngôi sao) biến mất khi tắt Impact flash |

Stage GPU thì ngược lại: nó sinh morph **trước** cổng `ShowEmbers`, và vẽ danh sách ring/flash **không** hỏi hai
công tắc kia. Tức là cùng một file preset cho ra hai hình khác nhau trên hai renderer — và trên WPF, **cả năm**
giá trị morph lẫn **cả năm** kiểu release "được nhận nhưng không vẽ", đúng thứ audit §4.11 không thể chứng minh.

**Bản sửa (sản phẩm, không phải harness):**

- `Stage/Effects/EffectChannel.cs`: mỗi hiệu ứng mang chủ sở hữu `Layer` (0), `Morph` (1) hoặc `Release` (2) — đúng
  cách đánh số kênh ring của GPU (`KeyRingChannelGain`).
- `PianoStage`: `SpawnImpactMorph` gắn `Morph` cho 6 điểm sinh, `SpawnReleaseFx` gắn `Release` cho 6 điểm sinh; ba
  đường vẽ chỉ bỏ qua hiệu ứng `Layer` khi công tắc của chính nó tắt, và **vẫn** gọi hàm vẽ khi trong danh sách còn
  hiệu ứng tự sở hữu.
- Ring lấy độ sáng qua `KeyRingChannelGain.Resolve(ring.Channel, wave, morph)`: ring morph theo slider morph, ring
  release mang sẵn `ReleaseIntensity` lúc sinh (không bị nhân hai lần).
- Ghim: `tests/PianoPath.Tests/EffectChannelTests.cs` (cách đánh số + bảng chân trị), `scan_effect_channel_ownership`
  trong `tools/check_sources.py`, và chính ma trận — `ImpactMorph`/`ReleaseEffect` bị **buộc** chụp với mọi công tắc
  tắt, nên một lần re-gate là CI đỏ.

Đổi lại: người dùng chọn Shatter với Sparks tắt giờ **thấy** shard (và bản ghi WPF khớp bản ghi GPU), còn ring
morph/release sáng theo đúng slider của chúng thay vì theo slider wave.

## 10. N12(f) — round-trip preset: look đã lưu phải là look mở lại

Ba review trước đều dựng settings **trong bộ nhớ**. Một look mà người chơi lưu hôm nay và mở lại mai đi qua một
con đường khác: `CopyFrom` vào settings đang sống → `WritePresetFile` ra tệp → `ReadPresetFile` đọc lại (kèm
`ApplyMigrations` + `Clamp`) → `CopyFrom` lần nữa. Một thuộc tính mà tệp **không mang** (hoặc một migration viết
lại) sẽ quay về mặc định, và look đổi hình sau khi mở lại — im lặng, không có ngoại lệ nào.

`Diagnostics/PresetRoundTripReview.cs` (`--verify-preset-roundtrip-only`, artifact `preset-roundtrip-n12f`) đi
đúng con đường đó cho **mọi preset đang ship** — 14 built-in + 6 tệp shelf, lấy từ `VisualPresets.BuiltIn` và
`CommunityPresets.All` chứ không từ danh sách chép tay — và kiểm **ba** điều, đỏ ở cả ba:

1. **Mọi thuộc tính ghi được** (255) của `PianoVisualSettings`, so bằng reflection và **không có danh sách bỏ
   qua, cũng không có danh sách chỉ định**, phải giống nhau giữa settings đã ghi và settings **đọc lại từ tệp**;
   lỗi **nêu đúng tên thuộc tính** và hai giá trị. Đây là phần phủ các trường mà cảnh được chụp không vẽ (burst,
   wave, flash, morph, AR…): chỉ so ảnh sẽ không thấy chúng.
2. Cùng phép so đó chạy lần nữa giữa settings đã ghi và settings **sau đường áp của dock**, nên một bước
   `Clamp`/migration trên đường vào settings sống cũng bị bắt chứ không chỉ đường ra tệp.
3. Khung hình **90 frame** của cảnh `Falling` phải **trùng từng byte** trên **cả WPF và D3D/WARP** trước và sau
   vòng tệp — so byte thật, **không** dùng dung sai 2/255 của ma trận phẳng, vì ở đây hai bên phải dựng lại đúng
   cùng một sân khấu chứ không phải "gần giống".

⇒ **20 preset × 2 renderer = 40 ảnh lưu** (ảnh "trước"), **80 frame so sánh**; manifest ghi hash của **cả hai**
khung, hash của ảnh đọc lại từ tệp, hash JSON settings trước **và** sau, `changed = 0`, `changedPixels = 0` và số
thuộc tính đã so. Một cảnh là đủ vì lý do ở (1): trường bị rơi bị bắt bởi phép so thuộc tính dù cảnh có vẽ nó
hay không.

Phần áp preset trong review **chép đúng ba dòng** của `MainWindow.ApplyPreset` (copy với `keepBackgroundImage`,
đặt `PresetName`/`PresetModified`, và luật "Image không có đường dẫn thì về Solid"); review ghi tệp **trực tiếp**
bằng `WritePresetFile` chứ không qua `VisualPresetStore.Save`, vì `CopyFrom` không bao giờ chép trường riêng của
máy (ngôn ngữ, GPU, camera, thiết bị âm thanh, hiệu chỉnh phím) — nên settings đang ghi đã ở đúng mặc định của
máy này, và bước `ClearMachineLocal` của `Save` không đổi gì để mà kiểm. `scan_preset_roundtrip` đỏ nếu một
trong ba dòng đó đổi ở dock mà review không đổi theo, nếu review bỏ `WritePresetFile`/`ReadPresetFile`, bỏ một
trong hai phép so thuộc tính, nới dung sai khung hình khỏi 0, hoặc bỏ `ExpectedPresets = 20`.

**Không chứng nhận:** không duyệt mỹ thuật; không phủ mọi phiên bản migration (chỉ cặp hiện tại) hay preset do
người dùng tạo trên máy khác; không phải GPU thật, không đổi mặc định.

## 9. N12(e) — trục số: hai mức slider không được ra cùng một khung hình

Ma trận ở §2 chỉ chụp **giá trị rời rạc**: `ImpactWave = "Ripple"`, `NoteStyle = "Gem"`. Nó không nhìn thấy một
slider, và slider che một lỗi khác hẳn: một ngưỡng/kẹp nằm trong **một** đường vẽ làm hai mức slider ra **cùng một
khung hình**, trong khi mỗi mức vẫn khác ảnh "tắt" — nên một review chỉ so với baseline sẽ gọi nó là xanh. Sân
khấu phần mềm đã có đúng lỗi đó: thanh giữ phím kẹp `Note width` ở 60, nên preset 30% vẽ thanh 60% trên WPF và
thanh 30% trên engine (xem `docs/NOTE-EFFECTS-ACTION-PLAN.md` §3.2b).

**Ma trận:** chín trục số, ba giá trị mỗi trục, mỗi giá trị chụp trên **WPF và D3D/WARP** ở 960×540, cùng cảnh,
cùng seed (`--verify-option-numeric-only`, artifact `option-numeric-n12e`):

| Trục | Setting | Cảnh | Giá trị | Vì sao ba mức đó |
|---|---|---|---|---|
| `note-width` | `NoteWidth` 30–100 | nốt rơi | 30 / 45 / 100 | 30 và 45 từng bị kẹp 60 ⇒ **trùng khung hình** trên WPF |
| `bar-width` | `NoteWidth` 30–100 | **giữ phím** | 30 / 45 / 100 | chính thanh giữ phím — đường vẽ đã có lỗi |
| `note-speed` | `NoteFallSpeed` 100–1000 | nốt rơi | 125 / 325 / 550 | 1000 px/s đã ra khỏi sân khấu trước mốc chụp, nên trục này là miễn trừ duy nhất của quy tắc "lấy đầu mút" (`EndpointExempt`) |
| `trail-intensity` | `FallingTrailIntensity` 0–100 | nốt rơi | 0 / 50 / 100 | 0 = không vẽ, 50 ↔ 100 = gấp đôi độ sáng |
| `wave-intensity` | `ImpactWaveIntensity` 0–150 | va chạm | 0 / 75 / 150 | 0 = ring vô hình, 75 ↔ 150 = gấp đôi |
| `wave-size` | `RingSize` 0–100 | va chạm | 5 / 50 / 100 | bán kính đổi ⇒ hình học đổi |
| `burst-amount` | `ParticleAmount` 0–120 | va chạm | 8 / 60 / 120 | số hạt đổi ⇒ khác **cấu trúc** |
| `flash-intensity` | `ImpactFlashIntensity` 0–100 | va chạm | 0 / 50 / 100 | 0 = không flash |
| `release-intensity` | `ReleaseIntensity` 0–100 | nhả phím | 0 / 50 / 100 | 0 = không vẽ, 50 ↔ 100 = gấp đôi |

⇒ **27 cảnh × 2 renderer = 54 ảnh**.

**Cổng:** mọi **cặp** giá trị trong một trục phải đổi **≥ 12 px** trên **cả hai** renderer (cùng ngưỡng với §2).
Cộng thêm một bất biến chống "manifest nói dối": mỗi ảnh **đọc lại** setting sau `Clamp()` và lượt chạy đỏ nếu
sản phẩm đã âm thầm đổi giá trị — nếu không, một giá trị ngoài khoảng slider sẽ được ghi dưới một mức nó chưa
từng vẽ. Ảnh vẫn lấy sha256 **của pixel đọc lại từ tệp**.

**Cảnh `Held` (mới):** `OptionMatrixPlan.Scene.Held` — nhấn ở khung 0 và **không nhả**, chụp ở 0,5 s. Đây là mốc
duy nhất nhìn thấy thanh giữ phím; bốn cảnh cũ không có nó (Release nhả ở khung 8). Không family rời rạc nào dùng
cảnh này nên ma trận §2 không đổi.

**Dùng chung một đường render:** review gọi `OptionMatrixReview.CaptureWpf`/`CaptureGpu` (đã tách theo `Scene`)
chứ không có vòng lặp riêng — hai bản sao của vòng lặp là cách hai review mô tả hai khung hình khác nhau.

**Không chứng nhận:** không duyệt mỹ thuật, **không** chứng minh phản ứng tuyến tính hay đơn điệu (chỉ chứng minh
hai mức khác nhau *khác ảnh*), không phủ AR (N12(d)), không phải GPU thật/khung hình thật, không đổi mặc định hay
preset.

**Lượt chạy đầu (2026-10-21):** run `39641339707` trên `4cde7a5` xanh toàn bộ — `Verification suite` render đủ
**54 ảnh** và **cả 27 cặp** qua ngưỡng 12 px trên **cả WPF và WARP**, gồm cặp 30 ↔ 45 của `Note width` (cặp mà
ngưỡng 60 cũ biến thành một khung hình duy nhất). Dòng `PASS numeric option review (N12(e))` trong log in thêm
**cặp sát ngưỡng nhất** của từng trục trên từng renderer, nên không cần mở artifact mới biết cổng cách ngưỡng bao
xa; log nằm trong artifact `keyflow-verification-log` (upload mọi lượt chạy), còn ảnh nằm trong
`option-numeric-n12e` (chỉ upload ở `main` để không đội quota).

## 8. N12(d), phần 1 — cùng option đó qua ba pose AR

Ma trận ở mục 2 chỉ chạy renderer sân khấu. Một option có thể vẽ đúng ở đó mà **không** đi qua đường chiếu AR —
ring thành quad, flash thành billboard, shard bị xoay theo nốt — và không ảnh nào ở mục 2 cho thấy điều đó.

Phần 1 của N12(d) (`Diagnostics/OptionPoseReview.cs`, route `--verify-option-pose-only`) chụp đúng năm trục mà
nghệ thuật của chúng nằm **trong ảnh camera**: wave, burst, hai kênh morph (hạt và flash) và vòng release. Mỗi
trục hai lần chụp — **tắt** và **bật** — ở cùng pose, cùng input, cùng seed; ba pose là ba pose đã hiệu chỉnh sẵn
của review hạt giữ phím (`HeldParticleControlPlan.Views`: reference, oblique, near-overhead), nên không thêm
fixture camera hay tuyên bố pose mới. Tổng **27 ảnh WARP 1280×720** (mỗi pose: bốn ảnh tắt + năm ảnh giá trị) (artifact `option-pose-n12d`), manifest ghi
góc pose, sha256 của ảnh camera và số pixel đổi của từng cặp.

Cổng: mỗi giá trị phải đổi **≥ 12 px** so với ảnh tắt **ở từng pose** — cùng ngưỡng với ma trận phẳng. Nếu một
option chỉ có nhánh ở sân khấu, lượt chạy đỏ ngay tại pose đầu tiên. Ảnh tĩnh, không phải clip; chưa phủ
ambient/nốt trong AR (sai số chiếu sẽ ảnh hưởng mọi giá trị của một trục như nhau, nên câu hỏi "option có nhánh
không" đã được ma trận phẳng trả lời) và chưa phủ che tay (N10).
