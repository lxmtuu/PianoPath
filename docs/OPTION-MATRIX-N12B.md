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

# hoặc chạy cùng lượt đầy đủ; --verify có --verify-review-out cũng chạy ma trận
PianoPath.exe --verify --verify-review-out=<thư-mục>
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
- `Diagnostics/VerificationSuite.cs`: assert `captures == 128` và dòng `PASS` nêu rõ phạm vi.

## 6. Không chứng nhận gì

- **Không** phải duyệt mỹ thuật: ảnh để người xem, không có điểm chất lượng tự động.
- **Không** phủ AR: chưa có pose/camera, nên family AR vẫn `Unverified` như audit §6 ghi.
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
