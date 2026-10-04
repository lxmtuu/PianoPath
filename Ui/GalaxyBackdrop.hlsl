sampler2D Input : register(s0);

float4 PrimaryColor : register(c0);
float4 SecondaryColor : register(c1);
float Time : register(c2);
float Aspect : register(c3);
float Intensity : register(c4);
float Density : register(c5);

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

float4 PsNebula(float2 uv : TEXCOORD0) : COLOR0
{
    float t = Time;
    float aspect = max(Aspect, 1.0);
    float2 p = (uv - 0.5) * float2(aspect, 1.0);

    // Two gently advected noise fields make a cloud-like nebula without a texture asset.
    float2 flow = p + float2(t * 0.012, -t * 0.009);
    float n0 = ValueNoise(flow * float2(3.0, 4.2) + float2(0.0, t * 0.018));
    float n1 = ValueNoise(flow * float2(6.2, 7.1) + float2(-t * 0.021, t * 0.014));
    float n2 = ValueNoise(flow * float2(12.5, 13.4) + float2(t * 0.028, -t * 0.023));
    float cloudNoise = n0 * 0.57 + n1 * 0.29 + n2 * 0.14;
    float cloud = saturate((cloudNoise - 0.34) * 2.5);

    // A diagonal, softly warped galactic arm gives the clouds a musical, flowing direction.
    float arm = p.y + p.x * 0.34 + sin(p.x * 4.1 + t * 0.16) * 0.035;
    float ribbon = 1.0 - smoothstep(0.035, 0.30, abs(arm));
    float colorMix = saturate(0.5 + p.x * 0.43 + (n1 - 0.5) * 0.45);
    float3 nebulaColor = lerp(PrimaryColor.rgb, SecondaryColor.rgb, colorMix);

    float3 color = lerp(float3(0.008, 0.010, 0.028), float3(0.016, 0.020, 0.050), uv.y * 0.72);
    color += nebulaColor * (cloud * 0.105 + ribbon * cloud * 0.15) * Intensity;
    color += PrimaryColor.rgb * (cloud * cloud * 0.055) * Intensity;

    // Stable sub-pixel star cells with independent twinkle phases: the field drifts, the stars stay crisp.
    float2 starGrid = uv * float2(300.0 * aspect, 300.0) + float2(t * 0.075, -t * 0.045);
    float2 cell = floor(starGrid);
    float2 local = frac(starGrid);
    float seed = Hash21(cell);
    float2 center = float2(Hash21(cell + 17.17), Hash21(cell + 41.73));
    float2 delta = local - center;
    float density = clamp(Density, 0.0, 2.0);
    float starMask = step(1.0 - 0.007 * density, seed);
    float brightMask = step(1.0 - 0.0015 * density, seed);
    float twinkle = 0.62 + 0.38 * sin(t * (1.1 + seed * 2.8) + seed * 61.0);
    float core = exp(-dot(delta, delta) * 720.0) * starMask * twinkle;
    float halo = exp(-dot(delta, delta) * 65.0) * brightMask * twinkle;
    float spikes = (exp(-abs(delta.x) * 115.0) + exp(-abs(delta.y) * 115.0)) * brightMask * 0.08;
    color += float3(0.70, 0.78, 1.0) * (core * 0.74 + halo * 0.50 + spikes);

    // A few broader gold glints echo the piano image and the warm notes above the keys.
    float gold = saturate(ribbon * cloud * (0.08 + 0.12 * sin(t * 0.35 + p.x * 2.0)));
    color += float3(0.72, 0.43, 0.18) * gold * 0.09;

    float vignette = saturate(1.0 - dot((uv - 0.5) * float2(aspect * 0.64, 1.08), (uv - 0.5) * float2(aspect * 0.64, 1.08)) * 0.62);
    color *= 0.48 + vignette * 0.52;

    float4 source = tex2D(Input, uv);
    return float4(saturate(color), source.a);
}
