# Prism Flow — Lộ trình thiết kế nốt, texture và dòng hạt

> Ngày lập: 2026-10-14. Trạng thái: **P0 có baseline PASS; bản thử P1/P2 palette/satin đã PASS CI và review sáu ảnh; chưa user sign-off/chưa đóng các gate P0 rộng hơn**.
> Tiến độ và bằng chứng P0: [Inventory, baseline inputs và khoảng trống Live Stage–AR](PRISM-FLOW-P0-AUDIT.md). P0 chưa đóng các gate lifecycle/delay/reset/full coverage. Có capture WARP; chưa có hardware benchmark mới.
> **Bản thử P1/P2:** [Prism Flow Preview](PRISM-FLOW-P1-PREVIEW.md) thêm palette 6 stop và bề mặt satin opt-in trên Solid/Glass. Không thay default/preset cũ; [ảnh/evidence](PRISM-FLOW-P1-REVIEW.md) ở run `38601052995`. Chưa có flow particles hoặc user sign-off.
>
> Phạm vi cập nhật: Prism Flow là mẫu thiết kế đầu tiên; chương trình bao gồm đồng bộ **Live Stage + AR mode**, thiết kế/nâng cấp toàn bộ hiệu ứng hạt khi nhấn/giữ phím, và cuối cùng tái thiết kế menu cài đặt dùng chung cho hai chế độ. Không sao chép asset, shader hoặc nhận diện của SeeMusic.
> Cập nhật yêu cầu: 2026-10-14. Đây là phạm vi kế hoạch toàn chương trình, không phải tuyên bố đã triển khai hoặc xác minh toàn bộ AR; evidence theo từng lát dọc được link ở trên.
> Tài liệu này là một lát dọc bổ sung cho [Visual Effects Master Roadmap](VISUAL-EFFECTS-MASTER-ROADMAP.md), không thay thế các gate, audit hay trạng thái capability hiện có.

## 1. Quyết định thiết kế

**Tên làm việc: Prism Flow.** Nền đen sâu; nốt kính màu có thân đầy, viền sáng mảnh; palette cyan–mint–lime–champagne–peach–coral; hạt nhỏ cuộn thành dòng phía trên phím; texture tạo chiều sâu nhưng không làm bẩn hình.

Thứ tự ưu tiên:

1. Đọc được nốt và thời điểm chạm phím.
2. Màu nốt, phím, hạt và hit line thống nhất.
3. Vật liệu có khối ngay cả khi tắt bloom.
4. Hạt có chuyển động chung, không phải mưa chấm ngẫu nhiên.
5. Texture là chi tiết phụ, không phải lớp ảnh màu phủ lên mọi thứ.

Không đổi default Concert Gold, không ghi đè preset người dùng. Dùng Prism Flow để chốt chuẩn hình ảnh trước, sau đó áp dụng quy trình thiết kế/nâng cấp cho **toàn bộ family/style hạt nhấn và giữ phím**, không ép mọi family thành kính/neon. Không mở thư viện texture tùy ý hoặc effect editor tổng quát ở vòng đầu.

**Ba yêu cầu bắt buộc:** đồng bộ Live Stage–AR từ thiết kế/simulation; hoàn tất inventory và review mọi hiệu ứng nhấn/giữ phím; tái thiết kế menu cài đặt gọn, rõ và thống nhất cho hai chế độ ở giai đoạn cuối. AR không còn là hạng mục tùy chọn chỉ xem xét khi phát hành.

### Điều biết được và điều chưa biết từ ảnh

Quan sát được: nền tối, nốt bo góc, thân có sắc độ/highlight, màu chuyển theo chiều ngang trong vùng đang chơi, chấm sáng và vệt cong quanh phím hoạt động.

Chưa xác nhận: thuật toán SeeMusic, quy tắc màu thật, chuyển động theo thời gian, cấu hình bloom, số hạt và loại texture. Curl noise, glass sheen và palette dưới đây là **giải pháp đề xuất của PianoPath**, không phải kết luận reverse-engineering.

## 2. Nền tảng đã có và phần cần bổ sung

