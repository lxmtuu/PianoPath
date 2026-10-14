# Wisps / Flames — review v1 và chỉnh shader v2

2026-10-15. **V2 PASS full CI; đã review static và temporal samples so với v1. Giữ opt-in; chưa user sign-off hoặc nghiệm thu cả roadmap.**

## Bằng chứng đã thực sự xem

- Source v1 `87ca500`, image commit `b17a691`; full runtime/storage proof ở run `38640821342`, source `29aefb7`, 6.047 assertions / 1 skipped group.
- Đã mở full scene Stage Wisp 60 và AR Flame 60. Đã xem 8 cặp active classic/art (hai họ × 60/61 × Stage/AR) dưới dạng crop 3× nearest-neighbour, không chỉnh sáng/màu. Classic cùng baseline input/seed ở `docs/previews/prism-flow-p3a-aux`; art v1 lấy từ commit `b17a691`.
- Đã xem 16 frame chọn ở **mỗi** cặp clip: 0,2,4,6,8,10,11,12,14,17,20,23,29,35,44,59. Đây là temporal sampling/contact sheets, **không** tuyên bố đã xem continuous motion hay mọi frame bằng mắt. Chuỗi 30 FPS có hold tới frame 10, thả/pedal từ frame 11; static active ở 0,6 s đang giữ phím không phải cùng input với motion ở 0,6 s đã thả.
- Hash toàn bộ 480 frame và 8 active ảnh đã khớp ở kiểm tra integrity trước. Hash match không phải art acceptance.

## Nhận xét v1

| Họ / output | Cải thiện thấy được | Điểm chưa đạt |
|---|---|---|
| Wisps / Stage | Từ các đốm tròn rời thành sợi uốn; giữ màu phím, có khoảng trống và tail nâng lên sau thả | Vẫn cột hẹp, chưa phải coherent flow toàn họ; ở kích thước nhỏ dễ tụ nét |
| Wisps / AR | Không còn chỉ vài fleck sát mặt phím; sợi có footprint đọc được ở calibration mẫu | Nhiều parcel chồng lên key sáng thành vùng đặc; khoảng trống bên trong kém rõ hơn Stage |
| Flames / Stage | Dáng thuôn và trục vươn lên rõ hơn cụm hạt tròn cũ | Lõi/thân còn phẳng, thân giống một miếng màu đặc; đầu nhọn chưa đủ chiều sâu |
| Flames / AR | Đọc được ngọn phía trên key, khác Wisps rõ hơn baseline | Cùng vấn đề thân đặc; cao hơn classic không tự chứng minh đẹp hơn hoặc đúng scale ở mọi pose |

Các frame lấy mẫu cho thấy wisps dạt/nâng lên rồi mờ; flames còn tail khi heat nguội, gần hết ở khoảng 1 s. Không thấy hạt giữ nguyên tới frame 59 trong crop mẫu. Đây không phải chứng minh mọi tail nằm trong crop hoặc particle count đã về 0; lifetime/epoch tests mới kiểm state.

## V2 — chỉ điều chỉnh material dùng chung

- Wisp giảm độ rộng nét và opacity mặt, biến thiên theo birth phase cố định. Không thay lượng sinh, thời gian, vận tốc, turbulence hoặc RNG.
- Flame có veil trong và spine hẹp sáng hơn, màu thân tối dần theo chiều cao parcel; core ngả ấm nhẹ thay vì thân đặc cùng một màu. Root và geometry giữ nguyên.
- Thay AA kiểu `smoothstep(width ± fwidth(abs(distance)))` bằng **pixel-box integrated band**: khi nét dưới một pixel, coverage giảm theo bề rộng thật, thay vì giữ khoảng nửa opacity của cả card. Dùng signed-distance derivative để tránh derivative bị triệt ở giữa nét. Đây là sửa footprint/material, không phải lifetime fade mới.
- Cùng HLSL được Stage/AR gọi; default flag false, classic/material khác, kind-16 watermark và menu không đổi.
- Reference maths tests kiểm band area, subpixel limits, symmetry/bounds và wiring hai shader entry points. Chúng không thay GPU capture/quality proof.

**V2 nay đã có ảnh thật và review theo mẫu** ở phần bên dưới. Giảm alpha không tự được coi là thành công; trade-off độ sáng/độ rõ vẫn phải ghi rõ.

## Player đối chiếu frame-locked

`python3 tools/review_held_particle_art.py --revision b17a691 --output <outside-repo>/index.html`

Không cần Pillow để tạo player. Tool kiểm APNG full-frame/source-blend rồi tách đúng 60 PNG lossless/clip vào HTML tự chứa; nút phát/pause, thanh kéo, bước frame, chọn Stage/AR, Wisps/Flames và mốc thả. Cả hai frame được decode trước khi thay ảnh, cùng index, không để hai APNG tự chạy lệch nhịp. Thời gian phát là mục tiêu của trình duyệt, không benchmark. CI tạo player từ capture mới vào artifact `phase1-visual-review/held-particle-art/review-player.html`, không nhét HTML vào thư mục image-only.

## Tiếp theo

