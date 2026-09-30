using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PianoPath;

/// <summary>The values <see cref="PianoVisualSettings.RecordingFormat"/> accepts; stored as written.</summary>
internal static class RecordingFormatIds
{
    internal const string Avi = "Avi";
    internal const string PngSequence = "PngSequence";
    internal const string Mp4 = "Mp4";
}

/// <summary>Serializable, user-editable live-stage and note rendering controls.</summary>
internal sealed class PianoVisualSettings
{
    // ---- Layers -------------------------------------------------------------------------------------
    public bool ShowBackground { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
    /// <summary>The grand staff drawn above the roll, following the playhead (see <see cref="SheetLayer"/>).</summary>
    public bool ShowSheet { get; set; } = false;
    /// <summary>Record the piano's own audio next to the video, as a WAV beside the recording.</summary>
    public bool RecordAudio { get; set; } = true;

    // ---- Webcam overlay -----------------------------------------------------------------------------
    /// <summary>Draw a live camera (or a video file) over the stage as a picture-in-picture.</summary>
    public bool ShowCameraOverlay { get; set; } = false;
    /// <summary>Symbolic link of the camera to open; empty means the first camera of the machine.</summary>
    public string CameraSourceLink { get; set; } = "";
    /// <summary>A video file to use instead of a live camera; empty means the camera.</summary>
    public string CameraVideoPath { get; set; } = "";
    /// <summary>Corner the overlay sits in, one of <see cref="CameraOverlay.Corners"/>.</summary>
    public string CameraCorner { get; set; } = "Bottom left";
    /// <summary>Width of the overlay as a percentage of the stage width.</summary>
    public double CameraSize { get; set; } = 30;
    /// <summary>Opacity of the overlay, percent.</summary>
    public double CameraOpacity { get; set; } = 90;
    /// <summary>Mirror the picture, the way a camera pointed at the player should look.</summary>
    public bool CameraMirror { get; set; } = true;
    /// <summary>Chroma-key tolerance against pure green; zero turns keying off.</summary>
    public double CameraKeyTolerance { get; set; } = 30;
    /// <summary>
    /// Follow the hand the camera sees and mark the key it is over, with the fingers it holds up
    /// (see <see cref="HandTracker"/>). Its own layer: the picture can stay hidden while the keys are marked.
    /// </summary>
    public bool ShowHandTracking { get; set; } = false;
    /// <summary>
    /// How much of the picture counts as skin, 0 to 100. Higher accepts more colours, which is what a warm light
    /// or a dark room needs; lower keeps more of the background out of the count.
    /// </summary>
    public double HandTrackingSensitivity { get; set; } = 50;
    public bool ShowEmbers { get; set; } = true;
    public bool ShowHalo { get; set; } = true;
    public bool ShowFlame { get; set; } = true;
    public bool ShowKeys { get; set; } = true;
    public bool ShowWatermark { get; set; } = false;
    public bool ShowCounter { get; set; } = false;
    public bool ShowFps { get; set; } = false;
    public bool AnimateKeys { get; set; } = true;
    public bool Notes3D { get; set; } = true;
    public bool BackgroundGuide { get; set; }
    public bool BackgroundGradient { get; set; }
    public bool ShowStars { get; set; }
    public bool ShowWisps { get; set; } = false;
    public bool ShowImpactRings { get; set; } = true;
    public bool ShowLightBeams { get; set; } = false;
    public bool ShowNoteLabels { get; set; } = false;
    public bool ShowKeyFelt { get; set; } = false;
    /// <summary>Blossom petals drifting across the stage (the "Your Lie in April" layer).</summary>
    public bool ShowPetals { get; set; } = false;
    public double PetalAmount { get; set; } = 55;
    public string PetalColor { get; set; } = "#FFB3CF";
    public bool ShowKeyShadow { get; set; } = true;
    public int BackgroundAppearanceVersion { get; set; }

    // ---- Interface language --------------------------------------------------------------------------
    /// <summary>
    /// Interface language id (<c>en</c>, <c>vi</c>), or empty to follow the Windows display language.
    /// The language is an application preference, not part of a look: <see cref="CopyFrom"/> never
    /// copies it, so applying a preset cannot translate the interface behind the user's back.
    /// </summary>
    public string Language { get; set; } = "";

    // ---- Interface theme (the chrome around the stage) -----------------------------------------------
    /// <summary>Interface theme id: concert-grand, concert-noir or velvet-gold. See <see cref="ShellThemes"/>.</summary>
    public string ShellTheme { get; set; } = ShellThemes.DefaultId;
    /// <summary>Off, Calm or Full: how much the interface chrome animates.</summary>
    public string ChromeMotion { get; set; } = "Full";
    /// <summary>0-200 %: how many particles the animated chrome backdrop draws.</summary>
    public double BackdropDensity { get; set; } = 100;

    // ---- Style / preset -----------------------------------------------------------------------------
    /// <summary>Name of the preset the current values were derived from (informational only).</summary>
    public string PresetName { get; set; } = "Neon Violet";
    /// <summary>True once any value was edited after the preset was applied.</summary>
    public bool PresetModified { get; set; }

    // ---- Colors -------------------------------------------------------------------------------------
    /// <summary>Gradient, PerHand, PerTrack, RainbowPitch or RainbowTime.</summary>
    public string ColorMode { get; set; } = "Gradient";
    public string Palette { get; set; } = "Spectrum";
    public string NoteColorStart { get; set; } = "#43E6FF";
    public string NoteColorEnd { get; set; } = "#D95EFF";
    public string LeftHandColor { get; set; } = "#3FA9FF";
    public string RightHandColor { get; set; } = "#FF6FD8";
    public string HaloColor { get; set; } = "#C66EFF";
    public double HaloIntensity { get; set; } = 90;
    /// <summary>GPU stage: bright pulses of the halo colour travelling along the hit line.</summary>
    public bool HaloPulse { get; set; } = false;
    /// <summary>Brightness of the travelling halo pulses (0-100).</summary>
    public double HaloPulseIntensity { get; set; } = 50;
    public string PressedKeyColor { get; set; } = "#F782FF";
    public string KeyFeltColor { get; set; } = "#C41C4A";
    public string BackgroundColor { get; set; } = "#000000";
    public List<string> TrackColors { get; set; } = ["#43E6FF", "#FF6FD8", "#FFD166", "#7CFF6B", "#FF7A59", "#8C7BFF", "#5CF2E8", "#FF4D8D"];
    public double HandSplitPitch { get; set; } = 60;
    public double RainbowSpeed { get; set; } = 30;

    // ---- Practice session ---------------------------------------------------------------------------
    /// <summary>Slows the song down after a run of misses and speeds it back up as the run goes well.</summary>
    /// <summary>Chooses the hand split point from the notes of each song when it opens.</summary>
    public bool InferHandSplit { get; set; }
    public bool PracticeAutoTempo { get; set; }
    /// <summary>Misses in a row that trigger one slow-down step while <see cref="PracticeAutoTempo"/> is on.</summary>
    public int PracticeMissThreshold { get; set; } = 3;

    // ---- Note shape ---------------------------------------------------------------------------------
    /// <summary>Solid, Neon (hollow glowing outline), Glass or Fire (burning texture).</summary>
    public string NoteStyle { get; set; } = "Neon";
    public double NoteWidth { get; set; } = 80;
    public double NoteMinLength { get; set; } = 16;
    public double NoteGap { get; set; } = 2;
    public double NoteHeadGlow { get; set; } = 40;
    public double NoteTexture { get; set; } = 60;
    public double NoteTint { get; set; } = 78;
    public double NoteGlow { get; set; } = 100;
    public double NoteEdge { get; set; } = 88;
    public double NoteRefraction { get; set; } = 35;
    public double NoteRoundness { get; set; } = 70;
    public double NoteEdgeWidth { get; set; } = 55;
    public double NoteFallSpeed { get; set; } = 550;
    /// <summary>Down: notes fall onto the keys and sink below the hit line. Up: notes rise from the keys toward the top of the stage.</summary>
    public string NoteDirection { get; set; } = "Down";
    // ---- Falling phase FX (while the note travels) ------------------------------------------------
    /// <summary>Trail behind falling notes: None, Glow, Sparkles, Speed Lines, Blur, Ribbon, Rainbow or Stream.</summary>
    public string FallingTrail { get; set; } = "None";
    /// <summary>Brightness of the falling trail (0-100 %).</summary>
    public double FallingTrailIntensity { get; set; } = 70;
    /// <summary>How far the trail reaches behind the note (0-100 % of its height).</summary>
    public double FallingTrailLength { get; set; } = 55;
    /// <summary>Notes breathe bright/dim while falling.</summary>
    public bool FallingPulse { get; set; } = false;
    /// <summary>Speed of the falling pulse (0-100).</summary>
    public double FallingPulseRate { get; set; } = 40;
    /// <summary>Faint echo copies lead each falling note.</summary>
    public bool FallingGhost { get; set; } = false;
    /// <summary>Visibility and number of the echo copies (0-100).</summary>
    public double FallingGhostAmount { get; set; } = 40;
    /// <summary>GPU stage: a light sheen sweeps along every travelling bar.</summary>
    public bool NoteShimmer { get; set; } = false;
    /// <summary>Strength of the sweeping sheen (0-100).</summary>
    public double NoteShimmerAmount { get; set; } = 45;
    /// <summary>GPU stage: occasional meteors cross the sky behind the notes.</summary>
    public bool ShootingStars { get; set; } = false;
    /// <summary>How often a meteor crosses (0-100).</summary>
    public double ShootingStarsAmount { get; set; } = 50;

    // ---- Particles: sparks --------------------------------------------------------------------------
    public double EmitterSize { get; set; } = 24;
    public double Spiral { get; set; } = 30;
    public double ParticleSpeed { get; set; } = 100;
    public double ParticleAmount { get; set; } = 34;
    public double ParticleVelocity { get; set; } = 210;
    public double ParticleRandomness { get; set; } = 38;
    public double ParticleSpread { get; set; } = 72;
    public double ParticleResponse { get; set; } = 55;
    public double ParticleLife { get; set; } = .9;
    public double ParticleLifeRandomness { get; set; } = 45;
    public double ParticleSize { get; set; } = 1.6;
    public double ParticleSizeRandomness { get; set; } = 80;
    public double ParticleGlow { get; set; } = 85;
    public double Gravity { get; set; } = 290;
    public double Drag { get; set; } = 26;
    public double VectorField { get; set; } = 36;
    public double FieldScale { get; set; } = 100;
    public double EvolutionSpeed { get; set; } = 100;
    public double PhysicsTimeFactor { get; set; } = 100;

    // ---- Particles: wisps, flames, rings ------------------------------------------------------------
    public double WispAmount { get; set; } = 45;
    public double WispSpeed { get; set; } = 170;
    public double WispHeight { get; set; } = 55;
    public double WispWidth { get; set; } = 40;
    public double WispTurbulence { get; set; } = 55;
    public double WispGlow { get; set; } = 80;
    public double FlameIntensity { get; set; } = 70;
    public double FlameHeight { get; set; } = 60;
    /// <summary>Warm (classic fire) or Note (flame takes the note color).</summary>
    public string FlameColorMode { get; set; } = "Warm";
    public double RingSize { get; set; } = 50;
    // ---- Impact phase FX (hit moment): wave channel + flash channel ---------------------------------
    // The full catalogue lives in Stage/Effects/EffectCatalog.cs; these are the v1 implemented channels.
    /// <summary>Impact wave style: None, Ring (hollow acoustic ring) or Shockwave (filled blast wave).</summary>
    public string ImpactWave { get; set; } = "Ring";
    /// <summary>Brightness of the impact wave (0-150 %).</summary>
    public double ImpactWaveIntensity { get; set; } = 100;
    /// <summary>White-hot flare at the hit point, fading in about 180 ms.</summary>
    public bool ShowImpactFlash { get; set; } = true;
    /// <summary>Brightness of the impact flash (0-100 %).</summary>
    public double ImpactFlashIntensity { get; set; } = 80;
    /// <summary>Burst style: Embers, Splash, Fireworks, Confetti or Dust.</summary>
    public string ImpactBurst { get; set; } = "Embers";
    /// <summary>What the note becomes on impact: None, Shatter, Melt, Absorb, Bounce or Morph.</summary>
    public string ImpactMorph { get; set; } = "None";
    /// <summary>Strength of the impact morph (0-100 %).</summary>
    public double ImpactMorphIntensity { get; set; } = 70;
    /// <summary>Flash style: Flash, Lightning or Plasma.</summary>
    public string ImpactFlashStyle { get; set; } = "Flash";
    // ---- Hold phase FX (while the key is held) ----------------------------------------------------
    /// <summary>The sounding bar burns brighter with a hot outline while held.</summary>
    public bool HoldBar { get; set; } = false;
    /// <summary>Strength of the hold-bar highlight (0-100 %).</summary>
    public double HoldBarIntensity { get; set; } = 60;
    /// <summary>Held keys and notes rhythmically breathe bright/dim.</summary>
    public bool HoldBreath { get; set; } = false;
    /// <summary>Speed of the breathing (0-100).</summary>
    public double HoldBreathRate { get; set; } = 35;
    /// <summary>Held notes tremble subtly.</summary>
    public bool HoldVibration { get; set; } = false;
    /// <summary>Strength of the vibration (0-100).</summary>
    public double HoldVibrationAmount { get; set; } = 40;
    /// <summary>Held notes cycle hue continuously.</summary>
    public bool HoldColorCycle { get; set; } = false;
    /// <summary>Speed of the color cycling (0-100).</summary>
    public double HoldColorCycleSpeed { get; set; } = 45;
    /// <summary>Electric arcs chain simultaneously held keys.</summary>
    public bool HoldElectricArc { get; set; } = false;
    /// <summary>Brightness of the electric arcs (0-100 %).</summary>
    public double HoldArcIntensity { get; set; } = 70;
    // ---- Release phase FX (when the note ends) ----------------------------------------------------
    /// <summary>What happens at the key when a note ends: Fade, Float Up, Dissolve, Smoke, Snap Back or Echo Rings.</summary>
    public string ReleaseEffect { get; set; } = "Fade";
    /// <summary>Strength of the release effect (0-100 %).</summary>
    public double ReleaseIntensity { get; set; } = 70;
    // ---- Ambient layers (stage-wide, behind the notes) ------------------------------------------------
    /// <summary>Particle &amp; Energy layer: None, Lightning Storm, Laser Beams, Confetti Rain or Fireworks.</summary>
    public string AmbientEnergy { get; set; } = "None";
    public double AmbientEnergyAmount { get; set; } = 60;
    public double AmbientEnergySpeed { get; set; } = 50;
    /// <summary>Nature layer: None, Rain, Snow, Smoke, Leaves, Butterflies, Dust or Aurora.</summary>
    public string AmbientNature { get; set; } = "None";
    public double AmbientNatureAmount { get; set; } = 60;
    public double AmbientNatureSpeed { get; set; } = 50;
    /// <summary>Light &amp; Color layer: None, Gradient Wave, Prism or Color Splash.</summary>
    public string AmbientLight { get; set; } = "None";
    public double AmbientLightAmount { get; set; } = 60;
    public double AmbientLightSpeed { get; set; } = 50;
    public string AmbientLightColor { get; set; } = "#7B5CFF";
    /// <summary>Cosmic layer: None, Galaxy, Black Hole, Matrix Rain, Geometric or Fractal.</summary>
    public string AmbientCosmic { get; set; } = "None";
    public double AmbientCosmicAmount { get; set; } = 60;
    public double AmbientCosmicSpeed { get; set; } = 50;
    // ---- Smart modulators (scale parameters, never draw) ------------------------------------------------
    /// <summary>Velocity colors notes and bursts: soft hits cool blue, hard hits hot red.</summary>
    public bool VelocityColor { get; set; } = false;
    public double VelocityColorAmount { get; set; } = 70;
    /// <summary>Each octave owns a slice of the rainbow.</summary>
    public bool OctaveColor { get; set; } = false;
    public double OctaveColorBlend { get; set; } = 70;
    /// <summary>Keys glow brighter while the sustain pedal is down.</summary>
    public bool PedalGlow { get; set; } = false;
    public double PedalGlowIntensity { get; set; } = 60;
    /// <summary>Glow pulses on every beat of the MIDI tempo map.</summary>
    public bool TempoSync { get; set; } = false;
    public double TempoSyncAmount { get; set; } = 60;
    /// <summary>Glow follows the musical energy envelope (note onsets).</summary>
    public bool AudioReactive { get; set; } = false;
    public double AudioReactiveAmount { get; set; } = 60;
    /// <summary>Bass zone erupts fire, treble zone splashes ice.</summary>
    public bool ZoneSplit { get; set; } = false;
    public double ZoneSplitPitch { get; set; } = 60;
    public double ZoneSplitAmount { get; set; } = 70;

    // ---- Keyboard -----------------------------------------------------------------------------------
    /// <summary>Classic, Studio (3D) or Glass.</summary>
    public string KeyboardStyle { get; set; } = "Studio";
    public double KeyboardScale { get; set; } = 100;
    /// <summary>Note (pressed keys take the note color) or Fixed (PressedKeyColor).</summary>
    public string PressedKeyColorMode { get; set; } = "Note";
    /// <summary>None, C (octave labels) or All.</summary>
    public string KeyLabels { get; set; } = "C";
    public double KeyLighting { get; set; } = 32;
    public double KeyGlowRadius { get; set; } = 50;
    public double KeyOverhang { get; set; } = 18;
    public double KeyPressDepth { get; set; } = 40;

    // ---- Ray-traced shading -----------------------------------------------------------------------
    /// <summary>Off (flat vector keys), Fast, Balanced or Cinematic.</summary>
    public string ShadingQuality { get; set; } = "Balanced";
    /// <summary>Intensity of the softbox above the keyboard.</summary>
    public double ShaderKeyLight { get; set; } = 92;
    /// <summary>How dark the shadows the black keys cast on the white keys are.</summary>
    public double ShaderShadows { get; set; } = 78;
    /// <summary>Contact occlusion in the gaps between keys.</summary>
    public double ShaderAmbientOcclusion { get; set; } = 70;
    /// <summary>Polish of the ivory and ebony; higher means tighter highlights.</summary>
    public double ShaderGloss { get; set; } = 72;
    /// <summary>Accent rim light rising from behind the fallboard.</summary>
    public double ShaderRimLight { get; set; } = 70;
    /// <summary>How strongly a sounding key glows and spills its color onto its neighbours.</summary>
    public double ShaderEmissive { get; set; } = 95;
    /// <summary>Exposure applied before the filmic tonemapper.</summary>
    public double ShaderExposure { get; set; } = 105;
    /// <summary>0 is a flat top-down bed, 100 a low camera with strong perspective.</summary>
    public double ShaderCameraTilt { get; set; } = 48;
    /// <summary>ACES filmic curve, the tonemapper Unreal selects by default.</summary>
    public bool ShaderFilmic { get; set; } = true;

    // ---- Background & camera ------------------------------------------------------------------------
    /// <summary>Solid, Image or ChromaGreen (pure green stage for OBS chroma keying).</summary>
    public string BackgroundMode { get; set; } = "Solid";
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundDim { get; set; } = 30;
    public double Vignette { get; set; } = 25;
    public double StarDensity { get; set; } = 50;
    public double HorizonGlow { get; set; } = 35;
    public double BeamIntensity { get; set; } = 45;
    public double CameraParallax { get; set; } = 24;
    public double CameraZoom { get; set; } = 100;
    public double CameraOffset { get; set; } = 50;
    public double Saturation { get; set; } = 100;
    public double Contrast { get; set; } = 100;
    public double BloomIntensity { get; set; } = 80;
    public double BloomSize { get; set; } = 70;

    // ---- Recording ----------------------------------------------------------------------------------
    /// <summary>Window, 720p or 1080p.</summary>
    public string RecordingResolution { get; set; } = "Window";
    public double RecordingFrameRate { get; set; } = 30;
    /// <summary>
    /// What REC writes: <c>Avi</c> for a video file, <c>PngSequence</c> for a folder of 32-bit frames with
    /// an alpha channel (see <see cref="IFrameRecorder"/>). An unknown value falls back to AVI.
    /// </summary>
    public string RecordingFormat { get; set; } = RecordingFormatIds.Avi;
    /// <summary>
    /// PNG sequence only: draw the stage without its opaque background so the frames keep their alpha.
    /// Every layer the look enables is still drawn; what is skipped is the fill that would block it.
    /// </summary>
    public bool RecordingTransparent { get; set; } = true;
    /// <summary>
    /// Which engine draws the stage: <c>Gpu</c> (Direct3D 11 on a render thread of its own; see
    /// <see cref="GpuRenderLoop"/>) replaced <c>Software</c> as the stage of the main window. The WPF
    /// renderer is no longer a choice — it only steps in automatically when Direct3D cannot start and
    /// for transparent PNG takes. The property stays in the file format so older settings files load;
    /// <see cref="Clamp"/> migrates any stored value to <c>Gpu</c>. It belongs to the machine, not to a
    /// look, so applying a preset keeps it.
    /// </summary>
    public string RenderBackend { get; set; } = "Gpu";
    /// <summary>GPU engine: frames per second the render thread aims for (<c>Unlimited</c> renders as fast as the GPU allows).</summary>
    public string GpuFrameRate { get; set; } = "144";
    /// <summary>GPU stage window: present on the display's vertical blank (off allows tearing for the lowest latency).</summary>
    public bool GpuVSync { get; set; } = true;

    internal static readonly string[] ColorModes = ["Gradient", "PerHand", "PerTrack", "RainbowPitch", "RainbowTime"];
    internal static readonly string[] ChromeMotions = ["Off", "Calm", "Full"];
    internal static readonly string[] Palettes = ["Spectrum", "Aurora", "Fire", "Ocean", "Violet", "Custom"];
    internal static readonly string[] NoteStyles = ["Solid", "Neon", "Glass", "Fire"];
    internal static readonly string[] NoteDirections = ["Down", "Up"];
    internal static readonly string[] FlameColorModes = ["Warm", "Note"];
    internal static readonly string[] ImpactWaves = ["None", "Ring", "Shockwave", "Ripple"];
    internal static readonly string[] AmbientEnergies = ["None", "Lightning Storm", "Laser Beams", "Confetti Rain", "Fireworks"];
    internal static readonly string[] AmbientNatures = ["None", "Rain", "Snow", "Smoke", "Leaves", "Butterflies", "Dust", "Aurora"];
    internal static readonly string[] AmbientLights = ["None", "Gradient Wave", "Prism", "Color Splash", "Spotlights"];
    internal static readonly string[] AmbientCosmics = ["None", "Galaxy", "Black Hole", "Matrix Rain", "Geometric", "Fractal"];
    internal static readonly string[] FallingTrails = ["None", "Glow", "Sparkles", "Speed Lines", "Blur", "Ribbon", "Rainbow", "Stream"];
    internal static readonly string[] ImpactBursts = ["Embers", "Splash", "Fireworks", "Confetti", "Dust"];
    internal static readonly string[] ImpactMorphs = ["None", "Shatter", "Melt", "Absorb", "Bounce", "Morph"];
    internal static readonly string[] ImpactFlashStyles = ["Flash", "Lightning", "Plasma"];
    internal static readonly string[] ReleaseEffects = ["Fade", "Float Up", "Dissolve", "Smoke", "Snap Back", "Echo Rings"];
    internal static readonly string[] KeyboardStyles = ["Classic", "Studio", "Glass"];
    internal static readonly string[] ShadingQualities = ["Off", "Fast", "Balanced", "Cinematic"];
    internal static readonly string[] PressedKeyColorModes = ["Note", "Fixed"];
    internal static readonly string[] KeyLabelModes = ["None", "C", "All"];
    internal static readonly string[] BackgroundModes = ["Solid", "Image", "ChromaGreen"];
    internal static readonly string[] RecordingResolutions = ["Window", "720p", "1080p"];
    internal static readonly string[] GpuFrameRates = ["60", "120", "144", "240", "Unlimited"];
    internal static readonly string[] RecordingFormats = [RecordingFormatIds.Avi, RecordingFormatIds.PngSequence, RecordingFormatIds.Mp4];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    internal static PianoVisualSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<PianoVisualSettings>(json, JsonOptions) ?? new PianoVisualSettings();
        settings.ApplyMigrations();
        settings.Clamp();
        return settings;
    }

