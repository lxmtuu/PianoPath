# Khúc xạ nốt: sáu chất liệu, ba mức, hai renderer (N8)

> **Trạng thái:** xong 2026-10-22 · **Mã:** `Diagnostics/RefractionReview.cs` · **Chốt chặn:**
> `tools/check_sources.py` (`scan_refraction_contract`) · **Ảnh đã commit:** `docs/evidence/refraction-sheets/`

N8 trong [`NOTE-EFFECTS-ACTION-PLAN.md`](NOTE-EFFECTS-ACTION-PLAN.md) không phải một lỗi cần sửa: bảng §3.6 đã
chốt rằng WPF và GPU **cố ý** khác nhau ở khúc xạ. Việc còn lại khi tài liệu đó viết xong là *ảnh nghiệm thu* —
6 chất liệu × 3 mức `NoteRefraction`, chụp tay trên máy có GPU. Đợt này biến việc chụp tay đó thành một review tự
động: nó chạy trong `--verify`, tự đỏ khi hợp đồng bị phá, và để lại hai tờ ảnh trong repository. **Không dòng mã
sản phẩm nào đổi** — chỉ thêm đường đo, chốt chặn và tài liệu.

## 1. Đo cái gì

| Hạng mục | Giá trị |
|---|---|
| Chất liệu | `PianoVisualSettings.NoteStyles` — Solid, Neon, Glass, Fire, Gem, Crystal Outline (đọc thẳng mảng của sản phẩm, không chép danh sách) |
| Mức khúc xạ | `NoteRefraction` = 0, 50, 100 (mặc định của sản phẩm là 35, nằm giữa 0 và 50) |
| Renderer | `PianoStage` (WPF) và `GpuStageRenderer` ép **WARP** |
| Cảnh | một nốt đang rơi (`Scene.Falling`), 1,5 s, seed `20261021`, 960×540, 60 fps |
| Khác biệt giữa hai ảnh trong một hàng | **chỉ** `NoteStyle` (hằng số của hàng) và `NoteRefraction` (cột) |
| Số ảnh | 36 = 6 chất liệu × 3 mức × 2 renderer |
| Tờ ảnh | 2 tờ, mỗi tờ 3 cột (mức) × 6 hàng (chất liệu), ô **960×540** — ảnh chụp ở đúng cỡ native, không lấy mẫu lại, vì chủ thể của trang là một đường viền 1 px |
| Ngưỡng "có đổi" | ≥ 12 px khác quá dung sai 2/255 trên một kênh |
| Baseline | `OptionMatrixReview.BaseSettings()` — cùng cảnh nền với ma trận N12(b), nên hai tài liệu không thể mô tả hai khung hình khác nhau |

Mỗi hàng là một chất liệu, đọc từ trái sang phải là slider đi từ 0 đến 100. Vì `Clamp()` được đọc lại sau khi đặt,
một mức bị kẹp xuống (ví dụ ai đổi range thành 0–50) sẽ đỏ ngay thay vì lặng lẽ chụp hai lần cùng một ảnh.

## 2. Hợp đồng, và nó được đo bằng pixel

| Chất liệu | WPF 0→50→100 | GPU 0→50→100 | Vì sao |
|---|---|---|---|
| `Solid` | đổi | đổi | WPF: viền hệ số 170; GPU: `spec` nhân `NoteB.w` (`StageShaders.hlsl:1145`) |
| `Neon` | đổi | **giống hệt từng byte** | WPF: viền hệ số 90; GPU: nhánh Neon không đọc `NoteB.w` |
| `Glass` | đổi | đổi | WPF: viền 170; GPU: `sweep` nhân `NoteB.w` (`:1169`) |
| `Fire` | đổi | **giống hệt từng byte** | WPF: viền 170; GPU: nhánh Fire không đọc `NoteB.w` |
| `Gem` | đổi | đổi | WPF: viền 130; GPU: `spec` nhân `NoteB.w` (`:1215`) |
| `Crystal Outline` | đổi | **giống hệt từng byte** | WPF: viền 72; GPU: chỉ đọc `NoteB.y`/`NoteB.x` — đúng như `EffectContracts` ghi |

