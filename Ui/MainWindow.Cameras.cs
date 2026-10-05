using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// Finding the cameras on its own: the machine's list is read when the window opens and again each time Windows says a device
/// came or went, so a webcam plugged in after the app started shows up in the picker (and, when the overlay is on and has no
/// camera, opens) without anyone pressing a button. The rule for what a change calls for lives in
/// <see cref="CameraRanking.ShouldReopen"/>, where a test can reach it.
/// </summary>
public partial class MainWindow
{
    /// <summary>WM_DEVICECHANGE: sent to every top-level window when a device arrives or leaves (DBT_DEVNODES_CHANGED needs no registration).</summary>
    private const int WmDeviceChange = 0x0219;
    /// <summary>A plug-in produces a burst of these messages while the driver loads; the list is read once, after the burst.</summary>
    private static readonly TimeSpan CameraScanDelay = TimeSpan.FromMilliseconds(900);

    private DispatcherTimer? _cameraScanTimer;
    private bool _cameraScanning;
    private bool _cameraScanAgain;
    private string? _cameraListSignature;

    /// <summary>Starts listening for device changes and reads the first list. Called once the window has a handle.</summary>
    private void HookCameraChanges()
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(CameraDeviceHook);
        ScheduleCameraScan(TimeSpan.FromMilliseconds(250));
    }

    private IntPtr CameraDeviceHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmDeviceChange) ScheduleCameraScan(CameraScanDelay);
        return IntPtr.Zero;
    }

    /// <summary>Reads the camera list after <paramref name="delay"/>; another request inside that time moves the reading later.</summary>
    private void ScheduleCameraScan(TimeSpan delay)
    {
        if (_cameraScanTimer is null)
        {
            _cameraScanTimer = new DispatcherTimer();
            _cameraScanTimer.Tick += (_, _) => { _cameraScanTimer!.Stop(); StartCameraScan(); };
        }
        _cameraScanTimer.Stop();
        _cameraScanTimer.Interval = delay;
        _cameraScanTimer.Start();
    }

    /// <summary>Lists the cameras on a thread of its own — asking a driver can take a second — and hands the answer back to this one.</summary>
    private void StartCameraScan()
    {
        if (_cameraScanning) { _cameraScanAgain = true; return; }
        _cameraScanning = true;
        var thread = new Thread(() =>
        {
            IReadOnlyList<CameraInfo> list = [];
            string? error = null;
            try { list = CameraFrameReader.Devices(out error); }
            catch (Exception ex) { error = ex.Message; }
            Dispatcher.BeginInvoke(new Action(() => ApplyCameraScan(list, error)));
        }) { IsBackground = true, Name = "Keyflow camera scan" };
        thread.Start();
    }

    /// <summary>Takes a new camera list: refreshes the picker and the status line, and reopens the overlay when the change calls for it.</summary>
    private void ApplyCameraScan(IReadOnlyList<CameraInfo> list, string? error)
    {
        _cameraScanning = false;
        if (_cameraScanAgain) { _cameraScanAgain = false; ScheduleCameraScan(TimeSpan.FromMilliseconds(250)); }
        var links = list.Where(device => device.Link.Length > 0).Select(device => device.Link).ToList();
        var signature = CameraRanking.Signature(links);
        var first = _cameraListSignature is null;
        if (!first && signature == _cameraListSignature && error == _cameraListError) return;
        _cameraListSignature = signature;
        _cameraDevices = list;
        _cameraListError = error;
        _visualChoiceOptions[nameof(PianoVisualSettings.CameraSourceLink)] = CameraChoiceOptions(list);
        RefreshChoiceCaptions();
        RebuildCameraList();
        RefreshCameraOverlayStatus(announce: false);
        // The first reading only fills the picker: the overlay opened (or tried to) at start-up with its own list.
        if (first || !string.IsNullOrWhiteSpace(_visualSettings.CameraVideoPath)) return;
        var wanted = _visualSettings.ShowCameraOverlay || _visualSettings.ShowHandTracking || DecideAr().NeedsCamera;
        if (!wanted) return;
        var running = _camera is { IsLive: true };
        if (CameraRanking.ShouldReopen(links, _camera?.Source ?? "", running, _visualSettings.CameraSourceLink))
        {
            StartCameraOverlay();
            RebuildCameraList();
            RefreshCameraOverlayStatus(announce: false);
        }
    }

    // =====================================================================================================
    // The list of cameras on the settings page: every camera by its own name, one click to use it
    // =====================================================================================================

    private ListBox? _cameraList;
    private bool _cameraListLoading;

    /// <summary>Builds the list inside its card. The lines come from <see cref="RebuildCameraList"/>.</summary>
    private void BuildCameraList(Panel body)
    {
        _cameraList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_cameraList, ScrollBarVisibility.Disabled);
        // The list never scrolls by itself, so the wheel belongs to the page around it.
        _cameraList.PreviewMouseWheel += (sender, e) =>
        {
            if (e.Handled || _cameraList.Parent is not UIElement parent) return;
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
        };
        _cameraList.SelectionChanged += CameraList_SelectionChanged;
        Loc.Set(_cameraList, "CAMERAS ON THIS MACHINE", AutomationProperties.NameProperty);
        Register(body, _cameraList, null, "cameras on this machine list detected webcam name devices choose select camera");
        Loc.OnChanged(RebuildCameraList);
        RebuildCameraList();
    }

    /// <summary>Repaints the list from the machine's cameras, the one that is open and the one that was picked.</summary>
    private void RebuildCameraList()
    {
        if (_cameraList is null) return;
        var listed = _cameraDevices.Where(device => device.Link.Length > 0).ToList();
        var running = _camera is { IsLive: true } live ? live.Source : "";
        var rows = CameraListing.Build(listed.Select(device => device.Name).ToList(), listed.Select(device => device.Link).ToList(), running, _visualSettings.CameraSourceLink);
        _cameraListLoading = true;
        try
        {
            _cameraList.Items.Clear();
            foreach (var row in rows) _cameraList.Items.Add(new ListBoxItem { Content = CameraRowContent(row), Tag = row.Link, Padding = new Thickness(8, 6, 8, 6) });
            _cameraList.SelectedIndex = rows.ToList().FindIndex(row => row.Selected);
        }
        finally { _cameraListLoading = false; }
    }

    /// <summary>One line: a dot, the camera's name with a line about it underneath, and a small tag on the right.</summary>
    private FrameworkElement CameraRowContent(CameraRow row)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), FontSize = 10, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        if (row.IsAutomatic)
        {
            Loc.Set(name, "Automatic");
            if (row.Picks is { } picks) Loc.Bind(detail, () => Loc.F("Picks {0}", picks));
            else Loc.Set(detail, "No camera found yet");
        }
        else
        {
            name.Text = row.Name;                       // the camera's own name, exactly as Windows reports it
            Loc.Set(detail, row.Kind switch
            {
                CameraKind.Infrared => "Infrared camera: made for face login, not for a picture of the keys",
                CameraKind.Virtual => "Virtual camera: shows another program's picture",
                _ => "Colour camera",
            });
        }
        name.ToolTip = row.IsAutomatic ? null : row.Name;
        AutomationProperties.SetName(grid, row.IsAutomatic ? Loc.T("Automatic") : row.Name);
        text.Children.Add(name); text.Children.Add(detail);
        grid.Children.Add(text);
        var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        if (!row.IsAutomatic && row.Kind != CameraKind.Colour) tags.Children.Add(CameraTag(row.Kind == CameraKind.Infrared ? "INFRARED" : "VIRTUAL", "ControlHoverBrush"));
        if (row.InUse) tags.Children.Add(CameraTag("IN USE", "AccentSoftBrush"));
        Grid.SetColumn(tags, 1);
        grid.Children.Add(tags);
        return grid;
    }

    private Border CameraTag(string key, string background)
    {
        var label = new TextBlock { FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") };
        Loc.Set(label, key);
        return new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(6, 0, 0, 0), Background = (Brush)FindResource(background), Child = label };
    }

    /// <summary>A click on a line makes that camera the one the overlay opens, and a live camera replaces a video file as the source.</summary>
    private void CameraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cameraListLoading || _cameraList?.SelectedItem is not ListBoxItem { Tag: string link }) return;
        var name = _cameraDevices.FirstOrDefault(device => device.Link.Length > 0 && string.Equals(device.Link, link, StringComparison.OrdinalIgnoreCase))?.Name;
        _visualSettings.CameraSourceLink = link;
        _visualSettings.CameraVideoPath = "";
        MarkModified(nameof(PianoVisualSettings.CameraSourceLink));
        MarkModified(nameof(PianoVisualSettings.CameraVideoPath));
        RefreshSettingControls();                      // the picker in the overlay card follows
        ApplyVisualSettings("Camera chosen: {0}", false, name ?? Loc.T("Automatic"));
        RefreshCameraOverlayStatus(announce: true);
        Dispatcher.BeginInvoke(new Action(RebuildCameraList), DispatcherPriority.Background);   // "in use" moves to the camera that opened
    }
}
