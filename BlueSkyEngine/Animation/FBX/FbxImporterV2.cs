using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Animation.FBX;

public struct EngineMesh
{
    public Vector3[] Vertices;
    public uint[] Indices;
    public Vector3[] Normals;
    public Vector2[] UVs;
    public string Name;
    public string MaterialName;
    public FbxGlobalSettings GlobalSettings;
}

public class FbxImporterV2
{
    public EngineMesh? Import(string filePath)
    {
        var parser = new FbxParser();
        if (!parser.Parse(filePath))
        {
            Console.WriteLine("[FbxImporterV2] Parse failed");
            return null;
        }

        if (parser.RootNodes.Count == 0)
        {
            Console.WriteLine("[FbxImporterV2] No root nodes");
            return null;
        }

        // Build resolver from the full document (searches all root nodes)
        var resolver = new FbxConnectionResolver();
        resolver.BuildObjectMap(parser.Document);
        resolver.BuildConnectionMap(parser.Document);

        // Extract global settings (UnitScaleFactor, axis info)
        var globalSettings = FbxMeshExtractor.ExtractGlobalSettings(parser.Document);

        // Find all geometry IDs and extract the first valid mesh
        var geometryIds = FindAllGeometryIds(parser.Document);
        if (geometryIds.Count == 0)
        {
            Console.WriteLine("[FbxImporterV2] No geometry found");
            return null;
        }

        var extractor = new FbxMeshExtractor(resolver);

        // Try each geometry until we get a valid mesh
        foreach (var geometryId in geometryIds)
        {
            var meshData = extractor.ExtractMesh(geometryId);
            if (meshData.Vertices.Length > 0)
            {
                return new EngineMesh
                {
                    Vertices = meshData.Vertices,
                    Indices = meshData.Indices,
                    Normals = meshData.Normals,
                    UVs = meshData.UVs,
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    MaterialName = meshData.MaterialName,
                    GlobalSettings = globalSettings
                };
            }
        }

        Console.WriteLine("[FbxImporterV2] No vertices extracted from any geometry");
        return null;
    }

    /// <summary>
    /// Import all meshes from an FBX file (multi-mesh support).
    /// </summary>
    public List<EngineMesh> ImportAll(string filePath)
    {
        var results = new List<EngineMesh>();

        var parser = new FbxParser();
        if (!parser.Parse(filePath))
        {
            Console.WriteLine("[FbxImporterV2] Parse failed");
            return results;
        }

        var resolver = new FbxConnectionResolver();
        resolver.BuildObjectMap(parser.Document);
        resolver.BuildConnectionMap(parser.Document);

        // Extract global settings
        var globalSettings = FbxMeshExtractor.ExtractGlobalSettings(parser.Document);

        var geometryIds = FindAllGeometryIds(parser.Document);
        var extractor = new FbxMeshExtractor(resolver);
        string baseName = System.IO.Path.GetFileNameWithoutExtension(filePath);

        int meshIndex = 0;
        foreach (var geometryId in geometryIds)
        {
            var meshData = extractor.ExtractMesh(geometryId);
            if (meshData.Vertices.Length > 0)
            {
                results.Add(new EngineMesh
                {
                    Vertices = meshData.Vertices,
                    Indices = meshData.Indices,
                    Normals = meshData.Normals,
                    UVs = meshData.UVs,
                    Name = geometryIds.Count > 1 ? $"{baseName}_{meshIndex}" : baseName,
                    MaterialName = meshData.MaterialName,
                    GlobalSettings = globalSettings
                });
                meshIndex++;
            }
        }

        return results;
    }

    private List<long> FindAllGeometryIds(FbxDocument document)
    {
        var ids = new List<long>();
        var geometryNodes = document.FindAllNodes("Geometry");

        foreach (var geomNode in geometryNodes)
        {
            if (geomNode.Properties.Count > 0)
            {
                long? id = geomNode.GetPropertyValue<long>(0);
                if (id.HasValue && id.Value != 0)
                    ids.Add(id.Value);
            }
        }

        return ids;
    }
}