| Mảng | Điểm tựa trong repo | Việc cần làm |
|---|---|---|
| Màu | `Stage/PianoStage.cs`: `NoteColorCore`; `Gpu/GpuLook.cs`: bảng màu dùng cho GPU | Palette nhiều stop, vùng pitch và quy tắc nội suy rõ ràng; không giả định gradient hai màu tương đương rainbow. |
| Nốt | `Gpu/StageShaders.hlsl`: `ShadeNoteMaterial`, Solid/Glass | Thử preset trên vật liệu có sẵn; chỉ thêm material ID mới khi ảnh review chứng minh cần. |
| Hạt | `Gpu/GpuStageSimulation.cs`: burst, held emitters, wisps, physics | Chế độ flow opt-in, seed ổn định, budget và lifecycle riêng biệt. Mô phỏng hiện ở CPU, vẽ instance ở GPU. |
| Texture | Có `NoteTexture`, noise procedural và texture cho glyph/background/camera | Chưa phải pipeline material texture tổng quát. Không đổi nghĩa `NoteTexture` của Fire hoặc chiếm dụng glyph atlas. |
| Ánh sáng | HDR, soft-knee bloom, `HitLineColor`, key tint/spill | Cân bằng màu/thân/viền, so khớp vị trí màu với layout phím; đo trước khi thêm pass. |
| Settings | `PianoVisualSettings`, preset/share/normalize, `PianoStage.Gpu.cs`, `GpuLook` | Nối đủ settings → snapshot bất biến → shader/simulation → preset/UI → kiểm thử. |
| Output | GPU stage, WPF fallback, recording và AR có đường riêng | Ghi capability theo từng output; không suy ra parity từ việc dùng chung settings/shader. |

## 3. Đặc tả hình ảnh

### 3.1 Palette: một nguồn màu, nhiều vai trò

Palette gợi ý, **không phải mã màu đo từ ảnh**:

| Vị trí chuẩn hóa | Màu | Vai trò |
|---|---|---|
| 0.00 | `#00CFE8` | Cyan |
| 0.22 | `#00E3BE` | Mint/turquoise |
| 0.44 | `#A9E879` | Lime nhạt |
| 0.64 | `#F2DF85` | Champagne |
| 0.82 | `#FFAD60` | Peach |
| 1.00 | `#FF8E94` | Coral |

- Chuẩn hóa theo khoảng pitch cấu hình; clamp ngoài khoảng. Default của look giữ khoảng cố định, không tự đổi khi bài sang đoạn khác.
- Cho phép chọn “toàn bàn phím” hoặc “khoảng pitch tùy chọn”. Fit theo bài, nếu thêm sau, chỉ tính một lần khi tải và phải hiển thị rõ.
- Chọn và test một quy tắc nội suy thống nhất. Khởi đầu bằng nội suy linear-light giữa các stop; chỉ cân nhắc không gian cảm nhận như OKLab nếu review cần, không thêm độ phức tạp trước bằng chứng.
- Hạt thừa hưởng màu tại lúc phát; highlight chỉ pha trắng cục bộ. Không để hạt đang bay đổi màu theo phím khác.
- Phím lấy màu nốt đang hoạt động. Hit line lấy màu theo layout phím thực, không chỉ chia chiều rộng thành 88 đoạn bằng nhau.
- Quy định rõ hành vi nhiều track cùng pitch và các modulator (velocity/zone/octave). Preset mới tắt các modulator làm lệch palette; không thay hành vi preset cũ.

### 3.2 Nốt: “luminous satin glass”

Các lớp thị giác, không nhất thiết là các draw pass riêng:

1. **Body:** màu đầy vừa đủ, giữa sáng hơn mép; đọc được khi tắt FX.
2. **Bevel/rim:** viền mảnh có anti-aliasing, góc bo vừa; nốt ngắn không biến thành đốm tròn.
3. **Sheen:** phản sáng chéo trắng kem, tiết chế; seed khác nhau cho từng nốt để không giống bản sao.
4. **Texture:** caustic mềm hoặc satin grain rất nhẹ, bị giới hạn bên trong hình SDF.
5. **Emission:** mạnh ở rim, highlight và đầu chạm; không đẩy cả thân lên trắng.
6. **Halo:** quầng mềm bên ngoài, không thay thế silhouette.

Chuyển động sheen chậm, không chớp đồng loạt. Trạng thái chạm sáng lên ngắn rồi ổn định khi giữ; release tắt êm, không pop.

### 3.3 Hạt: hai lớp chính, một lớp tùy chọn

- **Micro-sparks:** chấm nhỏ có lõi sáng, biến thiên kích thước; phần lớn giữ hue, một số gần trắng. Tránh tất cả hạt thành streak dài.
- **Flow wisps:** ít hơn, mềm hơn, chịu trường dòng chảy chung để tạo đường cong/co cụm. Không dùng sprite khói to phủ kín nốt.
- **Ribbon tùy chọn, hậu kỳ:** đoạn vệt mảnh nối lịch sử chuyển động, có giới hạn chiều dài và fade. Chỉ thêm nếu chấm + wisp chưa đủ tạo dòng; không thuộc MVP.

Đánh nốt: burst ngắn. Giữ nốt: emitter liên tục nhẹ hơn. Thả nốt: ngừng phát mới theo chính sách sustain, hạt cũ sống hết tuổi thọ. Pedal không nhân density vô hạn.

### 3.4 Phím và sân khấu

