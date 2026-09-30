using System.Windows;
using System.Windows.Controls;

namespace PianoPath;

/// <summary>
/// The GPU graphics engine as the main window sees it: one <see cref="GpuStageFeed"/> the stage writes
/// into, one <see cref="GpuRenderLoop"/> (a render thread with its own Direct3D 11 device) started only
/// while something shows GPU frames, and the optional full-rate <see cref="GpuStageWindow"/>.
/// </summary>
public partial class MainWindow
{
    private readonly GpuStageFeed _gpuFeed = new();
    private GpuRenderLoop? _gpuLoop;
    private GpuStageWindow? _gpuWindow;
    private bool _gpuSessionOverride, _gpuFailureShown, _gpuHooked;
    private TextBlock? _gpuStatusLabel;

    /// <summary><c>--gpu</c>: use the GPU engine for this run without touching the settings file.</summary>
    internal void UseGpuForSession()
    {
        _gpuSessionOverride = true;
        ApplyRenderBackend();
    }

    /// <summary>True while the GPU engine draws the stage inside this window.</summary>
    internal bool GpuStageActive => Stage.UsesGpuFrame && _gpuLoop is { Error: null };
    internal GpuRenderLoop? GpuLoop => _gpuLoop;

    private bool WantsEmbeddedGpu => _gpuSessionOverride || _visualSettings.RenderBackend == "Gpu";

    /// <summary>Starts, reconfigures or stops the GPU engine to match the settings; cheap when nothing changed.</summary>
    private void ApplyRenderBackend()
    {
        if (_closing) return;
        if (!_gpuHooked)
        {
            _gpuHooked = true;
            Stage.GpuPixelSizeChanged += (width, height) => _gpuLoop?.SetEmbedded(Stage.UsesGpuFrame, width, height);
            Stage.GpuStats = () => (_gpuLoop?.Fps ?? 0, _gpuLoop?.ParticleCount ?? 0);
        }
        var embedded = WantsEmbeddedGpu;
        var needed = embedded || _gpuWindow is not null;
        if (needed && _gpuLoop is { Error: not null }) { StopGpuLoop(); needed = _gpuWindow is not null; embedded = false; }
        if (!needed)
        {
            Stage.AttachGpu(null, false);
            StopGpuLoop();
            RefreshGpuStatus();
            return;
        }
        if (_gpuLoop is null)
        {
            _gpuLoop = new GpuRenderLoop(_gpuFeed);
            _gpuLoop.StatusChanged += () => Dispatcher.BeginInvoke(new Action(OnGpuStatusChanged));
        }
        _gpuLoop.TargetFps = _visualSettings.GpuTargetFps;
        _gpuLoop.VSync = _visualSettings.GpuVSync;
        Stage.AttachGpu(_gpuFeed, embedded);
        var (width, height) = Stage.GpuPixelSize;
        _gpuLoop.SetEmbedded(Stage.UsesGpuFrame, width, height);
        RefreshGpuStatus();
    }

    private void StopGpuLoop()
    {
        if (_gpuLoop is null) return;
        var loop = _gpuLoop;
        _gpuLoop = null;
        loop.Dispose();
    }

    /// <summary>The render thread started or failed; a failure falls back to the software stage once, with an explanation.</summary>
    private void OnGpuStatusChanged()
    {
        if (_closing) return;
        if (_gpuLoop is { Error: { } error })
        {
            Stage.AttachGpu(null, false);
            if (!_gpuFailureShown)
            {
                _gpuFailureShown = true;
                ShowMessage(Loc.F("The GPU engine could not start, so the stage stays on the software renderer.\n\n{0}", error), "Graphics engine");
            }
        }
        RefreshGpuStatus();
    }

    private void RefreshGpuStatus()
    {
        if (_gpuStatusLabel is null) return;
        Loc.Bind(_gpuStatusLabel, () =>
        {
            var loop = _gpuLoop;
            if (loop is null) return Loc.T("Engine: software (WPF) · the GPU engine is idle.");
            if (loop.Error is { } error) return Loc.F("Engine: software (WPF) · the GPU engine failed: {0}", error);
            if (!loop.IsReady) return Loc.T("Engine: starting Direct3D 11…");
            var adapter = loop.IsWarp ? Loc.T("software rasterizer (WARP)") : loop.AdapterName;
            return Loc.F("Engine: Direct3D 11 on {0} · shaders compiled in {1} ms", adapter, Math.Round(loop.ShaderCompileMilliseconds));
        });
    }

    private void OpenGpuStage_Click(object sender, RoutedEventArgs e) => OpenGpuStageWindow();

    /// <summary>Opens (or brings forward) the full-rate GPU stage window.</summary>
    internal void OpenGpuStageWindow()
    {
        if (_gpuWindow is not null) { _gpuWindow.Activate(); return; }
        if (_gpuLoop is { Error: not null }) StopGpuLoop();
        if (_gpuLoop is null)
        {
            _gpuLoop = new GpuRenderLoop(_gpuFeed);
            _gpuLoop.StatusChanged += () => Dispatcher.BeginInvoke(new Action(OnGpuStatusChanged));
        }
        _gpuWindow = new GpuStageWindow(_gpuLoop) { Owner = this };
        _gpuWindow.Closed += (_, _) => { _gpuWindow = null; ApplyRenderBackend(); };
        ApplyRenderBackend();
        _gpuWindow.Show();
    }

    private void ShutdownGpuStage()
    {
        _gpuWindow?.Close();
        _gpuWindow = null;
        Stage.AttachGpu(null, false);
        StopGpuLoop();
    }
}
