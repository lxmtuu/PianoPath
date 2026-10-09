using Xunit;

namespace PianoPath.Tests;

public sealed class GpuAlphaLayerPairTapTests
{
    [Fact]
    public void Geometry_and_glow_publish_together_with_one_shared_frame_serial()
    {
        var pair = new GpuAlphaLayerPairTap(16, 16, 60);
        Assert.False(pair.Geometry.TryRead(_ => { }, out var unpublishedSerial));
        Assert.Equal(0L, unpublishedSerial);

        Array.Fill(pair.GeometryBackBuffer(16, 16), (byte)73);
        Array.Fill(pair.GlowBackBuffer(16, 16), (byte)149);
        pair.Publish();

        byte geometryPixel = 0, glowPixel = 0;
        Assert.True(pair.Geometry.TryRead(frame => geometryPixel = frame[0], out var geometrySerial));
        Assert.True(pair.Glow.TryRead(frame => glowPixel = frame[0], out var glowSerial));
        Assert.Equal(geometrySerial, glowSerial);
        Assert.Equal(1L, geometrySerial);
        Assert.Equal((byte)73, geometryPixel);
        Assert.Equal((byte)149, glowPixel);
        byte pairGeometryPixel = 0, pairGlowPixel = 0;
        Assert.True(pair.TryReadPair(frame => pairGeometryPixel = frame[0], frame => pairGlowPixel = frame[0], out var pairSerial));
        Assert.Equal(geometrySerial, pairSerial);
        Assert.Equal((byte)73, pairGeometryPixel);
        Assert.Equal((byte)149, pairGlowPixel);

        Array.Fill(pair.GeometryBackBuffer(16, 16), (byte)41);
        Array.Fill(pair.GlowBackBuffer(16, 16), (byte)211);
        byte oldGeometryPixel = 0, oldGlowPixel = 0;
        Assert.True(pair.Geometry.TryRead(frame => oldGeometryPixel = frame[0], out var oldGeometrySerial));
        Assert.True(pair.Glow.TryRead(frame => oldGlowPixel = frame[0], out var oldGlowSerial));
        Assert.Equal(geometrySerial, oldGeometrySerial);
        Assert.Equal(geometrySerial, oldGlowSerial);
        Assert.Equal((byte)73, oldGeometryPixel);
        Assert.Equal((byte)149, oldGlowPixel);
        byte oldPairGeometry = 0, oldPairGlow = 0;
        Assert.True(pair.TryReadPair(frame => oldPairGeometry = frame[0], frame => oldPairGlow = frame[0], out var oldPairSerial));
        Assert.Equal(geometrySerial, oldPairSerial);
        Assert.Equal((byte)73, oldPairGeometry);
        Assert.Equal((byte)149, oldPairGlow);

        pair.Publish();
        byte nextGeometryPixel = 0, nextGlowPixel = 0;
        Assert.True(pair.TryReadPair(frame => nextGeometryPixel = frame[0], frame => nextGlowPixel = frame[0], out var nextPairSerial));
        Assert.Equal(2L, nextPairSerial);
        Assert.Equal(nextPairSerial, pair.Geometry.Serial);
        Assert.Equal(nextPairSerial, pair.Glow.Serial);
        Assert.Equal((byte)41, nextGeometryPixel);
        Assert.Equal((byte)211, nextGlowPixel);
    }

    [Fact]
    public void Layer_back_buffers_validate_the_shared_output_dimensions()
    {
        var pair = new GpuAlphaLayerPairTap(16, 16, 60);
        Assert.Throws<InvalidOperationException>(() => pair.GeometryBackBuffer(32, 16));
        Assert.Throws<InvalidOperationException>(() => pair.GlowBackBuffer(16, 32));
    }
}