- Phím không bấm vẫn nhìn rõ, không bị bloom rửa trắng.
- Màu sáng tập trung ở phím hoạt động và spill nhỏ gần đó.
- Hit line có lõi mảnh và halo mềm; tránh một dải trắng dày liên tục.
- Tắt nebula, fireworks, shockwave, lightning, stars và flame trong recipe này.
- Texture nền nếu cần chỉ là noise/dither cực nhẹ chống banding, không thêm bụi sáng vào vùng nghỉ.

### 3.5 Hợp đồng đồng bộ Live Stage và AR mode

“Đồng bộ” là cùng sự kiện âm nhạc, màu/chất liệu, ý nghĩa tham số và vòng đời; **không phải ảnh giống từng pixel** giữa sân khấu ảo và camera phối cảnh.

- Một nguồn sự thật cho note-on/off, velocity, trạng thái phím vật lý, sounding/sustain, pedal và clock. Phân biệt “tay còn giữ phím” với “âm còn ngân do pedal”; mỗi emitter khai báo rõ nó theo trạng thái nào.
- Resolve palette/material/settings một lần thành snapshot bất biến; hai output không tự duy trì công thức màu hoặc defaults khác nhau.
- Mô phỏng trong không gian phím/scene có đơn vị chuẩn rồi ánh xạ sang Live Stage hoặc pose AR. Không lấy tọa độ pixel Live Stage làm vị trí vật lý AR. Dòng xoáy phải gắn với đàn, không chạy theo màn hình khi đổi phối cảnh.
- Khi cả hai output cùng hoạt động: cùng event ID/seed và một timeline simulation có thẩm quyền; vẽ thêm output không được phát burst hoặc tiến simulation lần thứ hai. Các khác biệt chất lượng/LOD được khai báo, không làm lệch lifecycle.
- Vị trí emitter theo pitch, mặt phím và hướng bay khỏi đàn; kiểm tra black/white keys, mirror, crop, perspective, resize và thay calibration. Quy định reset/reproject khi thay pose lớn, tránh hạt nhảy tới vị trí sai.
- AR dùng cơ chế camera/MIDI alignment hiện có sau khi audit; delay hiển thị không được làm chậm âm thanh hoặc âm thầm đổi timeline của Live Stage. Kiểm tra cả delay bằng 0 và khác 0.
- Texture giữ local UV/seed; hiệu chỉnh brightness/scale theo mode nếu cần phải là override rõ ràng. Camera sáng, nền da tay và compositing alpha cần review riêng; không giả định đã có hand occlusion.
- Switching mode, camera disconnect/reconnect, MIDI disconnect, all-notes-off, seek/loop và đổi preset đều có chính sách reset/carry-over cụ thể; không kẹt phím/hạt hoặc phát burst kép.
- Ma trận từng family/style/parameter phải có cột Live Stage, AR, recording và fallback; lưu bằng chứng, không nâng trạng thái AR hiện có chỉ vì kế hoạch yêu cầu đồng bộ. Một mục chưa hỗ trợ phải hiện lý do; **không được coi bản đồng bộ hoàn tất khi family nhấn/giữ bắt buộc còn thiếu trên một mode**, trừ khi người dùng duyệt thu hẹp phạm vi rõ ràng.

### 3.6 Nâng cấp toàn bộ hiệu ứng nhấn/giữ phím

Audit từ settings/runtime/contracts, không chỉ đếm tên trong catalog. Bảng sau là inventory khởi đầu, phải bổ sung mọi nhánh phát hiện ở P0:

| Nhóm | Phạm vi bắt buộc | Tiêu chí thiết kế |
|---|---|---|
| Burst nhấn phím | Embers, Splash, Fireworks, Confetti, Dust; biến thể chia vùng nếu runtime có | Mỗi style có silhouette, vật liệu, hướng bay và độ sáng riêng; velocity có tác dụng đo được, không đồng hóa thành chấm neon |
| Emitter giữ phím | Toàn bộ held emitters theo burst family; wisps; flames | Duy trì đặc tính style khi chuyển từ nhấn sang giữ; không nhân tốc độ phát theo số output/FPS |
| Particle sinh từ morph | Shatter, Melt, Bounce và mọi morph có phát hạt | Phân biệt mảnh kính, giọt và streak; kiểm tra lifecycle/anchor chung, kể cả lựa chọn liên quan không phát hạt |
| Hold accents và FX liên kết | Hold bar, breath, vibration, electric arcs, key glow/spill, halo; impact waves/flashes | Không gọi tất cả là particle; review chung để tránh layer đè nhau, nhấp nháy hoặc sai màu |
| Hạt khi thả phím | Dissolve, Smoke và mọi release phát hạt; rà soát toàn bộ release styles | Nối đúng với hold/pedal; không có hard cut hoặc giữ emitter vô hạn |

