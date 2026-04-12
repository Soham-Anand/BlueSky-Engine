using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Mesh;

/// <summary>
/// Mesh simplification system for generating LOD meshes.
/// Uses edge collapse algorithm to reduce polygon count while preserving shape.
/// Optimized for old hardware with fast simplification.
/// </summary>
public class MeshSimplifier
{
    /// <summary>
    /// Simplify a mesh to a target triangle count using edge collapse.
    /// </summary>
    public static SimplifiedMesh Simplify(MeshData sourceMesh, float targetRatio)
    {
        if (sourceMesh == null || sourceMesh.Vertices.Length == 0)
            return new SimplifiedMesh(sourceMesh);
        
        int targetTriangles = (int)(sourceMesh.Indices.Length / 3 * targetRatio);
        if (targetTriangles < 4) targetTriangles = 4; // Minimum 2 triangles
        
        return EdgeCollapseSimplify(sourceMesh, targetTriangles);
    }
    
    /// <summary>
    /// Simplify mesh using edge collapse algorithm (Quadric Error Metrics).
    /// </summary>
    private static SimplifiedMesh EdgeCollapseSimplify(MeshData sourceMesh, int targetTriangles)
    {
        // Build adjacency information
        var vertices = new List<VertexData>();
        var triangles = new List<TriangleData>();
        
        // Convert to working format
        for (int i = 0; i < sourceMesh.Vertices.Length; i++)
        {
            vertices.Add(new VertexData
            {
                Position = sourceMesh.Vertices[i],
                Normal = sourceMesh.Normals[i],
                UV = sourceMesh.UVs[i],
                Index = i
            });
        }
        
        for (int i = 0; i < sourceMesh.Indices.Length; i += 3)
        {
            triangles.Add(new TriangleData
            {
                V0 = (int)sourceMesh.Indices[i],
                V1 = (int)sourceMesh.Indices[i + 1],
                V2 = (int)sourceMesh.Indices[i + 2]
            });
        }
        
        // Build edge list with error metrics
        var edges = BuildEdgeList(vertices, triangles);
        
        // Simplify by collapsing edges with lowest error
        while (triangles.Count > targetTriangles && edges.Count > 0)
        {
            // Find edge with minimum error
            int minEdgeIndex = 0;
            float minError = edges[0].Error;
            
            for (int i = 1; i < edges.Count; i++)
            {
                if (edges[i].Error < minError)
                {
                    minError = edges[i].Error;
                    minEdgeIndex = i;
                }
            }
            
            // Collapse the edge
            var edge = edges[minEdgeIndex];
            CollapseEdge(vertices, triangles, edges, edge);
        }
        
        // Rebuild simplified mesh
        var simplified = new SimplifiedMesh(sourceMesh);
        simplified.Vertices = new Vector3[vertices.Count];
        simplified.Normals = new Vector3[vertices.Count];
        simplified.UVs = new Vector2[vertices.Count];
        simplified.Indices = new uint[triangles.Count * 3];
        
        for (int i = 0; i < vertices.Count; i++)
        {
            simplified.Vertices[i] = vertices[i].Position;
            simplified.Normals[i] = vertices[i].Normal;
            simplified.UVs[i] = vertices[i].UV;
        }
        
        int idx = 0;
        foreach (var tri in triangles)
        {
            simplified.Indices[idx++] = (uint)tri.V0;
            simplified.Indices[idx++] = (uint)tri.V1;
            simplified.Indices[idx++] = (uint)tri.V2;
        }
        
        // Recalculate normals
        RecalculateNormals(simplified);
        
        simplified.OriginalVertexCount = sourceMesh.Vertices.Length;
        simplified.OriginalTriangleCount = sourceMesh.Indices.Length / 3;
        simplified.SimplifiedVertexCount = vertices.Count;
        simplified.SimplifiedTriangleCount = triangles.Count;
        
        return simplified;
    }
    
    /// <summary>
    /// Build list of edges with error metrics.
    /// </summary>
    private static List<EdgeData> BuildEdgeList(List<VertexData> vertices, List<TriangleData> triangles)
    {
        var edges = new List<EdgeData>();
        var edgeSet = new HashSet<(int, int)>();
        
        foreach (var tri in triangles)
        {
            // Edge 0: V0-V1
            AddEdge(edges, edgeSet, vertices, tri.V0, tri.V1, triangles);
            
            // Edge 1: V1-V2
            AddEdge(edges, edgeSet, vertices, tri.V1, tri.V2, triangles);
            
            // Edge 2: V2-V0
            AddEdge(edges, edgeSet, vertices, tri.V2, tri.V0, triangles);
        }
        
        return edges;
    }
    
    private static void AddEdge(List<EdgeData> edges, HashSet<(int, int)> edgeSet,
                                List<VertexData> vertices, int v0, int v1, List<TriangleData> triangles)
    {
        // Ensure consistent ordering
        if (v0 > v1)
        {
            (v0, v1) = (v1, v0);
        }
        
        var key = (v0, v1);
        if (edgeSet.Contains(key))
            return;
        
        edgeSet.Add(key);
        
        // Calculate edge error (simplified - just distance)
        float error = Vector3.Distance(vertices[v0].Position, vertices[v1].Position);
        
        edges.Add(new EdgeData
        {
            V0 = v0,
            V1 = v1,
            Error = error
        });
    }
    
