using System;
using BlueSky.Core.Math;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Rendering
{
    public interface IRenderer : IDisposable
    {
        void Initialize();
        void BeginFrame(float r, float g, float b, float a = 1.0f);
        void EndFrame();
        
        void Clear(float r, float g, float b, float a = 1.0f);
        void ClearDepth();
        void SetViewport(int x, int y, int width, int height);
        void SetScissor(int x, int y, int width, int height);
        
        void RenderScene(World world, CameraComponent camera, TransformComponent cameraTransform);
        void RenderSceneWithShadows(World world, CameraComponent camera, TransformComponent cameraTransform, 
            Vector3 lightPosition, Vector3 lightDirection);
        void DrawLine(Vector3 start, Vector3 end, Vector3 color, Matrix4x4 view, Matrix4x4 proj);
        void DrawGrid(Matrix4x4 view, Matrix4x4 proj, int size, float spacing);
        void RenderSky(float time, Vector3 sunDir, Quaternion camRot, float aspect, float tanFov);
        
        // Resource Management
        int CreateVertexBuffer(float[] vertices);
        int CreateIndexBuffer(uint[] indices);
        int CreateShader(string vertexSource, string fragmentSource);
        int CreateTexture(int width, int height, byte[] data, bool srgb = true);
        int CreateFramebuffer(int width, int height);
        int GetFramebufferTexture(int fboId);
        
        // Pipeline State
        void SetShader(int shaderId);
        void SetTexture(int stage, int textureId);
        void SetRenderTarget(int fboId);
        void SetUniform(string name, Matrix4x4 matrix);
        void SetUniform(string name, Vector3 vector);
        void SetUniform(string name, float value);
        
        void DeleteResource(ResourceType type, int id);
        
        // Legacy/Utility
        int CreateMesh(float[] vertices, uint[] indices);
        void UpdateMesh(int meshId, float[] vertices, uint[] indices);
        void DeleteMesh(int meshId);
    }

    public enum ResourceType
    {
        VertexBuffer,
        IndexBuffer,
        Shader,
        Texture
    }
}
