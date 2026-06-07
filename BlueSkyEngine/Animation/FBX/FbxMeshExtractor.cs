using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Animation.FBX;

public struct FbxMeshData
{
    public Vector3[] Vertices;
    public uint[] Indices;
    public Vector3[] Normals;
    public Vector2[] UVs;
    public string MaterialName;
}

public struct FbxGlobalSettings
{
    public float UnitScaleFactor; // cm per unit (default 1.0 = centimeters)
    public int UpAxis;            // 0=X, 1=Y, 2=Z
    public int UpAxisSign;        // 1 or -1
    public int CoordAxis;         // front axis
    public int CoordAxisSign;
}

public class FbxMeshExtractor
{
    private readonly FbxConnectionResolver _resolver;

    public FbxMeshExtractor(FbxConnectionResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Extract global settings (UnitScaleFactor, axis info) from the FBX document.
    /// </summary>
    public static FbxGlobalSettings ExtractGlobalSettings(FbxDocument document)
    {
        var settings = new FbxGlobalSettings
        {
            UnitScaleFactor = 1.0f,
            UpAxis = 1,
            UpAxisSign = 1,
            CoordAxis = 0,
            CoordAxisSign = 1
        };

        var gsNode = document.FindNode("GlobalSettings");
        if (gsNode == null) return settings;

        var props70 = gsNode.FindChild("Properties70");
        if (props70 == null) return settings;

        foreach (var prop in props70.Children)
        {
            if (prop.Name != "P" || prop.Properties.Count < 5) continue;
            var name = prop.GetProperty<string>(0);
            if (name == null) continue;

            if (name == "UnitScaleFactor")
            {
                double? v = prop.GetPropertyValue<double>(4);
                if (v.HasValue) settings.UnitScaleFactor = (float)v.Value;
            }
            else if (name == "UpAxis")
            {
                var v = prop.GetPropertyValue<int>(4);
                if (v.HasValue) settings.UpAxis = v.Value;
            }
            else if (name == "UpAxisSign")
            {
                var v = prop.GetPropertyValue<int>(4);
                if (v.HasValue) settings.UpAxisSign = v.Value;
            }
            else if (name == "CoordAxis")
            {
                var v = prop.GetPropertyValue<int>(4);
                if (v.HasValue) settings.CoordAxis = v.Value;
            }
            else if (name == "CoordAxisSign")
            {
                var v = prop.GetPropertyValue<int>(4);
                if (v.HasValue) settings.CoordAxisSign = v.Value;
            }
        }

        Console.WriteLine($"[FbxMeshExtractor] GlobalSettings: UnitScaleFactor={settings.UnitScaleFactor}, UpAxis={settings.UpAxis}");
        return settings;
    }

    /// <summary>
    /// Find material name connected to a geometry node via the connection graph.
    /// </summary>
    public string FindMaterialForGeometry(long geometryId)
    {
        // Geometry -> Model (parent), Model has Material children
        var modelConns = _resolver.GetConnectionsFrom(geometryId);
        foreach (var mc in modelConns)
        {
            var modelNode = _resolver.GetObjectById(mc.DestinationId);
            if (modelNode == null || modelNode.Name != "Model") continue;

            // Find Material connected TO this Model
            var matConns = _resolver.GetConnectionsTo(mc.DestinationId);
            foreach (var matConn in matConns)
            {
                var matNode = _resolver.GetObjectById(matConn.SourceId);
                if (matNode != null && matNode.Name == "Material" && matNode.Properties.Count >= 2)
                {
                    var matName = matNode.GetProperty<string>(1);
                    if (!string.IsNullOrEmpty(matName))
                    {
                        // FBX material names often have "\x00\x01Material" suffix
                        int nullIdx = matName.IndexOf('\0');
                        if (nullIdx >= 0) matName = matName.Substring(0, nullIdx);
                        return matName;
                    }
                }
            }
        }
        return "";
    }

    public FbxMeshData ExtractMesh(long geometryId)
    {
        var result = new FbxMeshData
        {
            Vertices = Array.Empty<Vector3>(),
            Indices = Array.Empty<uint>(),
            Normals = Array.Empty<Vector3>(),
            UVs = Array.Empty<Vector2>(),
            MaterialName = ""
        };

        var geometryNode = _resolver.GetObjectById(geometryId);
        if (geometryNode == null)
            return result;

        var verticesNode = geometryNode.FindChild("Vertices");
        var indicesNode = geometryNode.FindChild("PolygonVertexIndex");

        if (verticesNode == null || indicesNode == null)
            return result;

        double[]? vertArray = verticesNode.GetProperty<double[]>(0);
        int[]? idxArray = indicesNode.GetProperty<int[]>(0);

        if (vertArray == null || idxArray == null)
            return result;

        // Extract vertices
        var vertices = new List<Vector3>();
        for (int i = 0; i < vertArray.Length; i += 3)
        {
            vertices.Add(new Vector3(
                (float)vertArray[i],
                (float)vertArray[i + 1],
                (float)vertArray[i + 2]
            ));
        }

        // Try to find and apply model transform
        var modelNode = FindModelForGeometry(geometryId);
        if (modelNode != null)
        {
            var transform = ExtractModelTransform(modelNode);
            if (transform != Matrix4x4.Identity)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = Vector3.Transform(vertices[i], transform);
                }
            }
        }

