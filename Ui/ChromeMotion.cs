using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PianoPath;

/// <summary>
/// Small, dependency-free entrance and hover choreography for the chrome.
///
/// WPF's own <c>Storyboard</c> markup would need a trigger per state and a target name per element;
/// these helpers keep the window code readable and share one set of easing curves, so every panel
/// moves with the same rhythm. All animations use <see cref="FillBehavior.Stop"/> over an already-set
/// base value, which means the final state is always the plain property value — a cancelled or
/// interrupted animation can therefore never leave the interface stuck half-transparent.
/// </summary>
internal static class ChromeMotion
{
    private static readonly IEasingFunction Enter = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction Exit = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
    private static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .35 });

    private static bool _disabled;

    /// <summary>Honours the "Off" motion level, the OS animation switch and automated snapshot runs.</summary>
    internal static bool Enabled
    {
        get => !_disabled && SystemParameters.ClientAreaAnimation;
        set => _disabled = !value;
    }

    internal static void FadeIn(UIElement element, double duration = 240, double delay = 0, double from = 0)
    {
        if (!Enabled) { element.Opacity = 1; return; }
        element.Opacity = 1;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, 1, new Duration(Ms(duration))) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = Enter, FillBehavior = FillBehavior.Stop });
    }

    internal static void FadeOut(UIElement element, double duration = 160, Action? onCompleted = null)
    {
        if (!Enabled) { element.Opacity = 0; onCompleted?.Invoke(); return; }
        var animation = new DoubleAnimation(element.Opacity, 0, new Duration(Ms(duration))) { EasingFunction = Exit, FillBehavior = FillBehavior.Stop };
        if (onCompleted is not null) animation.Completed += (_, _) => onCompleted();
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>Slides an element in from <paramref name="offsetX"/>/<paramref name="offsetY"/> pixels while fading it in.</summary>
    internal static void SlideIn(FrameworkElement element, double offsetX, double offsetY, double duration = 280, double delay = 0)
    {
        if (!Enabled) { element.Opacity = 1; SetOffset(element, 0, 0); return; }
        element.Opacity = 1;
        SetOffset(element, 0, 0);
        var x = new DoubleAnimation(offsetX, 0, new Duration(Ms(duration))) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = Spring, FillBehavior = FillBehavior.Stop };
        var y = new DoubleAnimation(offsetY, 0, new Duration(Ms(duration))) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = Spring, FillBehavior = FillBehavior.Stop };
        Translate(element).BeginAnimation(TranslateTransform.XProperty, x);
        Translate(element).BeginAnimation(TranslateTransform.YProperty, y);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(Ms(duration * .6))) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = Enter, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Scales an element up from slightly smaller — used for dialogs so they feel like they open.</summary>
    internal static void PopIn(FrameworkElement element, double from = .94, double duration = 260)
    {
        if (!Enabled) { element.Opacity = 1; return; }
        element.Opacity = 1;
        var scale = Scale(element);
        var animation = new DoubleAnimation(from, 1, new Duration(Ms(duration))) { EasingFunction = Spring, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(Ms(duration * .7))) { EasingFunction = Enter, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Fades and slides a list of elements in sequence; the stagger is what reads as motion rather than a pop.</summary>
    internal static void Cascade(IEnumerable<FrameworkElement> elements, double stagger = 55, double offsetY = 16)
    {
        var index = 0;
        foreach (var element in elements) { SlideIn(element, 0, offsetY, 340, index * stagger); index++; }
    }

    /// <summary>A soft accent pulse, used when a hotspot (the play button, a hit) wants attention without a jump.</summary>
    internal static void Pulse(UIElement element, double strength = .12, double duration = 220)
    {
        if (!Enabled) return;
        var scale = Scale(element);
        var up = new DoubleAnimation(1, 1 + strength, new Duration(Ms(duration))) { EasingFunction = Enter, AutoReverse = true, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }

    /// <summary>Drives a repeating light sweep across a gradient brush (header and footer hairlines).</summary>
    internal static void StartSweep(LinearGradientBrush brush, double period = 3.6)
    {
        if (!Enabled) return;
        var start = new PointAnimation(new Point(-.4, 0), new Point(.6, 0), new Duration(TimeSpan.FromSeconds(period))) { RepeatBehavior = RepeatBehavior.Forever };
        var end = new PointAnimation(new Point(0, 0), new Point(1, 0), new Duration(TimeSpan.FromSeconds(period))) { RepeatBehavior = RepeatBehavior.Forever };
        brush.BeginAnimation(LinearGradientBrush.StartPointProperty, start);
        brush.BeginAnimation(LinearGradientBrush.EndPointProperty, end);
    }

    private static TranslateTransform Translate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform translate) return translate;
        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private static ScaleTransform Scale(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform scale) return scale;
        scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;
        element.RenderTransformOrigin = new Point(.5, .5);
        return scale;
    }

    private static void SetOffset(FrameworkElement element, double x, double y)
    {
        var translate = Translate(element);
        translate.X = x; translate.Y = y;
    }

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));

    private static T Freeze<T>(T value) where T : Freezable { if (value.CanFreeze) value.Freeze(); return value; }
}
