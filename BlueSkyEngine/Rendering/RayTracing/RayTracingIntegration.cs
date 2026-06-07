// BlueSkyEngine - Ray Tracing Integration Example
//
// COMPLETE INTEGRATION: From Scene Upload to Final Render
// =========================================================
// This file demonstrates how to integrate ray tracing into UltraRenderer
// Shows the complete pipeline from BVH building to final output
//
// Usage:
// 1. Create IntelligentRTSelector to detect GPU and select backend
// 2. Build BVH from scene geometry
// 3. Create appropriate ray tracer (Hardware or Software)
// 4. Upload scene to GPU
// 5. Trace rays each frame
// 6. Composite RT output with rasterized scene

using System;
using System.Collections.Generic;
using System.Numerics;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Rendering.RayTracing.Polaris;

namespace BlueSky.Rendering.RayTracing;

/// <summary>
/// Ray tracing integration manager
/// Handles RT backend selection, scene upload, and frame rendering
/// </summary>
public class RayTracingIntegration : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly IntelligentRTSelector _rtSelector;
    private readonly RTConfiguration _config;
    
    // Ray tracers (only one will be active)
    private SoftwareRayTracer? _softwareRT;
    private HardwareRayTracer? _hardwareRT;
    private PolarisRayTracer? _polarisRT; // ★ Project Polaris: AVX CPU RT
    
    // Scene data
    private BVH? _bvh;
    private bool _sceneUploaded = false;
    
    // Statistics
    private float _lastFrameTimeMs = 0.0f;
    private int _frameCount = 0;
    
    public RTBackend ActiveBackend => _rtSelector.SelectedBackend;
    public RTQualityPreset ActivePreset => _rtSelector.SelectedPreset;
    public RTConfiguration Configuration => _config;
    public bool IsInitialized => _sceneUploaded;
    public float LastFrameTimeMs => _lastFrameTimeMs;
    
    public RayTracingIntegration(IRHIDevice device)
    {
        _device = device;
        
        // Step 1: Intelligent backend selection
        Console.WriteLine();
        _rtSelector = new IntelligentRTSelector(device);
        _config = _rtSelector.GetRTConfiguration();
        
        // Step 2: Create appropriate ray tracer
        Console.WriteLine();
        Console.WriteLine("[RTIntegration] Creating ray tracer...");
        
        switch (_rtSelector.SelectedBackend)
        {
            case RTBackend.HardwareRT:
                Console.WriteLine("[RTIntegration] Hardware RT selected (DXR/Vulkan RT/Metal RT)");
                _hardwareRT = new HardwareRayTracer(device, _config);
                Console.WriteLine("[RTIntegration] NOTE: Hardware RT is Phase 4 stub - basic functionality only");
                break;
                
            case RTBackend.SoftwareRT:
                Console.WriteLine("[RTIntegration] Software RT selected (Compute Shaders)");
                _softwareRT = new SoftwareRayTracer(device, _config);
                break;
            
            case RTBackend.Polaris:
                if (!BlueSky.Core.Platform.Detection.ProcessorCapabilities.Probe().SupportsAvx)
                {
                    Console.WriteLine("[RTIntegration] Polaris requested but AVX is unavailable. Falling back to screen-space rendering.");
                    break;
                }
                Console.WriteLine("[RTIntegration] ★ PROJECT POLARIS selected (AVX CPU Ray Tracing)");
                _polarisRT = new PolarisRayTracer(device, new PolarisConfig
                {
                    RenderWidth = _config.RenderWidth,
                    RenderHeight = _config.RenderHeight,
                    OutputWidth = _config.OutputWidth,
                    OutputHeight = _config.OutputHeight,
                    UseCheckerboard = true,
                    EnableShadows = _config.EnableRTShadows,
                    ThreadCount = Math.Max(2, Environment.ProcessorCount)
                });
                break;
                
            case RTBackend.ScreenSpace:
                Console.WriteLine("[RTIntegration] Screen-Space techniques only (No RT)");
                Console.WriteLine("[RTIntegration] Ray tracing disabled - using traditional rendering");
                break;
        }
        
        Console.WriteLine("[RTIntegration] Initialization complete");
    }
    
    /// <summary>
    /// Build BVH from scene geometry and upload to GPU
    /// Call this once at startup or when scene changes
    /// </summary>
    public void UploadScene(World world)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine("UPLOADING SCENE FOR RAY TRACING");
        Console.WriteLine("================================================================================");
        
        var startTime = DateTime.UtcNow;
        
        // Step 1: Collect all triangles from scene
        Console.WriteLine("[RTIntegration] Collecting scene geometry...");
        var triangles = CollectSceneTriangles(world);
        Console.WriteLine($"[RTIntegration] Collected {triangles.Count:N0} triangles");
        
        if (triangles.Count == 0)
        {
            Console.WriteLine("[RTIntegration] WARNING: No geometry found in scene!");
            return;
        }
        
        // Step 2: Build BVH
        Console.WriteLine();
        _bvh = new BVH();
        _bvh.Build(triangles.ToArray());
        
        // Step 3: Upload to appropriate ray tracer
        Console.WriteLine();
        if (_polarisRT != null)
        {
            // Polaris builds its own SIMD-optimized BVH internally
            _polarisRT.BuildScene(triangles.ToArray());
        }
        else if (_softwareRT != null)
        {
            _softwareRT.UploadScene(_bvh);
        }
        else if (_hardwareRT != null)
        {
            _hardwareRT.UploadScene(_bvh);
        }
        
        _sceneUploaded = true;
        
        var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
        Console.WriteLine();
        Console.WriteLine($"[RTIntegration] Scene upload complete in {totalTime:F2}ms");
        Console.WriteLine("================================================================================");
    }
    
    /// <summary>
    /// Trace rays for current frame
    /// Call this every frame in your render loop
    /// </summary>
    public void TraceFrame(IRHICommandBuffer cmd, 
                          Matrix4x4 viewMatrix, 
                          Matrix4x4 projMatrix,
                          Vector3 cameraPos)
    {
        if (!_sceneUploaded)
        {
            Console.WriteLine("[RTIntegration] WARNING: Scene not uploaded! Call UploadScene() first.");
            return;
        }
        
        var startTime = DateTime.UtcNow;
        
        if (_polarisRT != null)
        {
            _polarisRT.RenderFrame(cmd, cameraPos, viewMatrix, projMatrix);
        }
        else if (_softwareRT != null)
        {
            _softwareRT.TraceFrame(cmd, viewMatrix, projMatrix, cameraPos);
        }
        else if (_hardwareRT != null)
        {
            _hardwareRT.TraceFrame(cmd, viewMatrix, projMatrix, cameraPos);
        }
        
        _lastFrameTimeMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
        _frameCount++;
        
        // Print stats every 60 frames
        if (_frameCount % 60 == 0)
        {
            if (_polarisRT != null)
                _polarisRT.PrintStats();
            else
                Console.WriteLine($"[RTIntegration] Frame {_frameCount}: {_lastFrameTimeMs:F2}ms ({1000.0f / _lastFrameTimeMs:F1} FPS)");
        }
    }
    
    /// <summary>
    /// Get ray traced output texture for compositing
    /// Returns null if RT is disabled
    /// </summary>
    public IRHITexture? GetOutputTexture()
    {
        if (_polarisRT != null)
            return _polarisRT.GetOutputTexture();
        
        if (_softwareRT != null)
            return _softwareRT.GetOutputTexture();
        
        if (_hardwareRT != null)
            return _hardwareRT.GetOutputTexture();
        
        return null;
    }
    
    /// <summary>
    /// Collect all triangles from scene entities
    /// Converts StaticMeshComponents to triangle soup
    /// </summary>
    private List<Triangle> CollectSceneTriangles(World world)
    {
        var triangles = new List<Triangle>();
        
        // Query all entities with StaticMeshComponent and TransformComponent
        var query = world.CreateQuery()
            .All<StaticMeshComponent>()
            .All<TransformComponent>()
            .Build();
        
        var chunks = world.GetQueryChunks(query);
        
        foreach (var chunk in chunks)
        {
            int meshIndex = chunk.GetComponentIndex(typeof(StaticMeshComponent));
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            
            var meshes = chunk.GetComponentSpan<StaticMeshComponent>(meshIndex);
            var transforms = chunk.GetComponentSpan<TransformComponent>(transformIndex);
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var meshComp = meshes[i];
                var transform = transforms[i];
                
                if (string.IsNullOrEmpty(meshComp.MeshAssetId))
                    continue;
                
                // Get mesh data
                // TODO: Extract vertices and indices from MeshAssetId
                // For now, we'll skip this and assume mesh data is available
                
                // Transform vertices to world space and create triangles
                // triangles.AddRange(ExtractTriangles(meshComp, transform));
            }
        }
        
        // If no triangles found, create a test scene
        if (triangles.Count == 0)
        {
            Console.WriteLine("[RTIntegration] No scene geometry found, creating test scene...");
            triangles.AddRange(CreateTestScene());
        }
        
        return triangles;
    }
    
    /// <summary>
    /// Create a simple test scene for demonstration
    /// Cornell box with a few objects
    /// </summary>
    private List<Triangle> CreateTestScene()
    {
        var triangles = new List<Triangle>();
        
        // Floor (2 triangles)
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 0, -5),
            V1 = new Vector3(5, 0, -5),
            V2 = new Vector3(5, 0, 5),
            N0 = Vector3.UnitY,
            N1 = Vector3.UnitY,
            N2 = Vector3.UnitY,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 0),
            UV2 = new Vector2(1, 1)
        });
        
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 0, -5),
            V1 = new Vector3(5, 0, 5),
            V2 = new Vector3(-5, 0, 5),
            N0 = Vector3.UnitY,
            N1 = Vector3.UnitY,
            N2 = Vector3.UnitY,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 1),
            UV2 = new Vector2(0, 1)
        });
        
        // Ceiling (2 triangles)
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 5, -5),
            V1 = new Vector3(5, 5, 5),
            V2 = new Vector3(5, 5, -5),
            N0 = -Vector3.UnitY,
            N1 = -Vector3.UnitY,
            N2 = -Vector3.UnitY,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 1),
            UV2 = new Vector2(1, 0)
        });
        
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 5, -5),
            V1 = new Vector3(-5, 5, 5),
            V2 = new Vector3(5, 5, 5),
            N0 = -Vector3.UnitY,
            N1 = -Vector3.UnitY,
            N2 = -Vector3.UnitY,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(0, 1),
            UV2 = new Vector2(1, 1)
        });
        
        // Back wall (2 triangles)
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 0, -5),
            V1 = new Vector3(-5, 5, -5),
            V2 = new Vector3(5, 5, -5),
            N0 = Vector3.UnitZ,
            N1 = Vector3.UnitZ,
            N2 = Vector3.UnitZ,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(0, 1),
            UV2 = new Vector2(1, 1)
        });
        
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 0, -5),
            V1 = new Vector3(5, 5, -5),
            V2 = new Vector3(5, 0, -5),
            N0 = Vector3.UnitZ,
            N1 = Vector3.UnitZ,
            N2 = Vector3.UnitZ,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 1),
            UV2 = new Vector2(1, 0)
        });
        
        // Left wall - RED (2 triangles)
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 0, -5),
            V1 = new Vector3(-5, 0, 5),
            V2 = new Vector3(-5, 5, 5),
            N0 = Vector3.UnitX,
            N1 = Vector3.UnitX,
            N2 = Vector3.UnitX,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 0),
            UV2 = new Vector2(1, 1)
        });
        
        triangles.Add(new Triangle
        {
            V0 = new Vector3(-5, 0, -5),
            V1 = new Vector3(-5, 5, 5),
            V2 = new Vector3(-5, 5, -5),
            N0 = Vector3.UnitX,
            N1 = Vector3.UnitX,
            N2 = Vector3.UnitX,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 1),
            UV2 = new Vector2(0, 1)
        });
        
        // Right wall - GREEN (2 triangles)
        triangles.Add(new Triangle
        {
            V0 = new Vector3(5, 0, -5),
            V1 = new Vector3(5, 5, 5),
            V2 = new Vector3(5, 0, 5),
            N0 = -Vector3.UnitX,
            N1 = -Vector3.UnitX,
            N2 = -Vector3.UnitX,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(1, 1),
            UV2 = new Vector2(1, 0)
        });
        
        triangles.Add(new Triangle
        {
            V0 = new Vector3(5, 0, -5),
            V1 = new Vector3(5, 5, -5),
            V2 = new Vector3(5, 5, 5),
            N0 = -Vector3.UnitX,
            N1 = -Vector3.UnitX,
            N2 = -Vector3.UnitX,
            UV0 = new Vector2(0, 0),
            UV1 = new Vector2(0, 1),
            UV2 = new Vector2(1, 1)
        });
        
        // Cube in center (12 triangles)
        AddCube(triangles, new Vector3(0, 1.5f, 0), new Vector3(1, 1, 1));
        
        Console.WriteLine($"[RTIntegration] Created test scene with {triangles.Count} triangles");
        return triangles;
    }
    
    private void AddCube(List<Triangle> triangles, Vector3 center, Vector3 size)
    {
        Vector3 min = center - size * 0.5f;
        Vector3 max = center + size * 0.5f;
        
        // Front face
        AddQuad(triangles,
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            Vector3.UnitZ);
        
        // Back face
        AddQuad(triangles,
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            -Vector3.UnitZ);
        
        // Left face
        AddQuad(triangles,
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(min.X, max.Y, min.Z),
            -Vector3.UnitX);
        
        // Right face
        AddQuad(triangles,
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z),
            Vector3.UnitX);
        
        // Top face
        AddQuad(triangles,
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            Vector3.UnitY);
        
        // Bottom face
        AddQuad(triangles,
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(min.X, min.Y, max.Z),
            -Vector3.UnitY);
    }
    
    private void AddQuad(List<Triangle> triangles, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal)
    {
        // Triangle 1
        triangles.Add(new Triangle
        {
            V0 = v0, V1 = v1, V2 = v2,
            N0 = normal, N1 = normal, N2 = normal,
            UV0 = new Vector2(0, 0), UV1 = new Vector2(1, 0), UV2 = new Vector2(1, 1)
        });
        
        // Triangle 2
        triangles.Add(new Triangle
        {
            V0 = v0, V1 = v2, V2 = v3,
            N0 = normal, N1 = normal, N2 = normal,
            UV0 = new Vector2(0, 0), UV1 = new Vector2(1, 1), UV2 = new Vector2(0, 1)
        });
    }
    
    public void Dispose()
    {
        _polarisRT?.Dispose();
        _softwareRT?.Dispose();
        _hardwareRT?.Dispose();
        _bvh?.Dispose();
    }
}
