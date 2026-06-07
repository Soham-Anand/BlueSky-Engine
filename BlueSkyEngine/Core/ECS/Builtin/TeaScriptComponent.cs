using System;

namespace BlueSky.Core.ECS.Builtin;

/// <summary>
/// Component that attaches a TeaScript to an entity.
/// The script can access and control the entity through registered engine functions.
/// </summary>
public unsafe struct TeaScriptComponent
{
    // Absolute project asset paths can easily exceed 128 chars.
    // Keep this component unmanaged for ECS storage, but give script paths
    // enough room to survive real project folder depth.
    private fixed char _scriptAssetId[512];
    
    /// <summary>
    /// Asset ID of the .tea script file.
    /// </summary>
    public string ScriptAssetId
    {
        get
        {
            fixed (char* ptr = _scriptAssetId)
            {
                return new string(ptr).TrimEnd('\0');
            }
        }
        set
        {
            value ??= string.Empty;
            int length = System.Math.Min(511, value.Length);
            for (int i = 0; i < length; i++)
            {
                _scriptAssetId[i] = value[i];
            }
            _scriptAssetId[length] = '\0';
        }
    }
    
    /// <summary>
    /// Whether the script has been initialized (start() called).
    /// </summary>
    public bool IsInitialized;
    
    /// <summary>
    /// Whether the script is currently enabled.
    /// </summary>
    public bool IsEnabled;
    
    /// <summary>
    /// Runtime instance ID (managed by TeaScriptSystem).
    /// </summary>
    public uint RuntimeInstance;
}
