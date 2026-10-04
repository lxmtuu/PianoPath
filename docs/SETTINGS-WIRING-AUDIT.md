# Kiểm tra liên kết logic của mọi chức năng cài đặt
> **Cập nhật sau tài liệu này:** thêm trang **General** (ngôn ngữ giao diện) và trang **History** (lịch sử luyện tập) nên danh mục là **13 trang / 4 nhóm**; bảng đối chiếu setting ↔ code bên dưới vẫn đúng từng dòng vì hai trang mới không thêm thông số sân khấu nào.


Kết quả rà soát toàn bộ repo: **mọi chức năng cài đặt (UI) đều đã được liên kết với logic thật** —
không phát hiện cài đặt nào "chết" (có trong UI nhưng không có code tiêu thụ). Tài liệu này là bảng
đối chiếu để kiểm lại từng mũi tên trong sơ đồ dòng cài đặt: mỗi hàng = một chức năng cài đặt,
cột "Logic tiêu thụ" chỉ đúng file:line nơi giá trị thực sự ảnh hưởng hành vi.

Phương pháp:
1. Lấy toàn bộ ~100 thuộc tính của `PianoVisualSettings` (model cài đặt sân khấu) và grep từng
   thuộc tính trong toàn bộ code logic (`PianoStage.cs`, `Shading/*`, `MainWindow*.cs`,
   `PianoAudioEngine.cs`, `SfzSynthesizer.cs`, `SalamanderPiano.cs`, `AviVideoRecorder.cs`…); loại các tham chiếu
   chỉ thuộc về UI (sinh control, lưu/hiển thị giá trị).
2. Kiểm tra từng control tĩnh trong XAML (tab Audio/MIDI/Practice/Recording, header, footer,
   Play dialog, main menu) xem handler của nó có thực sự thay đổi trạng thái/logic không.
