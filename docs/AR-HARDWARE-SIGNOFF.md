# Sổ duyệt phần cứng AR (N18-HW) — quy trình, chưa phải kết quả

> **Trạng thái 2026-10-27: CHƯA DUYỆT.** Mọi thứ xanh cho đến nay là **WARP/CI**: run
> [40364410157](https://github.com/lxmtuu/PianoPath/actions/runs/40364410157) có 8075 assertions, ma trận
> giá trị **32 cột / 17 family / 128 khung AR + 24 khung Stage**, `fullyCovered = true`. WARP là bộ dựng
> hình phần mềm, không phải card đồ hoạ; camera trong `--bench-ar` là camera tổng hợp, không phải webcam.
> `ArProjectedParameterBindings` trong `Stage/Effects/EffectContracts.cs` vẫn có `Recording` và `ArOverlay`
> ở mức **`Unverified`**. Tài liệu này là **cái cần làm**, không phải biên bản đã làm.
>
> **Cập nhật 2026-10-27 — cổng 1 của §6 đã qua.** Hai report `warp: false` (Lite và Ultra) đo trên
> card thật **NVIDIA GeForce RTX 4050 Laptop GPU, driver 32.0.16.1714** đều nằm hoàn toàn trong ngân
> sách 8/16/16 ms; bằng chứng nằm ngoài repo tại `C:\keyflow-signoff\2026-10-27\`. Cổng 2 (năm kịch
> bản webcam/OBS thật) và cổng 3 (chữ ký chủ repo) **vẫn CHƯA DUYỆT**.

## 1. Cái đã có và cái còn thiếu

| Hạng mục | Trạng thái | Bằng chứng hiện có |
| --- | --- | --- |
| Renderer rẽ nhánh, 17 family hiệu ứng AR | ✅ Xanh | `ar-value-pilot/` 128 khung, `gateFailures = []`, `fullyCovered = true` trên WARP |
| Slider chạm tới ảnh (Live Stage, không trần) | ✅ Xanh | `stage-value-pilot/` 24 khung trên WARP |
| Cổng fail-fast CI cho N17V / N17V-S | ✅ Xanh | `.github/workflows/build.yml`, hai step riêng + notice |
| `--bench-tier=<Lite\|Standard\|Ultra>` | ✅ Có công tắc | `Diagnostics/FrameBenchmark.cs`; tier ghi vào `arTier` của JSON và dòng log |
| Script gom/kiểm chứng bằng chứng | ✅ Có công cụ | `tools/ar_hardware_signoff.py`, tự kiểm bằng `--check` trên CI |
| **p95 trên card đồ hoạ thật, 2 tier** | ✅ Xanh (2026-10-27) | `C:\keyflow-signoff\2026-10-27\`: `warp: false`, Lite 2,997 / 2,646 / 3,040 ms, Ultra 3,026 / 3,030 / 3,024 ms (`default`/`heavy`/`ar` so với 8/16/16 ms) |
| **Webcam thật / OBS Virtual Camera** | ❌ Chưa có | Chưa có ảnh, video hay biên bản 5 kịch bản |
| **Duyệt mỹ thuật** | ❌ Chưa có | Chủ repo chưa ký |
| **Lật `Unverified` → `Applied`** | ❌ Chưa được phép | Cần đủ (2)+(3)+chữ ký chủ repo, xem §6 |

## 2. Chuẩn bị trên máy thật

- Windows 10/11 x64, bản build **Release** (`bin/Release/net10.0-windows/PianoPath.exe`) từ đúng commit sẽ duyệt.
- Một card đồ hoạ thật hỗ trợ D3D11, driver đã cập nhật; ghi lại **tên adapter, phiên bản driver, bản Windows**.
- Webcam thật (ưu tiên 1280×720@60 MJPG) **và** OBS Virtual Camera để thử stream; ghi lại mode thật.
- Đàn/MIDI thật hoặc file MIDI để có nốt sống; ánh sáng đủ và một chỗ tối để thử kịch bản thiếu sáng.
- Thư mục bằng chứng, ví dụ `D:\keyflow-signoff\2026-10-27\` — **không commit** vào repo (ảnh/video lớn).

## 3. Đo 2 tier trên card thật

Chạy **hai lượt riêng biệt**, cùng adapter, cùng số khung. `--bench-tier` tự bật cảnh `--bench-ar`;
`--bench-adapter` bắt buộc nếu máy có hơn một GPU — tên phải khớp **đúng một** adapter, nếu không lượt đo
sẽ dừng kèm danh sách adapter thay vì âm thầm đo card khác.

```powershell
$exe = '.\bin\Release\net10.0-windows\PianoPath.exe'
$dir = 'D:\keyflow-signoff\2026-10-27'
$gpu = 'RTX 4050'          # đổi thành chuỗi tên adapter của máy cậu
New-Item -ItemType Directory -Force -Path $dir | Out-Null
foreach ($tier in @('Lite', 'Ultra')) {
  & $exe --bench=120 --bench-ar `
    "--bench-tier=$tier" `
    "--bench-adapter=$gpu" `
    "--bench-out=$dir\bench-$tier.json"
  if ($LASTEXITCODE -ne 0) { throw "tier $tier failed with exit $LASTEXITCODE" }
}
```

Kiểm tra ngay: mỗi file JSON phải có `warp: false`, `softwareAdapter: false`, `arTier` đúng (`Lite`/`Ultra`),
cùng `adapter`, ba cảnh `default`/`heavy`/`ar` đều `Measured`, và cảnh `ar` có `camera.frames > 0`.
Ngưỡng tuyệt đối: **p95 < 8 ms** cho `default`, **< 16 ms** cho `heavy` và `ar` (`FrameBudget.All`).
Nếu `verdict: Fail` ở tier nào, dừng lại và báo cáo — đừng chỉnh ngưỡng cho vừa.

Cả đoạn trên có sẵn thành một lệnh: `pwsh tools/ar_hardware_bench.ps1 -Adapter 'RTX 4050' -EvidenceDir $dir`.
Bỏ `-Adapter` thì script tự dò card rời NVIDIA và **dừng lại** nếu máy có nhiều hơn một ứng viên — laptop
hybrid đo nhầm iGPU vẫn cho report xanh mà vô nghĩa. Script chạy một lượt nháp 20 khung để chắc
`warp: false` trước khi đo 120 khung, dừng ngay khi tier nào vượt ngân sách (exit 3), ghi log ra
`bench-<tier>.log` (exe là `WinExe`, nên terminal có thể chỉ hiện exit code), và sinh sẵn `webcam.json`
với phần máy điền từ chính máy đó — phần còn lại là `FILL-ME`, và script sẽ nhắc cho tới khi bị thay.

## 4. Webcam / OBS thật — 5 kịch bản bắt buộc

Chụp **ảnh hoặc video** cho mỗi kịch bản vào `$dir\shots\`, rồi điền `webcam.json` (mẫu ở §5).
Đây là phần không thể giả lập bằng WARP: méo ống kính, nhiễu thiếu sáng, motion blur và tay che phím.

| Kịch bản | Cần chứng minh | Gợi ý chụp |
| --- | --- | --- |
| `overhead` | Nốt/hiệu ứng rơi đúng phím thật khi nhìn từ trên xuống | Webcam trên giá, toàn bộ 88 phím vào khung |
| `fisheye` | Calibration vẫn khớp với ống kính góc rộng/méo | Webcam góc rộng, bàn phím ở mép khung |
| `low_light` | Key light và nốt vẫn đọc được khi nhiễu cao | Tắt đèn, tăng gain, giữ nguyên calibration |
| `fast_motion` | Không giật hình, latency ổn khi tay chạy nhanh | Chạy gam/lặp hợp âm tốc độ cao, quay 60 fps |
| `hand_occlusion` | Hiệu ứng không vẽ đè sai lên tay người chơi | Đặt tay lên phím đang có nốt/impact |

Ngoài năm kịch bản: bật/tắt **OBS Virtual Camera** trong lúc app đang mở, thử **hai webcam cùng tên** (hậu tố
`(2)`), và đo độ trễ bằng **MEASURE DELAY** rồi lưu số ms vào `webcam.json`. Hai kiểm tra đầu không có ảnh
nên `webcam.json` chính là nơi duy nhất chúng được ghi: kết quả nằm ở `camera.obs_toggle_notes` và
`camera.same_name_pair_notes`, và **`tools/ar_hardware_signoff.py` từ chối biên bản thiếu hai ô đó** —
không có chỗ ghi thì kiểm tra có thể bị bỏ qua mà `--evidence` vẫn in PASS. Cùng lý do đó, mọi giá trị
còn là câu mẫu `FILL-ME` cũng bị từ chối, nên chạy thẳng `--evidence` không cho PASS trên biên bản chưa
ai điền. Ghi rõ kết quả dù xấu —
một kịch bản hỏng là dữ liệu, không phải lý do để bỏ qua nó. `-FillWebcam` hỏi thẳng hai câu đó,
còn `-ValidateOnly` báo lại từng ô còn trống.

## 5. Cấu trúc thư mục bằng chứng

```
2026-10-27/
  bench-Lite.json        # report của --bench-tier=Lite
  bench-Ultra.json       # report của --bench-tier=Ultra
  webcam.json            # biên bản thủ công, mẫu dưới đây
  shots/
    overhead.png  fisheye.png  low_light.png  fast_motion.png  hand_occlusion.png
```

```json
{
  "operator": "ten-nguoi-do",
  "machine": { "gpu": "NVIDIA GeForce RTX 4050", "driver": "560.94", "os": "Windows 11 23H2" },
  "camera": {
    "device": "Logitech C920 / OBS Virtual Camera",
    "mode": "1280x720@60 MJPG",
    "obs_virtual_camera": true,
    "obs_toggle_notes": "tắt/bật hai lần khi app đang mở, overlay vẫn vẽ",
    "same_name_pair_notes": "thiết bị (2) mở ra khung đen, giữ thiết bị đầu"
  },
  "scenarios": {
    "overhead":       { "file": "shots/overhead.png",       "notes": "" },
    "fisheye":        { "file": "shots/fisheye.png",        "notes": "" },
    "low_light":      { "file": "shots/low_light.png",      "notes": "" },
    "fast_motion":    { "file": "shots/fast_motion.png",    "notes": "" },
    "hand_occlusion": { "file": "shots/hand_occlusion.png", "notes": "" }
  },
  "measured_delay_ms": 0,
  "art_approved": false,
  "owner_approved": false
}
```

Chạy script để kiểm chứng (nó **chỉ đọc** bằng chứng, không render, không sửa source):

```powershell
python tools/ar_hardware_signoff.py --evidence D:\keyflow-signoff\2026-10-27
python tools/ar_hardware_signoff.py --check
```

`--check` là bài tự kiểm của chính script trên dữ liệu giả; CI chạy nó trong step
`AR hardware sign-off kit (N18-HW)` và biến dòng `PASS` thành notice. Nó **không** tạo bằng chứng
phần cứng và **không** thay được việc đọc ảnh bằng mắt. Kết quả script gồm p95 của từng tier trên
cùng adapter, danh sách kịch bản còn thiếu, và một dòng `PENDING owner approval`.

## 6. Cổng quyết định — không tự lật binding

Script **không bao giờ** sửa `Stage/Effects/EffectContracts.cs`. Dù `--evidence` in `PASS`, hai ô
`art_approved` và `owner_approved` vẫn là quyết định của con người. Chỉ khi **cả ba** điều dưới đây
đúng thì chủ repo mới mở một PR riêng để đổi `Recording`/`ArOverlay` của
`ArProjectedParameterBindings` từ `Unverified` sang `Applied`:

1. Hai report `Lite` và `Ultra` trên **card thật** (`warp: false`) đều nằm trong ngân sách 8/16/16 ms.
2. Năm kịch bản webcam/OBS thật có ảnh + ghi chú, không còn ô `notes` trống ở kịch bản lỗi.
3. Chủ repo **duyệt mỹ thuật** và ký `owner_approved: true` trong biên bản. Việc ký có lệnh riêng —
   `pwsh tools/ar_hardware_bench.ps1 -Approve` — và lệnh đó **từ chối ký** khi validator còn bất kỳ
   điều gì chưa đạt, kèm đúng danh sách chưa đạt. Chữ ký ký lên (1) và (2), chứ không thay thế chúng:
   duyệt một thư mục chưa có ảnh là tiêu hao quyết định duy nhất mà máy không làm được.

PR đó phải dẫn tới file JSON bằng chứng và nêu rõ adapter/driver; guard
`tools/hardware_signoff_contract.py` đang khoá binding ở mức `Unverified`, nên nếu lật mà chưa có
đủ (1)+(2)+(3) thì chính guard sẽ đỏ — đó là mục đích của nó. Cho đến lúc đó: **WARP không phải GPU thật,
camera tổng hợp không phải webcam, và `PASS` của script không phải chữ ký duyệt.**
