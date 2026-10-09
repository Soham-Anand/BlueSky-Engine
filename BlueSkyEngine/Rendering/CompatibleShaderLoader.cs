using System;
using System.Collections.Generic;
using System.IO;
using BlueSky.Rendering.RHI;

namespace BlueSky.Rendering;

/// <summary>Loads backend-specific compiled shaders for the Polaris upscaler.</summary>
public sealed class CompatibleShaderLoader
{
    private readonly IRHIDevice _device;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

    public CompatibleShaderLoader(IRHIDevice device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public byte[]? LoadShader(string name, ShaderStage stage)
    {
        var candidates = GetCandidateFiles(name, stage);
        foreach (var candidate in candidates)
        {
            if (_cache.TryGetValue(candidate, out var cached))
                return cached;
            if (!File.Exists(candidate))
                continue;

            var bytes = File.ReadAllBytes(candidate);
            if (bytes.Length == 0)
                throw new InvalidDataException($"Compiled shader is empty: {candidate}");

            _cache[candidate] = bytes;
            return bytes;
        }

        return null;
    }

    public IRHIPipeline? CreateGraphicsPipeline(string name, GraphicsPipelineDesc desc)
    {
        var vertexShader = LoadShader(name, ShaderStage.Vertex);
        var fragmentShader = LoadShader(name, ShaderStage.Fragment);
        if (vertexShader == null || fragmentShader == null)
            return null;

        desc.VertexShader.Bytecode = vertexShader;
        desc.FragmentShader.Bytecode = fragmentShader;
        return _device.CreateGraphicsPipeline(desc);
    }

    private IEnumerable<string> GetCandidateFiles(string name, ShaderStage stage)
    {
        var fileNames = _device.Backend switch
        {
            RHIBackend.Metal => new[] { $"{name}.metallib" },
            RHIBackend.Vulkan => new[]
            {
                $"{name}.{StageSuffix(stage)}.spv"
            },
            RHIBackend.DirectX11 => new[]
            {
                $"{StagePrefix(stage)}_{name}.cso",
                $"{name}_{StageSuffix(stage)}.cso"
            },
            _ => Array.Empty<string>()
        };

        foreach (var fileName in fileNames)
        {
            yield return Path.Combine(AppContext.BaseDirectory, "Shaders", fileName);
            yield return Path.Combine(AppContext.BaseDirectory, "Editor", "Shaders", fileName);
            yield return Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders", fileName);
            yield return Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", fileName);
            yield return Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Rendering", "Shaders", fileName);
        }
    }

    private static string StagePrefix(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vs",
        ShaderStage.Fragment => "ps",
        ShaderStage.Compute => "cs",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private static string StageSuffix(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vert",
        ShaderStage.Fragment => "frag",
        ShaderStage.Compute => "comp",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}
