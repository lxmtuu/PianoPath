# Quyết trần AR 192 hạt/khung — phân tích và kế hoạch bench

> Trạng thái: **phân tích, chưa quyết**. Tài liệu này không nâng `MaxProjectedParticles`. Nó ghi lại vì sao N17 + N17S đo hai thứ khác nhau, và cần đo gì trên máy có GPU thật trước khi cậu gật.

## 1. Hai con số, hai renderer, hai ý nghĩa

| Điểm | AR overlay (`GpuArLayer`) | Live Stage (`GpuStageRenderer`) |
|------|---------------------------|----------------------------------|
| Trần | `MaxProjectedParticles = 192` (`GpuArLayer.Effects.cs:432`) | `EffectBudget.GpuParticleCap = 24 000` |
| Cách chọn hạt khi vượt trần | `ArParticleBudget.Select(eligible, 192)` — fair theo role/group, oldest-first trong group | Không chọn — vẽ hết `sprites` (instanced draw) |
| N17 đo được (Held 0,4 s, Impact 0,2 s) | `wisps` 480/192, `sparks` 264/192 → cột 85 px / 1 775 px là **của trần** | `wisps` 480/480 → 22 085 px, `sparks` 264/264 → 103 323 px → là **của slider** |
| N17S sheet | `ar-effect/sheet-held.png`, `sheet-impact.png` | `stage-effect/sheet-held.png` (1 054 898 B), `sheet-impact.png` (562 124 B) |

Kết luận N17S §7.6: **cùng shader, cùng timing, cùng seed, chỉ khác cap**. 85 px không sai — nó đúng là 85 px **bị cắt**. 22 085 px cũng đúng — nó đúng là slider **còn sống**.

> Quy tắc hiển thị: không trình bày hai số như hai lần đo độc lập của cùng một đại lượng. Bảng §1.1 ghi `capped`, tờ stage ghi `demand == quads`.

## 2. Vì sao không tự hạ `WispAmount` hay kéo dài cảnh để “hết capped”

`§7.6` đã chứng minh:
- `WispHeight = 5` (min) → life 0,445 s → 267 hạt/phím ở 150 → vẫn >192 với 1 phím.
- `WispAmount = 150` là `Maximum` contract khai, hạ nó là tự đặt ngưỡng mới.
- Cảnh Held 0,4 s cho 480 hạt, kéo lên 1,7 s mới đạt steady-state 1 674 — nhưng cả hai đều >192. Chỉ cảnh <0,16 s (192 ÷ 1 200 hạt/giây) mới xuống dưới trần, lúc đó lại đo **duratia** không phải **slider**.

Nói ngắn: **không có cảnh AR hợp lệ nào ở Maximum mà không capped**. Đây là **quyết định sản phẩm**, không phải lỗi trang.

## 3. Nếu nâng trần thì mất gì — cần bench gì

`EffectBudget` chú thích rõ: cap là **ngân sách frame-time**, không phải gu thẩm mỹ. AR hạt là quad + bloom, Stage hạt là point instanced — chi phí khác nhau.

Để gật “nâng” cần 3 số trên **máy có GPU thật** (WARP không tính):

1. **`--bench` mặc định** (1920×1080, look mặc định vs nặng nhất): p95 <8 ms / <16 ms.
2. **`--bench-detail` ma trận mật độ** (1280×720, 1920×1080, 2560×1440 × 1/20/60 nốt × trail on/off) — đo chi phí trail/note riêng.
3. **`--bench-ar`** (1920×1080 + camera 1280×720@60) — đo `camera` giữ hình (trung bình/tệ nhất).

So sánh tương đối với baseline `perf-baseline` đã cache trong CI (`keyflow-perf-*`). Nếu nâng 192 → 384/512 mà p95 vượt ngân sách ở 60 nốt + trail thì không nâng.

Tier: `ArQualityBudget.cs` cắt family theo thứ tự cố định khi quá ngân sách (hạt → pulse → note light → bars → style). Một family có thể **biến mất ở Lite** mà vẫn xanh ở Ultra/WARP. Vì vậy bench phải chạy **ít nhất 2 tier** (Lite/Ultra).

## 4. Lựa chọn — xếp theo đề xuất của mình

