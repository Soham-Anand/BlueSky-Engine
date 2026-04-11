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
