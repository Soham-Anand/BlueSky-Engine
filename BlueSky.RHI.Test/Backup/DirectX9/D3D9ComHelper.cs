using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX9;

/// <summary>
/// Helper for calling COM methods through vtables
/// </summary>
internal static class D3D9ComHelper
{
    public static unsafe TDelegate GetComMethod<TDelegate>(IntPtr comObject, int vtableOffset) where TDelegate : Delegate
    {
        var vtable = *(IntPtr*)comObject;
        var methodPtr = *((IntPtr*)vtable + vtableOffset);
        return (TDelegate)Marshal.GetDelegateForFunctionPointer(methodPtr, typeof(TDelegate));
    }
}
