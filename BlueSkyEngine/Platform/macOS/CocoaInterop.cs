using System;
using System.Runtime.InteropServices;

namespace BlueSky.Platform.macOS;

/// <summary>
/// Objective-C runtime interop for Cocoa APIs.
/// </summary>
public static class CocoaInterop
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const string AppKitFramework = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string QuartzCoreFramework = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";
    
    static CocoaInterop()
    {
        // Load AppKit and QuartzCore frameworks
        NativeLibrary.Load(AppKitFramework);
        NativeLibrary.Load(QuartzCoreFramework);
    }
    
    // Objective-C Runtime
    
    [DllImport(ObjCLib)]
    public static extern IntPtr objc_getClass(string name);
    
    [DllImport(ObjCLib)]
    public static extern IntPtr sel_registerName(string name);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ret_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool value);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_ptr(IntPtr receiver, IntPtr selector, IntPtr value);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_ulong(IntPtr receiver, IntPtr selector, ulong value);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_rect(IntPtr receiver, IntPtr selector, CGRect rect);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_rect(IntPtr receiver, IntPtr selector, CGRect rect);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_CGSize(IntPtr receiver, IntPtr selector, CGSize size);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_uint(IntPtr receiver, IntPtr selector, uint value);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_int(IntPtr receiver, IntPtr selector, int value);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern ushort objc_msgSend_ushort(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector, CGRect rect);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern double objc_msgSend_double(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_double(IntPtr receiver, IntPtr selector, double value);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend_ptr_rect_uint_uint_bool(
        IntPtr receiver, IntPtr selector, CGRect rect, uint styleMask, uint backing, bool defer);
    
    // Helper Methods
    
    public static IntPtr GetClass(string name)
    {
        var cls = objc_getClass(name);
        if (cls == IntPtr.Zero)
            throw new Exception($"Failed to get Objective-C class: {name}");
        return cls;
    }
    
    public static IntPtr GetSelector(string name)
    {
        var sel = sel_registerName(name);
        if (sel == IntPtr.Zero)
            throw new Exception($"Failed to register selector: {name}");
        return sel;
    }
    
    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
        {
            var releaseSel = GetSelector("release");
            objc_msgSend_void(obj, releaseSel);
        }
    }
    
    public static IntPtr Retain(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return IntPtr.Zero;
        var retainSel = GetSelector("retain");
        return objc_msgSend(obj, retainSel);
    }
    
    // Structures
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
        
        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
        
        public CGSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;

        public CGRect(double x, double y, double width, double height)
        {
            Origin = new CGPoint(x, y);
            Size = new CGSize(width, height);
        }
    }

    // NSWindow Style Masks
    public const uint NSWindowStyleMaskBorderless = 0;
    public const uint NSWindowStyleMaskTitled = 1 << 0;
    public const uint NSWindowStyleMaskClosable = 1 << 1;
    public const uint NSWindowStyleMaskMiniaturizable = 1 << 2;
    public const uint NSWindowStyleMaskResizable = 1 << 3;
    
    // NSBackingStoreType
    public const uint NSBackingStoreBuffered = 2;
    
    // CAMetalLayer Pixel Formats
    public const ulong MTLPixelFormatBGRA8Unorm = 80;
}
