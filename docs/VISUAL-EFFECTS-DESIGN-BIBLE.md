# PianoPath Concert FX — design bible (Phase 1, direction selected)

> **Trạng thái (2026-10-06):** người dùng đã chọn art direction Concert / Neon / Learning và cả 17 recipe đã được nối vào runtime. CI [37480841688](https://github.com/lxmtuu/PianoPath/actions/runs/37480841688) và [37495463612](https://github.com/lxmtuu/PianoPath/actions/runs/37495463612) trước đó xác nhận build, tests, `--verify`, gallery 17 preset và note-material trên WPF/software + GPU/WARP. Harness đọc fixture và tạo đúng **48 ảnh** cho ba preset tham chiếu; Windows CI [37509006311](https://github.com/lxmtuu/PianoPath/actions/runs/37509006311) đã pass build, verification và upload artifact `phase1-visual-review`, gồm manifest/index. Còn chờ người dùng duyệt nội dung ảnh; sau đó mới mở rộng review sang 17 preset, accessibility/contrast và duyệt Visual Effects Studio. Phase 1 vẫn mở. WARP xác nhận code path, không chứng minh chất lượng/performance trên GPU thật hay AR.
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

Các giá trị dưới đây là token/target cho ba vai trò; đã map vào preset runtime **Concert Gold**, **Neon Violet** và **Two Hands**. Bản đồ recipe đầy đủ nằm ở [`VISUAL-EFFECTS-PRESET-DESIGN.md`](VISUAL-EFFECTS-PRESET-DESIGN.md); target chưa được xem là đạt cho tới khi gallery được render bằng ứng dụng và review bằng mắt.

| Profile | Cảm xúc | Màu note trái/phải hoặc dải chính | Vật liệu và môi trường | Mức chuyển động |
|---|---|---|---|---|
| **Concert** | Grand, ấm, sang nhưng tiết chế | Sea-glass `#63D7DE` / brass `#F3C56E` | Nốt đặc, satin keyboard, một spotlight mềm, bụi/ring thưa; giữ negative space | Vừa, chậm, không flash mặc định |
| **Neon** | Năng lượng live set, rõ khi stream | Violet `#786DF1` → cyan `#54D8E8` | Lõi + viền sắc, trail/shimmer ngắn, hit-local energy; bloom có trần | Theo event/beat, không strobe toàn màn |
| **Learning** | Bình tĩnh, có tính hướng dẫn | Blue `#54B7E5` / rose `#E88FAA` | Thân matte/opaque, hand split C4, nhãn C; ambient/particle tắt | Nền tĩnh; chỉ giữ cue note cần để đọc |

Quy tắc màu: track/hand/pitch giữ ổn định trong cả scene; flash, hạt và key glow là dẫn xuất ngắn hạn, không tự đổi màu nốt nguồn. Không dùng “gradient” để chỉ cùng lúc gradient theo pitch và gradient dọc thân note; loại sau vẫn là Planned trong catalog.

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

## 6. Một scene review cố định, bốn checkpoint

Nguồn sự thật là [`visual-effects-phase1-scene.json`](samples/visual-effects-phase1-scene.json): C–G–Am–F, 4/4, 96 BPM rồi 72 BPM, có velocity thấp/cao, legato, hợp âm giữ, pedal, release và 1.167 giây tail. Fixture được tiêu thụ **chỉ bởi verification review harness**, không phải cấu hình playback phiên người dùng. `Diagnostics/Phase1VisualReview.cs` phát timeline cố định và render từ geometry 1920×1080 qua `PianoStage` WPF lẫn D3D11/WARP; tạo 3 preset tham chiếu × 4 checkpoint × 2 renderer × 2 cỡ = **48 PNG**. Ảnh 960×540 là arithmetic mean từng khối nguồn 2×2 từ ảnh 1920×1080, tuyệt đối không relayout scene. Bốn checkpoint vẫn là:

1. **0.8 s — Note readability:** viền, pitch column, nốt sắp chạm.
2. **4.1 s — Held chord and impact:** hai tay, velocity, note giữ, hit cục bộ.
3. **6.1 s — Sustain pedal:** pedal đang bật; bass giữ và melody vẫn tách bạch, pedal glow cục bộ, không flash toàn khung.
4. **11.25 s — Release and rest:** decay và khoảng nghỉ sạch sau khi note/pedal đã nhả.

Prototype HTML dùng cùng JSON nhưng là canvas minh họa HTML, **không phải ảnh do PianoPath/WPF/GPU render**. Gallery CI preset dùng `PianoPath.exe --snapshot` cho WPF/software và GPU/WARP; note-material gallery riêng có 9 cấu hình Solid/Gem/Crystal/… × hai engine, mỗi ô 540×350 (50%). Harness Phase 1 tạo artifact `phase1-visual-review` gồm `index.html`, `manifest.json` và 48 PNG; chạy tay từ thư mục gốc bằng `PianoPath.exe --verify --verify-review-out=<dir>`. Ảnh này là runtime/pixel evidence để review, không tự chấm mỹ thuật; WARP không chứng minh FPS/chất lượng trên GPU thật hoặc webcam thật. Chroma-key không nằm trong ma trận 48 ảnh này và vẫn phải được review riêng bằng gate Green Screen hiện có trước khi tuyên bố đã duyệt chroma.

## 7. Chuyển thiết kế sang code hiện có

| Cụm thiết kế | Điểm bám hiện tại | Trạng thái Phase 1 |
|---|---|---|
| Note palette/material | `PianoVisualSettings.ColorMode`, `Palette`, `NoteStyle`, `NoteColor*`, `NoteGlow`, `NoteEdge`, `NoteRefraction` | Cả 17 preset và Gem/Crystal Outline đã build/render trên WPF/software + GPU/WARP; note-style pixel tests và gallery 50% pass CI [37495463612]. AR camera-facing quads now route all six known note styles through the shared shader; new WARP samples await CI. Full AR parameter/value parity, webcam and hardware remain Unverified; preset contrast/fixed-scene review is also open. |
| Motion theo note | `FallingTrail`, `FallingPulse`, `Hold*`, `ReleaseEffect`, `Impact*` | Recipe đã chọn lọc lớp và qua suite hiện có; Phase 3/4 vẫn phải kiểm tra từng behavior/capability. |
| Atmosphere/compositor | `Ambient*`, `BackgroundMotion`, `Bloom*`, `PianoStage` và GPU passes | Gallery xác nhận code path WPF/WARP, không khẳng định parity trên hardware hay AR. |
| Color Gradient | `ColorMode="Gradient"` | Hiện là theo pitch. Gradient head-to-tail vẫn là `Planned` trong `EffectCatalog`. |
| Reduced motion/flash | Các effect có thể bật/tắt riêng | Chưa có global contract/setting; phải hoàn tất ở Phase 2/7, không tuyên bố đã có. |
| Preset/schema | Built-in, community JSON và `CreateDefaultSettings()` | `--verify` xác nhận default first-run/recovery và saved look hợp lệ vẫn được giữ; không đổi schema. |

Phase 1 gồm mapping recipe và wiring cần thiết cho fresh-install/reset; không chỉnh renderer hay thêm field settings. Compile, portable tests, `--verify` và render WPF/WARP đã qua CI; chỉ đóng Phase 1 sau khi các mục review thị giác/accessibility còn mở ở gate dưới đây được xác nhận.

## 8. Gate Phase 1 — hướng đã chọn; review chi tiết còn mở

- [x] Người dùng chọn hướng Concert / Neon / Learning áp lên toàn bộ preset đã ship; Concert Gold là default/reset.
- [ ] Harness tùy chọn `--verify-review-out=<dir>` đã được nối trong source: đọc fixture, feed timeline cố định cho WPF/GPU-WARP và dự kiến tạo 48 PNG + manifest/index; **chưa đánh dấu đạt** cho đến khi Windows build và runtime capture xác nhận.
- [ ] Build Windows + chạy `--verify --verify-review-out=...` thành công, xác nhận đúng 48 PNG (12× 1920×1080 và 12× 960×540 cho mỗi renderer) cùng `manifest.json`/`index.html` trong artifact `phase1-visual-review`. Chưa chạy được trong sandbox này: `dotnet` không cài; chờ Windows CI.
- [ ] Người dùng duyệt 48 ảnh: độ rõ/viền nốt, pitch registration, tách tay, hợp âm giữ, pedal-on/release/nghỉ, contrast thực tế, flash cục bộ và khả năng đọc ở 50%; sửa sai rồi render lại trước khi đóng gate.
- [ ] Review chroma-key Green Screen riêng; preset đó không thuộc ma trận 3 profile/48 ảnh. Không gộp kết quả WARP hay ảnh solid-background để tuyên bố chroma đã đạt.
- [x] CI run [37480841688](https://github.com/lxmtuu/PianoPath/actions/runs/37480841688) render đủ 17 preset qua WPF/software + GPU/WARP; đã xem tổng quan gallery và preview Concert Gold.
- [x] CI run [37495463612](https://github.com/lxmtuu/PianoPath/actions/runs/37495463612) build/test/`--verify` pass (2.188 assertions, 0 skipped), kiểm tra pixel Gem/Crystal Outline và render gallery 9 material/depth configs × hai engine tại 50%; ảnh nằm ở `docs/previews/note-styles.jpg` (commit `3a3bd93`). Đây là WPF + WARP code-path gate, không phải hardware/AR approval.
- [ ] Sau khi ba profile chuẩn được review và chấp thuận, mới mở rộng fixed-scene checkpoint review cho đủ 17 preset.
- [ ] Duyệt bố cục Visual Effects Studio ba pane và **Quick / Advanced** bằng wireframe riêng; đây chưa phải UI đã triển khai.
- [ ] Chấp thuận scene 12 giây + tempo map + seed làm fixture dùng chung cho Phase 3 trở đi.
- [x] Những CI gates cũ đã pass build, portable xUnit và `--verify`; gate 48 ảnh **đã được Windows CI render và assert** (`VerifyPhase1VisualReview` đòi đúng 48 ảnh mỗi lượt `--verify-review-out`, mới nhất [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913)). Việc còn lại là **review thủ công** từng ảnh, không phải chạy được hay chưa.
- [ ] Sau khi review thị giác/accessibility và các mục còn lại được xác nhận, mới đóng Phase 1 rồi mở Effect Contract / compatibility.

Prototype HTML vẫn chỉ là minh họa; không dùng nó để chứng minh renderer. Harness runtime (`Phase1VisualReview` qua `--verify-review-out`) đã được nối vào code/workflow và chạy trong mỗi lượt `--verify` của Windows CI, mới nhất [39294579913](https://github.com/lxmtuu/PianoPath/actions/runs/39294579913). WARP chỉ xác nhận code path; **chưa xác nhận hardware performance/AR** và chưa tuyên bố quality review đầy đủ.
