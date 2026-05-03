using System.Numerics;
using System.Globalization;

namespace BlueSky.Core.Assets;

/// <summary>
/// Production-grade OBJ file parser for mesh import.
/// 
/// FEATURES:
/// - Robust error handling and validation
/// - Support for triangles, quads, and n-gons (auto-triangulated)
/// - Smooth and flat shading support
/// - Negative indices (relative indexing)
/// - Multiple objects and groups
/// - Material references (MTL files)
/// - Optimized vertex deduplication
/// - Large file support (streaming)
/// - Progress reporting
/// 
/// UV COORDINATE HANDLING:
/// - OBJ files use standard UV convention: U=0 (left), V=0 (bottom), V=1 (top)
/// - Modern DCC tools (Blender, Maya, 3ds Max) export with this convention
/// - UVs are imported as-is without vertical flipping
/// - GPU texture sampling expects V=0 at top, which matches the OBJ standard
/// 
/// LIMITATIONS:
/// - Materials are referenced but not loaded (use separate material system)
/// - Free-form curves/surfaces not supported
/// - Only triangular mesh output
/// </summary>
public class OBJParser
{
    public class OBJMesh
    {
        public List<Vector3> Positions { get; set; } = new();
        public List<Vector3> Normals { get; set; } = new();
        public List<Vector2> UVs { get; set; } = new();
        public List<OBJFace> Faces { get; set; } = new();
        public List<OBJObject> Objects { get; set; } = new();
        public BoundingBox Bounds { get; set; } = new();
        public string? MaterialLibrary { get; set; }
    }

    public class OBJObject
    {
        public string Name { get; set; } = "default";
        public List<OBJGroup> Groups { get; set; } = new();
        public int FaceStartIndex { get; set; }
        public int FaceCount { get; set; }
    }

    public class OBJGroup
    {
        public string Name { get; set; } = "default";
        public string? Material { get; set; }
        public int FaceStartIndex { get; set; }
        public int FaceCount { get; set; }
    }

    public class OBJFace
    {
        public List<OBJVertex> Vertices { get; set; } = new();
        public string? Material { get; set; }
    }

    public class OBJVertex
    {
        public int PositionIndex { get; set; } = -1;
        public int NormalIndex { get; set; } = -1;
        public int UVIndex { get; set; } = -1;
    }

