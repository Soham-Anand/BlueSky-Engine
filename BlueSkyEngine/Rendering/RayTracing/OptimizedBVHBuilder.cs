using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.RayTracing;

/// <summary>
/// Optimized BVH builder with adaptive quality
/// </summary>
public class OptimizedBVHBuilder
{
    private readonly RTTierConfig _config;
    private readonly bool _useSimplification;
    private readonly int _maxDepth;
    
    public OptimizedBVHBuilder(RTTierConfig config)
    {
        _config = config;
        _useSimplification = config.UseBVHSimplification;
        _maxDepth = config.BVHMaxDepth;
        
        Console.WriteLine("[OptimizedBVH] Configuration:");
        Console.WriteLine($"  Simplification: {_useSimplification}");
        Console.WriteLine($"  Max Depth: {_maxDepth}");
    }
    
    /// <summary>
    /// Build BVH with adaptive quality
    /// </summary>
    public BVHBuildResult Build(Triangle[] triangles)
    {
        var startTime = DateTime.UtcNow;
        
        Console.WriteLine($"[OptimizedBVH] Building BVH for {triangles.Length:N0} triangles...");
        
        BVHBuildResult result;
        
        if (_useSimplification)
        {
            // Ultra-low quality: 2-level hierarchy
            result = BuildSimplifiedBVH(triangles);
        }
        else
        {
            // Standard quality: Full SAH BVH
            result = BuildStandardBVH(triangles);
        }
        
        var buildTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        Console.WriteLine($"[OptimizedBVH] Build complete in {buildTime:F2}ms");
        Console.WriteLine($"  Nodes: {result.NodeCount:N0}");
        Console.WriteLine($"  Depth: {result.MaxDepth}");
        Console.WriteLine($"  Memory: {result.MemoryMB:F2} MB");
        Console.WriteLine($"  Triangles per Leaf: {result.AvgTrianglesPerLeaf:F1}");
        
        return result;
    }
    
    /// <summary>
    /// Build simplified BVH for ultra-low-end hardware
    /// EXTREME MODE: Flat array with spatial hashing
    /// </summary>
    private BVHBuildResult BuildSimplifiedBVH(Triangle[] triangles)
    {
        Console.WriteLine("[OptimizedBVH] EXTREME MODE: Building flat spatial hash BVH...");
        
        // EXTREME: Skip hierarchy entirely, use flat spatial grid
        int gridSize = Math.Max(4, (int)Math.Pow(triangles.Length / 16, 1.0/3.0)); // Cube root
        var grid = new Dictionary<Vector3Int, List<Triangle>>();
        
        // Find scene bounds
        var sceneBounds = CalculateBounds(triangles, 0, triangles.Length);
        Vector3 cellSize = (sceneBounds.Max - sceneBounds.Min) / gridSize;
        
        Console.WriteLine($"  Grid: {gridSize}×{gridSize}×{gridSize} cells");
        Console.WriteLine($"  Cell size: {cellSize.X:F2}×{cellSize.Y:F2}×{cellSize.Z:F2}");
        
        // Assign triangles to grid cells
        foreach (var tri in triangles)
        {
            Vector3 center = (tri.V0 + tri.V1 + tri.V2) / 3.0f;
            Vector3Int cell = new Vector3Int(
                Math.Clamp((int)((center.X - sceneBounds.Min.X) / cellSize.X), 0, gridSize - 1),
                Math.Clamp((int)((center.Y - sceneBounds.Min.Y) / cellSize.Y), 0, gridSize - 1),
                Math.Clamp((int)((center.Z - sceneBounds.Min.Z) / cellSize.Z), 0, gridSize - 1)
            );
            
            if (!grid.ContainsKey(cell))
                grid[cell] = new List<Triangle>();
            
            grid[cell].Add(tri);
        }
        
        // Create one leaf node per non-empty cell
        var nodes = new List<BVHNode>();
        int totalTriangles = 0;
        
        foreach (var kvp in grid)
        {
            if (kvp.Value.Count == 0) continue;
            
            var cellBounds = CalculateBounds(kvp.Value.ToArray(), 0, kvp.Value.Count);
            nodes.Add(new BVHNode
            {
                Bounds = cellBounds,
                IsLeaf = true,
                PrimitiveOffset = (short)totalTriangles,
                PrimitiveCount = (short)Math.Min(kvp.Value.Count, short.MaxValue)
            });
            
            totalTriangles += kvp.Value.Count;
        }
        
        Console.WriteLine($"  Created {nodes.Count} leaf nodes (avg {(float)triangles.Length / nodes.Count:F1} triangles each)");
        Console.WriteLine($"  Memory: {nodes.Count * Marshal.SizeOf<BVHNode>() / 1024.0f:F2} KB (vs {triangles.Length * 64 / 1024.0f:F2} KB standard)");
        Console.WriteLine($"  Reduction: {100.0f * (1.0f - nodes.Count * Marshal.SizeOf<BVHNode>() / (float)(triangles.Length * 64)):F1}%");
        
        return new BVHBuildResult
        {
            CoarseNodes = null,
            FineNodes = nodes.ToArray(),
            NodeCount = nodes.Count,
            MaxDepth = 1, // Flat structure
            MemoryMB = nodes.Count * Marshal.SizeOf<BVHNode>() / 1024.0f / 1024.0f,
            AvgTrianglesPerLeaf = (float)triangles.Length / nodes.Count,
            IsSimplified = true
        };
    }
    
