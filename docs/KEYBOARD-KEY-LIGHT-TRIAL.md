# Key light: bản thử tắt đúng đèn chính ở 0%

Ngày: 2026-10-16. Người dùng đã duyệt ảnh đối chiếu trong viewer: **“tớ thấy ổn rồi”**. Chốt hướng sửa và hình ảnh của bản thử này; **chưa phải xác nhận app/thời gian thực trên máy người dùng hoặc full regression**. Không mở lại các bản particle đã duyệt và không bắt đầu thiết kế lại Settings.

## Thay đổi có chủ đích

GPU trước đây dùng `softbox = .35 + 1.25*t` với `t = Key light / 100`. Vì vậy 0% vẫn còn gain đèn chính `.35`. Software đã có đèn chính về 0; không thay mapping software để ép hai renderer sáng bằng nhau.

Bản thử chỉ sửa gain softbox trong `PsKey`:

```
t < .25: t * (4.05 - 5.6*t)
t >= .25: .35 + 1.25*t
```

Gain bằng 0 tại 0%, tăng đơn điệu trong miền điều khiển 0–200%, liên tục cả giá trị lẫn đạo hàm tại 25% (gain .6625, slope 1.25). Từ 25% trở lên giữ nguyên biểu thức cũ, bao gồm các mức mặc định/phổ biến đã kiểm tra. Vùng dưới 25% chủ đích thay đổi; preset không bị viết lại hay migration.

Giữ nguyên công thức rim, ambient, emission, exposure, shading software và camera/AR. **Ambient vẫn phụ thuộc Key light qua công thức cũ**; chưa tách thành điều khiển riêng. Vì thế 0% tắt main softbox, không có nghĩa toàn bộ bàn phím đen, hay mọi nguồn còn lại bất biến khi kéo thanh. Không chỉnh màu/alpha/số lượng/chuyển động hạt, camera exposure, UI Settings hoặc các cờ review mặc định-off.

Các mode `legacy`/`forced-off` phục vụ chẩn đoán là nội bộ, không serialize vào preset. Chế độ production mặc định là 0; không cần bật cờ review để dùng đường đáp ứng mới.

## Bằng chứng Windows đã hoàn thành

