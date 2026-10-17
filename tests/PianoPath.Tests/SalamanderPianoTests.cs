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
    public async Task A_verified_sample_writer_closes_its_file_before_the_caller_moves_it()
    {
        var sample = new byte[] { 0, 1, 2, 3, 127, 255 };
        var header = System.Text.Encoding.ASCII.GetBytes($"blob {sample.Length}\0");
        var gitBlob = header.Concat(sample).ToArray();
        var entry = new SalamanderPiano.Entry(Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(gitBlob)), sample.Length, "Samples/test.flac");
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pianopath-sample-write-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        var temporary = System.IO.Path.Combine(folder, "sample.flac.part");
        var target = System.IO.Path.Combine(folder, "sample.flac");
        try
        {
            using var source = new System.IO.MemoryStream(sample);
            await SalamanderPiano.DownloadSampleToTemporaryAsync(source, temporary, entry, CancellationToken.None);

            // On Windows this move fails with “file is being used by another process” if the
            // FileShare.None writer from the downloader is still alive at this point.
            System.IO.File.Move(temporary, target);
            Assert.Equal(sample, System.IO.File.ReadAllBytes(target));
        }
        finally { System.IO.Directory.Delete(folder, recursive: true); }
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
