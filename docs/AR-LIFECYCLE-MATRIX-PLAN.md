# Ma trận vòng đời cho 17 family — kế hoạch đạt Applied

> Trạng thái: **full matrix đã code (N17L 78 khung, 9 tờ) — static pass 2026-10-24, pilot 6 khung đã xanh, chờ bench GPU**. Defer `VerifyArLifecyclePilot` + `VerifyArLifecycle` khỏi full suite do tràn ngăn xếp, chạy riêng `--verify-ar-lifecycle-pilot-only` / `--verify-ar-lifecycle-only`.

## 1. Vì sao N17 chưa đủ

N17/N17S đã chụp mỗi family ở **một mốc duy nhất** trong vòng đời của nó:

| Cảnh | Timing hiện tại | Family | Vòng đời thực |
|---|---|---|---|
| Held | 12 khung @1/30 → 0,4 s giữ | `hold.*`, `particles.wisps/flames`, `beams` | **liên tục** khi còn giữ, tàn ngay khi nhấc |
| Approach | 30 khung @1/60 → 0,5 s bay | `falling.*` | **0,5 s bay** → tới phím thì đổi thành `hold`/`impact` |
| Impact | 12 khung @1/60 → 0,2 s sau đánh | `burst`, `sparks`, `wave`, `flash`, `morph` | **0,26–0,55 s** rồi tắt (`FlareLife 0,38 s`, `StyledFlash 0,26 s`, `Ring 0,55 s`) |
| Release | 24 khung @1/60 → 0,2 s giữ + 0,2 s tàn | `release.fx` | **0,7–1,2 s** hạt rơi, sau đó hết |

Một mốc chứng minh *có vẽ*, không chứng minh *tàn đúng*. `impact.flash` từng bị bắt ở §8.4 vì chụp ở 0,30 s > 0,26 s — `far↔off = 0 px` dù `far↔rest = 32k px`. Đó chính là lỗi ma trận vòng đời sẽ bắt.

## 2. Mục tiêu — mỗi family 2–3 mốc, không phải mọi frame

- **Hold/Approach**: 2 mốc là đủ — `vừa giữ/vừa bay` (0,4 s/0,5 s như N17) + `sắp tàn` (trước khi nhấc/tới phím). Hold không có “mới sinh” vì đã giữ từ frame 0.
- **Impact**: 3 mốc — `mới sinh` (0,05 s, flash còn nở), `giữa` (0,2 s như N17), `sắp tàn` (0,35 s, chỉ flare/ring còn, flash đã tắt).
- **Release**: 2 mốc — `vừa nhấc` (0,2 s như N17) + `sắp tàn` (0,6 s).

Chỉ đổi **thời gian**, giữ nguyên setting/cột/pose/seed/5 cổng. Mỗi mốc là một bộ 3 hàng `rest/off/far` như N17, nên số khung = mốc × 3.

Ước lượng (full 17 family):

- Hold 8 family × 2 mốc × 3 = 48, Approach 3×2×3=18, Impact 5×3×3=45, Release 1×2×3=6 → **≈117 khung AR** (so với 51).
- Stage N17S tương ứng: 3 family × 2 mốc × 3 ≈ **18 khung** (so với 9).
- Tổng **≈135 khung**, 6–8 tờ. Cộng ma trận giá trị (§11.3.1) thì thành **≈285 khung** nếu làm cả hai cùng lúc — vì vậy không làm gộp.

Vì vậy làm **pilot 1 family Impact** (`impact.flash` — từng có bug 0 px) trước, số còn lại theo sau.

> **2026-10-24 2cb8197**: pilot `Diagnostics/ArLifecyclePilotReview.cs` đã code — 3 mốc T0 0.05s/ T1 0.20s/ T2 0.35s × off/Plasma = **6 khung** `ar-lifecycle-pilot/`, WARP `ar-oblique`, cổng `≥12 px` (T0/T1) + `<12 px` (T2 hết hạn). `check_sources` + `test` + `build` (verification suite) xanh sau khi defer pilot khỏi full suite (tràn ngăn xếp khi chạy 51+9+9+12+6 cùng lúc). Chạy riêng `dotnet run -- --verify-ar-lifecycle-pilot-only --verify-review-out=...` để lấy `manifest.json`/`index.html`; chưa bench RTX 4050 (cần p95 < 16 ms để duyệt full ma trận).

> **2026-10-24 full**: `Diagnostics/ArLifecycleReview.cs` đã code — **78 khung** `ar-lifecycle/` (9 tờ, mỗi tờ một mốc): Held 8×2 (0.40 s/1.00 s), Approach 3×2 (0.25 s/0.50 s), Impact 5×3 (0.05 s/0.20 s/0.35 s), Release 1×2 (0.40 s/0.60 s), mỗi mốc `off↔on` cùng pose/camera/seed `ar-oblique` WARP, cổng `≥12 px` (T0/T1) và `<12 px` cho `impact.flash` T2 hết hạn. `check_sources` pass; full suite vẫn defer (tràn ngăn xếp), chạy riêng `--verify-ar-lifecycle-only` (bench GPU vẫn là gate trước khi gộp vào full suite/đổi binding).

## 3. Pilot `impact.flash` — con số để cậu hình dung

| Mốc | Timing | Kỳ vọng | Cổng nào bắt |
|---|---|---|---|
| T0 = 0,05 s | vừa nở, `Growth 55 ms` chưa xong | `Plasma` vs `None` ~80k px, `rest` còn flare | `off↔rest≥12` |
| T1 = 0,20 s | như N17 hiện tại | `Plasma` 82k px, `Lightning` trung gian | `far↔off≥12`, `far↔rest≥12` |
| T2 = 0,35 s | flare sắp tắt, flash đã tắt | `Plasma` ≈ `None` (0 px) → **phải bị từ chối** nếu vẫn ghi | `far↔rest≥12` bắt |

Pilot 1 family × 3 mốc × 3 hàng = **9 khung**, 1 tờ. Chạy xong là có ngay “đường tàn” để khóa `*LifeSeconds` trong `EffectBudget.cs`.

## 4. Thứ tự đề xuất

1. **Giữ N17/N17S như baseline** — 51+9 khung đã xanh (`f227303`). Mọi ma trận mới là thư mục riêng (`ar-lifecycle-pilot/`).
2. **Pilot `impact.flash` (docs-only trước, code sau khi cậu gật)** — review `ArLifecyclePilotReview.cs` theo khuôn N17, nhưng vòng lặp qua `Timing(stage, milestone)`.
3. **Mở rộng 16 family còn lại** — chỉ sau pilot xanh.
4. **Gộp với ma trận giá trị** — chỉ sau khi bench `docs/AR-CAP-192-DECISION.md` §3 cho thấy còn ngân sách, vì 285 khung trên WARP không nói gì về p95 trên RTX.

Không đợt nào tự lật binding. Lật cần **ma trận giá trị + vòng đời + GPU thật + 2 tier + cậu duyệt** như §11.3.4.

---
*2026-10-24: N17/N17S xanh; pilot N17L code xong, full suite defer, isolated pass. Full 78 khung đã code, static pass, isolated gate `--verify-ar-lifecycle-only`. Chưa bench GPU thật — bước kế 4.2 trước khi gộp/binding.*
