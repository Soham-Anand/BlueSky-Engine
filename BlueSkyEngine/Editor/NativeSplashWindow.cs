using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using BlueSky.Platform.Windows;

namespace BlueSky.Editor;

public class NativeSplashWindow : IDisposable
{
    private IntPtr _nsWindow;
    private bool _isShowing = false;

    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private const uint NSWindowStyleMaskBorderless = 0;
    private const uint NSBackingStoreBuffered = 2;

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr GetSelector(string name);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_rect_ulong_ulong_byte(IntPtr receiver, IntPtr selector, CGRect rect, ulong styleMask, ulong backing, byte defer);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_rect(IntPtr receiver, IntPtr selector, CGRect rect);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_byte(IntPtr receiver, IntPtr selector, byte value);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_rect(IntPtr receiver, IntPtr selector, CGRect rect);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_ulong_ptr_ptr_byte(IntPtr receiver, IntPtr selector, ulong mask, IntPtr untilDate, IntPtr mode, byte dequeue);
    
    [DllImport("/usr/lib/libdl.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen(string path, int mode);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
        
        public CGRect(double x, double y, double width, double height)
        {
            Origin = new CGPoint(x, y);
            Size = new CGSize(width, height);
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
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
    private struct CGSize
    {
        public double Width;
        public double Height;
        
        public CGSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }
    
    private string GetSplashPath()
    {
        string[] possiblePaths = new[]
        {
            "../../../../Assets/splash.png",
            "../../../Assets/splash.png",
            "Assets/splash.png",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Assets", "splash.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "splash.png")
        };
        
        foreach (var path in possiblePaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                Console.WriteLine($"[NativeSplash] Found at: {fullPath}");
                return fullPath;
            }
        }
        
        return "";
    }
    
    private IntPtr CreateNSString(string str)
    {
        var nsStringClass = GetClass("NSString");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("initWithUTF8String:");
        
        var nsString = objc_msgSend(nsStringClass, allocSel);
        var utf8Ptr = Marshal.StringToHGlobalAnsi(str);
        nsString = objc_msgSend_ptr(nsString, initSel, utf8Ptr);
        Marshal.FreeHGlobal(utf8Ptr);
        
        return nsString;
    }
    
    private IntPtr GetSharedApplication()
    {
        var nsAppClass = GetClass("NSApplication");
        var sharedAppSel = GetSelector("sharedApplication");
        return objc_msgSend(nsAppClass, sharedAppSel);
    }
    
    public void ShowAndWait(int durationMs = 2000)
    {
        string splashPath = GetSplashPath();

        if (string.IsNullOrEmpty(splashPath) || !File.Exists(splashPath))
        {
            Console.WriteLine("[NativeSplash] splash.png not found, skipping");
            return;
        }

        if (IsWindows)
        {
            ShowAndWaitWin32(splashPath, durationMs);
            return;
        }

        if (!IsMacOS)
        {
            Console.WriteLine("[NativeSplash] Unsupported platform, skipping");
            return;
        }

        try
        {
            dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 2); // RTLD_NOW
            
            // Set activation policy
            var nsApp = GetSharedApplication();
            var setActivationPolicySel = GetSelector("setActivationPolicy:");
            objc_msgSend_ptr(nsApp, setActivationPolicySel, IntPtr.Zero);
            
            // Load image
            var nsImageClass = GetClass("NSImage");
            if (nsImageClass == IntPtr.Zero) Console.WriteLine("[NativeSplash] NSImage class is NULL");
            var allocSel = GetSelector("alloc");
            var initByRefSel = GetSelector("initWithContentsOfFile:");
            
            var pathString = CreateNSString(splashPath);
            var nsImage = objc_msgSend(nsImageClass, allocSel);
            nsImage = objc_msgSend_ptr(nsImage, initByRefSel, pathString);
            
            if (nsImage == IntPtr.Zero)
            {
                Console.WriteLine("[NativeSplash] Failed to load image");
                return;
            }
            
            // Get image size
            var sizeSel = GetSelector("size");
            var sizePtr = objc_msgSend(nsImage, sizeSel);
            
            // Default to 800x450 if we can't get size
            double imgWidth = 800;
            double imgHeight = 450;
            
            // Create borderless window
            var windowClass = GetClass("NSWindow");
            var window = objc_msgSend(windowClass, allocSel);
            
            var rect = new CGRect(0, 0, imgWidth, imgHeight);
            var initSel = GetSelector("initWithContentRect:styleMask:backing:defer:");
            window = objc_msgSend_ptr_rect_ulong_ulong_byte(
                window, initSel, rect, NSWindowStyleMaskBorderless, NSBackingStoreBuffered, 0);
            
            if (window == IntPtr.Zero)
            {
                Console.WriteLine("[NativeSplash] Failed to create window");
                return;
            }
            
            _nsWindow = window;
            
            // Make window opaque with white background
            var setOpaqueSel = GetSelector("setOpaque:");
            objc_msgSend_void_byte(window, setOpaqueSel, 1);
            
            var whiteColorClass = GetClass("NSColor");
            var whiteColorSel = GetSelector("whiteColor");
            var whiteColor = objc_msgSend(whiteColorClass, whiteColorSel);
            var setBackgroundColorSel = GetSelector("setBackgroundColor:");
            objc_msgSend_void_ptr(window, setBackgroundColorSel, whiteColor);
            
            // Create NSImageView
            var imageViewClass = GetClass("NSImageView");
            var imageView = objc_msgSend(imageViewClass, allocSel);
            var initWithFrameSel = GetSelector("initWithFrame:");
            imageView = objc_msgSend_ptr_rect(imageView, initWithFrameSel, rect);
            
            // Set image
            var setImageSel = GetSelector("setImage:");
            objc_msgSend_void_ptr(imageView, setImageSel, nsImage);
            
            // Set image scaling
            var setImageScalingSel = GetSelector("setImageScaling:");
            objc_msgSend_ptr(imageView, setImageScalingSel, new IntPtr(1)); // NSImageScaleProportionallyUpOrDown
            
            // Set as content view
            var setContentViewSel = GetSelector("setContentView:");
            objc_msgSend_void_ptr(window, setContentViewSel, imageView);
            
            // Center window
            var centerSel = GetSelector("center");
            objc_msgSend_void(window, centerSel);
            
            // Show window
            var makeKeyAndOrderFrontSel = GetSelector("makeKeyAndOrderFront:");
            objc_msgSend_void_ptr(window, makeKeyAndOrderFrontSel, IntPtr.Zero);
            
            // Activate app
            var activateSel = GetSelector("activateIgnoringOtherApps:");
            objc_msgSend_void_byte(nsApp, activateSel, 1);
            
            _isShowing = true;
            Console.WriteLine($"[NativeSplash] Showing for {durationMs}ms");
            
            // Process events for duration
            var startTime = DateTime.UtcNow;
            var untilDateSel = GetSelector("distantPast");
            var distantPast = objc_msgSend(GetClass("NSDate"), untilDateSel);
            var nextEventSel = GetSelector("nextEventMatchingMask:untilDate:inMode:dequeue:");
            var defaultModeStr = CreateNSString("kCFRunLoopDefaultMode");
            var sendEventSel = GetSelector("sendEvent:");
            
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < durationMs)
            {
                var evt = objc_msgSend_ptr_ulong_ptr_ptr_byte(nsApp, nextEventSel, ulong.MaxValue, distantPast, defaultModeStr, 1);
                if (evt != IntPtr.Zero)
                {
                    objc_msgSend_void_ptr(nsApp, sendEventSel, evt);
                }
                Thread.Sleep(16); // ~60fps
            }
            
            // Close window
            var closeSel = GetSelector("close");
            objc_msgSend_void(window, closeSel);
            
            _isShowing = false;
            Console.WriteLine("[NativeSplash] Closed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeSplash] Error: {ex.Message}");
        }
    }
    
    // ── Windows splash via Win32 borderless window + GDI+ ──────────────────
    private void ShowAndWaitWin32(string splashPath, int durationMs)
    {
        try
        {
            // Initialize GDI+
            Win32Interop.GdiplusStartup(out var gdiToken, IntPtr.Zero, IntPtr.Zero);

            // Load image to get dimensions
            int loadResult = Win32Interop.GdipCreateBitmapFromFile(splashPath, out var hBitmap);
            if (loadResult != 0 || hBitmap == IntPtr.Zero)
            {
                Console.WriteLine($"[NativeSplash] GDI+ failed to load image: 0x{loadResult:X}");
                Win32Interop.GdiplusShutdown(gdiToken);
                return;
            }

            Win32Interop.GdipGetImageWidth(hBitmap, out uint imgW);
            Win32Interop.GdipGetImageHeight(hBitmap, out uint imgH);
            if (imgW == 0) imgW = 800;
            if (imgH == 0) imgH = 450;

            // Register a minimal window class
            string className = $"BlueSkySplash_{Guid.NewGuid():N}";
            var hInstance = Win32Interop.GetModuleHandleW(null);

            var wndClass = new Win32Interop.WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(new Win32Interop.WndProc((IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
                {
                    if (msg == 0x000F) // WM_PAINT
                    {
                        var ps = new Win32Interop.PAINTSTRUCT();
                        IntPtr hdc = Win32Interop.BeginPaint(hWnd, out ps);
                        if (hdc != IntPtr.Zero)
                        {
                            Win32Interop.GdipCreateFromHDC(hdc, out var gfx);
                            if (gfx != IntPtr.Zero)
                            {
                                Win32Interop.GdipDrawImageRect(gfx, hBitmap, 0, 0, (float)imgW, (float)imgH);
                                Win32Interop.GdipDeleteGraphics(gfx);
                            }
                            Win32Interop.EndPaint(hWnd, ref ps);
                        }
                        return IntPtr.Zero;
                    }
                    if (msg == 0x0002) // WM_DESTROY
                    {
                        Win32Interop.PostQuitMessage(0);
                        return IntPtr.Zero;
                    }
                    return Win32Interop.DefWindowProcW(hWnd, msg, wParam, lParam);
                })),
                hInstance = hInstance,
                lpszClassName = className,
                hCursor = Win32Interop.LoadCursor(IntPtr.Zero, (IntPtr)0x7F00), // IDC_ARROW
            };

            Win32Interop.RegisterClassW(ref wndClass);

            // Center on screen
            int screenW = Win32Interop.GetSystemMetrics(0); // SM_CXSCREEN
            int screenH = Win32Interop.GetSystemMetrics(1); // SM_CYSCREEN
            int winX = (screenW - (int)imgW) / 2;
            int winY = (screenH - (int)imgH) / 2;

            // Create borderless window
            _hwndWin32 = Win32Interop.CreateWindowExW(
                0, className, "BlueSky Engine",
                0x80000000, // WS_POPUP
                winX, winY, (int)imgW, (int)imgH,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (_hwndWin32 == IntPtr.Zero)
            {
                Console.WriteLine("[NativeSplash] Failed to create Win32 window");
                Win32Interop.GdipDisposeImage(hBitmap);
                Win32Interop.GdiplusShutdown(gdiToken);
                return;
            }

            Win32Interop.ShowWindow(_hwndWin32, 5); // SW_SHOW
            Win32Interop.UpdateWindow(_hwndWin32);

            _isShowing = true;
            Console.WriteLine($"[NativeSplash] Showing Win32 splash for {durationMs}ms");

            // Simple event loop
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var msg = new Win32Interop.MSG();
            while (sw.ElapsedMilliseconds < durationMs)
            {
                while (Win32Interop.PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE
                {
                    Win32Interop.TranslateMessage(ref msg);
                    Win32Interop.DispatchMessageW(ref msg);
                }
                Thread.Sleep(16);
            }

            // Cleanup
            Win32Interop.DestroyWindow(_hwndWin32);
            _hwndWin32 = IntPtr.Zero;
            Win32Interop.GdipDisposeImage(hBitmap);
            Win32Interop.GdiplusShutdown(gdiToken);
            _isShowing = false;
            Console.WriteLine("[NativeSplash] Win32 splash closed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeSplash] Win32 error: {ex.Message}");
        }
    }

    private IntPtr _hwndWin32;

    public void Dispose()
    {
        if (_nsWindow != IntPtr.Zero && _isShowing)
        {
            var closeSel = GetSelector("close");
            objc_msgSend_void(_nsWindow, closeSel);
            _isShowing = false;
        }
    }
}
