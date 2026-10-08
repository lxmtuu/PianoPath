# Visual Effects Studio — wireframe đề xuất (Phase 1)

> **Trạng thái:** thiết kế để duyệt, chưa phải WPF UI. Không có setting/preset nào được ghi hoặc áp dụng từ trang này.
> **Bố cục tham chiếu:** desktop 1440×900; kiểm responsive ở 1280×720.
> **Cùng bộ:** [design bible](VISUAL-EFFECTS-DESIGN-BIBLE.md) · [scene fixture](samples/visual-effects-phase1-scene.json) · [prototype xem trước](prototypes/visual-effects-phase1/index.html).

## 1. Khung chính — Effects Studio

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ← STYLE / EFFECTS STUDIO                         LOOK: Concert ▾     Undo   Redo   A/B   Save as preset │ 64 px
├──────────────────────┬───────────────────────────────────────────────────────┬───────────────────────────┤
│ EFFECT LIBRARY       │ PREVIEW                                                 │ INSPECTOR                 │
│ Search effects…  ⌕   │ [Stage] [Recording] [AR Surface · synthetic]   1920×1080│ Selected: Concert look    │
│                      │ ┌───────────────────────────────────────────────────┐ │                           │
│ QUICK ADJUST         │ │                                                   │ │ Palette                 │
│ • Look cards         │ │         deterministic piano scene                │ │ Left hand      [swatch] │
│ • Global intensity   │ │         falling notes + keyboard                  │ │ Right hand     [swatch] │
│ • Particle density   │ │                                                   │ │                           │
│ • Quality tier       │ │                                                   │ │ Motion                  │
│                      │ └───────────────────────────────────────────────────┘ │ Trail amount     ───●── │
│ GROUPS               │                                                       │ Bloom            ──●─── │
│ ▸ Falling            │ [⏮] [▶ Play] [⏭]  Loop 1×  Seed 424242   [Reset]      │                           │
│ ▸ Impact             ├───────────────────────────────────────────────────────┤ │ EFFECT / LAYER INSPECTOR│
│ ▸ Hold               │ TEST TIMELINE                  00:04.100 / 00:12.000  │ │ Enable / style         │
│ ▸ Release            │ L  ────────────────●───────────────────────────────   │ │ Parameters by effect   │
│ ▸ Ambient            │ R       ────────●───────●───────────────────────────  │ │ Cost / capability      │
│ ▸ Keyboard           │ Pedal             ┌────────┐                            │ │ Per-effect reset       │
│ ▸ AR Surface         │       bar 1     bar 2     bar 3     bar 4               │ │                         │
├──────────────────────┴───────────────────────────────────────────────────────┴───────────────────────────┤
│ ⓪ Design-only preview · same MIDI and seed across looks · renderer support not implied                    │ 32 px
└──────────────────────────────────────────────────────────────────────────────────────────────────────────┘
  236 px                    flexible; stage keeps 16:9, min width 560 px                 320 px
```

### Bố cục và ưu tiên

- **Top bar:** back, tên bề mặt, chọn look/preset, undo/redo, A/B và lưu bản sao. Dirty state có dấu chấm; không autosave làm đổi preset gốc.
- **Library trái:** tìm kiếm + nhóm hiệu ứng; Quick Adjust luôn là entry đầu. Card có tên người dùng hiểu, phase/trigger ngắn, trạng thái hỗ trợ và cost badge.
- **Preview giữa:** stage lớn nhất, dùng aspect 16:9; viewport selector không giả vờ camera/hardware khi đang dùng synthetic scene. Controls nằm ngoài khung ảnh để không ăn vào ảnh export.
- **Timeline dưới preview:** scrub, loop, play/pause, step frame, tốc độ; tracks tối thiểu Left/Right/Pedal/Impact. Mọi checkpoint trong JSON chọn được trực tiếp.
- **Inspector phải:** context-sensitive; chỉ hiện tham số của look/effect đang chọn, mô tả tác động và reset tại chỗ. Không đưa mọi shader slider ra cùng lúc.
- **Bottom status:** trạng thái preview, seed, backend/quality, warning về capability; đây là nơi giải thích `Stage-only` hoặc `Synthetic AR`, không âm thầm bỏ effect.

## 2. Phân cấp thao tác

### Quick Adjust — mặc định cho người mới

```text
LOOKS  [Concert] [Neon] [Learning]
MASTER [Effect amount ─────●──]   QUALITY [Auto ▾]
LAYER  [Notes ✓] [Impact ✓] [Atmosphere ✓] [AR preview ○]
MOTION [Full ▾]                 FLASH [Reduced ▾]
```

- Chọn look trước; chỉ bốn điều khiển toàn cục cần chạm thường xuyên.
- Look card cho biết điều gì sẽ đổi (“warm beam + sparse dust”), không chỉ đưa tên theme.
- `Reset look` reset về default của look đang xem; `Reset all` có xác nhận nếu preset đã sửa.
- Chế độ Learning đặt atmosphere rất thấp, giữ nốt/keyboard nổi bật; không tự bật cả lớp decor.

### Advanced Studio — khi chọn một effect

```text
FALLING / Ribbon trail                                      [Enabled ✓]
  Trigger     Every falling note
  Material    Satin ribbon ▾
  Intensity   ───────────●────  62
  Length      ─────────●──────  48
  Blend       Behind note ▾
  Cost        MEDIUM     Support: Stage + Recording
  [Reset this effect]     [Compare A/B]
