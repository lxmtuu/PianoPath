# Kiểm tra liên kết logic của mọi chức năng cài đặt

Kết quả rà soát toàn bộ repo: **mọi chức năng cài đặt (UI) đều đã được liên kết với logic thật** —
không phát hiện cài đặt nào "chết" (có trong UI nhưng không có code tiêu thụ). Tài liệu này là bảng
đối chiếu để kiểm lại từng mũi tên trong sơ đồ dòng cài đặt: mỗi hàng = một chức năng cài đặt,
cột "Logic tiêu thụ" chỉ đúng file:line nơi giá trị thực sự ảnh hưởng hành vi.

Phương pháp:
1. Lấy toàn bộ ~100 thuộc tính của `PianoVisualSettings` (model cài đặt sân khấu) và grep từng
   thuộc tính trong toàn bộ code logic (`PianoStage.cs`, `Shading/*`, `MainWindow*.cs`,
   `PianoAudioEngine.cs`, `SoundFontSynthesizer.cs`, `AviVideoRecorder.cs`…); loại các tham chiếu
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
| Velocity / randomness / Speed | `ParticleVelocity`, `ParticleRandomness`, `ParticleSpeed` | `PianoStage.cs:210` |
| Spread | `ParticleSpread` | `PianoStage.cs:207` |
| Emitter size | `EmitterSize` | `PianoStage.cs:211` |
| Spiral | `Spiral` | `PianoStage.cs:209` |
| Lifetime (+ randomness) | `ParticleLife`, `ParticleLifeRandomness` | `PianoStage.cs:212` |
| Size (+ randomness) | `ParticleSize`, `ParticleSizeRandomness` | `PianoStage.cs:218` |
| Glow | `ParticleGlow` | `PianoStage.cs:679` |
| Gravity / Drag | `Gravity`, `Drag` | `PianoStage.cs:257` (bước physics) |
| Vector field / Field scale / Evolution | `VectorField`, `FieldScale`, `EvolutionSpeed` | `PianoStage.cs:250, :256` |
| Physics time factor | `PhysicsTimeFactor` | `PianoStage.cs:227` (dt nhân hệ số) |
| Wisp density / rise / height / width / turbulence / glow | `WispAmount…WispGlow` | `PianoStage.cs:239` (spawn gate), `:283-291` (SpawnWisps), `:247` (turbulence), `:663` (glow) |
| Flame intensity / height / color | `FlameIntensity`, `FlameHeight`, `FlameColorMode` | `PianoStage.cs:1004-1005` |
| Impact rings · Ring size | `ShowImpactRings`, `RingSize` | `PianoStage.cs:201, :692` |

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
| Vignette / Horizon glow / Beam intensity | `Vignette`, `HorizonGlow`, `BeamIntensity` | `PianoStage.cs:346, :767-782`; `:335, :388-389`; `:336, :408-413` |
| Halo line / color / intensity | `ShowHalo`, `HaloColor`, `HaloIntensity` | `PianoStage.cs:345, :387/:702/:787`, `:703`; màu halo còn tint rim light của shader (`Shading/PianoShaderScene.cs:159`) |
| Parallax / Zoom / Horizontal framing | `CameraParallax`, `CameraZoom`, `CameraOffset` | `PianoStage.cs:126, :307, :306, :308` (transform camera mỗi frame) |
| Saturation / Contrast | `Saturation`, `Contrast` | `PianoStage.cs:633-641` (AdjustColor, áp cho mọi màu nốt/đèn); cũng trong shader (`Shading/*`) |
| Bloom intensity / size | `BloomIntensity`, `BloomSize` | `PianoStage.cs:326, :453, :707` (intensity); `:452, :662, :706` (size) |

## 6. Trang Audio / MIDI / Practice / Recording (control tĩnh trong XAML)