Đã so v2/v1 theo phạm vi bên dưới và xác minh classic không đổi. Bước tiếp theo là tách contrast/key-light contribution và kiểm thêm pose; chưa mở thêm family hoặc đổi mặc định trong lượt review này. Full-view continuous motion, oblique/overhead thật, Lite/Standard, nonzero delay, GPU alpha recording, WPF và hardware budget vẫn mở. **Menu làm cuối** theo [roadmap](PRISM-FLOW-DESIGN-ROADMAP.md).

## Review v2 — source/run và tính toàn vẹn

[Run `38645329801`](https://github.com/lxmtuu/PianoPath/actions/runs/38645329801), source **`74941f0`**, đã **SUCCESS**. Preview **`e4c9dd6`**; toàn bộ static/portable/Windows build/isolated captures + APNG/player generation/full verification/benchmark/installer/galleries/publication đều qua. Full suite **6.047 assertions / 1 skipped group**. Máy CI là software/WARP, không chứng nhận hiệu năng hardware hoặc cost của material opt-in.

Sau khi fetch preview, đã kiểm độc lập:

- Decode 480 APNG frame (8 × 60) và 8 active PNG; SHA-256 raw BGRA khớp toàn bộ manifest mới.
- 32 capture records giữ nguyên kế hoạch/input/time/seed/counters so với v1 (ngoại trừ hash và changed-pixel metric do material).
- 16 off/expired capture hashes giống v1; 4 classic APNG **byte-identical** với `b17a691`.
- Static source check lại sau auto-commit PASS; JSON vẫn ở `docs/evidence`, không trở lại thư mục image-only.
- Review bằng mắt: full scene pitch 60 cho cả hai họ ở Stage và AR, 8 cặp active v1/v2 ở 3× nearest, và cùng 16 sampled frames/cặp cho bốn cặp clip art v1/v2. Không tuyên bố đã xem mọi frame bằng mắt hoặc continuous playback.

### Kết quả nhìn thấy

| Họ / output | So với v1 | Đánh giá giới hạn |
|---|---|---|
| Wisps / Stage | Mảnh và trong hơn, giảm vùng trắng đặc; đường cong vẫn đọc được trên nền đen | Tối hơn rõ ở native 1×; không nên giảm alpha tiếp trước khi kiểm contrast/palette/chord |
| Wisps / AR | Vẫn thấy sợi/phần đỉnh, bớt vùng phủ quanh mép phím | Pitch 60 trên key sáng vẫn khó tách phần chân khỏi key tint; chưa giải quyết toàn bộ contrast |
| Flames / Stage | Thân hẹp/trong hơn, lõi và viền bớt đồng màu; không còn đặc như v1 | Hiệu ứng vẫn nhỏ ở full scene và chưa chứng minh trên chord dày hoặc palette khác |
| Flames / AR | Dáng thuôn và phần lõi được giữ, viền mềm/bớt cutout | Root trên key sáng vẫn hòa màu; cần kiểm contribution của lighting riêng |

16 frame lấy mẫu vẫn cho thấy lift/drift của wisps và cooling tail của flames; late tail v2 mờ hơn, không suy ra particle lifetime ngắn hơn. Không thấy một khối cố định còn nguyên tới frame 59 trong crop. State/lifetime correctness dựa vào tests, không từ việc crop trông trống.

### Số liệu chỉ làm guard, không chấm điểm artwork

ChangedPixels là delta so với effect-off **cùng input/time**, không phải số pixel sáng, brightness, hoặc pixel parity Stage/AR. Capture active ở 0,6 s đang giữ; motion ở 0,6 s đã thả nên không trộn hai input khi kết luận.

| Họ / mode | Pitch 60 v1 → v2 | Pitch 61 v1 → v2 |
|---|---:|---:|
| Wisps / Stage | 804 → 637 | 811 → 635 |
| Wisps / AR | 386 → 310 | 391 → 339 |
| Flames / Stage | 744 → 651 | 761 → 647 |
| Flames / AR | 515 → 426 | 516 → 427 |

Giảm delta phù hợp với material mảnh/trong hơn; bản thân việc giảm không chứng minh đẹp hơn. Active vẫn có signal, controls/timing/counters không đổi.

### Quyết định

Giữ **v2 làm bản thử tiếp tục**, không bật mặc định, không chỉnh shader v3 ngay chỉ vì vùng key còn sáng. Classic đã có key tint trong composition; ảnh composite hiện tại chưa tách được phần key light và phần particle. Bước nhỏ nên làm tiếp: cùng input nhưng có control key-light=0, các palette tương phản, chord, pose oblique/near-overhead; phân biệt material contrast và ánh sáng nền trước khi thay emitter/độ sáng tiếp.

Player ngoài Git đã được làm mới bằng capture v2: trang chính so **art v1 (`b17a691`) ↔ art v2 (`e4c9dd6`)** cùng frame; trang phụ `classic-v2.html` so classic ↔ v2. Không đổi source player hay shader trong lượt kiểm tra này. HTML tự chứa và crop sheets là deliverable review; không commit thêm 480 PNG hoặc HTML base64 lớn vào Git.
