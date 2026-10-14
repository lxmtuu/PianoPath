# Wisps / Flames — review v1 và chỉnh shader v2

2026-10-15. **Đã review evidence v1; v2 đang triển khai/chờ CI và review mới. Không user sign-off.**

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

**V2 chưa có ảnh mới được review.** Nếu dim quá khiến AR mất nét hoặc flame vẫn phẳng, phải sửa sau khi so ảnh native 1× và crop; không tự gọi giảm alpha là thành công.

## Player đối chiếu frame-locked

`python3 tools/review_held_particle_art.py --revision b17a691 --output <outside-repo>/index.html`

Không cần Pillow để tạo player. Tool kiểm APNG full-frame/source-blend rồi tách đúng 60 PNG lossless/clip vào HTML tự chứa; nút phát/pause, thanh kéo, bước frame, chọn Stage/AR, Wisps/Flames và mốc thả. Cả hai frame được decode trước khi thay ảnh, cùng index, không để hai APNG tự chạy lệch nhịp. Thời gian phát là mục tiêu của trình duyệt, không benchmark. CI tạo player từ capture mới vào artifact `phase1-visual-review/held-particle-art/review-player.html`, không nhét HTML vào thư mục image-only.

## Tiếp theo

So v2 với classic và v1 cùng nguồn input/time/seed, kiểm native 1× + 3× crop, frame onset/hold/release/tail, hai pitch và hai mode. Chưa mở thêm family cho tới khi rõ trade-off v2. Full-view continuous motion, oblique/overhead thật, Lite/Standard, nonzero delay, GPU alpha recording, WPF và hardware budget vẫn mở. **Menu làm cuối** theo [roadmap](PRISM-FLOW-DESIGN-ROADMAP.md).