3. Chạy `python tools/check_sources.py` (cân bằng cú pháp C#, XML XAML hợp lệ, mọi
   `StaticResource` có định nghĩa, mọi event handler trong XAML đều tồn tại trong C#) → **PASS**.

## 1. Trang Style (preset + chuyển đổi nhanh lớp)

| Chức năng cài đặt | Thuộc tính | Logic tiêu thụ (file:line) |
|---|---|---|
| Preset list / Apply / Save As / Delete / Import / Export / Reset to default | `PresetName`, `PresetModified` | `MainWindow.Settings.cs` (LoadPresetList/ApplyPreset/SavePresetAs/DeletePreset/ImportPreset/ExportPreset/ResetVisualSettings); model `VisualPresets.cs`; lưu JSON `PianoVisualSettings.cs` (VisualPresetStore) |
| Falling notes | `ShowNotes` | `PianoStage.cs:423` (gate `DrawNotes`), `:648` (live trails) |
| Sparks | `ShowEmbers` | `PianoStage.cs:202` (spawn), `:344, :678` (draw) |
| Wisps | `ShowWisps` | `PianoStage.cs:239` (spawn), `:344, :669` (draw) |
| Flames | `ShowFlame` | `PianoStage.cs:342, :1004` (DrawFlames) |
| Impact rings | `ShowImpactRings` | `PianoStage.cs:201` (spawn), `:343, :692` (draw) |
| Light beams | `ShowLightBeams` | `PianoStage.cs:336, :400-417` (DrawKeyBeams) |
| Hit line halo | `ShowHalo` | `PianoStage.cs:345, :700-758` (DrawImpactLine) |
| Piano keys | `ShowKeys` | `PianoStage.cs:347, :784` (DrawKeyboard) |
| Background layers | `ShowBackground` | `PianoStage.cs:316, :323, :330` |
| Keyflow watermark | `ShowWatermark` | `PianoStage.cs:348, :1031` (DrawWatermark) |
| Key counter | `ShowCounter` | `PianoStage.cs:349, :1041` (DrawCounter) |
| FPS & particle HUD | `ShowFps` | `PianoStage.cs:349, :1042` (DrawCounter) |
| Ô tìm kiếm cài đặt | — | `MainWindow.Settings.cs` (SettingsSearch_TextChanged → RefreshDependentRows) |
| SAVE / RESET PAGE / tự lưu sau 0,65 s | — | `SaveVisualSettings_Click` / `ResetPage_Click` / `_settingsSaveTimer` (`MainWindow.xaml.cs:25`) |

## 2. Trang Notes (màu, hình dáng, glow, tốc độ)

| Chức năng cài đặt | Thuộc tính | Logic tiêu thụ |
|---|---|---|
| Color mode (5 chế độ) | `ColorMode` | `PianoStage.cs:603` (switch Gradient/PerHand/PerTrack/RainbowPitch/RainbowTime) |
| Palette (6) | `Palette` | `PianoStage.cs:620` (Aurora/Fire/Ocean/Violet/Custom; Spectrum đi nhánh mặc định `:628` — hue theo cao độ, vẫn render) |
| Gradient start / end | `NoteColorStart`, `NoteColorEnd` | `PianoStage.cs:622, :626`; sửa màu tự chuyển Palette=Custom (`MainWindow.Settings.cs` CommitColor) |
| Left hand / Right hand color | `LeftHandColor`, `RightHandColor` | `PianoStage.cs:606` |
| Hand split point (chấp nhận tên nốt `C4`) | `HandSplitPitch` | `PianoStage.cs:606` (màu theo tay) + `MainWindow.xaml.cs` ApplyTrackFilter (chế độ tập một tay) |
| Track palette 8 màu | `TrackColors` | `PianoStage.cs:609`; chỉnh từ Notes hoặc danh sách track (MIDI) |
| Rainbow speed | `RainbowSpeed` | `PianoStage.cs:618` (RainbowTime) |
| Note style (4 kiểu) | `NoteStyle` | `PianoStage.cs:450` (switch Solid/Neon/Glass/Fire, mỗi kiểu một nhánh vẽ riêng) |
| Note width | `NoteWidth` | `PianoStage.cs:429` (MIDI notes), `:646` (live trails) |
| Corner roundness | `NoteRoundness` | `PianoStage.cs:451` |
| Minimum length | `NoteMinLength` | `PianoStage.cs:436` |
| Gap between notes | `NoteGap` | `PianoStage.cs:430` |
| Fire texture | `NoteTexture` | `PianoStage.cs:479, :482` (ember mask) |
| 3D shading | `Notes3D` | `PianoStage.cs:517, :538` |
| Note names on bars | `ShowNoteLabels` | `PianoStage.cs:556` |
| Tint / opacity | `NoteTint` | `PianoStage.cs:455, :651` |
| Bloom / glow | `NoteGlow` | `PianoStage.cs:453` |
| Edge brightness | `NoteEdge` | `PianoStage.cs:466, :471, :500, :509, :515` |
| Edge width | `NoteEdgeWidth` | `PianoStage.cs:456` |
| Leading-edge glow | `NoteHeadGlow` | `PianoStage.cs:548-553` |
| Light refraction | `NoteRefraction` | `PianoStage.cs:533-536` |
| Fall speed | `NoteFallSpeed` | `PianoStage.cs:426` (nốt MIDI), `:85-89, :268` (live trails); đồng bộ hai chiều với thanh Speed trong Play dialog (`MainWindow.Menu.cs` PlaySpeed_Changed) |
| Direction (Down/Up) | `NoteDirection` | `PianoStage.cs` DrawNotes (nhánh rising: nốt sinh tại đường chạm, đầu thanh bò lên khỏi đỉnh sân khấu), DrawLiveTrails (nhánh rising: thanh chốt vào đường chạm khi giữ phím), Advance (chu kỳ sống trail rising: không có burst rơi thứ hai, xoá khi đuôi ra khỏi đỉnh), DrawConfiguredNote (glow cạnh trước chuyển lên cạnh trên khi rising); kiểm thử `VerificationSuite` (clamp hướng sai → Down, dock đổi hướng → model thanh live thay đổi) |

## 3. Trang Particles (sparks, physics, wisps, flames & rings)

| Chức năng cài đặt | Thuộc tính | Logic tiêu thụ |
|---|---|---|
| Amount / Response | `ParticleAmount`, `ParticleResponse` | `PianoStage.cs:204` |
| Impact burst / held-key GPU emitter family | `ImpactBurst`, `ShowEmbers` | `PianoStage.SpawnImpactBurst`; `GpuStageSimulation.Impact` and `Step` use the same family for impact and sustain, including Zone → Embers/Splash |
| Velocity / randomness / Speed | `ParticleVelocity`, `ParticleRandomness`, `ParticleSpeed` | `PianoStage.cs:210` |
| Spread | `ParticleSpread` | `PianoStage.cs:207` |
| Emitter size | `EmitterSize` | `PianoStage.cs:211` |
| Spiral | `Spiral` | `PianoStage.cs:209` |
| Lifetime (+ randomness) | `ParticleLife`, `ParticleLifeRandomness` | `PianoStage.cs:212` |
| Size (+ randomness) | `ParticleSize`, `ParticleSizeRandomness` | `PianoStage.cs:218` |
| Glow | `ParticleGlow` | `PianoStage.cs:679` |
| Gravity / Drag | `Gravity`, `Drag` | `PianoStage.cs:257` (bước physics) |
| Vector field / Field scale / Evolution | `VectorField`, `FieldScale`, `EvolutionSpeed` | `PianoStage.cs:250, :256` |
| Physics time factor | `PhysicsTimeFactor` | `PianoStage.cs:227` (dt của hiệu ứng); trên GPU, `GpuStageSimulation.Step` chỉ nhân hệ số vào tuổi/vận tốc hạt — đồng hồ sân khấu, nốt, nền và halo vẫn chạy theo thời gian thật |
| Wisp density / rise / height / width / turbulence / glow | `WispAmount…WispGlow` | `PianoStage.cs:239` (spawn gate), `:283-291` (SpawnWisps), `:247` (turbulence), `:663` (glow) |
| Flame intensity / height / color | `FlameIntensity`, `FlameHeight`, `FlameColorMode` | `PianoStage.cs:1004-1005` |
| Impact wave (Ring/Shockwave/None) · size · intensity | `ShowImpactRings`, `ImpactWave`, `RingSize`, `ImpactWaveIntensity` | `PianoStage.cs` SpawnImpactWave → DrawRings/DrawShockwave (size and brightness follow hit strength) |
| Impact flash · intensity | `ShowImpactFlash`, `ImpactFlashIntensity` | `PianoStage.cs` SpawnImpactFlash → DrawImpactFlashes (~180 ms white-hot flare, strength-scaled) |

## 4. Trang Keyboard (hiện ra, lighting, ray-traced shading)

| Chức năng cài đặt | Thuộc tính | Logic tiêu thụ |
|---|---|---|
| Show keyboard / Style / Height | `ShowKeys`, `KeyboardStyle`, `KeyboardScale` | `PianoStage.cs:347`, `:786`, `:71` (KeyboardHeight); style còn đổi màu phím nền của shader (`Shading/PianoShaderScene.cs:171-184`) |
| Black key length | `KeyOverhang` | `PianoStage.cs:856, :1089` |
| Key labels (None/C/All) | `KeyLabels` | `PianoStage.cs:898-901` |
| Lid shadow / Felt strip / Felt color | `ShowKeyShadow`, `ShowKeyFelt`, `KeyFeltColor` | `PianoStage.cs:823, :820/:876, :881` |
| Animate pressed keys | `AnimateKeys` | `PianoStage.cs:838, :850, :863` |
| Pressed key color (Note/Fixed) | `PressedKeyColorMode`, `PressedKeyColor` | `PianoStage.cs:780` |
| Light intensity / Glow radius / Press depth | `KeyLighting`, `KeyGlowRadius`, `KeyPressDepth` | `PianoStage.cs:801-806, :791, :790` |
| Shading engine + 8 tham số + filmic | `ShadingQuality`, `ShaderKeyLight…ShaderFilmic` | `PianoStage.cs:914, :923` (chọn vector hay shader) → `Shading/PianoShaderScene.cs:142-166` (mọi tham số ánh xạ vào giá trị scene: GGX roughness, penumbra, AO, rim, emissive, exposure, tilt camera, ACES on/off) |

## 5. Trang Background & Camera

| Chức năng cài đặt | Thuộc tính | Logic tiêu thụ |
|---|---|---|
| Mode (Solid/Image/ChromaGreen) | `BackgroundMode` | `PianoStage.cs:305` (chroma tắt vignette/aura/sao/beam — nhánh `else` `:312-337`), `:316`, `:914` (shader tắt khi green screen) |
| Background color / Image + dim / CHOOSE-CLEAR IMAGE | `BackgroundColor`, `BackgroundImagePath`, `BackgroundDim` | `PianoStage.cs:315, :316-321, :94` (tải ảnh); `MainWindow.Settings.cs` ChooseStageBackground |
| Aura gradient / Stars / density / Guide lanes | `BackgroundGradient`, `ShowStars`, `StarDensity`, `BackgroundGuide` | `PianoStage.cs:323, :332, :357, :333` |
| GPU animated backdrop | `BackgroundMotion`, `BackgroundMotionAmount`, `BackgroundMotionSpeed`, `BackgroundMotionColor` | `GpuLook.From` → `GpuStageRenderer.BuildConstants` (`SceneFx`) → `StageShaders.hlsl::BackgroundMotion` (`PsBackground`); Aurora / Nebula / Prism / Ember Haze / Ocean Flow / Retro Grid; skipped for None, hidden background and chroma |
| GPU hit-line animation | `HaloPulse`, `HaloPulseStyle`, `HaloPulseIntensity`, `HaloPulseSpeed` | `GpuStageSimulation.AddHaloPulses` for travelling geometry + `GpuStageRenderer.BuildConstants` (`HitFx`) → `StageShaders.hlsl::PsSprite`; Pulse / Sweep / Twin Comets / Spectrum / Electric Arc / Ripple |
| Vignette / Horizon glow / Beam intensity | `Vignette`, `HorizonGlow`, `BeamIntensity` | `PianoStage.cs:346, :767-782`; `:335, :388-389`; `:336, :408-413` |
| Halo line / color / intensity | `ShowHalo`, `HaloColor`, `HaloIntensity` | `PianoStage.cs:345, :387/:702/:787`, `:703`; màu halo còn tint rim light của shader (`Shading/PianoShaderScene.cs:159`) |
| Parallax / Zoom / Horizontal framing | `CameraParallax`, `CameraZoom`, `CameraOffset` | `PianoStage.cs:126, :307, :306, :308` (transform camera mỗi frame) |
| Saturation / Contrast | `Saturation`, `Contrast` | `PianoStage.cs:633-641` (AdjustColor, áp cho mọi màu nốt/đèn); cũng trong shader (`Shading/*`) |
| Bloom intensity / size | `BloomIntensity`, `BloomSize` | `PianoStage.cs:326, :453, :707` (intensity); `:452, :662, :706` (size) |

## 6. Trang Audio / MIDI / Practice / Recording (control tĩnh trong XAML)

| Chức năng cài đặt | Logic tiêu thụ |
|---|---|
| Tải / huỷ / xác minh lại Salamander | `Ui/MainWindow.xaml` (`SalamanderButton`, progress bar) → `MainWindow.xaml.cs` (`SalamanderButton_Click`) → `SalamanderPiano.DownloadAsync`; tiến độ báo theo byte đã xác minh, huỷ giữ lại tệp hoàn chỉnh để lần sau tiếp tục |
| Nạp đàn đã cài | `SalamanderPlayButton` → `SalamanderPlay_Click` → `LoadSalamanderAsync` → `PianoAudioEngine.LoadSfzInstrument`; cùng luồng nạp chạy sau khi tải đủ và khi khởi động nếu mapping đã cài |
| SFZ/Sample pipeline (instrument duy nhất) | `PianoAudioEngine.LoadSfzInstrument` → `SfzReader.Read` → `SfzSynthesizer`; engine chỉ nhận mapping SFZ do `SalamanderPiano` sinh, không có API nhập SF2 hoặc chọn nhạc cụ ngoài |
| HALL REVERB | `ReverbToggle_Changed` → `PianoAudioEngine.ReverbEnabled`; bypass tác động lên pipeline live mà không cắt đột ngột đuôi reverb |
| Reverb style / custom IR | `ReverbStyleCombo_SelectionChanged` / `ReverbImpulse_Click` → `SetReverbAsync` / `PianoAudioEngine.SetReverbAsync`; WAV/FLAC đọc trên worker, request cũ bị bỏ qua khi người dùng chọn phòng mới |
| STRING RESONANCE | `ResonanceToggle_Changed` → `PianoAudioEngine.StringResonanceEnabled` → bank 64 waveguide được bật/tắt theo pedal sustain |
| Chọn đầu ra âm thanh | `AudioOutputCombo_SelectionChanged` lưu `AudioOutputMode` rồi gọi `ApplyAudioOutput` → `PianoAudioEngine.ConfigureOutput`; lựa chọn hỗ trợ Auto, WASAPI shared/exclusive (endpoint cụ thể), ASIO và waveOut |
| Danh sách thiết bị | `RefreshAudioOutputCatalog` gọi `WasapiDeviceCatalog.GetActiveRenderEndpoints` trên worker; ASIO lấy từ `AsioDriverRegistry`; kết quả được gắn lại vào UI dispatcher và giữ ID ổn định |
| Bảng điều khiển ASIO | `AsioPanelButton` → `AsioPanel_Click` → `PianoAudioEngine.ShowAsioControlPanel`; chỉ bật khi pipeline thực sự đang phát qua ASIO và panel chạy trên luồng sở hữu driver |
| Độ trễ mục tiêu | `AudioLatencyCombo_SelectionChanged` → `AudioLatencySteps` (20/10/5/3 ms) → `ConfigureOutput`; backend được mở lại bất đồng bộ trên luồng điều khiển, không giữ WPF input thread |
| Master piano level | `AudioGainSlider_ValueChanged` → `AudioMasterGain` + `PianoAudioEngine.MasterGain`; gain áp sau synth với soft limiter để nâng mức đàn mà giữ an toàn peak |
| Trạng thái đầu ra | `UpdateAudioOutputHint` báo đầu ra/buffer đang hoạt động, fallback nếu lựa chọn không mở được hoặc nguyên nhân im lặng; không hiện tên ASIO như đường ra nếu ASIO không thực sự active |
| MIDI input / output / Refresh | `MainWindow.xaml.cs` + `MidiDeviceService.cs` (WinMM in/out, Note On/Off, pedal CC vào app) |
| Track solo + mute/đổi màu | `MainWindow.xaml.cs` (`ApplyTrackFilter`) + `MainWindow.Settings.cs` (`RebuildTrackList`/`TrackMute_Changed`) |
| Metronome | `TickMetronome` theo tempo map + time signature; chỉ bật khi có piano đã nạp, click pitch 77 đi qua cùng Salamander SFZ engine |
| Practice mode (4 chế độ) | `ModeCombo` → Follow along / Wait for my note / solo tay phải/trái, dùng chung Hand split |
| Infer hand split from the song | `Ui/MainWindow.Library.cs` (`ApplyInferredHandSplit` → `HandSplit.Infer`) → slider **Hand split point**, ghi vào `library.json` kèm `SplitInferred` |
| Auto practice tempo / miss threshold | `MainWindow.Practice.cs` → `PracticeAutoTempo`/`PracticeMissThreshold` → chấm điểm nốt và `TempoSlider` |
| Playback tempo / loop | `TempoSlider`, loop A/B/× và seek được xử lý trong `MainWindow.xaml.cs`; pedal CC gửi vào `PianoAudioEngine` và MIDI output |
| Recording format / audio / resolution / fps | `MainWindow.Settings.cs` cài `RecordingFormat`/`RecordAudio`/`RecordingSize`; `RecordVideo_Click` chọn AVI/PNG/MP4, `PianoAudioEngine.SetTap` đưa block PCM vào WAV hoặc AAC |
| START/STOP RECORDING | `MainWindow.xaml.cs` ghi frame theo đồng hồ thật, dừng gần giới hạn 2 GB đối với AVI |
| Shortcuts | `MainWindow.xaml.cs` (`MapComputerKey`, F11/Esc, auto-hide) + `PianoStage.PianoKeyChanged` cho piano ảo |

## 7. Shell: main menu, Play dialog, header, footer

| Chức năng | Logic tiêu thụ |
|---|---|
| Main menu Play / Quick Adjustments / Piano & Audio Setup / MIDI Devices / About / Exit | `MainWindow.Menu.cs` opens the matching menu surface; setup shortcuts jump directly to the Audio or MIDI settings page |
| Main menu MIDI Devices shortcut | `MainMenuMidi_Click` selects `SettingsPages.Midi` and opens the dock |
| HOME (header) | `MainWindow.xaml:55` → `MainMenu_Click` (`MainWindow.Menu.cs`) |
| PLAY / SETTINGS / thu nhỏ / toàn màn hình / thoát (header) | Header **PLAY** mở `OpenPlayDialog`; **MIDI File** bên trong hộp thoại gọi `OpenMidi_Click`; dock dùng `Settings_Click`, các nút cửa sổ giữ handler riêng |
| Play dialog: Back / Close / MIDI File / Live Play / CTA | `MainWindow.Menu.cs`: Back trở về bề mặt gọi hộp thoại, Close đóng về sân khấu, CTA đổi giữa chọn tệp và bắt đầu phát, Live Play chuyển sang biểu diễn trực tiếp |
| Play dialog ↔ dock cài đặt | Nút SETTINGS mở trang Style; thẻ tay và “More settings…” deep-link tới đúng tab. Dock giữ bề mặt quay lại (Play/menu/sân khấu), có nút Back riêng và Escape theo cùng tuyến |
| Play dialog: toggle lớp Background/Notes/Embers/Halo/Flame/Keys | `MainWindow.Menu.cs:66-90` — mỗi toggle Tag = thuộc tính thật, dùng chung handler `VisualToggle_Changed` nên dialog và dock luôn khớp; đồng bộ ngược qua `SyncPlayDialogToggle` |
| Play dialog: Speed + reset | `NoteFallSpeed` hai chiều (PlaySpeed_Changed ↔ slider dock) |
| Play dialog: màu halo, chevron deep-link 8 mục, card tay | `MainWindow.Menu.cs` (PlayDialogHaloColor_Click → `HaloColor`; PlayDialogDeepLink_Click mở đúng tab theo index; card biên màu `LeftHandColor/RightHandColor` hoặc gradient nốt) |
| Play / Restart / seek / 3 pedal / accuracy-score-streak | `MainWindow.xaml.cs` (TogglePlay/Restart/SeekToSlider, PedalToggle_Changed → CC 67/66/64 vào Salamander SFZ + MIDI output, UpdateStats) |
| Pedal MIDI vào → Salamander SFZ & output | `MidiDeviceService.PedalChanged` → `SetPedalState` (`MainWindow.xaml.cs:533-548`) → `SfzSynthesizer.ProcessMidi` CC 64/66/67 (`Audio/SfzSynthesizer.cs`) |

## 8. Các ghi chú (không phải lỗi liên kết)

1. `BackgroundAppearanceVersion` — cờ nội bộ cho migration JSON cũ, **không phải** cài đặt người dùng
   (chỉ đọc/ghi trong `ApplyMigrations`), nên không cần mũi tên nào trỏ vào logic render.
2. Palette `Spectrum` — là palette duy nhất không có nhánh tường minh trong `NoteColor`
   (`PianoStage.cs:620`), nó đi vào nhánh mặc định `_ =>` (hue theo cao độ) và vẫn render đúng;
   `Clamp()` giữ giá trị hợp lệ.
3. Ô tìm kiếm chỉ lọc hàng đã đăng ký trên Style/Theme/Notes/Particles/Keyboard/Background/Camera/Recording/General;
   các trang Audio/MIDI/Practice/History dựng tĩnh từ XAML nên chưa được lọc. Tooltip “Filter the settings on every page”
   vì thế rộng hơn phạm vi thật; đây là giới hạn UX, không phải cài đặt chết.
4. Metronome chỉ bật điểm click khi **có bài MIDI** (cần tempo map `_beatTimes`); checkbox được
   kích hoạt theo piano đã nạp. Click phát bằng cùng Salamander SFZ engine.
5. `RESET PAGE` trên các trang tĩnh hiển thị "This page has no visual settings to reset" — đúng
   thiết kế vì các trang đó không có tham số visual.

## 9. Bằng chứng kiểm thử

- `python3 tools/check_sources.py` → **PASS (2026‑10‑04)**: 116 file C# và 2 file XAML được quét,
  99 resource key, 159 phần tử có tên; XML hợp lệ, mọi `StaticResource` và event handler đều phân giải,
  hai bảng dịch đồng bộ và tài liệu phát hành khớp generator.
- `VerificationSuite` (`PianoPath.exe --verify`, chạy CI trên Windows) có kiểm thử cho dock 13 trang,
  tìm kiếm, preset, accessibility, dialog/menu, MIDI/practice, pedal, Salamander/SFZ, output/reverb và recording.
  **Không chạy được trong sandbox Linux của lượt này** (không có .NET SDK/WPF); cần job Windows/CI để xác nhận runtime.
  Xem `docs/UI-SHADER-REVIEW.md` và README mục Kiểm thử.

**Kết luận theo đối chiếu mã:** mọi mũi tên trong sơ đồ 13 trang dock + các nhóm điều khiển XAML tĩnh +
Play dialog + menu/footer đều có handler hoặc consumer. Tĩnh đã kiểm; hành vi thiết bị thật vẫn cần xác nhận trên Windows.

## 10. Bổ sung: trang Theme và việc bỏ số tab "magic" (đợt nâng cấp giao diện hoà nhạc)

- **Thứ tự trang giờ nằm một chỗ**: `Ui/SettingsPages.cs` giữ tên trang và mảng `Order`
  (13 trang, **Theme ở vị trí 1**); mọi chỗ trước đây dùng số nguyên (`= 1 // Notes`, `= 6 // Audio`,
  `Math.Clamp(tab, 0, 9)`, mảng `pages` trong tìm kiếm, bảng `--settings-tab`) đều gọi
  `SettingsPages.IndexOf(name)` / `SettingsPageHost(index)`. Thêm trang mới = 1 dòng trong
  `SettingsPages` + 1 `TabItem` trong XAML.
- **Trang Theme** (`BuildThemePage`) nối thật vào hệ cài đặt: chip giao diện (Concert Grand /
  Concert Noir / Velvet Gold) → `ShellTheme` → `ShellThemeManager.Apply` → hàng chục brush trong
  `Application.Resources` đổi qua `DynamicResource`; `ChromeMotion` (Off/Calm/Full) và
  `BackdropDensity` nuôi `ChromeBackdrop.Configure`; công tắc `ShowPetals`
  (kèm số lượng/màu, hàng phụ thuộc `VisibleWhen`) được `PianoStage` vẽ thật bằng
  `DrawPetals` (đèn sân khấu `ShowSpotlights`/`DrawSpotlights` đã xóa ở effects-redesign v1); ba nút "quick look" áp preset Sakura Nocturne / Concert Gold /
  Moonlight Sonata (đều đã có trong `VisualPresets.BuiltIn`).
- **Mỗi preset có sẵn khai báo giao diện hợp nhất** (`ShellTheme`), nên áp preset đổi cả sân khấu lẫn
  vỏ app; preset của người dùng vẫn giữ nguyên hành vi cũ.
- **Bằng chứng kiểm thử mới** (trong `VerificationSuite`):
  - `tabs.Items.Count == SettingsPages.Order.Length` (13), tiêu đề tab 0 = Style, tab 1 = Theme,
    tab cuối = Recording, và `IndexOf` trả đúng chỉ số / `-1` cho tên lạ;
  - **điều hướng ba nhóm**: thứ tự tab bằng đúng `SettingsPages.Order`, `Order` bằng đúng chuỗi trang
    của `Sections`, mỗi nhãn nhóm nằm trên trang đầu của nhóm đó và `SectionOf("Nope")` là `null`;
  - **id giao diện cũ**: `Find("sakura" | "noir" | "velvet" | "Sakura Nocturne")` trả về id chuẩn, và
    file lưu với `"ShellTheme":"sakura"` được viết lại thành `concert-grand` khi nạp;
  - trang Theme có chip cho từng giao diện, đổi `ShellTheme` → `AccentColor` trong resource đổi theo,
    `ShowPetals` làm `HasActiveEffects` bật và `DrawPetals` vẽ ra petal thật
    (`stage.PetalCount` trong khoảng 1–150); impact FX (`ImpactWave`/`ShowImpactFlash`) sinh wave/flash
    vẽ geometry thật và tắt dần theo thời gian;
  - sân khấu chạy trên `FrameClock` dùng chung: không còn field `_timer` 16 ms, `PressNote` phải
    `Acquire` được đồng hồ.

## 11. Đợt effects-redesign v1: xóa đèn sân khấu + Impact FX theo phase

- **Xóa toàn bộ đèn sân khấu**: `ShowSpotlights`/`SpotlightIntensity` (model), `DrawSpotlights`/
  `DrawSpotlight`/`SpotlightConeBrush`/`SpotlightPoolBrush` (renderer), toggle + slider "Stage
  illumination" (trang Theme), tooltip Play dialog, mọi preset, test và tài liệu. File JSON cũ còn
  key này vẫn đọc được (parser bỏ qua key lạ).
- **Impact phase hoàn thiện theo kênh**: hạt (`ShowEmbers` + hệ sparks), sóng (`ShowImpactRings` +
  `ImpactWave` Ring/Shockwave/None + `RingSize` + `ImpactWaveIntensity`), chớp (`ShowImpactFlash` +
  `ImpactFlashIntensity`). Độ lớn/độ sáng theo lực nhấn (modulator velocity đầu tiên).
- **Bảng tổng hợp**: `Stage/Effects/EffectCatalog.cs` liệt kê toàn bộ 87 effects (4 phase nốt +
  4 họ ambient + modulators + themes), mỗi effect ghi rõ kênh renderer và trạng thái
  Available/Planned. Quy tắc: effect Planned không hiện UI cho đến khi có renderer thật.
- Xem chi tiết kiến trúc và roadmap trong `docs/EFFECTS-REDESIGN.md`.

## 12. Đợt effects-redesign v2–v7: hoàn thiện toàn bộ 87 effects

- **v2 Falling + Impact còn lại**: `FallingTrail` 7 kiểu + Intensity/Length, `FallingPulse` +
  Rate, `FallingGhost` + Amount (trang Notes, card FALLING FX); `ImpactBurst` 5 kiểu
  (kèm `Spark.Kind`/`Grav`/`DragK` và 4 renderer hạt mới), `ImpactMorph` 5 kiểu (+ Intensity),
  `ImpactFlashStyle` 3 kiểu (trang Particles); test `VerifyFallingFx`; sửa bug test v1
  (`Advance` clamp 50 ms/step nên decay phải lặp nhiều step).
- **v3 Hold**: `HoldBar`/`HoldBreath`/`HoldVibration`/`HoldColorCycle`/`HoldElectricArc` +
  slider (card HOLD FX); `BreathFactor()` chỉ scale bán kính/glow nên không phá cache brush;
  arc điện procedural nối tối đa 6 cặp phím; test `VerifyHoldFx`.
- **v4 Release**: `ReleaseEffect` 6 kiểu + Intensity (card RELEASE FX); `ReleaseLiveNote` +
  `ScanReleaseFx` bắt MIDI note-end (con trỏ đơn điệu, chặn seek ngược, trần 24/frame);
  test `VerifyReleaseFx`.
- **v5 Ambient**: 4 khe `AmbientEnergy`/`AmbientNature`/`AmbientLight`/`AmbientCosmic`
  (Choice + Amount + Speed, Light thêm Color; card AMBIENT LAYERS trang Background),
  render procedural sau background; `ImpactWave` thêm Ripple; `DrawBolt` dùng chung;
  `HasActiveEffects` bao 4 khe; test `VerifyAmbientFx`.
- **v6 Smart**: `VelocityColor`/`OctaveColor`/`ZoneSplit` (màu qua `NoteColor` bọc ngoài
  `NoteColorCore`, zone ép kiểu burst), `PedalGlow`/`TempoSync`/`AudioReactive` (3 boost
  nhân vào glow/halo/phím); nối dây thật — velocity MIDI/live → `Impact`, sustain →
  `SetSustainPedal`, beat tempo map → `PulseBeat` (kể cả khi tắt metronome); card SMART
  MODULATORS; test `VerifySmartFx`.
- **v7 Themes**: 7 preset graphs (Inferno, Ice Crystal, Sakura Nocturne nâng cấp + Galaxy
  Voyage, Electric Storm, Ocean Depths, Retro Arcade mới); test `VerifyThemes`. Toàn bộ
  87/87 effects trong `EffectCatalog` đã Available; mọi control trên UI đều có logic thật.
