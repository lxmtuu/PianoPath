// Keyflow GPU stage - Direct3D 11 shaders (shader model 5.0).
//
// One file, many entry points; Gpu/GpuStageRenderer.cs compiles every entry point once at start-up
// with D3DCompile (vs_5_0 / ps_5_0). The pipeline is:
//
//   1. VsFullscreen + PsBackground   - background colour/image, procedural atmosphere, stars, horizon glow, key bed
//   2. VsNote       + PsNote         - instanced SDF note capsules: Solid, Neon, Glass, Fire (lava)
//   3. VsKey        + PsKey          - instanced 3D key boxes, GGX + Lambert, rim, emissive, AO, shadows
//   4. VsSprite     + PsSprite       - instanced additive particles, rings, flares, beams, hit line
//   4b. VsArQuad    + PsArQuad       - AR layer: notes projected onto the camera picture
//   5. PsBloomPrefilter / PsBloomDown / PsBloomUp - HDR bloom pyramid (13-tap down, tent up)
//   6. PsComposite                   - exposure, ACES, saturation, contrast, vignette, dither, chroma
//
// Everything before the composite writes linear HDR colour into an RGBA16F target with premultiplied
// alpha, blended One / InvSrcAlpha: an effect that outputs alpha 0 is purely additive light, an
// effect with alpha 1 covers what is behind it. The alpha channel therefore also records "something
// solid is here", which the chroma-key composite uses to decide where the green screen shows.
//
// This file must stay plain ASCII: D3DCompile receives it as an ANSI string.

cbuffer Frame : register(b0)
{
    float4 ScreenTime;   // x,y = output size px, z = time s, w = hit line y (scene units)
    float4 SceneSize;    // x,y = scene size (stage DIPs), z = output pixels per scene unit, w = 1 for the lid shadow
    float4 Camera;       // x,y = scale, z,w = offset px (scene -> screen)
    float4 Background;   // rgb = background colour (linear), w = 1 when chroma green
    float4 Aura;         // rgb = aura colour, w = gradient strength
    float4 Horizon;      // rgb = horizon glow colour, w = intensity
    float4 SceneA;       // x = star density, y = star brightness, z = 1 when a background image is bound, w = image dim
    float4 SceneB;       // x = image aspect (w/h), y = scene units per reference dip (1), z = beat pulse, w = key bed darkness
    float4 NoteA;        // x = style, y = corner radius px, z = edge width px, w = glow radius px
    float4 NoteB;        // x = tint, y = edge brightness, z = head glow, w = refraction
    float4 NoteC;        // x = texture, y = notes 3D, z = glow strength, w = fire heat distance px
    float4 KeyA;         // x = key light, y = shadows, z = ambient occlusion, w = gloss
    float4 KeyB;         // x = rim light, y = emissive, z = camera tilt, w = keyboard style (0 classic, 1 studio, 2 glass)
    float4 KeyC;         // x = keyboard height px, y = black key length px, z = key lighting spill, w = front face height px
    float4 KeyD;         // x = white key width px, y = black key width px, z = black key height px, w = perspective
    float4 RimColor;     // rgb = rim light colour, w = horizon glow height (scene units)
    float4 Post;         // x = exposure, y = filmic (1/0), z = saturation, w = contrast
    float4 Post2;        // x = vignette, y = bloom intensity, z = bloom threshold, w = dither amplitude
    float4 SceneFx;      // x = GPU background-motion id, y = amount, z = speed, w = key activity
    float4 SceneFxColor; // rgb = background-motion tint
    float4 HitFx;        // x = hit-line motion id, y = intensity, z = speed, w = key activity
    float4 KeyRange;     // x = lowest pitch of the keyboard, y = pitches above it (keys - 1)
    float4 ArPicture;    // x,y = left, top and z,w = width, height of the camera picture on the output (px)
    float4 ArFlags;      // x = 1 when mirrored, y = 1 when the camera is active, z = picture opacity, w = 1 for transparent AR effects output
    float4 ArMatrix0;    // legacy scene->image map rows; active AR notes use ArCameraPose + GpuArLayer quads instead
    float4 ArMatrix1;
    float4 ArMatrix2;
    float4 ArMatrixFlags; // legacy shader-branch flags; the active renderer keeps all four lanes zero
    float4 NoteD;         // x = opt-in satin surface (Solid/Glass only); remaining lanes reserved
};

cbuffer PassConstants : register(b1)
{
    float4 PassA;        // x,y = source texel size, z = radius / knee, w = mode
};

struct NoteInstance
{
    float4 Rect;         // x, y (top-left), w, h in scene px
    float4 Color;        // rgb linear note colour, a = opacity
    float4 Misc;         // x = sounding (0..1), y = seed, z = head direction (+1 bottom, -1 top), w = played
};

struct ArQuadInstance
{
    float4 A;            // x0, y0, x1, y1: back-left and back-right corners (output px)
    float4 B;            // x2, y2, x3, y3: front-right and front-left corners (output px)
    float4 W;            // the four corner weights of the projective map
    float4 Color;        // rgb linear, a = opacity
    float4 Misc;         // note: x=sounding, y=seed, w=hold-bar; z = 0 note/bar, 1 key tint, 2 particle, 3 key light, 4 pulse, 5 ribbon, 6 flash, 7 beam, 8 note shimmer, 9 note trail, 10 note label, 11 damper strip
};

struct KeyInstance
{
    float4 Box;          // x0, x1 (scene units), z = length, w = height
    float4 Emit;         // rgb = emissive colour (linear), a = strength 0..1
    float4 Misc;         // x = kind (0 white, 1 black, 2 felt), y = press 0..1, z = shadow overlap left px, w = shadow overlap right px
    float4 Base;         // x = base height (black keys stand on the whites), y = spill strength, z = depth offset, w = unused
};

struct SpriteInstance
{
    float4 PosSize;      // x, y scene px, z = size px, w = kind
    float4 Color;        // rgb = HDR colour, a = alpha
    float4 Dir;          // x,y = direction (unit), z = stretch (streak length / beam height), w = age 0..1
};

StructuredBuffer<NoteInstance> Notes : register(t2);
StructuredBuffer<KeyInstance> Keys : register(t3);
StructuredBuffer<SpriteInstance> Sprites : register(t4);
StructuredBuffer<float4> KeyColors : register(t5);   // 128 entries: rgb = note colour of the pitch, a = activity
StructuredBuffer<ArQuadInstance> ArQuads : register(t8);   // AR layer: one projected quad per note

Texture2D SourceTex : register(t0);
Texture2D SecondTex : register(t1);
Texture2D AtlasTex : register(t6);       // glyph atlas: 16 x 24 cells of 64 x 32 px, coverage in alpha
Texture2D CameraTex : register(t7);      // the newest camera picture, display-encoded (not sRGB-converted on read)
Texture2D MarkTex : register(t8);        // the export watermark strip, whole-line text coverage in alpha
SamplerState LinearClamp : register(s0);
SamplerState PointClamp : register(s1);

static const float PI = 3.14159265;

// ---------------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------------

float2 SceneToScreen(float2 p) { return p * Camera.xy + Camera.zw; }
float2 ScreenToScene(float2 p) { return (p - Camera.zw) / Camera.xy; }

float4 ToClip(float2 screenPx, float depth)
{
    float2 ndc = screenPx / ScreenTime.xy * float2(2.0, -2.0) + float2(-1.0, 1.0);
    return float4(ndc, depth, 1.0);
}

// Legacy shader helper for the dormant scene->image map branch. The normal renderer leaves ArMatrixFlags zero;
// active 3D notes are projected on the CPU and drawn by VsArQuad from the camera-facing patches in GpuArLayer.
float3 ArMapAt(float2 scene)
{
    float3 at = float3(scene, 1.0);
    return float3(dot(ArMatrix0.xyz, at), dot(ArMatrix1.xyz, at), dot(ArMatrix2.xyz, at));
}

// The weight below which the scene->image map has no picture (ArProjection.MinW on the C# side, the same units).
static const float ArHorizonW = 0.1;

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float Fbm(float2 p)
{
    float sum = 0.0;
    float amp = 0.55;
    [unroll] for (int i = 0; i < 4; i++)
    {
        sum += ValueNoise(p) * amp;
        p = p * 2.03 + float2(17.1, 9.7);
        amp *= 0.5;
    }
    return sum;
}

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float SdRoundBox(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

// 6 vertices of a quad as two triangles, corners in [-1, 1].
float2 QuadCorner(uint id)
{
    uint i = id % 6;
    float2 c = float2(-1.0, -1.0);
    if (i == 1 || i == 4) c = float2(1.0, -1.0);
    else if (i == 2 || i == 3) c = float2(-1.0, 1.0);
    else if (i == 5) c = float2(1.0, 1.0);
    return c;
}

// ---------------------------------------------------------------------------------------------------
// 1. background
// ---------------------------------------------------------------------------------------------------

struct FullscreenOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

FullscreenOut VsFullscreen(uint id : SV_VertexID)
{
    FullscreenOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.Position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    o.Uv = uv;
    return o;
}

float3 Stars(float2 p)
{
    float cellSize = 26.0 * SceneB.y;
    float2 cell = floor(p / cellSize);
    float2 local = frac(p / cellSize);
    float present = Hash21(cell + 3.7);
    if (present > SceneA.x) return 0.0;
    float2 centre = float2(Hash21(cell + 11.1), Hash21(cell + 23.9)) * 0.8 + 0.1;
    float d = length((local - centre) * cellSize);
    float twinkle = 0.55 + 0.45 * sin(ScreenTime.z * (1.1 + present * 3.0) + present * 40.0);
    float size = 0.6 + Hash21(cell + 5.3) * 1.1;
    float star = exp(-d * d / (size * size)) * twinkle;
    float3 tint = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.86, 0.95), Hash21(cell + 8.8));
    return tint * star * SceneA.y;
}

float3 SpectrumRgb(float hue)
{
    float3 k = frac(hue + float3(0.0, 0.6666667, 0.3333333)) * 6.0;
    return saturate(abs(k - 3.0) - 1.0);
}

// Full-screen procedural atmosphere: no particle allocation or CPU-generated geometry is needed for
// these slow, broad colour fields. The mode is uniform for the draw, so only one branch runs per pixel.
float3 BackgroundMotion(float2 uv)
{
    float mode = SceneFx.x;
    float amount = saturate(SceneFx.y);
    if (mode < 0.5 || amount <= 0.001) return 0.0;
    float speed = max(SceneFx.z, 0.05);
    float t = ScreenTime.z * speed;
    float activity = SceneFx.w;
    float3 tint = SceneFxColor.rgb;

    if (mode < 1.5) // aurora: two silk curtains with a noisy, travelling edge
    {
        float n = ValueNoise(uv * float2(3.0, 5.5) + float2(t * 0.055, -t * 0.11));
        float wave = 0.26 + 0.075 * sin(uv.x * 8.0 + t * 0.65) + 0.035 * sin(uv.x * 19.0 - t * 0.42);
        float edge = wave + (n - 0.5) * 0.16;
        float curtain = exp(-abs(uv.y - edge) * 15.0) * (1.0 - smoothstep(0.32, 0.98, uv.y));
        float fold = exp(-abs(uv.y - edge - 0.105) * 22.0) * 0.52;
        float rays = 0.72 + 0.28 * sin(uv.x * 48.0 + n * 6.0 + t * 1.1);
        float3 aurora = lerp(tint, float3(0.08, 0.62, 0.48), 0.42 + 0.14 * sin(uv.x * 4.0 + t * 0.24));
        return aurora * (curtain + fold) * rays * amount * (0.42 + activity * 0.12);
    }
    if (mode < 2.5) // nebula: softly domain-warped indigo and rose clouds
    {
        float2 q = (uv - float2(0.5, 0.42)) * float2(3.0, 4.1);
        float warp = ValueNoise(q * 0.8 + float2(t * 0.035, -t * 0.025));
        float cloud = ValueNoise(q * 1.45 + float2(warp * 1.8 - t * 0.025, warp * 1.1 + t * 0.02));
        float radial = saturate(1.0 - length((uv - float2(0.5, 0.39)) * float2(1.35, 1.8)));
        float mask = smoothstep(0.39, 0.73, cloud + radial * 0.18) * radial;
        float3 nebula = lerp(tint, Aura.rgb + float3(0.12, 0.015, 0.16), saturate(warp * 0.75));
        float roseCloud = smoothstep(0.55, 0.82, cloud * 0.68 + warp * 0.32);
        float3 rose = float3(0.42, 0.035, 0.19) * roseCloud;
        return (nebula * 0.62 + rose) * mask * amount * (0.56 + activity * 0.12);
    }
    if (mode < 3.5) // prism: restrained chromatic bands that drift across the sky
    {
        float phase = uv.x * 0.72 + uv.y * 0.22 + t * 0.035;
        float band = 0.5 + 0.5 * sin(phase * 6.2831853);
        float prismDistance = (band - 0.52) * 2.1;
        float veil = exp(-prismDistance * prismDistance) * (0.35 + 0.65 * uv.y);
        float3 spectral = lerp(tint, SpectrumRgb(phase + tint.r * 0.12), 0.72);
        return spectral * veil * amount * 0.22;
    }
    if (mode < 4.5) // ember haze: heat shimmer and a molten horizon, not a full-screen orange wash
    {
        float heat = ValueNoise(uv * float2(4.0, 6.0) + float2(t * 0.035, -t * 0.1));
        float wave = 0.64 + 0.045 * sin(uv.x * 10.0 + t * 0.48) + (heat - 0.5) * 0.08;
        float mask = smoothstep(wave - 0.07, wave + 0.08, uv.y) * (1.0 - smoothstep(0.78, 0.94, uv.y));
        float flicker = 0.82 + 0.18 * sin(t * 2.1 + heat * 7.0 + activity * 1.5);
        return lerp(float3(0.72, 0.075, 0.012), tint, 0.48) * mask * flicker * amount * 0.46;
    }
    if (mode < 5.5) // ocean flow: overlapping caustic ribbons, brighter near the surface
    {
        float phase = uv.x * 10.0 + sin(uv.y * 9.0 - t * 0.38) * 1.3 - t * 0.62;
        float wave = 0.5 + 0.5 * sin(phase);
        float caustic = pow(saturate(wave * (0.62 + 0.38 * sin(uv.x * 5.0 + t * 0.3))), 7.0);
        float depth = saturate(1.0 - uv.y * 0.9);
        float3 water = lerp(float3(0.018, 0.18, 0.34), tint, 0.62);
        return water * (0.14 + caustic * 0.9) * depth * amount * 0.5;
    }
    // retro grid: a low-perspective neon floor and slow scan line, confined to the lower sky
    float horizonY = 0.43 + 0.012 * sin(t * 0.42);
    float depth01 = saturate((uv.y - horizonY) / max(1.0 - horizonY, 0.05));
    float verticalGrid = 1.0 - smoothstep(0.025, 0.075, abs(frac(uv.x * 18.0 + 0.5) - 0.5));
    float perspectiveY = log2(1.0 + depth01 * 25.0) * 3.1 - t * 0.32;
    float horizontalGrid = 1.0 - smoothstep(0.035, 0.12, abs(frac(perspectiveY) - 0.5));
    float floorMask = smoothstep(horizonY - 0.015, horizonY + 0.035, uv.y);
    float grid = (verticalGrid * 0.22 + horizontalGrid * 0.48) * floorMask;
    float scan = 0.88 + 0.12 * sin((uv.y * ScreenTime.y) * 0.18 - t * 2.0);
    return tint * grid * scan * amount * 0.72;
}

