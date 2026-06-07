using System;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Rendering.PostProcessing;

/// <summary>
/// Complete post-processing stack for cinematic visuals
/// "Games are not benchmarks" - optimized for visual quality, not just FPS
/// Inspired by Frostbite's post-processing and UE5's cinematic tools
/// </summary>
public class PostProcessStack : IDisposable
{
    private readonly IRHIDevice _device;
    
    // Post-processing effects
    private readonly ACESTonemapper _tonemapper;
    private readonly TemporalAntiAliasing _taa;
    private readonly DepthOfField _dof;
    private readonly Bloom _bloom;
    private readonly ColorGrading _colorGrading;
    private readonly FilmGrain _filmGrain;
    private readonly Vignette _vignette;
    private readonly ChromaticAberration _chromaticAberration;
    
    // Optional effects
    private OptimizedSSAO? _ssao;
    private OptimizedSSR? _ssr;
    private EnhancedMotionBlur? _enhancedMotionBlur;
    private readonly RadialBlur _radialBlur;
    
    // Render targets
    private IRHITexture? _hdrTarget;
    private IRHITexture? _ldrTarget;
    private IRHITexture? _tempTarget;
    
    private int _width;
    private int _height;
    
    public PostProcessSettings Settings { get; set; } = PostProcessSettings.Cinematic;
    
    public PostProcessStack(IRHIDevice device)
    {
        _device = device;
        
        // Initialize core effects (always enabled)
        _tonemapper = new ACESTonemapper(device);
        _taa = new TemporalAntiAliasing(device);
        _dof = new DepthOfField(device);
        _bloom = new Bloom(device);
        _colorGrading = new ColorGrading(device);
        _filmGrain = new FilmGrain(device);
        _vignette = new Vignette(device);
        _chromaticAberration = new ChromaticAberration(device);
        _radialBlur = new RadialBlur(device);
        
        Console.WriteLine("[PostProcessStack] Initialized with cinematic defaults");
    }
    
    public void Initialize(int width, int height, PostProcessSettings? settings = null)
    {
        _width = width;
        _height = height;
        
        if (settings.HasValue)
            Settings = settings.Value;
        
        CreateRenderTargets();
        
        // Initialize core effects
        _tonemapper.Initialize();
        _taa.Initialize(width, height);
        _dof.Initialize(width, height, Settings.DOFQuality);
        _bloom.Initialize(width, height, Settings.BloomQuality);
        _colorGrading.Initialize();
        _filmGrain.Initialize();
        _vignette.Initialize();
        _chromaticAberration.Initialize();
        _radialBlur.Initialize();
        
        // Initialize optional effects
        if (Settings.EnableSSAO)
        {
            _ssao = new OptimizedSSAO(_device);
            _ssao.Initialize(width, height, Settings.SSAOQuality);
        }
        
        if (Settings.EnableSSR)
        {
            _ssr = new OptimizedSSR(_device);
            _ssr.Initialize(width, height, Settings.SSRQuality);
        }
        
        if (Settings.EnableMotionBlur)
        {
            _enhancedMotionBlur = new EnhancedMotionBlur(_device);
            _enhancedMotionBlur.Initialize(width, height);
        }
        
        Console.WriteLine($"[PostProcessStack] Initialized at {width}x{height}");
        Console.WriteLine($"[PostProcessStack] Profile: {GetProfileName()}");
    }
    
