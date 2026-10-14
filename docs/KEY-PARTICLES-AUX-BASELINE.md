# P3a — baseline các kênh hạt bổ sung

**Trạng thái (2026-10-15): đã triển khai harness, chờ Windows/portable CI.** Đây là inventory trước art pass; chưa phải nâng cấp hình ảnh, motion/latency parity hay nghiệm thu toàn bộ roadmap.

## Phạm vi

| Kênh | Material/shape hiện có | Trigger | Release/pedal |
|---|---|---|---|
| Wisps | Dot có flag Wisp, origin 2 | Physical held snapshot | Ngừng phát mới khi thả; hạt cũ hết life |
| Flames | Flame, origin 2 | Held → heat | Heat còn nguội tiếp tục phát; pedal không sở hữu emitter |
| Shatter / Melt / Bounce | Shard / Droplet / Streak, origin 0 | Một queued impact, không held | Không emitter liên tục; Bounce có ring đi kèm |
| Float Up / Dissolve / Smoke | Droplet / Droplet / Dust, origin 1 | Live Note On rồi Note Off | Phát một lần khi thả dù pedal đang xuống |

Absorb/Morph và Snap Back/Echo Rings là ring/flash channels, **không nằm trong inventory particle này**. Diagnostics đối chiếu catalogue settings để mỗi selector được phân loại rõ, không lặng lẽ bỏ selector mới. Các channel phi hạt vẫn còn gate riêng.

Không đổi shipping simulation, shader, preset, settings UI hoặc family coefficients. Dùng recipe riêng trong diagnostics dựa trên particle-off Prism Flow Preview; không thay 14 built-ins hoặc ép mọi vật liệu thành neon/glass.

## Kiểm thử và captures

- Giữ nguyên contract **70 ảnh** của năm burst/held families + hai Zone routes trước đó.
- Thêm **128 ảnh**: 8 channels × white/black (60/61) × 4 phases × Live Stage/AR Ultra.
- Phases: **off / active / pedal-tail / expired**. Off và active có cùng timeline, trạng thái phím, impact/live events, seed và sample time; chỉ tắt channel cần đo. Không dùng background ở thời điểm khác làm control.
- Active: held 0,60 s; morph impact 0,05 s; release 0,40 s. Pedal-tail ở 0,65 s, expired ở 3 s. Các emitter được thả sau 21 held frames (0,35 s); Note Off được xử lý ở step kế tiếp. Morph không held; release không queued impact. Pedal được giữ sau release boundary.
- Guard trên output: active có particles và khác off >4 RGB levels; off/expired không có particle sprites; AR không chứa geometry Stage-space; PNG round trip giữ pixel. Pedal-tail lưu counter/hash nhưng **không yêu cầu mọi channel có cùng số hạt hoặc độ sáng**.
- Shipping diagnostics: shape/pitch/origin tags; living/finite particles; Stage/AR builders read-only; heat tail vs physical release; duplicate live Note Off không phát lại; song-end cursor phát release một lần (cursor được thiết lập ở position 0 trước khi chạy); disabled controls và expiry.
- **4 portable test methods** chỉ kiểm input plan/capture slots, không thay thế execution của shipping C#/GPU.

Manifest/index/full 128 PNG: `phase1-visual-review/key-particles-aux` trong artifact CI. Workflow staging kiểm đủ count và lưu **32 active representatives** dưới `docs/previews/prism-flow-p3a-aux`, kèm counter/hash annotations. Isolated suite sẽ có **306 captures = 78 P0 + 30 P1 + 70 original-family + 128 auxiliary**. Chưa có ảnh mới hoặc CI result ở lúc viết báo cáo này.

## Giới hạn và gate kế tiếp

Synthetic camera, Fit/no mirror/delay 0, WARP, single pitch. Không chứng nhận real MIDI routing, tất cả tiers/poses, nonzero camera delay, WPF/recording parity, motion hoặc hardware FPS. Song-release diagnostic không phải policy cho mọi mid-song load/seek.

Sau CI phải đọc counter/hash, kiểm ảnh active và tails có tính đại diện, ghi source/run/assertion count riêng. Chỉ sau đó mới dùng baseline để làm **art pass toàn bộ press/held families**, không coi thêm harness là hoàn thành yêu cầu nâng cấp hạt. P4 hit line, P5 thiết bị thật và **P6 menu cuối cùng**, gọn/readable/nhất quán Stage–AR, vẫn mở.