Mỗi family/style cần một phiếu thiết kế: trigger → anchor → clock/seed → hình/texture → motion → màu/ánh sáng → density/budget → note-off/pedal/reset → bindings/UI → test Live Stage + AR + recording. Phân loại **giữ nguyên có bằng chứng / tinh chỉnh / thiết kế lại / bổ sung support**, không viết lại mọi thuật toán chỉ để gọi là nâng cấp.

Texture theo vật liệu: ember có lõi nhiệt; splash có glint và giọt; confetti đọc như giấy, glow thấp; dust mềm; shard có cạnh/glint; flame có cấu trúc nhiệt; wisp mềm và thanh. Ưu tiên hàm/procedural hiện có, bitmap chỉ khi có lợi đã chứng minh. Ambient không neo phím nằm ngoài chương trình nâng cấp bắt buộc này, nhưng phải kiểm regression khi dùng chung renderer.

## 4. Kế hoạch texture

### 4.1 MVP ưu tiên procedural, không phụ thuộc ảnh tải ngoài

| Texture | Cách làm đầu tiên | Vùng dùng | Nguy cơ cần tránh |
|---|---|---|---|
| Soft caustic | Noise tần số thấp + dải sáng được biến dạng nhẹ | Thân kính | Trông như dung nham; quá nhiều octave noise trên từng pixel |
| Diagonal sheen | Hàm giải tích theo tọa độ nốt và seed | Highlight | Một vệt trắng chiếm toàn thân; tất cả nốt quét sáng đồng thời |
| Satin micrograin | Noise biên độ nhỏ, có lọc theo độ phủ pixel | Thân/phản sáng | Nhấp nháy khi nốt rơi, moiré ở 50% |
| Spark core / wisp falloff | Hàm radial/elliptical hiện có hoặc mở rộng | Sprite hạt | Hình vuông ở mép alpha, halo quá rộng |

Chỉ bật **một texture thân chính** cho preset mặc định; không cộng toàn bộ caustic, grain, sheen ở cường độ cao.

### 4.2 Quy tắc tọa độ và thời gian

- Texture bám theo nốt thay vì màn hình; dùng tọa độ local và seed ổn định.
- Phân biệt UV chuẩn hóa để dựng sheen với tọa độ theo kích thước vật lý để grain không kéo giãn theo duration.
- Trong live MIDI, nốt tăng chiều dài không được làm texture trượt/kéo cả thân. Neo mẫu texture vào đầu nốt và đơn vị scene, kiểm thử cả rising/falling.
- Phase lấy từ clock xác định trong playback/export, không lấy thời gian hệ thống. Khi seek, reset hoặc tái dựng có quy tắc, không mang lịch sử hạt cũ sang vị trí mới.
- Chi tiết nhỏ hơn khả năng hiển thị phải được giảm/lọc; 720p và video nén vẫn cần sạch.

### 4.3 Texture bitmap là bước bổ sung có điều kiện

Nếu procedural không đạt chất liệu mong muốn hoặc đắt hơn sampling:

- Thử một noise/caustic tile tự tạo, tileable, grayscale; kích thước khởi đầu 256–512 px, không cần asset 4K.
- Map dữ liệu noise/mask dùng định dạng UNorm tuyến tính; texture màu nếu có phải giải mã sRGB đúng. Không gamma-correct mask như màu.
- Noise tile dùng wrap; sprite dùng clamp. Nếu dùng atlas phải có padding/gutter cho từng mip để tránh bleeding; không dùng wrap cho toàn atlas sprite.
- Có mip chain, kiểm tra độ sắc/lọc khi thu nhỏ và di chuyển.
- Tạo resource/SRV một lần, dùng lại; không upload hoặc tạo bitmap từng frame. Dispose, device recreation và recording phải được phủ.
- Audit register HLSL và bindings hiện hữu trước khi cấp slot mới; không mặc định một số slot đang rảnh, không tái dùng glyph/camera/watermark resource.
- Asset nhỏ cần thiết có thể đưa vào thư mục con `Assets` và embed theo quy ước project; script tái tạo/seed/license đi kèm. Không đưa scratch render, gallery lớn hoặc asset bên thứ ba chưa rõ quyền vào Git.

**Chưa làm ở vòng đầu:** import texture tùy ý, normal-map/parallax, refraction scene thật nhiều pass, texture video, fluid simulation hoặc compute-particle rewrite.

## 5. Lộ trình triển khai theo gate

Không ấn định số ngày trước khi có baseline Windows/GPU. Mỗi pha chỉ qua khi có bằng chứng và review; các con số hiệu năng ở dưới là mục tiêu đề xuất, không phải kết quả đo.

### P0 — Baseline và khung kiểm chứng

**Đầu ra:** fixture MIDI cố định, preset nền thử nghiệm, ảnh và clip baseline, ma trận output.

