using System.Windows;
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
        RefreshCameraOverlayStatus(announce: false);
        // The first reading only fills the picker: the overlay opened (or tried to) at start-up with its own list.
        if (first || !string.IsNullOrWhiteSpace(_visualSettings.CameraVideoPath)) return;
        var wanted = _visualSettings.ShowCameraOverlay || _visualSettings.ShowHandTracking || DecideAr().NeedsCamera;
        if (!wanted) return;
        var running = _camera is { IsLive: true };
        if (CameraRanking.ShouldReopen(links, _camera?.Source ?? "", running, _visualSettings.CameraSourceLink))
        {
            StartCameraOverlay();
            RefreshCameraOverlayStatus(announce: false);
        }
    }
}