    /// <summary>The GPU engine's target frame rate as a number; 0 means unlimited.</summary>
    internal int GpuTargetFps => int.TryParse(GpuFrameRate, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var fps) ? fps : 0;

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deep copy used by the preset system so a preset can be applied without sharing list instances.</summary>
    internal PianoVisualSettings Clone() => FromJson(ToJson());

    /// <summary>Copies every value from <paramref name="source"/> into this instance, so the renderer keeps its existing reference.</summary>
    internal void CopyFrom(PianoVisualSettings source, bool keepBackgroundImage = true)
    {
        var image = BackgroundImagePath;
        foreach (var property in typeof(PianoVisualSettings).GetProperties())
        {
            if (!property.CanWrite || !property.CanRead) continue;
            // The interface language belongs to the person, not to the look being applied.
            if (property.Name == nameof(Language)) continue;
            // So does the graphics engine: it depends on the machine's GPU, not on the look.
            if (property.Name is nameof(RenderBackend) or nameof(GpuFrameRate) or nameof(GpuVSync)) continue;
            var value = property.GetValue(source);
            property.SetValue(this, value is List<string> list ? new List<string>(list) : value);
        }
        if (keepBackgroundImage) BackgroundImagePath = image;
        ApplyMigrations(); Clamp();
    }

