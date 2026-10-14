# P3a follow-up — ring channel gain Stage / AR

**Trạng thái (2026-10-15): đã sửa và qua static check; chờ CI mới.** Đây là sửa đồng bộ knob ownership phát hiện khi review baseline, không phải art pass hoặc nghiệm thu particle roadmap.

## Lỗi có bằng chứng

[Baseline auxiliary](KEY-PARTICLES-AUX-BASELINE.md), source `5d7a3c7`, preview `34dd559`, run `38622867552` đã PASS với 5.549 assertions. Cặp Bounce có ring rõ ở AR, nhưng Stage không dựng ring khi impact-wave intensity = 0. `AddRingSprites` của Stage lấy `ImpactWaveIntensity` cho mọi ring; AR đã phân biệt wave/morph/release. Một morph/release không nên bị mute bởi knob của một kênh khác.

## Thay đổi nhỏ, dùng chung

`KeyRingChannelGain` được cả Stage và AR gọi:

| Channel | Gain draw-time | Điều không được làm |
|---|---|---|
| 0 — impact wave | ImpactWaveIntensity | Không lấy morph/release knob |
| 1 — impact morph | ImpactMorphIntensity | Không bị wave=0 làm mất Absorb/Bounce ring |
| 2 — release | 1 | Không nhân ReleaseIntensity lần hai: nó đã vào Strength lúc sinh |
| Unknown / invalid selected gain | 0 | Không cho non-finite gain vào draw data |

ReleaseIntensity = 0 ở **lúc phát** vẫn không tạo ring. Đổi release setting sau khi ring đã sinh không hủy/tua lại tail; đây là carry-over policy hiện có ở AR, nay Stage dùng cùng policy. Không đổi queue, seed/RNG, clock, ring life, shape/profile coefficients, shader, preset hoặc settings/menu. Không hứa pixel parity vì projection và mask/gain formulas vẫn khác nhau.

## Kiểm thử bổ sung

- **11 portable cases**: wave 0…1,5; morph không phụ thuộc wave; release không bị double-weight; unknown channels, NaN/infinity/negative selected knob.
- Shipping simulation + hai builders: Ring/Shockwave/Ripple, Absorb/Bounce, Snap Back/Echo Rings, cả 60/61. Kiểm real births có channel/pitch/origin đúng; Stage ring sprites và AR pulse bands đều hiện; thay knob không liên quan không đổi ring output; chỉ đúng wave/morph live gain được mute; release giữ birth strength; build không tiến time/đổi ring state; tail hết tuổi thọ.
- Giữ full verification và cả isolated 306-frame suite. Preview Bounce Stage có thể thay đổi do ring được khôi phục; không gọi pixel change này là nâng cấp tất cả artwork.

Local static checker PASS (291 C# / 2 XAML), 7 fixture Python tests PASS, diff check PASS. Chưa có local Windows/.NET; baseline CI ở trên **không** chứng nhận source sửa gain. Source/run/count/captures mới cần bổ sung sau CI.

## Việc còn mở

Art pass cho năm burst/held families, wisps/flames và particle morph/release; intrinsic appearance và parameter meaning Stage–AR; Melt visibility/layering; motion, nonzero delay, tất cả tiers/real-camera/recording/fallback và hardware budgets. **P6 menu làm cuối cùng**. Ring/flash channels khác chưa được nghiệm thu nghệ thuật chỉ vì routing tests PASS.
