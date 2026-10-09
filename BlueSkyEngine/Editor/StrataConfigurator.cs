using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Editor.UI;
using BlueSky.Rendering.Strata;
using BlueSky.Rendering.RHI;

namespace BlueSky.Editor;

/// <summary>
/// Strata live material configurator (form-based, docked panel).
/// Every change re-assembles a fresh material and drives
/// ViewportRenderer.StrataOverride — the main viewport IS the preview.
/// Colors edited in sRGB (0-255), linearized on Assemble. Nothing stored sRGB.
/// </summary>
partial class Program
{
    private static bool _strataDirty = true;

    private static string _strataName = "ConfigMaterial";
    private static float _strataR = 140f, _strataG = 30f, _strataB = 25f;
    private static float _strataMetallic = 0f, _strataRoughness = 0.35f, _strataAO = 1f;
    private static float _strataER = 0f, _strataEG = 0f, _strataEB = 0f, _strataEI = 0f;
    private static float _strataCoatR = 255f, _strataCoatG = 255f, _strataCoatB = 255f;
    private static float _strataTile = 8f;
    private static float _strataAlpha = 1f;
    private static bool _strataUseAO = true, _strataUseDetail, _strataUseWrap = true,
        _strataUseToksvig, _strataUseBump, _strataUseDoor, _strataUseClearcoat, _strataUseIridescence,
        _strataUseSheen, _strataUseAnisotropy, _strataUseSkin, _strataUseHair;
    private static readonly Dictionary<string, (byte[] Rgba, int W, int H)> _strataTexBlobs = new();
    private static string _strataTargetMesh = "";
    private static string _strataTargetSlot = "0";
    private static string _strataOpenPath = "";
    private static string _strataStatus = "Tweak values — preview is live.";

    /// <summary>
    /// Opens a .stratamat file into the configurator: decodes, populates every
    /// widget, shows the panel, rebuilds the live preview. Loud on failure.
    /// </summary>
    internal static void OpenStrataMaterial(string path)
    {
        Console.WriteLine($"[StrataDbg] OpenStrataMaterial: {path}");
        try
        {
            var mat = StrataMaterial.Load(path);
            Console.WriteLine($"[StrataDbg] Decoded '{mat.Name}' mask=0x{(uint)mat.Features:X} tex={mat.Textures.Count}");
            LoadIntoState(mat, path);
            _strataDirty = true;
            _strataStatus = $"Opened {System.IO.Path.GetFileName(path)} (mask=0x{(uint)mat.Features:X})";
            Console.WriteLine("[StrataDbg] Panel flagged visible.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StrataDbg] Open failed: {ex.Message}");
            _strataStatus = $"Open failed: {ex.Message}";
        }
    }

    internal static void LoadIntoState(StrataMaterial mat, string path)
    {
        _strataOpenPath = path;
        _strataName = mat.Name;
        var alb = StrataImporter.SrgbEncode(mat.AlbedoLinear);
        _strataR = alb.X * 255f; _strataG = alb.Y * 255f; _strataB = alb.Z * 255f;
        _strataMetallic = mat.Metallic;
        _strataRoughness = mat.Roughness;
        _strataAO = mat.AO;
        var emi = StrataImporter.SrgbEncode(mat.EmissiveLinear);
        _strataER = emi.X * 255f; _strataEG = emi.Y * 255f; _strataEB = emi.Z * 255f;
        _strataEI = mat.EmissiveIntensity;
        var coatTint = StrataImporter.SrgbEncode(mat.ClearcoatTintLinear);
        _strataCoatR = coatTint.X * 255f;
        _strataCoatG = coatTint.Y * 255f;
        _strataCoatB = coatTint.Z * 255f;
        _strataAlpha = mat.Alpha;
        _strataUseAO = mat.Features.HasFlag(StrataFeature.BakedAO);
        _strataUseDetail = mat.Features.HasFlag(StrataFeature.DetailNormal);
        _strataUseWrap = mat.Features.HasFlag(StrataFeature.WrappedDiffuse);
        _strataUseToksvig = mat.Features.HasFlag(StrataFeature.Toksvig);
        _strataUseBump = mat.Features.HasFlag(StrataFeature.BumpOffset);
        _strataUseDoor = mat.Features.HasFlag(StrataFeature.ScreenDoor);
        _strataUseClearcoat = mat.Features.HasFlag(StrataFeature.Clearcoat);
        _strataUseIridescence = mat.Features.HasFlag(StrataFeature.Iridescence);
        _strataUseSheen = mat.Features.HasFlag(StrataFeature.Sheen);
        _strataUseAnisotropy = mat.Features.HasFlag(StrataFeature.Anisotropy);
        _strataUseSkin = mat.Features.HasFlag(StrataFeature.Skin);
        _strataUseHair = mat.Features.HasFlag(StrataFeature.Hair);
        _strataTexBlobs.Clear();
        foreach (var tex in mat.Textures)
        {
            int size = (int)tex.PayloadSize, off = (int)tex.PayloadOffset;
            if (size <= 0 || off < 0 || off + size > mat.Payload.Length)
                continue;
            var bytes = new byte[size];
            Buffer.BlockCopy(mat.Payload, off, bytes, 0, size);
            _strataTexBlobs[tex.Slot] = (bytes, tex.Width, tex.Height);
        }
    }

