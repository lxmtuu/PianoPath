using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
// DXGI declares its own MapFlags (IDXGISurface::Map); the stage maps D3D11 resources.
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace PianoPath;

/// <summary>
/// What the UI thread asks of the camera layer: where the frames come from and how the picture sits on the stage.
/// Replaced whole (see <see cref="GpuStageFeed.Camera"/>), never changed; null means no camera layer.
/// </summary>
/// <param name="Ring">The ring the camera reader publishes into. The render thread is its only consumer while this is set.</param>
/// <param name="Fit">Whole picture with bars, or filling the stage with a crop.</param>
/// <param name="Mirror">Flip the picture left to right (the calibration is not affected).</param>
/// <param name="Opacity">0..1, how much of the picture shows; the rest of the stage's own background shows through nothing, it simply gets darker.</param>
internal sealed record GpuCameraInput(CameraFrameRing Ring, PictureFit Fit, bool Mirror, float Opacity);

/// <summary>
/// The newest camera picture as a GPU texture: one dynamic texture that is rewritten with <c>Map(WRITE_DISCARD)</c> for each
/// frame and only re-created when the camera changes size. Used from the render thread only.
/// </summary>
/// <remarks>
/// The texture is plain B8G8R8A8 (not sRGB): the composite pass blends the picture over the already display-encoded light,
/// so the camera's own gamma passes through unchanged and the picture is exactly what the camera delivered.
/// </remarks>
internal sealed class GpuCameraTexture : IDisposable
{
    /// <summary>Largest side accepted; anything bigger is a corrupt frame, not a camera.</summary>
    internal const int MaxSide = 16384;

    private readonly ID3D11Device _device;
    private ID3D11Texture2D? _texture;
    internal ID3D11ShaderResourceView? View { get; private set; }
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    /// <summary>Sequence number of the frame now in the texture (0 before the first).</summary>
    internal long Sequence { get; private set; }
    /// <summary>How many times the texture has been (re)created; a steady camera creates it once.</summary>
    internal int Creations { get; private set; }
    /// <summary>How many frames have been written into it.</summary>
    internal long Uploads { get; private set; }

    internal GpuCameraTexture(ID3D11Device device) => _device = device;

    /// <summary>True when the frame has a usable size and enough pixels.</summary>
    internal static bool IsUsable(in CameraFrameView frame) =>
        frame.Width is > 0 and <= MaxSide && frame.Height is > 0 and <= MaxSide && frame.Pixels is not null
        && frame.Pixels.Length >= (long)frame.Width * frame.Height * 4;

    /// <summary>Writes the frame into the texture; returns false (and leaves the texture as it was) for an unusable frame.</summary>
    internal bool Upload(ID3D11DeviceContext context, in CameraFrameView frame)
    {
        if (!IsUsable(frame)) return false;
        if (_texture is null || frame.Width != Width || frame.Height != Height)
        {
            View?.Dispose(); _texture?.Dispose();
            _texture = _device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)frame.Width, (uint)frame.Height, mipLevels: 1,
                bindFlags: BindFlags.ShaderResource, usage: ResourceUsage.Dynamic, cpuAccessFlags: CpuAccessFlags.Write);
            View = _device.CreateShaderResourceView(_texture);
            Width = frame.Width; Height = frame.Height;
            Creations++;
        }
        var rowBytes = frame.Width * 4;
        var mapped = context.Map(_texture, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            for (var y = 0; y < frame.Height; y++)
                Marshal.Copy(frame.Pixels, y * rowBytes, mapped.DataPointer + (nint)((long)y * mapped.RowPitch), rowBytes);
        }
        finally { context.Unmap(_texture, 0); }
        Sequence = frame.Sequence;
        Uploads++;
        return true;
    }

    public void Dispose()
    {
        View?.Dispose(); _texture?.Dispose();
        View = null; _texture = null; Width = Height = 0;
    }
}
