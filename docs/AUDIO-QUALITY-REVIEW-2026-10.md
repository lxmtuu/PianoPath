# Rà soát âm thanh và hướng nâng chất tiếng đàn — 2026-10-18

## Kết luận nhanh

PianoPath đã có nền tảng âm thanh tốt cho một ứng dụng luyện đàn/visualizer tự chứa: Salamander Grand Piano V3 dạng SFZ tải riêng (~748 MB), phân lớp lực nhấn, sample release/pedal, mô hình half-pedal, cộng hưởng dây khi đạp sustain, nhiều đường xuất WASAPI/ASIO và reverb thuật toán hoặc convolution. Tuy nhiên, đây **không phải** Garritan CFX hay Pianoteq 9: hai sản phẩm đó dựa trên thư viện/engine riêng và cách thu/mô hình hóa riêng. Không thể đạt tương đương chỉ bằng vài hiệu ứng; yếu tố quyết định trước tiên vẫn là chất lượng và quyền sử dụng nguồn nhạc cụ.

Đợt này đã bổ sung một điều khiển hữu ích, an toàn và nghe được ngay: **Reverb amount 0–150%**, mặc định 100% để giữ nguyên tiếng cũ. Thanh trượt chỉnh wet return trực tiếp, không khởi động lại ASIO/WASAPI và không đổi tệp impulse; 0% là khô, còn 100% giữ mức mặc định của từng phòng. Thiết lập này lưu trên máy, không bị preset giao diện hoặc mã chia sẻ đổi hộ.

## Những gì repo đang có

- `Audio/SalamanderPiano.cs`, `SfzReader.cs`, `SfzSynthesizer.cs`: cài đàn Salamander Grand Piano V3, SFZ velocity regions, round-robin, sample release/pedal-noise và phản ứng damper liên tục. Đây là **sampled piano**, không phải mô hình vật lý kiểu Pianoteq.
- `Audio/PianoAudioEngine.cs`: pipeline tổng hợp → cộng hưởng dây → reverb → gain/soft limiter; lệnh MIDI được xếp hàng để callback thiết bị không chờ synth. Thu âm lấy tín hiệu trước mute đầu ra.
- `Audio/StringResonance.cs`: sympathetic resonance khi pedal sustain mở; `ConvolutionReverb.cs` và `StereoHallReverb` cho hall, concert hall, soundboard và impulse WAV/FLAC tự chọn.
- `Audio/WasapiAudioSink.cs`, `AsioAudioSink.cs`, `WaveOutAudioSink.cs`: nhiều backend, điều khiển latency và fallback; giao diện báo đường ra thực tế.
- `Ui/MainWindow.xaml` / `Ui/MainWindow.xaml.cs`: chọn đàn, phòng, output, độ trễ, mức tổng, mute và MIDI. Các điều khiển mới nằm trong trang **Sound & MIDI → Audio settings**.
- Điểm cần lưu ý: engine render nội bộ ở 44,1 kHz; source Salamander được mô tả là 48 kHz/24-bit. Chất lượng cuối còn phụ thuộc sample map, cách bank được đóng gói, driver, thiết bị phát và headphone/loa.

## Thứ tự cải tiến đề xuất

