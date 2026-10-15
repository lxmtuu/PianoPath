# Tint / spill và độ phủ hợp âm dưới AR cap

## Trạng thái

Lát chẩn đoán tiếp theo sau [held controls](KEY-PARTICLES-HELD-CONTROLS.md). **Đã capture và đọc mẫu ảnh thực tế.** Run [38665781114](https://github.com/lxmtuu/PianoPath/actions/runs/38665781114), source **`1597661`**, hoàn tất **SUCCESS**: static, portable .NET tests, Windows build, 192 WARP frames/48 native quartets/source guards, full verification **6.096 assertions / 1 skipped group**, benchmark, installer, galleries và distributable. Evidence/52 PNGs được publish ở **`4a3f94c`**. Run trước `38665554308` đã bị supersede/cancel khi bổ sung canonical ordering cho floating-point age summaries; không dùng run đó làm kết quả cuối.

Kiểm độc lập tại local: **48/48 raw quartet hashes, 4/4 low full-frame hashes, 19/19 canonical source hashes**, đầy đủ metadata 192 captures. So manifest controls mới với capture đã kiểm trước (`edce79d`): **288/288 full-frame pixel hashes, 72/72 sheet hashes và toàn bộ comparison metrics không đổi**. Những frame/sheet này không bị đổi pixel bởi seams/observers mới. Chỉ 4 originals mới có ở local; CI kiểm đủ 192 originals và từng native crop. Không tuyên bố đã mở native toàn bộ 192 ảnh.

Local: **34 Python tests PASS**, source checker **305 C# / 2 XAML PASS**, diff check PASS. Synthetic contract E2E cũng kiểm đủ 192 fake frames/48 crop reconstructions và từ chối sheet bị sửa dù hash của sheet được cập nhật — đây là test validator, **không phải WARP evidence**. Viewer dùng metadata thật qua 48 mocked-DOM selections + stale/error decode guards và 59 HTTP routes; không thay cho browser/GPU visual test.

Không đổi shader v2, material/lifetime/clock/RNG, production recipes, cap 192 hoặc thứ tự chọn AR particles. Wisps/Flames vẫn experimental/default-off; không có settings/menu binding mới. Mọi burst/held family còn lại vẫn thuộc roadmap, menu vẫn cuối cùng.

## Ma trận giới hạn

- Load: Wisps normal (`WispAmount=32`), Wisps low (`8`, chỉ fixture), Flames normal (giữ intensity 70, không thay intensity để giả lập density).
- Palette: Prism và Indigo; warm Amber vẫn có trong bộ controls trước.
- Cùng hợp âm MIDI 60/61/64/67, velocity 0,9, giữ 36 bước × 1/60 s, seed 20261014, pedal/beat off.
- Stage và AR reference/oblique/near-overhead; cùng camera fixture và calibration của bộ trước. AR Ultra/Fit/70°/delay 0, không mirror.
- Ba factor độc lập: particles, key tint, neighbouring-key spill (2×2×2).

**24 cảnh × 8 variants = 192 full frames**, 48 native quartet sheets. Mỗi cảnh có hai sheets (particles off/on), mỗi sheet: trên trái neither, trên phải tint, dưới trái spill, dưới phải both. Mỗi pane 384×360 pixel native.

### Phạm vi “tint”

Ở Stage, “tint” là **pressed-key tint + emission group** (`Emit.W`), spill là `Base.Y`; không phải phép tách tint khỏi emission. Giữ nguyên static ivory illumination, RGB, độ nhấn, shadow và felt. Ở AR, tắt riêng calibrated key patches hoặc `BuildProjectedNoteLights`; KeyLight vẫn dùng 0,6 khi một trong hai bật. Đây là seams verification-only, mặc định không kích hoạt; không thêm setting cho người dùng.

Bốn trạng thái tương đương light-group của normal loads đã giữ **đúng raw pixel hash** với 288-frame controls vừa render trong cùng run. Nhánh product mặc định vẫn phải qua toàn bộ baseline, full verification và benchmark hiện có.

## Đo độ phủ thực sự, không suy từ số sprite tổng

Observers mặc định null, thông báo **sau khi** Stage sprite / AR particle đã được pack thành công, không tham gia selection và không thay simulation. Một snapshot read-only ở frame cuối lấy cả live pool:

1. **Raw:** live particle pool theo pitch.
2. **Eligible:** cùng điều kiện resolve surface và alpha >0,003 của Stage. Đây không phải danh sách nguyên nhân AR rejection; pose/near-plane/cap vẫn là các bước khác.
3. **Packed:** callback thực tế của active renderer. Tổng phải khớp StageSpriteCount hoặc ArParticleFxCount, inactive-mode counters phải zero.

Mỗi nhóm giữ rõ cả bốn pitch (kể cả zero), count, histogram tuổi tuyệt đối [0;0,2), [0,2;0,4), [0,4;+∞), min/max/mean age và mean normalized progress. Sorted hashes của (pitch,age,life) kiểm ánh sáng/view không đổi population; histogram/count không phải on-screen area, occlusion hoặc perceptual visibility.

Guard bắt buộc:

- Tint/spill không thay candidate/packed population; particles/spill không thay tint packing và particles/tint không thay spill packing.
- Live pool không đổi theo view hoặc light factors trong cùng family/load/palette/input.
- Low Wisps và Flames: eligible <192, đủ bốn pitch, packed sample hash bằng eligible hash. Không lặng lẽ chấp nhận particle/pitch bị mất ở sub-cap.
- Normal Wisps: fixture phải thực sự vượt cap; AR phải pack 192, không sửa cap cho tests xanh.
- Particle-off pixels phải giống giữa family/load tương ứng.
- Native key-mask và AR-build tests kiểm riêng bốn tổ hợp tint/spill, đồng thời kiểm geometry/state/particle preservation.

## Evidence và giới hạn

PNG round trip tại encoder; manifest ghi source revision, 19 canonical source hashes, camera/pixel hashes, factor flags, per-pitch age summaries. Python kiểm schema/factor pairing, coverage arithmetic/cross-view invariants, source hashes; decode đủ 192 full images + 48 sheets và so native crop trong CI. Schema-1 viewer tiếp tục đọc evidence cũ có 18 source hashes hoặc code mới có 19.

Đủ full frames, manifest và self-contained viewer ở artifact `phase1-visual-review/held-light-isolation/`. Git chỉ lưu manifest tại `docs/evidence/held-light-isolation/` và 48 sheets + **4 low-Wisps/Prism combined full scenes** tại `docs/previews/held-light-isolation/`. Normal full-scene equivalents đã có trong bộ trước; không commit thêm cả dataset hoặc HTML/base64 lớn.

```sh
python -m pip install Pillow==11.3.0
python tools/review_held_light_isolation.py <review>/held-light-isolation --source-root .
```

Subset published có thể dùng `--sheets-only` khi đặt sheets trong thư mục `sheets/` cùng manifest. Chế độ này không chứng nhận full frames không có tại chỗ, và ghi rõ giới hạn đó.

## Kết quả population / age thực đo

Các số dưới đây lấy từ `on-both` trong [manifest](evidence/held-light-isolation/manifest.json); ba light states còn lại giữ nguyên candidate/packed sample hashes. Hai palette cùng population hashes; ba AR poses cùng packed hashes trong fixture này.

| Load / output | Raw = eligible | Packed | Theo pitch 60 / 61 / 64 / 67 | Packed age bins young / middle / older | Mean age (s) |
|---|---:|---:|---|---|---:|
| Wisps normal / Stage | 304 | 304 | 76 / 76 / 76 / 76 | 92 / 104 / 108 | 0,307895 |
| Wisps normal / AR, cả 3 pose | 304 | 192 | 47 / 47 / 49 / 49 | 92 / 100 / **0** | 0,198611 |
| Wisps low / Stage và AR | 76 | 76 | 19 / 19 / 19 / 19 | 24 / 28 / 24 | 0,297368 |
| Flames / Stage và AR | 83 | 83 | 21 / 21 / 20 / 21 | 36 / 40 / 7 | 0,216466 |

- **Normal Wisps có age bias rõ ở AR, không phải mất một phím hoàn toàn.** Đóng gói 192/304; cả 108 candidates tuổi ≥0,4 s và 4 middle-age candidates không được chọn. Max packed age 0,383333 s (MIDI 60/61: 0,366667; 64/67: 0,383333), so với eligible max 0,600000 s. Nhóm trẻ nhất vẫn đủ 92/92. Đây là kết quả của fixture/cap/traversal hiện có, không chứng minh cân bằng pitch/age trên mọi input hoặc mixed effects.
- **Dưới cap không mất eligible sample:** low Wisps 76/76 và Flames 83/83, sorted sample hashes lẫn từng row/count/age statistics giống Stage/AR. Stage cũng giữ đủ 304 normal Wisps. Trong các cảnh này raw = eligible; không có thêm hao hụt do Stage surface threshold. Không suy rộng điều đó cho mọi frame.
- Low đổi birth cadence bằng WispAmount, không phải lấy một subset cố định của normal pool. Seed/RNG algorithm/lifetime recipe giữ nguyên nhưng lượng/lịch tiêu thụ RNG thay đổi tự nhiên theo births; không so một-một particle position/life giữa hai load.

## Nhận xét ảnh thật và contrast

Đã mở bốn overview 50% chứa đủ 48 quartets. Sau đó đọc native tám sheets: Wisps normal/Prism/AR reference **off + on**; Wisps low/Prism/AR reference/on; Wisps low/Indigo/AR oblique/on; Wisps normal và low/Indigo/Stage/on; Wisps low/Prism/AR near-overhead/on; Flames/Prism/AR reference/on. Đã mở native đủ **bốn low/Prism full scenes 1280×720**. Đây là sampled static review, không phải native review mọi capture hoặc motion.

1. **Dải màu dài theo phím AR thuộc tint/key patches.** Nó có trong particles-off/tint-only; không cần spill hoặc particles. **Quầng mềm lan ngang qua các phím lân cận thuộc spill**, hiện riêng trong spill-only. Tắt một nhóm không xóa nhóm còn lại. Không quy toàn bộ vùng sáng cạnh root cho shader hạt.
2. **Low Wisps vẫn gặp contrast/scale ở AR khi đã giữ đủ population.** Native reference/near-overhead còn các nét rất nhỏ, nhạt ở mép ivory; combined vẫn bị mảng phím chiếm ưu thế. Vì vậy không thể quy toàn bộ vấn đề độ rõ cho cap, cũng chưa có cơ sở nói giảm density là bản sửa artwork. Full low/oblique cũng chưa cho độ tách rõ của thân hạt.
3. **Stage low thưa và mảnh hơn Stage normal**, dễ thấy trên Indigo; thay alpha toàn cục sẽ đánh vào nhánh này trong khi AR còn vấn đề projection/background/tint. Count parity dưới cap không đồng nghĩa screen-size, area, occlusion hoặc readability parity.
4. **Flames vẫn có thân/đỉnh rõ hơn Wisps trong reference sample**, khi tint/spill tắt hoặc bật; toàn bộ 83 particles được giữ. Chưa suy ra material Flames đã đạt yêu cầu cho camera thật hay temporal input.

Ví dụ full-frame particle-factor footprint RGB >4, **Prism / AR reference**:

| Load | Neither | Tint only | Spill only | Both |
|---|---:|---:|---:|---:|
| Wisps normal | 934 | 794 | 934 | 794 |
| Wisps low | 788 | 528 | 788 | 528 |
| Flames | 1.752 | 1.701 | 1.752 | 1.701 |

Ở reference, tint làm giảm footprint theo ngưỡng này, spill không đổi nó; không suy ra spill vô hại ở mọi pose (oblique có tương tác khác). Đây là display-RGB difference, **không phải brightness, năng lượng cộng tuyến hay điểm chất lượng**. Stage Wisps normal/low lần lượt 2.610/1.001 pixels ở cả bốn light states trong fixture này.

## Quyết định / gate tiếp theo

- Giữ **shader v2 experimental/default-off**; không giảm alpha toàn cục, tăng cap hoặc chỉnh product density/key-light strength dựa vào test này.
- Tách hai vấn đề cho bước tiếp: **contrast / projection / screen scale của held art dưới cap** và **giữ đại diện tuổi khi AR vượt cap**. Nếu thử selection policy khác, đó phải là candidate chẩn đoán riêng, cap vẫn 192, không đổi simulation/seed/emission hay làm mất ưu tiên burst/release trong product mặc định. Chưa triển khai policy mới ở lát này.
- Mọi artwork candidate tiếp theo phải đối chiếu **Stage + AR từ đầu**, gồm normal/low, tint/spill controls và motion; không chỉ làm đẹp một crop.
- Các press/held family khác còn mở trong roadmap; không coi harness này là nâng cấp toàn bộ effects. Settings/menu redesign vẫn **cuối cùng**.
- Synthetic planar camera, static 0,6 s và WARP không thay được temporal, real-camera/hand occlusion/exposure, delay/tier, recording/alpha/WPF hoặc hardware tests.
