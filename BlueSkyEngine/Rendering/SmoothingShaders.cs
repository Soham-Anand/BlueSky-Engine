using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Rendering;

/// <summary>
/// Smoothing Shaders - CPU-based vertex normal smoothing and mesh preprocessing
/// 
/// Provides immediate fixes for shading artifacts like visible triangle edges on curved surfaces.
/// Focuses on CPU-based solutions that work without complex shader compilation.
/// </summary>
public static class SmoothingShaders
{
    /// <summary>
    /// Smooths vertex normals using angle-weighted averaging to eliminate hard edges
    /// </summary>
    public static void SmoothVertexNormals(Span<Vector3> vertices, Span<Vector3> normals, Span<uint> indices, float smoothingAngle = 60.0f)
    {
        if (vertices.Length != normals.Length)
            throw new ArgumentException("Vertices and normals arrays must have the same length");
        
        var smoothingCosine = MathF.Cos(smoothingAngle * MathF.PI / 180.0f);
        var vertexGroups = new Dictionary<Vector3, List<int>>();
        
        // Group vertices by position (with small tolerance for floating point precision)
        for (int i = 0; i < vertices.Length; i++)
        {
            var pos = RoundVector(vertices[i], 0.0001f);
            if (!vertexGroups.ContainsKey(pos))
                vertexGroups[pos] = new List<int>();
            vertexGroups[pos].Add(i);
        }
        
        // Calculate face normals for angle-based smoothing
        var faceNormals = CalculateFaceNormals(vertices, indices);
        
        // Smooth normals for each vertex group
        foreach (var group in vertexGroups.Values)
        {
            if (group.Count <= 1) continue;
            
            var smoothedNormals = new Vector3[group.Count];
            
            for (int i = 0; i < group.Count; i++)
            {
                var vertexIndex = group[i];
                var currentNormal = normals[vertexIndex];
                var smoothedNormal = currentNormal;
                
                // Average with other normals in the group if angle is within threshold
                for (int j = 0; j < group.Count; j++)
                {
                    if (i == j) continue;
                    
                    var otherIndex = group[j];
                    var otherNormal = normals[otherIndex];
                    
                    // Check angle between normals
                    var dot = Vector3.Dot(Vector3.Normalize(currentNormal), Vector3.Normalize(otherNormal));
                    if (dot >= smoothingCosine)
                    {
                        smoothedNormal += otherNormal;
                    }
                }
                
                smoothedNormals[i] = Vector3.Normalize(smoothedNormal);
            }
            
            // Apply smoothed normals back to vertices
            for (int i = 0; i < group.Count; i++)
            {
                normals[group[i]] = smoothedNormals[i];
            }
        }
    }
    
    /// <summary>
    /// Applies Laplacian smoothing to vertex positions to reduce geometric noise
    /// </summary>
    public static void LaplacianSmooth(Span<Vector3> vertices, Span<uint> indices, float factor = 0.1f, int iterations = 1)
    {
        var adjacency = BuildAdjacencyList(vertices.Length, indices);
        var originalVertices = vertices.ToArray();
        
        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                if (adjacency[i].Count == 0) continue;
                
                var sum = Vector3.Zero;
                foreach (var neighbor in adjacency[i])
                {
                    sum += originalVertices[neighbor];
                }
                
                var average = sum / adjacency[i].Count;
                vertices[i] = Vector3.Lerp(originalVertices[i], average, factor);
            }
            
