// BlueSkyEngine - Project Polaris: 8-Wide Ray Packet (SoA)
//
// THE KEY INSIGHT: Structure-of-Arrays
// =====================================
// Instead of: Ray[8] { origin, direction }      ← cache-hostile
// We use:     RayPacket8 { originX[8], originY[8], ... } ← cache-friendly
//
// This lets AVX process all 8 rays with a SINGLE instruction per operation.
// One VMULPS instruction multiplies 8 ray origins simultaneously.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.Rendering.RayTracing.Polaris;

/// <summary>
/// 8-wide ray packet in Structure-of-Arrays layout.
/// Every field holds data for 8 simultaneous rays processed via AVX.
/// Total size: ~256 bytes (fits in 4 cache lines)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RayPacket8
{
    // ═══════════════════════════════════════════════════════════════
    // RAY DATA (SoA: 8 values per field)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Ray origin X coordinates (8 rays)</summary>
    public Vector256<float> OriginX;
    /// <summary>Ray origin Y coordinates (8 rays)</summary>
    public Vector256<float> OriginY;
    /// <summary>Ray origin Z coordinates (8 rays)</summary>
    public Vector256<float> OriginZ;
    
    /// <summary>Ray direction X (normalized)</summary>
    public Vector256<float> DirX;
    /// <summary>Ray direction Y (normalized)</summary>
    public Vector256<float> DirY;
    /// <summary>Ray direction Z (normalized)</summary>
    public Vector256<float> DirZ;
    
    /// <summary>Precomputed 1/direction.X for slab test acceleration</summary>
    public Vector256<float> InvDirX;
    /// <summary>Precomputed 1/direction.Y for slab test acceleration</summary>
    public Vector256<float> InvDirY;
    /// <summary>Precomputed 1/direction.Z for slab test acceleration</summary>
    public Vector256<float> InvDirZ;
    
    /// <summary>Minimum ray distance (typically small epsilon to avoid self-intersection)</summary>
    public Vector256<float> TMin;
    /// <summary>Maximum ray distance / closest hit so far</summary>
    public Vector256<float> TMax;
    
    // ═══════════════════════════════════════════════════════════════
    // HIT RESULTS (filled by traversal)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Closest hit distance per ray (MaxFloat = no hit)</summary>
    public Vector256<float> HitT;
    /// <summary>Barycentric U coordinate at hit point</summary>
    public Vector256<float> HitU;
    /// <summary>Barycentric V coordinate at hit point</summary>
    public Vector256<float> HitV;
    /// <summary>Index of hit triangle (as float; -1 = no hit)</summary>
    public Vector256<float> HitTriIdx;
    
    // ═══════════════════════════════════════════════════════════════
    // FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Create a ray packet for 8 primary camera rays.
    /// </summary>
    /// <param name="cameraPos">Camera world position (shared by all 8 rays)</param>
    /// <param name="directions">8 ray directions (one per pixel)</param>
    /// <param name="count">Number of active rays (1-8)</param>
    public static RayPacket8 CreatePrimary(
        Vector3 cameraPos,
        ReadOnlySpan<Vector3> directions,
        int count)
    {
        var packet = new RayPacket8();
        
        // Broadcast camera position to all 8 lanes
        packet.OriginX = AVXMath.Broadcast(cameraPos.X);
        packet.OriginY = AVXMath.Broadcast(cameraPos.Y);
        packet.OriginZ = AVXMath.Broadcast(cameraPos.Z);
        
        // Pack directions into SoA
        unsafe
        {
            float* dx = stackalloc float[8];
            float* dy = stackalloc float[8];
            float* dz = stackalloc float[8];
            
            int n = Math.Min(count, 8);
            for (int i = 0; i < n; i++)
            {
                dx[i] = directions[i].X;
                dy[i] = directions[i].Y;
                dz[i] = directions[i].Z;
            }
            // Pad inactive lanes with safe direction (won't produce false hits)
            for (int i = n; i < 8; i++)
            {
                dx[i] = 0.0f;
                dy[i] = 1.0f; // point up
                dz[i] = 0.0f;
            }
            
            packet.DirX = AVXMath.Load(dx);
            packet.DirY = AVXMath.Load(dy);
            packet.DirZ = AVXMath.Load(dz);
        }
        
        // Precompute inverse directions (with Newton-Raphson for full precision)
        packet.InvDirX = AVXMath.Reciprocal(packet.DirX);
        packet.InvDirY = AVXMath.Reciprocal(packet.DirY);
        packet.InvDirZ = AVXMath.Reciprocal(packet.DirZ);
        
        // Ray interval
        packet.TMin = AVXMath.Broadcast(0.001f);
        packet.TMax = AVXMath.Broadcast(1000.0f); // draw distance
        
        // Initialize results to "no hit"
        packet.HitT   = AVXMath.MaxFloat;
        packet.HitU   = AVXMath.Zero;
        packet.HitV   = AVXMath.Zero;
        packet.HitTriIdx = AVXMath.Broadcast(BitConverter.Int32BitsToSingle(-1));
        
        return packet;
    }
    
    /// <summary>
    /// Create a shadow ray packet (from hit point toward light).
    /// </summary>
    public static RayPacket8 CreateShadow(
        Vector256<float> originX, Vector256<float> originY, Vector256<float> originZ,
        Vector3 lightDir, float maxDist)
    {
        var packet = new RayPacket8();
        
        // Origins are per-ray (from previous hit points)
        // Offset slightly along normal to avoid self-intersection
        packet.OriginX = originX;
        packet.OriginY = originY;
        packet.OriginZ = originZ;
        
        // Direction is shared (directional light)
        packet.DirX = AVXMath.Broadcast(lightDir.X);
        packet.DirY = AVXMath.Broadcast(lightDir.Y);
        packet.DirZ = AVXMath.Broadcast(lightDir.Z);
        
        packet.InvDirX = AVXMath.Reciprocal(packet.DirX);
        packet.InvDirY = AVXMath.Reciprocal(packet.DirY);
        packet.InvDirZ = AVXMath.Reciprocal(packet.DirZ);
        
        packet.TMin = AVXMath.Broadcast(0.01f);
        packet.TMax = AVXMath.Broadcast(maxDist);
        
        packet.HitT    = AVXMath.MaxFloat;
        packet.HitU    = AVXMath.Zero;
        packet.HitV    = AVXMath.Zero;
        packet.HitTriIdx = AVXMath.Broadcast(BitConverter.Int32BitsToSingle(-1));
        
        return packet;
    }
    
    /// <summary>
    /// Check if any of the 8 rays found a hit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AnyHit()
    {
        var hitMask = AVXMath.CmpLT(HitT, AVXMath.MaxFloat);
        return AVXMath.MoveMask(hitMask) != 0;
    }
    
    /// <summary>
    /// Get hit distance for a specific lane (0-7).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetHitT(int lane) => HitT.GetElement(lane);
    
    /// <summary>
    /// Get hit triangle index for a specific lane (0-7). Returns -1 if no hit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetHitTriangle(int lane)
        => BitConverter.SingleToInt32Bits(HitTriIdx.GetElement(lane));
    
    /// <summary>
    /// Get barycentric coordinates for a specific lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector2 GetHitBarycentric(int lane)
        => new Vector2(HitU.GetElement(lane), HitV.GetElement(lane));
    
    /// <summary>
    /// Compute hit position for a specific lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector3 GetHitPosition(int lane)
    {
        float t = HitT.GetElement(lane);
        return new Vector3(
            OriginX.GetElement(lane) + DirX.GetElement(lane) * t,
            OriginY.GetElement(lane) + DirY.GetElement(lane) * t,
            OriginZ.GetElement(lane) + DirZ.GetElement(lane) * t
        );
    }
}
