// ═══════════════════════════════════════════════════════════════════════════════
// EasePlus Material Pass — METAL OPTIMIZED for Apple Silicon & Intel Iris
// ═══════════════════════════════════════════════════════════════════════════════
// Optimizations:
// - Half precision for color calculations
// - Fast math intrinsics
// - Optimized bilateral upsampling (3×3 instead of 9×9)
// - Early fragment tests
// - ACES tonemapping with fast approximation
// ═══════════════════════════════════════════════════════════════════════════════

#include <metal_stdlib>
using namespace metal;

struct ViewUniforms {
    float4x4 View;
    float4x4 Proj;
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4   CameraPos;
    float2   ScreenSize;
    float    NearPlane;
    float    FarPlane;
    float3   SunDirection;
    float    SunIntensity;
    float3   SunColor;
    int      TilesX;
};

struct ObjectUniforms {
    float4x4 Model;
    float4   AlbedoColor;
    float    Metallic;
    float    Roughness;
    float    AO;
    float    Emission;
    int      UseAlbedoTex;
    int      UseNormalTex;
    int      UseRMATex;
    int      UseInstanceBuffer;
    int      InstanceBase;
    int      _pad0;
    int      _pad1;
    int      _pad2;
};

struct InstanceUniforms {
    float4x4 Model;
};

struct VertexInput {
    float3 position [[attribute(0)]];
    float3 normal   [[attribute(1)]];
    float2 uv       [[attribute(2)]];
};

struct VertexOutput {
    float4 position [[position]];
    half3 normal; // Half precision
    float3 worldPos;
    float2 uv;
};

// ── CONSTEXPR SAMPLERS ───────────────────────────────────────────────────────
constexpr sampler linearSamp(coord::normalized, filter::linear, address::clamp_to_edge);

// ── VERTEX SHADER (OPTIMIZED) ────────────────────────────────────────────────
vertex VertexOutput easeplus_vs_material(
    VertexInput in [[stage_in]],
    uint instanceID [[instance_id]],
    constant ViewUniforms& view [[buffer(10)]],
    constant ObjectUniforms& obj [[buffer(11)]],
    constant InstanceUniforms* instances [[buffer(12)]])
{
    VertexOutput out;
    uint modelIndex = uint(obj.InstanceBase) + instanceID;
    float4x4 model = obj.UseInstanceBuffer != 0
        ? instances[modelIndex].Model
        : obj.Model;
    
    // Combined transform
    float4 worldPos = model * float4(in.position, 1.0);
    out.position = view.ViewProj * worldPos;
    out.worldPos = worldPos.xyz;
    
    // Fast normal transform (half precision)
    out.normal = half3((model * float4(in.normal, 0.0)).xyz); // Defer normalization to FS
    out.uv = in.uv;
    
    return out;
}


// ── BILATERAL UPSAMPLING (OPTIMIZED) ─────────────────────────────────────────
// Reduced from 9×9 to 3×3 for better performance on integrated GPUs
half3 BilateralSampleLight(
    float2 screenUV,
    float centerDepth,
    texture2d<half> LightBuffer,
    texture2d<float> DepthBuffer,
    float2 screenSize)
{
    // HD 3000-safe path: one filtered half-res light sample. The previous
    // 3x3 bilateral pass was expensive per material fragment and still needed
    // a robust ambient fallback for editor assets.
    return LightBuffer.sample(linearSamp, screenUV).rgb * 5.0h; // HDR unpack range 0..5
}

