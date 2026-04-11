// ─────────────────────────────────────────────────────────────────────────────
// DirectX 9 HLSL Viewport Shader
// Matches viewport_3d.metal functionality
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// Shared Uniforms
// ─────────────────────────────────────────────────────────────────────────────
// We use binding 10 for ViewUniforms to avoid overlapping with EntityUniforms
// in DX9 (where bindings map directly to registers).
// ViewUniforms takes 18 registers (c10 to c27).
float4x4 View          : register(c10);
float4x4 Proj          : register(c14);
float4x4 ViewProj      : register(c18);
float4x4 InvViewProj   : register(c22);
float4x4 LightSpaceMatrix : register(c26);
float3   CameraPos     : register(c30);
float    Time          : register(c30); // actually w component
float3   SunDirection  : register(c31);

Texture2D ShadowMap    : register(t1);
SamplerState ShadowSampler : register(s1);

// Entity Uniforms takes 5 registers (c32 to c36).
float4x4 EntityModel   : register(c32);
float4   EntityColor   : register(c36);

// ═════════════════════════════════════════════════════════════════════════════
// SKY
// ═════════════════════════════════════════════════════════════════════════════
struct VS_SKY_OUTPUT {
    float4 position : POSITION;
    float2 uv       : TEXCOORD0;
    float3 rayDir   : TEXCOORD1;
};

VS_SKY_OUTPUT vs_sky(uint vertexID : SV_VertexID) {
    VS_SKY_OUTPUT output;
    
    // Full-screen triangle trick
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.position = float4(uv * 2.0 - 1.0, 0.9999, 1.0);
    output.uv = uv;

    float4 clipPos = float4(uv * 2.0 - 1.0, 1.0, 1.0);
    float4 worldPosH = mul(InvViewProj, clipPos); // HLSL uses mul(M, v)
    float3 worldPos = worldPosH.xyz / worldPosH.w;
    output.rayDir = normalize(worldPos - CameraPos);

    return output;
}

// Simple hash function for noise
float hash(float n) {
    return frac(sin(n) * 43758.5453123);
}

// 2D noise function
float noise(float2 x) {
    float2 p = floor(x);
    float2 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0;
    return lerp(lerp(hash(n), hash(n + 1.0), f.x),
               lerp(hash(n + 57.0), hash(n + 58.0), f.x), f.y);
}

