using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NotBSRenderer.Vulkan;

internal readonly struct VulkanDescriptorBindingInfo
{
    internal readonly uint Set;
    internal readonly uint Binding;
    internal readonly uint DescriptorType;
    internal readonly uint StageFlags;

    internal VulkanDescriptorBindingInfo(uint set, uint binding, uint descriptorType, uint stageFlags)
    {
        Set = set;
        Binding = binding;
        DescriptorType = descriptorType;
        StageFlags = stageFlags;
    }
}

/// <summary>
/// Vulkan pipeline wrapping VkPipeline + VkPipelineLayout + VkRenderPass.
/// Stores the render pass and layout so VulkanCommandBuffer can reference them.
/// </summary>
internal sealed class VulkanPipeline : IRHIPipeline
{
    private readonly VulkanDevice _owner;

    internal IntPtr PipelineHandle { get; private set; }
    internal IntPtr LayoutHandle { get; private set; }
    internal IntPtr RenderPassHandle { get; private set; }
    internal bool IsCompute { get; private set; }
    internal IntPtr[] DescriptorSetLayouts { get; private set; } = Array.Empty<IntPtr>();
    internal Dictionary<ulong, VulkanDescriptorBindingInfo> DescriptorBindings { get; private set; } = new();

    private VulkanPipeline(VulkanDevice owner) { _owner = owner; }

    internal static ulong DescriptorKey(uint set, uint binding) => ((ulong)set << 32) | binding;

    /// <summary>
    /// Create a graphics pipeline from the given description.
    /// </summary>
    internal static VulkanPipeline CreateGraphics(VulkanDevice owner, GraphicsPipelineDesc desc)
    {
        var pipeline = new VulkanPipeline(owner) { IsCompute = false };

        // ── Shader Modules ────────────────────────────────────────────
        var vsModule = CreateShaderModule(owner, desc.VertexShader.Bytecode);
        var fsModule = CreateShaderModule(owner, desc.FragmentShader.Bytecode);

        var vsEntryName = Marshal.StringToHGlobalAnsi(desc.VertexShader.EntryPoint ?? "main");
        var fsEntryName = Marshal.StringToHGlobalAnsi(desc.FragmentShader.EntryPoint ?? "main");

        var stages = new VulkanInterop.VkPipelineShaderStageCreateInfo[]
        {
            new()
            {
                sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                stage = VulkanInterop.VK_SHADER_STAGE_VERTEX_BIT,
                module = vsModule,
                pName = vsEntryName
            },
            new()
            {
                sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                stage = VulkanInterop.VK_SHADER_STAGE_FRAGMENT_BIT,
                module = fsModule,
                pName = fsEntryName
            }
        };

        // ── Vertex Input ──────────────────────────────────────────────
        VulkanInterop.VkVertexInputBindingDescription[]? bindings = null;
        VulkanInterop.VkVertexInputAttributeDescription[]? attributes = null;
        GCHandle bindingsPin = default, attributesPin = default;

        var vertexInput = new VulkanInterop.VkPipelineVertexInputStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO
        };

        if (desc.VertexLayout.Bindings != null && desc.VertexLayout.Bindings.Length > 0)
        {
            bindings = new VulkanInterop.VkVertexInputBindingDescription[desc.VertexLayout.Bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                bindings[i] = new VulkanInterop.VkVertexInputBindingDescription
                {
                    binding = desc.VertexLayout.Bindings[i].Binding,
                    stride = desc.VertexLayout.Bindings[i].Stride,
                    inputRate = desc.VertexLayout.Bindings[i].PerInstance
                        ? VulkanInterop.VK_VERTEX_INPUT_RATE_INSTANCE
                        : VulkanInterop.VK_VERTEX_INPUT_RATE_VERTEX
                };
            }
            bindingsPin = GCHandle.Alloc(bindings, GCHandleType.Pinned);
            vertexInput.vertexBindingDescriptionCount = (uint)bindings.Length;
            vertexInput.pVertexBindingDescriptions = bindingsPin.AddrOfPinnedObject();
        }

