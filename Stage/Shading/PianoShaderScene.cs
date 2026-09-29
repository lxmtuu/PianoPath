using System.Globalization;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Which keys sound right now; the shader treats every one of them as an emissive surface plus a colored area light.</summary>
internal sealed class KeyLightState
{
    internal readonly bool[] Down = new bool[128];
    internal readonly Color[] Tint = new Color[128];
    internal readonly double[] Amount = new double[128];
    internal readonly List<int> Pitches = [];

    internal void Clear()
    {
        Array.Clear(Down); Array.Clear(Amount); Pitches.Clear();
    }

    internal void Light(int pitch, Color color, double amount)
    {
        if (pitch < 0 || pitch > 127 || amount <= 0) return;
        if (!Down[pitch]) Pitches.Add(pitch);
        Down[pitch] = true; Tint[pitch] = color; Amount[pitch] = Math.Clamp(amount, 0, 2);
    }
}

/// <summary>
/// The scene description for the ray-traced keyboard: a cylindrical pinhole camera looking down at 88
/// key boxes, a softbox above them and the materials of ivory, ebony, the key bed and the fallboard.
/// World units are white keys, so one unit of X is exactly one key lane and falling notes stay aligned.
/// </summary>
internal sealed class PianoShaderScene
{
    internal const int WhiteKeys = 52;
    internal const int FirstPitch = 21;
    internal const int KeyCount = 88;
    internal const double WorldWidth = WhiteKeys;
    /// <summary>Gap between two neighbouring white keys, and the black key footprint; both match the vector keyboard hit test.</summary>
    internal const double WhiteGap = .028;
    internal const double BlackWidth = .52;
    /// <summary>How far a sounding key lights up its neighbours, in world units.</summary>
    internal const double LitReach = 4.2;

    internal static readonly int[] WhitesBelow = BuildWhitesBelow();
    internal static readonly int[] WhitePitchAt = BuildWhitePitchAt();
    internal static readonly int[] BlackPitchAtBoundary = BuildBlackPitchAtBoundary();
    internal static readonly double[] KeyCenterX = BuildKeyCenterX();

