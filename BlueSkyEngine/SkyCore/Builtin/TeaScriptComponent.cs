using System;

namespace BlueSky.SkyCore.Builtin;

public unsafe struct TeaScriptComponent
{
    private fixed char _scriptAssetId[512];
    
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
    
    public bool IsInitialized;
    public bool IsEnabled;
    public bool AllowRuntimeUI;
    public bool BlockRuntimeInput;
    public uint RuntimeInstance;
}
