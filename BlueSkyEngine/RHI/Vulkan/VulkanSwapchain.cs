using System;
using System.Runtime.InteropServices;
using BlueSky.Platform;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Vulkan swapchain implementation — manages VkSwapchainKHR, swapchain images,
/// image views, and frame synchronization (semaphores + fences).
/// </summary>
internal sealed class VulkanSwapchain : IRHISwapchain
{
    private readonly VulkanDevice _owner;
    private readonly IntPtr _surface;

    private IntPtr _swapchain;
    private VulkanTexture[] _images = Array.Empty<VulkanTexture>();
    private uint _imageCount;
    private uint _currentImageIndex;
    private uint _currentFrame;
    private uint _vkFormat;

    // Per-frame sync (double or triple-buffered)
    private const int MaxFramesInFlight = 2;
    private readonly IntPtr[] _imageAvailableSemaphores = new IntPtr[MaxFramesInFlight];
    private readonly IntPtr[] _renderFinishedSemaphores = new IntPtr[MaxFramesInFlight];
    private readonly IntPtr[] _inFlightFences = new IntPtr[MaxFramesInFlight];

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public TextureFormat Format { get; private set; }
    public IRHITexture CurrentRenderTarget => _images[_currentImageIndex];

    internal IntPtr ImageAvailableSemaphore => _imageAvailableSemaphores[_currentFrame % MaxFramesInFlight];
    internal IntPtr RenderFinishedSemaphore => _renderFinishedSemaphores[_currentFrame % MaxFramesInFlight];
    internal IntPtr InFlightFence => _inFlightFences[_currentFrame % MaxFramesInFlight];
    internal IntPtr Handle => _swapchain;
    internal uint CurrentImageIndex => _currentImageIndex;

    internal VulkanSwapchain(VulkanDevice owner, IWindow window, PresentMode presentMode)
    {
        _owner = owner;
        _surface = VulkanSurface.Create(owner.Instance, window);

        // Verify surface support on the selected queue family
        VulkanInterop.VkCheck(
            VulkanInterop.vkGetPhysicalDeviceSurfaceSupportKHR(
                owner.PhysicalDevice, owner.GraphicsQueueFamily, _surface, out var supported),
            "vkGetPhysicalDeviceSurfaceSupportKHR");

        if (supported == 0)
            throw new InvalidOperationException("[Vulkan] Selected queue family does not support presentation to this surface");

        CreateSyncObjects();
        CreateSwapchain((uint)window.Size.X, (uint)window.Size.Y, presentMode);
    }

