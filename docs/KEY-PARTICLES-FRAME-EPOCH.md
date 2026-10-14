# P3a follow-up — clear/seek frame epochs

**Trạng thái (2026-10-15): PASS CI** ở source `1b6ac51` (implementation `e8979ee`), [run 38617785565](https://github.com/lxmtuu/PianoPath/actions/runs/38617785565). Static, portable, Windows build, kiểm thử frame epoch, full regression và packaging đều xanh. Đây là sửa vòng đời/đồng bộ, chưa phải art pass hoặc hoàn tất particle roadmap.

## Lỗi cần ngăn

`Capture(old pressed/song state) → UI clear + stop/seek → Step(old input)` trước đây có thể dọn particles rồi lập tức phát lại emitter từ snapshot cũ. Queue đã có generation nhưng `GpuFrameInput` chưa có; vì thế lọc Note On cũ không đủ.

Một race khác: frame đang chạy trước clear có thể dequeue Note On **mới** sau clear; frame kế tiếp reset làm mất hiệu ứng của Note On đó. Camera delay cũng có thể sample key glow từ history trước clear.

## Thay đổi

- `TransientFrameEpoch` dùng chung short state gate với feed. Capture ghi stamp cùng notes/position/playing/pressed/pedal; `PrepareFrame` kiểm stamp và refresh **toàn snapshot** nếu clear xảy ra giữa Capture và Step.
- Simulation soft-clears khi nhận clear hoặc đổi epoch, kể cả khi clear flag đã được consumer khác lấy. Hai snapshot cũ liên tiếp không được reset lần hai hoặc hồi sinh held state. Clock/RNG không bị tua lại.
- Clear dọn queue **trước khi publish epoch mới**. Producer mới sau boundary không bị queue-clear xóa. Producer đọc stamp cũ rồi enqueue trễ được lọc ra.
- Dequeue ở simulation dùng **epoch của frame**, không tự lấy epoch mới nhất để hút event của frame tương lai. Lock chỉ quanh state/dequeue ngắn, không giữ lock qua physics hay GPU draw. Producer vẫn đọc generation và enqueue không lấy state lock.
- Từ clear đến lần `SetState` tiếp theo, song-active emission bị fence; feed không dùng lại các interval đang chạy của bài cũ. Note On mới vẫn được xử lý trong lúc chờ snapshot. Stop/seek/audio/pedal vẫn do owner hiện có điều khiển, không thay MIDI sounding semantics.
- AR key history mang epoch. Record của epoch mới xóa history cũ trước khi sample camera delay; trước sample đầu của epoch mới, phím tối thay vì dùng ánh sáng trước seek.
- Render loop xây layout lại từ snapshot đã refresh; simulation giữ aspect khi height của snapshot thay đổi cùng boundary.
- Không đổi settings/preset/shader, hình dạng hay density của hạt. Không thêm UI control hoặc redesign menu ở bước này.

## Kiểm thử

- **Portable:** invalidate stamp kể cả sau khi consume flag, coalesce nhiều clear, 1.000 clear song song có generation duy nhất, shared-lock boundary, history ở delay 50/150/300 ms, interpolation trong epoch mới và invalid time.
- **Shipping feed + simulation:** Capture→clear/stop-state→Step hai thread theo thứ tự cố định không dùng sleeps; năm burst/held families, trắng/đen, hai Zone routes; stale song/pressed/viewport snapshot; Note On mới không mất/không lặp; queue producer cũ enqueue trễ; clear kép và flag đã consume; sparks/wisps/flames dưới pedal; clear→chờ publication gap.
- **Regression:** giữ full verification và bộ 78 P0 + 30 palette/satin + 70 family captures. Không nới guard hoặc bỏ suite cũ để chạy xanh.

## Boundary và phạm vi chưa giải quyết

- Clear sau khi frame đã được accept có hiệu lực ở frame kế tiếp. Frame/readback/recording đã in-flight có thể hoàn tất; không hứa hủy tức thì mọi pixel đang hiển thị.
- Epoch dùng cho **soft clear**, không phải hard reset của RNG/ambient/clock. Exact replay vẫn dùng simulation mới với seed cố định.
- Chỉ reset **key-glow history**; particles/rings/morph/release chưa có đầy đủ nonzero-delay history. Không suy ra mọi FX đã camera-aligned.
- Không giải quyết toàn bộ OS MIDI ownership, out-of-order source publications, mode/pose/layout transition matrix, WPF/recording parity, chất lượng hạt hay hardware FPS.
- Art pass vẫn phải nâng tất cả burst/held families, wisps/flames và particle-producing morph/release. Menu P6 vẫn làm cuối cùng.

## Evidence

[Evidence có version/source/hash](KEY-PARTICLES-FRAME-EPOCH-EVIDENCE.json) ghi riêng kết quả của bản sửa này; [P3a baseline đã có](KEY-PARTICLES-P3A-REVIEW.md) vẫn là mốc trước thay đổi.

- **Windows:** annotation `PASS P3a frame epoch` xác nhận shipping feed/simulation regression. Full suite **5.245 assertions / 1 skipped check group**, process exit **0**, 224 log lines. Tên nhóm skip không có trong 50 annotations được giữ lại; chưa đọc được full log qua sandbox, nên không suy đoán nhóm đó.
- **Portable:** job PASS, bao gồm **9 case mới**. Test shared-gate dùng async completion để bỏ cảnh báo blocking Task của xUnit; không đổi assertions hoặc nới kiểm tra.
- **Capture:** isolated suite và staging PASS **178 frames = 78 P0 + 30 palette/satin + 70 particle-family**. Năm cặp held counters Stage/AR khớp baseline P3a trước sửa.
- **Hash:** 12 PNG control P0/P1 được decode BGRA và đối chiếu độc lập với hash trong annotations của **run mới**, tất cả khớp. 20 PNG P3a đang tracked vẫn khớp evidence review trước; manifest hash mới của 70 frames chưa tải/đối chiếu độc lập. Workflow preview PASS và không tạo commit preview mới. Không dùng static hashes để chứng nhận motion hay art mới.
- **Artifacts:** verification log `11919384948`, visual review `11920951631`, previews `11921702430`, benchmark `11919974568`. Link truy cập trong [run](https://github.com/lxmtuu/PianoPath/actions/runs/38617785565); artifact/log download đi qua host ngoài allowlist của sandbox nên chưa đọc trực tiếp toàn bộ.
- **Perf:** Microsoft Basic Render Driver/WARP, p95 default/heavy/AR **2.630,74 / 700,03 / 266,01 ms**. Hardware budgets không được áp dụng; không tuyên bố tối ưu hoặc đạt FPS trên thiết bị thật.
- **Local:** source checker PASS (283 C# / 2 XAML), 7 fixture Python tests PASS, `git diff --check` PASS. Không có local Windows/.NET build; chứng nhận product dựa trên CI ở source nêu trên.

## Bước kế tiếp — chưa triển khai

Bổ sung baseline hình/counter cho wisps/flames và các morph/release có sinh hạt ở cả Stage/AR trước art pass toàn bộ press/held families. Delay/motion/physical-camera và hardware budget vẫn là gate riêng. P6 menu làm cuối cùng; không coi fix epoch là nghiệm thu những hạng mục này.
