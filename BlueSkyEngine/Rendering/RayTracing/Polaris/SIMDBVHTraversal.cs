// BlueSkyEngine - Project Polaris: AVX SIMD BVH Traversal
//
// THE HEART OF POLARIS: 8 rays tested against BVH nodes simultaneously
// =====================================================================
// AABB slab test:  8 rays × 1 box  = 1 AVX instruction per axis
// Möller-Trumbore: 8 rays × 1 tri  = full intersection in ~20 AVX ops
// Stack traversal: depth-first with near-child priority

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.Rendering.RayTracing.Polaris;

/// <summary>
/// Flattened BVH node optimized for SIMD traversal.
/// 32 bytes per node → fits nicely in cache lines.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PolarisNode
{
    public float MinX, MinY, MinZ;
    public float MaxX, MaxY, MaxZ;
    /// <summary>Left child index (internal) or first triangle index (leaf)</summary>
    public int LeftOrOffset;
    /// <summary>Right child index (internal) or triangle count (leaf). -1 = internal node</summary>
    public int RightOrCount;
}

/// <summary>
/// Triangle stored in SoA-friendly flat layout for fast SIMD intersection.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PolarisTriangle
{
    public float V0X, V0Y, V0Z;
    public float V1X, V1Y, V1Z;
    public float V2X, V2Y, V2Z;
    public float N0X, N0Y, N0Z; // vertex normal (for shading)
    public int MaterialIndex;
    public int Padding;
}

/// <summary>
/// SIMD BVH traversal engine. Tests 8 rays against the BVH simultaneously.
/// All math uses AVX1 (Sandy Bridge compatible, no AVX2/FMA3).
/// </summary>
public class SIMDBVHTraversal
{
    private PolarisNode[] _nodes;
    private PolarisTriangle[] _triangles;
    private int _nodeCount;
    
    // Build stats
    public int NodeCount => _nodeCount;
    public int TriangleCount => _triangles?.Length ?? 0;
    public float BuildTimeMs { get; private set; }
    
    private const int MAX_STACK_DEPTH = 64;
    private const int MAX_LEAF_TRIS = 4;
    
    // ═══════════════════════════════════════════════════════════════
    // BVH CONSTRUCTION (SAH-based, outputs flattened array)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Build a flattened BVH from raw triangles.
    /// </summary>
    public void Build(ReadOnlySpan<Triangle> inputTriangles)
    {
        var start = DateTime.UtcNow;
        
        int triCount = inputTriangles.Length;
        Console.WriteLine($"[Polaris BVH] Building for {triCount:N0} triangles...");
        
        // Convert to Polaris format
        _triangles = new PolarisTriangle[triCount];
        var indices = new int[triCount]; // indirect index for sorting during build
        
        for (int i = 0; i < triCount; i++)
        {
            ref readonly var t = ref inputTriangles[i];
            _triangles[i] = new PolarisTriangle
            {
                V0X = t.V0.X, V0Y = t.V0.Y, V0Z = t.V0.Z,
                V1X = t.V1.X, V1Y = t.V1.Y, V1Z = t.V1.Z,
                V2X = t.V2.X, V2Y = t.V2.Y, V2Z = t.V2.Z,
                N0X = t.N0.X, N0Y = t.N0.Y, N0Z = t.N0.Z,
                MaterialIndex = 0
            };
            indices[i] = i;
        }
        
        // Allocate worst-case node buffer (2*N - 1)
        _nodes = new PolarisNode[triCount * 2 + 1];
        _nodeCount = 0;
        
        // Build recursively with SAH
        BuildRecursive(indices, 0, triCount, 0);
        
        // Trim node array
        Array.Resize(ref _nodes, _nodeCount);
        
        BuildTimeMs = (float)(DateTime.UtcNow - start).TotalMilliseconds;
        Console.WriteLine($"[Polaris BVH] Built in {BuildTimeMs:F2}ms");
        Console.WriteLine($"  Nodes: {_nodeCount:N0}");
        Console.WriteLine($"  Memory: {(_nodeCount * 32 + triCount * 56) / 1024.0f:F1} KB");
    }
    
