# SeeMusic — phân tích ngữ pháp chuyển động hạt và đối chiếu với PianoPath

> **Ngày:** 2026-10-19 · **Nhánh:** `arena/efa69378-pianopath` · **Loại:** phân tích tham chiếu, **không sửa mã sản phẩm**
> **Kèm theo:** [SeeMusic-style motion playground](prototypes/particle-motion-lab/seemusic.html) — dựng lại *ngữ pháp*
> chuyển động (không phải bản sao app) để xem bằng mắt từng kiểu.
> **Bản trước:** [PARTICLE-MOTION-REVIEW.md](PARTICLE-MOTION-REVIEW.md) — số đo của hệ hạt PianoPath hiện tại.

## 0. Mức độ tin cậy — đọc trước

| Loại thông tin | Nguồn | Tin được đến đâu |
|---|---|---|
| Tên lớp, tên preset, tên control | Transcript hướng dẫn chính thức của SeeMusic (YouTube, 2023) + trang support/feature chính thức + forum chính thức | **Cao** — đây là lời của tác giả app |
| Hành vi của từng preset ("explode", "twirl", "high turbulence"…) | Cùng transcript + mô tả của người dùng | **Trung bình** — mô tả bằng lời, không phải công thức |
| Công thức vật lý bên trong | — | **Không biết.** SeeMusic đóng; nguồn bên thứ ba cho là viết bằng Unity. Mình **không chạy app** trong phiên này |
| Con số (size, count, speed, life) | Do PianoPath tự chọn cho prototype | **Thấp** — là của PianoPath, không phải của SeeMusic |
| Điều *người xem cảm nhận* | Ảnh chụp từ prototype + suy luận từ mô tả | **Trung bình** |

Nói thẳng: mình **không reverse-engineer** SeeMusic. Thứ mình làm được là đọc đúng cái app đó *công bố*, dựng lại
các kiểu chuyển động đó trong một playground, rồi so với PianoPath. Mọi chỗ suy luận đều được đánh dấu.

## 1. SeeMusic có những lớp hạt nào

Forum chính thức và bài hướng dẫn của app nói **ba lớp bật/tắt độc lập**, nằm trong *Particle Settings* (trong *Effect Settings*):

| Lớp | Hình ảnh | Vai trò trong bố cục | Ghi chú |
|---|---|---|---|
| **Particles** | Chấm sáng nhỏ, sống ngắn | "Điểm nhấn" — lấp lánh tại phím vừa gõ | Lớp mặc định, có preset riêng |
| **Streaklets** | Vệt dài (long-exposure) | "Chuyển động" — vẽ *đường đi* của hạt | Biên bản 8.5.1: *"Improved Streaklets"* — tức là vẫn đang được tinh chỉnh |
| **Smoke** | Mảng mờ lớn, nổi lên, sống lâu | "Chiều sâu" — khí quyển, không giành sự chú ý | Alpha thấp, gần như không có lõi sáng |
| **Particle Rays** (riêng) | Tia phát ra từ phím | Hiệu ứng mạnh, đắt | Tác giả khuyến nghị **chỉ bật khi render**, không dùng livestream |

Điểm đáng học ngay: **ba lớp này độc lập**. Người dùng có thể tắt Smoke mà giữ Particles. PianoPath hiện gộp
Particles và Streaklets vào **một** enum `Shape` và **một** danh sách `_particles`, phân biệt bằng hình dáng chứ
không bằng vai trò. Smoke thì có — nhưng ở **lớp ambient nền** (`GpuStageSimulation.Ambient.cs`), không phải lớp
hạt theo nốt; `Shape.Dust` trong burst là dạng gần nhất nhưng vẫn thuộc burst family.

## 2. Chuyển động được điều khiển bằng gì

Theo transcript chính thức, Particle Settings có: **size, count, speed, turbulence**. Bản tóm tắt tính năng của
bên thứ ba còn nhắc **wind speed** và **brightness/bloom** — nên coi hai từ này là mô tả *tính năng có tồn tại*,
không phải tên control chắc chắn. Forum chính thức nhắc **magnetism**. Lịch sử phiên bản ghi *"Improved Particle
Turbulence"* (8.5) và *"Improved Streaklets"* (8.5.1), tức đây là hai chỗ tác giả đang đầu tư.

