using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Draws the camera picture and the four corners the person is marking on it, and passes the mouse and keys to the
/// <see cref="CalibrationWizard"/>, which decides everything. Nothing here is a rule: it paints what the wizard reports (the picture
/// in its box, turned round when the stage turns it round, the outline, the grid of keys the corners would give, a magnifier on the
/// corner being moved) and forwards input in its own coordinates.
/// </summary>
internal sealed class CalibrationSurface : FrameworkElement
{
    private const double MagnifierBox = 168;
    private static readonly Brush Backdrop = Freeze(new SolidColorBrush(Color.FromRgb(8, 7, 12)));
    private static readonly Brush WhiteKeyFill = Freeze(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)));
    private static readonly Brush BlackKeyFill = Freeze(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
    private static readonly Pen WhiteKeyPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), 0.8));
    private static readonly Pen BlackKeyPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(220, 67, 230, 255)), 0.8));
    private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(67, 230, 255)));
    private static readonly Brush ProblemBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 83, 113)));
    private static readonly Pen Halo = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 4.2));

    private readonly bool _mirror;
    private BitmapSource _bitmap;

    internal CalibrationSurface(CalibrationPicture picture, CalibrationWizard wizard, bool mirror)
    {
        Wizard = wizard; _mirror = mirror; _bitmap = ToBitmap(picture); Picture = picture;
        Focusable = true; Cursor = Cursors.Cross; ClipToBounds = true;
    }

    internal CalibrationWizard Wizard { get; }

    /// <summary>The picture on show, as the camera delivered it.</summary>
    internal CalibrationPicture Picture { get; private set; }

    /// <summary>Raised after anything that changed what the wizard says, so the window can refresh its text and buttons.</summary>
    internal event Action? Changed;

    /// <summary>Shows another picture of the same size, keeping the corners placed so far.</summary>
    internal void SetPicture(CalibrationPicture picture) { _bitmap = ToBitmap(picture); Picture = picture; InvalidateVisual(); }

    private static BitmapSource ToBitmap(CalibrationPicture picture)
    {
        var bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgr32, null, picture.Bgra, picture.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
    private static Pen FreezePen(Pen pen) { pen.Freeze(); return pen; }

    // ---- input ------------------------------------------------------------------------------------------

    internal bool HandlePress(Point position)
    {
        var taken = Wizard.PointerDown(position.X, position.Y);
        Refresh();
        return taken;
    }

    internal void HandleMove(Point position)
    {
        if (Wizard.PointerMove(position.X, position.Y)) Refresh();
    }

    internal void HandleRelease() { Wizard.PointerUp(); Refresh(); }

    /// <summary>The keys of the window: arrows nudge, Tab changes corner, Backspace takes one back, Delete starts over. True when the key was used.</summary>
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return false;
        var large = (modifiers & ModifierKeys.Shift) != 0;
        switch (key)
        {
            case Key.Left: Wizard.Nudge(-1, 0, large); break;
            case Key.Right: Wizard.Nudge(1, 0, large); break;
            case Key.Up: Wizard.Nudge(0, -1, large); break;
            case Key.Down: Wizard.Nudge(0, 1, large); break;
            case Key.Tab when Wizard.Points.Count > 0: Wizard.SelectNext(backwards: large); break;
            case Key.Back: Wizard.Undo(); break;
            case Key.Delete: Wizard.Clear(); break;
            default: return false;
        }
        Refresh();
        return true;
    }

    internal void Refresh() { Changed?.Invoke(); InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (HandlePress(e.GetPosition(this))) CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) HandleMove(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        HandleRelease();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => HandleRelease();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Wizard.SetViewport(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height, _mirror);
        Changed?.Invoke();
    }

    // ---- drawing ----------------------------------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var view = Wizard.View;
        if (!view.Valid) return;
        DrawPicture(dc, new Rect(view.Left, view.Top, view.Width, view.Height), _mirror, nearest: false);
        DrawKeys(dc);
        DrawOutline(dc);
        DrawMagnifier(dc);
    }

    /// <summary>The picture in a rectangle, turned round about the rectangle's middle when the stage turns it round.</summary>
    private void DrawPicture(DrawingContext dc, Rect rect, bool mirror, bool nearest, double? flipAbout = null)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, nearest ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        using (var inner = group.Open()) inner.DrawImage(_bitmap, rect);
        if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, flipAbout ?? rect.X + rect.Width / 2, 0));
        dc.DrawDrawing(group);
        if (mirror) dc.Pop();
    }

    private void DrawKeys(DrawingContext dc)
    {
        var keys = Wizard.PreviewKeys();
        if (keys.Count == 0) return;
        var whites = new StreamGeometry(); var blacks = new StreamGeometry();
        using (var white = whites.Open())
        using (var black = blacks.Open())
            foreach (var key in keys)
            {
                var context = key.Black ? black : white;
                context.BeginFigure(new Point(key.Corners[0].X, key.Corners[0].Y), true, true);
                context.PolyLineTo([new Point(key.Corners[1].X, key.Corners[1].Y), new Point(key.Corners[2].X, key.Corners[2].Y), new Point(key.Corners[3].X, key.Corners[3].Y)], true, false);
            }
        whites.Freeze(); blacks.Freeze();
        dc.DrawGeometry(WhiteKeyFill, WhiteKeyPen, whites);
        dc.DrawGeometry(BlackKeyFill, BlackKeyPen, blacks);
        // The lowest and the highest key are named at the near edge, so a grid that runs the wrong way round shows itself.
        var ends = keys.Where(key => !key.Black).ToList();
        if (ends.Count > 0) { DrawKeyName(dc, ends[0]); DrawKeyName(dc, ends[^1]); }
    }

    private void DrawKeyName(DrawingContext dc, WizardKey key)
    {
        var text = Text(CalibrationHints.KeyName(key.Pitch), 12, Accent);
        var x = Math.Clamp((key.Corners[2].X + key.Corners[3].X) / 2 - text.Width / 2, 2, Math.Max(2, ActualWidth - text.Width - 2));
        // outside the near edge: below it when the keys' near edge is the lower one, above it when the picture shows them the other way up
        var nearIsLower = key.Corners[2].Y + key.Corners[3].Y >= key.Corners[0].Y + key.Corners[1].Y;
        var y = Math.Clamp(nearIsLower ? Math.Max(key.Corners[2].Y, key.Corners[3].Y) + 4 : Math.Min(key.Corners[2].Y, key.Corners[3].Y) - text.Height - 4, 2, Math.Max(2, ActualHeight - text.Height - 2));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), null, new Rect(x - 3, y - 1, text.Width + 6, text.Height + 2));
        dc.DrawText(text, new Point(x, y));
    }

    private void DrawOutline(DrawingContext dc)
    {
        var count = Wizard.Points.Count;
        if (count == 0) return;
        var brush = Wizard.Step == WizardStep.Fix ? ProblemBrush : Accent;
        var pen = new Pen(brush, 1.6);
        var points = Enumerable.Range(0, count).Select(i => Wizard.PointOnScreen(i)).Select(p => new Point(p.X, p.Y)).ToArray();
        for (var i = 1; i < count; i++) { dc.DrawLine(Halo, points[i - 1], points[i]); dc.DrawLine(pen, points[i - 1], points[i]); }
        if (count == 4) { dc.DrawLine(Halo, points[3], points[0]); dc.DrawLine(pen, points[3], points[0]); }
        for (var i = 0; i < count; i++)
        {
            var selected = i == Wizard.Selected;
            dc.DrawEllipse(selected ? brush : null, Halo, points[i], selected ? 6 : 5, selected ? 6 : 5);
            dc.DrawEllipse(selected ? brush : null, pen, points[i], selected ? 6 : 5, selected ? 6 : 5);
            var label = Text((i + 1).ToString(CultureInfo.InvariantCulture), 12, Brushes.White);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), null, new Rect(points[i].X + 8, points[i].Y - 21, label.Width + 6, label.Height + 2));
            dc.DrawText(label, new Point(points[i].X + 11, points[i].Y - 20));
        }
    }

    /// <summary>A zoomed view of the picture around the corner being moved, in the top corner that is farthest from it.</summary>
    private void DrawMagnifier(DrawingContext dc)
    {
        if (Wizard.Selected < 0 || Wizard.Selected >= Wizard.Points.Count) return;
        var source = Wizard.MagnifierSource(Wizard.Selected);
        var corner = Wizard.PointOnScreen(Wizard.Selected);
        var leftBox = new Rect(12, 12, MagnifierBox, MagnifierBox);
        var box = leftBox;
        if (corner.X < leftBox.Right + 48 && corner.Y < leftBox.Bottom + 48) box = new Rect(Math.Max(12, ActualWidth - MagnifierBox - 12), 12, MagnifierBox, MagnifierBox);
        var zoom = MagnifierBox / source.Size;
        dc.DrawRectangle(Backdrop, null, box);
        dc.PushClip(new RectangleGeometry(box));
        DrawPicture(dc, new Rect(box.X - source.X * zoom, box.Y - source.Y * zoom, Wizard.ImageWidth * zoom, Wizard.ImageHeight * zoom), _mirror, nearest: true, flipAbout: box.X + box.Width / 2);
        var middle = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        foreach (var pen in new[] { Halo, new Pen(Brushes.White, 1) })
        {
            dc.DrawLine(pen, new Point(middle.X - 22, middle.Y), new Point(middle.X - 6, middle.Y));
            dc.DrawLine(pen, new Point(middle.X + 6, middle.Y), new Point(middle.X + 22, middle.Y));
            dc.DrawLine(pen, new Point(middle.X, middle.Y - 22), new Point(middle.X, middle.Y - 6));
            dc.DrawLine(pen, new Point(middle.X, middle.Y + 6), new Point(middle.X, middle.Y + 22));
        }
        dc.Pop();
        dc.DrawRectangle(null, new Pen(Accent, 1.5), box);
    }

    private FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

}

