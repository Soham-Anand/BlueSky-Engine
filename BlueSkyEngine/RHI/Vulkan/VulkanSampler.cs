namespace BlueSky.BSR.RHI.Vulkan;

internal sealed class VulkanSampler : IRHISampler
{
    private readonly VulkanDevice _owner;
    private bool _disposed;

    public SamplerDesc Desc { get; }
    internal IntPtr Handle { get; private set; }

    public VulkanSampler(VulkanDevice owner, SamplerDesc desc)
    {
        _owner = owner;
        Desc = desc;
        Handle = CreateVkSampler(owner, desc);
    }

    internal static IntPtr CreateVkSampler(VulkanDevice owner, SamplerDesc desc)
    {
        uint filter = desc.Filter == SamplerFilter.Nearest
            ? VulkanInterop.VK_FILTER_NEAREST
            : VulkanInterop.VK_FILTER_LINEAR;
        var info = new VulkanInterop.VkSamplerCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO,
            magFilter = filter,
            minFilter = filter,
            mipmapMode = desc.Filter == SamplerFilter.Nearest
                ? VulkanInterop.VK_FILTER_NEAREST
                : VulkanInterop.VK_SAMPLER_MIPMAP_MODE_LINEAR,
            addressModeU = ToVkAddress(desc.AddressU),
            addressModeV = ToVkAddress(desc.AddressV),
            addressModeW = ToVkAddress(desc.AddressW),
            mipLodBias = 0,
            anisotropyEnable = desc.Filter == SamplerFilter.Anisotropic ? 1u : 0u,
            maxAnisotropy = Math.Max(1f, desc.MaxAnisotropy),
            compareEnable = desc.CompareLess ? 1u : 0u,
            compareOp = 3, // VK_COMPARE_OP_LESS_OR_EQUAL
            minLod = desc.MinLod,
            maxLod = desc.MaxLod,
            borderColor = VulkanInterop.VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK,
        };
        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateSampler(owner.Device, ref info, IntPtr.Zero, out var sampler),
            "vkCreateSampler");
        return sampler;
    }

    private static uint ToVkAddress(SamplerAddressMode mode) => mode switch
    {
        SamplerAddressMode.Repeat => VulkanInterop.VK_SAMPLER_ADDRESS_MODE_REPEAT,      // 0
        SamplerAddressMode.Mirror => 1,  // VK_SAMPLER_ADDRESS_MODE_MIRRORED_REPEAT
        SamplerAddressMode.Clamp => VulkanInterop.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE, // 2
        SamplerAddressMode.Border => 3,  // VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_BORDER
        _ => VulkanInterop.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
    };

    public void Dispose()
    {
        if (_disposed) return;
        if (Handle != IntPtr.Zero)
        {
            VulkanInterop.vkDestroySampler(_owner.Device, Handle, IntPtr.Zero);
            Handle = IntPtr.Zero;
        }
        _disposed = true;
    }
}
