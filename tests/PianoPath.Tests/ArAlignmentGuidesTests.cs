using System;
using System.Collections.Generic;
using Xunit;

namespace PianoPath.Tests;

public class ArAlignmentGuidesTests
{
    private const int W = 1280, H = 720;

    private static readonly ArCalibration Lean45 = ArCalibration.Create(KeyboardLayout.Standard88,
        [(70, 292), (1214, 268), (1236, 421), (52, 449)], W, H, out _)!;

    private static readonly ArCalibration Overhead90 = ArCalibration.Create(KeyboardLayout.Standard88,
        [(40, 150), (1240, 150), (1240, 270), (40, 270)], W, H, out _)!;

    private static readonly ArView View = new(W, H, W, H, PictureFit.Fit, false);

    private static List<ArAlignmentGuides.GuidePatch> Guides(ArCalibration calibration)
    {
        var into = new List<ArAlignmentGuides.GuidePatch>();
        ArAlignmentGuides.Build(calibration, View, into);
        return into;
    }

    [Fact]
    public void Locked_numbers_keep_the_set_readable_over_any_look()
    {
        Assert.Equal(.018, ArAlignmentGuides.BorderThicknessV);
        Assert.Equal(.0038, ArAlignmentGuides.GuideThicknessU);
        Assert.Equal(.5f, ArAlignmentGuides.BorderAlpha);
        Assert.Equal(.22f, ArAlignmentGuides.OctaveAlpha);
    }

    [Fact]
    public void Borders_come_first_then_one_line_per_octave_boundary()
    {
        var guides = Guides(Lean45);
        // The 88-key row holds eight C keys with a neighbour below: 24, 36, 48, 60, 72, 84, 96, 108.
        Assert.Equal(12, guides.Count);
        for (var i = 0; i < 4; i++)
            Assert.Equal(ArAlignmentGuides.BorderAlpha, guides[i].Alpha);
        for (var i = 4; i < guides.Count; i++)
            Assert.Equal(ArAlignmentGuides.OctaveAlpha, guides[i].Alpha);
    }

    [Fact]
    public void Every_guide_stays_on_the_picture_on_both_reference_views()
    {
        foreach (var calibration in new[] { Lean45, Overhead90 })
            foreach (var guide in Guides(calibration))
                foreach (var corner in new[] { guide.Patch.BackLeft, guide.Patch.BackRight, guide.Patch.FrontRight, guide.Patch.FrontLeft })
                    Assert.True(corner.X >= 0 && corner.X <= W && corner.Y >= 0 && corner.Y <= H,
                        $"The guide corner ({corner.X:0.#},{corner.Y:0.#}) must stay on the picture.");
    }

    [Fact]
    public void Overhead_90_frame_recovers_the_saved_quad()
    {
        var guides = Guides(Overhead90);
        var back = guides[0].Patch;
        var left = guides[2].Patch;
        // The saved quad spans x 40..1240, y 150..270: the back border hugs its top edge, the left border its left edge.
        Assert.InRange(back.BackLeft.Y - 150, -1, 1);
        Assert.InRange(back.FrontLeft.Y - back.BackLeft.Y, 1.5, 3);
        Assert.InRange(left.BackLeft.X - 40, -1, 1);
        Assert.InRange(left.BackRight.X - 40, 3.5, 6);
        // The right border closes the frame at the far edge, so the four borders draw the saved quad itself.
        Assert.InRange(guides[3].Patch.FrontLeft.X - 1240, -6, -3.5);
        Assert.InRange(guides[1].Patch.BackLeft.Y - 270, -3.5, -1.5);
    }

    [Fact]
    public void Octave_lines_stand_where_the_C_keys_begin()
    {
        var guides = Guides(Lean45);
        var (middleC, _) = KeyboardLayout.Standard88.Span(60);
        Assert.True(ArProjection.TryProject(Lean45, View, middleC - .0005, middleC + .0005, 0, 1,
            out var hairline, out _, out _));
        // 24, 36, 48, then 60: the fourth octave guide must sit where middle C starts, within a pixel and a half.
        var guide = guides[7].Patch;
        var guideX = (guide.BackLeft.X + guide.BackRight.X) / 2;
        var hairlineX = (hairline.BackLeft.X + hairline.BackRight.X) / 2;
        Assert.InRange(Math.Abs(guideX - hairlineX), 0, 1.5);
    }

    [Fact]
    public void Guide_colours_offer_five_high_contrast_picks_with_a_white_fallback()
    {
        Assert.Equal(new[] { "White", "Cyan", "Yellow", "Magenta", "Lime" }, ArAlignmentGuides.ColorIds);
        Assert.Equal("White", ArAlignmentGuides.DefaultColorId);
        var seen = new HashSet<(float R, float G, float B)>();
        foreach (var id in ArAlignmentGuides.ColorIds)
            Assert.True(seen.Add(ArAlignmentGuides.ColorFor(id)), $"Guide colours must stay distinct: {id}");
        // Anything unknown — another language, a misspelling, an empty store — keeps the readable white.
        Assert.Equal(ArAlignmentGuides.ColorFor("White"), ArAlignmentGuides.ColorFor("magenta"));
        Assert.Equal(ArAlignmentGuides.ColorFor("White"), ArAlignmentGuides.ColorFor(null));
        Assert.Equal(ArAlignmentGuides.ColorFor("White"), ArAlignmentGuides.ColorFor(""));
    }

    [Fact]
    public void A_second_build_reuses_the_list_without_leaking_stale_lines()
    {
        var into = Guides(Lean45);
        var first = into.Count;
        ArAlignmentGuides.Build(Overhead90, View, into);
        Assert.Equal(first, into.Count);
    }
}
