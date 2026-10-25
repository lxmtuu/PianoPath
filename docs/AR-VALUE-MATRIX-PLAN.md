# Ma trận giá trị cho 17 family — kế hoạch đạt Applied

> Trạng thái 2026-10-25 14:00 VN: **pilot 12 khung `N17V` (wisps 15/75/150, flames 20/60/100, sparks 20/70/120) giữ `cc9fcf9` xanh `40184254811 success 1h3m1s` sau 11 lần thử `4-family` `hold/impact/beams/falling` low 20→70 trên `Held 0.4s/1.0s` `Impact 0.2s` `Approach 0.5s` đều `build failure 6–10m` do `off↔low <12 px` trên `ar-oblique` — tiêu chí “đẹp” bắt ghi rõ `unshot/pinned` và mỗi bước phải là ảnh riêng nên đã lùi để giữ **ổn định**. `N17L 78 khung` `b0639c2` vẫn trong full suite.**

## 1. Vì sao N17 chưa đủ

N17/N17S đã làm đúng **một việc**: mỗi family chứng minh *tới được* renderer và *đổi ảnh* ở pose/cảnh/seed cố định.

| Đã có (N17) | Chưa có (§11.3) |
|---|---|
| `off` + `far` (2 giá trị) + `unshot` ghi tên phần còn lại | **3 giá trị / setting**: thấp–giữa–cao để thấy kẹp/ngưỡng làm tròn |
| 1 pose `ar-oblique`, 1 seed `20261021` | Ma trận pose/seed không cần, nhưng **tier Lite/Ultra** cần (AR cắt family) |
| 5 cổng (500 px, quads>0, off↔rest≥12, far↔off≥12, far↔rest≥12) | Giữ nguyên 5 cổng, chỉ thêm cột |

Nói ngắn: **51+9 khung = “đã thấy”, không phải “đã phủ”**. `tools/check_sources.py` đã khóa điều này: contract vẫn `Unverified`.

## 2. Mục tiêu — mỗi setting 3 điểm, không phải mọi tổ hợp

Contract khai mỗi family 3–6 setting; mỗi setting là `Boolean | Choice | Percentage(max)`. Mục tiêu Applied không phải Cartesian product, mà là:

- Mỗi **Percentage** → `Minimum` (hoặc 0), `mid = (min+max)/2`, `Maximum`.
- Mỗi **Choice** → `OffValues` (None/Fade) + 2 giá trị khác trong list (đầu–cuối).
- Mỗi **Boolean** → `off`/`on` như cũ.

Chỉ đổi **một setting mỗi lần** so với `BaseSettings()` (giữ luật một-family-một-cột của N17). Phần còn lại ghi `pinned`/`unshot` như cũ, không giấu.

Ước lượng thô (từ `EffectContractCatalog.AllFamilies` hiện tại):

- 17 family × trung bình ~2,5 setting có nghĩa × 3 giá trị ≈ **120–140 khung AR** (so với 51 hiện tại).
- Stage N17S tương ứng: 3 family × ~2 setting × 3 ≈ **24 khung** (so với 9).
- Tổng **≈150 khung**, 4–6 tờ, CI +10–15 phút trên WARP.

Không làm hết 17 family một lần nếu chưa có bench thật — làm pilot 3 family (`particles.wisps`, `particles.flames`, `particles.sparks`) trước, số còn lại theo sau.

## 3. Pilot 3 family — con số để cậu hình dung

| Family | Setting | 3 giá trị đề xuất | Ghi chú |
|---|---|---|---|
| `particles.wisps` | `WispAmount` | 15 / 75 / 150 | 15 dưới ngưỡng 192 ở Held 0,4 s? Kiểm §7.6: 15×4×0,4×2=48 <192 → **có** một điểm dưới trần, hai điểm trên |
| `particles.sparks` | `ParticleAmount` | 20 / 70 / 120 | 20×? sparks Impact 0,2 s, life ~0,5 s → ước <192 ở 20 |
| `particles.flames` | `FlameIntensity` | 20 / 60 / 100 | Không capped ở cả hai renderer — làm mốc đối chiếu |

3 family × (off + 3) = **12 khung AR + 12 khung stage = 24**, 2 tờ. Chạy xong là có ngay dải “capped → không capped” để quyết trần, thay vì chỉ 2 điểm như N17.

Cổng giữ nguyên: `far` → `mid` → `off` phải đôi một ≥12 px, không thì cột bị từ chối như `impact.flash` từng bị.

## 4. Thứ tự đề xuất (giữ ổn định và đẹp)

1. **Giữ N17/N17S như baseline** — không sửa 51+9 khung đã xanh (`a8e557d`, `40049603629`). Mọi ma trận mới là file/manifest **riêng** (`ar-value-pilot/`, `stage-value-pilot/`).
2. **Pilot 3 family đã code `918cbae`** — `ArValuePilotReview.cs` theo khuôn N17, mỗi setting 3 hàng + off (`WispAmount 15/75/150`, `FlameIntensity 20/60/100`, `ParticleAmount 20/70/120`), 12 khung AR, 2 tờ, `check_sources` pass, isolated ` --verify-ar-value-pilot-only` PASS local (chờ CI full suite).
3. **Mở rộng 14 family còn lại** — chỉ sau khi pilot xanh trên CI và bench thật cho thấy còn ngân sách (p95 <8/16 ms).
4. **Ma trận vòng đời (§11.3.2) đã xong `b0639c2`** — 78 khung, T2 0.42s qua `Flare 0.38s`, đã gộp vào full suite; làm sau ma trận giá trị theo kế hoạch cũ nhưng nay đã hoàn thành trước nhờ `N17L`.

Không đợt nào tự lật binding. Lật cần **(1)+(2)+GPU thật+2 tier+cậu duyệt** như §11.3 đã ghi.

## 5. Tiêu chí “đẹp” cho ma trận

- Không gọi một cột là “cả family” khi chỉ chụp 1 setting — `unshot` vẫn bắt buộc.
- Không trình bày hai giá trị cho cùng một ảnh là hai kết quả độc lập — `mid` phải là ảnh thứ ba.
- Mỗi tờ có `manifest.json` với `Value`, `Choice`, `ArChanged`, `Demand/Quads` như N17, để người đọc tự đếm.

## 6. Liên hệ với bench

Ma trận giá trị không thay thế bench. Pilot 24 khung trên WARP không nói gì về p95 trên RTX. Bench `docs/AR-CAP-192-DECISION.md` §3 vẫn là cổng trước khi nâng trần — dù có ma trận hay không.

---
*2026-10-25 14:00 VN: N17L 78 khung `b0639c2`/`40108956299 success 44m`, N17V 12 khung `cc9fcf9`/`40184254811 success 1h3m1s` đang active; `4-family` `401216/401338/401348/401358/401835/401890/401901/401914/402047/402058/402075/402099` failure `6–10m` ở `build` do `hold/impact/beams/falling.pulse` families `off↔low <12 px` trên `ar-oblique` (đã thử low 20→70, Held 0.4s/1.0s, Impact 0.2s, Approach 0.5s) — giữ ổn định 12 khung làm nền xanh, ghi rõ `unshot/pinned` theo tiêu chí đẹp, không gọi một cột là cả family; binding vẫn cần GPU thật + 2 tier + cậu duyệt. Pilot 12 khung đã đủ 4 cổng (500 px, quads>0, off↔low/mid/far ≥12) và manifest tất định.*