    internal void Clamp()
    {
        NoteTint = Math.Clamp(NoteTint, 0, 100); NoteGlow = Math.Clamp(NoteGlow, 0, 200); NoteEdge = Math.Clamp(NoteEdge, 0, 200);
        NoteRefraction = Math.Clamp(NoteRefraction, 0, 100); NoteRoundness = Math.Clamp(NoteRoundness, 0, 100); NoteEdgeWidth = Math.Clamp(NoteEdgeWidth, 0, 100);
        NoteFallSpeed = Math.Clamp(NoteFallSpeed, 100, 1000); EmitterSize = Math.Clamp(EmitterSize, 0, 100); Spiral = Math.Clamp(Spiral, 0, 100);
        NoteWidth = Math.Clamp(NoteWidth, 30, 100); NoteMinLength = Math.Clamp(NoteMinLength, 4, 60); NoteGap = Math.Clamp(NoteGap, 0, 12);
        NoteHeadGlow = Math.Clamp(NoteHeadGlow, 0, 100); NoteTexture = Math.Clamp(NoteTexture, 0, 100);
        ParticleSpeed = Math.Clamp(ParticleSpeed, 0, 300); ParticleAmount = Math.Clamp(ParticleAmount, 0, 120); ParticleVelocity = Math.Clamp(ParticleVelocity, 0, 800);
        ParticleRandomness = Math.Clamp(ParticleRandomness, 0, 100); ParticleSpread = Math.Clamp(ParticleSpread, 0, 100); ParticleResponse = Math.Clamp(ParticleResponse, 0, 100);
        ParticleLife = Math.Clamp(ParticleLife, .05, 3); ParticleLifeRandomness = Math.Clamp(ParticleLifeRandomness, 0, 100); ParticleSize = Math.Clamp(ParticleSize, .2, 16);
        ParticleSizeRandomness = Math.Clamp(ParticleSizeRandomness, 0, 100); ParticleGlow = Math.Clamp(ParticleGlow, 0, 200); Gravity = Math.Clamp(Gravity, -600, 1200);
        Drag = Math.Clamp(Drag, 0, 100); VectorField = Math.Clamp(VectorField, 0, 1000); FieldScale = Math.Clamp(FieldScale, 10, 300); EvolutionSpeed = Math.Clamp(EvolutionSpeed, 0, 400);
        PhysicsTimeFactor = Math.Clamp(PhysicsTimeFactor, 10, 300);
        WispAmount = Math.Clamp(WispAmount, 0, 150); WispSpeed = Math.Clamp(WispSpeed, 20, 600); WispHeight = Math.Clamp(WispHeight, 5, 100);
        WispWidth = Math.Clamp(WispWidth, 0, 100); WispTurbulence = Math.Clamp(WispTurbulence, 0, 100); WispGlow = Math.Clamp(WispGlow, 0, 200);
        FlameIntensity = Math.Clamp(FlameIntensity, 0, 100); FlameHeight = Math.Clamp(FlameHeight, 0, 100); RingSize = Math.Clamp(RingSize, 0, 100);
        ImpactWaveIntensity = Math.Clamp(ImpactWaveIntensity, 0, 150); ImpactFlashIntensity = Math.Clamp(ImpactFlashIntensity, 0, 100);
        FallingTrailIntensity = Math.Clamp(FallingTrailIntensity, 0, 100); FallingTrailLength = Math.Clamp(FallingTrailLength, 0, 100);
        FallingPulseRate = Math.Clamp(FallingPulseRate, 0, 100); FallingGhostAmount = Math.Clamp(FallingGhostAmount, 0, 100);
        NoteShimmerAmount = Math.Clamp(NoteShimmerAmount, 0, 100); HaloPulseIntensity = Math.Clamp(HaloPulseIntensity, 0, 100);
        ShootingStarsAmount = Math.Clamp(ShootingStarsAmount, 0, 100);
        ImpactMorphIntensity = Math.Clamp(ImpactMorphIntensity, 0, 100);
        HoldBarIntensity = Math.Clamp(HoldBarIntensity, 0, 100); HoldBreathRate = Math.Clamp(HoldBreathRate, 0, 100);
        HoldVibrationAmount = Math.Clamp(HoldVibrationAmount, 0, 100); HoldColorCycleSpeed = Math.Clamp(HoldColorCycleSpeed, 0, 100);
        HoldArcIntensity = Math.Clamp(HoldArcIntensity, 0, 100);
        ReleaseIntensity = Math.Clamp(ReleaseIntensity, 0, 100);
        AmbientEnergyAmount = Math.Clamp(AmbientEnergyAmount, 0, 100); AmbientEnergySpeed = Math.Clamp(AmbientEnergySpeed, 0, 100);
        AmbientNatureAmount = Math.Clamp(AmbientNatureAmount, 0, 100); AmbientNatureSpeed = Math.Clamp(AmbientNatureSpeed, 0, 100);
        AmbientLightAmount = Math.Clamp(AmbientLightAmount, 0, 100); AmbientLightSpeed = Math.Clamp(AmbientLightSpeed, 0, 100);
        AmbientCosmicAmount = Math.Clamp(AmbientCosmicAmount, 0, 100); AmbientCosmicSpeed = Math.Clamp(AmbientCosmicSpeed, 0, 100);
        VelocityColorAmount = Math.Clamp(VelocityColorAmount, 0, 100); OctaveColorBlend = Math.Clamp(OctaveColorBlend, 0, 100);
        PedalGlowIntensity = Math.Clamp(PedalGlowIntensity, 0, 100); TempoSyncAmount = Math.Clamp(TempoSyncAmount, 0, 100);
        AudioReactiveAmount = Math.Clamp(AudioReactiveAmount, 0, 100);
        ZoneSplitPitch = Math.Clamp(ZoneSplitPitch, 21, 108); ZoneSplitAmount = Math.Clamp(ZoneSplitAmount, 0, 100);
        HaloIntensity = Math.Clamp(HaloIntensity, 0, 200);
        PetalAmount = Math.Clamp(PetalAmount, 0, 150);
        BackdropDensity = Math.Clamp(BackdropDensity, 0, 200);
        KeyboardScale = Math.Clamp(KeyboardScale, 60, 140); KeyLighting = Math.Clamp(KeyLighting, 0, 100); KeyGlowRadius = Math.Clamp(KeyGlowRadius, 0, 100);
        KeyOverhang = Math.Clamp(KeyOverhang, 0, 100); KeyPressDepth = Math.Clamp(KeyPressDepth, 0, 100);
        ShaderKeyLight = Math.Clamp(ShaderKeyLight, 0, 200); ShaderShadows = Math.Clamp(ShaderShadows, 0, 100);
        ShaderAmbientOcclusion = Math.Clamp(ShaderAmbientOcclusion, 0, 100); ShaderGloss = Math.Clamp(ShaderGloss, 0, 100);
        ShaderRimLight = Math.Clamp(ShaderRimLight, 0, 150); ShaderEmissive = Math.Clamp(ShaderEmissive, 0, 200);
        ShaderExposure = Math.Clamp(ShaderExposure, 20, 250); ShaderCameraTilt = Math.Clamp(ShaderCameraTilt, 0, 100);
        HandSplitPitch = Math.Clamp(Math.Round(HandSplitPitch), 21, 108); RainbowSpeed = Math.Clamp(RainbowSpeed, 0, 100);
        PracticeMissThreshold = Math.Clamp(PracticeMissThreshold, 1, 6);
        CameraParallax = Math.Clamp(CameraParallax, 0, 100); CameraZoom = Math.Clamp(CameraZoom, 65, 150);
        CameraOffset = Math.Clamp(CameraOffset, 0, 100); BackgroundDim = Math.Clamp(BackgroundDim, 0, 100); Saturation = Math.Clamp(Saturation, 0, 200);
        Contrast = Math.Clamp(Contrast, 0, 200); BloomIntensity = Math.Clamp(BloomIntensity, 0, 150); BloomSize = Math.Clamp(BloomSize, 0, 150);
        Vignette = Math.Clamp(Vignette, 0, 100); StarDensity = Math.Clamp(StarDensity, 0, 100); HorizonGlow = Math.Clamp(HorizonGlow, 0, 100); BeamIntensity = Math.Clamp(BeamIntensity, 0, 100);
        RecordingFrameRate = Math.Clamp(Math.Round(RecordingFrameRate), 15, 60);
        if (!Palettes.Contains(Palette)) Palette = "Spectrum";
        if (!ColorModes.Contains(ColorMode)) ColorMode = "Gradient";
        if (!NoteStyles.Contains(NoteStyle)) NoteStyle = "Neon";
        if (!NoteDirections.Contains(NoteDirection)) NoteDirection = "Down";
        if (!FlameColorModes.Contains(FlameColorMode)) FlameColorMode = "Warm";
        if (!ImpactWaves.Contains(ImpactWave)) ImpactWave = "Ring";
        if (!FallingTrails.Contains(FallingTrail)) FallingTrail = "None";
        if (!ImpactBursts.Contains(ImpactBurst)) ImpactBurst = "Embers";
        if (!ImpactMorphs.Contains(ImpactMorph)) ImpactMorph = "None";
        if (!ImpactFlashStyles.Contains(ImpactFlashStyle)) ImpactFlashStyle = "Flash";
        if (!ReleaseEffects.Contains(ReleaseEffect)) ReleaseEffect = "Fade";
        if (!AmbientEnergies.Contains(AmbientEnergy)) AmbientEnergy = "None";
        if (!AmbientNatures.Contains(AmbientNature)) AmbientNature = "None";
        if (!AmbientLights.Contains(AmbientLight)) AmbientLight = "None";
        if (!AmbientCosmics.Contains(AmbientCosmic)) AmbientCosmic = "None";
        if (!KeyboardStyles.Contains(KeyboardStyle)) KeyboardStyle = "Studio";
        if (!ShadingQualities.Contains(ShadingQuality)) ShadingQuality = "Balanced";
        if (!PressedKeyColorModes.Contains(PressedKeyColorMode)) PressedKeyColorMode = "Note";
        if (!KeyLabelModes.Contains(KeyLabels)) KeyLabels = "C";
        if (!BackgroundModes.Contains(BackgroundMode)) BackgroundMode = "Solid";
        if (!RecordingResolutions.Contains(RecordingResolution)) RecordingResolution = "Window";
        if (!RecordingFormats.Contains(RecordingFormat)) RecordingFormat = RecordingFormatIds.Avi;
        // The GPU stage replaced the software stage as the main window's look: a stored "Software"
        // (or anything else a file may name) migrates to the GPU engine on load.
        RenderBackend = "Gpu";
        if (!GpuFrameRates.Contains(GpuFrameRate)) GpuFrameRate = "144";
        if (!CameraOverlay.Corners.Contains(CameraCorner)) CameraCorner = CameraOverlay.Corners[0];
        CameraSize = Math.Clamp(CameraSize, 15, 60); CameraOpacity = Math.Clamp(CameraOpacity, 20, 100);
        CameraKeyTolerance = Math.Clamp(CameraKeyTolerance, 0, 100);
        HandTrackingSensitivity = Math.Clamp(HandTrackingSensitivity, 0, 100);
        if (!ChromeMotions.Contains(ChromeMotion)) ChromeMotion = "Full";
        if (string.IsNullOrWhiteSpace(ShellTheme)) ShellTheme = ShellThemes.DefaultId;
        TrackColors ??= [];
        var defaults = new List<string> { "#43E6FF", "#FF6FD8", "#FFD166", "#7CFF6B", "#FF7A59", "#8C7BFF", "#5CF2E8", "#FF4D8D" };
        for (var i = 0; i < 8; i++) if (TrackColors.Count <= i) TrackColors.Add(defaults[i]); else if (string.IsNullOrWhiteSpace(TrackColors[i])) TrackColors[i] = defaults[i];
        if (TrackColors.Count > 8) TrackColors.RemoveRange(8, TrackColors.Count - 8);
        PresetName = string.IsNullOrWhiteSpace(PresetName) ? "Custom" : PresetName.Trim();
        if (PresetName.Length > 40) PresetName = PresetName[..40];
    }

