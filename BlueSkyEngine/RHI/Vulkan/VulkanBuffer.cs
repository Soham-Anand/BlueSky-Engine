using System;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Vulkan buffer implementation wrapping VkBuffer + VkDeviceMemory.
/// </summary>
internal sealed class VulkanBuffer : IRHIBuffer
{
    private readonly VulkanDevice _owner;
    internal IntPtr Handle { get; private set; }
    internal IntPtr Memory { get; private set; }
    internal IntPtr MappedPointer { get; private set; }

    public ulong Size { get; }
    public BufferUsage Usage { get; }
    public MemoryType MemoryType { get; }

    internal VulkanBuffer(VulkanDevice owner, BufferDesc desc)
    {
        _owner = owner;
        Size = desc.Size;
        Usage = desc.Usage;
        MemoryType = desc.MemoryType;

        // Map BlueSky buffer usage → VkBufferUsageFlags
        uint vkUsage = VulkanInterop.VK_BUFFER_USAGE_TRANSFER_DST_BIT; // Always allow uploads
        if ((desc.Usage & BufferUsage.Vertex) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
        if ((desc.Usage & BufferUsage.Index) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
        if ((desc.Usage & BufferUsage.Uniform) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
        if ((desc.Usage & BufferUsage.Storage) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
        if ((desc.Usage & BufferUsage.Indirect) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
        if ((desc.Usage & BufferUsage.TransferSrc) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        if ((desc.Usage & BufferUsage.TransferDst) != 0) vkUsage |= VulkanInterop.VK_BUFFER_USAGE_TRANSFER_DST_BIT;

        var createInfo = new VulkanInterop.VkBufferCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
            size = desc.Size,
            usage = vkUsage,
            sharingMode = VulkanInterop.VK_SHARING_MODE_EXCLUSIVE
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateBuffer(owner.Device, ref createInfo, IntPtr.Zero, out var buffer),
            "vkCreateBuffer");
        Handle = buffer;

        // Get memory requirements
        VulkanInterop.vkGetBufferMemoryRequirements(owner.Device, Handle, out var memReqs);

        // Select memory type
        uint memoryPropertyFlags = desc.MemoryType switch
        {
            MemoryType.GpuOnly => VulkanInterop.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
            MemoryType.CpuToGpu => VulkanInterop.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
                                   VulkanInterop.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            MemoryType.GpuToCpu => VulkanInterop.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
                                   VulkanInterop.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            _ => VulkanInterop.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
                 VulkanInterop.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
        };

        uint memTypeIndex = owner.FindMemoryType(memReqs.memoryTypeBits, memoryPropertyFlags);

        var allocInfo = new VulkanInterop.VkMemoryAllocateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
            allocationSize = memReqs.size,
            memoryTypeIndex = memTypeIndex
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkAllocateMemory(owner.Device, ref allocInfo, IntPtr.Zero, out var memory),
            "vkAllocateMemory (buffer)");
        Memory = memory;

        VulkanInterop.VkCheck(
            VulkanInterop.vkBindBufferMemory(owner.Device, Handle, Memory, 0),
            "vkBindBufferMemory");

        // Persistently map host-visible buffers
        if (desc.MemoryType != MemoryType.GpuOnly)
        {
            VulkanInterop.VkCheck(
                VulkanInterop.vkMapMemory(owner.Device, Memory, 0, desc.Size, 0, out var mapped),
                "vkMapMemory (buffer)");
            MappedPointer = mapped;
        }

        if (desc.DebugName != null)
            Console.WriteLine($"[Vulkan] Buffer created: {desc.DebugName} ({desc.Size} bytes)");
    }

    public void Dispose()
    {
        if (MappedPointer != IntPtr.Zero)
        {
            VulkanInterop.vkUnmapMemory(_owner.Device, Memory);
            MappedPointer = IntPtr.Zero;
        }

        if (Handle != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyBuffer(_owner.Device, Handle, IntPtr.Zero);
            Handle = IntPtr.Zero;
        }

        if (Memory != IntPtr.Zero)
        {
            VulkanInterop.vkFreeMemory(_owner.Device, Memory, IntPtr.Zero);
            Memory = IntPtr.Zero;
        }
    }
}
