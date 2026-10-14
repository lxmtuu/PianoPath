# Prism Flow Preview — P1 palette / P2 satin prototype

**Trạng thái:** đã viết bản thử opt-in; đang chờ Windows CI/capture, chưa visual sign-off. Đây không phải hoàn tất roadmap hoặc phát hành một look ổn định.

## Thử trong ứng dụng

1. Mở Style/preset list, chọn **Prism Flow Preview** trong community shelf, rồi Apply. Không thay default Concert Gold và không ghi đè preset đã lưu của người dùng.
2. Notes → Color: chọn palette **Prism Flow**. Hai điều khiển pitch quy định khoảng cố định; recipe dùng **C2–C6 (MIDI 36–84)**. Có thể nhập tên nốt hoặc số MIDI; ngoài khoảng giữ màu đầu gần nhất. Không auto-fit bài hoặc layout, không chạy màu theo thời gian/track.
3. Notes → Shape & Style: **Satin surface** bật bề mặt mới trên Solid/Glass. **Material texture** điều khiển vân; đặt 0 chỉ bỏ vân, không tắt thân/viền/sheen. Với Fire, điều khiển này vẫn là ember-hole texture cũ. Satin không áp dụng cho Neon/Fire/Gem/CrystalOutline, không đổi material ID.
4. Live Stage và AR dùng cùng bảng màu và hàm shader vật liệu. AR Ultra là cấu hình capture đầu tiên; Lite/LOD, delay, webcam/đàn thật và recording/fallback cần evidence riêng.

Chỉ thêm bindings cần thiết vào menu hiện có, có nhãn/tooltip Anh–Việt. **Chưa thực hiện redesign menu P6.**

## Công thức / giới hạn

- Sáu stop đề xuất, không lấy mẫu pixel từ ảnh tham chiếu: cyan `#00CFE8` → mint `#00E3BE` → lime `#A9E879` → champagne `#F2DF85` → peach `#FFAD60` → coral `#FF8E94`, vị trí `0 / .22 / .44 / .64 / .82 / 1`.
- `PrismFlowPalette` nội suy linear-light bằng sRGB transfer chuẩn, trả sRGB; Stage quantise về byte rồi dùng bảng màu có sẵn cho key/note/effects. Renderer giữ transfer/grade hiện hữu, không thay gamma của các preset cũ.
- `NoteSatin=false` mặc định và trong mọi built-in cũ. Bề mặt opt-in thay nhánh body Solid/Glass; thân có pigment, bevel và highlight nhỏ, không làm toàn thân white-hot. Không thêm material ID, texture bitmap, shader pass hay sampler.
- Vân procedural tĩnh theo UV local và phase note seed, chống alias bằng derivatives. Tắt grain không tắt sheen. Không thêm noise theo màn hình/camera hoặc clock. Chưa có motion sheen; UV chuẩn hóa có thể co giãn khi chiều dài bar thay đổi, cần review bằng clip.
- WPF có body/gradient/clip/grain tĩnh gần đúng, không tuyên bố material parity với shader. GPU Stage/AR dùng chung `ShadeNoteMaterial`; khác projection/layout/alpha vẫn cần review riêng.
- Recipe tắt particle/ambient trang trí và các modulator đổi hue để đọc đúng màu thân. **Chưa có flow field, sparks/wisps mới hoặc nâng cấp lifecycle hạt nhấn/giữ**; các mục này vẫn thuộc P3 và phải phủ toàn inventory.
- Hit line hiện vẫn là màu halo chung, chưa có gradient bám layout phím. Đó là việc P4, không giả vờ đã đồng bộ mọi layer chỉ vì body/key nhận cùng palette.

## Kiểm chứng được thêm

- Portable xUnit: sáu stop, midpoint linear-light, clamp, invalid range/position và continuity/gamut. Sandbox không có .NET SDK; kết quả C# thực phải lấy từ CI.
- Windows product checks: settings default/legacy, JSON/apply/share, range clamp, nhập tên nốt, shared colour table cho 128 pitch × 8 track, snapshot opt-in và constant-buffer alignment. Giữ nguyên thứ tự material ID cũ.
- Capture riêng vẫn giữ **78 ảnh P0**. Thêm **30 ảnh P1/P2**: 12 checkpoint × hai mode = 24 candidate, thêm sáu control surface-off / texture-off / bloom-off ở dense chord.
- Control texture đo số pixel khác trên 1 mức kênh; surface/bloom dùng trên 8. Đây là effect-presence/readback check, không phải đo thẩm mỹ hoặc FPS.
- Manifest P1 lưu recipe đầy đủ, seed, input checksum, timestamps, counters và pixel hash. CI giữ sáu preview nhỏ đại diện; full gallery ở artifact `phase1-visual-review/prism-flow-p1`.
- Full Windows verification suite vẫn chạy độc lập và vẫn chặn build khi fail. Không nới guard camera-age hoặc xóa các kiểm tra cũ.

```powershell
dotnet test tests/PianoPath.Tests/PianoPath.Tests.csproj -c Release
dotnet build PianoPath.csproj -c Release
.\bin\Release\net10.0-windows\PianoPath.exe --verify --verify-prism-only "--verify-review-out=$env:TEMP\prism-review" "--verify-log=$env:TEMP\prism-review.log"
```

## Evidence và review

Chưa có kết quả capture của bản thử ở thời điểm ghi phần triển khai. Bổ sung run/source commit và ảnh sau CI; không dùng ảnh AI/mockup làm bằng chứng renderer.

Đối chứng trước thay đổi: [P0 review](PRISM-FLOW-P0-REVIEW.md). Phạm vi lớn hơn và các gate còn mở: [roadmap](PRISM-FLOW-DESIGN-ROADMAP.md), [P0 audit](PRISM-FLOW-P0-AUDIT.md).
