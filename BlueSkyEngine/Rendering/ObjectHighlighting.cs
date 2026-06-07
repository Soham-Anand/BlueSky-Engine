using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;
using NotBSRenderer;

namespace BlueSky.Rendering;

public class HighlightedEntity
{
    public Entity Entity { get; set; }
    public Vector4 Color { get; set; }
    public float FadeTime { get; set; }
    public float CurrentFade { get; set; }
    public bool IsFadingOut { get; set; }
    
    // Configurable outline properties
    public float OutlineWidth { get; set; } = 1.05f; // Scale factor along normal
}

/// <summary>
/// Manages and renders object highlighting (Runner Vision).
/// </summary>
public class ObjectHighlighting : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _highlightPipeline;
    private IRHIBuffer? _uniformBuffer;
    
    private readonly Dictionary<Entity, HighlightedEntity> _highlighted = new();
    
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct HighlightUniforms
    {
        public Matrix4x4 ViewProjection;
        public Matrix4x4 ModelMatrix;
        public Vector4 Color;
        public float OutlineScale;
        public Vector3 _padding;
    }

    public ObjectHighlighting(IRHIDevice device)
    {
        _device = device;
    }

    public void Initialize()
    {
        CreateUniformBuffer();
        CreatePipeline();
    }

    public void Highlight(Entity entity, Vector4 color, float fadeInTime = 0.2f)
    {
        if (_highlighted.TryGetValue(entity, out var h))
        {
            h.Color = color;
            h.FadeTime = fadeInTime;
            h.IsFadingOut = false;
        }
        else
        {
            _highlighted[entity] = new HighlightedEntity
            {
                Entity = entity,
                Color = color,
                FadeTime = fadeInTime,
                CurrentFade = 0.0f,
                IsFadingOut = false
            };
        }
    }

    public void Unhighlight(Entity entity, float fadeOutTime = 0.2f)
    {
        if (_highlighted.TryGetValue(entity, out var h))
        {
            h.FadeTime = fadeOutTime;
            h.IsFadingOut = true;
        }
    }

    public void Update(float deltaTime)
    {
        var toRemove = new List<Entity>();
        
        foreach (var kvp in _highlighted)
        {
            var h = kvp.Value;
            float fadeSpeed = h.FadeTime > 0 ? 1.0f / h.FadeTime : 1000.0f;
            
            if (h.IsFadingOut)
            {
                h.CurrentFade -= fadeSpeed * deltaTime;
                if (h.CurrentFade <= 0)
                {
                    toRemove.Add(h.Entity);
                }
            }
            else
            {
                h.CurrentFade += fadeSpeed * deltaTime;
                if (h.CurrentFade > 1.0f) h.CurrentFade = 1.0f;
            }
        }
        
        foreach (var entity in toRemove)
        {
            _highlighted.Remove(entity);
        }
    }

    /// <summary>
    /// Renders the outlines. Requires stencil buffer to mask the original object.
    /// </summary>
    public void RenderOutlines(IRHICommandBuffer cmd, World world, Matrix4x4 viewProj)
    {
        if (_highlightPipeline == null || _highlighted.Count == 0) return;

        cmd.SetPipeline(_highlightPipeline);

        foreach (var kvp in _highlighted)
        {
            var h = kvp.Value;
            
            // Need the entity's transform and mesh
            if (world.HasComponent<Core.ECS.Builtin.TransformComponent>(h.Entity) && 
                world.HasComponent<Core.ECS.Builtin.MeshComponent>(h.Entity))
            {
                var transform = world.GetComponent<Core.ECS.Builtin.TransformComponent>(h.Entity);
                var meshComp = world.GetComponent<Core.ECS.Builtin.MeshComponent>(h.Entity);
                
                // Construct the color with alpha for fade
                var finalColor = h.Color;
                finalColor.W *= h.CurrentFade;

                var worldMat = transform.WorldMatrix;
                var sysMat = new System.Numerics.Matrix4x4(
                    worldMat.M11, worldMat.M12, worldMat.M13, worldMat.M14,
                    worldMat.M21, worldMat.M22, worldMat.M23, worldMat.M24,
                    worldMat.M31, worldMat.M32, worldMat.M33, worldMat.M34,
                    worldMat.M41, worldMat.M42, worldMat.M43, worldMat.M44
                );

                var uniforms = new HighlightUniforms
                {
                    ViewProjection = viewProj,
                    ModelMatrix = sysMat,
                    Color = finalColor,
                    OutlineScale = h.OutlineWidth
                };

                _device.UpdateBuffer(_uniformBuffer!, System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref uniforms, 1)));
                
                cmd.SetUniformBuffer(_uniformBuffer!, 0);
                
                // Assuming mesh component gives us buffers. 
                // Actual rendering would require binding the specific mesh buffers.
                // cmd.SetVertexBuffer(meshComp.VertexBuffer);
                // cmd.SetIndexBuffer(meshComp.IndexBuffer, IndexType.UInt32);
                // cmd.DrawIndexed(meshComp.IndexCount);
            }
        }
    }

    private void CreateUniformBuffer()
    {
        _uniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)System.Runtime.InteropServices.Marshal.SizeOf<HighlightUniforms>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "HighlightUniforms"
        });
    }

    private void CreatePipeline()
    {
        // Pipeline should be created with DepthWrite = false, DepthTest = true (or false if rendering through walls)
        // Stencil settings:
        // Render scene objects writing 1 to stencil.
        // Render outline checking if stencil != 1.
    }

    public void Dispose()
    {
        _uniformBuffer?.Dispose();
        _highlightPipeline?.Dispose();
    }
}
