using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PianoPath;

/// <summary>
/// The downloadable concert grand: Alexander Holm's Salamander Grand Piano V3 (a Yamaha C5 recorded at
/// 48 kHz/24-bit, CC BY 3.0) fetched file-by-file from the sfzinstruments GitHub mirror. The repo ships
/// no gigabyte sample sets, so this class owns everything about getting one onto the user's disk: a
/// manifest pinned to one commit (path, git blob hash and size for all 641 FLAC files), a resumable
/// downloader that skips finished files and verifies every new one against its hash, and a generated
/// SFZ mapping written for exactly the opcodes Keyflow's engine implements — 16 velocity layers, key-off
/// hammer noises, three string-resonance release layers and the pedal's own down/up thumps.
/// </summary>
internal static class SalamanderPiano
{
    public const string DisplayName = "Salamander Grand Piano";
    private const string Commit = "3382bf9496bba2486f5ab0de55a264d1dfc38404";
    private const string BaseUrl = "https://raw.githubusercontent.com/sfzinstruments/SalamanderGrandPiano/" + Commit + "/";
    /// <summary>Sum of all manifest sizes, for progress math and the “750 MB” label.</summary>
    public const long TotalBytes = 748_397_030;

    public static string InstallDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keyflow", "Instruments", "SalamanderGrandPiano");
    public static string SfzPath => System.IO.Path.Combine(InstallDirectory, "Salamander Grand Piano.sfz");
    /// <summary>Installed means the mapping exists; the mapping is only written after every sample verified.</summary>
    public static bool IsInstalled => System.IO.File.Exists(SfzPath);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>One manifest line: where a sample lives upstream and how to prove a local copy is right.</summary>
    public readonly record struct Entry(string BlobSha, long Size, string Path);

