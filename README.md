# Keyflow · Piano VFX Studio

Keyflow là ứng dụng desktop Windows viết bằng C# và WPF để học đàn theo MIDI. Sân khấu đen mặc định có nốt neon rơi theo thời gian, bàn phím 88 phím phát sáng và hạt sáng tại điểm nốt chạm phím. Có thể bật sao, guide, gradient hoặc chọn ảnh nền khi muốn; giao diện lấy cảm hứng từ piano visualizer và phần mềm học đàn, đây là ứng dụng riêng chứ không phải bản sao của Piano VFX, Synthesia hay Embers.

![Keyflow live piano stage](preview.png)

## Chạy ứng dụng

Yêu cầu Windows 10/11, .NET 10 SDK và **Git LFS** (SoundFont `Assets/ConcertGrand.sf2` ~113 MiB được lưu bằng LFS). Mở PowerShell:

```powershell
git lfs install
git clone https://github.com/lxmtuu/PianoPath.git
cd PianoPath
git lfs pull            # bỏ qua nếu clone đã tải sẵn file .sf2 thật (khoảng 113 MiB, không phải file 134 byte)
dotnet run --project .\PianoPath.csproj
```

Hoặc chạy bản đã biên dịch từ `bin\Debug\net10.0-windows\PianoPath.exe`. Nếu `Assets\ConcertGrand.sf2` chỉ là con trỏ LFS (vài trăm byte), ứng dụng vẫn mở nhưng báo không nạp được SoundFont; hãy chạy `git lfs pull` hoặc nạp một tệp `.sf2` khác bằng **LOAD SOUNDFONT**.

## Bắt đầu sử dụng

1. Ứng dụng khởi động ở **Live Play**, không tự mở hoặc phát bài mẫu. Nhấn một phím trên piano, bàn phím máy tính hoặc đàn MIDI để chỉ hiện nốt vừa chơi.
2. SoundFont grand Yamaha được nạp tự động khi ứng dụng mở; nốt phím máy tính, piano ảo và MIDI sẽ phát tiếng ngay. Nút **HALL** bật/tắt tiếng vang phòng hòa nhạc. Dùng **LOAD SOUNDFONT** nếu muốn đổi sang tệp `.sf2` khác.
3. Chọn **OPEN MIDI** để mở bài `.mid` hoặc `.midi`. Việc nhấn phím không bắt đầu bài; bấm Play hoặc Space mới chạy các nốt trong MIDI theo playhead.
4. Khi bài chạy, nốt đi xuống và chạm đường sáng ngay trên phím đàn. Nhấn phím đàn để chơi cùng và nhận phản hồi đúng/sai.
5. Nếu đã cắm đàn MIDI khi mở ứng dụng, Keyflow tự kết nối ngõ vào đầu tiên tìm thấy. Khi cắm sau, mở ⚙ → **Refresh MIDI**; ứng dụng tự chọn và kết nối đàn mới. Nhãn xanh phía trên bàn phím đổi thành `MIDI IN · C4` khi nhận Note On. Mở ⚙ để chọn thiết bị vào/ra khác, chế độ tập, tempo, track, metronome, và điểm đầu/cuối vòng lặp.

Keyflow đi kèm YDP Grand Piano SF2 từ FreePats, dùng multisample Yamaha Disklavier Pro; preset grand acoustic được chọn sẵn. Engine phát stereo 44.1 kHz, phản hồi nốt nhấn/nhả và ba pedal MIDI, cùng hall reverb có thể bật/tắt. Có thể thay bằng SoundFont `.sf2` riêng. MIDI output là đường âm thanh bổ sung nếu chọn thiết bị MIDI ngoài.

### Điều khiển

