# Wisps v3 — candidate material dưới cap

## Trạng thái

**Bước sửa hướng AR sau review:** ảnh trong báo cáo artwork này được giữ ở geometry lịch sử (`legacyParticleAxesForReview=true`) để không trộn thay đổi material với thay đổi hướng. Đây không phải demo của đường AR mới; xem [đồng bộ hướng hạt/nốt](AR-PARTICLE-DIRECTION-ALIGNMENT.md). Artwork đã duyệt vẫn giữ nguyên.

**Đã triển khai và capture candidate; người dùng đã chấp thuận hướng hình ảnh ở phần review hiện tại. Chưa promote/default enablement hoặc nghiệm thu toàn bộ điều kiện sử dụng.** Run [38672193965](https://github.com/lxmtuu/PianoPath/actions/runs/38672193965), source **`93e2a75`**, hoàn tất **SUCCESS**: static, portable .NET tests, Windows build, 64 full-frame hashes/16 native quartets, paired population/geometry, 240 lossless APNG frames, full verification **6.108 assertions / 1 skipped group**, benchmark, installer, galleries và distributable. Evidence/22 PNG/APNGs publish ở **`7c43e9b`**. Native renderer: Microsoft Basic Render Driver (WARP).

Run đầu `38671949052` tìm ra harness dùng record-copy với `GpuLook` vốn là class; đã sửa thành copy review-only, không đổi type/equality toàn bộ look. Run `38672121075` bị supersede/cancel. Chỉ dùng run cuối ở trên làm bằng chứng build/render mới.

Local kiểm độc lập: **16 candidate + 48 retained-baseline quartet hashes**, **2 candidate full-frame hashes**, **19 static + 3 motion source hashes**, **240 APNG decoded frame hashes/timings**, paired coverage/geometry metadata. So manifest trước `464c58b`: **288 lighting-control và 192 tint/spill full-frame pixel hashes**, cùng toàn bộ comparison metrics, không đổi. CI còn kiểm đủ 64 v3 originals/native crops và motion PNG → APNG; local không có đủ 64 originals và không tuyên bố đã mở tất cả.

**41 Python tests PASS**, source checker 305 C# / 2 XAML và diff check PASS. Synthetic validator E2E kiểm 192 fake v2 + 64 fake v3 frames, 64 crops và 240 APNG frames, từ chối crop/frame hash bị sửa; đó là test contract, không phải native visual evidence. Live viewer có 16 paired mock-DOM states + 120 motion states, stale/error decode guards và 45 HTTP routes PASS; không thay browser/GPU/temporal acceptance.

Tiếp theo [tint/spill + coverage](KEY-PARTICLES-TINT-SPILL-COVERAGE.md). V2 đã chỉ ra hai vấn đề riêng: AR normal bỏ nhóm tuổi ≥0,4 s ở cap 192, nhưng low giữ đủ 76 samples vẫn khó đọc trên ivory. Lát này chỉ thử **material dưới cap**, không trộn selection policy hoặc đổi geometry.

## Candidate

- `GpuLook.WispArtV3ForReview` internal/default-false, không settings/JSON/preset/menu binding; cần HeldParticleArt đang bật mới đi qua material experimental.
- Shared surface chỉ đổi discriminator Wisps 7 → 8; half extents, RGB/alpha payload, progress, phase, anchoring, simulation/physics, lifecycle, clocks, RNG, birth recipe giữ nguyên. Flames giữ discriminator 4; v2 path giữ lại.
- Shared Stage/AR HLSL: ribbon có shoulder + ridge màu và tapered ends; bounded straight colour, saturation shaping nhẹ, không warm-white core. Giữ pixel-box stroke integration, không thêm minimum screen-size hay lifetime fade thứ hai. Alpha envelope dùng nguyên payload; coverage của material thay đổi có chủ ý, không giảm alpha toàn bộ family.
- Không sửa projector, billboard axes, cap 192, priority/traversal, production WispAmount hoặc key tint/spill strength. Đây không phải bản sửa scale/projection hoặc default enablement.

## Capture / kiểm độc lập

- Hai palette Prism/Indigo × Stage + 3 AR poses × 8 particle/tint/spill states = **8 cảnh / 64 full frames / 16 native quartets**. Cùng low fixture WispAmount 8, hợp âm 60/61/64/67, seed 20261014, 36 steps ở 60 FPS (0,6 s).
- V2 render trước trong cùng run: bắt buộc v3 giữ nguyên raw/eligible/packed coverage và sorted sample hashes, particle-off pixels; active pixels phải thay đổi và có particle signal. V2 mới phải giữ đủ **192 pixel hashes/48 sheets/comparison metrics** từ manifest lịch sử đã publish, không cập nhật baseline âm thầm.
- Hai renderer quan sát **actual packed geometry**, không tính lại bằng projector giả: quad corners (AR) hoặc sprite rectangle (Stage) thành output pixels. Ghi edge width/height min/max/mean và centre-Y range; v3/v2 geometry hash phải giống. Đây là bounds của quad, không phải diện tích silhouette, occlusion hay độ rõ. Các số đo giúp phân biệt billboard collapse với độ trải tâm hạt; xem số đo thực bên dưới. Centre-Y range là global bounds, có cả khác biệt pitch/roll, không tự nó chứng minh nguyên nhân trajectory.
- Native tests so sánh Stage/AR packets v2/v3: chỉ tag material Wisps thay đổi; góc/weights/alpha/progress/phase/count giữ nguyên. Default look không bật candidate.
- Motion: Prism chord low, Stage/reference AR, v2/v3; **4 sequences × 60 frames / 30 FPS**. Hold frames 0…11, physical release trước step 12 trong khi pedal bật, first released sample ở 13/30 s. Dùng Wisp lifetime nguyên bản; hết tail phải hội tụ baseline. Crop 140×160 từ render 640×360, 240 frame hashes và APNG frame/timing decode độc lập; chưa cover motion ở oblique/overhead/Indigo.

Full frames, frame PNGs và self-contained viewer ở artifact `phase1-visual-review/wisps-v3/`. Git chỉ publish 16 sheets, hai Prism combined fulls (Stage/reference), bốn APNGs, static/motion manifests. Không commit HTML/base64 hoặc toàn bộ dataset.

```sh
python -m pip install Pillow==11.3.0
python tools/review_wisps_v3.py <review>/wisps-v3 \
  --baseline <review>/held-light-isolation --source-root . \
  --expected-revision <capture-sha> --historical <review>/prior-light-isolation.json
```

`--sheets-only` kiểm subset sheets/APNGs, không chứng nhận các full-frame files không có tại local. Viewer paired/decode-gated/generation guarded; không thay browser/GPU hoặc temporal visual review.

## Số đo geometry thực, không phải visible-area estimate

Prism, active/both, 76 packed samples; **v2/v3 geometry hashes và từng summary giống hoàn toàn**:

| View | Mean quad width (px) | Mean quad height (px) | Centre-Y min…max (px) | Global Y span (px) |
|---|---:|---:|---|---:|
| Stage | 3,471 | 10,962 | 505,663…568,557 | 62,895 |
| AR reference | 3,231 | 11,316 | 279,285…297,954 | 18,669 |
| AR oblique | 2,986 | 9,423 | 331,345…381,206 | 49,861 |
| AR near-overhead | 2,910 | 9,194 | 312,028…315,819 | 3,791 |

Ở reference, **bản thân quad không thấp hơn Stage**. Cả ba AR poses vẫn có height hữu hạn; near-overhead min height 4,786 px. Đây không phải kiểu mất toàn bộ billboard do đi edge-on. Nhưng tâm các parcels ở near-overhead tụ trong dải Y rất hẹp. Global bounds gồm cả khác biệt pitch/roll; muốn chẩn đoán hướng rise theo tuổi phải thêm phép so theo từng pitch/age, không gọi con số span này là phép đo trajectory độc lập.

Không có cơ sở tăng đồng loạt kích thước billboard hoặc đổi camera-facing axes từ kết quả này. Lát này **không sửa geometry**; footprint silhouette còn phụ thuộc material, nền, overlap và projection của các vị trí.

## Đọc ảnh thật / sampled motion

Đã mở hai overview 50% so đủ tám scene active của cả v2/v3. Đọc native v3 Prism/reference, Indigo/Stage, Indigo/oblique, Prism/near-overhead; đọc native v2 Prism/reference và Indigo/Stage để so. Đã mở hai v3 full scenes Prism/Stage và reference. Motion review tại local mở hai contact sheets **native pixel**: frames 0/8/11/12/20/30/45/59 của cả hai versions, Stage/reference (32 cropped samples). Chưa xem playback liên tục trên browser/hardware hoặc đọc native toàn bộ 240 frames.

- **V3 có vùng thân màu liền hơn, bớt nét trắng ở Prism/reference.** Khác biệt là tăng dần, không phải một bước nhảy lớn ở full scene. Trong combined, màu tint/phím vẫn chiếm ưu thế.
- **Indigo trên Stage có shoulder/ridge rõ hơn một chút**, nhưng low fixture vẫn thưa và mảnh; chưa thể tuyên bố đã đạt độ rõ cho bài hát dày hoặc màn hình nhỏ.
- **Near-overhead/oblique AR chưa giải quyết xong tổng silhouette/readability.** Các nét vẫn nhỏ/gần mép ivory dù đủ 76 samples; v3 material không tự tạo plume có chiều cao như Stage. Đây là lý do chưa promote candidate.
- Sampled motion giữ cùng nhịp hold/release và đã hội tụ ở frame 59 (được kiểm hash); tail đi lên và mờ dần trong các samples. Chưa dùng tám checkpoints để kết luận không flicker hoặc temporal quality tốt hơn.

Ví dụ Prism, particle-factor full-frame RGB >4 footprint, **v2 → v3**:

| View | Neither | Tint only | Spill only | Both |
|---|---|---|---|---|
| Stage | 1.001 → 1.128 | 1.001 → 1.128 | 1.001 → 1.128 | 1.001 → 1.128 |
| AR reference | 788 → 847 | 528 → 631 | 788 → 847 | 528 → 631 |
| AR oblique | 773 → 837 | 378 → 554 | 774 → 832 | 369 → 542 |
| AR near-overhead | 453 → 474 | 327 → 372 | 460 → 475 | 327 → 371 |

Có signal trong mọi particle comparison, nhưng **không dùng footprint làm brightness/quality score**. Premultiplied material mới bound colour và đổi coverage có chủ ý; output alpha/silhouette không được tuyên bố giống v2 dù lifetime alpha payload giữ nguyên.

Live review có paired static quartets, hai cặp full scenes và player motion **cùng frame index**, decode cả hai trước khi hiện, có play/pause/step/release. APNG autoplay độc lập ở artifact không tự bảo đảm đồng bộ trình bày; dùng frame index để so timing. Không lưu HTML/base64 lớn trong Git.

## Quyết định / gate tiếp theo

Giữ v3 là **review-only/default-off**, giữ v2 làm baseline; chưa promote hoặc bật mặc định. Chưa sửa age bias cap 192, production density, emitter hoặc lifetime. Hướng geometry tiếp theo nên là candidate độc lập về **độ trải/rise trên AR theo pitch/age và pose**, giữ cùng pool, không tăng tất cả quad hoặc đổi projector theo phỏng đoán; phải so paired Stage/AR và material v2/v3.

Sau đó mới tiếp tục Flames và các press/held families còn lại. Chưa nghiệm thu continuous motion, camera thật/hand occlusion/exposure, tier/delay, alpha-recording/WPF hoặc hardware. Settings menu vẫn làm cuối cùng.


## Phản hồi người dùng — 2026-10-15

Sau khi nhận viewer và kết quả v3, người dùng phản hồi: **“Dạ tớ thấy ổn rồi”**. Ghi nhận chấp thuận hướng hình ảnh Wisps v3 ở phần review hiện tại; không suy ra đã xem từng capture/frame, yêu cầu đổi mặc định hoặc nghiệm thu mọi camera/tier/hardware.

Hướng artwork Wisps hiện tại được giữ làm mốc để chuyển vòng thiết kế sang **Flames**, luôn paired Stage/AR. Các vấn đề projection/rise và age bias đã ghi vẫn là hạng mục riêng; phản hồi này không thay số đo hoặc xác nhận chúng đã được sửa. Chưa đổi code/product flag; các press/held families khác tiếp tục theo roadmap, settings/menu vẫn cuối cùng.