    private int BuildRecursive(int[] indices, int start, int end, int depth)
    {
        int count = end - start;
        int nodeIdx = _nodeCount++;
        
        // Compute bounds
        ComputeBounds(indices, start, end, out float minX, out float minY, out float minZ,
                      out float maxX, out float maxY, out float maxZ);
        
        // Leaf condition
        if (count <= MAX_LEAF_TRIS || depth >= MAX_STACK_DEPTH)
        {
            _nodes[nodeIdx] = new PolarisNode
            {
                MinX = minX, MinY = minY, MinZ = minZ,
                MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
                LeftOrOffset = start,
                RightOrCount = count // positive = leaf
            };
            return nodeIdx;
        }
        
        // Find best SAH split
        int bestAxis = 0;
        float bestPos = 0, bestCost = float.MaxValue;
        float parentArea = SurfaceArea(minX, minY, minZ, maxX, maxY, maxZ);
        
        for (int axis = 0; axis < 3; axis++)
        {
            float axisMin = axis == 0 ? minX : (axis == 1 ? minY : minZ);
            float axisMax = axis == 0 ? maxX : (axis == 1 ? maxY : maxZ);
            
            if (axisMax - axisMin < 1e-6f) continue;
            
            for (int b = 1; b < 8; b++)
            {
                float pos = axisMin + (axisMax - axisMin) * (b / 8.0f);
                float cost = EvaluateSAH(indices, start, end, axis, pos, parentArea);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestPos = pos;
                }
            }
        }
        
        // Partition
        int mid = Partition(indices, start, end, bestAxis, bestPos);
        if (mid == start || mid == end) mid = (start + end) / 2;
        
        // Reserve this node, then build children
        _nodes[nodeIdx] = new PolarisNode
        {
            MinX = minX, MinY = minY, MinZ = minZ,
            MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
        };
        
        int left = BuildRecursive(indices, start, mid, depth + 1);
        int right = BuildRecursive(indices, mid, end, depth + 1);
        
        _nodes[nodeIdx].LeftOrOffset = left;
        _nodes[nodeIdx].RightOrCount = -1; // negative = internal node marker
        // Store right child; left is always at LeftOrOffset
        // We encode: for internal nodes, RightOrCount < 0, and we use a side array
        // Actually simpler: use sign of RightOrCount as leaf flag
        _nodes[nodeIdx].RightOrCount = -(right + 1); // negative → internal, decode: -(val+1)
        
        return nodeIdx;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsLeaf(ref PolarisNode node) => node.RightOrCount >= 0;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetRightChild(ref PolarisNode node) => -(node.RightOrCount + 1);
    
