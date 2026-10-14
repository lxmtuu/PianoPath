using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PianoPath;

/// <summary>Counts actual primary-video payloads, not accepted-frame counters or idx1 entries.
/// Diagnostic for the app's single-video-stream RIFF AVI files; not a decoder.</summary>
internal readonly record struct AviFrameInventory(int Samples, int NonEmptySamples, int DistinctPayloads)
{
    internal static AviFrameInventory Read(byte[] bytes)
    {
        if (bytes.Length < 12 || Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "AVI ")
            throw new ArgumentException("Expected a RIFF AVI file.", nameof(bytes));
        var end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4,4));
        if (end > bytes.Length || end < 12) throw new ArgumentException("Truncated AVI.", nameof(bytes));
        int samples = 0, nonEmpty = 0;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        void Walk(int start, long limit, bool movie)
        {
            for (long offset = start; offset < limit;)
            {
                if (limit - offset < 8) throw new ArgumentException("Truncated AVI chunk.");
                var at = (int)offset;
                var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+4,4));
                var next = offset + 8 + size; var padded = next + (size & 1);
                if (next > limit || padded > limit) throw new ArgumentException("Truncated AVI payload.");
                var tag = Tag(bytes,at);
                if (tag == "LIST")
                {
                    if (size < 4) throw new ArgumentException("Truncated AVI list.");
                    Walk(at+12,next,movie || Tag(bytes,at+8) == "movi");
                }
                else if (movie && tag is "00dc" or "00db")
                {
                    samples++;
                    if (size > 0)
                    {
                        nonEmpty++;
                        hashes.Add(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(at+8,(int)size))));
                    }
                }
                offset = padded;
            }
        }
        Walk(12,end,false);
        return new(samples,nonEmpty,hashes.Count);
    }

    private static string Tag(byte[] bytes,int at) => System.Text.Encoding.ASCII.GetString(bytes,at,4);
}
