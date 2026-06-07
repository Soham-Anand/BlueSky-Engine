// BlueSkyEngine - Bounding Volume Hierarchy (BVH)
//
// THE SECRET SAUCE: 20,000x Speedup for Ray Tracing
// ===================================================
// Without BVH: Test ray against ALL triangles (1 million tests)
// With BVH:    Test ray against tree structure (50 tests)
//
// BVH is a binary tree where:
// - Each node has an axis-aligned bounding box (AABB)
// - Leaf nodes contain triangles
// - Internal nodes contain child nodes
//
// Ray traversal:
// 1. Test ray against root AABB
// 2. If hit, recursively test children
// 3. Only test triangles in leaf nodes that were hit
//
// Build Strategy: Surface Area Heuristic (SAH)
// - Split primitives to minimize expected ray-box intersection cost
// - Used by production renderers (Embree, OptiX, etc.)

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.RayTracing;

/// <summary>
/// Bounding Volume Hierarchy for fast ray-triangle intersection
/// </summary>
public class BVH : IDisposable
{
    private BVHNode[] _nodes;
    private Triangle[] _triangles;
    private int _nodeCount;
    private int _triangleCount;
    
    // Statistics
    public int NodeCount => _nodeCount;
    public int TriangleCount => _triangleCount;
    public int MaxDepth { get; private set; }
    public float BuildTimeMs { get; private set; }
    
    /// <summary>
    /// Build BVH from triangle list
    /// </summary>
    public void Build(ReadOnlySpan<Triangle> triangles)
    {
        var startTime = DateTime.UtcNow;
        
        _triangleCount = triangles.Length;
        _triangles = triangles.ToArray();
        
        // Allocate nodes (worst case: 2N-1 nodes for N triangles)
        _nodes = new BVHNode[_triangleCount * 2];
        _nodeCount = 0;
        
        Console.WriteLine($"[BVH] Building for {_triangleCount:N0} triangles...");
        
        // Build primitive info array
        var primitiveInfo = new PrimitiveInfo[_triangleCount];
        for (int i = 0; i < _triangleCount; i++)
        {
            primitiveInfo[i] = new PrimitiveInfo
            {
                PrimitiveIndex = i,
                Bounds = ComputeTriangleBounds(_triangles[i]),
                Centroid = ComputeTriangleCentroid(_triangles[i])
            };
        }
        
        // Recursively build BVH
        int totalNodes = 0;
        MaxDepth = 0;
        BuildRecursive(primitiveInfo, 0, _triangleCount, ref totalNodes, 0);
        
        BuildTimeMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
        
        Console.WriteLine($"[BVH] Build complete:");
        Console.WriteLine($"  Nodes: {_nodeCount:N0}");
        Console.WriteLine($"  Max Depth: {MaxDepth}");
        Console.WriteLine($"  Build Time: {BuildTimeMs:F2}ms");
        Console.WriteLine($"  Memory: {GetMemoryUsageMB():F2} MB");
    }
    
