using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using BlueSky.Core.Assets;
using BlueSky.Editor.UI;
using BlueSky.Rendering.Materials;
using NotBSRenderer;

namespace BlueSky.Editor;

/// <summary>
/// Visual Node-Graph Material Editor for mapping textures to PBR properties.
/// Now uses Material System V2 (MaterialAssetV2).
/// </summary>
public class MaterialEditor
{
    public bool IsOpen { get; set; } = false;
    
    /// <summary>
    /// Fired after a successful save. Subscribers (e.g. ViewportRenderer) should
    /// invalidate their material/texture caches for the saved path.
    /// </summary>
    public event Action<string>? OnSaved;
    private string _assetPath = "";
    private MaterialAssetV2? _currentMaterialV2;
    private MaterialAsset? _currentAsset; // Legacy support
    private bool _isDirty = false;
    private bool _useV2 = true; // Use Material System V2 by default

    // Node layout constants
    private const float OutputNodeX = 600f;
    private const float OutputNodeY = 200f;
    private const float TexNodeX = 100f;
    private const float NodeWidth = 200f;
    private const float NodeHeight = 150f;

    // Input ports on the BSDF node
    private readonly string[] _ports = { "Base Color", "Normal", "RMA (Rough/Metal/AO)" };

    // Fake node state
    private bool _hasAlbedoNode;
    private bool _hasNormalNode;
    private bool _hasRmaNode;

    // Asset picker state
    private bool _showAssetPicker = false;
    private Action<string>? _assetPickerCallback = null;
    private string _assetPickerTitle = "";

    public void Open(string assetPath)
    {
        _assetPath = assetPath;
        
        // Try loading as Material System V2 first
        _currentMaterialV2 = MaterialAssetV2.Load(assetPath);
        
        if (_currentMaterialV2 != null)
        {
            _useV2 = true;
            _hasAlbedoNode = _currentMaterialV2.Textures.ContainsKey("albedoMap");
            _hasNormalNode = _currentMaterialV2.Textures.ContainsKey("normalMap");
            _hasRmaNode = _currentMaterialV2.Textures.ContainsKey("rmaMap");
            Console.WriteLine($"[MaterialEditor] Opened Material V2: {assetPath}");
        }
        else
        {
            // Fallback to legacy material
            _useV2 = false;
            _currentAsset = MaterialAsset.Load(assetPath) ?? new MaterialAsset { MaterialName = System.IO.Path.GetFileNameWithoutExtension(assetPath) };
            _hasAlbedoNode = !string.IsNullOrEmpty(_currentAsset.AlbedoTexturePath);
            _hasNormalNode = !string.IsNullOrEmpty(_currentAsset.NormalTexturePath);
            _hasRmaNode = !string.IsNullOrEmpty(_currentAsset.RMATexturePath);
            Console.WriteLine($"[MaterialEditor] Opened Legacy Material: {assetPath}");
        }
        
        _isDirty = false;
        IsOpen = true;
    }

    public void Save()
    {
        if (!_isDirty) return;
        
        if (_useV2 && _currentMaterialV2 != null)
        {
            // Save Material System V2
            Console.WriteLine($"[MaterialEditor] Saving Material V2: {_assetPath}");
            Console.WriteLine($"[MaterialEditor] Material name: {_currentMaterialV2.Name}");
            Console.WriteLine($"[MaterialEditor] Features: {_currentMaterialV2.Features}");
            
            _currentMaterialV2.Save(_assetPath);
            _isDirty = false;
            Console.WriteLine($"[MaterialEditor] ✓ Saved Material V2: {_assetPath}");
            OnSaved?.Invoke(_assetPath);
        }
        else if (_currentAsset != null)
        {
            // Save legacy material
            Console.WriteLine($"[MaterialEditor] Saving Legacy Material: {_assetPath}");
            bool success = _currentAsset.Save(_assetPath);
            
            if (success)
            {
                _isDirty = false;
                Console.WriteLine($"[MaterialEditor] ✓ Saved legacy material: {_assetPath}");
                OnSaved?.Invoke(_assetPath);
            }
            else
            {
                Console.WriteLine($"[MaterialEditor] ✗ Save failed!");
            }
        }
    }

