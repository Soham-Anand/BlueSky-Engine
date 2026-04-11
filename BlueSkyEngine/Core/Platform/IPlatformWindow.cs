using System;
using BlueSky.Core.Platform.Detection;

namespace BlueSky.Core.Platform
{
    public interface IPlatformWindow : IDisposable
    {
        string Title { get; set; }
        int Width { get; }
        int Height { get; }
        bool IsClosing { get; }
        object NativeWindow { get; }
        
        /// <summary>
        /// GPU capabilities detected at window creation, or null if detection was not performed.
        /// </summary>
        GpuCapabilities? DetectedGpu { get; }
        
        event Action Load;
        event Action<double> Update;
        event Action<double> Render;
        event Action Resize;
        event Action Closing;

        void Run();
        void Close();
    }
}
