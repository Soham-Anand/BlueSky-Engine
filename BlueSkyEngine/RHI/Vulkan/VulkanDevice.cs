using System;
using System.Runtime.InteropServices;
using System.Text;
using BlueSky.Platform;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Full Vulkan RHI device implementation.
/// Manages VkInstance, VkPhysicalDevice, VkDevice, and all resource creation.
/// </summary>
public sealed class VulkanDevice : IRHIDevice
{
    internal IntPtr Instance { get; private set; }
    internal IntPtr PhysicalDevice { get; private set; }
    internal IntPtr Device { get; private set; }
    internal IntPtr GraphicsQueue { get; private set; }
    internal IntPtr ComputeQueue { get; private set; }
    internal IntPtr TransferQueue { get; private set; }
    internal uint GraphicsQueueFamily { get; private set; }
    internal uint ComputeQueueFamily { get; private set; }
    internal uint TransferQueueFamily { get; private set; }

    private VulkanInterop.VkPhysicalDeviceMemoryProperties _memoryProperties;
    private VulkanInterop.VkPhysicalDeviceProperties _deviceProperties;
    private VulkanInterop.VkPhysicalDeviceFeatures _deviceFeatures;

    // Staging buffer for uploads
    private VulkanBuffer? _stagingBuffer;
    private IntPtr _uploadCommandPool;
    private IntPtr _uploadCommandBuffer;

    internal IntPtr DefaultSampler { get; private set; }

    public RHIBackend Backend => RHIBackend.Vulkan;
    public RHICapabilities Capabilities { get; private set; }
    public DescriptorBindingMode BindingMode => DescriptorBindingMode.SlotBased;
    public string DeviceName { get; private set; } = "Unknown";

    public VulkanDevice(IWindow window)
    {
        VulkanInterop.EnsureLoaded();
        CreateInstance();
        SelectPhysicalDevice();
        CreateLogicalDevice();
        CreateDefaultSampler();
        CreateUploadResources();

        Console.WriteLine($"[Vulkan] Device ready: {DeviceName}");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Initialization
    // ═══════════════════════════════════════════════════════════════════

    private void CreateInstance()
    {
        var appNamePtr = Marshal.StringToHGlobalAnsi("BlueSky Engine");
        var engineNamePtr = Marshal.StringToHGlobalAnsi("BlueSky");

        var appInfo = new VulkanInterop.VkApplicationInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_APPLICATION_INFO,
            pApplicationName = appNamePtr,
            applicationVersion = VulkanInterop.VK_MAKE_API_VERSION(0, 1, 0, 0),
            pEngineName = engineNamePtr,
            engineVersion = VulkanInterop.VK_MAKE_API_VERSION(0, 1, 0, 0),
            apiVersion = VulkanInterop.VK_API_VERSION_1_2
        };

        // Required extensions
        var surfaceExtensions = VulkanSurface.GetRequiredExtensions();
        var extPtrs = new IntPtr[surfaceExtensions.Length];
        for (int i = 0; i < surfaceExtensions.Length; i++)
            extPtrs[i] = Marshal.StringToHGlobalAnsi(surfaceExtensions[i]);

        var extPin = GCHandle.Alloc(extPtrs, GCHandleType.Pinned);

        // Validation layers (debug only)
        string[] layers = Array.Empty<string>();
#if DEBUG
        layers = new[] { "VK_LAYER_KHRONOS_validation" };
#endif
        var layerPtrs = new IntPtr[layers.Length];
        for (int i = 0; i < layers.Length; i++)
            layerPtrs[i] = Marshal.StringToHGlobalAnsi(layers[i]);

        var layerPin = layers.Length > 0 ? GCHandle.Alloc(layerPtrs, GCHandleType.Pinned) : default;

        var appInfoPin = GCHandle.Alloc(appInfo, GCHandleType.Pinned);

        uint instanceFlags = 0;
        if (OperatingSystem.IsMacOS())
            instanceFlags = 0x01; // VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR

        var createInfo = new VulkanInterop.VkInstanceCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
            flags = instanceFlags,
            pApplicationInfo = appInfoPin.AddrOfPinnedObject(),
            enabledExtensionCount = (uint)extPtrs.Length,
            ppEnabledExtensionNames = extPin.AddrOfPinnedObject(),
            enabledLayerCount = (uint)layers.Length,
            ppEnabledLayerNames = layerPin.IsAllocated ? layerPin.AddrOfPinnedObject() : IntPtr.Zero
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateInstance(ref createInfo, IntPtr.Zero, out var instance),
            "vkCreateInstance");
        Instance = instance;

