using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Vulkan command buffer implementation. Records GPU commands into a VkCommandBuffer.
/// Manages render pass state, framebuffer creation, and resource binding.
/// </summary>
internal sealed class VulkanCommandBuffer : IRHICommandBuffer
{
    private readonly VulkanDevice _owner;
    private readonly IntPtr _commandPool;
    private IntPtr _commandBuffer;
    private bool _recording;
    private bool _inRenderPass;
    private IntPtr _descriptorPool;

    // Current state
    private VulkanPipeline? _currentPipeline;
    private IntPtr _currentFramebuffer;
    private IntPtr _currentRenderPass;

    // Framebuffer cache to avoid recreation every frame
    private readonly Dictionary<long, IntPtr> _framebufferCache = new();
    private readonly List<IntPtr> _transientFramebuffers = new();
    private readonly List<IntPtr> _transientRenderPasses = new();
    private readonly Dictionary<ulong, BoundDescriptor> _boundDescriptors = new();

    internal IntPtr Handle => _commandBuffer;

    internal VulkanCommandBuffer(VulkanDevice owner)
    {
        _owner = owner;

        // Create command pool
        var poolInfo = new VulkanInterop.VkCommandPoolCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
            flags = VulkanInterop.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
            queueFamilyIndex = owner.GraphicsQueueFamily
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateCommandPool(owner.Device, ref poolInfo, IntPtr.Zero, out _commandPool),
            "vkCreateCommandPool");

