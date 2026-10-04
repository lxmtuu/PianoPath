using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Vortice.D3DCompiler;

namespace PianoPath;

/// <summary>
/// WPF pixel shader for the home-screen nebula: a drifting volumetric cloud, a diagonal galactic arm,
/// and hundreds of crisp stars with independent twinkle phases. The shader runs on the GPU; its time
/// uniform is advanced on the shared composition clock.
/// </summary>
internal sealed class GalaxyShaderEffect : ShaderEffect
{
    private const string ResourceName = "PianoPath.Ui.GalaxyBackdrop.hlsl";
    private static readonly Lazy<byte[]> Bytecode = new(CompileShader, LazyThreadSafetyMode.ExecutionAndPublication);

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(GalaxyShaderEffect), 0);

    public static readonly DependencyProperty PrimaryColorProperty =
        DependencyProperty.Register(nameof(PrimaryColor), typeof(Color), typeof(GalaxyShaderEffect),
            new UIPropertyMetadata(Color.FromRgb(0x9A, 0x7B, 0xFF), PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty SecondaryColorProperty =
        DependencyProperty.Register(nameof(SecondaryColor), typeof(Color), typeof(GalaxyShaderEffect),
            new UIPropertyMetadata(Color.FromRgb(0x78, 0xE7, 0xFF), PixelShaderConstantCallback(1)));

    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(nameof(Time), typeof(double), typeof(GalaxyShaderEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(2)));

    public static readonly DependencyProperty AspectProperty =
        DependencyProperty.Register(nameof(Aspect), typeof(double), typeof(GalaxyShaderEffect),
            new UIPropertyMetadata(1.7777778, PixelShaderConstantCallback(3)));

    public static readonly DependencyProperty IntensityProperty =
        DependencyProperty.Register(nameof(Intensity), typeof(double), typeof(GalaxyShaderEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(4)));

    public static readonly DependencyProperty DensityProperty =
        DependencyProperty.Register(nameof(Density), typeof(double), typeof(GalaxyShaderEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(5)));

    internal GalaxyShaderEffect()
    {
        var shader = new PixelShader();
        using (var stream = new MemoryStream(Bytecode.Value, writable: false))
            shader.SetStreamSource(stream);
        PixelShader = shader;

        // Use WPF's implicit visual input; ChromeBackdrop supplies an opaque white surface while this
        // procedural shader generates every visible color from normalized pixel coordinates.
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(PrimaryColorProperty);
        UpdateShaderValue(SecondaryColorProperty);
        UpdateShaderValue(TimeProperty);
        UpdateShaderValue(AspectProperty);
        UpdateShaderValue(IntensityProperty);
        UpdateShaderValue(DensityProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public Color PrimaryColor
    {
        get => (Color)GetValue(PrimaryColorProperty);
        set => SetValue(PrimaryColorProperty, value);
    }

    public Color SecondaryColor
    {
        get => (Color)GetValue(SecondaryColorProperty);
        set => SetValue(SecondaryColorProperty, value);
    }

    public double Time
    {
        get => (double)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public double Aspect
    {
        get => (double)GetValue(AspectProperty);
        set => SetValue(AspectProperty, Math.Clamp(value, 0.25, 5.0));
    }

    public double Intensity
    {
        get => (double)GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, Math.Clamp(value, 0.0, 2.0));
    }

    public double Density
    {
        get => (double)GetValue(DensityProperty);
        set => SetValue(DensityProperty, Math.Clamp(value, 0.0, 2.0));
    }

    /// <summary>Source for diagnostics and the Windows verification suite.</summary>
    internal static string ShaderSource
    {
        get
        {
            using var stream = typeof(GalaxyShaderEffect).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded shader resource '{ResourceName}' is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    private static byte[] CompileShader()
    {
        var source = ShaderSource;
        // WPF ShaderEffect consumes Direct3D 9 pixel-shader bytecode (ps_3_0); this is deliberately
        // separate from the D3D11 ps_5_0 stage shader. Windows ships d3dcompiler_47 for both paths.
        return Compiler.Compile(source, "PsNebula", "GalaxyBackdrop.hlsl", "ps_3_0", ShaderFlags.OptimizationLevel3).ToArray();
    }
}
