# Phối hợp hạt — lát kiểm tra native đầu tiên

2026-10-16. **Đã hoàn thành kiểm tra kỹ thuật theo checkpoint; có phát hiện về phân bổ cap AR. Chưa sửa selection, chưa nghiệm thu hình ảnh phối hợp/chuyển động và chưa đổi mặc định.** Settings UI vẫn làm cuối.

## Kết quả và nguồn

- Native source: `79930e52cb3773f02d9e8e59be2c111236917227`.
- [Run 38846496183](https://github.com/lxmtuu/PianoPath/actions/runs/38846496183) **SUCCESS**: static, portable C# tests và Windows `key-ensemble`. Job full build và Key light được skip có chủ đích trong lane này; không gọi đây là full regression mới.
- Publication: `849f8c9413c4751882d88df888b1d8aebd30cce5`.
- [Manifest](evidence/key-particle-ensemble-v1/manifest.json.gz): 1.120 records và 22 source guards; [sáu PNG sheets](previews/key-particle-ensemble-v1), 24 pane native lossless. Tổng **680.518 byte**, không resample/quantize/đổi exposure.
- Windows WARP / **Microsoft Basic Render Driver**, 640×360, scene logic 1280×720. Full original PNG/off/classic/control ở artifact `key-particle-ensemble-native`; không tải bộ lớn này về sandbox. CI kiểm từng ảnh gốc, hash BGRA, provenance crop và tính lại các thống kê ảnh trước khi publish.

### Khép phần Key light trước đó

[Run 38841543462](https://github.com/lxmtuu/PianoPath/actions/runs/38841543462), source `8325ae8`, đã **SUCCESS toàn bộ**, gồm full regression, benchmark, galleries và distributable. Evidence refresh `d36364c` đã được kéo về. Đối chiếu freeze trước sửa Key light: **69.964 digest pixel/frame/raw-state/geometry giữ nguyên, 16 families / 18 manifests**, chỉ loại source/revision metadata. Đây là kết quả của run trước; production GPU/CPU lighting/particle sources không bị sửa trong lượt ensemble này.

## Cảnh và phạm vi chủ ý

| Recipe | Burst — shipping classic | Held — v3 đã duyệt | Morph — shipping classic | Release material đã duyệt |
|---|---|---|---|---|
| embers-wisps | Embers | Wisps | Shatter | Float Up |
| splash-flames | Splash | Flames | Melt | Dissolve |
| fireworks-wisps | Fireworks | Wisps | Bounce | Smoke |
| confetti-flames | Confetti | Flames | Shatter | Float Up |
| dust-wisps | Dust | Wisps | Melt | Dissolve |

Không tự bật các trial burst/morph khác hoặc gán trạng thái “đã duyệt” cho chúng. Chỉ ghép held/release đã duyệt với các shipping controls. Giữ nguyên artwork, emission/RNG/physics, cap/selection, shader/renderer, preset và UI sản phẩm. Các cờ art/front-layer chỉ được bật trên clone `GpuLook` trong diagnostic, không serialize/persist.

- **off:** giữ notes/key lighting, tắt particle channels.
- **classic:** held v2 qua `HeldParticleArt`, release legacy; không phải bản classic gốc của toàn bộ sản phẩm.
- **mixed:** Wisps/Flames v3 và release art; Stage depth hiện hành .6.
- **front-control:** giống mixed, chỉ release Stage depth .01. Đây là đối chứng chính sách riêng, không phải promotion/default mới; AR không đổi.

Hai palettes Prism/Indigo, Key light 0/100, Stage và AR reference; 7 checkpoints × 4 variants × 5 recipes = **1.120 PNG**. Rim 62, emission 149, gloss 92, tilt 38, shadow 46, AO 52, bloom 35; tint/spill và live note bars bật. Dựa trên fixture Prism Flow, **không phải bản sao preset đầy đủ của người dùng**. AR dùng ảnh synthetic repository, Ultra, không delay, production note-aligned axes; không dùng historical-axis seam.

### Timeline thật, không relabel impact thành release

30 Hz, seed 20261016:

- f0: Note On 60, velocity .85; f12: Note Off.
- f18: Note On hợp âm **48/55/60/61/64/67/72/73**, velocity .9; f36: Note Off cả tám.
- Mỗi Note On có đúng một `Impact` cùng pitch/strength. Held lấy physical pressed snapshot; release do `LiveNote(false)` thật.
- Pedal xuống f24, lên f54; f36 đã hết physical pressed dù pedal còn xuống.
- Checkpoints: **f2/f10/f20/f33/f38/f56/f149**. Mỗi checkpoint replay fresh simulation từ f0 với fixed dt; chỉ raster frame cuối. Không phải video/continuous-motion hay MIDI thiết bị thật.
- Hàng giữ hợp âm có thể còn đuôi release của nốt đơn f12. Không coi mọi release trước f36 là lỗi.

## Kiểm chứng đã qua

- Raw particle hash và population giữ nguyên giữa classic/mixed/front-control; normalized sprite/AR-quad geometry, packet counts và actual Stage key hash giữ nguyên. Normalization cố ý loại RGBA/material tags, nên **không tuyên bố actual RGBA/packet bytes của các material bằng nhau**. Stage note bars có count guard, không có full Stage-note vertex-hash proof.
- Key light 0/100 không đổi raw particle state hoặc actual sprite/AR packets/key packets. AR reference giữ nguyên pixel; không yêu cầu Stage/AR packed counts hay độ sáng bằng nhau.
- AR không chứa Stage key/note/sprite geometry. Có live note bars tại các checkpoint held.
- Off và rest không có raw particles; mọi material ở rest off-identical. Pedal-up được phép đã hết hạt, không ép kéo dài lifetime để pass.
- Đối chiếu local thêm: cả **tám pitch** có raw origin-1 release ở f38 trong mọi non-off palette/light/view/variant. Không chỉ kiểm thấy một đuôi release cũ.
- Kiểm tra local cuối: **144 Python tests PASS (44.491s)**, static source check và `git diff --check` PASS; viewer adapter được kiểm trong synthetic roundtrip và trên publication thật. Unit fixtures synthetic chỉ kiểm validator/adapter, không làm art evidence.
- Chromium thực PASS: sáu sheets / 24 pane đúng hash BGRA, sáu tổ hợp controls, đổi nhanh không hiển thị state cũ, mobile 390px không tràn và không page errors. Đã xem cả sáu sheets và screenshot trang; không tuyên bố đã xem đủ 1.120 originals.

## Phát hiện: held bị chặn khỏi AR khi hợp âm dày

Trong Prism/Key light 100/mixed, AR reference:

| Recipe | Raw held tại f10 | AR-selected held f10 | Raw held tại f33 | AR-selected held f33 | Tổng AR-selected f33 |
|---|---:|---:|---:|---:|---:|
| embers-wisps | 54 | 54 | 666 | 0 | 192 |
| splash-flames | 23 | 23 | 243 | 0 | 192 |
| fireworks-wisps | 54 | 54 | 671 | 0 | 192 |
| confetti-flames | 23 | 23 | 256 | 0 | 192 |
| dust-wisps | 54 | 54 | 681 | 0 | 192 |

Ở **20/20** chord-held AR cases mỗi variant classic/mixed/front-control (5 recipes × 2 palettes × 2 lights), 192 selected đều thuộc origin 0, không có origin 2. `GpuArLayer.Effects.cs::BuildStageParticles` hiện ưu tiên non-held trước, rồi mới held; khi pass đầu dùng hết cap, vòng held không được chạy. Đây là bằng chứng cụ thể về **starvation do ưu tiên trong fixture dày**, không phải chẩn đoán chỉ từ ảnh mờ. Không suy rằng mọi raw particle còn lại đều đủ alpha/projectability, hoặc mọi hợp âm/camera đều bị như vậy.

Nốt đơn f10 vẫn chọn đủ held trong năm mẫu Prism trên. Classic cũng có hiện tượng ở hợp âm, và Key light 0/100 cho ảnh AR giống nhau: không gán vấn đề này cho artwork v3 hay bản sửa Key light. Việc tăng độ sáng/mật độ không sửa được việc held chưa được đưa vào draw list.

### Depth và số đo hình ảnh

Front-control đổi pixel ở **28/140** Stage pairs của full matrix; **0/140** AR pairs. Sáu cặp Stage trong subset Prism/100/f33–f38 đều pixel-identical, nên viewer không được dùng làm bằng chứng phải đổi depth. Chưa chọn policy Stage mới.

`Signal` là tổng |ΔRGB| toàn frame so với effect-off cùng scene/input/time. `VisiblePixels`: max-channel delta >4. `ClippedDeltaPixels`: trong vùng delta đó, có ít nhất một kênh encoded RGB ≥250; **không phải white-pixel count, luminance hay điểm đẹp/xấu**. Bàn phím sáng chung với off không bị quy thành “hạt cháy trắng” từ số đo này. Stage raw/AR-selected khác nhau còn chịu alpha/projection/cap; không suy toàn bộ chênh lệch là starvation ngoài các case đầy cap đã phân tích.

## Publication, chạy lại và bước sau

Lượt đầu [38845844652](https://github.com/lxmtuu/PianoPath/actions/runs/38845844652) build/render/validate ảnh gốc thành công nhưng fail budget 350.000 byte (thực đo 680.525). Đã nâng cap lên 750.000 sau khi lấy lại **310.740 byte** bằng lossless opaque-still compaction của đúng hai file allowlisted:

- `prism-flow-p3a-aux/wisps-60-ar-active.png`: BGRA `C05BE0BF209E37C35E6BA6042403535748E9502722BBEC65AEBE53BF486B155B`.
- `held-particle-controls-scenes/flames-prism-chord-ar-near-overhead.png`: BGRA `3B776048492CB17608564AC057D741CE2CD5A8E19D4C303FD17749DF679F2E19`.

Giữ kích thước/BGRA và display chunks, không chạm APNG/motion hay giảm chất lượng để lách cap. Bộ subset chỉ hiển thị ba recipes đầu, Prism/100, f33/f38; các tổ hợp khác chỉ có records local và originals trong artifact.

```sh
PianoPath.exe --verify --verify-key-ensemble-only --verify-review-out=<directory>
python tools/review_key_particle_ensemble.py <directory> --source-root .
# Pillow; output ngoài Git
python tools/serve_key_particle_ensemble.py --output /path/to/cache/ensemble --port 8783
# puppeteer-core + CHROME_BIN hoặc @sparticuz/chromium
node tools/check_key_particle_ensemble_browser.cjs http://127.0.0.1:8783 /path/to/cache/review.png
```

CI lane chuyên biệt dùng workflow input `review=key-ensemble` hoặc push marker `[ensemble-native]`, concurrency riêng để không hủy full run. Lượt này dùng push vì quyền integration không cho workflow dispatch; không cần credential mới để push.

**Ưu tiên kế tiếp được đề xuất:** một trial selection policy riêng, default-off, phân bổ cap hiện có giữa impact/release/held và pitch để held không bị loại cả nhóm. Giữ nguyên cap 192, births/RNG/physics/artwork và giữ output order ổn định trong tập đã chọn; phải đo/kiểm riêng thay đổi selected identities/geometry, không gọi đây là material-only. **Chưa triển khai trial đó trong lượt này.** Sau đó mới mở rộng full-scene motion, AR oblique/near-overhead, palette/preset breadth và hardware/MIDI/camera checks. Chưa redesign Settings hoặc promote art/depth defaults.


**Cập nhật lượt kế tiếp:** đã triển khai [AR fixed-cap selection trial riêng, default-off](AR-PARTICLE-BUDGET-TRIAL.md). Native 904 captures và source/payload/under-cap/frozen-default guards đã PASS; có viewer trái/phải với ba AR poses và 16 frame quanh Note Off. Kết quả này không đổi provenance/source `79930e5` của ensemble gallery ở trên và không tự promote trial hay Settings.
