using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Software shader pipeline that ray-traces the 88-key stage. One primary ray per pixel is cast through
/// a cylindrical pinhole camera and shaded with the model Unreal Engine uses by default: a Cook-Torrance
/// GGX specular lobe, Lambert diffuse, an analytic image-based ambient term, jittered softbox shadow rays
/// that produce a real penumbra, hemisphere occlusion rays for contact AO, colored area lights for every
/// sounding key and an ACES filmic tonemapper with ordered dithering on the way out.
/// The camera is linear along X, so a key always lands on exactly the same pixel column as its falling note.
/// </summary>
internal static class PianoKeyboardRenderer
{
    private const double Epsilon = 1e-4;
    private const double ShadowReach = 14;
    private const int MaterialWhite = 0, MaterialBlack = 1, MaterialKeyBed = 2, MaterialBed = 3, MaterialFallboard = 4;

    /// <summary>
    /// Renders one rectangle of the keyboard band. <paramref name="focusPitch"/> turns the result into a
    /// pre-multiplied overlay tile: pixels far from that key fade to transparent so the cached base bake
    /// shows through, which is what lets a single key change without re-shading the whole keyboard.
    /// </summary>
    internal static BitmapSource? Render(PianoShaderScene scene, KeyLightState lights, int viewX, int viewY, int viewWidth, int viewHeight, int focusPitch)
    {
        if (scene.BandWidth < 2 || scene.BandHeight < 2) return null;
        viewX = Math.Clamp(viewX, 0, scene.BandWidth - 1);
        viewY = Math.Clamp(viewY, 0, scene.BandHeight - 1);
        viewWidth = Math.Min(viewWidth, scene.BandWidth - viewX);
        viewHeight = Math.Min(viewHeight, scene.BandHeight - viewY);
        if (viewWidth < 1 || viewHeight < 1) return null;
        var scale = Math.Clamp(scene.RenderScale, .25, 1);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(viewWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(viewHeight * scale));
        var stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        var context = new RenderContext(scene, lights, viewX, viewY, scale, focusPitch);
        Parallel.For(0, pixelHeight, row => RenderRow(context, row, pixelWidth, stride, pixels));
        var bitmap = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>True when the requested shading level wants the ray-traced keyboard at all.</summary>
    internal static bool IsEnabled(string quality) => quality is "Fast" or "Balanced" or "Cinematic";

    private static void RenderRow(RenderContext ctx, int row, int pixelWidth, int stride, byte[] pixels)
    {
        var scene = ctx.Scene;
        // The row maps to one camera scale factor s; every pixel on it shares the ray direction.
        var viewY = ctx.ViewY + (row + .5) / ctx.Scale;
        var s = Math.Max(ctx.STop + viewY / scene.BandHeight * (ctx.SLip - ctx.STop), 1e-4);
        var inverse = 1 / s;
        var length = Math.Sqrt(1 + inverse * inverse);
        var dirY = -1 / length;
        var dirDepth = inverse / length;
        var rowOffset = row * stride;
        var litStart = 0;
        for (var column = 0; column < pixelWidth; column++)
        {
            var viewX = ctx.ViewX + (column + .5) / ctx.Scale;
            var worldX = viewX / scene.BandWidth * PianoShaderScene.WorldWidth;
            while (litStart < ctx.LitX.Length && ctx.LitX[litStart] < worldX - PianoShaderScene.LitReach) litStart++;
            ShadePixel(ctx, ctx.ViewX + column, ctx.ViewY + row, worldX, scene.CameraHeight, -scene.CameraDistance, dirY, dirDepth, litStart, rowOffset + column * 4, pixels);
        }
    }

    private static void ShadePixel(RenderContext ctx, int absX, int absY, double worldX, double originY, double originDepth,
        double dirY, double dirDepth, int litStart, int offset, byte[] pixels)
    {
        var scene = ctx.Scene;
        var bestT = double.MaxValue;
        var material = -1; var pitch = -1; var topFace = false;
        double boxX0 = 0, boxX1 = 0, boxDepth0 = 0, boxDepth1 = 0;

        var floor = Math.Floor(worldX);
        var whiteIndex = (int)Math.Clamp(floor, 0, PianoShaderScene.WhiteKeys - 1);
        var whitePitch = PianoShaderScene.WhitePitchAt[whiteIndex];
        var whiteSink = ctx.KeyDown[whitePitch] ? scene.PressDepth : 0;
        var whiteX0 = whiteIndex + PianoShaderScene.WhiteGap * .5;
        var whiteX1 = whiteIndex + 1 - PianoShaderScene.WhiteGap * .5;
        if (worldX >= whiteX0 && worldX <= whiteX1
            && IntersectSlabs(originY, originDepth, dirY, dirDepth, -scene.WhiteLip - whiteSink, -whiteSink, 0, scene.WhiteDepth, out var whiteT, out var whiteTop)
            && whiteT < bestT)
        {
            bestT = whiteT; material = MaterialWhite; pitch = whitePitch; topFace = whiteTop;
            boxX0 = whiteX0; boxX1 = whiteX1; boxDepth0 = 0; boxDepth1 = scene.WhiteDepth;
        }

        // A black key can overlap near whiteIndex or whiteIndex + 1.
        var b0 = (int)Math.Clamp(floor, 0, PianoShaderScene.WhiteKeys);
        var b1 = (int)Math.Clamp(floor + 1, 0, PianoShaderScene.WhiteKeys);
        for (var b = b0; b <= b1; b++)
        {
            var blackPitch = PianoShaderScene.BlackPitchAtBoundary[b];
            if (blackPitch <= 0) continue;
            var blackCenter = PianoShaderScene.KeyCenterX[blackPitch];
            var bX0 = blackCenter - PianoShaderScene.BlackWidth * .5;
            var bX1 = blackCenter + PianoShaderScene.BlackWidth * .5;
            if (worldX >= bX0 && worldX <= bX1)
            {
                var blackSink = ctx.KeyDown[blackPitch] ? scene.PressDepth : 0;
                if (IntersectSlabs(originY, originDepth, dirY, dirDepth, -blackSink, scene.BlackHeight - blackSink, 0, scene.BlackDepth, out var blackT, out var blackTop)
                    && blackT < bestT)
                {
                    bestT = blackT; material = MaterialBlack; pitch = blackPitch; topFace = blackTop;
                    boxX0 = bX0; boxX1 = bX1; boxDepth0 = 0; boxDepth1 = scene.BlackDepth;
                }
            }
        }

        if (material < 0 && IntersectSlabs(originY, originDepth, dirY, dirDepth, -1.4, -scene.KeyBedDepth, -.3, scene.WhiteDepth, out var keyBedT, out var keyBedTop))
        {
            bestT = keyBedT; material = MaterialKeyBed; topFace = keyBedTop;
            boxX0 = 0; boxX1 = PianoShaderScene.WorldWidth; boxDepth0 = -.3; boxDepth1 = scene.WhiteDepth;
        }
        if (material < 0 && IntersectSlabs(originY, originDepth, dirY, dirDepth, -1.4, -scene.BedDrop, scene.WhiteDepth, scene.WhiteDepth + scene.BedDepth, out var bedT, out var bedTop))
        {
            bestT = bedT; material = MaterialBed; topFace = bedTop;
            boxX0 = 0; boxX1 = PianoShaderScene.WorldWidth; boxDepth0 = scene.WhiteDepth; boxDepth1 = scene.WhiteDepth + scene.BedDepth;
        }
        if (material < 0 && IntersectSlabs(originY, originDepth, dirY, dirDepth, -scene.BedDrop, scene.FallboardHeight,
                scene.WhiteDepth + scene.BedDepth, scene.WhiteDepth + scene.BedDepth + .6, out var boardT, out var boardTop))
        {
            bestT = boardT; material = MaterialFallboard; topFace = boardTop;
            boxX0 = 0; boxX1 = PianoShaderScene.WorldWidth;
            boxDepth0 = scene.WhiteDepth + scene.BedDepth; boxDepth1 = scene.WhiteDepth + scene.BedDepth + .6;
        }
        if (material < 0)
        {
            // Nothing under this pixel: transparent for an overlay tile, opaque black for the base bake
            // so the cached keyboard never punches a hole in the stage.
            pixels[offset] = 0; pixels[offset + 1] = 0; pixels[offset + 2] = 0;
            pixels[offset + 3] = ctx.FocusPitch > 0 ? (byte)0 : (byte)255;
            return;
        }

        var point = new Vec3(worldX, originY + dirY * bestT, originDepth + dirDepth * bestT);
        var normal = topFace ? new Vec3(0, 1, 0) : new Vec3(0, 0, -1);
        if (material is MaterialWhite or MaterialBlack && topFace)
        {
            var bevel = material == MaterialBlack ? 0.045 : 0.038;
            var edgeX = Math.Min(point.X - boxX0, boxX1 - point.X);
            if (edgeX < bevel)
            {
                var sign = point.X < (boxX0 + boxX1) * 0.5 ? -1.0 : 1.0;
                var fac = 1.0 - edgeX / bevel;
                normal = new Vec3(normal.X + sign * fac * 0.36, normal.Y, normal.Z).Normalized();
            }
            if (point.Z < bevel)
            {
                var fac = 1.0 - point.Z / bevel;
                normal = new Vec3(normal.X, normal.Y, normal.Z - fac * 0.44).Normalized();
            }
        }
        var view = new Vec3(0, -dirY, -dirDepth).Normalized();

        Vec3 albedo;
        double roughness;
        switch (material)
        {
            case MaterialWhite: albedo = ctx.WhiteAlbedo; roughness = scene.WhiteRoughness; break;
            case MaterialBlack: albedo = ctx.BlackAlbedo; roughness = scene.BlackRoughness; break;
            case MaterialKeyBed: albedo = ctx.KeyBedAlbedo; roughness = scene.BedRoughness; break;
            case MaterialBed: albedo = ctx.BedAlbedo; roughness = scene.BedRoughness; break;
            default: albedo = ctx.FallboardAlbedo; roughness = scene.FallboardRoughness; break;
        }

        var emissive = Vec3.Zero;
        if (pitch > 0 && ctx.KeyDown[pitch])
        {
            var litColor = ShaderMath.ToLinear(ctx.KeyColor[pitch]);
            albedo = Vec3.Lerp(albedo, litColor, material == MaterialBlack ? .5 : .34);
            emissive = litColor * (scene.EmissiveIntensity * ctx.KeyAmount[pitch]);
        }

        var radiance = Vec3.Zero;

        // 1 · Softbox key light. Stratifying the samples over a grid of the light rectangle makes the
        // penumbra converge without per-pixel grain; a little hash jitter per cell keeps banding away.
        // The bed, fallboard and key front faces sit in shadow almost everywhere, so they take one
        // deterministic attenuated sample instead of stochastic rays and stay perfectly smooth; only the
        // key tops - where the black-key penumbra actually moves - keep the stratified softbox rays.
        var keyFace = topFace && material is MaterialWhite or MaterialBlack;
        var samples = keyFace ? Math.Max(1, scene.ShadowSamples) : 1;
        var grid = (int)Math.Ceiling(Math.Sqrt(samples));
        for (var i = 0; i < samples; i++)
        {
            Vec3 toLight;
            double visibility;
            if (keyFace)
            {
                var jitterA = ((i % grid + .5 + (ShaderMath.Hash(absX, absY, i * 2) - .5) * .9) / grid * 2 - 1) * scene.LightSize;
                var jitterB = ((i / grid + .5 + (ShaderMath.Hash(absX, absY, i * 2 + 1) - .5) * .9) / grid * 2 - 1) * scene.LightSize;
                toLight = (ctx.ToKeyLight + ctx.LightSideA * jitterA + ctx.LightSideB * jitterB).Normalized();
                visibility = IsShadowed(ctx, point, normal, toLight) ? 1 - scene.ShadowStrength : 1;
            }
            else
            {
                toLight = ctx.ToKeyLight;
                visibility = 1 - scene.ShadowStrength * .85;
            }
            var ndl = Vec3.Dot(normal, toLight);
            if (ndl <= 0) continue;
            radiance += EvaluateBrdf(albedo, roughness, ctx.F0, normal, view, toLight) * ctx.KeyLightLinear * (ndl * visibility);
        }
        radiance = radiance / samples;

        // 2 · Cool fill from the room, unshadowed.
        var fillDot = Vec3.Dot(normal, ctx.ToFill);
        if (fillDot > 0) radiance += EvaluateBrdf(albedo, roughness, ctx.F0, normal, view, ctx.ToFill) * ctx.FillLinear * fillDot;

        // 3 · Accent rim light from behind the fallboard; the signature look of a stage-lit keyboard.
        var rimDot = Vec3.Dot(normal, ctx.ToRim);
        if (rimDot > 0) radiance += EvaluateBrdf(albedo, roughness, ctx.F0, normal, view, ctx.ToRim) * ctx.RimLinear * Math.Pow(rimDot, 1.6);

        // 3b · Direct illumination from the glowing halo impact line at the fallboard edge.
        if (scene.RimIntensity > 0)
        {
            var toHalo = new Vec3(0, .4 - point.Y, scene.WhiteDepth - point.Z);
            var hdist = toHalo.Length;
            if (hdist > 1e-4)
            {
                var hdir = toHalo / hdist;
                var hndl = Math.Max(Vec3.Dot(normal, hdir), 0.0);
                if (hndl > 0)
                {
                    var hatten = 1.0 / (1.0 + hdist * hdist * .12);
                    var hb = EvaluateBrdf(albedo, roughness, ctx.F0, normal, view, hdir);
                    radiance += hb * ctx.RimLinear * (hndl * hatten * 1.25);
                }
            }
        }

        // 4 · Image based ambient plus ray-traced contact occlusion.
        var occlusion = TraceOcclusion(ctx, point, normal, absX, absY);
        var skyFactor = ShaderMath.Clamp01(normal.Y * .5 + .5);
        radiance += albedo * Vec3.Lerp(ctx.GroundLinear, ctx.SkyLinear, skyFactor) * (scene.AmbientIntensity * occlusion);

        // 5 · Glossy environment reflection, evaluated along the reflection vector.
        var reflected = Vec3.Reflect(-view, normal);
        var reflectionSky = ShaderMath.Clamp01(reflected.Y * .5 + .5);
        radiance += Vec3.Lerp(ctx.GroundLinear, ctx.SkyLinear, reflectionSky) * 1.1
            * ShaderMath.FresnelSchlick(Vec3.Dot(normal, view), ctx.F0) * (1 - roughness) * occlusion;

        // 6 · Colored area lights from every sounding key nearby.
        for (var k = litStart; k < ctx.LitX.Length; k++)
        {
            if (ctx.LitX[k] - worldX > PianoShaderScene.LitReach) break;
            var delta = ctx.LitPosition[k] - point;
            var distanceSquared = delta.LengthSquared;
            if (distanceSquared < 1e-6) continue;
            var toLight = delta / Math.Sqrt(distanceSquared);
            var ndl = Vec3.Dot(normal, toLight);
            if (ndl <= 0) continue;
            var litBrdf = EvaluateBrdf(albedo, roughness, ctx.F0, normal, view, toLight);
            radiance += litBrdf * ctx.LitColor[k] * (ndl * (ctx.LitAmount[k] / (1 + distanceSquared * .55)));
        }

        radiance += emissive;

        // Edge darkening: keys read as separate objects because their sides lose ambient light.
        if (material is MaterialWhite or MaterialBlack)
        {
            var edgeX = Math.Min(point.X - boxX0, boxX1 - point.X);
            var edgeDepth = Math.Min(point.Z - boxDepth0, boxDepth1 - point.Z);
            radiance *= 1 - scene.EdgeDarkening * scene.Occlusion * (1 - ShaderMath.Smoothstep(0, .05, edgeX)) * .9;
            radiance *= 1 - scene.EdgeDarkening * (1 - ShaderMath.Smoothstep(0, .1, edgeDepth)) * .3;
            if (topFace && edgeDepth < .2) radiance += ctx.KeyLightLinear * (.06 * (1 - edgeDepth / .2));
        }

        // ---- post: exposure, filmic curve, grade, dither, sRGB -------------------------------------
        radiance = radiance * scene.Exposure;
        radiance = scene.Filmic
            ? ShaderMath.AcesTonemap(radiance)
            : new Vec3(ShaderMath.Clamp01(radiance.X), ShaderMath.Clamp01(radiance.Y), ShaderMath.Clamp01(radiance.Z));
        radiance = ShaderMath.AdjustContrast(ShaderMath.AdjustSaturation(radiance, scene.Saturation), scene.Contrast);
        var dither = ShaderMath.Dither(absX, absY);
        var red = ShaderMath.EncodeSrgb(radiance.X, dither);
        var green = ShaderMath.EncodeSrgb(radiance.Y, dither);
        var blue = ShaderMath.EncodeSrgb(radiance.Z, dither);

        // An overlay tile only ever repaints its own key plus the bed and gaps around it. Painting another
        // key's face would fight the tile of that neighbour, because tiles are cached per key and color.
        var influence = 1.0;
        if (ctx.FocusPitch > 0 && pitch != ctx.FocusPitch)
            influence = material is MaterialWhite or MaterialBlack ? 0 : 1 - ShaderMath.Smoothstep(.2, 2.4, (point - ctx.FocusCenter).Length);
        if (influence <= 0) { pixels[offset] = 0; pixels[offset + 1] = 0; pixels[offset + 2] = 0; pixels[offset + 3] = 0; return; }

        var alpha = (byte)Math.Clamp((int)Math.Round(influence * 255), 0, 255);
        var blend = alpha / 255.0;
        pixels[offset] = (byte)Math.Clamp((int)Math.Round(blue * blend), 0, 255);
        pixels[offset + 1] = (byte)Math.Clamp((int)Math.Round(green * blend), 0, 255);
        pixels[offset + 2] = (byte)Math.Clamp((int)Math.Round(red * blend), 0, 255);
        pixels[offset + 3] = alpha;
    }

    /// <summary>Cook-Torrance micro-facet BRDF: GGX distribution, Smith geometry, Schlick fresnel.</summary>
    private static Vec3 EvaluateBrdf(Vec3 albedo, double roughness, Vec3 f0, Vec3 n, Vec3 v, Vec3 l)
    {
        var h = (v + l).Normalized();
        var nov = Math.Max(Vec3.Dot(n, v), 1e-4);
        var nol = Math.Max(Vec3.Dot(n, l), 0);
        var noh = Math.Max(Vec3.Dot(n, h), 0);
        var voh = Math.Max(Vec3.Dot(v, h), 0);
        var distribution = ShaderMath.DistributionGgx(roughness, noh);
        var geometry = ShaderMath.GeometrySmith(roughness, nov, nol);
        var fresnel = ShaderMath.FresnelSchlick(voh, f0);
        var kd = new Vec3(1, 1, 1) - fresnel;
        var specular = fresnel * (distribution * geometry / (4 * nov * nol + 1e-4));
        return albedo * kd * (1 / ShaderMath.Pi) + specular;
    }

    private static bool IsShadowed(RenderContext ctx, Vec3 point, Vec3 normal, Vec3 toLight)
    {
        var scene = ctx.Scene;
        var origin = point + normal * .004;
        var minX = Math.Min(origin.X, origin.X + toLight.X * ShadowReach) - PianoShaderScene.BlackWidth;
        var maxX = Math.Max(origin.X, origin.X + toLight.X * ShadowReach) + PianoShaderScene.BlackWidth;
        var first = Math.Max(0, (int)Math.Floor(minX));
        var last = Math.Min(PianoShaderScene.WhiteKeys, (int)Math.Ceiling(maxX));
        for (var boundary = first; boundary <= last; boundary++)
        {
            var blackPitch = PianoShaderScene.BlackPitchAtBoundary[boundary];
            if (blackPitch <= 0) continue;
            var sink = ctx.KeyDown[blackPitch] ? scene.PressDepth : 0;
            var bc = PianoShaderScene.KeyCenterX[blackPitch];
            if (IntersectBox(origin, toLight, bc - PianoShaderScene.BlackWidth * .5, bc + PianoShaderScene.BlackWidth * .5,
                    -sink, scene.BlackHeight - sink, 0, scene.BlackDepth, ShadowReach)) return true;
        }
        var boardDepth = scene.WhiteDepth + scene.BedDepth;
        return IntersectBox(origin, toLight, -1e6, 1e6, -scene.BedDrop, scene.FallboardHeight, boardDepth, boardDepth + .6, ShadowReach);
    }

    private static double TraceOcclusion(RenderContext ctx, Vec3 point, Vec3 normal, int absX, int absY)
    {
        var scene = ctx.Scene;
        var samples = scene.OcclusionSamples;
        if (samples <= 0 || scene.Occlusion <= 0) return 1;
        var tangent = new Vec3(1, 0, 0);
        var bitangent = normal.Y > .5 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
        var origin = point + normal * .004;
        var occluded = 0;
        for (var i = 0; i < samples; i++)
        {
            // Fixed golden-angle spiral directions shared by every pixel: occlusion becomes a smooth spatial
            // function instead of per-pixel noise, and the ordered dither hides any residual banding.
            var radial = Math.Sqrt((i + .5) / samples);
            var angle = i * 2.399963229728653;
            var height = Math.Sqrt(Math.Max(0, 1 - radial * radial));
            var direction = (tangent * (radial * Math.Cos(angle)) + bitangent * (radial * Math.Sin(angle)) + normal * height).Normalized();
            if (HitsOccluder(ctx, origin, direction)) occluded++;
        }
        return 1 - scene.Occlusion * (occluded / (double)samples);
    }

    private static bool HitsOccluder(RenderContext ctx, Vec3 origin, Vec3 direction)
    {
        var scene = ctx.Scene;
        const double radius = .85;
        var minX = Math.Min(origin.X, origin.X + direction.X * radius) - PianoShaderScene.BlackWidth;
        var maxX = Math.Max(origin.X, origin.X + direction.X * radius) + PianoShaderScene.BlackWidth;
        var first = Math.Max(0, (int)Math.Floor(minX));
        var last = Math.Min(PianoShaderScene.WhiteKeys, (int)Math.Ceiling(maxX));
        for (var boundary = first; boundary <= last; boundary++)
        {
            var blackPitch = PianoShaderScene.BlackPitchAtBoundary[boundary];
            if (blackPitch <= 0) continue;
            var sink = ctx.KeyDown[blackPitch] ? scene.PressDepth : 0;
            var bc = PianoShaderScene.KeyCenterX[blackPitch];
            if (IntersectBox(origin, direction, bc - PianoShaderScene.BlackWidth * .5, bc + PianoShaderScene.BlackWidth * .5,
                    -sink, scene.BlackHeight - sink, 0, scene.BlackDepth, radius)) return true;
        }
        var boardDepth = scene.WhiteDepth + scene.BedDepth;
        return IntersectBox(origin, direction, -1e6, 1e6, -scene.BedDrop, scene.FallboardHeight, boardDepth, boardDepth + .6, radius);
    }

    /// <summary>Ray against the (height, depth) rectangle of a key column; the primary ray has no X component.</summary>
    private static bool IntersectSlabs(double originY, double originDepth, double dirY, double dirDepth,
        double y0, double y1, double depth0, double depth1, out double t, out bool topFace)
    {
        var ty0 = (y0 - originY) / dirY; var ty1 = (y1 - originY) / dirY;
        var enterY = Math.Min(ty0, ty1); var exitY = Math.Max(ty0, ty1);
        var td0 = (depth0 - originDepth) / dirDepth; var td1 = (depth1 - originDepth) / dirDepth;
        var enterDepth = Math.Min(td0, td1); var exitDepth = Math.Max(td0, td1);
        var enter = Math.Max(enterY, enterDepth); var exit = Math.Min(exitY, exitDepth);
        if (enter > exit || exit <= Epsilon) { t = 0; topFace = false; return false; }
        t = Math.Max(enter, Epsilon);
        topFace = enterY >= enterDepth;
        return true;
    }

    private static bool IntersectBox(Vec3 origin, Vec3 direction, double x0, double x1, double y0, double y1, double d0, double d1, double maxT)
    {
        var enter = Epsilon; var exit = maxT;
        if (Math.Abs(direction.X) < 1e-9) { if (origin.X < x0 || origin.X > x1) return false; }
        else
        {
            var inverse = 1 / direction.X; var t0 = (x0 - origin.X) * inverse; var t1 = (x1 - origin.X) * inverse;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > enter) enter = t0;
            if (t1 < exit) exit = t1;
            if (enter > exit) return false;
        }
        if (Math.Abs(direction.Y) < 1e-9) { if (origin.Y < y0 || origin.Y > y1) return false; }
        else
        {
            var inverse = 1 / direction.Y; var t0 = (y0 - origin.Y) * inverse; var t1 = (y1 - origin.Y) * inverse;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > enter) enter = t0;
            if (t1 < exit) exit = t1;
            if (enter > exit) return false;
        }
        if (Math.Abs(direction.Z) < 1e-9) { if (origin.Z < d0 || origin.Z > d1) return false; }
        else
        {
            var inverse = 1 / direction.Z; var t0 = (d0 - origin.Z) * inverse; var t1 = (d1 - origin.Z) * inverse;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > enter) enter = t0;
            if (t1 < exit) exit = t1;
            if (enter > exit) return false;
        }
        return true;
    }

    /// <summary>Everything the pixel shader reads; built once per bake and shared read-only by the worker rows.</summary>
    private sealed class RenderContext
    {
        internal readonly PianoShaderScene Scene;
        internal readonly int ViewX, ViewY, FocusPitch;
        internal readonly double Scale, STop, SLip;
        internal readonly bool[] KeyDown;
        internal readonly Color[] KeyColor;
        internal readonly double[] KeyAmount;
        internal readonly Vec3 KeyLightLinear, FillLinear, RimLinear, SkyLinear, GroundLinear, F0;
        internal readonly Vec3 WhiteAlbedo, BlackAlbedo, KeyBedAlbedo, BedAlbedo, FallboardAlbedo;
        internal readonly Vec3 ToKeyLight, ToFill, ToRim, LightSideA, LightSideB, FocusCenter;
        internal readonly double[] LitX, LitAmount;
        internal readonly Vec3[] LitPosition, LitColor;

        internal RenderContext(PianoShaderScene scene, KeyLightState lights, int viewX, int viewY, double scale, int focusPitch)
        {
            Scene = scene; ViewX = viewX; ViewY = viewY; Scale = scale; FocusPitch = focusPitch;
            STop = scene.STop; SLip = scene.SLip;
            KeyDown = lights.Down; KeyColor = lights.Tint; KeyAmount = lights.Amount;
            KeyLightLinear = ShaderMath.ToLinear(scene.KeyLightColor) * scene.KeyLightIntensity;
            FillLinear = ShaderMath.ToLinear(scene.FillColor) * scene.FillIntensity;
            RimLinear = ShaderMath.ToLinear(scene.RimColor) * scene.RimIntensity;
            SkyLinear = ShaderMath.ToLinear(scene.SkyColor) * scene.AmbientIntensity;
            GroundLinear = ShaderMath.ToLinear(scene.GroundColor) * scene.AmbientIntensity;
            WhiteAlbedo = ShaderMath.ToLinear(scene.WhiteKeyColor);
            BlackAlbedo = ShaderMath.ToLinear(scene.BlackKeyColor);
            KeyBedAlbedo = ShaderMath.ToLinear(scene.KeyBedColor);
            BedAlbedo = ShaderMath.ToLinear(scene.BedColor);
            FallboardAlbedo = ShaderMath.ToLinear(scene.FallboardColor);
            // Unreal maps its Specular input of 0.5 to an F0 of 0.08, not to 0.5.
            var f0 = scene.Specular * .16;
            F0 = new Vec3(f0, f0, f0);
            ToKeyLight = (-scene.KeyLightTravel).Normalized();
            ToFill = (-scene.FillTravel).Normalized();
            ToRim = (-scene.RimTravel).Normalized();
            var reference = Math.Abs(ToKeyLight.Y) < .95 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            LightSideA = Vec3.Cross(reference, ToKeyLight).Normalized();
            LightSideB = Vec3.Cross(ToKeyLight, LightSideA).Normalized();
            var focusIsBlack = focusPitch > 0 && PianoShaderScene.IsBlack(focusPitch);
            FocusCenter = new Vec3(
                focusPitch > 0 ? PianoShaderScene.KeyCenterX[focusPitch] : -99,
                (focusIsBlack ? scene.BlackHeight : 0) - scene.PressDepth,
                (focusIsBlack ? scene.BlackDepth : scene.WhiteDepth) * .55);

            var ordered = lights.Pitches.Where(p => p > 0).OrderBy(p => PianoShaderScene.KeyCenterX[p]).ToArray();
            LitX = new double[ordered.Length];
            LitAmount = new double[ordered.Length];
            LitPosition = new Vec3[ordered.Length];
            LitColor = new Vec3[ordered.Length];
            for (var i = 0; i < ordered.Length; i++)
            {
                var key = ordered[i];
                var black = PianoShaderScene.IsBlack(key);
                var surface = (black ? scene.BlackHeight : 0) - scene.PressDepth;
                LitX[i] = PianoShaderScene.KeyCenterX[key];
                LitAmount[i] = lights.Amount[key] * scene.EmissiveIntensity;
                LitPosition[i] = new Vec3(LitX[i], surface + (black ? .3 : .38), (black ? scene.BlackDepth : scene.WhiteDepth) * .5);
                LitColor[i] = ShaderMath.ToLinear(lights.Tint[key]);
            }
        }
    }
}