    // ═══════════════════════════════════════════════════════════════
    // 8-WIDE AABB INTERSECTION (the hot path)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Test 8 rays against one AABB simultaneously. Returns bitmask of hits.
    /// This is the single most performance-critical function in the engine.
    /// ~15 AVX instructions total.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IntersectAABB8(
        ref RayPacket8 packet,
        float boxMinX, float boxMinY, float boxMinZ,
        float boxMaxX, float boxMaxY, float boxMaxZ)
    {
        // Broadcast box bounds to all 8 lanes
        var bMinX = AVXMath.Broadcast(boxMinX);
        var bMinY = AVXMath.Broadcast(boxMinY);
        var bMinZ = AVXMath.Broadcast(boxMinZ);
        var bMaxX = AVXMath.Broadcast(boxMaxX);
        var bMaxY = AVXMath.Broadcast(boxMaxY);
        var bMaxZ = AVXMath.Broadcast(boxMaxZ);
        
        // Slab test: for each axis, compute entry and exit distances
        // t1 = (boxMin - origin) * invDir
        // t2 = (boxMax - origin) * invDir
        // tNear = min(t1, t2), tFar = max(t1, t2)
        
        // X axis
        var t1x = Avx.Multiply(Avx.Subtract(bMinX, packet.OriginX), packet.InvDirX);
        var t2x = Avx.Multiply(Avx.Subtract(bMaxX, packet.OriginX), packet.InvDirX);
        var tNear = Avx.Min(t1x, t2x);
        var tFar  = Avx.Max(t1x, t2x);
        
        // Y axis
        var t1y = Avx.Multiply(Avx.Subtract(bMinY, packet.OriginY), packet.InvDirY);
        var t2y = Avx.Multiply(Avx.Subtract(bMaxY, packet.OriginY), packet.InvDirY);
        tNear = Avx.Max(tNear, Avx.Min(t1y, t2y));
        tFar  = Avx.Min(tFar,  Avx.Max(t1y, t2y));
        
        // Z axis
        var t1z = Avx.Multiply(Avx.Subtract(bMinZ, packet.OriginZ), packet.InvDirZ);
        var t2z = Avx.Multiply(Avx.Subtract(bMaxZ, packet.OriginZ), packet.InvDirZ);
        tNear = Avx.Max(tNear, Avx.Min(t1z, t2z));
        tFar  = Avx.Min(tFar,  Avx.Max(t1z, t2z));
        
        // Clamp to valid ray interval
        tNear = Avx.Max(tNear, packet.TMin);
        tFar  = Avx.Min(tFar,  packet.HitT); // early-out: ignore if further than current closest
        
        // Hit if tNear <= tFar
        var hitMask = AVXMath.CmpLE(tNear, tFar);
        return AVXMath.MoveMask(hitMask);
    }
    
    // ═══════════════════════════════════════════════════════════════
    // 8-WIDE MÖLLER-TRUMBORE TRIANGLE INTERSECTION
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Test 8 rays against one triangle (Möller-Trumbore). Updates packet.HitT etc.
    /// ~30 AVX instructions. Returns bitmask of new hits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IntersectTriangle8(ref RayPacket8 packet, ref PolarisTriangle tri, int triIndex)
    {
        // Edge vectors
        var e1x = AVXMath.Broadcast(tri.V1X - tri.V0X);
        var e1y = AVXMath.Broadcast(tri.V1Y - tri.V0Y);
        var e1z = AVXMath.Broadcast(tri.V1Z - tri.V0Z);
        var e2x = AVXMath.Broadcast(tri.V2X - tri.V0X);
        var e2y = AVXMath.Broadcast(tri.V2Y - tri.V0Y);
        var e2z = AVXMath.Broadcast(tri.V2Z - tri.V0Z);
        
        // h = cross(dir, e2)
        AVXMath.Cross3(packet.DirX, packet.DirY, packet.DirZ,
                       e2x, e2y, e2z, out var hx, out var hy, out var hz);
        
        // a = dot(e1, h)
        var a = AVXMath.Dot3(e1x, e1y, e1z, hx, hy, hz);
        
        // Backface / parallel check: |a| < epsilon → miss
        var validMask = AVXMath.CmpGT(AVXMath.Abs(a), AVXMath.Epsilon);
        int mask = AVXMath.MoveMask(validMask);
        if (mask == 0) return 0; // all 8 rays miss
        
        // f = 1/a
        var f = AVXMath.Reciprocal(a);
        
        // s = origin - v0
        var v0x = AVXMath.Broadcast(tri.V0X);
        var v0y = AVXMath.Broadcast(tri.V0Y);
        var v0z = AVXMath.Broadcast(tri.V0Z);
        var sx = Avx.Subtract(packet.OriginX, v0x);
        var sy = Avx.Subtract(packet.OriginY, v0y);
        var sz = Avx.Subtract(packet.OriginZ, v0z);
        
        // u = f * dot(s, h)
        var u = Avx.Multiply(f, AVXMath.Dot3(sx, sy, sz, hx, hy, hz));
        
        // Check 0 <= u <= 1
        validMask = AVXMath.And(validMask, AVXMath.CmpGE(u, AVXMath.Zero));
        validMask = AVXMath.And(validMask, AVXMath.CmpLE(u, AVXMath.One));
        mask = AVXMath.MoveMask(validMask);
        if (mask == 0) return 0;
        
        // q = cross(s, e1)
        AVXMath.Cross3(sx, sy, sz, e1x, e1y, e1z, out var qx, out var qy, out var qz);
        
        // v = f * dot(dir, q)
        var v = Avx.Multiply(f, AVXMath.Dot3(packet.DirX, packet.DirY, packet.DirZ, qx, qy, qz));
        
        // Check 0 <= v, u+v <= 1
        validMask = AVXMath.And(validMask, AVXMath.CmpGE(v, AVXMath.Zero));
        validMask = AVXMath.And(validMask, AVXMath.CmpLE(Avx.Add(u, v), AVXMath.One));
        mask = AVXMath.MoveMask(validMask);
        if (mask == 0) return 0;
        
        // t = f * dot(e2, q)
        var t = Avx.Multiply(f, AVXMath.Dot3(e2x, e2y, e2z, qx, qy, qz));
        
        // Check tMin < t < current HitT (closest hit)
        validMask = AVXMath.And(validMask, AVXMath.CmpGT(t, packet.TMin));
        validMask = AVXMath.And(validMask, AVXMath.CmpLT(t, packet.HitT));
        mask = AVXMath.MoveMask(validMask);
        if (mask == 0) return 0;
        
        // Update hit records for rays that found a closer hit
        var triIdxFloat = AVXMath.Broadcast(BitConverter.Int32BitsToSingle(triIndex));
        packet.HitT      = AVXMath.Select(validMask, t, packet.HitT);
        packet.HitU      = AVXMath.Select(validMask, u, packet.HitU);
        packet.HitV      = AVXMath.Select(validMask, v, packet.HitV);
        packet.HitTriIdx = AVXMath.Select(validMask, triIdxFloat, packet.HitTriIdx);
        
        return mask;
    }
    
    // ═══════════════════════════════════════════════════════════════
    // FULL BVH TRAVERSAL (stack-based, 8 rays at once)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Traverse the BVH with a packet of 8 rays.
    /// Uses a fixed-size stack (no heap allocation).
    /// </summary>
    public void Traverse(ref RayPacket8 packet)
    {
        if (_nodeCount == 0) return;
        
        // Fixed-size traversal stack (no allocation)
        Span<int> stack = stackalloc int[MAX_STACK_DEPTH];
        int stackPtr = 0;
        stack[stackPtr++] = 0; // push root
        
        while (stackPtr > 0)
        {
            int nodeIdx = stack[--stackPtr]; // pop
            ref var node = ref _nodes[nodeIdx];
            
            // Test 8 rays against this node's AABB
            int hitMask = IntersectAABB8(ref packet,
                node.MinX, node.MinY, node.MinZ,
                node.MaxX, node.MaxY, node.MaxZ);
            
            if (hitMask == 0) continue; // all 8 rays miss this node
            
            if (IsLeaf(ref node))
            {
                // Test 8 rays against each triangle in the leaf
                int offset = node.LeftOrOffset;
                int count = node.RightOrCount;
                for (int i = 0; i < count && offset + i < _triangles.Length; i++)
                {
                    IntersectTriangle8(ref packet, ref _triangles[offset + i], offset + i);
                }
            }
            else
            {
                // Push both children (nearer child on top for better early-out)
                int left = node.LeftOrOffset;
                int right = GetRightChild(ref node);
                
                if (stackPtr + 2 <= MAX_STACK_DEPTH)
                {
                    stack[stackPtr++] = right;
                    stack[stackPtr++] = left; // left popped first = tested first
                }
            }
        }
    }
    
    /// <summary>
    /// Shadow ray test: returns true if ANY of the 8 rays is occluded.
    /// Early-exits as soon as any hit is found (faster than full traversal).
    /// </summary>
    public bool TraverseAnyHit(ref RayPacket8 packet)
    {
        if (_nodeCount == 0) return false;
        
        Span<int> stack = stackalloc int[MAX_STACK_DEPTH];
        int stackPtr = 0;
        stack[stackPtr++] = 0;
        
        while (stackPtr > 0)
        {
            int nodeIdx = stack[--stackPtr];
            ref var node = ref _nodes[nodeIdx];
            
            int hitMask = IntersectAABB8(ref packet,
                node.MinX, node.MinY, node.MinZ,
                node.MaxX, node.MaxY, node.MaxZ);
            
            if (hitMask == 0) continue;
            
            if (IsLeaf(ref node))
            {
                int offset = node.LeftOrOffset;
                int count = node.RightOrCount;
                for (int i = 0; i < count && offset + i < _triangles.Length; i++)
                {
                    if (IntersectTriangle8(ref packet, ref _triangles[offset + i], offset + i) != 0)
                        return true; // shadow ray: any hit is enough
                }
            }
            else
            {
                int left = node.LeftOrOffset;
                int right = GetRightChild(ref node);
                if (stackPtr + 2 <= MAX_STACK_DEPTH)
                {
                    stack[stackPtr++] = right;
                    stack[stackPtr++] = left;
                }
            }
        }
        
        return false;
    }
    
    // ═══════════════════════════════════════════════════════════════
    // BUILD HELPERS
    // ═══════════════════════════════════════════════════════════════
    
    private void ComputeBounds(int[] indices, int start, int end,
        out float minX, out float minY, out float minZ,
        out float maxX, out float maxY, out float maxZ)
    {
        minX = minY = minZ = float.MaxValue;
        maxX = maxY = maxZ = float.MinValue;
        
        for (int i = start; i < end; i++)
        {
            ref var t = ref _triangles[indices[i]];
            minX = MathF.Min(minX, MathF.Min(t.V0X, MathF.Min(t.V1X, t.V2X)));
            minY = MathF.Min(minY, MathF.Min(t.V0Y, MathF.Min(t.V1Y, t.V2Y)));
            minZ = MathF.Min(minZ, MathF.Min(t.V0Z, MathF.Min(t.V1Z, t.V2Z)));
            maxX = MathF.Max(maxX, MathF.Max(t.V0X, MathF.Max(t.V1X, t.V2X)));
            maxY = MathF.Max(maxY, MathF.Max(t.V0Y, MathF.Max(t.V1Y, t.V2Y)));
            maxZ = MathF.Max(maxZ, MathF.Max(t.V0Z, MathF.Max(t.V1Z, t.V2Z)));
        }
    }
    
    private float GetCentroid(int triIndex, int axis)
    {
        ref var t = ref _triangles[triIndex];
        return axis switch
        {
            0 => (t.V0X + t.V1X + t.V2X) / 3f,
            1 => (t.V0Y + t.V1Y + t.V2Y) / 3f,
            _ => (t.V0Z + t.V1Z + t.V2Z) / 3f,
        };
    }
    
    private float EvaluateSAH(int[] indices, int start, int end, int axis, float pos, float parentArea)
    {
        float lMinX = float.MaxValue, lMinY = float.MaxValue, lMinZ = float.MaxValue;
        float lMaxX = float.MinValue, lMaxY = float.MinValue, lMaxZ = float.MinValue;
        float rMinX = float.MaxValue, rMinY = float.MaxValue, rMinZ = float.MaxValue;
        float rMaxX = float.MinValue, rMaxY = float.MinValue, rMaxZ = float.MinValue;
        int lCount = 0, rCount = 0;
        
        for (int i = start; i < end; i++)
        {
            float c = GetCentroid(indices[i], axis);
            ref var t = ref _triangles[indices[i]];
            float tMinX = MathF.Min(t.V0X, MathF.Min(t.V1X, t.V2X));
            float tMinY = MathF.Min(t.V0Y, MathF.Min(t.V1Y, t.V2Y));
            float tMinZ = MathF.Min(t.V0Z, MathF.Min(t.V1Z, t.V2Z));
            float tMaxX = MathF.Max(t.V0X, MathF.Max(t.V1X, t.V2X));
            float tMaxY = MathF.Max(t.V0Y, MathF.Max(t.V1Y, t.V2Y));
            float tMaxZ = MathF.Max(t.V0Z, MathF.Max(t.V1Z, t.V2Z));
            
            if (c < pos)
            {
                lCount++;
                lMinX = MathF.Min(lMinX, tMinX); lMinY = MathF.Min(lMinY, tMinY); lMinZ = MathF.Min(lMinZ, tMinZ);
                lMaxX = MathF.Max(lMaxX, tMaxX); lMaxY = MathF.Max(lMaxY, tMaxY); lMaxZ = MathF.Max(lMaxZ, tMaxZ);
            }
            else
            {
                rCount++;
                rMinX = MathF.Min(rMinX, tMinX); rMinY = MathF.Min(rMinY, tMinY); rMinZ = MathF.Min(rMinZ, tMinZ);
                rMaxX = MathF.Max(rMaxX, tMaxX); rMaxY = MathF.Max(rMaxY, tMaxY); rMaxZ = MathF.Max(rMaxZ, tMaxZ);
            }
        }
        
        if (lCount == 0 || rCount == 0) return float.MaxValue;
        
        float lArea = SurfaceArea(lMinX, lMinY, lMinZ, lMaxX, lMaxY, lMaxZ);
        float rArea = SurfaceArea(rMinX, rMinY, rMinZ, rMaxX, rMaxY, rMaxZ);
        return (lArea * lCount + rArea * rCount) / parentArea;
    }
    
    private int Partition(int[] indices, int start, int end, int axis, float pos)
    {
        int mid = start;
        for (int i = start; i < end; i++)
        {
            if (GetCentroid(indices[i], axis) < pos)
            {
                (indices[mid], indices[i]) = (indices[i], indices[mid]);
                mid++;
            }
        }
        return mid;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SurfaceArea(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
        return 2f * (dx * dy + dy * dz + dz * dx);
    }
    
    /// <summary>Get triangle data for shading after a hit.</summary>
    public ref PolarisTriangle GetTriangle(int index) => ref _triangles[index];
}