    public static Entry[] ParseManifest() =>
        Manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 3))
            .Select(parts => new Entry(parts[0], long.Parse(parts[1]), parts[2]))
            .ToArray();

    /// <summary>
    /// Downloads every missing sample, four at a time, reporting cumulative verified bytes. Complete local
    /// files are hash-checked before reuse; interrupted setup therefore resumes safely without trusting size
    /// alone. New downloads are streamed to temporary files and verified against Git's blob SHA-1 before
    /// they are moved into place. The SFZ mapping is written last and marks installation as complete.
    /// </summary>
    public static async Task DownloadAsync(IProgress<long> progress, CancellationToken cancel)
    {
        var entries = ParseManifest();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(InstallDirectory, "Samples"));
        long done = 0;
        using var throttle = new SemaphoreSlim(4);
        var tasks = entries.Select(async entry =>
        {
            await throttle.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                var target = System.IO.Path.Combine(InstallDirectory, entry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (new System.IO.FileInfo(target) is { Exists: true } existing && existing.Length == entry.Size
                    && await IsVerifiedFileAsync(target, entry, cancel).ConfigureAwait(false))
                {
                    progress.Report(Interlocked.Add(ref done, entry.Size));
                    return;
                }

                var url = BaseUrl + string.Join('/', entry.Path.Split('/').Select(Uri.EscapeDataString));
                var temporary = target + ".part";
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        await using var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                        await using var destination = new System.IO.FileStream(temporary, System.IO.FileMode.Create, System.IO.FileAccess.Write,
                            System.IO.FileShare.None, 131072, System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                        hash.AppendData(Encoding.ASCII.GetBytes($"blob {entry.Size}\0"));
                        var buffer = new byte[131072]; long received = 0;
                        while (true)
                        {
                            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancel).ConfigureAwait(false);
                            if (read == 0) break;
                            received += read;
                            if (received > entry.Size) throw new System.IO.InvalidDataException($"{entry.Path} is larger than its manifest entry.");
                            hash.AppendData(buffer, 0, read);
                            await destination.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                        }
                        await destination.FlushAsync(cancel).ConfigureAwait(false);
                        var actualHash = Convert.ToHexStringLower(hash.GetHashAndReset());
                        if (received != entry.Size || actualHash != entry.BlobSha)
                            throw new System.IO.InvalidDataException($"{entry.Path} failed verification after download.");
                        System.IO.File.Move(temporary, target, overwrite: true);
                        progress.Report(Interlocked.Add(ref done, entry.Size));
                        break;
                    }
                    catch (HttpRequestException) when (attempt < 3)
                    {
                        try { System.IO.File.Delete(temporary); } catch { }
                        await Task.Delay(1000 * attempt, cancel).ConfigureAwait(false);
                    }
                    catch
                    {
                        try { System.IO.File.Delete(temporary); } catch { }
                        throw;
                    }
                }
            }
            finally { throttle.Release(); }
        }).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(InstallDirectory, "ATTRIBUTION.txt"),
            "Salamander Grand Piano V3 — a Yamaha C5 sampled by Alexander Holm.\n" +
            "License: Creative Commons Attribution 3.0 (CC BY 3.0), https://creativecommons.org/licenses/by/3.0/\n" +
            "Source: https://github.com/sfzinstruments/SalamanderGrandPiano (commit " + Commit + ")\n" +
            "The .sfz mapping in this folder was generated by Keyflow for its own playback engine.\n", cancel).ConfigureAwait(false);
        await System.IO.File.WriteAllTextAsync(SfzPath, BuildSfz(), cancel).ConfigureAwait(false);
    }

    private static async Task<bool> IsVerifiedFileAsync(string path, Entry entry, CancellationToken cancel)
    {
        await using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
            System.IO.FileShare.Read, 131072, System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {entry.Size}\0"));
        var buffer = new byte[131072]; long readTotal = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancel).ConfigureAwait(false);
            if (read == 0) break;
            readTotal += read;
            hash.AppendData(buffer, 0, read);
        }
        return readTotal == entry.Size && Convert.ToHexStringLower(hash.GetHashAndReset()) == entry.BlobSha;
    }

    /// <summary>The 30 sampled keys: every minor third from A0 to C8, each covering its neighbours.</summary>
    private static readonly (string Name, int LoKey, int HiKey, int Root, int Release)[] Notes =
    [
        ("A0", 21, 22, 21, 0), ("C1", 23, 25, 24, 0), ("D#1", 26, 28, 27, 0), ("F#1", 29, 31, 30, 0),
        ("A1", 32, 34, 33, 0), ("C2", 35, 37, 36, 0), ("D#2", 38, 40, 39, 0), ("F#2", 41, 43, 42, 0),
        ("A2", 44, 46, 45, 0), ("C3", 47, 49, 48, 0), ("D#3", 50, 52, 51, 0), ("F#3", 53, 55, 54, 0),
        ("A3", 56, 58, 57, 0), ("C4", 59, 61, 60, 0), ("D#4", 62, 64, 63, 0), ("F#4", 65, 67, 66, 0),
        ("A4", 68, 70, 69, 0), ("C5", 71, 73, 72, 0), ("D#5", 74, 76, 75, 0), ("F#5", 77, 79, 78, 0),
        ("A5", 80, 82, 81, 0), ("C6", 83, 85, 84, 0), ("D#6", 86, 88, 87, 0),
        // The top octaves have no dampers on the real instrument, so they ring on after key-off.
        ("F#6", 89, 91, 90, 3), ("A6", 92, 94, 93, 3), ("C7", 95, 97, 96, 4), ("D#7", 98, 100, 99, 4),
        ("F#7", 101, 103, 102, 4), ("A7", 104, 106, 105, 4), ("C8", 107, 108, 108, 4),
    ];

    /// <summary>Velocity windows for the 16 recorded dynamic layers, straight from the source mapping.</summary>
    private static readonly (int Lo, int Hi)[] Layers =
    [
        (1, 26), (27, 34), (35, 36), (37, 43), (44, 46), (47, 50), (51, 56), (57, 64),
        (65, 72), (73, 80), (81, 88), (89, 96), (97, 104), (105, 112), (113, 120), (121, 127),
    ];

    public static string BuildSfz()
    {
        var sfz = new StringBuilder();
        sfz.AppendLine("// Salamander Grand Piano V3 — Yamaha C5, sampled by Alexander Holm (CC BY 3.0).");
        sfz.AppendLine("// Mapping generated by Keyflow for its own SFZ engine. Values follow the");
        sfz.AppendLine("// sfzinstruments reconstruction; see ATTRIBUTION.txt in this folder.");
        sfz.AppendLine("<control> default_path=Samples/");
        sfz.AppendLine("<global> ampeg_release=1");

        for (var layer = 0; layer < Layers.Length; layer++)
        {
            sfz.AppendLine($"<group> lovel={Layers[layer].Lo} hivel={Layers[layer].Hi} // dynamic layer {layer + 1}");
            foreach (var note in Notes)
                sfz.AppendLine($"<region> lokey={note.LoKey} hikey={note.HiKey} pitch_keycenter={note.Root}" +
                               (note.Release > 0 ? $" ampeg_release={note.Release}" : "") + $" sample={note.Name}v{layer + 1}.flac");
        }

        sfz.AppendLine("// Key-off hammer and damper noise, one recording per key.");
        sfz.AppendLine("<group> trigger=release volume=-37 amp_veltrack=82 rt_decay=2");
        for (var key = 21; key <= 108; key++)
            sfz.AppendLine($"<region> key={key} sample=rel{key - 20}.flac");

        sfz.AppendLine("// Strings keep ringing for a moment after the damper lands: three resonance layers.");
        foreach (var (prefix, extra, first, last) in new[]
                 {
                     ("harmL", "volume=-4 amp_veltrack=94 rt_decay=6 lovel=45", 0, 2),
                     ("harmL", "volume=-4 amp_veltrack=94 rt_decay=7 lovel=45", 3, 5),
                     ("harmL", "volume=-4 amp_veltrack=90 rt_decay=8 lovel=45", 6, 9),
                     ("harmL", "volume=-4 amp_veltrack=90 rt_decay=9 lovel=45", 10, 22),
                     ("harmS", "amp_veltrack=95 rt_decay=7 hivel=44", 0, 7),
                     ("harmS", "amp_veltrack=90 rt_decay=7 hivel=44", 8, 22),
                     ("harmV3", "amp_veltrack=96 rt_decay=2", 0, 22),
                 })
        {
            sfz.AppendLine($"<group> trigger=release {extra}");
            for (var i = first; i <= last; i++)
            {
                var note = Notes[i];
                sfz.AppendLine($"<region> lokey={note.LoKey} hikey={note.HiKey} pitch_keycenter={note.Root} sample={prefix}{note.Name}.flac");
            }
        }

        sfz.AppendLine("// The pedal mechanism itself: a thump on the way down, a softer one on the way up.");
        sfz.AppendLine("<group> on_locc64=127 on_hicc64=127 volume=-20");
        sfz.AppendLine("<region> lorand=0 hirand=0.5 sample=pedalD1.flac");
        sfz.AppendLine("<region> lorand=0.5 hirand=1 sample=pedalD2.flac");
        sfz.AppendLine("<group> on_locc64=0 on_hicc64=0 volume=-19");
        sfz.AppendLine("<region> lorand=0 hirand=0.5 sample=pedalU1.flac");
        sfz.AppendLine("<region> lorand=0.5 hirand=1 sample=pedalU2.flac");
        return sfz.ToString();
    }

    /// <summary>641 lines of “git-blob-sha size path”, pinned to <see cref="Commit"/>. Generated, do not edit.</summary>
    private const string Manifest = """
9423ba514607a30a69f999260da96dd265d79685 2164885 Samples/A0v1.flac
e787a20d6167cbed7cfca6d80d4c1e5c517c07c1 2799401 Samples/A0v10.flac
fcc6c45bf0bd29ba299d32d1750f9edbf791fbe4 2734680 Samples/A0v11.flac
18f219e85b86643435fdfa4ec6665c8040545f66 2785384 Samples/A0v12.flac
9a6d8072b524f0d4283a40d6c5886350e7ae9f67 2814539 Samples/A0v13.flac
2afc3cf40088297a0710a757ab02e884f363d70a 2730478 Samples/A0v14.flac
b9fc281cfc3330d67548cd053c98fcec3a355e01 2740560 Samples/A0v15.flac
7c29a8970608feac6e1a907ae560af4b9d802145 2864588 Samples/A0v16.flac
6a6598c07123b48b269116e56351f83b8416eb7a 2179707 Samples/A0v2.flac
f412432623c8dd90eebe4b752ef58fcba876cffb 2346995 Samples/A0v3.flac
535628fa41bfbf9795d2dc8ae5a53304d061b670 2607698 Samples/A0v4.flac
bc8eeee03af472f1e7c700a3eff5dd4c8195a1d3 2638509 Samples/A0v5.flac
96a81274405b889ca1c4fb286b9a1d059cedfae9 2634082 Samples/A0v6.flac
722768e860f198294fce01e150ddcb90535e9fe4 2764837 Samples/A0v7.flac
b287d339ba5b1935eaa15a52dd0e58a682961385 2685849 Samples/A0v8.flac
727bda330566603cee29f16f7c383fa6da4ec0d4 2727560 Samples/A0v9.flac
652d90d5598e035d0391ece0e1feb5a9bfa4da13 2166601 Samples/A1v1.flac
20855977760f9944dc7c520028f86e2016f9ff46 2709073 Samples/A1v10.flac
f96847ba1a8d01f9ba382d45100b6a922f80ef2a 2732379 Samples/A1v11.flac
a251b3f48b712f11565d4caeacbfc6905af02a5f 2786755 Samples/A1v12.flac
aab911e758d16a1872835fc0e5dd36d79789b31b 2779724 Samples/A1v13.flac
c96e8fd14043cdb7e116d6df15b2d0d715ebea7f 2782403 Samples/A1v14.flac
e451211057d9f005df733622cc256a0795e93a15 2823992 Samples/A1v15.flac
3f86fb2ef9ceeca594560619fad231f4aa87a0ed 2879654 Samples/A1v16.flac
a998814a472c478ad0c5fee65d3e09a7a33d09c8 2186772 Samples/A1v2.flac
59b1a4a9426949ac7c76640e8a5c7e0350900780 2262291 Samples/A1v3.flac
79631701dd545f3d8b6f907f0c4c341502e1cfbd 2242163 Samples/A1v4.flac
12dfcdabc0b272ca6d3161b1ed9405ac66d199ab 2402980 Samples/A1v5.flac
9cadd6618240e88db3e97701445ad1496bb7ebb2 2691797 Samples/A1v6.flac
22223d9d655f8af0261e47d7ab70c338af245eed 2668737 Samples/A1v7.flac
529ce51d5a90642ff2fc84a526564c5a971852e7 2707617 Samples/A1v8.flac
a0ce1736d9018ab32740258def6e78cd914e47bb 2707943 Samples/A1v9.flac
d780529440e5b73ad600ab164f90f2d345c261ae 1823625 Samples/A2v1.flac
d5baac2d9a33a2ac3b11eddc2983760911073449 1931380 Samples/A2v10.flac
762733d08ea85d2bfdca56a5fdc7ce1764f640df 1928332 Samples/A2v11.flac
5e09e9e0dee6a74ba90e133bd4f7d127b9f8490b 1976480 Samples/A2v12.flac
d166edfa09c6292b772539a95960f9a11fd2fe2c 2011461 Samples/A2v13.flac
5df522c3ddc18f4b2e6b10f5f12f41193113b2a7 1991765 Samples/A2v14.flac
29c5d2a34051ba52c3c3ff364e8a61672eff58a5 2049937 Samples/A2v15.flac
9feefc52ca202aa8638c7c9407758dcda42fb8de 2086327 Samples/A2v16.flac
cd35494ae9d985e3738c0bae5011bb2506b64913 1856830 Samples/A2v2.flac
5322f6f0e301244438c3fe93dd24504bc437dddc 1838734 Samples/A2v3.flac
174e85b13bb2a848aa7a8d326d230e8118b45f26 1898385 Samples/A2v4.flac
72c9646dd84a8fbe115e77b18b37c1bc106bbe9b 1887699 Samples/A2v5.flac
c5667a910c556503f7b9611b01bb0943094a86fb 1877093 Samples/A2v6.flac
979ed1d2effa56df42dbab7daac722f356a72f26 1903028 Samples/A2v7.flac
d3859c5c2c2d54ffe7c50989bd38c39a4e0c2f05 1901758 Samples/A2v8.flac
8965cd9430b0e8a3759d37868d16ddbf9631307a 1945375 Samples/A2v9.flac
a6d944e938ba66586ee59290a4db4577a2ee14d8 1423583 Samples/A3v1.flac
92766b9a6979abb7d7e2623f89189ba81f012ac0 1758312 Samples/A3v10.flac
f8c4d3ff474498d04a1aad8bc76e2174169153b7 1790713 Samples/A3v11.flac
eeb68a29bca9d50f817cbf667fef6cf52da805d5 1788330 Samples/A3v12.flac
1979f26908f6a3d0f9f1e67e3d58e3722a194855 1814316 Samples/A3v13.flac
9cdd898927186283ae65d2d6b38924cd27eb4683 1814660 Samples/A3v14.flac
f44b6d6767d6750950b69519d7cb3bc000db17da 1874239 Samples/A3v15.flac
989751de51f2eea7bffd036ba866dcf356c11b5d 1901195 Samples/A3v16.flac
2fea156e1475e64ae4480452a190d76f92fbafea 1568956 Samples/A3v2.flac
3ab462d27701c33d673c2bf12059ca74080b4f0b 1660396 Samples/A3v3.flac
85daca85afedb67bcb43091f237369fd11ab471f 1757672 Samples/A3v4.flac
1daaa346f973a9ac486022e0535c9a4a143bbf03 1738211 Samples/A3v5.flac
a760a33dee97421c6e4410dbfc356b4b2b0ebca7 1763993 Samples/A3v6.flac
01459a4f8fd16fa6c8223463c103144667df4aca 1813811 Samples/A3v7.flac
b4620f98c824e392d397866958d2e0b95db3f7ee 1787465 Samples/A3v8.flac
8b165d88124fd0f0ea6af9803aea411de54b7636 1796674 Samples/A3v9.flac
93a30e9f03670085122b052471a747e020515220 1302933 Samples/A4v1.flac
03f67d4185fb0832dc7d71d1f4b6ab5e6411bdbc 1442501 Samples/A4v10.flac
9b3fc4b4cecc1be0a37da49ae9ed82cc6255706e 1452468 Samples/A4v11.flac
b585d2e01dea3a2a9af9fa79d523d00053003e5b 1451680 Samples/A4v12.flac
28586184a0027066d359726de05cf35ebe1b7443 1468684 Samples/A4v13.flac
378fed62805cf0db2bb7b845151eabc76451cb8d 1480560 Samples/A4v14.flac
d79ba25264a3de5517efee975b1961e224fb0914 1531315 Samples/A4v15.flac
34afed4c9cc4554da71be6a115049b8866875836 1602001 Samples/A4v16.flac
e83947525cb9c394c9997b4c583f4550925b0ccb 1308537 Samples/A4v2.flac
0d659f4cf5b153cb37c1eb53d151e3ce06fc4d4a 1353378 Samples/A4v3.flac
1a9018881386288b67074613e667fbf8c19495d9 1383657 Samples/A4v4.flac
c64c25ca4f81346a4701083207b990d5c21f712e 1389193 Samples/A4v5.flac
ec1891dfea1f77d77071beb6a5f8215a4a27ced9 1412851 Samples/A4v6.flac
16165e5075318b017fcccd27172bccf8c1edbab5 1395306 Samples/A4v7.flac
f2d55a0077eca5782b8a70ae42bbe3230dc2cc26 1382828 Samples/A4v8.flac
7b9206eef6d8726847343a7e4246fbdf3bb07290 1412927 Samples/A4v9.flac
1e22a7ec2036dec5676d298e84409410337769b9 697245 Samples/A5v1.flac
365aa2345745b36ce388c30e7cdad1815f953a9d 1123676 Samples/A5v10.flac
b1d655651e6bc7dd7bfdca4204d4e3931caec1b8 1108296 Samples/A5v11.flac
2b6646139a55836d798370917dc3c0c9ff7399ea 1119370 Samples/A5v12.flac
e491c7b956593616d938de6120800aadd7d419c2 1107301 Samples/A5v13.flac
8946d2d4a490f3e544d01dd6ee33c8200ee44141 1162414 Samples/A5v14.flac
6c3ada038787d29fa399a9638dfccce271722cf9 1126306 Samples/A5v15.flac
fce284749647ecde3c3ae7e62cb8c52b59482b2f 1170434 Samples/A5v16.flac
3a3a279c6fdeae186f545ca46172d361bec609b6 692482 Samples/A5v2.flac
4e1c6cb8e48e1978000855917a98e6f729f36714 698413 Samples/A5v3.flac
b5a67dd216af1634e63f6dda86eaa814c4128fac 717980 Samples/A5v4.flac
90acac06598bfabd4a1ce0a890288f83381562c0 893488 Samples/A5v5.flac
f18bc527ca5ee9a9504138d59be9a3ce93390362 1023382 Samples/A5v6.flac
bf6bd73dd2ea92276084566b35fd53db18db0d80 1093287 Samples/A5v7.flac
06c2ab95dda24dfd66c6a29b2add45dca098163d 1109347 Samples/A5v8.flac
6d9cf58af362186629b5d0be5c609f5571af092c 1109304 Samples/A5v9.flac
322210834a2ec14acfbf904b9609fa214adfd9aa 526856 Samples/A6v1.flac
89e03b4ea345079836f7ff0f631c120eae3f7a20 677301 Samples/A6v10.flac
e20f3f18623c37673dd67057fe7ebc04a82aee5a 657585 Samples/A6v11.flac
bc258fd95774bb9b7789ca6139bd7e96aac37772 681845 Samples/A6v12.flac
0cf5110ce17275d17796d65d2c3448d7945b3501 670257 Samples/A6v13.flac
c564d9b2dcd30a5e380de0540b65f0ae8188b25b 691884 Samples/A6v14.flac
b1669ff803456bfc40d19fae42477304d2fbc359 698766 Samples/A6v15.flac
faa24d80d55bbe58b6a072eeae6e9bcdeadb6fca 724195 Samples/A6v16.flac
d82c9e95e4484244094b5a8f63bcd5733abed4dd 571359 Samples/A6v2.flac
297ecf1d323fc87309365904f01b9f478d6a200b 565397 Samples/A6v3.flac
50fbb0b4ed59afbb886f74d76905a04cbd9c5f7f 620431 Samples/A6v4.flac
7c00d71e0d79803617364458d3de6c3d05c86740 665943 Samples/A6v5.flac
8c0eb95262488bdd1c29ba662d4c6ca033422efe 651860 Samples/A6v6.flac
9ebf3d552bf9c6d69be2559487f397a9d823a58c 667567 Samples/A6v7.flac
54c51c1bb786f7d5714d83d912f20727d9906b4c 681474 Samples/A6v8.flac
dd6f2d915284417b1c5e62257b52d7f44e6d26be 667115 Samples/A6v9.flac
5b72b5d4c9fc97bad6ab45ab146f8ff6bb6e0384 241393 Samples/A7v1.flac
5f4a735b7a596d44f90c66815c410422267efb11 377699 Samples/A7v10.flac
8ab27c9c50d3d56b657c06af70cfba746bd6b446 377168 Samples/A7v11.flac
3729453e8d1a3ea2d3e1dd5ba82ccf51fa40e667 374698 Samples/A7v12.flac
eb3cb1106726448642164dc65d1285895f2d4c55 384467 Samples/A7v13.flac
9ebb2f5683b413ee36bde8310209576d5d7db3f1 363441 Samples/A7v14.flac
31c9e783061e52918cdf4ab1aa5d0b8694d002ae 370376 Samples/A7v15.flac
350c68bbbf01c4febf26b6d80d93a6affc2e7d05 384330 Samples/A7v16.flac
70a38e64655a8239a3e169474887bc925f536666 330590 Samples/A7v2.flac
89a861d64bb78d8d221f0eb97da6be024bed7f39 366359 Samples/A7v3.flac
f44a21c5e28ca05a9ad2048602b7a3372c02fd24 366018 Samples/A7v4.flac
a30947fdaf409b94ebc89a5d755b671fbd43fb63 365182 Samples/A7v5.flac
3d8d0d7e6c62bf14c7dbe84e7f19326fe655516f 360892 Samples/A7v6.flac
fa2934b4e4b452a953569507c05b9c93ba834f46 349834 Samples/A7v7.flac
e6709d06167bea4717bd68e489d267346fcc893c 392082 Samples/A7v8.flac
5a7217980aa045472f54859300535a6f6fc115be 356638 Samples/A7v9.flac
67124b0f31daf0d7448aaebb6742d09f99c3d161 2438456 Samples/C1v1.flac
6ed5183c91ebc3e87dfe1cfeb22369416198e0af 2522080 Samples/C1v10.flac
452db540eaca31bc7426f503702ae01a9393cdd6 2534515 Samples/C1v11.flac
29c3a15da3b698dfd28ac9beeea4e540f2357de0 2540684 Samples/C1v12.flac
d4f78a7423984e5808cf9c9979b55509a3a67de2 2551193 Samples/C1v13.flac
dfbbc12b3e233a69f185cd226da2f78f480a48dd 2584712 Samples/C1v14.flac
e0835ec0b9c10d91844fc3692d0b47f6917ba229 2616594 Samples/C1v15.flac
046b085908b960187af96b8c96268785e191b9c3 2585455 Samples/C1v16.flac
78c49e96db79c5d5c3279fabdf4b45bc8572e516 2442369 Samples/C1v2.flac
7ee6af95b68ccabbada25a78dd4de4d4a335e2d4 2472280 Samples/C1v3.flac
886d82cb146d108be4d5ca342bfd9949991a08f2 2479481 Samples/C1v4.flac
735637db74952d4223fa3528b375fafa0fef8f02 2498437 Samples/C1v5.flac
20e5e1813c37ac51b5ecdc3a3fab329563a1d5e3 2510076 Samples/C1v6.flac
56d8192833208351f8448dc199ed0edb0394568e 2507952 Samples/C1v7.flac
0b00306a129aae190d5f67d3711b181693a5572e 2514395 Samples/C1v8.flac
b7b4cced49eaab9d771b2d2b4a37539c84830598 2508365 Samples/C1v9.flac
3548d64c6bcd0927130d8f9590814b5edb09b45c 1980907 Samples/C2v1.flac
9cc0d113f911c0a4b75d67ab1cfc40080c513ec9 2571694 Samples/C2v10.flac
c675c5e2206b280fda3cbc590ab2847665d43bed 2596170 Samples/C2v11.flac
02da76a4ea218bc75bed1d9eed35b7d1965ca255 2608263 Samples/C2v12.flac
ab5b9f203cc41f6127dcd2b395baed55429ed4fe 2624902 Samples/C2v13.flac
27e3ed37c4c0871c3f45d4ceaa479c7089255d45 2642260 Samples/C2v14.flac
53babf89fe6f98f42c0e2bfd81ebdcd3d4b74484 2680349 Samples/C2v15.flac
d8d276b57d9f27c28b797f4792dc3102639b4472 2698276 Samples/C2v16.flac
092c24626d08934258ddc65931662e6445dc196d 2100686 Samples/C2v2.flac
88816eaec5086a8d2eb9c3607c7a95b3efbed333 2252210 Samples/C2v3.flac
44069876a12eb7010efd1d42952bcec27d1c448b 2222842 Samples/C2v4.flac
ed5012311c0b9f53f5b86c7ea9dbde88d560781a 2510357 Samples/C2v5.flac
dbd3fbc4357710eddf8707d82e0c8c13fd48574c 2532539 Samples/C2v6.flac
6c7aa37b0b1014efd6886f8fe781acd27efcb8d3 2540876 Samples/C2v7.flac
377ae98aa10f4e1698b1a0492c24a5817a9293ec 2561685 Samples/C2v8.flac
eaab3e071021f8b39eee01640256586593655538 2564042 Samples/C2v9.flac
208ad6b0a8788653ad57356a11b2b2da71e9a171 1624899 Samples/C3v1.flac
8f738c6a688f7c1b7535396d94c263d6786c3e52 1745075 Samples/C3v10.flac
0a5d7bc258a0b9a69e028bafe7520fb27233c387 1756061 Samples/C3v11.flac
7e06c77fe2f687b01fa110b9be12ce5c6620ed46 1753840 Samples/C3v12.flac
da9f7279ee6d75e5b5820f2e2cf5aa2001c7c5ed 1803471 Samples/C3v13.flac
0272e0501e72b93803701a90248eaece7fe0dca0 1801932 Samples/C3v14.flac
0d7f3effa72101f4a24dd023a10d5c6983725cea 1854312 Samples/C3v15.flac
05391ccf3a7fd724920ffefda57eac91a935fc56 1875350 Samples/C3v16.flac
09c219308f70aeecb0aee64b3699288dc17dc73f 1651757 Samples/C3v2.flac
6b8aebb0461ab8c8bb8168eda55df4771b92cd20 1689056 Samples/C3v3.flac
f444418d4e08bf183c468fc47877a6edac4dc7b3 1689449 Samples/C3v4.flac
c51aef18f9b305ec9a724247be7533b0138be275 1731432 Samples/C3v5.flac
dca2b04df6b7b20f5fbcf28357c1de631c42d681 1737740 Samples/C3v6.flac
be9e29437c4dc1302541b826aaf132ae3b02d13d 1724174 Samples/C3v7.flac
c2d2a2019e13bf292923667a3db1df8a47ea5cfc 1741485 Samples/C3v8.flac
63bf5f090d23dc96a66a98936a823cbcb859ec33 1759949 Samples/C3v9.flac
acf46f2ea8b395cf5609f1411199410f7e93a975 1265676 Samples/C4v1.flac
b5b7a1ee9237d68f475bb75d7f407a8fb334765e 1817704 Samples/C4v10.flac
f1a2af385ef5112d213c77a9c55c23591f78987e 1818768 Samples/C4v11.flac
418e7e1b4ebedc6c9104f711118144941ee4cd06 1776181 Samples/C4v12.flac
29f46c7a7d971465d86403fbf8a8f68ba475165c 1768613 Samples/C4v13.flac
693021f19ae408329c1f2a5d749fe15c82374f38 1787908 Samples/C4v14.flac
19a6239a440341dafe307584a1c9762d9306bc34 1829283 Samples/C4v15.flac
0ab0b0700532f483468a0b299fd2902eaaa59599 1772214 Samples/C4v16.flac
7dd32efe240000ea766f3639e4207d0bad500bc2 1448551 Samples/C4v2.flac
3af205934917e965a99fb4f6784c3ff7b7dd25c4 1697814 Samples/C4v3.flac
e117320966904afc39687b40d6a1285ba2af4083 1628805 Samples/C4v4.flac
f2c8eaa8e01ff2b93a7f39cb1cf6280d25d74604 1685205 Samples/C4v5.flac
82ba1bce4239a026dc76f9dcc0db16f6f9c4ac21 1728236 Samples/C4v6.flac
f2534916543a625409717f8e01edc70c95706c61 1716540 Samples/C4v7.flac
5d1c81dd7ee1d21cbec2fa1f2f605c098cf19679 1774472 Samples/C4v8.flac
13d499beff71b525edc82520e5a717e7bbb7599b 1747731 Samples/C4v9.flac
3194ad58ac43d0868ecada1893a2710298875c46 1238975 Samples/C5v1.flac
23ea6cc91183718127e540155e6ab98fa74da143 1545522 Samples/C5v10.flac
da8c7d4813749f0e338e9643dffb809a88e2bb5e 1464734 Samples/C5v11.flac
773aef039444641fe4d1ec0aa66aad192b40c499 1537892 Samples/C5v12.flac
25169a08ee7987ede03294e2a476b5dd3ea149ab 1498911 Samples/C5v13.flac
78d67b3145e2b3bc3f3e14bc5dee2663a403dffa 1534248 Samples/C5v14.flac
d70182f30f45bdb23487c771c4118c8d98856a9b 1492303 Samples/C5v15.flac
42c23f6132943738fd1ccadaae374e874adaf9c3 1531778 Samples/C5v16.flac
3f75aba977f726103d21188b29b53f76cb203726 1355346 Samples/C5v2.flac
d133894f58fba392492dfdf74adb3781a4a0c17d 1378629 Samples/C5v3.flac
2b72e6ac462075d0b79432ddb4f5fbbe114ca6be 1468588 Samples/C5v4.flac
8703666d2c69323e9bcfbb0d1f1aec8eeaf83c0c 1532927 Samples/C5v5.flac
8cdb7caba928d219b0fee17bbf2e5233d2b58b80 1529239 Samples/C5v6.flac
eba3884496ecbc4cb79313184319759a2d13b686 1543329 Samples/C5v7.flac
8f2a4baab012730693c1b173ce463056c22e42d0 1516971 Samples/C5v8.flac
9b24bb3e9cf683440f70405e43bb0ea3222d1bbd 1542971 Samples/C5v9.flac
8ea5cab9449ab6e59601aae94da384099c96d734 567727 Samples/C6v1.flac
c15d8b70374cfdc51c261da5810c61c6a87b2171 692563 Samples/C6v10.flac
10c8f79e8b73962cc0edf234b1d20aae3a84a82a 727868 Samples/C6v11.flac
7511b31db67a31f8555d83aa5d5f48fbe9365b87 722945 Samples/C6v12.flac
86254f762e0052e7f5f784346edee354c5eba9cd 701778 Samples/C6v13.flac
ed61406726823f859672d3adb087458ec122881a 709598 Samples/C6v14.flac
b6bfc3adf18fe0f9a7196b71b827c8dea9c11d36 722505 Samples/C6v15.flac
d9a3c6e69c421f7ee92869a3a4a0577a02c7cb82 730412 Samples/C6v16.flac
3e8d4c207decb4942597cf1340fe6b11ad232eb8 534649 Samples/C6v2.flac
d19a36a6e1564c90c69992dd980d67634f731ec4 553119 Samples/C6v3.flac
c92b02529426929342b3354731b32b1fd6f06aaf 601137 Samples/C6v4.flac
caa2be196dd23c48208e6a69454976d47868b61e 642347 Samples/C6v5.flac
07a1ef68d9dde5d0c79ae4d2a901df5e00399ee3 667159 Samples/C6v6.flac
648594b9bf3efcf434be50b4597a131c7763bace 689877 Samples/C6v7.flac
4728c69c56c82a98e54cf93a289afd282705317d 695617 Samples/C6v8.flac
a50fa50b99dbafde7519a6eb7a15c7d0cf358ae3 689941 Samples/C6v9.flac
809fe48dc39c3fffb39d7a612d5f38e2b17f0b5f 405957 Samples/C7v1.flac
d4b101d9bf95ce5658c871a52730aa6bf5d632bd 459825 Samples/C7v10.flac
76530cde5c87508dc9c3424acb6e4f84e5a4a99c 444551 Samples/C7v11.flac
bfb00b6a6218ccf23e9101008701bf67e9d927b6 459884 Samples/C7v12.flac
f12aa030392dffc9915db76ccc4d8f2a1fbee79f 470922 Samples/C7v13.flac
de42808a262879d761bc1ba4b8680ee61d61532e 427030 Samples/C7v14.flac
ca442d8e12240e3a6237c51214cfa45a002de8e7 444280 Samples/C7v15.flac
bcff7eba0f3530f510e348b95920015a0ea92cb6 462505 Samples/C7v16.flac
4c2f44223f0cb50978c92a140f40e7fd9ac9ff2d 405498 Samples/C7v2.flac
0668ba7c00a3298a4f4b7f7a04b78908d14b109d 434222 Samples/C7v3.flac
473d480a9216c2cd0d73e8413d42f02de614e388 401277 Samples/C7v4.flac
02b417f24a5393537212105e0f9f921a77ee3a9c 414109 Samples/C7v5.flac
7080ee2fb611ef79d0efa61e8aba9f841093bf07 419770 Samples/C7v6.flac
390e230172a9b768fbe8df13cd3ad0b30c1cef8d 441141 Samples/C7v7.flac
65a6c3d80ffc3a40a3dffbf183972a10e316018e 412115 Samples/C7v8.flac
17485b3a9b169a5176c16ddc1a9718176689386b 413637 Samples/C7v9.flac
0f9019609dcab9e3a28c2aaf2dae4d99adfeef98 331147 Samples/C8v1.flac
8d34eb8753a4048892d5e4876f76bac714ffa6bb 412965 Samples/C8v10.flac
8057e7f266be2a377263ae3d970fad92a778abe5 430355 Samples/C8v11.flac
d40f996dd63c232d9aa5a3c684d330f4c8bc0508 401592 Samples/C8v12.flac
ce96f2ab9679d7fd28a45233a76c6f9eb2833129 443825 Samples/C8v13.flac
994c4f83d356df7f6127d17661f2bf22852317ca 455589 Samples/C8v14.flac
a210681597f6d7948cf1362a307c3614835245ef 458197 Samples/C8v15.flac
19e96c9dc23a0a8618bab726de1ac606ba283e55 421807 Samples/C8v16.flac
625ad6eb73599aa02df844e1e8d97d551bca643e 347437 Samples/C8v2.flac
4a4556603298271bd45d97c2d1c15d18117cebd4 302459 Samples/C8v3.flac
5a322f58cd9cbba1463b680b1801ea75bda0c5a7 306364 Samples/C8v4.flac
445b3bd7c735ed9c18beac33f47d13de7da90870 302658 Samples/C8v5.flac
767a8e48a2d82d5858b48122f41870021a0d684d 402315 Samples/C8v6.flac
7a99355935a4af8ff33ea26489eb25efd91765a8 395305 Samples/C8v7.flac
48ebffd513821de09968fb36560d3d0a384b689c 428472 Samples/C8v8.flac
5d24c49ebb702703257a04d110e95b6448fe7f0e 426387 Samples/C8v9.flac
d2a74f4cf4e924c4cf3b845a128330780175892d 2169326 Samples/D#1v1.flac
b496d5100b4f2bb1c55dda5b708fa6dd777353bc 2876881 Samples/D#1v10.flac
b6cd9f5a5a38dec38eccd520ad399f90c26eb3fe 2892784 Samples/D#1v11.flac
5a40da0718ee19e8fa611d0600cbf833fe743ad9 2896372 Samples/D#1v12.flac
489b01b0df80ba49bfa5db5c40e855fb737bfc10 2921669 Samples/D#1v13.flac
6bdb4e1211b6550f330759b27319eca3e73f3155 2933400 Samples/D#1v14.flac
b24e53faa9e897fd6748113131344108a92c81c0 2944808 Samples/D#1v15.flac
a0ad005c1f0eed7db714d8d2a3e0206a2e9547bc 2983375 Samples/D#1v16.flac
410ffd474af6dc0e6cc4e40f18c1a64ee794552e 2497323 Samples/D#1v2.flac
b93f6cd0a50b1bd73af005993553e5613b44afd5 2496013 Samples/D#1v3.flac
fc5939b97eb88417b8da7a2ba083becc32ba4a69 2570362 Samples/D#1v4.flac
594b59bf2ae10d833f609f3006711f271acdc61b 2840893 Samples/D#1v5.flac
888a721244abbeaaa1cfab271dda91e73d017a3f 2851218 Samples/D#1v6.flac
28c6812cc56b36bee784e575d2d45359c9d7f504 2847777 Samples/D#1v7.flac
f1fe37b4781c702ff75cf1b084b137a0ca571ef6 2889777 Samples/D#1v8.flac
59ad976421e9e3c8cc1b3a15ebcd78a18455e256 2901002 Samples/D#1v9.flac
6d691083463c3e6703527d5e860cfb8a1439d68e 2087725 Samples/D#2v1.flac
fab951535b1b4dbea78eaf35df45729c09186bd2 2606773 Samples/D#2v10.flac
49301f4d41a97cfd20f53350738cf043cedb6321 2575005 Samples/D#2v11.flac
d1a5949eb9cdbc805ce9ed808424e593930549d7 2579711 Samples/D#2v12.flac
8df4477b157ddf9e3914f8e7d92424a0d0b3883d 2611764 Samples/D#2v13.flac
1efdf9356c4c75a29e9f12bdc9a19c1ff9977ed2 2621999 Samples/D#2v14.flac
17306110dca32df6c9de6b9cb0237fb3ed91a818 2693272 Samples/D#2v15.flac
30974f0a48bab534763751ec6c5318eb233263f8 2700194 Samples/D#2v16.flac
f9f108733aad450ea694f01af38d2dd450fb9286 2295789 Samples/D#2v2.flac
b332d02474833db1413d8d3c1d21fcc645651cbf 2435017 Samples/D#2v3.flac
02d16509980233442732fd8bb270e381d86b826b 2546475 Samples/D#2v4.flac
21a1d25e55810b1a175e7471c09c1e800c1ac2d0 2578767 Samples/D#2v5.flac
fe419915589a1c34aeb9a73ddd7ca73b0ac199df 2571610 Samples/D#2v6.flac
93e6690d9fe0d7fb52d7144c9d8bd2708e513f46 2586193 Samples/D#2v7.flac
378ef8fc0820f605b5645e4fd573bbde11350665 2577675 Samples/D#2v8.flac
8f416ecf432a54aed860d1b0dda29905724fb34f 2579428 Samples/D#2v9.flac
57947a54357c5c262f841b359ac4711940d1c688 1381434 Samples/D#3v1.flac
3cf3301db3a797af410b400cf984bc54013d7cbb 1809266 Samples/D#3v10.flac
a48b2246bcf2c933b59f5ef06cce3a0f8fe442be 1815803 Samples/D#3v11.flac
7c636afe2c88b455620bcf55621035c0998b391c 1769835 Samples/D#3v12.flac
0e86fb64462bfd0c173abcfde07d0267a823e858 1790968 Samples/D#3v13.flac
9ef5f3c887d3d05af212cd321f5e039a2477817b 1842436 Samples/D#3v14.flac
322257d63ea3e86f4efc81e95da7721974f752ea 1860531 Samples/D#3v15.flac
916f03d03217bad5288b87a6a41be95e6bb95287 1861439 Samples/D#3v16.flac
36d68f453267b393aebfd390c9ece1db0cfeee54 1580308 Samples/D#3v2.flac
a36d81fd053966eae082046cbcdf87454420564d 1743106 Samples/D#3v3.flac
c15e1da31dc99ff6adcc3cad8ea22f08201b0dd8 1746227 Samples/D#3v4.flac
f844a60324b2138682cb089f0eac21ebbd2a9b52 1790253 Samples/D#3v5.flac
2e8eb3cdc1c4199b96f0aa7a013b6ab5403dd945 1745644 Samples/D#3v6.flac
490363edef02bba6c35523cbd921789d87932817 1748888 Samples/D#3v7.flac
39c4deeac4149cb57627669f9aa7ffe2e7cf96dc 1770977 Samples/D#3v8.flac
5294b746e08a2bfdcf486527a95f33d003824974 1772749 Samples/D#3v9.flac
e4a5d6391f5423b075cddf2e999e285b983d314c 1329951 Samples/D#4v1.flac
ac7f1a68e5e28fe789f7cf734f4b0bc06f268f2a 1553345 Samples/D#4v10.flac
668635338a79eb3294be52241d1caa34da2234db 1572307 Samples/D#4v11.flac
618418d9da018a7b39f76ec08929b9cad232f993 1645028 Samples/D#4v12.flac
99809ca657eadd3110369d33506fe92ea0cf8acf 1674994 Samples/D#4v13.flac
bdd279892181e40bca3c94a9c6e57a9c58674e96 1691587 Samples/D#4v14.flac
bb13277980e43c2bf6dc470ac44dc4365a38a519 1619221 Samples/D#4v15.flac
c0e3c6d245a66378d494373af7c62698e8af00c9 1617165 Samples/D#4v16.flac
f18f984076fd1f5946774b3d2dac387325505317 1365324 Samples/D#4v2.flac
10b16b4f1f8eac28b7ac85c7f0b6b07c8e0a22c6 1448930 Samples/D#4v3.flac
84b262757ef3d13bb03e2ce2f91bf8f20080de87 1550296 Samples/D#4v4.flac
e5b25349b2f5909dc1d9bbd655aa8b900e3444d2 1529007 Samples/D#4v5.flac
cfb0c93dbc91be5a33cbf0f01d729e79c2b3cf7c 1526123 Samples/D#4v6.flac
4dcfa86150a82d26619d1b7f40f4aa1b996ffbbb 1568599 Samples/D#4v7.flac
d29ac26013b03f46ec0c787c05e494437cdd0213 1652067 Samples/D#4v8.flac
fcadc16daa89c332c8710bdbf2025a11b073e505 1583173 Samples/D#4v9.flac
9256559eb2d6a5bf562d913f4f6f159b565c1d36 1198920 Samples/D#5v1.flac
41757e6312472297d836d636a748887700f7c497 1299972 Samples/D#5v10.flac
e7293abe37ef8d272867709122bde95f057cce88 1333643 Samples/D#5v11.flac
1f5e56bd4b02d7f30e88eef34ca587377ad3216f 1346881 Samples/D#5v12.flac
a0dcb164cd598613921cbbe17e073a6b1c4ceba5 1308203 Samples/D#5v13.flac
90de7a03b02debd81683293e7e477abb3e3ffc0c 1371140 Samples/D#5v14.flac
6e868706fb531c0847269e2b6f36e5f687225173 1375399 Samples/D#5v15.flac
1f93fb479a781441711d8e6e30b9fd7ef012104f 1413841 Samples/D#5v16.flac
a6b622e90c793c77413b171aa062c65e3e925ea5 1226144 Samples/D#5v2.flac
207217b5ca20aebabe1a034d3259727b8c5436a7 1232031 Samples/D#5v3.flac
64ad0790398a06cea1f047e846e3365d2d142cc5 1263747 Samples/D#5v4.flac
d47e8942acc6802313234da0bc6cf12e0606a774 1234965 Samples/D#5v5.flac
34b86e4904ef99bf663b45149a45475e59dac377 1274068 Samples/D#5v6.flac
e5f6e95419e98db6eea4734797982484210c47e8 1240910 Samples/D#5v7.flac
2669ad6e47a5d17882db0960d2a05f162b2f0b1a 1276573 Samples/D#5v8.flac
1634d78b28b018ad5675c28540fc6d454affd309 1305830 Samples/D#5v9.flac
23d5620a2da0ec48377c860bad7d838c03bac8ec 767576 Samples/D#6v1.flac
f58fc9ef0ce5aac07e3a3bb51abeb8b094866971 783385 Samples/D#6v10.flac
726b48e4f40d440a198ca932539bc31214c9b8fb 810901 Samples/D#6v11.flac
6972953060aeae45445cacae083a14822a52ec96 804486 Samples/D#6v12.flac
663435207d519e9746f6e8dddac22b61beab1716 805108 Samples/D#6v13.flac
f3a9b0ad8e3e7612b7a62848210a8c753e26cf78 784144 Samples/D#6v14.flac
194699b83ad04f42f6f796835fe81f38fa5a5531 817807 Samples/D#6v15.flac
f3bd1f64826e585be71737d532b367ae1ef20434 781652 Samples/D#6v16.flac
03746a8eed012751ba909fa15865137d9fa74eb7 745050 Samples/D#6v2.flac
dd74c7b0a2f650166e50d7ebbe702abbdfc067e5 749154 Samples/D#6v3.flac
1b17d7e0ae76bda00d370b44d82f50f53bb69e42 743953 Samples/D#6v4.flac
26a45af5d2645ca128caf79b6c044c1554d0c348 776942 Samples/D#6v5.flac
a35c28236911e0fab254992dbf12df2fde7cc4c1 748419 Samples/D#6v6.flac
0c327c74c1828a2b346ce3ebc1cf0af9cb7efa17 771589 Samples/D#6v7.flac
5eec895354095719acfa86d88f08488f3939b058 778561 Samples/D#6v8.flac
bb04827561d02eef026978c7466a9dccdbc0b227 774129 Samples/D#6v9.flac
69de7607ee86396449449c26fad02f6dc5cf13e2 486944 Samples/D#7v1.flac
049b777d8519a7ce858ef1b73e08eece3b8bea8a 489991 Samples/D#7v10.flac
e03c2173bd1e6cb9bdf11b0b1e77c9774a2d4d45 515419 Samples/D#7v11.flac
cfaf8fc8d793a9a64a6cf6af56abbf9a7c491e91 514343 Samples/D#7v12.flac
674f4ab43afd936d1b66ade21d8aa1124f3f45d9 483234 Samples/D#7v13.flac
99facfbbf9b58e087ff11a69fa4d924910105c59 493124 Samples/D#7v14.flac
9e86c01297eedb548f483ca0e6cf4d2a3acea61c 534846 Samples/D#7v15.flac
30e29eebf6e983aa02ee7b271e1a8ef4a7552257 551221 Samples/D#7v16.flac
7233bc211f9b1cf18c10414b8449f0b9d0750104 497381 Samples/D#7v2.flac
a0613beab4c8af61c5401c20395926b5e4e0dc2a 490560 Samples/D#7v3.flac
fb3320c369664e5a290e18ee40585fd962c250ef 473644 Samples/D#7v4.flac
46c8c940e6e25ef4d804dcb5362fb94322c70403 472541 Samples/D#7v5.flac
0a0258bc1816debd0d17771265bc4c0be1e73f5b 461818 Samples/D#7v6.flac
40dd9e83b029529b36cf01b7342a49d2a5979f75 475024 Samples/D#7v7.flac
2abc0bba8df135f77a9697b05836ddc926d5b3da 485801 Samples/D#7v8.flac
70ec0f574b42f01983eb2a2b944b39a91d420998 478906 Samples/D#7v9.flac
0b4e94f5f0f4da0fe992c1803272d9d041fcc98a 1768820 Samples/F#1v1.flac
5b4fdfb6daa433ffff1ebd0b18ee96aca53866e6 2690006 Samples/F#1v10.flac
080d9e0b497daf0fffd2dd6f6815b4d26c5243b8 2711519 Samples/F#1v11.flac
dbccedc2ba13a084571db1cca3667b2b94294099 2729124 Samples/F#1v12.flac
fb3cb9dd519ee10548f28a49a1351aab711a8503 2727847 Samples/F#1v13.flac
45369ea006400ae344aac32fdab1f51f6ce78bac 2765040 Samples/F#1v14.flac
9c0a501caf8f140ea6e82c4cfe2404517743fb16 2762913 Samples/F#1v15.flac
dc0e687edb389b8b60c6b726a83bb9a58953ac8b 2792731 Samples/F#1v16.flac
cae453b5d35c58f06d0fc6a788fd1b33f1137337 2063507 Samples/F#1v2.flac
4fe9da08bed643cfd94ca495d2f15987983f897f 2263896 Samples/F#1v3.flac
dcbae9ecaf328b1cee96b4c61888cf026cfde451 2613244 Samples/F#1v4.flac
a6bccee17162b6ca8ac34017a6b02e525644d49a 2660928 Samples/F#1v5.flac
d145120de99dde2e30ab7ebcdfc9031852792a2e 2650092 Samples/F#1v6.flac
05b88dd49ec6b956963995bb57bdfbbaedc512a1 2684060 Samples/F#1v7.flac
3daaccc66c09a7aa3564e30b27a999c9f7fe5b0c 2694821 Samples/F#1v8.flac
e56f13ab274cbd7a24dbab0db85a2959d20b43ba 2696747 Samples/F#1v9.flac
67543a1394e3fc3de2836ff889901b7098841c2c 1846818 Samples/F#2v1.flac
59f768d31c6f1fad6dbac2957cbd0cece2c23301 2343261 Samples/F#2v10.flac
b0d0da20e1ea2cb1490b1fd320fdca44aa50f2d1 2376880 Samples/F#2v11.flac
a458e12c74a8e8acf4e482aae6fd59548e3bd6a6 2363308 Samples/F#2v12.flac
6b6e041799bc7e22a4bf69c8d3fd605ba92c1dc0 2407608 Samples/F#2v13.flac
825a63bd98e9349ae543266f3f9f408b640d0d4b 2405155 Samples/F#2v14.flac
2c1a2193f1fdd33b379f992c91dd9d6daebec56a 2430966 Samples/F#2v15.flac
1a584ee073caba5915da79675cc3c80364f017a8 2490944 Samples/F#2v16.flac
1a9028f0830149e2c88a1e14192e37c86d131a54 1983201 Samples/F#2v2.flac
4063a36e1c194a8c662f99b012d9743be2519670 2163309 Samples/F#2v3.flac
4920957694cb66c041c7a43718465cddf3138a94 2174299 Samples/F#2v4.flac
ff6ae1c6f9b4bace00a575c54dd19e95cb8aff7d 2314774 Samples/F#2v5.flac
4937825e523ac58a887a8314153a0764f14caef9 2338769 Samples/F#2v6.flac
309c0a37ca3d1b0c18c2ed61d51b486fe849cd67 2307655 Samples/F#2v7.flac
ffb5fdab4db9271b211c36fa2c2da6567d2ac86a 2331259 Samples/F#2v8.flac
bb843fae011a0c091a8134d46495b32712020cb7 2339282 Samples/F#2v9.flac
aba43a2d81f77e79de29869e372f91234ac66bed 1664654 Samples/F#3v1.flac
368334bf28e5fb4ab2ce796ce5b4ec9547b8ecd3 1816439 Samples/F#3v10.flac
54a6073b32757ce431cad41341154cc8fd1577e6 1796864 Samples/F#3v11.flac
c54c2f93814e07043bcbd860d6f23456dbf6690b 1806073 Samples/F#3v12.flac
764eb23513e15caf380a7e3e51783c4dc9d276da 1816533 Samples/F#3v13.flac
d7e71447ad2916022200b3e2a0d38c0300ddc431 1830639 Samples/F#3v14.flac
f9d7d1c3d5f31798d89fc156fa76bae5ba2b3406 1854286 Samples/F#3v15.flac
3d4c1f7f8d23aaa94008e3816a55a2163ac991a1 1815793 Samples/F#3v16.flac
0f7918d8bd9e7b33463f1890d1308e754da5476d 1506111 Samples/F#3v2.flac
f5d078bd6fb4572846765c3e6842496a4c04117a 1754406 Samples/F#3v3.flac
9c606c221ffa7157f0154a0ea3745f49ab558d2e 1767209 Samples/F#3v4.flac
c78aa038ba0f883efad7e8259c478222728e3964 1775604 Samples/F#3v5.flac
0acd8a735eb25405758ded37cb6fc1f112fcca66 1802747 Samples/F#3v6.flac
fc93345e24eaba83a8240d99bb3e24b1941ff97c 1800780 Samples/F#3v7.flac
8b928e0affb6f03174588e8d1bce28b8d538f127 1805025 Samples/F#3v8.flac
91d3a368eb28691f8504b4159334eb1d9cc0ec10 1822051 Samples/F#3v9.flac
97c630cca330707c5da5c712148e1931cf6bf19f 1199292 Samples/F#4v1.flac
73ffb431db8f7b87b3ce87b1bee7a98d37cb227e 1495027 Samples/F#4v10.flac
948521a7594300d91f2b9a4ca08f638d5dccdf7f 1481344 Samples/F#4v11.flac
4787268dc66d6cc75093b92d39efaca3f32ecdea 1549067 Samples/F#4v12.flac
46521483440f9fc333f42421a96324aa3ebbc1d9 1491508 Samples/F#4v13.flac
17c1a8c7b7bbdf533f285597f9f7aeb50f40c9c4 1571127 Samples/F#4v14.flac
25dc91639fd3a0654f0c846b636ce769290d2ec8 1556619 Samples/F#4v15.flac
de02ded96a34777dfa1b99853b5092d84524dbd3 1554241 Samples/F#4v16.flac
a87ef1b2c6de3299d504112ac1e38604702fa4b7 1301432 Samples/F#4v2.flac
41ce06664a2d8386444d01361ca1d9fd8e362c7c 1338739 Samples/F#4v3.flac
54b88eb6ff6b483bb6167d5bafd51adb04312a72 1317911 Samples/F#4v4.flac
451a1afc50c2f8f443733de815fc3b8289bee297 1441445 Samples/F#4v5.flac
c741e53321703c50430ad76903fa19d4f61034a4 1453107 Samples/F#4v6.flac
5e4084d553dea6427001a9fe82f7db07e33eca2b 1484679 Samples/F#4v7.flac
191f7ee347d4513c8c7f0f41222239cfdbf7f497 1495175 Samples/F#4v8.flac
ca3a512a25b2cfa7cb89601afb301961a63fb7c6 1516761 Samples/F#4v9.flac
1fb0be7a07d14ab956caacd7d4aa344a4dd3d399 1040868 Samples/F#5v1.flac
64e705839fb189e5674fe5dcca01ee4ab6ed0298 1147890 Samples/F#5v10.flac
f88e9b4a4cec486716bfbf14e0a59ad8ba1d7b5e 1162182 Samples/F#5v11.flac
62e1d10b8942895485116d08f915aea00f39e306 1169784 Samples/F#5v12.flac
d5859daa8dcac2afe1bde330002a713d5dd13e85 1155608 Samples/F#5v13.flac
f5481f3d7802a838f4affdd402c39bdbb8294eb7 1238210 Samples/F#5v14.flac
8aa11d20b5c0ab1540066dfb57acce505c440c2e 1219482 Samples/F#5v15.flac
61f7121d7256744cfad506c0854db648c8899018 1258455 Samples/F#5v16.flac
484ec36673bfcded5d6e99001168e84b60a0e1df 1073200 Samples/F#5v2.flac
af7bcba971958c30339591e7e698f99a3f6ab5b5 1114230 Samples/F#5v3.flac
142a45fd5c55bdabcf3bb7ee19f3934fac8c10c2 1119439 Samples/F#5v4.flac
811a0bd746be5314f4b278ebe16a9aea83182e9a 1145753 Samples/F#5v5.flac
aee4d9d2dbe45e1888d13a3cf1da0f259285e19a 1180064 Samples/F#5v6.flac
215e7f068035902f8058e0285f850166178d60fd 1189998 Samples/F#5v7.flac
1128d4579bb3a2d48ae1e53104377fd05d566463 1164441 Samples/F#5v8.flac
c1f9e8a2ba8bb746ca1595e6ff8c0d559291211b 1173927 Samples/F#5v9.flac
7b96a51306448f7cea1449455db6494320971f7d 458733 Samples/F#6v1.flac
37b5207734a463a8d74a31b97c26a62b9ee5b25c 696164 Samples/F#6v10.flac
cbd557d3683feab38a238ad970f021958539d99d 679207 Samples/F#6v11.flac
3d365e417225a2a93687073a55cb9b96a691e918 698769 Samples/F#6v12.flac
bf6b7c589d05d90bd6b9b03093af5b732a561f5a 655144 Samples/F#6v13.flac
b948b273ff5c0ab516b598d5e9b35ddf6ef6797e 703793 Samples/F#6v14.flac
1b369ba1764b98c08a117f11c471dfd6db29384e 682072 Samples/F#6v15.flac
e6727a162aea51395f22ad45e59796ca8edcbdb2 701195 Samples/F#6v16.flac
1157469d165f7c5ee227061ed9bc48178c1868de 471711 Samples/F#6v2.flac
31b8fa3230cb5ed8a570b349f7e383eda2f4725a 596575 Samples/F#6v3.flac
cd1f16223c7b79338796155d37e8f9e022dee7a8 605865 Samples/F#6v4.flac
df19892992290caa586a94083901008dc1563f02 618762 Samples/F#6v5.flac
a8802259b3e37b82ff9c4a2a6ea541f45009a255 665664 Samples/F#6v6.flac
6d09af80ac25155977653fb0fa97464554d9a134 682900 Samples/F#6v7.flac
ebf05faa5b7991a848b11e53fc6cebc66e26f1c2 655340 Samples/F#6v8.flac
46139f89f020692b805199d0a0301365b502d38f 661406 Samples/F#6v9.flac
190654850a1c8ee69ebdf3a2a4b817c244fde556 585050 Samples/F#7v1.flac
ab4b49a84c241b5564bb7434d2d163a158395db4 573186 Samples/F#7v10.flac
2eaa924ef150b92846a46e088c75465dac3e8452 592865 Samples/F#7v11.flac
0bc83c0161d4d3129bb3d8fff95c6b78ed90debd 587353 Samples/F#7v12.flac
84e4198cd478643da620db13bdfce7895631be33 572098 Samples/F#7v13.flac
afaacc05689bd922cc3a7abe4687091414e75892 580297 Samples/F#7v14.flac
f923458d5f3daa20f36010def62220ff6c6b8907 589555 Samples/F#7v15.flac
926ea523daa2cdd1394baa6b9e2d9ea4265e4ecb 565916 Samples/F#7v16.flac
560c3903d27264d9c2d4f8de6b5677ae8a9c0160 542101 Samples/F#7v2.flac
af810a740e405fadb7504ad07c88a630ae4ef7cd 551382 Samples/F#7v3.flac
c1ac67e7d0d4de1a90dab5ffad7507d1d968ec0a 570635 Samples/F#7v4.flac
122ae17be788d87621b597dc0202ba0628b2a8e3 548407 Samples/F#7v5.flac
80d8cbd3c8b3578a6c2be10709d8078d9ec0080c 546743 Samples/F#7v6.flac
b6f6adc649bfa16f836a5102d7f93a9558f3a030 555659 Samples/F#7v7.flac
e19f9963d3ca10803e7612c4f601d8a42a16ef22 582250 Samples/F#7v8.flac
a7eab33307ac579ccfc2bbdac1c9a7eb503d8362 541733 Samples/F#7v9.flac
bfdef7fb4af3776e8c6d527e8e6c52187df68704 255386 Samples/harmLA0.flac
b1d7438938e6e7ffb517bc256f7d575a76aecc34 272244 Samples/harmLA1.flac
000d3bba2ce18368767a24579abff3fe1538bd11 263110 Samples/harmLA2.flac
b1349d898d21e6911242fd0dd42e9b05a301c87a 252726 Samples/harmLA3.flac
3960d8687da4ff252b94c51f01e5790c0acdeed8 264718 Samples/harmLA4.flac
d5a0e25a978c66be1446a29bf337b14d34e2ba3d 259336 Samples/harmLA5.flac
701d329d9afb9406a94322fa5128dd5d8168d96f 263110 Samples/harmLC1.flac
883eb68be28c3b0e24fe0c7f64c806d018e8e239 258318 Samples/harmLC2.flac
4fa7d3becac9aa1edcf59e066aad88a4a9aa2d27 253633 Samples/harmLC3.flac
90bec8c9ab6de7ddefda97bcd0ec975102558e5b 259274 Samples/harmLC4.flac
1a42b858af162e193aa1131e1f6fdb84e5b7d369 256165 Samples/harmLC5.flac
2b904f704134661924fc1ca1210f81f367bb7101 250074 Samples/harmLC6.flac
209fe32b16417b1b524fe3489dc503131d8fb6bd 257266 Samples/harmLD#1.flac
d534675931d33bb118d4fb65af82f4a31086b919 261928 Samples/harmLD#2.flac
9f688b09634999048a8d9704acdc0b9c0cc8169b 261096 Samples/harmLD#3.flac
34e0657a90db556b0f5370eeda9157f767caff96 256379 Samples/harmLD#4.flac
91c910f5a7e070316847ab5ea79c48937f500a88 263332 Samples/harmLD#5.flac
dc06392f64f126568d29d4ee934782ebb7e5ea93 256813 Samples/harmLD#6.flac
d8e40ffb232ce16dc7cc357e05bda4ac1c6f81f4 262524 Samples/harmLF#1.flac
849c78a6568aced7438cdc7651d1b3fea057c99d 271060 Samples/harmLF#2.flac
75bf572489b5dedd8f05bf98d9a09469f50e67ae 258166 Samples/harmLF#3.flac
919c0ec6d295933c3ebb7cd3f16388ba1ce5581e 257975 Samples/harmLF#4.flac
b37fa2046f6d92831d633d4bc4d5df5f1eb3ef6f 267374 Samples/harmLF#5.flac
899ab349ee888b51c1c4721484bf7d88aaa6f36c 178862 Samples/harmSA0.flac
613959ff800c0d42034c65e76ae81643ea424f15 186159 Samples/harmSA1.flac
9950cf2ebcfc7f6bd9034eb7f3beac640f689cc5 185981 Samples/harmSA2.flac
b093bf845f0ab78220556e88349ba3148ba59b38 186379 Samples/harmSA3.flac
5a37c26c6896facf727c4b999478c9e4006134c6 156974 Samples/harmSA4.flac
2102602aebd3cb4eeb10d0dbddf02b6144fa11fe 169211 Samples/harmSA5.flac
2cfe5e2c527ccd7d985812c14c32143ac0cd0912 185044 Samples/harmSC1.flac
afbf9ca70cf71f5843bcd5f47c0e12ac9bcd45bc 184590 Samples/harmSC2.flac
a984113202b19608127c8e6e866a681f757d2933 185015 Samples/harmSC3.flac
d747d3e9b31d220a836713af65d2b34d381bf9ce 171481 Samples/harmSC4.flac
a3d4754b439c7b1c73ffd402cb788090f3977009 176373 Samples/harmSC5.flac
4868cd53beed10aa919e69ffff41854d9bee98e6 182397 Samples/harmSC6.flac
f4e50dfe52b09761c5c64397672e8283747741b7 175943 Samples/harmSD#1.flac
1fbd13970afbfa60a281e6254cc19200b741fd84 173444 Samples/harmSD#2.flac
02c970551c2f046cca5e402db83f079e0241e14a 174563 Samples/harmSD#3.flac
e732d68e4424704e469243d41a6c1b7730b62a6f 171557 Samples/harmSD#4.flac
6b98ff37b0e9649dc7a27b624dc99783d4d92b4b 180487 Samples/harmSD#5.flac
387d2df94e6cbb44eba98ad657b35b73c431f955 189071 Samples/harmSD#6.flac
08092b8a31f2d17ccb44dd238dc1e10a995a2e5e 180952 Samples/harmSF#1.flac
542e12e3343bbdfc082a71cee18f2ca19749bf4b 179729 Samples/harmSF#2.flac
5859183fc04fd68a56c854cf955d57a4bc141e7c 176537 Samples/harmSF#3.flac
ca2b1eeec4607c681f4037b3c01c9b51ea273407 174543 Samples/harmSF#4.flac
b75a32a9c9c8433ae5d756e18f286a8065f04fd2 169559 Samples/harmSF#5.flac
80dc5ff5be563c1d6baad9e92d13411dfaf6eb2b 172285 Samples/harmV3A0.flac
7f17751caef7b69fec9ef207111d3cd59770b313 173285 Samples/harmV3A1.flac
0c55fba63c5cf1e4a56a1933f955fb06b07105b7 171899 Samples/harmV3A2.flac
97e0b7f4db4b7e4c8c493aad57424e92c217fe59 171560 Samples/harmV3A3.flac
5f11ba7325f798c3cc5e4fba79688b9951b388e1 168681 Samples/harmV3A4.flac
1d9a8411885f7e9f03d84d9b8c19e26d2d23a63f 169457 Samples/harmV3A5.flac
94d81df031b5dcc827734753a1e01cbae9ef1d66 168114 Samples/harmV3C1.flac
98e15aae25d112295aae2dc8616a83fe7abe73bc 173135 Samples/harmV3C2.flac
f004f28fa45177c55e1047a59ae7260c24f68539 166101 Samples/harmV3C3.flac
893b2c86d76ce1ac7b901605e060abe6330dd850 170533 Samples/harmV3C4.flac
012da66d526829e9fcb4ff86c61177be64f10af5 172604 Samples/harmV3C5.flac
f4abfcc0e9d35d36ab762d22e692170ff5a6d094 169923 Samples/harmV3C6.flac
e3b77af890d4e48a8ce73260bd787656966cded5 170194 Samples/harmV3D#1.flac
5afdb84bedf06ec37cf87e969f2fdbec0f3429ba 169058 Samples/harmV3D#2.flac
ba8e25752be996070be6bc09dc41e16c9edbabc9 172741 Samples/harmV3D#3.flac
ecaeea545a5ff712891093c2563e987a1d9c6d9e 172222 Samples/harmV3D#4.flac
3d2d0e61aec0324312961e566dd39acbf9177496 167875 Samples/harmV3D#5.flac
b065bf169e72c78d6a231c3bf49e6e929a404ba5 177184 Samples/harmV3D#6.flac
baf275171a30285de5b22163f0afdf0f91f9d460 173150 Samples/harmV3F#1.flac
109675de4532ddf8d6c65e012b32f2781011470c 168882 Samples/harmV3F#2.flac
63b19ef7232ea17586bc13e68a6874620a80bd5f 168930 Samples/harmV3F#3.flac
53e522fac3f1ee6c1f789e0e3ff949ca008672d6 174028 Samples/harmV3F#4.flac
713aa4db9be2d7ac19c810445199449e43340301 171059 Samples/harmV3F#5.flac
098828eb035e42354de3b0710556bbcb0f29fc27 824409 Samples/pedalD1.flac
467291af801c900897054dbe91a3ebee64fb98ad 832915 Samples/pedalD2.flac
b7c0e6228151fcc148837b5aa7a55f0007193618 77855 Samples/pedalU1.flac
50d0bf224f14fa36ca9e62f7f769879fc21c28cd 67694 Samples/pedalU2.flac
942a88601dcba2f5badb94d1c226fce95b5c1366 65800 Samples/rel1.flac
bc4ae72c7ef0c5d12350caf27fba8ef87cfbab93 64739 Samples/rel10.flac
23a8881d9cbbc1db7a38b9055843558cce62bde0 62880 Samples/rel11.flac
1f778d0177358c58ed86babeb9a74a8dddd31dbd 65049 Samples/rel12.flac
97e9e3dbfc9e743ab2f262bdef7e0c36508a1e9d 63589 Samples/rel13.flac
191cedbdecb31f976526d30c18d8c6c5efdb2446 65184 Samples/rel14.flac
a81ab211c62a725fe5e8477dc265c2da4ee9f9de 64733 Samples/rel15.flac
561295622d6485b7fef9feb19d308dfbfbf47783 64941 Samples/rel16.flac
923944e2491a7be9ef0f8c841c51d43f55eac8a7 63124 Samples/rel17.flac
f2a2a70925b54cf6e16044d5b63c60837657a740 64463 Samples/rel18.flac
7e20e3a65fb8c773d7b60a4dc3a4699af1570c1d 65818 Samples/rel19.flac
143053ad760002dfe8eaacd307355e0db88eb9cb 65814 Samples/rel2.flac
5c55a8a36351b3b5960a3824ac911f03e5d3a376 63266 Samples/rel20.flac
ee089f35d9247da1104b4f93fc7ba7aee69196cd 61488 Samples/rel21.flac
b9ee47ebf3d48620e164a030b8ee72e0cc4d099b 62347 Samples/rel22.flac
3257dc07bb478d1a95a0d3cdef1151714230f2d9 63501 Samples/rel23.flac
f8b748c6ed22918a0808de52937f64db26dc0120 63789 Samples/rel24.flac
1c8221d18576e67dca1c79a10a56b9ea8f011dd7 63836 Samples/rel25.flac
b79b5affe79d606157146c72a28033372f30185b 64540 Samples/rel26.flac
a332dbc4ff960d3d242e7b07bfe1e7beefe3e0da 63514 Samples/rel27.flac
4a9b2167e81a0ad42e3fad35411da45bd8629ded 62755 Samples/rel28.flac
88ca1caf2cc2638b8ac94a58e1bb71cca31431c3 64416 Samples/rel29.flac
7f4db2fff75f132fdab3c4bd216b879fd7b4f987 62713 Samples/rel3.flac
c15d18c07dbee38440b8d50a437b0cbe1040b781 62281 Samples/rel30.flac
ecbd1f8f3c6c87d829c5e04a052da65727030572 64405 Samples/rel31.flac
681a5650bbae266ce44af8c63d602d239cd8ac08 63896 Samples/rel32.flac
4251ea1691decbc2a81c538f1c781f9ed8e5e048 65237 Samples/rel33.flac
dedc32cb6995836c03d8dba3478c8a14b8f83f1f 65028 Samples/rel34.flac
548018828f4687c45fe23af9d8457a86575e5d0c 62480 Samples/rel35.flac
ad5997a46bf30e6d8c622868c6ba1f488a2efd61 63338 Samples/rel36.flac
7ba4c00927fe01b5da9546fdd630e59dd1cefc2e 63314 Samples/rel37.flac
073746acc818319d9d706d5d921f4b0c9e2ed667 62182 Samples/rel38.flac
01049566e4684170b7cf0d09aeaad66549b9298a 62861 Samples/rel39.flac
39208169c2a11e072ca31e7eaabee61678387053 65645 Samples/rel4.flac
4c8946aaada7b4a9a15ef4cd080b4dd78ec40a0f 63966 Samples/rel40.flac
4bb524c8c708fbd9ae36b94334cde808fa2d0154 62226 Samples/rel41.flac
ab58d110dba7f18ac82ea2b835520d777cdf80dd 63106 Samples/rel42.flac
66d656e38c545d3c629200407584d65e69abf52e 61000 Samples/rel43.flac
772e6482b905e92b2722f13e706d86c66e99eab9 64654 Samples/rel44.flac
feea8c7106fdbc19a619eb6d2cef3d8d1e3d9dfc 64774 Samples/rel45.flac
d6d65cd0a0a76c5003002242a52ad252d9615a28 60464 Samples/rel46.flac
26a13403e2f55b4ae1dc94bc1087288de1a61df0 63775 Samples/rel47.flac
8e4ad9d7fccd737c54b8129a9a67ce688f24f07b 62870 Samples/rel48.flac
ce6b3ab5866723bcd83e44ce6b52b2fc72a92c91 65187 Samples/rel49.flac
2b5db8f2e26547487adef21c3f11093f45b7b3c9 67904 Samples/rel5.flac
17f5efcef5e6935f5f6486571451027df8c807d3 64851 Samples/rel50.flac
ba1f3c062aff7881bbcd14bdd9f50b7293017a21 64456 Samples/rel51.flac
f712a57b96e7bfcd0d8d3336cd0ac40f95bddd6d 61757 Samples/rel52.flac
7e17ada9339e78245adccabf66d985f03f7d2301 65363 Samples/rel53.flac
d995b9afcb0d72c1e2dc476143483c2f22048889 64475 Samples/rel54.flac
f0a463659e9a6b535b96fabee4288750b9f57fec 60317 Samples/rel55.flac
8a16a5a377689d5bf683580f318be3c143e390d4 60661 Samples/rel56.flac
2b909ca529b492082d4f89a863dc60e7d4e7fc4d 62320 Samples/rel57.flac
bc1647407eb3a27315b8febaf1345e4efa49ebb6 62869 Samples/rel58.flac
e7d559ba154a65f11224a28effd576bc2a70a977 64365 Samples/rel59.flac
8a7515bc31530c561f92e894ae69bf216afd5884 64228 Samples/rel6.flac
35dba2623fa2420c4062752f45e16bbd2fd18f72 62794 Samples/rel60.flac
25c7ca84bea41fc6b6bf014d173a83b5146d8822 62634 Samples/rel61.flac
8825ae86e16d22e2c8d3447d9eb6c7f0b400dc26 64289 Samples/rel62.flac
dd291139beb802d3ae7b042ce040a98ff2311775 63445 Samples/rel63.flac
d59eeb08d25475503496ad854073dd3284084d3b 61812 Samples/rel64.flac
d8be059af24e11fa1e1a785c5dcc192c9bdc6f2a 63777 Samples/rel65.flac
7507c24c231d477c66d48ee87bed75f6b7ec4b94 63113 Samples/rel66.flac
9fead7e840271dbbe4b94c8e65f0644130a8a707 62209 Samples/rel67.flac
f8d88e648d9539c662946796908b3486c9395373 63016 Samples/rel68.flac
6b86dda52841876e2b799b7f5e70f47d2655a7d7 63634 Samples/rel69.flac
d94eea14c337cd545b56cc99f648321fdd9b4844 65815 Samples/rel7.flac
57a766e12976367e2087155dff3bdf46e658b923 62235 Samples/rel70.flac
e5be6e1665e22436e35c5d6b178fefbb6113863f 64302 Samples/rel71.flac
fd8b702f1d2b51dde1238648c2d3e655da1b8991 64955 Samples/rel72.flac
61fe08184c8acc85702aaf3c293214b36b5ce3c3 65240 Samples/rel73.flac
9397f3e258cba7fadf1afd045af9cad0988b647f 63103 Samples/rel74.flac
17d24c0c741b037675edd3859f378c2040d8a6e5 62389 Samples/rel75.flac
2f702d46a46c2885ae121d4d003d5e186127de54 55686 Samples/rel76.flac
0f7fadfc61a166b99ff11105122e8836b5171e94 64076 Samples/rel77.flac
66fdfe659e9a3a1651062d4a5db1ce24cd588b38 64028 Samples/rel78.flac
17ab7d6b0156dd36723811d7cc8b73e8746d24b1 65881 Samples/rel79.flac
f1d98dc3c6b3ce20a517c8f3ba36248b334a8f70 66131 Samples/rel8.flac
ee6972ac0841c71f55b0ec1f6f25885703bb7313 66306 Samples/rel80.flac
814025901e03c92b6e0bef370a8fbafb978780f8 58584 Samples/rel81.flac
19f110eb39c8a537f3c0154ba6dd4818e744269a 67693 Samples/rel82.flac
99f265a445c6bf95f5e4f25937d805f7d9eb5852 65751 Samples/rel83.flac
c7b851dd178864c6212105ae6760ea6cc4758850 65091 Samples/rel84.flac
00a7d179c28102df73f10c6594c111410dcf768a 64606 Samples/rel85.flac
14214d8557dd417c2eb1c67ac63501e1e93bc3e8 66664 Samples/rel86.flac
954813fb5afe480d7e13c58a7825965d62eeaf28 64663 Samples/rel87.flac
8a6c4e628b8d7d5796423a8a758f627146479641 63299 Samples/rel88.flac
7ad64195d9ef51a013f5ce7f504ee9f1de786a0c 63913 Samples/rel9.flac
""";
}