- Đọc lại wiring/contract hiện tại; chụp Concert Gold, Glass và Neon làm control. Lập inventory toàn bộ nhánh nhấn/giữ/thả phím và ma trận Live Stage–AR cho từng style/parameter; ghi cả phần thiếu và alias.
- Chụp baseline AR với pose/camera fixture cố định cùng MIDI; audit event routing, delay, seed, không gian tọa độ và lifecycle trước khi chia sẻ simulation. Lưu fixture, không lưu hình camera riêng tư vào Git.
- Fixture gồm nốt đơn, nốt rất ngắn/dài, nốt đen/trắng, chord dày, pedal, rest, repeated same pitch, nhiều track, seek và loop.
- Chụp ở playhead/seed cố định; thêm frame tắt bloom và frame tắt particles để tách nguyên nhân.
- Đo CPU simulation, GPU từng pass/toàn frame, allocations, số sprite và kích thước output trên máy Windows thực.

**Gate:** build/tests/static checks hiện có pass; baseline tái lập được trong cùng backend/cấu hình; inventory có mọi family/style nhấn/giữ và output status; sơ đồ đồng bộ Live Stage–AR được chốt. Không gọi WARP là chứng nhận hardware.

### P1 — Palette + recipe bằng renderer hiện có (MVP thị giác đầu tiên)

**Đầu ra:** palette nhiều stop, khoảng pitch cấu hình, preset Prism Flow thử nghiệm trên Solid/Glass.

- Mặc định toàn bộ trường mới không làm đổi preset cũ; giữ stable IDs.
- Một hàm resolve màu phục vụ stage/GPU snapshot, phím, hạt, hit line; kiểm tra màu ở phím đen và biên palette.
- UI ban đầu chỉ cần chọn recipe và các control hiện có; không dựng một tab texture rỗng.
- Review palette/anchor song song trong Live Stage và AR; hiệu ứng chưa đạt AR phải được ghi thành blocker và giải quyết trong lát dọc, không để tới P5 mới khảo sát.

**Gate:** màu liền mạch, cùng pitch có màu ổn định, settings/share round-trip; ảnh rest không có nốt/hạt ma; người dùng chọn Solid/Glass làm nền hoặc xác nhận cần vật liệu mới.

### P2 — Vật liệu và texture (MVP có texture)

**Đầu ra:** caustic/sheeny glass procedural được duyệt; material mới chỉ khi cần.

- Nếu thêm material: cấp ID mới sau audit, không đổi ID/nghĩa của sáu material cũ.
- Controls tối thiểu, tên dự kiến: texture mode, amount, scale, motion và sheen. Chốt schema sau audit; đây chưa phải property/API hiện hữu.
- Amount = 0 vô hiệu hóa lớp texture; Off không làm ảnh đổi khi kéo scale/motion; speed = 0 đóng băng chuyển động, không tắt texture.
- Tránh tái sử dụng `NoteTexture` theo cách làm thay đổi Fire; nhánh material mới chỉ đọc tham số mới khi được chọn.
- Cập nhật CPU/HLSL constant layout đồng bộ nếu thêm buffer fields, defaults/normalize/preset/localization/UI và fallback.
- Render texture trên Live Stage và calibrated AR quads trong cùng fixture; kiểm perspective/mirror và local UV. Các vật liệu hạt khác dùng đặc tả riêng ở §3.6, không nhận caustic kính hàng loạt.

**Gate:** tắt bloom vẫn thấy body/bevel; texture không bò theo màn hình hoặc giãn theo live duration; nốt ngắn không bị highlight nuốt; ảnh/clip 100% và 50% được người dùng duyệt. Không thay ảnh control của preset cũ ngoài thay đổi có chủ đích được giải thích.

### P3 — Toàn bộ hiệu ứng nhấn/giữ và flow particles, đồng bộ hai mode

**Đầu ra:** hoàn tất từng lát dọc trong inventory §3.6, gồm thiết kế/nâng cấp hoặc giữ nguyên có bằng chứng; Prism Flow có sparks + wisps theo trường vận tốc, lifecycle xác định trên Live Stage và AR.

Chia batch: **P3a** burst styles và held emitters tương ứng; **P3b** wisps/flames và flow; **P3c** morph/release particles cùng hold accents liên kết. Mỗi batch qua gate hai mode trước khi chuyển tiếp; không chỉ nghiệm thu preset Prism Flow rồi gọi toàn bộ particle system hoàn tất.