- `A W S E D F T G Y H U J K`: nốt trắng/đen từ quãng giữa; nhấn giữ để duy trì nốt, thả ra để gửi Note Off.
- `Space`: phát/tạm dừng; `F11`: bật/tắt toàn màn hình; `Esc`: mở/đóng bảng cài đặt chính (Stage Design) bất kể thanh công cụ đang ẩn hay hiện.
- Không di chuột khoảng 2,8 giây thì toàn bộ giao diện (thanh công cụ trên/dưới, Menu, REC và cả bảng Stage Design nếu đang mở) tự ẩn, chỉ còn đàn, nền và nốt đang chạy. Di chuyển chuột để hiện lại đúng những gì vừa ẩn. Giao diện không tự ẩn khi đang kéo slider, mở danh sách chọn hoặc đang mở bảng chọn màu; gõ phím trong bảng cài đặt cũng được tính là đang thao tác.
- REC lưu hình ảnh sân khấu thành AVI (20 fps, canh theo đồng hồ thật); đường tiếng không được trộn vào tệp. Máy không có codec MJPEG thì ghi RGB không nén và tự dừng khi chạm giới hạn 2 GB của định dạng AVI.
- **OPEN MIDI**: nhập bài Standard MIDI format 0/1.
- Thanh thời gian: tua bài; **A**, **B**, **×**: đặt/xóa vòng lặp.
- Chế độ tập: Follow along, Wait for my note, Right hand only, Left hand only.
- Tempo: 50–150%; chọn track để lọc bài MIDI.

## Chức năng hiện có

- Giao diện WPF trình diễn toàn màn hình; toàn bộ menu, thanh công cụ, REC và bảng cài đặt ẩn khi chuột đứng yên, hiện lại khi di chuột; Esc mở/đóng bảng Stage Design. Piano roll custom-draw có 88 phím, nốt rơi live và MIDI, ánh sáng phím, sao nền, hạt/halo/lửa; REC quay sân khấu AVI.
- Bảng Stage Design có nhóm Scene, Notes, Particles, Camera, Audio và Practice. Bảng chọn màu HSV/HEX chỉnh màu đầu/cuối của nốt và màu halo; hỗ trợ ảnh nền PNG, JPEG, BMP, GIF và TIFF. Màu nốt, glow, viền, khúc xạ, bo góc, tốc độ rơi, emitter, hạt/physics, parallax, bloom và ánh sáng phím áp dụng tức thì, tự lưu và có nút lưu thủ công.
- Phím máy tính, click/giữ phím ảo và WinMM MIDI input; tự mở MIDI input đầu tiên, giữ lựa chọn sau khi làm mới danh sách, hiển thị tín hiệu/nốt MIDI nhận được; velocity và Note On/Off đi vào cùng luồng phản hồi.
- Ba pedal MIDI tiêu chuẩn: una corda/soft (CC 67, giảm gain và làm dịu âm), sostenuto (CC 66, giữ những nốt đang được nhấn khi pedal xuống), sustain/damper (CC 64, giữ tiếng sau khi nhả phím). Nút trên màn hình, pedal MIDI vào SoundFont, và MIDI output đều được kết nối; độ dài thanh nốt live chỉ tăng trong lúc phím được giữ, còn pedal chỉ tác động lên tiếng đàn.
- Tự nạp SoundFont Yamaha grand đi kèm; cho phép nạp SoundFont `.sf2` khác, liệt kê bank/program, chọn preset, đa âm và phát PCM stereo 44.1 kHz qua Windows `waveOut`.
- Hall reverb stereo chạy thời gian thực, bật mặc định, có nút bypass độc lập; reverb vẫn vang sau khi nhả phím.
- WinMM MIDI output để gửi Note On/Off tới synthesizer hoặc thiết bị MIDI đã chọn.
- Đọc MIDI format 0/1, tempo map, nhịp (time signature), tên track, thời lượng/velocity nốt (bỏ qua kênh trống 10), lọc track, tua, tempo, loop A–B, metronome theo tempo map của bài (nhấn mạnh phách đầu ô nhịp), chờ nốt và luyện riêng từng tay.
- Tua, đổi chế độ/track hoặc lặp A–B không tính các nốt đã bỏ qua là miss; mỗi vòng lặp A–B được chấm lại từ đầu.
- Thống kê hit/miss, accuracy, streak và phản hồi đúng thời điểm.

## Giới hạn hiện tại

- SoundFont mặc định là YDP Grand Piano của FreePats, xây từ multisample Yamaha Disklavier Pro. Xem `Assets/ATTRIBUTION.txt` để biết tác giả, nguồn và giấy phép CC BY 3.0. File SF2 khoảng 113 MiB.

