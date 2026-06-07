using System;
using System.Numerics;

namespace BlueSky.Rendering;

/// <summary>
/// Shader Tricks - Fake expensive effects using clever math
/// "Why calculate when you can approximate?"
/// 
/// These techniques make things LOOK ultra without the cost:
/// - Fake reflections using cubemaps instead of ray tracing
/// - Fake ambient occlusion using bent normals
/// - Fake subsurface scattering using wrap lighting
/// - Fake translucency using thickness maps
/// - Fake detail using noise functions
/// </summary>
public static class ShaderTricks
{
    /// <summary>
    /// Fake Screen-Space Reflections using cubemap approximation
    /// Much cheaper than real SSR, looks 80% as good
    /// </summary>
    public static Vector3 FakeSSR(Vector3 worldPos, Vector3 normal, Vector3 viewDir, 
                                  float roughness, Func<Vector3, Vector3> cubemapSampler)
    {
        // Calculate reflection vector
        Vector3 reflectDir = Vector3.Reflect(-viewDir, normal);
        
        // Blur based on roughness (fake prefiltered environment)
        float mipLevel = roughness * 5.0f; // 5 mip levels
        
        // Sample cubemap
        Vector3 reflection = cubemapSampler(reflectDir);
        
        // Fade out at grazing angles (Fresnel approximation)
        float fresnel = MathF.Pow(1.0f - Vector3.Dot(normal, viewDir), 5.0f);
        
        return reflection * fresnel;
    }
    
    /// <summary>
    /// Fake Ambient Occlusion using bent normals
    /// No ray tracing needed, just a texture lookup
    /// </summary>
    public static float FakeAO(Vector3 normal, Vector3 bentNormal)
    {
        // Bent normal points towards least occluded direction
        // Difference from geometric normal indicates occlusion
        float occlusion = Vector3.Dot(normal, bentNormal);
        return MathF.Pow(occlusion, 2.0f); // Sharpen the effect
    }
    
    /// <summary>
    /// Fake Subsurface Scattering using wrap lighting
    /// Makes skin, leaves, etc look translucent without expensive scattering simulation
    /// </summary>
    public static float FakeSSS(Vector3 normal, Vector3 lightDir, float wrapAmount)
    {
        // Wrap lighting - light "wraps around" the surface
        float NdotL = Vector3.Dot(normal, lightDir);
        float wrap = (NdotL + wrapAmount) / (1.0f + wrapAmount);
        return Math.Max(0, wrap);
    }
    
    /// <summary>
    /// Fake Translucency using thickness map
    /// Light passes through thin objects like leaves, paper, cloth
    /// </summary>
    public static Vector3 FakeTranslucency(Vector3 normal, Vector3 lightDir, Vector3 viewDir,
                                          float thickness, Vector3 lightColor)
    {
        // Light scatters through the object
        Vector3 H = Vector3.Normalize(lightDir + normal * 0.5f);
        float VdotH = MathF.Pow(Math.Max(0, Vector3.Dot(viewDir, -H)), 4.0f);
        
        // Thinner = more light passes through
        float scatter = VdotH * (1.0f - thickness);
        
        return lightColor * scatter;
    }
    
    /// <summary>
    /// Fake Detail using procedural noise
    /// Add micro-detail without high-res textures
    /// </summary>
    public static float FakeDetail(Vector2 uv, float scale, int octaves)
    {
        float detail = 0;
        float amplitude = 1.0f;
        float frequency = scale;
        
        for (int i = 0; i < octaves; i++)
        {
            detail += SimplexNoise(uv * frequency) * amplitude;
            amplitude *= 0.5f;
            frequency *= 2.0f;
        }
        
        return detail;
    }
    
    /// <summary>
    /// Fake Depth of Field using single-pass blur
    /// Much cheaper than gathering samples
    /// </summary>
    public static float FakeDOF(float depth, float focusDistance, float focusRange)
    {
        float distance = Math.Abs(depth - focusDistance);
        float blur = Math.Max(0, (distance - focusRange) / focusRange);
        return Math.Min(blur, 1.0f);
    }
    
    /// <summary>
    /// Fake Motion Blur using velocity buffer
    /// Cheaper than multi-sample motion blur
    /// </summary>
    public static Vector2 FakeMotionBlur(Vector2 screenPos, Vector2 velocity, int samples)
    {
        // Sample along velocity vector
        Vector2 blurredPos = screenPos;
        
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            blurredPos += velocity * t / samples;
        }
        