float4 PsBackground(FullscreenOut i) : SV_Target
{
    float2 screenPx = i.Uv * ScreenTime.xy;
    float2 p = ScreenToScene(screenPx);
    float hitY = ScreenTime.w;
    if (Background.w > 0.5) return float4(0.0, 0.0, 0.0, 0.0);

    float3 c = Background.rgb;
    if (SceneA.z > 0.5)
    {
        // "cover" fit: the image fills the stage and keeps its aspect ratio
        float stageAspect = SceneSize.x / SceneSize.y;
        float2 uv = p / SceneSize.xy;
        if (SceneB.x > stageAspect) uv.x = (uv.x - 0.5) * stageAspect / SceneB.x + 0.5;
        else uv.y = (uv.y - 0.5) * SceneB.x / stageAspect + 0.5;
        c = SecondTex.SampleLevel(LinearClamp, uv, 0).rgb * (1.0 - SceneA.w);
    }

    // aura: a soft violet cloud high above the keys, like the software stage's radial gradient
    float2 a = (p / SceneSize.xy - float2(0.5, 0.24)) / float2(0.72, 0.88);
    float aura = saturate(1.0 - length(a));
    c += Aura.rgb * Aura.w * aura * aura * (p.y < hitY ? 1.0 : 0.0);

    if (p.y < hitY)
    {
        c += BackgroundMotion(i.Uv);
        c += Stars(p);
        // horizon: light rising off the keyboard, coloured by the keys that sound. The software stage's
        // gradient: 40 + glow x 2.6 DIPs tall (RimColor.w), linear in sRGB, so about rise^2.2 in linear light
        float rise = saturate(1.0 - (hitY - p.y) / max(RimColor.w, 1.0));
        c += Horizon.rgb * Horizon.w * pow(rise, 2.2);
    }
    else
    {
        // key bed: the dark case the keys sit in
        c = lerp(c, float3(0.004, 0.004, 0.006), SceneB.w);
    }
    return float4(c, 0.0);
}

// ---------------------------------------------------------------------------------------------------
// AR layer: quads of the keyboard plane projected onto the camera picture (Camera/ArProjection.cs)
// ---------------------------------------------------------------------------------------------------

// Common input to the full Live Stage note material, used by both stage-space notes and camera-projected AR notes.
struct NoteMaterialInput
{
    float2 Local;      // scene DIPs from the note centre
    float2 Half;       // half size in scene DIPs
    float4 Color;      // linear rgb and opacity
    float4 Misc;       // x = sounding, y = seed, z = head direction, w = hold-bar strength
    float SceneY;      // scene DIPs
};

float4 ShadeNoteMaterial(NoteMaterialInput v);

struct ArQuadOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;       // x across the note, y from its tail (0) to its leading edge (1)
    float4 Color : TEXCOORD1;
    float4 Misc : TEXCOORD2;
};

ArQuadOut VsArQuad(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    ArQuadInstance q = ArQuads[iid];
    // two triangles over the corners 0,1,2 and 0,2,3
    uint k = vid < 3u ? vid : (vid == 3u ? 0u : vid - 2u);
    float2 uv = float2((k == 1u || k == 2u) ? 1.0 : 0.0, k >= 2u ? 1.0 : 0.0);
    float2 p = q.A.xy;
    float w = q.W.x;
    if (k == 1u) { p = q.A.zw; w = q.W.y; }
    else if (k == 2u) { p = q.B.xy; w = q.W.z; }
    else if (k == 3u) { p = q.B.zw; w = q.W.w; }
    if (q.Misc.z < 0.5)
    {
        // PsNote needs a margin outside the body for its bevel and halo. Expand this same calibrated billboard
        // in its own screen-facing axes and extend UVs so its interior still maps to the original note rectangle.
        float2 across = ((q.A.zw - q.A.xy) + (q.B.xy - q.B.zw)) * 0.5;
        float2 along = ((q.B.zw - q.A.xy) + (q.B.xy - q.A.zw)) * 0.5;
        float width = max(length(across), 1e-3);
        float height = max(length(along), 1e-3);
        float2 acrossUnit = across / width;
        float2 alongUnit = along / height;
        float2 cornerSign = uv * 2.0 - 1.0;
        float margin = (NoteA.w + NoteA.z + 2.0) * SceneSize.z;
        p += acrossUnit * cornerSign.x * margin + alongUnit * cornerSign.y * margin;
        uv += cornerSign * float2(margin / width, margin / height);
    }
    float2 ndc = float2(p.x / ScreenTime.x * 2.0 - 1.0, 1.0 - p.y / ScreenTime.y * 2.0);
    ArQuadOut o;
    // the corner weight as clip w: the rasteriser then interpolates across the quad in perspective
    o.Position = float4(ndc * w, 0.0, w);
    o.Uv = uv;
    o.Color = q.Color;
    o.Misc = q.Misc;
    return o;
}

// Pixel-box integration of a thin signed-distance band. Unlike smoothstep(width +/- fwidth(abs(d))),
// a subpixel stroke loses coverage with its width instead of staying a half-opaque card at any scale.
float HeldStrokeCoverage(float signedDistance, float halfWidth, float pixelHalfWidth)
{
    float pixel = max(pixelHalfWidth, 0.0001);
    float overlap = min(signedDistance + pixel, halfWidth) - max(signedDistance - pixel, -halfWidth);
    return saturate(overlap / (2.0 * pixel));
}

// Experimental held surfaces. Both Stage and AR use the same premultiplied material;
// world anchoring/projection happens in the builders, not in this shading function.
float4 ShadeHeldParticleArt(float2 uv, float4 color, float progress, float shape, float phase)
{
    float h = saturate(uv.y);
    float x = uv.x * 2.0 - 1.0;
    float coverage, core;
    float3 bodyColor = color.rgb;
    if (shape < 5.0) // translucent heat veil around a narrower, curved luminous core
    {
        float centre = sin(h * 5.5 + phase + progress * 2.0) * 0.22 * h;
        float width = lerp(0.78, 0.035, pow(h, 0.8));
        float signedDistance = x - centre;
        float pixelHalf = max(fwidth(signedDistance) * 0.5, 0.0125);
        float veil = HeldStrokeCoverage(signedDistance, width, pixelHalf);
        float spine = HeldStrokeCoverage(signedDistance, width * 0.28 + 0.015, pixelHalf);
        float ends = smoothstep(0.0, 0.12, h) * (1.0 - smoothstep(0.86, 1.0, h));
        coverage = (veil * 0.28 + spine * 0.60) * ends;
        core = spine * ends * (1.0 - h) * 0.16;
        bodyColor *= lerp(1.0, 0.65, h); // depth along the parcel, not another lifetime fade
    }
    else if (shape < 6.5) // Flames v3 review: two tapered tongues, coloured mantle and luminous inner body
    {
        // Confined to the existing base-anchored quad: no new births/geometry, no extra age fade.
        float bend = sin(h * 4.8 + phase + progress * 2.6) * 0.20 * h;
        float belly = 0.72 * pow(max(1.0 - h, 0.0), 0.78) * (0.80 + 0.20 * sin(h * 3.14159));
        float d = x - bend;
        float pixelHalf = max(fwidth(d) * 0.5, 0.0125);
        float ends = smoothstep(0.0, 0.09, h) * (1.0 - smoothstep(0.88, 1.0, h));
        float mantle = HeldStrokeCoverage(d, belly, pixelHalf) * ends;
        float inner = HeldStrokeCoverage(d + belly * 0.10, belly * lerp(0.48, 0.22, h), pixelHalf) * ends;
        float sideH = saturate(h / 0.73);
        float sideWidth = 0.22 * pow(max(1.0 - sideH, 0.0), 0.85);
        float sideD = x - (bend + 0.28 + sin(phase) * 0.08);
        float side = HeldStrokeCoverage(sideD, sideWidth, max(fwidth(sideD) * 0.5, 0.0125));
        side *= smoothstep(0.0, 0.14, h) * (1.0 - smoothstep(0.58, 0.73, h));
        float peak = max(max(color.r, color.g), color.b);
        float3 hue = pow(saturate(color.rgb / max(peak, 0.0001)), 1.12);
        float3 outerColor = hue * min(peak, 0.78) * lerp(1.0, 0.68, h);
        float3 innerColor = lerp(hue, float3(1.0, 0.80, 0.35), 0.12 * (1.0 - h) * (1.0 - h)) * min(peak, 0.98);
        float outerAlpha = mantle * 0.52;
        float innerAlpha = inner * 0.25;
        float sideAlpha = side * 0.12;
        float alpha = (outerAlpha + innerAlpha + sideAlpha) * color.a;
        float3 rgb = (outerColor * (outerAlpha + sideAlpha) + innerColor * innerAlpha) * color.a;
        return float4(rgb, alpha); // bounded premultiplied colour; existing CPU lifetime envelope only
    }
    else if (shape > 7.5) // Wisps v3 review candidate; v2 shape 7 and flames stay byte-for-byte below/above
    {
        // A tapered ribbon with a coloured shoulder and narrow ridge, not an emissive white hair.
        // Same UV extent, birth phase and lifetime envelope as v2; no screen-space size floor.
        float centre = sin(h * 5.2 + phase + progress * 3.0) * 0.30;
        float taper = pow(max(sin(h * 3.14159), 0.0), 0.7);
        float width = 0.055 + 0.22 * taper;
        float signedDistance = x - centre;
        float pixelHalf = max(fwidth(signedDistance) * 0.5, 0.0175);
        float shoulder = HeldStrokeCoverage(signedDistance, width, pixelHalf);
        float ridge = HeldStrokeCoverage(signedDistance + width * 0.20, width * 0.26, pixelHalf);
        float ends = smoothstep(0.0, 0.13, h) * (1.0 - smoothstep(0.82, 1.0, h));
        float variation = 0.85 + 0.15 * (0.5 + 0.5 * cos(phase));
        float body = shoulder * ends * variation;
        float crest = ridge * ends * variation;
        float peak = max(max(color.r, color.g), color.b);
        // Bound the straight colour, not the shared alpha/lifetime. HDR gain in v2 could bleach
        // accumulated parcels against ivory; this shoulder remains chromatic over a bright key.
        float3 hue = pow(saturate(color.rgb / max(peak, 0.0001)), 1.35);
        float3 shoulderColor = hue * min(peak, 0.78);
        float3 ridgeColor = hue * min(peak, 0.95);
        float shoulderAlpha = body * 0.64;
        float ridgeAlpha = crest * 0.18;
        float a = saturate(shoulderAlpha + ridgeAlpha) * color.a;
        float3 rgb = (shoulderColor * shoulderAlpha + ridgeColor * ridgeAlpha) * color.a;
        return float4(rgb, a); // premultiplied, no warm/white core or second fade
    }
    else // fine, partly transparent thread: dense births must not become an opaque AR patch
    {
        float centre = sin(h * 6.0 + phase + progress * 4.0) * 0.34;
        float width = 0.08 + sin(h * 3.14159) * 0.08;
        float signedDistance = x - centre;
        float pixelHalf = max(fwidth(signedDistance) * 0.5, 0.0175);
        float ends = smoothstep(0.0, 0.16, h) * (1.0 - smoothstep(0.8, 1.0, h));
        float variation = 0.78 + 0.22 * (0.5 + 0.5 * cos(phase)); // fixed birth phase, no RNG/time mutation
        coverage = HeldStrokeCoverage(signedDistance, width, pixelHalf) * ends * 0.52 * variation;
        core = coverage * exp(-signedDistance * signedDistance * 90.0) * 0.08;
    }
    float a = saturate(coverage) * color.a;
    float3 warmCore = lerp(color.rgb, float3(1.0, 0.94, 0.70), 0.18);
    float3 rgb = bodyColor * coverage + warmCore * core;
    return float4(rgb * color.a, a);
}

