# Chất liệu nốt trong lớp AR: sáu thiết lập, ba chất liệu (N16)

> **Trạng thái:** làm 2026-10-22 · **Mã:** `Diagnostics/ArMaterialReview.cs` · **Chốt chặn:**
> `tools/check_sources.py` (`scan_ar_material_review`) · **Ảnh:** `docs/evidence/ar-material-sheets/material.png`

Trong `Stage/Effects/EffectContracts.cs`, ba chất liệu nốt của pilot (Solid, Gem, Crystal Outline) có
`WpfStage`/`GpuStage`/`Recording` = `Native`, nhưng `ArOverlay` vẫn `Unverified`, và mọi binding tham số có
`ArOverlay = Unverified`. Lý do được ghi ngay trong mã: *"AR material capability and parameter bindings remain
Unverified until an AR-specific style/value matrix is reviewed."* Tài liệu này là ma trận đó.

## 1. Câu hỏi và câu trả lời

**Câu hỏi:** một thiết lập của chất liệu nốt có thật sự đi tới quad AR không, hay chỉ đi tới sân khấu?

**Vì sao phải đo:** quad AR được tô bằng **chính** `ShadeNoteMaterial` mà sân khấu dùng, nên đọc mã thì câu trả lời
là "có" cho mọi thứ nhánh đó đọc. Nhưng đọc mã không chứng minh được rằng uniform thật sự tới được pass AR (có thể
bị ghi đè ở `PsArQuad`, bị bỏ ở `GpuLook.From`, hoặc bị một lớp khác phủ lên). Không có ảnh thì đó vẫn là suy luận.

**Cách đo:** giữ nguyên pose, camera, cảnh, seed; đổi **một** thiết lập; đếm pixel đổi.

| Hạng mục | Giá trị |
|---|---|
| Chất liệu | ba chất liệu của `EffectContractCatalog.NoteMaterialPilot` — đọc từ contract, không chép tay |
| Thiết lập | `NoteTint`, `NoteGlow`, `NoteEdge`, `NoteEdgeWidth`, `NoteRefraction`, `Notes3D` — cũng đọc từ contract |
| Giá trị mỗi dòng | đầu mút **xa giá trị mặc định nhất** trong khoảng của contract (boolean thì lật) |
| Pose | `ar-reference` (pose chuẩn của các review AR khác) |
| Cảnh | một nốt đang rơi, 1,5 s, seed `20261021`, 1280×720, 60 fps |
| Renderer | AR trên **D3D/WARP**; cùng hai thiết lập đó vẽ thêm trên WPF và GPU/WARP (sân khấu phẳng, 960×540 — khung AR là ảnh camera 1280×720 nên số pixel của hai cột không cùng thang đo, chỉ cùng ngưỡng 12 px) để so |
| Số khung AR | 3 chất liệu × (1 khung "default" + 6 khung) = **21** |
| Số ô trên tờ | 21 = 3 cột (chất liệu) × 7 hàng (default + 6 thiết lập), mỗi ô là **ảnh gốc 1280×720** — không hạ mẫu |
| Ngưỡng "có đổi" | ≥ 12 px khác quá dung sai 2/255 trên một kênh |

## 2. Kỳ vọng đến từ contract, không đến từ tên chất liệu

Mỗi ô được phán bằng đúng dữ liệu typed, không bằng một bảng viết tay:

- Thiết lập **không có** trong `Parameters` của chất liệu đó ⇒ phải **dưới ngưỡng 12 px** (kỳ vọng 0).
  (Crystal Outline không khai báo `Notes3D`; khung vẫn được chụp để cho thấy điều đó là thật, không phải bỏ sót.)
- Thiết lập có, nhưng contract ghi `GpuStage = Ignored` ⇒ cũng phải **dưới ngưỡng 12 px**, vì quad AR chạy đúng nhánh shader đó.
- Còn lại ⇒ phải **≥ 12 px**.

Vì sao "dưới ngưỡng" chứ không "đúng 0": khung AR là ảnh ghép trên ảnh camera, nên điều cần khẳng định là *thiết lập
không tới được chất liệu*, không phải hai lần render cùng cảnh giống nhau tuyệt đối. Con số thật vẫn được ghi vào
manifest từng ô, và ở N8 — nơi đo trên đường phẳng — cùng loại bằng chứng đã ra đúng **0 px**.

**Chốt chặn chống "xanh vì không có gì để đổi":** khung AR là ảnh ghép trên camera, nên đếm pixel "không đen" luôn
ra toàn ảnh (921 600 px) và chẳng chứng minh gì. Thứ đáng đếm là phần **lớp phủ vẽ đè lên camera**. Vì vậy *mọi* khung
AR — kể cả khung "default" — phải tô đè ít nhất **500 px**; nếu quad biến mất hoặc mờ đi, mọi thiết lập sẽ ra 0 px và
các ô "Ignored" sẽ đạt một cách vô nghĩa. Review ném lỗi ngay ở khung vi phạm, trước khi con số của nó được tin.

Nói cách khác: nếu shader bắt đầu đọc một thứ mà contract nói nó không đọc, hoặc ngừng đọc một thứ contract nói
nó đọc, lượt chạy đỏ và gọi tên ô đó. Đây là lý do review đọc `Bindings.GpuStage` thay vì so tên chất liệu — và
`scan_ar_material_review` cấm review viết tên chất liệu vào mã.

