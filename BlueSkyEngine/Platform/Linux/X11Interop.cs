using System.Runtime.InteropServices;

namespace BlueSky.Platform.Linux;

internal static class X11Interop
{
    private const string X11 = "libX11.so.6";

    public const long ExposureMask = 1L << 15;
    public const long StructureNotifyMask = 1L << 17;
    public const long KeyPressMask = 1L << 0;
    public const long KeyReleaseMask = 1L << 1;
    public const long ButtonPressMask = 1L << 2;
    public const long ButtonReleaseMask = 1L << 3;
    public const long PointerMotionMask = 1L << 6;
    public const long EnterWindowMask = 1L << 4;
    public const long LeaveWindowMask = 1L << 5;

    // Event types
    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int ButtonPress = 4;
    public const int ButtonRelease = 5;
    public const int MotionNotify = 6;
    public const int EnterNotify = 7;
    public const int LeaveNotify = 8;

[StructLayout(LayoutKind.Sequential, Size = 192)]
    public struct XEvent
    {
        public int Type;
    }

    [DllImport(X11)]
    public static extern nint XOpenDisplay(string? displayName);

    [DllImport(X11)]
    public static extern int XDefaultScreen(nint display);

    [DllImport(X11)]
    public static extern ulong XRootWindow(nint display, int screenNumber);

    [DllImport(X11)]
    public static extern ulong XBlackPixel(nint display, int screenNumber);

    [DllImport(X11)]
    public static extern ulong XWhitePixel(nint display, int screenNumber);

    [DllImport(X11)]
    public static extern ulong XCreateSimpleWindow(
        nint display,
        ulong parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        ulong border,
        ulong background);

    [DllImport(X11)]
    public static extern int XStoreName(nint display, ulong window, string windowName);

    [DllImport(X11)]
    public static extern int XSelectInput(nint display, ulong window, nint eventMask);

    [DllImport(X11)]
    public static extern int XMapWindow(nint display, ulong window);

    [DllImport(X11)]
    public static extern int XUnmapWindow(nint display, ulong window);

    [DllImport(X11)]
    public static extern int XDestroyWindow(nint display, ulong window);

    [DllImport(X11)]
    public static extern int XPending(nint display);

    [DllImport(X11)]
    public static extern int XNextEvent(nint display, ref XEvent xevent);

    [DllImport(X11)]
    public static extern int XFlush(nint display);

    [DllImport(X11)]
    public static extern int XCloseDisplay(nint display);
    
    // Cursor functions
    [DllImport(X11)]
    public static extern ulong XCreateFontCursor(nint display, uint shape);
    
    [DllImport(X11)]
    public static extern int XDefineCursor(nint display, ulong window, ulong cursor);
    
    [DllImport(X11)]
    public static extern int XUndefineCursor(nint display, ulong window);
    
    [DllImport(X11)]
    public static extern int XFreeCursor(nint display, ulong cursor);
    
    // Pointer grabbing
    [DllImport(X11)]
    public static extern int XGrabPointer(
        nint display,
        ulong grab_window,
        bool owner_events,
        nint event_mask,
        int pointer_mode,
        int keyboard_mode,
        ulong confine_to,
        ulong cursor,
        uint time);
    
    [DllImport(X11)]
    public static extern int XUngrabPointer(nint display, uint time);
    
    // Cursor shapes
    public const uint XC_arrow = 0;
    public const uint XC_crosshair = 34;
    public const uint XC_hand1 = 58;
    public const uint XC_watch = 150;
    public const uint XC_X_cursor = 158;
}
