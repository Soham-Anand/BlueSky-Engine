using System;
using System.Numerics;
using System.Collections.Generic;
using BlueSky.Platform.Input;

namespace BlueSky.Platform.Windows;

public class Win32Input : IInputContext
{
    private readonly HashSet<KeyCode> _keysDown = new();
    private readonly HashSet<KeyCode> _keysPressed = new();
    private readonly HashSet<KeyCode> _keysReleased = new();
    
    private readonly HashSet<MouseButton> _buttonsDown = new();
    private readonly HashSet<MouseButton> _buttonsPressed = new();
    private readonly HashSet<MouseButton> _buttonsReleased = new();
    
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;
    
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    public Vector2 ScrollDelta => _scrollDelta;
    
    public event Action<KeyCode, ModifierKeys>? KeyDown;
    public event Action<KeyCode, ModifierKeys>? KeyUp;
    public event Action<char>? CharInput;
    public event Action<MouseButton>? MouseDown;
    public event Action<MouseButton>? MouseUp;
    public event Action<Vector2>? MouseMove;
    public event Action<Vector2>? MouseScroll;
    
    public Win32Input(Win32Window window)
    {
        window.OnMessage += HandleMessage;
    }

    private void HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32Interop.WM_MOUSEMOVE:
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                var newPos = new Vector2(x, y);
                _mouseDelta = newPos - _mousePosition;
                _mousePosition = newPos;
                MouseMove?.Invoke(_mousePosition);
                break;
                
            case Win32Interop.WM_LBUTTONDOWN:
                _buttonsDown.Add(MouseButton.Left);
                _buttonsPressed.Add(MouseButton.Left);
                MouseDown?.Invoke(MouseButton.Left);
                break;
                
            case Win32Interop.WM_LBUTTONUP:
                _buttonsDown.Remove(MouseButton.Left);
                _buttonsReleased.Add(MouseButton.Left);
                MouseUp?.Invoke(MouseButton.Left);
                break;
                
            case Win32Interop.WM_RBUTTONDOWN:
                _buttonsDown.Add(MouseButton.Right);
                _buttonsPressed.Add(MouseButton.Right);
                MouseDown?.Invoke(MouseButton.Right);
                break;
                
            case Win32Interop.WM_RBUTTONUP:
                _buttonsDown.Remove(MouseButton.Right);
                _buttonsReleased.Add(MouseButton.Right);
                MouseUp?.Invoke(MouseButton.Right);
                break;
                
            case 0x020A: // WM_MOUSEWHEEL
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                _scrollDelta = new Vector2(0, delta / 120f);
                MouseScroll?.Invoke(_scrollDelta);
                break;
                
            case Win32Interop.WM_KEYDOWN:
            case 0x0104: // WM_SYSKEYDOWN
                KeyCode downKey = (KeyCode)wParam.ToInt32();
                _keysDown.Add(downKey);
                _keysPressed.Add(downKey);
                KeyDown?.Invoke(downKey, ModifierKeys.None);
                break;
                
            case Win32Interop.WM_KEYUP:
            case 0x0105: // WM_SYSKEYUP
                KeyCode upKey = (KeyCode)wParam.ToInt32();
                _keysDown.Remove(upKey);
                _keysReleased.Add(upKey);
                KeyUp?.Invoke(upKey, ModifierKeys.None);
                break;
                
            case 0x0102: // WM_CHAR
                char c = (char)wParam.ToInt32();
                if (!char.IsControl(c))
                    CharInput?.Invoke(c);
                break;
        }
    }

    public void BeginFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _buttonsPressed.Clear();
        _buttonsReleased.Clear();
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
    }
    
    public bool IsKeyDown(KeyCode key) => _keysDown.Contains(key);
    public bool IsKeyPressed(KeyCode key) => _keysPressed.Contains(key);
    public bool IsKeyReleased(KeyCode key) => _keysReleased.Contains(key);
    public ModifierKeys GetModifiers() => ModifierKeys.None;
    public bool IsMouseButtonDown(MouseButton button) => _buttonsDown.Contains(button);
    public bool IsMouseButtonPressed(MouseButton button) => _buttonsPressed.Contains(button);
    public bool IsMouseButtonReleased(MouseButton button) => _buttonsReleased.Contains(button);
    
    public void Dispose() { }
}
