# Benchmark GPU cho vật liệu nốt (Solid · Gem · Crystal Outline)

Tài liệu này ghi nhận kết quả đo chi phí GPU của ba vật liệu nốt trên **adapter phần cứng**
(AMD Radeon(TM) Graphics, không phải WARP), chạy ngày 2026-10-07 bằng probe
`--bench-note-materials` trong [`Diagnostics/NoteMaterialGpuBenchmark.cs`](../Diagnostics/NoteMaterialGpuBenchmark.cs).

> **Phạm vi đo — đọc trước khi diễn giải.** Đây **không** phải p95 toàn khung hình:
> cặp query `D3D11 timestamp` + `timestamp-disjoint` chỉ kẹp quanh riêng lời gọi
> `DrawInstanced` của pass nốt trong `GpuStageRenderer`, theo đồng hồ GPU. Nó không bao gồm
> các pass vẽ khác, bloom/composite hay readback. Báo cáo này phục vụ **review thủ công**;
> nó không đặt budget và không ghi/cập nhật `EffectCostAssessment`.

## 1. Cách đo

- **Fixture**: một nốt MIDI tĩnh (pitch 60), playhead cố định tại 1.5 s, duration 2.0 s, depth 2D;
  so sánh Solid (control) với Gem và Crystal Outline. Tắt: hold vibration/breath/color cycle,
  falling pulse/trail/rainbow/shimmer, landing glow, particles, keys, background, ambient layers,
  bloom và camera/AR.
- **Warm-up**: ít nhất 24 khung hình **và** 1.5 s cho mỗi vật liệu ở mỗi vòng; chỉ lấy mẫu sau
  khi cả hai ngưỡng đạt.
- **Cân bằng thứ tự**: 3 vòng cyclic counterbalanced —
  vòng 1 `Solid → Gem → CrystalOutline`, vòng 2 `Gem → CrystalOutline → Solid`,
  vòng 3 `CrystalOutline → Solid → Gem` — mỗi vật liệu xuất hiện ở đủ ba vị trí.
- **Số mẫu**: 120 mẫu/vật liệu (40/40/40 theo vòng). Mẫu chỉ hợp lệ khi `DrawInstances == 1`,
  particles == 0; mẫu dính disjoint bị loại và query error thì batch không được tính complete.
  Không có fallback sang whole-frame timing.
- **Chạy lại được**:

  ```powershell
  dotnet .\bin\Release\net10.0-windows\PianoPath.dll "--bench-note-materials=120" "--bench-out=$env:TEMP\keyflow-note-full.json"
  ```

  JSON thô nằm ở máy người đo (không commit vào repo); exit code 0 chỉ có nghĩa là mọi batch
  drain đủ mẫu hợp lệ, không phải là "đạt budget".

## 2. Môi trường

- Adapter: `AMD Radeon(TM) Graphics` — `warp: false`, `softwareAdapter: false`
  (Direct3D 11 / non-WARP adapter), kích thước 1920×1080.
- Lượt ghi nhận chính: `2026-10-07T12:50:58Z`; lượt lặp lại độc lập lúc `12:54:05Z`
  cho p95 lệch ≤ 0.08 µs (0.023520 / 0.026800 / 0.026880 ms) — phép đo lặp lại tốt.
- CI Windows chỉ chạy probe trên WARP để chứng minh đường query và schema hoạt động;
  nó **không** đại diện cho hiệu năng GPU vật lý.

## 3. Kết quả (ms GPU, quanh DrawInstanced của một nốt)

| Vật liệu | median | p95 | p99 | max | mean |
|---|---|---|---|---|---|
| Solid | 0.01472 | 0.02356 | 0.02376 | 0.03712 | 0.014914 |
| Gem | 0.00936 | 0.02688 | 0.02708 | 0.02708 | 0.015360 |
| Crystal Outline | 0.01492 | 0.02680 | 0.02760 | 0.03584 | 0.017118 |

- Gem so với Solid ở p95: **+0.00332 ms (+14.1%)**; Crystal Outline so với Solid:
  **+0.00324 ms (+13.8%)** — khác biệt tuyệt đối cỡ 3 µs, không đáng kể ở quy mô khung hình.
- Mọi vật liệu: 120/120 mẫu hợp lệ, `queryErrors = 0`, `disjointSamplesExcluded = 0`,
  `querySlotsBusyFrames = 0`, `DrawInstances` min/max = 1/1; warm-up thực tế
  633/614/676 khung hình (~4517 ms mỗi vật liệu).

**Ổn định giữa ba vòng** (p95 từng vòng, ms):

| Vật liệu | vòng 1 | vòng 2 | vòng 3 |
|---|---|---|---|
| Solid | 0.02356 | 0.02352 | 0.02356 |
| Gem | 0.02680 | 0.02680 | 0.02704 |
| Crystal Outline | 0.02668 | 0.02688 | 0.02672 |

p95 phẳng trong băng ±0.3 µs giữa các vòng — không có drift nhiệt/trạng thái đáng kể
trong thời gian đo.

## 4. Cách đọc đúng

- Phân phối mẫu có **hai đỉnh** (khung ~7 µs xen ~26 µs, nhịp frame pacing), nên median nhạy với
  tỉ lệ khung nhanh/chậm từng vòng; **p95 là con số chi phí đuôi ổn định** và là con số nên so sánh.
- Lượt smoke 20 mẫu trước đó cho p95 62–73 µs: đó là hiệu ứng batch đầu nguội ngay sau warm-up,
  đập vào cả ba vật liệu như nhau; lượt 120 mẫu cho chi phí trạng thái ổn định thấp hơn nhiều.
- Kết quả chỉ nói về pass của **một nốt tĩnh**. Không ngoại suy tuyến tính sang nhiều nốt đồng thời
  hay sang toàn khung hình; muốn biết chi phí đó phải đo bằng probe khác (frame benchmark).

## 5. Ghi nhận thủ công vào contract

Sau review thủ công, p95 GPU-stage của hai vật liệu được ghi vào
[`Stage/Effects/EffectContracts.cs`](../Stage/Effects/EffectContracts.cs)
(`EffectCostProfile.GpuStage = Measured`, `EvidenceId` trỏ về tài liệu này và lượt đo
`2026-10-07T12:50:58Z`): Gem 0.02688 ms, Crystal Outline 0.0268 ms. Ba đầu ra còn lại
(WPF/recording/AR) giữ `Unmeasured` cho tới khi có bằng chứng riêng. Probe vẫn không tự ghi
gì: đây là bản ghi chép của người review, không phải budget và không cập nhật tự động.

Bối cảnh thiết kế của ba vật liệu nằm trong
[`VISUAL-EFFECTS-MASTER-ROADMAP.md`](VISUAL-EFFECTS-MASTER-ROADMAP.md) và
[`EFFECTS-REDESIGN.md`](EFFECTS-REDESIGN.md).