    public void Render(NotBSUI ui, float x, float y, float width, float height)
    {
        if (!IsOpen || (_currentAsset == null && _currentMaterialV2 == null)) return;

        // Show asset picker overlay if active
        if (_showAssetPicker)
        {
            DrawAssetPicker(ui, x, y, width, height);
            return;
        }

        // Background
        ui.RoundedPanel(x, y, width, height, new Vector4(0.12f, 0.12f, 0.14f, 1), 8f);

        // Title bar
        ui.RoundedGradientPanel(x, y, width, 40,
            new Vector4(0.3f, 0.4f, 0.5f, 1),
            new Vector4(0.2f, 0.3f, 0.4f, 1), 8f);

        ui.SetCursor(x + 15, y + 12);
        string materialName = _useV2 ? _currentMaterialV2?.Name ?? "Unknown" : _currentAsset?.MaterialName ?? "Unknown";
        string version = _useV2 ? " (V2)" : " (Legacy)";
        ui.Text($"Material Editor - {materialName}{version}", new Vector4(0.95f, 0.95f, 0.95f, 1));

        // Close button
        float closeX = x + width - 40;
        if (ui.ButtonEx(closeX, y + 8, 30, 24, "X",
            new Vector4(0.6f, 0.2f, 0.2f, 1),
            new Vector4(0.7f, 0.25f, 0.25f, 1),
            new Vector4(0.5f, 0.15f, 0.15f, 1),
            new Vector4(0, 0, 0, 0.3f),
            Vector4.One))
        {
            if (_isDirty) Save();
            IsOpen = false;
            return;
        }

        // Canvas Area
        float canvasX = x + 10;
        float canvasY = y + 50;
        float canvasW = width - 20;
        float canvasH = height - 60;
        ui.RoundedPanel(canvasX, canvasY, canvasW, canvasH, new Vector4(0.08f, 0.08f, 0.1f, 1), 6f);
        DrawGrid(ui, canvasX, canvasY, canvasW, canvasH);

        // --- Draw Edges First ---
        float outPortBaseY = canvasY + OutputNodeY + 45;
        
        if (_hasAlbedoNode) DrawEdge(ui, canvasX + TexNodeX + NodeWidth, canvasY + 150 + 45, canvasX + OutputNodeX, outPortBaseY);
        if (_hasNormalNode) DrawEdge(ui, canvasX + TexNodeX + NodeWidth, canvasY + 350 + 45, canvasX + OutputNodeX, outPortBaseY + 30);
        if (_hasRmaNode) DrawEdge(ui, canvasX + TexNodeX + NodeWidth, canvasY + 550 + 45, canvasX + OutputNodeX, outPortBaseY + 60);

        // --- Draw Output Node (Principled BSDF) ---
        DrawNode(ui, canvasX + OutputNodeX, canvasY + OutputNodeY, "Principled BSDF", new Vector4(0.2f, 0.5f, 0.3f, 1));
        for (int i = 0; i < _ports.Length; i++)
        {
            ui.SetCursor(canvasX + OutputNodeX + 20, canvasY + OutputNodeY + 40 + (i * 30));
            ui.Text(_ports[i], new Vector4(0.8f, 0.8f, 0.8f, 1));
            ui.Circle(canvasX + OutputNodeX, canvasY + OutputNodeY + 45 + (i * 30), 6, new Vector4(0.8f, 0.8f, 0.3f, 1), true);
        }

        // --- Draw Texture Nodes ---
        if (_hasAlbedoNode)
        {
            string albedoPath = _useV2 ? (_currentMaterialV2?.Textures.ContainsKey("albedoMap") == true ? _currentMaterialV2.Textures["albedoMap"].Path : "") : _currentAsset?.AlbedoTexturePath ?? "";
            DrawTextureNode(ui, canvasX + TexNodeX, canvasY + 150, "Albedo Texture", albedoPath, 
                p => { 
                    if (_useV2 && _currentMaterialV2 != null) 
                        _currentMaterialV2.Textures["albedoMap"] = new TextureSlot { Path = p, SamplerPreset = "anisotropic_repeat", IsSRGB = true }; 
                    else if (_currentAsset != null) 
                        _currentAsset.AlbedoTexturePath = p; 
                    _isDirty = true; 
                }, 
                () => { 
                    _hasAlbedoNode = false; 
                    if (_useV2 && _currentMaterialV2 != null) 
                        _currentMaterialV2.Textures.Remove("albedoMap"); 
                    else if (_currentAsset != null) 
                        _currentAsset.AlbedoTexturePath = ""; 
                    _isDirty = true; 
                });
        }
        if (_hasNormalNode)
        {
            string normalPath = _useV2 ? (_currentMaterialV2?.Textures.ContainsKey("normalMap") == true ? _currentMaterialV2.Textures["normalMap"].Path : "") : _currentAsset?.NormalTexturePath ?? "";
            DrawTextureNode(ui, canvasX + TexNodeX, canvasY + 350, "Normal Texture", normalPath, 
                p => { 
                    if (_useV2 && _currentMaterialV2 != null) 
                        _currentMaterialV2.Textures["normalMap"] = new TextureSlot { Path = p, SamplerPreset = "anisotropic_repeat", IsSRGB = false }; 
                    else if (_currentAsset != null) 
                        _currentAsset.NormalTexturePath = p; 
                    _isDirty = true; 
                }, 
                () => { 
                    _hasNormalNode = false; 
                    if (_useV2 && _currentMaterialV2 != null) 
                        _currentMaterialV2.Textures.Remove("normalMap"); 
                    else if (_currentAsset != null) 
                        _currentAsset.NormalTexturePath = ""; 
                    _isDirty = true; 
                });
        }
        if (_hasRmaNode)
        {
            string rmaPath = _useV2 ? (_currentMaterialV2?.Textures.ContainsKey("rmaMap") == true ? _currentMaterialV2.Textures["rmaMap"].Path : "") : _currentAsset?.RMATexturePath ?? "";
            DrawTextureNode(ui, canvasX + TexNodeX, canvasY + 550, "RMA Texture", rmaPath, 
                p => { 
                    if (_useV2 && _currentMaterialV2 != null) 
                        _currentMaterialV2.Textures["rmaMap"] = new TextureSlot { Path = p, SamplerPreset = "anisotropic_repeat", IsSRGB = false, Channels = "RMA" }; 
                    else if (_currentAsset != null) 
                        _currentAsset.RMATexturePath = p; 
                    _isDirty = true; 
                }, 
                () => { 
                    _hasRmaNode = false; 
                    if (_useV2 && _currentMaterialV2 != null) 
                        _currentMaterialV2.Textures.Remove("rmaMap"); 
                    else if (_currentAsset != null) 
                        _currentAsset.RMATexturePath = ""; 
                    _isDirty = true; 
                });
        }

        // --- Toolbar ---
        DrawToolbar(ui, x, y + height - 50, width, 50);
    }

