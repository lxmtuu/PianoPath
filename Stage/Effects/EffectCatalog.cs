namespace PianoPath;

/// <summary>
/// The complete effects catalogue for the Keyflow stage: every effect the app offers (or plans to
/// offer), grouped the way the renderer and the settings UI think about them.
/// <para>
/// Design logic — 4 principles:
/// <list type="number">
/// <item><b>One note, 4 life phases.</b> Every note is born (Falling), lands (Impact), is held
/// (Hold) and dies (Release). A note effect belongs to exactly one phase, so the UI can show one
/// card per phase instead of 40 loose toggles.</item>
/// <item><b>Ambient layers are stage-wide.</b> Particle &amp; Energy, Nature, Light &amp; Color and
/// Cosmic effects do not follow notes; each family is one independent layer slot.</item>
/// <item><b>Smart effects are modulators, not renderers.</b> Velocity, pitch, octave, zone, pedal,
/// tempo and audio-reactive mappings only scale the parameters (size, color, intensity) of the
/// effects above — they never draw anything themselves.</item>
/// <item><b>Themes are preset graphs.</b> Fire, Ice, Galaxy… are not code; each one is a fixed
/// combination of (falling, impact, hold, release, ambient, modulators).</item>
/// </list>
/// </para>
/// <para>
/// Every rendered effect is tuned with the same universal sliders — Intensity, Size, Speed,
/// Amount — plus a color only when the effect truly needs one. Learn 4 sliders once, use them
/// everywhere. Effects marked <see cref="EffectStatus.Planned"/> are documented here first and
/// implemented phase by phase (see docs/EFFECTS-REDESIGN.md); nothing Planned is shown in the UI
/// until its renderer exists, so every visible setting always drives real logic.
/// </para>
/// </summary>
internal enum EffectStatus
{
    /// <summary>Fully implemented: settings, renderer, preset support and verification.</summary>
    Available,
    /// <summary>Catalogued with its future channel; not yet rendered or shown in the UI.</summary>
    Planned,
}

/// <summary>
/// One effect in the catalogue. <see cref="Channel"/> names the renderer channel that implements
/// it (or will): note channels (falling.*, impact.*, hold.*, release.*), ambient layers
/// (ambient.*), modulators (mod.*) or preset graphs (theme).
/// </summary>
internal sealed record EffectDefinition(string Id, string Name, string NameVi, string Description, EffectStatus Status, string Channel);