    internal static void RegisterStrataPanel()
    {
        if (_dockingSystem == null)
        {
            Console.WriteLine("[StrataDbg] RegisterStrataPanel SKIPPED (no docking system).");
            return;
        }
        _dockingSystem.AddPanel("strata", "Strata", DrawStrataPanel);
        _dockingSystem.DockTo("strata", "details", DockPosition.Center);
        Console.WriteLine("[StrataDbg] Strata panel registered + docked.");
    }

    private static bool _strataDrewOnce;

    private static void DrawStrataPanel(EditorUI ui, DockRect rect)
    {
        // No flag gate: the docking system only calls this when the Strata
        // tab is visible. (Gating here left the tab body empty — the tab
        // selection never set _showStrataPanel.)
        if (!_strataDrewOnce)
        {
            _strataDrewOnce = true;
            Console.WriteLine("[StrataDbg] DrawStrataPanel drawing first frame.");
        }
        float x = rect.X + 12, y = rect.Y + 34;
        float w = rect.W - 24;

        void Label(string t)
        {
            ui.SetCursor(x, y); ui.Text(t, EditorTheme.TextPrimary); y += 22;
        }
        void SliderRow(string t, ref float v, float min, float max)
        {
            ui.SetCursor(x, y); ui.Text(t, EditorTheme.TextSecondary);
            ui.SetCursor(x + 130, y - 2);
            if (ui.Slider(ref v, min, max, Math.Min(180, w - 200), 20)) _strataDirty = true;
            y += 26;
        }
        bool ToggleRow(string t, bool v)
        {
            ui.SetCursor(x, y); ui.Text(t, EditorTheme.TextSecondary);
            var c = ui.GetCursor();
            bool nv = v;
            if (ui.ButtonEx(c.X + 130, y - 2, 52, 20, v ? "ON" : "off",
                EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover, EditorTheme.AccentDim,
                new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 0))
            {
                nv = !v; _strataDirty = true;
            }
            y += 26;
            return nv;
        }
        void TexRow(string slot, string label)
        {
            ui.SetCursor(x, y); ui.Text(label, EditorTheme.TextSecondary);
            bool has = _strataTexBlobs.ContainsKey(slot);
            var c = ui.GetCursor();
            if (ui.ButtonEx(c.X + 130, y - 2, 90, 20, has ? "Replace" : "Pick…",
                EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover, EditorTheme.AccentDim,
                new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 0))
                PickStrataTexture(slot);
            if (has)
            {
                var c2 = ui.GetCursor();
                if (ui.ButtonEx(c2.X + 226, y - 2, 52, 20, "Clear",
                    EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover, EditorTheme.AccentDim,
                    new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 0))
                {
                    _strataTexBlobs.Remove(slot); _strataDirty = true;
                }
            }
            y += 26;
        }

        Label("STRATA — live material configurator");
        ui.SetCursor(x, y); ui.Text("Name", EditorTheme.TextSecondary);
        ui.SetCursor(x + 130, y - 4);
        if (ui.TextField(ref _strataName, Math.Min(180, w - 140), 24)) _strataDirty = true;
        y += 30;

        Label("Albedo (sRGB — linearized on write)");
        SliderRow("R", ref _strataR, 0, 255);
        SliderRow("G", ref _strataG, 0, 255);
        SliderRow("B", ref _strataB, 0, 255);
        SliderRow("Metallic", ref _strataMetallic, 0, 1);
        SliderRow("Roughness", ref _strataRoughness, 0.04f, 1f);
        SliderRow("AO", ref _strataAO, 0, 1);
        SliderRow("Alpha", ref _strataAlpha, 0, 1);
        Label("Emissive (sRGB)");
        SliderRow("ER", ref _strataER, 0, 255);
        SliderRow("EG", ref _strataEG, 0, 255);
        SliderRow("EB", ref _strataEB, 0, 255);
        SliderRow("Intensity", ref _strataEI, 0, 8);
        SliderRow("Detail tile", ref _strataTile, 1, 32);

        Label("Tier-1 lobes");
        _strataUseAO = ToggleRow("Baked AO", _strataUseAO);
        _strataUseDetail = ToggleRow("Detail+RNM", _strataUseDetail);
        _strataUseWrap = ToggleRow("Wrapped diffuse", _strataUseWrap);
        _strataUseToksvig = ToggleRow("Toksvig", _strataUseToksvig);
        _strataUseBump = ToggleRow("Bump offset", _strataUseBump);
        _strataUseDoor = ToggleRow("Screen door", _strataUseDoor);
        _strataUseClearcoat = ToggleRow("Clearcoat", _strataUseClearcoat);
        if (_strataUseClearcoat)
        {
            Label("Clearcoat tint (sRGB)");
            SliderRow("Coat R", ref _strataCoatR, 0, 255);
            SliderRow("Coat G", ref _strataCoatG, 0, 255);
            SliderRow("Coat B", ref _strataCoatB, 0, 255);
        }
        _strataUseIridescence = ToggleRow("Iridescence", _strataUseIridescence);
        _strataUseSheen = ToggleRow("Sheen", _strataUseSheen);
        _strataUseAnisotropy = ToggleRow("Anisotropy", _strataUseAnisotropy);
        _strataUseSkin = ToggleRow("Skin", _strataUseSkin);
        _strataUseHair = ToggleRow("Hair", _strataUseHair);

        Label("Texture slots (blobs, intent-flagged)");
        TexRow("albedo", "Albedo (sRGB)");
        TexRow("normal", "Normal (linear)");
        TexRow("rma", "RMA gray (linear)");
        TexRow("detailAlbedo", "Detail albedo");
        TexRow("detailNormal", "Detail normal");

        Label("Assign to mesh slot");
        ui.SetCursor(x, y); ui.Text("Mesh .blueskyasset", EditorTheme.TextSecondary);
        ui.SetCursor(x + 130, y - 4);
        ui.TextField(ref _strataTargetMesh, Math.Min(180, w - 140), 24);
        y += 30;
        ui.SetCursor(x, y); ui.Text("Slot", EditorTheme.TextSecondary);
        ui.SetCursor(x + 130, y - 4);
        ui.TextField(ref _strataTargetSlot, 60, 24);
        y += 30;

        var cb = ui.GetCursor();
        if (ui.ButtonEx(x, y, 120, 24, "Assign + Save",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 0))
            AssignStrataToSlot();
        if (ui.ButtonEx(x + 128, y, 120, 24, "Save file",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 0))
            SaveOpenStrataFile();
        if (ui.ButtonEx(x + 128, y, 120, 24, "Clear preview",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 0))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.StrataOverride = null;
            _strataStatus = "Preview cleared (orange clay).";
        }
        y += 30;
        ui.SetCursor(x, y); ui.Text(_strataStatus, EditorTheme.TextMuted);

