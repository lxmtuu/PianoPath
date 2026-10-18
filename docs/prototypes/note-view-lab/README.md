# Note view lab

Ba đường vẽ nốt trong một trang, chạy cùng một bản nhạc và cùng một đồng hồ:

1. **WPF / GPU stage** — một công thức chung: `lane = W/88`, bề rộng `lane × NoteWidth%`,
   chiều cao `clamp(duration·speed − gap, 16, 0,9·hitY)`.
2. **AR phẳng** — mặc định của AR: bề rộng `span(phím) × 0,86`, đuôi cắt ở `Reach`, live-bar release 0,35 s.
3. **AR 3D** — `Notes in perspective`: thêm ghost, landing glow, nhãn nốt, min-aspect, cắt nốt trùng pitch,
   live-bar release 3,0 s.

Mục đích là nhìn thấy ba việc N1–N3 trong
[NOTE-EFFECTS-ACTION-PLAN.md](../../NOTE-EFFECTS-ACTION-PLAN.md) trước khi sửa mã sản phẩm.

## Chạy

```bash
# từ thư mục gốc repo — phục vụ cả repo để link tới docs/ hoạt động
python3 -m http.server 8081 --bind 0.0.0.0
# mở http://localhost:8081/docs/prototypes/note-view-lab/
```

Trang tĩnh, không cần build, không gọi mạng.

## Điều khiển

- **Khung hình** 900p / 1080p / 1440p: đổi chiều cao logic, giữ nguyên 16:9. Đây là cách thấy thanh nốt dài
  bị cắt khác nhau theo cửa sổ.
- **Note width** 30–100%: bề rộng của stage (AR không đổi).
- **Quy ước bề rộng**: `Lane` (hiện tại), `Lane + kẹp 98% phím đen`, `Theo phím (Fit keys)` — ba phương án của N1.
- **Min-aspect**: 0–200%, chỉ ảnh hưởng panel 3D.
- **Hiệu ứng AR 3D**: bật/tắt ghost, landing glow, nhãn — để thấy panel phẳng không có gì trong số đó.

## Số đo đã ghim

`tools/check_note_view_lab.cjs` kiểm tra 44 điều kiện: hình học 88 phím, bề rộng 17,45 / 31,75 px ở 1080p,
thanh 4 s bị cắt còn 2,50 / 3,00 / 4,00 s ở 900/1080/1440p, reach 3,0 s, min-aspect, cắt nốt trùng pitch,
release 0,35 vs 3,0 s, và đường vẽ không ném lỗi.

```bash
node tools/check_note_view_lab.cjs
```

## Giới hạn

Đây là **hình học và thời gian**, không phải render thật. Màu, bloom, vật liệu và camera được đơn giản hoá;
AR dùng phép chiếu phẳng/lược đồ với một key-depth bằng chiều cao bàn phím, không đi qua calibration camera.
Muốn số liệu chính xác của từng hằng số, chạy `python3 tools/note_effects_report.py --check`.

Không sửa mã sản phẩm.
