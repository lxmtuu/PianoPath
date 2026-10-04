using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// Owns the non-real-time audio-device control work on a dedicated STA dispatcher. ASIO drivers are COM
/// objects with thread-affinity quirks, while opening/closing a driver can take seconds; keeping both away
/// from the WPF window avoids UI freezes without moving the ASIO object between apartments.
/// </summary>
internal sealed class AudioControlDispatcher : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private int _disposed;

    internal AudioControlDispatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Keyflow audio device control", Priority = ThreadPriority.Normal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void Run()
    {
        var initializeResult = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        }
        finally
        {
            if (initializeResult is 0 or 1) CoUninitialize();
        }
    }

    internal void Post(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try
        {
            dispatcher.BeginInvoke(priority, new Action(() =>
            {
                // Work queued before shutdown is allowed to finish cleanup. Rebuild actions see the owner's
                // disposed flag and return; they must not be abandoned halfway through a driver lifetime.
                try { action(); }
                catch { /* asynchronous device errors are reported by the owner, never on the WPF dispatcher */ }
            }));
        }
        catch (InvalidOperationException) { }
    }

    internal void Invoke(Action action)
    {
        if (Thread.CurrentThread == _thread) { action(); return; }
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AudioControlDispatcher));
        (_dispatcher ?? throw new InvalidOperationException("The audio control thread did not start."))
            .Invoke(action, DispatcherPriority.Send);
    }

    internal T Invoke<T>(Func<T> action)
    {
        if (Thread.CurrentThread == _thread) return action();
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AudioControlDispatcher));
        return (_dispatcher ?? throw new InvalidOperationException("The audio control thread did not start."))
            .Invoke(action, DispatcherPriority.Send);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var dispatcher = _dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            try { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            catch (InvalidOperationException) { }
        }
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
    }

    private const uint CoInitApartmentThreaded = 0x2;
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