| Lựa chọn | Khi nào chọn | Cần gì |
|----------|--------------|--------|
| **A. Giữ 192, ghi rõ “đo trần”** | Mặc định hôm nay. Đã làm: cột AR ghi `capped`, stage ghi `slider còn sống`. Không đổi hiệu năng. | **Không cần gật thêm** — đã xong ở `3748194`/`34f6f33`. |
| **B. Nâng có điều kiện** (ví dụ 192→384) | Bench trên GPU thật cho thấy còn dư ngân sách ở 60 nốt + trail + `bench-ar`, và tier Lite vẫn đủ. | **Cần cậu gật + bench thật + 2 tier** (§11.3.3). |
| **C. Giữ 192 nhưng thêm “đo dưới trần”** (ví dụ wisp 30 thay vì 150) | Muốn một con số AR không capped để so sánh, nhưng đó là ngưỡng ngoài contract — phải ghi `unshot`/`pinned` rõ ràng, không gọi là “cả family”. | **Cần cậu gật** vì đổi định nghĩa `far`. |

Mình nghiêng **A** cho tới khi có bench thật. B và C đều là code đổi look, phải re-baseline có chủ đích và có ảnh.

## 5. Kế hoạch nếu cậu gật B

1. Chốt máy bench: ghi adapter (`--bench-adapter="RTX …"`), chạy 3 lệnh trên, lưu `bench.json` làm baseline.
2. Đổi `MaxProjectedParticles` (ví dụ 384) trong một branch riêng, chạy lại bench, so p95.
3. Chạy lại N17+N17S (51+9 khung) trên cùng WARP để lấy số mới, cập nhật `stage-effect`/`ar-effect` sheets.
4. Nếu tier Lite mất family → ghi vào `AR-OVERLAY-ROADMAP.md` §14.8 tier table, không giấu.

## 6. Liên hệ với §11.3 (muốn lật `Unverified→Applied`)

Nâng trần **không** thay thế ma trận giá trị/vòng đời. Dù chọn A hay B, để lật vẫn cần:

- Ma trận giá trị: 3 giá trị/setting (hiện 2).
- Ma trận vòng đời: 2–3 mốc/family (hiện 1).
- GPU thật + 2 tier (hiện chỉ WARP Ultra).
- Cậu duyệt (§11.3.4).

Vì vậy tài liệu này dừng ở **phân tích**, không đề xuất lật.

---
### 3.1 Kết quả bench RTX 4050 Laptop GPU (2026-10-24, app 2.2.0, 1920×1080, WARP=false)

> Nguồn: cậu chạy `bc3ff65..f73adb5` trên `NVIDIA GeForce RTX 4050 Laptop GPU` (shader 1.76–2.48 s, `softwareAdapter=false`). Exit 0 cả 4 lệnh.

| Lệnh | Cảnh | Preset | p95 | Budget | Verdict |
|------|------|--------|-----|--------|---------|
| `--bench=240` | default | Concert Gold | **3.21 ms** | 8 ms | Pass |
| `--bench=240` | heavy | Galaxy Voyage | **2.52 ms** | 16 ms | Pass |
| `--bench=240 --bench-ar` | default | Concert Gold | **2.93 ms** | 8 ms | Pass |
| `--bench=240 --bench-ar` | heavy | Galaxy Voyage | **2.41 ms** | 16 ms | Pass |
| `--bench=240 --bench-ar` | ar | Concert Gold + camera 1280×720@60 | **2.96 ms** | 16 ms | Pass |
| `--bench=240 --bench-adapter="RTX 4050"` | default | Concert Gold | **3.36 ms** | 8 ms | Pass |
| `--bench=240 --bench-adapter="RTX 4050"` | heavy | Galaxy Voyage | **2.86 ms** | 16 ms | Pass |
| `--bench-detail --bench=120` | default (matrix) | Concert Gold | **3.06 ms** | 8 ms | Pass |

Camera `ar`: 126 frames, age mean **1.67 ms**, worst **6.20 ms**. `--bench-adapter` ghim đúng 1 adapter duy nhất, không fallback.

**Đọc:** cả 3 cảnh đều **dư 2.3–6.3×** so với budget (8→3.2, 16→2.4–2.9). Nâng 192→384 quad ước +0.3–0.6 ms theo N9 detail (60 nốt trail on/off), vẫn trong 8/16 ms trên RTX 4050. WARP không tính — đã có số GPU thật để quyết B.

*Cập nhật 2026-10-24: N17 51 khung + N17S 9 khung xanh trên CI `40045263020`/`40049603629`, stage sheets đã commit `3748194`. Không có cảnh nào dưới trần ở Maximum. Bench RTX 4050 Pass hết — đủ điều kiện bàn B (nâng có điều kiện).*