```

- Chỉ hiện tham số hợp lệ với effect đang chọn; các field khác ẩn, không chỉ disable mơ hồ.
- Slider có số, range, đơn vị/tooltip, keyboard focus, reset và feedback ngay trong preview.
- Mỗi option hiển thị runtime readiness sau khi Phase 2 metadata tồn tại: `Available`, `Stage-only`, `Planned` hoặc capability-specific. Đây là **yêu cầu tương lai**, không lấy `EffectStatus` hiện tại làm runtime filter.
- Hành vi có thể làm đổi preset sẽ báo dirty state và yêu cầu lưu bản sao; không thay schema ở Phase 1.

## 3. Luồng sử dụng chính

```text
Style / Theme
  → chọn look hoặc preset
  → Open Effects Studio
  → Quick Adjust: xem cùng scene ở 3 look
  → chọn group/effect
  → chỉnh một tham số và xem A/B
  → Save as new preset (không ghi đè mặc định)
  → quay lại stage / recording preview
```

### Hành vi tương tác

1. **Chọn look:** preview giữ frame time, seed, scene và viewport; chỉ palette/mix/material profile đổi.
2. **Scrub timeline:** preview dừng tại frame xác định; click checkpoint label để tới `0.8`, `4.1`, `6.1` hoặc `11.25` giây.
3. **Play:** sample rate hiển thị; pause/step/reset không thay đổi song position của người dùng.
4. **A/B:** ghi nhớ A; đổi một look/parameter; swipe/split comparison dùng cùng frame và crop. A/B không được chạy hai simulation độc lập với clock lệch nhau.
5. **Save as preset:** tên, mô tả, thumbnail và summary; xác nhận các capability không lưu vào preset máy như camera calibration/device.
6. **AR Surface:** chỉ hiện nếu có camera source/calibration hoặc synthetic reference; badge nói rõ `Synthetic`, `Calibrated surface` hay `Not available`. Không hiển thị capability từ `Available` status chung.

## 4. Responsive và trạng thái

| Kích thước | Bố cục |
|---|---|
| ≥1440 px | Ba pane đầy đủ; preview 16:9, trái 236 px, inspector 320 px. |
| 1280–1439 px | Inspector thu còn 288 px; library 216 px; preview không thấp hơn 720×405 px. |
| 1024–1279 px | Library thành rail icon/overlay; inspector thành drawer; preview vẫn là pane chính. |
| <1024 px | Không hứa full Studio; thông báo mở cửa sổ rộng hơn hoặc chỉ hiển thị Quick Adjust. Không nén ba cột thành text nhỏ. |

Trạng thái phải thiết kế: no MIDI/empty history, scene loading, device lost, unsupported effect, zero intensity, reduced motion, no AR calibration, dirty preset, render slow/quality downgraded. Error không được làm biến mất controls đã chỉnh.

## 5. Accessibility và localization

- Tab order: top bar → library/search → preview transport/timeline → inspector; focus không bị mất khi đổi look.
- Search có accessible name và clear; card công bố tên + phase + support badge; slider đọc value/units.
- Keyboard: `Space` play/pause khi focus không nằm trong text input; `←/→` step/scrub; `A/B` chỉ khi shortcut không xung đột; `Esc` đóng inspector/drawer theo thứ tự.
- Màu luôn có nhãn/shape/state; focus ring không lấy cùng màu với selection fill nếu làm mất tương phản.
- Mọi string, trạng thái, tooltip và thông báo có tiếng Việt/English; preset IDs và scene IDs không dịch.

## 6. Ranh giới với UI hiện hữu

Hiện app có 13 trang dock và nhóm control trong `MainWindow.Settings.cs`; wireframe này **không** đổi navigation hoặc sửa settings ở Phase 1. Khi Phase 7 triển khai, Studio trước hết nên thay/bao nhóm chỉnh visual trong Style/Notes/Particles/Keyboard/Background, còn Audio/MIDI/Practice/History/Recording/General vẫn là nơi riêng. Cần giữ deep-link hiện có và không nhân đôi owner của một setting.

`EffectCatalog` hiện chỉ là inventory; settings/defaults và UI choices là nguồn runtime. Trước khi UI đọc typed registry, Phase 2 phải chuẩn hóa ID/alias/capability, migration JSON và round-trip preset.

## 7. Checklist duyệt wireframe

- [ ] Preview chiếm ưu tiên rõ nhất; không bị inspector/library bóp nhỏ trên 1280×720.
- [ ] Quick và Advanced có ranh giới dễ hiểu; preset/look không bị nhầm với effect toggle.
- [ ] Timeline phát lại đúng fixture, pause/seek/reset được mà không đổi playback thật.
- [ ] A/B cùng frame/seed; có 3 profile trong cùng viewport.
- [ ] Capability/AR badges nói thật, có synthetic/unavailable state.
- [ ] Không mất setting cũ, shortcut, localization, JSON/preset khi triển khai sau.

**Quyết định chờ duyệt:** wireframe là đề xuất UX; chưa đánh dấu Phase 1 hoàn tất cho tới khi người dùng chọn/điều chỉnh profile và duyệt scene/cấu trúc Studio.