    /// <summary>
    /// Build standard SAH BVH
    /// </summary>
    private BVHBuildResult BuildStandardBVH(Triangle[] triangles)
    {
        Console.WriteLine("[OptimizedBVH] Building standard SAH BVH...");
        
        var nodes = new List<BVHNode>();
        int maxDepth = 0;
        
        BuildRecursive(triangles, nodes, 0, triangles.Length, 0, ref maxDepth);
        
        return new BVHBuildResult
        {
            CoarseNodes = null,
            FineNodes = nodes.ToArray(),
            NodeCount = nodes.Count,
            MaxDepth = maxDepth,
            MemoryMB = nodes.Count * Marshal.SizeOf<BVHNode>() / 1024.0f / 1024.0f,
            AvgTrianglesPerLeaf = (float)triangles.Length / CountLeaves(nodes),
            IsSimplified = false
        };
    }
    
    /// <summary>
    /// Cluster triangles spatially
    /// </summary>
    private List<TriangleCluster> ClusterTriangles(Triangle[] triangles, int clusterSize)
    {
        var clusters = new List<TriangleCluster>();
        
        // Simple spatial clustering using grid
        var grid = new Dictionary<Vector3Int, List<Triangle>>();
        
        // Find scene bounds
        var bounds = CalculateBounds(triangles, 0, triangles.Length);
        Vector3 gridSize = (bounds.Max - bounds.Min) / 8.0f; // 8x8x8 grid
        
        // Assign triangles to grid cells
        foreach (var tri in triangles)
        {
            Vector3 center = (tri.V0 + tri.V1 + tri.V2) / 3.0f;
            Vector3Int cell = new Vector3Int(
                (int)((center.X - bounds.Min.X) / gridSize.X),
                (int)((center.Y - bounds.Min.Y) / gridSize.Y),
                (int)((center.Z - bounds.Min.Z) / gridSize.Z)
            );
            
            if (!grid.ContainsKey(cell))
                grid[cell] = new List<Triangle>();
            
            grid[cell].Add(tri);
        }
        
        // Create clusters from grid cells
        foreach (var cell in grid.Values)
        {
            if (cell.Count == 0) continue;
            
            var cluster = new TriangleCluster
            {
                Triangles = cell,
                Bounds = CalculateBounds(cell.ToArray(), 0, cell.Count)
            };
            
            clusters.Add(cluster);
        }
        
        return clusters;
    }
    
    /// <summary>
    /// Build coarse BVH over clusters
    /// </summary>
    private void BuildCoarseBVH(List<TriangleCluster> clusters, List<BVHNode> nodes, int start, int end, int depth)
    {
        if (end - start <= 1 || depth >= 4) // Max 4 depth for coarse BVH
        {
            // Leaf node
            nodes.Add(CreateClusterLeafNode(clusters, start, end));
            return;
        }
        
        // Find best split using SAH
        int splitAxis = 0;
        float splitPos = 0.0f;
        float bestCost = float.MaxValue;
        
        var bounds = CalculateClusterBounds(clusters, start, end);
        Vector3 extent = bounds.Max - bounds.Min;
        
        // Try each axis
        for (int axis = 0; axis < 3; axis++)
        {
            // Try split at median
            float median = (bounds.Min[axis] + bounds.Max[axis]) * 0.5f;
            float cost = EvaluateClusterSplit(clusters, start, end, axis, median);
            
            if (cost < bestCost)
            {
                bestCost = cost;
                splitAxis = axis;
                splitPos = median;
            }
        }
        
        // Partition clusters
        int mid = PartitionClusters(clusters, start, end, splitAxis, splitPos);
        
        if (mid == start || mid == end)
        {
            // Partition failed - make leaf
            nodes.Add(CreateClusterLeafNode(clusters, start, end));
            return;
        }
        
        // Create interior node
        int nodeIndex = nodes.Count;
        nodes.Add(new BVHNode { Bounds = bounds, IsLeaf = false });
        
        // Recurse
        BuildCoarseBVH(clusters, nodes, start, mid, depth + 1);
        BuildCoarseBVH(clusters, nodes, mid, end, depth + 1);
    }
    
