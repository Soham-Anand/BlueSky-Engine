using System.Runtime.InteropServices;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Vulkan P/Invoke declarations and constants
/// Cross-platform: Windows (vulkan-1.dll), Linux (libvulkan.so.1), macOS (libvulkan.dylib via MoltenVK)
/// </summary>
internal static class VulkanInterop
{
    // Platform-specific library names
    private const string VulkanLibWindows = "vulkan-1.dll";
    private const string VulkanLibLinux = "libvulkan.so.1";
    private const string VulkanLibMacOS = "libvulkan.dylib";
    
    // Vulkan API Version
    public const uint VK_API_VERSION_1_0 = 0x00400000;
    public const uint VK_API_VERSION_1_1 = 0x00401000;
    public const uint VK_API_VERSION_1_2 = 0x00402000;
    public const uint VK_API_VERSION_1_3 = 0x00403000;
    
    // Result codes
    public const int VK_SUCCESS = 0;
    public const int VK_ERROR_OUT_OF_HOST_MEMORY = -1;
    public const int VK_ERROR_OUT_OF_DEVICE_MEMORY = -2;
    
    // Structure types
    public const uint VK_STRUCTURE_TYPE_APPLICATION_INFO = 0;
    public const uint VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1;
    public const uint VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO = 3;
    
    // Queue flags
    public const uint VK_QUEUE_GRAPHICS_BIT = 0x00000001;
    public const uint VK_QUEUE_COMPUTE_BIT = 0x00000002;
    public const uint VK_QUEUE_TRANSFER_BIT = 0x00000004;
    
    // Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct VkApplicationInfo
    {
        public uint sType;
        public IntPtr pNext;
        public IntPtr pApplicationName;
        public uint applicationVersion;
        public IntPtr pEngineName;
        public uint engineVersion;
        public uint apiVersion;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct VkInstanceCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr pApplicationInfo;
        public uint enabledLayerCount;
        public IntPtr ppEnabledLayerNames;
        public uint enabledExtensionCount;
        public IntPtr ppEnabledExtensionNames;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct VkDeviceCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint queueCreateInfoCount;
        public IntPtr pQueueCreateInfos;
        public uint enabledLayerCount;
        public IntPtr ppEnabledLayerNames;
        public uint enabledExtensionCount;
        public IntPtr ppEnabledExtensionNames;
        public IntPtr pEnabledFeatures;
    }
    
    // Platform-specific P/Invoke declarations
    
    // Windows
    [DllImport(VulkanLibWindows, EntryPoint = "vkCreateInstance")]
    private static extern int vkCreateInstance_Windows(
        ref VkInstanceCreateInfo pCreateInfo,
        IntPtr pAllocator,
        out IntPtr pInstance);
    
    // Linux
    [DllImport(VulkanLibLinux, EntryPoint = "vkCreateInstance")]
    private static extern int vkCreateInstance_Linux(
        ref VkInstanceCreateInfo pCreateInfo,
        IntPtr pAllocator,
        out IntPtr pInstance);
    
    // macOS (MoltenVK)
    [DllImport(VulkanLibMacOS, EntryPoint = "vkCreateInstance")]
    private static extern int vkCreateInstance_macOS(
        ref VkInstanceCreateInfo pCreateInfo,
        IntPtr pAllocator,
        out IntPtr pInstance);
    
    // Platform-agnostic wrapper
    public static int vkCreateInstance(
        ref VkInstanceCreateInfo pCreateInfo,
        IntPtr pAllocator,
        out IntPtr pInstance)
    {
        if (OperatingSystem.IsWindows())
            return vkCreateInstance_Windows(ref pCreateInfo, pAllocator, out pInstance);
        
        if (OperatingSystem.IsLinux())
            return vkCreateInstance_Linux(ref pCreateInfo, pAllocator, out pInstance);
        
        if (OperatingSystem.IsMacOS())
            return vkCreateInstance_macOS(ref pCreateInfo, pAllocator, out pInstance);
        
        throw new PlatformNotSupportedException("Vulkan not supported on this platform");
    }
    
    // TODO: Add more Vulkan functions
    // - vkEnumeratePhysicalDevices
    // - vkCreateDevice
    // - vkGetDeviceQueue
    // - vkCreateBuffer
    // - vkCreateImage
    // - vkCreateSwapchainKHR
    // - vkCreateCommandPool
    // - vkAllocateCommandBuffers
    // - etc.
}
