# Flames v3 — material candidate, Stage + AR

## Trạng thái

**Đã triển khai, có native capture và viewer để người dùng review; chưa có chấp thuận artwork Flames hoặc default enablement.** Run [38676096695](https://github.com/lxmtuu/PianoPath/actions/runs/38676096695), source **`75845d68de2ae01a7aef2ef25d57565aa5db3bac`**, hoàn tất **SUCCESS**: static, portable .NET tests, Windows build, paired Wisps/Flames validation, full verification suite, benchmark, installer, galleries và distributable. Native renderer: Microsoft Basic Render Driver (WARP). Evidence/12 PNG/APNGs publish ở **`66aaf91`**.

Tiếp nối [Wisps v3](KEY-PARTICLES-WISPS-V3.md) đã được người dùng chấp thuận về hướng hình ảnh. Không mở lại Wisps làm điều kiện chặn Flames. Candidate Flames vẫn review-only/default-off, không binding settings/preset/menu.

## Material và invariants

- Flames tag **4 → 6**; shared Stage/AR HLSL có mantle màu, inner body, tongue phụ thuôn và chuyển sắc dọc thân. Giữ pixel-box stroke coverage; bounded premultiplied RGB, không thêm white-hot core hoặc lifetime envelope thứ hai.
- Giữ nguyên CPU quad/half extents, base anchoring, RGB/alpha payload, progress/phase, emitter/birth recipe, RNG, heat/pedal behaviour, số lượng, cap/packing, particle-centre trajectory, projector và tint/spill. Material có chuyển động nét bên trong quad; không đồng nghĩa silhouette từng pixel phải đi theo đường v2.
- `FlameArtV3ForReview` internal/default-false; chỉ chọn shader khi HeldParticleArt đã bật. Không đổi v2 hoặc Wisps tag 7/8, không sửa default/preset sản phẩm.
- Normal fixture intensity **70**, seed 20261014, chord **60/61/64/67**: **83 packed samples = 21/21/20/21**, dưới cap 192 ở cả Stage/ba AR poses. Không nâng density để làm candidate có vẻ rõ hơn.

## Kiểm chứng thực tế

**CI** kiểm đủ 64 original full-frame hashes/native crops, 16 candidate quartets, same-run paired populations/actual geometry và 240 original motion PNG → APNG decoded hashes/timings. Ghép thêm **120 paired frames**: mỗi frame đặt v2 trái/v3 phải; hash BGRA từng nửa phải bằng native manifest. Không dùng hai APNG autoplay độc lập để so timing.

**Local độc lập** kiểm 8 active candidate + 16 paired-v2 quartet hashes (off candidate trùng v2, không nhân đôi file), 2 candidate full-frame hashes, 19 static + 3 motion source hashes, 240 native motion-half hashes/timings từ hai paired APNGs. So manifest đã lưu ở `18bfb2d`: **288 original control + 192 retained-v2 isolation + 64 accepted-Wisps full-frame hashes**, quartet hashes/comparison metrics, accepted-Wisps population/geometry và **240 accepted-Wisps motion hashes** đều không đổi. Đây là các fixture đã kiểm, không phải chứng nhận mọi tổ hợp sử dụng.

Local không có đủ 64 original fulls hoặc 240 original motion PNGs và không tuyên bố đã mở tất cả. Các originals nằm trong artifact `phase1-visual-review/flames-v3/`; CI là nơi kiểm chúng.

**45 Python tests PASS** với Pillow; source checker 305 C# / 2 XAML và diff check PASS. Synthetic validator E2E kiểm 192 fake v2 + 64 fake v3 frames, 64 sheets/crops, 240 PNG/APNG frames và 120 paired frames; từ chối crop bị sửa dù cập nhật hash của sheet. Đó là test contract, không phải WARP/visual evidence. Viewer có 16 static + 120 motion mock-DOM states, stale/error decode guards, 152 HTTP routes và bốn invalid-frame 404s PASS; chưa phải real-browser playback acceptance.

### Capture recipe

- Static: Prism/Indigo × Stage/reference AR/oblique/near-overhead × tám independent particle/tint/spill states = **8 scenes / 64 fulls / 16 quartets**. 36 steps ở 60 FPS, sample 0,6 s, pedal off; cùng camera/input/seed với v2. Off pixels bằng v2; active pixels thay đổi và có particle signal.
- Motion: Prism, Stage/reference AR, v2/v3; **4 × 60 frames / 30 FPS**, crop 140×160 từ output 640×360. Held 0…11, physical release trước step 12/pedal on, first released sample 13/30 s. Flames vẫn sinh trong lúc heat nguội như baseline. Cuối clip hai version hội tụ trong crop.
- Camera synthetic phẳng, các pose warp khớp calibration; không real camera, không AR relighting hoặc performance acceptance trên GPU thật.

### Actual packed geometry, v2 = v3

83 instances ở mỗi hàng; Prism/Indigo cùng geometry. Đơn vị output pixels, không phải visible silhouette.

| View | Mean quad width | Mean quad height | Global centre-Y range |
|---|---:|---:|---:|
| Stage | 9,2249 | 23,7211 | 53,7816 |
| AR reference | 8,4852 | 24,2177 | 17,5472 |
| AR oblique | 7,8860 | 20,2688 | 50,6490 |
| AR near-overhead | 7,6542 | 19,7046 | 15,3705 |

Corners/geometry hashes giống v2. Global centre-Y range có cả khác biệt pitch/roll, không tự nó chứng minh trajectory hoặc độ rõ. Không thêm minimum screen-size hay projector fix.

### Particle signal, Prism

Số pixel có RGB delta >4 giữa particle-on/off trên cùng lighting state. Thứ tự **neither / tint / spill / both**; là footprint threshold, không phải điểm chất lượng.

| View | V2 | V3 |
|---|---|---|
| Stage | 2529 / 2529 / 2529 / 2529 | 2603 / 2603 / 2603 / 2603 |
| AR reference | 1752 / 1701 / 1752 / 1701 | 1781 / 1758 / 1781 / 1758 |
| AR oblique | 1903 / 1763 / 1910 / 1772 | 1994 / 1911 / 1998 / 1917 |
| AR near-overhead | 1211 / 1174 / 1211 / 1174 | 1236 / 1222 / 1236 / 1222 |

## Quan sát hình ảnh — giới hạn rõ

Đã xem native Prism Stage/reference quartets, combined crops của đủ tám palette/view scenes, và samples motion **0, 6, 11, 12, 18, 24, 35, 59** ở Stage/reference AR: **16 paired frames = 32 original-version samples**. Không nhận đây là đã xem liên tục toàn bộ motion trong browser.

- **Stage:** thân đầy và liền mạch màu hơn, bớt các vệt lõi sáng mảnh rời nhau của v2; đỉnh vẫn thuôn, màu Prism vàng/Indigo tím còn phân biệt. Đổi material nhìn thấy được nhưng không phải tăng chiều cao hoặc thêm ngọn.
- **AR reference:** thân màu đọc rõ hơn trên ivory, nhất là phần giữa; tint/spill vẫn giữ màu nền/key như đối chứng. Thay đổi nhẹ hơn cảm giác Stage, không biến thành glow lớn che keyboard.
- **Oblique/near-overhead:** giữ được sắc và đỉnh nhỏ nhưng độ đọc vẫn hạn chế do bối cảnh/scale. Shader có tongue phụ, chưa khẳng định nhìn thành hai đầu tách biệt ở mọi góc. Thân đầy hơn cũng có thể trông hơi phẳng/block ở scale nhỏ; cần người dùng chọn hướng, không gọi là final quality.
- **Chord:** cụm 60/61 vẫn gần/chồng nhau; đủ sample counts không đồng nghĩa nhìn rõ từng note riêng. Không sửa spacing/packing trong lát material này.
- **Motion samples:** v3 giữ thân màu đầy hơn khi giữ/thả, trong khi heat-tail recipe giữ nguyên; samples cuối không thấy particle khác biệt trong crop. Các samples không thay temporal/flicker, real-camera hoặc hardware sign-off.

## Mở review / tái kiểm

Live viewer **“Flames · Stage & AR review”**: chọn Prism/Indigo, bốn view, particle on/off; mỗi quartet ↖ neither / ↗ tint / ↙ spill / ↘ both. Link motion có phát/dừng, bước frame và nhảy tới release 12; v2/v3 nằm trong **cùng PNG**, không có lệch autoplay. Link full context chỉ có hai candidate fulls, không giả làm paired v2 fulls.

```sh
python -m pip install Pillow==11.3.0
python tools/serve_flames_v3_review.py --output /path/to/flames-review --port 8768
# Hoặc chỉ kiểm published subset, không mở server:
python tools/serve_flames_v3_review.py --output /path/to/flames-review --verify-only
```

Full artifact validation (cần manifests lịch sử đã lưu trước khi overwrite):

```sh
python tools/review_flames_v3.py <review>/flames-v3 \
  --baseline <review>/held-light-isolation --source-root . \
  --expected-revision 75845d68de2ae01a7aef2ef25d57565aa5db3bac \
  --historical <prior-light-isolation.json> --wisps <review>/wisps-v3 \
  --prior-wisps <prior-wisps-v3.json> --prior-wisps-motion <prior-wisps-motion.json>
```

Git chỉ thêm tám active quartets, hai candidate fulls, hai paired APNGs (~5,9 MiB) và hai manifests. Full original dataset/bốn APNGs/standalone artifact viewer không commit; off quartets tái dùng retained v2 sau khi kiểm hash, không xóa deliverable đã duyệt.

**Gate còn mở:** người dùng review hướng Flames, motion/poses/palettes rộng hơn và thiết bị/camera thật; chưa promote/default enablement. Age-representative packing experiment vẫn tách riêng. Tiếp tục các press/held families còn lại; **settings menu là bước cuối**, đồng bộ và gọn/dễ đọc ở Stage + AR.
