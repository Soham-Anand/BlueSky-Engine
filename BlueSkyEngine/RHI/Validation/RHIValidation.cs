using System;
using System.Collections.Generic;

namespace NotBSRenderer;

internal static class RHIValidation
{
    internal static bool Enabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("BLUESKY_RHI_NO_VALIDATION");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return !(value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                         value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         value.Equals("yes", StringComparison.OrdinalIgnoreCase));
            }

            return true;
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }

    internal static void RequireState(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    internal static void RequireNotDisposed(bool disposed, string typeName)
    {
        if (disposed)
            throw new ObjectDisposedException(typeName);
    }

    internal static void ValidateBufferDesc(BufferDesc desc)
    {
        Require(desc.Size > 0, "Buffer size must be greater than zero.");
        Require(desc.Usage != 0, "Buffer usage must be specified.");
    }

    internal static void ValidateTextureDesc(TextureDesc desc)
    {
        Require(desc.Width > 0, "Texture width must be greater than zero.");
        Require(desc.Height > 0, "Texture height must be greater than zero.");
        Require(desc.Depth > 0, "Texture depth must be greater than zero.");
        Require(desc.MipLevels > 0, "Texture mip levels must be greater than zero.");
        Require(desc.ArrayLayers > 0, "Texture array layers must be greater than zero.");
        Require(desc.Usage != 0, "Texture usage must be specified.");
    }

    internal static void ValidateShaderDesc(ShaderDesc desc, ShaderStage expectedStage)
    {
        Require(desc.Stage == expectedStage, $"Shader stage mismatch. Expected {expectedStage}.");
        
        // Allow empty bytecode for fixed-function pipelines (e.g., DX9)
        if (desc.Bytecode != null && desc.Bytecode.Length > 0)
        {
            Require(!string.IsNullOrWhiteSpace(desc.EntryPoint), "Shader entry point must be provided when bytecode is present.");
        }
    }

    internal static void ValidateVertexLayout(VertexLayoutDesc layout)
    {
        if (layout.Attributes == null)
            throw new ArgumentException("Vertex attributes must be provided.");
        if (layout.Bindings == null)
            throw new ArgumentException("Vertex bindings must be provided.");

        var bindings = layout.Bindings;
        var attributes = layout.Attributes;

        var bindingStrides = new Dictionary<uint, uint>();
        foreach (var binding in bindings)
        {
            Require(binding.Stride > 0, $"Vertex binding {binding.Binding} stride must be greater than zero.");
            bindingStrides[binding.Binding] = binding.Stride;
        }

        var seenLocations = new HashSet<uint>();
        foreach (var attrib in attributes)
        {
            Require(seenLocations.Add(attrib.Location), $"Duplicate vertex attribute location {attrib.Location}.");
            Require(bindingStrides.ContainsKey(attrib.Binding), $"Vertex attribute binding {attrib.Binding} is not declared.");

            var stride = bindingStrides[attrib.Binding];
            Require(attrib.Offset < stride, $"Vertex attribute offset {attrib.Offset} exceeds stride {stride}.");
        }
    }

    internal static void ValidatePipelineDesc(GraphicsPipelineDesc desc)
    {
        ValidateShaderDesc(desc.VertexShader, ShaderStage.Vertex);
        ValidateShaderDesc(desc.FragmentShader, ShaderStage.Fragment);
        ValidateVertexLayout(desc.VertexLayout);
        bool hasColor = desc.ColorFormats != null && desc.ColorFormats.Length > 0;
        bool hasDepth = desc.DepthFormat.HasValue;
        Require(hasColor || hasDepth, "Pipeline must have at least one color format or a depth format.");
    }

    internal static void ValidateViewport(Viewport viewport)
    {
        Require(viewport.Width > 0, "Viewport width must be greater than zero.");
        Require(viewport.Height > 0, "Viewport height must be greater than zero.");
        Require(viewport.MinDepth <= viewport.MaxDepth, "Viewport MinDepth must be <= MaxDepth.");
    }

    internal static void ValidateScissor(Scissor scissor)
    {
        Require(scissor.Width > 0, "Scissor width must be greater than zero.");
        Require(scissor.Height > 0, "Scissor height must be greater than zero.");
    }

    internal static void ValidateBufferRange(ValidationBuffer buffer, ReadOnlySpan<byte> data, ulong offset)
    {
        Require(offset <= buffer.Size, "Buffer offset exceeds buffer size.");
        Require((ulong)data.Length <= buffer.Size - offset, "Buffer data upload exceeds buffer size.");
    }

    internal static void ValidateTextureUpload(ValidationTexture texture, ReadOnlySpan<byte> data, uint mipLevel)
    {
        Require(mipLevel < texture.Desc.MipLevels, "Mip level out of range for texture.");
        Require(data.Length > 0, "Texture upload data must not be empty.");

        var expectedBytes = CalculateMipDataSize(texture.Desc, mipLevel);
        Require((ulong)data.Length >= expectedBytes, $"Texture upload data is too small for mip level {mipLevel}.");
    }

    internal static ulong CalculateMipDataSize(TextureDesc desc, uint mipLevel)
    {
        uint width = Math.Max(1u, desc.Width >> (int)mipLevel);
        uint height = Math.Max(1u, desc.Height >> (int)mipLevel);
        uint depth = Math.Max(1u, desc.Depth >> (int)mipLevel);

        if (IsBlockCompressed(desc.Format))
        {
            var blockSize = GetBlockByteSize(desc.Format);
            var blocksWide = Math.Max(1u, (width + 3) / 4);
            var blocksHigh = Math.Max(1u, (height + 3) / 4);
            return (ulong)blocksWide * blocksHigh * depth * desc.ArrayLayers * blockSize;
        }

        return (ulong)width * height * depth * desc.ArrayLayers * GetBytesPerPixel(desc.Format);
    }

    private static bool IsBlockCompressed(TextureFormat format)
    {
        return format == TextureFormat.BC1 ||
               format == TextureFormat.BC3 ||
               format == TextureFormat.BC7;
    }

    private static uint GetBlockByteSize(TextureFormat format)
    {
        return format == TextureFormat.BC1 ? 8u : 16u;
    }

    private static uint GetBytesPerPixel(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => 1,
            TextureFormat.R32Float => 4,
            TextureFormat.RGBA8Unorm => 4,
            TextureFormat.RGBA8Srgb => 4,
            TextureFormat.BGRA8Unorm => 4,
            TextureFormat.BGRA8Srgb => 4,
            TextureFormat.RG32Float => 8,
            TextureFormat.RGB32Float => 12,
            TextureFormat.RGBA16Float => 8,
            TextureFormat.RGBA32Float => 16,
            TextureFormat.Depth32Float => 4,
            TextureFormat.Depth24Stencil8 => 4,
            _ => 4
        };
    }
}