    private int BuildRecursive(PrimitiveInfo[] primitiveInfo, int start, int end, ref int totalNodes, int depth)
    {
        MaxDepth = Math.Max(MaxDepth, depth);
        
        int nodeIndex = _nodeCount++;
        totalNodes++;
        
        // Compute bounds of all primitives in this node
        AABB bounds = primitiveInfo[start].Bounds;
        for (int i = start + 1; i < end; i++)
        {
            bounds = AABB.Union(bounds, primitiveInfo[i].Bounds);
        }
        
        int primitiveCount = end - start;
        
        // Leaf node: few primitives or max depth reached
        if (primitiveCount <= 4 || depth >= 32)
        {
            _nodes[nodeIndex] = new BVHNode
            {
                Bounds = bounds,
                PrimitiveOffset = (short)start,
                PrimitiveCount = (short)primitiveCount,
                IsLeaf = true
            };
            return nodeIndex;
        }
        
        // Interior node: split primitives using SAH
        AABB centroidBounds = new AABB(primitiveInfo[start].Centroid, primitiveInfo[start].Centroid);
        for (int i = start + 1; i < end; i++)
        {
            centroidBounds = AABB.Union(centroidBounds, primitiveInfo[i].Centroid);
        }
        
        // Choose split axis (longest centroid extent)
        int splitAxis = centroidBounds.MaximumExtent();
        
        // Partition primitives
        int mid = (start + end) / 2;
        
        // If all centroids are at the same point, create leaf
        if (Vector3Extensions.Get(centroidBounds.Max, splitAxis) == Vector3Extensions.Get(centroidBounds.Min, splitAxis))
        {
            _nodes[nodeIndex] = new BVHNode
            {
                Bounds = bounds,
                PrimitiveOffset = (short)start,
                PrimitiveCount = (short)primitiveCount,
                IsLeaf = true
            };
            return nodeIndex;
        }
        
        // Partition using SAH (Surface Area Heuristic)
        mid = PartitionSAH(primitiveInfo, start, end, splitAxis, centroidBounds);
        
        // Recursively build children
        int leftChild = BuildRecursive(primitiveInfo, start, mid, ref totalNodes, depth + 1);
        int rightChild = BuildRecursive(primitiveInfo, mid, end, ref totalNodes, depth + 1);
        
        _nodes[nodeIndex] = new BVHNode
        {
            Bounds = bounds,
            LeftChild = leftChild,
            RightChild = rightChild,
            IsLeaf = false,
            SplitAxis = (byte)splitAxis
        };
        
        return nodeIndex;
    }
    
    private int PartitionSAH(PrimitiveInfo[] primitiveInfo, int start, int end, int axis, AABB centroidBounds)
    {
        const int numBuckets = 12;
        
        // Initialize buckets
        var buckets = new SAHBucket[numBuckets];
        for (int i = 0; i < numBuckets; i++)
        {
            buckets[i] = new SAHBucket();
        }
        
        // Assign primitives to buckets
        for (int i = start; i < end; i++)
        {
            int bucket = (int)(numBuckets * Vector3Extensions.Get(centroidBounds.Offset(primitiveInfo[i].Centroid), axis));
            if (bucket == numBuckets) bucket = numBuckets - 1;
            
            buckets[bucket].Count++;
            buckets[bucket].Bounds = AABB.Union(buckets[bucket].Bounds, primitiveInfo[i].Bounds);
        }
        
        // Compute costs for splitting after each bucket
        float[] costs = new float[numBuckets - 1];
        for (int i = 0; i < numBuckets - 1; i++)
        {
            AABB b0 = new AABB();
            AABB b1 = new AABB();
            int count0 = 0, count1 = 0;
            
            for (int j = 0; j <= i; j++)
            {
                b0 = AABB.Union(b0, buckets[j].Bounds);
                count0 += buckets[j].Count;
            }
            
            for (int j = i + 1; j < numBuckets; j++)
            {
                b1 = AABB.Union(b1, buckets[j].Bounds);
                count1 += buckets[j].Count;
            }
            
            costs[i] = 0.125f + (count0 * b0.SurfaceArea() + count1 * b1.SurfaceArea()) / centroidBounds.SurfaceArea();
        }
        
        // Find bucket with minimum cost
        float minCost = costs[0];
        int minCostSplitBucket = 0;
        for (int i = 1; i < numBuckets - 1; i++)
        {
            if (costs[i] < minCost)
            {
                minCost = costs[i];
                minCostSplitBucket = i;
            }
        }
        
        // Partition primitives based on SAH split
        int mid = start;
        for (int i = start; i < end; i++)
        {
            int bucket = (int)(numBuckets * Vector3Extensions.Get(centroidBounds.Offset(primitiveInfo[i].Centroid), axis));
            if (bucket == numBuckets) bucket = numBuckets - 1;
            
            if (bucket <= minCostSplitBucket)
            {
                // Swap to left partition
                var temp = primitiveInfo[mid];
                primitiveInfo[mid] = primitiveInfo[i];
                primitiveInfo[i] = temp;
                mid++;
            }
        }
        
        // Ensure we don't create empty partitions
        if (mid == start || mid == end)
            mid = (start + end) / 2;
        
        return mid;
    }
    
