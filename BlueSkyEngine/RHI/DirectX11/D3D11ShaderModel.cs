namespace NotBSRenderer.DirectX11;

/// <summary>
/// Shader Model levels corresponding to DirectX feature levels.
/// Drives compile-time shader variant selection and runtime feature gating.
///
/// INTEL HD 3000 TARGET:
///   Feature Level 10_1 → Shader Model 4.1
///   - No Compute Shaders (requires SM 5.0 / FL 11.0)
///   - No tessellation hull/domain shaders
///   - No UAV access from pixel shader
///   - Geometry shaders available
///   - Max 8 render targets
///   - Max 8192 texture dimensions
/// </summary>
public enum D3D11ShaderModel
{
    /// <summary>SM 4.0 — FL 10.0 minimum. VS/GS/PS only.</summary>
    SM_4_0,

    /// <summary>SM 4.1 — FL 10.1 (Intel HD 3000). Cubemap arrays, gather4.</summary>
    SM_4_1,

    /// <summary>SM 5.0 — FL 11.0. Compute shaders, tessellation, UAVs.</summary>
    SM_5_0,

    /// <summary>SM 5.0+ — FL 11.1. UAVs at every stage, logical blend ops.</summary>
    SM_5_0_Extended
}

/// <summary>
/// Maps feature levels to shader models and hardware limits.
/// UltraRenderer queries this to select render paths and shader variants.
/// </summary>
public static class D3D11ShaderCompatibility
{
    /// <summary>Returns the shader model supported by a given feature level.</summary>
    public static D3D11ShaderModel GetShaderModel(D3D11FeatureLevel level) => level switch
    {
        D3D11FeatureLevel.Level_10_0 => D3D11ShaderModel.SM_4_0,
        D3D11FeatureLevel.Level_10_1 => D3D11ShaderModel.SM_4_1,
        D3D11FeatureLevel.Level_11_0 => D3D11ShaderModel.SM_5_0,
        D3D11FeatureLevel.Level_11_1 => D3D11ShaderModel.SM_5_0_Extended,
        _ => D3D11ShaderModel.SM_4_0
    };

    /// <summary>HLSL compiler target string for fxc/dxc.</summary>
    public static string GetVSTarget(D3D11ShaderModel sm) => sm switch
    {
        D3D11ShaderModel.SM_4_0 => "vs_4_0",
        D3D11ShaderModel.SM_4_1 => "vs_4_1",
        _ => "vs_5_0"
    };

    public static string GetPSTarget(D3D11ShaderModel sm) => sm switch
    {
        D3D11ShaderModel.SM_4_0 => "ps_4_0",
        D3D11ShaderModel.SM_4_1 => "ps_4_1",
        _ => "ps_5_0"
    };

    public static string GetGSTarget(D3D11ShaderModel sm) => sm switch
    {
        D3D11ShaderModel.SM_4_0 => "gs_4_0",
        D3D11ShaderModel.SM_4_1 => "gs_4_1",
        _ => "gs_5_0"
    };

    public static string GetCSTarget(D3D11ShaderModel sm) => sm switch
    {
        >= D3D11ShaderModel.SM_5_0 => "cs_5_0",
        _ => "" // Compute shaders not available below SM 5.0
    };

    /// <summary>Whether compute shaders are available at this shader model.</summary>
    public static bool SupportsCompute(D3D11ShaderModel sm) => sm >= D3D11ShaderModel.SM_5_0;

    /// <summary>Whether hull/domain (tessellation) shaders are available.</summary>
    public static bool SupportsTessellation(D3D11ShaderModel sm) => sm >= D3D11ShaderModel.SM_5_0;

    /// <summary>Whether UAVs can be bound to the pixel shader stage.</summary>
    public static bool SupportsPixelShaderUAV(D3D11ShaderModel sm) => sm >= D3D11ShaderModel.SM_5_0;

    /// <summary>Whether UAVs can be bound at all shader stages (not just PS/CS).</summary>
    public static bool SupportsAllStageUAV(D3D11ShaderModel sm) => sm >= D3D11ShaderModel.SM_5_0_Extended;

    /// <summary>Maximum texture dimension for this shader model.</summary>
    public static uint MaxTextureDimension(D3D11ShaderModel sm) => sm switch
    {
        D3D11ShaderModel.SM_4_0 or D3D11ShaderModel.SM_4_1 => 8192,
        _ => 16384
    };

    /// <summary>Maximum simultaneous render targets.</summary>
    public static uint MaxRenderTargets(D3D11ShaderModel sm) => 8; // All DX10+ support 8 MRT

    /// <summary>Maximum constant buffer slots per stage.</summary>
    public static uint MaxConstantBuffers(D3D11ShaderModel sm) => 14; // D3D11 limit

    /// <summary>Maximum texture/SRV slots per stage.</summary>
    public static uint MaxTextureSlots(D3D11ShaderModel sm) => sm switch
    {
        D3D11ShaderModel.SM_4_0 or D3D11ShaderModel.SM_4_1 => 128,
        _ => 128 // Same for SM5, but UAV slots add more
    };

    /// <summary>Maximum number of UAV slots (CS/PS only, FL 11.0+).</summary>
    public static uint MaxUAVSlots(D3D11ShaderModel sm) => sm switch
    {
        >= D3D11ShaderModel.SM_5_0 => 8,
        _ => 0
    };

    /// <summary>Prints a human-readable capability report for the given feature level.</summary>
    public static void PrintReport(D3D11FeatureLevel level)
    {
        var sm = GetShaderModel(level);
        Console.WriteLine("┌───────────────────────────────────────────────────┐");
        Console.WriteLine($"│  Shader Model:       {sm,-30}│");
        Console.WriteLine($"│  VS Target:          {GetVSTarget(sm),-30}│");
        Console.WriteLine($"│  PS Target:          {GetPSTarget(sm),-30}│");
        Console.WriteLine($"│  Compute Shaders:    {(SupportsCompute(sm) ? "✓" : "✗  (CPU fallback)"),-30}│");
        Console.WriteLine($"│  Tessellation:       {(SupportsTessellation(sm) ? "✓" : "✗  (skip)"),-30}│");
        Console.WriteLine($"│  Pixel Shader UAVs:  {(SupportsPixelShaderUAV(sm) ? "✓" : "✗"),-30}│");
        Console.WriteLine($"│  Max Texture Dim:    {MaxTextureDimension(sm),-30}│");
        Console.WriteLine($"│  Max RT:             {MaxRenderTargets(sm),-30}│");
        Console.WriteLine($"│  Max CB/Stage:       {MaxConstantBuffers(sm),-30}│");
        Console.WriteLine($"│  Max UAV Slots:      {MaxUAVSlots(sm),-30}│");
        Console.WriteLine("└───────────────────────────────────────────────────┘");
    }
}
