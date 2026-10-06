# PianoPath Concert FX — design bible (Phase 1)

> **Trạng thái:** bản đề xuất Phase 1, cần người dùng duyệt ba look trước khi chuyển sang refactor renderer. Đây là đặc tả thiết kế, không phải UI/runtime implementation.
> **Ngày:** 2026-10-06
> **Cùng bộ:** [Visual Effects Studio wireframe](VISUAL-EFFECTS-STUDIO-WIREFRAME.md) · [reference scene JSON](samples/visual-effects-phase1-scene.json) · [preview prototype](prototypes/visual-effects-phase1/index.html) · [master roadmap](VISUAL-EFFECTS-MASTER-ROADMAP.md).

## 1. Ý đồ sản phẩm

**Một sân khấu piano rõ như nhạc cụ thật, giàu cảm xúc như buổi diễn, nhưng luôn để nốt nhạc dẫn mắt.** Ánh sáng phải giải thích được một sự kiện MIDI; không phủ hiệu ứng chỉ để lấp đầy khung hình.

Ba tiêu chí ngang hàng:

1. **Đọc được:** pitch, onset, duration, tay trái/phải và hit-line nhận ra ngay cả ở màn hình nhỏ.
2. **Có cảm xúc:** vật liệu, màu và chuyển động tạo được cảm giác Concert, Neon hoặc Learning mà không đổi hình học/thời điểm của MIDI.
3. **Có kiểm chứng:** cùng scene/seed cho cùng ảnh; một setting chỉ xuất hiện khi nó có consumer; GPU, software fallback, recording và AR có capability/giới hạn được nói rõ.

Mục tiêu chất lượng tham chiếu là độ chỉn chu, mượt và dễ đọc của các piano visualizer cao cấp như Keysight/SeeMusic. Không sao chép thương hiệu, artwork, preset hay layout độc quyền.

## 2. Thứ bậc thị giác

Từ quan trọng nhất tới ít quan trọng nhất:

1. **Nốt và bàn phím:** hình học, thời điểm, pitch, màu tay/track.
2. **Đường chạm và trạng thái tương tác:** hit-line, phím đang giữ, pedal, label cần thiết cho việc học.
3. **FX theo vòng đời nốt:** trail, impact, hold, release — neo vào đúng pitch và đúng MIDI event.
4. **Không khí:** background motion, hạt nền, beam, bloom — tạo chiều sâu nhưng không tranh tương phản.
5. **Chrome:** card, inspector và timeline không được sáng hơn vùng preview.

**Ngân sách tập trung:** tại mỗi hit chỉ có một điểm nhấn mạnh; lớp nền không được che note body, key edge hoặc label. Không dùng flash toàn màn hình làm mặc định.

### Tiêu chí nhìn ảnh

- Note body và viền giữ tối thiểu **3:1** tương phản với nền cục bộ; chữ nhỏ dùng trong preview nhắm **4.5:1**.
- Ở 1280×720, hai nốt kề nhau vẫn tách được; velocity thấp/cao phân biệt qua cường độ/độ dày hợp lý nhưng không làm lệch pitch.
- At-rest frame vẫn có điểm neo rõ; vùng nghỉ không giữ lại tàn dư hạt/halo kéo dài.
- Thử ảnh ở 100% scale và khi thu preview còn 50%; không chấp nhận “đẹp khi phóng to” nhưng mất nốt khi stream.
- Màu không là tín hiệu duy nhất: hand split/track có thể được xác nhận bằng vị trí, outline và nhãn.

Các tỷ lệ trên là **tiêu chí thiết kế cần được kiểm tra bằng ảnh**, không phải cam kết hiện trạng của renderer.

## 3. Bộ token chung

### 3.1 Surface của ứng dụng

| Token | Giá trị đề xuất | Dùng cho |
|---|---|---|
| `chrome.void` | `#090C12` | Nền ngoài cùng |
| `chrome.panel` | `#111722` | Navigation, card và inspector |
| `chrome.raised` | `#182130` | Control đang hover/được chọn |
| `chrome.hairline` | `#2A3545` | Đường chia, keyline |
| `text.primary` | `#F1F4F8` | Tiêu đề/nội dung chính |
| `text.secondary` | `#A7B1C0` | Mô tả và metadata |
| `accent.primary` | `#9B83FF` | Focus, selection và thao tác chính |
| `accent.secondary` | `#57D7E5` | Trạng thái phụ/đường nhạc |
| `state.warning` | `#FFC66D` | Cảnh báo trạng thái, không dùng như màu lỗi |
| `state.error` | `#FF6B78` | Lỗi/clip/không hỗ trợ |

