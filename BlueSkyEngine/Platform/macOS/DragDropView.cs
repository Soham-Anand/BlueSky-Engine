using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using static BlueSky.Platform.macOS.CocoaInterop;

namespace BlueSky.Platform.macOS;

/// <summary>
/// Custom NSView subclass that implements NSDraggingDestination for file drag and drop.
/// </summary>
public class DragDropView
{
    private IntPtr _viewClass;
    private IntPtr _viewInstance;
    private Action<string[]>? _onFilesDropped;

    // Keep delegates alive to prevent GC
    private static DraggingEnteredDelegate? _draggingEnteredDelegate;
    private static PerformDragOperationDelegate? _performDragOperationDelegate;

    public IntPtr Instance => _viewInstance;

    public DragDropView(CGRect frame, Action<string[]>? onFilesDropped = null)
    {
        _onFilesDropped = onFilesDropped;
        CreateViewClass();
        CreateViewInstance(frame);
    }

    private void CreateViewClass()
    {
        // Check if class already exists (prevent duplicate registration)
        var existingClass = GetClass("BlueSkyDragDropView");
        if (existingClass != IntPtr.Zero)
        {
            _viewClass = existingClass;
            Console.WriteLine("[DragDropView] Using existing class");
            return;
        }

        // Create a new class subclassing NSView
        var nsViewClass = GetClass("NSView");
        _viewClass = objc_allocateClassPair(nsViewClass, "BlueSkyDragDropView", 0);

        if (_viewClass == IntPtr.Zero)
        {
            Console.WriteLine("[DragDropView] Failed to allocate class");
            return;
        }

        // Add dragging destination protocol
        var draggingDestinationProtocol = objc_getProtocol("NSDraggingDestination");
        class_addProtocol(_viewClass, draggingDestinationProtocol);

        // Override draggingEntered:
        var draggingEnteredSel = GetSelector("draggingEntered:");
        _draggingEnteredDelegate = DraggingEntered;
        var draggingEnteredImp = Marshal.GetFunctionPointerForDelegate(_draggingEnteredDelegate);
        class_addMethod(_viewClass, draggingEnteredSel, draggingEnteredImp, "@@:@");

        // Override performDragOperation:
        var performDragOperationSel = GetSelector("performDragOperation:");
        _performDragOperationDelegate = PerformDragOperation;
        var performDragOperationImp = Marshal.GetFunctionPointerForDelegate(_performDragOperationDelegate);
        class_addMethod(_viewClass, performDragOperationSel, performDragOperationImp, "i@:@");

        // Register the class
        objc_registerClassPair(_viewClass);

        Console.WriteLine("[DragDropView] Created custom view class");
    }

    private void CreateViewInstance(CGRect frame)
    {
        // Proper Objective-C pattern: alloc, then initWithFrame:
        var allocSel = GetSelector("alloc");
        var initWithFrameSel = GetSelector("initWithFrame:");
        
        var allocedInstance = objc_msgSend(_viewClass, allocSel);
        if (allocedInstance == IntPtr.Zero)
        {
            Console.WriteLine("[DragDropView] Failed to alloc view instance");
            return;
        }
        
        _viewInstance = objc_msgSend_rect(allocedInstance, initWithFrameSel, frame);

        if (_viewInstance == IntPtr.Zero)
        {
            Console.WriteLine("[DragDropView] Failed to init view instance");
            return;
        }

        Console.WriteLine($"[DragDropView] Created instance: {_viewInstance}");
    }

    private IntPtr DraggingEntered(IntPtr self, IntPtr selector, IntPtr sender)
    {
        // Accept the drag
        return IntPtr.Zero; // NSDragOperationNone = 0, we'll handle it in performDragOperation
    }

    private int PerformDragOperation(IntPtr self, IntPtr selector, IntPtr sender)
    {
        try
        {
            // Get the dragging pasteboard
            var draggingPasteboardSel = GetSelector("draggingPasteboard");
            var pasteboard = objc_msgSend(sender, draggingPasteboardSel);

            // Get file URLs from pasteboard
            var propertyListSel = GetSelector("propertyListForType:");
            var fileType = CreateNSString("public.file-url");
            var propertyList = objc_msgSend(pasteboard, propertyListSel, fileType);

            if (propertyList != IntPtr.Zero)
            {
                var countSel = GetSelector("count");
                var count = objc_msgSend_int(propertyList, countSel);

                var filePaths = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    var objectAtIndexSel = GetSelector("objectAtIndex:");
                    var urlObj = objc_msgSend(propertyList, objectAtIndexSel, i);
                    var pathSel = GetSelector("path");
                    var pathPtr = objc_msgSend(urlObj, pathSel);
                    var path = NSStringToCSharpString(pathPtr);
                    filePaths.Add(path);
                }

                if (filePaths.Count > 0 && _onFilesDropped != null)
                {
                    Console.WriteLine($"[DragDropView] Files dropped: {string.Join(", ", filePaths)}");
                    _onFilesDropped(filePaths.ToArray());
                }

                Release(fileType);
            }

            return 1; // NSDragOperationCopy = 1
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DragDropView] Error in performDragOperation: {ex.Message}");
            return 0;
        }
    }

    private static string NSStringToCSharpString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return "";

        var utf8Sel = GetSelector("UTF8String");
        var utf8Ptr = objc_msgSend(nsString, utf8Sel);
        return Marshal.PtrToStringUTF8(utf8Ptr) ?? "";
    }

    private static IntPtr CreateNSString(string str)
    {
        var nsStringClass = GetClass("NSString");
        var stringWithUTF8StringSel = GetSelector("stringWithUTF8String:");
        var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(str);
        var utf8Ptr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);
        Marshal.Copy(utf8Bytes, 0, utf8Ptr, utf8Bytes.Length);
        Marshal.WriteByte(utf8Ptr, utf8Bytes.Length, 0); // null terminator
        var nsString = objc_msgSend(nsStringClass, stringWithUTF8StringSel, utf8Ptr);
        Marshal.FreeHGlobal(utf8Ptr);
        return nsString;
    }

    private static void Release(IntPtr obj)
    {
        var releaseSel = GetSelector("release");
        objc_msgSend_void(obj, releaseSel);
    }

    // Delegates for Objective-C methods
    private delegate IntPtr DraggingEnteredDelegate(IntPtr self, IntPtr selector, IntPtr sender);
    private delegate int PerformDragOperationDelegate(IntPtr self, IntPtr selector, IntPtr sender);

    // Objective-C runtime functions
    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, long extraBytes);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getProtocol(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector, int index);
}