    /// <summary>
    /// Process a frame through the complete post-processing stack
    /// Input: HDR scene color, depth, normal, velocity
    /// Output: Final LDR image ready for display
    /// </summary>
    public IRHITexture ProcessFrame(IRHICommandBuffer cmd, 
                                    IRHITexture sceneColor,
                                    IRHITexture depthBuffer,
                                    IRHITexture normalBuffer,
                                    IRHITexture? velocityBuffer,
                                    Matrix4x4 viewMatrix,
                                    Matrix4x4 projMatrix,
                                    float deltaTime)
    {
        IRHITexture current = sceneColor;
        
        // 1. SSAO (if enabled) - adds depth and realism
        if (Settings.EnableSSAO && _ssao != null)
        {
            _ssao.Render(cmd, depthBuffer, normalBuffer, projMatrix, viewMatrix);
            // Apply AO to scene color
            current = ApplyAO(cmd, current, _ssao.GetAOTexture()!);
        }
        
        // 2. SSR (if enabled) - adds reflections
        if (Settings.EnableSSR && _ssr != null)
        {
            _ssr.Render(cmd, current, depthBuffer, normalBuffer, projMatrix, viewMatrix);
            // Composite reflections
            current = CompositeReflections(cmd, current, _ssr.GetReflectionTexture()!);
        }
        
        // 3. Temporal Anti-Aliasing - smooths edges and reduces shimmer
        if (Settings.EnableTAA && velocityBuffer != null)
        {
            current = _taa.Apply(cmd, current, depthBuffer, velocityBuffer, viewMatrix, projMatrix);
        }
        
        // 4. Depth of Field - cinematic focus effect
        if (Settings.EnableDOF)
        {
            current = _dof.Apply(cmd, current, depthBuffer, Settings.DOFFocalDistance, 
                                Settings.DOFAperture, Settings.DOFBokehShape);
        }
        
        // 5. Motion Blur - adds sense of speed and smoothness
        if (Settings.EnableMotionBlur && _enhancedMotionBlur != null && velocityBuffer != null)
        {
            current = _enhancedMotionBlur.Apply(cmd, current, velocityBuffer, depthBuffer, Settings.MotionBlurIntensity);
        }
        
        // 5.5 Radial Blur - speed line effect
        if (Settings.RadialBlurIntensity > 0.0f)
        {
            current = _radialBlur.Apply(cmd, current, Settings.RadialBlurIntensity, Settings.RadialBlurCenter);
        }
        
        // 6. Bloom - adds glow to bright areas (HDR must be before tonemapping)
        if (Settings.EnableBloom)
        {
            var bloomTexture = _bloom.Extract(cmd, current, Settings.BloomThreshold, Settings.BloomIntensity);
            current = _bloom.Composite(cmd, current, bloomTexture);
        }
        
        // 7. ACES Tonemapping - HDR to LDR conversion with filmic look
        current = _tonemapper.Apply(cmd, current, Settings.Exposure, Settings.ACESContrast);
        
        // 8. Color Grading - artistic color adjustments
        if (Settings.EnableColorGrading)
        {
            current = _colorGrading.Apply(cmd, current, Settings.ColorGradingLUT, 
                                         Settings.Saturation, Settings.Contrast, Settings.ColorFilter);
        }
        
        // 9. Chromatic Aberration - lens distortion effect
        if (Settings.EnableChromaticAberration)
        {
            current = _chromaticAberration.Apply(cmd, current, Settings.ChromaticAberrationIntensity);
        }
        
        // 10. Film Grain - adds texture and cinematic feel
        if (Settings.EnableFilmGrain)
        {
            current = _filmGrain.Apply(cmd, current, Settings.FilmGrainIntensity, deltaTime);
        }
        
        // 11. Vignette - darkens edges for focus
        if (Settings.EnableVignette)
        {
            current = _vignette.Apply(cmd, current, Settings.VignetteIntensity, Settings.VignetteSmoothness);
        }
        
        return current;
    }
    
    private void CreateRenderTargets()
    {
        // HDR intermediate target
        _hdrTarget = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            Format = TextureFormat.RGBA16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "HDR_Intermediate"
        });
        
        // LDR output target
        _ldrTarget = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            Format = TextureFormat.RGBA8Srgb,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "LDR_Output"
        });
        
        // Temporary target for ping-pong
        _tempTarget = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            Format = TextureFormat.RGBA16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "Temp_Target"
        });
    }
    
    private IRHITexture ApplyAO(IRHICommandBuffer cmd, IRHITexture color, IRHITexture ao)
    {
        // TODO: Implement AO application shader
        return color;
    }
    
    private IRHITexture CompositeReflections(IRHICommandBuffer cmd, IRHITexture color, IRHITexture reflections)
    {
        // TODO: Implement reflection compositing shader
        return color;
    }
    
    private string GetProfileName()
    {
        if (Settings.Equals(PostProcessSettings.Cinematic))
            return "Cinematic (Film-like)";
        else if (Settings.Equals(PostProcessSettings.Performance))
            return "Performance (60+ FPS)";
        else if (Settings.Equals(PostProcessSettings.Competitive))
            return "Competitive (Low Latency)";
        else
            return "Custom";
    }
    
    public void Dispose()
    {
        _tonemapper?.Dispose();
        _taa?.Dispose();
        _dof?.Dispose();
        _bloom?.Dispose();
        _colorGrading?.Dispose();
        _filmGrain?.Dispose();
        _vignette?.Dispose();
        _chromaticAberration?.Dispose();
        _ssao?.Dispose();
        _ssr?.Dispose();
        _enhancedMotionBlur?.Dispose();
        _radialBlur?.Dispose();
        
        _hdrTarget?.Dispose();
        _ldrTarget?.Dispose();
        _tempTarget?.Dispose();
    }
}

/// <summary>
/// Post-processing settings with presets for different use cases
/// </summary>
public struct PostProcessSettings
{
    // Core effects
    public bool EnableTAA;
    public bool EnableBloom;
    public bool EnableDOF;
    public bool EnableColorGrading;
    
    // Optional effects
    public bool EnableSSAO;
    public bool EnableSSR;
    public bool EnableMotionBlur;
    public bool EnableFilmGrain;
    public bool EnableVignette;
    public bool EnableChromaticAberration;
    
    // Quality settings
    public SSAOQuality SSAOQuality;
    public SSRQuality SSRQuality;
    public DOFQuality DOFQuality;
    public BloomQuality BloomQuality;
    public MotionBlurQuality MotionBlurQuality;
    
    // Tonemapping
    public float Exposure;
    public float ACESContrast;
    
    // Bloom
    public float BloomThreshold;
    public float BloomIntensity;
    
    // DOF
    public float DOFFocalDistance;
    public float DOFAperture;
    public BokehShape DOFBokehShape;
    
    // Motion Blur
    public float MotionBlurIntensity;
    