// ── FAST ACES TONEMAPPING ────────────────────────────────────────────────────
// Optimized ACES approximation using half precision
half3 ACESFilmicTonemap(half3 x) {
    // Narkowicz 2015, "ACES Filmic Tone Mapping Curve"
    half a = 2.51h;
    half b = 0.03h;
    half c = 2.43h;
    half d = 0.59h;
    half e = 0.14h;
    return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

// ── ENVIRONMENT BRDF & SKY ───────────────────────────────────────────────────
half2 EnvBRDFApprox(half roughness, half NdotV) {
    half4 c0 = half4(-1.0h, -0.0275h, -0.572h, 0.022h);
    half4 c1 = half4(1.0h, 0.0425h, 1.04h, -0.04h);
    half4 r = roughness * c0 + c1;
    half a004 = min(r.x * r.x, exp2(-9.28h * NdotV)) * r.x + r.y;
    half2 AB = half2(-1.04h, 1.04h) * a004 + r.zw;
    return AB;
}

half3 sampleSkyEnv(half3 dir, half3 sunDir, half roughness) {
    half horizonAngle = abs(dir.y);
    half3 zenith  = half3(0.08h, 0.26h, 0.56h);
    half3 horizon = half3(0.52h, 0.72h, 0.92h);
    half3 sky = mix(horizon, zenith, pow(saturate(dir.y), 0.6h));
    
    half cosTheta = dot(dir, normalize(sunDir));
    half sunPower = exp2(10.0h * (1.0h - roughness) + 1.0h);
    half sunGlow = pow(saturate(cosTheta), sunPower) * (1.05h / (1.0h + roughness * 6.0h));
    sky += half3(1.0h, 0.93h, 0.78h) * sunGlow;
    
    half horizonStrip = exp(-horizonAngle * mix(40.0h, 15.0h, roughness));
    half secondaryStrip = exp(-abs(dir.y - 0.2h) * 20.0h) * 0.3h;
    sky += half3(0.95h, 0.98h, 1.0h) * (horizonStrip + secondaryStrip) * (0.35h * (1.0h - roughness));
    
    half3 ground = half3(0.065h, 0.070h, 0.075h);
    sky = mix(ground, sky, smoothstep(-0.08h, 0.18h, dir.y));
    
    half3 avgIrradiance = half3(0.24h, 0.28h, 0.34h);
    sky = mix(sky, avgIrradiance, roughness * roughness * 0.78h);
    return max(sky, 0.0h);
}

// ── FRAGMENT SHADER (OPTIMIZED) ──────────────────────────────────────────────
fragment half4 easeplus_fs_material(
    VertexOutput in [[stage_in]],
    constant ViewUniforms& view [[buffer(10)]],
    constant ObjectUniforms& obj [[buffer(11)]], // Note: buffer 11 for material uniforms
    texture2d<half> LightBuffer [[texture(0)]],
    texture2d<float> DepthBuffer [[texture(1)]],
    texture2d<half> albedoTex [[texture(2)]],
    texture2d<half> normalTex [[texture(3)]],
    texture2d<half> rmaTex [[texture(4)]])
{
    // Convert screen position to UV
    float2 screenUV = in.position.xy / view.ScreenSize;
    float depth = in.position.z;
    
    // Hardware depth test handles this - no manual discard needed
    
    // Sample lighting with bilateral upsampling (half precision)
    half3 lighting = BilateralSampleLight(
        screenUV, depth,
        LightBuffer, DepthBuffer,
        view.ScreenSize
    );
    
    // Apply material (half precision)
    half3 albedo = half3(obj.AlbedoColor.rgb);
    if (obj.UseAlbedoTex != 0)
        albedo *= albedoTex.sample(linearSamp, in.uv).rgb;
        
    half ao = half(obj.AO);
    half roughness = half(obj.Roughness);
    half metallic = half(obj.Metallic);
    
    if (obj.UseRMATex != 0)
    {
        half3 rma = rmaTex.sample(linearSamp, in.uv).rgb;
        roughness = rma.r;
        metallic = rma.g;
        ao *= rma.b;
    }
    
    half emission = half(obj.Emission);
    roughness = clamp(roughness, 0.08h, 0.95h);
    metallic = saturate(metallic);
    ao = max(ao, 0.15h);
    
    half3 N = normalize(in.normal);
    if (obj.UseNormalTex != 0)
    {
        half3 tangentNormal = normalTex.sample(linearSamp, in.uv).rgb * 2.0h - 1.0h;
        float3 dp1 = dfdx(in.worldPos);
        float3 dp2 = dfdy(in.worldPos);
        float2 duv1 = dfdx(in.uv);
        float2 duv2 = dfdy(in.uv);
        
        float3 T = normalize(dp1 * duv2.y - dp2 * duv1.y);
        float3 B = normalize(cross(float3(N), T)) * (duv1.x * duv2.y - duv2.x * duv1.y < 0.0 ? -1.0 : 1.0);
        
        N = normalize(half3(T) * tangentNormal.x + half3(B) * tangentNormal.y + N * tangentNormal.z);
    }

    half3 V = normalize(half3(view.CameraPos.xyz - in.worldPos));
    half3 L = normalize(half3(view.SunDirection)); // SunDirection already points TOWARD sun
    half NdotL = saturate(dot(N, L));
    half NdotV = saturate(dot(N, V));
    half wrap = saturate(dot(N, L) * 0.5h + 0.5h);
    half skyMix = saturate(N.y * 0.5h + 0.5h);
    
    // Cheap safety lighting: keeps imported editor assets readable even when
    // the half-res light buffer is empty, low precision, or facing away.
    half sunStrength = clamp(half(view.SunIntensity), 1.0h, 4.5h);
    half3 skyBounce = mix(half3(0.07h, 0.06h, 0.05h), half3(0.34h, 0.42h, 0.56h), skyMix);
    half3 wrappedSun = half3(view.SunColor) * sunStrength * (NdotL * 0.48h + wrap * 0.16h);
    half3 minimumLighting = skyBounce + wrappedSun;
    lighting = max(lighting, minimumLighting);
    
    // Combine lighting with material. Keep some diffuse readability on metals
    // because many imported car paints arrive as over-metallic.
    half diffuseKeep = mix(1.0h, 0.38h, metallic);
    half3 color = albedo * lighting * ao * diffuseKeep;
    
    // Add full-res sun specular for sharp, crisp highlights (solves half-res light buffer issues for the key light)
    half3 H = normalize(V + L);
    half NdotH = max(dot(N, H), 0.0h);
    half a = roughness * roughness;
    half a2 = a * a;
    half denom = NdotH * NdotH * (a2 - 1.0h) + 1.0h;
    half D = a2 / (3.14159h * denom * denom + 0.0001h);
    
    half k = (roughness + 1.0h) * (roughness + 1.0h) * 0.125h;
    half G = (NdotV / (NdotV * (1.0h - k) + k)) * (NdotL / (NdotL * (1.0h - k) + k));
    
    half3 F0 = mix(half3(0.04h), albedo, metallic);
    half HdotV = max(dot(H, V), 0.0h);
    half3 F = F0 + (1.0h - F0) * pow(1.0h - HdotV, 5.0h);
    
    half3 sunSpec = (D * G * F) / (4.0h * max(NdotV, 0.001h) * max(NdotL, 0.001h) + 0.0001h);
    color += sunSpec * half3(view.SunColor) * sunStrength * NdotL * ao;
    
    // High-quality procedural environment specular
    half3 R = reflect(-V, N);
    half horizonAO = saturate(R.y * 0.5h + 0.5h);
    horizonAO = horizonAO * horizonAO; // Darken reflections pointing down at geometry
    
    half3 envColor = sampleSkyEnv(R, half3(view.SunDirection), roughness);
    
    half2 envBRDF = EnvBRDFApprox(roughness, NdotV);
    half3 specularColor = F0 * envBRDF.x + envBRDF.y;
    
    // Multi-scatter energy compensation
    half3 energyComp = 1.0h + F0 * (1.0h / envBRDF.x - 1.0h);
    specularColor *= energyComp;
    
    color += envColor * specularColor * ao * horizonAO;
    
    // Fake clearcoat for shiny things (cars)
    if (roughness < 0.2h && metallic > 0.5h) {
        half3 ccR = reflect(-V, in.normal); // use smooth normal for clearcoat
        half3 ccEnv = sampleSkyEnv(ccR, half3(view.SunDirection), 0.02h);
        half ccFresnel = 0.04h + 0.96h * pow(1.0h - NdotV, 5.0h);
        color += ccEnv * ccFresnel * ao * saturate(ccR.y * 0.5h + 0.5h);
    }
    
    // Rim lighting (adds shape)
    half rim = 1.0h - NdotV;
    rim = smoothstep(0.6h, 1.0h, rim) * saturate(NdotL + 0.2h);
    color += half3(view.SunColor) * rim * 0.15h * ao;
    
    // Add emission
    color += albedo * emission;
    
    // Fast ACES tonemapping
    color = ACESFilmicTonemap(color);
    
    // Fast gamma correction (half precision)
    color = pow(saturate(color), 1.0h / 2.2h);
    
    return half4(color, half(obj.AlbedoColor.a));
}