- Làm mode opt-in, giữ physics cũ cho preset cũ. Áp dụng hợp đồng §3.5 cho event/state, seed, clock, tọa độ và chuyển mode; nếu nâng cấp thay đổi look cũ, cần version/opt-in và migration được test.
- Bắt đầu với trường xoáy nhẹ; cân nhắc cache lưới/interpolation nếu noise mỗi hạt đắt. Không chuyển ngay sang compute shader.
- Dùng seed sự kiện ổn định từ dữ liệu note/track/onset hoặc live-event ID, không phụ thuộc số lần draw. Playback/offline dùng timestep cố định hoặc substep có giới hạn và chính sách dropped-time rõ ràng.
- Quy định note-off, sustain, pedal-up, all-notes-off, seek/loop và đổi preset. Không để budget emission tồn đọng phát một đợt lớn sau khi chạm cap.
- Cap theo phím và toàn cảnh; velocity điều khiển burst có giới hạn; sustain duy trì nhẹ, không nhân bùng nổ.

**Gate:** từng style trong inventory có clip/test nhấn–giữ–pedal–thả trên cả Live Stage và AR; mọi control có tác dụng đúng, không còn mục bắt buộc thiếu support chưa được duyệt ngoại lệ. Dòng cong có liên kết, không thành bụi trắng; mật độ ổn định qua 30/60/120 FPS render; pause/seek/reset/chuyển mode không để hạt treo hoặc burst kép. Mục tiêu là tương đương chuyển động theo thời gian, không hứa bitwise parity giữa mọi GPU.

### P4 — Ánh sáng, bàn phím và hoàn thiện composition

**Đầu ra:** recipe ứng viên phát hành, hit line/key spill đồng màu, composite giữ hue.

- Cân chỉnh emission trước bloom; đo vùng highlight bị clip, không nâng exposure để chữa thân tối.
- Rà soát màu hit line theo keyboard geometry; không đổi pitch/timing/layout để chiều theo hiệu ứng.
- Chỉ tách emission mask hoặc thêm bloom control khi đo/ảnh cho thấy pass hiện tại không đủ. Nếu sửa global composite, phải review lại preset cũ.
- Kiểm tra trên UI thật và clean stage/export; panel điều khiển không được che nội dung cần xem.

**Gate:** nốt/chord còn tách bạch, không có dải trắng che phím, khoảng nghỉ sạch; người dùng duyệt clip final và ảnh các checkpoint.

### P5 — Kiểm chứng output và hiệu năng trước tái thiết kế UI

**Đầu ra:** hiệu ứng ứng viên hoàn chỉnh, support matrix từng style/parameter, bằng chứng test/benchmark hai mode để làm nguồn cho UI cuối. Chưa phát hành toàn chương trình ở pha này.

- Live MIDI, playback và recording cùng scene time/material/palette. Kiểm PNG/readback rồi kiểm video nén thực tế, không coi một ảnh đẹp là bằng chứng codec.
- WPF: chọn triển khai gần đúng có giải thích hoặc capability/fallback rõ ràng trước phát hành; không hứa pixel-identical.
- AR: xác minh lại những lát dọc đã làm từ P0–P4: local-UV, pose/scale, lifecycle, camera/MIDI alignment và overlay/compositing. Có fixture synthetic/WARP rồi kiểm webcam/đàn thật khi có thiết bị. Chỉ cập nhật capability/contracts có bằng chứng, không nâng toàn family thành Native từ một sample.
- Đo 1080p trước; 1440p/4K và nhiều màn hình là các cấu hình đo riêng. Có GPU tích hợp và GPU rời nếu thiết bị sẵn có.
- Mục tiêu thảo luận: giữ 60 FPS ở 1080p trên cấu hình tham chiếu được ghi tên; đo p95 tổng frame so với 16.67 ms, đồng thời theo dõi CPU/GPU riêng. Không cộng hai timing có overlap rồi gọi là frame time.
- Đánh giá adaptive quality hiện có (bloom/ambient) riêng với budget flow mới; nếu thêm hạ mật độ, chỉ ảnh hưởng trang trí, không sửa notes/timing/settings đã lưu. Offline export nên dùng quality profile cố định để tránh chất lượng đổi theo tải tức thời.
- Cập nhật docs/changelog/preset thumbnail sau khi gate pass. Gallery/clip/benchmark thô để ở artifact theo quy ước repo, không commit dữ liệu lớn.

**Gate P5:** không crash/leak, save/share đúng, toggle có tác dụng đúng, output/fallback có nhãn trung thực, regression pass và người dùng duyệt diện mạo hai mode. Nếu không có hardware thì ghi `Unmeasured`, không công bố FPS. Những thiếu hụt kiểm chứng AR thật phải được công khai, không coi fixture synthetic là đủ. Chuyển sang P6 sau khi semantics/settings ổn định.

### P6 — Tái thiết kế menu cài đặt cuối cùng cho Live Stage và AR

**Đầu ra:** một hệ menu/dock thống nhất, gọn, dễ đọc; ngữ cảnh riêng cho mỗi mode, không có hai bản UI tách rời với tên/control/default lệch nhau. Wireframe có thể chuẩn bị từ P0, nhưng chốt và triển khai lại UI sau P5 để dựa trên chức năng/capability thật.