    private void DrawTextureNode(NotBSUI ui, float nx, float ny, string title, string path, Action<string> onPathChanged, Action onDelete)
    {
        DrawNode(ui, nx, ny, title, new Vector4(0.4f, 0.3f, 0.2f, 1));
        
        ui.SetCursor(nx + 10, ny + 40);
        ui.Text("Path:", new Vector4(0.7f, 0.7f, 0.7f, 1));
        
        ui.SetCursor(nx + 10, ny + 60);
        string displayPath = string.IsNullOrEmpty(path) ? "None" : System.IO.Path.GetFileName(path);
        ui.Text(displayPath, new Vector4(0.9f, 0.9f, 0.9f, 1));

        // Output port
        ui.Circle(nx + NodeWidth, ny + 45, 6, new Vector4(0.8f, 0.8f, 0.3f, 1), true);

        // Delete button
        if (ui.ButtonEx(nx + 10, ny + NodeHeight - 35, 60, 25, "Remove", new Vector4(0.6f, 0.2f, 0.2f, 1), new Vector4(0.7f, 0.3f, 0.3f, 1), new Vector4(0.5f, 0.1f, 0.1f, 1), new Vector4(0, 0, 0, 0.3f), Vector4.One))
        {
            onDelete();
        }

        // Browse button
        if (ui.ButtonEx(nx + 80, ny + NodeHeight - 35, 110, 25, "Browse...", new Vector4(0.2f, 0.4f, 0.6f, 1), new Vector4(0.3f, 0.5f, 0.7f, 1), new Vector4(0.1f, 0.3f, 0.5f, 1), new Vector4(0, 0, 0, 0.3f), Vector4.One))
        {
            // Open asset picker for texture selection
            _showAssetPicker = true;
            _assetPickerCallback = onPathChanged;
            _assetPickerTitle = $"Select Texture for {title}";
        }
    }

