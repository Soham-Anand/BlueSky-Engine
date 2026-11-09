using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BlueSky.Aegis.Detection.Probes
{
    internal class WindowsProbe : IPlatformGpuProbe
    {
        // ── Native DXGI 1.1 / 1.6 P/Invoke for <1ms 64-bit VRAM & High-Performance Adapter Detection ──
        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

        [DllImport("d3d12.dll", ExactSpelling = true)]
        private static extern int D3D12CreateDevice(
            IntPtr pAdapter,
            uint minimumFeatureLevel,
            in Guid riid,
            out IntPtr ppDevice);

        private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
        private static readonly Guid IID_IDXGIFactory6 = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
        private static readonly Guid IID_IDXGIAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
        private static readonly Guid IID_ID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

        private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
        private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
        private const uint DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE = 2;
        private const uint D3D_FEATURE_LEVEL_11_0 = 0xb000;

        // D3D12_FEATURE constants
        private const uint D3D12_FEATURE_D3D12_OPTIONS1 = 8;
        private const uint D3D12_FEATURE_D3D12_OPTIONS5 = 27;
        private const uint D3D12_FEATURE_D3D12_OPTIONS6 = 30;
        private const uint D3D12_FEATURE_D3D12_OPTIONS7 = 32;
        private const uint D3D12_FEATURE_ARCHITECTURE1 = 16;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public nuint DedicatedVideoMemory;
            public nuint DedicatedSystemMemory;
            public nuint SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_FEATURE_DATA_ARCHITECTURE1
        {
            public uint NodeIndex;
            public int TileBasedRenderer;
            public int UMA;
            public int CacheCoherentUMA;
            public int IsolatedMMU;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_FEATURE_DATA_D3D12_OPTIONS5
        {
            public int SRVOnlyTiledResourceTier3;
            public uint RenderPassesTier;
            public uint RaytracingTier; // 0 = NotSupported, 10 = Tier 1.0, 11 = Tier 1.1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_FEATURE_DATA_D3D12_OPTIONS7
        {
            public uint MeshShaderTier; // 0 = NotSupported, 10 = Tier 1
            public uint SamplerFeedbackTier;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr thisPtr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Delegate(IntPtr thisPtr, uint adapterIndex, out IntPtr ppAdapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapterByGpuPreferenceDelegate(
            IntPtr thisPtr,
            uint adapterIndex,
            uint gpuPreference,
            in Guid riid,
            out IntPtr ppvAdapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Delegate(IntPtr thisPtr, out DXGI_ADAPTER_DESC1 pDesc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CheckFeatureSupportDelegate(
            IntPtr thisPtr,
            uint feature,
            void* pFeatureSupportData,
            uint featureSupportDataSize);

        public GpuCapabilities? Probe()
        {
            int sysMemMB = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));

            var caps = new GpuCapabilities
            {
                OS = OSPlatform.Windows,
                SystemMemoryMB = sysMemMB,
                SupportsOpenGL33 = true,
                DetectionMethod = "native:dxgi"
            };

            // 1. Primary fast path: Native DXGI 1.6 / 1.1 + D3D12 hardware feature check (<1ms, avoids WMI 4GB uint32 wrap)
            bool gotData = ProbeDxgiNative(ref caps, out bool d3d12Probed);

            // 2. Fallback: PowerShell CIM query
            if (!gotData)
            {
                gotData = ProbePowerShell(ref caps);
                if (gotData)
                    caps.DetectionMethod = "shell:powershell";
            }

            // 3. Legacy fallback: wmic (Windows 10 and older)
            if (!gotData)
            {
                gotData = ProbeWmic(ref caps);
                if (gotData)
                    caps.DetectionMethod = "shell:wmic";
            }

            if (!gotData)
                return null;

            // DX support based on OS version and D3D12 probe
            caps.SupportsDX11 = true;
            if (!d3d12Probed)
                caps.SupportsDX12 = Environment.OSVersion.Version.Major >= 10;
            caps.SupportsCompute = true;
            caps.SupportsIndirectDrawing = true;
            caps.SupportsBCTextureCompression = true;
            caps.SupportsFloat32Filtering = true;

            // Check for vulkan-1.dll in system32
            try
            {
                var systemDir = Environment.SystemDirectory;
                caps.SupportsVulkan = !string.IsNullOrEmpty(systemDir) && File.Exists(Path.Combine(systemDir, "vulkan-1.dll"));
            }
            catch
            {
                caps.SupportsVulkan = !GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);
            }

            caps.SupportsOpenGL45 = !GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name) ||
                                    caps.Vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase) ||
                                    caps.Vendor.Equals("Intel", StringComparison.OrdinalIgnoreCase);

            caps.SupportsMetal = false;
            caps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);
            caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);

            // Fallback Hardware RayTracing & Mesh Shaders heuristic when D3D12 feature query wasn't available
            if (!d3d12Probed)
            {
                string upperName = (caps.Name ?? "").ToUpperInvariant();
                if (caps.SupportsDX12 &&
                    (upperName.Contains("RTX") || upperName.Contains("RX 6") || upperName.Contains("RX 7") ||
                     upperName.Contains("RX 8") || upperName.Contains("RX 9") || upperName.Contains("ARC")))
                {
                    caps.SupportsRayTracing = true;
                    caps.SupportsMeshShaders = true;
                    caps.SupportsShaderBarycentrics = true;
                }
            }

            return caps;
        }

        private static unsafe bool ProbeDxgiNative(ref GpuCapabilities caps, out bool d3d12Probed)
        {
            d3d12Probed = false;
            if (!OperatingSystem.IsWindows()) return false;

            IntPtr factory = IntPtr.Zero;
            IntPtr bestAdapter = IntPtr.Zero;
            bool usedFactory6 = false;

            try
            {
                // Prefer IDXGIFactory6 so we can ask Windows for DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE directly
                Guid iid6 = IID_IDXGIFactory6;
                int hr = CreateDXGIFactory1(in iid6, out factory);
                if (hr >= 0 && factory != IntPtr.Zero)
                {
                    usedFactory6 = true;
                }
                else
                {
                    Guid iid1 = IID_IDXGIFactory1;
                    hr = CreateDXGIFactory1(in iid1, out factory);
                    if (hr < 0 || factory == IntPtr.Zero)
                        return false;
                }

                IntPtr* factoryVtbl = *(IntPtr**)factory;
                var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(factoryVtbl[12]);
                EnumAdapterByGpuPreferenceDelegate? enumByPref = usedFactory6
                    ? Marshal.GetDelegateForFunctionPointer<EnumAdapterByGpuPreferenceDelegate>(factoryVtbl[29])
                    : null;

                string bestName = "";
                string bestVendor = "Unknown";
                long bestVramMB = -1;
                long bestSharedMB = 0;
                bool bestIsDiscrete = false;

                for (uint i = 0; i < 16; i++)
                {
                    IntPtr adapter = IntPtr.Zero;
                    int enumHr;
                    if (enumByPref != null)
                    {
                        Guid adapterIid = IID_IDXGIAdapter1;
                        enumHr = enumByPref(factory, i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, in adapterIid, out adapter);
                    }
                    else
                    {
                        enumHr = enumAdapters1(factory, i, out adapter);
                    }

                    if (enumHr == DXGI_ERROR_NOT_FOUND || enumHr < 0 || adapter == IntPtr.Zero)
                        break;

                    bool keepAdapter = false;
                    try
                    {
                        IntPtr* adapterVtbl = *(IntPtr**)adapter;
                        // IDXGIAdapter1::GetDesc1 is vtable slot 10
                        var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(adapterVtbl[10]);
                        if (getDesc1(adapter, out DXGI_ADAPTER_DESC1 desc) >= 0)
                        {
                            // Skip Microsoft Basic Render Driver (VendorId 0x1414) or software adapters unless nothing else exists
                            bool isSoftware = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || desc.VendorId == 0x1414;
                            if (isSoftware && !string.IsNullOrEmpty(bestName))
                                continue;

                            string name = (desc.Description ?? "").Trim('\0').Trim();
                            if (string.IsNullOrEmpty(name)) continue;

                            string vendor = VendorFromPciId(desc.VendorId, name);
                            long dedicatedMB = (long)((ulong)desc.DedicatedVideoMemory / (1024UL * 1024UL));
                            long sharedMB = (long)((ulong)desc.SharedSystemMemory / (1024UL * 1024UL));
                            long effectiveMB = dedicatedMB > 0 ? dedicatedMB : sharedMB;
                            bool isDiscrete = !GpuClassifier.IsLikelyIntegrated(vendor, name) && !isSoftware;

                            if (string.IsNullOrEmpty(bestName) ||
                                (!bestIsDiscrete && isDiscrete) ||
                                (bestIsDiscrete == isDiscrete && effectiveMB > bestVramMB))
                            {
                                bestName = name;
                                bestVendor = vendor;
                                bestVramMB = effectiveMB;
                                bestSharedMB = sharedMB;
                                bestIsDiscrete = isDiscrete;

                                if (bestAdapter != IntPtr.Zero)
                                    ReleaseComObject(bestAdapter);
                                bestAdapter = adapter;
                                keepAdapter = true;
                            }
                        }
                    }
                    finally
                    {
                        if (!keepAdapter)
                            ReleaseComObject(adapter);
                    }
                }

                if (string.IsNullOrEmpty(bestName))
                    return false;

                caps.Name = bestName;
                caps.Vendor = bestVendor;
                caps.VramMB = (int)System.Math.Max(0L, bestVramMB);
                caps.RecommendedMaxWorkingSetMB = caps.VramMB > 0 ? caps.VramMB : (int)System.Math.Max(0L, bestSharedMB);

                // Probe D3D12 hardware features directly on the chosen adapter (RaytracingTier, MeshShaderTier, UMA)
                if (bestAdapter != IntPtr.Zero)
                {
                    d3d12Probed = TryProbeD3D12Features(bestAdapter, ref caps);
                    if (d3d12Probed)
                        caps.DetectionMethod = "native:dxgi+d3d12";
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (bestAdapter != IntPtr.Zero)
                    ReleaseComObject(bestAdapter);
                if (factory != IntPtr.Zero)
                    ReleaseComObject(factory);
            }
        }

        private static unsafe bool TryProbeD3D12Features(IntPtr adapter, ref GpuCapabilities caps)
        {
            IntPtr d3d12Device = IntPtr.Zero;
            try
            {
                Guid deviceIid = IID_ID3D12Device;
                int hr = D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, in deviceIid, out d3d12Device);
                if (hr < 0 || d3d12Device == IntPtr.Zero)
                    return false;

                caps.SupportsDX12 = true;

                // ID3D12Device::CheckFeatureSupport is vtable slot 13
                // (IUnknown 0..2, ID3D12Object 3..6, ID3D12Device: GetNodeCount 7, CreateCommandQueue 8,
                //  CreateCommandAllocator 9, CreateGraphicsPipelineState 10, CreateComputePipelineState 11,
                //  CreateCommandList 12, CheckFeatureSupport 13)
                IntPtr* deviceVtbl = *(IntPtr**)d3d12Device;
                var checkFeature = Marshal.GetDelegateForFunctionPointer<CheckFeatureSupportDelegate>(deviceVtbl[13]);

                // Check UMA / Unified Memory Architecture
                var arch1 = new D3D12_FEATURE_DATA_ARCHITECTURE1 { NodeIndex = 0 };
                if (checkFeature(d3d12Device, D3D12_FEATURE_ARCHITECTURE1, &arch1, (uint) sizeof(D3D12_FEATURE_DATA_ARCHITECTURE1)) >= 0)
                {
                    if (arch1.UMA != 0 && arch1.CacheCoherentUMA != 0)
                        caps.HasUnifiedMemory = true;
                }

                // Check DXR Raytracing Tier (Options5)
                var opts5 = new D3D12_FEATURE_DATA_D3D12_OPTIONS5();
                if (checkFeature(d3d12Device, D3D12_FEATURE_D3D12_OPTIONS5, &opts5, (uint) sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS5)) >= 0)
                {
                    caps.SupportsRayTracing = opts5.RaytracingTier >= 10; // D3D12_RAYTRACING_TIER_1_0 = 10
                }

                // Check Mesh Shader Tier (Options7)
                var opts7 = new D3D12_FEATURE_DATA_D3D12_OPTIONS7();
                if (checkFeature(d3d12Device, D3D12_FEATURE_D3D12_OPTIONS7, &opts7, (uint) sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS7)) >= 0)
                {
                    caps.SupportsMeshShaders = opts7.MeshShaderTier >= 10; // D3D12_MESH_SHADER_TIER_1 = 10
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (d3d12Device != IntPtr.Zero)
                    ReleaseComObject(d3d12Device);
            }
        }

        private static unsafe void ReleaseComObject(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return;
            try
            {
                IntPtr* vtbl = *(IntPtr**)ptr;
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(vtbl[2]);
                release(ptr);
            }
            catch { }
        }

        private static string VendorFromPciId(uint vendorId, string fallbackName)
        {
            return vendorId switch
            {
                0x10DE => "NVIDIA",
                0x1002 or 0x1022 => "AMD",
                0x8086 => "Intel",
                0x106B => "Apple",
                0x5143 or 0x4D4F => "Qualcomm",
                _ => DeriveVendor(fallbackName)
            };
        }

        private static bool ProbeWmic(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("wmic",
                "path win32_VideoController get Name,AdapterRAM,DriverVersion /format:csv");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return ParseCsvOutput(output, ref caps);
        }

        private static bool ProbePowerShell(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("powershell",
                "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM,DriverVersion | ConvertTo-Csv -NoTypeInformation\"");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return ParseCsvOutput(output, ref caps);
        }

        internal static bool ParseCsvOutput(string output, ref GpuCapabilities caps)
        {
            try
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                int headerIdx = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("Name", StringComparison.OrdinalIgnoreCase) &&
                        lines[i].Contains("AdapterRAM", StringComparison.OrdinalIgnoreCase))
                    {
                        headerIdx = i;
                        break;
                    }
                }

                if (headerIdx < 0 || headerIdx + 1 >= lines.Length)
                    return false;

                var headers = lines[headerIdx].Split(',').Select(h => h.Trim().Trim('"')).ToArray();
                int nameIdx = Array.FindIndex(headers, h => h.Equals("Name", StringComparison.OrdinalIgnoreCase));
                int ramIdx = Array.FindIndex(headers, h => h.Equals("AdapterRAM", StringComparison.OrdinalIgnoreCase));
                int driverIdx = Array.FindIndex(headers, h => h.Equals("DriverVersion", StringComparison.OrdinalIgnoreCase));

                string bestName = "";
                long bestVram = 0;
                string bestDriver = "";

                for (int i = headerIdx + 1; i < lines.Length; i++)
                {
                    var fields = lines[i].Split(',').Select(f => f.Trim().Trim('"')).ToArray();

                    string name = nameIdx >= 0 && nameIdx < fields.Length ? fields[nameIdx] : "";
                    long vramBytes = 0;
                    string driver = "";

                    if (ramIdx >= 0 && ramIdx < fields.Length)
                        long.TryParse(fields[ramIdx], out vramBytes);
                    if (driverIdx >= 0 && driverIdx < fields.Length)
                        driver = fields[driverIdx];

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // Skip Microsoft Basic Render Driver unless nothing else exists
                    if (name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(bestName))
                        continue;

                    // Handle uint32 overflow in WMI AdapterRAM (negative when parsed as signed 32-bit)
                    if (vramBytes < 0)
                        vramBytes = (uint)vramBytes;

                    bool currentIsIntegrated = GpuClassifier.IsLikelyIntegrated(DeriveVendor(name), name);
                    bool bestIsIntegrated = GpuClassifier.IsLikelyIntegrated(DeriveVendor(bestName), bestName);

                    if (string.IsNullOrEmpty(bestName) ||
                        (bestIsIntegrated && !currentIsIntegrated) ||
                        (bestIsIntegrated == currentIsIntegrated && vramBytes > bestVram))
                    {
                        bestName = name;
                        bestVram = vramBytes;
                        bestDriver = driver;
                    }
                }

                if (string.IsNullOrEmpty(bestName))
                    return false;

                caps.Name = bestName;
                caps.Vendor = DeriveVendor(bestName);
                caps.VramMB = (int)(bestVram / (1024 * 1024));
                caps.DriverVersion = bestDriver;

                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string DeriveVendor(string gpuName)
        {
            var upper = (gpuName ?? "").ToUpperInvariant();
            if (upper.Contains("NVIDIA") || upper.Contains("GEFORCE") || upper.Contains("RTX") || upper.Contains("GTX") || upper.Contains("QUADRO")) return "NVIDIA";
            if (upper.Contains("AMD") || upper.Contains("RADEON")) return "AMD";
            if (upper.Contains("INTEL")) return "Intel";
            if (upper.Contains("QUALCOMM") || upper.Contains("ADRENO")) return "Qualcomm";
            return "Unknown";
        }
    }
}
