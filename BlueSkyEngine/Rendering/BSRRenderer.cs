using System;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Platform;
using BlueSky.Rendering.RHI;

namespace BlueSky.Rendering;

/// <summary>
/// BSR's editor viewport adapter. It owns the offscreen color and depth targets
/// while ViewportRenderer owns scene passes, materials, lighting, and geometry.
/// The editor owns the RHI device and the attached ViewportRenderer.
/// </summary>
public sealed class BSRRenderer : IRenderer
{
    private readonly IRHIDevice _device;
    private readonly IWindow _window;
    private BlueSky.Editor.ViewportRenderer? _viewportRenderer;
    private IRHITexture? _colorTarget;
    private IRHITexture? _depthTarget;
    private Vector4 _clearColor = new(0.02f, 0.025f, 0.03f, 1.0f);
    private uint _requestedWidth;
    private uint _requestedHeight;
    private uint _targetWidth;
    private uint _targetHeight;
    private bool _initialized;
    private bool _frameActive;
    private bool _disposed;

    public IRHITexture? FinalTarget => _colorTarget;

    public BSRRenderer(IWindow window, IRHIDevice device)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>Attach the scene renderer before Initialize is called.</summary>
    public void SetViewportRenderer(BlueSky.Editor.ViewportRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            throw new InvalidOperationException("The viewport renderer must be attached before BSRRenderer.Initialize().");
        _viewportRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;
        if (_viewportRenderer == null)
            throw new InvalidOperationException("BSRRenderer requires a ViewportRenderer to render scene content.");

        EnsureRenderTargets();
        _initialized = true;
    }

    public void BeginFrame(float r, float g, float b, float a = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) Initialize();
        _clearColor = new Vector4(r, g, b, a);
        EnsureRenderTargets();
        _frameActive = true;
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requestedWidth = (uint)Math.Max(1, width);
        _requestedHeight = (uint)Math.Max(1, height);
    }

    public void RenderScene(World world, CameraComponent camera, TransformComponent cameraTransform)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) Initialize();
        if (!_frameActive) BeginFrame(_clearColor.X, _clearColor.Y, _clearColor.Z, _clearColor.W);
        if (_colorTarget == null || _depthTarget == null || _viewportRenderer == null)
            throw new InvalidOperationException("BSR render targets or scene renderer are unavailable.");

        using var cmd = _device.CreateCommandBuffer();
        var view = ToNumerics(BlueSky.Core.Math.Matrix4x4.CreateLookAt(
            cameraTransform.Position,
            cameraTransform.Position + cameraTransform.Forward,
            cameraTransform.Up));
        var projection = ToNumerics(camera.GetProjectionMatrix());
        var sunDirection = Vector3.Normalize(new Vector3(0.5f, 0.8f, 0.4f));

        _viewportRenderer.PreRender(cmd, sunDirection);
        cmd.BeginRenderPass(
            new[] { _colorTarget },
            _depthTarget,
            ClearValue.FromColor(_clearColor.X, _clearColor.Y, _clearColor.Z, _clearColor.W));
        cmd.SetViewport(new BlueSky.Rendering.RHI.Viewport
        {
            X = 0,
            Y = 0,
            Width = _targetWidth,
            Height = _targetHeight,
            MinDepth = 0,
            MaxDepth = 1
        });
        cmd.SetScissor(new Scissor { X = 0, Y = 0, Width = _targetWidth, Height = _targetHeight });

        _viewportRenderer.Render(
            cmd,
            view,
            projection,
            new Vector3(cameraTransform.Position.X, cameraTransform.Position.Y, cameraTransform.Position.Z),
            0,
            0,
            checked((int)_targetWidth),
            checked((int)_targetHeight),
            0.016f);

        cmd.EndRenderPass();
        _device.Submit(cmd);
    }

    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _frameActive = false;
    }

    private void EnsureRenderTargets()
    {
        uint width = _requestedWidth > 0 ? _requestedWidth : (uint)Math.Max(1, _window.FramebufferSize.X);
        uint height = _requestedHeight > 0 ? _requestedHeight : (uint)Math.Max(1, _window.FramebufferSize.Y);
        if (_colorTarget != null && _depthTarget != null && width == _targetWidth && height == _targetHeight)
            return;

        _colorTarget?.Dispose();
        _depthTarget?.Dispose();
        _colorTarget = _device.CreateTexture(new TextureDesc
        {
            Width = width,
            Height = height,
            Depth = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "BSR.ViewportColor"
        });
        _depthTarget = _device.CreateTexture(new TextureDesc
        {
            Width = width,
            Height = height,
            Depth = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "BSR.ViewportDepth"
        });
        _targetWidth = width;
        _targetHeight = height;
    }

    private static Matrix4x4 ToNumerics(BlueSky.Core.Math.Matrix4x4 matrix) => new(
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44);

    public void Dispose()
    {
        if (_disposed) return;
        _colorTarget?.Dispose();
        _depthTarget?.Dispose();
        _colorTarget = null;
        _depthTarget = null;
        _disposed = true;
    }
}