        // Free
        appInfoPin.Free(); extPin.Free();
        if (layerPin.IsAllocated) layerPin.Free();
        foreach (var p in extPtrs) Marshal.FreeHGlobal(p);
        foreach (var p in layerPtrs) Marshal.FreeHGlobal(p);
        Marshal.FreeHGlobal(appNamePtr);
        Marshal.FreeHGlobal(engineNamePtr);

        // Load instance-level functions
        VulkanInterop.LoadInstanceFunctions(Instance);

        Console.WriteLine($"[Vulkan] Instance created (API {VulkanInterop.VK_API_VERSION_1_2 >> 22}.{(VulkanInterop.VK_API_VERSION_1_2 >> 12) & 0x3FF})");
    }

    private void SelectPhysicalDevice()
    {
        uint deviceCount = 0;
        VulkanInterop.vkEnumeratePhysicalDevices(Instance, ref deviceCount, IntPtr.Zero);
        if (deviceCount == 0)
            throw new InvalidOperationException("[Vulkan] No physical devices found!");

        var devices = new IntPtr[deviceCount];
        var devicesPin = GCHandle.Alloc(devices, GCHandleType.Pinned);
        VulkanInterop.vkEnumeratePhysicalDevices(Instance, ref deviceCount, devicesPin.AddrOfPinnedObject());
        devicesPin.Free();

        // Score devices: prefer discrete GPU
        IntPtr bestDevice = IntPtr.Zero;
        int bestScore = -1;

        foreach (var dev in devices)
        {
            VulkanInterop.vkGetPhysicalDeviceProperties(dev, out var props);
            VulkanInterop.vkGetPhysicalDeviceFeatures(dev, out var features);

            int score = 0;

            // Device type scoring
            switch (props.deviceType)
            {
                case 2: score += 10000; break; // VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU
                case 1: score += 5000; break;  // VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU
                case 3: score += 1000; break;  // VK_PHYSICAL_DEVICE_TYPE_VIRTUAL_GPU
                case 4: score += 100; break;   // VK_PHYSICAL_DEVICE_TYPE_CPU
            }

            // Feature bonuses
            if (features.geometryShader != 0) score += 100;
            if (features.tessellationShader != 0) score += 100;
            if (features.multiDrawIndirect != 0) score += 200;
            if (features.samplerAnisotropy != 0) score += 50;

            if (score > bestScore)
            {
                bestScore = score;
                bestDevice = dev;
                _deviceProperties = props;
                _deviceFeatures = features;
            }
        }

        PhysicalDevice = bestDevice;

        // Extract device name
        unsafe
        {
            fixed (byte* deviceNamePtr = _deviceProperties.deviceName)
            {
            DeviceName = Encoding.UTF8.GetString(
                    new ReadOnlySpan<byte>(deviceNamePtr, 256)).TrimEnd('\0');
            }
        }

        // Get memory properties
        VulkanInterop.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out _memoryProperties);

        // Determine capabilities
        Capabilities = RHICapabilities.None;
        if (_deviceFeatures.geometryShader != 0) Capabilities |= RHICapabilities.GeometryShaders;
        if (_deviceFeatures.tessellationShader != 0) Capabilities |= RHICapabilities.TessellationShaders;
        if (_deviceFeatures.multiDrawIndirect != 0) Capabilities |= RHICapabilities.IndirectDrawing;
        // Vulkan always has compute shaders
        Capabilities |= RHICapabilities.ComputeShaders;

        Console.WriteLine($"[Vulkan] Selected GPU: {DeviceName} (type={_deviceProperties.deviceType}, score={bestScore})");
    }

    private void CreateLogicalDevice()
    {
        // Enumerate queue families
        uint queueFamilyCount = 0;
        VulkanInterop.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, IntPtr.Zero);
        var queueFamilies = new VulkanInterop.VkQueueFamilyProperties[queueFamilyCount];
        var qfPin = GCHandle.Alloc(queueFamilies, GCHandleType.Pinned);
        VulkanInterop.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, qfPin.AddrOfPinnedObject());
        qfPin.Free();

        // Find queue families
        GraphicsQueueFamily = uint.MaxValue;
        ComputeQueueFamily = uint.MaxValue;
        TransferQueueFamily = uint.MaxValue;

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            var flags = queueFamilies[i].queueFlags;

            if ((flags & VulkanInterop.VK_QUEUE_GRAPHICS_BIT) != 0 && GraphicsQueueFamily == uint.MaxValue)
                GraphicsQueueFamily = i;

            // Prefer dedicated compute queue (not graphics)
            if ((flags & VulkanInterop.VK_QUEUE_COMPUTE_BIT) != 0 &&
                (flags & VulkanInterop.VK_QUEUE_GRAPHICS_BIT) == 0 &&
                ComputeQueueFamily == uint.MaxValue)
                ComputeQueueFamily = i;

            // Prefer dedicated transfer queue
            if ((flags & VulkanInterop.VK_QUEUE_TRANSFER_BIT) != 0 &&
                (flags & VulkanInterop.VK_QUEUE_GRAPHICS_BIT) == 0 &&
                (flags & VulkanInterop.VK_QUEUE_COMPUTE_BIT) == 0 &&
                TransferQueueFamily == uint.MaxValue)
                TransferQueueFamily = i;
        }

        // Fallback: use graphics queue for compute/transfer
        if (ComputeQueueFamily == uint.MaxValue) ComputeQueueFamily = GraphicsQueueFamily;
        if (TransferQueueFamily == uint.MaxValue) TransferQueueFamily = GraphicsQueueFamily;

        if (GraphicsQueueFamily == uint.MaxValue)
            throw new InvalidOperationException("[Vulkan] No graphics queue family found!");

        // Create unique queue create infos
        var uniqueFamilies = new System.Collections.Generic.HashSet<uint>
        {
            GraphicsQueueFamily, ComputeQueueFamily, TransferQueueFamily
        };

        float priority = 1.0f;
        var priorityPin = GCHandle.Alloc(new[] { priority }, GCHandleType.Pinned);

        var queueCreateInfos = new VulkanInterop.VkDeviceQueueCreateInfo[uniqueFamilies.Count];
        int idx = 0;
        foreach (var family in uniqueFamilies)
        {
            queueCreateInfos[idx++] = new VulkanInterop.VkDeviceQueueCreateInfo
            {
                sType = VulkanInterop.VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
                queueFamilyIndex = family,
                queueCount = 1,
                pQueuePriorities = priorityPin.AddrOfPinnedObject()
            };
        }

        // Device extensions
        var deviceExtensions = new[] { "VK_KHR_swapchain" };
        var extPtrs = new IntPtr[deviceExtensions.Length];
        for (int i = 0; i < deviceExtensions.Length; i++)
            extPtrs[i] = Marshal.StringToHGlobalAnsi(deviceExtensions[i]);
        var extPin = GCHandle.Alloc(extPtrs, GCHandleType.Pinned);

        // Enable features
        var enabledFeatures = new VulkanInterop.VkPhysicalDeviceFeatures
        {
            samplerAnisotropy = _deviceFeatures.samplerAnisotropy,
            fillModeNonSolid = _deviceFeatures.fillModeNonSolid,
            multiDrawIndirect = _deviceFeatures.multiDrawIndirect,
            geometryShader = _deviceFeatures.geometryShader,
            tessellationShader = _deviceFeatures.tessellationShader,
            fragmentStoresAndAtomics = _deviceFeatures.fragmentStoresAndAtomics,
            vertexPipelineStoresAndAtomics = _deviceFeatures.vertexPipelineStoresAndAtomics,
            independentBlend = _deviceFeatures.independentBlend
        };
        var featuresPin = GCHandle.Alloc(enabledFeatures, GCHandleType.Pinned);

        var queueInfosPin = GCHandle.Alloc(queueCreateInfos, GCHandleType.Pinned);

        var deviceCreateInfo = new VulkanInterop.VkDeviceCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
            queueCreateInfoCount = (uint)queueCreateInfos.Length,
            pQueueCreateInfos = queueInfosPin.AddrOfPinnedObject(),
            enabledExtensionCount = (uint)deviceExtensions.Length,
            ppEnabledExtensionNames = extPin.AddrOfPinnedObject(),
            pEnabledFeatures = featuresPin.AddrOfPinnedObject()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateDevice(PhysicalDevice, ref deviceCreateInfo, IntPtr.Zero, out var device),
            "vkCreateDevice");
        Device = device;

        priorityPin.Free(); extPin.Free(); featuresPin.Free(); queueInfosPin.Free();
        foreach (var p in extPtrs) Marshal.FreeHGlobal(p);

        // Load device-level functions
        VulkanInterop.LoadDeviceFunctions(Device);

        // Get queue handles
        VulkanInterop.vkGetDeviceQueue(Device, GraphicsQueueFamily, 0, out var gfxQueue);
        GraphicsQueue = gfxQueue;

        VulkanInterop.vkGetDeviceQueue(Device, ComputeQueueFamily, 0, out var compQueue);
        ComputeQueue = compQueue;

        VulkanInterop.vkGetDeviceQueue(Device, TransferQueueFamily, 0, out var xferQueue);
        TransferQueue = xferQueue;

        Console.WriteLine($"[Vulkan] Logical device created. Queue families: gfx={GraphicsQueueFamily}, comp={ComputeQueueFamily}, xfer={TransferQueueFamily}");
    }

    private void CreateUploadResources()
    {
        // Create a staging buffer (4MB) for uploads
        _stagingBuffer = new VulkanBuffer(this, new BufferDesc
        {
            Size = 4 * 1024 * 1024,
            Usage = BufferUsage.TransferSrc,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Staging Buffer"
        });

        // Create upload command pool + buffer
        var poolInfo = new VulkanInterop.VkCommandPoolCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
            flags = VulkanInterop.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
            queueFamilyIndex = TransferQueueFamily
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateCommandPool(Device, ref poolInfo, IntPtr.Zero, out _uploadCommandPool),
            "vkCreateCommandPool (upload)");

        var allocInfo = new VulkanInterop.VkCommandBufferAllocateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
            commandPool = _uploadCommandPool,
            level = VulkanInterop.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
            commandBufferCount = 1
        };

        var cmdBufArr = new IntPtr[1];
        var cmdBufPin = GCHandle.Alloc(cmdBufArr, GCHandleType.Pinned);
        VulkanInterop.vkAllocateCommandBuffers(Device, ref allocInfo, cmdBufPin.AddrOfPinnedObject());
        cmdBufPin.Free();
        _uploadCommandBuffer = cmdBufArr[0];
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IRHIDevice — Resource Creation
    // ═══════════════════════════════════════════════════════════════════

    public IRHIBuffer CreateBuffer(BufferDesc desc)
    {
        return new VulkanBuffer(this, desc);
    }

    public IRHITexture CreateTexture(TextureDesc desc)
    {
        return new VulkanTexture(this, desc);
    }

    public IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync)
    {
        return new VulkanSwapchain(this, window, presentMode);
    }

    public IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        return VulkanPipeline.CreateGraphics(this, desc);
    }

    public IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        return VulkanPipeline.CreateCompute(this, desc);
    }

    public IRHICommandBuffer CreateCommandBuffer()
    {
        return new VulkanCommandBuffer(this);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IRHIDevice — Submission
    // ═══════════════════════════════════════════════════════════════════

    public void Submit(IRHICommandBuffer commandBuffer)
    {
        var vkCmd = (VulkanCommandBuffer)commandBuffer;
        vkCmd.EndRecording();

        var cmdBuf = vkCmd.Handle;
        var cmdBufPin = GCHandle.Alloc(new[] { cmdBuf }, GCHandleType.Pinned);

        var submitInfo = new VulkanInterop.VkSubmitInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SUBMIT_INFO,
            commandBufferCount = 1,
            pCommandBuffers = cmdBufPin.AddrOfPinnedObject()
        };

        var submitPin = GCHandle.Alloc(submitInfo, GCHandleType.Pinned);

        VulkanInterop.VkCheck(
            VulkanInterop.vkQueueSubmit(GraphicsQueue, 1, submitPin.AddrOfPinnedObject(), IntPtr.Zero),
            "vkQueueSubmit");

        submitPin.Free(); cmdBufPin.Free();
        VulkanInterop.vkQueueWaitIdle(GraphicsQueue);
    }

    /// <summary>
    /// Submit a command buffer with swapchain synchronization.
    /// </summary>
    public void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
        var vkCmd = (VulkanCommandBuffer)commandBuffer;
        var vkSwap = (VulkanSwapchain)swapchain;

        // Transition the current swapchain image to present layout before ending
        var currentTarget = (VulkanTexture)vkSwap.CurrentRenderTarget;
        vkCmd.TransitionImageLayout(currentTarget,
            VulkanInterop.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
            VulkanInterop.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
            0);

        vkCmd.EndRecording();

        var cmdBuf = vkCmd.Handle;
        var waitSemaphore = vkSwap.ImageAvailableSemaphore;
        var signalSemaphore = vkSwap.RenderFinishedSemaphore;
        var fence = vkSwap.InFlightFence;

        var waitStage = VulkanInterop.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;

        var cmdBufPin = GCHandle.Alloc(new[] { cmdBuf }, GCHandleType.Pinned);
        var waitSemPin = GCHandle.Alloc(new[] { waitSemaphore }, GCHandleType.Pinned);
        var waitStagePin = GCHandle.Alloc(new[] { waitStage }, GCHandleType.Pinned);
        var sigSemPin = GCHandle.Alloc(new[] { signalSemaphore }, GCHandleType.Pinned);

        var submitInfo = new VulkanInterop.VkSubmitInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SUBMIT_INFO,
            waitSemaphoreCount = 1,
            pWaitSemaphores = waitSemPin.AddrOfPinnedObject(),
            pWaitDstStageMask = waitStagePin.AddrOfPinnedObject(),
            commandBufferCount = 1,
            pCommandBuffers = cmdBufPin.AddrOfPinnedObject(),
            signalSemaphoreCount = 1,
            pSignalSemaphores = sigSemPin.AddrOfPinnedObject()
        };

        var submitPin = GCHandle.Alloc(submitInfo, GCHandleType.Pinned);

        VulkanInterop.VkCheck(
            VulkanInterop.vkQueueSubmit(GraphicsQueue, 1, submitPin.AddrOfPinnedObject(), fence),
            "vkQueueSubmit (swapchain)");

        submitPin.Free(); cmdBufPin.Free(); waitSemPin.Free(); waitStagePin.Free(); sigSemPin.Free();
        VulkanInterop.vkQueueWaitIdle(GraphicsQueue);
    }

    public void WaitIdle()
    {
        VulkanInterop.vkDeviceWaitIdle(Device);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IRHIDevice — Upload
    // ═══════════════════════════════════════════════════════════════════

    public void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        var vkBuf = (VulkanBuffer)buffer;

        if (vkBuf.MappedPointer != IntPtr.Zero)
        {
            // Direct mapped copy
            unsafe
            {
                fixed (byte* src = data)
                {
                    Buffer.MemoryCopy(src, (void*)(vkBuf.MappedPointer + (nint)offset), (long)vkBuf.Size, data.Length);
                }
            }
        }
        else
        {
            // Stage + copy
            ulong byteLength = (ulong)data.Length;
            StagedUpload(data, (stagingBuf) =>
            {
                var region = new VulkanInterop.VkBufferCopy
                {
                    srcOffset = 0,
                    dstOffset = offset,
                    size = byteLength
                };
                var regionPin = GCHandle.Alloc(region, GCHandleType.Pinned);
                VulkanInterop.vkCmdCopyBuffer(_uploadCommandBuffer, stagingBuf, vkBuf.Handle, 1, regionPin.AddrOfPinnedObject());
                regionPin.Free();
            });
        }
    }

    public void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        UploadBuffer(buffer, data, offset);
    }

    public void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0)
    {
        var vkTex = (VulkanTexture)texture;

        StagedUpload(data, (stagingBuf) =>
        {
            // Transition to transfer dst
            TransitionUploadImage(vkTex, VulkanInterop.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);

            var region = new VulkanInterop.VkBufferImageCopy
            {
                bufferOffset = 0,
                bufferRowLength = 0,
                bufferImageHeight = 0,
                imageSubresource = new VulkanInterop.VkImageSubresourceLayers
                {
                    aspectMask = VulkanInterop.VK_IMAGE_ASPECT_COLOR_BIT,
                    mipLevel = mipLevel,
                    baseArrayLayer = 0,
                    layerCount = 1
                },
                imageOffsetX = 0, imageOffsetY = 0, imageOffsetZ = 0,
                imageExtent = new VulkanInterop.VkExtent3D { width = vkTex.Width, height = vkTex.Height, depth = 1 }
            };
            var regionPin = GCHandle.Alloc(region, GCHandleType.Pinned);
            VulkanInterop.vkCmdCopyBufferToImage(_uploadCommandBuffer, stagingBuf, vkTex.ImageHandle,
                VulkanInterop.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, regionPin.AddrOfPinnedObject());
            regionPin.Free();

            // Transition to shader read optimal
            TransitionUploadImage(vkTex, VulkanInterop.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
        });

        vkTex.CurrentLayout = VulkanInterop.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    }

    private void StagedUpload(ReadOnlySpan<byte> data, Action<IntPtr> recordCommands)
    {
        // Copy data to staging buffer
        unsafe
        {
            fixed (byte* src = data)
            {
                Buffer.MemoryCopy(src, (void*)_stagingBuffer!.MappedPointer, (long)_stagingBuffer.Size, data.Length);
            }
        }

        // Record transfer commands
        VulkanInterop.vkResetCommandBuffer(_uploadCommandBuffer, 0);
        var beginInfo = new VulkanInterop.VkCommandBufferBeginInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
            flags = 1 // VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT
        };
        VulkanInterop.vkBeginCommandBuffer(_uploadCommandBuffer, ref beginInfo);

        recordCommands(_stagingBuffer.Handle);

        VulkanInterop.vkEndCommandBuffer(_uploadCommandBuffer);

        // Submit and wait
        var cmdBufPin = GCHandle.Alloc(new[] { _uploadCommandBuffer }, GCHandleType.Pinned);
        var submitInfo = new VulkanInterop.VkSubmitInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SUBMIT_INFO,
            commandBufferCount = 1,
            pCommandBuffers = cmdBufPin.AddrOfPinnedObject()
        };
        var submitPin = GCHandle.Alloc(submitInfo, GCHandleType.Pinned);

        VulkanInterop.vkQueueSubmit(TransferQueue, 1, submitPin.AddrOfPinnedObject(), IntPtr.Zero);
        VulkanInterop.vkQueueWaitIdle(TransferQueue);

        submitPin.Free(); cmdBufPin.Free();
    }

    private void TransitionUploadImage(VulkanTexture texture, uint newLayout)
    {
        bool isDepth = VulkanInterop.IsDepthFormat(texture.VkFormat);
        uint srcStage, dstStage, srcAccess, dstAccess;

        if (newLayout == VulkanInterop.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
        {
            srcStage = VulkanInterop.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
            dstStage = VulkanInterop.VK_PIPELINE_STAGE_TRANSFER_BIT;
            srcAccess = 0;
            dstAccess = VulkanInterop.VK_ACCESS_TRANSFER_WRITE_BIT;
        }
        else // Shader read optimal
        {
            srcStage = VulkanInterop.VK_PIPELINE_STAGE_TRANSFER_BIT;
            dstStage = VulkanInterop.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            srcAccess = VulkanInterop.VK_ACCESS_TRANSFER_WRITE_BIT;
            dstAccess = VulkanInterop.VK_ACCESS_SHADER_READ_BIT;
        }

        var barrier = new VulkanInterop.VkImageMemoryBarrier
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
            srcAccessMask = srcAccess,
            dstAccessMask = dstAccess,
            oldLayout = texture.CurrentLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = ~0u,
            dstQueueFamilyIndex = ~0u,
            image = texture.ImageHandle,
            subresourceRange = new VulkanInterop.VkImageSubresourceRange
            {
                aspectMask = isDepth ? VulkanInterop.VK_IMAGE_ASPECT_DEPTH_BIT : VulkanInterop.VK_IMAGE_ASPECT_COLOR_BIT,
                baseMipLevel = 0, levelCount = 1,
                baseArrayLayer = 0, layerCount = 1
            }
        };

        var pin = GCHandle.Alloc(barrier, GCHandleType.Pinned);
        VulkanInterop.vkCmdPipelineBarrier(
            _uploadCommandBuffer, srcStage, dstStage,
            0, 0, IntPtr.Zero, 0, IntPtr.Zero,
            1, pin.AddrOfPinnedObject());
        pin.Free();

        texture.CurrentLayout = newLayout;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IRHIDevice — Bindless (stubs — Vulkan bindless requires VK_EXT_descriptor_indexing)
    // ═══════════════════════════════════════════════════════════════════

    public BindlessResourceHandle RegisterBindlessTexture(IRHITexture texture)
    {
        return new BindlessResourceHandle { Index = 0, Generation = 0 };
    }

    public BindlessResourceHandle RegisterBindlessBuffer(IRHIBuffer buffer)
    {
        return new BindlessResourceHandle { Index = 0, Generation = 0 };
    }

    public void UnregisterBindlessResource(BindlessResourceHandle handle) { }

    private void CreateDefaultSampler()
    {
        var samplerInfo = new VulkanInterop.VkSamplerCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO,
            magFilter = VulkanInterop.VK_FILTER_LINEAR,
            minFilter = VulkanInterop.VK_FILTER_LINEAR,
            mipmapMode = VulkanInterop.VK_SAMPLER_MIPMAP_MODE_LINEAR,
            addressModeU = VulkanInterop.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            addressModeV = VulkanInterop.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            addressModeW = VulkanInterop.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            minLod = 0,
            maxLod = 1000,
            borderColor = VulkanInterop.VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateSampler(Device, ref samplerInfo, IntPtr.Zero, out var sampler),
            "vkCreateSampler");
        DefaultSampler = sampler;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Memory Helpers
    // ═══════════════════════════════════════════════════════════════════

    internal uint FindMemoryType(uint typeFilter, uint propertyFlags)
    {
        for (int i = 0; i < _memoryProperties.memoryTypeCount; i++)
        {
            if ((typeFilter & (1u << i)) != 0)
            {
                var memType = _memoryProperties.GetMemoryType(i);
                if ((memType.propertyFlags & propertyFlags) == propertyFlags)
                    return (uint)i;
            }
        }

        // Fallback: any matching type
        for (int i = 0; i < _memoryProperties.memoryTypeCount; i++)
        {
            if ((typeFilter & (1u << i)) != 0)
                return (uint)i;
        }

        throw new InvalidOperationException($"[Vulkan] Failed to find suitable memory type (filter={typeFilter:X}, flags={propertyFlags:X})");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Dispose
    // ═══════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        WaitIdle();

        _stagingBuffer?.Dispose();

        if (DefaultSampler != IntPtr.Zero)
        {
            VulkanInterop.vkDestroySampler(Device, DefaultSampler, IntPtr.Zero);
            DefaultSampler = IntPtr.Zero;
        }

        if (_uploadCommandPool != IntPtr.Zero)
            VulkanInterop.vkDestroyCommandPool(Device, _uploadCommandPool, IntPtr.Zero);

        if (Device != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyDevice(Device, IntPtr.Zero);
            Device = IntPtr.Zero;
        }

        if (Instance != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyInstance(Instance, IntPtr.Zero);
            Instance = IntPtr.Zero;
        }

        Console.WriteLine("[Vulkan] Device disposed");
    }
}
