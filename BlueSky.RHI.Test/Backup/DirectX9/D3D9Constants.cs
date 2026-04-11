namespace NotBSRenderer.DirectX9;

/// <summary>
/// DirectX 9 render state constants for better code readability
/// </summary>
public static class D3D9RenderState
{
    // Render States
    public const uint D3DRS_ZENABLE = 7;
    public const uint D3DRS_ZWRITEENABLE = 14;
    public const uint D3DRS_SRCBLEND = 19;
    public const uint D3DRS_DESTBLEND = 20;
    public const uint D3DRS_CULLMODE = 22;
    public const uint D3DRS_ZFUNC = 23;
    public const uint D3DRS_ALPHABLENDENABLE = 27;
    public const uint D3DRS_LIGHTING = 137;
    public const uint D3DRS_BLENDOP = 171;
    public const uint D3DRS_SCISSORTESTENABLE = 174;
    public const uint D3DRS_SEPARATEALPHABLENDENABLE = 206;
    public const uint D3DRS_SRCBLENDALPHA = 207;
    public const uint D3DRS_DESTBLENDALPHA = 208;
    public const uint D3DRS_BLENDOPALPHA = 209;
}

/// <summary>
/// DirectX 9 transform state constants
/// </summary>
public static class D3D9TransformState
{
    public const uint D3DTS_VIEW = 2;
    public const uint D3DTS_PROJECTION = 3;
    public const uint D3DTS_WORLD = 256;
}
