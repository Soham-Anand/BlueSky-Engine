using System;
using System.Numerics;
using System.Collections.Generic;
using BlueSky.Platform.Input;

namespace BlueSky.Platform.macOS;

/// <summary>
/// macOS input implementation using Cocoa events.
/// </summary>
public class CocoaInput : IInputContext
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
    private ModifierKeys _modifiers;
    
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
    
    public ModifierKeys GetModifiers() => _modifiers;
    
    public bool IsMouseButtonDown(MouseButton button) => _buttonsDown.Contains(button);
    
    public bool IsMouseButtonPressed(MouseButton button) => _buttonsPressed.Contains(button);
    
    public bool IsMouseButtonReleased(MouseButton button) => _buttonsReleased.Contains(button);
    
    internal void OnKeyDown(KeyCode key, ModifierKeys modifiers)
    {
        if (!_keysDown.Contains(key))
        {
            _keysDown.Add(key);
            _keysPressed.Add(key);
            _modifiers = modifiers;
            KeyDown?.Invoke(key, modifiers);
        }
    }
    
    internal void OnKeyUp(KeyCode key, ModifierKeys modifiers)
    {
        if (_keysDown.Contains(key))
        {
            _keysDown.Remove(key);
            _keysReleased.Add(key);
            _modifiers = modifiers;
            KeyUp?.Invoke(key, modifiers);
        }
    }
    
    internal void OnCharInput(char c)
    {
        CharInput?.Invoke(c);
    }
    
    internal void OnMouseDown(MouseButton button)
    {
        if (!_buttonsDown.Contains(button))
        {
            _buttonsDown.Add(button);
            _buttonsPressed.Add(button);
            MouseDown?.Invoke(button);
        }
    }
    
    internal void OnMouseUp(MouseButton button)
    {
        if (_buttonsDown.Contains(button))
        {
            _buttonsDown.Remove(button);
            _buttonsReleased.Add(button);
            MouseUp?.Invoke(button);
        }
    }
    
    internal void OnMouseMove(Vector2 position, Vector2 delta)
    {
        _mousePosition = position;
        _mouseDelta += delta;
        MouseMove?.Invoke(position);
    }

    /// <summary>
    /// Called during cursor capture: accumulates delta without changing the stored position.
    /// </summary>
    internal void OnMouseDelta(Vector2 delta)
    {
        _mouseDelta += delta;
    }
    
    internal void OnMouseScroll(Vector2 delta)
    {
        _scrollDelta += delta;
        MouseScroll?.Invoke(delta);
    }
    
    public void Dispose()
    {
        // Nothing to dispose
    }
}
