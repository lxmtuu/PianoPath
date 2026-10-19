# Particle Motion Lab

Prototype thiết kế cho phần **chuyển động hạt** của Keyflow. Mở `index.html` bằng trình duyệt là chạy;
không cần build, không cần server, không có dependency. Muốn đi qua lại giữa các lab và mở các link tài liệu
thì chạy `python3 tools/serve_prototypes.py` từ gốc repo rồi vào `http://localhost:8080/`.

Lab này **mô phỏng lại** công thức trong `Gpu/GpuStageSimulation.Step()` (và bản WPF trong
`Stage/PianoStage.Advance()`) bằng JavaScript, để so sánh nhanh trước khi sửa mã C#. Nó **không phải**
ảnh chụp từ ứng dụng, không dùng shader, không đo hiệu năng GPU thật.

## Ba trang

- **`index.html`** — A/B *hiện tại ↔ v2* (và chế độ *GPU ↔ WPF*) cho hệ hạt của chính PianoPath.
- **`seemusic.html`** — playground các *kiểu chuyển động* theo ngữ pháp SeeMusic công bố: ba lớp
  Particles / Streaklets / Smoke, tám preset hạt (Default, explode, fast playing, high count, high
  turbulence, long streaklets, straight, twirl) cộng mục Smoke để xem lớp nền, cùng wind / turbulence /
  magnetism. Xem
  [phân tích SeeMusic](../../PARTICLE-MOTION-SEEMUSIC-REFERENCE.md).
- **`damper.html`** — A/B ánh sáng thanh damper: mức pedal 0/1 không nội suy (trước D0) so với đường cong
  cơ học, lõi sáng chạy và đèn damper từng phím. D0–D3 đã vào mã sản phẩm (D3 thêm bloom ignition và nhịp thở,
canvas 2D không mô phỏng hai thứ đó), nên cột
  "hiện tại" trong lab giờ là ảnh chụp lịch sử để so sánh; `damper.js` vẫn là mô hình tham chiếu mà sản phẩm
  được đối chiếu từng hằng số. Xem [phân tích damper](../../DAMPER-BAR-LIGHT-ANALYSIS.md).

`lab.js` vẽ sân khấu cho hai trang hạt; `damper.js` là mô hình + cách vẽ riêng của trang damper.

## Hai chế độ

- **Hiện tại ↔ v2** — hai panel nhận *cùng một chuỗi phát* (same seed, same birth times, same angles,
  speeds and lifetimes). Mọi khác biệt nhìn thấy đến từ mô hình chuyển động, không từ RNG.
- **GPU ↔ WPF** — cùng preset, hai đường render đang có trong sản phẩm. Dùng để thấy lệch wisp
  (GPU cộng dao động lúc vẽ, WPF cộng vào vận tốc).

## Thành phần v2 bật/tắt được

`Curl field`, `Drag curve`, `Life curves`, `Diffusion`, `Spin`, `Stagger birth`, `Depth`, `Floor bounce`,
`Flame physics`, `Wisp in sim` — mỗi cái tương ứng một mục trong
[PARTICLE-MOTION-REVIEW.md](../../PARTICLE-MOTION-REVIEW.md) §5.

## Phím tắt

`Space` phát/dừng · `R` chơi lại.

## Kiểm tra

```bash
node tools/check_particle_motion_lab.cjs
node tools/check_damper_light.cjs
```

Hai script chạy headless: `check_particle_motion_lab.cjs` (36 assertions) kiểm tra lịch phát, tính tất định
theo seed, độ ổn định 8 s cho cả năm họ hạt, độ nhạy của slider, các tính năng v2, đường vẽ, và playground
SeeMusic (tám preset, ba lớp, magnetism, wind, twirl). `check_damper_light.cjs` (33 assertions) khoá các con số
của [DAMPER-BAR-LIGHT-ANALYSIS.md](../../DAMPER-BAR-LIGHT-ANALYSIS.md): hai mức trước D0 vs 51 mức đề xuất, dead
zone, thời gian lên/xuống, quãng chạy của lõi, lớp ô LED theo phím, và **khớp từng hằng số** với
`Camera/ArDampers.cs`. Script hạt cố ý **thất bại** nếu hai mô
hình trở nên giống hệt nhau — prototype mà A/B không khác gì thì không chứng minh được gì.

## Giới hạn

- Không phải renderer của sản phẩm: không bloom, không HDR, không shader, không bàn phím 3D.
- Thang đo 960×540; vị trí phím dùng tỉ lệ của `KeyboardLayout`, không phải pixel của cửa sổ thật.
- `MaxParticles` của lab là không giới hạn; ứng dụng có trần 24.000 và ngân sách phát theo phím.
- Số liệu trong tài liệu review là số của *công thức*, không phải của GPU thật.