"Giống hệt từng byte" ở đây là **0 px** khác giữa hai mức, không phải "khác ít". Đó là nửa còn lại của hợp đồng:
nếu một pass khác bắt đầu đọc slider, con số 0 thành khác 0 và lượt chạy đỏ — thay vì âm thầm đổi hình của một
preset đã duyệt.

Toàn bộ chỗ đọc `NoteB.w` trong shader là bốn dòng: sheen satin (`:1128`, dùng chung cho Solid/Glass khi bật satin)
và ba nhánh Solid/Glass/Gem. Không có chỗ thứ năm.

## 3. Vì sao khác nhau — nhắc lại để không ai "sửa" cho giống

WPF vẽ vector: một đường 1 px màu trắng, alpha = hệ số chất liệu × `NoteRefraction` × độ mờ, cộng sheen satin.
GPU là shader thủ tục, không có khái niệm "viền 1 px" ở cùng nghĩa, và thêm fringe vào shader sẽ đổi hình của preset
đã duyệt để đổi lấy một chi tiết chỉ thấy khi phóng to — đó là lý do §3.6 chốt **không** thêm. Vì thế tài liệu này
không so WPF với GPU như hai ảnh phải giống nhau; nó chỉ hỏi mỗi bên có đổi ảnh khi slider đổi hay không, đúng như
mã nói.

## 4. Chạy và đọc kết quả

```powershell
# chỉ riêng N8 (nhanh, không chạy toàn bộ suite)
PianoPath.exe --verify --verify-refraction-only --verify-review-out=<thư mục>

# hoặc trong lượt đầy đủ: review này tự chạy khi có --verify-review-out
PianoPath.exe --verify --verify-review-out=<thư mục>
```

- Ảnh native: `<thư mục>/refraction/<chất liệu>/<mức>-<renderer>.png` (36 tệp, chỉ nằm trong artifact của lượt chạy).
- Tờ ảnh: `<thư mục>/refraction/sheet-wpf.png` và `sheet-gpu-warp.png` — hai tệp được commit vào
  `docs/evidence/refraction-sheets/`.
- `manifest.json` ghi hợp đồng, từng cặp so sánh (kèm số px), hash pixel của 36 ảnh và của hai tờ; `index.html`
  là bản đọc nhanh trong artifact.
- Log có đúng một dòng `PASS refraction review (N8): 36 …` và nêu tên ba chất liệu phải giống hệt trên GPU.

## 5. Chốt chặn

`tools/check_sources.py` → `scan_refraction_contract()`:

- đọc **chính các nhánh** `else if (style < …)` trong `ShadeNoteMaterial` của `Gpu/StageShaders.hlsl`, xem nhánh nào
  có `NoteB.w`, rồi so với danh sách `GpuConsumers` mà review khẳng định — thêm/bớt một chỗ đọc là làn tĩnh đỏ;
- bắt review đọc `PianoVisualSettings.NoteStyles` thay vì chép tên chất liệu (và cấm chép tên vào review);
- ghim ba mức 0/50/100, 36 ảnh, sự tồn tại của đường viền WPF, baseline dùng chung với N12;
- ghim route `--verify-refraction-only` trong suite và hai tệp được chép/commit trong workflow.

Ngoài ra chính review tự đỏ khi: một mức bị `Clamp()` đổi giá trị, baseline gần như trống, WPF không đổi ảnh ở một
chất liệu, hoặc GPU đổi dù chỉ một pixel ở Neon/Fire/Crystal Outline; và nó đọc lại từng PNG đã ghi để so hash với
manifest.

## 6. Không chứng nhận

Không duyệt mỹ thuật (fringe đẹp hay xấu, dày hay mỏng), không so khớp **hình** giữa WPF và GPU, không đo khung hình
trên card thật (WARP), không nói gì về AR, các quality tier khác, hay thay đổi mặc định/preset. Ba mức 0/50/100 chỉ
chứng minh slider đi qua được ba vị trí, không chứng minh tuyến tính từng bậc.

## 7. Lượt chạy

| Hạng mục | Giá trị |
|---|---|
| Run | _(điền sau lượt xanh đầu tiên)_ |
| Commit | _(điền sau lượt xanh đầu tiên)_ |
| Evidence commit | _(điền sau lượt xanh đầu tiên)_ |
