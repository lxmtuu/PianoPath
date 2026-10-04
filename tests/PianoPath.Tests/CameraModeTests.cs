using Xunit;

namespace PianoPath.Tests;

/// <summary>Which of a webcam's modes the AR overlay asks for, how a mode reads in the dock, and how a format is named.</summary>
public class CameraModeTests
{
    private static CameraMode Mode(int width, int height, double fps, string format) =>
        new(width, height, (int)Math.Round(fps * 1000), 1000, format);

    private static int Choose(params CameraMode[] modes) => CameraModes.Choose(modes);

    // ---- The label -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1280, 720, 60000, 1000, "MJPG", "720p · 60 fps · MJPG")]
    [InlineData(1920, 1080, 30, 1, "NV12", "1080p · 30 fps · NV12")]
    [InlineData(640, 480, 30000, 1001, "YUY2", "480p · 30 fps · YUY2")]
    [InlineData(800, 600, 0, 0, "", "800 × 600")]
    [InlineData(1366, 768, 30, 1, "NV12", "1366 × 768 · 30 fps · NV12")]
    [InlineData(720, 1280, 30, 1, "", "720 × 1280 · 30 fps")]
    public void A_mode_reads_the_way_the_dock_prints_it(int width, int height, int numerator, int denominator, string format, string expected)
    {
        Assert.Equal(expected, new CameraMode(width, height, numerator, denominator, format).Label);
    }

    [Fact]
    public void A_rate_the_mode_does_not_state_is_zero_and_left_out()
    {
        Assert.Equal(0, new CameraMode(640, 480, 0, 0, "").Fps);
        Assert.Equal(0, new CameraMode(640, 480, 30, 0, "").Fps);
        Assert.Equal(29.97, new CameraMode(640, 480, 30000, 1001, "").Fps, 2);
    }

    // ---- The choice ------------------------------------------------------------------------------------

    [Fact]
    public void Four_hundred_and_eighty_p_beats_a_thousand_and_eighty_p_when_there_is_no_720p()
    {
        var modes = new[] { Mode(1920, 1080, 30, "MJPG"), Mode(640, 480, 30, "MJPG") };
        Assert.Equal(1, CameraModes.Choose(modes));
    }

    [Fact]
    public void Seven_twenty_p_at_the_highest_rate_with_the_cheapest_format_wins_and_the_order_does_not_matter()
    {
        var best = Mode(1280, 720, 60, "MJPG");
        var others = new[]
        {
            Mode(1920, 1080, 60, "MJPG"), Mode(1280, 720, 30, "MJPG"), Mode(1280, 720, 60, "YUY2"),
            Mode(640, 480, 60, "MJPG"), Mode(1280, 720, 15, "MJPG"), Mode(1280, 720, 60, "NV12"),
        };
        for (var at = 0; at <= others.Length; at++)
        {
            var list = others.ToList();
            list.Insert(at, best);
            Assert.Equal(at, CameraModes.Choose(list));
        }
    }

    [Fact]
    public void A_faster_rate_wins_inside_one_height_even_over_a_cheaper_format()
    {
        Assert.Equal(1, Choose(Mode(1280, 720, 30, "MJPG"), Mode(1280, 720, 60, "YUY2")));
    }

    [Fact]
    public void Rates_above_sixty_do_not_count_and_fifty_nine_point_nine_four_is_sixty()
    {
        // 120 fps raw against 60 fps MJPG: both are 60 as far as the choice goes, so the format decides.
        Assert.Equal(1, Choose(Mode(1280, 720, 120, "YUY2"), Mode(1280, 720, 60, "MJPG")));
        Assert.Equal(1, Choose(Mode(1280, 720, 59.94, "YUY2"), Mode(1280, 720, 60, "NV12")));
        Assert.Equal(0, Choose(new CameraMode(1280, 720, 60000, 1001, "MJPG"), Mode(1280, 720, 60, "MJPG")));
    }

    [Fact]
    public void Two_heights_as_far_from_720_as_each_other_give_the_taller()
    {
        Assert.Equal(1, Choose(Mode(800, 600, 30, "MJPG"), Mode(1280, 840, 30, "MJPG")));
    }

    [Fact]
    public void Nearer_to_sixteen_by_nine_wins_at_one_height_and_one_rate()
    {
        Assert.Equal(1, Choose(Mode(960, 720, 30, "MJPG"), Mode(1280, 720, 30, "MJPG")));
    }

    [Fact]
    public void A_compressed_format_the_reader_may_not_decode_is_used_only_when_nothing_else_is_there()
    {
        Assert.Equal(1, Choose(Mode(1280, 720, 60, "H264"), Mode(640, 480, 30, "YUY2")));
        Assert.Equal(0, Choose(Mode(1280, 720, 60, "H264"), Mode(640, 480, 30, "H264")));
        Assert.Equal(1, Choose(Mode(1280, 720, 30, ""), Mode(1280, 720, 30, "YUY2")));
    }

    [Fact]
    public void Nothing_usable_gives_minus_one()
    {
        Assert.Equal(-1, CameraModes.Choose([]));
        Assert.Equal(-1, Choose(new CameraMode(0, 0, 30, 1, "MJPG"), new CameraMode(1280, 0, 30, 1, "MJPG")));
    }

    // ---- The names of formats --------------------------------------------------------------------------

    [Theory]
    [InlineData("47504A4D-0000-0010-8000-00AA00389B71", "MJPG")]
    [InlineData("32595559-0000-0010-8000-00AA00389B71", "YUY2")]
    [InlineData("3231564E-0000-0010-8000-00AA00389B71", "NV12")]
    [InlineData("34363248-0000-0010-8000-00AA00389B71", "H264")]
    [InlineData("00000016-0000-0010-8000-00AA00389B71", "RGB32")]
    [InlineData("00000015-0000-0010-8000-00AA00389B71", "ARGB32")]
    [InlineData("00000014-0000-0010-8000-00AA00389B71", "RGB24")]
    [InlineData("73646976-0000-0010-8000-00AA00389B71", "VIDS")]
    public void A_media_foundation_subtype_is_named_by_its_four_letters_or_its_number(string guid, string expected)
    {
        Assert.Equal(expected, CameraModes.FormatName(new Guid(guid)));
    }

    [Fact]
    public void A_guid_that_is_not_a_media_subtype_has_no_name()
    {
        Assert.Equal("", CameraModes.FormatName(new Guid("47504A4D-0000-0010-8000-00AA00389B72")));
        Assert.Equal("", CameraModes.FormatName(Guid.Empty));
        Assert.Equal("", CameraModes.FormatName(new Guid("00000001-0000-0010-8000-00AA00389B71"))); // a number that names no RGB format
    }

    [Fact]
    public void The_formats_that_are_named_are_ranked_cheapest_first_and_the_rest_last()
    {
        Assert.True(CameraModes.FormatRank("MJPG") < CameraModes.FormatRank("NV12"));
        Assert.True(CameraModes.FormatRank("NV12") < CameraModes.FormatRank("YUY2"));
        Assert.True(CameraModes.FormatRank("YUY2") < CameraModes.FormatRank("RGB32"));
        Assert.Equal(9, CameraModes.FormatRank("H264"));
        Assert.Equal(9, CameraModes.FormatRank(""));
    }
}