            // Update original vertices for next iteration
            originalVertices = vertices.ToArray();
        }
    }
    
    /// <summary>
    /// Generates smooth normals from scratch using face area weighting
    /// </summary>
    public static Vector3[] GenerateSmoothNormals(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<uint> indices)
    {
        var normals = new Vector3[vertices.Length];
        
        // Calculate area-weighted normals for each face
        for (int i = 0; i < indices.Length; i += 3)
        {
            var i0 = (int)indices[i];
            var i1 = (int)indices[i + 1];
            var i2 = (int)indices[i + 2];
            
            var v0 = vertices[i0];
            var v1 = vertices[i1];
            var v2 = vertices[i2];
            
            // Calculate face normal with area weighting
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var faceNormal = Vector3.Cross(edge1, edge2);
            var area = faceNormal.Length() * 0.5f;
            
            if (area > 0.0001f) // Avoid degenerate triangles
            {
                faceNormal = Vector3.Normalize(faceNormal) * area;
                
                // Add to vertex normals
                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;
            }
        }
        
        // Normalize all vertex normals
        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i].LengthSquared() > 0.0001f)
                normals[i] = Vector3.Normalize(normals[i]);
            else
                normals[i] = Vector3.UnitY; // Default up vector for degenerate cases
        }
        
        return normals;
    }
    
    /// <summary>
    /// Applies edge-preserving smoothing that maintains sharp features
    /// </summary>
    public static void EdgePreservingSmooth(Span<Vector3> vertices, Span<Vector3> normals, Span<uint> indices, 
        float positionFactor = 0.05f, float normalThreshold = 0.7f)
    {
        var adjacency = BuildAdjacencyList(vertices.Length, indices);
        var originalVertices = vertices.ToArray();
        var originalNormals = normals.ToArray();
        
        for (int i = 0; i < vertices.Length; i++)
        {
            if (adjacency[i].Count == 0) continue;
            
            var currentNormal = Vector3.Normalize(originalNormals[i]);
            var positionSum = Vector3.Zero;
            var normalSum = Vector3.Zero;
            int validNeighbors = 0;
            
            foreach (var neighbor in adjacency[i])
            {
                var neighborNormal = Vector3.Normalize(originalNormals[neighbor]);
                var dot = Vector3.Dot(currentNormal, neighborNormal);
                
                // Only smooth with neighbors that have similar normals (preserve edges)
                if (dot >= normalThreshold)
                {
                    positionSum += originalVertices[neighbor];
                    normalSum += originalNormals[neighbor];
                    validNeighbors++;
                }
            }
            
            if (validNeighbors > 0)
            {
                var averagePosition = positionSum / validNeighbors;
                var averageNormal = normalSum / validNeighbors;
                
                vertices[i] = Vector3.Lerp(originalVertices[i], averagePosition, positionFactor);
                normals[i] = Vector3.Normalize(Vector3.Lerp(originalNormals[i], averageNormal, 0.3f));
            }
        }
    }
    
    /// <summary>
    /// Post-process smoothing filter similar to FXAA for reducing aliasing artifacts
    /// </summary>
    public static void ApplyAntiAliasing(Span<Vector3> vertices, Span<Vector3> normals, Span<uint> indices, float strength = 0.5f)
    {
        // This is a simplified CPU-based anti-aliasing that smooths high-frequency details
        var adjacency = BuildAdjacencyList(vertices.Length, indices);
        var smoothedVertices = vertices.ToArray();
        
        for (int i = 0; i < vertices.Length; i++)
        {
            if (adjacency[i].Count < 2) continue;
            
            // Calculate local variance to detect high-frequency details
            var center = vertices[i];
            var variance = 0.0f;
            
            foreach (var neighbor in adjacency[i])
            {
                var diff = vertices[neighbor] - center;
                variance += diff.LengthSquared();
            }
            variance /= adjacency[i].Count;
            
            // Apply smoothing based on local variance
            if (variance > 0.001f) // Only smooth high-variance areas
            {
                var sum = Vector3.Zero;
                foreach (var neighbor in adjacency[i])
                {
                    sum += vertices[neighbor];
                }
                var average = sum / adjacency[i].Count;
                
                smoothedVertices[i] = Vector3.Lerp(center, average, strength * MathF.Min(variance * 10.0f, 1.0f));
            }
        }
        
        // Copy back smoothed vertices
        smoothedVertices.CopyTo(vertices);
    }
    
    // Helper methods
    
    private static Vector3 RoundVector(Vector3 v, float precision)
    {
        return new Vector3(
            MathF.Round(v.X / precision) * precision,
            MathF.Round(v.Y / precision) * precision,
            MathF.Round(v.Z / precision) * precision
        );
    }
    
    private static Vector3[] CalculateFaceNormals(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<uint> indices)
    {
        var faceNormals = new Vector3[indices.Length / 3];
        
        for (int i = 0; i < indices.Length; i += 3)
        {
            var v0 = vertices[(int)indices[i]];
            var v1 = vertices[(int)indices[i + 1]];
            var v2 = vertices[(int)indices[i + 2]];
            
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Cross(edge1, edge2);
            
            faceNormals[i / 3] = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.UnitY;
        }
        
        return faceNormals;
    }
    
    private static List<int>[] BuildAdjacencyList(int vertexCount, ReadOnlySpan<uint> indices)
    {
        var adjacency = new List<int>[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            adjacency[i] = new List<int>();
        }
        
        // Build adjacency from triangle indices
        for (int i = 0; i < indices.Length; i += 3)
        {
            var i0 = (int)indices[i];
            var i1 = (int)indices[i + 1];
            var i2 = (int)indices[i + 2];
            
            // Add bidirectional connections
            AddUniqueConnection(adjacency[i0], i1);
            AddUniqueConnection(adjacency[i0], i2);
            AddUniqueConnection(adjacency[i1], i0);
            AddUniqueConnection(adjacency[i1], i2);
            AddUniqueConnection(adjacency[i2], i0);
            AddUniqueConnection(adjacency[i2], i1);
        }
        
        return adjacency;
    }
    
    private static void AddUniqueConnection(List<int> list, int vertex)
    {
        if (!list.Contains(vertex))
            list.Add(vertex);
    }
}