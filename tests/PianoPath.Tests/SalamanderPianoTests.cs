using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The downloadable Salamander grand's bookkeeping: the manifest the downloader verifies files against
/// and the SFZ mapping Keyflow generates for them must agree exactly, because a file the mapping names
/// but the manifest misses would only surface as a FileNotFoundException on a user's machine after a
/// 750 MB download. No test here touches the network.
/// </summary>
public class SalamanderPianoTests
{
    [Fact]
    public void The_manifest_lists_every_sample_with_hash_size_and_a_safe_relative_path()
    {
        var entries = SalamanderPiano.ParseManifest();

        Assert.Equal(641, entries.Length);
        Assert.Equal(SalamanderPiano.TotalBytes, entries.Sum(e => e.Size));
        Assert.Equal(entries.Length, entries.Select(e => e.Path).Distinct().Count());
        Assert.All(entries, e =>
        {
            Assert.Matches("^[0-9a-f]{40}$", e.BlobSha);
            Assert.InRange(e.Size, 1, 10_000_000);
            Assert.StartsWith("Samples/", e.Path);
            Assert.EndsWith(".flac", e.Path);
            Assert.DoesNotContain("..", e.Path); // paths are joined under the install folder
        });
    }

    [Fact]
    public void The_generated_mapping_references_each_downloaded_sample_exactly_once()
    {
        var names = SalamanderPiano.ParseManifest().Select(e => e.Path["Samples/".Length..]).ToHashSet(StringComparer.Ordinal);
        var lines = SalamanderPiano.BuildSfz().Split('\n');

        var referenced = lines.Where(l => l.StartsWith("<region>", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf("sample=", StringComparison.Ordinal) + "sample=".Length)..].Trim())
            .ToList();

        Assert.Equal(641, referenced.Count);
        Assert.Equal(641, referenced.Distinct().Count());
        Assert.All(referenced, name => Assert.Contains(name, names));
    }

    [Fact]
    public void The_generated_mapping_keeps_the_source_sets_layout()
    {
        var sfz = SalamanderPiano.BuildSfz();
        var lines = sfz.Split('\n');

        Assert.Contains("default_path=Samples/", sfz);
        Assert.Equal(16, lines.Count(l => l.StartsWith("<group> lovel=", StringComparison.Ordinal)));      // dynamic layers
        Assert.Equal(8, lines.Count(l => l.Contains("trigger=release")));                                   // hammer noise + 7 resonance groups
        Assert.Equal(2, lines.Count(l => l.StartsWith("<group> on_locc64=", StringComparison.Ordinal)));    // pedal down + up
        Assert.Equal(88, lines.Count(l => l.Contains("sample=rel")));                                       // one key-off noise per key
        // The keyboard is covered gaplessly from A0 to C8 by each dynamic layer.
        var windows = lines.Where(l => l.Contains("pitch_keycenter=") && l.Contains("v1.flac"))
            .Select(l => l.Split(' ').First(part => part.StartsWith("lokey=")))
            .Select(part => int.Parse(part["lokey=".Length..]))
            .OrderBy(k => k).ToList();
        Assert.Equal(21, windows.First());
        Assert.Equal(30, windows.Count);
    }
}