    /// <summary>
    /// Build fine BVH within cluster
    /// </summary>
    private void BuildFineBVH(List<Triangle> triangles, List<BVHNode> nodes, int start, int end, int depth, int maxDepth)
    {
        if (end - start <= 4 || depth >= maxDepth)
        {
            // Leaf node
            nodes.Add(CreateLeafNode(triangles));
            return;
        }
        
        // Simple median split (faster than SAH for small clusters)
        var bounds = CalculateBounds(triangles.ToArray(), start, end);
        Vector3 extent = bounds.Max - bounds.Min;
        int splitAxis = extent.X > extent.Y ? (extent.X > extent.Z ? 0 : 2) : (extent.Y > extent.Z ? 1 : 2);
        
        float splitPos = (bounds.Min[splitAxis] + bounds.Max[splitAxis]) * 0.5f;
        
        // Partition
        int mid = start;
        for (int i = start; i < end; i++)
        {
            Vector3 center = (triangles[i].V0 + triangles[i].V1 + triangles[i].V2) / 3.0f;
            if (center[splitAxis] < splitPos)
            {
                var temp = triangles[mid];
                triangles[mid] = triangles[i];
                triangles[i] = temp;
                mid++;
            }
        }
        
        if (mid == start || mid == end)
            mid = (start + end) / 2;
        
        // Create interior node
        int nodeIndex = nodes.Count;
        nodes.Add(new BVHNode { Bounds = bounds, IsLeaf = false });
        
        // Recurse
        BuildFineBVH(triangles, nodes, start, mid, depth + 1, maxDepth);
        BuildFineBVH(triangles, nodes, mid, end, depth + 1, maxDepth);
    }
    
    /// <summary>
    /// Build recursive SAH BVH
    /// </summary>
    private void BuildRecursive(Triangle[] triangles, List<BVHNode> nodes, int start, int end, int depth, ref int maxDepth)
    {
        maxDepth = Math.Max(maxDepth, depth);
        
        if (end - start <= 4 || depth >= _maxDepth)
        {
            // Leaf node
            var leafBounds = CalculateBounds(triangles, start, end);
            nodes.Add(new BVHNode
            {
                Bounds = leafBounds,
                IsLeaf = true,
                PrimitiveOffset = (short)start,
                PrimitiveCount = (short)(end - start)
            });
            return;
        }
        
        // Find best split using SAH
        var nodeBounds = CalculateBounds(triangles, start, end);
        int bestAxis = 0;
        float bestPos = 0.0f;
        float bestCost = float.MaxValue;
        
        for (int axis = 0; axis < 3; axis++)
        {
            // Try multiple split positions
            for (int i = 1; i < 8; i++)
            {
                float t = i / 8.0f;
                float pos = nodeBounds.Min[axis] * (1 - t) + nodeBounds.Max[axis] * t;
                float cost = EvaluateSplit(triangles, start, end, axis, pos, nodeBounds);
                
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestPos = pos;
                }
            }
        }
        
        // Partition triangles
        int mid = Partition(triangles, start, end, bestAxis, bestPos);
        
        if (mid == start || mid == end)
        {
            // Partition failed - make leaf
            nodes.Add(new BVHNode
            {
                Bounds = nodeBounds,
                IsLeaf = true,
                PrimitiveOffset = (short)start,
                PrimitiveCount = (short)(end - start)
            });
            return;
        }
        
        // Create interior node
        int nodeIndex = nodes.Count;
        nodes.Add(new BVHNode { Bounds = nodeBounds, IsLeaf = false });
        