// Review-only sparks: coloured faceted ember + velocity-aligned tapered sliver.
// Shared by Stage and calibrated AR; CPU already owns the lifetime envelope.
// No ScreenTime flicker, whitening core, geometry expansion or extra life fade.
// Physical raw-size coordinates, shared despite old Stage 4.32x / AR 2.4x quad extents.
// Soft pigment mote: no white core, temporal noise, additional fade or added geometry.
// Review-only coloured shard facet. No temporal glint, white core or extra age fade.
// Viscous Melt bead: broad lower shoulder, softly shaded meniscus, same-hue components.
// CPU owns the single lifetime envelope. No temporal noise, white core or geometry growth.
float4 ShadeMeltArt(float2 p,float4 color,float component)
{
    if(component>33.5)
    {
        float coverage=exp(-dot(p,p)*(component<34.5?12.0:4.5));
        return float4(color.rgb*coverage*color.a,coverage*color.a);
    }
    float width=lerp(0.66,0.96,smoothstep(-0.85,0.45,p.y));
    float d=length(float2(p.x/width,(p.y-0.04)/0.94));
    float coverage=saturate(0.5+(1.0-d)/max(fwidth(d),0.001));
    float shoulder=lerp(0.76,0.94,smoothstep(-0.7,0.65,p.y));
    float meniscus=exp(-pow((d-0.76)*9.0,2.0))*0.06;
    float3 pigment=color.rgb*min(1.0,shoulder+meniscus);
    return float4(pigment*coverage*color.a,coverage*color.a);
}

// Rounded directional Bounce stroke: a broad shoulder at the leading end and a quiet tapered tail.
// Same local pigment in both outputs; CPU alpha is the only lifetime fade. No phase noise or white core.
float4 ShadeBounceArt(float2 p,float4 color)
{
    float t=saturate((p.x+1.0)*0.5);
    float width=lerp(0.12,0.62,smoothstep(0.0,0.65,t));
    float d=length(float2(p.x,p.y/max(width,0.01)));
    float coverage=saturate(0.5+(0.96-d)/max(fwidth(d),0.001));
    float pigment=lerp(0.72,0.96,smoothstep(0.0,0.8,t));
    float alpha=coverage*color.a;
    return float4(color.rgb*pigment*alpha,alpha);
}

float4 ShadeShatterArt(float2 p,float4 color)
{
    float t=saturate((p.x+1.0)*0.5);
    float width=lerp(0.12,0.68,t);
    float d=max(abs(p.x)-0.92,abs(p.y)-width);
    float coverage=saturate(0.5-d/max(fwidth(d),0.001));
    float plane=smoothstep(-0.08,0.08,p.y+p.x*0.22);
    float3 tint=color.rgb*lerp(0.78,1.0,plane);
    return float4(tint*coverage*color.a,coverage*color.a);
}

float4 ShadeDustArt(float2 q,float4 color)
{
    float r=length(q);
    float edge=1.0-smoothstep(1.6,2.2,r);
    float coverage=exp(-dot(q,q)*0.9)*edge;
    float alpha=coverage*color.a;
    return float4(color.rgb*alpha,alpha);
}

// Paper coordinates in units of raw particle size. Clipped corners fit inside the narrow AR quad
// at every rotation; shared physical aspect, no new geometry or age/brightness oscillation.
float4 ShadeConfettiArt(float2 q,float4 color)
{
    float2 a=abs(q);
    float d=max(max(a.x-0.94,a.y-0.55),a.x+a.y-1.21);
    float coverage=saturate(0.5-d/max(fwidth(d),0.001));
    float fold=smoothstep(-0.14,0.14,q.x+q.y*0.32);
    float3 tint=color.rgb*lerp(0.80,1.0,fold);
    return float4(tint*coverage*color.a,coverage*color.a);
}

// Tapered multicolour radial spark; shared body for Stage and AR, no age fade or white core.
float4 ShadeFireworkArt(float2 uv,float4 color,bool streak)
{
    float2 p=uv*2.0-1.0;
    if(!streak)
    {
        float d=length(p);
        float coverage=saturate((0.74-d)/max(fwidth(d),0.001)+0.5);
        return float4(color.rgb*coverage*color.a,coverage*color.a);
    }
    float t=saturate((p.x+1.0)*0.5);
    float width=lerp(0.11,0.76,smoothstep(0.0,0.66,t))*sqrt(saturate((1.0-t)/0.24));
    float pixelHalf=max(fwidth(p.y)*0.5,0.0125);
    float body=HeldStrokeCoverage(p.y,width,pixelHalf);
    float facet=HeldStrokeCoverage(p.y+width*0.16,width*0.30,pixelHalf);
    float cap=saturate((1.0-abs(p.x))/max(fwidth(p.x),0.001));
    float coverage=saturate(body*0.82+facet*0.18)*cap;
    return float4(color.rgb*coverage*color.a,coverage*color.a);
}

// Liquid bead, coloured meniscus and restrained tinted glint; no white replacement or age fade.
float4 ShadeSplashArt(float2 uv,float4 color,float component)
{
    float2 p=uv*2.0-1.0;
    if(component<22.5) // Stage's existing broad halo quad: energy stays close to the bead.
    {
        float coverage=exp(-dot(p,p)*18.0)*0.09;
        return float4(color.rgb*coverage*color.a,coverage*color.a);
    }
    if(component>23.5 && component<24.5) // Existing small offset specular quad, never pure white.
    {
        float coverage=exp(-dot(p,p)*5.0)*0.22;
        return float4(color.rgb*coverage*color.a,coverage*color.a);
    }
    // Broad rounded lower bead with a pinched upper shoulder, inside the old ellipse quad.
    float width=lerp(0.55,0.92,smoothstep(-0.75,0.35,p.y));
    float d=length(float2(p.x/width,(p.y-0.08)/0.90));
    float aa=max(fwidth(d)*0.5,0.015);
    float body=saturate((1.0-d)/(2.0*aa)+0.5);
    float rim=HeldStrokeCoverage(d-0.78,0.10,aa);
    float ridge=exp(-dot(p-float2(-0.24,-0.22),p-float2(-0.24,-0.22))*32.0)*body;
    float coverage=saturate(body*0.84+rim*0.12+ridge*0.04);
    return float4(color.rgb*coverage*color.a,coverage*color.a);
}

float4 ShadeSparkArt(float2 uv, float4 color, bool streak)
{
    float2 p = uv * 2.0 - 1.0;
    float body, halo;
    if (streak)
    {
        float along = saturate((p.x + 1.0) * 0.5);
        float width = lerp(0.055, 0.30, smoothstep(0.0, 0.74, along));
        width *= sqrt(saturate((1.0 - along) / 0.16));
        float cap = saturate((1.0 - abs(p.x)) / max(fwidth(p.x), 0.001));
        body = HeldStrokeCoverage(p.y, width, fwidth(p.y) * 0.5) * cap;
        halo = exp(-p.y * p.y * 18.0) * pow(saturate(1.0 - abs(p.x)), 0.8) * 0.12;
    }
    else
    {
        float d = abs(p.x) * 0.9 + abs(p.y) * 1.1;
        body = saturate((0.57 - d) / max(fwidth(d), 0.001) + 0.5);
        halo = exp(-dot(p,p) * 6.0) * 0.18;
    }
    float coverage = saturate(body * 0.85 + halo);
    float peak = max(color.r, max(color.g, color.b));
    float3 tint = max(color.rgb, 0.0) / (1.0 + max(peak, 0.0));
    float alpha = coverage * saturate(color.a);
    return float4(tint * alpha, alpha);
}

// V2 receives one shared intrinsic tint + lifetime alpha from both CPU builders.
// More material coverage inside the SAME quad; no minimum pixel size or projector change.
float4 ShadeSparkArtV2(float2 uv, float4 color, bool streak)
{
    float2 p = uv * 2.0 - 1.0;
    float body, halo;
    if (streak)
    {
        float along = saturate((p.x + 1.0) * 0.5);
        float width = lerp(0.085, 0.55, smoothstep(0.0, 0.74, along));
        width *= sqrt(saturate((1.0 - along) / 0.16));
        float cap = saturate((1.0 - abs(p.x)) / max(fwidth(p.x), 0.001));
        body = HeldStrokeCoverage(p.y, width, fwidth(p.y) * 0.5) * cap;
        halo = exp(-p.y * p.y * 12.0) * pow(saturate(1.0 - abs(p.x)), 0.8) * 0.14;
    }
    else
    {
        float d = abs(p.x) + abs(p.y);
        body = saturate((0.72 - d) / max(fwidth(d), 0.001) + 0.5);
        halo = exp(-dot(p,p) * 5.0) * 0.18;
    }
    float alpha = saturate(body * 0.95 + halo) * saturate(color.a);
    return float4(clamp(color.rgb, 0.0, 0.95) * alpha, alpha);
}