| Chức năng cài đặt | Logic tiêu thụ |
|---|---|
| LOAD SOUNDFONT / Unload (silent) | `MainWindow.xaml.cs:223-265` → `PianoAudioEngine.LoadSoundFont/UnloadSoundFont` |
| Preset instrument (bank/program) | `MainWindow.xaml.cs:259-264` → `SoundFontSynthesizer.SelectPreset` (`SoundFontSynthesizer.cs:333-336`) |
| HALL REVERB | `MainWindow.xaml.cs:297-300` → `PianoAudioEngine.ReverbEnabled` (`PianoAudioEngine.cs:22-30, :126`) → bộ reverb Schroeder stereo chạy trên mọi buffer PCM (`StereoHallReverb`, `RenderBuffer`) |
| MIDI input / output / Refresh | `MainWindow.xaml.cs:602-676` + `MidiDeviceService.cs` (WinMM in/out, Note On/Off, pedal CC vào app) |
| Track solo (TrackCombo) + mute/đổi màu từng track | `MainWindow.xaml.cs:678-684` (ApplyTrackFilter) + `MainWindow.Settings.cs` RebuildTrackList/TrackMute_Changed |
| Metronome | `MainWindow.xaml.cs:187-197` (TickMetronome theo tempo map + time signature; bật khi có SoundFont, điểm click phát bằng chính SoundFont pitch 77) |
| Practice mode (4 chế độ) | `MainWindow.xaml.cs:203` (ModeCombo) → `:175` (Wait for my note giữ playhead), `:695-697` (solo tay phải/trái dùng chung Hand split) |
| Playback tempo 50–150% | `MainWindow.xaml.cs:199-201` → `:165` (`_position += elapsed * _tempo`) |
| Loop A / B / × | `MainWindow.xaml.cs:592-601` (đặt/xóa), `:167-173` (quay vòng B→A, reset điểm số trong vùng loop) |
| Recording resolution / fps | `MainWindow.Settings.cs:901-924` (RecordingSize) → `MainWindow.xaml.cs:404-405` (tạo `AviVideoRecorder` đúng kích thước + frame rate) |
| START/STOP RECORDING | `MainWindow.xaml.cs:397-472` (SaveFileDialog, ghi frame theo đồng hồ thật, tự dừng ở 2 GB) |
| Shortcuts (A W S E… / Space / F11 / Esc / chuột) | `MainWindow.xaml.cs:322-352` (MapComputerKey → PressNote), `:384-400` (F11/Esc), `:354-364` (chỉ hiện chrome), click phím ảo `PianoStage.PianoKeyChanged` |

## 7. Shell: main menu, Play dialog, header, footer

| Chức năng | Logic tiêu thụ |
|---|---|
| Main menu Play / Design / Settings / About / Exit | `MainWindow.Menu.cs` (MainMenuPlay/Design/Settings/About/Exit_Click) |
| HOME (header) | `MainWindow.xaml:55` → `MainMenu_Click` (`MainWindow.Menu.cs`) |
| OPEN MIDI / SETTINGS / thu nhỏ / toàn màn hình / thoát (header) | `MainWindow.xaml.cs` (OpenMidi_Click, Settings_Click, Minimize/FullScreen/CloseWindow_Click) |
| Play dialog: MIDI File / Live Play / Play | `MainWindow.Menu.cs` (PlayDialogOpenMidi/Live/Play_Click) |
| Play dialog: toggle lớp Background/Notes/Embers/Halo/Flame/Keys | `MainWindow.Menu.cs:66-90` — mỗi toggle Tag = thuộc tính thật, dùng chung handler `VisualToggle_Changed` nên dialog và dock luôn khớp; đồng bộ ngược qua `SyncPlayDialogToggle` |
| Play dialog: Speed + reset | `NoteFallSpeed` hai chiều (PlaySpeed_Changed ↔ slider dock) |
| Play dialog: màu halo, chevron deep-link 8 mục, card tay | `MainWindow.Menu.cs` (PlayDialogHaloColor_Click → `HaloColor`; PlayDialogDeepLink_Click mở đúng tab theo index; card biên màu `LeftHandColor/RightHandColor` hoặc gradient nốt) |
| Play / Restart / seek / 3 pedal / accuracy-score-streak | `MainWindow.xaml.cs` (TogglePlay/Restart/SeekToSlider, PedalToggle_Changed → CC 67/66/64 vào SoundFont + MIDI output, UpdateStats) |
| Pedal MIDI vào → SoundFont & output | `MidiDeviceService.PedalChanged` → `SetPedalState` (`MainWindow.xaml.cs:533-548`) → `SoundFontSynthesizer.ProcessMidi` CC 64/66/67 (`SoundFontSynthesizer.cs:346-358`) |

## 8. Các ghi chú (không phải lỗi liên kết)

1. `BackgroundAppearanceVersion` — cờ nội bộ cho migration JSON cũ, **không phải** cài đặt người dùng
   (chỉ đọc/ghi trong `ApplyMigrations`), nên không cần mũi tên nào trỏ vào logic render.
2. Palette `Spectrum` — là palette duy nhất không có nhánh tường minh trong `NoteColor`
   (`PianoStage.cs:620`), nó đi vào nhánh mặc định `_ =>` (hue theo cao độ) và vẫn render đúng;
   `Clamp()` giữ giá trị hợp lệ.
3. Ô tìm kiếm chỉ lọc 7 trang sinh động (Style…Camera + Recording); 4 trang tĩnh
   (Audio/MIDI/Practice/Recording) không có hàng nào đăng ký vào hệ tìm kiếm — tooltip
   "Filter the settings on every page" hơi rộng hơn phạm vi thật. Đây là giới hạn UX, không phải cài đặt chết.