- Source: [`8325ae81cb3463383d416c906eec099beda2326e`](https://github.com/lxmtuu/PianoPath/commit/8325ae81cb3463383d416c906eec099beda2326e).
- [Actions run 38841543462](https://github.com/lxmtuu/PianoPath/actions/runs/38841543462): job **keyboard-light SUCCESS**, **static SUCCESS**, **test SUCCESS**. Windows đã build C# và chạy native diagnostic, không phải ảnh synthetic từ Python.
- Publication commit: `216c78848f1f59b1d3240a31cf3d45781598ca50`.
- [Manifest](evidence/keyboard-key-light-v1/manifest.json.gz): 20 source hashes, 154 capture records; 10 PNG sheets / 60 panes ở [previews](previews/keyboard-key-light-v1). Tổng publication 966,087 byte; bản gốc đầy đủ nằm trong artifact `keyboard-key-light-native` của run, không chép vào Git.
- GPU: Windows WARP / **Microsoft Basic Render Driver**, 640×360, 114 captures. Software: native Cinematic keyboard band 640×128, 40 captures; không phải full UI screenshot.
- Chín mức: **0 / 1 / 10 / 25 / 50 / 70 / 92 / 100 / 200%**. Cùng seed/thời điểm cố định (frame 11), trạng thái nghỉ và giữ nốt 60 + 61.
- GPU có isolated / mixed / AR; software có isolated / mixed. Isolated tắt rim/emission/spill nhưng vẫn còn ambient. Mixed dùng tilt 38, shadow 46, AO 52, gloss 92, rim 62, emission 149; **không phải bản sao đầy đủ preset của người dùng**. Bloom tắt để giảm biến nhiễu.

Các kiểm tra native và kiểm chứng PNG đã qua:

1. 0% production bằng từng pixel với reference ép riêng đèn chính về 0, cả GPU và software.
2. GPU production bằng từng pixel với legacy ở các mốc **25 / 50 / 70 / 92 / 100 / 200%**.
3. Mean RGB của ROI tăng nghiêm ngặt qua chín mức, trong cả tám tổ hợp renderer/cảnh/trạng thái phím.
4. Raw simulation, actual key/sprite packets và AR packets giữ nguyên giữa các mode so sánh; phần ảnh ngoài bàn phím không đổi. Ảnh AR của fixture không đổi qua các mức đèn.
5. Python kiểm đủ ảnh gốc, source hashes, mean ROI tính lại và provenance của crop trước khi xuất publication. Local chỉ giữ subset crop lossless, không giả vờ đã tải đủ ảnh gốc.

### Số đo 0% từ ảnh native

Trung bình ba kênh RGB **đã encode** trong ROI: GPU `(0,240)–(640,360)`, software toàn band 640×128. Không phải lux, radiance tuyến tính, hay thước đo để so chéo renderer.

| Renderer / cảnh | Phím | Reference 0% | Production 0% |
|---|---|---:|---:|
| GPU isolated | nghỉ | legacy 63.8457 | 34.8911 |
| GPU isolated | giữ | legacy 63.4550 | 34.6067 |
| GPU mixed | nghỉ | legacy 66.4912 | 37.7106 |
| GPU mixed | giữ | legacy 69.6868 | 42.2039 |
| Software isolated | nghỉ | forced-off 59.4556 | 59.4556 |
| Software isolated | giữ | forced-off 59.4315 | 59.4315 |
| Software mixed | nghỉ | forced-off 81.2534 | 81.2534 |
| Software mixed | giữ | forced-off 84.2103 | 84.2103 |

Software không có lỗi floor GPU cũ; cặp bên trái của software là reference forced-off, không được gọi là “ảnh lỗi trước sửa”.

## Viewer và kiểm tra trình duyệt

```bash
# Cần Pillow. Output là cache ngoài Git, không phải thư mục publication.
python tools/serve_keyboard_key_light.py --output /path/to/cache/key-light --port 8782
# Hoặc chỉ xác minh/build trang, không mở server:
python tools/serve_keyboard_key_light.py --output /path/to/cache/key-light --verify-only
```

Viewer có tám tổ hợp GPU/software × isolated/mixed × nghỉ/giữ, cặp 0%, ramp 0/25/50/100/200 và AR 0/200. Ảnh lấy trực tiếp từ publication, không render lại hay dùng ảnh AI. Điều khiển chờ decode và bỏ kết quả cũ khi đổi lựa chọn nhanh.

Chromium thực đã PASS: hash BGRA của 10 sheets / 60 panes, 56 phần ảnh pair/ramp dẫn xuất, cả tám tổ hợp điều khiển, nhãn reference đúng, AR đúng trạng thái, viewport mobile 390px không tràn ngang, không page errors. Kiểm tra này chứng minh ảnh/controls trong viewer, **không phải GPU rendering hay performance của app**.

```bash
# Cần puppeteer-core; đặt CHROME_BIN hoặc cài @sparticuz/chromium.
node tools/check_keyboard_key_light_browser.cjs http://127.0.0.1:8782 /path/to/cache/review.png
```

Kiểm tra local cuối cùng: **140 Python tests PASS** (33.443 giây), static source check PASS, `git diff --check` PASS. Assertion ảnh synthetic trong unit test chỉ kiểm tra validator/viewer, không được dùng thay bằng chứng Windows phía trên.

## Giới hạn và việc chưa chốt

Tại thời điểm viết, job Windows **build/full regression vẫn đang chạy** trong cùng run; không gọi đây là full regression xanh và chưa xác nhận lại toàn bộ historical manifests sau lần rerender này. Không dùng các lượt bị supersede trước đó để tuyên bố full pass. Hai lượt focused trước đã render thành công nhưng vướng budget publication 700,000 byte; budget hiện 1,050,000 byte, không giảm chất lượng crop để lách kiểm tra.

Chưa kiểm app UI thực, realtime, các GPU vật lý, toàn bộ style/quality/camera pose, camera live hoặc preset đầy đủ trên máy người dùng. WARP và software native là bằng chứng cho fixture xác định, không thay cho bước đó.

Bước duyệt ảnh đối chiếu đã hoàn thành với phản hồi “tớ thấy ổn rồi”. Phần còn lại là theo dõi full regression và thử app Windows build mới với Key light 0 → 10 → 25 → 100. Kỳ vọng highlight đèn chính biến mất ở 0, phần còn lại có thể còn sáng do ambient/rim/emission. Chưa tự promote thay đổi thiết kế/particle khác; giữ Settings UI ở giai đoạn cuối đã thống nhất.