Sân khấu và chrome có palette riêng: không áp token panel lên ảnh/preview và không dùng note palette để tô mọi control.

### 3.2 Ba profile preview

Các màu trong JSON là **design targets** cho cùng một scene, không khẳng định preset runtime hiện có đã tạo đúng ảnh đó.

| Profile | Cảm xúc | Note color (trái / phải) | Vật liệu và môi trường | Mức chuyển động |
|---|---|---|---|---|
| **Concert** | Grand, ấm, sang nhưng tiết chế | Sea-glass `#63D7DE` / brass `#F3C56E` | Satin face, warm key pools, một beam mềm, bụi thưa; giữ negative space | Vừa, chậm, không flash khung hình |
| **Neon** | Năng lượng live set, rõ khi stream | Cyan `#38D8FF` / magenta `#FF57C9` | Lõi sáng + viền sắc, trail ngắn theo velocity, hit-local shock/ring; bloom có trần | Nhanh theo event/beat, không strobe |
| **Learning** | Bình tĩnh, có tính hướng dẫn | Blue `#53BCEB` / rose `#FF829C` | Thân matte/opaque, outline rõ, label chỉ khi đủ chỗ; ambient mặc định tắt | Reduced-motion target; chuyển động note cần để đọc vẫn được giữ |

Quy tắc màu: track/hand/pitch giữ ổn định trong cả scene; flash, hạt và key glow là dẫn xuất ngắn hạn, không tự đổi màu nốt nguồn. Không dùng “gradient” để chỉ cùng lúc gradient theo pitch và gradient dọc thân note.

## 4. Ngữ pháp vật liệu

| Vật liệu | Hình ảnh cần thấy | Điều cấm |
|---|---|---|
| **Solid / matte** | Mặt note đầy, silhouette và chiều dài rõ; outline vừa đủ. | Alpha mỏng đến mức nền xuyên qua làm mất pitch. |
| **Neon** | Lõi đọc rõ, edge/rim mảnh, halo nằm bên ngoài silhouette. | Bloom nuốt mất mép note hoặc biến cả scene thành một mảng sáng. |
| **Glass / crystal** | Một mặt sáng có độ trong, cạnh khúc xạ rõ; vẫn có fill để đọc được note. | Chỉ vẽ outline mờ hoặc refraction làm mất chiều rộng/thời lượng. |
| **Fire / ember** | Note body ổn định, chi tiết nóng/lạnh ở mép; particle bay khỏi đúng event. | Lửa nền lẫn với nốt, đổi pitch hoặc che key. |
| **Water / ripple** | Ring mỏng, mềm, neo tại key; tốc độ lan đọc được. | Mọi hit tạo splash lớn như nhau hoặc ring kéo qua note khác. |
| **Dust / smoke** | Thể tích nhẹ, nhiều lớp alpha, chuyển động có drag và lifetime. | Khói không giới hạn hoặc tồn tại sau khi scene reset. |

“Material” không được thay đổi logic note: pitch, onset, duration và velocity phải giữ nguyên giữa profile.

## 5. Ngữ pháp chuyển động

- **Clock:** effect dùng stage clock; snapshot dùng fixed time-step và seed; wall-clock không được đổi frame cuối của ảnh golden.
- **Falling:** thân note theo MIDI/time; trail đi sau hướng chuyển động, không kéo đầu nốt khỏi hit-line.
- **Impact:** khởi phát tại pitch/key; đỉnh sáng nhanh, decay rõ, mục tiêu cục bộ khoảng 160–220 ms cho flash; ring/burst có lifetime riêng.
- **Hold:** trạng thái bám Note On/Off; breathing là biến thiên luminance chậm, không rung tọa độ pitch. Pedal không kéo dài note bar nếu MIDI key đã release.
- **Release:** xuất phát tại điểm thả; fade/mote/ring kết thúc hữu hạn và không để “ma” khi seek/reset.
- **Randomness:** mỗi event có thể biến thiên trong một seed ổn định từ scene + event ID; cùng fixture cho cùng chuỗi, live vẫn có thể đổi seed theo event.
- **Reduced motion:** tắt background drift, continuous pulse, long trails và cycle; giữ chuyển động note cần thiết để hiểu thời gian/pitch. Tùy chọn này là yêu cầu UX tương lai, chưa phải setting runtime hiện tại.
- **Flash safety:** mặc định không full-screen strobe; cường độ/diện tích flash có giới hạn; `Reduce flashes` tắt/giảm flash mà không tắt dấu hiệu hit.