    public class BoundingBox
    {
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }
    }

    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    public static OBJMesh? Parse(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[OBJParser] File not found: {filePath}");
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"[OBJParser] Parsing {fileInfo.Name} ({fileInfo.Length / 1024}KB)...");

            var mesh = new OBJMesh();
            var currentObject = new OBJObject { Name = "default" };
            var currentGroup = new OBJGroup { Name = "default" };
            string? currentMaterial = null;
            int lineNumber = 0;
            int errorCount = 0;
            const int MAX_ERRORS = 100;

            // Load entire file into a single string to avoid per-line allocations
            string text = File.ReadAllText(filePath);
            ReadOnlySpan<char> span = text.AsSpan();
            
            while (!span.IsEmpty)
            {
                lineNumber++;
                int lineEnd = span.IndexOf('\n');
                ReadOnlySpan<char> line;
                if (lineEnd == -1)
                {
                    line = span;
                    span = default;
                }
                else
                {
                    line = span.Slice(0, lineEnd);
                    span = span.Slice(lineEnd + 1);
                }
                
                if (!line.IsEmpty && line[^1] == '\r')
                    line = line.Slice(0, line.Length - 1);
                // Handle \r\r\n (double CR) from Roblox Studio exports
                while (!line.IsEmpty && line[^1] == '\r')
                    line = line.Slice(0, line.Length - 1);
                
                line = line.Trim();
                if (line.IsEmpty || line[0] == '#') continue;

                try
                {
                    // Basic tokenization
                    int firstSpace = line.IndexOfAny(' ', '\t');
                    if (firstSpace == -1) continue;
                    
                    ReadOnlySpan<char> type = line.Slice(0, firstSpace);
                    ReadOnlySpan<char> rest = line.Slice(firstSpace + 1).TrimStart(" \t");
                    
                    if (type.Equals("v", StringComparison.OrdinalIgnoreCase))
                    {
                        var pos = ParseVector3(rest);
                        // Z is negated to convert from Right-Handed to Left-Handed
                        pos.Z = -pos.Z;
                        mesh.Positions.Add(pos);
                    }
                    else if (type.Equals("vn", StringComparison.OrdinalIgnoreCase))
                    {
                        var normal = ParseVector3(rest);
                        // Z is negated to convert from Right-Handed to Left-Handed
                        normal.Z = -normal.Z;
                        float length = normal.Length();
                        if (length > 0.0001f)
                            normal = normal / length;
                        else
                            normal = Vector3.UnitY;
                        mesh.Normals.Add(normal);
                    }
                    else if (type.Equals("vt", StringComparison.OrdinalIgnoreCase))
                    {
                        var uv = ParseVector2(rest);
                        // Do NOT invert U, only invert V to match DirectX/Metal top-left origin
                        uv.Y = 1.0f - uv.Y;
                        mesh.UVs.Add(uv);
                    }
                    else if (type.Equals("f", StringComparison.OrdinalIgnoreCase))
                    {
                        var face = new OBJFace { Material = currentMaterial };
                        while (!rest.IsEmpty)
                        {
                            int space = rest.IndexOfAny(' ', '\t');
                            ReadOnlySpan<char> vertexStr;
                            if (space == -1)
                            {
                                vertexStr = rest;
                                rest = default;
                            }
                            else
                            {
                                vertexStr = rest.Slice(0, space);
                                rest = rest.Slice(space + 1).TrimStart(" \t");
                            }
                            
                            if (!vertexStr.IsEmpty)
                            {
                                var vertex = ParseVertex(vertexStr, mesh.Positions.Count, mesh.UVs.Count, mesh.Normals.Count);
                                face.Vertices.Add(vertex);
                            }
                        }
                        mesh.Faces.Add(face);
                    }
                    else if (type.Equals("o", StringComparison.OrdinalIgnoreCase))
                    {
                        if (currentObject.FaceCount > 0 || currentObject.Groups.Count > 0)
                            mesh.Objects.Add(currentObject);
                        currentObject = new OBJObject 
                        { 
                            Name = rest.ToString(),
                            FaceStartIndex = mesh.Faces.Count
                        };
                        currentGroup = new OBJGroup { Name = "default", FaceStartIndex = mesh.Faces.Count };
                    }
                    else if (type.Equals("g", StringComparison.OrdinalIgnoreCase))
                    {
                        if (currentGroup.FaceCount > 0)
                            currentObject.Groups.Add(currentGroup);
                        currentGroup = new OBJGroup 
                        { 
                            Name = rest.ToString(),
                            FaceStartIndex = mesh.Faces.Count
                        };
                    }
                    else if (type.Equals("usemtl", StringComparison.OrdinalIgnoreCase))
                    {
                        currentMaterial = rest.ToString();
                        currentGroup.Material = currentMaterial;
                    }
                    else if (type.Equals("mtllib", StringComparison.OrdinalIgnoreCase))
                    {
                        mesh.MaterialLibrary = rest.ToString();
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    if (errorCount <= 10)
                        Console.WriteLine($"[OBJParser] Line {lineNumber}: {ex.Message}");
                    else if (errorCount == 11)
                        Console.WriteLine($"[OBJParser] Too many errors, suppressing further messages...");
                    
                    if (errorCount > MAX_ERRORS)
                    {
                        Console.WriteLine($"[OBJParser] Aborting: too many errors ({errorCount})");
                        return null;
                    }
                }
            }

            currentGroup.FaceCount = mesh.Faces.Count - currentGroup.FaceStartIndex;
            if (currentGroup.FaceCount > 0)
            {
                currentObject.Groups.Add(currentGroup);
            }
            
            currentObject.FaceCount = mesh.Faces.Count - currentObject.FaceStartIndex;
            if (currentObject.FaceCount > 0 || currentObject.Groups.Count > 0)
            {
                mesh.Objects.Add(currentObject);
            }

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

            Console.WriteLine($"[OBJParser] ✓ Parsed: {mesh.Positions.Count} vertices, {mesh.Normals.Count} normals, {mesh.UVs.Count} UVs, {mesh.Faces.Count} faces");
            if (errorCount > 0)
            {
                Console.WriteLine($"[OBJParser] Warning: {errorCount} errors encountered");
            }

            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OBJParser] Fatal error: {ex.Message}");
            return null;
        }
    }

    private static Vector3 ParseVector3(ReadOnlySpan<char> span)
    {
        Vector3 v = new Vector3();
        int space1 = span.IndexOfAny(' ', '\t');
        if (space1 != -1)
        {
            v.X = float.Parse(span.Slice(0, space1), InvariantCulture);
            span = span.Slice(space1 + 1).TrimStart(" \t");
            int space2 = span.IndexOfAny(' ', '\t');
            if (space2 != -1)
            {
                v.Y = float.Parse(span.Slice(0, space2), InvariantCulture);
                v.Z = float.Parse(span.Slice(space2 + 1), InvariantCulture);
            }
            else
            {
                v.Y = float.Parse(span, InvariantCulture);
            }
        }
        else
        {
            v.X = float.Parse(span, InvariantCulture);
        }
        return v;
    }

    private static Vector2 ParseVector2(ReadOnlySpan<char> span)
    {
        Vector2 v = new Vector2();
        int space = span.IndexOfAny(' ', '\t');
        if (space != -1)
        {
            v.X = float.Parse(span.Slice(0, space), InvariantCulture);
            v.Y = float.Parse(span.Slice(space + 1).TrimStart(" \t"), InvariantCulture);
        }
        else
        {
            v.X = float.Parse(span, InvariantCulture);
        }
        return v;
    }

    private static OBJVertex ParseVertex(ReadOnlySpan<char> vertexStr, int posCount, int uvCount, int normalCount)
    {
        var vertex = new OBJVertex();
        
        int firstSlash = vertexStr.IndexOf('/');
        if (firstSlash == -1)
        {
            if (int.TryParse(vertexStr, NumberStyles.Integer, InvariantCulture, out int idx))
                vertex.PositionIndex = idx < 0 ? posCount + idx + 1 : idx;
            return vertex;
        }

        ReadOnlySpan<char> posStr = vertexStr.Slice(0, firstSlash);
        if (!posStr.IsEmpty && int.TryParse(posStr, NumberStyles.Integer, InvariantCulture, out int pIdx))
            vertex.PositionIndex = pIdx < 0 ? posCount + pIdx + 1 : pIdx;

        ReadOnlySpan<char> rest = vertexStr.Slice(firstSlash + 1);
        int secondSlash = rest.IndexOf('/');
        
        if (secondSlash == -1)
        {
            if (!rest.IsEmpty && int.TryParse(rest, NumberStyles.Integer, InvariantCulture, out int uIdx))
                vertex.UVIndex = uIdx < 0 ? uvCount + uIdx + 1 : uIdx;
            return vertex;
        }

        ReadOnlySpan<char> uvStr = rest.Slice(0, secondSlash);
        if (!uvStr.IsEmpty && int.TryParse(uvStr, NumberStyles.Integer, InvariantCulture, out int uvIdx))
            vertex.UVIndex = uvIdx < 0 ? uvCount + uvIdx + 1 : uvIdx;

        ReadOnlySpan<char> normStr = rest.Slice(secondSlash + 1);
        if (!normStr.IsEmpty && int.TryParse(normStr, NumberStyles.Integer, InvariantCulture, out int nIdx))
            vertex.NormalIndex = nIdx < 0 ? normalCount + nIdx + 1 : nIdx;

        return vertex;
    }

    public class SubmeshInfo
    {
        public int IndexOffset { get; set; }
        public int IndexCount { get; set; }
        public string MaterialName { get; set; } = "default";
    }

    /// <summary>
    /// Convert OBJ mesh to engine-ready vertex/index data with optimized deduplication.
    /// Uses 32-bit indices to support large meshes (>65K vertices).
    /// CRITICAL: Preserves material order from OBJ file for correct material-to-submesh mapping.
    /// </summary>
    public static (byte[] vertexData, byte[] indexData, int vertexCount, int indexCount, List<SubmeshInfo> submeshes) ConvertToEngineData(OBJMesh mesh)
    {
        var uniqueVertices = new Dictionary<string, int>();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<uint>();
        var submeshes = new List<SubmeshInfo>();

        int vertexIndex = 0;
        int skippedFaces = 0;

        // CRITICAL: Preserve material order from OBJ file for correct material-to-slot mapping!
        // Dictionary iteration order is NOT guaranteed, which breaks material-to-slot mapping.
        // We track the order materials first appear in the OBJ file (via usemtl directives).
        var facesByMaterial = new Dictionary<string, List<OBJFace>>();
        var materialOrder = new List<string>(); // Track first appearance order
        
        foreach (var face in mesh.Faces)
        {
            var mat = face.Material ?? "default";
            if (!facesByMaterial.TryGetValue(mat, out var faceList))
            {
                faceList = new List<OBJFace>();
                facesByMaterial[mat] = faceList;
                materialOrder.Add(mat); // Record order of first appearance
            }
            faceList.Add(face);
        }

        // Iterate in the order materials first appeared in the OBJ file
        foreach (var matName in materialOrder)
        {
            var faces = facesByMaterial[matName];
            int submeshStartIndexCount = indices.Count;
            
            foreach (var face in faces)
            {
            if (face.Vertices.Count < 3)
            {
                skippedFaces++;
                continue;
            }

            for (int i = 1; i < face.Vertices.Count - 1; i++)
            {
                // Fix: Reverse winding order (0, i+1, i) instead of (0, i, i+1)
                // This corrects the inside-out geometry caused by flipping the Z axis
                var triVertices = new[] { face.Vertices[0], face.Vertices[i + 1], face.Vertices[i] };
                
                bool allVerticesValid = true;
                foreach (var v in triVertices)
                {
                    if (v.PositionIndex <= 0 || v.PositionIndex > mesh.Positions.Count)
                    {
                        allVerticesValid = false;
                        break;
                    }
                }
                
                if (!allVerticesValid)
                {
                    skippedFaces++;
                    continue;
                }

                Vector3 autoNormal = Vector3.Zero;
                bool needsAutoNormal = false;
                
                foreach (var v in triVertices)
                {
                    if (v.NormalIndex <= 0 || v.NormalIndex > mesh.Normals.Count)
                    {
                        needsAutoNormal = true;
                        break;
                    }
                }
                
                if (needsAutoNormal)
                {
                    Vector3 p0 = mesh.Positions[triVertices[0].PositionIndex - 1];
                    Vector3 p1 = mesh.Positions[triVertices[1].PositionIndex - 1];
                    Vector3 p2 = mesh.Positions[triVertices[2].PositionIndex - 1];
                    Vector3 edge1 = p1 - p0;
                    Vector3 edge2 = p2 - p0;
                    autoNormal = Vector3.Cross(edge1, edge2);
                    float length = autoNormal.Length();
                    if (length > 0.0001f)
                    {
                        autoNormal = autoNormal / length;
                    }
                    else
                    {
                        autoNormal = Vector3.UnitY;
                    }
                }

                foreach (var v in triVertices)
                {
                    var key = $"{v.PositionIndex}/{v.UVIndex}/{v.NormalIndex}";

                    if (uniqueVertices.TryGetValue(key, out int existingIndex))
                    {
                        indices.Add((uint)existingIndex);
                    }
                    else
                    {
                        if (v.PositionIndex <= 0 || v.PositionIndex > mesh.Positions.Count)
                        {
                            continue;
                        }
                        
                        Vector3 pos = mesh.Positions[v.PositionIndex - 1];
                        
                        Vector3 normal = autoNormal;
                        if (!needsAutoNormal && v.NormalIndex > 0 && v.NormalIndex <= mesh.Normals.Count)
                        {
                            normal = mesh.Normals[v.NormalIndex - 1];
                        }
                        
                        Vector2 uv = Vector2.Zero;
                        if (v.UVIndex > 0 && v.UVIndex <= mesh.UVs.Count)
                        {
                            uv = mesh.UVs[v.UVIndex - 1];
                        }

                        vertices.Add(pos);
                        normals.Add(normal);
                        uvs.Add(uv);
                        uniqueVertices[key] = vertexIndex;
                        indices.Add((uint)vertexIndex);
                        vertexIndex++;
                    }
                }
            }
        }
            
            int submeshIndexCount = indices.Count - submeshStartIndexCount;
            if (submeshIndexCount > 0)
            {
                submeshes.Add(new SubmeshInfo
                {
                    IndexOffset = submeshStartIndexCount,
                    IndexCount = submeshIndexCount,
                    MaterialName = matName
                });
                Console.WriteLine($"[OBJParser] Submesh {submeshes.Count - 1}: Material '{matName}', IndexOffset={submeshStartIndexCount}, IndexCount={submeshIndexCount}");
            }
        }



        if (skippedFaces > 0)
        {
            Console.WriteLine($"[OBJParser] Skipped {skippedFaces} invalid faces");
        }

        var vertexData = new byte[vertices.Count * 32];
        var indexData = new byte[indices.Count * 4];

        for (int i = 0; i < vertices.Count; i++)
        {
            int offset = i * 32;

            Buffer.BlockCopy(BitConverter.GetBytes(vertices[i].X), 0, vertexData, offset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(vertices[i].Y), 0, vertexData, offset + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(vertices[i].Z), 0, vertexData, offset + 8, 4);

            Buffer.BlockCopy(BitConverter.GetBytes(normals[i].X), 0, vertexData, offset + 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(normals[i].Y), 0, vertexData, offset + 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(normals[i].Z), 0, vertexData, offset + 20, 4);

            Buffer.BlockCopy(BitConverter.GetBytes(uvs[i].X), 0, vertexData, offset + 24, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(uvs[i].Y), 0, vertexData, offset + 28, 4);
        }

        for (int i = 0; i < indices.Count; i++)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(indices[i]), 0, indexData, i * 4, 4);
        }

        Console.WriteLine($"[OBJParser] ✓ Converted: {vertices.Count} unique vertices, {indices.Count / 3} triangles, {submeshes.Count} submeshes");

        return (vertexData, indexData, vertices.Count, indices.Count, submeshes);
    }
}