using System;
using BlueSky.Core.Math;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Rendering
{
    /// <summary>
    /// A stub implementation of IRenderer to allow the Engine to compile
    /// while transitioning fully to the native NotBSRenderer RHI.
    /// This replaces the deleted BgfxRenderer.
    /// </summary>
    public class StubRenderer : IRenderer
    {
        public void Initialize() { }
        public void BeginFrame(float r, float g, float b, float a = 1.0f) { }
        public void EndFrame() { }
        
        public void Clear(float r, float g, float b, float a = 1.0f) { }
        public void ClearDepth() { }
        public void SetViewport(int x, int y, int width, int height) { }
        public void SetScissor(int x, int y, int width, int height) { }
        
        public void RenderScene(World world, CameraComponent camera, TransformComponent cameraTransform) { }
        public void RenderSceneWithShadows(World world, CameraComponent camera, TransformComponent cameraTransform, Vector3 lightPosition, Vector3 lightDirection) { }
        public void DrawLine(Vector3 start, Vector3 end, Vector3 color, Matrix4x4 view, Matrix4x4 proj) { }
        public void DrawGrid(Matrix4x4 view, Matrix4x4 proj, int size, float spacing) { }
        public void RenderSky(float time, Vector3 sunDir, Quaternion camRot, float aspect, float tanFov) { }
        
        public int CreateVertexBuffer(float[] vertices) => 1;
        public int CreateIndexBuffer(uint[] indices) => 1;
        public int CreateShader(string vertexSource, string fragmentSource) => 1;
        public int CreateTexture(int width, int height, byte[] data, bool srgb = true) => 1;
        public int CreateFramebuffer(int width, int height) => 1;
        public int GetFramebufferTexture(int fboId) => 1;
        
        public void SetShader(int shaderId) { }
        public void SetTexture(int stage, int textureId) { }
        public void SetRenderTarget(int fboId) { }
        public void SetUniform(string name, Matrix4x4 matrix) { }
        public void SetUniform(string name, Vector3 vector) { }
        public void SetUniform(string name, float value) { }
        
        public void DeleteResource(ResourceType type, int id) { }
        
        public int CreateMesh(float[] vertices, uint[] indices) => 1;
        public void UpdateMesh(int meshId, float[] vertices, uint[] indices) { }
        public void DeleteMesh(int meshId) { }

        public void Dispose() { }
    }
}
