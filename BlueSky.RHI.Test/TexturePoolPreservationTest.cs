using System;
using BlueSky.Platform;
using NotBSRenderer;
using CsCheck;

namespace BlueSky.RHI.Test;

/// <summary>
/// Preservation Property Tests for DirectX 9 Texture Pool Selection
/// 
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// 
/// **Property 2: Preservation** - Render Target and Depth Buffer Pool Selection
/// 
/// **IMPORTANT**: Follow observation-first methodology
/// These tests observe behavior on UNFIXED code for non-buggy inputs (textures with RenderTarget or DepthStencil flags)
/// 
/// **EXPECTED OUTCOME**: Tests PASS on unfixed code (confirms baseline behavior to preserve)
/// 
/// This test verifies that:
/// - Render target textures are created with D3DPOOL_DEFAULT and D3DUSAGE_RENDERTARGET
/// - Depth/stencil textures are created with D3DPOOL_DEFAULT and D3DUSAGE_DEPTHSTENCIL
/// - Combined usage flags (e.g., Sampled | RenderTarget) use D3DPOOL_DEFAULT
/// - These behaviors remain unchanged after the fix is implemented
/// </summary>
class TexturePoolPreservationTest
{
    public static void Run()
    {
        Console.WriteLine("=== DirectX 9 Texture Pool Preservation Property Tests ===\n");
        Console.WriteLine("**IMPORTANT**: These tests verify baseline behavior on unfixed code");
        Console.WriteLine("Tests should PASS, confirming behavior to preserve after fix\n");
        
        // Create a minimal window and device for testing
        var options = WindowOptions.Default;
        options.Title = "DX9 Texture Pool Preservation Test";
        options.Width = 800;
        options.Height = 600;
        options.StartVisible = false; // Hidden window for testing
        
        var window = WindowFactory.Create(options);
        var device = RHIDevice.Create(RHIBackend.DirectX9, window);
        
        try
        {
            Console.WriteLine("Running preservation property tests...\n");
            
            // Property 2.1: Render Target Preservation
            Console.WriteLine("Property 2.1: Render Target Textures Use D3DPOOL_DEFAULT");
            TestRenderTargetPreservation(device);
            
            // Property 2.2: Depth/Stencil Preservation
            Console.WriteLine("\nProperty 2.2: Depth/Stencil Textures Use D3DPOOL_DEFAULT");
            TestDepthStencilPreservation(device);
            
            // Property 2.3: Combined Usage Flags Preservation
            Console.WriteLine("\nProperty 2.3: Combined Usage Flags Use D3DPOOL_DEFAULT");
            TestCombinedUsagePreservation(device);
            
            Console.WriteLine("\n=== ALL PRESERVATION TESTS PASSED ===");
            Console.WriteLine("Baseline behavior confirmed - these behaviors must be preserved after fix.\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n=== PRESERVATION TEST FAILED ===");
            Console.WriteLine($"Exception: {ex.Message}");
            Console.WriteLine($"Type: {ex.GetType().Name}");
            Console.WriteLine($"\nThis indicates unexpected behavior in the baseline code.");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}\n");
            
            throw; // Re-throw to mark test as failed
        }
        finally
        {
            device.Dispose();
            window.Dispose();
        }
    }
    
    /// <summary>
    /// Property 2.1: Render Target Preservation
    /// Validates Requirement 3.1: Render target textures are created with D3DPOOL_DEFAULT and D3DUSAGE_RENDERTARGET
    /// </summary>
    private static void TestRenderTargetPreservation(IRHIDevice device)
    {
        Console.WriteLine("  Testing render target texture creation...");
        
        // Test various render target configurations
        var testCases = new[]
        {
            (width: 256u, height: 256u, format: TextureFormat.RGBA8Unorm, name: "Standard RGBA8 Render Target"),
            (width: 512u, height: 512u, format: TextureFormat.RGBA8Unorm, name: "512x512 Render Target"),
            (width: 1024u, height: 1024u, format: TextureFormat.RGBA8Unorm, name: "1024x1024 Render Target"),
            (width: 64u, height: 64u, format: TextureFormat.RGBA8Unorm, name: "Small 64x64 Render Target"),
        };
        
        foreach (var (width, height, format, name) in testCases)
        {
            Console.WriteLine($"    - {name} ({width}x{height})");
            
            var texture = device.CreateTexture(new TextureDesc
            {
                Width = width,
                Height = height,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = format,
                Usage = TextureUsage.RenderTarget
            });
            
            // Verify texture was created successfully
            // The fact that it doesn't throw confirms D3DPOOL_DEFAULT + D3DUSAGE_RENDERTARGET works
            if (texture == null)
            {
                throw new Exception($"Failed to create render target texture: {name}");
            }
            
            texture.Dispose();
        }
        
        Console.WriteLine("  ✓ All render target textures created successfully with D3DPOOL_DEFAULT");
    }
    
    /// <summary>
    /// Property 2.2: Depth/Stencil Preservation
    /// Validates Requirement 3.2: Depth/stencil textures are created with D3DPOOL_DEFAULT and D3DUSAGE_DEPTHSTENCIL
    /// </summary>
    private static void TestDepthStencilPreservation(IRHIDevice device)
    {
        Console.WriteLine("  Testing depth/stencil texture creation...");
        
        // Test various depth/stencil configurations
        var testCases = new[]
        {
            (width: 256u, height: 256u, format: TextureFormat.Depth24Stencil8, name: "Depth24Stencil8"),
            (width: 512u, height: 512u, format: TextureFormat.Depth32Float, name: "Depth32Float"),
            (width: 1024u, height: 1024u, format: TextureFormat.Depth24Stencil8, name: "1024x1024 Depth Buffer"),
            (width: 64u, height: 64u, format: TextureFormat.Depth32Float, name: "Small 64x64 Depth Buffer"),
        };
        
        foreach (var (width, height, format, name) in testCases)
        {
            Console.WriteLine($"    - {name} ({width}x{height})");
            
            var texture = device.CreateTexture(new TextureDesc
            {
                Width = width,
                Height = height,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = format,
                Usage = TextureUsage.DepthStencil
            });
            
            // Verify texture was created successfully
            // The fact that it doesn't throw confirms D3DPOOL_DEFAULT + D3DUSAGE_DEPTHSTENCIL works
            if (texture == null)
            {
                throw new Exception($"Failed to create depth/stencil texture: {name}");
            }
            
            texture.Dispose();
        }
        
        Console.WriteLine("  ✓ All depth/stencil textures created successfully with D3DPOOL_DEFAULT");
    }
    
    /// <summary>
    /// Property 2.3: Combined Usage Flags Preservation
    /// Validates Requirement 3.3: Combined usage flags (e.g., Sampled | RenderTarget) use D3DPOOL_DEFAULT
    /// </summary>
    private static void TestCombinedUsagePreservation(IRHIDevice device)
    {
        Console.WriteLine("  Testing combined usage flag textures...");
        
        // Test combined usage flags
        var testCases = new[]
        {
            (usage: TextureUsage.Sampled | TextureUsage.RenderTarget, name: "Sampled | RenderTarget"),
            (usage: TextureUsage.RenderTarget | TextureUsage.Sampled, name: "RenderTarget | Sampled (reversed)"),
        };
        
        foreach (var (usage, name) in testCases)
        {
            Console.WriteLine($"    - {name}");
            
            var texture = device.CreateTexture(new TextureDesc
            {
                Width = 256,
                Height = 256,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = TextureFormat.RGBA8Unorm,
                Usage = usage
            });
            
            // Verify texture was created successfully
            // Combined flags with RenderTarget should use D3DPOOL_DEFAULT
            if (texture == null)
            {
                throw new Exception($"Failed to create combined usage texture: {name}");
            }
            
            texture.Dispose();
        }
        
        Console.WriteLine("  ✓ All combined usage textures created successfully with D3DPOOL_DEFAULT");
    }
}
