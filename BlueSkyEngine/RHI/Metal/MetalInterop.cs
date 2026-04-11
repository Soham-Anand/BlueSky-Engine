using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace NotBSRenderer.Metal;

internal static class MetalInterop
{
    private static readonly ConcurrentDictionary<string, IntPtr> _selectorCache = new();

    // Objective-C runtime
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    public static IntPtr GetSelector(string name)
    {
        if (_selectorCache.TryGetValue(name, out var cached))
            return cached;
        
        var ptr = sel_registerName(name);
        _selectorCache[name] = ptr;
        return ptr;
    }
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ulong(IntPtr receiver, IntPtr selector, ulong arg1);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ptr_ulong(IntPtr receiver, IntPtr selector, ulong arg1);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ptr_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ptr_ref(IntPtr receiver, IntPtr selector, IntPtr arg1, ref IntPtr error);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern ulong objc_msgSend_ret_ulong(IntPtr receiver, IntPtr selector);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_ulong(IntPtr receiver, IntPtr selector, ulong arg1);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    public static extern CGSize objc_msgSend_CGSize(IntPtr receiver, IntPtr selector);
    
    // Metal pixel formats (matching MTLPixelFormat enum)
    public const ulong MTLPixelFormatInvalid = 0;
    public const ulong MTLPixelFormatR8Unorm = 10;
    public const ulong MTLPixelFormatBGRA8Unorm = 80;
    public const ulong MTLPixelFormatBGRA8Unorm_sRGB = 81;
    public const ulong MTLPixelFormatRGBA8Unorm = 70;
    public const ulong MTLPixelFormatRGBA8Unorm_sRGB = 71;
    public const ulong MTLPixelFormatDepth32Float = 252;
    public const ulong MTLPixelFormatDepth24Unorm_Stencil8 = 255;
    public const ulong MTLPixelFormatR32Float = 28;
    public const ulong MTLPixelFormatRGBA16Float = 115;
    public const ulong MTLPixelFormatRGBA32Float = 125;
    
    // MTLTextureUsage
    public const ulong MTLTextureUsageUnknown = 0;
    public const ulong MTLTextureUsageShaderRead = 1;
    public const ulong MTLTextureUsageShaderWrite = 2;
    public const ulong MTLTextureUsageRenderTarget = 4;
    
    // Metal resource options
    public const ulong MTLResourceStorageModeShared = 0;
    public const ulong MTLResourceStorageModeManaged = 1 << 4;
    public const ulong MTLResourceStorageModePrivate = 2 << 4;
    public const ulong MTLResourceCPUCacheModeDefaultCache = 0;
    public const ulong MTLResourceCPUCacheModeWriteCombined = 1;
    
    // Metal load/store actions
    public const ulong MTLLoadActionDontCare = 0;
    public const ulong MTLLoadActionLoad = 1;
    public const ulong MTLLoadActionClear = 2;
    
    public const ulong MTLStoreActionDontCare = 0;
    public const ulong MTLStoreActionStore = 1;
    
    // Metal primitive types
    public const ulong MTLPrimitiveTypePoint = 0;
    public const ulong MTLPrimitiveTypeLine = 1;
    public const ulong MTLPrimitiveTypeLineStrip = 2;
    public const ulong MTLPrimitiveTypeTriangle = 3;
    public const ulong MTLPrimitiveTypeTriangleStrip = 4;
    
    // Metal index types
    public const ulong MTLIndexTypeUInt16 = 0;
    public const ulong MTLIndexTypeUInt32 = 1;
    
    // Metal compare functions
    public const ulong MTLCompareFunctionNever = 0;
    public const ulong MTLCompareFunctionLess = 1;
    public const ulong MTLCompareFunctionEqual = 2;
    public const ulong MTLCompareFunctionLessEqual = 3;
    public const ulong MTLCompareFunctionGreater = 4;
    public const ulong MTLCompareFunctionNotEqual = 5;
    public const ulong MTLCompareFunctionGreaterEqual = 6;
    public const ulong MTLCompareFunctionAlways = 7;
    
