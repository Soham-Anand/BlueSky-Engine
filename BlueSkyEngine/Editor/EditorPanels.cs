using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using BlueSky.Editor.UI;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;
using BlueSky.Rendering;
using BlueSky.Core.Scripting;
using BlueSky.Core.Scene;
using BlueSky.Rendering.RHI;

namespace BlueSky.Editor;

partial class Program
{
    // ── Details/Outliner per-frame caches (freeze fixes) ──────────────
    // Resolving selection via GetAllEntities().FirstOrDefault 6x/frame allocates
    // a full snapshot per call. Resolve once per DrawDetailsPanel instead.
    // Mesh headers hit disk+JSON; cache by asset id, reload only on change.
    private static uint _detailsCachedId = uint.MaxValue;
    private static Entity _detailsCachedEntity = default;
    private static bool _detailsCachedFound = false;
    private static string _detailsCachedMeshId = "";
    private static int _detailsCachedSubmeshCount = 0;
    // Debug view selector (toolbar): 0 Lit, 1 Normals, 2 Unlit, 3 Shadow, 4 AO.
    // Consumed as flags bits 14-16 by fs_mesh (mesh-vs-shader diagnosis).
    internal static int _debugView = 0;
    internal static readonly string[] DebugViewNames = { "Lit", "Nrm", "Unl", "Shd", "AO", "Dir", "Env", "Sun" };
    // ── Dockable Panel Content Callbacks ──────────────────────────────

    private static void DrawViewportPanel(EditorUI ui, DockRect rect)
    {
        _lastViewportRect = rect;

        // Skip 3D rendering when modals are open
        if (_showScriptEditor || _showImportDialog || _showRenameDialog)
        {
            ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg0);
            float cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2;
            ui.SetCursor(cx - 60, cy - 8);
            ui.Text("Viewport paused (modal open)", EditorTheme.TextDisabled);
            return;
        }

        // Only draw a background when there's no 3D renderer attached.
        // When the viewport IS active, the 3D content is composited AFTER
        // the UI pass via RenderTexture, so it naturally covers this area.
        // Drawing a solid panel here would BLOCK the composited 3D content.
        if (_viewport == null)
        {
            ui.Panel(rect.X, rect.Y, rect.W, rect.H, new System.Numerics.Vector4(0.05f, 0.05f, 0.07f, 1.0f));
        }

