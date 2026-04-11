using System;
using System.Numerics;
using BlueSky.Platform;
using NotBSRenderer;

namespace BlueSky.RHI.Test;

class SimpleClearTest
{
    public static void Run()
    {
        Console.WriteLine("=== Simple Clear Test ===\n");
        
        var options = WindowOptions.Default;
        options.Title = "RHI Clear Test";
        options.Width = 800;
        options.Height = 600;
        
        var window = WindowFactory.Create(options);
        var device = RHIDevice.Create(RHIBackend.Metal, window);
        var swapchain = device.CreateSwapchain(window);
        
        Console.WriteLine("[Clear Test] Setup complete, starting render loop");
        
        float hue = 0f;
        window.Render += (dt) =>
        {
            // Cycle through colors
            hue += 0.01f;
            if (hue > 1f) hue = 0f;
            
            var color = HSVToRGB(hue, 1f, 1f);
            
            swapchain.AcquireNextImage();
            var cmd = device.CreateCommandBuffer();
            
            cmd.BeginRenderPass(swapchain.CurrentRenderTarget, ClearValue.FromColor(color.X, color.Y, color.Z, 1f));
            cmd.EndRenderPass();
            
            device.Submit(cmd, swapchain);
            swapchain.Present();
            cmd.Dispose();
        };
        
        window.Closing += () =>
        {
            swapchain.Dispose();
            device.Dispose();
        };
        
        window.Show();
        
        var frameTime = System.Diagnostics.Stopwatch.StartNew();
        const double targetFrameTime = 1.0 / 60.0;
        
        while (!window.IsClosing)
        {
            window.ProcessEvents();
            
            var elapsed = frameTime.Elapsed.TotalSeconds;
            if (elapsed < targetFrameTime)
            {
                System.Threading.Thread.Sleep((int)((targetFrameTime - elapsed) * 1000));
            }
            frameTime.Restart();
        }
    }
    
    static Vector3 HSVToRGB(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1 - MathF.Abs((h * 6) % 2 - 1));
        float m = v - c;
        
        float r, g, b;
        if (h < 1f/6f) { r = c; g = x; b = 0; }
        else if (h < 2f/6f) { r = x; g = c; b = 0; }
        else if (h < 3f/6f) { r = 0; g = c; b = x; }
        else if (h < 4f/6f) { r = 0; g = x; b = c; }
        else if (h < 5f/6f) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        
        return new Vector3(r + m, g + m, b + m);
    }
}
