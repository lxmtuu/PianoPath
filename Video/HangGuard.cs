using System.Runtime.ExceptionServices;

namespace PianoPath;

/// <summary>
/// Runs one call into native code on a thread of its own, with a limit, so a media stack that never answers
/// costs the take rather than the window.
///
/// <para>
/// Media Foundation is native code, and it does not always fail politely: a verification run on a machine whose
/// media stack wedges inside a sample call showed that the call simply never comes back. Opening the MP4
/// recorder on the interface thread would then freeze everything the window does — the stage, the dock, the
/// clock — with no message and no way out. The work therefore runs beside the caller, and a call that does not
/// come back inside the limit is reported as exactly that: the caller keeps its thread, tells the user, and
/// carries on with a recorder that does not need the media stack.
/// </para>
///
/// <para>
/// The thread that did not come back is left where it is, as a background thread: it holds native state that is
/// not safe to abandon halfway, the process is not waiting on it, and killing a thread inside a native call is
/// the one thing that can make a bad day worse.
/// </para>
/// </summary>
internal static class HangGuard
{
    /// <summary>
    /// Runs <paramref name="work"/> on a thread of its own, with <paramref name="limit"/> to finish.
    /// </summary>
    /// <returns>True when the work finished, however it finished; false when it had not come back in time.</returns>
    /// <remarks>Whatever the work threw is rethrown here, on the caller's thread, with its stack.</remarks>
    internal static bool Run(Action work, TimeSpan limit)
    {
        ExceptionDispatchInfo? failure = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally { done.Set(); }
        }) { IsBackground = true, Name = "guarded-call" };
        thread.Start();
        if (!done.Wait(limit)) return false;
        failure?.Throw();
        return true;
    }
}