float4 PsArQuad(ArQuadOut i) : SV_Target
{
    float kind = i.Misc.z;
    if(kind>38.5 && kind<39.5) return ShadeBounceArt(i.Uv*2.0-1.0,i.Color);
    if(kind>32.5 && kind<33.5) return ShadeMeltArt(i.Uv*2.0-1.0,i.Color,33);
    if(kind>30.5 && kind<31.5) return ShadeShatterArt(i.Uv*2.0-1.0,i.Color);
    if(kind>28.5 && kind<30.5) return ShadeDustArt((i.Uv*2.0-1.0)*2.4,i.Color);
    if(kind>27.5 && kind<28.5)
    {
        float2 p=(i.Uv*2.0-1.0)*float2(1.6,1.0);
        float2 q=float2(p.x*i.Misc.x+p.y*i.Misc.y,-p.x*i.Misc.y+p.y*i.Misc.x);
        return ShadeConfettiArt(q,i.Color);
    }
    if(kind>25.5 && kind<27.5) return ShadeFireworkArt(i.Uv,i.Color,kind<26.5);
    if (kind > 24.5 && kind < 25.5) return ShadeSplashArt(i.Uv,i.Color,23);
    if (kind > 19.5 && kind < 21.5)
        return ShadeSparkArtV2(i.Uv, i.Color, kind < 20.5);
    if (kind > 17.5 && kind < 19.5)
        return ShadeSparkArt(i.Uv, i.Color, kind < 18.5);
    if (kind > 16.5 && kind < 17.5)
        return ShadeHeldParticleArt(i.Uv, i.Color, i.Misc.x, i.Misc.y, i.Misc.w);
    if (kind < 0.5)
    {
        // Reuse the exact Live Stage material in linear light. UV derivatives recover the projected body's
        // local pixel size, then convert it to scene DIPs so bevel/glow widths match PsNote at any output scale.
        float2 uvPerPixel = max(fwidth(i.Uv), float2(1e-6, 1e-6));
        float2 halfSize = 0.5 / uvPerPixel / max(SceneSize.z, 1e-3);
        NoteMaterialInput note;
        note.Local = (i.Uv - 0.5) * 2.0 * halfSize;
        note.Half = halfSize;
        note.Color = i.Color;
        note.Misc = float4(i.Misc.x, i.Misc.y, 1.0, i.Misc.w);
        note.SceneY = i.Position.y / max(SceneSize.z, 1e-3);
        return ShadeNoteMaterial(note);
    }
    if (kind > 0.5 && kind < 1.5)
    {
        // A lit physical key: its colour replaces part of the camera pixel, rather than adding a virtual key body.
        float ka = i.Color.a;
        return float4(i.Color.rgb * 1.1 * ka, ka);
    }
    if (kind > 1.5 && kind < 2.5)
    {
        // Live Stage particle material. The CPU projects the real simulated particle and selects its family;
        // this shader supplies each family's soft ember, streak, droplet, shard, flame, dust or wisp silhouette.
        float2 p = i.Uv * 2.0 - 1.0;
        float progress = saturate(i.Misc.x);
        float shape = floor(i.Misc.y + 0.5);
        float phase = i.Misc.w;
        float coverage = 0.0;
        float gain = 0.0;
        if (shape < 0.5) // ember / dot
        {
            float r2 = dot(p, p);
            coverage = exp(-r2 * 5.5) + exp(-r2 * 1.8) * 0.24;
            gain = exp(-r2 * 7.0) * 1.5 + exp(-r2 * 2.0) * 0.45;
        }
        else if (shape < 1.5) // velocity-aligned streak
        {
            float edge = exp(-p.y * p.y * 18.0);
            float tip = saturate(1.15 - abs(p.x) * 0.92);
            coverage = edge * tip;
            gain = edge * (0.55 + 1.2 * tip);
        }
        else if (shape < 2.5) // tumbling confetti
        {
            float angle = phase + progress * 7.0;
            float cs = cos(angle), sn = sin(angle);
            float2 q = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
            float diamond = abs(q.x) * 0.78 + abs(q.y) * 0.78;
            coverage = 1.0 - smoothstep(0.78, 1.0, diamond);
            gain = coverage * 1.05;
        }
        else if (shape < 3.5) // slow, soft dust
        {
            float r2 = dot(p, p);
            coverage = exp(-r2 * 2.7) * 0.7;
            gain = exp(-r2 * 2.2) * 0.7;
        }
        else if (shape < 4.5) // rising flame, tapered toward its tip
        {
            float h = i.Uv.y;
            float width = lerp(0.74, 0.08, h);
            float centre = 0.5 + sin(h * 8.0 + phase) * 0.07 * h;
            float across = abs(i.Uv.x - centre);
            float body = 1.0 - smoothstep(width * 0.58, width, across);
            float base = smoothstep(0.0, 0.16, h);
            coverage = body * base * (1.0 - progress * 0.28);
            gain = coverage * (1.35 + 0.55 * (1.0 - h));
        }
        else if (shape < 5.5) // liquid bead with a specular dot
        {
            float r2 = dot(p, p);
            float bead = 1.0 - smoothstep(0.62, 1.0, r2);
            float highlight = exp(-dot(p - float2(-0.27, -0.31), p - float2(-0.27, -0.31)) * 55.0);
            coverage = bead;
            gain = bead * 1.05 + highlight * 0.9;
        }
        else if (shape < 6.5) // narrow glass shard, point leading
        {
            float along = saturate((p.x + 1.0) * 0.5);
            float halfWidth = lerp(0.08, 0.58, along) * (1.0 - 0.32 * along);
            float shard = 1.0 - smoothstep(halfWidth * 0.72, halfWidth, abs(p.y));
            coverage = shard * saturate(1.18 - abs(p.x) * 0.82);
            gain = coverage * 1.65;
        }
        else // wisps: an undulating, tapered thread
        {
            float wave = sin(p.x * 7.0 + phase + progress * 6.0) * 0.32;
            float width = lerp(0.08, 0.28, saturate(1.0 - abs(p.x)));
            float thread = exp(-pow((p.y - wave) / width, 2.0) * 2.4);
            coverage = thread * saturate(1.0 - abs(p.x) * 0.6);
            gain = coverage * (0.75 + 0.6 * sin(progress * 6.283 + phase));
        }
        float a = saturate(coverage) * i.Color.a;
        return float4(i.Color.rgb * gain * i.Color.a, a);
    }
    if (kind > 2.5 && kind < 3.5)
    {
        // 3D-projected key/impact light and spill on the calibrated real key plane.
        float2 p = i.Uv * 2.0 - 1.0;
        float beam = exp(-p.x * p.x * 3.2 - p.y * p.y * 1.45);
        float hot = exp(-dot(p, p) * 4.5);
        float coverage = beam * i.Color.a * 0.8;
        return float4(i.Color.rgb * (beam * 0.72 + hot * 0.9) * i.Color.a, coverage);
    }
    if (kind > 3.5 && kind < 4.5)
    {
        // The ring itself is simulated by Live Stage; these are its actual style, radius and decay, not a generic hit pulse.
        float r = length(i.Uv * 2.0 - 1.0);
        float width = max(i.Misc.y, 0.006);
        float band = exp(-pow((r - i.Misc.x) / width, 2.0));
        float style = floor(i.Misc.w + 0.5);
        float core = exp(-dot(i.Uv * 2.0 - 1.0, i.Uv * 2.0 - 1.0) * 4.0);
        float body = band;
        float gain = 1.0;
        if (style > 0.5 && style < 1.5) { body = saturate(band * 1.45 + core * 0.20); gain = 1.35 + core * 0.6; }
        else if (style > 1.5 && style < 2.5) { body = band * 0.85; gain = 1.15; }
        else if (style > 2.5) { body = band; gain = 1.3; }
        float alpha = saturate(body) * i.Color.a;
        return float4(i.Color.rgb * gain * alpha, alpha);
    }
    if (kind > 4.5 && kind < 5.5)
    {
        // 3D ribbon segments for the configured electric hold arcs and lightning flash.
        float2 p = i.Uv * 2.0 - 1.0;
        float across = exp(-p.y * p.y * (10.0 + i.Misc.y * 18.0));
        float taper = saturate(1.1 - abs(p.x) * 0.85);
        float core = exp(-p.y * p.y * 48.0) * taper;
        float alpha = saturate((across * 0.38 + core * 0.8) * i.Color.a);
        return float4(i.Color.rgb * (across * 0.75 + core * 1.5) * i.Color.a, alpha);
    }
    if (kind > 5.5 && kind < 6.5)
    {
        // Live Stage flash channel: soft flare, plasma caustics or the impact-morph star.
        float2 p = i.Uv * 2.0 - 1.0;
        float r = length(p);
        float style = floor(i.Misc.y + 0.5);
        float progress = saturate(i.Misc.x);
        float coverage, gain;
        if (style > 2.5)
        {
            float angle = atan2(p.y, p.x);
            float radius = lerp(0.34, 0.98, 0.5 + 0.5 * cos(angle * 5.0));
            float star = 1.0 - smoothstep(radius - 0.07, radius + 0.07, r);
            float glow = exp(-r * 2.8) * (1.0 - progress * 0.5);
            coverage = saturate(star + glow * 0.38);
            gain = star * 1.8 + glow * 0.85;
        }
        else if (style > 1.5)
        {
            float swirl = sin(r * 24.0 - progress * 13.0 + atan2(p.y, p.x) * 3.0);
            float shell = exp(-r * r * 2.2);
            float caustic = exp(-swirl * swirl * 5.0) * shell;
            coverage = saturate(shell * 0.35 + caustic * 0.72);
            gain = shell * 0.85 + caustic * 1.75;
        }
        else
        {
            float core = exp(-r * r * 8.0);
            float skirt = exp(-r * r * 2.0) * saturate((1.0 - r) * 2.2);
            coverage = saturate(core + skirt * 0.55);
            gain = core * 1.55 + skirt * 0.75;
        }
        float alpha = coverage * i.Color.a;
        return float4(i.Color.rgb * gain * i.Color.a, alpha);
    }
    if (kind > 6.5 && kind < 7.5)
    {
        // A keyed light beam in the piano's local surface-normal direction, not an uncalibrated screen-space sprite.
        float2 p = i.Uv * 2.0 - 1.0;
        float width = exp(-p.x * p.x * 5.5);
        float taper = saturate(1.0 - abs(p.x) * 0.6);
        float along = pow(saturate(1.0 - i.Uv.y), 0.72);
        float glow = width * taper * along;
        float core = exp(-p.x * p.x * 24.0) * along;
        float a = saturate((glow * 0.45 + core * 0.42) * i.Color.a);
        return float4(i.Color.rgb * (glow * 0.9 + core * 0.85) * i.Color.a, a);
    }
    if (kind > 7.5 && kind < 8.5)
    {
        // The note sheen rides inside the same pitch-tagged, calibrated AR patch (not a flat stage-space sprite).
        float phase = i.Misc.x;
        float strength = saturate(i.Misc.y);
        float sweepA = 0.5 + 0.5 * sin(ScreenTime.z * 0.50 + phase);
        float sweepB = 0.5 + 0.5 * sin(ScreenTime.z * 0.77 + phase + 2.1);
        float2 a = (i.Uv - float2(0.16 + 0.68 * sweepA, 0.5)) * float2(1.0 / 0.13, 1.0 / 0.72);
        float2 b = (i.Uv - float2(0.16 + 0.68 * sweepB, 0.5)) * float2(1.0 / 0.13, 1.0 / 0.72);
        float lobeA = exp(-dot(a, a) * 2.0);
        float lobeB = exp(-dot(b, b) * 2.0);
        float gainA = (0.10 + 0.20 * pow(sin(sweepA * PI), 2.0)) * lobeA;
        float gainB = (0.10 + 0.20 * pow(sin(sweepB * PI), 2.0)) * lobeB * 0.55;
        float gain = (gainA + gainB) * strength;
        float3 tint = lerp(i.Color.rgb, float3(1.3, 1.3, 1.3), 0.72);
        return float4(tint * gain * i.Color.a * 2.2, 0.0);
    }
    if (kind > 8.5 && kind < 9.5)
    {
        // The seven note-trail variants share one calibrated 3D ribbon. UV.y runs from the attached edge
        // (1) to the fading end (0); colour, length and quality gates remain settings-driven on the CPU.
        float style = floor(i.Misc.x + 0.5);
        float seed = i.Misc.w;
        float strength = saturate(i.Misc.y);
        float across = i.Uv.x * 2.0 - 1.0;
        float along = saturate(i.Uv.y);
        float fade = pow(along, 1.2);
        float coverage = 0.0;
        float gain = 1.0;
        float3 tint = i.Color.rgb;
        if (style < 0.5) // Glow
        {
            coverage = exp(-across * across * 2.4) * fade * 0.52;
            gain = 1.3;
        }
        else if (style < 1.5) // Sparkles
        {
            float2 cell = floor(i.Uv * float2(5.0, 13.0));
            float2 jitter = frac(float2(Hash21(cell + seed), Hash21(cell + seed + 7.1)) + 0.17) - 0.5;
            float2 local = frac(i.Uv * float2(5.0, 13.0)) - 0.5 - jitter * 0.54;
            float twinkle = 0.35 + 0.65 * abs(sin(ScreenTime.z * (3.0 + Hash21(cell + seed + 2.7) * 5.0) + seed + cell.y));
            coverage = exp(-dot(local * float2(1.5, 1.0), local * float2(1.5, 1.0)) * 42.0) * fade * twinkle * 0.78;
            gain = 1.7;
        }
        else if (style < 2.5) // Speed Lines
        {
            float lineOffset = abs(frac(i.Uv.x * 6.0 + seed * 0.13) - 0.5);
            float dash = step(0.20, frac(i.Uv.y * 3.0 + seed * 0.07 + ScreenTime.z * 0.11));
            coverage = exp(-lineOffset * lineOffset * 540.0) * dash * fade * 0.52;
            gain = 1.6;
        }
        else if (style < 3.5) // Blur
        {
            coverage = exp(-across * across * 1.65) * fade * 0.22;
            gain = 0.72;
        }
        else if (style < 4.5) // Ribbon
        {
            float centre = sin(i.Uv.y * 6.283 + ScreenTime.z * 3.0 + seed) * 0.32 * (1.0 - along * 0.25);
            coverage = exp(-pow((across - centre) * 3.4, 2.0)) * fade * 0.7;
            gain = 1.55;
        }
        else if (style < 5.5) // Rainbow
        {
            float hue = frac(i.Uv.y * 0.58 + ScreenTime.z * 0.25 + seed * 0.013);
            tint = 0.5 + 0.5 * cos((hue + float3(0.0, 0.333333, 0.666667)) * (2.0 * PI));
            coverage = exp(-across * across * 1.8) * fade * 0.56;
            gain = 1.45;
        }
        else // Stream
        {
            float2 cell = floor(i.Uv * float2(6.0, 16.0));
            float phase = frac(i.Uv.y * 16.0 - ScreenTime.z * 0.55 + Hash21(cell + seed));
            float x = frac(i.Uv.x * 6.0 + Hash21(cell + seed + 3.1)) - 0.5;
            coverage = exp(-x * x * 90.0 - pow((phase - 0.5) * 2.0, 2.0) * 22.0) * fade * 0.75;
            gain = 1.55;
        }
        float alpha = saturate(coverage * strength) * i.Color.a;
        return float4(tint * gain * alpha, alpha);
    }
    if (kind > 9.5 && kind < 10.5)
    {
        // A note-name atlas cell on a calibrated note patch; it never becomes a screen-space sprite over the camera.
        float cell = i.Misc.x;
        float2 cellXY = float2(fmod(cell, 16.0), floor(cell / 16.0));
        float2 atlasUv = (cellXY + i.Uv) / float2(16.0, 24.0);
        float coverage = AtlasTex.SampleLevel(LinearClamp, atlasUv, 0).a;
        float alpha = coverage * i.Color.a;
        return float4(i.Color.rgb * alpha, alpha);
    }
    if (kind > 10.5 && kind < 11.5)
    {
        // The damper strip (roadmap §14.3, the Dampers element): a soft glowing line glued to the key back edge.
        // uv.y runs from the fallboard side (0) to the key edge (1): a hot core hugs the key edge while a broad
        // halo falls away across the strip; the two u ends feather so the line never hard-cuts at the frame.
        float feather = 1.0 - i.Uv.y;
        float core = exp(-feather * feather * 38.0) * 2.1;
        float halo = exp(-feather * 5.0) * 0.55;
        float ends = smoothstep(0.0, 0.004, i.Uv.x) * (1.0 - smoothstep(0.996, 1.0, i.Uv.x));
        float gain = (core + halo) * ends;
        float alpha = saturate(gain * 0.5) * i.Color.a;
        return float4(i.Color.rgb * gain * i.Color.a, alpha);
    }

    // Unknown future quad kinds remain readable; note bodies above use the shared SDF material.
    return float4(i.Color.rgb * i.Color.a, i.Color.a);
}

// ---------------------------------------------------------------------------------------------------
// 2. notes
// ---------------------------------------------------------------------------------------------------

struct NoteOut
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;      // scene DIPs from the note centre
    float2 Half : TEXCOORD1;       // half size in scene DIPs
    float4 Color : TEXCOORD2;
    float4 Misc : TEXCOORD3;
    float SceneY : TEXCOORD4;
};

NoteOut VsNote(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    NoteInstance n = Notes[iid];
    float margin = NoteA.w + NoteA.z + 2.0;
    float2 halfSize = n.Rect.zw * 0.5;
    float2 centre = n.Rect.xy + halfSize;
    float2 corner = QuadCorner(vid);
    float2 local = corner * (halfSize + margin);
    NoteOut o;
    if (ArMatrixFlags.x > 0.5)
    {
        // Dormant legacy scene->image branch. The active AR renderer deliberately keeps ArMatrixFlags zero and
        // emits calibrated 3D notes through GpuArLayer/Ar3DNoteProjection instead; do not use this branch as the
        // runtime note path or as a condition for whether projected notes exist.
        float3 at = ArMapAt(centre + local);
        float3 mid = ArMapAt(centre);
        float w = max(at.z, 1e-3);                   // legacy branch expects CPU-side horizon clipping; the active AR quad path clips at the 3D near plane
        float wm = max(mid.z, 1e-3);
        float scale = max(ArMatrixFlags.y / (wm * sqrt(wm)), ArMatrixFlags.z) / SceneSize.z;
        float2 ndc = at.xy / w / ScreenTime.xy * float2(2.0, -2.0) + float2(-1.0, 1.0);
        o.Position = float4(ndc * w, 0.5 * w, w);
        o.Local = local * scale;
        o.Half = halfSize * scale;
    }
    else
    {
        o.Position = ToClip(SceneToScreen(centre + local), 0.5);
        o.Local = local;
        o.Half = halfSize;
    }
    o.Color = n.Color;
    o.Misc = n.Misc;
    o.SceneY = centre.y + local.y;
    return o;
}

float4 PsNote(NoteOut note) : SV_Target
{
    NoteMaterialInput material;
    material.Local = note.Local;
    material.Half = note.Half;
    material.Color = note.Color;
    material.Misc = note.Misc;
    material.SceneY = note.SceneY;
    return ShadeNoteMaterial(material);
}