#### Cấu trúc điều hướng đề xuất

```text
[Live Stage | AR]   [Preset]   [Tìm cài đặt…]
Diện mạo
  Nốt & texture · Màu sắc · Ánh sáng
Hiệu ứng phím
  Nhấn phím · Giữ phím · Thả phím · Nâng cao
Sân khấu / Camera & căn chỉnh  ← theo mode
Âm thanh & MIDI
Ghi hình & đầu ra
Ứng dụng & trợ năng
```

Đây là taxonomy đề xuất, không phải chỉ thị xóa trang hiện có. Audit `Ui/SettingsPages.cs`, `Ui/MainWindow.Settings.cs`, XAML và [Dock Navigation Audit](DOCK-NAVIGATION-AUDIT.md) trước khi chốt. Practice/History/General và mọi chức năng ngoài FX vẫn có đường truy cập rõ; giữ route/deep-link/shortcut hoặc migration, không làm mất tính năng khi gộp trang.

#### Quy tắc thiết kế UI

- Cùng thiết kế card, spacing, typography, icon, nhãn và đơn vị cho cả hai mode. Không dùng màu sắc làm tín hiệu duy nhất; tên mode hiện rõ.
- Control phổ biến trước: bật/tắt, style, màu, lượng, kích thước, chuyển động, độ sáng. Physics, seed, budget và texture chi tiết đặt trong **Nâng cao**; tái dùng nhãn với nghĩa nhất quán.
- Tách rõ **Nhấn / Giữ / Thả**; dùng subpanel liên kết thay vì nhân đôi cùng một setting. Nếu engine hiện chia sẻ một tham số giữa burst/hold, UI phải nói rõ phạm vi hoặc chờ tách schema, không giả lập hai control độc lập.
- Settings chung áp dụng hai mode mặc định; override theo mode chỉ khi có lý do (camera brightness, projection, masking...), có chỉ báo **Chung / Riêng cho mode này**, reset override rõ ràng. Chuyển mode không âm thầm ghi đè preset hay cấu hình camera/calibration.
- AR-only (camera, calibration, alignment/delay) và Stage-only (nền/phím ảo) xuất hiện đúng ngữ cảnh. Effect chưa hỗ trợ hiện trạng thái/lý do hoặc fallback rõ; không để slider hoạt động mà không ảnh hưởng hình.
- Preview trực tiếp theo mode đang chọn, có nút thử nhấn/giữ/thả chỉ phát sự kiện visual thử, không phát MIDI/âm thanh ngoài ý muốn. Cơ chế thử phải không làm kẹt input thật; reset an toàn khi đóng preview.
- Preset ở đầu, báo “đã chỉnh sửa”, reset theo nhóm và reset toàn bộ có phạm vi rõ. Tìm kiếm theo tên/alias, không tự mất giá trị khi ẩn control. Không cần thêm toàn bộ undo system trong vòng này nếu chưa có nền tảng.
- Dock có thể thu gọn, không che vùng phím/calibration quan trọng; hỗ trợ scroll/keyboard focus, DPI scaling, màn hình nhỏ và chuỗi tiếng Việt/Anh. Tooltip giải thích tác dụng, tránh nhồi mô tả dài cạnh mọi slider.
- Một nguồn metadata cho route/nhãn/binding/capability khi phù hợp với audit; không tạo registry mới rồi bỏ wiring cũ không kiểm chứng. UI cập nhật settings, không truy cập trực tiếp render-thread state.

#### Các bước và gate cuối

1. Inventory control/route và bản đồ “cũ → mới”; wireframe hai mode, được người dùng duyệt.
2. Triển khai shell/navigation và control dùng chung; sau đó context/overrides/preview, không đảo thứ tự.
3. Kiểm bindings, normalize/save/load/share, reset, search, deep-link, keyboard navigation, localization và accessibility; chụp menu thật trên Windows ở hai mode.
4. Test đổi mode khi đang chơi/giữ pedal, camera mất kết nối, mở/đóng dock và chỉnh nhanh: không kẹt phím, burst kép, mất calibration, đổi MIDI/audio routing hoặc ghi nhầm setting chung.
5. Người dùng thực hiện các tác vụ: đổi texture, chọn burst, chỉnh held wisps, chuyển mode giữ look, căn AR và ghi hình; kiểm tìm được control và hiểu phạm vi thay đổi mà không phải dò nhiều trang.

**Gate phát hành cuối P6:** toàn bộ gate P0–P5 vẫn pass sau đổi UI; mọi chức năng cũ có mapping; Live Stage/AR đồng bộ đúng hợp đồng; toàn inventory nhấn/giữ có quyết định và bằng chứng; menu hai mode được duyệt về hình ảnh và thao tác. Cập nhật screenshot/docs/changelog ở đây, không coi UI đẹp là bằng chứng renderer/AR đã đúng.

