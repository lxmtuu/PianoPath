# Keyflow · Piano VFX Studio

Keyflow là ứng dụng desktop Windows viết bằng C# và WPF để học đàn và làm video piano theo MIDI. Sân khấu đen mặc định có nốt neon rỗng rơi theo thời gian, bàn phím 88 phím phát sáng theo màu nốt, tia lửa, vòng sóng và lửa tại điểm nốt chạm phím. Bảng cài đặt chia thành 10 trang (Style, Notes, Particles, Keyboard, Background, Camera & FX, Audio, MIDI, Practice, Recording) với hệ preset có sẵn (Neon Violet, Inferno, Aurora Rainbow, Ice Crystal, Two Hands, Classic Roll, Green Screen) và preset người dùng lưu/nhập/xuất JSON. Giao diện lấy cảm hứng từ các MIDI visualizer như Piano VFX, Keysight và Embers nhưng đây là ứng dụng riêng, không phải bản sao.

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
- `Space`: phát/tạm dừng; `F11`: bật/tắt toàn màn hình; `Esc`: mở/đóng dock cài đặt bất kể thanh công cụ đang ẩn hay hiện (nếu đang gõ trong ô tìm kiếm, `Esc` xóa nội dung tìm trước).
- Không di chuột khoảng 2,8 giây thì toàn bộ giao diện (thanh công cụ trên/dưới, Menu, REC và cả dock cài đặt nếu đang mở) tự ẩn, chỉ còn đàn, nền và nốt đang chạy. Di chuyển chuột để hiện lại đúng những gì vừa ẩn. Giao diện không tự ẩn khi đang kéo slider, mở danh sách chọn hoặc đang mở bảng chọn màu; gõ phím trong bảng cài đặt cũng được tính là đang thao tác.
- REC lưu hình ảnh sân khấu thành AVI (độ phân giải theo cửa sổ/720p/1080p và 15–60 fps chọn trong trang Recording, canh theo đồng hồ thật); đường tiếng không được trộn vào tệp. Preset **Green Screen** tô nền xanh lá thuần để key trong OBS. Máy không có codec MJPEG thì ghi RGB không nén và tự dừng khi chạm giới hạn 2 GB của định dạng AVI.
- **OPEN MIDI**: nhập bài Standard MIDI format 0/1.
- Thanh thời gian: tua bài; **A**, **B**, **×**: đặt/xóa vòng lặp.
- Chế độ tập: Follow along, Wait for my note, Right hand only, Left hand only.
- Tempo: 50–150%; chọn track để solo, hoặc tắt tiếng/đổi màu từng track trong trang MIDI.

## Chức năng hiện có