    private void CreateSwapchain(uint width, uint height, PresentMode presentMode)
    {
        // Query surface capabilities
        VulkanInterop.VkCheck(
            VulkanInterop.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(
                _owner.PhysicalDevice, _surface, out var capabilities),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        // Choose surface format (prefer BGRA8 SRGB)
        uint formatCount = 0;
        VulkanInterop.vkGetPhysicalDeviceSurfaceFormatsKHR(_owner.PhysicalDevice, _surface, ref formatCount, IntPtr.Zero);
        var formats = new VulkanInterop.VkSurfaceFormatKHR[formatCount];
        var formatsHandle = GCHandle.Alloc(formats, GCHandleType.Pinned);
        VulkanInterop.vkGetPhysicalDeviceSurfaceFormatsKHR(_owner.PhysicalDevice, _surface, ref formatCount, formatsHandle.AddrOfPinnedObject());
        formatsHandle.Free();

        var chosenFormat = formats[0]; // Default
        foreach (var fmt in formats)
        {
            if (fmt.format == VulkanInterop.VK_FORMAT_B8G8R8A8_SRGB &&
                fmt.colorSpace == VulkanInterop.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
            {
                chosenFormat = fmt;
                break;
            }
            if (fmt.format == VulkanInterop.VK_FORMAT_B8G8R8A8_UNORM)
            {
                chosenFormat = fmt;
            }
        }

        _vkFormat = chosenFormat.format;
        Format = VulkanInterop.FromVkFormat(_vkFormat);

        // Choose present mode
        uint vkPresentMode = presentMode switch
        {
            PresentMode.Immediate => VulkanInterop.VK_PRESENT_MODE_IMMEDIATE_KHR,
            PresentMode.Mailbox => VulkanInterop.VK_PRESENT_MODE_MAILBOX_KHR,
            _ => VulkanInterop.VK_PRESENT_MODE_FIFO_KHR // Vsync
        };

        // Clamp extent
        if (capabilities.currentExtent.width != uint.MaxValue)
        {
            width = capabilities.currentExtent.width;
            height = capabilities.currentExtent.height;
        }
        else
        {
            width = Math.Clamp(width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
            height = Math.Clamp(height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
        }

        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        // Image count: prefer triple-buffer
        uint imageCount = capabilities.minImageCount + 1;
        if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount)
            imageCount = capabilities.maxImageCount;

        var createInfo = new VulkanInterop.VkSwapchainCreateInfoKHR
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR,
            surface = _surface,
            minImageCount = imageCount,
            imageFormat = chosenFormat.format,
            imageColorSpace = chosenFormat.colorSpace,
            imageExtent = new VulkanInterop.VkExtent2D { width = Width, height = Height },
            imageArrayLayers = 1,
            imageUsage = VulkanInterop.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,
            imageSharingMode = VulkanInterop.VK_SHARING_MODE_EXCLUSIVE,
            preTransform = capabilities.currentTransform,
            compositeAlpha = VulkanInterop.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR,
            presentMode = vkPresentMode,
            clipped = 1,
            oldSwapchain = _swapchain // Pass old swapchain for recreation
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateSwapchainKHR(_owner.Device, ref createInfo, IntPtr.Zero, out var newSwapchain),
            "vkCreateSwapchainKHR");

        // Destroy old swapchain if we're recreating
        if (_swapchain != IntPtr.Zero)
        {
            DestroySwapchainImages();
            VulkanInterop.vkDestroySwapchainKHR(_owner.Device, _swapchain, IntPtr.Zero);
        }
        _swapchain = newSwapchain;

        // Get swapchain images
        _imageCount = 0;
        VulkanInterop.vkGetSwapchainImagesKHR(_owner.Device, _swapchain, ref _imageCount, IntPtr.Zero);
        var imageHandles = new IntPtr[_imageCount];
        var imagesPin = GCHandle.Alloc(imageHandles, GCHandleType.Pinned);
        VulkanInterop.vkGetSwapchainImagesKHR(_owner.Device, _swapchain, ref _imageCount, imagesPin.AddrOfPinnedObject());
        imagesPin.Free();

        // Wrap as VulkanTexture (non-owning)
        _images = new VulkanTexture[_imageCount];
        for (int i = 0; i < _imageCount; i++)
        {
            _images[i] = new VulkanTexture(_owner, imageHandles[i], Width, Height, _vkFormat,
                TextureUsage.RenderTarget);
        }

        Console.WriteLine($"[Vulkan] Swapchain created: {Width}×{Height}, {_imageCount} images, format={_vkFormat}");
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new VulkanInterop.VkSemaphoreCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO
        };

        var fenceInfo = new VulkanInterop.VkFenceCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_FENCE_CREATE_INFO,
            flags = VulkanInterop.VK_FENCE_CREATE_SIGNALED_BIT // Start signaled so first WaitForFences works
        };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            VulkanInterop.VkCheck(
                VulkanInterop.vkCreateSemaphore(_owner.Device, ref semaphoreInfo, IntPtr.Zero, out _imageAvailableSemaphores[i]),
                "vkCreateSemaphore (imageAvailable)");
            VulkanInterop.VkCheck(
                VulkanInterop.vkCreateSemaphore(_owner.Device, ref semaphoreInfo, IntPtr.Zero, out _renderFinishedSemaphores[i]),
                "vkCreateSemaphore (renderFinished)");
            VulkanInterop.VkCheck(
                VulkanInterop.vkCreateFence(_owner.Device, ref fenceInfo, IntPtr.Zero, out _inFlightFences[i]),
                "vkCreateFence");
        }
    }

    public void AcquireNextImage()
    {
        int frameIdx = (int)(_currentFrame % MaxFramesInFlight);

        // Wait for previous frame using this fence slot to complete
        var fence = _inFlightFences[frameIdx];
        var fencePin = GCHandle.Alloc(new[] { fence }, GCHandleType.Pinned);
        VulkanInterop.vkWaitForFences(_owner.Device, 1, fencePin.AddrOfPinnedObject(), 1, ulong.MaxValue);
        VulkanInterop.vkResetFences(_owner.Device, 1, fencePin.AddrOfPinnedObject());
        fencePin.Free();

        int result = VulkanInterop.vkAcquireNextImageKHR(
            _owner.Device, _swapchain, ulong.MaxValue,
            _imageAvailableSemaphores[frameIdx], IntPtr.Zero,
            out _currentImageIndex);

        if (result == VulkanInterop.VK_ERROR_OUT_OF_DATE_KHR)
        {
            // Swapchain needs recreation — caller should handle resize
            Console.WriteLine("[Vulkan] Swapchain out of date during acquire");
        }
    }

    public void Present()
    {
        int frameIdx = (int)(_currentFrame % MaxFramesInFlight);
        var waitSemaphore = _renderFinishedSemaphores[frameIdx];
        var swapchain = _swapchain;
        var imageIndex = _currentImageIndex;

        var waitSemPin = GCHandle.Alloc(new[] { waitSemaphore }, GCHandleType.Pinned);
        var swapchainPin = GCHandle.Alloc(new[] { swapchain }, GCHandleType.Pinned);
        var indexPin = GCHandle.Alloc(new[] { imageIndex }, GCHandleType.Pinned);

        var presentInfo = new VulkanInterop.VkPresentInfoKHR
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PRESENT_INFO_KHR,
            waitSemaphoreCount = 1,
            pWaitSemaphores = waitSemPin.AddrOfPinnedObject(),
            swapchainCount = 1,
            pSwapchains = swapchainPin.AddrOfPinnedObject(),
            pImageIndices = indexPin.AddrOfPinnedObject()
        };

        int result = VulkanInterop.vkQueuePresentKHR(_owner.GraphicsQueue, ref presentInfo);

        waitSemPin.Free();
        swapchainPin.Free();
        indexPin.Free();

        if (result == VulkanInterop.VK_ERROR_OUT_OF_DATE_KHR || result == VulkanInterop.VK_SUBOPTIMAL_KHR)
        {
            Console.WriteLine("[Vulkan] Swapchain suboptimal/out-of-date during present");
        }

        _currentFrame++;
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0) return;

        VulkanInterop.vkDeviceWaitIdle(_owner.Device);
        CreateSwapchain(width, height, PresentMode.Vsync);
    }

    private void DestroySwapchainImages()
    {
        foreach (var img in _images)
            img.Dispose(); // Only destroys the image view, not the swapchain image
        _images = Array.Empty<VulkanTexture>();
    }

    public void Dispose()
    {
        VulkanInterop.vkDeviceWaitIdle(_owner.Device);

        DestroySwapchainImages();

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (_imageAvailableSemaphores[i] != IntPtr.Zero)
                VulkanInterop.vkDestroySemaphore(_owner.Device, _imageAvailableSemaphores[i], IntPtr.Zero);
            if (_renderFinishedSemaphores[i] != IntPtr.Zero)
                VulkanInterop.vkDestroySemaphore(_owner.Device, _renderFinishedSemaphores[i], IntPtr.Zero);
            if (_inFlightFences[i] != IntPtr.Zero)
                VulkanInterop.vkDestroyFence(_owner.Device, _inFlightFences[i], IntPtr.Zero);
        }

        if (_swapchain != IntPtr.Zero)
        {
            VulkanInterop.vkDestroySwapchainKHR(_owner.Device, _swapchain, IntPtr.Zero);
            _swapchain = IntPtr.Zero;
        }

        if (_surface != IntPtr.Zero)
        {
            VulkanInterop.vkDestroySurfaceKHR(_owner.Instance, _surface, IntPtr.Zero);
        }

        Console.WriteLine("[Vulkan] Swapchain disposed");
    }
}