    private void DrawAssetPicker(NotBSUI ui, float x, float y, float width, float height)
    {
        // Semi-transparent overlay
        ui.Panel(x, y, width, height, new Vector4(0, 0, 0, 0.7f));

        // Picker dialog
        float dialogW = 600;
        float dialogH = 500;
        float dialogX = x + (width - dialogW) / 2;
        float dialogY = y + (height - dialogH) / 2;

        ui.Shadow(dialogX, dialogY, dialogW, dialogH, 5, 5, 0.5f);
        ui.RoundedPanel(dialogX, dialogY, dialogW, dialogH, new Vector4(0.15f, 0.15f, 0.17f, 1), 10f);

        // Title bar
        ui.RoundedGradientPanel(dialogX, dialogY, dialogW, 40,
            new Vector4(0.25f, 0.35f, 0.45f, 1),
            new Vector4(0.2f, 0.3f, 0.4f, 1), 10f);
        
        ui.SetCursor(dialogX + 15, dialogY + 12);
        ui.Text(_assetPickerTitle, Vector4.One);

        // Close button
        if (ui.ButtonEx(dialogX + dialogW - 40, dialogY + 8, 30, 24, "X",
            new Vector4(0.6f, 0.2f, 0.2f, 1),
            new Vector4(0.7f, 0.25f, 0.25f, 1),
            new Vector4(0.5f, 0.15f, 0.15f, 1),
            new Vector4(0, 0, 0, 0.3f),
            Vector4.One))
        {
            _showAssetPicker = false;
            _assetPickerCallback = null;
        }

        // Content area
        float contentY = dialogY + 50;
        float contentH = dialogH - 100;
        
        ui.SetCursor(dialogX + 20, contentY + 10);
        ui.Text("Select a texture asset from your project:", new Vector4(0.8f, 0.8f, 0.8f, 1));

        // List imported texture assets
        float listY = contentY + 40;
        float itemH = 40;
        int itemIndex = 0;

        // Scan for .blueskyasset files with type Texture in Assets folder
        string assetsDir = ProjectManager.AssetsDir ?? "Assets";
        if (Directory.Exists(assetsDir))
        {
            var assetFiles = Directory.GetFiles(assetsDir, "*.blueskyasset", SearchOption.AllDirectories);
            
            foreach (var assetPath in assetFiles)
            {
                var asset = BlueAsset.Load(assetPath);
                if (asset != null && asset.Type == AssetType.Texture)
                {
                    float itemY = listY + (itemIndex * itemH);
                    
                    // Item background
                    var bgColor = new Vector4(0.2f, 0.22f, 0.25f, 1);
                    ui.RoundedPanel(dialogX + 20, itemY, dialogW - 40, itemH - 5, bgColor, 6f);

                    // Asset name
                    ui.SetCursor(dialogX + 30, itemY + 8);
                    ui.Text(asset.AssetName, new Vector4(0.9f, 0.9f, 0.9f, 1));

                    // Metadata
                    if (asset.Metadata.TryGetValue("width", out var w) && asset.Metadata.TryGetValue("height", out var h))
                    {
                        ui.SetCursor(dialogX + 30, itemY + 22);
                        ui.Text($"{w}x{h}", new Vector4(0.6f, 0.6f, 0.6f, 1));
                    }

                    // Select button
                    if (ui.ButtonEx(dialogX + dialogW - 120, itemY + 5, 80, 30, "Select",
                        new Vector4(0.2f, 0.5f, 0.3f, 1),
                        new Vector4(0.3f, 0.6f, 0.4f, 1),
                        new Vector4(0.1f, 0.4f, 0.2f, 1),
                        new Vector4(0, 0, 0, 0.3f),
                        Vector4.One))
                    {
                        _assetPickerCallback?.Invoke(assetPath);
                        _showAssetPicker = false;
                        _assetPickerCallback = null;
                    }

                    itemIndex++;
                }
            }
        }

        if (itemIndex == 0)
        {
            ui.SetCursor(dialogX + 20, listY + 10);
            ui.Text("No texture assets found. Import textures with Cmd+I first.", new Vector4(0.7f, 0.5f, 0.3f, 1));
        }

        // Bottom buttons
        if (ui.ButtonEx(dialogX + 20, dialogY + dialogH - 45, 150, 35, "Import New Texture",
            new Vector4(0.25f, 0.35f, 0.45f, 1),
            new Vector4(0.35f, 0.45f, 0.55f, 1),
            new Vector4(0.15f, 0.25f, 0.35f, 1),
            new Vector4(0, 0, 0, 0.5f),
            Vector4.One))
        {
            // Trigger import dialog (Cmd+I equivalent)
            string? selectedPath = NativeFilePicker.OpenFile(
                title: "Import Texture",
                filter: "Image Files|*.png;*.jpg;*.jpeg;*.tga;*.bmp"
            );

            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Import the texture as an asset
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(selectedPath);
                    
                    // Ensure Assets directory exists
                    if (!Directory.Exists(assetsDir))
                    {
                        Directory.CreateDirectory(assetsDir);
                        Console.WriteLine("[MaterialEditor] Created Assets directory");
                    }
                    
                    string assetPath = Path.Combine(assetsDir, fileName + ".blueskyasset");
                    
                    var asset = new BlueAsset
                    {
                        AssetId = Guid.NewGuid(),
                        AssetName = fileName,
                        Type = AssetType.Texture,
                        SourceFile = selectedPath,
                        ImportDate = DateTime.UtcNow
                    };

                    var importer = new TextureImportHandler();
                    var result = importer.Import(selectedPath, asset, null);

                    if (result.Success)
                    {
                        asset.PayloadData = result.PayloadData;
                        bool saved = asset.Save(assetPath);
                        
                        if (saved)
                        {
                            _assetPickerCallback?.Invoke(assetPath);
                            _showAssetPicker = false;
                            _assetPickerCallback = null;
                            
                            Console.WriteLine($"[MaterialEditor] ✓ Imported texture: {assetPath}");
                        }
                        else
                        {
                            Console.WriteLine($"[MaterialEditor] ✗ Failed to save texture asset");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[MaterialEditor] ✗ Import failed: {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MaterialEditor] Import failed: {ex.Message}");
                }
            }
        }
    }