### Render stack chuẩn

```text
Backplate / camera source
  → background atmosphere (screen-space, nếu có)
  → guides, sheet, hit-line
  → note body + material + head edge
  → note trails
  → key-local impact / hold / release
  → keyboard geometry + key light
  → accessibility labels / HUD / chrome
```

Thứ tự thật có thể khác ở từng backend; Phase 3 phải chứng minh parity/anchor chứ không được coi sơ đồ này là implementation.

## 6. Ba scene review cố định

Nguồn sự thật là [`visual-effects-phase1-scene.json`](samples/visual-effects-phase1-scene.json): C–G–Am–F, 4/4, 96 BPM rồi 72 BPM, có velocity thấp/cao, legato, hợp âm giữ, pedal, release và 1.167 giây tail. Mỗi profile phải xem cùng 720 frames ở 1920×1080; dùng các checkpoint:

1. **0.8 s — Note readability:** viền, pitch column, nốt sắp chạm.
2. **4.1 s — Held chord and impact:** hai tay, velocity, note giữ, hit cục bộ.
3. **11.25 s — Release and rest:** decay và khoảng nghỉ sạch.

Prototype HTML dùng cùng JSON nhưng là canvas minh họa HTML, **không phải ảnh do PianoPath/WPF/GPU render**. Khi bắt đầu phase có code, thay ảnh minh họa bằng `PianoPath.exe --snapshot`/preset gallery; không nâng prototype thành bằng chứng renderer.

## 7. Chuyển thiết kế sang code hiện có

| Cụm thiết kế | Điểm bám hiện tại | Trạng thái Phase 1 |
|---|---|---|
| Note palette/material | `PianoVisualSettings.ColorMode`, `Palette`, `NoteStyle`, `NoteColor*`, `NoteGlow`, `NoteEdge`, `NoteRefraction` | Có setting; ba profile chưa được map thành preset/ảnh chuẩn. |
| Motion theo note | `FallingTrail`, `FallingPulse`, `Hold*`, `ReleaseEffect`, `Impact*` | Cần Phase 3/4 kiểm thử từng behavior/capability. |
| Atmosphere/compositor | `Ambient*`, `BackgroundMotion`, `Bloom*`, `PianoStage` và GPU passes | Stage/WPF/AR khác nhau; không khẳng định parity từ design. |
| Color Gradient | `ColorMode="Gradient"` | Hiện là theo pitch. Gradient head-to-tail vẫn là `Planned` trong `EffectCatalog`. |
| Reduced motion/flash | Có thể rút ra từ design token + effect controls hiện có | Chưa có global contract/setting hoàn chỉnh; đưa vào UX/contract phase sau. |
| Design targets | JSON fixture + prototype page | Không ghi vào `PianoVisualSettings`, không lưu vào preset và không tự động đổi theme. |

Không sửa UI, renderer, preset hoặc settings schema ở Phase 1. Các target cần được map vào source-of-truth runtime ở phase implementation sau khi duyệt.

## 8. Gate đề nghị để duyệt Phase 1

- [ ] Chọn một trong ba hướng làm mặc định khởi đầu (hoặc yêu cầu chỉnh profile); không yêu cầu preset runtime mới ngay.
- [ ] Xác nhận hai màu hand/track có dễ đọc và phân biệt được trong 3 checkpoint.
- [ ] Xác nhận `Learning` giữ note rõ hơn mọi layer trang trí và không cần full-screen flash.
- [ ] Xác nhận bố cục Studio ba pane và cách tách **Quick / Advanced** hợp lý.
- [ ] Chấp thuận scene 12 giây + tempo map + seed là fixture dùng chung cho Phase 3 trở đi.
- [ ] Sau khi duyệt, đóng Phase 1 và mới bắt đầu Effect Contract / compatibility; chưa nhân rộng effect trước vertical slice.

**Yêu cầu chấp thuận:** người dùng xem ba card trong [preview prototype](prototypes/visual-effects-phase1/index.html), chọn profile mặc định và nêu điểm cần chỉnh. Cho tới khi các mục trên được duyệt, đây vẫn là design proposal; không đánh dấu “art direction approved”.