        if (_strataDirty)
        {
            _strataDirty = false;
            RebuildLiveMaterial();
        }
    }

    private static void PickStrataTexture(string slot)
    {
        try
        {
            string? path = NativeFilePicker.OpenFile($"Pick {slot} texture",
                "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tga");
            if (string.IsNullOrEmpty(path)) return;
            var decoded = BlueSky.Core.Assets.StrataImportWriter.DecodeImageFile(path);
            if (decoded == null)
            {
                _strataStatus = $"Unreadable image: {path}";
                return;
            }
            _strataTexBlobs[slot] = (decoded.Value.Rgba, decoded.Value.Width, decoded.Value.Height);
            _strataStatus = $"{slot}: {System.IO.Path.GetFileName(path)} ({decoded.Value.Width}x{decoded.Value.Height})";
            _strataDirty = true;
        }
        catch (Exception ex)
        {
            _strataStatus = $"Pick failed: {ex.Message}";
        }
    }

    internal static StrataMaterial BuildConfiguredMaterial(string name)
    {
        return BuildConfiguredMaterial(name,
            _strataR, _strataG, _strataB, _strataMetallic, _strataRoughness, _strataAO,
            _strataER, _strataEG, _strataEB, _strataEI, _strataTile,
            _strataUseAO, _strataUseDetail, _strataUseWrap, _strataUseToksvig,
            _strataUseBump, _strataUseDoor, _strataTexBlobs,
            useClearcoat: _strataUseClearcoat, alpha: _strataAlpha,
            useIridescence: _strataUseIridescence, useSheen: _strataUseSheen,
            useAnisotropy: _strataUseAnisotropy, useSkin: _strataUseSkin,
            useHair: _strataUseHair,
            clearcoatTintR: _strataCoatR, clearcoatTintG: _strataCoatG,
            clearcoatTintB: _strataCoatB);
    }

    internal static StrataMaterial BuildConfiguredMaterial(
        string name,
        float r, float g, float b, float metallic, float roughness, float ao,
        float er, float eg, float eb, float ei, float tile,
        bool useAO, bool useDetail, bool useWrap, bool useToksvig,
        bool useBump, bool useDoor,
        Dictionary<string, (byte[] Rgba, int W, int H)> blobs,
        bool useClearcoat = false, float alpha = 1f, bool useIridescence = false,
        bool useSheen = false, bool useAnisotropy = false, bool useSkin = false,
        bool useHair = false, float clearcoatTintR = 255f,
        float clearcoatTintG = 255f, float clearcoatTintB = 255f)
    {
        var albedoLin = StrataImporter.LinearizeSrgb(new Vector3(
            r / 255f, g / 255f, b / 255f));
        var emissiveLin = StrataImporter.LinearizeSrgb(new Vector3(
            er / 255f, eg / 255f, eb / 255f));
        var clearcoatTintLin = StrataImporter.LinearizeSrgb(new Vector3(
            clearcoatTintR / 255f, clearcoatTintG / 255f, clearcoatTintB / 255f));

        byte[]? albedo = null, normal = null, rma = null, detailA = null, detailN = null;
        int aw = 0, ah = 0, nw = 0, nh = 0, rw = 0, rh = 0, dw = 0, dh = 0;
        if (blobs.TryGetValue("albedo", out var a)) { albedo = a.Rgba; aw = a.W; ah = a.H; }
        if (blobs.TryGetValue("normal", out var n)) { normal = n.Rgba; nw = n.W; nh = n.H; }
        if (blobs.TryGetValue("detailAlbedo", out var da)) { detailA = da.Rgba; dw = da.W; dh = da.H; }
        if (blobs.TryGetValue("detailNormal", out var dn)) { detailN = dn.Rgba; dw = dn.W; dh = dn.H; }
        if (blobs.TryGetValue("rma", out var rr))
        {
            var repacked = new byte[rr.Rgba.Length];
            for (int i = 0; i + 3 < repacked.Length + 1; i += 4)
            {
                repacked[i] = rr.Rgba[i]; repacked[i + 1] = 0;
                repacked[i + 2] = 255; repacked[i + 3] = 255;
            }
            rma = repacked; rw = rr.W; rh = rr.H;
        }

        var mat = StrataImporter.Assemble(
            name, albedoLin, metallic, roughness, ao,
            emissiveLin, ei,
            albedoSrgb: albedo, albedoW: aw, albedoH: ah,
            normalMap: normal, normalW: nw, normalH: nh,
            rmaMap: rma, rmaW: rw, rmaH: rh,
            detailAlbedo: detailA, detailNormal: detailN,
            detailW: dw, detailH: dh, detailTile: tile,
            screenDoor: useDoor,
            clearcoat: useClearcoat ? 1f : 0f,
            clearcoatTintLinear: clearcoatTintLin,
            alpha: alpha,
            iridescence: useIridescence ? 1f : 0f,
            sheen: useSheen ? 1f : 0f,
            anisotropy: useAnisotropy ? 1f : 0f,
            skin: useSkin ? 1f : 0f,
            hair: useHair ? 1f : 0f);

        // Toggles beyond what data presence implies — but data still rules:
        // lobes needing textures stay OFF without blobs, toggles or not.
        if (useAO) mat.Features |= StrataFeature.BakedAO;
        else mat.Features &= ~StrataFeature.BakedAO;
        if (useWrap) mat.Features |= StrataFeature.WrappedDiffuse;
        else mat.Features &= ~StrataFeature.WrappedDiffuse;
        if (useDetail && detailA != null && detailN != null)
            mat.Features |= StrataFeature.DetailNormal | StrataFeature.BumpOffset;
        if (useToksvig && normal != null) mat.Features |= StrataFeature.Toksvig;
        else mat.Features &= ~StrataFeature.Toksvig;
        if (useBump && detailA != null && detailN != null) mat.Features |= StrataFeature.BumpOffset;
        if (useClearcoat) mat.Features |= StrataFeature.Clearcoat;
        else mat.Features &= ~StrataFeature.Clearcoat;
        if (useIridescence) mat.Features |= StrataFeature.Iridescence;
        else mat.Features &= ~StrataFeature.Iridescence;
        if (useSheen) mat.Features |= StrataFeature.Sheen;
        else mat.Features &= ~StrataFeature.Sheen;
        if (useAnisotropy) mat.Features |= StrataFeature.Anisotropy;
        else mat.Features &= ~StrataFeature.Anisotropy;
        if (useSkin) mat.Features |= StrataFeature.Skin;
        else mat.Features &= ~StrataFeature.Skin;
        if (useHair) mat.Features |= StrataFeature.Hair;
        else mat.Features &= ~StrataFeature.Hair;
        return mat;
    }

    private static int _strataLiveCounter;

    private static void RebuildLiveMaterial()
    {
        if (_editorViewportRenderer == null) return;
        try
        {
            // Fresh name forces re-upload (override cache is name-keyed).
            var mat = BuildConfiguredMaterial($"{_strataName}_live{++_strataLiveCounter}");
            _editorViewportRenderer.StrataOverride = mat;
            _editorViewportRenderer.InvalidateStrata();
            _strataStatus = $"Live: {mat.Name} (mask=0x{(uint)mat.Features:X})";
        }
        catch (Exception ex)
        {
            _strataStatus = $"Rebuild failed: {ex.Message}";
        }
    }

    private static void AssignStrataToSlot()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_strataTargetMesh) || !System.IO.File.Exists(_strataTargetMesh))
            {
                _strataStatus = "Set a valid mesh .blueskyasset path first.";
                return;
            }
            if (!int.TryParse(_strataTargetSlot, out int slot) || slot < 0 || slot > 63)
            {
                _strataStatus = "Slot must be 0-63.";
                return;
            }
            string? meshDir = System.IO.Path.GetDirectoryName(_strataTargetMesh);
            if (meshDir == null)
            {
                _strataStatus = "Bad mesh path.";
                return;
            }
            var (strataDir, _) = BlueSky.Core.Assets.StrataImportWriter.EnsureMaterialsLayout(meshDir);
            var mat = BuildConfiguredMaterial($"{System.IO.Path.GetFileNameWithoutExtension(_strataTargetMesh)}_slot{slot}");
            string stratamatPath = BlueSky.Core.Assets.StrataImportWriter.WriteStrataMaterial(strataDir, mat);

            var asset = BlueSky.Core.Assets.BlueAsset.Load(_strataTargetMesh);
            if (asset == null)
            {
                _strataStatus = "Mesh asset unreadable.";
                return;
            }
            BlueSky.Core.Assets.StrataImportWriter.LinkSlot(asset.Metadata, slot, stratamatPath);
            asset.Save(_strataTargetMesh);

            // Force renderer to pick up the new link next frame.
            _editorViewportRenderer?.InvalidateMeshGpuCache(_strataTargetMesh);
            _strataStatus = $"Assigned slot {slot} → {System.IO.Path.GetFileName(stratamatPath)}";
        }
        catch (Exception ex)
        {
            _strataStatus = $"Assign failed: {ex.Message}";
        }
    }

    private static void SaveOpenStrataFile()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_strataOpenPath))
            {
                _strataStatus = "No file open — double-click a .stratamat first.";
                return;
            }
            var mat = BuildConfiguredMaterial(_strataName);
            mat.Save(_strataOpenPath);
            _editorViewportRenderer?.InvalidateStrata();
            _strataStatus = $"Saved {System.IO.Path.GetFileName(_strataOpenPath)} (mask=0x{(uint)mat.Features:X})";
        }
        catch (Exception ex)
        {
            _strataStatus = $"Save failed: {ex.Message}";
        }
    }
}
