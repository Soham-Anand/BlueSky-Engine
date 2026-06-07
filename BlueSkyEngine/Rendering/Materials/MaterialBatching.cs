// BlueSkyEngine - Material Batching System
// Reduces draw calls via static/dynamic batching and GPU instancing

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Material batching system - reduces draw calls dramatically.
/// Combines multiple objects with same material into fewer draw calls.
/// </summary>
public class MaterialBatching
{
    private readonly Dictionary<Guid, List<RenderBatch>> _batches = new();
    private int _totalDrawCalls;
    private int _batchedDrawCalls;
    
    public int TotalDrawCalls => _totalDrawCalls;
    public int BatchedDrawCalls => _batchedDrawCalls;
    public float DrawCallReduction => _totalDrawCalls > 0 ? (1f - (float)_batchedDrawCalls / _totalDrawCalls) * 100f : 0f;
    
    /// <summary>
    /// Batch render objects by material.
    /// </summary>
    public List<RenderBatch> BatchObjects(World world)
    {
        _batches.Clear();
        _totalDrawCalls = 0;
        _batchedDrawCalls = 0;
        
        // Group entities by material
        var materialGroups = new Dictionary<Guid, List<Entity>>();
        
        foreach (var entity in world.GetAllEntities())
        {
            if (!world.HasComponent<StaticMeshComponent>(entity)) continue;
            if (!world.HasComponent<TransformComponent>(entity)) continue;
            
            var mesh = world.GetComponent<StaticMeshComponent>(entity);
            if (!Guid.TryParse(mesh.MaterialAssetId, out var materialId)) continue;
            
            if (!materialGroups.ContainsKey(materialId))
            {
                materialGroups[materialId] = new List<Entity>();
            }
            
            materialGroups[materialId].Add(entity);
            _totalDrawCalls++;
        }
        
        // Create batches for each material
        var allBatches = new List<RenderBatch>();
        
        foreach (var (materialId, entities) in materialGroups)
        {
            var batches = CreateBatches(world, materialId, entities);
            allBatches.AddRange(batches);
            _batchedDrawCalls += batches.Count;
        }
        
        return allBatches;
    }
    
    private List<RenderBatch> CreateBatches(World world, Guid materialId, List<Entity> entities)
    {
        var batches = new List<RenderBatch>();
        var staticEntities = new List<Entity>();
        var dynamicEntities = new List<Entity>();
        
        // Split entities into static and dynamic
        foreach (var entity in entities)
        {
            var mesh = world.GetComponent<StaticMeshComponent>(entity);
            if (mesh.IsStatic)
            {
                staticEntities.Add(entity);
            }
            else
            {
                dynamicEntities.Add(entity);
            }
        }
        
        // Static batching merges meshes permanently (or at least avoids re-evaluating transforms)
        if (staticEntities.Count > 0)
        {
            batches.Add(CreateStaticBatch(world, materialId, staticEntities));
        }
        
        // Dynamic batching uses GPU instancing
        if (dynamicEntities.Count > 0)
        {
            batches.AddRange(CreateDynamicBatches(world, materialId, dynamicEntities));
        }
        
        return batches;
    }
    
    private RenderBatch CreateStaticBatch(World world, Guid materialId, List<Entity> entities)
    {
        // Merge all static meshes into one big mesh
        var transforms = new List<Matrix4x4>();
        
        foreach (var entity in entities)
        {
            var transform = world.GetComponent<TransformComponent>(entity);
            transforms.Add(ToMatrix4x4(transform.WorldMatrix));
        }
        
        return new RenderBatch
        {
            MaterialId = materialId,
            BatchType = BatchType.Static,
            InstanceCount = entities.Count,
            Transforms = transforms.ToArray(),
            Entities = entities.ToArray()
        };
    }
    
    private RenderBatch CreateSingleObjectBatch(World world, Guid materialId, Entity entity)
    {
        var transform = world.GetComponent<TransformComponent>(entity);
        
        return new RenderBatch
        {
            MaterialId = materialId,
            BatchType = BatchType.Single,
            InstanceCount = 1,
            Transforms = new[] { ToMatrix4x4(transform.WorldMatrix) },
            Entities = new[] { entity }
        };
    }
    
    private List<RenderBatch> CreateDynamicBatches(World world, Guid materialId, List<Entity> entities)
    {
        var batches = new List<RenderBatch>();
        
        // Group by mesh (for GPU instancing)
        var meshGroups = entities.GroupBy(e =>
        {
            var mesh = world.GetComponent<StaticMeshComponent>(e);
            return mesh.MeshAssetId;
        });
        
        foreach (var group in meshGroups)
        {
            var groupEntities = group.ToList();
            
            // GPU instancing for identical meshes
            if (groupEntities.Count > 1)
            {
                var transforms = groupEntities.Select(e =>
                {
                    var transform = world.GetComponent<TransformComponent>(e);
                    return ToMatrix4x4(transform.WorldMatrix);
                }).ToArray();
                
                batches.Add(new RenderBatch
                {
                    MaterialId = materialId,
                    BatchType = BatchType.Instanced,
                    InstanceCount = groupEntities.Count,
                    Transforms = transforms,
                    Entities = groupEntities.ToArray()
                });
            }
            else
            {
                // Single object
                batches.Add(CreateSingleObjectBatch(world, materialId, groupEntities[0]));
            }
        }
        
        return batches;
    }
    
    private Matrix4x4 ToMatrix4x4(BlueSky.Core.Math.Matrix4x4 m)
    {
        return new Matrix4x4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        );
    }
}

/// <summary>
/// Render batch - group of objects rendered with one draw call.
/// </summary>
public class RenderBatch
{
    public Guid MaterialId;
    public BatchType BatchType;
    public int InstanceCount;
    public Matrix4x4[] Transforms = Array.Empty<Matrix4x4>();
    public Entity[] Entities = Array.Empty<Entity>();
    
    /// <summary>
    /// Get instance data for GPU upload.
    /// </summary>
    public byte[] GetInstanceData()
    {
        // Pack transforms into byte array for GPU
        int size = Transforms.Length * 64; // 64 bytes per matrix
        byte[] data = new byte[size];
        
        for (int i = 0; i < Transforms.Length; i++)
        {
            var matrix = Transforms[i];
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M11), 0, data, i * 64 + 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M12), 0, data, i * 64 + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M13), 0, data, i * 64 + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M14), 0, data, i * 64 + 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M21), 0, data, i * 64 + 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M22), 0, data, i * 64 + 20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M23), 0, data, i * 64 + 24, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M24), 0, data, i * 64 + 28, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M31), 0, data, i * 64 + 32, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M32), 0, data, i * 64 + 36, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M33), 0, data, i * 64 + 40, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M34), 0, data, i * 64 + 44, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M41), 0, data, i * 64 + 48, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M42), 0, data, i * 64 + 52, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M43), 0, data, i * 64 + 56, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(matrix.M44), 0, data, i * 64 + 60, 4);
        }
        
        return data;
    }
}

/// <summary>
/// Batch type.
/// </summary>
public enum BatchType
{
    Single,     // Single object (no batching)
    Static,     // Static batching (merged mesh)
    Dynamic,    // Dynamic batching (per-frame merge)
    Instanced   // GPU instancing (identical meshes)
}
