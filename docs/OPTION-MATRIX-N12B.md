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
- **Không** phải ảnh gốc: bảy tờ ảnh ở §11 là bản thu nhỏ còn 1/4 hoặc 1/6; chúng chứng minh có vẽ và vẽ ở
  đâu, không chứng minh từng pixel. Ảnh native vẫn chỉ nằm trong artifact của lượt chạy.
- **Không** phủ AR ngoài lát N12(d) ở §8: ba pose đã hiệu chỉnh × (năm trục hiệu ứng vẽ vào ảnh camera, chất
  liệu nốt, trail và bốn lớp ambient); ở AR mỗi trục của phần 2 chỉ có **một** giá trị, phần còn lại của ma trận
  (các giá trị khác của nốt/ambient) vẫn chỉ có bằng chứng ở sân khấu phẳng.
- **Không** phủ **hết giá trị số**: §9 chụp **chín** trục số ở ba mức mỗi trục trên cả hai renderer (54 ảnh),
  nên nó chứng minh *ba mức đó* phân biệt được — không phải mọi mức của mọi slider, và không phủ các trục còn lại
  (màu, hình học nốt, ambient, curve). §10 chứng minh look lưu rồi mở lại **không đổi một pixel**, không chứng minh
  look đó đẹp hay preset nào đúng gu. `seek`/`reset` không được chụp ở đâu cả.
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

## 8. N12(d) — cùng option đó qua ba pose AR

Ma trận ở mục 2 chỉ chạy renderer sân khấu. Một option có thể vẽ đúng ở đó mà **không** đi qua đường chiếu AR —
ring thành quad, flash thành billboard, shard bị xoay theo nốt — và không ảnh nào ở mục 2 cho thấy điều đó.

`Diagnostics/OptionPoseReview.cs` (route `--verify-option-pose-only`, artifact `option-pose-n12d`) chụp từng trục
hai lần — **tắt** và **bật** — ở cùng pose, cùng input, cùng seed; ba pose là ba pose đã hiệu chỉnh sẵn của review
hạt giữ phím (`HeldParticleControlPlan.Views`: reference, oblique, near-overhead), nên không thêm fixture camera
hay tuyên bố pose mới. Hai phần cộng lại thành **63 ảnh WARP 1280×720**:

- **Phần 1 (27 ảnh)** — năm trục mà nghệ thuật của chúng nằm **trong ảnh camera**: wave, burst, hai kênh morph
  (hạt và flash) và vòng release. Mỗi pose: bốn ảnh tắt + năm ảnh giá trị; hai giá trị của morph dùng chung một
  ảnh tắt vì chúng là hai kênh của cùng một nhà.
- **Phần 2 (36 ảnh)** — phần còn lại mà lớp phủ AR vẽ được: **chất liệu nốt** (`Gem`) và **trail** (`Ribbon`), thứ
  đi qua đường dựng mảng chiếu 3D chứ không phải sprite sân khấu, cùng **bốn lớp ambient** (`Laser Beams`,
  `Aurora`, `Color Splash`, `Galaxy`) vốn được phát ở không gian màn hình rồi đè lên ảnh camera. Mỗi pose: sáu cặp
  tắt/bật.

