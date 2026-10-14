# P3a follow-up — clear/seek frame epochs

**Trạng thái:** đã triển khai và qua static check; chờ portable/Windows CI trước khi chứng nhận. Đây là sửa vòng đời/đồng bộ, chưa phải art pass hoặc hoàn tất particle roadmap.

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

Bổ sung source/run/assertion count sau CI. [P3a baseline đã có](KEY-PARTICLES-P3A-REVIEW.md) là mốc trước thay đổi này; không gọi baseline cũ là evidence của fix mới.