        if (desc.VertexLayout.Attributes != null && desc.VertexLayout.Attributes.Length > 0)
        {
            attributes = new VulkanInterop.VkVertexInputAttributeDescription[desc.VertexLayout.Attributes.Length];
            for (int i = 0; i < attributes.Length; i++)
            {
                attributes[i] = new VulkanInterop.VkVertexInputAttributeDescription
                {
                    location = desc.VertexLayout.Attributes[i].Location,
                    binding = desc.VertexLayout.Attributes[i].Binding,
                    format = VulkanInterop.ToVkFormat(desc.VertexLayout.Attributes[i].Format),
                    offset = desc.VertexLayout.Attributes[i].Offset
                };
            }
            attributesPin = GCHandle.Alloc(attributes, GCHandleType.Pinned);
            vertexInput.vertexAttributeDescriptionCount = (uint)attributes.Length;
            vertexInput.pVertexAttributeDescriptions = attributesPin.AddrOfPinnedObject();
        }

        // ── Input Assembly ────────────────────────────────────────────
        var inputAssembly = new VulkanInterop.VkPipelineInputAssemblyStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO,
            topology = desc.Topology switch
            {
                PrimitiveTopology.TriangleList => VulkanInterop.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST,
                PrimitiveTopology.TriangleStrip => VulkanInterop.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP,
                PrimitiveTopology.LineList => VulkanInterop.VK_PRIMITIVE_TOPOLOGY_LINE_LIST,
                PrimitiveTopology.LineStrip => VulkanInterop.VK_PRIMITIVE_TOPOLOGY_LINE_STRIP,
                PrimitiveTopology.PointList => VulkanInterop.VK_PRIMITIVE_TOPOLOGY_POINT_LIST,
                _ => VulkanInterop.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST
            }
        };

        // ── Dynamic State (viewport + scissor) ────────────────────────
        var dynamicStates = new uint[]
        {
            VulkanInterop.VK_DYNAMIC_STATE_VIEWPORT,
            VulkanInterop.VK_DYNAMIC_STATE_SCISSOR
        };
        var dynamicPin = GCHandle.Alloc(dynamicStates, GCHandleType.Pinned);

        var dynamicState = new VulkanInterop.VkPipelineDynamicStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO,
            dynamicStateCount = 2,
            pDynamicStates = dynamicPin.AddrOfPinnedObject()
        };

        // ── Viewport (dynamic, just need count) ───────────────────────
        var viewportState = new VulkanInterop.VkPipelineViewportStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO,
            viewportCount = 1,
            scissorCount = 1
        };

        // ── Rasterizer ────────────────────────────────────────────────
        var rasterizer = new VulkanInterop.VkPipelineRasterizationStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO,
            depthClampEnable = desc.RasterizerState.DepthClampEnabled ? 1u : 0u,
            polygonMode = desc.RasterizerState.FillMode == FillMode.Wireframe
                ? VulkanInterop.VK_POLYGON_MODE_LINE
                : VulkanInterop.VK_POLYGON_MODE_FILL,
            cullMode = desc.RasterizerState.CullMode switch
            {
                CullMode.None => VulkanInterop.VK_CULL_MODE_NONE,
                CullMode.Front => VulkanInterop.VK_CULL_MODE_FRONT_BIT,
                CullMode.Back => VulkanInterop.VK_CULL_MODE_BACK_BIT,
                _ => VulkanInterop.VK_CULL_MODE_BACK_BIT
            },
            frontFace = desc.RasterizerState.FrontFace == FrontFace.Clockwise
                ? VulkanInterop.VK_FRONT_FACE_CLOCKWISE
                : VulkanInterop.VK_FRONT_FACE_COUNTER_CLOCKWISE,
            lineWidth = Math.Max(1.0f, desc.RasterizerState.LineWidth)
        };

        // ── Multisample (no MSAA for now) ─────────────────────────────
        var multisample = new VulkanInterop.VkPipelineMultisampleStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO,
            rasterizationSamples = VulkanInterop.VK_SAMPLE_COUNT_1_BIT
        };

        // ── Depth/Stencil ─────────────────────────────────────────────
        var depthStencil = new VulkanInterop.VkPipelineDepthStencilStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO,
            depthTestEnable = desc.DepthStencilState.DepthTestEnabled ? 1u : 0u,
            depthWriteEnable = desc.DepthStencilState.DepthWriteEnabled ? 1u : 0u,
            depthCompareOp = MapCompareOp(desc.DepthStencilState.DepthCompareOp)
        };

        // ── Color Blend ───────────────────────────────────────────────
        int colorAttachCount = desc.ColorFormats?.Length ?? 1;
        var blendAttachments = new VulkanInterop.VkPipelineColorBlendAttachmentState[colorAttachCount];
        for (int i = 0; i < colorAttachCount; i++)
        {
            blendAttachments[i] = new VulkanInterop.VkPipelineColorBlendAttachmentState
            {
                blendEnable = desc.BlendState.BlendEnabled ? 1u : 0u,
                srcColorBlendFactor = MapBlendFactor(desc.BlendState.SrcColorFactor),
                dstColorBlendFactor = MapBlendFactor(desc.BlendState.DstColorFactor),
                colorBlendOp = MapBlendOp(desc.BlendState.ColorOp),
                srcAlphaBlendFactor = MapBlendFactor(desc.BlendState.SrcAlphaFactor),
                dstAlphaBlendFactor = MapBlendFactor(desc.BlendState.DstAlphaFactor),
                alphaBlendOp = MapBlendOp(desc.BlendState.AlphaOp),
                colorWriteMask = 0xF // RGBA
            };
        }
        var blendPin = GCHandle.Alloc(blendAttachments, GCHandleType.Pinned);

        var colorBlend = new VulkanInterop.VkPipelineColorBlendStateCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO,
            attachmentCount = (uint)colorAttachCount,
            pAttachments = blendPin.AddrOfPinnedObject()
        };

        // ── Render Pass ───────────────────────────────────────────────
        pipeline.RenderPassHandle = CreateCompatibleRenderPass(owner, desc);

        // ── Pipeline Layout ───────────────────────────────────────────
        pipeline.BuildDescriptorLayouts(owner, ReflectPipelineDescriptors(desc.VertexShader, desc.FragmentShader));
        pipeline.LayoutHandle = CreatePipelineLayout(owner, pipeline.DescriptorSetLayouts, "vkCreatePipelineLayout");

        // ── Create Pipeline ───────────────────────────────────────────
        var stagesPin = GCHandle.Alloc(stages, GCHandleType.Pinned);
        var vertexInputPin = GCHandle.Alloc(vertexInput, GCHandleType.Pinned);
        var inputAssemblyPin = GCHandle.Alloc(inputAssembly, GCHandleType.Pinned);
        var viewportStatePin = GCHandle.Alloc(viewportState, GCHandleType.Pinned);
        var rasterizerPin = GCHandle.Alloc(rasterizer, GCHandleType.Pinned);
        var multisamplePin = GCHandle.Alloc(multisample, GCHandleType.Pinned);
        var depthStencilPin = GCHandle.Alloc(depthStencil, GCHandleType.Pinned);
        var colorBlendPin = GCHandle.Alloc(colorBlend, GCHandleType.Pinned);
        var dynamicStatePin = GCHandle.Alloc(dynamicState, GCHandleType.Pinned);

        var pipelineInfo = new VulkanInterop.VkGraphicsPipelineCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,
            stageCount = 2,
            pStages = stagesPin.AddrOfPinnedObject(),
            pVertexInputState = vertexInputPin.AddrOfPinnedObject(),
            pInputAssemblyState = inputAssemblyPin.AddrOfPinnedObject(),
            pViewportState = viewportStatePin.AddrOfPinnedObject(),
            pRasterizationState = rasterizerPin.AddrOfPinnedObject(),
            pMultisampleState = multisamplePin.AddrOfPinnedObject(),
            pDepthStencilState = depthStencilPin.AddrOfPinnedObject(),
            pColorBlendState = colorBlendPin.AddrOfPinnedObject(),
            pDynamicState = dynamicStatePin.AddrOfPinnedObject(),
            layout = pipeline.LayoutHandle,
            renderPass = pipeline.RenderPassHandle,
            subpass = 0,
            basePipelineIndex = -1
        };

        var pipelineInfoPin = GCHandle.Alloc(pipelineInfo, GCHandleType.Pinned);
        var pipelineHandleArr = new IntPtr[1];
        var pipelineHandlePin = GCHandle.Alloc(pipelineHandleArr, GCHandleType.Pinned);

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateGraphicsPipelines(
                owner.Device, IntPtr.Zero, 1,
                pipelineInfoPin.AddrOfPinnedObject(), IntPtr.Zero,
                pipelineHandlePin.AddrOfPinnedObject()),
            "vkCreateGraphicsPipelines");

        pipeline.PipelineHandle = pipelineHandleArr[0];

        // Cleanup pins
        pipelineHandlePin.Free(); pipelineInfoPin.Free();
        stagesPin.Free(); vertexInputPin.Free(); inputAssemblyPin.Free();
        viewportStatePin.Free(); rasterizerPin.Free(); multisamplePin.Free();
        depthStencilPin.Free(); colorBlendPin.Free(); dynamicStatePin.Free();
        dynamicPin.Free(); blendPin.Free();
        if (bindingsPin.IsAllocated) bindingsPin.Free();
        if (attributesPin.IsAllocated) attributesPin.Free();

        // Destroy shader modules (no longer needed after pipeline creation)
        VulkanInterop.vkDestroyShaderModule(owner.Device, vsModule, IntPtr.Zero);
        VulkanInterop.vkDestroyShaderModule(owner.Device, fsModule, IntPtr.Zero);
        Marshal.FreeHGlobal(vsEntryName);
        Marshal.FreeHGlobal(fsEntryName);

        Console.WriteLine($"[Vulkan] Graphics pipeline created: {desc.DebugName ?? "unnamed"}");
        return pipeline;
    }

    /// <summary>
    /// Create a compute pipeline.
    /// </summary>
    internal static VulkanPipeline CreateCompute(VulkanDevice owner, ComputePipelineDesc desc)
    {
        var pipeline = new VulkanPipeline(owner) { IsCompute = true };

        var csModule = CreateShaderModule(owner, desc.ComputeShader.Bytecode);
        var csEntryName = Marshal.StringToHGlobalAnsi(desc.ComputeShader.EntryPoint ?? "main");

        pipeline.BuildDescriptorLayouts(owner, ReflectShaderDescriptors(desc.ComputeShader, VulkanInterop.VK_SHADER_STAGE_COMPUTE_BIT));
        pipeline.LayoutHandle = CreatePipelineLayout(owner, pipeline.DescriptorSetLayouts, "vkCreatePipelineLayout (compute)");

        var computeInfo = new VulkanInterop.VkComputePipelineCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO,
            stage = new VulkanInterop.VkPipelineShaderStageCreateInfo
            {
                sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                stage = VulkanInterop.VK_SHADER_STAGE_COMPUTE_BIT,
                module = csModule,
                pName = csEntryName
            },
            layout = pipeline.LayoutHandle,
            basePipelineIndex = -1
        };

        var infoPin = GCHandle.Alloc(computeInfo, GCHandleType.Pinned);
        var handleArr = new IntPtr[1];
        var handlePin = GCHandle.Alloc(handleArr, GCHandleType.Pinned);

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateComputePipelines(
                owner.Device, IntPtr.Zero, 1,
                infoPin.AddrOfPinnedObject(), IntPtr.Zero,
                handlePin.AddrOfPinnedObject()),
            "vkCreateComputePipelines");

        pipeline.PipelineHandle = handleArr[0];
        infoPin.Free(); handlePin.Free();

        VulkanInterop.vkDestroyShaderModule(owner.Device, csModule, IntPtr.Zero);
        Marshal.FreeHGlobal(csEntryName);

        Console.WriteLine($"[Vulkan] Compute pipeline created: {desc.DebugName ?? "unnamed"}");
        return pipeline;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static IntPtr CreateShaderModule(VulkanDevice owner, byte[] bytecode)
    {
        if (bytecode.Length == 0)
            throw new InvalidOperationException("[Vulkan] Shader bytecode is empty. Vulkan pipelines require SPIR-V (.spv) bytecode.");

        var pin = GCHandle.Alloc(bytecode, GCHandleType.Pinned);
        var createInfo = new VulkanInterop.VkShaderModuleCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,
            codeSize = (nuint)bytecode.Length,
            pCode = pin.AddrOfPinnedObject()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateShaderModule(owner.Device, ref createInfo, IntPtr.Zero, out var module),
            "vkCreateShaderModule");
        pin.Free();
        return module;
    }

    private void BuildDescriptorLayouts(VulkanDevice owner, IEnumerable<VulkanDescriptorBindingInfo> descriptors)
    {
        DescriptorBindings = new Dictionary<ulong, VulkanDescriptorBindingInfo>();

        foreach (var descriptor in descriptors)
        {
            ulong key = DescriptorKey(descriptor.Set, descriptor.Binding);
            if (DescriptorBindings.TryGetValue(key, out var existing))
            {
                if (existing.DescriptorType != descriptor.DescriptorType)
                {
                    Console.WriteLine(
                        $"[Vulkan] Descriptor type conflict at set={descriptor.Set} binding={descriptor.Binding}; keeping first shader declaration.");
                    DescriptorBindings[key] = new VulkanDescriptorBindingInfo(
                        existing.Set, existing.Binding, existing.DescriptorType,
                        existing.StageFlags | descriptor.StageFlags);
                }
                else
                {
                    DescriptorBindings[key] = new VulkanDescriptorBindingInfo(
                        existing.Set, existing.Binding, existing.DescriptorType,
                        existing.StageFlags | descriptor.StageFlags);
                }
            }
            else
            {
                DescriptorBindings[key] = descriptor;
            }
        }

        if (DescriptorBindings.Count == 0)
        {
            DescriptorSetLayouts = Array.Empty<IntPtr>();
            return;
        }

        uint maxSet = DescriptorBindings.Values.Max(binding => binding.Set);
        DescriptorSetLayouts = new IntPtr[maxSet + 1];

        for (uint set = 0; set <= maxSet; set++)
        {
            var setBindings = DescriptorBindings.Values
                .Where(binding => binding.Set == set)
                .OrderBy(binding => binding.Binding)
                .Select(binding => new VulkanInterop.VkDescriptorSetLayoutBinding
                {
                    binding = binding.Binding,
                    descriptorType = binding.DescriptorType,
                    descriptorCount = 1,
                    stageFlags = binding.StageFlags
                })
                .ToArray();

            GCHandle bindingPin = default;
            var layoutInfo = new VulkanInterop.VkDescriptorSetLayoutCreateInfo
            {
                sType = VulkanInterop.VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,
                bindingCount = (uint)setBindings.Length,
                pBindings = IntPtr.Zero
            };

            if (setBindings.Length > 0)
            {
                bindingPin = GCHandle.Alloc(setBindings, GCHandleType.Pinned);
                layoutInfo.pBindings = bindingPin.AddrOfPinnedObject();
            }

            VulkanInterop.VkCheck(
                VulkanInterop.vkCreateDescriptorSetLayout(owner.Device, ref layoutInfo, IntPtr.Zero, out var layout),
                "vkCreateDescriptorSetLayout");

            if (bindingPin.IsAllocated) bindingPin.Free();
            DescriptorSetLayouts[set] = layout;
        }
    }

    private static IntPtr CreatePipelineLayout(VulkanDevice owner, IntPtr[] descriptorSetLayouts, string operation)
    {
        GCHandle layoutPin = default;
        var layoutInfo = new VulkanInterop.VkPipelineLayoutCreateInfo
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
            setLayoutCount = (uint)descriptorSetLayouts.Length,
            pSetLayouts = IntPtr.Zero
        };

        if (descriptorSetLayouts.Length > 0)
        {
            layoutPin = GCHandle.Alloc(descriptorSetLayouts, GCHandleType.Pinned);
            layoutInfo.pSetLayouts = layoutPin.AddrOfPinnedObject();
        }

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreatePipelineLayout(owner.Device, ref layoutInfo, IntPtr.Zero, out var pipelineLayout),
            operation);

        if (layoutPin.IsAllocated) layoutPin.Free();
        return pipelineLayout;
    }

    private static IEnumerable<VulkanDescriptorBindingInfo> ReflectPipelineDescriptors(
        ShaderDesc vertexShader,
        ShaderDesc fragmentShader)
    {
        foreach (var descriptor in ReflectShaderDescriptors(vertexShader, VulkanInterop.VK_SHADER_STAGE_VERTEX_BIT))
            yield return descriptor;
        foreach (var descriptor in ReflectShaderDescriptors(fragmentShader, VulkanInterop.VK_SHADER_STAGE_FRAGMENT_BIT))
            yield return descriptor;
    }

    private static IEnumerable<VulkanDescriptorBindingInfo> ReflectShaderDescriptors(ShaderDesc shader, uint stageFlags)
    {
        const uint SpvMagic = 0x07230203;
        const ushort OpTypeImage = 25;
        const ushort OpTypeSampledImage = 27;
        const ushort OpTypePointer = 32;
        const ushort OpVariable = 59;
        const ushort OpDecorate = 71;
        const uint DecorationBlock = 2;
        const uint DecorationBufferBlock = 3;
        const uint DecorationBinding = 33;
        const uint DecorationDescriptorSet = 34;
        const uint StorageClassUniformConstant = 0;
        const uint StorageClassUniform = 2;
        const uint StorageClassStorageBuffer = 12;

        if (shader.Bytecode.Length < 20 || shader.Bytecode.Length % 4 != 0)
            yield break;

        var words = MemoryMarshal.Cast<byte, uint>(shader.Bytecode);
        if (words.Length < 5 || words[0] != SpvMagic)
            yield break;

        var bindingById = new Dictionary<uint, uint>();
        var setById = new Dictionary<uint, uint>();
        var variableType = new Dictionary<uint, uint>();
        var variableStorage = new Dictionary<uint, uint>();
        var pointerPointee = new Dictionary<uint, uint>();
        var imageSampled = new Dictionary<uint, uint>();
        var sampledImageTypes = new HashSet<uint>();
        var blockTypes = new HashSet<uint>();
        var bufferBlockTypes = new HashSet<uint>();

        int offset = 5;
        while (offset < words.Length)
        {
            uint instruction = words[offset];
            ushort op = (ushort)(instruction & 0xFFFF);
            int wordCount = (int)(instruction >> 16);
            if (wordCount <= 0 || offset + wordCount > words.Length)
                yield break;

            switch (op)
            {
                case OpDecorate when wordCount >= 3:
                {
                    uint target = words[offset + 1];
                    uint decoration = words[offset + 2];
                    if (decoration == DecorationBinding && wordCount >= 4)
                        bindingById[target] = words[offset + 3];
                    else if (decoration == DecorationDescriptorSet && wordCount >= 4)
                        setById[target] = words[offset + 3];
                    else if (decoration == DecorationBlock)
                        blockTypes.Add(target);
                    else if (decoration == DecorationBufferBlock)
                        bufferBlockTypes.Add(target);
                    break;
                }
                case OpVariable when wordCount >= 4:
                {
                    uint resultType = words[offset + 1];
                    uint resultId = words[offset + 2];
                    uint storageClass = words[offset + 3];
                    variableType[resultId] = resultType;
                    variableStorage[resultId] = storageClass;
                    break;
                }
                case OpTypePointer when wordCount >= 4:
                {
                    uint resultId = words[offset + 1];
                    uint pointeeType = words[offset + 3];
                    pointerPointee[resultId] = pointeeType;
                    break;
                }
                case OpTypeImage when wordCount >= 8:
                {
                    uint resultId = words[offset + 1];
                    uint sampled = words[offset + 7];
                    imageSampled[resultId] = sampled;
                    break;
                }
                case OpTypeSampledImage when wordCount >= 3:
                    sampledImageTypes.Add(words[offset + 1]);
                    break;
            }

            offset += wordCount;
        }

        foreach (var (variableId, binding) in bindingById)
        {
            if (!variableType.TryGetValue(variableId, out uint resultType) ||
                !variableStorage.TryGetValue(variableId, out uint storageClass))
                continue;

            uint pointeeType = pointerPointee.TryGetValue(resultType, out uint pointee)
                ? pointee
                : resultType;

            uint descriptorType = storageClass switch
            {
                StorageClassUniformConstant when sampledImageTypes.Contains(pointeeType) =>
                    VulkanInterop.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
                StorageClassUniformConstant when imageSampled.TryGetValue(pointeeType, out uint sampled) && sampled == 2 =>
                    VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
                StorageClassUniformConstant =>
                    VulkanInterop.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
                StorageClassStorageBuffer =>
                    VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
                StorageClassUniform when bufferBlockTypes.Contains(pointeeType) =>
                    VulkanInterop.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
                StorageClassUniform when blockTypes.Contains(pointeeType) || !bufferBlockTypes.Contains(pointeeType) =>
                    VulkanInterop.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,
                _ => 0
            };

            if (descriptorType == 0)
                continue;

            uint set = setById.TryGetValue(variableId, out uint reflectedSet) ? reflectedSet : 0;
            yield return new VulkanDescriptorBindingInfo(set, binding, descriptorType, stageFlags);
        }
    }

    private static IntPtr CreateCompatibleRenderPass(VulkanDevice owner, GraphicsPipelineDesc desc)
    {
        int colorCount = desc.ColorFormats?.Length ?? 0;
        bool hasDepth = desc.DepthFormat.HasValue;
        int totalAttach = colorCount + (hasDepth ? 1 : 0);

        if (totalAttach == 0)
        {
            // Fallback: single BGRA8 color attachment
            colorCount = 1;
            totalAttach = 1;
        }

        var attachments = new VulkanInterop.VkAttachmentDescription[totalAttach];
        var colorRefs = new VulkanInterop.VkAttachmentReference[colorCount];

        for (int i = 0; i < colorCount; i++)
        {
            uint fmt = (desc.ColorFormats != null && i < desc.ColorFormats.Length)
                ? VulkanInterop.ToVkFormat(desc.ColorFormats[i])
                : VulkanInterop.VK_FORMAT_B8G8R8A8_SRGB;

            attachments[i] = new VulkanInterop.VkAttachmentDescription
            {
                format = fmt,
                samples = VulkanInterop.VK_SAMPLE_COUNT_1_BIT,
                loadOp = VulkanInterop.VK_ATTACHMENT_LOAD_OP_CLEAR,
                storeOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_STORE,
                stencilLoadOp = VulkanInterop.VK_ATTACHMENT_LOAD_OP_DONT_CARE,
                stencilStoreOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_DONT_CARE,
                initialLayout = VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED,
                finalLayout = VulkanInterop.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
            };

            colorRefs[i] = new VulkanInterop.VkAttachmentReference
            {
                attachment = (uint)i,
                layout = VulkanInterop.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
            };
        }

        var depthRef = new VulkanInterop.VkAttachmentReference();
        IntPtr depthRefPtr = IntPtr.Zero;
        GCHandle depthRefPin = default;

        if (hasDepth)
        {
            int depthIdx = colorCount;
            attachments[depthIdx] = new VulkanInterop.VkAttachmentDescription
            {
                format = VulkanInterop.ToVkFormat(desc.DepthFormat!.Value),
                samples = VulkanInterop.VK_SAMPLE_COUNT_1_BIT,
                loadOp = VulkanInterop.VK_ATTACHMENT_LOAD_OP_CLEAR,
                storeOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_STORE,
                stencilLoadOp = VulkanInterop.VK_ATTACHMENT_LOAD_OP_DONT_CARE,
                stencilStoreOp = VulkanInterop.VK_ATTACHMENT_STORE_OP_DONT_CARE,
                initialLayout = VulkanInterop.VK_IMAGE_LAYOUT_UNDEFINED,
                finalLayout = VulkanInterop.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL
            };

            depthRef = new VulkanInterop.VkAttachmentReference
            {
                attachment = (uint)depthIdx,
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
            srcAccessMask = 0,
            dstAccessMask = VulkanInterop.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                           VulkanInterop.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT
        };

        var attachPin = GCHandle.Alloc(attachments, GCHandleType.Pinned);
        var subpassPin = GCHandle.Alloc(subpass, GCHandleType.Pinned);
        var depPin = GCHandle.Alloc(dependency, GCHandleType.Pinned);

        var renderPassInfo = new VulkanInterop.VkRenderPassCreateInfo
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
            VulkanInterop.vkCreateRenderPass(owner.Device, ref renderPassInfo, IntPtr.Zero, out var renderPass),
            "vkCreateRenderPass");

        attachPin.Free(); subpassPin.Free(); depPin.Free(); colorRefsPin.Free();
        if (depthRefPin.IsAllocated) depthRefPin.Free();

        return renderPass;
    }

    private static uint MapCompareOp(CompareOp op) => op switch
    {
        CompareOp.Never => VulkanInterop.VK_COMPARE_OP_NEVER,
        CompareOp.Less => VulkanInterop.VK_COMPARE_OP_LESS,
        CompareOp.Equal => VulkanInterop.VK_COMPARE_OP_EQUAL,
        CompareOp.LessOrEqual => VulkanInterop.VK_COMPARE_OP_LESS_OR_EQUAL,
        CompareOp.Greater => VulkanInterop.VK_COMPARE_OP_GREATER,
        CompareOp.NotEqual => VulkanInterop.VK_COMPARE_OP_NOT_EQUAL,
        CompareOp.GreaterOrEqual => VulkanInterop.VK_COMPARE_OP_GREATER_OR_EQUAL,
        CompareOp.Always => VulkanInterop.VK_COMPARE_OP_ALWAYS,
        _ => VulkanInterop.VK_COMPARE_OP_LESS
    };

    private static uint MapBlendFactor(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => VulkanInterop.VK_BLEND_FACTOR_ZERO,
        BlendFactor.One => VulkanInterop.VK_BLEND_FACTOR_ONE,
        BlendFactor.SrcColor => VulkanInterop.VK_BLEND_FACTOR_SRC_COLOR,
        BlendFactor.OneMinusSrcColor => VulkanInterop.VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR,
        BlendFactor.DstColor => VulkanInterop.VK_BLEND_FACTOR_DST_COLOR,
        BlendFactor.OneMinusDstColor => VulkanInterop.VK_BLEND_FACTOR_ONE_MINUS_DST_COLOR,
        BlendFactor.SrcAlpha => VulkanInterop.VK_BLEND_FACTOR_SRC_ALPHA,
        BlendFactor.OneMinusSrcAlpha => VulkanInterop.VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA,
        BlendFactor.DstAlpha => VulkanInterop.VK_BLEND_FACTOR_DST_ALPHA,
        BlendFactor.OneMinusDstAlpha => VulkanInterop.VK_BLEND_FACTOR_ONE_MINUS_DST_ALPHA,
        _ => VulkanInterop.VK_BLEND_FACTOR_ONE
    };

    private static uint MapBlendOp(BlendOp op) => op switch
    {
        BlendOp.Add => VulkanInterop.VK_BLEND_OP_ADD,
        BlendOp.Subtract => VulkanInterop.VK_BLEND_OP_SUBTRACT,
        BlendOp.ReverseSubtract => VulkanInterop.VK_BLEND_OP_REVERSE_SUBTRACT,
        BlendOp.Min => VulkanInterop.VK_BLEND_OP_MIN,
        BlendOp.Max => VulkanInterop.VK_BLEND_OP_MAX,
        _ => VulkanInterop.VK_BLEND_OP_ADD
    };

    public void Dispose()
    {
        if (PipelineHandle != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyPipeline(_owner.Device, PipelineHandle, IntPtr.Zero);
            PipelineHandle = IntPtr.Zero;
        }
        if (LayoutHandle != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyPipelineLayout(_owner.Device, LayoutHandle, IntPtr.Zero);
            LayoutHandle = IntPtr.Zero;
        }
        if (RenderPassHandle != IntPtr.Zero)
        {
            VulkanInterop.vkDestroyRenderPass(_owner.Device, RenderPassHandle, IntPtr.Zero);
            RenderPassHandle = IntPtr.Zero;
        }
        foreach (var layout in DescriptorSetLayouts)
        {
            if (layout != IntPtr.Zero)
                VulkanInterop.vkDestroyDescriptorSetLayout(_owner.Device, layout, IntPtr.Zero);
        }
        DescriptorSetLayouts = Array.Empty<IntPtr>();
        DescriptorBindings.Clear();
    }
}
