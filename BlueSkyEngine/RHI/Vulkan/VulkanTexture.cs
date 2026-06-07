using System;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Vulkan texture implementation wrapping VkImage + VkImageView + VkDeviceMemory.
/// </summary>
internal sealed class VulkanTexture : IRHITexture
{
    private readonly VulkanDevice _owner;
    internal IntPtr ImageHandle { get; private set; }
    internal IntPtr ViewHandle { get; private set; }
    internal IntPtr Memory { get; private set; }
    internal uint VkFormat { get; }
    internal uint CurrentLayout { get; set; }
    internal bool OwnsImage { get; } // False for swapchain images

    public uint Width { get; }
    public uint Height { get; }
    public TextureFormat Format { get; }
    public TextureUsage Usage { get; }

    /// <summary>
    /// Create a texture with a new VkImage allocation.
    /// </summary>
    internal VulkanTexture(VulkanDevice owner, TextureDesc desc)
    {
        _owner = owner;
        Width = desc.Width;
        Height = desc.Height;
        Format = desc.Format;
        Usage = desc.Usage;
        VkFormat = VulkanInterop.ToVkFormat(desc.Format);
        OwnsImage = true;
        CurrentLayout = VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED;

        // Map usage flags
        uint vkUsage = 0;
        if ((desc.Usage & TextureUsage.Sampled) != 0) vkUsage |= VulkanInterop.VK_IMAGE_USAGE_SAMPLED_BIT;
        if ((desc.Usage & TextureUsage.Storage) != 0) vkUsage |= VulkanInterop.VK_IMAGE_USAGE_STORAGE_BIT;
        if ((desc.Usage & TextureUsage.RenderTarget) != 0) vkUsage |= VulkanInterop.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        if ((desc.Usage & TextureUsage.DepthStencil) != 0) vkUsage |= VulkanInterop.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
        if ((desc.Usage & TextureUsage.TransferSrc) != 0) vkUsage |= VulkanInterop.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        if ((desc.Usage & TextureUsage.TransferDst) != 0) vkUsage |= VulkanInterop.VK_IMAGE_USAGE_TRANSFER_DST_BIT;

        // If used as a sampled render target, ensure transfer_src for readback
        if ((desc.Usage & TextureUsage.RenderTarget) != 0 && (desc.Usage & TextureUsage.Sampled) != 0)
            vkUsage |= VulkanInterop.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;

        var createInfo = new VulkanInterop.VkImageCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
            imageType = VulkanInterop.VK_IMAGE_TYPE_2D,
            format = VkFormat,
            extent = new VulkanInterop.VkExtent3D { width = desc.Width, height = desc.Height, depth = Math.Max(1, desc.Depth) },
            mipLevels = Math.Max(1, desc.MipLevels),
            arrayLayers = Math.Max(1, desc.ArrayLayers),
            samples = VulkanInterop.VK_SAMPLE_COUNT_1_BIT,
            tiling = VulkanInterop.VK_IMAGE_TILING_OPTIMAL,
            usage = vkUsage,
            sharingMode = VulkanInterop.VK_SHARING_MODE_EXCLUSIVE,
            initialLayout = VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateImage(owner.Device, ref createInfo, IntPtr.Zero, out var image),
            "vkCreateImage");
        ImageHandle = image;

        // Allocate and bind memory
        VulkanInterop.vkGetImageMemoryRequirements(owner.Device, ImageHandle, out var memReqs);
        uint memTypeIndex = owner.FindMemoryType(memReqs.memoryTypeBits, VulkanInterop.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        var allocInfo = new VulkanInterop.VkMemoryAllocateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
            allocationSize = memReqs.size,
            memoryTypeIndex = memTypeIndex
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkAllocateMemory(owner.Device, ref allocInfo, IntPtr.Zero, out var memory),
            "vkAllocateMemory (texture)");
        Memory = memory;

        VulkanInterop.VkCheck(
            VulkanInterop.vkBindImageMemory(owner.Device, ImageHandle, Memory, 0),
            "vkBindImageMemory");

        // Create image view
        CreateImageView(desc.MipLevels);

        if (desc.DebugName != null)
            Console.WriteLine($"[Vulkan] Texture created: {desc.DebugName} ({desc.Width}×{desc.Height})");
    }

    /// <summary>
    /// Wrap an existing VkImage (e.g., swapchain image) — does NOT own the image.
    /// </summary>
    internal VulkanTexture(VulkanDevice owner, IntPtr existingImage, uint width, uint height, uint vkFormat, TextureUsage usage)
    {
        _owner = owner;
        ImageHandle = existingImage;
        Width = width;
        Height = height;
        VkFormat = vkFormat;
        Format = VulkanInterop.FromVkFormat(vkFormat);
        Usage = usage;
        OwnsImage = false;
        CurrentLayout = VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED;

        CreateImageView(1);
    }

    private void CreateImageView(uint mipLevels)
    {
        bool isDepth = VulkanInterop.IsDepthFormat(VkFormat);
        var viewInfo = new VulkanInterop.VkImageViewCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
            image = ImageHandle,
            viewType = VulkanInterop.VK_IMAGE_VIEW_TYPE_2D,
            format = VkFormat,
            components = new VulkanInterop.VkComponentMapping(), // Identity swizzle
            subresourceRange = new VulkanInterop.VkImageSubresourceRange
            {
                aspectMask = isDepth ? VulkanInterop.VK_IMAGE_ASPECT_DEPTH_BIT : VulkanInterop.VK_IMAGE_ASPECT_COLOR_BIT,
                baseMipLevel = 0,
                levelCount = Math.Max(1, mipLevels),
                baseArrayLayer = 0,
                layerCount = 1
            }
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateImageView(_owner.Device, ref viewInfo, IntPtr.Zero, out var view),
            "vkCreateImageView");
        ViewHandle = view;
    }

    public void Dispose()
    {
        if (ViewHandle != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyImageView(_owner.Device, ViewHandle, IntPtr.Zero);
            ViewHandle = IntPtr.Zero;
        }

        if (OwnsImage)
        {
            if (ImageHandle != IntPtr.Zero)
            {
                VulkanInterop.vkDestroyImage(_owner.Device, ImageHandle, IntPtr.Zero);
                ImageHandle = IntPtr.Zero;
            }

            if (Memory != IntPtr.Zero)
            {
                VulkanInterop.vkFreeMemory(_owner.Device, Memory, IntPtr.Zero);
                Memory = IntPtr.Zero;
            }
        }
    }
}