    /// <summary>
    /// Intersect ray with BVH
    /// Returns closest hit or null
    /// </summary>
    public RayHit? Intersect(Ray ray, float tMin = 0.001f, float tMax = float.MaxValue)
    {
        if (_nodeCount == 0)
            return null;
        
        RayHit? closestHit = null;
        float closestT = tMax;
        
        // Stack-based traversal (no recursion)
        Span<int> stack = stackalloc int[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0; // Start with root
        
        while (stackPtr > 0)
        {
            int nodeIndex = stack[--stackPtr];
            ref BVHNode node = ref _nodes[nodeIndex];
            
            // Test ray against node bounds
            if (!node.Bounds.Intersect(ray, tMin, closestT))
                continue;
            
            if (node.IsLeaf)
            {
                // Test ray against all triangles in leaf
                for (int i = 0; i < node.PrimitiveCount; i++)
                {
                    int triIndex = node.PrimitiveOffset + i;
                    var hit = IntersectTriangle(ray, _triangles[triIndex], tMin, closestT);
                    
                    if (hit.HasValue && hit.Value.T < closestT)
                    {
                        closestT = hit.Value.T;
                        closestHit = hit;
                    }
                }
            }
            else
            {
                // Push children onto stack
                if (stackPtr < 62) // Leave room for 2 children
                {
                    stack[stackPtr++] = node.LeftChild;
                    stack[stackPtr++] = node.RightChild;
                }
            }
        }
        
        return closestHit;
    }
    
    /// <summary>
    /// Test if ray intersects anything (shadow ray)
    /// Faster than full intersection test
    /// </summary>
    public bool IntersectAny(Ray ray, float tMin = 0.001f, float tMax = float.MaxValue)
    {
        if (_nodeCount == 0)
            return false;
        
        Span<int> stack = stackalloc int[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0;
        
        while (stackPtr > 0)
        {
            int nodeIndex = stack[--stackPtr];
            ref BVHNode node = ref _nodes[nodeIndex];
            
            if (!node.Bounds.Intersect(ray, tMin, tMax))
                continue;
            
            if (node.IsLeaf)
            {
                for (int i = 0; i < node.PrimitiveCount; i++)
                {
                    int triIndex = node.PrimitiveOffset + i;
                    if (IntersectTriangle(ray, _triangles[triIndex], tMin, tMax).HasValue)
                        return true; // Early exit
                }
            }
            else
            {
                if (stackPtr < 62)
                {
                    stack[stackPtr++] = node.LeftChild;
                    stack[stackPtr++] = node.RightChild;
                }
            }
        }
        
        return false;
    }
    
    private RayHit? IntersectTriangle(Ray ray, Triangle tri, float tMin, float tMax)
    {
        // Möller-Trumbore ray-triangle intersection
        Vector3 edge1 = tri.V1 - tri.V0;
        Vector3 edge2 = tri.V2 - tri.V0;
        Vector3 h = Vector3.Cross(ray.Direction, edge2);
        float a = Vector3.Dot(edge1, h);
        
        if (Math.Abs(a) < 1e-8f)
            return null; // Ray parallel to triangle
        
        float f = 1.0f / a;
        Vector3 s = ray.Origin - tri.V0;
        float u = f * Vector3.Dot(s, h);
        
        if (u < 0.0f || u > 1.0f)
            return null;
        
        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.Direction, q);
        
        if (v < 0.0f || u + v > 1.0f)
            return null;
        
        float t = f * Vector3.Dot(edge2, q);
        
        if (t < tMin || t > tMax)
            return null;
        
        // Interpolate normal
        Vector3 normal = (1 - u - v) * tri.N0 + u * tri.N1 + v * tri.N2;
        normal = Vector3.Normalize(normal);
        
        return new RayHit
        {
            T = t,
            Position = ray.Origin + t * ray.Direction,
            Normal = normal,
            UV = new Vector2(u, v),
            TriangleIndex = 0 // TODO: Store triangle index
        };
    }
    
    private AABB ComputeTriangleBounds(Triangle tri)
    {
        Vector3 min = Vector3.Min(Vector3.Min(tri.V0, tri.V1), tri.V2);
        Vector3 max = Vector3.Max(Vector3.Max(tri.V0, tri.V1), tri.V2);
        return new AABB(min, max);
    }
    
    private Vector3 ComputeTriangleCentroid(Triangle tri)
    {
        return (tri.V0 + tri.V1 + tri.V2) / 3.0f;
    }
    
    public float GetMemoryUsageMB()
    {
        int nodeSize = Marshal.SizeOf<BVHNode>();
        int triSize = Marshal.SizeOf<Triangle>();
        return (_nodeCount * nodeSize + _triangleCount * triSize) / (1024.0f * 1024.0f);
    }
    
    /// <summary>
    /// Get BVH nodes for GPU upload
    /// Returns flattened array of nodes ready for structured buffer
    /// </summary>
    public ReadOnlySpan<BVHNode> GetNodes()
    {
        return new ReadOnlySpan<BVHNode>(_nodes, 0, _nodeCount);
    }
    
    /// <summary>
    /// Get triangles for GPU upload
    /// Returns flattened array of triangles ready for structured buffer
    /// </summary>
    public ReadOnlySpan<Triangle> GetTriangles()
    {
        return new ReadOnlySpan<Triangle>(_triangles, 0, _triangleCount);
    }
    
    /// <summary>
    /// Get GPU-compatible BVH nodes (32-byte aligned)
    /// Converts internal BVH structure to GPU-friendly format
    /// </summary>
    public GPUBVHNode[] GetGPUNodes()
    {
        var gpuNodes = new GPUBVHNode[_nodeCount];
        
        for (int i = 0; i < _nodeCount; i++)
        {
            ref BVHNode node = ref _nodes[i];
            
            gpuNodes[i] = new GPUBVHNode
            {
                BoundsMin = node.Bounds.Min,
                BoundsMax = node.Bounds.Max,
                LeftChild = node.LeftChild,
                RightChild = node.RightChild,
                PrimitiveOffset = node.PrimitiveOffset,
                PrimitiveCount = node.PrimitiveCount,
                IsLeaf = node.IsLeaf ? 1u : 0u,
                SplitAxis = node.SplitAxis
            };
        }
        
        return gpuNodes;
    }
    
    /// <summary>
    /// Get GPU-compatible triangles (64-byte aligned)
    /// Converts internal triangle structure to GPU-friendly format
    /// </summary>
    public GPUTriangle[] GetGPUTriangles()
    {
        var gpuTriangles = new GPUTriangle[_triangleCount];
        
        for (int i = 0; i < _triangleCount; i++)
        {
            ref Triangle tri = ref _triangles[i];
            
            gpuTriangles[i] = new GPUTriangle
            {
                V0 = tri.V0,
                V1 = tri.V1,
                V2 = tri.V2,
                N0 = tri.N0,
                N1 = tri.N1,
                N2 = tri.N2,
                UV0 = tri.UV0,
                UV1 = tri.UV1,
                UV2 = tri.UV2,
                MaterialIndex = 0 // TODO: Add material index to Triangle struct
            };
        }
        
        return gpuTriangles;
    }
    
    public void Dispose()
    {
        _nodes = null!;
        _triangles = null!;
    }
}

/// <summary>
/// BVH node (32 bytes for cache efficiency)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct BVHNode
{
    public AABB Bounds;           // 24 bytes
    public int LeftChild;         // 4 bytes (or PrimitiveOffset for leaf)
    public int RightChild;        // 4 bytes (or PrimitiveCount for leaf)
    public bool IsLeaf;           // 1 byte
    public byte SplitAxis;        // 1 byte
    public short PrimitiveOffset; // 2 bytes (for leaf nodes)
    public short PrimitiveCount;  // 2 bytes (for leaf nodes)
}

/// <summary>
/// Axis-Aligned Bounding Box
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AABB
{
    public Vector3 Min;
    public Vector3 Max;
    
    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }
    