| Nhóm | Control | Nó làm gì với chuyển động |
|---|---|---|
| Sinh | Count | Mật độ; đổi *kết cấu* từ "vài tia" sang "đám bụi" |
| Sinh | Speed | Quãng đường hạt đi được; với Streaklet nó đổi **độ dài vệt** |
| Sinh | Size | Độ lớn điểm sáng; nhỏ + nhiều = lấp lánh, lớn + ít = khối |
| Lực | Gravity (ẩn) | Hạt rơi xuống và tản ra mặt phím |
| Lực | **Wind** | Trường gió ngang, đẩy cả đám hạt cùng chiều |
| Lực | **Turbulence** | Nhiễu không gian làm quỹ đạo bẻ cong, rối |
| Lực | **Twirl** (preset) | Lực tiếp tuyến quanh phím → xoáy tròn |
| Tương tác | **Magnetism** | Hạt từ 2+ nốt **hút/đẩy nhau** — tạo "dòng" giữa các phím đang kêu |
| Hiển thị | Direction | Chọn hướng phát (một số bản yêu cầu chọn direction thì mới phát) |
| Hiển thị | Colour | Theo màu nốt; nhưng khi dùng **gradient** thì hạt mặc định **trắng** (người dùng phàn nàn) |

## 3. Tám preset hạt — ngữ pháp chuyển động

Transcript 2023 giới thiệu **tám** preset (Default cộng bảy kiểu có tên) và mô tả từng cái. Cột phải là **cách mình dựng lại trong playground**:

| Preset (nguyên văn mô tả) | Cảm giác | Cơ chế đã dựng lại |
|---|---|---|
| **Default** | Hạt nhỏ bám phím, mật độ vừa | Nón phát hẹp + trọng lực vừa + drag cao |
| **Explode** — *"flying out to the side"* | Vỗ ngang mặt phím | Góc phát **gần như nằm ngang**, gravity ~0, đời sống ngắn |
| **Fast playing** — *"nice bursts for pieces with lots of short staccato notes"* | Câu staccato lấp lánh, nốt dài lặng hơn hẳn | **Hệ số theo độ dài nốt**: nốt < 0,25 s × 2,4 lượng hạt |
| **High count** — *"lots of very small particles"* | Sương lấp lánh thay vì tia | Count ×4, size ÷1,7, speed ÷1,4 |
| **High turbulence** — *"more ornate turbulence"* | Tia bẻ cong, quấn vào nhau | Nhiễu ×2,6 so với Default |
| **Long streaklets** — *"does away with the small particles and just gives you big long streaklets"* | Toàn là vệt dài | **Tắt lớp Particles**, chỉ còn Streaklets, life ×1,6 |
| **Straight** — *"simple straightforward effect"* | Sạch, không nhiễu | Turbulence = 0, wind = 0 |
| **Twirl** — *"more twirling type of turbulence"* | Xoáy tròn quanh phím | Lực **tiếp tuyến** quanh tâm phím, không phải nhiễu |

Ba điều rút ra từ chính danh sách này:

1. **"Preset" ở SeeMusic là *tổ hợp tham số*, không phải loại hạt mới.** Cả tám dùng chung một bộ máy.
   Đây đúng là điều master roadmap của PianoPath đã ghi: *"Fire/Ice/Galaxy… là recipe/preset, không phải renderer đặc biệt"*.
2. **Hai preset chỉ khác nhau ở một quyết định định tính**: *Long streaklets* = tắt particles; *Straight* = tắt nhiễu.
   Nghĩa là người dùng được phép *tắt một lớp* chứ không chỉ kéo số về 0.
3. **"Fast playing" là preset duy nhất phản ứng với *cấu trúc* nhạc** (độ dài nốt), không phải với velocity.
   PianoPath hiện chỉ có `ParticleResponse` theo **cường độ**, không có gì theo **độ dài nốt**.

