using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>P1/P2 product wiring; pure interpolation is also tested by the portable xUnit project.</summary>
    private static void VerifyPrismFlowLook()
    {
        var recipe = CommunityPresets.Find("Prism Flow Preview")?.Settings
            ?? throw new InvalidOperationException("The build must embed the opt-in Prism Flow Preview recipe.");
        Assert(recipe.Palette == "Prism Flow" && recipe.NoteSatin && recipe.NoteStyle == "Solid"
                && recipe.PaletteLowPitch == 36 && recipe.PaletteHighPitch == 84,
            "The experimental recipe must use a fixed pitch range and opt-in satin, not a replacement material ID.");
        Assert(VisualPresets.BuiltIn.All(p => !p.Settings.NoteSatin) && !new PianoVisualSettings().NoteSatin
                && !PianoVisualSettings.FromJson("{}").NoteSatin && VisualPresets.DefaultPresetName == "Concert Gold",
            "Legacy/default settings must not silently opt into satin or change the selected default.");
        Assert(!recipe.VelocityColor && !recipe.OctaveColor && !recipe.ZoneSplit && !recipe.HoldColorCycle
                && !recipe.NoteShimmer && !recipe.ShowEmbers && !recipe.ShowWisps && !recipe.ShowFlame
                && recipe.AmbientLight == "None" && recipe.BackgroundMotion == "None",
            "The palette/material preview must not hide behind hue modulators, new particles or moving background light.");
        var roundTrip = recipe.Clone();
        Assert(roundTrip.NoteSatin && roundTrip.Palette == recipe.Palette && roundTrip.PaletteLowPitch == 36
                && roundTrip.PaletteHighPitch == 84 && roundTrip.NoteTexture == recipe.NoteTexture,
            "Palette range, texture and satin must survive the existing JSON clone format.");
        var copy = new PianoVisualSettings(); copy.CopyFrom(recipe);
        Assert(copy.NoteSatin && copy.PaletteLowPitch == 36 && copy.PaletteHighPitch == 84,
            "Applying a look must copy its palette range and surface, not machine-specific defaults.");
        Assert(VisualPresetShare.TryDecode(VisualPresetShare.Encode(recipe), out var shared, out _)
                && shared.NoteSatin && shared.Palette == "Prism Flow" && shared.PaletteLowPitch == 36 && shared.PaletteHighPitch == 84,
            "A Prism Flow look must travel through the existing share code without losing range or surface.");
        var invalid = new PianoVisualSettings { Palette = "Prism Flow", PaletteLowPitch = 200, PaletteHighPitch = -5 };
        invalid.Clamp();
        Assert(invalid.PaletteLowPitch == 126 && invalid.PaletteHighPitch == 127,
            "Crossed/out-of-range palette bounds must normalise to an ascending MIDI range.");
        invalid.PaletteLowPitch = double.NaN; invalid.PaletteHighPitch = double.PositiveInfinity; invalid.Clamp();
        Assert(invalid.PaletteLowPitch == 21 && invalid.PaletteHighPitch == 108,
            "Non-finite palette bounds must have a safe, finite fallback before integer conversion.");
        Assert(MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.PaletteLowPitch), "C2", out var low) && low == 36
                && MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.PaletteHighPitch), "C6", out var high) && high == 84
                && !MainWindow.FormatSetting(nameof(PianoVisualSettings.PaletteLowPitch), 36).Contains('%'),
            "Palette pitch controls must accept note names and must not display MIDI pitches as percentages.");

        var previousLayout = KeyboardLayout.Active;
        try
        {
            var stage = new PianoStage();
            stage.SetVisualSettings(recipe);
            var live = GpuLook.From(roundTrip, stage.NoteColor, .2);
            var ar = GpuLook.From(roundTrip, stage.NoteColor, .2, overCamera: true);
            Assert(live.NoteSatin && ar.NoteSatin && live.NoteStyle == 0 && ar.NoteStyle == 0
                    && Math.Abs(live.NoteTexture - .32f) < .001f && Math.Abs(ar.NoteTexture - live.NoteTexture) < .001f,
                "Both GPU snapshots must receive the same opt-in surface and texture; material IDs remain unchanged.");
            for (var pitch = 0; pitch < 128; pitch++)
            {
                var source = PrismFlowPalette.ForPitch(pitch, 36, 84);
                var color = stage.NoteColor(pitch, 0);
                var srgb = new Vector3(color.R, color.G, color.B) / 255;
                Assert(Vector3.Distance(source, srgb) < .004f,
                    $"Pitch {pitch}: WPF source colour must preserve the tested palette (within byte quantisation).");
                for (var track = 0; track < GpuLook.TrackSlots; track++)
                    Assert(Vector3.Distance(live.NoteColor(pitch, track), srgb) < .00001f
                            && ar.NoteColor(pitch, track) == live.NoteColor(pitch, track),
                        $"Pitch {pitch}/track {track}: Stage and AR must use the shared, track-independent colour table.");
            }
            var frozen = live.NoteColor(60, 0);
            roundTrip.Palette = "Fire"; roundTrip.NoteSatin = false; roundTrip.NoteTexture = 100;
            Assert(live.NoteSatin && live.NoteTexture == .32f && live.NoteColor(60, 0) == frozen,
                "Mutating UI settings must not mutate an already-published render snapshot.");
            foreach (var style in new[] { "Neon", "Fire", "Gem", "CrystalOutline" })
            {
                var other = recipe.Clone(); other.NoteStyle = style;
                Assert(!GpuLook.From(other, stage.NoteColor, .2).NoteSatin,
                    $"The opt-in switch must not replace {style}'s own material/texture semantics.");
            }
            Assert(Marshal.SizeOf<GpuFrameConstants>() % 16 == 0
                    && Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.NoteD)).ToInt32() == Marshal.SizeOf<GpuFrameConstants>() - 16,
                "The satin constants must be appended and aligned, preserving all earlier constant offsets.");
        }
        finally { KeyboardLayout.Use(previousLayout); }
        Results.Add("PASS Prism Flow look: fixed linear-light palette, Stage/AR colour table, snapshot, opt-in material routing, JSON/apply/share and pitch-control plumbing. No lifecycle, WPF material parity or hardware claim.");
    }
}