        return blurredPos / samples;
    }
    
    /// <summary>
    /// Fake Volumetric Lighting using radial blur
    /// God rays without expensive ray marching
    /// </summary>
    public static float FakeGodRays(Vector2 screenPos, Vector2 lightScreenPos, int samples)
    {
        Vector2 delta = lightScreenPos - screenPos;
        float distance = delta.Length();
        
        if (distance < 0.001f) return 1.0f;
        
        Vector2 step = delta / samples;
        float accumulation = 0;
        
        for (int i = 0; i < samples; i++)
        {
            Vector2 samplePos = screenPos + step * i;
            // Would sample depth buffer here to check occlusion
            accumulation += 1.0f / (1.0f + distance);
        }
        
        return accumulation / samples;
    }
    
    /// <summary>
    /// Fake Caustics using animated noise
    /// Water caustics without ray tracing
    /// </summary>
    public static float FakeCaustics(Vector2 uv, float time)
    {
        // Two layers of animated noise
        float caustic1 = SimplexNoise(uv * 3.0f + new Vector2(time * 0.1f, 0));
        float caustic2 = SimplexNoise(uv * 3.0f + new Vector2(0, time * 0.15f));
        
        // Combine and sharpen
        float caustic = (caustic1 + caustic2) * 0.5f;
        caustic = MathF.Pow(Math.Max(0, caustic), 3.0f);
        
        return caustic;
    }
    
    /// <summary>
    /// Fake Iridescence (oil slick, soap bubble effect)
    /// </summary>
    public static Vector3 FakeIridescence(Vector3 normal, Vector3 viewDir, float time)
    {
        float fresnel = 1.0f - Vector3.Dot(normal, viewDir);
        
        // Rainbow colors based on angle
        float hue = fresnel * 3.0f + time * 0.1f;
        
        return HSVtoRGB(hue % 1.0f, 0.8f, 1.0f);
    }
    
    /// <summary>
    /// Fake Wet Surface using darkening and specular boost
    /// </summary>
    public static (Vector3 albedo, float roughness) FakeWetSurface(Vector3 dryAlbedo, 
                                                                    float dryRoughness, 
                                                                    float wetness)
    {
        // Wet surfaces are darker and more reflective
        Vector3 wetAlbedo = dryAlbedo * (1.0f - wetness * 0.5f);
        float wetRoughness = dryRoughness * (1.0f - wetness * 0.7f);
        
        return (wetAlbedo, wetRoughness);
    }
    
    /// <summary>
    /// Fake Parallax Occlusion Mapping - depth from height map
    /// </summary>
    public static Vector2 FakeParallax(Vector2 uv, Vector3 viewDirTangent, 
                                      Func<Vector2, float> heightSampler, 
                                      float heightScale, int steps)
    {
        float stepSize = 1.0f / steps;
        Vector2 uvOffset = new Vector2(viewDirTangent.X, viewDirTangent.Y) * heightScale / steps;
        
        Vector2 currentUV = uv;
        float currentHeight = 1.0f;
        
        // Ray march through height field
        for (int i = 0; i < steps; i++)
        {
            float sampledHeight = heightSampler(currentUV);
            
            if (currentHeight <= sampledHeight)
                break;
            
            currentUV -= uvOffset;
            currentHeight -= stepSize;
        }
        
        return currentUV;
    }
    
    // Helper functions
    
    private static float SimplexNoise(Vector2 v)
    {
        // Simplified 2D simplex noise
        // In real implementation, use proper simplex noise
        return MathF.Sin(v.X * 12.9898f + v.Y * 78.233f) * 43758.5453f % 1.0f;
    }
    
    private static Vector3 HSVtoRGB(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1.0f - Math.Abs((h * 6.0f) % 2.0f - 1.0f));
        float m = v - c;
        
        Vector3 rgb = (h * 6.0f) switch
        {
            < 1 => new Vector3(c, x, 0),
            < 2 => new Vector3(x, c, 0),
            < 3 => new Vector3(0, c, x),
            < 4 => new Vector3(0, x, c),
            < 5 => new Vector3(x, 0, c),
            _ => new Vector3(c, 0, x)
        };
        
        return rgb + new Vector3(m);
    }
    
}

/// <summary>
/// Cheap post-processing effects that look expensive
/// </summary>
public static class CheapPostFX
{
    /// <summary>
    /// Single-pass bloom using downsampling
    /// Much cheaper than dual-filtering
    /// </summary>
    public static Vector3 CheapBloom(Vector3 color, float threshold, float intensity)
    {
        // Extract bright areas
        float brightness = (color.X + color.Y + color.Z) / 3.0f;
        Vector3 bloom = brightness > threshold ? color * (brightness - threshold) : Vector3.Zero;
        
        // Apply bloom
        return color + bloom * intensity;
    }
    
    /// <summary>
    /// Cheap chromatic aberration using single offset
    /// </summary>
    public static Vector3 CheapChromaticAberration(Func<Vector2, Vector3> colorSampler,
                                                   Vector2 uv, float strength)
    {
        Vector2 offset = (uv - new Vector2(0.5f)) * strength;
        
        float r = colorSampler(uv + offset).X;
        float g = colorSampler(uv).Y;
        float b = colorSampler(uv - offset).Z;
        
        return new Vector3(r, g, b);
    }
    
    /// <summary>
    /// Cheap vignette using distance from center
    /// </summary>
    public static float CheapVignette(Vector2 uv, float intensity, float smoothness)
    {
        Vector2 center = uv - new Vector2(0.5f);
        float dist = center.Length();
        
        return MathF.Pow(1.0f - dist, smoothness) * (1.0f - intensity) + intensity;
    }
    
    /// <summary>
    /// Cheap film grain using noise
    /// </summary>
    public static float CheapFilmGrain(Vector2 uv, float time, float intensity)
    {
        float noise = (MathF.Sin(uv.X * 12.9898f + uv.Y * 78.233f + time) * 43758.5453f) % 1.0f;
        return (noise - 0.5f) * intensity;
    }
}
