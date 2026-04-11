using System;

namespace BlueSky.Platform.Input;

/// <summary>
/// Modifier key flags that can be combined.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,  // Windows key / Command key
}