    /// <summary>
    /// Collapse an edge by merging two vertices.
    /// </summary>
    private static void CollapseEdge(List<VertexData> vertices, List<TriangleData> triangles,
                                  List<EdgeData> edges, EdgeData edge)
    {
        // Merge V1 into V0
        int keepVertex = edge.V0;
        int removeVertex = edge.V1;
        
        // Update position to midpoint
        vertices[keepVertex].Position = (vertices[keepVertex].Position + vertices[removeVertex].Position) * 0.5f;
        vertices[keepVertex].Normal = Vector3.Normalize(vertices[keepVertex].Normal + vertices[removeVertex].Normal);
        
        // Update triangles
        for (int i = triangles.Count - 1; i >= 0; i--)
        {
            var tri = triangles[i];
            
            if (tri.V0 == removeVertex) tri.V0 = keepVertex;
            if (tri.V1 == removeVertex) tri.V1 = keepVertex;
            if (tri.V2 == removeVertex) tri.V2 = keepVertex;
            
            // Remove degenerate triangles
            if (tri.V0 == tri.V1 || tri.V1 == tri.V2 || tri.V2 == tri.V0)
            {
                triangles.RemoveAt(i);
            }
        }
        
        // Remove edges involving the removed vertex
        for (int i = edges.Count - 1; i >= 0; i--)
        {
            if (edges[i].V0 == removeVertex || edges[i].V1 == removeVertex)
            {
                edges.RemoveAt(i);
            }
            else if (edges[i].V0 == removeVertex)
            {
                edges[i].V0 = keepVertex;
            }
            else if (edges[i].V1 == removeVertex)
            {
                edges[i].V1 = keepVertex;
            }
        }
    }
    
    /// <summary>
    /// Recalculate vertex normals from triangle data.
    /// </summary>
    private static void RecalculateNormals(SimplifiedMesh mesh)
    {
        var normals = new Vector3[mesh.Vertices.Length];
        var counts = new int[mesh.Vertices.Length];
        
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int i0 = (int)mesh.Indices[i];
            int i1 = (int)mesh.Indices[i + 1];
            int i2 = (int)mesh.Indices[i + 2];
            
            var v0 = mesh.Vertices[i0];
            var v1 = mesh.Vertices[i1];
            var v2 = mesh.Vertices[i2];
            
            // Calculate face normal
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
            
            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
            counts[i0]++;
            counts[i1]++;
            counts[i2]++;
        }
        
        // Normalize
        for (int i = 0; i < normals.Length; i++)
        {
            if (counts[i] > 0)
                mesh.Normals[i] = Vector3.Normalize(normals[i]);
            else
                mesh.Normals[i] = Vector3.UnitY;
        }
    }
    
    /// <summary>
    /// Generate multiple LOD levels for a mesh.
    /// </summary>
    public static SimplifiedMesh[] GenerateLODs(MeshData sourceMesh, int lodCount)
    {
        var lods = new SimplifiedMesh[lodCount];
        
        // LOD 0 = original mesh
        lods[0] = new SimplifiedMesh(sourceMesh);
        lods[0].LODLevel = 0;
        lods[0].OriginalVertexCount = sourceMesh.Vertices.Length;
        lods[0].OriginalTriangleCount = sourceMesh.Indices.Length / 3;
        lods[0].SimplifiedVertexCount = sourceMesh.Vertices.Length;
        lods[0].SimplifiedTriangleCount = sourceMesh.Indices.Length / 3;
        
        // Generate LODs with decreasing polygon count
        for (int i = 1; i < lodCount; i++)
        {
            float ratio = 1.0f / MathF.Pow(2.0f, i); // Each LOD halves the triangles
            lods[i] = Simplify(sourceMesh, ratio);
            lods[i].LODLevel = i;
        }
        
        return lods;
    }
}

/// <summary>
/// Simplified mesh data with LOD information.
/// </summary>
public class SimplifiedMesh
{
    public Vector3[] Vertices { get; set; }
    public Vector3[] Normals { get; set; }
    public Vector2[] UVs { get; set; }
    public uint[] Indices { get; set; }
    
    public int LODLevel { get; set; }
    public int OriginalVertexCount { get; set; }
    public int OriginalTriangleCount { get; set; }
    public int SimplifiedVertexCount { get; set; }
    public int SimplifiedTriangleCount { get; set; }
    
    public float ReductionRatio => (float)SimplifiedTriangleCount / OriginalTriangleCount;
    
    public SimplifiedMesh() { }
    
    public SimplifiedMesh(MeshData sourceMesh)
    {
        Vertices = sourceMesh.Vertices;
        Normals = sourceMesh.Normals;
        UVs = sourceMesh.UVs;
        Indices = sourceMesh.Indices;
    }
}

/// <summary>
/// Mesh data structure.
/// </summary>
public class MeshData
{
    public Vector3[] Vertices { get; set; }
    public Vector3[] Normals { get; set; }
    public Vector2[] UVs { get; set; }
    public uint[] Indices { get; set; }
}

// Internal data structures for simplification
internal class VertexData
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public int Index;
}

internal class TriangleData
{
    public int V0, V1, V2;
}

internal class EdgeData
{
    public int V0, V1;
    public float Error;
}