// Fractal Brownian Motion for cloud shapes
float fbm(float2 x) {
    float v = 0.0;
    float a = 0.5;
    float2 shift = float2(100.0, 100.0);
    for (int i = 0; i < 5; i++) {
        v += a * noise(x);
        x = x * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

float3 computeSky(float3 rayDir, float3 sunDir, float time) {
    float height = rayDir.y;
    float sunElevation = sunDir.y;
    
    float3 deepBlue = float3(0.1, 0.35, 0.75);     
    float3 midBlue = float3(0.25, 0.55, 0.9);      
    float3 lightBlue = float3(0.5, 0.75, 0.98);    
    float3 horizonColor = float3(0.7, 0.85, 1.0); 
    
    float t = saturate(height * 0.5 + 0.5);
    
    float3 skyColor;
    if (t > 0.5) {
        float upperBlend = smoothstep(0.5, 1.0, t);
        skyColor = lerp(midBlue, deepBlue, upperBlend);
    } else {
        float lowerBlend = smoothstep(0.0, 0.5, t);
        skyColor = lerp(horizonColor, lightBlue, lowerBlend);
        skyColor = lerp(skyColor, midBlue, lowerBlend * lowerBlend);
    }
    
    float2 cloudUV = rayDir.xz * 1.5 + float2(time * 0.01, time * 0.005);
    float cloudNoise = fbm(cloudUV);
    float cloudDetail = fbm(cloudUV * 2.5) * 0.4;
    float cloudShape = cloudNoise + cloudDetail * 0.3;
    
    float cloudDensity = smoothstep(0.5, 0.7, cloudShape);
    float cloudEdge = smoothstep(0.4, 0.6, cloudShape) - cloudDensity;
    
    float sunDot = dot(rayDir, sunDir);
    float cloudBrightness = smoothstep(-0.3, 0.8, sunDot);
    
    float3 cloudLit = float3(1.0, 1.0, 0.98);
    float3 cloudShadow = float3(0.75, 0.8, 0.88);
    float3 cloudColor = lerp(cloudShadow, cloudLit, cloudBrightness);
    
    float3 finalColor = lerp(skyColor, cloudColor, cloudDensity * 0.7);
    finalColor = lerp(finalColor, skyColor, cloudEdge * 0.2);
    
    float cosTheta = dot(rayDir, sunDir);
    float sunAngle = acos(saturate(cosTheta));
    
    float sunGlow = exp(-sunAngle * sunAngle * 30.0) * 0.3;
    float sunDisc = smoothstep(0.02, 0.01, sunAngle);
    finalColor += float3(1.0, 0.98, 0.95) * (sunGlow + sunDisc);
    
    float horizonHaze = exp(-height * 3.0) * 0.15;
    finalColor = lerp(finalColor, float3(0.75, 0.88, 1.0), horizonHaze);
    
    finalColor = saturate(finalColor * 0.95);
    finalColor = pow(abs(finalColor), float3(1.0 / 2.2, 1.0 / 2.2, 1.0 / 2.2));
    
    return finalColor;
}

float4 fs_sky(VS_SKY_OUTPUT input) : COLOR {
    float3 skyColor = computeSky(input.rayDir, SunDirection, Time);
    return float4(skyColor, 1.0);
}

// ═════════════════════════════════════════════════════════════════════════════
// GRID
// ═════════════════════════════════════════════════════════════════════════════
struct VS_GRID_OUTPUT {
    float4 position : POSITION;
    float3 nearPoint: TEXCOORD0;
    float3 farPoint : TEXCOORD1;
};

VS_GRID_OUTPUT vs_grid(uint vertexID : SV_VertexID) {
    VS_GRID_OUTPUT output;
    
    float2 positions[6] = {
        float2(-1, -1), float2( 1, -1), float2( 1,  1),
        float2(-1, -1), float2( 1,  1), float2(-1,  1)
    };
    float2 p = positions[vertexID];

    float4 nearH = mul(InvViewProj, float4(p, 0.0, 1.0));
    float4 farH  = mul(InvViewProj, float4(p, 1.0, 1.0));
    float3 nearPt = nearH.xyz / nearH.w;
    float3 farPt  = farH.xyz  / farH.w;

    output.position  = float4(p, 0.0, 1.0);
    output.nearPoint = nearPt;
    output.farPoint  = farPt;
    
    return output;
}

float computeDepth(float3 pos, float4x4 viewProj) {
    float4 clip = mul(viewProj, float4(pos, 1.0));
    return clip.z / clip.w;
}

float4 gridPattern(float3 worldPos, float scale, float lineWidth) {
    float2 coord = worldPos.xz * scale;
    float2 derivative = fwidth(coord);
    float2 gridLine = abs(frac(coord - 0.5) - 0.5) / derivative;
    float lineDist = min(gridLine.x, gridLine.y);
    float alpha = 1.0 - min(lineDist, 1.0);
    return float4(0.35, 0.35, 0.40, alpha);
}

struct FS_GRID_OUTPUT {
    float4 color : SV_Target0;
    float  depth : SV_Depth;
};

FS_GRID_OUTPUT fs_grid(VS_GRID_OUTPUT input) {
    FS_GRID_OUTPUT outFrag;

    float3 ray = input.farPoint - input.nearPoint;
    float t = -input.nearPoint.y / ray.y;

    if (t < 0.0) clip(-1);

    float3 hitPos = input.nearPoint + t * ray;

    float dist = length(hitPos - CameraPos);
    float fadeFar = 1.0 - smoothstep(15.0, 80.0, dist);

    if (fadeFar < 0.001) clip(-1);

    float4 fineGrid   = gridPattern(hitPos, 1.0, 1.0);
    float4 coarseGrid = gridPattern(hitPos, 0.1, 1.5);

    float4 gridColor = fineGrid;
    gridColor.a = max(fineGrid.a * 0.4, coarseGrid.a * 0.7);
    gridColor.rgb = lerp(fineGrid.rgb, coarseGrid.rgb * 1.2, coarseGrid.a);

    float2 axisDerivative = fwidth(hitPos.xz);
    
    float xAxisDist = abs(hitPos.z) / axisDerivative.y;
    float xAxisLine = 1.0 - min(xAxisDist, 1.0);
    
    float zAxisDist = abs(hitPos.x) / axisDerivative.x;
    float zAxisLine = 1.0 - min(zAxisDist, 1.0);

    if (xAxisLine > 0.01) {
        gridColor.rgb = lerp(gridColor.rgb, float3(0.85, 0.20, 0.18), xAxisLine);
        gridColor.a = max(gridColor.a, xAxisLine * 0.9);
    }
    if (zAxisLine > 0.01) {
        gridColor.rgb = lerp(gridColor.rgb, float3(0.20, 0.35, 0.85), zAxisLine);
        gridColor.a = max(gridColor.a, zAxisLine * 0.9);
    }

    gridColor.a *= fadeFar;

    float fadeNear = smoothstep(0.3, 1.5, dist);
    gridColor.a *= fadeNear;

    float depth = computeDepth(hitPos, ViewProj);
    depth = clamp(depth, 0.0, 1.0);

    // ── 0. Compute Shadow ──────────────────────────────────────────────
    float4 lightSpacePos = mul(LightSpaceMatrix, float4(hitPos, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;
    
    float shadow = 1.0;
    if (shadowUV.x >= 0.0 && shadowUV.x <= 1.0 && shadowUV.y >= 0.0 && shadowUV.y <= 1.0 && projCoords.z >= 0.0 && projCoords.z <= 1.0) {
        float bias = 0.001;
        float currentDepth = projCoords.z - bias;
        float shadowSum = 0.0;
        
        // Simple PCF
        float2 texelSize = 1.0 / 2048.0;
        for (int x = -1; x <= 1; ++x) {
            for (int y = -1; y <= 1; ++y) {
                float pcfDepth = ShadowMap.Sample(ShadowSampler, shadowUV + float2(x, y) * texelSize).r;
                shadowSum += currentDepth < pcfDepth ? 1.0 : 0.0;
            }
        }
        shadow = shadowSum / 9.0;
    }
    
    gridColor.rgb *= lerp(0.4, 1.0, shadow);

    outFrag.color = gridColor;
    outFrag.depth = depth;
    return outFrag;
}

// ═════════════════════════════════════════════════════════════════════════════
// MESH
// ═════════════════════════════════════════════════════════════════════════════
struct VS_MESH_INPUT {
    float3 position : POSITION;
    float3 normal   : NORMAL;
};

struct VS_MESH_OUTPUT {
    float4 position      : SV_POSITION;
    float4 lightSpacePos : TEXCOORD1;
    float3 normal        : TEXCOORD0;
};

VS_MESH_OUTPUT vs_mesh(VS_MESH_INPUT input) {
    VS_MESH_OUTPUT output;
    
    float4 worldPos = mul(EntityModel, float4(input.position, 1.0));
    output.position = mul(ViewProj, worldPos);
    output.lightSpacePos = mul(LightSpaceMatrix, worldPos);
    
    output.normal = normalize(mul((float3x3)EntityModel, input.normal));
    
    return output;
}

// ═════════════════════════════════════════════════════════════════════════════
// SHADOW PASS
// ═════════════════════════════════════════════════════════════════════════════
struct VS_SHADOW_OUTPUT {
    float4 position : SV_POSITION;
};

VS_SHADOW_OUTPUT vs_shadow(VS_MESH_INPUT input) {
    VS_SHADOW_OUTPUT output;
    float4 worldPos = mul(EntityModel, float4(input.position, 1.0));
    output.position = mul(LightSpaceMatrix, worldPos);
    return output;
}

float4 fs_shadow(VS_SHADOW_OUTPUT input) : SV_Target0 {
    return float4(input.position.z, 0, 0, 1);
}

float4 fs_mesh(VS_MESH_OUTPUT input) : SV_Target0 {
    float3 lightDir = normalize(SunDirection);
    float3 normal = normalize(input.normal);
    
    float3 ambient = 0.2 * EntityColor.rgb;
    
    float NdotL = max(dot(normal, lightDir), 0.0);
    float3 diffuse = NdotL * EntityColor.rgb;
    
    // Shadow map sampling
    float3 projCoords = input.lightSpacePos.xyz / input.lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;
    
    float shadow = 1.0;
    if (shadowUV.x >= 0.0 && shadowUV.x <= 1.0 && shadowUV.y >= 0.0 && shadowUV.y <= 1.0 && projCoords.z >= 0.0 && projCoords.z <= 1.0) {
        float bias = max(0.005 * (1.0 - dot(normal, lightDir)), 0.001);
        float currentDepth = projCoords.z - bias;
        float shadowSum = 0.0;
        
        float2 texelSize = 1.0 / 2048.0;
        for (int x = -1; x <= 1; ++x) {
            for (int y = -1; y <= 1; ++y) {
                float pcfDepth = ShadowMap.Sample(ShadowSampler, shadowUV + float2(x, y) * texelSize).r;
                shadowSum += currentDepth < pcfDepth ? 1.0 : 0.0;
            }
        }
        shadow = shadowSum / 9.0;
    }
    
    float3 finalColor = ambient + diffuse * lerp(0.2, 1.0, shadow);
    finalColor = pow(finalColor, float3(1.0 / 2.2, 1.0 / 2.2, 1.0 / 2.2));
    
    return float4(finalColor, EntityColor.a);
}