    private void DrawNode(NotBSUI ui, float nx, float ny, string title, Vector4 headerColor)
    {
        ui.Shadow(nx, ny, NodeWidth, NodeHeight, 3, 3, 0.4f);
        ui.RoundedPanel(nx, ny, NodeWidth, NodeHeight, new Vector4(0.2f, 0.22f, 0.25f, 1), 8f);
        ui.RoundedGradientPanel(nx, ny, NodeWidth, 30, headerColor, headerColor * 0.8f, 8f);
        ui.SetCursor(nx + 10, ny + 8);
        ui.Text(title, Vector4.One);
    }

    private void DrawEdge(NotBSUI ui, float x1, float y1, float x2, float y2)
    {
        ui.Line(x1, y1, x2, y2, new Vector4(0.7f, 0.7f, 0.5f, 1));
    }

    private void DrawGrid(NotBSUI ui, float x, float y, float width, float height)
    {
        float gridSize = 50;
        var gridColor = new Vector4(0.15f, 0.15f, 0.17f, 1);
        for (float gx = 0; gx < width; gx += gridSize) ui.Panel(x + gx, y, 1, height, gridColor);
        for (float gy = 0; gy < height; gy += gridSize) ui.Panel(x, y + gy, width, 1, gridColor);
    }

    private void DrawToolbar(NotBSUI ui, float x, float y, float width, float height)
    {
        ui.RoundedPanel(x, y, width, height, new Vector4(0.15f, 0.15f, 0.17f, 1), 6f);
        
        float btnX = x + 10;
        
        if (ui.ButtonEx(btnX, y + 10, 120, 30, "+ Albedo Node", new Vector4(0.25f, 0.35f, 0.45f, 1), new Vector4(0.35f, 0.45f, 0.55f, 1), new Vector4(0.15f, 0.25f, 0.35f, 1), new Vector4(0, 0, 0, 0.5f), Vector4.One))
        {
            _hasAlbedoNode = true;
            _isDirty = true;
        }
        btnX += 130;

        if (ui.ButtonEx(btnX, y + 10, 120, 30, "+ Normal Node", new Vector4(0.25f, 0.35f, 0.45f, 1), new Vector4(0.35f, 0.45f, 0.55f, 1), new Vector4(0.15f, 0.25f, 0.35f, 1), new Vector4(0, 0, 0, 0.5f), Vector4.One))
        {
            _hasNormalNode = true;
            _isDirty = true;
        }
        btnX += 130;

        if (ui.ButtonEx(btnX, y + 10, 120, 30, "+ RMA Node", new Vector4(0.25f, 0.35f, 0.45f, 1), new Vector4(0.35f, 0.45f, 0.55f, 1), new Vector4(0.15f, 0.25f, 0.35f, 1), new Vector4(0, 0, 0, 0.5f), Vector4.One))
        {
            _hasRmaNode = true;
            _isDirty = true;
        }
        
        if (_isDirty)
        {
            if (ui.ButtonEx(x + width - 120, y + 10, 100, 30, "Save", new Vector4(0.2f, 0.6f, 0.3f, 1), new Vector4(0.3f, 0.7f, 0.4f, 1), new Vector4(0.1f, 0.5f, 0.2f, 1), new Vector4(0, 0, 0, 0.5f), Vector4.One))
            {
                Save();
            }
        }
    }
}