        // Allocate command buffer
        var allocInfo = new VulkanInterop.VkCommandBufferAllocateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
            commandPool = _commandPool,
            level = VulkanInterop.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
            commandBufferCount = 1
        };

        var cmdBufArr = new IntPtr[1];
        var cmdBufPin = GCHandle.Alloc(cmdBufArr, GCHandleType.Pinned);
        VulkanInterop.VkCheck(
            VulkanInterop.vkAllocateCommandBuffers(owner.Device, ref allocInfo, cmdBufPin.AddrOfPinnedObject()),
            "vkAllocateCommandBuffers");
        cmdBufPin.Free();
        _commandBuffer = cmdBufArr[0];

        CreateDescriptorPool();

        // Begin recording immediately
        BeginRecording();
    }

    private readonly struct BoundDescriptor
    {
        internal readonly uint DescriptorType;
        internal readonly VulkanBuffer? Buffer;
        internal readonly VulkanTexture? Texture;

        internal BoundDescriptor(uint descriptorType, VulkanBuffer buffer)
        {
            DescriptorType = descriptorType;
            Buffer = buffer;
            Texture = null;
        }

        internal BoundDescriptor(uint descriptorType, VulkanTexture texture)
        {
            DescriptorType = descriptorType;
            Buffer = null;
            Texture = texture;
        }
    }

    private void CreateDescriptorPool()
    {
        var poolSizes = new[]
        {
            new VulkanInterop.VkDescriptorPoolSize
            {
                type = VulkanInterop.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
                descriptorCount = 2048
            },
            new VulkanInterop.VkDescriptorPoolSize
            {
                type = VulkanInterop.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,
                descriptorCount = 2048
            },
            new VulkanInterop.VkDescriptorPoolSize
            {
                type = VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
                descriptorCount = 1024
            },
            new VulkanInterop.VkDescriptorPoolSize
            {
                type = VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
                descriptorCount = 512
            }
        };

        var poolSizesPin = GCHandle.Alloc(poolSizes, GCHandleType.Pinned);
        var poolInfo = new VulkanInterop.VkDescriptorPoolCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,
            maxSets = 2048,
            poolSizeCount = (uint)poolSizes.Length,
            pPoolSizes = poolSizesPin.AddrOfPinnedObject()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateDescriptorPool(_owner.Device, ref poolInfo, IntPtr.Zero, out _descriptorPool),
            "vkCreateDescriptorPool");
        poolSizesPin.Free();
    }

    private void BeginRecording()
    {
        VulkanInterop.vkResetCommandBuffer(_commandBuffer, 0);

        var beginInfo = new VulkanInterop.VkCommandBufferBeginInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
            flags = 0 // One-time submit would be 0x01
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkBeginCommandBuffer(_commandBuffer, ref beginInfo),
            "vkBeginCommandBuffer");

        _recording = true;
    }

    internal void EndRecording()
    {
        if (_inRenderPass)
            EndRenderPass();

        if (_recording)
        {
            VulkanInterop.VkCheck(
                VulkanInterop.vkEndCommandBuffer(_commandBuffer),
                "vkEndCommandBuffer");
            _recording = false;
        }
    }

    // ── Render Pass ──────────────────────────────────────────────────

    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        BeginRenderPass(new[] { renderTarget }, null, clearValue);
    }

    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        if (_inRenderPass) EndRenderPass();

        if (colorTargets.Length == 0) return;

        var firstColor = (VulkanTexture)colorTargets[0];
        uint width = firstColor.Width;
        uint height = firstColor.Height;

        // Transition color targets to attachment optimal
        foreach (var target in colorTargets)
        {
            var vkTex = (VulkanTexture)target;
            TransitionImageLayout(vkTex,
                VulkanInterop.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                VulkanInterop.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                VulkanInterop.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT);
        }

        if (depthTarget != null)
        {
            var vkDepth = (VulkanTexture)depthTarget;
            TransitionImageLayout(vkDepth,
                VulkanInterop.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
                VulkanInterop.VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT,
                VulkanInterop.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
        }

        // Create or get cached render pass & framebuffer
        var renderPass = CreateOnTheFlyRenderPass(colorTargets, depthTarget, clearValue);
        var framebuffer = CreateOnTheFlyFramebuffer(renderPass, colorTargets, depthTarget, width, height);
        _transientRenderPasses.Add(renderPass);
        _transientFramebuffers.Add(framebuffer);

        // Set up clear values
        int clearCount = colorTargets.Length + (depthTarget != null ? 1 : 0);
        var clearValues = new VulkanInterop.VkClearValue[clearCount];
        for (int i = 0; i < colorTargets.Length; i++)
        {
            clearValues[i] = new VulkanInterop.VkClearValue
            {
                r = clearValue.Color.X,
                g = clearValue.Color.Y,
                b = clearValue.Color.Z,
                a = clearValue.Color.W
            };
        }
        if (depthTarget != null)
        {
            // For depth clear, r = depth, g = stencil (as uint bits in float)
            clearValues[^1] = new VulkanInterop.VkClearValue
            {
                r = clearValue.Depth,
                g = 0 // stencil
            };
        }

        var clearPin = GCHandle.Alloc(clearValues, GCHandleType.Pinned);

        var rpBegin = new VulkanInterop.VkRenderPassBeginInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO,
            renderPass = renderPass,
            framebuffer = framebuffer,
            renderArea = new VulkanInterop.VkRect2D
            {
                offsetX = 0, offsetY = 0,
                extentWidth = width, extentHeight = height
            },
            clearValueCount = (uint)clearCount,
            pClearValues = clearPin.AddrOfPinnedObject()
        };

        VulkanInterop.vkCmdBeginRenderPass(_commandBuffer, ref rpBegin, VulkanInterop.VK_SUBPASS_CONTENTS_INLINE);
        clearPin.Free();

        _inRenderPass = true;
        _currentRenderPass = renderPass;
        _currentFramebuffer = framebuffer;
    }

    public void EndRenderPass()
    {
        if (!_inRenderPass) return;
        VulkanInterop.vkCmdEndRenderPass(_commandBuffer);
        _inRenderPass = false;
    }

    // ── Pipeline Binding ─────────────────────────────────────────────

    public void SetPipeline(IRHIPipeline pipeline)
    {
        var vkPipeline = (VulkanPipeline)pipeline;
        _currentPipeline = vkPipeline;

        uint bindPoint = vkPipeline.IsCompute
            ? VulkanInterop.VK_PIPELINE_BIND_POINT_COMPUTE
            : VulkanInterop.VK_PIPELINE_BIND_POINT_GRAPHICS;

        VulkanInterop.vkCmdBindPipeline(_commandBuffer, bindPoint, vkPipeline.PipelineHandle);
        BindDescriptorSets();
    }

    public void SetViewport(Viewport viewport)
    {
        var vkViewport = new VulkanInterop.VkViewport
        {
            x = viewport.X,
            y = viewport.Y,
            width = viewport.Width,
            height = viewport.Height,
            minDepth = viewport.MinDepth,
            maxDepth = viewport.MaxDepth
        };
        VulkanInterop.vkCmdSetViewport(_commandBuffer, 0, 1, ref vkViewport);
    }

    public void SetScissor(Scissor scissor)
    {
        var vkScissor = new VulkanInterop.VkRect2D
        {
            offsetX = scissor.X,
            offsetY = scissor.Y,
            extentWidth = scissor.Width,
            extentHeight = scissor.Height
        };
        VulkanInterop.vkCmdSetScissor(_commandBuffer, 0, 1, ref vkScissor);
    }

    // ── Resource Binding ─────────────────────────────────────────────

    public void SetVertexBuffer(IRHIBuffer buffer, uint binding = 0, ulong offset = 0)
    {
        var vkBuf = (VulkanBuffer)buffer;
        var buffers = new[] { vkBuf.Handle };
        var offsets = new[] { offset };
        var bufPin = GCHandle.Alloc(buffers, GCHandleType.Pinned);
        var offPin = GCHandle.Alloc(offsets, GCHandleType.Pinned);
        VulkanInterop.vkCmdBindVertexBuffers(_commandBuffer, binding, 1, bufPin.AddrOfPinnedObject(), offPin.AddrOfPinnedObject());
        bufPin.Free(); offPin.Free();
    }

    public void SetIndexBuffer(IRHIBuffer buffer, IndexType indexType, ulong offset = 0)
    {
        var vkBuf = (VulkanBuffer)buffer;
        uint vkIndexType = indexType == IndexType.UInt16
            ? VulkanInterop.VK_INDEX_TYPE_UINT16
            : VulkanInterop.VK_INDEX_TYPE_UINT32;
        VulkanInterop.vkCmdBindIndexBuffer(_commandBuffer, vkBuf.Handle, offset, vkIndexType);
    }

    public void SetUniformBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        var vkBuf = (VulkanBuffer)buffer;
        _boundDescriptors[VulkanPipeline.DescriptorKey(set, binding)] =
            new BoundDescriptor(VulkanInterop.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, vkBuf);
        BindDescriptorSets();
    }

    public void SetTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        var vkTex = (VulkanTexture)texture;
        if (vkTex.CurrentLayout != VulkanInterop.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL &&
            !_inRenderPass) // Can't do layout transitions inside a render pass
        {
            TransitionImageLayout(vkTex,
                VulkanInterop.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                VulkanInterop.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                VulkanInterop.VK_ACCESS_SHADER_READ_BIT);
        }

        _boundDescriptors[VulkanPipeline.DescriptorKey(set, binding)] =
            new BoundDescriptor(VulkanInterop.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, vkTex);
        BindDescriptorSets();
    }

    public void SetStorageBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        var vkBuf = (VulkanBuffer)buffer;
        _boundDescriptors[VulkanPipeline.DescriptorKey(set, binding)] =
            new BoundDescriptor(VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, vkBuf);
        BindDescriptorSets();
    }

    public void SetStorageTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        var vkTex = (VulkanTexture)texture;
        if (vkTex.CurrentLayout != VulkanInterop.VK_IMAGE_LAYOUT_GENERAL && !_inRenderPass)
        {
            TransitionImageLayout(vkTex,
                VulkanInterop.VK_IMAGE_LAYOUT_GENERAL,
                VulkanInterop.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VulkanInterop.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                VulkanInterop.VK_ACCESS_SHADER_READ_BIT | VulkanInterop.VK_ACCESS_SHADER_WRITE_BIT);
        }

        _boundDescriptors[VulkanPipeline.DescriptorKey(set, binding)] =
            new BoundDescriptor(VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, vkTex);
        BindDescriptorSets();
    }

    public void SetBindlessResourceTable(uint set, ReadOnlySpan<BindlessResourceHandle> handles)
    {
        // Bindless descriptor set binding
    }

    public void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        // Vulkan uses explicit buffers for uniform data in this RHI path.
    }

    public void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        // Vulkan uses explicit buffers for uniform data in this RHI path.
    }

    public void SetComputeUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        // Vulkan uses explicit buffers for uniform data in this RHI path.
    }

    public void SetVertexUniforms(uint binding, ref Matrix4x4 matrix)
    {
        // Vulkan uses explicit buffers for uniform data in this RHI path.
    }

    private void BindDescriptorSets()
    {
        if (_currentPipeline == null ||
            _descriptorPool == IntPtr.Zero ||
            _currentPipeline.DescriptorSetLayouts.Length == 0)
            return;

        uint bindPoint = _currentPipeline.IsCompute
            ? VulkanInterop.VK_PIPELINE_BIND_POINT_COMPUTE
            : VulkanInterop.VK_PIPELINE_BIND_POINT_GRAPHICS;

        for (uint set = 0; set < _currentPipeline.DescriptorSetLayouts.Length; set++)
        {
            IntPtr layout = _currentPipeline.DescriptorSetLayouts[set];
            if (layout == IntPtr.Zero)
                continue;

            bool hasBoundResource = false;
            foreach (var descriptor in _currentPipeline.DescriptorBindings.Values)
            {
                if (descriptor.Set != set)
                    continue;

                if (_boundDescriptors.ContainsKey(VulkanPipeline.DescriptorKey(set, descriptor.Binding)))
                {
                    hasBoundResource = true;
                    break;
                }
            }

            if (!hasBoundResource)
                continue;

            IntPtr descriptorSet = AllocateDescriptorSet(layout);
            UpdateDescriptorSet(descriptorSet, set);

            var setPin = GCHandle.Alloc(new[] { descriptorSet }, GCHandleType.Pinned);
            VulkanInterop.vkCmdBindDescriptorSets(
                _commandBuffer,
                bindPoint,
                _currentPipeline.LayoutHandle,
                set,
                1,
                setPin.AddrOfPinnedObject(),
                0,
                IntPtr.Zero);
            setPin.Free();
        }
    }

    private IntPtr AllocateDescriptorSet(IntPtr layout)
    {
        var layoutPin = GCHandle.Alloc(new[] { layout }, GCHandleType.Pinned);
        var descriptorSets = new IntPtr[1];
        var descriptorSetPin = GCHandle.Alloc(descriptorSets, GCHandleType.Pinned);

        var allocInfo = new VulkanInterop.VkDescriptorSetAllocateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,
            descriptorPool = _descriptorPool,
            descriptorSetCount = 1,
            pSetLayouts = layoutPin.AddrOfPinnedObject()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkAllocateDescriptorSets(_owner.Device, ref allocInfo, descriptorSetPin.AddrOfPinnedObject()),
            "vkAllocateDescriptorSets");

        descriptorSetPin.Free();
        layoutPin.Free();
        return descriptorSets[0];
    }

    private void UpdateDescriptorSet(IntPtr descriptorSet, uint set)
    {
        if (_currentPipeline == null)
            return;

        var writes = new List<VulkanInterop.VkWriteDescriptorSet>();
        var pins = new List<GCHandle>();

        foreach (var descriptor in _currentPipeline.DescriptorBindings.Values)
        {
            if (descriptor.Set != set)
                continue;

            ulong key = VulkanPipeline.DescriptorKey(set, descriptor.Binding);
            if (!_boundDescriptors.TryGetValue(key, out var bound))
                continue;

            if ((descriptor.DescriptorType == VulkanInterop.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER ||
                 descriptor.DescriptorType == VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER) &&
                bound.Buffer != null)
            {
                var bufferInfo = new[]
                {
                    new VulkanInterop.VkDescriptorBufferInfo
                    {
                        buffer = bound.Buffer.Handle,
                        offset = 0,
                        range = bound.Buffer.Size
                    }
                };
                var bufferInfoPin = GCHandle.Alloc(bufferInfo, GCHandleType.Pinned);
                pins.Add(bufferInfoPin);

                writes.Add(new VulkanInterop.VkWriteDescriptorSet
                {
                    sType = VulkanInterop.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
                    dstSet = descriptorSet,
                    dstBinding = descriptor.Binding,
                    descriptorCount = 1,
                    descriptorType = descriptor.DescriptorType,
                    pBufferInfo = bufferInfoPin.AddrOfPinnedObject()
                });
            }
            else if ((descriptor.DescriptorType == VulkanInterop.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER ||
                      descriptor.DescriptorType == VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE) &&
                     bound.Texture != null)
            {
                uint imageLayout = descriptor.DescriptorType == VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE
                    ? VulkanInterop.VK_IMAGE_LAYOUT_GENERAL
                    : VulkanInterop.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

                var imageInfo = new[]
                {
                    new VulkanInterop.VkDescriptorImageInfo
                    {
                        sampler = descriptor.DescriptorType == VulkanInterop.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER
                            ? _owner.DefaultSampler
                            : IntPtr.Zero,
                        imageView = bound.Texture.ViewHandle,
                        imageLayout = imageLayout
                    }
                };
                var imageInfoPin = GCHandle.Alloc(imageInfo, GCHandleType.Pinned);
                pins.Add(imageInfoPin);

                writes.Add(new VulkanInterop.VkWriteDescriptorSet
                {
                    sType = VulkanInterop.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
                    dstSet = descriptorSet,
                    dstBinding = descriptor.Binding,
                    descriptorCount = 1,
                    descriptorType = descriptor.DescriptorType,
                    pImageInfo = imageInfoPin.AddrOfPinnedObject()
                });
            }
        }

        if (writes.Count > 0)
        {
            var writesArray = writes.ToArray();
            var writesPin = GCHandle.Alloc(writesArray, GCHandleType.Pinned);
            VulkanInterop.vkUpdateDescriptorSets(_owner.Device, (uint)writesArray.Length, writesPin.AddrOfPinnedObject(), 0, IntPtr.Zero);
            writesPin.Free();
        }

        foreach (var pin in pins)
            pin.Free();
    }

    // ── Draw Commands ────────────────────────────────────────────────

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        VulkanInterop.vkCmdDraw(_commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        VulkanInterop.vkCmdDrawIndexed(_commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride)
    {
        var vkBuf = (VulkanBuffer)buffer;
        VulkanInterop.vkCmdDrawIndirect(_commandBuffer, vkBuf.Handle, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride)
    {
        var vkBuf = (VulkanBuffer)buffer;
        VulkanInterop.vkCmdDrawIndexedIndirect(_commandBuffer, vkBuf.Handle, offset, drawCount, stride);
    }

    // ── Compute ──────────────────────────────────────────────────────

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        VulkanInterop.vkCmdDispatch(_commandBuffer, groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(IRHIBuffer buffer, ulong offset)
    {
        var vkBuf = (VulkanBuffer)buffer;
        VulkanInterop.vkCmdDispatchIndirect(_commandBuffer, vkBuf.Handle, offset);
    }

    // ── Barriers ─────────────────────────────────────────────────────

    public void MemoryBarrier()
    {
        VulkanInterop.vkCmdPipelineBarrier(
            _commandBuffer,
            VulkanInterop.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            VulkanInterop.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            0, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero);
    }

    public void BufferBarrier(IRHIBuffer buffer)
    {
        var vkBuf = (VulkanBuffer)buffer;
        var barrier = new VulkanInterop.VkBufferMemoryBarrier
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,
            srcAccessMask = VulkanInterop.VK_ACCESS_SHADER_WRITE_BIT,
            dstAccessMask = VulkanInterop.VK_ACCESS_SHADER_READ_BIT,
            srcQueueFamilyIndex = ~0u,
            dstQueueFamilyIndex = ~0u,
            buffer = vkBuf.Handle,
            offset = 0,
            size = VulkanInterop.VK_WHOLE_SIZE
        };

        var pin = GCHandle.Alloc(barrier, GCHandleType.Pinned);
        VulkanInterop.vkCmdPipelineBarrier(
            _commandBuffer,
            VulkanInterop.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
            VulkanInterop.VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VulkanInterop.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
            0, 0, IntPtr.Zero,
            1, pin.AddrOfPinnedObject(),
            0, IntPtr.Zero);
        pin.Free();
    }

    public void TextureBarrier(IRHITexture texture)
    {
        var vkTex = (VulkanTexture)texture;
        TransitionImageLayout(vkTex,
            VulkanInterop.VK_IMAGE_LAYOUT_GENERAL,
            VulkanInterop.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            VulkanInterop.VK_ACCESS_MEMORY_READ_BIT | VulkanInterop.VK_ACCESS_MEMORY_WRITE_BIT);
    }

    // ── Image Layout Transition ──────────────────────────────────────

    internal void TransitionImageLayout(VulkanTexture texture, uint newLayout, uint dstStageMask, uint dstAccessMask)
    {
        if (texture.CurrentLayout == newLayout) return;

        uint srcStageMask = VulkanInterop.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
        uint srcAccessMask = VulkanInterop.VK_ACCESS_MEMORY_WRITE_BIT;

        bool isDepth = VulkanInterop.IsDepthFormat(texture.VkFormat);

        var barrier = new VulkanInterop.VkImageMemoryBarrier
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
            srcAccessMask = srcAccessMask,
            dstAccessMask = dstAccessMask,
            oldLayout = texture.CurrentLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = ~0u,
            dstQueueFamilyIndex = ~0u,
            image = texture.ImageHandle,
            subresourceRange = new VulkanInterop.VkImageSubresourceRange
            {
                aspectMask = isDepth ? VulkanInterop.VK_IMAGE_ASPECT_DEPTH_BIT : VulkanInterop.VK_IMAGE_ASPECT_COLOR_BIT,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1
            }
        };

        var pin = GCHandle.Alloc(barrier, GCHandleType.Pinned);
        VulkanInterop.vkCmdPipelineBarrier(
            _commandBuffer,
            srcStageMask, dstStageMask,
            0, 0, IntPtr.Zero, 0, IntPtr.Zero,
            1, pin.AddrOfPinnedObject());
        pin.Free();

        texture.CurrentLayout = newLayout;
    }

    // ── On-the-fly Render Pass / Framebuffer ─────────────────────────

    private IntPtr CreateOnTheFlyRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        int colorCount = colorTargets.Length;
        bool hasDepth = depthTarget != null;
        int totalAttach = colorCount + (hasDepth ? 1 : 0);

        var attachments = new VulkanInterop.VkAttachmentDescription[totalAttach];
        var colorRefs = new VulkanInterop.VkAttachmentReference[colorCount];

        uint loadOp = clearValue.LoadInsteadOfClear
            ? VulkanInterop.VK_ATTACHMENT_LOAD_OP_LOAD
            : VulkanInterop.VK_ATTACHMENT_LOAD_OP_CLEAR;

        for (int i = 0; i < colorCount; i++)
        {
            var vkTex = (VulkanTexture)colorTargets[i];
            attachments[i] = new VulkanInterop.VkAttachmentDescription
            {
                format = vkTex.VkFormat,
                samples = VulkanInterop.VK_SAMPLE_COUNT_1_BIT,
                loadOp = loadOp,
                storeOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_STORE,
                stencilLoadOp = VulkanInterop.VK_ATTACHMENT_LOAD_OP_DONT_CARE,
                stencilStoreOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_DONT_CARE,
                initialLayout = clearValue.LoadInsteadOfClear
                    ? VulkanInterop.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
                    : VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED,
                finalLayout = VulkanInterop.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
            };
            colorRefs[i] = new VulkanInterop.VkAttachmentReference
            {
                attachment = (uint)i,
                layout = VulkanInterop.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
            };
        }

        var depthRef = new VulkanInterop.VkAttachmentReference();
        GCHandle depthRefPin = default;
        IntPtr depthRefPtr = IntPtr.Zero;

        if (hasDepth)
        {
            var vkDepth = (VulkanTexture)depthTarget!;
            attachments[colorCount] = new VulkanInterop.VkAttachmentDescription
            {
                format = vkDepth.VkFormat,
                samples = VulkanInterop.VK_SAMPLE_COUNT_1_BIT,
                loadOp = clearValue.LoadInsteadOfClear
                    ? VulkanInterop.VK_ATTACHMENT_LOAD_OP_LOAD
                    : VulkanInterop.VK_ATTACHMENT_LOAD_OP_CLEAR,
                storeOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_STORE,
                stencilLoadOp = VulkanInterop.VK_ATTACHMENT_LOAD_OP_DONT_CARE,
                stencilStoreOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_DONT_CARE,
                initialLayout = clearValue.LoadInsteadOfClear
                    ? VulkanInterop.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL
                    : VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED,
                finalLayout = VulkanInterop.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL
            };
            depthRef = new VulkanInterop.VkAttachmentReference
            {
                attachment = (uint)colorCount,
                layout = VulkanInterop.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL
            };
            depthRefPin = GCHandle.Alloc(depthRef, GCHandleType.Pinned);
            depthRefPtr = depthRefPin.AddrOfPinnedObject();
        }

        var colorRefsPin = GCHandle.Alloc(colorRefs, GCHandleType.Pinned);

        var subpass = new VulkanInterop.VkSubpassDescription
        {
            pipelineBindPoint = VulkanInterop.VK_PIPELINE_BIND_POINT_GRAPHICS,
            colorAttachmentCount = (uint)colorCount,
            pColorAttachments = colorRefsPin.AddrOfPinnedObject(),
            pDepthStencilAttachment = depthRefPtr
        };

        var dependency = new VulkanInterop.VkSubpassDependency
        {
            srcSubpass = VulkanInterop.VK_SUBPASS_EXTERNAL,
            dstSubpass = 0,
            srcStageMask = VulkanInterop.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
                          VulkanInterop.VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT,
            dstStageMask = VulkanInterop.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
                          VulkanInterop.VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT,
            dstAccessMask = VulkanInterop.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                           VulkanInterop.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT
        };

        var attachPin = GCHandle.Alloc(attachments, GCHandleType.Pinned);
        var subpassPin = GCHandle.Alloc(subpass, GCHandleType.Pinned);
        var depPin = GCHandle.Alloc(dependency, GCHandleType.Pinned);

        var rpInfo = new VulkanInterop.VkRenderPassCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO,
            attachmentCount = (uint)totalAttach,
            pAttachments = attachPin.AddrOfPinnedObject(),
            subpassCount = 1,
            pSubpasses = subpassPin.AddrOfPinnedObject(),
            dependencyCount = 1,
            pDependencies = depPin.AddrOfPinnedObject()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateRenderPass(_owner.Device, ref rpInfo, IntPtr.Zero, out var renderPass),
            "vkCreateRenderPass (on-the-fly)");

        attachPin.Free(); subpassPin.Free(); depPin.Free(); colorRefsPin.Free();
        if (depthRefPin.IsAllocated) depthRefPin.Free();

        return renderPass;
    }

    private IntPtr CreateOnTheFlyFramebuffer(IntPtr renderPass, IRHITexture[] colorTargets, IRHITexture? depthTarget, uint width, uint height)
    {
        int totalViews = colorTargets.Length + (depthTarget != null ? 1 : 0);
        var views = new IntPtr[totalViews];

        for (int i = 0; i < colorTargets.Length; i++)
            views[i] = ((VulkanTexture)colorTargets[i]).ViewHandle;

        if (depthTarget != null)
            views[^1] = ((VulkanTexture)depthTarget).ViewHandle;

        var viewsPin = GCHandle.Alloc(views, GCHandleType.Pinned);

        var fbInfo = new VulkanInterop.VkFramebufferCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO,
            renderPass = renderPass,
            attachmentCount = (uint)totalViews,
            pAttachments = viewsPin.AddrOfPinnedObject(),
            width = width,
            height = height,
            layers = 1
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateFramebuffer(_owner.Device, ref fbInfo, IntPtr.Zero, out var framebuffer),
            "vkCreateFramebuffer");
        viewsPin.Free();

        return framebuffer;
    }

    public void Dispose()
    {
        // Cleanup cached framebuffers
        foreach (var fb in _framebufferCache.Values)
        {
            if (fb != IntPtr.Zero)
                VulkanInterop.vkDestroyFramebuffer(_owner.Device, fb, IntPtr.Zero);
        }
        _framebufferCache.Clear();

        foreach (var fb in _transientFramebuffers)
        {
            if (fb != IntPtr.Zero)
                VulkanInterop.vkDestroyFramebuffer(_owner.Device, fb, IntPtr.Zero);
        }
        _transientFramebuffers.Clear();

        foreach (var rp in _transientRenderPasses)
        {
            if (rp != IntPtr.Zero)
                VulkanInterop.vkDestroyRenderPass(_owner.Device, rp, IntPtr.Zero);
        }
        _transientRenderPasses.Clear();

        if (_descriptorPool != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyDescriptorPool(_owner.Device, _descriptorPool, IntPtr.Zero);
            _descriptorPool = IntPtr.Zero;
        }

        if (_commandPool != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyCommandPool(_owner.Device, _commandPool, IntPtr.Zero);
        }
    }
}
