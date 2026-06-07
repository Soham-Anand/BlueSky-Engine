using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Platform;
using BlueSky.Platform.Input;

namespace BlueSky.Core.Input;

/// <summary>
/// ECS system wrapper for input handling and buffering.
/// </summary>
public class InputSystem : SystemBase
{
    private readonly IInputContext _inputContext;
    public InputBuffer Buffer { get; } = new();

    // Map high-level actions to keycodes (simplified mapping)
    private readonly Dictionary<string, KeyCode> _actionMap = new();

    public InputSystem(IInputContext inputContext)
    {
        _inputContext = inputContext;
        
        // Setup default mappings
        _actionMap["Jump"] = KeyCode.Space;
        _actionMap["Dash"] = KeyCode.LeftShift;
        _actionMap["Attack"] = KeyCode.Z; // Temporary fallback
        _actionMap["Interact"] = KeyCode.E;
    }

    public void MapAction(string action, KeyCode key)
    {
        _actionMap[action] = key;
    }

    public override void Update(float dt)
    {
        // Advance buffer time
        // In a real engine, this might use a global time provider
        float currentTime = (float)DateTime.Now.TimeOfDay.TotalSeconds;
        Buffer.Update(currentTime);

        // Check for new inputs and buffer them
        foreach (var kvp in _actionMap)
        {
            if (_inputContext.IsKeyPressed(kvp.Value))
            {
                Buffer.BufferAction(kvp.Key, currentTime);
            }
        }
    }

    // Helpers to query input
    public bool IsActionPressed(string action)
    {
        if (_actionMap.TryGetValue(action, out var key))
        {
            return _inputContext.IsKeyPressed(key);
        }
        return false;
    }

    public bool IsActionHeld(string action)
    {
        if (_actionMap.TryGetValue(action, out var key))
        {
            return _inputContext.IsKeyDown(key);
        }
        return false;
    }

    public bool ConsumeBufferedAction(string action)
    {
        return Buffer.ConsumeBufferedAction(action, true);
    }
}
