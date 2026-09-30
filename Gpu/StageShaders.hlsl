// Keyflow GPU stage - Direct3D 11 shaders (shader model 5.0).
//
// One file, many entry points; Gpu/GpuStageRenderer.cs compiles every entry point once at start-up
// with D3DCompile (vs_5_0 / ps_5_0). The pipeline is:
//
//   1. VsFullscreen + PsBackground   - background colour/image, aura, stars, horizon glow, key bed
//   2. VsNote       + PsNote         - instanced SDF note capsules: Solid, Neon, Glass, Fire (lava)
//   3. VsKey        + PsKey          - instanced 3D key boxes, GGX + Lambert, rim, emissive, AO, shadows
//   4. VsSprite     + PsSprite       - instanced additive particles, rings, flares, beams, hit line
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
    float4 SceneSize;    // x,y = scene size (stage DIPs), z = output pixels per scene unit, w = unused
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
    float4 RimColor;     // rgb = rim light colour, w = unused
    float4 Post;         // x = exposure, y = filmic (1/0), z = saturation, w = contrast
    float4 Post2;        // x = vignette, y = bloom intensity, z = bloom threshold, w = dither amplitude
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

Texture2D SourceTex : register(t0);
Texture2D SecondTex : register(t1);
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
        c += Stars(p);
        // horizon: light rising off the keyboard, coloured by the keys that sound
        float rise = saturate(1.0 - (hitY - p.y) / (SceneSize.y * 0.42));
        c += Horizon.rgb * Horizon.w * rise * rise * rise;
    }
    else
    {
        // key bed: the dark case the keys sit in
        c = lerp(c, float3(0.004, 0.004, 0.006), SceneB.w);
    }
    return float4(c, 0.0);
}

// ---------------------------------------------------------------------------------------------------
// 2. notes
// ---------------------------------------------------------------------------------------------------

struct NoteOut
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;      // px from the note centre
    float2 Half : TEXCOORD1;       // half size px
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
    o.Position = ToClip(SceneToScreen(centre + local), 0.5);
    o.Local = local;
    o.Half = halfSize;
    o.Color = n.Color;
    o.Misc = n.Misc;
    o.SceneY = centre.y + local.y;
    return o;
}

