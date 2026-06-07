using System;
using System.Runtime.InteropServices;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Vulkan P/Invoke declarations and function pointer loader.
/// Uses NativeLibrary.Load for cross-platform function resolution —
/// no per-platform DllImport triplication needed.
///
/// Cross-platform: Windows (vulkan-1.dll), Linux (libvulkan.so.1), macOS (libvulkan.dylib via MoltenVK)
/// </summary>
internal static unsafe class VulkanInterop
{
    // ═══════════════════════════════════════════════════════════════════
    //  Library Handle & Bootstrap
    // ═══════════════════════════════════════════════════════════════════

    private static IntPtr _libHandle;
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;

        string libName = OperatingSystem.IsWindows() ? "vulkan-1.dll" :
                         OperatingSystem.IsMacOS()   ? "libvulkan.dylib" :
                                                       "libvulkan.so.1";

        if (!NativeLibrary.TryLoad(libName, typeof(VulkanInterop).Assembly, null, out _libHandle))
            throw new PlatformNotSupportedException($"Failed to load Vulkan library: {libName}");

        LoadCoreFunctions();
        _loaded = true;
    }

    private static T LoadFunc<T>(string name) where T : Delegate
    {
        if (NativeLibrary.TryGetExport(_libHandle, name, out var ptr) && ptr != IntPtr.Zero)
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        return null!;
    }

    /// <summary>
    /// Load a device-level or instance-level function via vkGetInstanceProcAddr / vkGetDeviceProcAddr
    /// </summary>
    public static T LoadInstanceFunc<T>(IntPtr instance, string name) where T : Delegate
    {
        var ptr = _vkGetInstanceProcAddr(instance, name);
        if (ptr == IntPtr.Zero) return null!;
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public static T LoadDeviceFunc<T>(IntPtr device, string name) where T : Delegate
    {
        var ptr = _vkGetDeviceProcAddr(device, name);
        if (ptr == IntPtr.Zero) return null!;
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Constants
    // ═══════════════════════════════════════════════════════════════════

    // API Versions
    public static uint VK_MAKE_API_VERSION(uint variant, uint major, uint minor, uint patch)
        => (variant << 29) | (major << 22) | (minor << 12) | patch;

    public static readonly uint VK_API_VERSION_1_0 = VK_MAKE_API_VERSION(0, 1, 0, 0);
    public static readonly uint VK_API_VERSION_1_1 = VK_MAKE_API_VERSION(0, 1, 1, 0);
    public static readonly uint VK_API_VERSION_1_2 = VK_MAKE_API_VERSION(0, 1, 2, 0);
    public static readonly uint VK_API_VERSION_1_3 = VK_MAKE_API_VERSION(0, 1, 3, 0);

    // Result codes
    public const int VK_SUCCESS = 0;
    public const int VK_NOT_READY = 1;
    public const int VK_TIMEOUT = 2;
    public const int VK_SUBOPTIMAL_KHR = 1000001003;
    public const int VK_ERROR_OUT_OF_HOST_MEMORY = -1;
    public const int VK_ERROR_OUT_OF_DEVICE_MEMORY = -2;
    public const int VK_ERROR_INITIALIZATION_FAILED = -3;
    public const int VK_ERROR_DEVICE_LOST = -4;
    public const int VK_ERROR_OUT_OF_DATE_KHR = -1000001004;
    public const int VK_ERROR_SURFACE_LOST_KHR = -1000000000;

    // Structure types
    public const uint VK_STRUCTURE_TYPE_APPLICATION_INFO = 0;
    public const uint VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1;
    public const uint VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO = 2;
    public const uint VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO = 3;
    public const uint VK_STRUCTURE_TYPE_SUBMIT_INFO = 4;
    public const uint VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO = 5;
    public const uint VK_STRUCTURE_TYPE_FENCE_CREATE_INFO = 8;
    public const uint VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO = 9;
    public const uint VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO = 12;
    public const uint VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO = 14;
    public const uint VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO = 15;
    public const uint VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO = 16;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO = 18;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO = 19;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO = 20;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO = 22;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO = 23;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO = 24;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO = 25;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO = 26;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO = 27;
    public const uint VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO = 30;
    public const uint VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO = 31;
    public const uint VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO = 32;
    public const uint VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO = 33;
    public const uint VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO = 34;
    public const uint VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET = 35;
    public const uint VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO = 37;
    public const uint VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO = 38;
    public const uint VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO = 39;
    public const uint VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO = 40;
    public const uint VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO = 42;
    public const uint VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO = 43;
    public const uint VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER = 44;
    public const uint VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER = 45;
    public const uint VK_STRUCTURE_TYPE_MEMORY_BARRIER = 46;
    public const uint VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO = 28;
    public const uint VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO = 29;
    public const uint VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR = 1000001000;
    public const uint VK_STRUCTURE_TYPE_PRESENT_INFO_KHR = 1000001001;
    public const uint VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR = 1000004000;
    public const uint VK_STRUCTURE_TYPE_WAYLAND_SURFACE_CREATE_INFO_KHR = 1000006000;
    public const uint VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR = 1000009000;
    public const uint VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT = 1000217000;
    public const uint VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2 = 1000059000;

    // Queue flags
    public const uint VK_QUEUE_GRAPHICS_BIT = 0x00000001;
    public const uint VK_QUEUE_COMPUTE_BIT = 0x00000002;
    public const uint VK_QUEUE_TRANSFER_BIT = 0x00000004;

    // Memory property flags
    public const uint VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT = 0x01;
    public const uint VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT = 0x02;
    public const uint VK_MEMORY_PROPERTY_HOST_COHERENT_BIT = 0x04;

    // Buffer usage flags
    public const uint VK_BUFFER_USAGE_TRANSFER_SRC_BIT = 0x0001;
    public const uint VK_BUFFER_USAGE_TRANSFER_DST_BIT = 0x0002;
    public const uint VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT = 0x0010;
    public const uint VK_BUFFER_USAGE_STORAGE_BUFFER_BIT = 0x0020;
    public const uint VK_BUFFER_USAGE_INDEX_BUFFER_BIT = 0x0040;
    public const uint VK_BUFFER_USAGE_VERTEX_BUFFER_BIT = 0x0080;
    public const uint VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT = 0x0100;

    // Image usage flags
    public const uint VK_IMAGE_USAGE_TRANSFER_SRC_BIT = 0x01;
    public const uint VK_IMAGE_USAGE_TRANSFER_DST_BIT = 0x02;
    public const uint VK_IMAGE_USAGE_SAMPLED_BIT = 0x04;
    public const uint VK_IMAGE_USAGE_STORAGE_BIT = 0x08;
    public const uint VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT = 0x10;
    public const uint VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT = 0x20;

    // Image layouts
    public const uint VK_IMAGE_LAYOUT_UNDEFINED = 0;
    public const uint VK_IMAGE_LAYOUT_GENERAL = 1;
    public const uint VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL = 2;
    public const uint VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL = 3;
    public const uint VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL = 5;
    public const uint VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL = 6;
    public const uint VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL = 7;
    public const uint VK_IMAGE_LAYOUT_PRESENT_SRC_KHR = 1000001002;

    // Image aspects
    public const uint VK_IMAGE_ASPECT_COLOR_BIT = 0x01;
    public const uint VK_IMAGE_ASPECT_DEPTH_BIT = 0x02;
    public const uint VK_IMAGE_ASPECT_STENCIL_BIT = 0x04;

    // Pipeline stage flags
    public const uint VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT = 0x00000001;
    public const uint VK_PIPELINE_STAGE_VERTEX_INPUT_BIT = 0x00000004;
    public const uint VK_PIPELINE_STAGE_VERTEX_SHADER_BIT = 0x00000008;
    public const uint VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT = 0x00000080;
    public const uint VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT = 0x00000100;
    public const uint VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT = 0x00000200;
    public const uint VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT = 0x00000400;
    public const uint VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT = 0x00000800;
    public const uint VK_PIPELINE_STAGE_TRANSFER_BIT = 0x00001000;
    public const uint VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT = 0x00002000;
    public const uint VK_PIPELINE_STAGE_ALL_COMMANDS_BIT = 0x00010000;

    // Access flags
    public const uint VK_ACCESS_INDIRECT_COMMAND_READ_BIT = 0x01;
    public const uint VK_ACCESS_INDEX_READ_BIT = 0x02;
    public const uint VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT = 0x04;
    public const uint VK_ACCESS_UNIFORM_READ_BIT = 0x08;
    public const uint VK_ACCESS_SHADER_READ_BIT = 0x20;
    public const uint VK_ACCESS_SHADER_WRITE_BIT = 0x40;
    public const uint VK_ACCESS_COLOR_ATTACHMENT_READ_BIT = 0x80;
    public const uint VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT = 0x100;
    public const uint VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT = 0x200;
    public const uint VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT = 0x400;
    public const uint VK_ACCESS_TRANSFER_READ_BIT = 0x800;
    public const uint VK_ACCESS_TRANSFER_WRITE_BIT = 0x1000;
    public const uint VK_ACCESS_HOST_READ_BIT = 0x2000;
    public const uint VK_ACCESS_HOST_WRITE_BIT = 0x4000;
    public const uint VK_ACCESS_MEMORY_READ_BIT = 0x8000;
    public const uint VK_ACCESS_MEMORY_WRITE_BIT = 0x10000;

    // Formats (commonly used)
    public const uint VK_FORMAT_UNDEFINED = 0;
    public const uint VK_FORMAT_R8_UNORM = 9;
    public const uint VK_FORMAT_R8G8B8A8_UNORM = 37;
    public const uint VK_FORMAT_R8G8B8A8_SRGB = 43;
    public const uint VK_FORMAT_B8G8R8A8_UNORM = 44;
    public const uint VK_FORMAT_B8G8R8A8_SRGB = 50;
    public const uint VK_FORMAT_R16G16B16A16_SFLOAT = 97;
    public const uint VK_FORMAT_R32_SFLOAT = 100;
    public const uint VK_FORMAT_R32G32_SFLOAT = 103;
    public const uint VK_FORMAT_R32G32B32_SFLOAT = 106;
    public const uint VK_FORMAT_R32G32B32A32_SFLOAT = 109;
    public const uint VK_FORMAT_D32_SFLOAT = 126;
    public const uint VK_FORMAT_D24_UNORM_S8_UINT = 129;
    public const uint VK_FORMAT_BC1_RGB_UNORM_BLOCK = 131;
    public const uint VK_FORMAT_BC3_UNORM_BLOCK = 137;
    public const uint VK_FORMAT_BC7_UNORM_BLOCK = 145;

    // Attachment load/store ops
    public const uint VK_ATTACHMENT_LOAD_OP_LOAD = 0;
    public const uint VK_ATTACHMENT_LOAD_OP_CLEAR = 1;
    public const uint VK_ATTACHMENT_LOAD_OP_DONT_CARE = 2;
    public const uint VK_ATTACHMENT_STORE_OP_STORE = 0;
    public const uint VK_ATTACHMENT_STORE_OP_DONT_CARE = 1;

    // Present modes
    public const uint VK_PRESENT_MODE_IMMEDIATE_KHR = 0;
    public const uint VK_PRESENT_MODE_MAILBOX_KHR = 1;
    public const uint VK_PRESENT_MODE_FIFO_KHR = 2;

    // Color spaces
    public const uint VK_COLOR_SPACE_SRGB_NONLINEAR_KHR = 0;

    // Composite alpha
    public const uint VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR = 0x01;

    // Fence flags
    public const uint VK_FENCE_CREATE_SIGNALED_BIT = 0x01;

    // Command pool flags
    public const uint VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT = 0x02;

    // Command buffer levels
    public const uint VK_COMMAND_BUFFER_LEVEL_PRIMARY = 0;

    // Subpass
    public const uint VK_SUBPASS_EXTERNAL = ~0u;
    public const uint VK_PIPELINE_BIND_POINT_GRAPHICS = 0;
    public const uint VK_PIPELINE_BIND_POINT_COMPUTE = 1;

    // Descriptor types
    public const uint VK_DESCRIPTOR_TYPE_SAMPLER = 0;
    public const uint VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER = 1;
    public const uint VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE = 2;
    public const uint VK_DESCRIPTOR_TYPE_STORAGE_IMAGE = 3;
    public const uint VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER = 6;
    public const uint VK_DESCRIPTOR_TYPE_STORAGE_BUFFER = 7;
    public const uint VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC = 8;
    public const uint VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC = 9;

    // Shader stage flags (for descriptor bindings)
    public const uint VK_SHADER_STAGE_VERTEX_BIT = 0x01;
    public const uint VK_SHADER_STAGE_FRAGMENT_BIT = 0x10;
    public const uint VK_SHADER_STAGE_COMPUTE_BIT = 0x20;
    public const uint VK_SHADER_STAGE_ALL_GRAPHICS = 0x1F;
    public const uint VK_SHADER_STAGE_ALL = 0x7FFFFFFF;

    // Image types
    public const uint VK_IMAGE_TYPE_2D = 1;
    public const uint VK_IMAGE_VIEW_TYPE_2D = 1;
    public const uint VK_SAMPLE_COUNT_1_BIT = 1;
    public const uint VK_IMAGE_TILING_OPTIMAL = 0;
    public const uint VK_IMAGE_TILING_LINEAR = 1;
    public const uint VK_SHARING_MODE_EXCLUSIVE = 0;

    // Misc
    public const ulong VK_WHOLE_SIZE = ~0UL;
    public const uint VK_INDEX_TYPE_UINT16 = 0;
    public const uint VK_INDEX_TYPE_UINT32 = 1;
    public const uint VK_SUBPASS_CONTENTS_INLINE = 0;
    public const uint VK_FILTER_LINEAR = 1;
    public const uint VK_FILTER_NEAREST = 0;
    public const uint VK_SAMPLER_ADDRESS_MODE_REPEAT = 0;
    public const uint VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE = 2;
    public const uint VK_SAMPLER_MIPMAP_MODE_LINEAR = 1;
    public const uint VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK = 3;
    public const uint VK_COMPARE_OP_NEVER = 0;
    public const uint VK_COMPARE_OP_LESS = 1;
    public const uint VK_COMPARE_OP_EQUAL = 2;
    public const uint VK_COMPARE_OP_LESS_OR_EQUAL = 3;
    public const uint VK_COMPARE_OP_GREATER = 4;
    public const uint VK_COMPARE_OP_NOT_EQUAL = 5;
    public const uint VK_COMPARE_OP_GREATER_OR_EQUAL = 6;
    public const uint VK_COMPARE_OP_ALWAYS = 7;

    // Dynamic states
    public const uint VK_DYNAMIC_STATE_VIEWPORT = 0;
    public const uint VK_DYNAMIC_STATE_SCISSOR = 1;
    public const uint VK_DYNAMIC_STATE_LINE_WIDTH = 2;

    // Blend factors/ops
    public const uint VK_BLEND_FACTOR_ZERO = 0;
    public const uint VK_BLEND_FACTOR_ONE = 1;
    public const uint VK_BLEND_FACTOR_SRC_COLOR = 2;
    public const uint VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR = 3;
    public const uint VK_BLEND_FACTOR_DST_COLOR = 4;
    public const uint VK_BLEND_FACTOR_ONE_MINUS_DST_COLOR = 5;
    public const uint VK_BLEND_FACTOR_SRC_ALPHA = 6;
    public const uint VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA = 7;
    public const uint VK_BLEND_FACTOR_DST_ALPHA = 8;
    public const uint VK_BLEND_FACTOR_ONE_MINUS_DST_ALPHA = 9;
    public const uint VK_BLEND_OP_ADD = 0;
    public const uint VK_BLEND_OP_SUBTRACT = 1;
    public const uint VK_BLEND_OP_REVERSE_SUBTRACT = 2;
    public const uint VK_BLEND_OP_MIN = 3;
    public const uint VK_BLEND_OP_MAX = 4;

    // Cull modes
    public const uint VK_CULL_MODE_NONE = 0;
    public const uint VK_CULL_MODE_FRONT_BIT = 1;
    public const uint VK_CULL_MODE_BACK_BIT = 2;
    public const uint VK_FRONT_FACE_COUNTER_CLOCKWISE = 0;
    public const uint VK_FRONT_FACE_CLOCKWISE = 1;
    public const uint VK_POLYGON_MODE_FILL = 0;
    public const uint VK_POLYGON_MODE_LINE = 1;

    // Topology
    public const uint VK_PRIMITIVE_TOPOLOGY_POINT_LIST = 0;
    public const uint VK_PRIMITIVE_TOPOLOGY_LINE_LIST = 1;
    public const uint VK_PRIMITIVE_TOPOLOGY_LINE_STRIP = 2;
    public const uint VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST = 3;
    public const uint VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP = 4;

    // Vertex input rates
    public const uint VK_VERTEX_INPUT_RATE_VERTEX = 0;
    public const uint VK_VERTEX_INPUT_RATE_INSTANCE = 1;

    // ═══════════════════════════════════════════════════════════════════
    //  Structures
    // ═══════════════════════════════════════════════════════════════════

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
    public struct VkDeviceQueueCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint queueFamilyIndex;
        public uint queueCount;
        public IntPtr pQueuePriorities;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceProperties
    {
        public uint apiVersion;
        public uint driverVersion;
        public uint vendorID;
        public uint deviceID;
        public uint deviceType; // VkPhysicalDeviceType
        public fixed byte deviceName[256];
        public fixed byte pipelineCacheUUID[16];
        public VkPhysicalDeviceLimits limits;
        public VkPhysicalDeviceSparseProperties sparseProperties;
    }

    [StructLayout(LayoutKind.Sequential, Size = 504)]
    public struct VkPhysicalDeviceLimits { }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceSparseProperties
    {
        public uint residencyStandard2DBlockShape;
        public uint residencyStandard2DMultisampleBlockShape;
        public uint residencyStandard3DBlockShape;
        public uint residencyAlignedMipSize;
        public uint residencyNonResidentStrict;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkQueueFamilyProperties
    {
        public uint queueFlags;
        public uint queueCount;
        public uint timestampValidBits;
        public VkExtent3D minImageTransferGranularity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkExtent2D
    {
        public uint width;
        public uint height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkExtent3D
    {
        public uint width;
        public uint height;
        public uint depth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceMemoryProperties
    {
        public uint memoryTypeCount;
        public VkMemoryType memoryType_0, memoryType_1, memoryType_2, memoryType_3,
            memoryType_4, memoryType_5, memoryType_6, memoryType_7,
            memoryType_8, memoryType_9, memoryType_10, memoryType_11,
            memoryType_12, memoryType_13, memoryType_14, memoryType_15,
            memoryType_16, memoryType_17, memoryType_18, memoryType_19,
            memoryType_20, memoryType_21, memoryType_22, memoryType_23,
            memoryType_24, memoryType_25, memoryType_26, memoryType_27,
            memoryType_28, memoryType_29, memoryType_30, memoryType_31;
        public uint memoryHeapCount;
        public VkMemoryHeap memoryHeap_0, memoryHeap_1, memoryHeap_2, memoryHeap_3,
            memoryHeap_4, memoryHeap_5, memoryHeap_6, memoryHeap_7,
            memoryHeap_8, memoryHeap_9, memoryHeap_10, memoryHeap_11,
            memoryHeap_12, memoryHeap_13, memoryHeap_14, memoryHeap_15;

        public VkMemoryType GetMemoryType(int index)
        {
            // Access via Unsafe or switch — using switch for safety
            return index switch
            {
                0 => memoryType_0, 1 => memoryType_1, 2 => memoryType_2, 3 => memoryType_3,
                4 => memoryType_4, 5 => memoryType_5, 6 => memoryType_6, 7 => memoryType_7,
                8 => memoryType_8, 9 => memoryType_9, 10 => memoryType_10, 11 => memoryType_11,
                12 => memoryType_12, 13 => memoryType_13, 14 => memoryType_14, 15 => memoryType_15,
                16 => memoryType_16, 17 => memoryType_17, 18 => memoryType_18, 19 => memoryType_19,
                20 => memoryType_20, 21 => memoryType_21, 22 => memoryType_22, 23 => memoryType_23,
                24 => memoryType_24, 25 => memoryType_25, 26 => memoryType_26, 27 => memoryType_27,
                28 => memoryType_28, 29 => memoryType_29, 30 => memoryType_30, 31 => memoryType_31,
                _ => default
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkMemoryType
    {
        public uint propertyFlags;
        public uint heapIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkMemoryHeap
    {
        public ulong size;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkMemoryAllocateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public ulong allocationSize;
        public uint memoryTypeIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkMemoryRequirements
    {
        public ulong size;
        public ulong alignment;
        public uint memoryTypeBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkBufferCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public ulong size;
        public uint usage;
        public uint sharingMode;
        public uint queueFamilyIndexCount;
        public IntPtr pQueueFamilyIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkImageCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint imageType;
        public uint format;
        public VkExtent3D extent;
        public uint mipLevels;
        public uint arrayLayers;
        public uint samples;
        public uint tiling;
        public uint usage;
        public uint sharingMode;
        public uint queueFamilyIndexCount;
        public IntPtr pQueueFamilyIndices;
        public uint initialLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkImageViewCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr image;
        public uint viewType;
        public uint format;
        public VkComponentMapping components;
        public VkImageSubresourceRange subresourceRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkComponentMapping
    {
        public uint r, g, b, a; // VK_COMPONENT_SWIZZLE_IDENTITY = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkImageSubresourceRange
    {
        public uint aspectMask;
        public uint baseMipLevel;
        public uint levelCount;
        public uint baseArrayLayer;
        public uint layerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkShaderModuleCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public nuint codeSize;
        public IntPtr pCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineShaderStageCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint stage;
        public IntPtr module;
        public IntPtr pName;
        public IntPtr pSpecializationInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkVertexInputBindingDescription
    {
        public uint binding;
        public uint stride;
        public uint inputRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkVertexInputAttributeDescription
    {
        public uint location;
        public uint binding;
        public uint format;
        public uint offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineVertexInputStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint vertexBindingDescriptionCount;
        public IntPtr pVertexBindingDescriptions;
        public uint vertexAttributeDescriptionCount;
        public IntPtr pVertexAttributeDescriptions;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineInputAssemblyStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint topology;
        public uint primitiveRestartEnable;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkViewport
    {
        public float x, y, width, height, minDepth, maxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkRect2D
    {
        public int offsetX, offsetY;
        public uint extentWidth, extentHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineViewportStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint viewportCount;
        public IntPtr pViewports;
        public uint scissorCount;
        public IntPtr pScissors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineRasterizationStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint depthClampEnable;
        public uint rasterizerDiscardEnable;
        public uint polygonMode;
        public uint cullMode;
        public uint frontFace;
        public uint depthBiasEnable;
        public float depthBiasConstantFactor;
        public float depthBiasClamp;
        public float depthBiasSlopeFactor;
        public float lineWidth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineMultisampleStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint rasterizationSamples;
        public uint sampleShadingEnable;
        public float minSampleShading;
        public IntPtr pSampleMask;
        public uint alphaToCoverageEnable;
        public uint alphaToOneEnable;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineDepthStencilStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint depthTestEnable;
        public uint depthWriteEnable;
        public uint depthCompareOp;
        public uint depthBoundsTestEnable;
        public uint stencilTestEnable;
        public VkStencilOpState front;
        public VkStencilOpState back;
        public float minDepthBounds;
        public float maxDepthBounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkStencilOpState
    {
        public uint failOp, passOp, depthFailOp, compareOp;
        public uint compareMask, writeMask, reference;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineColorBlendAttachmentState
    {
        public uint blendEnable;
        public uint srcColorBlendFactor;
        public uint dstColorBlendFactor;
        public uint colorBlendOp;
        public uint srcAlphaBlendFactor;
        public uint dstAlphaBlendFactor;
        public uint alphaBlendOp;
        public uint colorWriteMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineColorBlendStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint logicOpEnable;
        public uint logicOp;
        public uint attachmentCount;
        public IntPtr pAttachments;
        public float blendConstant0, blendConstant1, blendConstant2, blendConstant3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineDynamicStateCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint dynamicStateCount;
        public IntPtr pDynamicStates;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPipelineLayoutCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint setLayoutCount;
        public IntPtr pSetLayouts;
        public uint pushConstantRangeCount;
        public IntPtr pPushConstantRanges;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkGraphicsPipelineCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint stageCount;
        public IntPtr pStages;
        public IntPtr pVertexInputState;
        public IntPtr pInputAssemblyState;
        public IntPtr pTessellationState;
        public IntPtr pViewportState;
        public IntPtr pRasterizationState;
        public IntPtr pMultisampleState;
        public IntPtr pDepthStencilState;
        public IntPtr pColorBlendState;
        public IntPtr pDynamicState;
        public IntPtr layout;
        public IntPtr renderPass;
        public uint subpass;
        public IntPtr basePipelineHandle;
        public int basePipelineIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkComputePipelineCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public VkPipelineShaderStageCreateInfo stage;
        public IntPtr layout;
        public IntPtr basePipelineHandle;
        public int basePipelineIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkAttachmentDescription
    {
        public uint flags;
        public uint format;
        public uint samples;
        public uint loadOp;
        public uint storeOp;
        public uint stencilLoadOp;
        public uint stencilStoreOp;
        public uint initialLayout;
        public uint finalLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkAttachmentReference
    {
        public uint attachment;
        public uint layout;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSubpassDescription
    {
        public uint flags;
        public uint pipelineBindPoint;
        public uint inputAttachmentCount;
        public IntPtr pInputAttachments;
        public uint colorAttachmentCount;
        public IntPtr pColorAttachments;
        public IntPtr pResolveAttachments;
        public IntPtr pDepthStencilAttachment;
        public uint preserveAttachmentCount;
        public IntPtr pPreserveAttachments;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSubpassDependency
    {
        public uint srcSubpass;
        public uint dstSubpass;
        public uint srcStageMask;
        public uint dstStageMask;
        public uint srcAccessMask;
        public uint dstAccessMask;
        public uint dependencyFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkRenderPassCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint attachmentCount;
        public IntPtr pAttachments;
        public uint subpassCount;
        public IntPtr pSubpasses;
        public uint dependencyCount;
        public IntPtr pDependencies;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkFramebufferCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr renderPass;
        public uint attachmentCount;
        public IntPtr pAttachments;
        public uint width;
        public uint height;
        public uint layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkCommandPoolCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint queueFamilyIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkCommandBufferAllocateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public IntPtr commandPool;
        public uint level;
        public uint commandBufferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkCommandBufferBeginInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr pInheritanceInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkClearValue
    {
        public float r, g, b, a; // Union — use float for both color and depth
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkRenderPassBeginInfo
    {
        public uint sType;
        public IntPtr pNext;
        public IntPtr renderPass;
        public IntPtr framebuffer;
        public VkRect2D renderArea;
        public uint clearValueCount;
        public IntPtr pClearValues;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSubmitInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint waitSemaphoreCount;
        public IntPtr pWaitSemaphores;
        public IntPtr pWaitDstStageMask;
        public uint commandBufferCount;
        public IntPtr pCommandBuffers;
        public uint signalSemaphoreCount;
        public IntPtr pSignalSemaphores;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkFenceCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSemaphoreCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkImageMemoryBarrier
    {
        public uint sType;
        public IntPtr pNext;
        public uint srcAccessMask;
        public uint dstAccessMask;
        public uint oldLayout;
        public uint newLayout;
        public uint srcQueueFamilyIndex;
        public uint dstQueueFamilyIndex;
        public IntPtr image;
        public VkImageSubresourceRange subresourceRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkBufferMemoryBarrier
    {
        public uint sType;
        public IntPtr pNext;
        public uint srcAccessMask;
        public uint dstAccessMask;
        public uint srcQueueFamilyIndex;
        public uint dstQueueFamilyIndex;
        public IntPtr buffer;
        public ulong offset;
        public ulong size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkBufferCopy
    {
        public ulong srcOffset;
        public ulong dstOffset;
        public ulong size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkBufferImageCopy
    {
        public ulong bufferOffset;
        public uint bufferRowLength;
        public uint bufferImageHeight;
        public VkImageSubresourceLayers imageSubresource;
        public int imageOffsetX, imageOffsetY, imageOffsetZ;
        public VkExtent3D imageExtent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkImageSubresourceLayers
    {
        public uint aspectMask;
        public uint mipLevel;
        public uint baseArrayLayer;
        public uint layerCount;
    }

    // ── Swapchain ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSurfaceCapabilitiesKHR
    {
        public uint minImageCount;
        public uint maxImageCount;
        public VkExtent2D currentExtent;
        public VkExtent2D minImageExtent;
        public VkExtent2D maxImageExtent;
        public uint maxImageArrayLayers;
        public uint supportedTransforms;
        public uint currentTransform;
        public uint supportedCompositeAlpha;
        public uint supportedUsageFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSurfaceFormatKHR
    {
        public uint format;
        public uint colorSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSwapchainCreateInfoKHR
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr surface;
        public uint minImageCount;
        public uint imageFormat;
        public uint imageColorSpace;
        public VkExtent2D imageExtent;
        public uint imageArrayLayers;
        public uint imageUsage;
        public uint imageSharingMode;
        public uint queueFamilyIndexCount;
        public IntPtr pQueueFamilyIndices;
        public uint preTransform;
        public uint compositeAlpha;
        public uint presentMode;
        public uint clipped;
        public IntPtr oldSwapchain;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPresentInfoKHR
    {
        public uint sType;
        public IntPtr pNext;
        public uint waitSemaphoreCount;
        public IntPtr pWaitSemaphores;
        public uint swapchainCount;
        public IntPtr pSwapchains;
        public IntPtr pImageIndices;
        public IntPtr pResults;
    }

    // ── Descriptor Sets ──────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorSetLayoutBinding
    {
        public uint binding;
        public uint descriptorType;
        public uint descriptorCount;
        public uint stageFlags;
        public IntPtr pImmutableSamplers;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorSetLayoutCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint bindingCount;
        public IntPtr pBindings;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorPoolSize
    {
        public uint type;
        public uint descriptorCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorPoolCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint maxSets;
        public uint poolSizeCount;
        public IntPtr pPoolSizes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorSetAllocateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public IntPtr descriptorPool;
        public uint descriptorSetCount;
        public IntPtr pSetLayouts;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorBufferInfo
    {
        public IntPtr buffer;
        public ulong offset;
        public ulong range;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDescriptorImageInfo
    {
        public IntPtr sampler;
        public IntPtr imageView;
        public uint imageLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkWriteDescriptorSet
    {
        public uint sType;
        public IntPtr pNext;
        public IntPtr dstSet;
        public uint dstBinding;
        public uint dstArrayElement;
        public uint descriptorCount;
        public uint descriptorType;
        public IntPtr pImageInfo;
        public IntPtr pBufferInfo;
        public IntPtr pTexelBufferView;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkSamplerCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public uint magFilter;
        public uint minFilter;
        public uint mipmapMode;
        public uint addressModeU;
        public uint addressModeV;
        public uint addressModeW;
        public float mipLodBias;
        public uint anisotropyEnable;
        public float maxAnisotropy;
        public uint compareEnable;
        public uint compareOp;
        public float minLod;
        public float maxLod;
        public uint borderColor;
        public uint unnormalizedCoordinates;
    }

    // ── Platform Surface Structs ─────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct VkXlibSurfaceCreateInfoKHR
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr dpy;
        public ulong window;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkWaylandSurfaceCreateInfoKHR
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr display;
        public IntPtr surface;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkWin32SurfaceCreateInfoKHR
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr hinstance;
        public IntPtr hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkMetalSurfaceCreateInfoEXT
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr pLayer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceFeatures
    {
        // 55 bools — only the commonly used ones are named
        public uint robustBufferAccess;
        public uint fullDrawIndexUint32;
        public uint imageCubeArray;
        public uint independentBlend;
        public uint geometryShader;
        public uint tessellationShader;
        public uint sampleRateShading;
        public uint dualSrcBlend;
        public uint logicOp;
        public uint multiDrawIndirect;
        public uint drawIndirectFirstInstance;
        public uint depthClamp;
        public uint depthBiasClamp;
        public uint fillModeNonSolid;
        public uint depthBounds;
        public uint wideLines;
        public uint largePoints;
        public uint alphaToOne;
        public uint multiViewport;
        public uint samplerAnisotropy;
        // ... remaining 35 features
        public uint textureCompressionETC2;
        public uint textureCompressionASTC_LDR;
        public uint textureCompressionBC;
        public uint occlusionQueryPrecise;
        public uint pipelineStatisticsQuery;
        public uint vertexPipelineStoresAndAtomics;
        public uint fragmentStoresAndAtomics;
        public uint shaderTessellationAndGeometryPointSize;
        public uint shaderImageGatherExtended;
        public uint shaderStorageImageExtendedFormats;
        public uint shaderStorageImageMultisample;
        public uint shaderStorageImageReadWithoutFormat;
        public uint shaderStorageImageWriteWithoutFormat;
        public uint shaderUniformBufferArrayDynamicIndexing;
        public uint shaderSampledImageArrayDynamicIndexing;
        public uint shaderStorageBufferArrayDynamicIndexing;
        public uint shaderStorageImageArrayDynamicIndexing;
        public uint shaderClipDistance;
        public uint shaderCullDistance;
        public uint shaderFloat64;
        public uint shaderInt64;
        public uint shaderInt16;
        public uint shaderResourceResidency;
        public uint shaderResourceMinLod;
        public uint sparseBinding;
        public uint sparseResidencyBuffer;
        public uint sparseResidencyImage2D;
        public uint sparseResidencyImage3D;
        public uint sparseResidency2Samples;
        public uint sparseResidency4Samples;
        public uint sparseResidency8Samples;
        public uint sparseResidency16Samples;
        public uint sparseResidencyAliased;
        public uint variableMultisampleRate;
        public uint inheritedQueries;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Function Delegates & Pointers
    // ═══════════════════════════════════════════════════════════════════

    // Bootstrap
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr PFN_vkGetInstanceProcAddr(IntPtr instance, [MarshalAs(UnmanagedType.LPStr)] string pName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr PFN_vkGetDeviceProcAddr(IntPtr device, [MarshalAs(UnmanagedType.LPStr)] string pName);

    // Instance
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateInstance(ref VkInstanceCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pInstance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyInstance(IntPtr instance, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkEnumeratePhysicalDevices(IntPtr instance, ref uint pPhysicalDeviceCount, IntPtr pPhysicalDevices);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetPhysicalDeviceProperties(IntPtr physicalDevice, out VkPhysicalDeviceProperties pProperties);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetPhysicalDeviceFeatures(IntPtr physicalDevice, out VkPhysicalDeviceFeatures pFeatures);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetPhysicalDeviceMemoryProperties(IntPtr physicalDevice, out VkPhysicalDeviceMemoryProperties pMemoryProperties);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetPhysicalDeviceQueueFamilyProperties(IntPtr physicalDevice, ref uint pQueueFamilyPropertyCount, IntPtr pQueueFamilyProperties);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkEnumerateInstanceExtensionProperties(IntPtr pLayerName, ref uint pPropertyCount, IntPtr pProperties);

    // Device
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateDevice(IntPtr physicalDevice, ref VkDeviceCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pDevice);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyDevice(IntPtr device, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetDeviceQueue(IntPtr device, uint queueFamilyIndex, uint queueIndex, out IntPtr pQueue);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkDeviceWaitIdle(IntPtr device);

    // Memory
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkAllocateMemory(IntPtr device, ref VkMemoryAllocateInfo pAllocateInfo, IntPtr pAllocator, out IntPtr pMemory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkFreeMemory(IntPtr device, IntPtr memory, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkMapMemory(IntPtr device, IntPtr memory, ulong offset, ulong size, uint flags, out IntPtr ppData);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkUnmapMemory(IntPtr device, IntPtr memory);

    // Buffer
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateBuffer(IntPtr device, ref VkBufferCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pBuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyBuffer(IntPtr device, IntPtr buffer, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetBufferMemoryRequirements(IntPtr device, IntPtr buffer, out VkMemoryRequirements pMemoryRequirements);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkBindBufferMemory(IntPtr device, IntPtr buffer, IntPtr memory, ulong memoryOffset);

    // Image
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateImage(IntPtr device, ref VkImageCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pImage);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyImage(IntPtr device, IntPtr image, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkGetImageMemoryRequirements(IntPtr device, IntPtr image, out VkMemoryRequirements pMemoryRequirements);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkBindImageMemory(IntPtr device, IntPtr image, IntPtr memory, ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateImageView(IntPtr device, ref VkImageViewCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pView);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyImageView(IntPtr device, IntPtr imageView, IntPtr pAllocator);

    // Sampler
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateSampler(IntPtr device, ref VkSamplerCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pSampler);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroySampler(IntPtr device, IntPtr sampler, IntPtr pAllocator);

    // Shader Module
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateShaderModule(IntPtr device, ref VkShaderModuleCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pShaderModule);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyShaderModule(IntPtr device, IntPtr shaderModule, IntPtr pAllocator);

    // Pipeline
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateGraphicsPipelines(IntPtr device, IntPtr pipelineCache, uint createInfoCount, IntPtr pCreateInfos, IntPtr pAllocator, IntPtr pPipelines);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateComputePipelines(IntPtr device, IntPtr pipelineCache, uint createInfoCount, IntPtr pCreateInfos, IntPtr pAllocator, IntPtr pPipelines);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyPipeline(IntPtr device, IntPtr pipeline, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreatePipelineLayout(IntPtr device, ref VkPipelineLayoutCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pPipelineLayout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyPipelineLayout(IntPtr device, IntPtr pipelineLayout, IntPtr pAllocator);

    // Render Pass
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateRenderPass(IntPtr device, ref VkRenderPassCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pRenderPass);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyRenderPass(IntPtr device, IntPtr renderPass, IntPtr pAllocator);

    // Framebuffer
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateFramebuffer(IntPtr device, ref VkFramebufferCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pFramebuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyFramebuffer(IntPtr device, IntPtr framebuffer, IntPtr pAllocator);

    // Descriptor
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateDescriptorSetLayout(IntPtr device, ref VkDescriptorSetLayoutCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pSetLayout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyDescriptorSetLayout(IntPtr device, IntPtr descriptorSetLayout, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateDescriptorPool(IntPtr device, ref VkDescriptorPoolCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pDescriptorPool);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyDescriptorPool(IntPtr device, IntPtr descriptorPool, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkAllocateDescriptorSets(IntPtr device, ref VkDescriptorSetAllocateInfo pAllocateInfo, IntPtr pDescriptorSets);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkUpdateDescriptorSets(IntPtr device, uint descriptorWriteCount, IntPtr pDescriptorWrites, uint descriptorCopyCount, IntPtr pDescriptorCopies);

    // Command Pool / Buffer
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateCommandPool(IntPtr device, ref VkCommandPoolCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pCommandPool);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyCommandPool(IntPtr device, IntPtr commandPool, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkAllocateCommandBuffers(IntPtr device, ref VkCommandBufferAllocateInfo pAllocateInfo, IntPtr pCommandBuffers);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkBeginCommandBuffer(IntPtr commandBuffer, ref VkCommandBufferBeginInfo pBeginInfo);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkEndCommandBuffer(IntPtr commandBuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkResetCommandBuffer(IntPtr commandBuffer, uint flags);

    // Recording
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdBeginRenderPass(IntPtr commandBuffer, ref VkRenderPassBeginInfo pRenderPassBegin, uint contents);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdEndRenderPass(IntPtr commandBuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdBindPipeline(IntPtr commandBuffer, uint pipelineBindPoint, IntPtr pipeline);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdSetViewport(IntPtr commandBuffer, uint firstViewport, uint viewportCount, ref VkViewport pViewports);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdSetScissor(IntPtr commandBuffer, uint firstScissor, uint scissorCount, ref VkRect2D pScissors);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdBindVertexBuffers(IntPtr commandBuffer, uint firstBinding, uint bindingCount, IntPtr pBuffers, IntPtr pOffsets);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdBindIndexBuffer(IntPtr commandBuffer, IntPtr buffer, ulong offset, uint indexType);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdBindDescriptorSets(IntPtr commandBuffer, uint pipelineBindPoint, IntPtr layout, uint firstSet, uint descriptorSetCount, IntPtr pDescriptorSets, uint dynamicOffsetCount, IntPtr pDynamicOffsets);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdDraw(IntPtr commandBuffer, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdDrawIndexed(IntPtr commandBuffer, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdDrawIndirect(IntPtr commandBuffer, IntPtr buffer, ulong offset, uint drawCount, uint stride);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdDrawIndexedIndirect(IntPtr commandBuffer, IntPtr buffer, ulong offset, uint drawCount, uint stride);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdDispatch(IntPtr commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdDispatchIndirect(IntPtr commandBuffer, IntPtr buffer, ulong offset);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdPipelineBarrier(IntPtr commandBuffer, uint srcStageMask, uint dstStageMask, uint dependencyFlags, uint memoryBarrierCount, IntPtr pMemoryBarriers, uint bufferMemoryBarrierCount, IntPtr pBufferMemoryBarriers, uint imageMemoryBarrierCount, IntPtr pImageMemoryBarriers);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdCopyBuffer(IntPtr commandBuffer, IntPtr srcBuffer, IntPtr dstBuffer, uint regionCount, IntPtr pRegions);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkCmdCopyBufferToImage(IntPtr commandBuffer, IntPtr srcBuffer, IntPtr dstImage, uint dstImageLayout, uint regionCount, IntPtr pRegions);

    // Synchronization
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateSemaphore(IntPtr device, ref VkSemaphoreCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pSemaphore);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroySemaphore(IntPtr device, IntPtr semaphore, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateFence(IntPtr device, ref VkFenceCreateInfo pCreateInfo, IntPtr pAllocator, out IntPtr pFence);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroyFence(IntPtr device, IntPtr fence, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkWaitForFences(IntPtr device, uint fenceCount, IntPtr pFences, uint waitAll, ulong timeout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkResetFences(IntPtr device, uint fenceCount, IntPtr pFences);

    // Submission
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkQueueSubmit(IntPtr queue, uint submitCount, IntPtr pSubmits, IntPtr fence);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkQueueWaitIdle(IntPtr queue);

    // Swapchain (KHR extension)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateSwapchainKHR(IntPtr device, ref VkSwapchainCreateInfoKHR pCreateInfo, IntPtr pAllocator, out IntPtr pSwapchain);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroySwapchainKHR(IntPtr device, IntPtr swapchain, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkGetSwapchainImagesKHR(IntPtr device, IntPtr swapchain, ref uint pSwapchainImageCount, IntPtr pSwapchainImages);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkAcquireNextImageKHR(IntPtr device, IntPtr swapchain, ulong timeout, IntPtr semaphore, IntPtr fence, out uint pImageIndex);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkQueuePresentKHR(IntPtr queue, ref VkPresentInfoKHR pPresentInfo);

    // Surface
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PFN_vkDestroySurfaceKHR(IntPtr instance, IntPtr surface, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkGetPhysicalDeviceSurfaceSupportKHR(IntPtr physicalDevice, uint queueFamilyIndex, IntPtr surface, out uint pSupported);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR(IntPtr physicalDevice, IntPtr surface, out VkSurfaceCapabilitiesKHR pSurfaceCapabilities);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkGetPhysicalDeviceSurfaceFormatsKHR(IntPtr physicalDevice, IntPtr surface, ref uint pSurfaceFormatCount, IntPtr pSurfaceFormats);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkGetPhysicalDeviceSurfacePresentModesKHR(IntPtr physicalDevice, IntPtr surface, ref uint pPresentModeCount, IntPtr pPresentModes);

    // Platform surface creation
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateXlibSurfaceKHR(IntPtr instance, ref VkXlibSurfaceCreateInfoKHR pCreateInfo, IntPtr pAllocator, out IntPtr pSurface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateWaylandSurfaceKHR(IntPtr instance, ref VkWaylandSurfaceCreateInfoKHR pCreateInfo, IntPtr pAllocator, out IntPtr pSurface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateWin32SurfaceKHR(IntPtr instance, ref VkWin32SurfaceCreateInfoKHR pCreateInfo, IntPtr pAllocator, out IntPtr pSurface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PFN_vkCreateMetalSurfaceEXT(IntPtr instance, ref VkMetalSurfaceCreateInfoEXT pCreateInfo, IntPtr pAllocator, out IntPtr pSurface);

    // ═══════════════════════════════════════════════════════════════════
    //  Static Function Pointers (loaded once)
    // ═══════════════════════════════════════════════════════════════════

    // Bootstrap
    public static PFN_vkGetInstanceProcAddr _vkGetInstanceProcAddr = null!;
    public static PFN_vkGetDeviceProcAddr _vkGetDeviceProcAddr = null!;

    // Global (no instance needed)
    public static PFN_vkCreateInstance vkCreateInstance = null!;
    public static PFN_vkEnumerateInstanceExtensionProperties vkEnumerateInstanceExtensionProperties = null!;

    // Instance-level (loaded after vkCreateInstance)
    public static PFN_vkDestroyInstance vkDestroyInstance = null!;
    public static PFN_vkEnumeratePhysicalDevices vkEnumeratePhysicalDevices = null!;
    public static PFN_vkGetPhysicalDeviceProperties vkGetPhysicalDeviceProperties = null!;
    public static PFN_vkGetPhysicalDeviceFeatures vkGetPhysicalDeviceFeatures = null!;
    public static PFN_vkGetPhysicalDeviceMemoryProperties vkGetPhysicalDeviceMemoryProperties = null!;
    public static PFN_vkGetPhysicalDeviceQueueFamilyProperties vkGetPhysicalDeviceQueueFamilyProperties = null!;
    public static PFN_vkCreateDevice vkCreateDevice = null!;
    public static PFN_vkDestroySurfaceKHR vkDestroySurfaceKHR = null!;
    public static PFN_vkGetPhysicalDeviceSurfaceSupportKHR vkGetPhysicalDeviceSurfaceSupportKHR = null!;
    public static PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR vkGetPhysicalDeviceSurfaceCapabilitiesKHR = null!;
    public static PFN_vkGetPhysicalDeviceSurfaceFormatsKHR vkGetPhysicalDeviceSurfaceFormatsKHR = null!;
    public static PFN_vkGetPhysicalDeviceSurfacePresentModesKHR vkGetPhysicalDeviceSurfacePresentModesKHR = null!;

    // Platform surface (loaded from instance)
    public static PFN_vkCreateXlibSurfaceKHR? vkCreateXlibSurfaceKHR;
    public static PFN_vkCreateWaylandSurfaceKHR? vkCreateWaylandSurfaceKHR;
    public static PFN_vkCreateWin32SurfaceKHR? vkCreateWin32SurfaceKHR;
    public static PFN_vkCreateMetalSurfaceEXT? vkCreateMetalSurfaceEXT;

    // Device-level (loaded after vkCreateDevice)
    public static PFN_vkDestroyDevice vkDestroyDevice = null!;
    public static PFN_vkGetDeviceQueue vkGetDeviceQueue = null!;
    public static PFN_vkDeviceWaitIdle vkDeviceWaitIdle = null!;
    public static PFN_vkAllocateMemory vkAllocateMemory = null!;
    public static PFN_vkFreeMemory vkFreeMemory = null!;
    public static PFN_vkMapMemory vkMapMemory = null!;
    public static PFN_vkUnmapMemory vkUnmapMemory = null!;
    public static PFN_vkCreateBuffer vkCreateBuffer = null!;
    public static PFN_vkDestroyBuffer vkDestroyBuffer = null!;
    public static PFN_vkGetBufferMemoryRequirements vkGetBufferMemoryRequirements = null!;
    public static PFN_vkBindBufferMemory vkBindBufferMemory = null!;
    public static PFN_vkCreateImage vkCreateImage = null!;
    public static PFN_vkDestroyImage vkDestroyImage = null!;
    public static PFN_vkGetImageMemoryRequirements vkGetImageMemoryRequirements = null!;
    public static PFN_vkBindImageMemory vkBindImageMemory = null!;
    public static PFN_vkCreateImageView vkCreateImageView = null!;
    public static PFN_vkDestroyImageView vkDestroyImageView = null!;
    public static PFN_vkCreateSampler vkCreateSampler = null!;
    public static PFN_vkDestroySampler vkDestroySampler = null!;
    public static PFN_vkCreateShaderModule vkCreateShaderModule = null!;
    public static PFN_vkDestroyShaderModule vkDestroyShaderModule = null!;
    public static PFN_vkCreateGraphicsPipelines vkCreateGraphicsPipelines = null!;
    public static PFN_vkCreateComputePipelines vkCreateComputePipelines = null!;
    public static PFN_vkDestroyPipeline vkDestroyPipeline = null!;
    public static PFN_vkCreatePipelineLayout vkCreatePipelineLayout = null!;
    public static PFN_vkDestroyPipelineLayout vkDestroyPipelineLayout = null!;
    public static PFN_vkCreateRenderPass vkCreateRenderPass = null!;
    public static PFN_vkDestroyRenderPass vkDestroyRenderPass = null!;
    public static PFN_vkCreateFramebuffer vkCreateFramebuffer = null!;
    public static PFN_vkDestroyFramebuffer vkDestroyFramebuffer = null!;
    public static PFN_vkCreateDescriptorSetLayout vkCreateDescriptorSetLayout = null!;
    public static PFN_vkDestroyDescriptorSetLayout vkDestroyDescriptorSetLayout = null!;
    public static PFN_vkCreateDescriptorPool vkCreateDescriptorPool = null!;
    public static PFN_vkDestroyDescriptorPool vkDestroyDescriptorPool = null!;
    public static PFN_vkAllocateDescriptorSets vkAllocateDescriptorSets = null!;
    public static PFN_vkUpdateDescriptorSets vkUpdateDescriptorSets = null!;
    public static PFN_vkCreateCommandPool vkCreateCommandPool = null!;
    public static PFN_vkDestroyCommandPool vkDestroyCommandPool = null!;
    public static PFN_vkAllocateCommandBuffers vkAllocateCommandBuffers = null!;
    public static PFN_vkBeginCommandBuffer vkBeginCommandBuffer = null!;
    public static PFN_vkEndCommandBuffer vkEndCommandBuffer = null!;
    public static PFN_vkResetCommandBuffer vkResetCommandBuffer = null!;
    public static PFN_vkCmdBeginRenderPass vkCmdBeginRenderPass = null!;
    public static PFN_vkCmdEndRenderPass vkCmdEndRenderPass = null!;
    public static PFN_vkCmdBindPipeline vkCmdBindPipeline = null!;
    public static PFN_vkCmdSetViewport vkCmdSetViewport = null!;
    public static PFN_vkCmdSetScissor vkCmdSetScissor = null!;
    public static PFN_vkCmdBindVertexBuffers vkCmdBindVertexBuffers = null!;
    public static PFN_vkCmdBindIndexBuffer vkCmdBindIndexBuffer = null!;
    public static PFN_vkCmdBindDescriptorSets vkCmdBindDescriptorSets = null!;
    public static PFN_vkCmdDraw vkCmdDraw = null!;
    public static PFN_vkCmdDrawIndexed vkCmdDrawIndexed = null!;
    public static PFN_vkCmdDrawIndirect vkCmdDrawIndirect = null!;
    public static PFN_vkCmdDrawIndexedIndirect vkCmdDrawIndexedIndirect = null!;
    public static PFN_vkCmdDispatch vkCmdDispatch = null!;
    public static PFN_vkCmdDispatchIndirect vkCmdDispatchIndirect = null!;
    public static PFN_vkCmdPipelineBarrier vkCmdPipelineBarrier = null!;
    public static PFN_vkCmdCopyBuffer vkCmdCopyBuffer = null!;
    public static PFN_vkCmdCopyBufferToImage vkCmdCopyBufferToImage = null!;
    public static PFN_vkCreateSemaphore vkCreateSemaphore = null!;
    public static PFN_vkDestroySemaphore vkDestroySemaphore = null!;
    public static PFN_vkCreateFence vkCreateFence = null!;
    public static PFN_vkDestroyFence vkDestroyFence = null!;
    public static PFN_vkWaitForFences vkWaitForFences = null!;
    public static PFN_vkResetFences vkResetFences = null!;
    public static PFN_vkQueueSubmit vkQueueSubmit = null!;
    public static PFN_vkQueueWaitIdle vkQueueWaitIdle = null!;
    public static PFN_vkCreateSwapchainKHR vkCreateSwapchainKHR = null!;
    public static PFN_vkDestroySwapchainKHR vkDestroySwapchainKHR = null!;
    public static PFN_vkGetSwapchainImagesKHR vkGetSwapchainImagesKHR = null!;
    public static PFN_vkAcquireNextImageKHR vkAcquireNextImageKHR = null!;
    public static PFN_vkQueuePresentKHR vkQueuePresentKHR = null!;

    // ═══════════════════════════════════════════════════════════════════
    //  Loader
    // ═══════════════════════════════════════════════════════════════════

    private static void LoadCoreFunctions()
    {
        _vkGetInstanceProcAddr = LoadFunc<PFN_vkGetInstanceProcAddr>("vkGetInstanceProcAddr");
        _vkGetDeviceProcAddr = LoadFunc<PFN_vkGetDeviceProcAddr>("vkGetDeviceProcAddr");
        vkCreateInstance = LoadFunc<PFN_vkCreateInstance>("vkCreateInstance");
        vkEnumerateInstanceExtensionProperties = LoadFunc<PFN_vkEnumerateInstanceExtensionProperties>("vkEnumerateInstanceExtensionProperties");

        Console.WriteLine("[Vulkan] Core functions loaded via NativeLibrary");
    }

    /// <summary>
    /// Load instance-level functions after vkCreateInstance succeeds.
    /// </summary>
    public static void LoadInstanceFunctions(IntPtr instance)
    {
        vkDestroyInstance = LoadInstanceFunc<PFN_vkDestroyInstance>(instance, "vkDestroyInstance");
        vkEnumeratePhysicalDevices = LoadInstanceFunc<PFN_vkEnumeratePhysicalDevices>(instance, "vkEnumeratePhysicalDevices");
        vkGetPhysicalDeviceProperties = LoadInstanceFunc<PFN_vkGetPhysicalDeviceProperties>(instance, "vkGetPhysicalDeviceProperties");
        vkGetPhysicalDeviceFeatures = LoadInstanceFunc<PFN_vkGetPhysicalDeviceFeatures>(instance, "vkGetPhysicalDeviceFeatures");
        vkGetPhysicalDeviceMemoryProperties = LoadInstanceFunc<PFN_vkGetPhysicalDeviceMemoryProperties>(instance, "vkGetPhysicalDeviceMemoryProperties");
        vkGetPhysicalDeviceQueueFamilyProperties = LoadInstanceFunc<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(instance, "vkGetPhysicalDeviceQueueFamilyProperties");
        vkCreateDevice = LoadInstanceFunc<PFN_vkCreateDevice>(instance, "vkCreateDevice");

        // Surface functions
        vkDestroySurfaceKHR = LoadInstanceFunc<PFN_vkDestroySurfaceKHR>(instance, "vkDestroySurfaceKHR");
        vkGetPhysicalDeviceSurfaceSupportKHR = LoadInstanceFunc<PFN_vkGetPhysicalDeviceSurfaceSupportKHR>(instance, "vkGetPhysicalDeviceSurfaceSupportKHR");
        vkGetPhysicalDeviceSurfaceCapabilitiesKHR = LoadInstanceFunc<PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR>(instance, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        vkGetPhysicalDeviceSurfaceFormatsKHR = LoadInstanceFunc<PFN_vkGetPhysicalDeviceSurfaceFormatsKHR>(instance, "vkGetPhysicalDeviceSurfaceFormatsKHR");
        vkGetPhysicalDeviceSurfacePresentModesKHR = LoadInstanceFunc<PFN_vkGetPhysicalDeviceSurfacePresentModesKHR>(instance, "vkGetPhysicalDeviceSurfacePresentModesKHR");

        // Platform surface extensions (null if not available)
        vkCreateXlibSurfaceKHR = LoadInstanceFunc<PFN_vkCreateXlibSurfaceKHR>(instance, "vkCreateXlibSurfaceKHR");
        vkCreateWaylandSurfaceKHR = LoadInstanceFunc<PFN_vkCreateWaylandSurfaceKHR>(instance, "vkCreateWaylandSurfaceKHR");
        vkCreateWin32SurfaceKHR = LoadInstanceFunc<PFN_vkCreateWin32SurfaceKHR>(instance, "vkCreateWin32SurfaceKHR");
        vkCreateMetalSurfaceEXT = LoadInstanceFunc<PFN_vkCreateMetalSurfaceEXT>(instance, "vkCreateMetalSurfaceEXT");

        Console.WriteLine("[Vulkan] Instance-level functions loaded");
    }

    /// <summary>
    /// Load device-level functions after vkCreateDevice succeeds.
    /// </summary>
    public static void LoadDeviceFunctions(IntPtr device)
    {
        vkDestroyDevice = LoadDeviceFunc<PFN_vkDestroyDevice>(device, "vkDestroyDevice");
        vkGetDeviceQueue = LoadDeviceFunc<PFN_vkGetDeviceQueue>(device, "vkGetDeviceQueue");
        vkDeviceWaitIdle = LoadDeviceFunc<PFN_vkDeviceWaitIdle>(device, "vkDeviceWaitIdle");

        // Memory
        vkAllocateMemory = LoadDeviceFunc<PFN_vkAllocateMemory>(device, "vkAllocateMemory");
        vkFreeMemory = LoadDeviceFunc<PFN_vkFreeMemory>(device, "vkFreeMemory");
        vkMapMemory = LoadDeviceFunc<PFN_vkMapMemory>(device, "vkMapMemory");
        vkUnmapMemory = LoadDeviceFunc<PFN_vkUnmapMemory>(device, "vkUnmapMemory");

        // Buffer
        vkCreateBuffer = LoadDeviceFunc<PFN_vkCreateBuffer>(device, "vkCreateBuffer");
        vkDestroyBuffer = LoadDeviceFunc<PFN_vkDestroyBuffer>(device, "vkDestroyBuffer");
        vkGetBufferMemoryRequirements = LoadDeviceFunc<PFN_vkGetBufferMemoryRequirements>(device, "vkGetBufferMemoryRequirements");
        vkBindBufferMemory = LoadDeviceFunc<PFN_vkBindBufferMemory>(device, "vkBindBufferMemory");

        // Image
        vkCreateImage = LoadDeviceFunc<PFN_vkCreateImage>(device, "vkCreateImage");
        vkDestroyImage = LoadDeviceFunc<PFN_vkDestroyImage>(device, "vkDestroyImage");
        vkGetImageMemoryRequirements = LoadDeviceFunc<PFN_vkGetImageMemoryRequirements>(device, "vkGetImageMemoryRequirements");
        vkBindImageMemory = LoadDeviceFunc<PFN_vkBindImageMemory>(device, "vkBindImageMemory");
        vkCreateImageView = LoadDeviceFunc<PFN_vkCreateImageView>(device, "vkCreateImageView");
        vkDestroyImageView = LoadDeviceFunc<PFN_vkDestroyImageView>(device, "vkDestroyImageView");

        // Sampler
        vkCreateSampler = LoadDeviceFunc<PFN_vkCreateSampler>(device, "vkCreateSampler");
        vkDestroySampler = LoadDeviceFunc<PFN_vkDestroySampler>(device, "vkDestroySampler");

        // Shader
        vkCreateShaderModule = LoadDeviceFunc<PFN_vkCreateShaderModule>(device, "vkCreateShaderModule");
        vkDestroyShaderModule = LoadDeviceFunc<PFN_vkDestroyShaderModule>(device, "vkDestroyShaderModule");

        // Pipeline
        vkCreateGraphicsPipelines = LoadDeviceFunc<PFN_vkCreateGraphicsPipelines>(device, "vkCreateGraphicsPipelines");
        vkCreateComputePipelines = LoadDeviceFunc<PFN_vkCreateComputePipelines>(device, "vkCreateComputePipelines");
        vkDestroyPipeline = LoadDeviceFunc<PFN_vkDestroyPipeline>(device, "vkDestroyPipeline");
        vkCreatePipelineLayout = LoadDeviceFunc<PFN_vkCreatePipelineLayout>(device, "vkCreatePipelineLayout");
        vkDestroyPipelineLayout = LoadDeviceFunc<PFN_vkDestroyPipelineLayout>(device, "vkDestroyPipelineLayout");

        // Render Pass / Framebuffer
        vkCreateRenderPass = LoadDeviceFunc<PFN_vkCreateRenderPass>(device, "vkCreateRenderPass");
        vkDestroyRenderPass = LoadDeviceFunc<PFN_vkDestroyRenderPass>(device, "vkDestroyRenderPass");
        vkCreateFramebuffer = LoadDeviceFunc<PFN_vkCreateFramebuffer>(device, "vkCreateFramebuffer");
        vkDestroyFramebuffer = LoadDeviceFunc<PFN_vkDestroyFramebuffer>(device, "vkDestroyFramebuffer");

        // Descriptors
        vkCreateDescriptorSetLayout = LoadDeviceFunc<PFN_vkCreateDescriptorSetLayout>(device, "vkCreateDescriptorSetLayout");
        vkDestroyDescriptorSetLayout = LoadDeviceFunc<PFN_vkDestroyDescriptorSetLayout>(device, "vkDestroyDescriptorSetLayout");
        vkCreateDescriptorPool = LoadDeviceFunc<PFN_vkCreateDescriptorPool>(device, "vkCreateDescriptorPool");
        vkDestroyDescriptorPool = LoadDeviceFunc<PFN_vkDestroyDescriptorPool>(device, "vkDestroyDescriptorPool");
        vkAllocateDescriptorSets = LoadDeviceFunc<PFN_vkAllocateDescriptorSets>(device, "vkAllocateDescriptorSets");
        vkUpdateDescriptorSets = LoadDeviceFunc<PFN_vkUpdateDescriptorSets>(device, "vkUpdateDescriptorSets");

        // Command Pool / Buffer
        vkCreateCommandPool = LoadDeviceFunc<PFN_vkCreateCommandPool>(device, "vkCreateCommandPool");
        vkDestroyCommandPool = LoadDeviceFunc<PFN_vkDestroyCommandPool>(device, "vkDestroyCommandPool");
        vkAllocateCommandBuffers = LoadDeviceFunc<PFN_vkAllocateCommandBuffers>(device, "vkAllocateCommandBuffers");
        vkBeginCommandBuffer = LoadDeviceFunc<PFN_vkBeginCommandBuffer>(device, "vkBeginCommandBuffer");
        vkEndCommandBuffer = LoadDeviceFunc<PFN_vkEndCommandBuffer>(device, "vkEndCommandBuffer");
        vkResetCommandBuffer = LoadDeviceFunc<PFN_vkResetCommandBuffer>(device, "vkResetCommandBuffer");

        // Commands
        vkCmdBeginRenderPass = LoadDeviceFunc<PFN_vkCmdBeginRenderPass>(device, "vkCmdBeginRenderPass");
        vkCmdEndRenderPass = LoadDeviceFunc<PFN_vkCmdEndRenderPass>(device, "vkCmdEndRenderPass");
        vkCmdBindPipeline = LoadDeviceFunc<PFN_vkCmdBindPipeline>(device, "vkCmdBindPipeline");
        vkCmdSetViewport = LoadDeviceFunc<PFN_vkCmdSetViewport>(device, "vkCmdSetViewport");
        vkCmdSetScissor = LoadDeviceFunc<PFN_vkCmdSetScissor>(device, "vkCmdSetScissor");
        vkCmdBindVertexBuffers = LoadDeviceFunc<PFN_vkCmdBindVertexBuffers>(device, "vkCmdBindVertexBuffers");
        vkCmdBindIndexBuffer = LoadDeviceFunc<PFN_vkCmdBindIndexBuffer>(device, "vkCmdBindIndexBuffer");
        vkCmdBindDescriptorSets = LoadDeviceFunc<PFN_vkCmdBindDescriptorSets>(device, "vkCmdBindDescriptorSets");
        vkCmdDraw = LoadDeviceFunc<PFN_vkCmdDraw>(device, "vkCmdDraw");
        vkCmdDrawIndexed = LoadDeviceFunc<PFN_vkCmdDrawIndexed>(device, "vkCmdDrawIndexed");
        vkCmdDrawIndirect = LoadDeviceFunc<PFN_vkCmdDrawIndirect>(device, "vkCmdDrawIndirect");
        vkCmdDrawIndexedIndirect = LoadDeviceFunc<PFN_vkCmdDrawIndexedIndirect>(device, "vkCmdDrawIndexedIndirect");
        vkCmdDispatch = LoadDeviceFunc<PFN_vkCmdDispatch>(device, "vkCmdDispatch");
        vkCmdDispatchIndirect = LoadDeviceFunc<PFN_vkCmdDispatchIndirect>(device, "vkCmdDispatchIndirect");
        vkCmdPipelineBarrier = LoadDeviceFunc<PFN_vkCmdPipelineBarrier>(device, "vkCmdPipelineBarrier");
        vkCmdCopyBuffer = LoadDeviceFunc<PFN_vkCmdCopyBuffer>(device, "vkCmdCopyBuffer");
        vkCmdCopyBufferToImage = LoadDeviceFunc<PFN_vkCmdCopyBufferToImage>(device, "vkCmdCopyBufferToImage");

        // Synchronization
        vkCreateSemaphore = LoadDeviceFunc<PFN_vkCreateSemaphore>(device, "vkCreateSemaphore");
        vkDestroySemaphore = LoadDeviceFunc<PFN_vkDestroySemaphore>(device, "vkDestroySemaphore");
        vkCreateFence = LoadDeviceFunc<PFN_vkCreateFence>(device, "vkCreateFence");
        vkDestroyFence = LoadDeviceFunc<PFN_vkDestroyFence>(device, "vkDestroyFence");
        vkWaitForFences = LoadDeviceFunc<PFN_vkWaitForFences>(device, "vkWaitForFences");
        vkResetFences = LoadDeviceFunc<PFN_vkResetFences>(device, "vkResetFences");
        vkQueueSubmit = LoadDeviceFunc<PFN_vkQueueSubmit>(device, "vkQueueSubmit");
        vkQueueWaitIdle = LoadDeviceFunc<PFN_vkQueueWaitIdle>(device, "vkQueueWaitIdle");

        // Swapchain
        vkCreateSwapchainKHR = LoadDeviceFunc<PFN_vkCreateSwapchainKHR>(device, "vkCreateSwapchainKHR");
        vkDestroySwapchainKHR = LoadDeviceFunc<PFN_vkDestroySwapchainKHR>(device, "vkDestroySwapchainKHR");
        vkGetSwapchainImagesKHR = LoadDeviceFunc<PFN_vkGetSwapchainImagesKHR>(device, "vkGetSwapchainImagesKHR");
        vkAcquireNextImageKHR = LoadDeviceFunc<PFN_vkAcquireNextImageKHR>(device, "vkAcquireNextImageKHR");
        vkQueuePresentKHR = LoadDeviceFunc<PFN_vkQueuePresentKHR>(device, "vkQueuePresentKHR");

        Console.WriteLine("[Vulkan] Device-level functions loaded");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helper: Check Vulkan result and throw on error
    // ═══════════════════════════════════════════════════════════════════

    public static void VkCheck(int result, string operation = "Vulkan operation")
    {
        if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR)
        {
            throw new InvalidOperationException(
                $"[Vulkan] {operation} failed with VkResult = {result}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Format Conversion
    // ═══════════════════════════════════════════════════════════════════

    public static uint ToVkFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => VK_FORMAT_R8_UNORM,
        TextureFormat.R32Float => VK_FORMAT_R32_SFLOAT,
        TextureFormat.RGBA8Unorm => VK_FORMAT_R8G8B8A8_UNORM,
        TextureFormat.RGBA8Srgb => VK_FORMAT_R8G8B8A8_SRGB,
        TextureFormat.BGRA8Unorm => VK_FORMAT_B8G8R8A8_UNORM,
        TextureFormat.BGRA8Srgb => VK_FORMAT_B8G8R8A8_SRGB,
        TextureFormat.RG32Float => VK_FORMAT_R32G32_SFLOAT,
        TextureFormat.RGB32Float => VK_FORMAT_R32G32B32_SFLOAT,
        TextureFormat.RGBA16Float => VK_FORMAT_R16G16B16A16_SFLOAT,
        TextureFormat.RGBA32Float => VK_FORMAT_R32G32B32A32_SFLOAT,
        TextureFormat.Depth32Float => VK_FORMAT_D32_SFLOAT,
        TextureFormat.Depth24Stencil8 => VK_FORMAT_D24_UNORM_S8_UINT,
        TextureFormat.BC1 => VK_FORMAT_BC1_RGB_UNORM_BLOCK,
        TextureFormat.BC3 => VK_FORMAT_BC3_UNORM_BLOCK,
        TextureFormat.BC7 => VK_FORMAT_BC7_UNORM_BLOCK,
        _ => VK_FORMAT_UNDEFINED
    };

    public static TextureFormat FromVkFormat(uint vkFormat) => vkFormat switch
    {
        VK_FORMAT_R8G8B8A8_UNORM => TextureFormat.RGBA8Unorm,
        VK_FORMAT_R8G8B8A8_SRGB => TextureFormat.RGBA8Srgb,
        VK_FORMAT_B8G8R8A8_UNORM => TextureFormat.BGRA8Unorm,
        VK_FORMAT_B8G8R8A8_SRGB => TextureFormat.BGRA8Srgb,
        VK_FORMAT_R16G16B16A16_SFLOAT => TextureFormat.RGBA16Float,
        VK_FORMAT_D32_SFLOAT => TextureFormat.Depth32Float,
        VK_FORMAT_D24_UNORM_S8_UINT => TextureFormat.Depth24Stencil8,
        _ => TextureFormat.RGBA8Unorm
    };

    public static bool IsDepthFormat(uint vkFormat) =>
        vkFormat == VK_FORMAT_D32_SFLOAT || vkFormat == VK_FORMAT_D24_UNORM_S8_UINT;
}