**Vì sao nốt/trail nằm trong khung hình** (và đây là chỗ một bản trước của tài liệu này đã ghi **sai**): không
phải `head + duration × fall`. `Ar3DNoteProjection.Build` kẹp đầu nốt ở mặt phím
(`head = max(0, (start − position) × fall)`) và tính đuôi từ **vị trí chưa kẹp**
(`tail = max((start − position) × fall + duration × fall − gap, head)`). Nốt của cảnh `Falling` bắt đầu ở 0 và vẫn
đang ngân ở mốc chụp 1,5 s, nên phần **nhìn thấy được** chỉ là phần còn nằm trên phím: **head 0** và **tail 0,735**
độ sâu phím phía sau mặt phím — không phải 3,75 như bản trước viết, và tâm nốt ở **0,37** chứ không phải 2,25.
Trail chạy từ 0,735 tới `min(reach, tail + max(0,08; thân × 0,55 + 0,08))` ≈ **1,22** độ sâu phím. Toàn bộ vùng đó
nằm **phía trên mép sau bàn phím**, đúng chỗ fixture (`docs/samples/ar-keyboard-topdown.png`, các góc hiệu chỉnh
ở `y ≈ 268–292` cho mép sau) có nền trời/tường — nên nốt và trail đều vào khung. Con số được ghim bằng test
(`The_falling_scene_note_is_still_inside_the_calibrated_picture`) đọc chính công thức trên qua
`ArNoteStyle.Default`, nên đổi tốc độ rơi hay độ dài nốt sẽ đỏ trên Linux trước khi runner kịp dựng ảnh.

**Bản sửa:** mục này từng kết luận "không chụp được nốt/trail vì nốt nằm 2,25 độ sâu phím phía sau" và để phần 2
chỉ có ambient (51 ảnh, run `39666230753` xanh). Kết luận đó sai vì bỏ qua việc head bị kẹp về 0; đọc lại mã cho
ra 0,37/0,735, tức là **trong** khung. Hình dạng đúng là 63 ảnh như trên; con số 51 chỉ còn là lịch sử của một
lượt chạy.

Cổng cho **cả hai** phần: mỗi giá trị phải đổi **≥ 12 px** so với ảnh tắt **ở từng pose** — cùng ngưỡng với ma
trận phẳng. Nếu một option chỉ có nhánh ở sân khấu, lượt chạy đỏ ngay tại pose đầu tiên và câu lỗi nêu đúng pose
+ family + số pixel. Manifest ghi góc pose, sha256 của ảnh camera, `part` của từng ảnh và số pixel đổi của từng
cặp; `scan_option_pose` giữ hai phần **rời nhau** (một family không được nằm ở cả hai, mỗi family đúng một dòng ở
phần 2), giữ mọi giá trị là giá trị sản phẩm và khác giá trị trung tính, và giữ 27 + 36 = 63 khớp với suite.

