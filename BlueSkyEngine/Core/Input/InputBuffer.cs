using System;
using System.Collections.Generic;

namespace BlueSky.Core.Input;

public struct BufferedInputAction
{
    public string ActionName;
    public float Timestamp;
    public float ValidDuration;
    public bool Handled;
}

/// <summary>
/// Handles input buffering for actions to allow lenient input timing.
/// Essential for fluid movement mechanics (e.g. pressing jump slightly before landing).
/// </summary>
public class InputBuffer
{
    private readonly List<BufferedInputAction> _buffer = new();
    
    // Configurable window in seconds
    public float DefaultBufferWindow { get; set; } = 0.15f; 

    public void BufferAction(string actionName, float currentTime, float? customDuration = null)
    {
        _buffer.Add(new BufferedInputAction
        {
            ActionName = actionName,
            Timestamp = currentTime,
            ValidDuration = customDuration ?? DefaultBufferWindow,
            Handled = false
        });
    }

    public void Update(float currentTime)
    {
        // Remove expired inputs
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            var action = _buffer[i];
            if (action.Handled || (currentTime - action.Timestamp) > action.ValidDuration)
            {
                _buffer.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Checks if an action is currently buffered and valid.
    /// If consume is true, marks it as handled so it won't be triggered again.
    /// </summary>
    public bool ConsumeBufferedAction(string actionName, bool consume = true)
    {
        for (int i = 0; i < _buffer.Count; i++)
        {
            if (_buffer[i].ActionName == actionName && !_buffer[i].Handled)
            {
                if (consume)
                {
                    var action = _buffer[i];
                    action.Handled = true;
                    _buffer[i] = action;
                }
                return true;
            }
        }
        return false;
    }
    
    public void Clear()
    {
        _buffer.Clear();
    }
}