## 4. Tổng hợp: có bao nhiêu kiểu chuyển động

Gom lại thành **sáu họ**, mỗi họ là một câu hỏi thiết kế khác nhau:

### Họ A — Sinh (khi nào, ở đâu)
1. **Impact burst** tại phím vừa gõ — có ở cả hai app.
2. **Sustain stream** khi giữ phím — có ở cả hai; PianoPath dùng chung nón phát (xem review §4.1).
3. **Note trail** *(suy luận)*: hạt rơi *dọc theo* thanh nốt đang hạ xuống. Transcript chỉ nói hiệu ứng nốt có
   nhiều lớp; việc SeeMusic có hạt bám thân nốt hay không thì mình **chưa thấy tài liệu**. PianoPath có
   `FallingTrail` — vẽ vệt theo nốt, không phải hạt mô phỏng.
4. **Release puff** khi nhả phím — PianoPath có (`SpawnReleaseFx`); SeeMusic không nêu rõ trong transcript.
5. **Rays** phát xuyên từ phím — PianoPath có *light beams* (không phải hạt) ở `ShowLightBeams`.

### Họ B — Hướng và hình dạng quỹ đạo
6. **Nón phát** (direction + spread) — cả hai có.
7. **Explode ngang** — PianoPath **không có** (mọi burst đều hướng lên, ±65°).
8. **Vệt dài (streaklet)** — PianoPath có `Shape.Streak`, độ dài theo tốc độ; chỉ thiếu lớp điều khiển riêng.
9. **Xoáy (twirl)** — PianoPath có `Spiral` nhưng nó chỉ *bẻ góc phát* lúc sinh, không tạo lực tiếp tuyến.

### Họ C — Trường lực
10. **Trọng lực + lực nổi** — cả hai có.
11. **Gió (wind)** — SeeMusic có slider; PianoPath có `VectorField` nhưng **vô hình ở mặc định** (3,6 px trên 0,9 s).
12. **Nhiễu (turbulence)** — SeeMusic có slider + preset + bản 8.5 cải tiến; PianoPath chỉ có một `sin(Y)` dùng chung.
13. **Magnetism giữa các nốt** — **PianoPath không có gì tương đương.** Đây là khác biệt lớn nhất về *ý tưởng*.

### Họ D — Vòng đời
14. **Size theo tuổi** — SeeMusic có (High count / Smoke cho thấy rõ); PianoPath gần như hằng số.
15. **Màu theo tuổi** — PianoPath có (flame đổi màu theo nhiệt; ember có blackbody cooling ở WPF).
16. **Fade in/out** — PianoPath tuyến tính, không có fade-in (xem review §4.5).
17. **Sống lâu = mờ + to** (smoke) — PianoPath `Shape.Dust` gần đúng nhưng nằm trong burst family.

### Họ E — Hiển thị
18. **Bloom/glow reactive** — cả hai có; SeeMusic tách Glow (halo tĩnh) và **Real-time Bloom**.
19. **3D + camera** *(suy luận)* — SeeMusic có note style 3D, và nguồn bên thứ ba cho rằng app viết bằng Unity; khả năng cao hạt
    nằm trong scene có phối cảnh. PianoPath có AR projection + stage phím 3D, nhưng hạt phẳng 2D (trừ AR).

### Họ F — Tác giả & hiệu năng
20. **Preset gallery tách *note styles* và *particle styles*** — PianoPath gộp chung một preset (14 built-in + 3 community).
21. **Chọn CPU hay GPU particles** *(suy luận từ forum)* — người dùng báo SeeMusic 2.0 có backend GPU (Unity
    VisualEffect) và CPU. PianoPath: mô phỏng CPU, vẽ GPU, không có lựa chọn (và cũng **không cần**, vì AR đọc
    vị trí trên CPU).
22. **Đánh đổi Count ↔ Speed ↔ Rays** — tài liệu SeeMusic nói rõ Rays là thủ phạm tụt FPS.

