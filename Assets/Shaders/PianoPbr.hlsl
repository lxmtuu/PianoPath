// Direct3D 11 Shader Model 5.0 physically based material shader.
// This is compiled by the Direct3D renderer at runtime; it is not a WPF brush/effect.
cbuffer FrameConstants : register(b0)
{
    row_major float4x4 World;
    row_major float4x4 View;
    row_major float4x4 Projection;
    float4 CameraPosition;
    float4 KeyLightDirection;
    float4 KeyLightColor;
    float4 FillLightPosition;
    float4 FillLightColor;
    float4 AmbientColor;
    float4 MaterialBaseColor;
    float4 MaterialParams; // x = metallic, y = roughness, z = emissive, w = unused
};

struct VertexInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 WorldNormal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

static const float PI = 3.14159265359;

PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(float4(input.Position, 1.0), World);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = normalize(mul(float4(input.Normal, 0.0), World).xyz);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.TexCoord = input.TexCoord;
    return output;
}

float DistributionGGX(float NdotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 0.0001);
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / max(NdotV * (1.0 - k) + k, 0.0001);
}

float3 FresnelSchlick(float cosTheta, float3 f0)
{
    return f0 + (1.0 - f0) * pow(saturate(1.0 - cosTheta), 5.0);
}

float3 EvaluateLight(float3 N, float3 V, float3 L, float3 radiance, float3 albedo, float metallic, float roughness)
{
    float3 H = normalize(V + L);
    float NdotL = saturate(dot(N, L));
    float NdotV = saturate(dot(N, V));
    float NdotH = saturate(dot(N, H));
    float VdotH = saturate(dot(V, H));
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 F = FresnelSchlick(VdotH, f0);
    float D = DistributionGGX(NdotH, roughness);
    float G = GeometrySchlickGGX(NdotV, roughness) * GeometrySchlickGGX(NdotL, roughness);
    float3 specular = (D * G * F) / max(4.0 * NdotV * NdotL, 0.001);
    float3 diffuse = (1.0 - F) * (1.0 - metallic) * albedo / PI;
    return (diffuse + specular) * radiance * NdotL;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float3 albedo = MaterialBaseColor.rgb;
    float metallic = saturate(MaterialParams.x);
    float roughness = clamp(MaterialParams.y, 0.045, 1.0);
    float3 N = normalize(input.WorldNormal);
    float3 V = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 keyL = normalize(-KeyLightDirection.xyz);
    float3 fillDelta = FillLightPosition.xyz - input.WorldPosition;
    float fillDistance2 = max(dot(fillDelta, fillDelta), 0.01);
    float3 fillL = fillDelta * rsqrt(fillDistance2);
    float3 color = AmbientColor.rgb * albedo;
    color += EvaluateLight(N, V, keyL, KeyLightColor.rgb, albedo, metallic, roughness);
    color += EvaluateLight(N, V, fillL, FillLightColor.rgb / (1.0 + 0.12 * fillDistance2), albedo, metallic, roughness);
    color += albedo * MaterialParams.z;

    // ACES filmic tone mapping keeps highlights controlled like a real-time PBR renderer.
    color = saturate((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14));
    return float4(color, MaterialBaseColor.a);
}
