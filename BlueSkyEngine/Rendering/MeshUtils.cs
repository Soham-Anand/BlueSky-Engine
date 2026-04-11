using System;
using System.Collections.Generic;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Rendering
{
    public static class MeshUtils
    {
        public static MeshComponent CreateCube(IRenderer renderer)
        {
            float[] vertices = {
                // Position           // Normal           // TexCoords // Tangent (3) // Bitangent (3)
                -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  1.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  1.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,

                -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  0.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  0.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,

                -0.5f,  0.5f,  0.5f, -1.0f,  0.0f,  0.0f,  1.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f,  0.5f, -0.5f, -1.0f,  0.0f,  0.0f,  1.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f, -0.5f,  0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,

                 0.5f,  0.5f,  0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f,  0.5f, -0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f,  0.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f, -0.5f,  0.5f,  1.0f,  0.0f,  0.0f,  0.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,

                -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,  1.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,  1.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,

                -0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,  0.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 1.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                 0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f,
                -0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,  0.0f, 0.0f,  0f, 0f, 0f,  0f, 0f, 0f
            };

            uint[] indices = {
                0, 1, 2, 2, 3, 0,
                4, 5, 6, 6, 7, 4,
                8, 9, 10, 10, 11, 8,
                12, 13, 14, 14, 15, 12,
                16, 17, 18, 18, 19, 16,
                20, 21, 22, 22, 23, 20
            };

            int id = renderer.CreateMesh(vertices, indices);
            return new MeshComponent { 
                VertexBufferId = id, 
                IndexBufferId = id, 
                VertexCount = 24, 
                IndexCount = 36 
            };
        }
    }
}
