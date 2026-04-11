using System;

namespace BlueSky.Core.Platform.Detection
{
    public static class RendererOverride
    {
        /// <summary>
        /// Parses a renderer backend override from command-line args or BLUESKY_RENDERER env var.
        /// CLI args take precedence. Returns null if no override specified.
        /// </summary>
        public static RendererBackend? ParseOverride(string[] args)
        {
            // Check command-line args first (higher priority)
            foreach (var arg in args)
            {
                if (arg.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("--renderer=".Length);
                    var parsed = ParseBackend(value);
                    if (parsed.HasValue)
                    {
                        Console.WriteLine($"[GPU] Renderer override from CLI: {parsed.Value}");
                        return parsed;
                    }
                    Console.WriteLine($"[GPU] Warning: Unknown renderer override '{value}', ignoring");
                }
            }

            // Check environment variable
            var envValue = Environment.GetEnvironmentVariable("BLUESKY_RENDERER");
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                var parsed = ParseBackend(envValue);
                if (parsed.HasValue)
                {
                    Console.WriteLine($"[GPU] Renderer override from BLUESKY_RENDERER: {parsed.Value}");
                    return parsed;
                }
                Console.WriteLine($"[GPU] Warning: Unknown BLUESKY_RENDERER value '{envValue}', ignoring");
            }

            return null;
        }

        private static RendererBackend? ParseBackend(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "opengl" or "gl" => RendererBackend.OpenGL,
                "vulkan" or "vk" => RendererBackend.Vulkan,
                "dx11" or "d3d11" or "directx11" => RendererBackend.DX11,
                "dx12" or "d3d12" or "directx12" => RendererBackend.DX12,
                "metal" or "mtl" => RendererBackend.Metal,
                _ => null
            };
        }
    }
}
