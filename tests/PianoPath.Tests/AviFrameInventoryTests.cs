using System.Text;
using Xunit;

namespace PianoPath.Tests;

public sealed class AviFrameInventoryTests
{
    private static byte[] Chunk(string tag, byte[] bytes)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes(tag)); writer.Write(bytes.Length); writer.Write(bytes);
        if ((bytes.Length&1)!=0) writer.Write((byte)0);
        return stream.ToArray();
    }
    private static byte[] FileOf(params byte[][] samples)
    {
        var movie=Encoding.ASCII.GetBytes("movi").Concat(samples.SelectMany(s=>Chunk("00dc",s))).ToArray();
        var body=Encoding.ASCII.GetBytes("AVI ").Concat(Chunk("LIST",movie)).Concat(Chunk("idx1",Encoding.ASCII.GetBytes("00dc"))).ToArray();
        return Chunk("RIFF",body);
    }
    [Fact]
    public void TimelineSlotsDoNotProveDistinctContent()
    {
        var samples=Enumerable.Range(0,12).Select(i=> i<5 ? new[]{(byte)i} : Array.Empty<byte>()).ToArray();
        Assert.Equal(new AviFrameInventory(12,5,5),AviFrameInventory.Read(FileOf(samples)));
    }
    [Fact]
    public void ExactPayloadsAndDuplicateImagesAreDistinguished()
    {
        Assert.Equal(new AviFrameInventory(12,12,12),AviFrameInventory.Read(FileOf(Enumerable.Range(0,12).Select(i=>new[]{(byte)i}).ToArray())));
        Assert.Equal(new AviFrameInventory(3,3,1),AviFrameInventory.Read(FileOf([1],[1],[1])));
    }
    [Fact]
    public void TruncationIsRejected()
    {
        var bytes=FileOf([1,2,3]);
        Assert.Throws<ArgumentException>(()=>AviFrameInventory.Read(bytes[..^2]));
        Assert.Throws<ArgumentException>(()=>AviFrameInventory.Read([1,2,3]));
    }
}
