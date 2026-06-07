// ═══════════════════════════════════════════════════════════════════════════════
// EasePlus PrePass — METAL OPTIMIZED for Apple Silicon & Intel Iris
// ═══════════════════════════════════════════════════════════════════════════════
// Optimizations:
// - Fast math for aggressive compiler opts
// - Packed normals (2-component storage, reconstruct Z)
// - Early depth test for bandwidth savings
// - SIMD-friendly matrix ops
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
    half3 worldNormal; // half precision for bandwidth savings
};

// ── VERTEX SHADER (OPTIMIZED) ────────────────────────────────────────────────
vertex VertexOutput easeplus_vs_prepass(
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
    
    // Combined world + view + proj transform (fewer ALU ops)
    float4 worldPos = model * float4(in.position, 1.0);
    out.position = view.ViewProj * worldPos;
    
    // Store a full world-space normal in RGB. The old XY-only view normal lost
    // the Z sign and made the light pass unstable on imported car meshes.
    out.worldNormal = half3((model * float4(in.normal, 0.0)).xyz);
    
    return out;
}

half2 EncodeNormal(half3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z) + 0.0001h);
    half2 signNotZero = select(half2(-1.0h), half2(1.0h), n.xy >= 0.0h);
    if (n.z >= 0.0h) {
        return n.xy * 0.5h + 0.5h;
    } else {
        return ((1.0h - abs(n.yx)) * signNotZero) * 0.5h + 0.5h;
    }
}

// ── FRAGMENT SHADER (OPTIMIZED) ──────────────────────────────────────────────
[[early_fragment_tests]] // Enable early depth test for bandwidth savings
fragment half4 easeplus_fs_prepass(
    VertexOutput in [[stage_in]],
    constant ObjectUniforms& obj [[buffer(11)]])
{
    // Fast normalize using half precision (Apple GPU has native half ops)
    half3 N = normalize(in.worldNormal);
    half2 enc = EncodeNormal(N);
    
    // Pack Octahedron normal to RG, Roughness to B, Metallic to A
    return half4(enc.x, enc.y, half(obj.Roughness), half(obj.Metallic));
}