## 5. Đối chiếu trực tiếp với PianoPath

| Kiểu chuyển động | SeeMusic | PianoPath | Khoảng cách |
|---|---|---|---|
| Impact burst | ✓ | ✓ (`Impact`) | — |
| Sustain stream | ✓ | ✓ nhưng **dùng chung nón phát** | **Lớn** (review §4.1) |
| Release | (không nêu) | ✓ | PianoPath hơn |
| Vệt dài | ✓ lớp riêng | ✓ `Shape.Streak` | Thiếu công tắc lớp |
| Explode ngang | ✓ preset | ✗ | **Lớn** (rẻ để thêm) |
| Twirl thật | ✓ preset | ~ `Spiral` chỉ bẻ góc | **Lớn** |
| Wind | ✓ slider | ~ vô hình ở mặc định | **Lớn** |
| Turbulence | ✓ slider + preset | ~ một `sin` dùng chung | **Lớn** |
| Magnetism | ✓ | ✗ **không có** | **Lớn** |
| Size theo tuổi | ✓ | ~ | Trung bình |
| Fade-in | (không rõ) | ✗ | Nhỏ |
| Smoke như lớp nền | ✓ | ~ ambient `Smoke` có, nhưng tách khỏi hệ hạt theo nốt; `Dust` nằm trong burst | Trung bình |
| Preset note/particle tách rời | ✓ | ✗ gộp | Trung bình |
| Chống trùng lặp GPU/WPF/AR | không áp dụng | **đang lệch** (review §4.3) | Việc nội bộ |

**Tổng kết một câu:** SeeMusic mạnh ở **trường lực có thể chỉnh** (wind, turbulence, twirl, magnetism) và ở
**việc tách lớp theo vai trò**; PianoPath mạnh ở **vật liệu và độ chính xác theo MIDI** (nhiệt, blackbody,
hold/release, AR theo phím thật) nhưng yếu đúng chỗ SeeMusic mạnh.

## 6. Nên lấy gì, không nên lấy gì

**Nên lấy (ý tưởng, không phải asset):**

1. **Ba lớp theo vai trò** — Particles / Streaklets / Smoke, mỗi lớp một công tắc. Đổi lớn nhất, chi phí thấp,
   và khớp với cấu trúc preset hiện có.
2. **Magnetism** — chỉ cần một lực hút giữa các phím đang kêu là có thứ mà hiện tại không có gì thay thế.
   Đây cũng là thứ *rất hợp* với piano: hai tay đang chơi tạo thành hai cụm, hạt bị kéo về phía cụm kia.
3. **Preset là tổ hợp tham số**, không phải loại mới — giữ nguyên luật của master roadmap.
4. **Preset phản ứng với cấu trúc nhạc** ("Fast playing" theo độ dài nốt). PianoPath đã có `note.Duration`
   trong `BuildNotes`, nên đây là thay đổi rất rẻ.
5. **Hai preset định tính** kiểu *Straight* (tắt nhiễu) và *Long streaklets* (tắt particles) — người dùng hiểu
   ngay, không cần kéo bốn slider.

**Không nên lấy:**

- **Không sao chép giá trị số, màu, tên preset, asset.** Tên preset chỉ dùng làm *nhãn tham chiếu* trong tài liệu.
- **Không bắt chước kiến trúc Unity/VisualEffect của họ.** PianoPath cần mô phỏng CPU để AR và video đọc được.
- **Không thêm "Particle Rays"** khi chưa đo: chính tài liệu SeeMusic nói nó là thứ đắt nhất.

## 7. Đề xuất cập nhật cho PianoPath (nối tiếp W1–W3)

Đặt cạnh lộ trình trong [review](PARTICLE-MOTION-REVIEW.md) §7:

