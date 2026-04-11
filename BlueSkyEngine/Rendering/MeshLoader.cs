using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using System.Globalization;

namespace BlueSky.Rendering
{
    public struct VertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoords;
        public Vector3 Tangent;
        public Vector3 Bitangent;
    }

    public class MeshData
    {
        public VertexData[] Vertices { get; set; } = Array.Empty<VertexData>();
        public uint[] Indices { get; set; } = Array.Empty<uint>();
        public string Name { get; set; } = string.Empty;
        public int MaterialIndex { get; set; } = -1;
    }

    public class ModelData
    {
        public List<MeshData> Meshes { get; set; } = new();
        public List<MaterialData> Materials { get; set; } = new();
        public string Name { get; set; } = string.Empty;
    }

    public class MaterialData
    {
        public string Name { get; set; } = string.Empty;
        public Vector3 Albedo { get; set; } = Vector3.One;
        public float Metallic { get; set; } = 0.0f;
        public float Roughness { get; set; } = 0.5f;
        public string? AlbedoTexture { get; set; }
        public string? NormalTexture { get; set; }
        public string? MetallicRoughnessTexture { get; set; }
    }

    /// <summary>
    /// Built-in OBJ/MTL mesh loader — zero external dependencies.
    /// Supports OBJ files with positions, normals, tex coords, and faces.
    /// Compatible with WinXP (i5-2410m Intel HD 3000) through Mac Studio M4 Max.
    /// </summary>
    public static class MeshLoader
    {
        public static ModelData? LoadModel(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.WriteLine($"[MeshLoader] File not found: {path}");
                return null;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".obj" => LoadOBJ(path),
                _ => null
            };
        }

        public static MeshData? LoadMesh(string path)
        {
            var model = LoadModel(path);
            return model?.Meshes.Count > 0 ? model.Meshes[0] : null;
        }

        private static ModelData? LoadOBJ(string path)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var texCoords = new List<Vector2>();
            var vertices = new List<VertexData>();
            var indices = new List<uint>();
            var vertexMap = new Dictionary<string, uint>();

            string? mtlLib = null;
            var basePath = Path.GetDirectoryName(path) ?? "";

            try
            {
                foreach (var rawLine in File.ReadLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("mtllib "))
                    {
                        mtlLib = line.Substring(7).Trim();
                    }
                    else if (line.StartsWith("v "))
                    {
                        var parts = line.Substring(2).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            positions.Add(new Vector3(
                                float.Parse(parts[0], CultureInfo.InvariantCulture),
                                float.Parse(parts[1], CultureInfo.InvariantCulture),
                                float.Parse(parts[2], CultureInfo.InvariantCulture)));
                        }
                    }
                    else if (line.StartsWith("vn "))
                    {
                        var parts = line.Substring(3).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            normals.Add(new Vector3(
                                float.Parse(parts[0], CultureInfo.InvariantCulture),
                                float.Parse(parts[1], CultureInfo.InvariantCulture),
                                float.Parse(parts[2], CultureInfo.InvariantCulture)));
                        }
                    }
                    else if (line.StartsWith("vt "))
                    {
                        var parts = line.Substring(3).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            texCoords.Add(new Vector2(
                                float.Parse(parts[0], CultureInfo.InvariantCulture),
                                float.Parse(parts[1], CultureInfo.InvariantCulture)));
                        }
                    }
                    else if (line.StartsWith("f "))
                    {
                        var parts = line.Substring(2).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        
                        // Triangulate fan-style for polygons with > 3 vertices
                        for (int i = 1; i < parts.Length - 1; i++)
                        {
                            AddVertex(parts[0], positions, normals, texCoords, vertices, indices, vertexMap);
                            AddVertex(parts[i], positions, normals, texCoords, vertices, indices, vertexMap);
                            AddVertex(parts[i + 1], positions, normals, texCoords, vertices, indices, vertexMap);
                        }
                    }
                }

                // Generate normals if none were provided
                if (normals.Count == 0)
                {
                    GenerateFlatNormals(vertices, indices);
                }

                var model = new ModelData
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Meshes = { new MeshData
                    {
                        Vertices = vertices.ToArray(),
                        Indices = indices.ToArray(),
                        Name = Path.GetFileNameWithoutExtension(path)
                    }},
                    Materials = { new MaterialData { Name = "Default" } }
                };

                // Load MTL if referenced
                if (mtlLib != null)
                {
                    var mtlPath = Path.Combine(basePath, mtlLib);
                    if (File.Exists(mtlPath))
                    {
                        LoadMTL(mtlPath, model);
                    }
                }

                Console.WriteLine($"[MeshLoader] Loaded '{path}': {vertices.Count} vertices, {indices.Count / 3} triangles");
                return model;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MeshLoader] Error loading '{path}': {ex.Message}");
                return null;
            }
        }

        private static void AddVertex(string faceVertex, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> texCoords, List<VertexData> vertices, List<uint> indices, Dictionary<string, uint> vertexMap)
        {
            if (vertexMap.TryGetValue(faceVertex, out var existingIndex))
            {
                indices.Add(existingIndex);
                return;
            }

            var parts = faceVertex.Split('/');
            var vertex = new VertexData();

            // Position (required, 1-indexed)
            int posIdx = int.Parse(parts[0]) - 1;
            if (posIdx < 0) posIdx += positions.Count + 1; // Handle negative indices
            vertex.Position = positions[posIdx];

            // Tex coords (optional)
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                int tcIdx = int.Parse(parts[1]) - 1;
                if (tcIdx < 0) tcIdx += texCoords.Count + 1;
                vertex.TexCoords = texCoords[tcIdx];
            }

            // Normal (optional)
            if (parts.Length > 2 && parts[2].Length > 0)
            {
                int nIdx = int.Parse(parts[2]) - 1;
                if (nIdx < 0) nIdx += normals.Count + 1;
                vertex.Normal = normals[nIdx];
            }

            uint newIndex = (uint)vertices.Count;
            vertices.Add(vertex);
            indices.Add(newIndex);
            vertexMap[faceVertex] = newIndex;
        }

        private static void GenerateFlatNormals(List<VertexData> vertices, List<uint> indices)
        {
            for (int i = 0; i < indices.Count; i += 3)
            {
                if (i + 2 >= indices.Count) break;
                
                var v0 = vertices[(int)indices[i]];
                var v1 = vertices[(int)indices[i + 1]];
                var v2 = vertices[(int)indices[i + 2]];

                var edge1 = v1.Position - v0.Position;
                var edge2 = v2.Position - v0.Position;
                var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

                v0.Normal = normal; vertices[(int)indices[i]] = v0;
                v1.Normal = normal; vertices[(int)indices[i + 1]] = v1;
                v2.Normal = normal; vertices[(int)indices[i + 2]] = v2;
            }
        }

        private static void LoadMTL(string path, ModelData model)
        {
            try
            {
                MaterialData? current = null;
                var basePath = Path.GetDirectoryName(path) ?? "";

                foreach (var rawLine in File.ReadLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("newmtl "))
                    {
                        current = new MaterialData { Name = line.Substring(7).Trim() };
                        model.Materials.Add(current);
                    }
                    else if (current != null)
                    {
                        if (line.StartsWith("Kd "))
                        {
                            var parts = line.Substring(3).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                current.Albedo = new Vector3(
                                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                                    float.Parse(parts[2], CultureInfo.InvariantCulture));
                            }
                        }
                        else if (line.StartsWith("map_Kd "))
                        {
                            current.AlbedoTexture = Path.Combine(basePath, line.Substring(7).Trim());
                        }
                        else if (line.StartsWith("bump ") || line.StartsWith("map_Bump "))
                        {
                            var texName = line.Contains("bump ") ? line.Substring(line.IndexOf("bump ") + 5).Trim() : line.Substring(9).Trim();
                            current.NormalTexture = Path.Combine(basePath, texName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MeshLoader] Error loading MTL '{path}': {ex.Message}");
            }
        }
    }
}