float4 ShadeNoteMaterial(NoteMaterialInput v)
{
    float2 p = v.Local;
    float2 hb = max(v.Half, 0.5);
    // Stable NoteStyle IDs: Solid=0, Neon=1, Glass=2, Fire=3, Gem=4, CrystalOutline=5.
    float style = NoteA.x;
    bool facetedMaterial = style > 3.5 && style < 5.5;
    float radius = facetedMaterial ? 0.0 : min(NoteA.y, min(hb.x, hb.y));
    float d = SdRoundBox(p, hb, radius);                 // < 0 inside
    if (facetedMaterial)
    {
        // Gem and Crystal Outline use actual clipped, octagonal corners rather than a rounded box.
        float chamfer = style < 4.5 ? 0.28 : 0.48;
        float2 q = abs(p) / hb;
        float invLen = rsqrt(1.0 / (hb.x * hb.x) + 1.0 / (hb.y * hb.y));
        float cornerD = (q.x + q.y - (2.0 - chamfer)) * invLen;
        d = max(d, cornerD);
    }
    float inside = saturate(0.5 - d * SceneSize.z);
    float outsideD = max(d, 0.0);
    float3 col = v.Color.rgb;
    float opacity = v.Color.a;
    float sounding = v.Misc.x;
    float edgeW = max(NoteA.z, 0.75);
    float glowR = max(NoteA.w, 1.0);

    // Exponential falloff reads as light rather than a flat ring; faceted materials keep a lower halo so their planes survive at 50%.
    float materialHalo = style < 4.5 ? 0.78 : style < 5.5 ? 0.30 : 1.0;
    float halo = exp(-outsideD / (glowR * 0.33)) * saturate(1.0 - outsideD / glowR) * materialHalo;
    float3 emit = col * halo * NoteC.z * (0.55 + 0.75 * sounding) * (1.0 - inside);
    float alpha = 0.0;

    // leading edge (the head that meets the keys)
    float along = saturate(p.y * v.Misc.z / hb.y * 0.5 + 0.5);
    float head = pow(along, 10.0) * NoteB.z;
    float xN = p.x / hb.x;

    bool satin = NoteD.x > 0.5 && (style < 0.5 || (style > 1.5 && style < 2.5));
    if (satin)
    {
        // Pigment-first satin, not another material ID. Local normalised UVs are shared with AR;
        // static per-note phase avoids screen-space swimming and time/seek-dependent flicker.
        float2 uv = p / hb;
        float phase = frac(v.Misc.y * 0.6180339) * 6.2831853;
        float bevel = saturate(-d / max(1.5, hb.x * 0.55));
        float shade = lerp(1.0, 0.72 + 0.28 * bevel, NoteC.y);
        float grain = sin(uv.y * 47.0 + sin(uv.x * 7.0 + phase) * 1.6 + phase);
        // Attenuate grain on short/distant bars before it aliases. No texture sample or extra pass.
        float frequency = length(fwidth(uv * float2(7.0, 47.0)));
        float filtered = 1.0 - smoothstep(0.65, 2.0, frequency);
        float grainDelta = grain * 0.085 * NoteC.x * filtered;
        float sheen = exp(-pow((uv.x + uv.y * 0.16 + 0.32) * 5.5, 2.0)) * NoteB.w;
        float rim = exp(-abs(d + edgeW * 0.4) / max(0.5, edgeW * 0.4));
        float3 pigment = col * (0.62 + 0.38 * NoteB.x) * shade * (1.0 + grainDelta);
        float3 highlight = lerp(col, 1.0, 0.18);
        emit += (pigment + highlight * (sheen * 0.12 + rim * NoteB.y * 0.26)
                 + col * sounding * 0.10) * inside;
        // Filled and readable without bloom, including on bright camera pictures.
        alpha = inside;
    }
    else if (style < 0.5)
    {
        // Solid: a lacquered bar with a rounded bevel, a bright rim and a cool reflection
        float bevel = saturate(-d / max(2.0, min(hb.x, hb.y) * 0.9));
        float light = lerp(1.2, 0.72, xN * 0.5 + 0.5);
        float shade = lerp(1.0, light, NoteC.y) * (0.72 + 0.28 * bevel);
        float3 body = col * (0.45 + 0.75 * NoteB.x) * shade;
        float rim = exp(-abs(d + edgeW * 0.5) / (edgeW * 0.6));
        float spec = pow(saturate(1.0 - abs(xN + 0.55) * 3.0), 3.0) * NoteB.w * NoteC.y;
        emit += (body + lerp(col, 1.0, 0.55) * rim * NoteB.y * 0.9 + spec * 0.8) * inside;
        emit += col * sounding * 0.6 * inside;
        alpha = inside;
    }
    else if (style < 1.5)
    {
        // Neon: a hollow glass tube - white-hot core line, saturated gas glow, faint inner fill. The tube
        // stays a fraction of the bar's half width, so a narrow bar still shows its dark hollow middle.
        float tubeW = min(edgeW, max(hb.x * 0.3, 0.9));
        float ringD = d + tubeW * 0.55;
        float tube = exp(-(ringD * ringD) / (tubeW * tubeW * 0.55));
        float core = exp(-(ringD * ringD) / (tubeW * tubeW * 0.06));
        float fill = inside * (0.03 + 0.12 * NoteB.x) * (0.7 + 0.3 * sounding);
        emit += col * tube * (1.9 + 1.6 * sounding) * NoteB.y;
        emit += lerp(col, 1.0, 0.75) * core * (1.3 + sounding) * NoteB.y;
        emit += col * fill;
        emit += col * exp(-outsideD / (glowR * 0.18)) * 0.9 * NoteC.z * (1.0 - inside);
        alpha = inside * 0.08;
    }
    else if (style < 2.5)
    {
        // Glass: translucent body, fresnel rim, a diagonal specular sweep and a caustic line
        float fres = pow(saturate(1.0 + d / max(3.0, hb.x * 0.7)), 5.0);
        float sweep = exp(-pow((xN * 0.8 + p.y / hb.y * 0.25 + 0.35) * 5.0, 2.0)) * NoteB.w;
        float3 body = col * (0.14 + 0.3 * NoteB.x);
        emit += (body + lerp(col, 1.0, 0.5) * fres * 1.6 * NoteB.y + sweep * 0.9) * inside;
        emit += col * sounding * 0.35 * inside;
        alpha = inside * (0.3 + 0.25 * NoteB.x);
    }
    else if (style < 3.5)
    {
        // Fire / lava: keep the chosen note hue as the pigment of the ember body and crust; cream hot spots
        // and a warm rim preserve the fire read without drowning out PerHand/PerTrack/custom note colors.
        // The moving crack field stays coarse enough to survive on a narrow bar (as in the software stage).
        float2 q = float2(p.x / max(hb.x, 1.0) * 0.7, (v.SceneY + v.Misc.y * 97.0) / (22.0 * SceneB.y));
        q.y -= ScreenTime.z * 0.35;
        float n = Fbm(q * float2(1.6, 1.0));
        float cracks = smoothstep(0.34, 0.5, n) * (0.35 + 0.65 * NoteC.x) + (1.0 - NoteC.x) * 0.65;
        float veins = 1.0 - exp(-pow((n - 0.42) * 14.0, 2.0));
        float heat = saturate(1.0 - (ScreenTime.w - v.SceneY) / max(NoteC.w, 1.0));
        float3 fireTint = saturate(col);
        float3 hot = lerp(float3(1.0, 0.93, 0.72), fireTint, 0.68);
        float3 crust = lerp(float3(0.3, 0.02, 0.008), fireTint * 0.18, 0.68);
        float3 body = lerp(crust, hot, saturate(cracks * veins));
        float3 heatTint = lerp(fireTint, float3(1.0, 0.22, 0.035), 0.42);
        body = lerp(body, heatTint, heat * 0.32);
        float3 warmEdge = lerp(float3(1.0, 0.36, 0.06), fireTint, 0.66);
        float3 warmGlow = lerp(float3(1.0, 0.3, 0.05), fireTint, 0.70);
        float3 warmCore = lerp(float3(1.0, 0.55, 0.2), fireTint, 0.55);
        float rim = exp(-abs(d + edgeW * 0.3) / (edgeW * 0.45));
        // Keep the burn below the bloom threshold (1.8) so both the note tint and the moving ember texture remain legible.
        emit += (body * (0.62 + 0.36 * NoteB.x) + warmEdge * rim * 0.7 * NoteB.y) * inside;
        emit += warmGlow * exp(-outsideD / (glowR * 0.25)) * 0.55 * NoteC.z * (1.0 - inside);
        emit += warmCore * sounding * 0.25 * inside;
        alpha = inside;
    }
    else if (style < 4.5)
    {
        // Gem: a mostly opaque crystal bar with dark edge planes, pale bevel facets and a restrained specular seam.
        float xN = p.x / hb.x;
        float yN = p.y / hb.y;
        float depth = saturate(NoteC.y);
        float sideShade = smoothstep(0.24, 1.0, abs(xN));
        float3 body = col * lerp(1.12, 0.68, sideShade * lerp(0.82, 1.0, depth));
        float3 highlight = lerp(col, 1.0, 0.44 + depth * 0.2);
        float facet = exp(-abs(abs(xN) - 0.55) * 28.0);
        float spine = exp(-abs(xN) * 34.0);
        float bevel = saturate(-d / max(2.0, hb.x * 0.5));
        float rim = exp(-abs(d + edgeW * 0.38) / max(edgeW * 0.55, 0.8));
        float spec = pow(saturate(1.0 - abs(xN + 0.38 + yN * 0.08) * 2.8), 4.0) * NoteB.w;
        emit += (body * (0.9 + 0.1 * bevel) + highlight * (facet * 0.12 + spine * (0.1 + depth * 0.15) + spec * 0.25) * NoteB.y
                 + highlight * rim * 0.24 * NoteB.y) * inside;
        emit += col * sounding * 0.22 * inside;
        alpha = inside * (0.92 + 0.08 * NoteB.x);
    }
    else if (style < 5.5)
    {
        // Crystal Outline: a translucent center with a crisp, luminous angular contour and quiet internal planes.
        float xN = p.x / hb.x;
        float yN = p.y / hb.y;
        // A two-pixel-scale rim (and a softer inner echo) survives a half-size box filter without filling the crystal.
        float rimWidth = max(edgeW * 0.84, 1.6);
        float rim = exp(-abs(d + edgeW * 0.45) / rimWidth) * NoteB.y;
        float innerRim = exp(-abs(d + edgeW * 1.5) / max(edgeW * 0.92, 1.5)) * NoteB.y;
        float spine = exp(-abs(xN) * 34.0);
        float facetA = exp(-abs(xN - yN * 0.12 - 0.5) * 24.0);
        float facetB = exp(-abs(xN + yN * 0.12 + 0.5) * 24.0);
        float3 edgeColor = lerp(col, 1.0, 0.90);
        float3 planeColor = lerp(col, 1.0, 0.72);
        float facet = (spine * 0.18 + facetA * 0.22 + facetB * 0.22) * inside;
        emit += col * inside * (0.04 + 0.04 * NoteB.x);
        emit += edgeColor * (rim * 1.45 + innerRim * 0.42);
        emit += planeColor * facet * 0.68;
        alpha = max(inside * 0.07, rim * 0.72 + facet * 0.19);
    }
    else
    {
        // Unknown future IDs fall back to a readable opaque bar rather than disappearing.
        emit += col * inside;
        alpha = inside;
    }

    emit += lerp(col, 1.0, satin ? 0.18 : 0.65) * head * inside * (satin ? 0.35 : 2.2);

    // Hold Bar: a white-hot outline just outside a sounding bar plus a tinted halo around it
    float holdBar = v.Misc.w;
    if (holdBar > 0.0)
    {
        float rimLine = exp(-pow((d - 1.5) / 1.1, 2.0));
        float haloLine = exp(-pow((d - 3.5) / 2.6, 2.0));
        // tinted by the note so a narrow bar keeps its colour instead of two white lines
        emit += (lerp(col, 1.0, 0.6) * rimLine * 1.1 + col * haloLine * 0.6) * holdBar;
        alpha = max(alpha, rimLine * holdBar * 0.5);
    }
    return float4(emit * opacity, alpha * opacity);
}

// ---------------------------------------------------------------------------------------------------
// 3. keyboard - instanced 3D boxes
// ---------------------------------------------------------------------------------------------------
//
// Key space: x = scene px (identical to the note lanes), z = depth from the fallboard (0) to the front
// edge (Box.z), y = height above the key bed. The camera is an oblique projection that keeps x
// linear (so every key stays under its note lane) plus a small horizontal perspective that opens the
// side walls of the keys away from the centre, like a camera standing in front of the piano.

struct KeyOut
{
    float4 Position : SV_Position;
    float3 Normal : NORMAL0;
    float3 KeyPos : TEXCOORD0;     // x relative to the key centre, y height, z depth
    float4 Emit : TEXCOORD1;
    float4 Misc : TEXCOORD2;
    float3 Size : TEXCOORD3;       // width, length, height
    float Spill : TEXCOORD4;
};

static const uint FaceCorners[30] =
{
    // top (y = 1): corners as (x, z) bits
    0, 1, 2, 2, 1, 3,
    // front (z = 1)
    4, 5, 6, 6, 5, 7,
    // left (x = 0)
    8, 9, 10, 10, 9, 11,
    // right (x = 1)
    12, 13, 14, 14, 13, 15,
    // back (z = 0) - closes the box for the depth test
    16, 17, 18, 18, 17, 19
};