| Ưu tiên | Hướng làm | Vì sao / cách kiểm chứng |
|---|---|---|
| **P1 — Instrument library** | Cho phép thêm/chọn bank SFZ khác (và metadata/tên/credit riêng) thay vì chỉ một đàn cài từ Salamander; chuẩn hoá kiểm tra path, sample thiếu, key/velocity map và giấy phép. | Đây là bước tạo khác biệt lớn nhất về màu đàn. Cần thử bank hợp pháp, có đủ dải phím/velocity và sample stereo/pedal phù hợp trước khi thiết kế mixer; không đóng gói thư viện bên thứ ba nếu chưa có quyền. |
| **P1 — Đường động lực** | Thêm lựa chọn velocity curve (linear / soft / hard / custom) và đồ thị nhỏ, giữ velocity gốc cho chọn velocity-layer rồi chỉ điều chỉnh biểu cảm amp. | Giúp đàn MIDI nhẹ/phím nặng điều khiển được pp–ff tự nhiên. Kiểm bằng sweep 1–127, đo loudness theo từng velocity, nghe nốt đơn + hợp âm; tránh làm mất ranh giới velocity layer. |
| **P1 — Listening/benchmark workflow** | Tạo bộ MIDI ngắn có pp–ff, sostenuto, half-pedal, pedal lift, bass/treble và hợp âm dày; render offline/WAV và checklist nghe A/B trên cùng mức loudness. | Phát hiện click, clipping, chuyển layer gắt, release quá ngắn, bass đục và reverb lấn. So sánh cùng mức âm lượng; nếu không, bản to hơn dễ bị tưởng là hay hơn. |
| **P2 — Piano perspective** | Sau khi xác minh bank: thử lựa chọn close / player / audience bằng pan, stereo width và mức ambience; có bypass/mono check. | Thay đổi hình ảnh stereo có thể nâng cảm giác “ngồi trước đàn”, nhưng không nên giả lập mic placement khi nguồn chỉ mono hoặc bản quyền/metadata không rõ. |
| **P2 — Tinh chỉnh cộng hưởng/pedal** | Mở mức cộng hưởng dây, độ dài release hoặc ngưỡng half-pedal thành control nâng cao sau khi có listening test. | Engine đã có mô hình liên quan; thêm slider sớm dễ tạo tiếng ngân giả hoặc tăng CPU. Cần đo tiếng đuôi và test nhiều controller pedal trước. |
| **P3 — Tone controls** | EQ tối giản (low / presence / high) hoặc tuning/A4, preset “recital / close / practice”, ưu tiên xử lý ít trễ. | Thêm sau khi xác lập mức gain staging và workflow đo; tránh biến master gain thành EQ/limiter khó đoán. |

### Giới hạn so sánh và kỳ vọng

- Garritan CFX là nguồn/engine thương mại; Pianoteq 9 là engine modeling thương mại. Dự án không có quyền truy cập model, sample hay benchmark nội bộ của hai bên. Mục tiêu thực tế nên là **piano SFZ miễn phí nghe tự nhiên, có độ động tốt, ổn định độ trễ thấp và dễ tinh chỉnh**, không tuyên bố tương đương âm thanh.
- Cần ưu tiên bank piano hợp pháp chất lượng cao, kiểm soát velocity response và nghe A/B có chuẩn hoá loudness trước khi thêm nhiều FX.
- Không nên tăng gain mặc định để tạo cảm giác “hay hơn”; giữ headroom và limiter chỉ là lớp bảo vệ, không thay thế mastering.

## Thay đổi đã thực hiện trong đợt rà soát

- Thêm `AudioReverbAmount` (0–150%, mặc định 100) vào cài đặt máy; loại khỏi preset/share để áp dụng một look không đổi âm thanh của người dùng.
- Thêm `IStereoReverb.Amount`; cả hall và convolution cập nhật wet return live, không rebuild pipeline. Với hall, dry gain đi về unity khi amount là 0; mức 100 giữ nguyên hệ số trước đây.
- Thêm thanh trượt, nhãn phần trăm, bản dịch tiếng Việt và wiring lưu cài đặt; cập nhật preset generator để mọi preset vẫn đầy đủ schema.
- Bổ sung kiểm tra mix convolution 0% / 50% và kiểm tra clamp/wiring trong verification suite.

## Kiểm chứng

- `python3 tools/check_sources.py` — đạt: nguồn, XAML, dịch, preset sinh tự động và project XML đều sạch.
- `git diff --check` — đạt.
- Không chạy được `dotnet test` trong môi trường này vì không có lệnh `dotnet`; cần chạy test/build trên Windows SDK trước khi phát hành.