    public AABB(Vector3 point)
    {
        Min = point;
        Max = point;
    }
    
    public static AABB Union(AABB a, AABB b)
    {
        return new AABB(
            Vector3.Min(a.Min, b.Min),
            Vector3.Max(a.Max, b.Max)
        );
    }
    
    public static AABB Union(AABB a, Vector3 point)
    {
        return new AABB(
            Vector3.Min(a.Min, point),
            Vector3.Max(a.Max, point)
        );
    }
    
    public float SurfaceArea()
    {
        Vector3 d = Max - Min;
        return 2.0f * (d.X * d.Y + d.X * d.Z + d.Y * d.Z);
    }
    
    public int MaximumExtent()
    {
        Vector3 d = Max - Min;
        if (d.X > d.Y && d.X > d.Z) return 0;
        if (d.Y > d.Z) return 1;
        return 2;
    }
    
    public Vector3 Offset(Vector3 point)
    {
        Vector3 o = point - Min;
        Vector3 d = Max - Min;
        if (d.X > 0) o.X /= d.X;
        if (d.Y > 0) o.Y /= d.Y;
        if (d.Z > 0) o.Z /= d.Z;
        return o;
    }
    
    public bool Intersect(Ray ray, float tMin, float tMax)
    {
        // Slab method
        for (int i = 0; i < 3; i++)
        {
            float invD = 1.0f / ray.Direction[i];
            float t0 = (Min[i] - ray.Origin[i]) * invD;
            float t1 = (Max[i] - ray.Origin[i]) * invD;
            
            if (invD < 0.0f)
            {
                float temp = t0;
                t0 = t1;
                t1 = temp;
            }
            
            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;
            
            if (tMax <= tMin)
                return false;
        }
        
        return true;
    }
}