/// <summary>
/// The window in which the person marks the four corners of the keys on a still picture from the camera: the picture, a line that
/// says what to do next, and the buttons. Returns the calibration when Done is pressed; the caller saves it.
/// </summary>
internal sealed class CalibrationWindow : Window
{
    private readonly CalibrationSurface _surface;
    private readonly TextBlock _hint, _note;
    private readonly Button _done, _undo, _startOver;
    private readonly ComboBox _orientation;

    private sealed record OrientationChoice(KeyboardOrientation Value, string Caption);
    private readonly Func<CalibrationPicture?>? _retake;
    private static readonly Brush NormalText = new SolidColorBrush(Color.FromRgb(231, 218, 244)), FixText = new SolidColorBrush(Color.FromRgb(255, 140, 160));

    /// <param name="picture">A still of the camera, as it delivers it.</param>
    /// <param name="layout">The keyboard being marked.</param>
    /// <param name="mirror">The stage shows the picture turned round, so the window does too.</param>
    /// <param name="retake">Gives a newer picture of the camera, or null when it has none.</param>
    /// <param name="orientation">How the keyboard looks in the picture to start with; when not given, as upright in the picture the camera delivers.</param>
    internal CalibrationWindow(CalibrationPicture picture, KeyboardLayout layout, bool mirror, Func<CalibrationPicture?>? retake = null, KeyboardOrientation? orientation = null)
    {
        _retake = retake;
        Title = Loc.T("Mark the keys");
        Width = Math.Min(1120, Math.Max(640, SystemParameters.WorkArea.Width - 60));
        Height = Math.Min(780, Math.Max(480, SystemParameters.WorkArea.Height - 60));
        MinWidth = 640; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; UseLayoutRounding = true;
        Background = new SolidColorBrush(Color.FromRgb(18, 16, 24)); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");

        var wizard = new CalibrationWizard(picture.Width, picture.Height, layout);
        wizard.SetViewport(0, 0, mirror);                        // the picture is shown turned round or not from the start, whatever the orientation means for it
        var start = orientation ?? KeyboardOrientations.Default(mirror);
        wizard.SetOrientation(start);
        _surface = new CalibrationSurface(picture, wizard, mirror);
        _hint = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, MinHeight = 38, Margin = new Thickness(16, 12, 16, 8), Foreground = new SolidColorBrush(Color.FromRgb(231, 218, 244)) };
        _note = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 120)), Margin = new Thickness(0, 2, 0, 0) };

        var info = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)) };
        info.Text = Loc.F("{0}-key keyboard · picture {1} × {2}", layout.KeyCount, picture.Width, picture.Height);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(info); left.Children.Add(_note);

        var newPicture = MakeButton(Loc.T("New picture"), false); newPicture.Click += (_, _) => Retake();
        _startOver = MakeButton(Loc.T("Start over"), false); _startOver.Click += (_, _) => { _surface.Wizard.Clear(); _surface.Refresh(); };
        _undo = MakeButton(Loc.T("Undo"), false); _undo.Click += (_, _) => { _surface.Wizard.Undo(); _surface.Refresh(); };
        var cancel = MakeButton(Loc.T("Cancel"), false); cancel.IsCancel = true;
        _done = MakeButton(Loc.T("Done"), true); _done.IsDefault = true; _done.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { newPicture, _startOver, _undo, cancel, _done }) { button.Margin = new Thickness(8, 0, 0, 0); buttons.Children.Add(button); }

        var bar = new DockPanel { Margin = new Thickness(16, 10, 16, 14), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons); bar.Children.Add(left);

        _orientation = new ComboBox { Width = 330, Height = 28, DisplayMemberPath = nameof(OrientationChoice.Caption), SelectedValuePath = nameof(OrientationChoice.Value), ToolTip = Loc.T(CalibrationHints.OrientationTip) };
        foreach (var choice in Enum.GetValues<KeyboardOrientation>()) _orientation.Items.Add(new OrientationChoice(choice, Loc.T(CalibrationHints.ForOrientation(choice))));
        _orientation.SelectedValue = start;
        _orientation.SelectionChanged += (_, _) =>
        {
            if (_orientation.SelectedValue is KeyboardOrientation chosen) { Wizard.SetOrientation(chosen); _surface.Refresh(); }
        };
        _orientation.DropDownClosed += (_, _) => _surface.Focus();          // the arrow keys and Tab are for the corners again as soon as the choice is made
        var orientationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        orientationRow.Children.Add(new TextBlock { Text = Loc.T(CalibrationHints.OrientationTitle), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)) });
        orientationRow.Children.Add(_orientation);

        var root = new DockPanel();
        DockPanel.SetDock(_hint, Dock.Top); root.Children.Add(_hint);
        DockPanel.SetDock(orientationRow, Dock.Top); root.Children.Add(orientationRow);
        DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        root.Children.Add(new Border { Margin = new Thickness(16, 0, 16, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(79, 65, 97)), BorderThickness = new Thickness(1), Child = _surface });
        Content = root;

        _surface.Changed += RefreshText;
        RefreshText();
        Loaded += (_, _) => _surface.Focus();
    }

    internal CalibrationSurface Surface => _surface;
    internal CalibrationWizard Wizard => _surface.Wizard;
    internal Button DoneButton => _done;

    /// <summary>The choice of how the keyboard looks in the picture.</summary>
    internal ComboBox OrientationBox => _orientation;

    /// <summary>The calibration the person marked; null until Done is pressed.</summary>
    internal ArCalibration? Result { get; private set; }

    /// <summary>What the window says under the picture about the last New picture, or empty.</summary>
    internal string NoteText => _note.Text;

    /// <summary>The line above the picture as it reads now.</summary>
    internal string HintText => _hint.Text;

    private void RefreshText()
    {
        var text = Loc.T(CalibrationHints.ForStep(Wizard));
        if (_hint.Text != text) _hint.Text = text;
        _hint.Foreground = Wizard.Step == WizardStep.Fix ? FixText : NormalText;
        _done.IsEnabled = Wizard.CanFinish;
        _undo.IsEnabled = Wizard.Points.Count > 0 && !Wizard.IsDragging;
        _startOver.IsEnabled = Wizard.Points.Count > 0;
        if (_note.Text.Length > 0 && Wizard.IsDragging) _note.Text = "";
    }

    /// <summary>New picture: a fresher still of the camera, keeping the corners placed so far.</summary>
    internal void Retake()
    {
        var picture = _retake?.Invoke();
        if (picture is null) { _note.Text = Loc.T("The camera has not delivered a picture yet. Try again in a moment."); return; }
        if (picture.Width != Wizard.ImageWidth || picture.Height != Wizard.ImageHeight)
        {
            _note.Text = Loc.T("The new picture has a different size. Close this window and mark the keys again.");
            return;
        }
        _note.Text = "";
        _surface.SetPicture(picture);
    }

    /// <summary>Done: takes the calibration out of the wizard and closes. Does nothing while the corners cannot be used.</summary>
    internal void Accept()
    {
        if (Result is not null || !Wizard.CanFinish) return;
        var result = Wizard.Finish(out _);
        if (result is null) return;
        Result = result;
        try { DialogResult = true; }
        catch (InvalidOperationException) { Close(); }      // not shown as a dialog (a check drives the window without one)
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Accept(); e.Handled = true; return; }
        if (_surface.HandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text, MinWidth = 92, Height = 30, Padding = new Thickness(12, 0, 12, 0), Foreground = Brushes.White,
        Background = new SolidColorBrush(primary ? Color.FromRgb(125, 61, 174) : Color.FromRgb(43, 38, 51)),
        BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(184, 119, 229) : Color.FromRgb(80, 71, 91)), BorderThickness = new Thickness(1),
    };
}