KeyOut VsKey(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    KeyInstance k = Keys[iid];
    uint face = vid / 6;
    uint corner = FaceCorners[vid] - face * 4;
    float u = (float)(corner & 1u);
    float w = (float)((corner >> 1) & 1u);
    float3 unit;
    float3 normal;
    if (face == 0) { unit = float3(u, 1.0, w); normal = float3(0.0, 1.0, 0.0); }
    else if (face == 1) { unit = float3(u, 1.0 - w, 1.0); normal = float3(0.0, 0.0, 1.0); }
    else if (face == 2) { unit = float3(0.0, 1.0 - u, w); normal = float3(-1.0, 0.0, 0.0); }
    else if (face == 3) { unit = float3(1.0, 1.0 - u, w); normal = float3(1.0, 0.0, 0.0); }
    else { unit = float3(u, 1.0 - w, 0.0); normal = float3(0.0, 0.0, -1.0); }

    float width = k.Box.y - k.Box.x;
    float len = k.Box.z;
    float height = k.Box.w;
    float3 pos = float3(k.Box.x + unit.x * width, k.Base.x + unit.y * height, k.Base.z + unit.z * len);

    // the key pivots on its hinge at the fallboard: the front sinks by the press depth
    float press = k.Misc.y;
    float drop = press * KeyC.w * 0.9 * (pos.z / max(k.Base.z + len, 1.0));
    pos.y -= drop;
    float angle = atan2(press * KeyC.w * 0.9, max(len, 1.0));
    float ca = cos(angle);
    float sa = sin(angle);
    normal = float3(normal.x, normal.y * ca - normal.z * sa, normal.y * sa + normal.z * ca);

    // oblique projection: depth runs down the screen, height runs up it
    float hitY = ScreenTime.w;
    float whiteHeight = KeyC.w;
    float2 scene;
    float centre = SceneSize.x * 0.5;
    scene.x = pos.x + (pos.x - centre) / centre * (pos.y - whiteHeight * 0.5) * KeyD.w;
    scene.y = hitY + pos.z - (pos.y - whiteHeight);
    float depth = saturate(0.5 - (pos.y * 0.9 + pos.z * 0.12) / 2000.0);

    KeyOut o;
    o.Position = ToClip(SceneToScreen(scene), depth);
    o.Normal = normal;
    o.KeyPos = float3(pos.x - (k.Box.x + width * 0.5), pos.y - k.Base.x, pos.z);
    o.Emit = k.Emit;
    o.Misc = k.Misc;
    o.Size = float3(width, len, height);
    o.Spill = k.Base.y;
    return o;
}

float DistributionGgx(float nh, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 1e-4);
}

float3 FresnelSchlick(float cosTheta, float3 f0)
{
    return f0 + (1.0 - f0) * pow(1.0 - saturate(cosTheta), 5.0);
}

float3 KeyLight(float3 n, float3 v, float3 l, float3 albedo, float roughness, float3 f0, float3 lightColor)
{
    float3 h = normalize(l + v);
    float nl = saturate(dot(n, l));
    float nv = saturate(dot(n, v)) + 1e-3;
    float nh = saturate(dot(n, h));
    float3 f = FresnelSchlick(saturate(dot(h, v)), f0);
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float g = (nl / (nl * (1.0 - k) + k)) * (nv / (nv * (1.0 - k) + k));
    float3 spec = DistributionGgx(nh, roughness) * g * f / max(4.0 * nl * nv, 1e-3);
    float3 diffuse = (1.0 - f) * albedo / PI;
    return (diffuse + spec) * lightColor * nl * PI;
}

// Specular part only, for keys that scale their gloss by where on the key the pixel is.
float3 KeySpecular(float3 n, float3 v, float3 l, float roughness, float3 f0, float3 lightColor)
{
    float3 h = normalize(l + v);
    float nl = saturate(dot(n, l));
    float nv = saturate(dot(n, v)) + 1e-3;
    float nh = saturate(dot(n, h));
    float3 f = FresnelSchlick(saturate(dot(h, v)), f0);
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float g = (nl / (nl * (1.0 - k) + k)) * (nv / (nv * (1.0 - k) + k));
    return DistributionGgx(nh, roughness) * g * f / max(4.0 * nl * nv, 1e-3) * lightColor * nl * PI;
}

float4 PsKey(KeyOut v) : SV_Target
{
    float kind = v.Misc.x;
    float3 n = normalize(v.Normal);
    float style = KeyB.w;
    float tilt = KeyB.z;
    float3 view = normalize(float3(0.0, 0.75 + tilt, 0.55));

    if (kind > 1.5)
    {
        // felt strip along the fallboard
        float3 felt = v.Emit.rgb * (0.35 + 0.65 * saturate(n.y * 0.8 + 0.2));
        float fuzz = ValueNoise(v.KeyPos.xz * 0.9) * 0.18;
        return float4(felt * (0.85 + fuzz), 1.0);
    }

    bool black = kind > 0.5;
    // Ebony lacquer reflects well under 1% diffusely; its look comes from the specular band below.
    float3 albedo = black ? float3(0.0035, 0.0035, 0.0045) : float3(0.86, 0.84, 0.8);
    float roughness = black ? lerp(0.5, 0.14, KeyA.w) : lerp(0.65, 0.3, KeyA.w);
    float3 f0 = black ? float3(0.05, 0.05, 0.05) : float3(0.035, 0.035, 0.035);
    if (style < 0.5) { roughness = min(1.0, roughness + 0.25); }
    else if (style > 1.5)
    {
        albedo = black ? float3(0.008, 0.01, 0.016) : float3(0.55, 0.62, 0.72);
        roughness = 0.08;
        f0 = float3(0.08, 0.08, 0.09);
    }

    // lights: a soft box above and in front, a fill from the room and a rim from behind the keys
    float3 keyDir = normalize(float3(-0.18, 1.0, 0.62));
    float3 lightColor = float3(1.0, 0.98, 0.95) * (0.35 + 1.25 * KeyA.x);
    float3 color = KeyLight(n, view, keyDir, albedo, roughness, f0, lightColor);
    if (black)
    {
        // A flat top under one directional light would reflect the soft box evenly and turn the ebony
        // grey. Real lacquer shows the box as a band: keep the diffuse, drop the flat specular and add
        // a narrow highlight a little behind the front edge of each black key.
        float3 spec = KeySpecular(n, view, keyDir, roughness, f0, lightColor);
        float along = saturate(v.KeyPos.z / max(v.Size.y, 1.0));
        float band = exp(-pow((along - 0.82) / 0.07, 2.0)) * (n.y > 0.5 ? 1.0 : 0.0);
        color = max(color - spec, 0.0);
        // bounded highlight: a GGX peak on a flat top is tens of times the light, far too much for a band
        color += band * lightColor * (0.018 + 0.05 * KeyA.w);
    }
    color += albedo * (0.16 + 0.12 * n.y) * (0.6 + 0.4 * KeyA.x);
    // the black keys' side walls catch a faint cool edge so their outline still reads against the gaps
    if (black && n.y < 0.5) color += float3(0.004, 0.0045, 0.006);
    float3 rimDir = normalize(float3(0.0, 0.45, -1.0));
    float rim = pow(saturate(1.0 - dot(n, view)), 3.0) * saturate(dot(n, rimDir) * 0.5 + 0.6);
    color += RimColor.rgb * rim * KeyB.x * (black ? 0.9 : 0.45);

    // ambient occlusion: dark where the key disappears under the fallboard and along the gaps
    float3 kp = v.KeyPos;
    float aoBack = 1.0 - exp(-kp.z / (6.0 * SceneB.y)) ;
    float gap = min(v.Size.x * 0.5 - abs(kp.x), 99.0);
    float aoGap = saturate(gap / (1.8 * SceneB.y));
    float ao = lerp(1.0, aoBack * (0.55 + 0.45 * aoGap), KeyA.z);
    if (n.z > 0.5) ao *= lerp(1.0, 0.55 + 0.45 * saturate(kp.y / max(v.Size.z, 1.0)), KeyA.z);
    color *= ao;
    // lid shadow (Keyboard -> Lid shadow): the fallboard's soft shadow across the top of the keys, over the
    // same depth the software stage shades (at most 18 DIPs, 16 percent of the keyboard)
    if (SceneSize.w > 0.5 && n.y > 0.5)
    {
        float lid = max(min(18.0, KeyC.x * 0.16) * SceneB.y, 1.0);
        color *= 1.0 - 0.45 * saturate(1.0 - kp.z / lid);
    }

    // shadows the black keys cast on the white ones
    if (!black && n.y > 0.5)
    {
        float left = kp.x + v.Size.x * 0.5;
        float right = v.Size.x * 0.5 - kp.x;
        float blackLen = KeyC.y;
        float soft = 5.0 * SceneB.y;
        float sl = v.Misc.z > 0.0 ? saturate((v.Misc.z + soft - left) / soft) : 0.0;
        float sr = v.Misc.w > 0.0 ? saturate((v.Misc.w + soft - right) / soft) : 0.0;
        float along = saturate((blackLen + soft * 2.5 - kp.z) / (soft * 2.5));
        color *= 1.0 - KeyA.y * 0.55 * max(sl, sr) * along;
    }

    // emissive: a pressed key glows with its note colour, hottest near the fallboard
    float strength = v.Emit.a;
    float grad = lerp(1.35, 0.55, saturate(kp.z / max(v.Size.y, 1.0)));
    float3 emissive = v.Emit.rgb * strength * KeyB.y * grad * (black ? 1.4 : 1.0);
    // a pressed white key takes the note colour the way the software stage fills it: the lit ivory is
    // tinted (hue kept, shading kept) rather than washed with added light, which tonemaps back to white
    float3 tint = v.Emit.rgb / max(max(v.Emit.r, max(v.Emit.g, v.Emit.b)), 1e-3);
    if (!black) color = lerp(color, color * tint * 0.85, saturate(strength));
    color += emissive * (n.z > 0.5 ? 0.7 : 1.0) * (black ? 1.0 : 0.55);
    // spill: neighbouring keys catch some of the light
    color += v.Emit.rgb * v.Spill * KeyC.z * (black ? 0.25 : 0.5);
    if (style > 1.5) color += v.Emit.rgb * strength * 0.8 + float3(0.02, 0.03, 0.05);
    return float4(color, 1.0);
}

// ---------------------------------------------------------------------------------------------------
// 4. sprites - particles, flares, rings, beams and the hit line (additive)
// ---------------------------------------------------------------------------------------------------

struct SpriteOut
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;     // corner in [-1, 1] along (axis, normal)
    float4 Color : TEXCOORD1;
    float4 Params : TEXCOORD2;    // x = kind, y = age (shapes: Dir.w), z = aspect (shapes: Dir.z), w = scene x
    float4 Extra : TEXCOORD3;     // shapes: xy = half extent in scene px, zw = per-kind values
};

