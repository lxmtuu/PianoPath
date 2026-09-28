# Keyflow · Piano VFX Studio

Keyflow là ứng dụng desktop Windows viết bằng C# và WPF để học đàn theo MIDI. Sân khấu đen mặc định có nốt neon rơi theo thời gian, bàn phím 88 phím phát sáng và hạt sáng tại điểm nốt chạm phím. Có thể bật sao, guide, gradient hoặc chọn ảnh nền khi muốn; giao diện lấy cảm hứng từ piano visualizer và phần mềm học đàn, đây là ứng dụng riêng chứ không phải bản sao của Piano VFX, Synthesia hay Embers.

![Keyflow live piano stage](preview.png)

## Chạy ứng dụng

Yêu cầu Windows 10/11 và .NET 10 SDK. Mở PowerShell tại thư mục `PianoPath`:

```powershell
dotnet run --project .\PianoPath.csproj
```

Hoặc chạy bản đã biên dịch từ `bin\Debug\net10.0-windows\PianoPath.exe`.

## Bắt đầu sử dụng

1. Ứng dụng khởi động ở **Live Play**, không tự mở hoặc phát bài mẫu. Nhấn một phím trên piano, bàn phím máy tính hoặc đàn MIDI để chỉ hiện nốt vừa chơi.
2. SoundFont grand Yamaha được nạp tự động khi ứng dụng mở; nốt phím máy tính, piano ảo và MIDI sẽ phát tiếng ngay. Nút **HALL** bật/tắt tiếng vang phòng hòa nhạc. Dùng **LOAD SOUNDFONT** nếu muốn đổi sang tệp `.sf2` khác.
3. Chọn **OPEN MIDI** để mở bài `.mid` hoặc `.midi`. Việc nhấn phím không bắt đầu bài; bấm Play hoặc Space mới chạy các nốt trong MIDI theo playhead.
4. Khi bài chạy, nốt đi xuống và chạm đường sáng ngay trên phím đàn. Nhấn phím đàn để chơi cùng và nhận phản hồi đúng/sai.
5. Nếu đã cắm đàn MIDI khi mở ứng dụng, Keyflow tự kết nối ngõ vào đầu tiên tìm thấy. Khi cắm sau, mở ⚙ → **Refresh MIDI**; ứng dụng tự chọn và kết nối đàn mới. Nhãn xanh phía trên bàn phím đổi thành `MIDI IN · C4` khi nhận Note On. Mở ⚙ để chọn thiết bị vào/ra khác, chế độ tập, tempo, track, metronome, và điểm đầu/cuối vòng lặp.

Keyflow đi kèm YDP Grand Piano SF2 từ FreePats, dùng multisample Yamaha Disklavier Pro; preset grand acoustic được chọn sẵn. Engine phát stereo 44.1 kHz, phản hồi nốt nhấn/nhả và ba pedal MIDI, cùng hall reverb có thể bật/tắt. Có thể thay bằng SoundFont `.sf2` riêng. MIDI output là đường âm thanh bổ sung nếu chọn thiết bị MIDI ngoài.

### Điều khiển

- `A W S E D F T G Y H U J K`: nốt trắng/đen từ quãng giữa; nhấn giữ để duy trì nốt, thả ra để gửi Note Off.
- `Space`: phát/tạm dừng; `F11`: bật/tắt toàn màn hình; khi thanh công cụ đã ẩn, `Esc` hiện lại Menu/REC; khi thanh công cụ đang hiện, `Esc` mở/đóng bảng Stage Design. Không di chuột khoảng 2,8 giây thì thanh công cụ tự ẩn.
- Di chuyển chuột để hiện thanh công cụ, nút Menu và REC. REC lưu hình ảnh sân khấu thành AVI; đường tiếng không được trộn vào tệp.
- **OPEN MIDI**: nhập bài Standard MIDI format 0/1.
- Thanh thời gian: tua bài; **A**, **B**, **×**: đặt/xóa vòng lặp.
- Chế độ tập: Follow along, Wait for my note, Right hand only, Left hand only.
- Tempo: 50–150%; chọn track để lọc bài MIDI.

## Chức năng hiện có

