using System;
using BlueSky.Platform;
using NotBSRenderer;
using CsCheck;

namespace BlueSky.RHI.Test;

/// <summary>
/// Bug Condition Exploration Test for DirectX 9 Texture Lock Failure
/// 
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// 
/// **Property 1: Bug Condition** - Sampled Textures Fail to Lock with D3DPOOL_DEFAULT
/// 
/// **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
/// **DO NOT attempt to fix the test or the code when it fails**
/// **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
/// **GOAL**: Surface counterexamples that demonstrate the bug exists
/// 
/// This test verifies that creating a texture with TextureUsage.Sampled and calling UploadTexture()
/// succeeds without throwing D3DERR_INVALIDCALL exception (HRESULT: 0x8876086C).
/// 
/// On UNFIXED code, this test will FAIL with "Failed to lock texture surface, HRESULT: 0x8876086C"
/// which proves the bug exists. After the fix is implemented, this test should PASS.
/// </summary>
class TextureLockBugExplorationTest
{
    public static void Run()
    {
        Console.WriteLine("=== DirectX 9 Texture Lock Bug Condition Exploration Test ===\n");
        Console.WriteLine("**CRITICAL**: This test is EXPECTED TO FAIL on unfixed code");
        Console.WriteLine("Failure with HRESULT 0x8876086C confirms the bug exists\n");
        
        // Create a minimal window and device for testing
        var options = WindowOptions.Default;
        options.Title = "DX9 Texture Lock Bug Test";
        options.Width = 800;
        options.Height = 600;
        options.StartVisible = false; // Hidden window for testing
        
        var window = WindowFactory.Create(options);
        var device = RHIDevice.Create(RHIBackend.DirectX9, window);
        
        try
        {
            Console.WriteLine("Running bug condition exploration tests...\n");
            
            // Test Case 1: Font Atlas Creation (R8Unorm format)
            Console.WriteLine("Test Case 1: Font Atlas Creation (TextureUsage.Sampled, TextureFormat.R8Unorm)");
            TestSampledTextureUpload(device, 256, 256, TextureFormat.R8Unorm, "Font Atlas");
            
            // Test Case 2: RGBA Texture Creation
            Console.WriteLine("\nTest Case 2: RGBA Texture Creation (TextureUsage.Sampled, TextureFormat.RGBA8Unorm)");
            TestSampledTextureUpload(device, 256, 256, TextureFormat.RGBA8Unorm, "RGBA Texture");
            
            // Test Case 3: Small Texture (64x64)
            Console.WriteLine("\nTest Case 3: Small Texture (64x64, TextureUsage.Sampled)");
            TestSampledTextureUpload(device, 64, 64, TextureFormat.RGBA8Unorm, "Small Texture");
            
            // Test Case 4: Large Texture (2048x2048)
            Console.WriteLine("\nTest Case 4: Large Texture (2048x2048, TextureUsage.Sampled)");
            TestSampledTextureUpload(device, 2048, 2048, TextureFormat.RGBA8Unorm, "Large Texture");
            
            Console.WriteLine("\n=== ALL TESTS PASSED ===");
            Console.WriteLine("This means the bug has been FIXED or doesn't exist in this environment.");
            Console.WriteLine("Expected behavior: Sampled textures can be locked and uploaded successfully.\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n=== TEST FAILED (EXPECTED ON UNFIXED CODE) ===");
            Console.WriteLine($"Exception: {ex.Message}");
            Console.WriteLine($"Type: {ex.GetType().Name}");
            
            if (ex.Message.Contains("0x8876086C") || ex.Message.Contains("Failed to lock texture surface"))
            {
                Console.WriteLine("\n**COUNTEREXAMPLE FOUND**: This confirms the bug exists!");
                Console.WriteLine("Root Cause: D3DPOOL_DEFAULT textures cannot be locked in DirectX 9");
                Console.WriteLine("Expected Fix: Use D3DPOOL_MANAGED for TextureUsage.Sampled textures\n");
            }
            else
            {
                Console.WriteLine("\nUnexpected error - this may indicate a different issue.");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}\n");
            }
            
            throw; // Re-throw to mark test as failed
        }
        finally
        {
            device.Dispose();
            window.Dispose();
        }
    }
    
    private static void TestSampledTextureUpload(IRHIDevice device, uint width, uint height, TextureFormat format, string testName)
    {
        Console.WriteLine($"  Creating {width}x{height} texture with {format}...");
        
        // Create texture with TextureUsage.Sampled (the bug condition)
        var texture = device.CreateTexture(new TextureDesc
        {
            Width = width,
            Height = height,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Usage = TextureUsage.Sampled // This is the bug condition!
        });
        
        try
        {
            // Calculate data size based on format
            int bytesPerPixel = format switch
            {
                TextureFormat.R8Unorm => 1,
                TextureFormat.RGBA8Unorm => 4,
                _ => throw new NotSupportedException($"Format {format} not supported in test")
            };
            
            int dataSize = (int)(width * height * bytesPerPixel);
            byte[] data = new byte[dataSize];
            
            // Fill with test pattern
            for (int i = 0; i < dataSize; i++)
            {
                data[i] = (byte)(i % 256);
            }
            
            Console.WriteLine($"  Uploading {dataSize} bytes of texture data...");
            
            // This should FAIL on unfixed code with HRESULT 0x8876086C
            device.UploadTexture(texture, data);
            
            Console.WriteLine($"  ✓ {testName} upload succeeded!");
        }
        finally
        {
            texture.Dispose();
        }
    }
}
