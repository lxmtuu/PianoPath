using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PianoPath;

/// <summary>
/// The half of <see cref="Loc"/> that paints live labels — everything that needs WPF.
///
/// The string table, the look-up and the diagnostics live in <c>Localization/Loc.cs</c> and are plain
/// computation, so a test project can compile them on any OS. This file joins the same class through
/// <c>partial</c> and implements <see cref="RefreshLiveLabels"/>, which is how a language switch
/// repaints what is already on screen: a build that does not link this file compiles that call away.
///
/// A label is bound once and then follows the language for as long as it lives, held weakly, so
/// switching language never leaks a closed window and never needs the surface to be rebuilt.
/// </summary>
internal static partial class Loc
{
    private static readonly List<WeakReference<DependencyObject>> _live = [];
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Func<string>>> _renderers = new();

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

    /// <summary>
    /// The WPF half of <see cref="Refresh"/>: re-runs every tracked renderer and drops the elements
    /// that have been collected. Declared in <c>Localization/Loc.cs</c>; a build without this file
    /// compiles the call away, so the plain subscribers still run and nothing here is required.
    /// </summary>
    static partial void RefreshLiveLabels()
    {
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            if (!_live[i].TryGetTarget(out var element)) { _live.RemoveAt(i); continue; }
            if (!_renderers.TryGetValue(element, out var table)) { _live.RemoveAt(i); continue; }
            foreach (var (property, render) in table) element.SetValue(property, render());
        }
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
        AutomationProperties.NameProperty,
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
        // A control whose only text is a tooltip is anonymous to a screen reader, so the tooltip is
        // mirrored into AutomationProperties.Name. The name is the same string in the same table, so
        // it follows the interface language with everything else instead of freezing the first one.
        if (table.TryGetValue(FrameworkElement.ToolTipProperty, out var tooltip) && !table.ContainsKey(AutomationProperties.NameProperty))
            table[AutomationProperties.NameProperty] = tooltip;
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

    /// Order matters: a Window *is* a ContentControl and a TabItem *is* a HeaderedContentControl, so the
    /// narrow types have to be matched before the wide ones or their text would land in the wrong property.
    private static DependencyProperty? DefaultProperty(DependencyObject element) => element switch
    {
        Window => Window.TitleProperty,
        TextBlock => TextBlock.TextProperty,
        HeaderedContentControl => HeaderedContentControl.HeaderProperty,
        ContentControl => ContentControl.ContentProperty,
        FrameworkElement => FrameworkElement.ToolTipProperty,
        _ => null
    };
}
