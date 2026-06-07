using System;
using System.Numerics;
using BlueSky.Animation.GLTF;
using BlueSky.Rendering;

namespace BlueSky.Tests;

/// <summary>
/// Test GLB import to verify mesh geometry and material colors are loaded correctly.
/// </summary>
public static class TestGLBImport
{
    public static void Run(string glbPath)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              GLB Import Diagnostic Test                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"File: {glbPath}");
        Console.WriteLine();

        try
        {
            // Import GLB
            var importer = GltfImporter.FromFile(glbPath);
            var root = importer.Root;

            Console.WriteLine($"✓ GLB loaded successfully");
            Console.WriteLine($"  Meshes: {root.Meshes?.Length ?? 0}");
            Console.WriteLine($"  Materials: {root.Materials?.Length ?? 0}");
            Console.WriteLine($"  Textures: {root.Textures?.Length ?? 0}");
            Console.WriteLine();

            // Extract and analyze meshes
            if (root.Meshes != null && root.Meshes.Length > 0)
            {
                Console.WriteLine("─── MESH ANALYSIS ───────────────────────────────────────────");
                for (int i = 0; i < Math.Min(3, root.Meshes.Length); i++)
                {
                    var meshData = importer.ExtractMesh(i);
                    Console.WriteLine($"Mesh {i}: {meshData.Name}");
                    Console.WriteLine($"  Primitives: {meshData.Primitives.Count}");
                    
                    foreach (var prim in meshData.Primitives)
                    {
                        Console.WriteLine($"    Vertices: {prim.Positions?.Length ?? 0}");
                        Console.WriteLine($"    Indices: {prim.Indices?.Length ?? 0}");
                        Console.WriteLine($"    Triangles: {(prim.Indices?.Length ?? 0) / 3}");
                        Console.WriteLine($"    Has Normals: {prim.Normals != null}");
                        Console.WriteLine($"    Has UVs: {prim.TexCoords0 != null}");
                        Console.WriteLine($"    Has Colors: {prim.Colors != null}");
                        Console.WriteLine($"    Material Index: {prim.MaterialIndex}");
                        
                        // Check winding order (first triangle)
                        if (prim.Indices != null && prim.Indices.Length >= 3 && prim.Positions != null)
                        {
                            var v0 = prim.Positions[prim.Indices[0]];
                            var v1 = prim.Positions[prim.Indices[1]];
                            var v2 = prim.Positions[prim.Indices[2]];
                            var edge1 = v1 - v0;
                            var edge2 = v2 - v0;
                            var normal = Vector3.Cross(edge1, edge2);
                            Console.WriteLine($"    First Triangle Normal: ({normal.X:F3}, {normal.Y:F3}, {normal.Z:F3})");
                        }
                    }
                    Console.WriteLine();
                }
            }

            // Extract and analyze materials
            if (root.Materials != null && root.Materials.Length > 0)
            {
                Console.WriteLine("─── MATERIAL ANALYSIS ───────────────────────────────────────");
                for (int i = 0; i < Math.Min(5, root.Materials.Length); i++)
                {
                    var matData = GltfToEngineBridge.ExtractMaterial(importer, i);
                    Console.WriteLine($"Material {i}: {matData.Name}");
                    Console.WriteLine($"  BaseColor: ({matData.BaseColor.X:F3}, {matData.BaseColor.Y:F3}, {matData.BaseColor.Z:F3}, {matData.BaseColor.W:F3})");
                    Console.WriteLine($"  Metallic: {matData.MetallicFactor:F3}");
                    Console.WriteLine($"  Roughness: {matData.RoughnessFactor:F3}");
                    Console.WriteLine($"  AlphaMode: {matData.AlphaMode}");
                    Console.WriteLine($"  DoubleSided: {matData.DoubleSided}");
                    Console.WriteLine($"  BaseColorTexture: {(matData.BaseColorTextureIndex >= 0 ? $"Texture {matData.BaseColorTextureIndex}" : "None")}");
                    Console.WriteLine();
                }
            }

            // Test coordinate conversion
            Console.WriteLine("─── COORDINATE CONVERSION TEST ──────────────────────────────");
            Console.WriteLine("Testing right-handed (GLTF) → left-handed (Engine) conversion:");
            
            var testPos = new Vector3(1, 2, 3);
            var convertedPos = new Vector3(-testPos.X, testPos.Y, testPos.Z);
            Console.WriteLine($"  Original: ({testPos.X}, {testPos.Y}, {testPos.Z})");
            Console.WriteLine($"  Converted: ({convertedPos.X}, {convertedPos.Y}, {convertedPos.Z})");
            Console.WriteLine($"  X negated: {testPos.X != convertedPos.X}");
            Console.WriteLine();

            // Test winding order swap
            uint[] testIndices = { 0, 1, 2, 3, 4, 5 };
            Console.WriteLine("  Original indices: [0, 1, 2] [3, 4, 5]");
            for (int t = 0; t + 2 < testIndices.Length; t += 3)
            {
                (testIndices[t + 1], testIndices[t + 2]) = (testIndices[t + 2], testIndices[t + 1]);
            }
            Console.WriteLine($"  Swapped indices: [{testIndices[0]}, {testIndices[1]}, {testIndices[2]}] [{testIndices[3]}, {testIndices[4]}, {testIndices[5]}]");
            Console.WriteLine($"  Winding reversed: {testIndices[1] == 2 && testIndices[2] == 1}");
            Console.WriteLine();

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    TEST COMPLETE                             ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ ERROR: {ex.Message}");
            Console.WriteLine($"  Stack: {ex.StackTrace}");
        }
    }
}
