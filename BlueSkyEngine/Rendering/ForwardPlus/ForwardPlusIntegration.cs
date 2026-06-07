using System;
using System.Numerics;
using NotBSRenderer;
using BlueSky.Rendering.Lighting;
using BlueSky.Rendering.Compute;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using SysVec3 = System.Numerics.Vector3;
using SysMat4 = System.Numerics.Matrix4x4;
using BSVec3 = BlueSky.Core.Math.Vector3;
using BSMat4 = BlueSky.Core.Math.Matrix4x4;

namespace BlueSky.Rendering.ForwardPlus;

/// <summary>
/// Integration layer for Forward+ rendering in BlueSky Engine
/// Bridges the gap between ECS, lighting system, and RHI
/// Provides automatic quality scaling from DX11 Feature Level 10.0 to modern APIs
/// </summary>
public class ForwardPlusIntegration : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly ForwardPlusRenderer _forwardPlusRenderer;
    private readonly BindlessResourceManager _bindlessManager;
    private readonly ComputeSystem _computeSystem;
    private readonly HorizonLighting _horizonLighting;
    
    private IRHIPipeline? _mainPipeline;
    private IRHIBuffer? _sceneDataBuffer;
    private IRHIBuffer? _objectDataBuffer;
    
    public RenderingPath CurrentPath { get; private set; }
    public bool IsInitialized { get; private set; }
    
    public ForwardPlusIntegration(IRHIDevice device)
    {
        _device = device;
        
        // Detect capabilities and select rendering path
        CurrentPath = RHICapabilityDetector.GetRecommendedPath(device.Capabilities);
        
        // Initialize subsystems
        _bindlessManager = new BindlessResourceManager(device);
        _computeSystem = new ComputeSystem(device);
        _horizonLighting = new HorizonLighting();
        
        // Get appropriate cluster config
        var clusterConfig = QualityPresets.GetClusterConfig(device.Capabilities);
        _forwardPlusRenderer = new ForwardPlusRenderer(device, clusterConfig);
        
        // Set lighting quality based on capabilities
        var lightingQuality = GetLightingQuality(device.Capabilities);
        _horizonLighting.SetQuality(lightingQuality);
        
        Console.WriteLine($"[ForwardPlusIntegration] Initialized with path: {CurrentPath}");
        Console.WriteLine($"[ForwardPlusIntegration] Lighting quality: {lightingQuality}");
        
        PrintCapabilityReport();
    }
    
    /// <summary>
    /// Initialize rendering resources
    /// </summary>
    public void Initialize(uint screenWidth, uint screenHeight)
    {
        // Create uniform buffers
        _sceneDataBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 512, // Scene data size
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "SceneDataBuffer"
        });
        
        _objectDataBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 256, // Object data size
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "ObjectDataBuffer"
        });
        
        // Create main rendering pipeline
        // TODO: Load compiled shaders based on backend
        // _mainPipeline = CreateMainPipeline();
        
        IsInitialized = true;
    }
    
    /// <summary>
    /// Render a frame using Forward+ rendering
    /// </summary>
    public void RenderFrame(IRHICommandBuffer cmd, World world, 
                           CameraComponent camera, TransformComponent cameraTransform,
                           IRHITexture colorTarget, IRHITexture depthTarget,
                           uint screenWidth, uint screenHeight)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ForwardPlusIntegration not initialized");
        
        // Build view and projection matrices
        Matrix4x4 viewMatrix = BuildViewMatrix(cameraTransform);
        Matrix4x4 projMatrix = BuildProjectionMatrix(camera, screenWidth, screenHeight);
        Matrix4x4 viewProjMatrix = viewMatrix * projMatrix;
        
        // Collect lights from ECS
        var lights = CollectLights(world);
        
        // Update lighting system
        _horizonLighting.PrepareFrame(
            ToSysVec3(cameraTransform.Position),
            viewProjMatrix,
            (int)screenWidth,
            (int)screenHeight,
            camera.NearPlane,
            camera.FarPlane
        );
        
        // Perform light culling (GPU or CPU based on capabilities)
        _forwardPlusRenderer.UpdateLights(
            cmd,
            lights,
            viewMatrix,
            projMatrix,
            camera.NearPlane,
            camera.FarPlane,
            screenWidth,
            screenHeight
        );
        
        // Begin render pass
        cmd.BeginRenderPass(
            new[] { colorTarget },
            depthTarget,
            ClearValue.FromColor(0.05f, 0.05f, 0.1f, 1.0f)
        );
        
        // Set viewport
        cmd.SetViewport(new NotBSRenderer.Viewport
        {
            X = 0,
            Y = 0,
            Width = screenWidth,
            Height = screenHeight,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        });
        
        // Bind main pipeline
        if (_mainPipeline != null)
        {
            cmd.SetPipeline(_mainPipeline);
        }
        
        // Update scene uniforms
        UpdateSceneUniforms(cmd, viewMatrix, projMatrix, viewProjMatrix, 
                          ToSysVec3(cameraTransform.Position), screenWidth, screenHeight,
                          camera.NearPlane, camera.FarPlane);
        
        // Bind cluster data
        _forwardPlusRenderer.BindClusterData(cmd);
        
        // Render all mesh entities
        RenderMeshes(cmd, world, viewProjMatrix);
        
        cmd.EndRenderPass();
    }
    
    private HorizonLight[] CollectLights(World world)
    {
        var lights = new System.Collections.Generic.List<HorizonLight>();
        
        foreach (var entity in world.GetAllEntities())
        {
            if (world.HasComponent<LightComponent>(entity))
            {
                var lightComp = world.GetComponent<LightComponent>(entity);
                var transform = world.HasComponent<TransformComponent>(entity) 
                    ? world.GetComponent<TransformComponent>(entity) 
                    : TransformComponent.Default;
                
                var light = new HorizonLight
                {
                    Type = ConvertLightType(lightComp.Type),
                    Position = ToSysVec3(transform.Position),
                    Direction = ToSysVec3(transform.Forward),
                    Color = ToSysVec3(lightComp.Color),
                    Intensity = lightComp.Intensity,
                    Range = lightComp.Range,
                    IsEnabled = true
                };
                
                lights.Add(light);
            }
        }
        
        return lights.ToArray();
    }
    
    private LightType ConvertLightType(LightComponent.LightType type)
    {
        return type switch
        {
            LightComponent.LightType.Directional => LightType.Directional,
            LightComponent.LightType.Point => LightType.Point,
            LightComponent.LightType.Spot => LightType.Spot,
            _ => LightType.Point
        };
    }
    
    private void RenderMeshes(IRHICommandBuffer cmd, World world, Matrix4x4 viewProjMatrix)
    {
        foreach (var entity in world.GetAllEntities())
        {
            if (world.HasComponent<StaticMeshComponent>(entity) &&
                world.HasComponent<TransformComponent>(entity))
            {
                var mesh = world.GetComponent<StaticMeshComponent>(entity);
                var transform = world.GetComponent<TransformComponent>(entity);
                
                // Update object uniforms
                UpdateObjectUniforms(cmd, ToSysMat4(transform.WorldMatrix));
                
                // Bind mesh resources
                // TODO: Bind vertex/index buffers and textures
                
                // Draw
                // cmd.DrawIndexed(mesh.IndexCount);
            }
        }
    }
    
    private void UpdateSceneUniforms(IRHICommandBuffer cmd, Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                                    Matrix4x4 viewProjMatrix, Vector3 cameraPos,
                                    uint screenWidth, uint screenHeight,
                                    float nearPlane, float farPlane)
    {
        // Pack scene data
        // TODO: Properly marshal scene data struct
        cmd.SetVertexUniforms(0, System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref viewProjMatrix, 1)));
    }
    
    private void UpdateObjectUniforms(IRHICommandBuffer cmd, Matrix4x4 modelMatrix)
    {
        cmd.SetVertexUniforms(1, System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref modelMatrix, 1)));
    }
    
    private Matrix4x4 BuildViewMatrix(TransformComponent cameraTransform)
    {
        return Matrix4x4.CreateLookAt(
            ToSysVec3(cameraTransform.Position),
            ToSysVec3(cameraTransform.Position + cameraTransform.Forward),
            ToSysVec3(cameraTransform.Up)
        );
    }
    
    private Matrix4x4 BuildProjectionMatrix(CameraComponent camera, uint width, uint height)
    {
        float aspect = (float)width / height;
        return Matrix4x4.CreatePerspectiveFieldOfView(
            camera.FieldOfView * MathF.PI / 180.0f,
            aspect,
            camera.NearPlane,
            camera.FarPlane
        );
    }
    
    private LightingQuality GetLightingQuality(RHICapabilities capabilities)
    {
        if (capabilities.HasFlag(RHICapabilities.BindlessResources) &&
            capabilities.HasFlag(RHICapabilities.ComputeShaders))
        {
            return LightingQuality.Ultra;
        }
        else if (capabilities.HasFlag(RHICapabilities.ComputeShaders))
        {
            return LightingQuality.High;
        }
        else
        {
            return LightingQuality.Medium;
        }
    }
    
    // Type conversion helpers
    private static SysVec3 ToSysVec3(BSVec3 v) => new SysVec3(v.X, v.Y, v.Z);
    private static SysMat4 ToSysMat4(BSMat4 m) => new SysMat4(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44
    );
    
    private void PrintCapabilityReport()
    {
        string report = RHICapabilityDetector.GenerateCapabilityReport(_device);
        Console.WriteLine(report);
    }
    
    public void Dispose()
    {
        _forwardPlusRenderer?.Dispose();
        _bindlessManager?.Dispose();
        _computeSystem?.Dispose();
        _mainPipeline?.Dispose();
        _sceneDataBuffer?.Dispose();
        _objectDataBuffer?.Dispose();
    }
}

/// <summary>
/// Light component for ECS
/// </summary>
public struct LightComponent
{
    public LightType Type;
    public BSVec3 Color;
    public float Intensity;
    public float Range;
    
    public enum LightType
    {
        Directional,
        Point,
        Spot
    }
}

/// <summary>
/// Camera component for ECS
/// </summary>
public struct CameraComponent
{
    public float FieldOfView;
    public float NearPlane;
    public float FarPlane;
    public bool IsActive;
}