        // Triangulate and track polygon mapping for per-polygon-vertex attributes
        var (indices, polyVertexMap) = TriangulatePolygonsWithMapping(idxArray);
        var normals = ExtractNormalsWithMapping(geometryNode, vertices.Count, idxArray, polyVertexMap);
        var uvs = ExtractUVsWithMapping(geometryNode, vertices.Count, idxArray, polyVertexMap);

        result.Vertices = vertices.ToArray();
        result.Indices = indices;
        result.Normals = normals.Length > 0 ? normals : GenerateSmoothNormals(result.Vertices, result.Indices);
        result.UVs = uvs.Length > 0 ? uvs : new Vector2[vertices.Count];
        result.MaterialName = FindMaterialForGeometry(geometryId);

        return result;
    }

    private FbxNode? FindModelForGeometry(long geometryId)
    {
        // Find connections where this geometry is the source
        var connections = _resolver.GetConnectionsFrom(geometryId);
        foreach (var conn in connections)
        {
            var targetNode = _resolver.GetObjectById(conn.DestinationId);
            if (targetNode != null && targetNode.Name == "Model")
            {
                return targetNode;
            }
        }
        return null;
    }

    private Matrix4x4 ExtractModelTransform(FbxNode modelNode)
    {
        // Look for Properties70 node containing transform properties
        var props70 = modelNode.FindChild("Properties70");
        if (props70 == null)
            return Matrix4x4.Identity;

        Vector3 translation = Vector3.Zero;
        Vector3 rotation = Vector3.Zero;
        Vector3 scale = Vector3.One;

        // Extract Lcl Translation, Lcl Rotation, Lcl Scaling
        foreach (var prop in props70.Children)
        {
            if (prop.Name != "P" || prop.Properties.Count < 5)
                continue;

            var propNameProp = prop.GetProperty<string>(0);
            if (propNameProp == null)
                continue;
            
            string propName = propNameProp.ToString() ?? "";

            if (propName == "Lcl Translation")
            {
                double? x = prop.GetPropertyValue<double>(4);
                double? y = prop.GetPropertyValue<double>(5);
                double? z = prop.GetPropertyValue<double>(6);
                if (x.HasValue && y.HasValue && z.HasValue)
                    translation = new Vector3((float)x.Value, (float)y.Value, (float)z.Value);
            }
            else if (propName == "Lcl Rotation")
            {
                double? x = prop.GetPropertyValue<double>(4);
                double? y = prop.GetPropertyValue<double>(5);
                double? z = prop.GetPropertyValue<double>(6);
                if (x.HasValue && y.HasValue && z.HasValue)
                    rotation = new Vector3((float)x.Value, (float)y.Value, (float)z.Value);
            }
            else if (propName == "Lcl Scaling")
            {
                double? x = prop.GetPropertyValue<double>(4);
                double? y = prop.GetPropertyValue<double>(5);
                double? z = prop.GetPropertyValue<double>(6);
                if (x.HasValue && y.HasValue && z.HasValue)
                    scale = new Vector3((float)x.Value, (float)y.Value, (float)z.Value);
            }
        }

        // Build transform matrix: Scale * Rotation * Translation
        var scaleMatrix = Matrix4x4.CreateScale(scale);
        
        // FBX default rotation order is XYZ (Euler)
        float degToRad = (float)Math.PI / 180.0f;
        var rotX = Matrix4x4.CreateRotationX(rotation.X * degToRad);
        var rotY = Matrix4x4.CreateRotationY(rotation.Y * degToRad);
        var rotZ = Matrix4x4.CreateRotationZ(rotation.Z * degToRad);
        var rotMatrix = rotX * rotY * rotZ; // XYZ order (FBX default)
        
        var transMatrix = Matrix4x4.CreateTranslation(translation);

        return scaleMatrix * rotMatrix * transMatrix;
    }

    /// <summary>
    /// Triangulate polygons and build a mapping from each output triangle vertex
    /// back to the original polygon-vertex index in the PolygonVertexIndex array.
    /// This is critical for correctly indexing per-polygon-vertex normals and UVs.
    /// </summary>
    private (uint[] indices, int[] polyVertexMap) TriangulatePolygonsWithMapping(int[] polygonIndices)
    {
        var triangles = new List<uint>();
        var polyMap = new List<int>(); // maps each triangle vertex -> original polyVertexIndex position
        var polygon = new List<int>();
        var polyPositions = new List<int>(); // original positions in polygonIndices
        int pos = 0;

        foreach (int idx in polygonIndices)
        {
            int actualIdx = idx < 0 ? (-idx - 1) : idx;
            polygon.Add(actualIdx);
            polyPositions.Add(pos);
            pos++;

            if (idx < 0)
            {
                // Fan triangulation
                for (int i = 1; i < polygon.Count - 1; i++)
                {
                    triangles.Add((uint)polygon[0]);
                    polyMap.Add(polyPositions[0]);
                    triangles.Add((uint)polygon[i]);
                    polyMap.Add(polyPositions[i]);
                    triangles.Add((uint)polygon[i + 1]);
                    polyMap.Add(polyPositions[i + 1]);
                }
                polygon.Clear();
                polyPositions.Clear();
            }
        }

        return (triangles.ToArray(), polyMap.ToArray());
    }

    private Vector3[] ExtractNormalsWithMapping(FbxNode geometryNode, int vertexCount, int[] polygonIndices, int[] polyVertexMap)
    {
        var normalsNode = geometryNode.FindChild("LayerElementNormal");
        if (normalsNode == null)
            return Array.Empty<Vector3>();

        var normalsData = normalsNode.FindChild("Normals");
        if (normalsData == null)
            return Array.Empty<Vector3>();

        double[]? normArray = normalsData.GetProperty<double[]>(0);
        if (normArray == null)
            return Array.Empty<Vector3>();

        // Parse all raw normals
        var rawNormals = new Vector3[normArray.Length / 3];
        for (int i = 0; i < rawNormals.Length; i++)
            rawNormals[i] = new Vector3((float)normArray[i * 3], (float)normArray[i * 3 + 1], (float)normArray[i * 3 + 2]);

        // Determine mapping type
        string mappingType = "ByPolygonVertex";
        var mappingNode = normalsNode.FindChild("MappingInformationType");
        if (mappingNode?.Properties.Count > 0)
            mappingType = mappingNode.GetProperty<string>(0) ?? "ByPolygonVertex";

        string refType = "Direct";
        var refNode = normalsNode.FindChild("ReferenceInformationType");
        if (refNode?.Properties.Count > 0)
            refType = refNode.GetProperty<string>(0) ?? "Direct";

        // Build output normals per triangle vertex
        var result = new Vector3[polyVertexMap.Length];
        for (int t = 0; t < polyVertexMap.Length; t++)
        {
            int polyIdx = polyVertexMap[t];
            if (mappingType == "ByPolygonVertex")
            {
                if (polyIdx < rawNormals.Length)
                    result[t] = rawNormals[polyIdx];
                else
                    result[t] = Vector3.UnitY;
            }
            else // ByVertex
            {
                int vertIdx = polygonIndices[polyIdx];
                if (vertIdx < 0) vertIdx = -vertIdx - 1;
                if (vertIdx < rawNormals.Length)
                    result[t] = rawNormals[vertIdx];
                else
                    result[t] = Vector3.UnitY;
            }
        }

        return result;
    }

    private Vector2[] ExtractUVsWithMapping(FbxNode geometryNode, int vertexCount, int[] polygonIndices, int[] polyVertexMap)
    {
        var uvNode = geometryNode.FindChild("LayerElementUV");
        if (uvNode == null)
            return Array.Empty<Vector2>();

        var uvData = uvNode.FindChild("UV");
        if (uvData == null)
            return Array.Empty<Vector2>();

        double[]? uvArray = uvData.GetProperty<double[]>(0);
        if (uvArray == null)
            return Array.Empty<Vector2>();

        var rawUVs = new Vector2[uvArray.Length / 2];
        for (int i = 0; i < rawUVs.Length; i++)
            rawUVs[i] = new Vector2((float)uvArray[i * 2], (float)uvArray[i * 2 + 1]);

        // Check for UVIndex indirection table
        int[]? uvIndexArray = null;
        var uvIndexNode = uvNode.FindChild("UVIndex");
        if (uvIndexNode != null)
            uvIndexArray = uvIndexNode.GetProperty<int[]>(0);

        string mappingType = "ByPolygonVertex";
        var mappingNode = uvNode.FindChild("MappingInformationType");
        if (mappingNode?.Properties.Count > 0)
            mappingType = mappingNode.GetProperty<string>(0) ?? "ByPolygonVertex";

        var result = new Vector2[polyVertexMap.Length];
        for (int t = 0; t < polyVertexMap.Length; t++)
        {
            int polyIdx = polyVertexMap[t];

            if (mappingType == "ByPolygonVertex")
            {
                if (uvIndexArray != null && polyIdx < uvIndexArray.Length)
                {
                    // Use UVIndex indirection
                    int uvIdx = uvIndexArray[polyIdx];
                    if (uvIdx >= 0 && uvIdx < rawUVs.Length)
                        result[t] = rawUVs[uvIdx];
                }
                else if (polyIdx < rawUVs.Length)
                {
                    result[t] = rawUVs[polyIdx];
                }
            }
            else // ByVertex
            {
                int vertIdx = polygonIndices[polyIdx];
                if (vertIdx < 0) vertIdx = -vertIdx - 1;
                if (vertIdx < rawUVs.Length)
                    result[t] = rawUVs[vertIdx];
            }
        }

        return result;
    }

    private Vector3[] GenerateSmoothNormals(Vector3[] vertices, uint[] indices)
    {
        var normals = new Vector3[vertices.Length];
        Array.Fill(normals, Vector3.Zero);

        for (int i = 0; i < indices.Length; i += 3)
        {
            if (i + 2 >= indices.Length) break;
            uint i0 = indices[i];
            uint i1 = indices[i + 1];
            uint i2 = indices[i + 2];
            if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length) continue;

            Vector3 edge1 = vertices[i1] - vertices[i0];
            Vector3 edge2 = vertices[i2] - vertices[i0];
            Vector3 faceNormal = Vector3.Cross(edge1, edge2);

            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            float len = normals[i].Length();
            normals[i] = len > 0.0001f ? normals[i] / len : Vector3.UnitY;
        }

        return normals;
    }
}