**Ngoại lệ có chủ ý — `NoteGlow`:** được **đo nhưng không tuyên bố**. Quầng sáng nằm *ngoài* hình chữ nhật của nốt,
mà quad AR cắt đúng theo thân nốt, nên trên quad nó chỉ chạm được bốn góc bo. Con số vì thế không nói lên điều một
người xem thấy; nó được ghi vào manifest kèm nhãn `Recorded` để lần sau ai muốn tuyên bố thì có sẵn số.

## 3. Đọc kết quả

- Ảnh native: `<thư mục review>/ar-material/<chất liệu>/<thiết lập>-<giá trị>.png` (21 tệp, chỉ trong artifact).
- Tờ ảnh: `<thư mục review>/ar-material/sheet.png` → commit thành `docs/evidence/ar-material-sheets/material.png`.
- `manifest.json` ghi từng ô: `ArChanged`, `WpfChanged`, `GpuChanged`, `ArLit`, `ArPainted` (phần lớp phủ vẽ đè camera), `Claim` (`Applied`/`Ignored`/`Recorded`) và ghi chú.
- `index.html` là bảng đọc nhanh trong artifact.
- Log có đúng một dòng `PASS AR note-material review (N16): 18 …`.

Khi đọc bảng, hãy nhớ ba cột là **ba renderer khác nhau**, không phải ba lần đo cùng một thứ: WPF vẽ vector, GPU
vẽ shader, AR vẽ shader đó lên ảnh camera. Chúng **được phép** khác nhau (N8 là hồ sơ của việc đó); điều review này
đòi là mỗi cột tự nhất quán với contract của chính nó.

## 4. Chạy

```powershell
PianoPath.exe --verify --verify-ar-material-only --verify-review-out=<thư mục>
PianoPath.exe --verify --verify-review-out=<thư mục>   # chạy kèm toàn bộ review
```

## 5. Chốt chặn

`tools/check_sources.py` → `scan_ar_material_review()`:

- review phải đọc `EffectContractCatalog.NoteMaterialPilot` (nguồn chất liệu);
- danh sách `Known` của review phải **bằng đúng** hợp các tham số của ba contract;
- mỗi tên phải có `case` trong `Apply`/`Read` (không thì thiết lập bị chụp ở giá trị mặc định mà tưởng là đã đổi);
- review **không** được viết tên chất liệu (`"Solid"`, `"Gem"`, `"CrystalOutline"`) trong mã;
- kỳ vọng phải lấy từ `Bindings.GpuStage`;
- suite phải còn route `--verify-ar-material-only` và lời gọi `ArMaterialReview.Render(`;
- workflow phải copy `ar-material/sheet.png` vào `docs/evidence/ar-material-sheets` và `git add` thư mục đó.

## 6. Không chứng nhận

Không duyệt mỹ thuật (một khung mỗi thiết lập, không phải mọi giá trị trong khoảng), không nói gì về camera thật,
che tay, GPU thật, các quality tier khác, hay thay đổi mặc định. Không so **hình** giữa WPF/GPU/AR như một yêu cầu
phải giống nhau. Cũng không khẳng định `ArOverlay = Native` cho cả ba chất liệu: đó là một câu về **năng lực**
backend, cần thêm phủ theo giá trị/lifecycle, và được xét riêng ở §7.

## 7. Cập nhật contract — hai bước, có chủ ý

Lượt đầu chỉ **đo**: review chạy, ra số, chưa đổi một dòng contract nào. Số đo được đọc từ manifest/log, rồi
`ArMaterialParameterBindings`/`WpfOnlyRefractionBindings` mới được đổi `ArOverlay` từ `Unverified` sang
`Applied`/`Ignored` trong một commit riêng, kèm cập nhật `EffectContractTests` và bảng audit. Làm gộp sẽ là đoán
trước kết quả rồi viết test cho vừa — đúng thứ mà cả tài liệu này tránh.

| Hạng mục | Giá trị |
|---|---|
| Lượt chạy | GitHub Actions [run 39777950433](https://github.com/lxmtuu/PianoPath/actions/runs/39777950433) trên `1bcf545`: `build` xanh, bước `Verification suite` chạy review và bước `Stage the review contact sheets` commit tờ ảnh. Log/artifact không tải được từ sandbox, nên số dưới đây đọc lại **từ chính tờ ảnh đã commit** (ô 1:1, cùng thuật toán `ChangedPixels`: bỏ alpha, dung sai 2/255) — cùng phép đo mà manifest ghi. |
| Số đo AR (px) | Solid: NoteTint **15 826**, NoteGlow 9 841 (Recorded), NoteEdge **37 874**, NoteEdgeWidth **14 456**, NoteRefraction **10 901**, Notes3D **5 030** · Gem: 706, 4 644 (Recorded), 3 198, 1 671, 402, 1 716 · Crystal Outline: 626, 3 999 (Recorded), 48 591, 5 319, **0**, **0** — hai số 0 đúng là `NoteRefraction` (contract `Ignored` trên GPU) và `Notes3D` (contract không khai báo; shader không đọc `NoteC.y`). |
| Contract | Bước 2, commit riêng: `ArMaterialParameterBindings.ArOverlay` → `Applied`; `WpfOnlyRefractionBindings.ArOverlay` → `Ignored`; `NoteGlow` tách sang `ArGlowParameterBindings`, `ArOverlay` vẫn `Unverified`; năng lực `ArOverlay` của cả ba chất liệu **vẫn** `Unverified`. `EffectContractTests` cập nhật theo bốn chỗ ghim cũ. |
