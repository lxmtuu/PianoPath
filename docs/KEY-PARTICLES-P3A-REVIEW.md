# P3a — nền vòng đời hạt và bộ ảnh đối chiếu Live Stage / AR

**Ngày: 2026-10-15. Đây là nền kỹ thuật + baseline của tạo hình hiện tại, chưa phải bản nâng cấp thị giác hoàn thiện.** Palette/satin P1/P2 vẫn là mẫu đối chiếu; chưa thay preset mặc định và chưa làm lại menu cài đặt.

## Phần đã thực hiện

- Giới hạn tín dụng phát hạt riêng cho từng phím của **sparks, wisps và flames**. Khi đầy pool 24.000 hạt, lượt phát bị chặn được bỏ đi, không tích thành đợt phun bù khi có chỗ trống.
- Tắt hiệu ứng hoặc kết thúc trạng thái key-active sẽ dọn tín dụng cũ. Sparks/wisps ngừng sinh hạt mới; hạt đã sinh vẫn hết tuổi thọ tự nhiên. **Flames vẫn có đuôi phát hạt từ nhiệt đang nguội**, đúng hành vi riêng của họ hiệu ứng này.
- Soft-clear dọn hạt, tín dụng, ánh sáng/trạng thái giữ phím, cursor release và cache vẽ nốt. Không tua lại clock/RNG/ambient; replay chính xác dùng simulation mới với seed cố định.
- Giữ các silhouette/profile/mix màu hiện tại. Kiểm thử dùng simulation thật, gồm phím trắng/đen, hai vùng Zone, pool thật sự đầy, pedal còn giữ sau khi nhả phím, toggle và dựng thêm output mà không tiến simulation hay dùng RNG lần hai.

**Key-active ở đây vẫn là pressed snapshot hoặc khoảng nốt đang hoạt động trong bài.** Bài thử phím live dùng pressed snapshot; chưa thay toàn bộ chính sách ownership giữa MIDI, pedal và nốt trong bài.

## Ảnh đối chiếu năm họ burst / held

Cùng seed, thông số nền và camera synthetic 1280×720; Stage / AR Ultra, Fit, không mirror, delay = 0. Cột **nhấn** là một impact riêng không giữ phím, tại 0,05 s. Cột **giữ** chỉ giữ phím, không enqueue impact, tại 0,60 s. Các ảnh này là mốc cho art pass kế tiếp, không phải render minh họa bằng AI.

| Họ hạt | Nhấn — Stage | Giữ — Stage | Nhấn — AR | Giữ — AR |
|---|---|---|---|---|
| Embers | [Ảnh](previews/prism-flow-p3a/embers-live-stage-burst.png) | [Ảnh](previews/prism-flow-p3a/embers-live-stage-held.png) | [Ảnh](previews/prism-flow-p3a/embers-ar-burst.png) | [Ảnh](previews/prism-flow-p3a/embers-ar-held.png) |
| Splash | [Ảnh](previews/prism-flow-p3a/splash-live-stage-burst.png) | [Ảnh](previews/prism-flow-p3a/splash-live-stage-held.png) | [Ảnh](previews/prism-flow-p3a/splash-ar-burst.png) | [Ảnh](previews/prism-flow-p3a/splash-ar-held.png) |
| Fireworks | [Ảnh](previews/prism-flow-p3a/fireworks-live-stage-burst.png) | [Ảnh](previews/prism-flow-p3a/fireworks-live-stage-held.png) | [Ảnh](previews/prism-flow-p3a/fireworks-ar-burst.png) | [Ảnh](previews/prism-flow-p3a/fireworks-ar-held.png) |
| Confetti | [Ảnh](previews/prism-flow-p3a/confetti-live-stage-burst.png) | [Ảnh](previews/prism-flow-p3a/confetti-live-stage-held.png) | [Ảnh](previews/prism-flow-p3a/confetti-ar-burst.png) | [Ảnh](previews/prism-flow-p3a/confetti-ar-held.png) |
| Dust | [Ảnh](previews/prism-flow-p3a/dust-live-stage-burst.png) | [Ảnh](previews/prism-flow-p3a/dust-live-stage-held.png) | [Ảnh](previews/prism-flow-p3a/dust-ar-burst.png) | [Ảnh](previews/prism-flow-p3a/dust-ar-held.png) |

Full inventory có **70 ảnh**: năm họ + hai vùng Zone × hai mode × năm phase (burst, held, released, tail, held-off). Released/tail nhả phím ở 0,35 s nhưng vẫn giữ pedal; tail được chụp ở 2,50 s. Bộ kiểm chứng kiểm tra hạt hiện diện, held khác emitter-off, tail hết hạt, không lọt geometry Stage lên camera, và PNG giải mã đúng từng pixel readback.

Nhãn phím được tắt trong cảnh thử: nhãn cũng là sprite, không được dùng nhầm để chứng minh còn/hết hạt. Stage sprite count và AR particle FX count là **hai loại bộ đếm draw khác nhau**, không phải số hạt vật lý có thể so ngang tuyệt đối.

