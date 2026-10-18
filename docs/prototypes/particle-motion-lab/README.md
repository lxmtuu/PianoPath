# Particle Motion Lab

Prototype thiết kế cho phần **chuyển động hạt** của Keyflow. Mở `index.html` bằng trình duyệt là chạy;
không cần build, không cần server, không có dependency.

Lab này **mô phỏng lại** công thức trong `Gpu/GpuStageSimulation.Step()` (và bản WPF trong
`Stage/PianoStage.Advance()`) bằng JavaScript, để so sánh nhanh trước khi sửa mã C#. Nó **không phải**
ảnh chụp từ ứng dụng, không dùng shader, không đo hiệu năng GPU thật.

## Hai chế độ

- **Hiện tại ↔ v2** — hai panel nhận *cùng một chuỗi phát* (same seed, same birth times, same angles,
  speeds and lifetimes). Mọi khác biệt nhìn thấy đến từ mô hình chuyển động, không từ RNG.
- **GPU ↔ WPF** — cùng preset, hai đường render đang có trong sản phẩm. Dùng để thấy lệch wisp
  (GPU cộng dao động lúc vẽ, WPF cộng vào vận tốc).

## Thành phần v2 bật/tắt được

`Curl field`, `Drag curve`, `Life curves`, `Diffusion`, `Spin`, `Stagger birth`, `Depth`, `Floor bounce`,
`Flame physics`, `Wisp in sim` — mỗi cái tương ứng một mục trong
[PARTICLE-MOTION-REVIEW.md](../PARTICLE-MOTION-REVIEW.md) §5.

## Phím tắt

`Space` phát/dừng · `R` chơi lại.

## Kiểm tra

```bash
node tools/check_particle_motion_lab.cjs
```

Script chạy headless: kiểm tra lịch phát, tính tất định theo seed, độ ổn định 8 s cho cả năm họ hạt,
độ nhạy của slider, các tính năng v2 và đường vẽ. Nó cố ý **thất bại** nếu hai mô hình trở nên giống hệt
nhau — prototype mà A/B không khác gì thì không chứng minh được gì.

## Giới hạn

- Không phải renderer của sản phẩm: không bloom, không HDR, không shader, không bàn phím 3D.
- Thang đo 960×540; vị trí phím dùng tỉ lệ của `KeyboardLayout`, không phải pixel của cửa sổ thật.
- `MaxParticles` của lab là không giới hạn; ứng dụng có trần 24.000 và ngân sách phát theo phím.
- Số liệu trong tài liệu review là số của *công thức*, không phải của GPU thật.