    internal static bool IsBlack(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;

    internal static double BlackKeyOffset(int pitch) => (pitch % 12) switch
    {
        1 => -0.08,
        3 => 0.08,
        6 => -0.10,
        8 => 0.00,
        10 => 0.10,
        _ => 0.0
    };

    // ---- output -----------------------------------------------------------------------------------
    internal int BandWidth = 1920;
    internal int BandHeight = 240;
    /// <summary>Internal supersampling factor; below 1 the bake is rendered smaller and filtered by WPF.</summary>
    internal double RenderScale = .8;

    // ---- camera -----------------------------------------------------------------------------------
    internal double CameraHeight = 8.2;
    internal double CameraDistance = 12.5;
    /// <summary>Fraction of the band reserved for the key bed and fallboard behind the keys.</summary>
    internal double BedFraction = .17;

    // ---- geometry ---------------------------------------------------------------------------------
    internal double WhiteDepth = 6.0;
    internal double BlackDepth = 3.9;
    internal double WhiteLip = .45;
    internal double BlackHeight = .52;
    internal double BedDepth = 2.6;
    internal double BedDrop = .08;
    internal double KeyBedDepth = .9;
    internal double FallboardHeight = 2.4;
    internal double PressDepth = .22;

    // ---- materials --------------------------------------------------------------------------------
    internal Color WhiteKeyColor = Color.FromRgb(238, 235, 228);
    internal Color BlackKeyColor = Color.FromRgb(26, 26, 32);
    internal Color KeyBedColor = Color.FromRgb(14, 13, 20);
    internal Color BedColor = Color.FromRgb(22, 21, 30);
    internal Color FallboardColor = Color.FromRgb(12, 12, 18);
    internal double WhiteRoughness = .34;
    internal double BlackRoughness = .2;
    internal double BedRoughness = .6;
    internal double FallboardRoughness = .14;
    /// <summary>Dielectric F0; 0.5 is Unreal's default for polished plastic and lacquer.</summary>
    internal double Specular = .5;

    // ---- lights -----------------------------------------------------------------------------------
    internal Color KeyLightColor = Color.FromRgb(255, 246, 232);
    internal double KeyLightIntensity = 3.1;
    /// <summary>Direction the key light travels in; shadows fall backwards and to the right.</summary>
    internal Vec3 KeyLightTravel = new(-.3, -.9, .32);
    internal Color FillColor = Color.FromRgb(122, 152, 255);
    internal double FillIntensity = .45;
    internal Vec3 FillTravel = new(.55, -.42, .72);
    internal Color RimColor = Color.FromRgb(198, 110, 255);
    internal double RimIntensity = 1.1;
    internal Vec3 RimTravel = new(0, -.35, .94);
    internal Color SkyColor = Color.FromRgb(70, 78, 110);
    internal Color GroundColor = Color.FromRgb(12, 11, 18);
    internal double AmbientIntensity = 1;
    /// <summary>Half extent of the softbox; bigger means a wider shadow penumbra.</summary>
    internal double LightSize = .95;
    internal double ShadowStrength = .82;
    internal double Occlusion = .75;
    internal double EmissiveIntensity = 1.7;
    internal double Exposure = 1;
    internal double Saturation = 1;
    internal double Contrast = 1;
    internal bool Filmic = true;
    internal double EdgeDarkening = .55;

    // ---- sampling ---------------------------------------------------------------------------------
    internal int ShadowSamples = 3;
    internal int OcclusionSamples = 2;

    /// <summary>Camera scale factor s at the very back of the image; s = height / (depth + distance).</summary>
    internal double SBack => CameraHeight / (WhiteDepth + CameraDistance);
    internal double SFront => CameraHeight / CameraDistance;
    internal double SLip => (CameraHeight + WhiteLip) / CameraDistance;
    internal double STop
    {
        get
        {
            var fraction = Math.Clamp(BedFraction, 0, .5);
            return (SBack - fraction * SLip) / (1 - fraction);
        }
    }

    /// <summary>Builds the scene for one keyboard band from the live visual settings.</summary>
    internal static PianoShaderScene From(PianoVisualSettings settings, int bandWidth, int bandHeight, string quality)
    {
        var tilt = settings.ShaderCameraTilt / 100;
        var scene = new PianoShaderScene
        {
            BandWidth = Math.Max(2, bandWidth),
            BandHeight = Math.Max(2, bandHeight),
            // A low camera exaggerates the perspective and the black key shadows; a high one flattens the bed.
            CameraHeight = 4.6 + tilt * 7.4,
            CameraDistance = 9.4 + tilt * 6.6,
            BedFraction = .1 + tilt * .13,
            RenderScale = quality switch { "Fast" => .55, "Cinematic" => 1, _ => .78 },
            // The reference captures show clean key tops: enough shadow/AO samples that the soft penumbra
            // and contact occlusion converge instead of reading as grain.
            ShadowSamples = quality switch { "Fast" => 4, "Cinematic" => 12, _ => 8 },
            OcclusionSamples = quality switch { "Fast" => 2, "Cinematic" => 6, _ => 4 },
            WhiteRoughness = .62 - Math.Clamp(settings.ShaderGloss / 100, 0, 1) * .5,
            BlackRoughness = .42 - Math.Clamp(settings.ShaderGloss / 100, 0, 1) * .36,
            KeyLightIntensity = settings.ShaderKeyLight / 100 * 3.4,
            ShadowStrength = settings.ShaderShadows / 100,
            Occlusion = settings.ShaderAmbientOcclusion / 100,
            RimIntensity = (settings.ShowHalo ? 1.0 : 0.0) * (settings.ShaderRimLight / 100 * 1.6),
            EmissiveIntensity = settings.ShaderEmissive / 100 * 2.2,
            Exposure = settings.ShaderExposure / 100,
            Saturation = settings.Saturation / 100,
            Contrast = settings.Contrast / 100,
            Filmic = settings.ShaderFilmic,
            PressDepth = .1 + settings.KeyPressDepth / 100 * .3,
            LightSize = .35 + settings.ShaderShadows / 100 * .6,
            WhiteDepth = 5.4 + settings.KeyOverhang / 100 * 1.2,
            BlackDepth = 3.5 + settings.KeyOverhang / 100 * .8,
            RimColor = Parse(settings.HaloColor, Color.FromRgb(198, 110, 255)),
            FillColor = Parse(settings.NoteColorEnd, Color.FromRgb(122, 152, 255)),
            WhiteKeyColor = settings.KeyboardStyle switch
            {
                "Glass" => Color.FromRgb(214, 226, 246),
                "Classic" => Color.FromRgb(246, 244, 238),
                _ => Color.FromRgb(238, 235, 228)
            },
            BlackKeyColor = settings.KeyboardStyle switch
            {
                "Glass" => Color.FromRgb(46, 54, 70),
                "Classic" => Color.FromRgb(32, 32, 38),
                _ => Color.FromRgb(26, 26, 32)
            }
        };
        if (settings.KeyboardStyle == "Glass")
        {
            scene.WhiteRoughness = Math.Min(scene.WhiteRoughness, .12);
            scene.BlackRoughness = Math.Min(scene.BlackRoughness, .08);
            scene.Specular = .62;
            scene.AmbientIntensity = 1.25;
        }
        return scene;
    }

    private static Color Parse(string value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value)!; }
        catch { return fallback; }
    }

    /// <summary>Hash of everything that changes the baked image, used to know when the cache is still valid.</summary>
    internal string Signature() => string.Join('|',
        BandWidth, BandHeight, RenderScale.ToString("0.###", CultureInfo.InvariantCulture), CameraHeight.ToString("0.###", CultureInfo.InvariantCulture), CameraDistance.ToString("0.###", CultureInfo.InvariantCulture),
        BedFraction.ToString("0.###", CultureInfo.InvariantCulture), WhiteDepth.ToString("0.###", CultureInfo.InvariantCulture), BlackDepth.ToString("0.###", CultureInfo.InvariantCulture), PressDepth.ToString("0.###", CultureInfo.InvariantCulture),
        WhiteRoughness.ToString("0.###", CultureInfo.InvariantCulture), BlackRoughness.ToString("0.###", CultureInfo.InvariantCulture), Specular.ToString("0.###", CultureInfo.InvariantCulture),
        KeyLightIntensity.ToString("0.###", CultureInfo.InvariantCulture), ShadowStrength.ToString("0.###", CultureInfo.InvariantCulture), Occlusion.ToString("0.###", CultureInfo.InvariantCulture),
        RimIntensity.ToString("0.###", CultureInfo.InvariantCulture), AmbientIntensity.ToString("0.###", CultureInfo.InvariantCulture), EmissiveIntensity.ToString("0.###", CultureInfo.InvariantCulture),
        Exposure.ToString("0.###", CultureInfo.InvariantCulture), Saturation.ToString("0.###", CultureInfo.InvariantCulture), Contrast.ToString("0.###", CultureInfo.InvariantCulture), Filmic, LightSize.ToString("0.###", CultureInfo.InvariantCulture),
        ShadowSamples, OcclusionSamples, WhiteKeyColor.ToString(), BlackKeyColor.ToString(),
        KeyLightColor.ToString(), FillColor.ToString(), RimColor.ToString(), SkyColor.ToString(), GroundColor.ToString());

    private static int[] BuildWhitesBelow()
    {
        var result = new int[128]; var count = 0;
        for (var pitch = 0; pitch < 128; pitch++)
        {
            result[pitch] = count;
            if (pitch >= FirstPitch && pitch < FirstPitch + KeyCount && !IsBlack(pitch)) count++;
        }
        return result;
    }

    private static int[] BuildWhitePitchAt()
    {
        var result = new int[WhiteKeys]; var index = 0;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount && index < WhiteKeys; pitch++)
            if (!IsBlack(pitch)) result[index++] = pitch;
        return result;
    }

    private static int[] BuildBlackPitchAtBoundary()
    {
        var result = new int[WhiteKeys + 1];
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
            if (IsBlack(pitch)) result[WhitesBelow[pitch]] = pitch;
        return result;
    }

    private static double[] BuildKeyCenterX()
    {
        var result = new double[128];
        for (var pitch = 0; pitch < 128; pitch++)
            result[pitch] = (IsBlack(pitch)
                ? (WhitesBelow[pitch] + BlackKeyOffset(pitch))
                : (WhitesBelow[pitch] + .5)) * (WorldWidth / WhiteKeys);
        return result;
    }
}
