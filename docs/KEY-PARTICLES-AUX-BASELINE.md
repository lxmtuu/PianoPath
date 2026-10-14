# P3a — baseline các kênh hạt bổ sung

**Trạng thái baseline (2026-10-15): PASS CI và review 32 active representatives** ở source `5d7a3c7`, preview `34dd559`. Đây là inventory trước art pass; chưa phải nâng cấp hình ảnh, motion/latency parity hay nghiệm thu toàn bộ roadmap.

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

Manifest/index/full 128 PNG: `phase1-visual-review/key-particles-aux` trong artifact CI. Workflow staging kiểm đủ count và lưu **32 active representatives** dưới `docs/previews/prism-flow-p3a-aux`, kèm counter/hash annotations. Isolated suite sẽ có **306 captures = 78 P0 + 30 P1 + 70 original-family + 128 auxiliary**. [Run 38622867552](https://github.com/lxmtuu/PianoPath/actions/runs/38622867552) **SUCCESS**: static, portable, Windows, full verification **5.549 assertions / 1 skipped group**, benchmark step, previews và packaging. Tên nhóm skip không có trong 50 annotations được giữ; không suy đoán nhóm đó. Full log/manifest chưa tải trực tiếp qua host artifact của sandbox.

[Evidence JSON](KEY-PARTICLES-AUX-EVIDENCE.json) lưu source/preview/run, counter và hashes. Đã decode **32/32 BGRA hashes khớp CI**, review tất cả 32 active ảnh bằng crop nguyên pixel; xem thêm cặp Wisps/white-key đầy đủ. Chưa review 128 frames, ảnh pedal-tail/expired hay chuyển động. Performance annotation có p95 default **2.745,39 ms** trên WARP; không chứng nhận hardware FPS.

## Ảnh và counters trước sửa channel-gain

Links dưới đây pin vào preview commit `34dd559`, để evidence baseline không bị hiểu nhầm khi CI cập nhật ảnh sau sửa. Stage sprites / AR particle FX là hai loại draw counters, không phải metric chất lượng ngang nhau. Δ là pixel khác off >4 RGB levels; cột counts lấy pitch 60.

| Channel | Stage sprites | AR particles | Stage Δ (60/61) | AR Δ (60/61) | Stage / AR trắng | Stage / AR đen |
|---|---|---|---|---|---|---|
| wisps | 76 | 76 | 690/698 | 55/45 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/wisps-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/wisps-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/wisps-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/wisps-61-ar-active.png) |
| flames | 19 | 19 | 626/633 | 207/206 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/flames-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/flames-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/flames-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/flames-61-ar-active.png) |
| morph-shatter | 20 | 10 | 239/242 | 16/18 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-shatter-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-shatter-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-shatter-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-shatter-61-ar-active.png) |
| morph-melt | 24 | 8 | 71/69 | 39/41 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-melt-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-melt-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-melt-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-melt-61-ar-active.png) |
| morph-bounce | 11 | 11 | 1076/795 | 670/676 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-bounce-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-bounce-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-bounce-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/morph-bounce-61-ar-active.png) |
| release-float-up | 18 | 6 | 355/350 | 25/29 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-float-up-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-float-up-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-float-up-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-float-up-61-ar-active.png) |
| release-dissolve | 30 | 10 | 253/252 | 21/18 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-dissolve-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-dissolve-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-dissolve-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-dissolve-61-ar-active.png) |
| release-smoke | 4 | 4 | 668/668 | 60/50 | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-smoke-60-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-smoke-60-ar-active.png) | [live-stage](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-smoke-61-live-stage-active.png) · [ar](https://github.com/lxmtuu/PianoPath/blob/34dd559/docs/previews/prism-flow-p3a-aux/release-smoke-61-ar-active.png) |

## Nhận xét để bắt đầu art pass

- Wisps ở Stage đọc như một cột chấm nhỏ, AR nén sát mép phím và dễ chìm trong key tint; 76 projected particles không có nghĩa 76 chi tiết nhìn thấy riêng biệt.
- Flames ở Stage là cụm sáng tròn, AR đọc như mảng/tip thuôn. Source xác nhận Stage dùng dot sprite còn AR có tapered-flame mask; không chỉ là khác perspective.
- Shatter đọc được mảnh ở Stage nhưng rất nhỏ trong AR. Float Up/Dissolve/Smoke trên AR cũng khó phân biệt ở sample sớm này; Smoke ở Stage còn giống điểm sáng mềm hơn là bụi/khói có chất liệu.
- Melt chỉ lộ rất ít ở Stage; không được dùng 24 sprite instances/Δ>0 để gọi là một bead silhouette dễ đọc. Stage depth/keyboard và vị trí spawn dưới hit line cần kiểm trước khi chỉ tăng gain.
- Bounce: AR có ring rõ, Stage thiếu ring vì builder đang lấy gain của impact-wave cho **mọi** ring. Đây là lỗi routing kênh, không nên che bằng art/density tuning. Sửa riêng trước khi đổi optics; không suy ra ring/flash channels khác đã được review.

Mục tiêu art tiếp theo: thống nhất intrinsic appearance/shading và parameter meaning Stage–AR theo từng material, giữ phân biệt sparks/water/confetti/dust/flame/shards/wisps; sau đó mới chỉnh flow/motion và mật độ. Không ép mọi họ thành cùng một chấm neon.

## Giới hạn và gate kế tiếp

Synthetic camera, Fit/no mirror/delay 0, WARP, single pitch. Không chứng nhận real MIDI routing, tất cả tiers/poses, nonzero camera delay, WPF/recording parity, motion hoặc hardware FPS. Song-release diagnostic không phải policy cho mọi mid-song load/seek.

Baseline active đã có evidence trên; tail/motion review vẫn mở. Sửa channel-gain và art sau này phải có source/run/evidence riêng, không tái dùng run này làm chứng nhận code mới. Chỉ sau đó mới dùng baseline để làm **art pass toàn bộ press/held families**, không coi thêm harness là hoàn thành yêu cầu nâng cấp hạt. P4 hit line, P5 thiết bị thật và **P6 menu cuối cùng**, gọn/readable/nhất quán Stage–AR, vẫn mở.
