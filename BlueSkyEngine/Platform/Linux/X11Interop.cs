using System.Runtime.InteropServices;

namespace BlueSky.Platform.Linux;

internal static class X11Interop
{
    private const string X11 = "libX11.so.6";

    public const long StructureNotifyMask = 1L << 17;
    public const long KeyPressMask = 1L << 0;
    public const long KeyReleaseMask = 1L << 1;
    public const long ButtonPressMask = 1L << 2;
    public const long ButtonReleaseMask = 1L << 3;
    public const long PointerMotionMask = 1L << 6;
    public const long SubstructureNotifyMask = 1L << 19;
    public const long SubstructureRedirectMask = 1L << 20;
    public const long FocusChangeMask = 1L << 21;

    // Event types
    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int ButtonPress = 4;
    public const int ButtonRelease = 5;
    public const int MotionNotify = 6;
    public const int FocusIn = 9;
    public const int FocusOut = 10;
    public const int DestroyNotify = 17;
    public const int ConfigureNotify = 22;
    public const int ClientMessage = 33;

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct XEvent
    {
        [FieldOffset(0)]
        public int Type;
        [FieldOffset(0)]
        public XKeyEvent Key;
        [FieldOffset(0)]
        public XButtonEvent Button;
        [FieldOffset(0)]
        public XMotionEvent Motion;
        [FieldOffset(0)]
        public XConfigureEvent Configure;
        [FieldOffset(0)]
        public XClientMessageEvent ClientMessage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int Type;
        public nuint Serial;
        [MarshalAs(UnmanagedType.Bool)] public bool SendEvent;
        public nint Display;
        public ulong Window, Root, Subwindow, Time;
        public int X, Y, XRoot, YRoot;
        public uint State, Keycode;
        [MarshalAs(UnmanagedType.Bool)] public bool SameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XButtonEvent
    {
        public int Type;
        public nuint Serial;
        [MarshalAs(UnmanagedType.Bool)] public bool SendEvent;
        public nint Display;
        public ulong Window, Root, Subwindow, Time;
        public int X, Y, XRoot, YRoot;
        public uint State, Button;
        [MarshalAs(UnmanagedType.Bool)] public bool SameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XMotionEvent
    {
        public int Type;
        public nuint Serial;
        [MarshalAs(UnmanagedType.Bool)] public bool SendEvent;
        public nint Display;
        public ulong Window, Root, Subwindow, Time;
        public int X, Y, XRoot, YRoot;
        public uint State;
        public byte IsHint;
        public int SameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XConfigureEvent
    {
        public int Type;
        public nuint Serial;
        [MarshalAs(UnmanagedType.Bool)] public bool SendEvent;
        public nint Display;
        public ulong Event, Window;
        public int X, Y, Width, Height, BorderWidth;
        public ulong Above;
        [MarshalAs(UnmanagedType.Bool)] public bool OverrideRedirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        [MarshalAs(UnmanagedType.Bool)] public bool SendEvent;
        public nint Display;
        public ulong Window, MessageType;
        public int Format;
        public nint Data0, Data1, Data2, Data3, Data4;
    }

    [DllImport(X11)]
    public static extern nint XOpenDisplay(string? displayName);

    [DllImport(X11)]
    public static extern int XDefaultScreen(nint display);

    [DllImport(X11)]
    public static extern int XDisplayWidth(nint display, int screenNumber);

    [DllImport(X11)]
    public static extern int XDisplayHeight(nint display, int screenNumber);

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
    public static extern int XResizeWindow(nint display, ulong window, uint width, uint height);

    [DllImport(X11)]
    public static extern int XMoveWindow(nint display, ulong window, int x, int y);

    [DllImport(X11, CharSet = CharSet.Ansi)]
    public static extern ulong XInternAtom(nint display, string atomName, bool onlyIfExists);

    [DllImport(X11)]
    public static extern int XSetWMProtocols(nint display, ulong window, [In] ulong[] protocols, int count);

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
    public static extern int XSendEvent(nint display, ulong window, bool propagate, nint eventMask, ref XEvent eventSend);

    [DllImport(X11)]
    public static extern int XCloseDisplay(nint display);
    
    // Cursor functions
    [DllImport(X11)]
    public static extern int XDefineCursor(nint display, ulong window, ulong cursor);
    
    [DllImport(X11)]
    public static extern int XUndefineCursor(nint display, ulong window);
    
    [DllImport(X11)]
    public static extern int XFreeCursor(nint display, ulong cursor);

    [DllImport(X11)]
    public static extern ulong XCreateBitmapFromData(nint display, ulong drawable, byte[] data, uint width, uint height);

    [DllImport(X11)]
    public static extern ulong XCreatePixmapCursor(nint display, ulong source, ulong mask, ref XColor foreground, ref XColor background, uint x, uint y);

    [DllImport(X11)]
    public static extern int XFreePixmap(nint display, ulong pixmap);
    
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

    [StructLayout(LayoutKind.Sequential)]
    public struct XColor
    {
        public ulong Pixel;
        public ushort Red, Green, Blue;
        public byte Flags;
        public byte Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XSizeHints
    {
        public nint Flags;
        public int X, Y, Width, Height;
        public int MinWidth, MinHeight, MaxWidth, MaxHeight;
        public int WidthInc, HeightInc;
        public int MinAspectX, MinAspectY, MaxAspectX, MaxAspectY;
        public int BaseWidth, BaseHeight, WinGravity;
    }

    [DllImport(X11)]
    public static extern void XSetWMNormalHints(nint display, ulong window, ref XSizeHints hints);
}