- Giao diện WPF trình diễn toàn màn hình; menu và REC ẩn khi rảnh, hiện theo chuyển động chuột hoặc Esc. Piano roll custom-draw có 88 phím, nốt rơi live và MIDI, ánh sáng phím, sao nền, hạt/halo/lửa; REC quay sân khấu AVI.
- Bảng Stage Design có nhóm Scene, Notes, Particles, Camera, Audio và Practice. Bảng chọn màu HSV/HEX chỉnh màu đầu/cuối của nốt và màu halo; hỗ trợ ảnh nền PNG, JPEG, BMP, GIF và TIFF. Màu nốt, glow, viền, khúc xạ, bo góc, tốc độ rơi, emitter, hạt/physics, parallax, bloom và ánh sáng phím áp dụng tức thì, tự lưu và có nút lưu thủ công.
- Phím máy tính, click/giữ phím ảo và WinMM MIDI input; tự mở MIDI input đầu tiên, giữ lựa chọn sau khi làm mới danh sách, hiển thị tín hiệu/nốt MIDI nhận được; velocity và Note On/Off đi vào cùng luồng phản hồi.
- Ba pedal MIDI tiêu chuẩn: una corda/soft (CC 67, giảm gain và làm dịu âm), sostenuto (CC 66, giữ những nốt đang được nhấn khi pedal xuống), sustain/damper (CC 64, giữ tiếng sau khi nhả phím). Nút trên màn hình, pedal MIDI vào SoundFont, và MIDI output đều được kết nối; độ dài thanh nốt live chỉ tăng trong lúc phím được giữ, còn pedal chỉ tác động lên tiếng đàn.
- Tự nạp SoundFont Yamaha grand đi kèm; cho phép nạp SoundFont `.sf2` khác, liệt kê bank/program, chọn preset, đa âm và phát PCM stereo 44.1 kHz qua Windows `waveOut`.
- Hall reverb stereo chạy thời gian thực, bật mặc định, có nút bypass độc lập; reverb vẫn vang sau khi nhả phím.
- WinMM MIDI output để gửi Note On/Off tới synthesizer hoặc thiết bị MIDI đã chọn.
- Đọc MIDI format 0/1, tempo map, thời lượng/velocity nốt, lọc track, tua, tempo, loop A–B, metronome, chờ nốt và luyện riêng từng tay.
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

Tại thư mục `outputs`, chạy bộ xác minh tích hợp:

```powershell
dotnet run --project .\PianoPath.Tests\PianoPath.Tests.csproj
```

Bộ kiểm thử tạo tệp MIDI, SoundFont SF2 và AVI nhỏ trong thư mục tạm; kiểm tra parser MIDI, map tempo, giải mã preset/zone/sample, tín hiệu âm thanh và Note Off, sustain/sostenuto/soft, lưu và clamp cấu hình hiệu ứng, ghi AVI frame, đóng/mở MIDI input thật nếu có, giải mã WinMM `MIM_DATA` tới nốt rơi WPF, MIDI output, ẩn/hiện thanh công cụ, độ dài nốt khi giữ phím, chế độ tập, loop, tua, tempo và render WPF. Chỉ xác nhận được phím đàn vật lý phát sự kiện khi nhấn một phím MIDI thực tế.

## Cấu trúc chính

- `MainWindow.xaml`: giao diện, bảng cài đặt và các điều khiển.
- `MainWindow.xaml.cs`: điều phối playback, MIDI, lựa chọn preset và chế độ luyện.
- `PianoVisualSettings.cs`: thông số sân khấu lưu JSON trong LocalAppData; `AviVideoRecorder.cs`: ghi frame AVI bằng Windows Video for Windows.
- `PianoStage.cs`: vẽ bàn phím, nốt rơi, vùng chạm và hạt sáng.
- `PianoAudioEngine.cs`: đầu ra PCM `waveOut`, quản lý luồng phát và preset.
- `SoundFontSynthesizer.cs`: đọc vùng mẫu `.sf2` và kết xuất tiếng theo MIDI note.
- `MidiFileReader.cs`, `MidiDeviceService.cs`: nhập bài MIDI và kết nối thiết bị WinMM.
- `PianoPath.Tests`: bộ kiểm tra hồi quy và fixtures tự tạo.
