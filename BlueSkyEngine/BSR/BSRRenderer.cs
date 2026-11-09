using System;
using System.Numerics;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.Aegis;
using BlueSky.BSR.RHI;

namespace BlueSky.BSR;

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
        var view = ToNumerics(BlueSky.Ramanujan.Matrix4x4.CreateLookAt(
            cameraTransform.Position,
            cameraTransform.Position + cameraTransform.Forward,
            cameraTransform.Up));
        var projection = ToNumerics(camera.GetProjectionMatrix());
        var sunDirection = BlueSky.Meridian.GlobalEnvironment.SunDirection;
        var camPos = new Vector3(cameraTransform.Position.X, cameraTransform.Position.Y, cameraTransform.Position.Z);

        _viewportRenderer.PreRender(cmd, sunDirection, camPos);
        cmd.BeginRenderPass(
            new[] { _colorTarget },
            _depthTarget,
            ClearValue.FromColor(_clearColor.X, _clearColor.Y, _clearColor.Z, _clearColor.W));
        cmd.SetViewport(new BlueSky.BSR.RHI.Viewport
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
            camPos,
            0,
            0,
            checked((int)_targetWidth),
            checked((int)_targetHeight),
            0.016f,
            _colorTarget,
            _depthTarget);

        cmd.EndRenderPass();
        SubmitScene(cmd);
    }

    private void SubmitScene(IRHICommandBuffer cmd)
    {
        // M4b: on Metal the scene commits async and signals a shared event;
        // the composite pass waits on it (zero CPU stall). Elsewhere blocking.
        var inner = (_device as IRHIWrapped<IRHIDevice>)?.Inner;
        if (inner is BlueSky.BSR.RHI.Metal.MetalDevice metal)
        {
            var innerCmd = (cmd as IRHIWrapped<IRHICommandBuffer>)?.Inner ?? cmd;
            metal.SubmitSceneNoWait(innerCmd);
            return;
        }
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

        // M4b: targets may be sampled by in-flight composite frames — drain first.
        var innerDev = (_device as IRHIWrapped<IRHIDevice>)?.Inner;
        (innerDev as BlueSky.BSR.RHI.Metal.MetalDevice)?.DrainPending();
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
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled | TextureUsage.TransferSrc,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "BSR.ViewportDepth"
        });
        _targetWidth = width;
        _targetHeight = height;
    }

    private static Matrix4x4 ToNumerics(BlueSky.Ramanujan.Matrix4x4 matrix) => new(
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
