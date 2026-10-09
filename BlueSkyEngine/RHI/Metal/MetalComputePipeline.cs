using System;
using System.Runtime.InteropServices;
using static BlueSky.Rendering.RHI.Metal.MetalInterop;

namespace BlueSky.Rendering.RHI.Metal
{
    /// <summary>
    /// Metal compute pipeline state — wraps MTLComputePipelineState.
    /// Works on all Metal-capable Macs (Intel HD 4000+ through Apple Silicon M4).
    /// Metal 1.0+ for compute shader support.
    /// </summary>
    internal class MetalComputePipeline : IRHIPipeline
    {
        private IntPtr _computePipelineState;
        private bool _disposed;

        internal IntPtr Handle => _computePipelineState;

        public MetalComputePipeline(MetalDevice device, ComputePipelineDesc desc)
        {
            CreateComputePipelineState(device, desc);
        }

        private void CreateComputePipelineState(MetalDevice device, ComputePipelineDesc desc)
        {
            // Load compute function from .metallib
            var computeFunction = LoadComputeFunction(device, desc.ComputeShader);

            // Create compute pipeline state via newComputePipelineStateWithFunction:error:
            var newPipelineSel = GetSelector("newComputePipelineStateWithFunction:error:");
            IntPtr error = IntPtr.Zero;
            _computePipelineState = NewComputePipelineState(device.Device, newPipelineSel, computeFunction, ref error);

            Release(computeFunction);

            if (_computePipelineState == IntPtr.Zero)
            {
                var errorMsg = GetNSErrorDescription(error);
                throw new Exception($"Failed to create Metal compute pipeline state: {errorMsg}");
            }

            // Set debug name
            if (!string.IsNullOrEmpty(desc.DebugName))
            {
                var labelSel = GetSelector("setLabel:");
                var nsString = CreateNSString(desc.DebugName);
                objc_msgSend_void_ptr(_computePipelineState, labelSel, nsString);
                Release(nsString);
            }
        }

        private static IntPtr LoadComputeFunction(MetalDevice device, ShaderDesc shader)
        {
            IntPtr library;

            if (shader.Bytecode != null && shader.Bytecode.Length > 0)
            {
                // Write bytecode to temp file for reliable loading
                var tempPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"bs_compute_{shader.EntryPoint}_{Guid.NewGuid()}.metallib");
                System.IO.File.WriteAllBytes(tempPath, shader.Bytecode);

                try
                {
                    var urlClass = GetClass("NSURL");
                    var fileURLWithPathSel = GetSelector("fileURLWithPath:");
                    var pathNS = CreateNSString(tempPath);
                    var url = objc_msgSend_ptr(urlClass, fileURLWithPathSel, pathNS);
                    Release(pathNS);

                    if (url == IntPtr.Zero)
                        throw new Exception("Failed to create NSURL for temp compute library");

                    var newLibraryWithURLSel = GetSelector("newLibraryWithURL:error:");
                    IntPtr error = IntPtr.Zero;
                    library = NewLibraryWithURL(device.Device, newLibraryWithURLSel, url, ref error);

                    if (library == IntPtr.Zero)
                    {
                        var errorMsg = GetNSErrorDescription(error);
                        throw new Exception($"Failed to load compute library from temp file: {errorMsg}");
                    }
                }
                finally
                {
                    try { System.IO.File.Delete(tempPath); } catch { }
                }
            }
            else
            {
                // Load from .metallib file — look for compute shaders
                string libraryName;
                if (shader.EntryPoint.StartsWith("cs_") || shader.EntryPoint.StartsWith("compute_"))
                    libraryName = "compute.metallib";
                else
                    libraryName = "default.metallib";

                var exeDir = AppContext.BaseDirectory;
                var libraryPath = System.IO.Path.Combine(exeDir, "Editor", "Shaders", libraryName);

                if (!System.IO.File.Exists(libraryPath))
                    libraryPath = System.IO.Path.Combine(exeDir, "Shaders", libraryName);

                if (!System.IO.File.Exists(libraryPath))
                {
                    var bundleResources = System.IO.Path.Combine(exeDir, "..", "Resources");
                    libraryPath = System.IO.Path.Combine(bundleResources, "Editor", "Shaders", libraryName);
                    if (!System.IO.File.Exists(libraryPath))
                        libraryPath = System.IO.Path.Combine(bundleResources, "Shaders", libraryName);
                }

                if (!System.IO.File.Exists(libraryPath))
                    throw new Exception($"Compute Metal library not found: {libraryPath}");

                var urlClass = GetClass("NSURL");
                var fileURLWithPathSel = GetSelector("fileURLWithPath:");
                var pathNS = CreateNSString(libraryPath);
                var url = objc_msgSend_ptr(urlClass, fileURLWithPathSel, pathNS);
                Release(pathNS);

                if (url == IntPtr.Zero)
                    throw new Exception("Failed to create NSURL for compute Metal library");

                var newLibraryWithURLSel = GetSelector("newLibraryWithURL:error:");
                IntPtr error = IntPtr.Zero;
                library = NewLibraryWithURL(device.Device, newLibraryWithURLSel, url, ref error);

                if (library == IntPtr.Zero)
                {
                    var errorMsg = GetNSErrorDescription(error);
                    throw new Exception($"Failed to load compute Metal library: {errorMsg}");
                }
            }

            // Get function by name
            var newFunctionSel = GetSelector("newFunctionWithName:");
            var entryPointNS = CreateNSString(shader.EntryPoint);
            var function = objc_msgSend_ptr(library, newFunctionSel, entryPointNS);

            Release(entryPointNS);
            Release(library);

            if (function == IntPtr.Zero)
                throw new Exception($"Failed to find compute shader function: {shader.EntryPoint}");

            return function;
        }

        private static string GetNSErrorDescription(IntPtr error)
        {
            if (error == IntPtr.Zero) return "Unknown error";

            var localizedDescSel = GetSelector("localizedDescription");
            var desc = objc_msgSend(error, localizedDescSel);
            return GetNSStringContent(desc);
        }

        private static string GetNSStringContent(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero) return "";

            var utf8Sel = GetSelector("UTF8String");
            var utf8Ptr = objc_msgSend(nsString, utf8Sel);
            return Marshal.PtrToStringAnsi(utf8Ptr) ?? "";
        }

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr NewComputePipelineState(IntPtr receiver, IntPtr selector, IntPtr function, ref IntPtr error);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr NewLibraryWithURL(IntPtr receiver, IntPtr selector, IntPtr url, ref IntPtr error);

        public void Dispose()
        {
            if (_disposed) return;

            if (_computePipelineState != IntPtr.Zero)
            {
                Release(_computePipelineState);
                _computePipelineState = IntPtr.Zero;
            }

            _disposed = true;
        }
    }
}
