using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// AR-S2: the style's two velocity graphs (docs/AR-OVERLAY-ROADMAP.md §14.5) — a note's width and fade-in
/// walking with the strike's velocity, for the song's notes and for the bars of the keys the player
/// presses. Flat graphs (the default) must leave every drawing exactly as it was.
/// </summary>
public class ArGraphTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
    private static readonly ArCalibration Calibration = ArProjectionTests.Calibrate(Sample88);
    private static readonly ArView View = new(1280, 720, 1000, 600, PictureFit.Fit, false);
    private static readonly ArNoteStyle Style = ArNoteStyle.Default;

    private static NoteEvent Note(int pitch, double start, double duration, int velocity) =>
        new() { Pitch = pitch, Start = start, Duration = duration, Velocity = velocity };

    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    private static (double U, double V) Keys(ArCorner corner)
    {
        var (px, py) = View.ToImage(corner.X, corner.Y);
        return Calibration.ToKeys(px, py);
    }

    private static List<ArNoteShape> Build(IReadOnlyList<NoteEvent> notes, double position, ArNoteStyle? style = null, PianoSimulation? simulation = null)
    {
        var into = new List<ArNoteShape>();
        ArNotes.Build(Calibration, View, notes, position, 1, style ?? Style, into, simulation);
        return into;
    }

    private static List<ArLiveBar> BuildLive(IReadOnlyList<ArLivePress> presses, ArNoteStyle? style = null, PianoSimulation? simulation = null)
    {
        var into = new List<ArLiveBar>();
        ArNotes.BuildLive(Calibration, View, presses, style ?? Style, into, simulation);
        return into;
    }

    private static double Width(ArPatch patch) => Keys(patch.FrontRight).U - Keys(patch.FrontLeft).U;

    private static ArNoteStyle WidthGraph(double soft, double hard) =>
        Style with { WidthVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: soft, Max: hard) };

    [Fact]
    public void A_soft_strike_is_narrower_than_a_hard_one_when_width_follows_velocity()
    {
        var style = WidthGraph(soft: .5, hard: 1);
        // both notes one second away, so both are on screen together
        var shapes = Build([Note(60, 11, .5, velocity: 20), Note(62, 11, .5, velocity: 127)], 10, style);
        Assert.Equal(2, shapes.Count);
        var soft = shapes.First(s => s.Pitch == 60); var hard = shapes.First(s => s.Pitch == 62);
        var (left, right) = Calibration.Layout.Span(60);
        var full = (right - left) * Style.WidthShare;
        Near(full * (.5 + .5 * 20 / 127.0), Width(soft.Patch), 1e-6);   // velocity 20 of 127 walks the graph partway
        Near(full, Width(hard.Patch), 1e-6);                        // a strike past the ceiling is full width
        Assert.True(Width(hard.Patch) > Width(soft.Patch));
    }

    [Fact]
    public void A_flat_width_graph_leaves_every_note_the_same_width()
    {
        var shapes = Build([Note(60, 11, .5, velocity: 20), Note(62, 11, .5, velocity: 127)], 10);
        Assert.Equal(2, shapes.Count);
        Near(Width(shapes[0].Patch), Width(shapes[1].Patch), 1e-9);
    }

    [Fact]
    public void The_width_graph_walks_the_simulation_band_not_the_raw_velocity()
    {
        var sim = new PianoSimulation { VelocityFloor = 20, VelocityCeiling = 100 };
        var style = WidthGraph(soft: .5, hard: 1);
        var shapes = Build([Note(60, 11, .5, velocity: 20), Note(62, 11, .5, velocity: 100)], 10, style, sim);
        var soft = shapes.First(s => s.Pitch == 60); var hard = shapes.First(s => s.Pitch == 62);
        var (left, right) = Calibration.Layout.Span(60);
        var full = (right - left) * Style.WidthShare;
        Near(full * .5, Width(soft.Patch), 1e-6);    // on the floor: the soft end of the graph
        Near(full, Width(hard.Patch), 1e-6);         // at the ceiling: the hard end
    }

    [Fact]
    public void A_soft_strike_fades_in_more_slowly_when_fade_follows_velocity()
    {
        var edge = Style.Reach / Style.FallSpeed;
        var halfway = 10 + edge - .5 * Style.Reach * .25 / Style.FallSpeed;   // the fade-in is halfway
        var style = Style with { FadeVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .5, Max: 1) };
        var soft = Assert.Single(Build([Note(60, halfway, .5, velocity: 20)], 10, style));
        var hard = Assert.Single(Build([Note(60, halfway, .5, velocity: 127)], 10, style));
        Near(.5 * 1, hard.Fade, 1e-9);                 // a full-velocity strike keeps the whole fade
        Assert.True(soft.Fade < hard.Fade, "A soft strike should keep part of its fade-in, a hard one all of it.");
        Near(.5 * (.5 + .5 * 20 / 127.0), soft.Fade, 1e-6);
        // and with the flat default the same note fades exactly as before
        var flat = Assert.Single(Build([Note(60, halfway, .5, velocity: 20)], 10));
        Near(.5, flat.Fade, 1e-9);
    }

    [Fact]
    public void A_pressed_key_bar_is_wider_and_brighter_the_harder_the_key_is_struck()
    {
        var style = WidthGraph(soft: .5, hard: 1) with { FadeVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .5, Max: 1) };
        var bars = BuildLive([new ArLivePress(60, .5, .5, true, .2f), new ArLivePress(62, .5, .5, true, 1f)], style);
        Assert.Equal(2, bars.Count);
        var soft = bars.First(b => b.Pitch == 60); var hard = bars.First(b => b.Pitch == 62);
        Assert.True(Width(hard.Patch) > Width(soft.Patch), "A harder press should leave a wider bar.");
        Near(Width(hard.Patch) * (.5 + .5 * .2), Width(soft.Patch), 1e-6); // strength .2 walks the graph partway
        Assert.True(hard.Fade > soft.Fade, "A harder press should keep more of its bar's brightness.");
        // without the optional lifetime graph, the bar stays solid; only an explicit velocity fade changes it
        var flat = BuildLive([new ArLivePress(60, .5, .5, true, .2f)]);
        Near(1, Assert.Single(flat).Fade, 1e-9);
    }

    [Fact]
    public void The_graphs_do_not_change_where_a_note_or_bar_is_only_how_wide_and_bright()
    {
        var style = WidthGraph(soft: .4, hard: 1) with { FadeVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .5, Max: 1) };
        var plain = Build([Note(60, 11, .5, velocity: 127)], 10);
        var graphed = Build([Note(60, 11, .5, velocity: 127)], 10, style);
        var (pl, pr) = (Keys(Assert.Single(plain).Patch.FrontLeft), Keys(Assert.Single(plain).Patch.FrontRight));
        var (gl, gr) = (Keys(Assert.Single(graphed).Patch.FrontLeft), Keys(Assert.Single(graphed).Patch.FrontRight));
        Near((pl.U + pr.U) / 2, (gl.U + gr.U) / 2, 1e-9);   // centred on the same key
        Near(pl.V, gl.V, 1e-9); Near(pr.V, gr.V, 1e-9);     // at the same depth
    }
}