- Âm thanh nội bộ đọc SoundFont 2 (`.sf2`) không nén. `.sf3` chưa được hỗ trợ; một số phần SF2 nâng cao như modulators, bộ lọc nhạc cụ và hiệu ứng reverb/chorus chưa được tái tạo đầy đủ. Âm thanh vì vậy có thể khác các synthesizer SoundFont chuyên dụng, tùy tệp.
- Bài MIDI định dạng 2, SMPTE time division, MusicXML, sheet music, quản lý thư viện bài và lưu lịch sử luyện tập chưa có.
- Phân tách tay dùng ranh giới C4 (MIDI 60); đây là quy tắc đơn giản, không suy luận cách chia tay từ bản nhạc.
- MIDI input vật lý cần đàn/thiết bị tương thích được Windows nhận diện. Nếu chưa cắm thiết bị, dùng bàn phím máy tính hoặc piano ảo.
- Dùng WinMM nên phiên bản này dành cho Windows.
- Video REC là AVI hình ảnh sân khấu, chưa trộn âm thanh đàn; máy không có codec MJPEG thì tệp dùng RGB không nén và dung lượng sẽ tăng nhanh hơn.

## Kiểm thử

Bộ xác minh tích hợp nằm trong `VerificationSuite.cs` và chạy ngay bằng chính ứng dụng (cần Windows vì khởi động WPF thật):

```powershell
dotnet run --project .\PianoPath.csproj -- --verify
# tùy chọn: --verify-log=C:\duong-dan\ket-qua.log (mặc định %TEMP%\keyflow-verification.log)
```

Mã thoát `0` là đạt, `1` là có lỗi; nhật ký ghi từng mục PASS/FAIL. Bộ kiểm thử tạo tệp MIDI, SoundFont SF2 và AVI nhỏ trong thư mục tạm; kiểm tra parser MIDI (đa track, tempo map, lưới phách, tên track, bỏ kênh trống, tệp hỏng), giải mã preset/zone/sample và ngữ nghĩa generator SF2 (instrument ghi đè, preset cộng dồn), loop qua giai đoạn release, giới hạn đa âm, tín hiệu âm thanh và Note Off, sustain/sostenuto/soft, lưu và clamp cấu hình hiệu ứng, ghi AVI frame, đóng/mở MIDI input thật nếu có, giải mã WinMM `MIM_DATA` tới nốt rơi WPF, MIDI output, tự ẩn/hiện giao diện theo chuột và Esc, độ dài nốt khi giữ phím, chế độ tập, loop, tua, tempo và render WPF. Chỉ xác nhận được phím đàn vật lý phát sự kiện khi nhấn một phím MIDI thực tế.

Các tham số dòng lệnh khác: `--show-settings [--settings-tab=notes|particles|camera|audio|practice]` mở sẵn bảng cài đặt, `--snapshot <file.png> [--compact] [--play-preview]` chụp màn hình rồi thoát (dùng để tạo `preview.png`/`settings-preview.png`).

## Cấu trúc chính

- `MainWindow.xaml`: giao diện, bảng cài đặt và các điều khiển.
- `MainWindow.xaml.cs`: điều phối playback, chấm điểm, MIDI, lựa chọn preset, chế độ luyện, ẩn/hiện giao diện và ghi video.
- `PianoVisualSettings.cs`: thông số sân khấu lưu JSON trong LocalAppData; `AviVideoRecorder.cs`: ghi frame AVI bằng Windows Video for Windows.
- `PianoStage.cs`: vẽ bàn phím, nốt rơi, vùng chạm và hạt sáng.
- `PianoAudioEngine.cs`: đầu ra PCM `waveOut`, luồng phát và hall reverb.
- `SoundFontSynthesizer.cs`: đọc vùng mẫu `.sf2` (generator theo đặc tả SF2) và kết xuất tiếng theo MIDI note.
- `MidiFileReader.cs`: đọc Standard MIDI File thành nốt, tempo map, lưới phách và tên track; `MidiDeviceService.cs`: kết nối thiết bị WinMM.
- `ColorPickerWindow.cs`: bảng chọn màu HSV/HEX.
- `VerificationSuite.cs`: bộ kiểm tra hồi quy chạy bằng `--verify`, fixtures tự tạo.
- `.github/workflows/build.yml`: CI biên dịch trên `windows-latest`.

## Giấy phép

Mã nguồn phát hành theo giấy phép MIT (xem `LICENSE`). SoundFont đi kèm thuộc FreePats, giấy phép CC BY 3.0 (xem `Assets/ATTRIBUTION.txt`).