    internal void ApplyMigrations()
    {
        if (BackgroundAppearanceVersion < 1)
        {
            // Previous releases enabled decorative purple sky layers by default. Keep the
            // user's image path and all other preferences, but make the default stage black.
            BackgroundGradient = false;
            BackgroundGuide = false;
            ShowStars = false;
            BackgroundAppearanceVersion = 1;
        }
        if (BackgroundAppearanceVersion < 2)
        {
            // Version 2 introduced the background mode; a selected image keeps working.
            if (!string.IsNullOrWhiteSpace(BackgroundImagePath)) BackgroundMode = "Image";
            BackgroundAppearanceVersion = 2;
        }
        // The interface language is stored as an id; empty keeps "follow Windows", and an id this
        // build does not know (a file from a future release) falls back to English rather than
        // leaving every label on screen untranslated.
        if (!string.IsNullOrWhiteSpace(Language)) Language = Languages.Normalize(Language);
        // Interface themes are stored by id; older releases used the retro ids sakura / noir / velvet
        // and the retired "Sakura Nocturne" name. Resolve them once on load so the picker, the header
        // and the JSON on disk all agree on one id, and an unknown value falls back to the default.
        ShellTheme = ShellThemes.Normalize(ShellTheme);
    }
}

/// <summary>
/// Where the current look and the user presets are read from and written to.
///
/// The default is <c>%LOCALAPPDATA%\Keyflow</c>. Automated runs redirect the folder: a verification
/// run must never overwrite the look the user saved, and a screenshot must show a pristine first-run
/// state no matter what ran before it in the same session.
/// </summary>
internal static class PianoVisualSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keyflow");

    internal static string SettingsDirectory => _directory;
    internal static string SettingsPath => Path.Combine(_directory, "visual-settings.json");

    /// <summary>Points settings and user presets at another folder (see <c>--settings-dir</c>).</summary>
    internal static void UseDirectory(string path)
    {
        _directory = Path.GetFullPath(path);
        VisualPresetStore.InvalidateDefault();
        UserThemeStore.InvalidateDefault();
    }

    internal static PianoVisualSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath)) return PianoVisualSettings.FromJson(File.ReadAllText(SettingsPath));
            var settings = new PianoVisualSettings(); settings.ApplyMigrations(); settings.Clamp(); return settings;
        }
        catch { var settings = new PianoVisualSettings(); settings.ApplyMigrations(); settings.Clamp(); return settings; }
    }

    internal static void Save(PianoVisualSettings settings)
    {
        settings.ApplyMigrations(); settings.Clamp();
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, true);
    }
}