SpriteOut VsSprite(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    SpriteInstance s = Sprites[iid];
    float2 corner = QuadCorner(vid);
    float kind = s.PosSize.w;
    float size = s.PosSize.z;
    float2 axis = s.Dir.xy;
    if (dot(axis, axis) < 1e-4) axis = float2(0.0, -1.0);
    axis = normalize(axis);
    float2 normal = float2(-axis.y, axis.x);
    float2 extent = float2(size, size);
    float2 offset = 0.0;
    float4 extra = 0.0;
    float depth = 0.6;
    if(kind>38.5 && kind<39.5)
    {
        // Exact legacy kind-1 streak axis, stretch and depth (.6), not a billboard or size change.
        extent=float2(size*(1.0+s.Dir.z),size);
    }
    else if(kind>32.5 && kind<38.5)
    {
        // Exact legacy XY extents/rotation/softness; the separate default-off front-layer trial only changes depth.
        if(kind>35.5)depth=0.01;
        extent=float2(size,size*max(s.Dir.z,0.02));
        extra=float4(extent,1.0,s.Dir.w);
    }
    else if(kind>30.5 && kind<31.5)
    {
        // Exact old kind-8 ellipse extents/axis/softness; only surface material changes.
        extent=float2(size,size*max(s.Dir.z,0.02));
        extra=float4(extent,1.0,s.Dir.w);
    }
    else if(kind>31.5 && kind<32.5)
    {
        // Exact old kind-7 line padding, offset, extent and depth.
        float halfLength=length(s.Dir.xy)*0.5;
        float pad=size*3.0+1.0;
        offset=s.Dir.xy*0.5;
        extent=float2(halfLength+pad,pad);
        extra=float4(extent,halfLength,max(size,0.35));
    }
    else if(kind>28.5 && kind<30.5)
    {
        // Exact old dust kind-0 square extent, axis and depth; no geometry enlargement.
    }
    else if(kind>27.5 && kind<28.5)
    {
        // Exact old kind-3 square extent and age/phase axis; material alone supplies the paper shape.
    }
    else if(kind>25.5 && kind<27.5)
    {
        // Exact old dot/streak geometry, not the unrelated auxiliary glass shard primitive.
        if(kind<26.5)extent=float2(size*(1.0+s.Dir.z),size);
    }
    else if(kind>21.5 && kind<24.5)
    {
        // Exact legacy ellipse/glow geometry, including rotation, y/x ratio and softness.
        extent=float2(size,size*max(s.Dir.z,0.02));
        extra=float4(extent,1.0,s.Dir.w);
    }
    else if (kind > 17.5 && kind < 21.5)
    {
        // Exactly the legacy dot/streak extents and axes; only the fragment material changes.
        if (kind < 18.5 || (kind > 19.5 && kind < 20.5)) extent = float2(size * (1.0 + s.Dir.z), size);
    }
    else if (kind > 16.5 && kind < 17.5)
    {
        axis = float2(1.0, 0.0); normal = float2(0.0, -1.0);
        extent = float2(size, s.Dir.x);
        extra = float4(s.Dir.y, s.Dir.z, s.Dir.w, 0.0); // progress, surface shape, birth phase
    }
    else if (kind > 6.5)
    {
        // shapes (kind 7+): Dir.xy is a rotation (the whole segment for lines), Dir.zw per-kind values
        float2 along = s.Dir.xy;
        normal = float2(-axis.y, axis.x);
        if (kind < 7.5)
        {
            // line segment from PosSize.xy along Dir.xy; size = glow half-width
            float halfLength = length(along) * 0.5;
            float pad = size * 3.0 + 1.0;
            offset = along * 0.5;
            extent = float2(halfLength + pad, pad);
            extra = float4(extent, halfLength, max(size, 0.35));
        }
        else if (kind < 10.5)
        {
            // ellipse (8 filled, 9 glow, 10 ring): size = x radius, Dir.z = y/x ratio, Dir.w = softness / ring width
            float margin = kind > 9.5 ? 1.2 : 1.0;
            extent = float2(size, size * max(s.Dir.z, 0.02)) * margin;
            extra = float4(extent, margin, s.Dir.w);
        }
        else
        {
            axis = float2(1.0, 0.0);
            normal = float2(0.0, 1.0);
            if (kind < 11.5) { extent = float2(size * 2.0, size); depth = 0.01; }         // glyph, in front of the keys
            else if (kind < 12.5) extent = float2(size, size * 0.866);                      // Sierpinski triangle
            else if (kind < 13.5) extent = float2(size * 1.4, max(s.Dir.w, 1.0));           // aurora curtain
            else if (kind < 14.5) { extent = float2(size, size * 0.8); depth = 0.01; }      // star
            else if (kind < 15.5) extent = float2(size, max(s.Dir.x, 0.5));                 // gradient rectangle
            else { extent = float2(size * max(s.Dir.z, 0.01), size); depth = 0.01; }        // 16: watermark strip, Dir.z = W/H aspect
            extra = kind > 11.5 && kind < 12.5 ? float4(s.Dir.xyz, 0.0) : float4(extent, 0.0, 0.0);
        }
    }
    else if (kind > 0.5 && kind < 1.5) extent = float2(size * (1.0 + s.Dir.z), size);          // streak
    else if (kind > 4.5 && kind < 5.5) { axis = float2(0.0, -1.0); normal = float2(1.0, 0.0); extent = float2(s.Dir.z * 0.5, size); offset = float2(0.0, -s.Dir.z * 0.5); }   // beam
    else if (kind > 5.5 && kind < 6.5) { axis = float2(1.0, 0.0); normal = float2(0.0, 1.0); extent = float2(size, s.Dir.z); }  // hit line
    else if (kind > 3.5 && kind < 4.5) extent = float2(size * 2.6, size);                   // flare
    float2 scene = s.PosSize.xy + offset + axis * corner.x * extent.x + normal * corner.y * extent.y;
    // flares and the hit line sit in front of the keys; particles fly behind them
    if ((kind > 3.5 && kind < 4.5) || (kind > 5.5 && kind < 6.5)) depth = 0.01;
    SpriteOut o;
    if (ArMatrixFlags.x > 1.5)
    {
        // Reserved for an explicit future sprite-projection option. Ambient particles, petals, meteors and
        // transients stay flat on the camera by default even while the AR notes use perspective.
        // A sprite is culled whole when any of its four corners reaches the horizon. The weight is affine, so its
        // least value over the quad is at a corner; the corners depend only on the instance, so all six of its
        // vertices agree on whether it is drawn at all.
        float2 quadCentre = s.PosSize.xy + offset;
        float2 reachX = axis * extent.x, reachY = normal * extent.y;
        float weightMin = min(min(ArMapAt(quadCentre + reachX + reachY).z, ArMapAt(quadCentre + reachX - reachY).z),
                              min(ArMapAt(quadCentre - reachX + reachY).z, ArMapAt(quadCentre - reachX - reachY).z));
        if (weightMin < ArHorizonW)
        {
            o.Position = float4(2.0, 2.0, 0.0, 1.0);             // outside the clip volume: nothing is rasterised
        }
        else
        {
            float3 at = ArMapAt(scene);
            float w = at.z;
            float2 ndc = at.xy / w / ScreenTime.xy * float2(2.0, -2.0) + float2(-1.0, 1.0);
            o.Position = float4(ndc * w, depth * w, w);
        }
    }
    else
    {
        o.Position = ToClip(SceneToScreen(scene), depth);
    }
    o.Local = corner;
    o.Color = s.Color;
    o.Params = kind > 6.5 ? float4(kind, s.Dir.w, s.Dir.z, scene.x) : float4(kind, s.Dir.w, extent.x / max(extent.y, 1e-3), scene.x);
    o.Extra = extra;
    return o;
}

float3 HitLineColor(float x)
{
    // the pitch under this x (the keyboard is close to linear in pitch at this scale)
    float t = saturate(x / SceneSize.x);
    float pitch = KeyRange.x + t * KeyRange.y;
    int p0 = (int)clamp(floor(pitch), 0.0, 127.0);
    float3 base = KeyColors[p0].rgb;
    float activity = 0.0;
    float3 lit = 0.0;
    [unroll] for (int k = -3; k <= 3; k++)
    {
        int q = clamp(p0 + k, 0, 127);
        float4 kc = KeyColors[q];
        float w = exp(-abs((float)k) * 0.6);
        activity += kc.a * w;
        lit += kc.rgb * kc.a * w;
    }
    return base * 0.55 + lit * 1.6;
}

// Shapes shared by the ambient layers, the hold/release/morph effects, note trails and the key labels.
float4 SpriteShape(SpriteOut v)
{
    float kind = v.Params.x;
    float3 c = v.Color.rgb;
    float a = v.Color.a;
    if (kind < 7.5)
    {
        // glowing line: a coloured glow around a hot core (bolts, arcs, lasers, lanes, speed lines, ribbons)
        float2 px = v.Local * v.Extra.xy;
        float dx = max(abs(px.x) - v.Extra.z, 0.0);
        float d = length(float2(dx, px.y)) / v.Extra.w;
        float glow = exp(-d * d * 0.7);
        float core = exp(-d * d * 6.0);
        return float4((c * glow + lerp(c, 1.0, 0.8) * core * v.Params.z * 1.5) * a, 0.0);
    }
    if (kind < 10.5)
    {
        float r = length(v.Local) * v.Extra.z;
        if (kind > 9.5)
        {
            // flat ring lying on the key plane: the impact waves and implosions
            float w = max(v.Params.y, 0.01);
            float band = exp(-pow((r - 1.0) / w, 2.0));
            return float4(c * a * band, 0.0);
        }
        float soft = exp(-r * r * 3.0) * saturate((1.0 - r) * 3.0);
        if (kind > 8.5) return float4(c * a * soft, 0.0);                     // additive glow
        float aa = max(fwidth(r), 1e-3);
        float crisp = saturate((1.0 - r) / (aa * 1.5));
        float m = lerp(crisp, soft, saturate(v.Params.y));
        return float4(c * a * m, a * m);                                      // solid: petals, leaves, wings, smoke
    }
    if (kind < 11.5)
    {
        // glyph from the atlas (key labels, matrix rain)
        float cell = v.Params.z;
        float2 cellXY = float2(fmod(cell, 16.0), floor(cell / 16.0));
        float2 uv = (cellXY + v.Local * 0.5 + 0.5) / float2(16.0, 24.0);
        float t = AtlasTex.SampleLevel(LinearClamp, uv, 0).a;
        return float4(c * a * t, a * t);
    }
    if (kind < 12.5)
    {
        // Sierpinski triangle, four levels, apex up; colour runs from Color (apex) to Extra.rgb (base)
        float u = v.Local.y * 0.5 + 0.5;
        if (abs(v.Local.x) > u) return 0.0;
        float2 st = float2(v.Local.x + u, u - v.Local.x) * 0.5 * 16.0;
        int2 ij = (int2)floor(st);
        float2 f = frac(st);
        bool present = f.x + f.y < 1.0 && (ij.x & ij.y) == 0 && ij.x + ij.y < 16;
        if (!present) return 0.0;
        float3 col = lerp(c, v.Extra.rgb, u);
        return float4(col * a, a);
    }
    if (kind < 13.5)
    {
        // aurora curtain: a wavy vertical band, brightest low down, with faint vertical rays
        float f = v.Local.y * 0.5 + 0.5;
        float phase = v.Params.z;
        float wobble = 0.257 * f;
        float left = -0.714 + sin(f * 5.0 + phase) * wobble;
        float right = 0.714 + sin(f * 5.0 + phase + 0.8) * wobble;
        float x = v.Local.x;
        float m = saturate((x - left) / 0.1) * saturate((right - x) / 0.1);
        float g = f < 0.55 ? f / 0.55 * 0.43 : (f < 0.85 ? lerp(0.43, 0.59, (f - 0.55) / 0.3) : lerp(0.59, 0.0, (f - 0.85) / 0.15));
        float rays = 0.75 + 0.25 * sin(x * 38.0 + phase * 3.0);
        return float4(c * a * g * m * rays, 0.0);
    }
    if (kind < 14.5)
    {
        // five-pointed star with a white edge (the Morph impact)
        float2 q = v.Local;
        float seg = 6.2831853 / 5.0;
        float ang = atan2(q.x, -q.y);
        float fold = abs(fmod(ang + 12.5663706 + seg * 0.5, seg) - seg * 0.5);
        float r = length(q);
        float2 qp = r * float2(sin(fold), cos(fold));
        float2 po = float2(0.0, 1.0);
        float2 pin = 0.45 * float2(sin(seg * 0.5), cos(seg * 0.5));
        float2 e = pin - po;
        float d = (e.x * (qp.y - po.y) - e.y * (qp.x - po.x)) / length(e);
        float w = max(fwidth(d), 1e-3);
        float fill = saturate(-d / w);
        float edge = exp(-pow(d / (w * 1.6), 2.0));
        return float4((c * fill + edge * 1.2) * a, a * max(fill, edge));
    }
    if (kind < 15.5)
    {
        // rectangle with a vertical alpha gradient: Params.z at the top, Params.y at the bottom
        float2 px2 = v.Local * v.Extra.xy;
        float2 inset = v.Extra.xy - abs(px2);
        float mask = saturate(inset.x + 0.5) * saturate(inset.y + 0.5);
        float grad = lerp(v.Params.z, v.Params.y, v.Local.y * 0.5 + 0.5);
        return float4(c * a * grad * mask, 0.0);
    }
    // 16: the watermark strip — the whole line of text lives in MarkTex; the instance colour tints it.
    float2 markUv = v.Local * 0.5 + 0.5;
    float mark = MarkTex.SampleLevel(LinearClamp, markUv, 0).a;
    return float4(c * a * mark, a * mark);
}

