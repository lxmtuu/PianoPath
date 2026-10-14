# P3b — Wisps + Flames, art thử dùng chung Stage / AR

**2026-10-15: prototype đã build/render được; AVI readback đã qua ở lần chạy sửa; full CI còn vướng khai báo JSON-only của prototype. Đang chạy lại; chưa nghiệm thu artwork.**

Tiếp nối [baseline auxiliary](KEY-PARTICLES-AUX-BASELINE.md) và [routing đã PASS](KEY-PARTICLES-RING-CHANNELS.md). Đây là hai họ held đầu tiên, không phải hoàn tất toàn bộ press/held hoặc thay mọi họ bằng Prism Flow.

## Lát thay đổi

- Wisps: nét sáng uốn cong, mềm ở hai đầu; không phải chấm nóng hoặc smoke card.
- Flames: parcel nhiệt có chân rộng, lưỡi cong thuôn lên và lõi ấm; chân material neo tại vị trí parcel mô phỏng, **không** đóng đinh mọi flame lên mặt phím.
- `HeldParticleAppearance`: một nguồn kích thước theo scene unit, màu linear, alpha tuổi thọ, phase và shape. Stage và AR dùng cùng hàm HLSL `ShadeHeldParticleArt` (kind 17), premultiplied output. Kind 16 của watermark không đổi.
- AR: billboard hướng camera tại **vị trí keyboard-local 3D thật** của hạt. Trục ngang theo keyboard, trục đứng vuông góc tia nhìn; không đổi trajectory thành screen-space hoặc flatten camera simulation. Flame chỉ dịch render centre để chân UV=0 trùng anchor vật lý.
- Không thay emitter, seed/RNG, heat, tốc độ, lifespan, release/pedal ownership hoặc clear epoch. Không thêm pass/texture mới. Các họ khác và đường classic giữ nguyên.

## Bật thử, không sửa menu

Flag JSON `HeldParticleArt` mặc định **false**; preset cũ được generator bổ sung false, không đổi các giá trị art cũ. Clone/apply/snapshot giữ flag. Có thể import [Held Particle Art Preview.json](examples/Held%20Particle%20Art%20Preview.json) theo luồng preset JSON hiện có để thử cả hai họ; đặt `HeldParticleArt: false` để so classic. Preset này chỉ là ví dụ trong docs, **không** thay hoặc thêm mặc định vào community shelf.

Phạm vi prototype là **GPU Live Stage + GPU AR**. WPF fallback chưa có material này. Import preset không tự chọn camera, calibration, chất lượng AR hoặc GPU; chọn chúng qua luồng hiện có. Chưa thêm checkbox/menu mới, **P6 menu vẫn làm cuối**.

## Kiểm chứng được chuẩn bị

- Portable: 8 cases cho envelope/kích thước/invalid inputs và camera-facing basis, ba calibration, mirror và Fit/Fill. Không thay portable tests thành bằng chứng GPU.
- Shipping: payload màu/alpha/progress/phase dùng chung, kind 17 ở cả builder, quay lại classic, build không đổi state/time/births, clone/apply và clear epoch.
- Static: 32 captures = 2 họ × 2 pitch (60/61) × 2 mode × 4 phase. So với classic **cùng run, cùng input/time/seed**: active phải khác; effect-off và expired phải bằng; pedal-tail có thể đã hết lifetime nên không ép khác.
- Motion: 8 sequence = 2 họ × 2 mode × classic/art; pitch 60, 60 frame ở 30 FPS, 2 giây. Nhấn/giữ 11 step; step đầu sau thả kết thúc ở t=0,4 s, pedal bật khi thả. Replay seeded prefix cho từng frame, không dùng async recorder nên không rơi/lặp frame do queue. Không phải test audio hoặc MIDI device thật.
- Clip là **APNG lossless**, giữ nguyên PNG frame data, full-frame SOURCE blend, không quantize palette. Crop cố định 140×160 từ output 640×360: Stage (240,180), AR (235,90). Crop giúp review material/tail và giữ Git nhỏ; không dùng clip để chứng nhận composition/full-scene hoặc rằng mọi particle luôn nằm trong crop. Full static 1280×720 và chuỗi PNG nằm trong CI artifact.
- CI giữ 8 active ảnh, 8 APNG crop và manifests ở `docs/previews/held-particle-art`; bộ 32/static + nguồn clip đầy đủ ở artifact. Chỉ được review khi CI đã sinh chúng, không dùng baseline cũ thay bằng chứng material mới.

Local Python/static/diff checks PASS; máy này không có .NET/Windows. Source `892f1d0`, [run `38634465675`](https://github.com/lxmtuu/PianoPath/actions/runs/38634465675): static/portable/Windows build/isolated capture và APNG assembly PASS. Full verification FAIL ở AR AVI readback (5/12 decoded frames, cần ≥6); benchmark/packaging và preview auto-commit không chạy. Artifacts đã giữ 32 ảnh và 8 clip, nhưng **chưa review hình/motion thật**. Lần sửa [offline recording](OFFLINE-VIDEO-FRAME-PRESERVATION.md), run `38636257464`/source `21e8f25`, đã qua AVI với 12 nonempty/distinct payloads, sau đó fail ở kiểm tra độ phủ settings do flag prototype chưa phân loại JSON-only. Đã bổ sung phân loại chính xác flag đó, giữ default-off và không thêm menu; full CI mới vẫn phải PASS trước khi chốt lát này.

## Gate còn mở

Nhận xét before–after thật, cân độ sáng/độ dày/density, full-view press/hold/release/pedal motion, prototype ở oblique/overhead camera thật, nonzero delay, all AR tiers, GPU recording/alpha, WPF fallback và hardware/performance. Tiếp tục mọi burst/held/morph/release family và accents; **menu cuối cùng**, theo [roadmap](PRISM-FLOW-DESIGN-ROADMAP.md).