/// <summary>Falling-note effects, grouped by note life phase and ambient family.</summary>
internal static class EffectCatalog
{
    // =============================================================================================
    // Phase 1 · Falling — while the note travels toward the keys (v2: all IMPLEMENTED)
    // =============================================================================================
    internal static class Falling
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("falling.glow-trail", "Glow Trail", "Vệt sáng mờ", "Soft light trail dragging behind the falling note.", EffectStatus.Available, "falling.trail"),
            new("falling.motion-blur", "Motion Blur", "Mờ chuyển động", "Note body smears along its travel direction.", EffectStatus.Available, "falling.trail"),
            new("falling.sparkle-tail", "Sparkle Tail", "Đuôi tia sáng", "Tail emits small twinkling sparkles.", EffectStatus.Available, "falling.trail"),
            new("falling.color-gradient", "Color Gradient", "Chuyển sắc đầu–đuôi", "Note blends from one color at the head to another at the tail.", EffectStatus.Available, "falling.body"),
            new("falling.pulsing", "Pulsing", "Nhấp nháy", "Note breathes bright/dim while falling.", EffectStatus.Available, "falling.body"),
            new("falling.ribbon-twist", "Ribbon Twist", "Dải lụa xoắn", "Silk ribbon swaying behind the note along its path.", EffectStatus.Available, "falling.trail"),
            new("falling.particle-stream", "Particle Stream", "Dòng hạt", "Thin stream of particles pours off the falling note.", EffectStatus.Available, "falling.trail"),
            new("falling.ghost-notes", "Ghost Notes", "Bóng ma", "Faint echo copies lead the note (visual echo).", EffectStatus.Available, "falling.trail"),
            new("falling.speed-lines", "Speed Lines", "Vệt tốc độ", "Anime-style speed streaks behind fast notes.", EffectStatus.Available, "falling.trail"),
            new("falling.rainbow-shift", "Rainbow Shift", "Cầu vồng trượt", "Note hue cycles continuously through the rainbow.", EffectStatus.Available, "falling.body"),
        ];
    }

    // =============================================================================================
    // Phase 2 · Impact — the first ~0.5 s after the note lands (v1+v2: all IMPLEMENTED)
    // =============================================================================================
    internal static class Impact
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("impact.burst", "Burst / Explosion", "Nổ hạt", "Bright particle explosion at the hit point.", EffectStatus.Available, "impact.burst"),
            new("impact.ember-burst", "Ember Burst", "Than hồng bắn", "Hot ember sparks with blackbody cooling.", EffectStatus.Available, "impact.burst"),
            new("impact.ripple-ring", "Ripple Ring", "Vòng sóng", "Hollow acoustic ring expanding from the key.", EffectStatus.Available, "impact.wave"),
            new("impact.shockwave", "Shockwave", "Sóng xung kích", "Filled blast wave with a bright leading rim.", EffectStatus.Available, "impact.wave"),
            new("impact.flash", "Flash", "Chớp sáng", "White-hot flare at the hit point, fading in ~180 ms.", EffectStatus.Available, "impact.flash"),
            new("impact.key-glow", "Key Press Glow", "Phím rực sáng", "Key flares up on impact, then cools down.", EffectStatus.Available, "hold.glow"),
            new("impact.splash", "Splash", "Bắn tung tóe", "Liquid-like droplets scattering like splashed water.", EffectStatus.Available, "impact.burst"),
            new("impact.firework", "Firework", "Pháo hoa mini", "Mini firework rocket bursting above the key.", EffectStatus.Available, "impact.burst"),
            new("impact.confetti-pop", "Confetti Pop", "Giấy màu", "Mini confetti pop in the note color.", EffectStatus.Available, "impact.burst"),
            new("impact.dust-cloud", "Dust Cloud", "Đám bụi", "Small mist puff rising from the hit point.", EffectStatus.Available, "impact.burst"),
            new("impact.shatter", "Shatter / Break", "Vỡ kính", "Note shatters into glass shards on impact.", EffectStatus.Available, "impact.morph"),
            new("impact.bounce", "Bounce", "Nảy lên", "Note bounces once off the key, then dissolves.", EffectStatus.Available, "impact.morph"),
            new("impact.absorb", "Absorb", "Phím hút nốt", "Key sucks the note in and lights up.", EffectStatus.Available, "impact.morph"),
            new("impact.melt", "Melt", "Tan chảy", "Note melts like wax onto the key.", EffectStatus.Available, "impact.morph"),
            new("impact.lightning-strike", "Lightning Strike", "Sét đánh", "Lightning bolt strikes down onto the key.", EffectStatus.Available, "impact.flash"),
            new("impact.note-morph", "Note Morph", "Biến hình", "Note morphs into another shape (star, heart…).", EffectStatus.Available, "impact.morph"),
        ];
    }

    // =============================================================================================
    // Phase 3 · Hold — while the key is physically held down (v3: all IMPLEMENTED)
    // =============================================================================================
    internal static class Hold
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("hold.flame-pillar", "Flame Pillar", "Cột lửa", "Fire column sustained while the key is held.", EffectStatus.Available, "hold.column"),
            new("hold.sustain-particles", "Sustain Particles", "Hạt bay liên tục", "Bright particles continuously rising from the key.", EffectStatus.Available, "hold.column"),
            new("hold.energy-column", "Energy Column", "Cột năng lượng", "Light column standing over the sounding key.", EffectStatus.Available, "hold.column"),
            new("hold.bar", "Hold Bar", "Thanh giữ dài", "Long bar stretching with the hold duration.", EffectStatus.Available, "hold.bar"),
            new("hold.breathing-glow", "Breathing Glow", "Phím thở sáng", "Key rhythmically breathes bright/dim.", EffectStatus.Available, "hold.glow"),
            new("hold.vibration", "Vibration", "Rung nhẹ", "Note/key trembles subtly while held.", EffectStatus.Available, "hold.glow"),
            new("hold.color-cycle", "Color Cycle", "Đổi màu liên tục", "Key cycles hue continuously while held.", EffectStatus.Available, "hold.glow"),
            new("hold.electric-arc", "Electric Arc", "Tia điện nối phím", "Electric arcs linking the held keys.", EffectStatus.Available, "hold.link"),
        ];
    }

    // =============================================================================================
    // Phase 4 · Release — after the key is let go
    // =============================================================================================
    internal static class Release
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("release.fade-out", "Fade Out", "Mờ dần", "Note fades away smoothly.", EffectStatus.Available, "release"),
            new("release.float-up", "Float Up", "Bay lên", "Bright motes float skyward, then vanish.", EffectStatus.Planned, "release"),
            new("release.dissolve", "Dissolve", "Tan thành hạt", "Note disintegrates into pixels/particles.", EffectStatus.Planned, "release"),
            new("release.smoke-puff", "Smoke Puff", "Puff khói", "One small smoke puff drifts up.", EffectStatus.Planned, "release"),
            new("release.snap-back", "Snap Back", "Co rút", "Note snaps shut quickly, then disappears.", EffectStatus.Planned, "release"),
            new("release.echo-rings", "Echo Rings", "Vòng sóng dội", "A few small rings spread out, then die.", EffectStatus.Planned, "release"),
        ];
    }

    // =============================================================================================
    // Ambient · Particle & Energy — stage-wide energy layers
    // =============================================================================================
    internal static class AmbientEnergy
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("ambient.embers", "Embers", "Than hồng bay", "Glowing embers rising whenever keys are struck.", EffectStatus.Available, "impact.burst"),
            new("ambient.fire", "Fire / Flames", "Ngọn lửa", "Flames licking up from struck keys.", EffectStatus.Available, "hold.column"),
            new("ambient.explosion", "Explosion / Burst", "Vụ nổ hạt", "Heavy particle burst on high-velocity hits.", EffectStatus.Available, "impact.burst"),
            new("ambient.sparkles", "Sparkles / Stars", "Tia sáng lấp lánh", "Twinkling starfield behind the notes.", EffectStatus.Available, "ambient.light"),
            new("ambient.lightning", "Lightning / Electric", "Tia sét", "Electric arcs running along the keys.", EffectStatus.Planned, "ambient.particles"),
            new("ambient.laser", "Laser Beams", "Tia laser", "Laser beams firing out of struck keys.", EffectStatus.Planned, "hold.column"),
            new("ambient.plasma", "Plasma", "Quả cầu plasma", "Plasma energy orb blooming over the key.", EffectStatus.Available, "impact.flash"),
            new("ambient.confetti", "Confetti", "Giấy màu", "Celebration confetti raining over the stage.", EffectStatus.Planned, "ambient.particles"),
            new("ambient.firework", "Firework", "Pháo hoa", "Fireworks blooming above the keyboard.", EffectStatus.Planned, "ambient.particles"),
        ];
    }

    // =============================================================================================
    // Ambient · Nature & Elements
    // =============================================================================================
    internal static class AmbientNature
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("ambient.petals", "Petals / Cherry Blossom", "Cánh hoa anh đào", "Blossom petals drifting across the stage.", EffectStatus.Available, "ambient.nature"),
            new("ambient.ripple", "Water Ripple", "Gợn sóng nước", "Water ripples spreading from each hit.", EffectStatus.Planned, "impact.wave"),
            new("ambient.rain", "Rain / Droplets", "Mưa", "Raindrops falling in time with the notes.", EffectStatus.Planned, "ambient.nature"),
            new("ambient.snow", "Snow / Ice", "Tuyết / Băng", "Snowflakes falling; frost crusting the keys.", EffectStatus.Planned, "ambient.nature"),
            new("ambient.smoke", "Smoke / Fog", "Khói / Sương", "Smoke and fog curling upward.", EffectStatus.Planned, "ambient.nature"),
            new("ambient.leaves", "Wind / Leaves", "Lá bay", "Leaves tumbling along the wind.", EffectStatus.Planned, "ambient.nature"),
            new("ambient.butterflies", "Butterflies", "Đàn bướm", "Butterflies fluttering out of struck keys.", EffectStatus.Planned, "impact.burst"),
            new("ambient.aurora", "Aurora / Northern Lights", "Cực quang", "Aurora ribbons waving behind the stage.", EffectStatus.Planned, "ambient.light"),
            new("ambient.dust", "Dust Cloud", "Mây bụi", "Fine dust mist hanging in the air.", EffectStatus.Planned, "ambient.nature"),
        ];
    }

    // =============================================================================================
    // Ambient · Light & Color
    // =============================================================================================
    internal static class AmbientLight
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("ambient.glow", "Glow / Neon", "Neon phát sáng", "Neon light radiating from notes and keys.", EffectStatus.Available, "falling.body"),
            new("ambient.bloom", "Bloom", "Hào quang", "Global halo blooming around bright objects.", EffectStatus.Available, "ambient.light"),
            new("ambient.flash", "Flash", "Chớp trắng", "White flash washing the whole key.", EffectStatus.Available, "impact.flash"),
            new("ambient.splash", "Color Splash / Paint", "Bắn màu sơn", "Paint splashes thrown across the screen.", EffectStatus.Planned, "impact.burst"),
            new("ambient.rainbow-trail", "Rainbow Trail", "Dải cầu vồng", "Long rainbow ribbon trailing the melody.", EffectStatus.Available, "falling.trail"),
            new("ambient.gradient-wave", "Gradient Wave", "Sóng gradient", "Gradient color wave sweeping sideways.", EffectStatus.Planned, "ambient.light"),
            new("ambient.prism", "Prism / Crystal", "Lăng kính", "Light dispersing through a crystal prism.", EffectStatus.Planned, "ambient.light"),
        ];
    }

    // =============================================================================================
    // Ambient · Cosmic & Abstract
    // =============================================================================================
    internal static class AmbientCosmic
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("ambient.galaxy", "Galaxy / Nebula", "Tinh vân", "Deep-space nebula breathing behind the stage.", EffectStatus.Planned, "ambient.cosmic"),
            new("ambient.black-hole", "Black Hole", "Hố đen", "Spiral vortex sucking light toward its core.", EffectStatus.Planned, "ambient.cosmic"),
            new("ambient.matrix", "Matrix Rain", "Mưa ký tự", "Glyph rain falling Matrix-style.", EffectStatus.Planned, "ambient.cosmic"),
            new("ambient.geometry", "Geometric Shapes", "Hình khối", "Rotating geometric solids drifting by.", EffectStatus.Planned, "ambient.cosmic"),
            new("ambient.fractal", "Fractal", "Hoa văn fractal", "Fractal pattern blooming outward.", EffectStatus.Planned, "ambient.cosmic"),
            new("ambient.ribbon", "Ribbon / Trail", "Dải lụa", "Soft silk ribbon trailing the notes.", EffectStatus.Available, "falling.trail"),
        ];
    }

    // =============================================================================================
    // Smart modulators — scale parameters, never draw
    // =============================================================================================
    internal static class Smart
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("mod.velocity-size", "Velocity Size", "Lực → Kích thước", "Harder hits render bigger notes and bursts.", EffectStatus.Available, "mod.velocity"),
            new("mod.velocity-map", "Velocity Mapping", "Lực → Cường độ", "Effect strength follows the hit velocity.", EffectStatus.Available, "mod.velocity"),
            new("mod.key-color", "Key Color Mapping", "Mỗi nốt một màu", "Each pitch owns its color.", EffectStatus.Available, "mod.pitch"),
            new("mod.velocity-color", "Velocity Color", "Lực → Màu sắc", "Soft = blue, hard = red.", EffectStatus.Planned, "mod.velocity"),
            new("mod.octave-color", "Octave Color", "Mỗi quãng 8 một màu", "One color per octave.", EffectStatus.Planned, "mod.pitch"),
            new("mod.pedal-glow", "Pedal Glow", "Phím sáng theo pedal", "Keys glow while the sustain pedal is down.", EffectStatus.Planned, "mod.pedal"),
            new("mod.tempo-sync", "Tempo Sync", "Nháy theo nhịp", "Effects pulse on the song BPM.", EffectStatus.Planned, "mod.tempo"),
            new("mod.audio-reactive", "Audio Reactive", "Nhảy theo âm thanh", "Visuals react to the audio spectrum (FFT).", EffectStatus.Planned, "mod.audio"),
            new("mod.zone-split", "Zone Split FX", "Chia vùng Bass/Treble", "Bass = fire, treble = ice (split zones).", EffectStatus.Planned, "mod.zone"),
        ];
    }

    // =============================================================================================
    // Combo themes — preset graphs over (falling, impact, hold, release, ambient, modulators)
    // =============================================================================================
    internal static class Themes
    {
        internal static readonly IReadOnlyList<EffectDefinition> All =
        [
            new("theme.fire", "Fire Theme", "Chủ đề Lửa", "Fire bars → fire burst + embers → flame pillar → smoke.", EffectStatus.Planned, "theme"),
            new("theme.ice", "Ice Theme", "Chủ đề Băng", "Ice shards → shatter + frost → frozen keys → melt.", EffectStatus.Planned, "theme"),
            new("theme.galaxy", "Galaxy Theme", "Chủ đề Vũ trụ", "Meteors → supernova + ripples → nebula glow → stardust.", EffectStatus.Planned, "theme"),
            new("theme.sakura", "Sakura Theme", "Chủ đề Hoa anh đào", "Petals → petal splash → blooming glow → petals drifting off.", EffectStatus.Planned, "theme"),
            new("theme.electric", "Electric Theme", "Chủ đề Điện", "Sparks → lightning strike + shockwave → electric arcs → fading sparks.", EffectStatus.Planned, "theme"),
            new("theme.ocean", "Ocean Theme", "Chủ đề Đại dương", "Water drops → ripples + splash → water column → dissolving foam.", EffectStatus.Planned, "theme"),
            new("theme.retro", "Retro / 8-bit Theme", "Chủ đề Retro", "Pixel blocks → 8-bit burst → blinking → game-style snap.", EffectStatus.Planned, "theme"),
        ];
    }

    /// <summary>Every catalogued effect, in pipeline order (notes → ambient → modulators → themes).</summary>
    internal static IReadOnlyList<EffectDefinition> AllEffects { get; } =
    [
        .. Falling.All,
        .. Impact.All,
        .. Hold.All,
        .. Release.All,
        .. AmbientEnergy.All,
        .. AmbientNature.All,
        .. AmbientLight.All,
        .. AmbientCosmic.All,
        .. Smart.All,
        .. Themes.All,
    ];

    /// <summary>How many effects are fully implemented (used by docs and the verification suite).</summary>
    internal static int AvailableCount => AllEffects.Count(e => e.Status == EffectStatus.Available);
}
