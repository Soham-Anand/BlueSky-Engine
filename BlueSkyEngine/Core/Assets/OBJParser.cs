using System.Numerics;

namespace BlueSky.Core.Assets;

/// <summary>
/// OBJ file parser for mesh import.
/// </summary>
public class OBJParser
{
    public class OBJMesh
    {
        public List<Vector3> Positions { get; set; } = new();
        public List<Vector3> Normals { get; set; } = new();
        public List<Vector2> UVs { get; set; } = new();
        public List<OBJFace> Faces { get; set; } = new();
        public BoundingBox Bounds { get; set; } = new();
    }

    public class OBJFace
    {
        public List<OBJVertex> Vertices { get; set; } = new();
    }

    public class OBJVertex
    {
        public int PositionIndex { get; set; } = -1;  // 1-based in OBJ
        public int NormalIndex { get; set; } = -1;
        public int UVIndex { get; set; } = -1;
    }

    public class BoundingBox
    {
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }
    }

    public static OBJMesh? Parse(string filePath)
    {
        try
        {
            var mesh = new OBJMesh();
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                var type = parts[0].ToLowerInvariant();

                switch (type)
                {
                    case "v":
                        if (parts.Length >= 4)
                        {
                            var pos = new Vector3(
                                float.Parse(parts[1]),
                                float.Parse(parts[2]),
                                float.Parse(parts[3])
                            );
                            mesh.Positions.Add(pos);
                        }
                        break;

                    case "vn":
                        if (parts.Length >= 4)
                        {
                            var normal = new Vector3(
                                float.Parse(parts[1]),
                                float.Parse(parts[2]),
                                float.Parse(parts[3])
                            );
                            mesh.Normals.Add(normal);
                        }
                        break;

                    case "vt":
                        if (parts.Length >= 3)
                        {
                            var uv = new Vector2(
                                float.Parse(parts[1]),
                                float.Parse(parts[2])
                            );
                            mesh.UVs.Add(uv);
                        }
                        break;

                    case "f":
                        var face = new OBJFace();
                        for (int i = 1; i < parts.Length; i++)
                        {
                            var vertex = ParseVertex(parts[i]);
                            face.Vertices.Add(vertex);
                        }
                        mesh.Faces.Add(face);
                        break;
                }
            }

            // Compute bounding box
            if (mesh.Positions.Count > 0)
            {
                var min = mesh.Positions[0];
                var max = mesh.Positions[0];
                foreach (var pos in mesh.Positions)
                {
                    min = Vector3.Min(min, pos);
                    max = Vector3.Max(max, pos);
                }
                mesh.Bounds = new BoundingBox { Min = min, Max = max };
            }

            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OBJParser] Failed to parse OBJ: {ex.Message}");
            return null;
        }
    }

    private static OBJVertex ParseVertex(string vertexStr)
    {
        var vertex = new OBJVertex();
        var indices = vertexStr.Split('/');

        // Position index (required)
        if (indices.Length > 0 && !string.IsNullOrEmpty(indices[0]))
        {
            vertex.PositionIndex = int.Parse(indices[0]);
        }

        // UV index (optional)
        if (indices.Length > 1 && !string.IsNullOrEmpty(indices[1]))
        {
            vertex.UVIndex = int.Parse(indices[1]);
        }

        // Normal index (optional)
        if (indices.Length > 2 && !string.IsNullOrEmpty(indices[2]))
        {
            vertex.NormalIndex = int.Parse(indices[2]);
        }

        return vertex;
    }

    /// <summary>
    /// Convert OBJ mesh to engine-ready vertex/index data.
    /// </summary>
    public static (byte[] vertexData, byte[] indexData, int vertexCount, int indexCount) ConvertToEngineData(OBJMesh mesh)
    {
        // Build unique vertices (deduplicate by position/normal/uv combination)
        var uniqueVertices = new Dictionary<string, int>();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<ushort>();

        int vertexIndex = 0;

        foreach (var face in mesh.Faces)
        {
            // Triangulate face (OBJ supports n-gons, we'll triangulate as fan)
            for (int i = 1; i < face.Vertices.Count - 1; i++)
            {
                // Triangle: v0, vi, vi+1
                var triVertices = new[] { face.Vertices[0], face.Vertices[i], face.Vertices[i + 1] };

                // Auto-calculate flat normal for this face if normals are missing
                Vector3 autoNormal = Vector3.Zero;
                bool needsAutoNormal = true;
                foreach (var v in triVertices)
                {
                    if (v.NormalIndex > 0) needsAutoNormal = false;
                }
                
                if (needsAutoNormal)
                {
                    Vector3 p0 = mesh.Positions[triVertices[0].PositionIndex - 1];
                    Vector3 p1 = mesh.Positions[triVertices[1].PositionIndex - 1];
                    Vector3 p2 = mesh.Positions[triVertices[2].PositionIndex - 1];
                    Vector3 u = p1 - p0;
                    Vector3 vec = p2 - p0;
                    autoNormal = Vector3.Normalize(Vector3.Cross(u, vec));
                    if (float.IsNaN(autoNormal.X)) autoNormal = Vector3.UnitY; // Fallback
                }

                foreach (var v in triVertices)
                {
                    // To prevent shared vertices of faceted normal blending without index, we bake autoNormal into the key
                    var key = needsAutoNormal ? $"{v.PositionIndex}/{v.UVIndex}/auto_{autoNormal.X}_{autoNormal.Y}_{autoNormal.Z}" : $"{v.PositionIndex}/{v.UVIndex}/{v.NormalIndex}";

                    if (uniqueVertices.TryGetValue(key, out int existingIndex))
                    {
                        indices.Add((ushort)existingIndex);
                    }
                    else
                    {
                        // Get position (OBJ is 1-based)
                        Vector3 pos = Vector3.Zero;
                        if (v.PositionIndex > 0 && v.PositionIndex <= mesh.Positions.Count)
                        {
                            pos = mesh.Positions[v.PositionIndex - 1];
                        }

                        // Get normal
                        Vector3 normal = autoNormal;
                        if (!needsAutoNormal && v.NormalIndex > 0 && v.NormalIndex <= mesh.Normals.Count)
                        {
                            normal = mesh.Normals[v.NormalIndex - 1];
                        }

                        // Get UV
                        Vector2 uv = Vector2.Zero;
                        if (v.UVIndex > 0 && v.UVIndex <= mesh.UVs.Count)
                        {
                            uv = mesh.UVs[v.UVIndex - 1];
                        }

                        vertices.Add(pos);
                        normals.Add(normal);
                        uvs.Add(uv);
                        uniqueVertices[key] = vertexIndex;
                        indices.Add((ushort)vertexIndex);
                        vertexIndex++;
                    }
                }
            }
        }

        // Pack vertex data (Position + Normal + UV)
        var vertexData = new byte[vertices.Count * 32]; // 12 (pos) + 12 (normal) + 8 (uv) = 32 bytes per vertex
        var indexData = new byte[indices.Count * 2]; // 2 bytes per index (ushort)

        for (int i = 0; i < vertices.Count; i++)
        {
            int offset = i * 32;

            // Position (12 bytes)
            var posBytes = BitConverter.GetBytes(vertices[i].X);
            Buffer.BlockCopy(posBytes, 0, vertexData, offset, 4);
            posBytes = BitConverter.GetBytes(vertices[i].Y);
            Buffer.BlockCopy(posBytes, 0, vertexData, offset + 4, 4);
            posBytes = BitConverter.GetBytes(vertices[i].Z);
            Buffer.BlockCopy(posBytes, 0, vertexData, offset + 8, 4);

            // Normal (12 bytes)
            var normBytes = BitConverter.GetBytes(normals[i].X);
            Buffer.BlockCopy(normBytes, 0, vertexData, offset + 12, 4);
            normBytes = BitConverter.GetBytes(normals[i].Y);
            Buffer.BlockCopy(normBytes, 0, vertexData, offset + 16, 4);
            normBytes = BitConverter.GetBytes(normals[i].Z);
            Buffer.BlockCopy(normBytes, 0, vertexData, offset + 20, 4);

            // UV (8 bytes)
            var uvBytes = BitConverter.GetBytes(uvs[i].X);
            Buffer.BlockCopy(uvBytes, 0, vertexData, offset + 24, 4);
            uvBytes = BitConverter.GetBytes(uvs[i].Y);
            Buffer.BlockCopy(uvBytes, 0, vertexData, offset + 28, 4);
        }

        // Pack index data
        for (int i = 0; i < indices.Count; i++)
        {
            var indexBytes = BitConverter.GetBytes(indices[i]);
            Buffer.BlockCopy(indexBytes, 0, indexData, i * 2, 2);
        }

        return (vertexData, indexData, vertices.Count, indices.Count);
    }
}