4. Metronome chỉ bật điểm click khi **có bài MIDI** (cần tempo map `_beatTimes`); checkbox được
   kích hoạt theo SoundFont. Đúng thiết kế vì click phát bằng chính tiếng SoundFont.
5. `RESET PAGE` trên các trang tĩnh hiển thị "This page has no visual settings to reset" — đúng
   thiết kế vì các trang đó không có tham số visual.

## 9. Bằng chứng kiểm thử

- `python tools/check_sources.py` → **PASS**: 19 file C# cân bằng ngoặc/nháy; 2 file XAML hợp lệ,
  85 resource key, mọi tham chiếu `StaticResource` có định nghĩa, **mọi event handler trong XAML
  đều tồn tại trong C#** (không có nút nào gán handler ảo).
- `VerificationSuite` (`PianoPath.exe --verify`, chạy CI trên Windows) bao phủ: dock 10 trang,
  đổi color mode → màu nốt thật (`stage.NoteColor`), hàng phụ thuộc ẩn/hiện, tìm kiếm, áp preset
  tại chỗ, công tắc shader đổi đúng stage vẽ + cache bake, Play dialog mirror cài đặt, chế độ tập/
  loop/tempo/tua, reverb stereo, metronome, pedal CC, AVI, nhập MIDI. Xem `docs/UI-SHADER-REVIEW.md`
  và README mục Kiểm thử.

**Kết luận: không có chức năng cài đặt nào thiếu logic — mọi mũi tên trong sơ đồ dòng cài đặt
(10 trang dock + 4 nhóm control tĩnh + Play dialog + menu/footer) đều đã có code liên kết thật.**

## 10. Bổ sung: trang Theme và việc bỏ số tab "magic" (đợt nâng cấp giao diện hoà nhạc)

- **Thứ tự trang giờ nằm một chỗ**: `Ui/SettingsPages.cs` giữ tên trang và mảng `Order`
  (11 trang, **Theme ở vị trí 1**); mọi chỗ trước đây dùng số nguyên (`= 1 // Notes`, `= 6 // Audio`,
  `Math.Clamp(tab, 0, 9)`, mảng `pages` trong tìm kiếm, bảng `--settings-tab`) đều gọi
  `SettingsPages.IndexOf(name)` / `SettingsPageHost(index)`. Thêm trang mới = 1 dòng trong
  `SettingsPages` + 1 `TabItem` trong XAML.
- **Trang Theme** (`BuildThemePage`) nối thật vào hệ cài đặt: chip giao diện (Concert Grand /
  Concert Noir / Velvet Gold) → `ShellTheme` → `ShellThemeManager.Apply` → hàng chục brush trong
  `Application.Resources` đổi qua `DynamicResource`; `ChromeMotion` (Off/Calm/Full) và
  `BackdropDensity` nuôi `ChromeBackdrop.Configure`; hai công tắc mới `ShowPetals`/`ShowSpotlights`
  (kèm số lượng/màu/độ sáng, hàng phụ thuộc `VisibleWhen`) được `PianoStage` vẽ thật bằng
  `DrawPetals`/`DrawSpotlights`; ba nút "quick look" áp preset Sakura Nocturne / Concert Gold /
  Moonlight Sonata (đều đã có trong `VisualPresets.BuiltIn`).
- **Mỗi preset có sẵn khai báo giao diện hợp nhất** (`ShellTheme`), nên áp preset đổi cả sân khấu lẫn
  vỏ app; preset của người dùng vẫn giữ nguyên hành vi cũ.
- **Bằng chứng kiểm thử mới** (trong `VerificationSuite`):
  - `tabs.Items.Count == SettingsPages.Order.Length` (11), tiêu đề tab 0 = Style, tab 1 = Theme,
    tab cuối = Recording, và `IndexOf` trả đúng chỉ số / `-1` cho tên lạ;
  - **điều hướng ba nhóm**: thứ tự tab bằng đúng `SettingsPages.Order`, `Order` bằng đúng chuỗi trang
    của `Sections`, mỗi nhãn nhóm nằm trên trang đầu của nhóm đó và `SectionOf("Nope")` là `null`;
  - **id giao diện cũ**: `Find("sakura" | "noir" | "velvet" | "Sakura Nocturne")` trả về id chuẩn, và
    file lưu với `"ShellTheme":"sakura"` được viết lại thành `concert-grand` khi nạp;
  - trang Theme có chip cho từng giao diện, đổi `ShellTheme` → `AccentColor` trong resource đổi theo,
    `ShowPetals/ShowSpotlights` làm `HasActiveEffects` bật và `DrawPetals` vẽ ra petal thật
    (`stage.PetalCount` trong khoảng 1–150) cùng geometry thật của spotlight;
  - sân khấu chạy trên `FrameClock` dùng chung: không còn field `_timer` 16 ms, `PressNote` phải
    `Acquire` được đồng hồ.
