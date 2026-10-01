using System.Reflection;

namespace PianoPath;

/// <summary>
/// The release version of the running build, read out of the assembly instead of being typed again.
/// </summary>
/// <remarks>
/// <para>
/// The number a user can see used to be written down four times over: <c>&lt;Version&gt;</c> in
/// <c>PianoPath.csproj</c>, the <c>#define AppVersion</c> fallback of <c>installer\Keyflow.iss</c>, the
/// start-up menu's own <c>?? "0.4"</c> fallback, and a whole sentence of the About box. A release cut
/// from that could therefore open a menu saying one version above an About box saying another — and the
/// About box is the one place a reader checks when a download looks wrong. Everything the interface
/// prints now comes from here, and here it comes from the assembly the SDK stamped.
/// </para>
/// <para>
/// The copies that cannot read an assembly (the installer fallback, the README examples, the two
/// CHANGELOGs) are pinned to the same number by <c>scan_release_version</c> in
/// <c>tools/check_sources.py</c>, so editing <c>&lt;Version&gt;</c> is still the only change in code a
/// release needs — and forgetting one of the mirrors turns the static check red instead of shipping.
/// </para>
/// </remarks>
internal static class AppInfo
{
    /// <summary>
    /// The release version: <c>&lt;Version&gt;</c> in <c>PianoPath.csproj</c>, which the SDK writes into
    /// the assembly as both the file version (<c>1.0.0.0</c>) and the informational one (<c>1.0.0</c>).
    /// SourceLink appends <c>+&lt;commit&gt;</c> to the informational version, and a build id is not what
    /// a reader of the About box is asking for, so only the part before the <c>+</c> is kept.
    /// </summary>
    internal static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0].Trim();
        if (!string.IsNullOrWhiteSpace(informational)) return informational;
        // No informational version — a source file linked into another assembly, which is how
        // tests/PianoPath.Tests compiles the product code it needs. The assembly version's first three
        // parts are the same release number, so the fallback still reads like one.
        return assembly.GetName().Version?.ToString(3) ?? "0.0";
    }
}
