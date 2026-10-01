using System.Globalization;

namespace PianoPath;

/// <summary>
/// One interface language: a stable id, the names the picker prints and the English → localized
/// string table.
/// </summary>
/// <param name="Id">Stable slug stored in the settings file (<c>en</c>, <c>vi</c>).</param>
/// <param name="EnglishName">Name in English, used for the menu chip and the changelog.</param>
/// <param name="NativeName">Name as its own speakers write it (<c>Tiếng Việt</c>).</param>
/// <param name="Table">Translations keyed by the English source text.</param>
internal sealed record AppLanguage(string Id, string EnglishName, string NativeName, IReadOnlyDictionary<string, string> Table)
{
    /// <summary>What the pickers print: the native name with the English one beside it.</summary>
    internal string Display => string.Equals(NativeName, EnglishName, StringComparison.Ordinal) ? NativeName : $"{NativeName} · {EnglishName}";
}

/// <summary>
/// The languages Keyflow ships and how a stored value is resolved.
///
/// A language is a <em>table</em>, never a branch in the renderer: adding one means adding an
/// <see cref="AppLanguage"/> here plus a table in <c>Strings.&lt;Language&gt;.cs</c>. Arabic or
/// right-to-left layouts would additionally need flow direction, which is deliberately out of scope
/// until a table for such a language exists (see <c>docs/LOCALIZATION.md</c>).
/// </summary>
internal static class Languages
{
    internal static readonly AppLanguage English = new("en", "English", "English", StringsEnglish.Table);
    internal static readonly AppLanguage Vietnamese = new("vi", "Vietnamese", "Tiếng Việt", StringsVietnamese.Table);

    /// <summary>Picker order; the first entry is the language used when the system asks for none we know.</summary>
    internal static readonly AppLanguage[] All = [English, Vietnamese];

    internal static AppLanguage Default => English;

