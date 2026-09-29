using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
/// A language is a <em>table</em>, never a branch in the renderer: adding one means adding a
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
/// </summary>
internal static class Loc
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
    private static readonly List<WeakReference<DependencyObject>> _live = [];
    private static readonly List<Action> _subscribers = [];
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Func<string>>> _renderers = new();

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

    /// <summary>Translates a template and fills its placeholders, for text built at runtime.</summary>
    internal static string F(string template, params object?[] args) =>
        args.Length == 0 ? T(template) : string.Format(CultureInfo.CurrentCulture, T(template), args);

    /// <summary>Names an entry of the settings catalogue (page, section) through the same table.</summary>
    internal static string Page(string name) => T(name);

    // =================================================================================================
    // Live text: a label that follows the language for as long as it is on screen
    // =================================================================================================

    /// <summary>Binds an element property to a renderer that is re-run whenever the language changes.</summary>
    internal static void Bind(DependencyObject element, Func<string> render, DependencyProperty? property = null)
    {
        property ??= DefaultProperty(element);
        if (property is null) return;
        var table = _renderers.GetValue(element, static _ => []);
        table[property] = render;
        if (!IsTracked(element)) _live.Add(new WeakReference<DependencyObject>(element));
        element.SetValue(property, render());
    }

    /// <summary>Binds an element property to a fixed English source string (the common case).</summary>
    internal static void Set(DependencyObject element, string key, DependencyProperty? property = null) =>
        Bind(element, () => T(key), property);

    /// <summary>Binds an element property to a template plus arguments, both re-evaluated on a switch.</summary>
    internal static void Format(DependencyObject element, string template, params object?[] args) =>
        Bind(element, () => F(template, args));

    /// <summary>Same, for an explicit property (tooltips and captions are not always the default one).</summary>
    internal static void Format(DependencyObject element, string template, DependencyProperty property, params object?[] args) =>
        Bind(element, () => F(template, args), property);

    /// <summary>Runs <paramref name="apply"/> once now and again after every language change (for rebuilt surfaces).</summary>
    internal static void OnChanged(Action apply) => _subscribers.Add(apply);

    /// <summary>Re-applies every tracked label: called once at startup and after each switch.</summary>
    internal static void Refresh()
    {
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            if (!_live[i].TryGetTarget(out var element)) { _live.RemoveAt(i); continue; }
            if (!_renderers.TryGetValue(element, out var table)) { _live.RemoveAt(i); continue; }
            foreach (var (property, render) in table) element.SetValue(property, render());
        }
        foreach (var apply in _subscribers.ToArray()) apply();
    }

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
    // XAML markers: local:Loc.Localize="True" on any element with a literal Text/Content/Header/ToolTip
    // =================================================================================================

    /// <summary>
    /// Marks an element whose literal text is translatable. The English literal stays in the XAML as
    /// the key, so the markup keeps reading naturally and a missing translation shows English.
    /// </summary>
    internal static readonly DependencyProperty LocalizeProperty = DependencyProperty.RegisterAttached(
        "Localize", typeof(bool), typeof(Loc), new PropertyMetadata(false, (element, e) => { if (e.NewValue is true) Track(element); }));

    internal static void SetLocalize(DependencyObject element, bool value) => element.SetValue(LocalizeProperty, value);

    internal static bool GetLocalize(DependencyObject element) => (bool)element.GetValue(LocalizeProperty);

    /// <summary>The localizable properties of one element, in the order they are looked up.</summary>
    private static readonly DependencyProperty[] CandidateProperties =
    [
        TextBlock.TextProperty,
        HeaderedContentControl.HeaderProperty,
        ContentControl.ContentProperty,
        FrameworkElement.ToolTipProperty,
        Window.TitleProperty,
    ];

    /// <summary>
    /// Registers one marked element: every literal it carries becomes a key, and the element is
    /// repainted by <see cref="Refresh"/> from then on. Called by the attached property, or once per
    /// window by <see cref="LocalizeTree"/> for elements that carry no marker themselves.
    /// </summary>
    internal static void Track(DependencyObject element)
    {
        if (!_renderers.TryGetValue(element, out var table)) table = _renderers.GetValue(element, static _ => []);
        foreach (var property in CandidateProperties)
        {
            if (table.ContainsKey(property)) continue;
            if (element.ReadLocalValue(property) is not string { Length: > 1 } text) continue;
            // A glyph button (the ↺ of the speed reset, the A and B of the loop) carries a symbol in
            // the same property that holds a caption elsewhere; symbols are not translatable, and
            // binding them would report every one of them as an unknown key.
            if (!text.Any(char.IsLetter)) continue;
            table[property] = () => T(text);
        }
        if (table.Count > 0 && !IsTracked(element)) _live.Add(new WeakReference<DependencyObject>(element));
    }

    /// <summary>Walks the logical tree of a window and registers every marked element (startup pass).</summary>
    internal static void LocalizeTree(DependencyObject root)
    {
        if (GetLocalize(root)) Track(root);
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node) LocalizeTree(node);
    }

    private static bool IsTracked(DependencyObject element)
    {
        foreach (var reference in _live)
            if (reference.TryGetTarget(out var candidate) && ReferenceEquals(candidate, element)) return true;
        return false;
    }

    private static DependencyProperty? DefaultProperty(DependencyObject element) => element switch
    {
        HeaderedContentControl => HeaderedContentControl.HeaderProperty,
        TextBlock => TextBlock.TextProperty,
        ContentControl => ContentControl.ContentProperty,
        Window => Window.TitleProperty,
        FrameworkElement => FrameworkElement.ToolTipProperty,
        _ => null
    };

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