    // Radial Blur
    public float RadialBlurIntensity;
    public Vector2 RadialBlurCenter;
    
    // Color Grading
    public string? ColorGradingLUT;
    public float Saturation;
    public float Contrast;
    public Vector3 ColorFilter;
    
    // Film Effects
    public float FilmGrainIntensity;
    public float VignetteIntensity;
    public float VignetteSmoothness;
    public float ChromaticAberrationIntensity;
    
    /// <summary>
    /// Cinematic preset - maximum visual quality, film-like look
    /// Target: 30-60 FPS on mid-range hardware
    /// Use for: Single-player games, cutscenes, photo mode
    /// </summary>
    public static PostProcessSettings Cinematic => new()
    {
        EnableTAA = true,
        EnableBloom = true,
        EnableDOF = true,
        EnableColorGrading = true,
        EnableSSAO = true,
        EnableSSR = true,
        EnableMotionBlur = true,
        EnableFilmGrain = true,
        EnableVignette = true,
        EnableChromaticAberration = true,
        
        SSAOQuality = SSAOQuality.High,
        SSRQuality = SSRQuality.High,
        DOFQuality = DOFQuality.High,
        BloomQuality = BloomQuality.High,
        MotionBlurQuality = MotionBlurQuality.High,
        
        Exposure = 1.0f,
        ACESContrast = 1.0f,
        BloomThreshold = 1.0f,
        BloomIntensity = 0.15f,
        DOFFocalDistance = 10.0f,
        DOFAperture = 2.8f,
        DOFBokehShape = BokehShape.Hexagon,
        MotionBlurIntensity = 0.5f,
        Saturation = 1.1f,
        Contrast = 1.05f,
        ColorFilter = Vector3.One,
        FilmGrainIntensity = 0.03f,
        VignetteIntensity = 0.3f,
        VignetteSmoothness = 0.5f,
        ChromaticAberrationIntensity = 0.5f
    };
    
    /// <summary>
    /// Performance preset - balanced quality and speed
    /// Target: 60 FPS on mid-range hardware
    /// Use for: Most games, default setting
    /// </summary>
    public static PostProcessSettings Performance => new()
    {
        EnableTAA = true,
        EnableBloom = true,
        EnableDOF = false,
        EnableColorGrading = true,
        EnableSSAO = true,
        EnableSSR = false,
        EnableMotionBlur = false,
        EnableFilmGrain = false,
        EnableVignette = true,
        EnableChromaticAberration = false,
        
        SSAOQuality = SSAOQuality.Medium,
        SSRQuality = SSRQuality.Low,
        DOFQuality = DOFQuality.Medium,
        BloomQuality = BloomQuality.Medium,
        MotionBlurQuality = MotionBlurQuality.Low,
        
        Exposure = 1.0f,
        ACESContrast = 1.0f,
        BloomThreshold = 1.2f,
        BloomIntensity = 0.1f,
        DOFFocalDistance = 10.0f,
        DOFAperture = 5.6f,
        DOFBokehShape = BokehShape.Circle,
        MotionBlurIntensity = 0.3f,
        Saturation = 1.0f,
        Contrast = 1.0f,
        ColorFilter = Vector3.One,
        FilmGrainIntensity = 0.0f,
        VignetteIntensity = 0.2f,
        VignetteSmoothness = 0.4f,
        ChromaticAberrationIntensity = 0.0f
    };
    
    /// <summary>
    /// Competitive preset - minimum latency, maximum clarity
    /// Target: 144+ FPS on high-end hardware
    /// Use for: Competitive multiplayer, esports
    /// </summary>
    public static PostProcessSettings Competitive => new()
    {
        EnableTAA = false, // TAA adds latency
        EnableBloom = false,
        EnableDOF = false,
        EnableColorGrading = false,
        EnableSSAO = false,
        EnableSSR = false,
        EnableMotionBlur = false, // Motion blur reduces clarity
        EnableFilmGrain = false,
        EnableVignette = false,
        EnableChromaticAberration = false,
        
        SSAOQuality = SSAOQuality.Low,
        SSRQuality = SSRQuality.Low,
        DOFQuality = DOFQuality.Low,
        BloomQuality = BloomQuality.Low,
        MotionBlurQuality = MotionBlurQuality.Low,
        
        Exposure = 1.0f,
        ACESContrast = 1.0f,
        BloomThreshold = 2.0f,
        BloomIntensity = 0.0f,
        DOFFocalDistance = 10.0f,
        DOFAperture = 16.0f,
        DOFBokehShape = BokehShape.Circle,
        MotionBlurIntensity = 0.0f,
        Saturation = 1.0f,
        Contrast = 1.0f,
        ColorFilter = Vector3.One,
        FilmGrainIntensity = 0.0f,
        VignetteIntensity = 0.0f,
        VignetteSmoothness = 0.0f,
        ChromaticAberrationIntensity = 0.0f
    };
}

public enum DOFQuality { Low, Medium, High, Ultra }
public enum BloomQuality { Low, Medium, High, Ultra }
public enum MotionBlurQuality { Low, Medium, High }
public enum BokehShape { Circle, Hexagon, Octagon }