| Ưu tiên | Việc | Vì sao |
|---|---|---|
| **P0** | Tách **3 lớp** (Particles / Streaklets / Smoke) trong `GpuStageSimulation` theo *vai trò*, không theo `Shape`; ambient Smoke hiện có chuyển thành lớp nền của cùng hệ | Mở đường cho mọi thứ còn lại; mỗi lớp một công tắc |
| **P0** | `Magnet` — lực hút giữa các pitch đang kêu, cường độ 0-100 | Khác biệt *ý tưởng* so với hiện tại, không thể thay bằng slider khác |
| **P1** | `Twirl` thành **lực tiếp tuyến thật** quanh tâm phím (không chỉ bẻ góc lúc sinh) | Đúng nghĩa "twirl", dùng lại được trong AR |
| **P1** | `BurstDirection` thêm **Sideways** (explode ngang) | Một preset mới gần như miễn phí |
| **P1** | **Note-length response**: `ShortNoteBoost` cho nốt < ngưỡng | Học từ "Fast playing"; rẻ vì `note.Duration` đã có |
| **P2** | **Tách preset note-style và particle-style** trong UI (không đổi schema JSON nếu làm được bằng view) | Người dùng chọn "gems" + "high turbulence" độc lập |
| **P2** | **Smoke theo nốt** (lớp khói riêng cuốn lên từ phím) tách khỏi ambient Smoke hiện có | Cho chiều sâu mà không làm bẩn nốt; tận dụng `Shape.Dust` đã có |

## 8. Xem prototype

```bash
xdg-open docs/prototypes/particle-motion-lab/seemusic.html   # hoặc double-click trên Windows
node tools/check_particle_motion_lab.cjs   # kiểm tra cả hai prototype
```

Trong playground: 9 preset ở cột trái (hai cái **chưa có** ở PianoPath — Explode, Twirl — được đánh dấu
*"chưa có"*; năm cái có một phần được đánh dấu *"một phần"*),
ba công tắc lớp, và năm slider (Count, Speed, Turbulence, Wind, Magnetism). Phím `1…9` đổi preset, `Space` dừng, `R` chơi lại.
Cột phải của mỗi preset ghi rõ nó đang *mô phỏng điều gì* và nguồn của mô tả đó.

## 9. Nguồn

- SeeMusic Academy — *How To Make a Piano Video With Particles* (2023), transcript có mục Particle Settings và tám preset:
  <https://www.youtube.com/watch?v=l8lboohrSUY>
- SeeMusic — *How-To Make a Piano Video with Particles — 2020 Version*, transcript nhắc Particle Settings
  (tăng/giảm số hạt để thành dày đặc hay thưa) và việc thêm đèn khi footage thiếu sáng:
  <https://www.youtube.com/watch?v=NiPJyWJgIs4>
- Forum chính thức — *How to access other particles?* (ba lớp Particles / Streaklets / Smoke, Particle Settings,
  magnetism): <https://www.seemusicapp.com/forumold/technical-support/how-to-access-other-particles>
- Forum chính thức — *SeeMusic fps very low* (Particle Rays chỉ nên dùng khi render):
  <https://www.seemusicapp.com/forumold/technical-support/seemusic-fps-very-low/dl-focus>
- Trang chính thức — particles "rays, smoke, and glow": <https://www.seemusicapp.com/>
- Trang Support — khuyến nghị tắt Saber/Glow/Particle Rays và giảm Particle Count khi lag:
  <https://www.seemusicapp.com/support>
- Forum chính thức — chủ đề *In depth description of colors, particles etc.* (hạt mặc định **trắng** khi chọn
  màu gradient; có mục "Particles direction and color"):
  <https://www.seemusicapp.com/forumold/tutorial-requests/in-depth-description-of-colors-particles-etc>
- Trang tổng hợp tính năng bên thứ ba (nhắc size / count / wind speed / bloom / brightness):
  <https://mwm.ai/apps/seemusic/1494198562>

> Ghi chú: các trang `forumold/...` đã trả **404** khi kiểm tra ngày 2026-10-19. Nội dung trích ở đây lấy từ
> đoạn mô tả trong kết quả tìm kiếm, không phải mở lại được. Nếu cần dẫn nguồn chính thức cho sản phẩm, hãy
> dùng các trang `seemusicapp.com` còn sống và transcript YouTube.