## 6. Ma trận review tối thiểu

| Cảnh | Điều cần xem |
|---|---|
| Nốt đơn dài, trắng/đen | Body, bevel, texture bám nốt, màu phím khớp |
| Nốt ngắn/lặp cùng pitch | Không thành blob, seed/lifecycle hợp lý, không mất onset |
| Bass + treble, chord nhiều track | Palette và luật ưu tiên màu, không thành mảng trắng |
| Pedal dài rồi nhả | Không tăng mật độ vô hạn, ngừng emitter đúng, fade tự nhiên |
| Rest sau hợp âm | Không còn nốt ma; hạt hết tuổi thọ; nền yên |
| Live rising/falling | Texture không bị kéo giãn, đồng bộ note-on/off |
| Pause/seek/loop/đổi preset | Không tích budget, không mang FX cũ sang cảnh mới |
| Bloom/texture/flow Off hoặc 0 | Đúng nghĩa tắt, không còn chi phí/ảnh hưởng không cần thiết |
| 1080p và bản 50%, video nén | Không aliasing/flicker rõ, hạt không biến thành nhiễu |
| Preset cũ và file cũ | Default mới không làm thay đổi diện mạo/tải/lưu cũ |
| Mọi burst/held style, morph/release particles | Test riêng từng style và parameter; đủ Live Stage/AR, không chỉ sample hỗn hợp |
| Hai output cùng chạy, chuyển mode giữa lúc giữ | Một event không phát hai lần; seed/clock/lifecycle đúng, không kẹt phím |
| AR mirror/crop/pose/calibration, camera sáng/tối | Emitter đúng phím, hướng flow/texture đúng phối cảnh, alpha/glow không bẩn hình |
| Camera/MIDI delay, disconnect/reconnect | Không lệch đồng bộ ngoài cấu hình hoặc làm chậm audio |
| Menu hai mode, DPI và VI/EN | Nhãn/đơn vị/bindings đồng nhất; không cắt chữ; keyboard focus và tìm kiếm dùng được |
| Settings chung/override và migration route | Chuyển mode/save/load/reset không mất cấu hình; chức năng cũ còn truy cập được |

Golden image so trong cùng backend/adapter khi phù hợp, dùng tolerance có giải thích; không áp một pixel hash cho mọi GPU. Motion cần clip theo thời gian, không suy ra từ ảnh tĩnh. Accessibility/reduced-motion phải có cách giảm chuyển động, flash và density mà vẫn đọc nốt tốt.

## 7. Phạm vi bản đầu và backlog

**Mốc thử nghiệm đầu:** một preset Prism Flow, palette nhiều stop, texture procedural nhẹ và flow để chốt chuẩn hình ảnh. Đây không phải toàn bộ yêu cầu hoàn tất.

**Bản hoàn chỉnh của chương trình:** đồng bộ Live Stage–AR; toàn inventory hiệu ứng nhấn/giữ và các nhánh morph/release liên quan được review, nâng cấp khi cần và có support/test; key/hit-line/material/texture nhất quán; save/share/fallback rõ ràng; cuối cùng menu cài đặt hai mode được tái thiết kế và nghiệm thu ở P6.

**Sau khi bản đầu được duyệt:** bitmap caustic tile nếu có lợi; ribbon trails; hai recipe phụ Clean (ít hạt) và Cinematic (flow rõ hơn) dùng cùng renderer. Không triển khai cả ba look song song trước khi chốt một look chính.

**Chưa cam kết:** import texture người dùng, physically based glass/refraction thật, fluid simulation, AR Native cho toàn bộ look, FPS trên mọi máy hoặc giống SeeMusic từng pixel.

## 8. Bước triển khai được khuyến nghị ngay sau khi duyệt lộ trình

Thực hiện **P0 → P1 → review ảnh hai mode**, rồi **P2 → review texture hai mode**, tiếp đến **P3a/P3b/P3c → review toàn bộ hiệu ứng nhấn/giữ bằng clip Live Stage + AR**, **P4 → cân ánh sáng**, **P5 → kiểm chứng output/hiệu năng**, và cuối cùng **P6 → tái thiết kế, kiểm thử menu cài đặt hai mode và nghiệm thu phát hành**. Tách các thay đổi để có thể quy lỗi và rollback theo từng thành phần. Không tăng texture, particle density và bloom cùng lúc rồi đánh giá bằng cảm giác.

Tiêu chuẩn cuối: **nhìn đẹp ngay khi đứng yên, chuyển động đẹp khi chơi, vẫn đọc được nốt khi nhạc dày và vẫn sạch sau khi nén video**.