- Giao diện WPF trình diễn toàn màn hình với theme tối dùng chung (`App.xaml`): header (bài đang mở, preset đang dùng, OPEN MIDI, SETTINGS, thu nhỏ/toàn màn hình/thoát), footer transport (Play, quay lại, thời gian, pedal, accuracy/score/streak, thanh tua) và dock cài đặt toàn chiều cao bên phải. Toàn bộ menu, thanh công cụ, REC và dock ẩn khi chuột đứng yên, hiện lại khi di chuột; Esc mở/đóng dock.
- Dock cài đặt có 10 trang với điều hướng bên trái, ô tìm kiếm lọc mọi trang, mỗi thông số có slider + ô nhập số (chấp nhận đơn vị, dấu phẩy, tên nốt như `C4` cho điểm chia tay) + nút reset; các hàng phụ thuộc tự ẩn/hiện theo lựa chọn (ví dụ màu tay trái/phải chỉ hiện ở chế độ Left / right hand). Mọi thay đổi áp dụng tức thì, tự lưu sau 0,65 s và có nút SAVE / RESET PAGE.
- **Style**: preset có sẵn Neon Violet (mặc định, nốt neon rỗng viền sáng + lửa), Inferno (nốt lửa cháy có vân ember, phím đỏ, bloom ấm), Aurora Rainbow (màu cầu vồng theo cao độ + wisps khói bốc lên), Ice Crystal (nốt kính lạnh), Two Hands (xanh tay trái / hồng tay phải), Classic Roll (piano roll đặc, nhẹ máy), Green Screen (nền xanh chroma key). Preset người dùng lưu trong `%LOCALAPPDATA%\Keyflow\presets\*.json`, có SAVE AS / DELETE / IMPORT / EXPORT; header hiển thị tên preset và dấu `*` khi đã chỉnh sửa.
- **Notes**: chế độ màu Gradient theo cao độ (6 palette hoặc màu đầu/cuối tùy chỉnh), theo tay (điểm chia tuỳ chỉnh), theo track MIDI (bảng 8 màu), cầu vồng theo cao độ hoặc đổi màu theo thời gian; kiểu nốt Solid / Neon outline / Glass / Fire (vân cháy animation bằng opacity mask nhiễu); độ rộng, bo góc, độ dài tối thiểu, khe hở giữa nốt, đổ bóng 3D, tên nốt in trên thanh, tint, bloom, độ sáng và độ dày viền, glow cạnh trước, khúc xạ, tốc độ rơi.
- **Particles**: tia lửa (emitter + physics: gravity, drag, vector field…), wisps plasma bốc lên từ phím đang giữ (mật độ, tốc độ, chiều cao, độ rộng, nhiễu loạn, glow), lửa (cường độ, chiều cao, màu ấm hoặc theo nốt, cháy tiếp khi giữ phím rồi tắt dần) và vòng sóng va chạm.
- **Keyboard**: kiểu Classic / Studio 3D / Glass, chiều cao, độ dài phím đen, nhãn phím (không/C/tất cả), bóng nắp đàn, dải nỉ đỏ; phím sáng theo màu nốt hoặc màu cố định, cường độ, bán kính hào quang lan lên trên, độ lún khi nhấn. Phím của nốt MIDI đang phát cũng sáng, không chỉ phím người chơi nhấn.
- **Background**: nền màu đặc / ảnh (PNG, JPEG, BMP, GIF, TIFF + làm tối) / Green screen; aura gradient, sao (mật độ), guide lanes, vignette, horizon glow, cường độ light beam; đường halo và màu halo. **Camera & FX**: parallax, zoom, khung hình, saturation, contrast, bloom. **Recording**: độ phân giải, fps, thông tin tệp đầu ra.
- Phím máy tính, click/giữ phím ảo và WinMM MIDI input; tự mở MIDI input đầu tiên, giữ lựa chọn sau khi làm mới danh sách, hiển thị tín hiệu/nốt MIDI nhận được; velocity và Note On/Off đi vào cùng luồng phản hồi.
- Ba pedal MIDI tiêu chuẩn: una corda/soft (CC 67, giảm gain và làm dịu âm), sostenuto (CC 66, giữ những nốt đang được nhấn khi pedal xuống), sustain/damper (CC 64, giữ tiếng sau khi nhả phím). Nút trên màn hình, pedal MIDI vào SoundFont, và MIDI output đều được kết nối; độ dài thanh nốt live chỉ tăng trong lúc phím được giữ, còn pedal chỉ tác động lên tiếng đàn.
- Tự nạp SoundFont Yamaha grand đi kèm; cho phép nạp SoundFont `.sf2` khác, liệt kê bank/program, chọn preset, đa âm và phát PCM stereo 44.1 kHz qua Windows `waveOut`.
- Hall reverb stereo chạy thời gian thực, bật mặc định, có nút bypass độc lập; reverb vẫn vang sau khi nhả phím.
- WinMM MIDI output để gửi Note On/Off tới synthesizer hoặc thiết bị MIDI đã chọn.
- Đọc MIDI format 0/1, tempo map, nhịp (time signature), tên track, thời lượng/velocity nốt (bỏ qua kênh trống 10), solo/mute và đổi màu từng track, tua, tempo, loop A–B, metronome theo tempo map của bài (nhấn mạnh phách đầu ô nhịp), chờ nốt và luyện riêng từng tay.
- Tua, đổi chế độ/track hoặc lặp A–B không tính các nốt đã bỏ qua là miss; mỗi vòng lặp A–B được chấm lại từ đầu.
- Thống kê hit/miss, accuracy, streak và phản hồi đúng thời điểm.

## Giới hạn hiện tại

- SoundFont mặc định là YDP Grand Piano của FreePats, xây từ multisample Yamaha Disklavier Pro. Xem `Assets/ATTRIBUTION.txt` để biết tác giả, nguồn và giấy phép CC BY 3.0. File SF2 khoảng 113 MiB.

- Âm thanh nội bộ đọc SoundFont 2 (`.sf2`) không nén. `.sf3` chưa được hỗ trợ; một số phần SF2 nâng cao như modulators, bộ lọc nhạc cụ và hiệu ứng reverb/chorus chưa được tái tạo đầy đủ. Âm thanh vì vậy có thể khác các synthesizer SoundFont chuyên dụng, tùy tệp.
- Bài MIDI định dạng 2, SMPTE time division, MusicXML, sheet music, quản lý thư viện bài và lưu lịch sử luyện tập chưa có.
- Phân tách tay dùng một điểm chia cố định (mặc định C4 = MIDI 60, đổi được trong Notes → Hand split point, dùng chung cho màu theo tay và chế độ tập từng tay); ứng dụng không suy luận cách chia tay từ bản nhạc.
- MIDI input vật lý cần đàn/thiết bị tương thích được Windows nhận diện. Nếu chưa cắm thiết bị, dùng bàn phím máy tính hoặc piano ảo.
- Dùng WinMM nên phiên bản này dành cho Windows.
- Video REC là AVI hình ảnh sân khấu, chưa trộn âm thanh đàn; máy không có codec MJPEG thì tệp dùng RGB không nén và dung lượng sẽ tăng nhanh hơn. Chưa xuất MP4 trực tiếp.
- Chưa có lớp phủ webcam/bàn tay như trong một số video Piano VFX; hãy dùng preset Green Screen và ghép trong OBS. Góc nhìn phối cảnh 3D (perspective) chưa hỗ trợ; sân khấu là piano roll phẳng với parallax nhẹ.

