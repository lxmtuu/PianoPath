# Wisps v3 — candidate material dưới cap

## Trạng thái

**Đã triển khai candidate/harness; chưa có Windows build/WARP evidence cho code mới.** Không dùng CI `38665781114` (v2 study) để chứng nhận v3. Cần CI mới, đọc ảnh/motion thật và ghi số đo trước khi kết luận.

Tiếp theo [tint/spill + coverage](KEY-PARTICLES-TINT-SPILL-COVERAGE.md). V2 đã chỉ ra hai vấn đề riêng: AR normal bỏ nhóm tuổi ≥0,4 s ở cap 192, nhưng low giữ đủ 76 samples vẫn khó đọc trên ivory. Lát này chỉ thử **material dưới cap**, không trộn selection policy hoặc đổi geometry.

## Candidate

- `GpuLook.WispArtV3ForReview` internal/default-false, không settings/JSON/preset/menu binding; cần HeldParticleArt đang bật mới đi qua material experimental.
- Shared surface chỉ đổi discriminator Wisps 7 → 8; half extents, RGB/alpha payload, progress, phase, anchoring, simulation/physics, lifecycle, clocks, RNG, birth recipe giữ nguyên. Flames giữ discriminator 4; v2 path giữ lại.
- Shared Stage/AR HLSL: ribbon có shoulder + ridge màu và tapered ends; bounded straight colour, saturation shaping nhẹ, không warm-white core. Giữ pixel-box stroke integration, không thêm minimum screen-size hay lifetime fade thứ hai. Alpha envelope dùng nguyên payload; coverage của material thay đổi có chủ ý, không giảm alpha toàn bộ family.
- Không sửa projector, billboard axes, cap 192, priority/traversal, production WispAmount hoặc key tint/spill strength. Đây không phải bản sửa scale/projection hoặc default enablement.

## Capture / kiểm độc lập

- Hai palette Prism/Indigo × Stage + 3 AR poses × 8 particle/tint/spill states = **8 cảnh / 64 full frames / 16 native quartets**. Cùng low fixture WispAmount 8, hợp âm 60/61/64/67, seed 20261014, 36 steps ở 60 FPS (0,6 s).
- V2 render trước trong cùng run: bắt buộc v3 giữ nguyên raw/eligible/packed coverage và sorted sample hashes, particle-off pixels; active pixels phải thay đổi và có particle signal. V2 mới phải giữ đủ **192 pixel hashes/48 sheets/comparison metrics** từ manifest lịch sử đã publish, không cập nhật baseline âm thầm.
- Hai renderer quan sát **actual packed geometry**, không tính lại bằng projector giả: quad corners (AR) hoặc sprite rectangle (Stage) thành output pixels. Ghi edge width/height min/max/mean và centre-Y range; v3/v2 geometry hash phải giống. Đây là bounds của quad, không phải diện tích silhouette, occlusion hay độ rõ. Các số đo giúp phân biệt billboard collapse với chiều cao/phân bố trajectory bị chiếu rút ngắn; chưa có số đo mới để kết luận.
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

## Gate còn mở

Windows CI/C# compilation, source/pixel/crop/motion validation và đọc ảnh mới. Sau đó mới quyết định candidate có tốt hơn trên cả Stage lẫn AR hay không. Không coi threshold footprint là điểm quality; chưa sửa age bias, chưa nâng cấp Flames/các press-held family khác, chưa nghiệm thu camera thật/tier/delay/alpha-recording/WPF/hardware. Settings menu vẫn làm cuối cùng.
