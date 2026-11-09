using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using BlueSky.Editor.UI;
using BlueSky.Aegis;
using BlueSky.Aegis.Input;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.BSR;
using BlueSky.Core.Scripting;
using BlueSky.Meridian;
using BlueSky.BSR.RHI;

namespace BlueSky.Editor;

partial class Program
{
    // ─────────────────────────────────────────────────────────────────────
    private static void ImportFilesDialog()
    {
        try
        {
            if (string.IsNullOrEmpty(ProjectManager.CurrentProjectDir))
            {
                Console.WriteLine("[Editor] No project open, cannot import assets");
                return;
            }

            // Use macOS native file dialog
            if (_window is Aegis.macOS.CocoaWindow cocoaWindow)
            {
                var files = cocoaWindow.ShowOpenFileDialog();
                if (files != null && files.Length > 0)
                {
                    HandleFilesDropped(files);
                }
            }
            // Use Win32 native file dialog (GetOpenFileNameW — no WinForms needed)
            else if (_window is Aegis.Windows.Win32Window win32Window)
            {
                var files = win32Window.ShowOpenFileDialog();
                if (files != null && files.Length > 0)
                {
                    HandleFilesDropped(files);
                }
            }
            else
            {
                // Fallback: use cross-platform NativeFilePicker
                var file = NativeFilePicker.OpenFile("Import Asset", 
                    "StrataPack|*.stratapack|3D Models|*.obj;*.fbx;*.gltf;*.glb|Images|*.png;*.jpg;*.jpeg;*.bmp;*.tga|Audio|*.wav;*.ogg;*.mp3;*.flac|All Files|*.*");
                if (!string.IsNullOrEmpty(file))
                {
                    HandleFilesDropped(new[] { file });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Error opening file dialog: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private static void HandleFilesDropped(string[] files)
    {
        try
        {
            if (string.IsNullOrEmpty(ProjectManager.CurrentProjectDir))
            {
                Log("No project open, cannot import assets");
                return;
            }

            // Filter for mesh files that need import dialog
            string[] meshExtensions = { ".obj", ".fbx", ".gltf", ".glb", ".stratapack" };
            var meshFiles = files.Where(f => meshExtensions.Contains(Path.GetExtension(f).ToLower())).ToArray();
            
            if (meshFiles.Length > 0)
            {
                // Show import dialog for mesh files
                ShowImportDialog(meshFiles);
            }

            // Handle other file types (textures, audio, etc.) immediately
            string[] otherExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".wav", ".ogg", ".mp3", ".flac", ".echosource", ".echosuper", ".blueskyasset" };
            foreach (var file in files.Where(f => !meshFiles.Contains(f)))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (!otherExtensions.Contains(ext))
                {
                    Log($"Skipping unsupported file: {Path.GetFileName(file)}");
                    continue;
                }

                try
                {
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga")
                    {
                        var importer = new BlueSky.Core.Assets.AssetImporter(ProjectManager.CurrentProjectDir!);
                        var importedAsset = importer.Import(file);
                        if (importedAsset != null)
                            Log($"✓ Imported Texture: {importedAsset.AssetName}");
                        else
                            Log($"✗ Failed to import Texture: {Path.GetFileName(file)}");
                    }
                    else
                    {
                        string destPath = Path.Combine(ProjectManager.AssetsDir!, Path.GetFileName(file));
                        File.Copy(file, destPath, overwrite: true);
                        Log($"✓ Copied: {Path.GetFileName(destPath)}");

                        // Raw audio becomes a canonical Echo Source next to the original.
                        if (ext == ".wav" || ext == ".ogg" || ext == ".mp3" || ext == ".flac")
                        {
                            var converted = BlueSky.Echo.EchoSourceAsset.Import(destPath);
                            if (converted != null)
                                Log($"✓ Converted to Echo Source: {Path.GetFileNameWithoutExtension(destPath)}.echosource");
                            else
                                Log($"✗ Echo Source conversion failed: {Path.GetFileName(destPath)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"✗ Failed to copy {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error handling dropped files: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Import Dialog - Shows when files are dragged & dropped
    // ─────────────────────────────────────────────────────────────────────
    private static void ShowImportDialog(string[] files)
    {
        _pendingImportFiles = files.Where(f => 
            f.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".stratapack", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (_pendingImportFiles.Length == 0)
        {
            Log("No valid mesh files to import");
            return;
        }

        _importMeshPreviewNames = _pendingImportFiles.Select(p => Path.GetFileNameWithoutExtension(p) ?? "").ToArray();
        _importSelectedMeshIndex = 0;
        _importScale = 1.0f;
        _importGenerateCollider = true;
        _showImportDialog = true;

        Log($"Import dialog opened for {_pendingImportFiles.Length} file(s)");
    }

    private static void DrawImportDialog(EditorUI ui, float windowW, float windowH)
    {
        if (!_showImportDialog || _pendingImportFiles.Length == 0) return;

        // Centered dialog
        float dialogW = 420, dialogH = 340;
        float dx = (windowW - dialogW) / 2;
        float dy = (windowH - dialogH) / 2;

        // Colors
        var bgOverlay = new System.Numerics.Vector4(0, 0, 0, 0.6f);
        var bgDialog = new System.Numerics.Vector4(0.12f, 0.125f, 0.13f, 1f);
        var bgHeader = new System.Numerics.Vector4(0.15f, 0.155f, 0.16f, 1f);
        var bgSection = new System.Numerics.Vector4(0.10f, 0.105f, 0.11f, 1f);
        var accentBlue = new System.Numerics.Vector4(0.25f, 0.55f, 0.95f, 1f);
        var accentBlueLight = new System.Numerics.Vector4(0.40f, 0.70f, 1.0f, 1f);
        var accentGreen = new System.Numerics.Vector4(0.30f, 0.85f, 0.50f, 1f);
        var accentRed = new System.Numerics.Vector4(0.90f, 0.40f, 0.40f, 1f);
        var textTitle = new System.Numerics.Vector4(0.98f, 0.98f, 1.0f, 1f);
        var textNormal = new System.Numerics.Vector4(0.80f, 0.82f, 0.85f, 1f);
        var textDim = new System.Numerics.Vector4(0.55f, 0.57f, 0.60f, 1f);
        var borderSubtle = new System.Numerics.Vector4(0.25f, 0.27f, 0.30f, 1f);

        // Darken background (click to cancel)
        ui.Panel(0, 0, windowW, windowH, bgOverlay);

        // Dialog panel with shadow
        ui.Shadow(dx + 4, dy + 6, dialogW, dialogH, 6, 10, 0.5f);
        ui.Panel(dx, dy, dialogW, dialogH, bgDialog);
        ui.Panel(dx, dy, dialogW, 40, bgHeader);
        ui.Panel(dx, dy + 40, dialogW, 1, borderSubtle);

        // Header
        ui.SetCursor(dx + 16, dy + 12);
        ui.Text("[+] Import Mesh", textTitle);

        // Close button (X)
        uint closeBtnId = 10001;
        if (ui.ButtonEx(dx + dialogW - 36, dy + 8, 28, 28, "✕",
            new System.Numerics.Vector4(0.2f, 0.22f, 0.25f, 1f),
            new System.Numerics.Vector4(0.85f, 0.40f, 0.40f, 1f),
            new System.Numerics.Vector4(0.7f, 0.30f, 0.30f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            textNormal, closeBtnId))
        {
            _showImportDialog = false;
            Log("Import cancelled");
        }

        float contentY = dy + 55;

        // File list
        ui.SetCursor(dx + 16, contentY);
        ui.Text($"Files to import ({_pendingImportFiles.Length}):", textNormal);
        contentY += 22;

        // File list box
        ui.Panel(dx + 16, contentY, dialogW - 32, 60, bgSection);
        float fileY = contentY + 6;
        foreach (var file in _pendingImportFiles)
        {
            ui.SetCursor(dx + 24, fileY);
            string fileName = Path.GetFileName(file);
            if (fileName.Length > 45) fileName = fileName[..42] + "...";
            ui.Text(" - " + fileName, textDim);
            fileY += 18;
        }
        contentY += 70;

        // Scale setting
        ui.SetCursor(dx + 16, contentY);
        ui.Text("Scale:", textNormal);
        ui.SetCursor(dx + 70, contentY);
        ui.Text($"{_importScale:F2}x", accentBlueLight);
        contentY += 28;

        // Scale buttons
        uint scaleDownId = 10002, scaleUpId = 10003;
        if (ui.ButtonEx(dx + 16, contentY, 40, 28, "-",
            new System.Numerics.Vector4(0.15f, 0.16f, 0.18f, 1f),
            new System.Numerics.Vector4(0.22f, 0.24f, 0.27f, 1f),
            new System.Numerics.Vector4(0.12f, 0.13f, 0.15f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            textNormal, scaleDownId))
        {
            _importScale = Math.Max(0.01f, _importScale - 0.1f);
        }
        if (ui.ButtonEx(dx + 62, contentY, 40, 28, "+",
            new System.Numerics.Vector4(0.15f, 0.16f, 0.18f, 1f),
            new System.Numerics.Vector4(0.22f, 0.24f, 0.27f, 1f),
            new System.Numerics.Vector4(0.12f, 0.13f, 0.15f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            textNormal, scaleUpId))
        {
            _importScale = Math.Min(10.0f, _importScale + 0.1f);
        }
        contentY += 45;

        // Checkboxes
        // Generate Collider
        uint colliderCheckId = 10004;
        var checkBg = _importGenerateCollider ? accentBlue : new System.Numerics.Vector4(0.15f, 0.16f, 0.18f, 1f);
        if (ui.ButtonEx(dx + 16, contentY, 22, 22, _importGenerateCollider ? "✓" : "",
            checkBg,
            new System.Numerics.Vector4(0.30f, 0.60f, 1.0f, 1f),
            new System.Numerics.Vector4(0.20f, 0.50f, 0.90f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            textTitle, colliderCheckId))
        {
            _importGenerateCollider = !_importGenerateCollider;
        }
        ui.SetCursor(dx + 44, contentY + 3);
        ui.Text("Generate Collider", textNormal);
        contentY += 32;

        // Action buttons at bottom
        float btnY = dy + dialogH - 45;

        // Cancel button
        uint cancelBtnId = 10006;
        if (ui.ButtonEx(dx + 16, btnY, 100, 32, "Cancel",
            new System.Numerics.Vector4(0.18f, 0.19f, 0.21f, 1f),
            new System.Numerics.Vector4(0.25f, 0.26f, 0.29f, 1f),
            new System.Numerics.Vector4(0.15f, 0.16f, 0.18f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            textNormal, cancelBtnId))
        {
            _showImportDialog = false;
            Log("Import cancelled");
        }

        // Import All button
        uint importBtnId = 10007;
        if (ui.ButtonEx(dx + dialogW - 126, btnY, 110, 32, "Import All",
            accentBlue,
            new System.Numerics.Vector4(0.35f, 0.65f, 1.0f, 1f),
            new System.Numerics.Vector4(0.20f, 0.50f, 0.90f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.4f),
            textTitle, importBtnId))
        {
            // Perform import
            PerformImport();
            _showImportDialog = false;
        }
    }

    private static string FindProjectRoot(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            // Check for .BlueSkyProj project files (the actual engine project format)
            var projFiles = Directory.GetFiles(dir, "*.BlueSkyProj");
            if (projFiles.Length > 0)
            {
                return dir;
            }
            // Also check legacy .blueproject marker
            if (File.Exists(Path.Combine(dir, ".blueproject")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback to currently open project
        return ProjectManager.CurrentProjectDir ?? "";
    }

    private static void PerformImport()
    {
        if (_pendingImportFiles.Length == 0) return;

        Log($"Importing {_pendingImportFiles.Length} file(s) with scale {_importScale:F2}x...");

        try
        {
            // Create AssetImporter
            var importer = new BlueSky.Core.Assets.AssetImporter(ProjectManager.CurrentProjectDir!);

            // Configure import options
            var importOptions = new BlueSky.Core.Assets.ImportOptions
            {
                Settings = new Dictionary<string, object>
                {
                    ["scale"] = _importScale,
                    ["generateCollider"] = _importGenerateCollider
                }
            };

            // Import each file
            foreach (var file in _pendingImportFiles)
            {
                try
                {
                    var asset = importer.Import(file, importOptions);
                    if (asset != null)
                    {
                        string options = "";
                        if (_importGenerateCollider) options += " +Collider";

                        Log($"✓ Imported: {asset.AssetName} → {asset.AssetName}.blueskyasset (scale: {_importScale:F2}x{options})");
                        
                        // Evict all cached GPU data for this asset so the viewport
                        // picks up the freshly imported version immediately.
                        string assetDir = Path.Combine(ProjectManager.AssetsDir ?? "", asset.AssetName);
                        _editorViewportRenderer?.InvalidateAssetDirectory(assetDir);
                    }
                    else
                    {
                        Log($"✗ Failed to import {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"✗ Failed to import {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Log("Import complete!");
        }
        catch (Exception ex)
        {
            Log($"✗ Import failed: {ex.Message}");
        }

        _pendingImportFiles = Array.Empty<string>();
    }

    private static BlueSky.Echo.AudioSource? _audioPreviewSource;

    /// <summary>
    /// Play a short editor-only preview of an audio file through Echo (2D, no entity).
    /// The regular editor frame loop advances/stops the preview source.
    /// </summary>
    internal static void PreviewAudioFile(string assetPath)
    {
        if (_echo == null)
        {
            Log("✗ Echo audio is unavailable.");
            return;
        }

        string resolved = BlueSky.Core.Assets.AssetPathResolver.Resolve(assetPath);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            Log($"✗ Audio file not found: {Path.GetFileName(assetPath)}");
            return;
        }

        if (_audioPreviewSource != null)
        {
            try { _echo.StopSound(_audioPreviewSource); } catch { /* best effort */ }
            _audioPreviewSource = null;
        }

        string clipName = "preview:" + resolved;
        string displayName = Path.GetFileNameWithoutExtension(resolved);
        _echo.LoadClip(clipName, resolved);
        _audioPreviewSource = _echo.PlaySound(
            clipName,
            Vector3.Zero,
            volume: 1.0f,
            loop: false,
            channel: BlueSky.Echo.AudioChannel.SFX,
            is3D: false);

        if (_audioPreviewSource != null)
        {
            // A preview is not a game sound: it keeps playing when Play mode pauses or stops.
            _audioPreviewSource.IgnoresGamePause = true;
            Log($"▶ Previewing audio: {displayName}");
        }
        else
            Log($"✗ Could not preview audio: {displayName}");
    }

    /// <summary>
    /// Where a dropped asset should spawn: project the mouse ray onto the Y=0 plane.
    /// Falls back to the origin when the ray is parallel or points away from the ground.
    /// </summary>
    private static BlueSky.Ramanujan.Vector3 GetViewportDropPosition()
    {
        if (_viewport == null || _input == null)
            return BlueSky.Ramanujan.Vector3.Zero;

        var ray = _viewport.GetRayFromMouse(_input.MousePosition);
        if (MathF.Abs(ray.Direction.Y) > 1e-4f)
        {
            float t = -ray.Origin.Y / ray.Direction.Y;
            if (t > 0f && t < 1000f)
            {
                var p = ray.GetPoint(t);
                return new BlueSky.Ramanujan.Vector3(p.X, p.Y, p.Z);
            }
        }

        return BlueSky.Ramanujan.Vector3.Zero;
    }

    /// <summary>
    /// Store project-relative paths (Assets/...) so scenes stay portable and fit
    /// inside AudioSourceComponent's fixed path buffer. Falls back to the full
    /// path when the asset lives outside the open project.
    /// </summary>
    private static string ToProjectRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(ProjectManager.CurrentProjectDir))
            return fullPath;

        try
        {
            string relative = Path.GetRelativePath(ProjectManager.CurrentProjectDir, fullPath).Replace('\\', '/');
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return relative;
        }
        catch
        {
            // Fall through to the absolute path.
        }

        return fullPath;
    }

    /// <summary>
    /// Spawn a placeable Audio Source entity for an audio clip. The entity is
    /// wired with a transform, an AudioSourceComponent (PlayOnStart = true) and
    /// a display name, so EchoSystem starts it when Play mode begins.
    /// Returns null when the clip cannot be found on disk.
    /// </summary>
    internal static Entity? SpawnAudioSourceEntity(World world, string assetPath, BlueSky.Ramanujan.Vector3 position)
    {
        if (world == null)
            return null;

        string resolvedAudio = BlueSky.Core.Assets.AssetPathResolver.Resolve(assetPath);
        if (string.IsNullOrWhiteSpace(resolvedAudio) || !File.Exists(resolvedAudio))
            return null;

        var audioEntity = world.CreateEntity();
        world.AddComponent(audioEntity, new TransformComponent
        {
            Position = position,
            Rotation = BlueSky.Ramanujan.Quaternion.Identity,
            Scale = BlueSky.Ramanujan.Vector3.One
        });

        var audioSource = new AudioSourceComponent(ToProjectRelativePath(resolvedAudio), volume: 1.0f, loop: false, is3D: true)
        {
            PlayOnStart = true,
            IsPlaying = false
        };

        // A Super carries its own output/spatial defaults.
        if (Path.GetExtension(resolvedAudio).Equals(BlueSky.Echo.EchoSuperAsset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var super = BlueSky.Echo.EchoSuperAsset.Load(resolvedAudio);
            if (super != null)
            {
                audioSource.Volume = super.Output.Gain;
                audioSource.Loop = super.Output.Loop;
                audioSource.Is3D = super.Output.Is3D;
                audioSource.MinDistance = super.Output.MinDistance;
                audioSource.MaxDistance = super.Output.MaxDistance;
                audioSource.AttenuationCurve = super.Output.AttenuationCurve;
            }
        }

        world.AddComponent(audioEntity, audioSource);
        world.AddComponent(audioEntity, new NameComponent(Path.GetFileNameWithoutExtension(assetPath)));

        return audioEntity;
    }

    internal static void SpawnDraggedAsset(string assetPath)
    {
        if (_world == null) return;

        uint versionBefore = _world.Version;
        SpawnDraggedAssetCore(assetPath);

        // A drop that added entities or components is a scene edit (Play mode edits are discarded on Stop).
        if (!_isPlaying && _world.Version != versionBefore)
            _sceneDirty = true;
    }

    private static void SpawnDraggedAssetCore(string assetPath)
    {
        if (_world == null) return;
        
        string ext = Path.GetExtension(assetPath).ToLower();
        
        // Handle audio clips: spawn a placeable Audio Source entity that plays on Play.
        if (ext == ".wav" || ext == ".ogg" || ext == ".mp3" || ext == ".flac" ||
            ext == ".echosource" || ext == ".echosuper")
        {
            var audioEntity = SpawnAudioSourceEntity(_world, assetPath, GetViewportDropPosition());
            if (audioEntity == null)
            {
                Log($"✗ Audio file not found: {Path.GetFileName(assetPath)}");
                return;
            }

            _selectedEntityId = (uint)audioEntity.Value.Id;
            _detailsCachedId = uint.MaxValue;

            Log($"✓ Spawned Audio Source: {Path.GetFileName(assetPath)} (3D, plays on Play)");
            return;
        }
        
        // Auto-import raw model files (GLB, GLTF, FBX, OBJ) before spawning
        if (ext == ".glb" || ext == ".gltf" || ext == ".fbx" || ext == ".obj")
        {
            Log($"⚙ Importing {Path.GetFileName(assetPath)}...");
            
            try
            {
                // Use the directory containing the GLB as the project root
                // (or find the nearest .blueproject file)
                string projectPath = FindProjectRoot(assetPath);
                if (string.IsNullOrEmpty(projectPath))
                {
                    Log("✗ No project found. Place the GLB in a BlueSky project folder.");
                    return;
                }
                
                var importer = new BlueSky.Core.Assets.AssetImporter(projectPath);
                var importedAsset = importer.Import(assetPath);
                
                if (importedAsset != null)
                {
                    // Get the path to the imported .blueskyasset file
                    var project = BlueSky.Core.Assets.BlueProject.Load(projectPath);
                    var assetsDir = project?.GetAssetsDirectory(projectPath) ?? Path.Combine(projectPath, "Assets");
                    var assetFileName = $"{importedAsset.AssetName}.blueskyasset";
                    
                    // For mesh formats, assets are in a subfolder
                    string importedAssetPath;
                    if (ext == ".glb" || ext == ".gltf" || ext == ".fbx" || ext == ".obj")
                    {
                        importedAssetPath = Path.Combine(assetsDir, importedAsset.AssetName, assetFileName);
                    }
                    else
                    {
                        importedAssetPath = Path.Combine(assetsDir, assetFileName);
                    }
                    
                    if (File.Exists(importedAssetPath))
                    {
                        Log($"✓ Imported successfully: {importedAsset.AssetName}");
                        // Recursively call with the imported asset path
                        SpawnDraggedAsset(importedAssetPath);
                        return;
                    }
                    else
                    {
                        Log($"✗ Import succeeded but asset file not found at: {importedAssetPath}");
                        return;
                    }
                }
                else
                {
                    Log($"✗ Import failed for {Path.GetFileName(assetPath)}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"✗ Import error: {ex.Message}");
                return;
            }
        }
        
        // Handle TeaScript files
        if (ext == ".tea")
        {
            // Find selected entity or create new one
            Entity targetEntity;
            
            if (_selectedEntityId > 0 && _selectedEntityId < EditorOnlyTagComponent.EditorSystemIdBase)
            {
                // Use selected entity
                var entities = _world.GetAllEntities().ToList();
                targetEntity = entities.FirstOrDefault(e => e.Id == _selectedEntityId);
                
                if (targetEntity.Id == 0)
                {
                    Log("✗ No valid entity selected. Creating new entity.");
                    targetEntity = _world.CreateEntity();
                    
                    var transform = new TransformComponent
                    {
                        Position = new BlueSky.Ramanujan.Vector3(0, 1, 0),
                        Rotation = BlueSky.Ramanujan.Quaternion.Identity,
                        Scale = BlueSky.Ramanujan.Vector3.One
                    };
                    _world.AddComponent(targetEntity, transform);
                }
            }
            else
            {
                // Create new entity
                targetEntity = _world.CreateEntity();
                
                var transform = new TransformComponent
                {
                    Position = new BlueSky.Ramanujan.Vector3(0, 1, 0),
                    Rotation = BlueSky.Ramanujan.Quaternion.Identity,
                    Scale = BlueSky.Ramanujan.Vector3.One
                };
                _world.AddComponent(targetEntity, transform);
            }
            
            // Add or update TeaScriptComponent
            var scriptComponent = new TeaScriptComponent
            {
                ScriptAssetId = assetPath,
                IsEnabled = true,
                IsInitialized = false,
                AllowRuntimeUI = false,
                BlockRuntimeInput = false,
                RuntimeInstance = 0
            };
            
            if (_world.HasComponent<TeaScriptComponent>(targetEntity))
            {
                // Update existing
                ref var existing = ref _world.GetComponent<TeaScriptComponent>(targetEntity);
                scriptComponent.AllowRuntimeUI = existing.AllowRuntimeUI;
                scriptComponent.BlockRuntimeInput = existing.BlockRuntimeInput;
                existing = scriptComponent;
                Log($"✓ Updated TeaScript on Entity_{targetEntity.Id}: {Path.GetFileName(assetPath)}");
            }
            else
            {
                // Add new
                _world.AddComponent(targetEntity, scriptComponent);
                Log($"✓ Added TeaScript to Entity_{targetEntity.Id}: {Path.GetFileName(assetPath)}");
            }
            
            return;
        }
        
        // Handle mesh assets (geometry only — material system removed)
        var header = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetPath);
        if (header != null && (header.Type == BlueSky.Core.Assets.AssetType.StaticMesh || header.Type == BlueSky.Core.Assets.AssetType.SkeletalMesh))
        {
            var entity = _world.CreateEntity();
            
            var transform = new TransformComponent
            {
                Position = new BlueSky.Ramanujan.Vector3(0, 0, 0), // Spawn at origin
                Rotation = BlueSky.Ramanujan.Quaternion.Identity,
                Scale = BlueSky.Ramanujan.Vector3.One
            };
            _world.AddComponent(entity, transform);

            // Spawn meshes without materials (geometry only)
            if (header.Type == BlueSky.Core.Assets.AssetType.SkeletalMesh)
            {
                var skeletalMesh = new BlueSky.SkyCore.Builtin.SkeletalMeshComponent(assetPath);

                _world.AddComponent(entity, skeletalMesh);
                Log($"  → Added SkeletalMesh component for {Path.GetFileName(assetPath)}");
            }
            else
            {
                var staticMesh = new BlueSky.SkyCore.Builtin.StaticMeshComponent
                {
                    MeshAssetId = assetPath
                };

                _world.AddComponent(entity, staticMesh);
            }

            Log($"✓ Spawned {header.AssetName} at {transform.Position.X},{transform.Position.Y},{transform.Position.Z}");
        }
        else
        {
            Log($"✗ Asset {System.IO.Path.GetFileName(assetPath)} is not a placeable asset.");
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    //  CONTEXT MENU
    // ═══════════════════════════════════════════════════════════════════════
    
    // ── Outliner Context Menu State ─────────────────────────────────────
    private static bool _showOutlinerContextMenu;
    private static float _outlinerContextMenuX;
    private static float _outlinerContextMenuY;

    private static void DrawOutlinerContextMenu(EditorUI ui, float x, float y)
    {
        float menuW = 200;
        float menuH = 60; // One item: Pack to Prefab
        float itemH = 28;

        // Background
        ui.Shadow(x, y, menuW, menuH, 4, 6, 0.4f);
        ui.Panel(x, y, menuW, menuH, EditorTheme.Bg2);
        ui.Panel(x, y, menuW, 1, EditorTheme.Border1);
        ui.Panel(x, y + menuH - 1, menuW, 1, EditorTheme.Border1);
        ui.Panel(x, y, 1, menuH, EditorTheme.Border1);
        ui.Panel(x + menuW - 1, y, 1, menuH, EditorTheme.Border1);

        float itemY = y + 4;

        // Pack to Prefab
        uint menuId = 9200;
        bool hasSelection = _selectedEntityId > 0 && _selectedEntityId < EditorOnlyTagComponent.EditorSystemIdBase;
        var itemBg = hasSelection ? EditorTheme.Bg2 : EditorTheme.Bg3;
        var itemText = hasSelection ? EditorTheme.TextPrimary : EditorTheme.TextDisabled;

        if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId,
            itemBg,
            hasSelection ? EditorTheme.HoverBg : itemBg,
            hasSelection ? EditorTheme.SelectionBg : itemBg))
        {
            if (hasSelection)
            {
                PackEntityToPrefab();
            }
            _showOutlinerContextMenu = false;
        }
        ui.SetCursor(x + 12, itemY + 8);
        ui.Text("📦 Pack to Prefab", itemText);

        // Close menu if clicked outside
        if (_input!.IsMouseButtonDown(MouseButton.Left))
        {
            if (!ui.IsHovering(x, y, menuW, menuH))
            {
                _showOutlinerContextMenu = false;
            }
        }
    }

    private static void PackEntityToPrefab()
    {
        if (_world == null || _selectedEntityId == 0 || _selectedEntityId >= EditorOnlyTagComponent.EditorSystemIdBase) return;

        if (!_world.TryResolveEntity(_selectedEntityId, out var entity))
        {
            Log($"⚠️ Entity_{_selectedEntityId} not found");
            return;
        }

        // Create a single-entity scene data from this entity
        var sceneData = SceneConverter.WorldToSceneDataForEntity(_world, entity, "Prefab");
        if (sceneData == null)
        {
            Log($"⚠️ Failed to create prefab data for Entity_{entity.Id}");
            return;
        }

        // Auto-generate prefab path in Assets directory
        // (Native save dialog can't work while the editor window captures the macOS event loop)
        string assetsDir = ProjectManager.AssetsDir ?? Path.Combine(ProjectManager.CurrentProjectDir ?? "", "Assets");
        string baseName = $"Entity_{entity.Id}_Prefab";
        string fileName = baseName;
        int counter = 1;
        string savePath = Path.Combine(assetsDir, $"{fileName}.bseprefab");

        // Find a unique filename
        while (File.Exists(savePath))
        {
            fileName = $"{baseName}_{counter}";
            savePath = Path.Combine(assetsDir, $"{fileName}.bseprefab");
            counter++;
        }

        try
        {
            if (SceneSerializer.SaveScene(sceneData, savePath))
                Log($"✓ Prefab saved: {Path.GetFileName(savePath)} (in Assets/)");
            else
                Log($"❌ Failed to save prefab: could not write {Path.GetFileName(savePath)}");
        }
        catch (Exception ex)
        {
            Log($"❌ Failed to save prefab: {ex.Message}");
        }
    }

    private static void DrawContextMenu(EditorUI ui, float x, float y)
    {
        float menuW = 180;
        float menuH = 190;
        float itemH = 28;
        
        // Background
        ui.Shadow(x, y, menuW, menuH, 4, 6, 0.4f);
        ui.Panel(x, y, menuW, menuH, EditorTheme.Bg2);
        ui.Panel(x, y, menuW, 1, EditorTheme.Border1);
        ui.Panel(x, y + menuH - 1, menuW, 1, EditorTheme.Border1);
        ui.Panel(x, y, 1, menuH, EditorTheme.Border1);
        ui.Panel(x + menuW - 1, y, 1, menuH, EditorTheme.Border1);
        
        float itemY = y + 4;
        
        // New TeaScript
        uint menuId1 = 9100;
        if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId1,
            EditorTheme.Bg2,
            EditorTheme.HoverBg,
            EditorTheme.SelectionBg))
        {
            CreateNewTeaScript();
            _showContextMenu = false;
        }
        ui.SetCursor(x + 12, itemY + 8);
        ui.Text("📜 New TeaScript", EditorTheme.TextPrimary);
        itemY += itemH;
        
        // New Folder
        uint menuId2 = 9101;
        if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId2,
            EditorTheme.Bg2,
            EditorTheme.HoverBg,
            EditorTheme.SelectionBg))
        {
            CreateNewFolder();
            _showContextMenu = false;
        }
        ui.SetCursor(x + 12, itemY + 8);
        ui.Text("📁 New Folder", EditorTheme.TextPrimary);
        itemY += itemH;
        
        // Separator
        ui.Panel(x + 8, itemY + 4, menuW - 16, 1, EditorTheme.Border1);
        itemY += 12;
        
        // Rename (if something is selected)
        if (_selectedAssetIndex >= 0)
        {
            uint menuId4 = 9103;
            if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId4,
                EditorTheme.Bg2,
                EditorTheme.HoverBg,
                EditorTheme.SelectionBg))
            {
                // Find selected file/folder
                var listing = Assets.GetContentBrowserListing(_currentBrowserDir);
                var dirs = listing?.Directories ?? Array.Empty<string>();
                var files = listing?.Files ?? Array.Empty<string>();
                
                int folderCount = dirs.Length;
                if (_selectedAssetIndex >= 5000 && _selectedAssetIndex < 5000 + folderCount)
                {
                    int folderIdx = _selectedAssetIndex - 5000;
                    _renameTarget = dirs[folderIdx];
                    _renameNewName = Path.GetFileName(_renameTarget);
                    _showRenameDialog = true;
                }
                else if (_selectedAssetIndex >= 6000)
                {
                    int fileIdx = _selectedAssetIndex - 6000;
                    if (fileIdx < files.Length)
                    {
                        _renameTarget = files[fileIdx];
                        _renameNewName = Path.GetFileNameWithoutExtension(_renameTarget);
                        _showRenameDialog = true;
                    }
                }
                
                _showContextMenu = false;
            }
            ui.SetCursor(x + 12, itemY + 8);
            ui.Text("✏️ Rename", EditorTheme.TextPrimary);
            itemY += itemH;
            
            // Delete (if something is selected)
            uint menuId5 = 9104;
            if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId5,
                EditorTheme.Bg2,
                new System.Numerics.Vector4(0.8f, 0.3f, 0.3f, 0.3f), // Red hover
                new System.Numerics.Vector4(0.7f, 0.2f, 0.2f, 0.5f))) // Red press
            {
                DeleteSelectedAsset();
                _showContextMenu = false;
            }
            ui.SetCursor(x + 12, itemY + 8);
            ui.Text("🗑️ Delete", EditorTheme.Red);
            itemY += itemH;
        }
        
        // Refresh
        uint menuId3 = 9102;
        if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId3,
            EditorTheme.Bg2,
            EditorTheme.HoverBg,
            EditorTheme.SelectionBg))
        {
            Assets.RefreshContentBrowserListing();
            Log("Refreshed content browser");
            _showContextMenu = false;
        }
        ui.SetCursor(x + 12, itemY + 8);
        ui.Text("🔄 Refresh", EditorTheme.TextPrimary);
        
        // Close menu if clicked outside
        if (_input!.IsMouseButtonDown(MouseButton.Left))
        {
            if (!ui.IsHovering(x, y, menuW, menuH))
            {
                _showContextMenu = false;
            }
        }
    }
    
    private static void CreateNewTeaScript()
    {
        if (string.IsNullOrEmpty(_contextMenuPath)) return;
        
        string scriptName = "NewScript";
        int counter = 1;
        string scriptPath = Path.Combine(_contextMenuPath, $"{scriptName}.tea");
        
        // Find unique name
        while (File.Exists(scriptPath))
        {
            scriptPath = Path.Combine(_contextMenuPath, $"{scriptName}{counter}.tea");
            counter++;
        }
        
        // Create default script
        string defaultTemplate =
            $"// {Path.GetFileNameWithoutExtension(scriptPath)}.tea\n\n" +
            "func start() {\n" +
            "    // Called once when the script starts\n" +
            "}\n\n" +
            "func update() {\n" +
            "    // Called every frame\n" +
            "}\n";
        File.WriteAllText(scriptPath, defaultTemplate);
        
        Log($"✓ Created TeaScript: {Path.GetFileName(scriptPath)}");
        
        // Open in editor
        OpenScriptEditor(scriptPath);
    }
    
    private static void CreateNewFolder()
    {
        if (string.IsNullOrEmpty(_contextMenuPath)) return;
        
        string folderName = "NewFolder";
        int counter = 1;
        string folderPath = Path.Combine(_contextMenuPath, folderName);
        
        // Find unique name
        while (Directory.Exists(folderPath))
        {
            folderPath = Path.Combine(_contextMenuPath, $"{folderName}{counter}");
            counter++;
        }
        
        Directory.CreateDirectory(folderPath);
        Log($"✓ Created folder: {Path.GetFileName(folderPath)}");
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    //  SCRIPT EDITOR
    // ═══════════════════════════════════════════════════════════════════════
    
    private static void OpenScriptEditor(string scriptPath)
    {
        if (!File.Exists(scriptPath)) return;

        _editingScriptPath = scriptPath;
        _editingScriptName = Path.GetFileName(scriptPath);
        _editingScriptContent = File.ReadAllText(scriptPath);
        _showScriptEditor = true;

        Log($"Opened script: {_editingScriptName}");
    }

    public static BlueSky.Editor.ViewportRenderer? GetMainViewport()
    {
        return _editorViewportRenderer;
    }
    
    private static void OpenStaticMeshEditor(string assetPath)
    {
        if (_staticMeshEditor == null)
        {
            Log("✗ Static Mesh Editor not initialized");
            return;
        }
        
        if (_world == null)
        {
            Log("✗ World not initialized");
            return;
        }
        
        if (_viewport == null)
        {
            Log("✗ Viewport not initialized");
            return;
        }

        if (_rhi is null)
        {
            Log("✗ RHI not initialized");
            return;
        }
        
        _staticMeshEditor.Open(assetPath, _world, _viewport, _rhi);
        Log($"Opened static mesh editor: {Path.GetFileName(assetPath)}");
    }

    private static void OpenSkeletalMeshEditor(string assetPath)
    {
        if (_skeletalMeshEditor == null)
        {
            Log("✗ Skeletal Mesh Editor not initialized");
            return;
        }
        if (_world == null)
        {
            Log("✗ World not initialized");
            return;
        }
        if (_viewport == null)
        {
            Log("✗ Viewport not initialized");
            return;
        }
        if (_rhi is null)
        {
            Log("✗ RHI not initialized");
            return;
        }

        _skeletalMeshEditor.Open(assetPath, _world, _viewport, _rhi);
        Log($"Opened skeletal mesh editor: {Path.GetFileName(assetPath)}");
    }

    private static void DrawScriptEditor(EditorUI ui, float screenW, float screenH)
    {
        float editorW = 800;
        float editorH = 600;
        float editorX = (screenW - editorW) / 2;
        float editorY = (screenH - editorH) / 2;
        
        // Modal overlay
        ui.Panel(0, 0, screenW, screenH, new System.Numerics.Vector4(0, 0, 0, 0.5f));
        
        // Editor window
        ui.Shadow(editorX, editorY, editorW, editorH, 6, 10, 0.5f);
        ui.Panel(editorX, editorY, editorW, editorH, EditorTheme.Bg1);
        ui.Panel(editorX, editorY, editorW, 1, EditorTheme.Border0);
        
        // Title bar
        float titleH = 36;
        ui.Panel(editorX, editorY, editorW, titleH, EditorTheme.Bg2);
        ui.Panel(editorX, editorY + titleH - 1, editorW, 1, EditorTheme.Border1);
        ui.SetCursor(editorX + 12, editorY + 10);
        ui.Text($"📜 {_editingScriptName}", EditorTheme.TextPrimary);
        
        // Close button
        uint closeId = 9200;
        if (ui.ButtonEx(editorX + editorW - 70, editorY + 6, 60, 24, "Close",
            EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextSecondary, closeId))
        {
            _showScriptEditor = false;
        }
        
        // Toolbar
        float toolbarY = editorY + titleH;
        float toolbarH = 32;
        ui.Panel(editorX, toolbarY, editorW, toolbarH, EditorTheme.Bg3);
        ui.Panel(editorX, toolbarY + toolbarH - 1, editorW, 1, EditorTheme.Border1);
        
        // Save button
        uint saveId = 9201;
        if (ui.ButtonEx(editorX + 8, toolbarY + 4, 60, 24, "💾 Save",
            EditorTheme.Accent,
            EditorTheme.AccentHover,
            EditorTheme.AccentDim,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextPrimary, saveId))
        {
            SaveScript();
        }
        
        // Hot Reload button
        uint reloadId = 9202;
        if (ui.ButtonEx(editorX + 76, toolbarY + 4, 90, 24, "🔥 Hot Reload",
            EditorTheme.Orange,
            EditorTheme.Lighten(EditorTheme.Orange, 0.15f),
            EditorTheme.Orange,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextPrimary, reloadId))
        {
            SaveScript();
            HotReloadScripts();
        }
        
        // Clear button
        uint clearId = 9203;
        if (ui.ButtonEx(editorX + 174, toolbarY + 4, 60, 24, "Clear",
            EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextSecondary, clearId))
        {
            _editingScriptContent = "";
            Log("Cleared script content");
        }
        
        // Text editor area
        float textY = toolbarY + toolbarH + 8;
        float textH = editorH - titleH - toolbarH - 60;
        float textW = editorW - 16;
        
        ui.Panel(editorX + 8, textY, textW, textH, EditorTheme.Bg0);
        ui.Panel(editorX + 8, textY, textW, 1, EditorTheme.Border1);
        
        // Debug: Show what input we're receiving
        if (!string.IsNullOrEmpty(_frameTypedText))
        {
            Console.WriteLine($"[ScriptEditor] Received input: '{_frameTypedText}' (length: {_frameTypedText.Length})");
        }
        
        // Always capture input when script editor is open
        if (!string.IsNullOrEmpty(_frameTypedText))
        {
            // Filter out control characters except newline
            foreach (char c in _frameTypedText)
            {
                if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
                {
                    if (c == '\r') continue; // Skip carriage return
                    _editingScriptContent += c;
                    Console.WriteLine($"[ScriptEditor] Added char: '{c}' (code: {(int)c})");
                }
            }
        }
        
        if (_frameBackspacePressed && _editingScriptContent.Length > 0)
        {
            _editingScriptContent = _editingScriptContent.Substring(0, _editingScriptContent.Length - 1);
            Console.WriteLine($"[ScriptEditor] Backspace - new length: {_editingScriptContent.Length}");
        }
        
        // Handle Enter key for new lines (in case CharInput doesn't send it)
        if (_input!.IsKeyDown(KeyCode.Enter))
        {
            // Check if we haven't already added a newline from CharInput
            if (!_editingScriptContent.EndsWith("\n"))
            {
                _editingScriptContent += "\n";
                Console.WriteLine("[ScriptEditor] Added newline from Enter key");
            }
        }
        
        // Display text with line numbers
        ui.SetCursor(editorX + 16, textY + 8);
        
        string[] lines = _editingScriptContent.Split('\n');
        float lineY = textY + 8;
        int lineNum = 1;
        
        // Debug: Show content length
        if (lineNum == 1)
        {
            Console.WriteLine($"[ScriptEditor] Displaying content length: {_editingScriptContent.Length}, lines: {lines.Length}");
        }
        
        foreach (var line in lines)
        {
            if (lineY > textY + textH - 20) break;
            
            // Line number
            ui.SetCursor(editorX + 16, lineY);
            ui.Text($"{lineNum,3}", EditorTheme.TextDisabled);
            
            // Code
            ui.SetCursor(editorX + 50, lineY);
            ui.Text(line.TrimEnd('\r'), EditorTheme.TextPrimary);
            
            lineY += 18;
            lineNum++;
        }
        
        // Cursor blink indicator on last line
        if (ui.Time % 1.0 < 0.5)
        {
            string lastLine = lines.Length > 0 ? lines[^1] : "";
            float cursorX = editorX + 50 + lastLine.Length * 7.2f;
            float cursorY = textY + 8 + (lines.Length - 1) * 18;
            if (cursorY >= textY + 8 && cursorY < textY + textH - 20)
            {
                ui.Panel(cursorX, cursorY, 2, 16, EditorTheme.Accent);
            }
        }
        
        // Status bar
        float statusY = editorY + editorH - 28;
        ui.Panel(editorX, statusY, editorW, 28, EditorTheme.Bg2);
        ui.Panel(editorX, statusY, editorW, 1, EditorTheme.Border1);
        ui.SetCursor(editorX + 12, statusY + 8);
        ui.Text($"Lines: {lines.Length}  |  Chars: {_editingScriptContent.Length}  |  {Path.GetFileName(_editingScriptPath)}", EditorTheme.TextMuted);
        
        // Hint
        ui.SetCursor(editorX + editorW - 380, statusY + 8);
        ui.Text("Type to edit • Enter for newline • Backspace to delete • Save to persist", EditorTheme.TextDisabled);
    }
    
    private static void SaveScript()
    {
        try
        {
            File.WriteAllText(_editingScriptPath, _editingScriptContent);
            Log($"✓ Saved: {_editingScriptName}");
        }
        catch (Exception ex)
        {
            Log($"✗ Failed to save script: {ex.Message}");
        }
    }



    private static void HotReloadScripts()
    {
        if (_world == null || _teaScriptSystem == null) return;
        
        try
        {
            // Reload all scripts with matching path
            var query = _world.CreateQuery()
                .All<TeaScriptComponent>()
                .All<TransformComponent>()
                .Build();
            
            var chunks = _world.GetQueryChunks(query);
            int reloadCount = 0;
            
            foreach (var chunk in chunks)
            {
                int scriptIndex = chunk.GetComponentIndex(typeof(TeaScriptComponent));
                int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
                var entities = chunk.GetEntities();
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = entities[i];
                    ref var script = ref chunk.GetComponent<TeaScriptComponent>(i, scriptIndex);
                    ref var transform = ref chunk.GetComponent<TransformComponent>(i, transformIndex);
                    
                    // Reset and reinitialize
                    script.IsInitialized = false;
                    script.RuntimeInstance = 0;
                    reloadCount++;
                }
            }
            
            Log($"🔥 Hot reloaded {reloadCount} script(s)");
        }
        catch (Exception ex)
        {
            Log($"✗ Hot reload failed: {ex.Message}");
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    //  RENAME DIALOG
    // ═══════════════════════════════════════════════════════════════════════
    
    private static void DrawRenameDialog(EditorUI ui, float screenW, float screenH)
    {
        float dialogW = 400;
        float dialogH = 150;
        float dialogX = (screenW - dialogW) / 2;
        float dialogY = (screenH - dialogH) / 2;
        
        // Modal overlay
        ui.Panel(0, 0, screenW, screenH, new System.Numerics.Vector4(0, 0, 0, 0.5f));
        
        // Dialog window
        ui.Shadow(dialogX, dialogY, dialogW, dialogH, 6, 10, 0.5f);
        ui.Panel(dialogX, dialogY, dialogW, dialogH, EditorTheme.Bg1);
        ui.Panel(dialogX, dialogY, dialogW, 1, EditorTheme.Border0);
        
        // Title
        ui.SetCursor(dialogX + 12, dialogY + 12);
        ui.Text("Rename", EditorTheme.TextPrimary);
        
        // Input area
        float inputY = dialogY + 50;
        ui.Panel(dialogX + 12, inputY, dialogW - 24, 32, EditorTheme.Bg0);
        ui.Panel(dialogX + 12, inputY, dialogW - 24, 1, EditorTheme.Border1);
        
        // Handle text input
        if (!string.IsNullOrEmpty(_frameTypedText))
        {
            _renameNewName += _frameTypedText;
        }
        
        if (_frameBackspacePressed && _renameNewName.Length > 0)
        {
            _renameNewName = _renameNewName.Substring(0, _renameNewName.Length - 1);
        }
        
        // Display current name
        ui.SetCursor(dialogX + 20, inputY + 10);
        ui.Text(_renameNewName, EditorTheme.TextPrimary);
        
        // Buttons
        float btnY = dialogY + dialogH - 44;
        
        // Cancel
        uint cancelId = 9300;
        if (ui.ButtonEx(dialogX + dialogW - 160, btnY, 70, 28, "Cancel",
            EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextSecondary, cancelId))
        {
            _showRenameDialog = false;
        }
        
        // Rename
        uint renameId = 9301;
        if (ui.ButtonEx(dialogX + dialogW - 82, btnY, 70, 28, "Rename",
            EditorTheme.Accent,
            EditorTheme.AccentHover,
            EditorTheme.AccentDim,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextPrimary, renameId))
        {
            PerformRename();
            _showRenameDialog = false;
        }
    }
    
    private static void PerformRename()
    {
        if (string.IsNullOrWhiteSpace(_renameNewName) || string.IsNullOrEmpty(_renameTarget))
            return;
        
        try
        {
            string dir = Path.GetDirectoryName(_renameTarget) ?? "";
            string ext = Path.GetExtension(_renameTarget);
            string newPath = Path.Combine(dir, _renameNewName + ext);
            
            if (File.Exists(_renameTarget))
            {
                File.Move(_renameTarget, newPath);
                Log($"✓ Renamed file: {Path.GetFileName(_renameTarget)} → {Path.GetFileName(newPath)}");
            }
            else if (Directory.Exists(_renameTarget))
            {
                Directory.Move(_renameTarget, newPath);
                Log($"✓ Renamed folder: {Path.GetFileName(_renameTarget)} → {Path.GetFileName(newPath)}");
            }
            
            _selectedAssetIndex = -1;
        }
        catch (Exception ex)
        {
            Log($"✗ Rename failed: {ex.Message}");
        }
    }
    
    private static void DeleteSelectedAsset()
    {
        if (_selectedAssetIndex < 0 || string.IsNullOrEmpty(_currentBrowserDir))
            return;
        
        try
        {
            var listing = Assets.GetContentBrowserListing(_currentBrowserDir);
            var dirs = listing?.Directories ?? Array.Empty<string>();
            var files = listing?.Files ?? Array.Empty<string>();
            
            int folderCount = dirs.Length;
            
            // Check if it's a folder
            if (_selectedAssetIndex >= 5000 && _selectedAssetIndex < 5000 + folderCount)
            {
                int folderIdx = _selectedAssetIndex - 5000;
                string folderPath = dirs[folderIdx];
                string folderName = Path.GetFileName(folderPath);
                
                // Delete folder and all contents
                Directory.Delete(folderPath, recursive: true);
                Log($"✓ Deleted folder: {folderName}");
            }
            // Check if it's a file
            else if (_selectedAssetIndex >= 6000)
            {
                int fileIdx = _selectedAssetIndex - 6000;
                if (fileIdx < files.Length)
                {
                    string filePath = files[fileIdx];
                    string fileName = Path.GetFileName(filePath);
                    
                    // Delete file
                    File.Delete(filePath);
                    Log($"✓ Deleted file: {fileName}");
                }
            }
            
            _selectedAssetIndex = -1;
        }
        catch (Exception ex)
        {
            Log($"✗ Delete failed: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    private static void SaveScene() => TrySaveScene();

    /// <summary>
    /// Saves the current scene to <see cref="_currentScenePath"/>, asking for a path first if the
    /// scene has none. Returns true only if the file was actually written.
    /// </summary>
    private static bool TrySaveScene()
    {
        if (_world == null) return false;

        // During Play mode the world is being simulated. Stop restores the authored scene,
        // so saving now would overwrite it with whatever state the simulation reached.
        if (_isPlaying)
        {
            _notificationSystem?.ShowWarning("Stop Play mode before saving the scene");
            return false;
        }

        if (string.IsNullOrEmpty(_currentScenePath))
            return TrySaveSceneAs();

        try
        {
            // Auto-save terrain assets before scene serialization
            AutoSaveTerrainAssets();

            _terrainSystem?.SaveAllTerrainAssets();
            var sceneData = BlueSky.Meridian.SceneConverter.WorldToSceneData(_world, Path.GetFileNameWithoutExtension(_currentScenePath));

            if (!BlueSky.Meridian.SceneSerializer.SaveScene(sceneData, _currentScenePath))
            {
                Console.WriteLine($"[Editor] ❌ Failed to write scene file: {_currentScenePath}");
                _notificationSystem?.ShowError($"Save failed: could not write {Path.GetFileName(_currentScenePath)}");
                return false;
            }

            _sceneDirty = false;

            var fileName = Path.GetFileName(_currentScenePath);
            Console.WriteLine($"[Editor] ✓ Scene saved: {_currentScenePath}");
            _notificationSystem?.ShowSuccess($"Saved: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] ❌ Failed to save scene: {ex.Message}");
            _notificationSystem?.ShowError($"Save failed: {ex.Message}");
            return false;
        }
    }

    private static void AutoSaveTerrainAssets()
    {
        if (_world == null) return;

        // Query all terrain entities
        var terrainQuery = _world.CreateQuery()
            .All<TerrainComponent>()
            .Build();

        var chunks = _world.GetQueryChunks(terrainQuery);

        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int terrainIndex = chunk.GetComponentIndex(typeof(TerrainComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                ref var terrain = ref chunk.GetComponent<TerrainComponent>(i, terrainIndex);
                
                // If TerrainAssetPath is empty, auto-generate one
                if (string.IsNullOrEmpty(terrain.TerrainAssetPath))
                {
                    var projectPath = ProjectManager.CurrentProjectDir;
                    if (string.IsNullOrEmpty(projectPath))
                        continue;

                    var terrainsDir = Path.Combine(projectPath, "Terrains");
                    Directory.CreateDirectory(terrainsDir);

                    var terrainName = $"Terrain_{entities[i].Id}_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var terrainPath = Path.Combine(terrainsDir, $"{terrainName}.bsterrain");
                    
                    terrain.TerrainAssetPath = terrainPath;
                    
                    Console.WriteLine($"[Editor] Auto-generated terrain path: {terrainPath}");
                }
            }
        }
    }

    private static void SaveSceneAs() => TrySaveSceneAs();

    private static bool TrySaveSceneAs()
    {
        if (_world == null) return false;

        var projectPath = ProjectManager.CurrentProjectDir;
        if (string.IsNullOrEmpty(projectPath))
        {
            _notificationSystem?.ShowError("No project loaded");
            return false;
        }

        var scenesDir = Path.Combine(projectPath, "Scenes");
        Directory.CreateDirectory(scenesDir);

        var selectedPath = NativeFilePicker.SaveFile(
            "Save BlueSky Scene", scenesDir, "blueskyscene", "BlueSky Scene");
        if (string.IsNullOrWhiteSpace(selectedPath))
            return false; // cancelled

        // Only adopt the new path if the write succeeds, so a failed Save As never
        // leaves the editor pointing at a file that does not exist.
        var previousPath = _currentScenePath;
        _currentScenePath = Path.GetFullPath(selectedPath);
        if (TrySaveScene())
            return true;

        _currentScenePath = previousPath;
        return false;
    }

    /// <summary>
    /// If the scene has unsaved changes, saves it before the world is replaced. Returns false (and says why)
    /// if the save did not happen, so the caller can stop instead of silently discarding the changes.
    /// </summary>
    private static bool EnsureSceneSavedBeforeDiscard(string action)
    {
        if (!_sceneDirty) return true;
        if (TrySaveScene()) return true;

        _notificationSystem?.ShowWarning($"{action} cancelled - the current scene has unsaved changes");
        return false;
    }

    private static bool IsSceneFile(string path) =>
        path.EndsWith(".blueskyscene", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".bsscene", StringComparison.OrdinalIgnoreCase);

    private static void LoadScene()
    {
        if (_world == null) return;

        if (_isPlaying)
        {
            _notificationSystem?.ShowWarning("Stop Play mode before opening a scene");
            return;
        }

        if (!EnsureSceneSavedBeforeDiscard("Open Scene")) return;

        var projectPath = ProjectManager.CurrentProjectDir;
        if (!string.IsNullOrEmpty(projectPath))
            Directory.CreateDirectory(Path.Combine(projectPath, "Scenes")); // so Save As and Open have a home

        // Platform-native open dialog (NSOpenPanel / Win32 / zenity).
        var selectedFiles = ShowOpenFileDialog();
        if (selectedFiles == null || selectedFiles.Length == 0)
            return; // cancelled

        var selectedPath = selectedFiles[0];
        if (!IsSceneFile(selectedPath))
        {
            _notificationSystem?.ShowError("Not a scene file - choose a .blueskyscene or .bsscene file");
            return;
        }

        LoadSceneFromPath(selectedPath);
    }

    private static void LoadSceneFromPath(string path)
    {
        if (_world == null) return;

        if (_isPlaying)
        {
            _notificationSystem?.ShowWarning("Stop Play mode before opening a scene");
            return;
        }

        if (!EnsureSceneSavedBeforeDiscard("Open Scene")) return;

        try
        {
            var sceneData = BlueSky.Meridian.SceneSerializer.LoadScene(path);
            if (sceneData == null)
            {
                _notificationSystem?.ShowError("Failed to load scene");
                return;
            }

            BlueSky.Meridian.SceneConverter.SceneDataToWorld(sceneData, _world, clearWorld: true);
            _terrainSystem?.Clear();
            _terrainSystem?.LoadTerrainAssetsForWorld();
            
            // Reinitialize viewport camera after clearing world
            _viewport?.ReinitializeCamera();
            
            _currentScenePath = path;
            _sceneDirty = false;
            _selectedEntityId = 0; // Clear selection
            Console.WriteLine($"[Editor] Scene loaded: {path}");
            _notificationSystem?.ShowSuccess($"Loaded: {sceneData.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Failed to load scene: {ex.Message}");
            _notificationSystem?.ShowError($"Failed to load: {ex.Message}");
        }
    }

    private static void NewScene()
    {
        if (_world == null) return;

        if (!EnsureSceneSavedBeforeDiscard("New Scene")) return;

        // Clear the world
        var allQuery = _world.CreateQuery().Build();
        var allChunks = _world.GetQueryChunks(allQuery);
        var entitiesToDestroy = new List<Entity>();

        foreach (var chunk in allChunks)
        {
            var entities = chunk.GetEntities();
            for (int i = 0; i < entities.Length; i++)
            {
                entitiesToDestroy.Add(entities[i]);
            }
        }

        foreach (var entity in entitiesToDestroy)
        {
            _world.DestroyEntity(entity);
        }

        // Close editors to clear preview entities
        if (_staticMeshEditor != null && _staticMeshEditor.IsOpen)
        {
            _staticMeshEditor.Close(_world);
        }

        // Reinitialize viewport camera after clearing world
        _viewport?.ReinitializeCamera();

        _currentScenePath = null;
        _sceneDirty = false;
        _selectedEntityId = 0;
        Console.WriteLine("[Editor] New scene created");
        _notificationSystem?.ShowInfo("New scene created");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PHYSICS SYNC
    // ═══════════════════════════════════════════════════════════════════════

}
