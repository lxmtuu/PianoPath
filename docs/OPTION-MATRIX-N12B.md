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
⇒ **64 cảnh**, mỗi cảnh hai renderer (WPF và Direct3D/WARP) ⇒ **128 ảnh 1280×720**,
cùng seed `20261021`, cùng 60 fps.

Hai family **không có giá trị "off"** — `ImpactBurst` và `ImpactFlashStyle` luôn vẽ một cái gì đó — nên baseline
của chúng tắt **công tắc hiển thị** (`ShowEmbers`, `ShowImpactFlash`) và mọi giá trị được chụp với công tắc bật.

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
4. **Đủ ảnh.** Đúng 128 ảnh, mỗi ảnh có trong manifest, và manifest khớp với đĩa.

## 4. Chạy và đọc kết quả

```powershell
# chỉ ma trận (không chạy toàn suite)
PianoPath.exe --verify --verify-option-matrix-only --verify-review-out=<thư-mục> --verify-log=<tệp.log>

# hoặc chạy cùng lượt đầy đủ; --verify có --verify-review-out cũng chạy ma trận
PianoPath.exe --verify --verify-review-out=<thư-mục>
```

Kết quả nằm trong `<thư-mục>/option-matrix/`:

- `index.html` — contact sheet, mỗi trục một hàng, baseline đứng đầu; dòng dưới ảnh ghi `+N px vs baseline`.
- `manifest.json` — `families` (trục, giá trị, cảnh, số khung, mốc chụp), `captures` (sha256, số pixel đổi,
  counter GPU: sprite/ambient/note-trail/note/key), `comparisons` (từng cặp), `baselines` (hash + số pixel sáng).
- `<trục>/neutral-{wpf,gpu-warp}.png` và `<trục>/<giá-trị>-{wpf,gpu-warp}.png`.

Trong CI, cả thư mục đi cùng artifact `phase1-visual-review` (bước *Verification suite*).

## 5. Ghim để ma trận không lặng lẽ cũ đi

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
- **Không** đổi mặc định, preset, RNG hay mã vẽ: ma trận chỉ đọc settings và gọi API công khai của stage.