/// <summary>
/// Triangle primitive
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Triangle
{
    public Vector3 V0, V1, V2;    // Positions
    public Vector3 N0, N1, N2;    // Normals
    public Vector2 UV0, UV1, UV2; // Texture coordinates
}

/// <summary>
/// Ray
/// </summary>
public struct Ray
{
    public Vector3 Origin;
    public Vector3 Direction;
    
    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }
}

/// <summary>
/// Ray hit result
/// </summary>
public struct RayHit
{
    public float T;
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public int TriangleIndex;
}

// Helper structures for BVH building
struct PrimitiveInfo
{
    public int PrimitiveIndex;
    public AABB Bounds;
    public Vector3 Centroid;
}

struct SAHBucket
{
    public int Count;
    public AABB Bounds;
}

// Vector3 indexer extension
static class Vector3Extensions
{
    public static float Get(this Vector3 v, int index)
    {
        return index switch
        {
            0 => v.X,
            1 => v.Y,
            2 => v.Z,
            _ => throw new IndexOutOfRangeException()
        };
    }
}

/// <summary>
/// GPU-compatible BVH node (32 bytes, 16-byte aligned)
/// Matches HLSL struct layout in SoftwareRT_Intersection.hlsl
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GPUBVHNode
{
    public Vector3 BoundsMin;      // 12 bytes
    public int LeftChild;          // 4 bytes
    public Vector3 BoundsMax;      // 12 bytes
    public int RightChild;         // 4 bytes
    public short PrimitiveOffset;  // 2 bytes
    public short PrimitiveCount;   // 2 bytes
    public uint IsLeaf;            // 4 bytes (0 or 1)
    public byte SplitAxis;         // 1 byte
    public byte Padding1;          // 1 byte
    public ushort Padding2;        // 2 bytes
}

/// <summary>
/// GPU-compatible triangle (64 bytes, 16-byte aligned)
/// Matches HLSL struct layout in SoftwareRT_Intersection.hlsl
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GPUTriangle
{
    public Vector3 V0;             // 12 bytes
    public float Padding0;         // 4 bytes
    public Vector3 V1;             // 12 bytes
    public float Padding1;         // 4 bytes
    public Vector3 V2;             // 12 bytes
    public float Padding2;         // 4 bytes
    public Vector3 N0;             // 12 bytes
    public float Padding3;         // 4 bytes
    public Vector3 N1;             // 12 bytes
    public float Padding4;         // 4 bytes
    public Vector3 N2;             // 12 bytes
    public float Padding5;         // 4 bytes
    public Vector2 UV0;            // 8 bytes
    public Vector2 UV1;            // 8 bytes
    public Vector2 UV2;            // 8 bytes
    public uint MaterialIndex;     // 4 bytes
    public uint Padding6;          // 4 bytes
}