    // Metal blend factors
    public const ulong MTLBlendFactorZero = 0;
    public const ulong MTLBlendFactorOne = 1;
    public const ulong MTLBlendFactorSourceColor = 2;
    public const ulong MTLBlendFactorOneMinusSourceColor = 3;
    public const ulong MTLBlendFactorSourceAlpha = 4;
    public const ulong MTLBlendFactorOneMinusSourceAlpha = 5;
    public const ulong MTLBlendFactorDestinationColor = 6;
    public const ulong MTLBlendFactorOneMinusDestinationColor = 7;
    public const ulong MTLBlendFactorDestinationAlpha = 8;
    public const ulong MTLBlendFactorOneMinusDestinationAlpha = 9;
    
    // Metal blend operations
    public const ulong MTLBlendOperationAdd = 0;
    public const ulong MTLBlendOperationSubtract = 1;
    public const ulong MTLBlendOperationReverseSubtract = 2;
    public const ulong MTLBlendOperationMin = 3;
    public const ulong MTLBlendOperationMax = 4;
    
    // Metal cull modes
    public const ulong MTLCullModeNone = 0;
    public const ulong MTLCullModeFront = 1;
    public const ulong MTLCullModeBack = 2;
    
    // Metal winding
    public const ulong MTLWindingClockwise = 0;
    public const ulong MTLWindingCounterClockwise = 1;
    
    // Helper methods
    public static IntPtr Retain(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return obj;
        var sel = GetSelector("retain");
        return objc_msgSend(obj, sel);
    }
    
    public static void Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return;
        var sel = GetSelector("release");
        objc_msgSend_void(obj, sel);
    }
    
    public static IntPtr CreateNSString(string str)
    {
        var nsStringClass = GetClass("NSString");
        var allocSel = GetSelector("alloc");
        var initWithUTF8Sel = GetSelector("initWithUTF8String:");
        var nsString = objc_msgSend(nsStringClass, allocSel);
        
        unsafe
        {
            fixed (byte* utf8Ptr = System.Text.Encoding.UTF8.GetBytes(str + "\0"))
            {
                nsString = objc_msgSend_ptr(nsString, initWithUTF8Sel, (IntPtr)utf8Ptr);
            }
        }
        
        return nsString;
    }
    
    public static ulong ToMTLPixelFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => MTLPixelFormatR8Unorm,
            TextureFormat.RGBA8Unorm => MTLPixelFormatRGBA8Unorm,
            TextureFormat.RGBA8Srgb => MTLPixelFormatRGBA8Unorm_sRGB,
            TextureFormat.BGRA8Unorm => MTLPixelFormatBGRA8Unorm,
            TextureFormat.BGRA8Srgb => MTLPixelFormatBGRA8Unorm_sRGB,
            TextureFormat.Depth32Float => MTLPixelFormatDepth32Float,
            TextureFormat.Depth24Stencil8 => MTLPixelFormatDepth24Unorm_Stencil8,
            TextureFormat.R32Float => MTLPixelFormatR32Float,
            TextureFormat.RGBA16Float => MTLPixelFormatRGBA16Float,
            TextureFormat.RGBA32Float => MTLPixelFormatRGBA32Float,
            _ => throw new NotSupportedException($"Texture format {format} not supported on Metal")
        };
    }
    
    public static ulong ToMTLPrimitiveType(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.PointList => MTLPrimitiveTypePoint,
            PrimitiveTopology.LineList => MTLPrimitiveTypeLine,
            PrimitiveTopology.LineStrip => MTLPrimitiveTypeLineStrip,
            PrimitiveTopology.TriangleList => MTLPrimitiveTypeTriangle,
            PrimitiveTopology.TriangleStrip => MTLPrimitiveTypeTriangleStrip,
            _ => throw new NotSupportedException($"Topology {topology} not supported")
        };
    }
    
    public static ulong ToMTLIndexType(IndexType indexType)
    {
        return indexType switch
        {
            IndexType.UInt16 => MTLIndexTypeUInt16,
            IndexType.UInt32 => MTLIndexTypeUInt32,
            _ => throw new NotSupportedException($"Index type {indexType} not supported")
        };
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double width;
        public double height;
    }
}