        // --- Drag Preview Visuals ---
        if (_isDraggingAsset && _draggedAssetPath != null)
        {
            float mouseX = _input!.MousePosition.X;
            float mouseY = _input!.MousePosition.Y;
            
            bool insideViewport = (mouseX >= rect.X && mouseX <= rect.X + rect.W &&
                                   mouseY >= rect.Y && mouseY <= rect.Y + rect.H);
            
            // Draw floating proxy attached to cursor
            ui.Panel(mouseX + 16, mouseY + 16, 120, 32, new System.Numerics.Vector4(0.2f, 0.4f, 0.8f, 0.7f));
            ui.SetCursor(mouseX + 24, mouseY + 24);
            ui.Text(System.IO.Path.GetFileName(_draggedAssetPath), new System.Numerics.Vector4(1, 1, 1, 1));
            
            if (insideViewport)
            {
                // Draw drop indicator highlighting edge limits of Viewport
                ui.Panel(rect.X + 2, rect.Y + 2, rect.W - 4, rect.H - 4, new System.Numerics.Vector4(0.2f, 0.8f, 0.4f, 0.2f));
                ui.Panel(mouseX, mouseY - 10, 2, 20, new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f));
                ui.Panel(mouseX - 10, mouseY, 20, 2, new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f));
            }
        }

        if (_viewport == null)
        {
            float cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2;
            ui.SetCursor(cx - 80, cy - 8);
            ui.Text("3D Viewport", EditorTheme.TextDisabled);
            ui.SetCursor(cx - 60, cy + 12);
            ui.Text("No renderer attached", EditorTheme.TextDisabled);
        }

        // ── Viewport toolbar (semi-transparent overlay) ───────────────
        // Only render when the 3D renderer is actually attached,
        // otherwise these buttons and panels show as rectangular artifacts
        if (_viewport != null)
        {
            float tbH = 34;
            ui.RoundedPanel(rect.X + 8, rect.Y + 8, rect.W - 16, tbH, EditorTheme.BgGlass, EditorTheme.CardRadius);
            EditorChrome.Stroke(ui, rect.X + 8, rect.Y + 8, rect.W - 16, tbH, EditorTheme.WithAlpha(EditorTheme.Border2, 0.45f));
            ui.Panel(rect.X + 18, rect.Y + 9, 46, 2, EditorTheme.Accent);
            ui.SetCursor(rect.X + 20, rect.Y + 18);
            ui.Text("Scene", EditorTheme.TextMuted);

            // Transform tools
            string[] tools = { "W Move", "E Rotate", "R Scale" };
            float tx = rect.X + 74;
            for (int i = 0; i < tools.Length; i++)
            {
                uint toolId = 700u + (uint)i;
                float tw = tools[i].Length * 7.2f + 16;
                ui.ButtonEx(tx, rect.Y + 14, tw, 22, tools[i],
                    EditorTheme.WithAlpha(EditorTheme.ToolbarBtnNormal, 0.6f),
                    EditorTheme.WithAlpha(EditorTheme.ToolbarBtnHover, 0.8f),
                    EditorTheme.Accent,
                    new System.Numerics.Vector4(0, 0, 0, 0), // No shadow!
                    EditorTheme.TextSecondary, toolId);
                tx += tw + 4;
            }

            // Separator
            tx += 4;
            ui.Panel(tx, rect.Y + 16, 1, 18, EditorTheme.WithAlpha(EditorTheme.Border1, 0.8f));
            tx += 8;

            // View modes
            string[] modes = { "Lit", "Wireframe", "Unlit" };
            for (int i = 0; i < modes.Length; i++)
            {
                uint modeId = 710u + (uint)i;
                float mw = modes[i].Length * 7.2f + 12;
                ui.ButtonEx(tx, rect.Y + 14, mw, 22, modes[i],
                    EditorTheme.WithAlpha(EditorTheme.ToolbarBtnNormal, 0.6f),
                    EditorTheme.WithAlpha(EditorTheme.ToolbarBtnHover, 0.8f),
                    EditorTheme.Accent,
                    new System.Numerics.Vector4(0, 0, 0, 0), // No shadow!
                    i == 0 ? EditorTheme.TextPrimary : EditorTheme.TextMuted, modeId);
                tx += mw + 4;
            }

            // Debug view cycle (Lit -> Nrm -> Unl -> Shd): pinpoints whether a
            // viewport artifact lives in mesh data, albedo, lighting or shadow.
            {
                string modeName = DebugViewNames[_debugView];
                float mw = modeName.Length * 7.2f + 22;
                if (ui.ButtonEx(tx, rect.Y + 14, mw, 22, "Dbg:" + modeName,
                    _debugView != 0 ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.45f)
                       : EditorTheme.WithAlpha(EditorTheme.ToolbarBtnNormal, 0.6f),
                    EditorTheme.WithAlpha(EditorTheme.ToolbarBtnHover, 0.8f),
                    EditorTheme.Accent,
                    new System.Numerics.Vector4(0, 0, 0, 0),
                    _debugView != 0 ? EditorTheme.TextPrimary : EditorTheme.TextMuted, 713u))
                    _debugView = (_debugView + 1) % 8;
                tx += mw + 4;
            }

            // ── Camera info overlay (bottom-left) ────────────────────────
            var camPos = _viewport.GetCameraPositionNumerics();
            float infoH = 26;
            ui.RoundedPanel(rect.X + 10, rect.Y + rect.H - infoH - 10, 250, infoH,
                EditorTheme.BgGlass, EditorTheme.PillRadius);
            EditorChrome.Stroke(ui, rect.X + 10, rect.Y + rect.H - infoH - 10, 250, infoH, EditorTheme.WithAlpha(EditorTheme.Border2, 0.38f));
            ui.Circle(rect.X + 24, rect.Y + rect.H - 23, 4, EditorTheme.AccentCyan, true);
            ui.SetCursor(rect.X + 36, rect.Y + rect.H - infoH - 3);
            ui.Text($"Cam ({camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1})",
                EditorTheme.WithAlpha(EditorTheme.TextMuted, 0.8f));
        }
    }

    private static void DrawOutlinerPanel(EditorUI ui, DockRect rect)
    {
        float inset = EditorTheme.Pad;
        float iconCol = rect.X + inset + 6;
        float nameCol = rect.X + inset + 24;

        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);
        ui.GradientPanel(rect.X, rect.Y, rect.W, EditorTheme.HeaderH, EditorTheme.Bg2, EditorTheme.Bg1);
        ui.Panel(rect.X, rect.Y + EditorTheme.HeaderH - 1, rect.W, 1, EditorTheme.Border0);
        ui.SetCursor(rect.X + inset + 4, rect.Y + (EditorTheme.HeaderH / 2) - 6);
        ui.Text("World Outliner", EditorTheme.TextPrimary);

        float searchY = rect.Y + EditorTheme.HeaderH + EditorTheme.Pad;
        ui.RoundedPanel(rect.X + inset, searchY, rect.W - inset * 2, 26, EditorTheme.Bg0, EditorTheme.InputRadius);
        EditorChrome.Stroke(ui, rect.X + inset, searchY, rect.W - inset * 2, 26, EditorTheme.Border1);
        ui.SetCursor(rect.X + inset + 12, searchY + 6);
        ui.Text("Search actors...", EditorTheme.TextDisabled);

        float listY = searchY + 26 + EditorTheme.PadLg;
        float scrollAreaY = listY;
        float scrollAreaH = rect.H - (scrollAreaY - rect.Y) - 60; // Reserve space for footer

        if (_world != null)
        {
            var entities = _world.GetAllEntities().ToList();
            
            // Calculate content height
            string[] systemItems = { "DirectionalLight", "SkyAtmosphere", "PostProcessVolume" };
            float contentHeight = EditorTheme.RowH + 3 + 12 + 1; // Header + separator
            contentHeight += systemItems.Length * (EditorTheme.RowH + 1); // System items
            contentHeight += 16 + 1; // Separator
            contentHeight += entities.Count * (EditorTheme.RowH + 1); // Entities
            contentHeight += 30; // Bottom padding
            
            // Begin scroll area
            float scrollOffset = ui.BeginScrollArea("WorldOutliner", rect.X, scrollAreaY, rect.W, scrollAreaH, contentHeight);
            
            listY = scrollAreaY;
            
            ui.SetCursor(iconCol, listY - scrollOffset);
            ui.Text("\u25BC  Persistent Level", EditorTheme.TextMuted);
            listY += EditorTheme.RowH + 3;

            ui.Panel(rect.X + inset, listY - scrollOffset, rect.W - inset * 2, 1, EditorTheme.Border1);
            listY += 12;

            string[] systemIcons = { "\u2600", "\u2601", "\u25C9" };
            for (int i = 0; i < systemItems.Length; i++)
            {
                uint id = 200u + (uint)i;
                bool isSel = _selectedEntityId == id;
                float drawY = listY - scrollOffset;

                if (ui.ClickableCard(rect.X + 6, drawY, rect.W - 12, EditorTheme.RowH,
                    id,
                    isSel ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.1f) : EditorTheme.Bg1,
                    EditorTheme.HoverBg,
                    EditorTheme.SelectionBg))
                {
                    _selectedEntityId = id;
                    Log($"Selected: {systemItems[i]}");
                }

                if (isSel)
                {
                    ui.Panel(rect.X + 6, drawY, rect.W - 12, EditorTheme.RowH,
                        EditorTheme.WithAlpha(EditorTheme.Accent, 0.16f));
                    ui.Panel(rect.X + 6, drawY, 3, EditorTheme.RowH, EditorTheme.Accent);
                }

                System.Numerics.Vector4 dotColor = i == 0 ? EditorTheme.Yellow : i == 1 ? EditorTheme.AccentCyan : EditorTheme.Purple;
                ui.Circle(iconCol + 4, drawY + EditorTheme.RowH / 2, 4, dotColor, filled: true);
                
                ui.SetCursor(nameCol, drawY + 6);
                ui.Text(systemItems[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
                listY += EditorTheme.RowH + 1;
            }

            ui.Panel(rect.X + inset, listY + 6 - scrollOffset, rect.W - inset * 2, 1, EditorTheme.Border1);
            listY += 16;

            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                float drawY = listY - scrollOffset;
                // Cull off-screen rows: advance layout, skip all component
                // lookups and draw commands. This keeps 100k-entity scenes
                // at ~30 visible rows instead of 100k draw cards per frame.
                bool visible = drawY + EditorTheme.RowH >= scrollAreaY && drawY <= scrollAreaY + scrollAreaH;
                if (!visible)
                {
                    listY += EditorTheme.RowH + 1;
                    continue;
                }
                // Skip internal editor camera entity (no layout space, as before).
                if (_world.TryGetComponent<CameraComponent>(entity, out _))
                    continue;
                uint id = (uint)entity.Id;
                bool isSel = _selectedEntityId == id;

                if (ui.ClickableCard(rect.X + 6, drawY, rect.W - 12, EditorTheme.RowH,
                    id,
                    isSel ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.1f) : EditorTheme.Bg1,
                    EditorTheme.HoverBg,
                    EditorTheme.SelectionBg))
                {
                    _selectedEntityId = id;
                    Log($"Selected Entity_{entity.Id}");
                }

                if (isSel)
                {
                    ui.Panel(rect.X + 6, drawY, rect.W - 12, EditorTheme.RowH,
                        EditorTheme.WithAlpha(EditorTheme.Accent, 0.16f));
                    ui.Panel(rect.X + 6, drawY, 3, EditorTheme.RowH, EditorTheme.Accent);
                }

                System.Numerics.Vector4 entColor = EditorTheme.Orange;
                if (_world.TryGetComponent<BlueSky.Core.ECS.Builtin.TransformComponent>(entity, out _)) entColor = EditorTheme.Orange;
                if (_world.TryGetComponent<TerrainComponent>(entity, out _)) entColor = EditorTheme.Green;
                if (_world.TryGetComponent<TeaScriptComponent>(entity, out _)) entColor = EditorTheme.Teal;

                ui.Circle(iconCol + 4, drawY + EditorTheme.RowH / 2, 4, entColor, filled: true);

                ui.SetCursor(nameCol, drawY + 6);
                ui.Text($"Entity_{entity.Id}", isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                listY += EditorTheme.RowH + 1;
            }
            
            ui.EndScrollArea("WorldOutliner");

            ui.Panel(rect.X + inset, rect.Y + rect.H - 28, rect.W - inset * 2, 1, EditorTheme.Border1);
            
            // Create button
            if (ui.ButtonEx(rect.X + inset, rect.Y + rect.H - 26, rect.W - inset * 2, 24, "+ Create Terrain",
                EditorTheme.Bg3, EditorTheme.Bg2, EditorTheme.Bg1, 
                new System.Numerics.Vector4(0,0,0,0), EditorTheme.Green, 9900))
            {
                CreateTerrain();
            }
            // Create Empty Entity button
            if (ui.ButtonEx(rect.X + inset, rect.Y + rect.H - 50, rect.W - inset * 2, 24, "+ Empty Entity",
                EditorTheme.Bg3, EditorTheme.Bg2, EditorTheme.Bg1,
                new System.Numerics.Vector4(0,0,0,0), EditorTheme.Green, 9901))
            {
                var entity = _world.CreateEntity();
                var transform = new TransformComponent
                {
                    Position = new BlueSky.Core.Math.Vector3(0, 1, 0),
                    Rotation = BlueSky.Core.Math.Quaternion.Identity,
                    Scale = BlueSky.Core.Math.Vector3.One
                };
                _world.AddComponent(entity, transform);
                Log($"Created empty entity Entity_{entity.Id}");
            }
            
            // ── RIGHT-CLICK CONTEXT MENU on outliner ───────────────────
            if (_input!.IsMouseButtonDown(MouseButton.Right) &&
                ui.IsHovering(rect.X, scrollAreaY, rect.W, scrollAreaH))
            {
                _showOutlinerContextMenu = true;
                _outlinerContextMenuX = ui.MousePosition.X;
                _outlinerContextMenuY = ui.MousePosition.Y;
            }

            if (_showOutlinerContextMenu)
            {
                DrawOutlinerContextMenu(ui, _outlinerContextMenuX, _outlinerContextMenuY);
            }

            ui.SetCursor(iconCol, rect.Y + rect.H - 78);
            ui.Text($"{entities.Count} actors", EditorTheme.TextDisabled);
        }
        else
        {
            ui.SetCursor(iconCol, rect.Y + 80);
            ui.Text("No level loaded", EditorTheme.TextMuted);
        }
    }

    private static void DrawDetailsPanel(EditorUI ui, DockRect rect)
    {
        float inset = EditorTheme.Pad;
        float labelCol = rect.X + inset + 4;
        float valueCol = rect.X + EditorTheme.PropLabelW + inset;

        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        // Get selected entity info — resolve ONCE per frame (no 6x snapshot scans).
        string entityName = "None Selected";
        string pos = "0.0, 0.0, 0.0";
        string rot = "0.0, 0.0, 0.0";
        string scale = "1.0, 1.0, 1.0";
        bool hasMesh = false;
        Entity selectedEntity = default;
        bool hasSelectedEntity = false;

        if (_world != null && _selectedEntityId > 0)
        {
            if (_selectedEntityId >= 200)
            {
                string[] sysNames = { "DirectionalLight", "SkyAtmosphere", "PostProcessVolume" };
                int idx = (int)_selectedEntityId - 200;
                if (idx < sysNames.Length && idx >= 0) entityName = sysNames[idx];
            }
            else
            {
                if (_detailsCachedId != _selectedEntityId)
                {
                    _detailsCachedId = _selectedEntityId;
                    _detailsCachedFound = _world.TryResolveEntity(_selectedEntityId, out _detailsCachedEntity);
                }
                if (_detailsCachedFound)
                {
                    selectedEntity = _detailsCachedEntity;
                    // Generation may have recycled; validate before use.
                    if (_world.IsEntityValid(selectedEntity))
                    {
                        hasSelectedEntity = true;
                        entityName = $"Entity_{selectedEntity.Id}";
                        if (_world.TryGetComponent<TransformComponent>(selectedEntity, out var transform))
                        {
                            pos = $"{transform.Position.X:F1}, {transform.Position.Y:F1}, {transform.Position.Z:F1}";
                            scale = $"{transform.Scale.X:F1}, {transform.Scale.Y:F1}, {transform.Scale.Z:F1}";
                        }
                        hasMesh = _world.TryGetComponent<MeshComponent>(selectedEntity, out _);
                    }
                    else
                    {
                        _detailsCachedFound = false;
                    }
                }
            }
        }

        EditorChrome.Header(ui, rect.X, rect.Y, rect.W, EditorTheme.HeaderH, "Inspector", _selectedEntityId > 0 ? $"ID {_selectedEntityId}" : "No selection", EditorTheme.Purple);
        
        // Setup scroll area
        float scrollAreaY = rect.Y + EditorTheme.HeaderH;
        float scrollAreaH = rect.H - EditorTheme.HeaderH - 28; // Reserve space for footer
        float contentHeight = 2000; // Estimated content height - will be enough for most cases
        float scrollOffset = ui.BeginScrollArea("DetailsPanel", rect.X, scrollAreaY, rect.W, scrollAreaH, contentHeight);
        
        float y = scrollAreaY + EditorTheme.Pad;

        // Entity name header
        float cardH = 40;
        ui.RoundedGradientPanel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, cardH, EditorTheme.BgElevated, EditorTheme.Bg2, EditorTheme.CardRadius);
        EditorChrome.Stroke(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, cardH, EditorTheme.Border1);
        ui.Circle(labelCol + 8, y - scrollOffset + (cardH / 2), 5, _selectedEntityId > 0 ? EditorTheme.Purple : EditorTheme.TextDisabled, true);
        ui.SetCursor(labelCol + 22, y - scrollOffset + 8);
        ui.Text(entityName, EditorTheme.TextPrimary);
        ui.SetCursor(labelCol + 22, y - scrollOffset + 22);
        ui.Text(hasMesh ? "Static Mesh Actor" : "Scene Object", EditorTheme.TextMuted);
        y += cardH + EditorTheme.PadLg;

        ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 1, EditorTheme.Border1);
        y += EditorTheme.PadLg;

        // Transform Section
        ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
        ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.Accent);
        ui.SetCursor(labelCol + 2, y - scrollOffset + 8);
        ui.Text("Transform", EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + EditorTheme.Pad;

        // Property rows with aligned columns
        string[] labels = { "Location", "Rotation", "Scale" };
        string[] values = { pos, rot, scale };
        for (int i = 0; i < labels.Length; i++)
        {
            ui.SetCursor(labelCol, y - scrollOffset + 2);
            ui.Text(labels[i], EditorTheme.TextMuted);
            
            // Rounded input box background - calculate width relative to panel
            float transformInputW = rect.W - (valueCol - rect.X) - inset;
            ui.RoundedPanel(valueCol - 6, y - scrollOffset - 2, transformInputW + 6, 22, EditorTheme.Bg0, EditorTheme.InputRadius);
            ui.Panel(valueCol - 6, y - scrollOffset - 2, transformInputW + 6, 1, EditorTheme.Border1); // inner shadow
            
            ui.SetCursor(valueCol, y - scrollOffset + 2);
            ui.Text(values[i], EditorTheme.TextPrimary);
            y += 26;
        }
        y += EditorTheme.PadLg;

        // Static Mesh Section
        ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
        ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.Purple);
        ui.SetCursor(labelCol + 2, y - scrollOffset + 8);
        ui.Text("Static Mesh", EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + EditorTheme.Pad;

        // Mesh row
        ui.SetCursor(labelCol, y - scrollOffset + 2);
        ui.Text("Mesh", EditorTheme.TextMuted);
        
        string meshName = "None";
        int submeshCount = 0;
        StaticMeshComponent cachedMeshComp = default;
        bool hasCachedMeshComp = false;
        if (hasSelectedEntity && _world != null)
        {
            if (_world.TryGetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(selectedEntity, out var meshComp))
            {
                hasMesh = true;
                hasCachedMeshComp = true;
                cachedMeshComp = meshComp;
                meshName = string.IsNullOrEmpty(meshComp.MeshAssetId) ? "None" : Path.GetFileNameWithoutExtension(meshComp.MeshAssetId);

                // Cached header: disk+JSON once per asset id, not 60x/sec.
                if (!string.IsNullOrEmpty(meshComp.MeshAssetId))
                {
                    if (_detailsCachedMeshId != meshComp.MeshAssetId)
                    {
                        _detailsCachedMeshId = meshComp.MeshAssetId;
                        _detailsCachedSubmeshCount = 0;
                        var meshHeader = BlueSky.Core.Assets.BlueAsset.LoadHeader(meshComp.MeshAssetId);
                        if (meshHeader != null && meshHeader.Metadata.TryGetValue("submeshCount", out var scStr)
                            && int.TryParse(scStr, out int sc))
                            _detailsCachedSubmeshCount = sc;
                    }
                    submeshCount = _detailsCachedSubmeshCount;
                }
                else
                {
                    submeshCount = 0;
                }
            }
        }

        float meshInputW = rect.W - (valueCol - rect.X) - inset;
        ui.RoundedPanel(valueCol - 6, (y - 2) - scrollOffset, meshInputW + 6, 22, EditorTheme.Bg0, EditorTheme.InputRadius);
        ui.Panel(valueCol - 6, (y - 2) - scrollOffset, meshInputW + 6, 1, EditorTheme.Border1); // inner shadow

        ui.SetCursor(valueCol, (y + 2) - scrollOffset);
        ui.Text(meshName, hasMesh ? EditorTheme.Purple : EditorTheme.TextDisabled);
        y += 28;

        // ── Static Mesh Actions ──
        if (hasMesh && hasCachedMeshComp && _world != null && hasSelectedEntity)
        {
            {
                // Open Mesh Editor button
                if (ui.ButtonEx(labelCol, y - scrollOffset, rect.W - inset * 2 - (labelCol - rect.X - inset), 28, "🔧 Edit Mesh",
                    EditorTheme.WithAlpha(EditorTheme.Purple, 0.2f), 
                    EditorTheme.WithAlpha(EditorTheme.Purple, 0.3f), 
                    EditorTheme.WithAlpha(EditorTheme.Purple, 0.4f), 
                    new System.Numerics.Vector4(0,0,0,0), 
                    EditorTheme.Purple, 9702))
                {
                    if (!string.IsNullOrEmpty(cachedMeshComp.MeshAssetId))
                    {
                        OpenStaticMeshEditor(cachedMeshComp.MeshAssetId);
                    }
                }
                y += 32;

                y += EditorTheme.PadLg;
            }
        }
        else if (!hasMesh)
        {
            // Show "No mesh" message
            ui.SetCursor(labelCol, (y + 2) - scrollOffset);
            ui.Text("No mesh assigned", EditorTheme.TextDisabled);
            y += 26;
        }

        // ── Skeletal Mesh Section ──
        bool hasSkelMesh = false;
        if (hasSelectedEntity && _world != null)
        {
            if (_world.TryGetComponent<BlueSky.Core.ECS.Builtin.SkeletalMeshComponent>(selectedEntity, out var skelComp))
            {
                hasSkelMesh = true;
                string skelMeshName = string.IsNullOrEmpty(skelComp.MeshAssetPath) ? "None" : Path.GetFileNameWithoutExtension(skelComp.MeshAssetPath);

                ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
                ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, new System.Numerics.Vector4(0.5f, 0.8f, 1.0f, 1.0f));
                ui.SetCursor(labelCol + 2, y - scrollOffset + 8);
                ui.Text("Skeletal Mesh", EditorTheme.TextPrimary);
                y += EditorTheme.SectionH + EditorTheme.Pad;

                ui.SetCursor(labelCol, y - scrollOffset + 2);
                ui.Text("Mesh", EditorTheme.TextMuted);
                ui.SetCursor(valueCol, y - scrollOffset + 2);
                ui.Text(skelMeshName, hasSkelMesh ? EditorTheme.Purple : EditorTheme.TextDisabled);
                y += 26;

                // Open Skeletal Mesh Editor button
                if (ui.ButtonEx(labelCol, y - scrollOffset, rect.W - inset * 2 - (labelCol - rect.X - inset), 28, "Edit Skeletal Mesh & Physics",
                    EditorTheme.WithAlpha(EditorTheme.Purple, 0.2f),
                    EditorTheme.WithAlpha(EditorTheme.Purple, 0.3f),
                    EditorTheme.WithAlpha(EditorTheme.Purple, 0.4f),
                    new System.Numerics.Vector4(0,0,0,0),
                    EditorTheme.Purple, 9703))
                {
                    if (!string.IsNullOrEmpty(skelComp.MeshAssetPath))
                    {
                        OpenSkeletalMeshEditor(skelComp.MeshAssetPath);
                    }
                }
                y += 32;
                y += EditorTheme.PadLg;
            }
        }
        
        // TeaScript Section
        bool hasScript = false;
        string scriptPath = "None";
        bool scriptEnabled = false;
        bool scriptAllowRuntimeUI = false;
        bool scriptBlockRuntimeInput = false;
        Entity selectedScriptEntity = default;
        
        if (hasSelectedEntity && _world != null)
        {
            if (_world.TryGetComponent<TeaScriptComponent>(selectedEntity, out var scriptComp))
            {
                selectedScriptEntity = selectedEntity;
                hasScript = true;
                scriptPath = string.IsNullOrEmpty(scriptComp.ScriptAssetId) ? "None" : Path.GetFileName(scriptComp.ScriptAssetId);
                scriptEnabled = scriptComp.IsEnabled;
                scriptAllowRuntimeUI = scriptComp.AllowRuntimeUI;
                scriptBlockRuntimeInput = scriptComp.BlockRuntimeInput;
            }
        }
        
        ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
        ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.Green);
        ui.SetCursor(labelCol + 2, (y + 8) - scrollOffset);
        ui.Text("TeaScript", EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + EditorTheme.Pad;
        
        ui.SetCursor(labelCol, (y + 2) - scrollOffset);
        ui.Text("Script", EditorTheme.TextMuted);
        
        float scriptInputW = rect.W - (valueCol - rect.X) - inset;
        ui.RoundedPanel(valueCol - 6, (y - 2) - scrollOffset, scriptInputW + 6, 22, EditorTheme.Bg0, EditorTheme.InputRadius);
        ui.Panel(valueCol - 6, (y - 2) - scrollOffset, scriptInputW + 6, 1, EditorTheme.Border1); // inner shadow

        ui.SetCursor(valueCol, (y + 2) - scrollOffset);
        ui.Text(scriptPath, hasScript ? EditorTheme.Green : EditorTheme.TextDisabled);
        y += 28;
        
        ui.SetCursor(labelCol, (y + 2) - scrollOffset);
        ui.Text("Enabled", EditorTheme.TextMuted);
        ui.SetCursor(valueCol, (y + 2) - scrollOffset);
        ui.Text(scriptEnabled ? "Yes" : "No", scriptEnabled ? EditorTheme.Green : EditorTheme.TextDisabled);
        y += 30;

        ui.SetCursor(labelCol, (y + 2) - scrollOffset);
        ui.Text("Runtime UI", EditorTheme.TextMuted);
        if (ui.ButtonEx(valueCol, y - scrollOffset, 72, 20, scriptAllowRuntimeUI ? "Allowed" : "Blocked",
            scriptAllowRuntimeUI ? EditorTheme.WithAlpha(EditorTheme.Green, 0.2f) : EditorTheme.Bg3,
            scriptAllowRuntimeUI ? EditorTheme.WithAlpha(EditorTheme.Green, 0.35f) : EditorTheme.Bg2,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            scriptAllowRuntimeUI ? EditorTheme.Green : EditorTheme.TextDisabled, 9701) &&
            hasScript && selectedScriptEntity.Id != 0 && _world != null)
        {
            ref var script = ref _world.GetComponent<TeaScriptComponent>(selectedScriptEntity);
            script.AllowRuntimeUI = !script.AllowRuntimeUI;
        }
        y += 28;

        ui.SetCursor(labelCol, (y + 2) - scrollOffset);
        ui.Text("Block Input", EditorTheme.TextMuted);
        if (ui.ButtonEx(valueCol, y - scrollOffset, 72, 20, scriptBlockRuntimeInput ? "On" : "Off",
            scriptBlockRuntimeInput ? EditorTheme.WithAlpha(EditorTheme.Orange, 0.2f) : EditorTheme.Bg3,
            scriptBlockRuntimeInput ? EditorTheme.WithAlpha(EditorTheme.Orange, 0.35f) : EditorTheme.Bg2,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            scriptBlockRuntimeInput ? EditorTheme.Orange : EditorTheme.TextDisabled, 9702) &&
            hasScript && selectedScriptEntity.Id != 0 && _world != null)
        {
            ref var script = ref _world.GetComponent<TeaScriptComponent>(selectedScriptEntity);
            script.BlockRuntimeInput = !script.BlockRuntimeInput;
        }
        y += 30;

        // ── Physics Inspector Section ──
        bool hasPhysics = false;
        if (hasSelectedEntity && _world != null)
        {
            var entity = selectedEntity;
            {
                // Physics (merged Rigidbody + Collider)
                if (_world.TryGetComponent<BlueSky.Core.ECS.Builtin.PhysicsComponent>(entity, out var physComp))
                {
                    hasPhysics = true;
                    ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
                    ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.Orange);
                    ui.SetCursor(labelCol + 2, (y + 8) - scrollOffset);
                    ui.Text("Physics", EditorTheme.TextPrimary);
                    
                    // Remove button
                    if (ui.ButtonEx(rect.X + rect.W - inset - 30, (y + 6) - scrollOffset, 24, 20, "×",
                        EditorTheme.WithAlpha(EditorTheme.Red, 0.2f), EditorTheme.Red, EditorTheme.Red,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.Red, 9801))
                    {
                        _world.RemoveComponent<BlueSky.Core.ECS.Builtin.PhysicsComponent>(entity);
                    }
                    y += EditorTheme.SectionH + EditorTheme.Pad;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Mass", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float mass = physComp.Mass;
                    if (ui.Slider(ref mass, 0.1f, 10000f, 100, 14))
                    {
                        physComp.Mass = mass;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Drag", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float drag = physComp.Drag;
                    if (ui.Slider(ref drag, 0f, 10f, 100, 14))
                    {
                        physComp.Drag = drag;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Ang. Drag", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float angDrag = physComp.AngularDrag;
                    if (ui.Slider(ref angDrag, 0f, 10f, 100, 14))
                    {
                        physComp.AngularDrag = angDrag;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Use Gravity", EditorTheme.TextMuted);
                    if (ui.ButtonEx(valueCol, y - scrollOffset, 40, 20, physComp.UseGravity ? "ON" : "OFF",
                        physComp.UseGravity ? EditorTheme.Green : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9802))
                    {
                        physComp.UseGravity = !physComp.UseGravity;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Kinematic", EditorTheme.TextMuted);
                    if (ui.ButtonEx(valueCol, y - scrollOffset, 40, 20, physComp.IsKinematic ? "ON" : "OFF",
                        physComp.IsKinematic ? EditorTheme.Green : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9806))
                    {
                        physComp.IsKinematic = !physComp.IsKinematic;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    // Freeze Position
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Freeze Pos", EditorTheme.TextMuted);
                    float freezeX = valueCol;
                    if (ui.ButtonEx(freezeX, y - scrollOffset, 28, 20, "X",
                        physComp.FreezePositionX ? EditorTheme.Red : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9807))
                    {
                        physComp.FreezePositionX = !physComp.FreezePositionX;
                        _world.AddComponent(entity, physComp);
                    }
                    if (ui.ButtonEx(freezeX + 32, y - scrollOffset, 28, 20, "Y",
                        physComp.FreezePositionY ? EditorTheme.Red : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9808))
                    {
                        physComp.FreezePositionY = !physComp.FreezePositionY;
                        _world.AddComponent(entity, physComp);
                    }
                    if (ui.ButtonEx(freezeX + 64, y - scrollOffset, 28, 20, "Z",
                        physComp.FreezePositionZ ? EditorTheme.Red : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9809))
                    {
                        physComp.FreezePositionZ = !physComp.FreezePositionZ;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    // Freeze Rotation
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Freeze Rot", EditorTheme.TextMuted);
                    if (ui.ButtonEx(freezeX, y - scrollOffset, 28, 20, "X",
                        physComp.FreezeRotationX ? EditorTheme.Red : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9810))
                    {
                        physComp.FreezeRotationX = !physComp.FreezeRotationX;
                        _world.AddComponent(entity, physComp);
                    }
                    if (ui.ButtonEx(freezeX + 32, y - scrollOffset, 28, 20, "Y",
                        physComp.FreezeRotationY ? EditorTheme.Red : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9811))
                    {
                        physComp.FreezeRotationY = !physComp.FreezeRotationY;
                        _world.AddComponent(entity, physComp);
                    }
                    if (ui.ButtonEx(freezeX + 64, y - scrollOffset, 28, 20, "Z",
                        physComp.FreezeRotationZ ? EditorTheme.Red : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9812))
                    {
                        physComp.FreezeRotationZ = !physComp.FreezeRotationZ;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    // ── Separator ──
                    y += 6;

                    // Collider Type selector
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Type", EditorTheme.TextMuted);
                    float typeX = valueCol;
                    if (ui.ButtonEx(typeX, y - scrollOffset, 36, 20, "Box",
                        physComp.Type == BlueSky.Core.ECS.Builtin.ColliderType.Box ? EditorTheme.Teal : EditorTheme.Bg3,
                        EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9820))
                    {
                        physComp.Type = BlueSky.Core.ECS.Builtin.ColliderType.Box;
                        _world.AddComponent(entity, physComp);
                    }
                    if (ui.ButtonEx(typeX + 40, y - scrollOffset, 48, 20, "Sphere",
                        physComp.Type == BlueSky.Core.ECS.Builtin.ColliderType.Sphere ? EditorTheme.Teal : EditorTheme.Bg3,
                        EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9821))
                    {
                        physComp.Type = BlueSky.Core.ECS.Builtin.ColliderType.Sphere;
                        _world.AddComponent(entity, physComp);
                    }
                    if (ui.ButtonEx(typeX + 92, y - scrollOffset, 56, 20, "Capsule",
                        physComp.Type == BlueSky.Core.ECS.Builtin.ColliderType.Capsule ? EditorTheme.Teal : EditorTheme.Bg3,
                        EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9822))
                    {
                        physComp.Type = BlueSky.Core.ECS.Builtin.ColliderType.Capsule;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    // Size/Radius/Height based on type
                    if (physComp.Type == BlueSky.Core.ECS.Builtin.ColliderType.Box)
                    {
                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Size X", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        float sizeX = physComp.Size.X;
                        if (ui.Slider(ref sizeX, 0.1f, 10f, 100, 14))
                        {
                            physComp.Size = new System.Numerics.Vector3(sizeX, physComp.Size.Y, physComp.Size.Z);
                            _world.AddComponent(entity, physComp);
                        }
                        y += 20;

                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Size Y", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        float sizeY = physComp.Size.Y;
                        if (ui.Slider(ref sizeY, 0.1f, 10f, 100, 14))
                        {
                            physComp.Size = new System.Numerics.Vector3(physComp.Size.X, sizeY, physComp.Size.Z);
                            _world.AddComponent(entity, physComp);
                        }
                        y += 20;

                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Size Z", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        float sizeZ = physComp.Size.Z;
                        if (ui.Slider(ref sizeZ, 0.1f, 10f, 100, 14))
                        {
                            physComp.Size = new System.Numerics.Vector3(physComp.Size.X, physComp.Size.Y, sizeZ);
                            _world.AddComponent(entity, physComp);
                        }
                        y += 24;
                    }
                    else if (physComp.Type == BlueSky.Core.ECS.Builtin.ColliderType.Sphere)
                    {
                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Radius", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        float radius = physComp.Radius;
                        if (ui.Slider(ref radius, 0.1f, 10f, 100, 14))
                        {
                            physComp.Radius = radius;
                            _world.AddComponent(entity, physComp);
                        }
                        y += 24;
                    }
                    else if (physComp.Type == BlueSky.Core.ECS.Builtin.ColliderType.Capsule)
                    {
                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Radius", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        float radius = physComp.Radius;
                        if (ui.Slider(ref radius, 0.1f, 10f, 100, 14))
                        {
                            physComp.Radius = radius;
                            _world.AddComponent(entity, physComp);
                        }
                        y += 20;

                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Height", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        float height = physComp.Height;
                        if (ui.Slider(ref height, 0.1f, 10f, 100, 14))
                        {
                            physComp.Height = height;
                            _world.AddComponent(entity, physComp);
                        }
                        y += 24;
                    }

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Is Trigger", EditorTheme.TextMuted);
                    if (ui.ButtonEx(valueCol, y - scrollOffset, 40, 20, physComp.IsTrigger ? "ON" : "OFF",
                        physComp.IsTrigger ? EditorTheme.Green : EditorTheme.Bg3, EditorTheme.AccentHover, EditorTheme.AccentDim,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.TextPrimary, 9823))
                    {
                        physComp.IsTrigger = !physComp.IsTrigger;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Friction", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float friction = physComp.Friction;
                    if (ui.Slider(ref friction, 0f, 1f, 100, 14))
                    {
                        physComp.Friction = friction;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 20;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Restitution", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float restitution = physComp.Restitution;
                    if (ui.Slider(ref restitution, 0f, 1f, 100, 14))
                    {
                        physComp.Restitution = restitution;
                        _world.AddComponent(entity, physComp);
                    }
                    y += 24;
                }
                
                // ── Terrain Inspector Section ──
                if (_world.TryGetComponent<TerrainComponent>(entity, out var terrainComp))
                {
                    ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
                    ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.Green);
                    ui.SetCursor(labelCol + 2, (y + 8) - scrollOffset);
                    ui.Text("Terrain", EditorTheme.TextPrimary);
                    
                    // Remove button
                    if (ui.ButtonEx(rect.X + rect.W - inset - 30, (y + 6) - scrollOffset, 24, 20, "×",
                        EditorTheme.WithAlpha(EditorTheme.Red, 0.2f), EditorTheme.Red, EditorTheme.Red,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.Red, 9830))
                    {
                        _world.RemoveComponent<TerrainComponent>(entity);
                    }
                    y += EditorTheme.SectionH + EditorTheme.Pad;

                    // Terrain dimensions
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Grid Size", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, (y + 2) - scrollOffset);
                    ui.Text($"{terrainComp.Width} x {terrainComp.Height}", EditorTheme.AccentHover);
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Asset", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, (y + 2) - scrollOffset);
                    string terrainAssetName = string.IsNullOrEmpty(terrainComp.TerrainAssetPath)
                        ? "Unsaved"
                        : Path.GetFileName(terrainComp.TerrainAssetPath);
                    ui.Text(terrainAssetName.Length > 20 ? terrainAssetName[..17] + "..." : terrainAssetName, EditorTheme.TextSecondary);
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("World Width", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float worldWidth = terrainComp.WorldWidth;
                    bool worldWidthChanged = ui.Slider(ref worldWidth, 10f, 500f, 100, 14);
                    // Only update if slider was actually moved AND value changed
                    if (worldWidthChanged && Math.Abs(worldWidth - terrainComp.WorldWidth) > 0.01f)
                    {
                        terrainComp.WorldWidth = worldWidth;
                        terrainComp.NeedsRebuild = true;
                        _world.AddComponent(entity, terrainComp);
                        Console.WriteLine($"[Inspector] WorldWidth changed to {worldWidth}");
                    }
                    y += 20;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("World Height", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float worldHeight = terrainComp.WorldHeight;
                    bool worldHeightChanged = ui.Slider(ref worldHeight, 10f, 500f, 100, 14);
                    // Only update if slider was actually moved AND value changed
                    if (worldHeightChanged && Math.Abs(worldHeight - terrainComp.WorldHeight) > 0.01f)
                    {
                        terrainComp.WorldHeight = worldHeight;
                        terrainComp.NeedsRebuild = true;
                        _world.AddComponent(entity, terrainComp);
                        Console.WriteLine($"[Inspector] WorldHeight changed to {worldHeight}");
                    }
                    y += 20;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Max Elevation", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float maxElevation = terrainComp.MaxElevation;
                    if (ui.Slider(ref maxElevation, 1f, 100f, 100, 14))
                    {
                        terrainComp.MaxElevation = maxElevation;
                        terrainComp.NeedsRebuild = true;
                        _world.AddComponent(entity, terrainComp);
                    }
                    y += 24;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Chunk Size", EditorTheme.TextMuted);
                    float chunkBtnX = valueCol;
                    foreach (int size in new[] { 16, 32, 64 })
                    {
                        bool active = terrainComp.ChunkSize == size;
                        if (ui.ButtonEx(chunkBtnX, y - scrollOffset, 30, 18, size.ToString(),
                            active ? EditorTheme.WithAlpha(EditorTheme.Green, 0.35f) : EditorTheme.Bg3,
                            EditorTheme.Bg2, EditorTheme.Bg1,
                            new System.Numerics.Vector4(0,0,0,0), active ? EditorTheme.Green : EditorTheme.TextSecondary,
                            (uint)(9840 + size)))
                        {
                            terrainComp.ChunkSize = size;
                            terrainComp.NeedsRebuild = true;
                            _world.AddComponent(entity, terrainComp);
                        }
                        chunkBtnX += 34;
                    }
                    y += 22;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("LOD Count", EditorTheme.TextMuted);
                    float lodBtnX = valueCol;
                    for (int lod = 1; lod <= 3; lod++)
                    {
                        bool active = terrainComp.LodCount == lod;
                        if (ui.ButtonEx(lodBtnX, y - scrollOffset, 26, 18, lod.ToString(),
                            active ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.35f) : EditorTheme.Bg3,
                            EditorTheme.Bg2, EditorTheme.Bg1,
                            new System.Numerics.Vector4(0,0,0,0), active ? EditorTheme.Accent : EditorTheme.TextSecondary,
                            (uint)(9860 + lod)))
                        {
                            terrainComp.LodCount = lod;
                            terrainComp.NeedsRebuild = true;
                            _world.AddComponent(entity, terrainComp);
                        }
                        lodBtnX += 30;
                    }
                    y += 22;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Collision", EditorTheme.TextMuted);
                    if (ui.ButtonEx(valueCol, y - scrollOffset, 54, 18, terrainComp.CollisionEnabled ? "On" : "Off",
                        terrainComp.CollisionEnabled ? EditorTheme.WithAlpha(EditorTheme.Green, 0.35f) : EditorTheme.Bg3,
                        EditorTheme.Bg2, EditorTheme.Bg1,
                        new System.Numerics.Vector4(0,0,0,0), terrainComp.CollisionEnabled ? EditorTheme.Green : EditorTheme.TextSecondary, 9865))
                    {
                        terrainComp.CollisionEnabled = !terrainComp.CollisionEnabled;
                        _world.AddComponent(entity, terrainComp);
                    }
                    y += 24;

                    // Brush controls
                    ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 1, EditorTheme.Border1);
                    y += 8;

                    ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 22, EditorTheme.Bg2);
                    ui.SetCursor(labelCol + 2, (y + 5) - scrollOffset);
                    ui.Text("Sculpt Tools", EditorTheme.TextPrimary);
                    y += 28;
                    
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Edit Mode", EditorTheme.TextMuted);
                    if (ui.ButtonEx(valueCol, y - scrollOffset, 66, 18, _terrainEditMode ? "Enabled" : "Disabled",
                        _terrainEditMode ? EditorTheme.WithAlpha(EditorTheme.Green, 0.35f) : EditorTheme.Bg3,
                        EditorTheme.Bg2, EditorTheme.Bg1,
                        new System.Numerics.Vector4(0,0,0,0), _terrainEditMode ? EditorTheme.Green : EditorTheme.TextSecondary, 9866))
                    {
                        _terrainEditMode = !_terrainEditMode;
                    }
                    y += 20;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Active", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, (y + 2) - scrollOffset);
                    ui.Text(_terrainBrushMode.ToString(), EditorTheme.Green);
                    y += 20;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Radius", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    ui.Slider(ref _terrainBrushRadius, 1f, 35f, 100, 14);
                    y += 20;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Strength", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    ui.Slider(ref _terrainBrushStrength, 0.05f, 1.5f, 100, 14);
                    y += 22;
                    
                    float brushX = labelCol;
                    DrawTerrainBrushButton(ui, brushX, y, "Raise", BrushMode.Raise, 9831);
                    DrawTerrainBrushButton(ui, brushX + 64, y, "Lower", BrushMode.Lower, 9832);
                    y += 24;
                    
                    DrawTerrainBrushButton(ui, brushX, y, "Smooth", BrushMode.Smooth, 9833);
                    DrawTerrainBrushButton(ui, brushX + 64, y, "Flatten", BrushMode.Flatten, 9834);
                    y += 24;

                    DrawTerrainBrushButton(ui, brushX, y, "Noise", BrushMode.Noise, 9835);
                    DrawTerrainBrushButton(ui, brushX + 64, y, "Erode", BrushMode.Erode, 9836);
                    y += 24;

                    DrawTerrainBrushButton(ui, brushX, y, "Erase", BrushMode.Erase, 9837);
                    y += 28;

                    if (_terrainBrushMode == BrushMode.Flatten)
                    {
                        ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Target H", EditorTheme.TextMuted);
                        ui.SetCursor(valueCol, y - scrollOffset);
                        ui.Slider(ref _terrainFlattenHeight, 0f, terrainComp.MaxElevation, 100, 14);
                        y += 22;
                    }

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset);
                    ui.Text(_terrainEditMode ? "Hover ring + left-drag" : "Enable edit mode first", EditorTheme.TextSecondary);
                    y += 20;
                }

                // Add Components
                y += 10;
                if (!hasPhysics)
                {
                    if (ui.ButtonEx(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 24, "+ Add Physics",
                        EditorTheme.Bg3, EditorTheme.Bg2, EditorTheme.Bg1, new System.Numerics.Vector4(0,0,0,0), EditorTheme.Orange, 9804))
                    {
                        _world.AddComponent(entity, new BlueSky.Core.ECS.Builtin.PhysicsComponent());
                    }
                    y += 28;
                }
                
                // Check if entity has car controller
                bool hasCarController = _world.TryGetComponent<BlueSky.Core.ECS.Builtin.CarControllerComponent>(entity, out _);
                if (!hasCarController)
                {
                    if (ui.ButtonEx(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 24, "🚗 Add Car Controller",
                        EditorTheme.WithAlpha(EditorTheme.Purple, 0.2f),
                        EditorTheme.WithAlpha(EditorTheme.Purple, 0.3f),
                        EditorTheme.WithAlpha(EditorTheme.Purple, 0.4f),
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.Purple, 9806))
                    {
                        _carControllerSystem?.AddCarController(entity);
                        Log("🚗 Car Controller added! It will auto-possess on next frame. Press E to exit.");
                    }
                    y += 28;
                }

                // Check if entity has network manager
                bool hasNetworkManager = _world.TryGetComponent<BlueSky.Core.ECS.Builtin.NetworkManagerComponent>(entity, out _);
                if (!hasNetworkManager)
                {
                    if (ui.ButtonEx(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 24, "🌐 Add Network Manager",
                        EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.2f),
                        EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.3f),
                        EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.4f),
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.AccentCyan, 9840))
                    {
                        int storIdx = BlueSky.Core.ECS.Builtin.NetworkManagerStorage.Allocate("");
                        _world.AddComponent(entity, new BlueSky.Core.ECS.Builtin.NetworkManagerComponent
                        {
                            StorageIndex = storIdx,
                            MaxPlayers = 8,
                            ReplicationInterval = 0.1f,
                            ReplicationTimer = 0f,
                            PlayerCount = 0,
                            IsHost = false,
                            HasStartedSpawning = false
                        });
                        Log("🌐 Network Manager added! Configure prefab path in inspector.");
                    }
                    y += 28;
                }

                // Check if entity has spawn point
                bool hasSpawnPoint = _world.TryGetComponent<BlueSky.Core.ECS.Builtin.SpawnPointComponent>(entity, out _);
                if (!hasSpawnPoint)
                {
                    if (ui.ButtonEx(rect.X + inset, y - scrollOffset, rect.W - inset * 2, 24, "📍 Add Spawn Point",
                        EditorTheme.WithAlpha(EditorTheme.Yellow, 0.2f),
                        EditorTheme.WithAlpha(EditorTheme.Yellow, 0.3f),
                        EditorTheme.WithAlpha(EditorTheme.Yellow, 0.4f),
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.Yellow, 9841))
                    {
                        _world.AddComponent(entity, new BlueSky.Core.ECS.Builtin.SpawnPointComponent
                        {
                            Index = 0,
                            IsOccupied = false,
                            OccupantEntityId = -1
                        });
                        Log("📍 Spawn Point added! Set its index in inspector.");
                    }
                    y += 28;
                }

                // ── NetworkManager Inspector Section ──
                if (_world.TryGetComponent<BlueSky.Core.ECS.Builtin.NetworkManagerComponent>(entity, out var nmComp))
                {
                    ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
                    ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.AccentCyan);
                    ui.SetCursor(labelCol + 2, (y + 8) - scrollOffset);
                    ui.Text("Network Manager", EditorTheme.TextPrimary);

                    // Remove button
                    if (ui.ButtonEx(rect.X + rect.W - inset - 30, (y + 6) - scrollOffset, 24, 20, "×",
                        EditorTheme.WithAlpha(EditorTheme.Red, 0.2f), EditorTheme.Red, EditorTheme.Red,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.Red, 9842))
                    {
                        BlueSky.Core.ECS.Builtin.NetworkManagerStorage.SetPrefabPath(nmComp.StorageIndex, "");
                        _world.RemoveComponent<BlueSky.Core.ECS.Builtin.NetworkManagerComponent>(entity);
                    }
                    y += EditorTheme.SectionH + EditorTheme.Pad;

                    // Prefab Path (drop target for .bseprefab files)
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Player Prefab", EditorTheme.TextMuted);
                    string prefabPath = BlueSky.Core.ECS.Builtin.NetworkManagerStorage.GetPrefabPath(nmComp.StorageIndex);
                    string prefabName = string.IsNullOrEmpty(prefabPath) ? "None" : Path.GetFileNameWithoutExtension(prefabPath);
                    float pfFieldX = valueCol - 6;
                    float pfFieldY = (y - 2) - scrollOffset;
                    float pfFieldW = rect.W - (valueCol - rect.X) - inset + 6;
                    float pfFieldH = 22;

                    // Highlight when dragging a .bseprefab file over this field
                    bool isOverDropZone = _isDraggingAsset && _draggedAssetPath != null &&
                        Path.GetExtension(_draggedAssetPath).Equals(".bseprefab", StringComparison.OrdinalIgnoreCase) &&
                        ui.IsHovering(pfFieldX, pfFieldY, pfFieldW, pfFieldH);

                    var pfBg = isOverDropZone ? EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.25f) : EditorTheme.Bg0;
                    var pfBorder = isOverDropZone ? EditorTheme.AccentCyan : EditorTheme.Border1;
                    ui.RoundedPanel(pfFieldX, pfFieldY, pfFieldW, pfFieldH, pfBg, EditorTheme.InputRadius);
                    EditorChrome.Stroke(ui, pfFieldX, pfFieldY, pfFieldW, pfFieldH, pfBorder);
                    ui.SetCursor(valueCol, (y + 2) - scrollOffset);
                    if (isOverDropZone)
                    {
                        ui.Text("Drop .bseprefab here", EditorTheme.AccentCyan);
                    }
                    else
                    {
                        ui.Text(prefabName.Length > 20 ? prefabName[..17] + "..." : prefabName,
                            !string.IsNullOrEmpty(prefabPath) ? EditorTheme.AccentCyan : EditorTheme.TextDisabled);
                    }
                    // Store drop zone bounds (screen coordinates) for drop detection
                    _nmPrefabDropZone = new System.Numerics.Vector4(pfFieldX, pfFieldY, pfFieldW, pfFieldH);
                    y += 24;

                    // Max Players slider
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Max Players", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float maxPlayers = nmComp.MaxPlayers;
                    if (ui.Slider(ref maxPlayers, 2f, 32f, 100, 14))
                    {
                        nmComp.MaxPlayers = (int)maxPlayers;
                        _world.AddComponent(entity, nmComp);
                    }
                    y += 24;

                    // Replication Interval slider
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Replication", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float replInterval = nmComp.ReplicationInterval;
                    if (ui.Slider(ref replInterval, 0.01f, 1.0f, 100, 14))
                    {
                        nmComp.ReplicationInterval = replInterval;
                        _world.AddComponent(entity, nmComp);
                    }
                    y += 28;
                }

                // ── SpawnPoint Inspector Section ──
                if (_world.TryGetComponent<BlueSky.Core.ECS.Builtin.SpawnPointComponent>(entity, out var spComp))
                {
                    ui.Panel(rect.X + inset, y - scrollOffset, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
                    ui.Panel(rect.X + inset, y - scrollOffset, 3, EditorTheme.SectionH, EditorTheme.Yellow);
                    ui.SetCursor(labelCol + 2, (y + 8) - scrollOffset);
                    ui.Text("Spawn Point", EditorTheme.TextPrimary);

                    // Remove button
                    if (ui.ButtonEx(rect.X + rect.W - inset - 30, (y + 6) - scrollOffset, 24, 20, "×",
                        EditorTheme.WithAlpha(EditorTheme.Red, 0.2f), EditorTheme.Red, EditorTheme.Red,
                        new System.Numerics.Vector4(0,0,0,0), EditorTheme.Red, 9843))
                    {
                        _world.RemoveComponent<BlueSky.Core.ECS.Builtin.SpawnPointComponent>(entity);
                    }
                    y += EditorTheme.SectionH + EditorTheme.Pad;

                    // Spawn Index
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Index", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float spIndex = spComp.Index;
                    if (ui.Slider(ref spIndex, 0f, 15f, 100, 14))
                    {
                        spComp.Index = (int)spIndex;
                        _world.AddComponent(entity, spComp);
                    }
                    y += 24;

                    // Occupied status (read-only)
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Occupied", EditorTheme.TextMuted);
                    ui.SetCursor(valueCol, (y + 2) - scrollOffset);
                    ui.Text(spComp.IsOccupied ? "Yes" : "No", spComp.IsOccupied ? EditorTheme.Orange : EditorTheme.TextSecondary);
                    y += 28;
                }
            }
        }

        ui.EndScrollArea("DetailsPanel");

        // Component count pinned to bottom
        ui.Panel(rect.X + inset, rect.Y + rect.H - 28, rect.W - inset * 2, 1, EditorTheme.Border1);
        ui.SetCursor(labelCol, rect.Y + rect.H - 20);
        int compCount = (hasMesh ? 1 : 0) + (hasScript ? 1 : 0) + (hasPhysics ? 1 : 0) + 1; // +1 for transform
        ui.Text($"{compCount} components", EditorTheme.TextDisabled);
    }


    private static void DrawContentBrowserPanel(EditorUI ui, DockRect rect)
    {

        // Map to centralized theme colors
        var accentBlue = EditorTheme.Accent;
        var accentBlueLight = EditorTheme.AccentHover;
        var accentBlueGlow = EditorTheme.AccentHover;
        var accentFolder = EditorTheme.FolderFront;
        var accentFolderDark = EditorTheme.FolderBack;
        var accentGreen = EditorTheme.Green;
        var accentPurple = EditorTheme.Purple;
        var textPrimary = EditorTheme.TextPrimary;
        var textSecondary = EditorTheme.TextSecondary;
        var textMuted = EditorTheme.TextMuted;
        var textDark = EditorTheme.TextDisabled;
        var borderLight = EditorTheme.Border2;
        var borderSubtle = EditorTheme.Border1;
        var borderDark = EditorTheme.Border0;
        var bgBase = EditorTheme.Bg1;
        var bgSidebar = EditorTheme.Bg2;
        var bgPanel = EditorTheme.Bg2;
        var bgCard = EditorTheme.Bg3;
        var bgCardHover = EditorTheme.Bg4;
        var bgInput = EditorTheme.Bg0;

        // ── Layout ───────────────────────────────────────────────────────────
        float sidebarW = 140;
        float toolbarH = EditorTheme.ToolbarH;
        float statusbarH = EditorTheme.StatusH;

        // ── Background ──────────────────────────────────────────────────────
        ui.Panel(rect.X, rect.Y, rect.W, rect.H, bgBase);

        // ── TOOLBAR ───────────────────────────────────────────────────────────
        ui.GradientPanel(rect.X, rect.Y, rect.W, toolbarH, bgPanel, EditorTheme.Bg1);
        ui.Panel(rect.X, rect.Y + toolbarH - 1, rect.W, 1, borderSubtle);

        // Breadcrumb — vertically centered in toolbar
        float bcY = rect.Y + (toolbarH - 12) / 2;
        float tx = rect.X + 14;
        ui.Circle(tx + 4, rect.Y + toolbarH / 2, 4, EditorTheme.Orange, true);
        tx += 16;
        ui.SetCursor(tx, bcY);
        ui.Text("Content", textMuted);
        tx += 52;
        ui.SetCursor(tx, bcY);
        ui.Text("/", EditorTheme.TextDisabled);
        tx += 10;
        ui.SetCursor(tx, bcY);
        ui.Text(Path.GetFileName(ProjectManager.AssetsDir ?? "Assets"), textPrimary);

        // Import Button — right-aligned in toolbar
        uint importBtnId = 8001;
        if (ui.ButtonEx(rect.X + rect.W - 96, rect.Y + (toolbarH - 24) / 2, 84, 24, "+ Import",
            accentBlue,
            new System.Numerics.Vector4(0.25f, 0.60f, 1.0f, 1f), // hover
            new System.Numerics.Vector4(0.15f, 0.45f, 0.85f, 1f), // pressed
            new System.Numerics.Vector4(0, 0, 0, 0.4f), // shadow
            textPrimary, importBtnId))
        {
            ImportFilesDialog();
        }

        // ── SIDEBAR ─────────────────────────────────────────────────────────
        float sidebarX = rect.X;
        float sidebarY = rect.Y + toolbarH;
        float sidebarH = rect.H - toolbarH - statusbarH;
        ui.Panel(sidebarX, sidebarY, sidebarW, sidebarH, bgSidebar);
        ui.Panel(sidebarX + sidebarW - 1, sidebarY, 1, sidebarH, borderSubtle);
        ui.SetCursor(sidebarX + 14, sidebarY + 12);
        ui.Text("Sources", EditorTheme.TextMuted);

        // Tree items — no header, start immediately
        float treeY = sidebarY + 28;
        string[] sources = { "Content", "Collections", "Shared" };
        string[] icons = { "\u25a3", "\u2605", "\u25c8" };
        for (int i = 0; i < sources.Length; i++)
        {
            uint sourceId = 7000u + (uint)i;
            bool isSel = _selectedSourceIndex == i;
            
            // Better row colors
            var rowBg = isSel ? EditorTheme.SelectionBg : EditorTheme.WithAlpha(EditorTheme.Bg2, 0f);
            var txtCol = isSel ? accentBlueLight : textSecondary;
            float rowH = 26;

            // Clickable row with padding
            float rowX = sidebarX + 8;
            float rowW = sidebarW - 16;
            
            if (ui.ClickableCard(rowX, treeY, rowW, rowH, sourceId,
                rowBg,
                EditorTheme.HoverBg, // hover
                EditorTheme.SelectionBg)) // pressed
            {
                _selectedSourceIndex = i;
                Log($"Switched to {sources[i]}");
            }

            // Selection indicator
            if (isSel)
            {
                ui.Panel(rowX, treeY, 3, rowH, accentBlue);
            }

            // Icon and text with better spacing
            ui.SetCursor(rowX + 10, treeY + 8);
            ui.Text(icons[i], isSel ? EditorTheme.TextPrimary : textSecondary);
            ui.SetCursor(rowX + 30, treeY + 8);
            ui.Text(sources[i], txtCol);
            
            treeY += rowH + 1; // tighter packing
        }
        


        // ── MAIN CONTENT ───────────────────────────────────────────────────
        float contentX = rect.X + sidebarW;
        float contentY = rect.Y + toolbarH;
        float contentW = rect.W - sidebarW;
        float contentH = rect.H - toolbarH - statusbarH;

        // ── ASSET GRID — starts immediately below toolbar ───────────
        float gridY = contentY + EditorTheme.Pad;
        float itemW = 104, itemH = 116;
        float gap = 10;

        if (string.IsNullOrEmpty(_currentBrowserDir))
        {
            _currentBrowserDir = ProjectManager.AssetsDir ?? "";
        }

        var browserListing = string.IsNullOrEmpty(_currentBrowserDir)
            ? null
            : Assets.GetContentBrowserListing(_currentBrowserDir);

        if (browserListing == null)
        {
            ui.SetCursor(contentX + 18, gridY + 12);
            ui.Text(string.IsNullOrEmpty(_currentBrowserDir) ? "Open a project to browse assets" : "Loading folder…", textMuted);
        }
        else if (browserListing.Error != null)
        {
            ui.SetCursor(contentX + 18, gridY + 12);
            ui.Text($"Could not read folder: {browserListing.Error}", EditorTheme.Red);
        }
        else
        {
            string[] dirs = browserListing.Directories;
            string[] files = browserListing.Files;

            float cx = contentX + 16;
            float cy = gridY;
            
            // Calculate content height
            int itemsPerRow = (int)((contentW - 32) / (itemW + gap));
            if (itemsPerRow <= 0) itemsPerRow = 1;
            bool hasBack = (_currentBrowserDir != ProjectManager.AssetsDir && ProjectManager.AssetsDir != null);
            int totalItems = (hasBack ? 1 : 0) + dirs.Length + files.Length;
            int rows = (int)Math.Ceiling((double)totalItems / itemsPerRow);
            float contentHeight = rows * (itemH + gap) + 16;
            
            float scrollOffset = ui.BeginScrollArea("ContentBrowserGrid", contentX, contentY, contentW, contentH, contentHeight);

            // Optional: Back Button
            if (hasBack)
            {
                if (cx + itemW > contentX + contentW - 16) { cx = contentX + 16; cy += itemH + gap; }
                
                float drawCy = cy - scrollOffset;
                
                // Culling check
                if (drawCy + itemH >= contentY && drawCy <= contentY + contentH)
                {
                    uint backId = 4999u;
                    bool isBackSel = _selectedAssetIndex == (int)backId;
                    
                    if (ui.ClickableCard(cx, drawCy, itemW, itemH, backId, bgCard, bgCardHover, new System.Numerics.Vector4(0.28f, 0.48f, 0.78f, 0.5f)))
                    {
                        _selectedAssetIndex = (int)backId;
                        
                        double now = ui.Time;
                        if (_doubleClickTarget == backId && (now - _lastClickTime) < 0.3)
                        {
                            var parentDir = Directory.GetParent(_currentBrowserDir)?.FullName;
                            if (parentDir != null && parentDir.StartsWith(ProjectManager.AssetsDir ?? ""))
                            {
                                _currentBrowserDir = parentDir;
                                _selectedAssetIndex = -1;
                            }
                            else
                            {
                                _currentBrowserDir = ProjectManager.AssetsDir ?? "";
                                _selectedAssetIndex = -1;
                            }
                        }
                        else
                        {
                            _doubleClickTarget = backId;
                            _lastClickTime = now;
                        }
                    }
                    
                    ui.SetCursor(cx + 8, drawCy + itemH - 24);
                    ui.Text("<- Back", textPrimary);
                }
                
                cx += itemW + gap;
            }

            // Folders - INTERACTIVE
            int folderIdx = 0;
            foreach (var dir in dirs)
            {
                if (cx + itemW > contentX + contentW - 16)
                { cx = contentX + 16; cy += itemH + gap; }

                float drawCy = cy - scrollOffset;
                if (drawCy + itemH < contentY || drawCy > contentY + contentH) 
                {
                    cx += itemW + gap;
                    folderIdx++;
                    continue;
                }

                uint cardId = 5000u + (uint)folderIdx;
                bool isCardSel = _selectedAssetIndex == (int)cardId;
                var cardBg = isCardSel ? new System.Numerics.Vector4(0.22f, 0.38f, 0.62f, 0.4f) : bgCard;

                // Interactive card with shadow
                if (ui.ClickableCard(cx, drawCy, itemW, itemH, cardId,
                    cardBg,
                    bgCardHover, // hover
                    new System.Numerics.Vector4(0.28f, 0.48f, 0.78f, 0.5f))) // pressed
                {
                    _selectedAssetIndex = (int)cardId;
                    Log($"Selected folder: {Path.GetFileName(dir)}");

                    double now = ui.Time;
                    if (_doubleClickTarget == cardId && (now - _lastClickTime) < 0.3)
                    {
                        // Double clicked -> Navigate into folder
                        _currentBrowserDir = dir;
                        _selectedAssetIndex = -1; // Deselect on folder change
                        _doubleClickTarget = 0;
                        Log($"Navigated to: {_currentBrowserDir}");
                    }
                    else
                    {
                        _doubleClickTarget = cardId;
                        _lastClickTime = now;
                    }
                }

                // Selection border
                if (isCardSel)
                {
                    ui.Panel(cx, drawCy, itemW, 2, accentBlueLight);
                    ui.Panel(cx, drawCy + itemH - 2, itemW, 2, accentBlueLight);
                }
                else
                {
                    ui.Panel(cx, drawCy, itemW, 2, borderLight);
                }

                // Clean folder icon - 3D style
                float ix = cx + (itemW - 48) / 2; // centered 48px icon
                float iy = drawCy + 16;
                float fw = 48, fh = 38;
                
                // Shadow
                ui.Shadow(ix, iy, fw, fh, 2, 3, 0.25f);
                
                // Folder tab
                ui.Panel(ix + 10, iy - 6, 18, 8, accentFolderDark);
                // Folder body (back)
                ui.Panel(ix, iy, fw, fh - 8, accentFolderDark);
                // Folder front
                ui.Panel(ix, iy + 6, fw, fh - 14, accentFolder);
                // Top shine
                ui.Panel(ix, iy + 6, fw, 2, new System.Numerics.Vector4(1f, 0.95f, 0.80f, 0.5f));

                // Label
                ui.SetCursor(cx + 10, drawCy + itemH - 24);
                string name = Path.GetFileName(dir);
                if (name.Length > 14) name = name[..12] + "..";
                ui.Text(name, isCardSel ? textPrimary : textSecondary);

                cx += itemW + gap;
                folderIdx++;
            }

            // Files - INTERACTIVE
            int fileIdx = 0;
            foreach (var file in files)
            {
                if (cx + itemW > contentX + contentW - 16)
                { cx = contentX + 16; cy += itemH + gap; }
                
                float drawCy = cy - scrollOffset;
                if (drawCy + itemH < contentY || drawCy > contentY + contentH)
                {
                    cx += itemW + gap;
                    fileIdx++;
                    continue;
                }

                string ext = Path.GetExtension(file).ToLower();
                bool isBlueAsset = ext == ".blueskyasset";
                bool isMesh = ext == ".obj" || ext == ".fbx" || ext == ".gltf";
                bool isTexture = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga";
                bool isCode = ext == ".cs" || ext == ".blueprint";
                bool isTeaScript = ext == ".tea";
                bool isBsphy = ext == ".bsphy";
                bool isStratamat = ext == ".stratamat";
                
                // Asset type info for tooltip
                string assetType = "File";
                string assetSubtype = "";
                System.Numerics.Vector4 typeColor = textMuted;

                uint cardId = 6000u + (uint)fileIdx;
                bool isCardSel = _selectedAssetIndex == (int)cardId;
                var cardBg = isBlueAsset ? new System.Numerics.Vector4(0.18f, 0.30f, 0.52f, 1f) :
                             isCardSel ? new System.Numerics.Vector4(0.22f, 0.38f, 0.62f, 0.4f) : bgCard;

                // Interactive card
                if (ui.ClickableCard(cx, drawCy, itemW, itemH, cardId,
                    cardBg,
                    bgCardHover,
                    new System.Numerics.Vector4(0.28f, 0.48f, 0.78f, 0.5f)))
                {
                    _selectedAssetIndex = (int)cardId;
                    _dragPos = ui.MousePosition; // record click origin for drag threshold
                    Log($"Selected file: {Path.GetFileName(file)}");
                    
                    // Double-click detection for .tea files
                    double now = ui.Time;
                    if (_doubleClickTarget == cardId && (now - _lastClickTime) < 0.3)
                    {
                        Console.WriteLine($"[StrataDbg] double-click: file={Path.GetFileName(file)} ext={ext} isStratamat={isStratamat}");
                        if (isTeaScript)
                        {
                            OpenScriptEditor(file);
                        }
                        else if (isStratamat)
                        {
                            OpenStrataMaterial(file);
                        }
                        else if (isBlueAsset)
                        {
                            var header = BlueSky.Core.Assets.BlueAsset.LoadHeader(file);
                            if (header != null)
                            {
                                if (header.Type == BlueSky.Core.Assets.AssetType.StaticMesh || header.Type == BlueSky.Core.Assets.AssetType.Mesh)
                                {
                                    OpenStaticMeshEditor(file);
                                }
                                else if (header.Type == BlueSky.Core.Assets.AssetType.SkeletalMesh)
                                {
                                    OpenSkeletalMeshEditor(file);
                                }
                            }
                        }
                    }
                    else
                    {
                        _doubleClickTarget = cardId;
                        _lastClickTime = now;
                    }
                }

                // --- Drag and Drop initiation ---
                if (ui.IsHovering(cx, drawCy, itemW, itemH) && ui.IsMouseDown)
                {
                    // First mouse-down frame on this card: select it and record click origin
                    if (_selectedAssetIndex != (int)cardId && !_isDraggingAsset)
                    {
                        _selectedAssetIndex = (int)cardId;
                        _dragPos = ui.MousePosition;
                    }

                    // If moved more than 5px from click origin, start dragging
                    if (System.Numerics.Vector2.Distance(ui.MousePosition, _dragPos) > 5 && !_isDraggingAsset)
                    {
                        bool isPrefab = ext == ".bseprefab";
                        if (isBlueAsset || isTeaScript || isPrefab || isBsphy || isStratamat)
                        {
                            _isDraggingAsset = true;
                            _draggedAssetPath = file;
                            Console.WriteLine($"[DragDrop] Started dragging: {Path.GetFileName(file)}");
                        }
                    }
                }

                // Type border (top)
                var typeBorder = borderLight;
                if (isCardSel) typeBorder = accentBlueLight;
                else if (isMesh) typeBorder = accentBlue;
                else if (isTexture) typeBorder = accentPurple;
                else if (isTeaScript) typeBorder = accentGreen;
                else if (isBsphy) typeBorder = EditorTheme.Orange;
                else if (isBlueAsset) typeBorder = accentBlueLight;
                ui.Panel(cx, drawCy, itemW, 2, typeBorder);

                // Selection indicator (bottom border too when selected)
                if (isCardSel)
                    ui.Panel(cx, drawCy + itemH - 2, itemW, 2, accentBlueLight);

                // Icon position
                float ix = cx + (itemW - 44) / 2;
                float iy = cy + 14;
                
                string badge = ext.TrimStart('.').ToUpper();
                string displayLabel = Path.GetFileNameWithoutExtension(file);
                
                if (isBlueAsset)
                {
                    var header = Assets.GetAssetHeader(file);
                    if (header != null)
                    {
                        displayLabel = header.AssetName;
                        
                        if (header.Type == BlueSky.Core.Assets.AssetType.StaticMesh)
                        {
                            isMesh = true;
                            assetType = "Static Mesh";
                            assetSubtype = "3D Model (Static)";
                            typeColor = new System.Numerics.Vector4(0.3f, 0.6f, 1.0f, 1f); // Blue
                        }
                        else if (header.Type == BlueSky.Core.Assets.AssetType.SkeletalMesh)
                        {
                            isMesh = true;
                            assetType = "Skeletal Mesh";
                            assetSubtype = "3D Model (Animated)";
                            typeColor = new System.Numerics.Vector4(0.5f, 0.8f, 1.0f, 1f); // Light blue
                        }
                        else if (header.Type == BlueSky.Core.Assets.AssetType.Texture)
                        {
                            assetType = "Texture";
                            assetSubtype = "Image Asset";
                            typeColor = new System.Numerics.Vector4(0.9f, 0.6f, 1.0f, 1f); // Light purple
                        }
                        badge = header.Type.ToString();
                    }
                }
                else if (isMesh)
                {
                    assetType = "Mesh File";
                    assetSubtype = ext.ToUpper() + " 3D Model";
                    typeColor = new System.Numerics.Vector4(0.3f, 0.6f, 1.0f, 1f); // Blue
                }
                else if (isTexture)
                {
                    assetType = "Texture";
                    assetSubtype = ext.ToUpper() + " Image";
                    typeColor = new System.Numerics.Vector4(0.8f, 0.5f, 1.0f, 1f); // Purple
                }
                else if (isTeaScript)
                {
                    assetType = "TeaScript";
                    assetSubtype = "Gameplay Script";
                    typeColor = new System.Numerics.Vector4(0.4f, 0.8f, 0.5f, 1f); // Green
                }
                else if (isCode)
                {
                    assetType = "Code";
                    assetSubtype = "Source File";
                    typeColor = new System.Numerics.Vector4(0.5f, 0.9f, 0.7f, 1f); // Light green
                }
                else if (isBsphy)
                {
                    assetType = "Physics Asset";
                    assetSubtype = "Skeletal Collision Data";
                    typeColor = EditorTheme.Orange;
                }
                else if (isStratamat)
                {
                    assetType = "Strata Material";
                    assetSubtype = ".stratamat surface";
                    typeColor = new System.Numerics.Vector4(1.0f, 0.55f, 0.15f, 1f); // Strata orange
                }

                if (isMesh)
                {
                    // Clean 3D cube icon
                    float cs = 32;
                    float cx2 = ix + (44 - cs) / 2;
                    float cy2 = iy + 4;
                    ui.Shadow(cx2 + 2, cy2 + 4, cs, cs - 8, 2, 3, 0.2f);
                    // Front face
                    ui.Panel(cx2 + 2, cy2 + 10, cs - 2, cs - 18, accentBlue);
                    // Top face
                    ui.Panel(cx2, cy2, cs - 2, 10, new System.Numerics.Vector4(0.45f, 0.70f, 1.0f, 1f));
                    // Side face
                    ui.Panel(cx2 + cs - 2, cy2 + 4, 6, cs - 14, new System.Numerics.Vector4(0.15f, 0.40f, 0.85f, 1f));
                }
                else if (isTexture)
                {
                    // Clean image icon
                    ui.Shadow(ix + 2, iy + 3, 40, 34, 2, 2, 0.2f);
                    ui.Panel(ix, iy, 40, 32, accentPurple);
                    ui.Panel(ix + 4, iy + 4, 32, 24, new System.Numerics.Vector4(0.90f, 0.70f, 1.0f, 1f));
                    // Corner detail
                    ui.Panel(ix + 2, iy + 2, 6, 2, accentPurple);
                    ui.Panel(ix + 2, iy + 2, 2, 6, accentPurple);
                }
                else if (isTeaScript)
                {
                    // TeaScript icon - scroll with tea cup
                    ui.Shadow(ix + 2, iy + 3, 36, 40, 2, 2, 0.2f);
                    ui.Panel(ix, iy, 36, 38, new System.Numerics.Vector4(0.95f, 0.90f, 0.80f, 1f)); // Parchment color
                    // Tea cup icon
                    ui.Panel(ix + 10, iy + 12, 16, 12, new System.Numerics.Vector4(0.40f, 0.70f, 0.50f, 1f));
                    ui.Panel(ix + 12, iy + 14, 12, 8, new System.Numerics.Vector4(0.60f, 0.85f, 0.65f, 1f));
                    // Handle
                    ui.Panel(ix + 24, iy + 16, 4, 6, new System.Numerics.Vector4(0.40f, 0.70f, 0.50f, 1f));
                    // Code lines
                    ui.Panel(ix + 6, iy + 28, 24, 2, new System.Numerics.Vector4(0.40f, 0.70f, 0.50f, 0.6f));
                    ui.Panel(ix + 6, iy + 32, 20, 2, new System.Numerics.Vector4(0.40f, 0.70f, 0.50f, 0.4f));
                }
                else if (isCode)
                {
                    // Clean document icon
                    ui.Shadow(ix + 2, iy + 3, 36, 40, 2, 2, 0.2f);
                    ui.Panel(ix, iy, 36, 38, bgPanel);
                    ui.Panel(ix + 6, iy + 6, 24, 3, accentGreen);
                    ui.Panel(ix + 6, iy + 14, 20, 2, new System.Numerics.Vector4(0.55f, 0.92f, 0.70f, 0.7f));
                    ui.Panel(ix + 6, iy + 22, 24, 2, new System.Numerics.Vector4(0.55f, 0.92f, 0.70f, 0.4f));
                }
                else if (isBsphy)
                {
                    // Physics asset icon - sphere with wireframe
                    ui.Shadow(ix + 2, iy + 3, 40, 36, 2, 3, 0.2f);
                    ui.Circle(ix + 20, iy + 18, 16, EditorTheme.Orange, true);
                    ui.Circle(ix + 20, iy + 18, 16, EditorTheme.WithAlpha(EditorTheme.Orange, 0.6f), false);
                    ui.Circle(ix + 20, iy + 18, 10, EditorTheme.WithAlpha(EditorTheme.Orange, 0.3f), false);
                }
                else
                {
                    // Generic document
                    ui.Shadow(ix + 2, iy + 3, 32, 38, 2, 2, 0.2f);
                    ui.Panel(ix, iy, 32, 36, textDark);
                    ui.Panel(ix + 6, iy + 8, 20, 3, bgCard);
                    ui.Panel(ix + 6, iy + 16, 16, 2, bgCard);
                }

                // Badge
                if (badge.Length > 10) badge = badge[..10];
                ui.SetCursor(cx + 8, drawCy + 6);
                ui.Text(badge, isBlueAsset ? accentBlueGlow : textDark);

                // Colored type indicator at bottom (3px thick line)
                ui.Panel(cx, drawCy + itemH - 3, itemW, 3, typeColor);

                // Filename
                ui.SetCursor(cx + 10, drawCy + itemH - 24);
                if (displayLabel.Length > 14) displayLabel = displayLabel[..12] + "..";
                ui.Text(displayLabel, (isBlueAsset || isCardSel) ? accentBlueGlow : textSecondary);

                // Hover tooltip
                if (ui.IsHovering(cx, drawCy, itemW, itemH))
                {
                    // Tooltip background - positioned above the card
                    float tooltipW = 160;
                    float tooltipH = 44;
                    float tooltipX = cx + (itemW - tooltipW) / 2;
                    float tooltipY = drawCy - tooltipH - 8;
                    
                    // Clamp to screen bounds
                    if (tooltipX < contentX + 8) tooltipX = contentX + 8;
                    if (tooltipX + tooltipW > contentX + contentW - 8) tooltipX = contentX + contentW - tooltipW - 8;
                    if (tooltipY < contentY + 8) tooltipY = cy + itemH + 8; // Show below if no room above
                    
                    // Shadow
                    ui.Shadow(tooltipX, tooltipY, tooltipW, tooltipH, 2, 4, 0.4f);
                    
                    // Background
                    ui.Panel(tooltipX, tooltipY, tooltipW, tooltipH, new System.Numerics.Vector4(0.12f, 0.12f, 0.14f, 0.98f));
                    
                    // Border with type color
                    ui.Panel(tooltipX, tooltipY, tooltipW, 2, typeColor);
                    ui.Panel(tooltipX, tooltipY + tooltipH - 1, tooltipW, 1, borderSubtle);
                    ui.Panel(tooltipX, tooltipY, 1, tooltipH, borderSubtle);
                    ui.Panel(tooltipX + tooltipW - 1, tooltipY, 1, tooltipH, borderSubtle);
                    
                    // Type indicator dot
                    ui.Panel(tooltipX + 8, tooltipY + 10, 6, 6, typeColor);
                    
                    // Text content
                    ui.SetCursor(tooltipX + 18, tooltipY + 8);
                    ui.Text(assetType, textPrimary);
                    ui.SetCursor(tooltipX + 18, tooltipY + 24);
                    ui.Text(assetSubtype, textMuted);
                }

                cx += itemW + gap;
                fileIdx++;
            }

            // Empty state - centered, polished
            if (dirs.Length == 0 && files.Length == 0)
            {
                float cx2 = contentX + contentW / 2;
                float cy2 = contentY + contentH / 2 - 20;

                // Large elegant folder - cleaner 3D design
                float fw = 100, fh = 80;
                float fx = cx2 - fw / 2;
                float fy = cy2 - fh / 2;
                
                // Drop shadow
                ui.Shadow(fx, fy, fw, fh, 4, 6, 0.3f);
                
                // Folder back (darker)
                float tabW = 35, tabH = 14;
                ui.Panel(fx + 20, fy - tabH + 4, tabW, tabH, accentFolderDark);
                ui.Panel(fx, fy, fw, fh - 10, accentFolderDark);
                
                // Folder front (main color)
                ui.Panel(fx, fy + 8, fw, fh - 18, accentFolder);
                
                // Top highlight/shine
                ui.Panel(fx, fy + 8, fw, 3, new System.Numerics.Vector4(1f, 0.95f, 0.85f, 0.6f));
                
                // Inner detail line
                ui.Panel(fx + 8, fy + 20, fw - 16, 2, new System.Numerics.Vector4(0.9f, 0.6f, 0.2f, 0.4f));
                ui.Panel(fx + 8, fy + 28, fw - 24, 2, new System.Numerics.Vector4(0.9f, 0.6f, 0.2f, 0.3f));

                // Text labels
                ui.SetCursor(cx2 - 80, cy2 + 50);
                ui.Text("This folder is empty", textSecondary);
                ui.SetCursor(cx2 - 105, cy2 + 72);
                ui.Text("Drop files or press Cmd+I to import", textDark);
            }
            
            ui.EndScrollArea("ContentBrowserGrid");
        }

        // ── STATUS BAR ─────────────────────────────────────────────────────
        float sy = rect.Y + rect.H - statusbarH;
        ui.Panel(rect.X, sy, rect.W, statusbarH, bgPanel);
        ui.Panel(rect.X, sy, rect.W, 1, borderSubtle);

        ui.SetCursor(rect.X + 16, sy + 6);
        ui.Text("BlueSky Engine  —  A game engine for the ease of Development", textMuted);
        
        // Standalone status indicator (multi-instance aware)
        if (_standalonePlay?.IsRunning ?? false)
        {
            var pids = _standalonePlay.ProcessIds;
            int pidCount = pids.Count();
            string statusText = pidCount > 1
                ? $"● Standalone ({pidCount} instances)"
                : $"● Standalone (PID: {_standalonePlay?.ProcessId})";
            ui.SetCursor(rect.X + rect.W - 280, sy + 6);
            ui.Text(statusText, accentGreen);
        }

        ui.SetCursor(rect.X + rect.W - 90, sy + 6);
        ui.Text("● Ready", accentGreen);
        
        // ── RIGHT-CLICK CONTEXT MENU ───────────────────────────────────────
        // Re-enable input for the context menu (modal)
        ui.InputEnabled = true;
        
        // Detect right-click in content area
        if (_input!.IsMouseButtonDown(MouseButton.Right) && 
            ui.IsHovering(contentX, contentY, contentW, contentH))
        {
            _showContextMenu = true;
            _contextMenuX = ui.MousePosition.X;
            _contextMenuY = ui.MousePosition.Y;
            _contextMenuPath = _currentBrowserDir ?? "";
        }
        
        // Draw context menu
        if (_showContextMenu)
        {
            DrawContextMenu(ui, _contextMenuX, _contextMenuY);
        }
    }

    private static void DrawTerrainBrushButton(EditorUI ui, float x, float y, string label, BrushMode mode, uint id)
    {
        bool active = _terrainBrushMode == mode;
        if (ui.ButtonEx(x, y, 60, 20, label,
            active ? EditorTheme.WithAlpha(EditorTheme.Green, 0.35f) : EditorTheme.Bg3,
            EditorTheme.AccentHover, EditorTheme.AccentDim,
            new System.Numerics.Vector4(0,0,0,0), active ? EditorTheme.Green : EditorTheme.TextPrimary, id))
        {
            _terrainBrushMode = mode;
        }
    }

    private static void DrawConsolePanel(EditorUI ui, DockRect rect)
    {
        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        // Toolbar header
        EditorChrome.Header(ui, rect.X, rect.Y, rect.W, EditorTheme.HeaderH, "Output Log", $"{_consoleLogs.Count} entries", EditorTheme.Green);

        // Clear button
        if (ui.ButtonEx(rect.X + rect.W - 76, rect.Y + 5, 64, 22, "Clear",
            EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim,
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            EditorTheme.TextSecondary, 9001))
        {
            _consoleLogs.Clear();
            Log("Console cleared");
        }

        // Command input bar at bottom
        float inputY = rect.Y + rect.H - 34;
        ui.RoundedPanel(rect.X + 8, inputY, rect.W - 16, 26, EditorTheme.Bg0, EditorTheme.InputRadius);
        EditorChrome.Stroke(ui, rect.X + 8, inputY, rect.W - 16, 26, EditorTheme.Border1);
        ui.SetCursor(rect.X + 18, inputY + 6);
        ui.Text("> Type command...", EditorTheme.TextDisabled);

        // Log output area
        float y = rect.Y + EditorTheme.HeaderH + 6;
        float maxY = inputY - 6;
        int lineHeight = 18;
        int maxLines = (int)((maxY - y) / lineHeight);

        int startIdx = System.Math.Max(0, _consoleLogs.Count - maxLines);
        for (int i = startIdx; i < _consoleLogs.Count && y < maxY; i++)
        {
            string log = _consoleLogs[i];
            var color = EditorTheme.TextSecondary;

            if (log.Contains("Error") || log.Contains("Failed") || log.Contains("\u2717"))
                color = EditorTheme.Red;
            else if (log.Contains("Warning") || log.Contains("\u26a0"))
                color = EditorTheme.Yellow;
            else if (log.Contains("Success") || log.Contains("\u2713") || log.Contains("Imported"))
                color = EditorTheme.Green;
            else if (log.Contains("Selected"))
                color = EditorTheme.Accent;

            // Alternating row tint for readability
            if (i % 2 == 0)
                ui.Panel(rect.X + 4, y - 2, rect.W - 8, lineHeight, EditorTheme.WithAlpha(EditorTheme.Bg0, 0.3f));

            string display = log;
            int maxChars = (int)(rect.W - 24) / 7;
            if (display.Length > maxChars)
                display = display[..(maxChars - 3)] + "...";

            ui.SetCursor(rect.X + 12, y);
            ui.Text(display, color);
            y += lineHeight;
        }

        if (_consoleLogs.Count == 0)
        {
            ui.SetCursor(rect.X + 12, rect.Y + 50);
            ui.Text("BlueSky Engine ready. Press Cmd+I to import assets.", EditorTheme.TextMuted);
        }
    }

}
