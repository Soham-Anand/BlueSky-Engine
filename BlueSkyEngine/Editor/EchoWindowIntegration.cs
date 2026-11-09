using System;
using System.IO;
using BlueSky.Editor.UI;

namespace BlueSky.Editor;

partial class Program
{
    private static EchoWindowHost? _echoWindowHost;

    /// <summary>True for converted Echo Source / Echo Super assets.</summary>
    internal static bool IsEchoAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string ext = Path.GetExtension(path);
        return ext.Equals(BlueSky.Echo.EchoSourceAsset.Extension, StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(BlueSky.Echo.EchoSuperAsset.Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens an Echo Source or Super in the Echo window, creating the window on
    /// first use. Safe to call from UI code: if the renderer is not ready yet
    /// the request is logged and ignored.
    /// </summary>
    internal static void OpenEchoAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (_rhi == null || _uiRenderer == null)
        {
            Log($"⚠ Echo editor is not ready (cannot open {Path.GetFileName(path)})");
            return;
        }

        if (_echoWindowHost == null)
        {
            var fontAtlas = _uiRenderer.FontAtlas;
            if (fontAtlas == null)
            {
                Log("⚠ Echo editor needs the UI font atlas, which is not loaded yet.");
                return;
            }

            _echoWindowHost = new EchoWindowHost(_rhi, fontAtlas, "Echo");
        }

        _echoWindowHost.FocusDocument(path);
    }
}