## Kiểm chứng đã hoàn thành

- [CI run **38609015755**](https://github.com/lxmtuu/PianoPath/actions/runs/38609015755) **SUCCESS**, source `08c83b5`, ảnh commit `78d2d24`. Static, portable tests, Windows Release build, full verification và đóng gói đều qua.
- Full suite: **5.120 assertions, 1 skipped check group**; không tính nhóm skip là đã kiểm xong.
- Isolated review: **78 P0 + 30 palette/satin + 70 particle = 178 ảnh**. Full suite còn tạo 48 ảnh Phase 1, tổng 226 ảnh trong review artifact; không suy ra đã review thủ công mọi frame.
- Đã decode 20 PNG đại diện, xem crop nguyên pixel của cả 20 và mở riêng cặp Embers held đầy đủ. [Evidence JSON](KEY-PARTICLES-P3A-EVIDENCE.json) lưu decoded BGRA hashes và bộ đếm từ CI. Chưa tải/đối chiếu độc lập toàn bộ 70 hashes trong manifest.
- Sáu ảnh palette/satin P1 **giữ nguyên từng pixel** so với trước P3a. Sáu ảnh P0 có hạt **đã thay đổi** sau sửa lịch phát/tín dụng; các recipe, silhouette và hệ số profile không đổi. Cả 12 decoded hashes khớp annotation CI mới. Không tuyên bố legacy particle animation pixel-identical.

Các delta dưới đây là số pixel khác emitter-off hơn 4 mức ở ít nhất một kênh RGB, tại held 0,60 s. Hai mode có projection/background khác nhau; không dùng delta để kết luận ngang nhau về độ sáng hay chất lượng.

| Họ hạt | Stage sprites | AR particle FX | Stage Δ pixels | AR Δ pixels |
|---|---|---|---|---|
| Embers | 14 | 14 | 715 | 81 |
| Splash | 42 | 14 | 588 | 42 |
| Fireworks | 14 | 14 | 330 | 66 |
| Confetti | 14 | 14 | 356 | 86 |
| Dust | 14 | 14 | 1.098 | 175 |

## Nhận xét thị giác để làm art pass

- Stage còn phân biệt được streak Embers, giọt Splash, tia radial Fireworks, mảnh màu Confetti và đốm mềm Dust. Nhưng burst ở 0,05 s thường dồn sát emitter; Dust dễ thành cụm sáng, chưa phải dòng hạt có chiều sâu.
- **AR trong camera fixture này nhỏ/nén sát phím hơn Stage**; tint phím sáng chiếm ưu thế, đặc biệt Splash khó đọc. Counter/pixel-diff chứng minh có hạt, không chứng minh hạt đã đẹp hay đồng nhất cảm giác giữa hai mode. Đây là mục tiêu cần xử lý ở thiết kế tiếp theo, không được bỏ qua bằng cách chỉ tăng số lượng hạt.
- Ảnh tĩnh không chứng minh chuyển động tự nhiên. Chưa nghiệm thu năm họ này về mặt nghệ thuật, chưa có flow sparks/wisps mới.
- Perf là **software rasterizer**, p95 default/heavy/AR: 2.590,15 / 762,68 / 277,74 ms; tăng khoảng 4,0% / 3,2% / 6,4% so với run trước. Không áp dụng hardware FPS budget và không suy ra đạt 60 FPS hay không regression trên GPU thật.

## Phạm vi còn phải hoàn tất

- Art pass cho **tất cả hiệu ứng nhấn/giữ**, không chỉ Prism Flow: năm họ trên, wisps/flames, các morph có phát hạt và các accent liên kết; sau đó review motion/clip ở cả hai mode.
- Cân gain/size/glow giữa Stage và AR. Hiện vẫn có khác biệt, nhất là Dust và cách dùng per-particle `Glow`; dùng cùng simulation **chưa chứng minh thị giác tương đương**.
- Ở mốc review này race giữa soft-clear và pressed snapshot đã capture còn mở; [follow-up frame epoch](KEY-PARTICLES-FRAME-EPOCH.md) sau đó đã PASS CI sửa boundary này. Full restart/particle history vẫn chưa được chứng nhận. WPF, recording, các quality tier, AR delay khác 0 và camera/đàn thật cần bằng chứng riêng.
- P4 ánh sáng/hit line và P5 hiệu năng trên GPU thật chưa đóng. **P6 làm lại menu cài đặt vẫn để cuối**, nhất quán, gọn và dễ đọc ở Live Stage lẫn AR.

Chi tiết chính sách và source/test map: [P3a baseline](KEY-PARTICLES-P3A-BASELINE.md). Lộ trình đầy đủ: [Prism Flow / toàn bộ hiệu ứng](PRISM-FLOW-DESIGN-ROADMAP.md).
