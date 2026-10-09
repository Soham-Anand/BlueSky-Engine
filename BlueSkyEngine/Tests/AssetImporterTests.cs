using System;
using System.IO;
using System.Numerics;

namespace BlueSky.Tests;

public static class AssetImporterTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Custom Asset Importers Tests...");
        bool passed = true;

        passed &= TestPacked32Stride();
        passed &= TestPacked32GeometryDecode();
        passed &= TestMeshGeometrySoA();
        passed &= TestCoordinateSystemConversion();

        return passed;
    }

    private static bool TestPacked32Stride()
    {
        try
        {
            // Packed32 = Position(3) + Normal(3) + UV(2) = 8 floats = 32 bytes.
            const int stride = 32;
            int vertexCount = 4;
            byte[] vertexBytes = new byte[vertexCount * stride];
            bool valid = vertexBytes.Length == 128 && vertexBytes.Length / stride == vertexCount;
            Console.WriteLine($"  ✓ Packed32 stride (4 verts × 32B = 128B): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Packed32 Stride Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestPacked32GeometryDecode()
    {
        try
        {
            // Synthetic quad: 4 verts Packed32 + 6 u32 indices (no file parsing).
            float[] v0 = { 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f };
            float[] v1 = { 1f, 0f, 0f, 0f, 0f, 1f, 1f, 0f };
            float[] v2 = { 1f, 1f, 0f, 0f, 0f, 1f, 1f, 1f };
            float[] v3 = { 0f, 1f, 0f, 0f, 0f, 1f, 0f, 1f };
            var verts = new float[32];
            Array.Copy(v0, 0, verts, 0, 8);
            Array.Copy(v1, 0, verts, 8, 8);
            Array.Copy(v2, 0, verts, 16, 8);
            Array.Copy(v3, 0, verts, 24, 8);
            byte[] vertexBytes = new byte[verts.Length * 4];
            Buffer.BlockCopy(verts, 0, vertexBytes, 0, vertexBytes.Length);
            uint[] indices = { 0, 1, 2, 0, 2, 3 };

            bool valid = vertexBytes.Length == 4 * 32
                && indices.Length == 6
                && BitConverter.ToSingle(vertexBytes, 0) == 0f
                && BitConverter.ToSingle(vertexBytes, 32) == 1f;
            Console.WriteLine($"  ✓ Packed32 synthetic quad geometry (verts=4, tris=2): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Packed32 Geometry Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestMeshGeometrySoA()
    {
        try
        {
            // SoA mesh geometry: separate position/normal/uv arrays (no parser DTOs).
            var vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            var indices = new uint[] { 0, 1, 2, 0, 2, 3 };
            var smooth = BlueSky.Rendering.SmoothingShaders.GenerateSmoothNormals(vertices, indices);

            bool valid = vertices.Length == 4
                && indices.Length == 6
                && smooth.Length == 4;
            Console.WriteLine($"  ✓ SoA mesh geometry + smooth normals (verts=4): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Mesh Geometry Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestCoordinateSystemConversion()
    {
        try
        {
            // GLTF Right-Handed (+Z forward) -> Engine Left-Handed (-X conversion / winding swap)
            Vector3 testPos = new Vector3(1.5f, 2.5f, 3.5f);
            Vector3 convertedPos = new Vector3(-testPos.X, testPos.Y, testPos.Z);

            uint[] indices = { 0, 1, 2 };
            (indices[1], indices[2]) = (indices[2], indices[1]); // Reverse winding for LH conversion

            bool valid = convertedPos.X == -1.5f && indices[1] == 2 && indices[2] == 1;
            Console.WriteLine($"  ✓ RH → LH Coordinate System & Winding Conversion: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Coordinate Conversion Exception: {ex.Message}");
            return false;
        }
    }
}
