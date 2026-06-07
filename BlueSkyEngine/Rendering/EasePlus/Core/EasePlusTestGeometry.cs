using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Rendering.EasePlus;

/// <summary>
/// Test geometry for EasePlus renderer debugging.
/// Provides simple procedural meshes (cube, sphere, plane) to verify the pipeline works.
/// </summary>
public static class EasePlusTestGeometry
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;
        
        public Vertex(Vector3 pos, Vector3 nrm, Vector2 uv)
        {
            Position = pos;
            Normal = nrm;
            UV = uv;
        }
    }
    
    public struct Mesh
    {
        public Vertex[] Vertices;
        public uint[] Indices;
        public IRHIBuffer? VertexBuffer;
        public IRHIBuffer? IndexBuffer;
    }
    
    /// <summary>
    /// Create a unit cube (2×2×2) centered at origin
    /// </summary>
    public static Mesh CreateCube(IRHIDevice device)
    {
        var vertices = new Vertex[]
        {
            // Front face (+Z)
            new(new(-1, -1,  1), new(0, 0, 1), new(0, 0)),
            new(new( 1, -1,  1), new(0, 0, 1), new(1, 0)),
            new(new( 1,  1,  1), new(0, 0, 1), new(1, 1)),
            new(new(-1,  1,  1), new(0, 0, 1), new(0, 1)),
            
            // Back face (-Z)
            new(new( 1, -1, -1), new(0, 0, -1), new(0, 0)),
            new(new(-1, -1, -1), new(0, 0, -1), new(1, 0)),
            new(new(-1,  1, -1), new(0, 0, -1), new(1, 1)),
            new(new( 1,  1, -1), new(0, 0, -1), new(0, 1)),
            
            // Top face (+Y)
            new(new(-1,  1,  1), new(0, 1, 0), new(0, 0)),
            new(new( 1,  1,  1), new(0, 1, 0), new(1, 0)),
            new(new( 1,  1, -1), new(0, 1, 0), new(1, 1)),
            new(new(-1,  1, -1), new(0, 1, 0), new(0, 1)),
            
            // Bottom face (-Y)
            new(new(-1, -1, -1), new(0, -1, 0), new(0, 0)),
            new(new( 1, -1, -1), new(0, -1, 0), new(1, 0)),
            new(new( 1, -1,  1), new(0, -1, 0), new(1, 1)),
            new(new(-1, -1,  1), new(0, -1, 0), new(0, 1)),
            
            // Right face (+X)
            new(new( 1, -1,  1), new(1, 0, 0), new(0, 0)),
            new(new( 1, -1, -1), new(1, 0, 0), new(1, 0)),
            new(new( 1,  1, -1), new(1, 0, 0), new(1, 1)),
            new(new( 1,  1,  1), new(1, 0, 0), new(0, 1)),
            
            // Left face (-X)
            new(new(-1, -1, -1), new(-1, 0, 0), new(0, 0)),
            new(new(-1, -1,  1), new(-1, 0, 0), new(1, 0)),
            new(new(-1,  1,  1), new(-1, 0, 0), new(1, 1)),
            new(new(-1,  1, -1), new(-1, 0, 0), new(0, 1)),
        };
        
        var indices = new uint[]
        {
            // Front
            0, 1, 2,  0, 2, 3,
            // Back
            4, 5, 6,  4, 6, 7,
            // Top
            8, 9, 10,  8, 10, 11,
            // Bottom
            12, 13, 14,  12, 14, 15,
            // Right
            16, 17, 18,  16, 18, 19,
            // Left
            20, 21, 22,  20, 22, 23
        };
        
        var mesh = new Mesh
        {
            Vertices = vertices,
            Indices = indices
        };
        
        // Upload to GPU
        mesh.VertexBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(vertices.Length * Marshal.SizeOf<Vertex>()),
            Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "TestCube.VB"
        });
        
        mesh.IndexBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(indices.Length * sizeof(uint)),
            Usage = BufferUsage.Index,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "TestCube.IB"
        });
        
        device.UpdateBuffer(mesh.VertexBuffer, MemoryMarshal.AsBytes<Vertex>(vertices));
        device.UpdateBuffer(mesh.IndexBuffer, MemoryMarshal.AsBytes<uint>(indices));
        
        Console.WriteLine($"[EasePlus] Created test cube: {vertices.Length} verts, {indices.Length} indices");
        
        return mesh;
    }
    
    /// <summary>
    /// Create a UV sphere
    /// </summary>
    public static Mesh CreateSphere(IRHIDevice device, int segments = 16, int rings = 8)
    {
        var vertices = new System.Collections.Generic.List<Vertex>();
        var indices = new System.Collections.Generic.List<uint>();
        
        // Generate vertices
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings;
            float y = MathF.Cos(phi);
            float ringRadius = MathF.Sin(phi);
            
            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = 2.0f * MathF.PI * seg / segments;
                float x = ringRadius * MathF.Cos(theta);
                float z = ringRadius * MathF.Sin(theta);
                
                var pos = new Vector3(x, y, z);
                var normal = Vector3.Normalize(pos);
                var uv = new Vector2((float)seg / segments, (float)ring / rings);
                
                vertices.Add(new Vertex(pos, normal, uv));
            }
        }
        
        // Generate indices
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                uint current = (uint)(ring * (segments + 1) + seg);
                uint next = current + (uint)(segments + 1);
                
                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);
                
                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }
        
        var mesh = new Mesh
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray()
        };
        
        // Upload to GPU
        mesh.VertexBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(mesh.Vertices.Length * Marshal.SizeOf<Vertex>()),
            Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "TestSphere.VB"
        });
        
        mesh.IndexBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(mesh.Indices.Length * sizeof(uint)),
            Usage = BufferUsage.Index,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "TestSphere.IB"
        });
        
        device.UpdateBuffer(mesh.VertexBuffer, MemoryMarshal.AsBytes<Vertex>(mesh.Vertices));
        device.UpdateBuffer(mesh.IndexBuffer, MemoryMarshal.AsBytes<uint>(mesh.Indices));
        
        Console.WriteLine($"[EasePlus] Created test sphere: {mesh.Vertices.Length} verts, {mesh.Indices.Length} indices");
        
        return mesh;
    }
    
    /// <summary>
    /// Create a ground plane
    /// </summary>
    public static Mesh CreatePlane(IRHIDevice device, float size = 10.0f, int subdivisions = 10)
    {
        var vertices = new System.Collections.Generic.List<Vertex>();
        var indices = new System.Collections.Generic.List<uint>();
        
        float step = size / subdivisions;
        float uvStep = 1.0f / subdivisions;
        
        // Generate vertices
        for (int z = 0; z <= subdivisions; z++)
        {
            for (int x = 0; x <= subdivisions; x++)
            {
                float px = -size / 2 + x * step;
                float pz = -size / 2 + z * step;
                
                vertices.Add(new Vertex(
                    new Vector3(px, 0, pz),
                    new Vector3(0, 1, 0),
                    new Vector2(x * uvStep, z * uvStep)
                ));
            }
        }
        
        // Generate indices
        for (int z = 0; z < subdivisions; z++)
        {
            for (int x = 0; x < subdivisions; x++)
            {
                uint topLeft = (uint)(z * (subdivisions + 1) + x);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((z + 1) * (subdivisions + 1) + x);
                uint bottomRight = bottomLeft + 1;
                
                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);
                
                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        }
        
        var mesh = new Mesh
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray()
        };
        
        // Upload to GPU
        mesh.VertexBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(mesh.Vertices.Length * Marshal.SizeOf<Vertex>()),
            Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "TestPlane.VB"
        });
        
        mesh.IndexBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(mesh.Indices.Length * sizeof(uint)),
            Usage = BufferUsage.Index,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "TestPlane.IB"
        });
        
        device.UpdateBuffer(mesh.VertexBuffer, MemoryMarshal.AsBytes<Vertex>(mesh.Vertices));
        device.UpdateBuffer(mesh.IndexBuffer, MemoryMarshal.AsBytes<uint>(mesh.Indices));
        
        Console.WriteLine($"[EasePlus] Created test plane: {mesh.Vertices.Length} verts, {mesh.Indices.Length} indices");
        
        return mesh;
    }
}