**Không chứng nhận:** ảnh tĩnh, không phải clip; mỗi trục chỉ một giá trị ở phần 2 (câu hỏi "*mọi* giá trị của
ambient hay của chất liệu nốt có đi qua AR không" vẫn do ma trận phẳng trả lời ở sân khấu); nốt chỉ được chụp ở
một mốc — 1,5 s của cảnh `Falling`, khi đầu nốt đã ở mặt phím và chỉ còn thân nằm trên phím — nên không chứng minh
mọi khoảng cách tới phím; sai số chiếu ảnh hưởng mọi giá trị của một trục như nhau; **chưa** phủ che tay (N10),
camera thật hay GPU thật.

**Lượt chạy (2026-10-21):** hình dạng 51 ảnh (chỉ ambient) chạy xanh trên `4b39ae8` (run `39666230753`); hình
dạng đúng **63 ảnh** chạy xanh trên `fa5e534` (run `39678491981`) — `static`, `test`, `keyboard-light`, `build`
(installer + preview + gói phát hành) đều xanh, bước `Verification suite` mất **4 phút 30 giây** trên runner đó
(cùng lượt, bước `Build` chỉ 23 s so với 40 s ở lượt trước, nên máy nhanh hơn chứ không phải làm ít hơn). Bước này
chạy `--verify --verify-review-out=...`, và trong mã `VerifyOptionPose` đòi đúng **27 ảnh phần 1 + 36 ảnh phần 2**
rồi so **từng pose** với cổng 12 px, nên xanh nghĩa là **cả sáu family của phần 2 — kể cả chất liệu nốt và trail —
đều đổi đủ pixel qua cả ba pose AR**, không phải chỉ một pose. Log nằm trong `keyflow-verification-log` (máy này
không tải được artifact — blob trả `EOF` — nên bằng chứng đọc từ kết luận job + assert trong mã).

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

**Lượt chạy đầu (2026-10-21):** run `39654999481` trên `f38574a` xanh toàn bộ — `static`, `test`,
`keyboard-light` và `build`. Bước `Verification suite` mất **7 phút 06 giây**, chỉ **+57 giây** so với lượt
N12(e) (6:09) dù thêm 20 preset × 4 lần dựng cảnh 90 frame = **7.200 khung hình**; bước đó chạy
`--verify --verify-review-out=...` nên `VerifyPresetRoundTrip` nằm trong cùng `try` — một thuộc tính lệch, một
khung lệch byte hay một preset thừa/thiếu đều làm bước **đỏ**, nên xanh nghĩa là cả 20 preset đã đi hết đường
lưu/mở. Ảnh nằm trong artifact `preset-roundtrip-n12f` (chỉ upload ở `main` để không đội quota), nên trên nhánh
bằng chứng là dòng `PASS preset round trip (N12(f))` trong `keyflow-verification-log`.

**Sửa ngay trong lượt đầu:** bản đầu gọi `VisualPresets.WritePresetFile`/`ReadPresetFile`, nhưng hai hàm đó
thuộc `VisualPresetStore`; run `39654504807` đỏ ở bước `Build` (kéo theo `keyboard-light`) trước khi chạy được
gì. `f38574a` sửa đúng tên kiểu, và `scan_preset_roundtrip` bám theo cách gọi mới để vẫn đỏ khi review rời khỏi
writer/reader của sản phẩm.

## 11. Tờ ảnh tiếp xúc — phần của bốn review được commit

Artifact của một lượt chạy chỉ tồn tại **hai ngày**, và trên nhánh đang làm việc thì bốn bước publish của N12
còn không chạy: chúng có điều kiện `github.ref == 'refs/heads/main'`. Tức là cho tới trước mục này, bằng chứng
hình ảnh của N12 chỉ là "job xanh cộng với assert trong mã" — trong khi 348 ảnh của các đợt khác thì đã nằm
trong `docs/`. Mục này đóng đúng khoảng đó, và chỉ đóng nó: **bảy tệp**, không phải 285 ảnh native.

| Tệp trong `docs/evidence/n12-sheets/` | Nội dung | Lưới | Kích thước |
|---|---|---|---|
| `matrix-wpf.png` | cả 64 khung của N12(b) trên sân khấu phần mềm | 8×8 ô 176×99 | 1518×1090 |
| `matrix-gpu-warp.png` | chính 64 khung đó trên D3D/WARP | 8×8 ô 176×99 | 1518×1090 |
| `numeric.png` | 54 khung của N12(e): chín trục, mỗi hàng một trục, cặp WPF ↔ GPU cạnh nhau | 6×9 ô 240×135 | 1526×1545 |
| `preset.png` | 40 khung của N12(f): hai mươi preset, mỗi preset một cặp renderer | 4×10 ô 240×135 | 1022×1712 |
| `pose-ar-reference.png`, `pose-ar-oblique.png`, `pose-ar-near-overhead.png` | 21 khung của một pose AR: phần 1 rồi phần 2, mỗi family một khung tắt rồi khung bật | 4×6 ô 320×180 | 1342×1314 |

Ô 176×99 đúng tỉ lệ 16:9 của khung 960×540, và `Blit` **từ chối** một ô không cùng tỉ lệ với ảnh — một ô
176×90 (bản đầu của đợt này) bóp ảnh ngang 10%, đúng thứ không thể đánh giá bằng mắt trên ảnh thu nhỏ. Nhãn của
ma trận in hai dòng: tên family rồi tên giá trị, vì cặp dài nhất là ba mươi ký tự.

**Vì sao tờ ảnh đáng tin.** `Diagnostics/ReviewSheet.cs` cắt ảnh bằng **trung bình khối** (không lấy mẫu), vẽ
nhãn bằng một font bitmap 5×7 viết trong mã, và ghép theo phép tính thuần trên mảng byte — không WPF, không
font hệ thống, không thiết bị đồ hoạ. Vì vậy `tests/PianoPath.Tests/ReviewSheetTests.cs` chạy được trên Linux
và ghim: kích thước lưới, phép thu nhỏ, hai định dạng alpha, tính tất định, và việc một ô chưa vẽ thì sheet
**không** được trả ra. Bốn chỗ dễ sai đã có test riêng: một ô trong suốt không được để lộ màu RGB chưa nhân
alpha; **một tờ dùng đúng một cỡ chữ** (bản đầu để mỗi nhãn tự chọn cỡ, nên "RELEASE FADE" cao gấp đôi nhãn
bên cạnh); **nhãn không bao giờ bị cắt** — nhãn không vừa ô là lỗi, vì bản đầu cắt "AMBIENT-ENERGY CONFETTI
RAIN" thành "CONFETTI RA" trên tờ đã commit, và người đọc không phân biệt được giá trị bị rút gọn với một giá
trị khác; và nhãn không tràn sang ô bên cạnh.

**Vì sao commit được.** Cùng một input cho ra đúng từng byte, nên CI commit tờ ảnh giống như commit preview.
Bước `Stage the N12 contact sheets` (ngay sau suite, lúc ảnh còn trong thư mục review) chép bảy tệp vào
`docs/evidence/n12-sheets`; bước `Save the N12 contact sheets` ở cuối job commit rồi đẩy.

Nó **không** dùng quy tắc "tip phải vẫn là commit mình vừa build" mà bốn bước evidence khác dùng: trên nhánh
này, job `keyboard-light` chạy song song và đẩy commit evidence của nó vài phút sau khi lượt bắt đầu, nên tip
đã dịch chuyển từ rất lâu trước khi job build render xong — quy tắc đó sẽ bỏ qua **mọi** lượt (đã thử: commit
`ece10f6`, tip nhảy sang `e064a0e` lúc 01:06). Thay vào đó nó replay đúng một commit — bảy tệp **mới**, không
bước nào khác ghi vào — lên tip mới, rồi push. Nếu replay đó xung đột thật (một lượt khác đã ghi tờ ảnh khác),
nó `git rebase --abort`, cảnh báo và để nhánh giữ bản của mình; không bao giờ để lại cây gãy hay job đỏ vì
chuyện đó. Push bị chặn (GH013, protected branch) cũng chỉ là cảnh báo, như các bước evidence khác. Mỗi review còn **từ chối** một tờ ảnh thiếu khung
(`sheet.Tiles != <số capture>`), vì một ô trống trên tờ đọc ra đúng như "option này không vẽ gì" — kết luận mà
review sinh ra để báo.

**Lượt chạy:** bảy tờ ảnh đang nằm trên nhánh là của run `39713639185` (commit `a932e1d`), commit evidence
`071ecca` — `static`, `test`, `keyboard-light`, `build` đều xanh. Ba lần sửa trước đó là do chính ảnh chụp lại
bắt được, không phải do test: cỡ chữ lẫn lộn, nhãn bị cắt giữa từ, và ô 176×90 bóp ảnh ngang 10%.

**Không chứng nhận:** tờ ảnh là bản thu nhỏ còn 1/4 hoặc 1/6, nên nó chứng minh **có vẽ, vẽ ở đâu, và hình dạng
tổng thể**; muốn soi từng pixel thì vẫn phải mở ảnh native trong artifact (hoặc chạy lại route). Nó không phải
bản duyệt mỹ thuật, và việc bảy tệp này nằm trong Git không làm phát biểu của §2/§8/§9/§10 mạnh thêm — chỉ làm
chúng **mở ra được**.