float4 PsSprite(SpriteOut v) : SV_Target
{
    float kind = v.Params.x;
    if(kind>38.5 && kind<39.5) return ShadeBounceArt(v.Local,v.Color);
    if(kind>32.5 && kind<38.5) return ShadeMeltArt(v.Local,v.Color,kind>35.5?kind-3:kind);
    if(kind>30.5 && kind<31.5) return ShadeShatterArt(v.Local,v.Color);
    if(kind>31.5 && kind<32.5)
    {
        float2 px=v.Local*v.Extra.xy;
        float d=length(float2(max(abs(px.x)-v.Extra.z,0.0),px.y))/v.Extra.w;
        float coverage=exp(-d*d*0.7);
        return float4(v.Color.rgb*coverage*v.Color.a,coverage*v.Color.a);
    }
    if(kind>28.5 && kind<30.5) return ShadeDustArt(v.Local*4.32,v.Color);
    if(kind>27.5 && kind<28.5) return ShadeConfettiArt(v.Local*1.6,v.Color);
    if(kind>25.5 && kind<27.5) return ShadeFireworkArt(v.Local*0.5+0.5,v.Color,kind<26.5);
    if(kind>21.5 && kind<24.5) return ShadeSplashArt(v.Local*0.5+0.5,v.Color,kind);
    if (kind > 19.5 && kind < 21.5)
        return ShadeSparkArtV2(v.Local * 0.5 + 0.5, v.Color, kind < 20.5);
    if (kind > 17.5 && kind < 19.5)
        return ShadeSparkArt(v.Local * 0.5 + 0.5, v.Color, kind < 18.5);
    if (kind > 16.5 && kind < 17.5)
        return ShadeHeldParticleArt(v.Local * 0.5 + 0.5, v.Color, v.Extra.x, v.Extra.y, v.Extra.z);
    if (kind > 6.5) return SpriteShape(v);
    float2 p = v.Local;
    float3 c = v.Color.rgb;
    float a = v.Color.a;
    float intensity = 0.0;

    if (kind < 0.5)
    {
        // soft glowing dot with a white-hot core
        float r = length(p);
        float glow = exp(-r * r * 4.0);
        float core = exp(-r * r * 26.0);
        return float4((c * glow + lerp(c, 1.0, 0.7) * core * 1.4) * a * saturate(1.0 - r), 0.0);
    }
    if (kind < 1.5)
    {
        // streak: a needle of light along its velocity
        float along = 1.0 - abs(p.x);
        float across = exp(-p.y * p.y * 7.0);
        intensity = pow(saturate(along), 0.8) * across;
        float hot = exp(-p.y * p.y * 40.0) * saturate(along * 1.5 - 0.3);
        return float4((c * intensity + lerp(c, 1.0, 0.8) * hot) * a, 0.0);
    }
    if (kind < 2.5)
    {
        // expanding ring
        float r = length(p);
        float width = 0.07 + 0.08 * (1.0 - v.Params.y);
        float ring = exp(-pow((r - 0.86) / width, 2.0));
        float inner = exp(-r * r * 3.0) * 0.15;
        return float4(c * (ring + inner) * a * saturate(1.0 - r * 0.9 + 0.1), 0.0);
    }
    if (kind < 3.5)
    {
        // confetti: a small bright square
        float2 q = abs(p);
        float body = saturate((0.72 - max(q.x, q.y)) * 8.0);
        return float4(c * body * a * 1.2, body * a * 0.6);
    }
    if (kind < 4.5)
    {
        // impact flare: bright round core plus an anamorphic horizontal streak
        float2 q = float2(p.x * v.Params.z, p.y);
        float r = length(q);
        float core = exp(-r * r * 5.0);
        float hot = exp(-r * r * 30.0);
        float streak = exp(-p.y * p.y * 90.0) * pow(saturate(1.0 - abs(p.x)), 2.0);
        return float4((c * (core * 1.1 + streak * 0.8) + lerp(c, 1.0, 0.85) * hot * 2.2) * a, 0.0);
    }
    if (kind < 5.5)
    {
        // light beam rising from a key: bright at the base, fading upward, soft edges
        float across = exp(-p.y * p.y * 3.5);
        float up = p.x * 0.5 + 0.5;          // 0 at the key, 1 at the top
        float fade = pow(saturate(1.0 - up), 1.6);
        return float4(c * across * fade * a, 0.0);
    }
    // The hit line keeps its note-aware filament, with six GPU-animated variants layered on top.
    float3 tint = lerp(c, HitLineColor(v.Params.w), 0.75);
    float core = exp(-p.y * p.y * 60.0);
    float glow = exp(-p.y * p.y * 5.0) * 0.45;
    float3 result = tint * (core * 1.6 + glow) * a + core * 0.25 * a;
    if (HitFx.x > 0.5 && HitFx.y > 0.001)
    {
        float q = saturate(v.Params.w / max(SceneSize.x, 1.0));
        float speed = max(HitFx.z, 0.05);
        float time = ScreenTime.z * speed;
        float gain = min(HitFx.y, 2.5);
        float3 accent = tint;
        float energy = 0.0;
        if (HitFx.x < 1.5) // breathing pulse: note energy brightens the line without strobing
        {
            float breath = 0.5 + 0.5 * sin(time * 1.7);
            energy = 0.22 + breath * 0.48 + HitFx.w * 0.22;
            accent = lerp(tint, 1.0, 0.18 + breath * 0.22);
        }
        else if (HitFx.x < 2.5) // a single travelling scan with a soft, short tail
        {
            float head = frac(time * 0.16);
            float d = q - head;
            float beam = exp(-d * d * 190.0);
            float tail = exp(-max(-d, 0.0) * 12.0) * step(0.0, -d);
            energy = beam * 1.15 + tail * 0.26;
            accent = lerp(tint, 1.0, beam * 0.72);
        }
        else if (HitFx.x < 3.5) // twin comets orbit in opposite directions
        {
            float headA = frac(time * 0.13);
            float headB = frac(0.5 - time * 0.13);
            float dA = q - headA;
            float dB = q - headB;
            float cometA = exp(-dA * dA * 240.0) + exp(-max(-dA, 0.0) * 15.0) * step(0.0, -dA) * 0.2;
            float cometB = exp(-dB * dB * 240.0) + exp(-max(dB, 0.0) * 15.0) * step(0.0, dB) * 0.2;
            energy = (cometA + cometB) * 0.72;
            accent = lerp(float3(0.12, 0.62, 1.1), float3(1.1, 0.22, 0.62), saturate(cometB / max(cometA + cometB, 1e-3)));
        }
        else if (HitFx.x < 4.5) // note-coloured spectrum drifting along the full line
        {
            float hue = q + time * 0.035;
            float3 spectral = SpectrumRgb(hue);
            energy = 0.2 + 0.32 * (0.5 + 0.5 * sin(q * 37.0 - time * 2.0));
            accent = lerp(tint, spectral, 0.82);
        }
        else if (HitFx.x < 5.5) // branching electric filament, jittering at musical speed
        {
            float jag = sin(q * 41.0 + time * 7.0) * 0.16 + sin(q * 97.0 - time * 11.0) * 0.075;
            float fork = sin(q * 17.0 - time * 3.0) * 0.08;
            float arc = exp(-pow((p.y - jag - fork) / 0.21, 2.0));
            float branch = exp(-pow((p.y + jag * 0.55 + fork - 0.22) / 0.14, 2.0)) * (0.35 + 0.65 * abs(sin(q * 19.0 + time * 4.0)));
            energy = (arc + branch * 0.42) * (0.78 + 0.22 * sin(time * 8.0 + q * 23.0));
            accent = lerp(tint, float3(0.48, 0.8, 1.3), 0.54);
        }
        else // concentric-looking ripples race outward from the centre of the keyboard
        {
            float distance = abs(q - 0.5);
            float phase = frac(time * 0.18 - distance * 1.45);
            float wave = exp(-pow((phase - 0.36) / 0.09, 2.0));
            float envelope = exp(-distance * 1.25);
            energy = wave * envelope * 1.25;
            accent = lerp(tint, float3(0.52, 0.82, 1.2), 0.38 + 0.24 * wave);
        }
        result += accent * (energy * gain) * (core * 1.5 + glow * 0.7) * a;
    }
    return float4(result, 0.0);
}

// ---------------------------------------------------------------------------------------------------
// 5. bloom
// ---------------------------------------------------------------------------------------------------

float3 Downsample13(float2 uv)
{
    float2 t = PassA.xy;
    float3 a = SourceTex.SampleLevel(LinearClamp, uv + t * float2(-2, -2), 0).rgb;
    float3 b = SourceTex.SampleLevel(LinearClamp, uv + t * float2(0, -2), 0).rgb;
    float3 c = SourceTex.SampleLevel(LinearClamp, uv + t * float2(2, -2), 0).rgb;
    float3 d = SourceTex.SampleLevel(LinearClamp, uv + t * float2(-2, 0), 0).rgb;
    float3 e = SourceTex.SampleLevel(LinearClamp, uv, 0).rgb;
    float3 f = SourceTex.SampleLevel(LinearClamp, uv + t * float2(2, 0), 0).rgb;
    float3 g = SourceTex.SampleLevel(LinearClamp, uv + t * float2(-2, 2), 0).rgb;
    float3 h = SourceTex.SampleLevel(LinearClamp, uv + t * float2(0, 2), 0).rgb;
    float3 i = SourceTex.SampleLevel(LinearClamp, uv + t * float2(2, 2), 0).rgb;
    float3 j = SourceTex.SampleLevel(LinearClamp, uv + t * float2(-1, -1), 0).rgb;
    float3 k = SourceTex.SampleLevel(LinearClamp, uv + t * float2(1, -1), 0).rgb;
    float3 l = SourceTex.SampleLevel(LinearClamp, uv + t * float2(-1, 1), 0).rgb;
    float3 m = SourceTex.SampleLevel(LinearClamp, uv + t * float2(1, 1), 0).rgb;
    float3 result = e * 0.125;
    result += (a + c + g + i) * 0.03125;
    result += (b + d + f + h) * 0.0625;
    result += (j + k + l + m) * 0.125;
    return result;
}

float4 PsBloomPrefilter(FullscreenOut i) : SV_Target
{
    float3 c = Downsample13(i.Uv);
    // soft-knee threshold: keeps the transition into bloom smooth instead of a hard cut
    float threshold = Post2.z;
    float knee = max(threshold * PassA.z, 1e-4);
    float brightness = max(c.r, max(c.g, c.b));
    float soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee);
    float contribution = max(soft, brightness - threshold) / max(brightness, 1e-4);
    // clamp fireflies so a single hot pixel cannot blow up the whole pyramid
    float3 result = c * contribution;
    result = min(result, 60.0);
    return float4(result, 1.0);
}

float4 PsBloomDown(FullscreenOut i) : SV_Target
{
    return float4(Downsample13(i.Uv), 1.0);
}

float4 PsBloomUp(FullscreenOut i) : SV_Target
{
    // 9-tap tent filter; blended additively onto the next larger level
    float2 t = PassA.xy * PassA.z;
    float3 s = SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(-1, -1), 0).rgb;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(0, -1), 0).rgb * 2.0;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(1, -1), 0).rgb;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(-1, 0), 0).rgb * 2.0;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv, 0).rgb * 4.0;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(1, 0), 0).rgb * 2.0;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(-1, 1), 0).rgb;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(0, 1), 0).rgb * 2.0;
    s += SourceTex.SampleLevel(LinearClamp, i.Uv + t * float2(1, 1), 0).rgb;
    return float4(s / 16.0 * PassA.w, 0.0);
}

// ---------------------------------------------------------------------------------------------------
// 6. composite
// ---------------------------------------------------------------------------------------------------

float3 AcesFitted(float3 color)
{
    // Stephen Hill's fit of the ACES RRT + ODT
    const float3x3 inputMatrix = float3x3(0.59719, 0.35458, 0.04823, 0.07600, 0.90834, 0.01566, 0.02840, 0.13383, 0.83777);
    const float3x3 outputMatrix = float3x3(1.60475, -0.53108, -0.07367, -0.10208, 1.10813, -0.00605, -0.00327, -0.07276, 1.07602);
    color = mul(inputMatrix, color);
    float3 a = color * (color + 0.0245786) - 0.000090537;
    float3 b = color * (0.983729 * color + 0.4329510) + 0.238081;
    color = a / b;
    return saturate(mul(outputMatrix, color));
}

float3 LinearToSrgb(float3 c)
{
    c = saturate(c);
    float3 low = c * 12.92;
    float3 high = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return lerp(high, low, step(c, 0.0031308));
}

float3 GradeCompositeColor(float3 linearColor, FullscreenOut i)
{
    float3 c = linearColor * Post.x;
    if (Post.y > 0.5) c = AcesFitted(c * 1.25);
    else c = c / (1.0 + max(Luma(c) - 1.0, 0.0));
    float l = Luma(c);
    c = lerp(l.xxx, c, Post.z);
    c = saturate((c - 0.5) * Post.w + 0.5);
    if (Background.w < 0.5)
    {
        float2 q = (i.Uv - 0.5) * float2(1.15, 1.0);
        c *= 1.0 - Post2.x * smoothstep(0.35, 1.05, length(q) * 1.4);
    }
    float3 display = LinearToSrgb(c);
    float noise = Hash21(i.Position.xy + frac(ScreenTime.z) * 61.0) - 0.5;
    return display + noise * Post2.w;
}

float4 PsComposite(FullscreenOut i) : SV_Target
{
    float4 scene = SourceTex.SampleLevel(PointClamp, i.Uv, 0);
    float3 bloom = SecondTex.SampleLevel(LinearClamp, i.Uv, 0).rgb * Post2.y;
    float3 linearColor = scene.rgb + bloom;
    float alphaOutputCoverage = 0.0;
    if (ArFlags.w > 0.5)
    {
        // The HDR scene is accumulated premultiplied. Unassociate before nonlinear tone mapping, then
        // premultiply again in display space for WPF/DWM's Pbgra32 contract.
        if (ArFlags.y < 0.5) return float4(0.0, 0.0, 0.0, 0.0);
        float energy = max(Luma(max(linearColor, 0.0)), 0.0);
        float lightCoverage = 1.0 - exp(-energy * 0.42);
        alphaOutputCoverage = saturate(max(scene.a, lightCoverage));
        linearColor /= max(alphaOutputCoverage, 1e-4);
    }
    float3 display = GradeCompositeColor(linearColor, i);
    if (ArFlags.w > 0.5)
    {
        // Stream/OBS output: never mix in the camera, stage background or chroma fill. Additive light/bloom
        // contributes soft coverage; RGB is premultiplied only after tone mapping the unassociated color.
        return float4(saturate(display) * alphaOutputCoverage, alphaOutputCoverage);
    }
    if (ArFlags.y > 0.5)
    {
        // the camera picture sits under the light: what the stage draws solid hides it, light adds to it
        float2 uv = float2((i.Position.x - ArPicture.x) / ArPicture.z, (i.Position.y - ArPicture.y) / ArPicture.w);
        if (ArFlags.x > 0.5) uv.x = 1.0 - uv.x;
        float inside = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
        float3 cam = CameraTex.SampleLevel(LinearClamp, uv, 0).rgb * (inside * ArFlags.z);
        display += cam * (1.0 - saturate(scene.a));
    }
    if (Background.w > 0.5)
    {
        // chroma key: solid content and the light around it cover the green; empty space shows it
        float coverage = saturate(scene.a * 1.5 + Luma(scene.rgb + bloom) * 1.5);
        display = lerp(float3(0.0, 1.0, 0.0), display, coverage);
    }
    return float4(display, 1.0);
}

// Geometry is the tone-mapped direct scene (including emissive cores) without the bloom pyramid. Glow
// contains only the soft bloom contribution: low-energy additive pixels stay visible in Geometry instead of
// disappearing when bloom is disabled, while the broad halo can be blended independently in OBS.
float4 PsCompositeGeometry(FullscreenOut i) : SV_Target
{
    if (ArFlags.w < 0.5 || ArFlags.y < 0.5) return float4(0.0, 0.0, 0.0, 0.0);
    float4 scene = SourceTex.SampleLevel(PointClamp, i.Uv, 0);
    float directEnergy = max(Luma(max(scene.rgb, 0.0)), 0.0);
    float directLightCoverage = 1.0 - exp(-directEnergy * 0.42);
    float coverage = saturate(max(scene.a, directLightCoverage));
    if (coverage <= 1e-4) return float4(0.0, 0.0, 0.0, 0.0);
    float3 linearColor = scene.rgb / max(coverage, 1e-4);
    float3 display = GradeCompositeColor(linearColor, i);
    return float4(saturate(display) * coverage, coverage);
}

float4 PsCompositeGlow(FullscreenOut i) : SV_Target
{
    if (ArFlags.w < 0.5 || ArFlags.y < 0.5) return float4(0.0, 0.0, 0.0, 0.0);
    float3 linearColor = max(SecondTex.SampleLevel(LinearClamp, i.Uv, 0).rgb * Post2.y, 0.0);
    float energy = max(Luma(linearColor), 0.0);
    float coverage = saturate(1.0 - exp(-energy * 0.42));
    if (coverage <= 1e-4) return float4(0.0, 0.0, 0.0, 0.0);
    float3 display = GradeCompositeColor(linearColor / max(coverage, 1e-4), i);
    return float4(saturate(display) * coverage, coverage);
}