## Kiểm thử

Bộ xác minh tích hợp nằm trong `VerificationSuite.cs` và chạy ngay bằng chính ứng dụng (cần Windows vì khởi động WPF thật):

```powershell
dotnet run --project .\PianoPath.csproj -- --verify
# tùy chọn: --verify-log=C:\duong-dan\ket-qua.log (mặc định %TEMP%\keyflow-verification.log)
```

Mã thoát `0` là đạt, `1` là có lỗi; nhật ký ghi từng mục PASS/FAIL. Bộ kiểm thử tạo tệp MIDI, SoundFont SF2 và AVI nhỏ trong thư mục tạm; kiểm tra parser MIDI (đa track, tempo map, lưới phách, tên track, bỏ kênh trống, tệp hỏng), giải mã preset/zone/sample và ngữ nghĩa generator SF2 (instrument ghi đè, preset cộng dồn), loop qua giai đoạn release, giới hạn đa âm, tín hiệu âm thanh và Note Off, sustain/sostenuto/soft, lưu và clamp cấu hình hiệu ứng, preset có sẵn/preset người dùng (lưu, nhập, xuất, xóa, tệp hỏng), dock cài đặt (10 trang, chế độ màu theo tay/track, hàng phụ thuộc, tìm kiếm, áp preset), ghi AVI frame, đóng/mở MIDI input thật nếu có, giải mã WinMM `MIM_DATA` tới nốt rơi WPF, MIDI output, tự ẩn/hiện giao diện theo chuột và Esc, độ dài nốt khi giữ phím, chế độ tập, loop, tua, tempo và render WPF. Chỉ xác nhận được phím đàn vật lý phát sự kiện khi nhấn một phím MIDI thực tế.

Các tham số dòng lệnh khác: `--show-settings [--settings-tab=style|notes|particles|keyboard|background|camera|audio|midi|practice|recording]` mở sẵn dock cài đặt, `--snapshot <file.png> [--compact] [--play-preview]` chụp màn hình rồi thoát (dùng để tạo `preview.png`/`settings-preview.png`).

## Cấu trúc chính

- `App.xaml`: theme tối dùng chung (button, switch, slider, combo, textbox, scrollbar, tab điều hướng, danh sách preset).
- `MainWindow.xaml`: bố cục header / sân khấu + dock cài đặt / footer transport và các trang tĩnh (Audio, MIDI, Practice, Recording).
- `MainWindow.xaml.cs`: điều phối playback, chấm điểm, MIDI, chế độ luyện, ẩn/hiện giao diện và ghi video; `MainWindow.Settings.cs`: sinh các trang cài đặt (card, slider + ô nhập, switch, combo, màu), tìm kiếm, preset, danh sách track, tuỳ chọn ghi hình.
- `PianoVisualSettings.cs`: thông số sân khấu lưu JSON trong LocalAppData (có migration); `VisualPresets.cs`: preset có sẵn và kho preset người dùng; `TextPromptWindow.cs`: hộp thoại đặt tên preset; `AviVideoRecorder.cs`: ghi frame AVI bằng Windows Video for Windows.
- `PianoStage.cs`: vẽ nền/vignette/beam, nốt theo 4 kiểu và 5 chế độ màu, tia lửa, wisps, lửa, vòng sóng, bàn phím 3 kiểu với ánh sáng phím.
- `PianoAudioEngine.cs`: đầu ra PCM `waveOut`, luồng phát và hall reverb.
- `SoundFontSynthesizer.cs`: đọc vùng mẫu `.sf2` (generator theo đặc tả SF2) và kết xuất tiếng theo MIDI note.
- `MidiFileReader.cs`: đọc Standard MIDI File thành nốt, tempo map, lưới phách và tên track; `MidiDeviceService.cs`: kết nối thiết bị WinMM.
- `ColorPickerWindow.cs`: bảng chọn màu HSV/HEX.
- `VerificationSuite.cs`: bộ kiểm tra hồi quy chạy bằng `--verify`, fixtures tự tạo.
- `.github/workflows/build.yml`: CI biên dịch trên `windows-latest`.

## Giấy phép

Mã nguồn phát hành theo giấy phép MIT (xem `LICENSE`). SoundFont đi kèm thuộc FreePats, giấy phép CC BY 3.0 (xem `Assets/ATTRIBUTION.txt`).
