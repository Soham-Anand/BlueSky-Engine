using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace BlueSky.RHI.Test;

public struct Vertex
{
    public Vector3 Position;
    public uint Color; // ARGB unorm
}

public static class ObjParser
{
    public static (Vertex[] vertices, uint[] indices) Parse(string filePath, uint color)
    {
        var positions = new List<Vector3>();
        var indices = new List<uint>();
        
        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
        var lines = File.ReadAllLines(fullPath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;
                
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "v")
            {
                float x = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                positions.Add(new Vector3(x, y, z));
            }
            else if (parts[0] == "f")
            {
                // Basic triangulated faces
                for (int i = 1; i <= 3; i++)
                {
                    var vertexDef = parts[i].Split('/');
                    int vIndex = int.Parse(vertexDef[0]) - 1; // OBJ is 1-indexed
                    indices.Add((uint)vIndex);
                }
            }
        }
        
        // Normalize and center positions
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        foreach (var p in positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        
        Vector3 center = (min + max) * 0.5f;
        float maxExtent = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        float scale = 1.0f / maxExtent;
        
        var vertices = new Vertex[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            // Center, scale, and move forward into Z range [0, 1] for DX9 visibility
            var np = (p - center) * scale;
            np.Z += 0.5f; // Push forward so it's not clipped by near/far planes
            
            vertices[i] = new Vertex
            {
                Position = np,
                Color = color
            };
        }
        
        return (vertices, indices.ToArray());
    }
}
