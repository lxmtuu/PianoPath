# Ma trận giá trị cho 17 family — kế hoạch đạt Applied

> **Trạng thái 2026-10-26 — đính chính chẩn đoán + mở rộng.** Code `N17V` hiện là **17 family / 68 khung** (đợt B, chờ CI): 12 family percentage của đợt A — `particles.wisps`, `particles.flames`, `atmosphere.beams` (`BeamIntensity` 30/65/100, cảnh Held, witness `ArBeamCount`), `hold.bar`, `hold.breath`, `hold.vibration`, `hold.color-cycle`, `hold.arc` (bốn `Hold*` intensity 25/60/100, `HoldBreathRate` 25/75/100, cảnh Held), `falling.pulse` (`FallingPulseRate` 25/75/100), `falling.shimmer` (`NoteShimmerAmount` 50/70/100, cảnh Approach 0,5 s), `particles.sparks`, `impact.flash` (`ImpactFlashIntensity` 25/60/100, cảnh Impact 0,2 s) — cộng **5 family `Choice` của đợt B**: `falling.trail` (`FallingTrail` Glow + `FallingTrailLength` 25/60/100), `impact.burst` (`ImpactBurst` Embers/Fireworks/Dust), `impact.wave` (`ImpactWave` Ring/Shockwave/Ripple, pin `ShowImpactRings`), `impact.morph` (`ImpactMorph` Shatter/Melt/Bounce), `release.fx` (`ReleaseEffect` Float Up/Dissolve/Smoke, cảnh Release nhả phím ở 0,2 s). Bốn công tắc dạng `Choice` dùng đúng giá trị tắt của renderer (`None`/`Fade`) như bảng `OffValues` của N17; ba family vẽ chỉ vài chục pixel ở N17 (`impact.wave` 597, `impact.morph` 52, `release.fx` 89) nên lái **chính cái Choice** ở 3 giá trị khai báo thay vì percentage dễ trùng ảnh. Mỗi family vẫn là **một công tắc + một setting**, chưa phủ hết setting contract khai và chưa có `unshot`/`pinned` — đó là đợt C. Mười hai lần thử mở rộng trước đây được ghi là thất bại do `off↔low < 12 px`; đọc lại annotation CI thì **11/12 lần thực ra văng `<family> low drew no quads`** — `ArValuePilotReview.Probe()` trả `ArParticleFxCount` cho *mọi* family, nên bất kỳ family không phải hạt nào (hold/impact/beams/falling) cũng bị đọc là 0 quad dù đã vẽ. Chỉ một lần (`7b3a9abb7`) là cổng pixel thật: `particles.wisps off↔far 2 < 12`. Đã sửa bằng cách cho N17V và N17L **dùng chung bảng witness của N17** thay vì giữ bản sao/đếm hạt cứng, và thêm guard tĩnh khoá bảng witness + số khung + hàng README. **Mốc đã xác nhận trên Windows/WARP**: run [40226467869](https://github.com/lxmtuu/PianoPath/actions/runs/40226467869) trên commit `a54b3ae` xanh cả bốn job; `Verification suite` báo `Verification passed: 8072 assertions`, trong đó `VerifyArValuePilot` chạy pilot **4 family / 16 khung** với `Assert(cells == 16)`. Không cần lùi về 12 khung. **Mốc 20 khung cũng đã xác nhận**: run [40233050064](https://github.com/lxmtuu/PianoPath/actions/runs/40233050064) trên commit `7922644` xanh cả bốn job, `Verification passed: 8074 assertions`, 0 annotation lỗi. Bước `AR value pilot (N17V)` chạy riêng trước full suite pass trong 13 giây; `atmosphere.beams` đo `BeamIntensity` low 763 → mid 1433 → far 4763 px so với `off`, mọi bước ≥12 px. Không cần lùi về 16 khung. **Đợt A (12 family / 48 khung) đã xanh run [40240140179](https://github.com/lxmtuu/PianoPath/actions/runs/40240140179)** trên `6a8f552` — cả bốn job success, `Verification passed: 8072 assertions`, 0 annotation lỗi. Nhưng chính số đo của run này lộ một kẽ hở: `hold.breath` và `falling.pulse` có `mid = 0 px` so với `off`, tức khung giữa **trùng hệt khung tắt** — giá trị 60 rơi đúng pha trung hoà của hai hiệu ứng đó ở cảnh 0,4 s / 0,5 s. Năm cổng cũ chỉ so các bước kề nhau nên lọt qua, vi phạm §5 ("mid phải là ảnh thứ ba"). Đã thêm **cổng thứ sáu `off↔mid ≥12 px`** (guard `value_pilot_contract.py` khoá luôn cổng này) và dời mid của hai family lên **75**. Run [40244416601](https://github.com/lxmtuu/PianoPath/actions/runs/40244416601) trên `9207d89` **success** — bốn job xanh, `Verification passed: 8074 assertions`, 0 annotation lỗi; `hold.breath` mid nay **602 px** và `falling.pulse` mid **2848 px** so với `off` (trước đó cả hai là 0). `impact.flash` 8792→33797→141682 px là family mạnh nhất, `hold.arc` 108→120→124 px là family yếu nhất nhưng vẫn qua mọi cổng. `N17L 78 khung` vẫn nằm trong full suite. Các dòng lịch sử bên dưới được giữ lại để không xoá bằng chứng cũ, kèm chú thích này.

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
> **2026-10-26 — lượt CI xác nhận (đã xanh).** Ba lượt đầu trên nhánh `arena/fdf9b307-pianopath` ([40224830210](https://github.com/lxmtuu/PianoPath/actions/runs/40224830210), [40225189880](https://github.com/lxmtuu/PianoPath/actions/runs/40225189880), [40226061768](https://github.com/lxmtuu/PianoPath/actions/runs/40226061768)) đều đỏ ở **hạ tầng**: `actions/setup-dotnet@v6` tải SDK `10.0.403` bị HTTP 409, trong khi feed chính thức của channel 10.0 vẫn ghi `latest-sdk: 10.0.402` (13/10/2026). Đã pin `dotnet-version: '10.0.402'` trong `build.yml` và `release.yml`; run [40226467869](https://github.com/lxmtuu/PianoPath/actions/runs/40226467869) trên `a54b3ae` **success** — `static`, `test`, `keyboard-light`, `build` đều xanh, 0 annotation lỗi, `Verification suite` 8072 assertions. Cảnh báo 409 của runtime `10.0.14` vẫn còn nhưng SDK 10.0.402 đã có sẵn trên runner nên không chặn job. Từ `7922644`, `build.yml` còn chạy `--verify-ar-value-pilot-only` như một bước riêng ngay sau N17 (fail-fast, ~13 giây) và biến PASS line thành notice, nên số đo từng family đọc được thẳng trong CI thay vì phải tải artifact. Run [40233050064](https://github.com/lxmtuu/PianoPath/actions/runs/40233050064) **success** cho mốc 5 family / **20 khung**: `Verification passed: 8074 assertions`, 0 annotation lỗi, `atmosphere.beams` 763→1433→4763 px. Guard `tools/value_pilot_contract.py` giờ khoá luôn bước fail-fast này. Đợt A lên 12 family / **48 khung** xanh hai lượt: [40240140179](https://github.com/lxmtuu/PianoPath/actions/runs/40240140179) (`6a8f552`, 8072 assertions) rồi [40244416601](https://github.com/lxmtuu/PianoPath/actions/runs/40244416601) (`9207d89`, 8074 assertions) sau khi vá cổng `off↔mid`; cả hai 0 annotation lỗi. Guard giờ khoá cả cổng thứ sáu đó. **Đợt B (17 family / 68 khung) đang chờ lượt CI mới**: guard đã mở rộng để khoá bốn giá trị tắt `None`/`Fade`, `SwitchOnValue`, việc áp `setting.Choice`, cảnh `Release` (nhả phím frame 12) và pin `ShowImpactRings`; 23 test synthetic xanh. Nếu một family `Choice` trượt cổng ≥12 px thì bỏ family đó khỏi đợt thay vì hạ ngưỡng. **Việc còn lại** đúng như kế hoạch: phủ mọi setting + `unshot`/`pinned` + `stage-value-pilot/` (đợt C), rồi bench GPU thật, 2 tier Lite/Ultra, webcam thật và cậu duyệt trước khi lật binding — đây mới chỉ là bằng chứng WARP.

*2026-10-25 14:00 VN (giữ nguyên, đã được đính chính ở đầu trang): N17L 78 khung `b0639c2`/`40108956299 success 44m`, N17V 12 khung `cc9fcf9`/`40184254811 success 1h3m1s` từng là nền xanh; `4-family` `401216/401338/401348/401358/401835/401890/401901/401914/402047/402058/402075/402099/402212` failure `6–10m` ở `build` — **11 lần do witness probe sai** (`low drew no quads` trên các family không phải hạt), **1 lần do cổng pixel thật** (`particles.wisps off↔far 2 < 12`), không phải tất cả do `off↔low <12 px` như dòng này từng ghi. Tiêu chí “đẹp” vẫn giữ: ghi rõ `unshot/pinned`, không gọi một cột là cả family, mỗi bước phải là ảnh riêng; binding vẫn cần GPU thật + 2 tier + cậu duyệt. Sau khi sửa probe và pin SDK, pilot 4 family / 16 khung đã xanh run `40226467869`; pilot 5 family / 20 khung đã xanh run `40233050064`; pilot 12 family / 48 khung (đợt A) xanh run `40240140179`, bản vá cổng `off↔mid` xanh run `40244416601`; pilot 17 family / 68 khung (đợt B) chờ CI xác nhận.*