float4 PsNote(NoteOut v) : SV_Target
{
    float2 p = v.Local;
    float2 hb = max(v.Half, 0.5);
    float radius = min(NoteA.y, min(hb.x, hb.y));
    float d = SdRoundBox(p, hb, radius);                 // < 0 inside
    float inside = saturate(0.5 - d * SceneSize.z);
    float outsideD = max(d, 0.0);
    float style = NoteA.x;
    float3 col = v.Color.rgb;
    float opacity = v.Color.a;
    float sounding = v.Misc.x;
    float edgeW = max(NoteA.z, 0.75);
    float glowR = max(NoteA.w, 1.0);

    // outer halo shared by every style: exponential falloff, so it reads as light not as a flat ring
    float halo = exp(-outsideD / (glowR * 0.33)) * saturate(1.0 - outsideD / glowR);
    float3 emit = col * halo * NoteC.z * (0.55 + 0.75 * sounding) * (1.0 - inside);
    float alpha = 0.0;

    // leading edge (the head that meets the keys)
    float along = saturate(p.y * v.Misc.z / hb.y * 0.5 + 0.5);
    float head = pow(along, 10.0) * NoteB.z;
    float xN = p.x / hb.x;

    if (style < 0.5)
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
        // Neon: a hollow glass tube - white-hot core line, saturated gas glow, faint inner fill
        float ringD = d + edgeW * 0.55;
        float tube = exp(-(ringD * ringD) / (edgeW * edgeW * 0.55));
        float core = exp(-(ringD * ringD) / (edgeW * edgeW * 0.06));
        float fill = inside * (0.05 + 0.18 * NoteB.x) * (0.7 + 0.3 * sounding);
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
    else
    {
        // Fire / lava: a cream-hot body cut by dark crusted cracks that crawl over it, glowing orange at
        // the rim and turning red-hot as it nears the keys
        float2 q = float2(p.x / max(hb.x, 1.0) * 1.3, (v.SceneY + v.Misc.y * 97.0) / (16.0 * SceneB.y));
        q.y -= ScreenTime.z * 0.35;
        float n = Fbm(q * float2(1.6, 1.0));
        float cracks = smoothstep(0.34, 0.5, n) * (0.35 + 0.65 * NoteC.x) + (1.0 - NoteC.x) * 0.65;
        float veins = 1.0 - exp(-pow((n - 0.42) * 14.0, 2.0));
        float heat = saturate(1.0 - (ScreenTime.w - v.SceneY) / max(NoteC.w, 1.0));
        float3 hot = lerp(float3(1.0, 0.93, 0.72), col, 0.22);
        float3 crust = float3(0.42, 0.04, 0.015);
        float3 body = lerp(crust, hot, saturate(cracks * veins));
        body = lerp(body, body * float3(1.0, 0.38, 0.2) + float3(0.25, 0.0, 0.0), heat * 0.75);
        float rim = exp(-abs(d + edgeW * 0.4) / (edgeW * 0.7));
        emit += (body * (1.1 + 0.6 * NoteB.x) + float3(1.0, 0.36, 0.06) * rim * 1.8 * NoteB.y) * inside;
        emit += float3(1.0, 0.3, 0.05) * exp(-outsideD / (glowR * 0.25)) * 1.2 * NoteC.z * (1.0 - inside);
        emit += float3(1.0, 0.55, 0.2) * sounding * 0.5 * inside;
        alpha = inside;
    }

    emit += lerp(col, 1.0, 0.65) * head * inside * 2.2;
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
    float3 albedo = black ? float3(0.018, 0.018, 0.022) : float3(0.86, 0.84, 0.8);
    float roughness = black ? lerp(0.5, 0.14, KeyA.w) : lerp(0.65, 0.3, KeyA.w);
    float3 f0 = black ? float3(0.05, 0.05, 0.05) : float3(0.035, 0.035, 0.035);
    if (style < 0.5) { roughness = min(1.0, roughness + 0.25); }
    else if (style > 1.5)
    {
        albedo = black ? float3(0.03, 0.035, 0.05) : float3(0.55, 0.62, 0.72);
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
        color -= spec * (1.0 - (0.04 + 0.55 * band));
        color = max(color, 0.0);
    }
    color += albedo * (0.16 + 0.12 * n.y) * (0.6 + 0.4 * KeyA.x);
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
    if (!black) color = lerp(color, color * 0.4, saturate(strength) * 0.6);
    color += emissive * (n.z > 0.5 ? 0.7 : 1.0);
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
    float4 Params : TEXCOORD2;    // x = kind, y = age, z = aspect, w = scene x
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
    if (kind > 0.5 && kind < 1.5) extent = float2(size * (1.0 + s.Dir.z), size);          // streak
    else if (kind > 4.5 && kind < 5.5) { axis = float2(0.0, -1.0); normal = float2(1.0, 0.0); extent = float2(s.Dir.z * 0.5, size); offset = float2(0.0, -s.Dir.z * 0.5); }   // beam
    else if (kind > 5.5) { axis = float2(1.0, 0.0); normal = float2(0.0, 1.0); extent = float2(size, s.Dir.z); }  // hit line
    else if (kind > 3.5) extent = float2(size * 2.6, size);                               // flare
    float2 scene = s.PosSize.xy + offset + axis * corner.x * extent.x + normal * corner.y * extent.y;
    // flares and the hit line sit in front of the keys; particles fly behind them
    float depth = ((kind > 3.5 && kind < 4.5) || kind > 5.5) ? 0.01 : 0.6;
    SpriteOut o;
    o.Position = ToClip(SceneToScreen(scene), depth);
    o.Local = corner;
    o.Color = s.Color;
    o.Params = float4(kind, s.Dir.w, extent.x / max(extent.y, 1e-3), scene.x);
    return o;
}

float3 HitLineColor(float x)
{
    // the pitch under this x (the keyboard is close to linear in pitch at this scale)
    float t = saturate(x / SceneSize.x);
    float pitch = 21.0 + t * 87.0;
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

float4 PsSprite(SpriteOut v) : SV_Target
{
    float kind = v.Params.x;
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
    // hit line: a thin bright filament across the keyboard, tinted by the notes under it
    float3 tint = lerp(c, HitLineColor(v.Params.w), 0.75);
    float core = exp(-p.y * p.y * 60.0);
    float glow = exp(-p.y * p.y * 5.0) * 0.45;
    return float4(tint * (core * 1.6 + glow) * a + core * 0.25 * a, 0.0);
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

float4 PsComposite(FullscreenOut i) : SV_Target
{
    float4 scene = SourceTex.SampleLevel(PointClamp, i.Uv, 0);
    float3 bloom = SecondTex.SampleLevel(LinearClamp, i.Uv, 0).rgb * Post2.y;
    float3 c = (scene.rgb + bloom) * Post.x;
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
    display += noise * Post2.w;
    if (Background.w > 0.5)
    {
        // chroma key: solid content and the light around it cover the green; empty space shows it
        float coverage = saturate(scene.a * 1.5 + Luma(scene.rgb + bloom) * 1.5);
        display = lerp(float3(0.0, 1.0, 0.0), display, coverage);
    }
    return float4(display, 1.0);
}
