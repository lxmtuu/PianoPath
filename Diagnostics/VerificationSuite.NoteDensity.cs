using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// N9's software half. <c>--bench-detail</c> measures the GPU stage on eighteen cells; this measures the
    /// software stage on the same matrix so the two tables can be put side by side, and so the question the
    /// plan asks — at how many bars does the WPF roll drop out of 60 fps — has a number from the machine that
    /// runs the build rather than from a guess.
    ///
    /// <para>
    /// Every cell draws the note layer and nothing else (no keyboard shading, no ambient, no impacts, no
    /// particles), which is what makes "plain" and "fx" comparable: the difference between them is the falling
    /// trail, the echo ghosts and the landing glow. The measurement is reported, not judged: a CI runner's
    /// frame time varies by more than the difference between two neighbouring cells, so an assertion on the
    /// numbers would fail for reasons that have nothing to do with the code. What <em>is</em> asserted is that
    /// every cell really drew notes, and that the effects add pixels rather than replacing the bars.
    /// </para>
    /// </summary>
    private static void VerifyWpfNoteDensity()
    {
        (int Width, int Height)[] sizes = [(1280, 720), (1920, 1080), (2560, 1440)];
        int[] counts = [1, 20, 60];
        var rows = new List<string>(sizes.Length * counts.Length * 2);
        foreach (var (width, height) in sizes)
        {
            foreach (var count in counts)
            {
                var plain = MeasureWpfNotes(width, height, count, effects: false);
                var fx = MeasureWpfNotes(width, height, count, effects: true);
                Assert(plain.NotePixels > 20 && fx.NotePixels > 20,
                    $"N9 WPF {height}p/{count}: a note cell must actually draw notes ({plain.NotePixels} plain, {fx.NotePixels} fx changed pixels).");
                Assert(fx.NotePixels >= plain.NotePixels,
                    $"N9 WPF {height}p/{count}: the trail, ghosts and landing glow must add pixels to the note layer, not replace it ({fx.NotePixels} against {plain.NotePixels}).");
                rows.Add($"{height}p·{count}: {FrameBudget.Ms(plain.MedianMs)} / {FrameBudget.Ms(fx.MedianMs)} ms");
            }
        }
        Results.Add("PASS WPF note density (N9): median draw time of the software roll per cell, plain / fx — "
            + string.Join(", ", rows)
            + ". Every cell drew its bars and the effects added pixels; the numbers are reported for the plan's table, not judged.");
    }

    /// <summary>One cell of the WPF matrix: median draw time over eight renders, and how many pixels the notes changed.</summary>
    private static (double MedianMs, int NotePixels) MeasureWpfNotes(int width, int height, int count, bool effects)
    {
        var settings = VisualPresets.CreateDefaultSettings();
        IsolateNoteLayer(settings, effects);
        var stage = new PianoStage { Width = width, Height = height };
        stage.SetVisualSettings(settings);
        stage.Measure(new Size(width, height));
        stage.Arrange(new Rect(0, 0, width, height));
        stage.UpdateLayout();
        stage.ResetAnimation(7);

        var hitY = Math.Max(1, stage.ActualHeight - stage.KeyboardHeight);
        var speed = Math.Max(1, PianoStage.FallSpeed * settings.NoteFallSpeed / 550);
        var visible = Math.Max(1, hitY) / speed;
        var spacing = Math.Max(.01, visible / Math.Max(1, count));
        var notes = new List<NoteEvent>(count);
        // Half a step in, so even the last bar sits wholly inside the roll instead of starting in the fade band.
        for (var i = 0; i < count; i++)
            notes.Add(new NoteEvent { Pitch = 48 + i % 60, Start = 1 + (i + .5) * spacing, Duration = Math.Max(.05, spacing * .9) });
        // The playhead sits before every bar, so the whole roll stands above the keys at once.
        stage.SetState(notes, 1, playing: true, pressed: new HashSet<int>());

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var pixels = new byte[width * height * 4];
        void Draw()
        {
            stage.InvalidateVisual();
            FlushRenderPass();
            bitmap.Render(stage);
        }

        for (var warm = 0; warm < 3; warm++) Draw();
        var samples = new double[8];
        for (var i = 0; i < samples.Length; i++)
        {
            var timer = Stopwatch.StartNew();
            Draw();
            samples[i] = timer.Elapsed.TotalMilliseconds;
        }
        var stats = FrameStats.From(samples);

        bitmap.CopyPixels(pixels, width * 4, 0);
        var quiet = settings.Clone();
        quiet.ShowNotes = false;
        stage.SetVisualSettings(quiet);
        Draw();
        var empty = new byte[width * height * 4];
        bitmap.CopyPixels(empty, width * 4, 0);
        var changed = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (Math.Max(Math.Abs(pixels[i] - empty[i]), Math.Max(Math.Abs(pixels[i + 1] - empty[i + 1]), Math.Abs(pixels[i + 2] - empty[i + 2]))) > 6)
                changed++;
        return (stats.MedianMs, changed);
    }

    /// <summary>
    /// Everything except the note layer off, then the three note effects for the cell — the same isolation the
    /// GPU matrix uses, so the two tables differ only in the engine that drew them.
    /// </summary>
    private static void IsolateNoteLayer(PianoVisualSettings settings, bool effects)
    {
        settings.ShadingQuality = "Off";
        settings.BackgroundGradient = false;
        settings.ShowStars = false; settings.ShowPetals = false; settings.ShowEmbers = false;
        settings.ShowFlame = false; settings.ShowWisps = false; settings.ShootingStars = false; settings.ShowLightBeams = false;
        settings.ShowHalo = false; settings.ShowKeys = false;
        settings.ShowImpactFlash = false; settings.ShowImpactRings = false;
        settings.ImpactMorph = "None"; settings.ReleaseEffect = "None";
        settings.BackgroundMotion = "None";
        settings.ParticleAmount = 0;
        settings.NoteShimmer = false; settings.HoldBreath = false; settings.HaloPulse = false;
        settings.FallingPulse = false; settings.HoldColorCycle = false; settings.HoldVibration = false;
        settings.HorizonGlow = 0; settings.Vignette = 0; settings.BloomIntensity = 0;
        settings.ShowNoteLabels = false;
        settings.FallingTrail = effects ? "Glow" : "None";
        settings.FallingTrailIntensity = effects ? 70 : 0;
        settings.FallingGhost = effects;
        settings.FallingGhostAmount = effects ? 60 : 0;
        settings.NoteLandingGlow = effects;
    }

    /// <summary>A detached visual reuses its retained drawing, so the render pass has to run before it can be timed.</summary>
    private static void FlushRenderPass()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