        // Recurse
        BuildRecursive(triangles, nodes, start, mid, depth + 1, ref maxDepth);
        BuildRecursive(triangles, nodes, mid, end, depth + 1, ref maxDepth);
    }
    
    private AABB CalculateBounds(Triangle[] triangles, int start, int end)
    {
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        
        for (int i = start; i < end; i++)
        {
            min = Vector3.Min(min, Vector3.Min(Vector3.Min(triangles[i].V0, triangles[i].V1), triangles[i].V2));
            max = Vector3.Max(max, Vector3.Max(Vector3.Max(triangles[i].V0, triangles[i].V1), triangles[i].V2));
        }
        
        return new AABB { Min = min, Max = max };
    }
    
    private AABB CalculateClusterBounds(List<TriangleCluster> clusters, int start, int end)
    {
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        
        for (int i = start; i < end; i++)
        {
            min = Vector3.Min(min, clusters[i].Bounds.Min);
            max = Vector3.Max(max, clusters[i].Bounds.Max);
        }
        
        return new AABB { Min = min, Max = max };
    }
    
    private float EvaluateSplit(Triangle[] triangles, int start, int end, int axis, float pos, AABB bounds)
    {
        int leftCount = 0, rightCount = 0;
        AABB leftBox = new AABB { Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
        AABB rightBox = new AABB { Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
        
        for (int i = start; i < end; i++)
        {
            Vector3 center = (triangles[i].V0 + triangles[i].V1 + triangles[i].V2) / 3.0f;
            if (center[axis] < pos)
            {
                leftCount++;
                leftBox.Min = Vector3.Min(leftBox.Min, Vector3.Min(Vector3.Min(triangles[i].V0, triangles[i].V1), triangles[i].V2));
                leftBox.Max = Vector3.Max(leftBox.Max, Vector3.Max(Vector3.Max(triangles[i].V0, triangles[i].V1), triangles[i].V2));
            }
            else
            {
                rightCount++;
                rightBox.Min = Vector3.Min(rightBox.Min, Vector3.Min(Vector3.Min(triangles[i].V0, triangles[i].V1), triangles[i].V2));
                rightBox.Max = Vector3.Max(rightBox.Max, Vector3.Max(Vector3.Max(triangles[i].V0, triangles[i].V1), triangles[i].V2));
            }
        }
        
        if (leftCount == 0 || rightCount == 0)
            return float.MaxValue;
        
        float leftArea = SurfaceArea(leftBox);
        float rightArea = SurfaceArea(rightBox);
        float parentArea = SurfaceArea(bounds);
        
        return (leftArea * leftCount + rightArea * rightCount) / parentArea;
    }
    
    private float EvaluateClusterSplit(List<TriangleCluster> clusters, int start, int end, int axis, float pos)
    {
        int leftCount = 0, rightCount = 0;
        
        for (int i = start; i < end; i++)
        {
            Vector3 center = (clusters[i].Bounds.Min + clusters[i].Bounds.Max) * 0.5f;
            if (center[axis] < pos)
                leftCount++;
            else
                rightCount++;
        }
        
        if (leftCount == 0 || rightCount == 0)
            return float.MaxValue;
        
        return leftCount + rightCount;
    }
    
    private int Partition(Triangle[] triangles, int start, int end, int axis, float pos)
    {
        int mid = start;
        for (int i = start; i < end; i++)
        {
            Vector3 center = (triangles[i].V0 + triangles[i].V1 + triangles[i].V2) / 3.0f;
            if (center[axis] < pos)
            {
                var temp = triangles[mid];
                triangles[mid] = triangles[i];
                triangles[i] = temp;
                mid++;
            }
        }
        return mid;
    }
    
    private int PartitionClusters(List<TriangleCluster> clusters, int start, int end, int axis, float pos)
    {
        int mid = start;
        for (int i = start; i < end; i++)
        {
            Vector3 center = (clusters[i].Bounds.Min + clusters[i].Bounds.Max) * 0.5f;
            if (center[axis] < pos)
            {
                var temp = clusters[mid];
                clusters[mid] = clusters[i];
                clusters[i] = temp;
                mid++;
            }
        }
        return mid;
    }
    
    private float SurfaceArea(AABB box)
    {
        Vector3 extent = box.Max - box.Min;
        return 2.0f * (extent.X * extent.Y + extent.Y * extent.Z + extent.Z * extent.X);
    }
    
    private BVHNode CreateLeafNode(List<Triangle> triangles)
    {
        var bounds = CalculateBounds(triangles.ToArray(), 0, triangles.Count);
        return new BVHNode
        {
            Bounds = bounds,
            IsLeaf = true,
            PrimitiveCount = (short)Math.Min(triangles.Count, short.MaxValue)
        };
    }
    
    private BVHNode CreateClusterLeafNode(List<TriangleCluster> clusters, int start, int end)
    {
        var bounds = CalculateClusterBounds(clusters, start, end);
        return new BVHNode
        {
            Bounds = bounds,
            IsLeaf = true,
            PrimitiveCount = (short)Math.Min(end - start, short.MaxValue)
        };
    }
    
    private int CountLeaves(List<BVHNode> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node.IsLeaf)
                count++;
        }
        return count;
    }
}

public struct BVHBuildResult
{
    public BVHNode[]? CoarseNodes;
    public BVHNode[] FineNodes;
    public int NodeCount;
    public int MaxDepth;
    public float MemoryMB;
    public float AvgTrianglesPerLeaf;
    public bool IsSimplified;
}

public class TriangleCluster
{
    public List<Triangle> Triangles = new();
    public AABB Bounds;
}

public struct Vector3Int
{
    public int X, Y, Z;
    
    public Vector3Int(int x, int y, int z)
    {
        X = x; Y = y; Z = z;
    }
    
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override bool Equals(object? obj) => obj is Vector3Int v && v.X == X && v.Y == Y && v.Z == Z;
}
