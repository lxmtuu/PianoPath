namespace PianoPath;

/// <summary>
/// The effects over a camera picture that the AR overlay does not draw. Two reasons, both checked
/// against the renderer rather than assumed:
///
/// <list type="bullet">
/// <item>The backdrop star field is part of the stage background pass (<c>GpuStageRenderer.cs:588</c>
/// draws it only when <c>!cameraActive</c>, and <c>SceneA.x</c> is the star density that pass reads), so
/// it never reaches the camera picture. The other ambient families are a separate screen-space pass and
/// do.</item>
/// <item>The flat (legacy) overlay draws note bodies only. The falling trail, the echo ghosts, the landing
/// glow and the note names are built in the projected path
/// (<c>GpuArLayer.Effects.cs:BuildProjectedSongNoteEffects</c>), which needs the saved 3D keyboard pose.</item>
/// </list>
///
/// The shimmer and the pulse are deliberately <em>not</em> in this list: <c>AddSongNote</c> applies both to
/// the AR's own note quads on the flat and the projected path alike (<c>GpuArLayer.cs:327</c>,
/// <c>:336</c>). An older contract comment claimed all three falling families were unsupported in AR;
/// that was true when the overlay was flat-only.
///
/// The list is a pure function of switches so the Linux test project can pin it, and so the AR card and
/// the tooltip cannot disagree with the renderer.
/// </summary>
internal static partial class ArOmittedEffects
{
    /// <summary>
    /// The localization keys of what the AR overlay leaves out of the current look, in interface order.
    /// Empty means nothing visible is lost. Switches arrive one by one so this half stays free of the
    /// WPF settings type.
    /// </summary>
    internal static IReadOnlyList<string> Omitted(bool projected, bool trail, bool ghosts, bool landingGlow,
        bool labels, bool stars)
    {
        var missing = new List<string>(5);
        if (!projected)
        {
            if (trail) missing.Add("Falling trail");
            if (ghosts) missing.Add("Echo ghosts");
            if (landingGlow) missing.Add("Landing glow");
            if (labels) missing.Add("Note names");
        }
        // Last because it is the background, not the note path: it is missing whatever the note path does.
        if (stars) missing.Add("Backdrop stars");
        return missing;
    }
}