    /// <summary>Maps an OS culture to a bundled language. Only the language subtag matters.</summary>
    internal static AppLanguage FromCulture(CultureInfo? culture)
    {
        var tag = culture?.TwoLetterISOLanguageName ?? "";
        return All.FirstOrDefault(language => string.Equals(language.Id, tag, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }

    /// <summary>
    /// Resolves a stored value: an id (<c>vi</c>), a culture tag (<c>vi-VN</c>), the English name or
    /// the native name. Unknown values fall back to English.
    /// </summary>
    internal static AppLanguage Find(string? id)
    {
        var value = id?.Trim();
        if (string.IsNullOrEmpty(value)) return Default;
        var language = value.Split('-', '_')[0];
        return All.FirstOrDefault(candidate => string.Equals(candidate.Id, language, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(candidate => string.Equals(candidate.EnglishName, value, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(candidate => string.Equals(candidate.NativeName, value, StringComparison.OrdinalIgnoreCase))
            ?? Default;
    }

    internal static string Normalize(string? id) => Find(id).Id;
}

/// <summary>
/// The runtime string table.
///
/// Keys are the English source text ("Falling notes", "Preset “{0}” applied"), which keeps the code
/// readable, makes a missing translation fall back to English instead of a blank label, and lets a
/// translator find a string by grepping the source. <see cref="StringsEnglish"/> is the canonical
/// inventory of every key; <c>tools/check_sources.py</c> proves that the bundled translations carry
/// the same key set and that every <c>T("…")</c> in the sources resolves.
///
/// Surfaces never print a literal and never compare one: settings values, preset names and theme ids
/// stay in English and are translated only for display, so switching language can never change what
/// is stored on disk.
///
/// <para>
/// This class is split in two on purpose. Everything here is plain computation — a table, a lookup,
/// the diagnostics that <c>--verify</c> reads — so a test project can compile it on any OS. The half
/// that paints live WPF labels lives in <c>Localization/Localizer.cs</c> and joins this one through
/// <see cref="RefreshLiveLabels"/>, a partial method: a build that links only this file compiles the
/// call away instead of needing WindowsBase. That seam is what lets <c>tests/PianoPath.Tests</c> cover
/// the MIDI, MusicXML, SoundFont and history code, all of which report through <see cref="T"/>.
/// </para>
/// </summary>
internal static partial class Loc
{
    /// <summary>Raised after a new language has been published, so open surfaces can restyle.</summary>
    internal static event Action? Changed;

    internal static AppLanguage Current { get; private set; } = Languages.Default;

    /// <summary>Round-trip: what the settings file stores. Empty means "follow Windows".</summary>
    internal static string StoredId { get; private set; } = "";

    /// <summary>Keys the sources used that the English inventory does not know about (a bug, checked by <c>--verify</c>).</summary>
    internal static IReadOnlyCollection<string> UnknownKeys { get { lock (_diagnostics) return [.. _unknownKeys]; } }

    /// <summary>Keys the active language is missing; they fall back to English (checked by <c>--verify</c>).</summary>
    internal static IReadOnlyCollection<string> UntranslatedKeys { get { lock (_diagnostics) return [.. _untranslatedKeys]; } }

    private static readonly object _diagnostics = new();
    private static readonly HashSet<string> _unknownKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _untranslatedKeys = new(StringComparer.Ordinal);
    private static readonly List<Action> _subscribers = [];

    // =================================================================================================
    // Look-up
    // =================================================================================================

    /// <summary>Translates one English source string; an unknown key is returned unchanged.</summary>
    internal static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (Current.Table.TryGetValue(key, out var translated) && translated.Length > 0) return translated;
        // A key that is not in the inventory is usually a user string (a device name, a preset file)
        // and not a bug, while a key in the inventory that this language lacks is a missing
        // translation. Both are collected for --verify, from the UI thread and from the audio thread
        // alike (a waveOut failure reports itself through a localized message), hence the lock.
        if (StringsEnglish.Table.ContainsKey(key))
        {
            if (!ReferenceEquals(Current, Languages.Default)) lock (_diagnostics) _untranslatedKeys.Add(key);
        }
        else lock (_diagnostics) _unknownKeys.Add(key);
        return key;
    }

    /// <summary>True when the text is a string of the interface (and therefore worth translating).
    /// A row's search phrase or a name the user typed is not: looking those up would fall through to
    /// English anyway and report a phantom unknown key.</summary>
    internal static bool Known(string key) => StringsEnglish.Table.ContainsKey(key);

    /// <summary>Translates a template and fills its placeholders, for text built at runtime.</summary>
    internal static string F(string template, params object?[] args) =>
        args.Length == 0 ? T(template) : string.Format(CultureInfo.CurrentCulture, T(template), args);

    /// <summary>Names an entry of the settings catalogue (page, section) through the same table.</summary>
    internal static string Page(string name) => T(name);

    /// <summary>Runs <paramref name="apply"/> once now and again after every language change (for rebuilt surfaces).</summary>
    internal static void OnChanged(Action apply) => _subscribers.Add(apply);

    /// <summary>Re-applies every tracked label: called once at startup and after each switch.</summary>
    internal static void Refresh()
    {
        RefreshLiveLabels();
        foreach (var apply in _subscribers.ToArray()) apply();
    }

    /// <summary>
    /// Repaints the labels the interface has bound. Implemented in <c>Localization/Localizer.cs</c>
    /// over the tracked WPF elements; in a build without that file the compiler removes the call, so
    /// <see cref="Refresh"/> still runs the plain subscribers and nothing needs WindowsBase.
    /// </summary>
    static partial void RefreshLiveLabels();

    /// <summary>
    /// Applies a language and repaints every live label. <paramref name="storedId"/> is what the
    /// settings file keeps: an empty value means "follow Windows" and is resolved on the spot.
    /// </summary>
    internal static void Apply(string? storedId, bool notify = true)
    {
        StoredId = storedId?.Trim() ?? "";
        Current = string.IsNullOrEmpty(StoredId) ? Languages.FromCulture(CultureInfo.CurrentUICulture) : Languages.Find(StoredId);
        lock (_diagnostics) _untranslatedKeys.Clear();
        if (notify) Refresh();
        Changed?.Invoke();
    }

    // =================================================================================================
    // Diagnostics
    // =================================================================================================

    /// <summary>Forgets what has been collected so a verification run starts from a clean slate.</summary>
    internal static void ResetDiagnostics()
    {
        lock (_diagnostics)
        {
            _unknownKeys.Clear();
            _untranslatedKeys.Clear();
        }
    }

    /// <summary>Keys of the English inventory that the active language does not translate.</summary>
    internal static IReadOnlyList<string> MissingFor(AppLanguage language) =>
        [.. StringsEnglish.Table.Keys.Where(key => !language.Table.ContainsKey(key)).Order(StringComparer.Ordinal)];
}
