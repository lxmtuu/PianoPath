using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Small HSV and hex color picker used by the live note and halo style controls.</summary>
internal sealed class ColorPickerWindow : Window
{
    private readonly ColorField _field;
    private readonly Slider _hueSlider;
    private readonly TextBox _hexInput;
    private readonly Border _preview;
    private Color _current;
    private double _hue;
    private bool _updating;

    internal string? SelectedHex { get; private set; }
    internal Color CurrentColor => _current;
    internal ColorField ColorSurface => _field;

    internal ColorPickerWindow(string initialColor)
    {
        Title = "Choose a color";
        Width = 390;
        Height = 510;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(18, 16, 24));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var color = ParseColor(initialColor, Color.FromRgb(67, 230, 255));
        (_hue, var saturation, var value) = ToHsv(color);
        _current = color;

        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = MakeButton("Cancel", false); cancel.Click += (_, _) => DialogResult = false;
        var apply = MakeButton("Apply color", true); apply.Margin = new Thickness(9, 0, 0, 0); apply.Click += (_, _) => { SelectedHex = ToHex(_current); DialogResult = true; };
        footer.Children.Add(cancel); footer.Children.Add(apply); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        var title = new TextBlock { Text = "COLOR PICKER", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(231, 218, 244)), Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);

        _field = new ColorField(_hue, saturation, value) { Height = 250, Margin = new Thickness(0, 0, 0, 12) };
        _field.ColorChanged += colorValue => SetCurrent(colorValue, updateField: false);
        DockPanel.SetDock(_field, Dock.Top);
        root.Children.Add(_field);

        var hueHeader = new DockPanel { Margin = new Thickness(0, 1, 0, 4) };
        hueHeader.Children.Add(new TextBlock { Text = "Hue", Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)), FontSize = 10 });
        DockPanel.SetDock(hueHeader.Children[^1], Dock.Left);
        hueHeader.Children.Add(new TextBlock { Text = "Drag the square for saturation and brightness", Foreground = new SolidColorBrush(Color.FromRgb(113, 105, 126)), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right });
        DockPanel.SetDock(hueHeader, Dock.Top); root.Children.Add(hueHeader);

        _hueSlider = new Slider { Minimum = 0, Maximum = 360, Value = _hue, Height = 27, Margin = new Thickness(0, 0, 0, 14), Background = HueBrush() };
        _hueSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _hue = _hueSlider.Value;
            _field.SetHue(_hue);
            SetCurrent(FromHsv(_hue, _field.Saturation, _field.Value), updateField: false);
        };
        DockPanel.SetDock(_hueSlider, Dock.Top); root.Children.Add(_hueSlider);

        var swatchHeader = new TextBlock { Text = "QUICK COLORS", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(156, 146, 168)), Margin = new Thickness(0, 0, 0, 7) };
        DockPanel.SetDock(swatchHeader, Dock.Top); root.Children.Add(swatchHeader);
        var swatches = new UniformGrid { Columns = 8, Rows = 1, Margin = new Thickness(0, 0, 0, 14) };
        Color[] quickColors = [
            Color.FromRgb(255, 73, 111), Color.FromRgb(255, 157, 64), Color.FromRgb(255, 219, 86), Color.FromRgb(91, 232, 149),
            Color.FromRgb(59, 202, 255), Color.FromRgb(89, 126, 255), Color.FromRgb(189, 105, 255), Color.FromRgb(255, 104, 214)
        ];
        foreach (var quickColor in quickColors)
        {
            var button = new Button { Height = 27, Margin = new Thickness(2), Padding = new Thickness(0), Background = new SolidColorBrush(quickColor), BorderBrush = new SolidColorBrush(Color.FromRgb(66, 58, 76)), BorderThickness = new Thickness(1), ToolTip = ToHex(quickColor) };
            button.Click += (_, _) => SetColorFromRgb(quickColor);
            swatches.Children.Add(button);
        }
        DockPanel.SetDock(swatches, Dock.Top); root.Children.Add(swatches);

        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        _preview = new Border { Height = 34, CornerRadius = new CornerRadius(6), BorderBrush = new SolidColorBrush(Color.FromRgb(83, 73, 96)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 9, 0) };
        Grid.SetColumn(_preview, 0); colorRow.Children.Add(_preview);
        _hexInput = new TextBox { Text = ToHex(color), MaxLength = 7, FontSize = 12, CharacterCasing = CharacterCasing.Upper, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(9, 4, 9, 4), Background = new SolidColorBrush(Color.FromRgb(29, 25, 36)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(79, 65, 97)) };
        _hexInput.LostFocus += (_, _) => ApplyHexInput();
        _hexInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyHexInput(); e.Handled = true; } };
        Grid.SetColumn(_hexInput, 1); colorRow.Children.Add(_hexInput);
        var rgb = new TextBlock { Text = "RGB / HEX", Foreground = new SolidColorBrush(Color.FromRgb(130, 120, 144)), FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(rgb, 2); colorRow.Children.Add(rgb);
        DockPanel.SetDock(colorRow, Dock.Top); root.Children.Add(colorRow);

        Content = root;
        RefreshColorDisplay();
    }

    internal static Color FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    internal static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    internal static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    private static Color ParseColor(string value, Color fallback) { try { return (Color)ColorConverter.ConvertFromString(value)!; } catch { return fallback; } }
    private static Button MakeButton(string label, bool primary) => new()
    {
        Content = label, MinWidth = primary ? 112 : 76, Height = 36, Padding = new Thickness(13, 5, 13, 5),
        Foreground = Brushes.White, Background = new SolidColorBrush(primary ? Color.FromRgb(125, 61, 174) : Color.FromRgb(43, 38, 51)),
        BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(184, 119, 229) : Color.FromRgb(80, 71, 91)), BorderThickness = new Thickness(1)
    };

    private static LinearGradientBrush HueBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        for (var i = 0; i <= 6; i++) brush.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6d));
        return brush;
    }

    private void SetColorFromRgb(Color color)
    {
        var (h, s, v) = ToHsv(color); _hue = h; _updating = true; _hueSlider.Value = h; _updating = false;
        _field.SetSelection(h, s, v); SetCurrent(color, updateField: false);
    }

    private void ApplyHexInput()
    {
        var color = ParseColor(_hexInput.Text, Color.FromArgb(0, 0, 0, 0));
        if (color.A == 0) { _hexInput.Text = ToHex(_current); return; }
        SetColorFromRgb(color);
    }

    private void SetCurrent(Color color, bool updateField)
    {
        _current = Color.FromRgb(color.R, color.G, color.B);
        if (updateField)
        {
            var (h, s, v) = ToHsv(color); _hue = h; _updating = true; _hueSlider.Value = h; _updating = false;
            _field.SetSelection(h, s, v);
        }
        RefreshColorDisplay();
    }

    private void RefreshColorDisplay()
    {
        _preview.Background = new SolidColorBrush(_current);
        if (!_hexInput.IsKeyboardFocusWithin) _hexInput.Text = ToHex(_current);
    }

    internal sealed class ColorField : FrameworkElement
    {
        private double _hue;
        private bool _dragging;
        internal double Saturation { get; private set; }
        internal double Value { get; private set; }
        internal event Action<Color>? ColorChanged;

        internal ColorField(double hue, double saturation, double value)
        {
            Focusable = true; ClipToBounds = true; SetSelection(hue, saturation, value);
            MouseLeftButtonDown += (_, e) => { _dragging = true; CaptureMouse(); UpdateFromPoint(e.GetPosition(this)); e.Handled = true; };
            MouseMove += (_, e) => { if (_dragging && e.LeftButton == MouseButtonState.Pressed) UpdateFromPoint(e.GetPosition(this)); };
            MouseLeftButtonUp += (_, e) => { if (!_dragging) return; UpdateFromPoint(e.GetPosition(this)); _dragging = false; ReleaseMouseCapture(); e.Handled = true; };
        }

        internal void SetHue(double hue) { _hue = hue; InvalidateVisual(); }
        internal void SetSelection(double hue, double saturation, double value)
        {
            _hue = hue; Saturation = Math.Clamp(saturation, 0, 1); Value = Math.Clamp(value, 0, 1); InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(new SolidColorBrush(FromHsv(_hue, 1, 1)), null, rect);
            var saturation = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0);
            dc.DrawRectangle(saturation, null, rect);
            var brightness = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brightness.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            brightness.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
            dc.DrawRectangle(brightness, null, rect);
            var marker = new Point(Saturation * ActualWidth, (1 - Value) * ActualHeight);
            dc.DrawEllipse(null, new Pen(Brushes.Black, 4), marker, 7, 7);
            dc.DrawEllipse(null, new Pen(Brushes.White, 2), marker, 7, 7);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(83, 74, 96)), 1), new Rect(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
        }

        private void UpdateFromPoint(Point point)
        {
            if (ActualWidth < 1 || ActualHeight < 1) return;
            Saturation = Math.Clamp(point.X / ActualWidth, 0, 1); Value = 1 - Math.Clamp(point.Y / ActualHeight, 0, 1);
            InvalidateVisual(); ColorChanged?.Invoke(FromHsv(_hue, Saturation, Value));
        }
    }
}
