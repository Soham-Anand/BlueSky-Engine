using System;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.BSR;

/// <summary>
/// Lifecycle and viewport operations used by the editor's scene renderer.
/// GPU resource creation belongs to IRHIDevice, not this scene-level interface.
/// </summary>
public interface IRenderer : IDisposable
{
    void Initialize();
    void BeginFrame(float r, float g, float b, float a = 1.0f);
    void EndFrame();
    void SetViewport(int x, int y, int width, int height);
    void RenderScene(World world, CameraComponent camera, TransformComponent cameraTransform);
}
