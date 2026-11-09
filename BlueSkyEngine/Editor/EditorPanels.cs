using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace BlueSky.Editor;

partial class Program
{
    private static uint _detailsCachedId = uint.MaxValue;
    private static Entity _detailsCachedEntity = default;
    private static bool _detailsCachedFound = false;

    internal static int _debugView = 0;
    internal static readonly string[] DebugViewNames = { "Lit", "Nrm", "Unl", "Shd", "AO", "Dir", "Env", "Sun" };
    internal static int _viewMode = 0;

    private static (float pitch, float yaw, float roll) QuaternionToEulerDegrees(BlueSky.Ramanujan.Quaternion q)
    {
        float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float pitch = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2f * (q.W * q.Y - q.Z * q.X);
        float yaw = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);

        float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float roll = MathF.Atan2(sinyCosp, cosyCosp);

        const float rad2Deg = 180f / MathF.PI;
        return (pitch * rad2Deg, yaw * rad2Deg, roll * rad2Deg);
    }

    // ── Dockable Panel Content Callbacks ──────────────────────────────

    private static void DrawViewportPanel(EditorUI ui, DockRect rect)
    {
        _lastViewportRect = rect;

        if (_showScriptEditor || _showImportDialog || _showRenameDialog)
        {
            ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg0);
            float cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2;
            ui.SetCursor(cx - 75, cy - 8);
            ui.Text("Viewport Paused", EditorTheme.TextDisabled);
            return;
        }

        if (_viewport == null)
        {
            ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg0);
            float cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2;
            ui.SetCursor(cx - 48, cy - 8);
            ui.Text("3D Viewport", EditorTheme.TextMuted);
            return;
        }

        // Drag preview
        if (_isDraggingAsset && _draggedAssetPath != null)
        {
            float mouseX = _input!.MousePosition.X;
            float mouseY = _input!.MousePosition.Y;
            bool insideViewport = (mouseX >= rect.X && mouseX <= rect.X + rect.W &&
                                   mouseY >= rect.Y && mouseY <= rect.Y + rect.H);

            string dragName = Path.GetFileName(_draggedAssetPath);
            float dragW = MathF.Max(120f, ui.MeasureText(dragName) + 24f);
            ui.RoundedPanel(mouseX + 14, mouseY + 14, dragW, 26, EditorTheme.WithAlpha(EditorTheme.Accent, 0.85f), EditorTheme.SmallRadius);
            ui.SetCursor(mouseX + 24, mouseY + 20);
            ui.Text(dragName, EditorTheme.TextPrimary);

            if (insideViewport)
                EditorChrome.Stroke(ui, rect.X + 2, rect.Y + 2, rect.W - 4, rect.H - 4, EditorTheme.Accent);
        }

        // ── Minimal Top-Right View Mode Pill (Lit / Wire / Unlit) ────
        string[] modes = { "Lit", "Wire", "Unlit" };
        float pillW = 156f, pillH = 28f;
        float pillX = rect.X + rect.W - pillW - 14f;
        float pillY = rect.Y + 12f;

        if (rect.W > 220f)
        {
            ui.RoundedPanel(pillX, pillY, pillW, pillH, EditorTheme.BgGlass, EditorTheme.PillRadius);
            float bx = pillX + 4f;
            float bw = (pillW - 16f) / 3f;
            for (int i = 0; i < modes.Length; i++)
            {
                bool active = (i == 0 && _viewMode == 0 && _debugView == 0)
                           || (i == 1 && _viewMode == 1)
                           || (i == 2 && _viewMode == 0 && _debugView == 2);

                if (active)
                    ui.RoundedPanel(bx, pillY + 3f, bw, pillH - 6f, EditorTheme.WithAlpha(EditorTheme.Accent, 0.28f), EditorTheme.PillRadius);

                if (ui.ClickableCard(bx, pillY + 3f, bw, pillH - 6f, 710u + (uint)i,
                    new Vector4(0, 0, 0, 0), EditorTheme.HoverBg, new Vector4(0, 0, 0, 0)))
                {
                    if (i == 0) { _viewMode = 0; _debugView = 0; }
                    else if (i == 1) { _viewMode = 1; }
                    else { _viewMode = 0; _debugView = 2; }
                }

                ui.TextCentered(bx, pillY + 3f, bw, pillH - 6f, modes[i], active ? EditorTheme.TextPrimary : EditorTheme.TextMuted);
                bx += bw + 4f;
            }
        }

        // ── Minimal Bottom-Left Camera Position ───────────────────────
        var camPos = _viewport.GetCameraPositionNumerics();
        string camStr = $"{camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1}";
        ui.SetCursor(rect.X + 16f, rect.Y + rect.H - 24f);
        ui.Text(camStr, EditorTheme.WithAlpha(EditorTheme.TextMuted, 0.65f));
    }

    private static void DrawOutlinerPanel(EditorUI ui, DockRect rect)
    {
        float inset = 12f;
        float iconCol = rect.X + inset + 6f;
        float nameCol = rect.X + inset + 24f;

        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        var entities = _world != null ? _world.GetAllEntities().ToList() : new System.Collections.Generic.List<Entity>();
        EditorChrome.Header(ui, rect.X, rect.Y, rect.W, EditorTheme.HeaderH, "Outliner", $"{entities.Count + 3}");

        float listY = rect.Y + EditorTheme.HeaderH + 10f;
        float scrollAreaY = listY;
        float footerH = 48f;
        float scrollAreaH = rect.H - (scrollAreaY - rect.Y) - footerH;

        if (_world != null)
        {
            string[] systemItems = { "DirectionalLight", "SkyAtmosphere", "PostProcessVolume" };
            Vector4[] systemColors = { EditorTheme.Yellow, EditorTheme.AccentCyan, EditorTheme.Purple };

            float rowStep = EditorTheme.RowH + 3f;
            float contentHeight = systemItems.Length * rowStep + 16f + entities.Count * rowStep + 20f;
            float scrollOffset = ui.BeginScrollArea("WorldOutliner", rect.X, scrollAreaY, rect.W, scrollAreaH, contentHeight);

            listY = scrollAreaY;

            for (int i = 0; i < systemItems.Length; i++)
            {
                uint id = EditorOnlyTagComponent.EditorSystemIdBase + (uint)i;
                bool isSel = _selectedEntityId == id;
                float drawY = listY - scrollOffset;

                if (ui.ClickableCard(rect.X + 8, drawY, rect.W - 16, EditorTheme.RowH,
                    id,
                    isSel ? EditorTheme.SelectionBg : new Vector4(0, 0, 0, 0),
                    EditorTheme.HoverBg,
                    EditorTheme.SelectionBg))
                {
                    _selectedEntityId = id;
                    Log($"Selected: {systemItems[i]}");
                }

                ui.Circle(iconCol + 3, drawY + EditorTheme.RowH * 0.5f, 3f, systemColors[i], filled: true);
                ui.SetCursor(nameCol, drawY + (EditorTheme.RowH - EditorTheme.CharHeight) * 0.5f);
                ui.Text(systemItems[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                listY += rowStep;
            }

            if (entities.Count > 0)
            {
                ui.Panel(rect.X + inset, listY + 4f - scrollOffset, rect.W - inset * 2, 1, EditorTheme.Border1);
                listY += 12f;
            }

            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                float drawY = listY - scrollOffset;
                bool visible = drawY + EditorTheme.RowH >= scrollAreaY && drawY <= scrollAreaY + scrollAreaH;
                if (!visible)
                {
                    listY += rowStep;
                    continue;
                }

                if (_world.TryGetComponent<CameraComponent>(entity, out _))
                    continue;

                uint id = (uint)entity.Id;
                bool isSel = _selectedEntityId == id;

                if (ui.ClickableCard(rect.X + 8, drawY, rect.W - 16, EditorTheme.RowH,
                    id,
                    isSel ? EditorTheme.SelectionBg : new Vector4(0, 0, 0, 0),
                    EditorTheme.HoverBg,
                    EditorTheme.SelectionBg))
                {
                    _selectedEntityId = id;
                    Log($"Selected Entity_{entity.Id}");
                }

                Vector4 entColor = EditorTheme.Accent;
                if (_world.HasComponent<TerrainComponent>(entity)) entColor = EditorTheme.Green;
                else if (_world.HasComponent<StaticMeshComponent>(entity)) entColor = EditorTheme.Purple;
                else if (_world.HasComponent<TeaScriptComponent>(entity)) entColor = EditorTheme.Teal;
                else if (_world.HasComponent<AudioSourceComponent>(entity)) entColor = EditorTheme.Yellow;

                ui.Circle(iconCol + 3, drawY + EditorTheme.RowH * 0.5f, 3f, entColor, filled: true);

                string displayName = $"Entity_{entity.Id}";
                if (_world.TryGetComponent<NameComponent>(entity, out var nameComp) && !string.IsNullOrWhiteSpace(nameComp.Name))
                    displayName = nameComp.Name;

                ui.SetCursor(nameCol, drawY + (EditorTheme.RowH - EditorTheme.CharHeight) * 0.5f);
                ui.Text(displayName, isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                listY += rowStep;
            }

            ui.EndScrollArea("WorldOutliner");

            // Minimal bottom action bar
            float footY = rect.Y + rect.H - footerH;
            ui.Panel(rect.X + inset, footY, rect.W - inset * 2, 1, EditorTheme.Border1);

            float halfW = (rect.W - inset * 2 - 10f) * 0.5f;
            float btnY = footY + 10f;

            if (AnimatedButton.Render(ui, rect.X + inset, btnY, halfW, 28f, "Actor", 9901,
                normalColor: EditorTheme.Bg2,
                hoverColor: EditorTheme.Bg3,
                pressColor: EditorTheme.AccentDim,
                textColor: EditorTheme.TextSecondary,
                iconKind: EditorUI.UIIcon.Plus))
            {
                CreateEmptyEntityCommand();
            }

            if (AnimatedButton.Render(ui, rect.X + inset + halfW + 10f, btnY, halfW, 28f, "Terrain", 9900,
                normalColor: EditorTheme.Bg2,
                hoverColor: EditorTheme.Bg3,
                pressColor: EditorTheme.AccentDim,
                textColor: EditorTheme.TextSecondary,
                iconKind: EditorUI.UIIcon.Plus))
            {
                CreateTerrain();
            }

            if (_input!.IsMouseButtonDown(MouseButton.Right) &&
                ui.IsHovering(rect.X, scrollAreaY, rect.W, scrollAreaH))
            {
                _showOutlinerContextMenu = true;
                _outlinerContextMenuX = ui.MousePosition.X;
                _outlinerContextMenuY = ui.MousePosition.Y;
            }

            if (_showOutlinerContextMenu)
                DrawOutlinerContextMenu(ui, _outlinerContextMenuX, _outlinerContextMenuY);
        }
    }

    private static void DrawDetailsPanel(EditorUI ui, DockRect rect)
    {
        uint versionBefore = _world?.Version ?? 0;
        DrawDetailsPanelContent(ui, rect);

        // The inspector only writes to the world in response to user input (sliders, toggles,
        // text fields, add/remove component buttons), so any change here is a scene edit.
        if (_world != null && !_isPlaying && _world.Version != versionBefore)
            _sceneDirty = true;
    }

    private static void DrawDetailsPanelContent(EditorUI ui, DockRect rect)
    {
        float inset = 14f;
        float labelCol = rect.X + inset;
        float valueCol = rect.X + EditorTheme.PropLabelW + inset;

        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);
        EditorChrome.Header(ui, rect.X, rect.Y, rect.W, EditorTheme.HeaderH, "Inspector",
            _selectedEntityId > 0 ? $"#{_selectedEntityId}" : "");

        // Clean empty state when nothing is selected
        if (_world == null || _selectedEntityId == 0)
        {
            float cy = rect.Y + rect.H * 0.45f;
            ui.TextCentered(rect.X, cy, rect.W, 20f, "Select an actor to inspect", EditorTheme.TextMuted);
            return;
        }

        bool isSystemActor = _selectedEntityId >= EditorOnlyTagComponent.EditorSystemIdBase;
        string entityName = "Actor";
        Vector3 posVec = Vector3.Zero;
        (float rx, float ry, float rz) rotEuler = (0f, 0f, 0f);
        Vector3 scaleVec = Vector3.One;
        bool hasMesh = false;
        Entity selectedEntity = default;
        bool hasSelectedEntity = false;

        if (isSystemActor)
        {
            string[] sysNames = { "DirectionalLight", "SkyAtmosphere", "PostProcessVolume" };
            int idx = (int)(_selectedEntityId - EditorOnlyTagComponent.EditorSystemIdBase);
            if (idx >= 0 && idx < sysNames.Length)
                entityName = sysNames[idx];
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
                if (_world.IsEntityValid(selectedEntity))
                {
                    hasSelectedEntity = true;
                    entityName = $"Entity_{selectedEntity.Id}";
                    if (_world.TryGetComponent<NameComponent>(selectedEntity, out var nmComp) && !string.IsNullOrWhiteSpace(nmComp.Name))
                        entityName = nmComp.Name;

                    if (_world.TryGetComponent<TransformComponent>(selectedEntity, out var transform))
                    {
                        posVec = new Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
                        scaleVec = new Vector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z);
                        rotEuler = QuaternionToEulerDegrees(transform.Rotation);
                    }
                    hasMesh = _world.TryGetComponent<StaticMeshComponent>(selectedEntity, out _);
                }
                else
                {
                    _detailsCachedFound = false;
                }
            }
        }

        float scrollAreaY = rect.Y + EditorTheme.HeaderH;
        float scrollAreaH = rect.H - EditorTheme.HeaderH;
        float scrollOffset = ui.BeginScrollArea("DetailsPanel", rect.X, scrollAreaY, rect.W, scrollAreaH, 1600f);

        float y = scrollAreaY + 14f;

        // Minimal actor title row
        ui.Circle(labelCol + 5f, y - scrollOffset + 8f, 4f, EditorTheme.Accent, true);
        ui.SetCursor(labelCol + 18f, y - scrollOffset + 1f);
        ui.Text(entityName, EditorTheme.TextPrimary);
        y += 32f;

        // If a System Actor is selected, show Environment & Vectra controls only
        if (isSystemActor)
        {
            EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Environment", EditorTheme.AccentCyan);
            y += EditorTheme.SectionH + 12f;

            bool studioSky = BlueSky.Meridian.GlobalEnvironment.SkyPreset > 0.5f;
            ui.SetCursor(labelCol, (y + 2) - scrollOffset);
            ui.Text("Studio Sky", EditorTheme.TextSecondary);
            if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, studioSky, EditorTheme.AccentCyan, 9759))
                ToggleStudioSkyCommand();
            y += 34f;

            if (_vectraDemoEmitter != null)
            {
                EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Vectra Particles", EditorTheme.AccentCyan);
                y += EditorTheme.SectionH + 12f;

                var vcfg = _vectraDemoEmitter.Config;
                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Enabled", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, _vectraDemoEmitter.Enabled, EditorTheme.Green, 9760))
                    _vectraDemoEmitter.Enabled = !_vectraDemoEmitter.Enabled;
                y += 30f;

                if (_vectraDemoEmitter.Enabled)
                {
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Rate", EditorTheme.TextSecondary);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float vrate = vcfg.RatePerSec;
                    if (ui.Slider(ref vrate, 0f, 60f, 110, 14)) { vcfg.RatePerSec = vrate; _vectraDemoEmitter.Config = vcfg; }
                    y += 28f;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Size", EditorTheme.TextSecondary);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float vsize = vcfg.SizeMax;
                    if (ui.Slider(ref vsize, 0.5f, 5f, 110, 14)) { vcfg.SizeMax = vsize; vcfg.SizeMin = vsize * 0.6f; _vectraDemoEmitter.Config = vcfg; }
                    y += 28f;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Opacity", EditorTheme.TextSecondary);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float valpha = vcfg.Alpha;
                    if (ui.Slider(ref valpha, 0.02f, 0.6f, 110, 14)) { vcfg.Alpha = valpha; _vectraDemoEmitter.Config = vcfg; }
                    y += 28f;
                }
            }

            ui.EndScrollArea("DetailsPanel");
            return;
        }

        // ── Transform Section ─────────────────────────────────────────
        EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Transform", EditorTheme.Accent);
        y += EditorTheme.SectionH + 12f;

        float transformInputW = MathF.Max(120f, rect.W - (valueCol - rect.X) - inset);

        ui.SetCursor(labelCol, y - scrollOffset + 5);
        ui.Text("Location", EditorTheme.TextSecondary);
        EditorChrome.Vector3Row(ui, valueCol, y - scrollOffset, transformInputW, posVec.X, posVec.Y, posVec.Z);
        y += 32f;

        ui.SetCursor(labelCol, y - scrollOffset + 5);
        ui.Text("Rotation", EditorTheme.TextSecondary);
        EditorChrome.Vector3Row(ui, valueCol, y - scrollOffset, transformInputW, rotEuler.rx, rotEuler.ry, rotEuler.rz);
        y += 32f;

        ui.SetCursor(labelCol, y - scrollOffset + 5);
        ui.Text("Scale", EditorTheme.TextSecondary);
        EditorChrome.Vector3Row(ui, valueCol, y - scrollOffset, transformInputW, scaleVec.X, scaleVec.Y, scaleVec.Z);
        y += 38f;

        // ── Static Mesh (only expanded when present) ──────────────────
        if (hasSelectedEntity && _world.TryGetComponent<StaticMeshComponent>(selectedEntity, out var meshComp))
        {
            EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Mesh", EditorTheme.Purple);
            y += EditorTheme.SectionH + 12f;

            string meshName = string.IsNullOrEmpty(meshComp.MeshAssetId) ? "None" : Path.GetFileNameWithoutExtension(meshComp.MeshAssetId);
            ui.SetCursor(labelCol, y - scrollOffset + 4);
            ui.Text("Asset", EditorTheme.TextSecondary);
            ui.SetCursor(valueCol, y - scrollOffset + 4);
            ui.Text(meshName, EditorTheme.TextPrimary);
            y += 28f;

            if (!string.IsNullOrEmpty(meshComp.MeshAssetId))
            {
                if (AnimatedButton.Render(ui, labelCol, y - scrollOffset, rect.W - inset * 2, 26, "Edit Mesh", 9702,
                    normalColor: EditorTheme.Bg2,
                    hoverColor: EditorTheme.Bg3,
                    pressColor: EditorTheme.Purple,
                    textColor: EditorTheme.Purple,
                    iconKind: EditorUI.UIIcon.Settings))
                {
                    OpenStaticMeshEditor(meshComp.MeshAssetId);
                }
                y += 38f;
            }
        }

        // ── Skeletal Mesh (only when present) ─────────────────────────
        if (hasSelectedEntity && _world.TryGetComponent<SkeletalMeshComponent>(selectedEntity, out var skelComp))
        {
            EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Skeletal Mesh", EditorTheme.AccentCyan);
            y += EditorTheme.SectionH + 12f;

            string skelMeshName = string.IsNullOrEmpty(skelComp.MeshAssetPath) ? "None" : Path.GetFileNameWithoutExtension(skelComp.MeshAssetPath);
            ui.SetCursor(labelCol, y - scrollOffset + 4);
            ui.Text("Asset", EditorTheme.TextSecondary);
            ui.SetCursor(valueCol, y - scrollOffset + 4);
            ui.Text(skelMeshName, EditorTheme.TextPrimary);
            y += 28f;

            if (!string.IsNullOrEmpty(skelComp.MeshAssetPath))
            {
                if (AnimatedButton.Render(ui, labelCol, y - scrollOffset, rect.W - inset * 2, 26, "Edit Skeleton", 9703,
                    normalColor: EditorTheme.Bg2,
                    hoverColor: EditorTheme.Bg3,
                    pressColor: EditorTheme.AccentCyan,
                    textColor: EditorTheme.AccentCyan,
                    iconKind: EditorUI.UIIcon.Settings))
                {
                    OpenSkeletalMeshEditor(skelComp.MeshAssetPath);
                }
                y += 38f;
            }
        }

        // ── Audio Source (only when present) ─────────────────────────
        if (hasSelectedEntity && _world.TryGetComponent<AudioSourceComponent>(selectedEntity, out var audioComp))
        {
            EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Audio Source", EditorTheme.Yellow);

            bool removeAudio = ui.ButtonEx(rect.X + rect.W - inset - 24, (y + 2) - scrollOffset, 22, 20, "×",
                new Vector4(0, 0, 0, 0), EditorTheme.Red, EditorTheme.Red,
                new Vector4(0, 0, 0, 0), EditorTheme.TextMuted, 9811);
            y += EditorTheme.SectionH + 12f;

            if (removeAudio)
            {
                _world.RemoveComponent<AudioSourceComponent>(selectedEntity);
            }
            else
            {
                string clipPath = audioComp.ClipPath;
                ui.SetCursor(labelCol, (y + 3) - scrollOffset);
                ui.Text("Clip", EditorTheme.TextSecondary);
                float clipFieldW = MathF.Max(80f, rect.W - (valueCol - rect.X) - inset - 64f);
                ui.SetCursor(valueCol, y - scrollOffset);
                if (ui.TextField(ref clipPath, clipFieldW, 24))
                {
                    audioComp.ClipPath = clipPath;
                    _world.AddComponent(selectedEntity, audioComp);
                }

                if (AnimatedButton.Render(ui, rect.X + rect.W - inset - 56, y - scrollOffset, 56, 24, "Preview", 9812,
                    normalColor: EditorTheme.Bg2,
                    hoverColor: EditorTheme.Bg3,
                    pressColor: EditorTheme.Yellow,
                    textColor: EditorTheme.Yellow,
                    iconKind: EditorUI.UIIcon.Play))
                {
                    PreviewAudioFile(audioComp.ClipPath);
                }
                y += 32f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Volume", EditorTheme.TextSecondary);
                ui.SetCursor(valueCol, y - scrollOffset);
                float audioVolume = audioComp.Volume;
                if (ui.Slider(ref audioVolume, 0f, 2f, 110, 14))
                {
                    audioComp.Volume = audioVolume;
                    _world.AddComponent(selectedEntity, audioComp);
                }
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Pitch", EditorTheme.TextSecondary);
                ui.SetCursor(valueCol, y - scrollOffset);
                float audioPitch = audioComp.Pitch;
                if (ui.Slider(ref audioPitch, 0.25f, 3f, 110, 14))
                {
                    audioComp.Pitch = audioPitch;
                    _world.AddComponent(selectedEntity, audioComp);
                }
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Loop", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, audioComp.Loop, EditorTheme.Yellow, 9813))
                {
                    audioComp.Loop = !audioComp.Loop;
                    _world.AddComponent(selectedEntity, audioComp);
                }
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("3D Spatial", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, audioComp.Is3D, EditorTheme.AccentCyan, 9814))
                {
                    audioComp.Is3D = !audioComp.Is3D;
                    _world.AddComponent(selectedEntity, audioComp);
                }
                y += 28f;

                if (audioComp.Is3D)
                {
                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Min Dist", EditorTheme.TextSecondary);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float minDist = audioComp.MinDistance;
                    if (ui.Slider(ref minDist, 0.01f, 25f, 110, 14))
                    {
                        audioComp.MinDistance = minDist;
                        audioComp.MaxDistance = MathF.Max(minDist, audioComp.MaxDistance);
                        _world.AddComponent(selectedEntity, audioComp);
                    }
                    y += 28f;

                    ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Max Dist", EditorTheme.TextSecondary);
                    ui.SetCursor(valueCol, y - scrollOffset);
                    float maxDist = audioComp.MaxDistance;
                    if (ui.Slider(ref maxDist, 1f, 500f, 110, 14))
                    {
                        audioComp.MaxDistance = MathF.Max(audioComp.MinDistance, maxDist);
                        _world.AddComponent(selectedEntity, audioComp);
                    }
                    y += 28f;
                }

                if (IsEchoAssetPath(audioComp.ClipPath))
                {
                    if (AnimatedButton.Render(ui, labelCol, y - scrollOffset, 132, 24, "Edit in Echo", 9816,
                        normalColor: EditorTheme.Bg2,
                        hoverColor: EditorTheme.Bg3,
                        pressColor: EditorTheme.AccentCyan,
                        textColor: EditorTheme.AccentCyan))
                    {
                        OpenEchoAsset(audioComp.ClipPath);
                    }
                    y += 30f;
                }

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Play On Start", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, audioComp.PlayOnStart, EditorTheme.Green, 9815))
                {
                    audioComp.PlayOnStart = !audioComp.PlayOnStart;
                    _world.AddComponent(selectedEntity, audioComp);
                }
                y += 36f;
            }
        }

        // ── TeaScript (only when present) ─────────────────────────────
        if (hasSelectedEntity && _world.TryGetComponent<TeaScriptComponent>(selectedEntity, out var scriptComp))
        {
            EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "TeaScript", EditorTheme.Green);
            y += EditorTheme.SectionH + 12f;

            string scriptPath = string.IsNullOrEmpty(scriptComp.ScriptAssetId) ? "None" : Path.GetFileName(scriptComp.ScriptAssetId);
            ui.SetCursor(labelCol, (y + 3) - scrollOffset);
            ui.Text("Script", EditorTheme.TextSecondary);
            ui.SetCursor(valueCol, (y + 3) - scrollOffset);
            ui.Text(scriptPath, EditorTheme.Green);
            y += 28f;

            ui.SetCursor(labelCol, (y + 2) - scrollOffset);
            ui.Text("Runtime UI", EditorTheme.TextSecondary);
            if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, scriptComp.AllowRuntimeUI, EditorTheme.Green, 9701))
            {
                ref var script = ref _world.GetComponent<TeaScriptComponent>(selectedEntity);
                script.AllowRuntimeUI = !script.AllowRuntimeUI;
            }
            y += 28f;

            ui.SetCursor(labelCol, (y + 2) - scrollOffset);
            ui.Text("Block Input", EditorTheme.TextSecondary);
            if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, scriptComp.BlockRuntimeInput, EditorTheme.Orange, 9702))
            {
                ref var script = ref _world.GetComponent<TeaScriptComponent>(selectedEntity);
                script.BlockRuntimeInput = !script.BlockRuntimeInput;
            }
            y += 36f;
        }

        // ── Physics & Optional Components ─────────────────────────────
        bool hasPhysics = false;
        if (hasSelectedEntity)
        {
            var entity = selectedEntity;
            if (_world.TryGetComponent<PhysicsComponent>(entity, out var physComp))
            {
                hasPhysics = true;
                EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Physics", EditorTheme.Orange);
                if (ui.ButtonEx(rect.X + rect.W - inset - 24, (y + 2) - scrollOffset, 22, 20, "×",
                    new Vector4(0, 0, 0, 0), EditorTheme.Red, EditorTheme.Red,
                    new Vector4(0, 0, 0, 0), EditorTheme.TextMuted, 9801))
                {
                    _world.RemoveComponent<PhysicsComponent>(entity);
                }
                y += EditorTheme.SectionH + 12f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Mass", EditorTheme.TextSecondary);
                ui.SetCursor(valueCol, y - scrollOffset);
                float mass = physComp.Mass;
                if (ui.Slider(ref mass, 0.1f, 10000f, 110, 14))
                {
                    physComp.Mass = mass;
                    _world.AddComponent(entity, physComp);
                }
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Use Gravity", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, physComp.UseGravity, EditorTheme.Green, 9802))
                {
                    physComp.UseGravity = !physComp.UseGravity;
                    _world.AddComponent(entity, physComp);
                }
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Kinematic", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, physComp.IsKinematic, EditorTheme.Green, 9806))
                {
                    physComp.IsKinematic = !physComp.IsKinematic;
                    _world.AddComponent(entity, physComp);
                }
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Collider", EditorTheme.TextSecondary);
                float typeX = valueCol;
                if (ui.ButtonEx(typeX, y - scrollOffset, 38, 20, "Box",
                    physComp.Type == ColliderType.Box ? EditorTheme.Teal : EditorTheme.Bg2,
                    EditorTheme.Bg3, EditorTheme.AccentDim,
                    new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 9820))
                {
                    physComp.Type = ColliderType.Box;
                    _world.AddComponent(entity, physComp);
                }
                if (ui.ButtonEx(typeX + 42, y - scrollOffset, 50, 20, "Sphere",
                    physComp.Type == ColliderType.Sphere ? EditorTheme.Teal : EditorTheme.Bg2,
                    EditorTheme.Bg3, EditorTheme.AccentDim,
                    new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 9821))
                {
                    physComp.Type = ColliderType.Sphere;
                    _world.AddComponent(entity, physComp);
                }
                if (ui.ButtonEx(typeX + 96, y - scrollOffset, 56, 20, "Capsule",
                    physComp.Type == ColliderType.Capsule ? EditorTheme.Teal : EditorTheme.Bg2,
                    EditorTheme.Bg3, EditorTheme.AccentDim,
                    new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 9822))
                {
                    physComp.Type = ColliderType.Capsule;
                    _world.AddComponent(entity, physComp);
                }
                y += 34f;
            }

            // Terrain section
            if (_world.TryGetComponent<TerrainComponent>(entity, out var terrainComp))
            {
                EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Terrain", EditorTheme.Green);
                y += EditorTheme.SectionH + 12f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Sculpt Mode", EditorTheme.TextSecondary);
                if (ui.ToggleSwitch(valueCol, y - scrollOffset, 40, 20, _terrainEditMode, EditorTheme.Green, 9866))
                    _terrainEditMode = !_terrainEditMode;
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Radius", EditorTheme.TextSecondary);
                ui.SetCursor(valueCol, y - scrollOffset);
                ui.Slider(ref _terrainBrushRadius, 1f, 35f, 110, 14);
                y += 28f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Strength", EditorTheme.TextSecondary);
                ui.SetCursor(valueCol, y - scrollOffset);
                ui.Slider(ref _terrainBrushStrength, 0.05f, 1.5f, 110, 14);
                y += 30f;

                float brushX = labelCol;
                DrawTerrainBrushButton(ui, brushX, y - scrollOffset, "Raise", BrushMode.Raise, 9831);
                DrawTerrainBrushButton(ui, brushX + 66, y - scrollOffset, "Lower", BrushMode.Lower, 9832);
                DrawTerrainBrushButton(ui, brushX + 132, y - scrollOffset, "Smooth", BrushMode.Smooth, 9833);
                y += 34f;
            }

            // NetworkManager section
            if (_world.TryGetComponent<NetworkManagerComponent>(entity, out var nmComp))
            {
                EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Network Manager", EditorTheme.AccentCyan);
                y += EditorTheme.SectionH + 12f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Player Prefab", EditorTheme.TextSecondary);
                string prefabPath = NetworkManagerStorage.GetPrefabPath(nmComp.StorageIndex);
                string prefabName = string.IsNullOrEmpty(prefabPath) ? "Drop .bseprefab" : Path.GetFileNameWithoutExtension(prefabPath);
                float pfFieldX = valueCol;
                float pfFieldY = (y - 2) - scrollOffset;
                float pfFieldW = rect.W - (valueCol - rect.X) - inset;
                float pfFieldH = 24;

                ui.RoundedPanel(pfFieldX, pfFieldY, pfFieldW, pfFieldH, EditorTheme.Bg0, EditorTheme.InputRadius);
                ui.SetCursor(valueCol + 8, (y + 3) - scrollOffset);
                ui.Text(prefabName, !string.IsNullOrEmpty(prefabPath) ? EditorTheme.AccentCyan : EditorTheme.TextDisabled);
                _nmPrefabDropZone = new Vector4(pfFieldX, pfFieldY, pfFieldW, pfFieldH);
                y += 34f;
            }

            // SpawnPoint section
            if (_world.TryGetComponent<SpawnPointComponent>(entity, out var spComp))
            {
                EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Spawn Point", EditorTheme.Yellow);
                y += EditorTheme.SectionH + 12f;

                ui.SetCursor(labelCol, (y + 2) - scrollOffset); ui.Text("Index", EditorTheme.TextSecondary);
                ui.SetCursor(valueCol, y - scrollOffset);
                float spIndex = spComp.Index;
                if (ui.Slider(ref spIndex, 0f, 15f, 110, 14))
                {
                    spComp.Index = (int)spIndex;
                    _world.AddComponent(entity, spComp);
                }
                y += 34f;
            }

            // ── Compact 2×2 Add Component Grid ────────────────────────
            EditorChrome.SectionTitle(ui, rect.X + inset, y - scrollOffset, rect.W - inset * 2, "Add Component", EditorTheme.TextMuted);
            y += EditorTheme.SectionH + 12f;

            float colW = (rect.W - inset * 2 - 10f) * 0.5f;
            float leftX = rect.X + inset;
            float rightX = leftX + colW + 10f;

            if (!hasPhysics)
            {
                if (AnimatedButton.Render(ui, leftX, y - scrollOffset, colW, 28, "Physics", 9804,
                    normalColor: EditorTheme.Bg2, hoverColor: EditorTheme.Bg3, pressColor: EditorTheme.Orange,
                    textColor: EditorTheme.TextSecondary, iconKind: EditorUI.UIIcon.Plus))
                {
                    _world.AddComponent(entity, new PhysicsComponent());
                }
            }

            if (!_world.TryGetComponent<CarControllerComponent>(entity, out _))
            {
                if (AnimatedButton.Render(ui, rightX, y - scrollOffset, colW, 28, "Vehicle", 9806,
                    normalColor: EditorTheme.Bg2, hoverColor: EditorTheme.Bg3, pressColor: EditorTheme.Purple,
                    textColor: EditorTheme.TextSecondary, iconKind: EditorUI.UIIcon.Plus))
                {
                    _carControllerSystem?.AddCarController(entity);
                }
            }
            y += 36f;

            if (!_world.TryGetComponent<NetworkManagerComponent>(entity, out _))
            {
                if (AnimatedButton.Render(ui, leftX, y - scrollOffset, colW, 28, "Network", 9840,
                    normalColor: EditorTheme.Bg2, hoverColor: EditorTheme.Bg3, pressColor: EditorTheme.AccentCyan,
                    textColor: EditorTheme.TextSecondary, iconKind: EditorUI.UIIcon.Plus))
                {
                    int storIdx = NetworkManagerStorage.Allocate("");
                    _world.AddComponent(entity, new NetworkManagerComponent { StorageIndex = storIdx, MaxPlayers = 8, ReplicationInterval = 0.1f });
                }
            }

            if (!_world.TryGetComponent<SpawnPointComponent>(entity, out _))
            {
                if (AnimatedButton.Render(ui, rightX, y - scrollOffset, colW, 28, "Spawn Point", 9841,
                    normalColor: EditorTheme.Bg2, hoverColor: EditorTheme.Bg3, pressColor: EditorTheme.Yellow,
                    textColor: EditorTheme.TextSecondary, iconKind: EditorUI.UIIcon.Plus))
                {
                    _world.AddComponent(entity, new SpawnPointComponent { Index = 0, OccupantEntityId = -1 });
                }
            }
            y += 36f;

            if (!_world.TryGetComponent<AudioSourceComponent>(entity, out _))
            {
                if (AnimatedButton.Render(ui, leftX, y - scrollOffset, colW, 28, "Audio", 9842,
                    normalColor: EditorTheme.Bg2, hoverColor: EditorTheme.Bg3, pressColor: EditorTheme.Yellow,
                    textColor: EditorTheme.TextSecondary, iconKind: EditorUI.UIIcon.Plus))
                {
                    _world.AddComponent(entity, new AudioSourceComponent("", 1.0f, loop: false, is3D: true) { PlayOnStart = true });
                    _detailsCachedId = uint.MaxValue;
                }
            }
            y += 36f;
        }

        ui.EndScrollArea("DetailsPanel");
    }

    private static void DrawContentBrowserPanel(EditorUI ui, DockRect rect)
    {
        float toolbarH = 36f;
        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        // Minimal top bar inside Content Browser (no extra left sidebar clutter!)
        ui.Panel(rect.X, rect.Y, rect.W, toolbarH, EditorTheme.Bg1);
        ui.Panel(rect.X + 12, rect.Y + toolbarH - 1, rect.W - 24, 1, EditorTheme.Border1);

        float bcY = rect.Y + (toolbarH - EditorTheme.CharHeight) * 0.5f;
        ui.Icon(EditorUI.UIIcon.Folder, rect.X + 16, rect.Y + (toolbarH - 14) * 0.5f, 14, EditorTheme.FolderFront);
        string folderTitle = Path.GetFileName(string.IsNullOrEmpty(_currentBrowserDir) ? (ProjectManager.AssetsDir ?? "Assets") : _currentBrowserDir);
        ui.SetCursor(rect.X + 38, bcY);
        ui.Text(folderTitle, EditorTheme.TextPrimary);

        // Clean Import button on the right
        float importX = rect.X + rect.W - 96, importY = rect.Y + (toolbarH - 24) * 0.5f;
        if (AnimatedButton.Render(ui, importX, importY, 82, 24, "Import", 8001,
            normalColor: EditorTheme.Bg2,
            hoverColor: EditorTheme.Bg3,
            pressColor: EditorTheme.AccentDim,
            textColor: EditorTheme.TextPrimary,
            iconKind: EditorUI.UIIcon.Import))
        {
            ImportFilesDialog();
        }

        float contentX = rect.X;
        float contentY = rect.Y + toolbarH;
        float contentW = rect.W;
        float contentH = rect.H - toolbarH;

        float gridY = contentY + 16f;
        float itemW = 96, itemH = 100;
        float gap = 16;

        if (string.IsNullOrEmpty(_currentBrowserDir))
            _currentBrowserDir = ProjectManager.AssetsDir ?? "";

        var browserListing = string.IsNullOrEmpty(_currentBrowserDir)
            ? null
            : Assets.GetContentBrowserListing(_currentBrowserDir);

        if (browserListing == null)
        {
            ui.SetCursor(contentX + 20, gridY + 12);
            ui.Text("Open a project to browse assets", EditorTheme.TextMuted);
        }
        else if (browserListing.Error != null)
        {
            ui.SetCursor(contentX + 20, gridY + 12);
            ui.Text($"Error: {browserListing.Error}", EditorTheme.Red);
        }
        else
        {
            string[] dirs = browserListing.Directories;
            string[] files = browserListing.Files;

            float cx = contentX + 20;
            float cy = gridY;

            int itemsPerRow = Math.Max(1, (int)((contentW - 40) / (itemW + gap)));
            bool hasBack = (_currentBrowserDir != ProjectManager.AssetsDir && ProjectManager.AssetsDir != null);
            int totalItems = (hasBack ? 1 : 0) + dirs.Length + files.Length;
            int rows = (int)Math.Ceiling((double)totalItems / itemsPerRow);
            float contentHeight = rows * (itemH + gap) + 24;

            float scrollOffset = ui.BeginScrollArea("ContentBrowserGrid", contentX, contentY, contentW, contentH, contentHeight);

            if (hasBack)
            {
                float drawCy = cy - scrollOffset;
                if (drawCy + itemH >= contentY && drawCy <= contentY + contentH)
                {
                    uint backId = 4999u;
                    if (ui.ClickableCard(cx, drawCy, itemW, itemH, backId, EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.SelectionBg))
                    {
                        var parentDir = Directory.GetParent(_currentBrowserDir)?.FullName;
                        if (parentDir != null && parentDir.StartsWith(ProjectManager.AssetsDir ?? ""))
                            _currentBrowserDir = parentDir;
                        else
                            _currentBrowserDir = ProjectManager.AssetsDir ?? "";
                        _selectedAssetIndex = -1;
                    }
                    ui.Icon(EditorUI.UIIcon.ChevronLeft, cx + itemW * 0.5f - 8f, drawCy + 28f, 16f, EditorTheme.TextSecondary);
                    ui.TextCentered(cx, drawCy + itemH - 26, itemW, 18, "Back", EditorTheme.TextSecondary);
                }
                cx += itemW + gap;
            }

            // Folders
            int folderIdx = 0;
            foreach (var dir in dirs)
            {
                if (cx + itemW > contentX + contentW - 20)
                { cx = contentX + 20; cy += itemH + gap; }

                float drawCy = cy - scrollOffset;
                if (drawCy + itemH < contentY || drawCy > contentY + contentH)
                {
                    cx += itemW + gap;
                    folderIdx++;
                    continue;
                }

                uint cardId = 5000u + (uint)folderIdx;
                bool isCardSel = _selectedAssetIndex == (int)cardId;

                if (ui.ClickableCard(cx, drawCy, itemW, itemH, cardId,
                    isCardSel ? EditorTheme.SelectionBg : EditorTheme.Bg2,
                    EditorTheme.Bg3,
                    EditorTheme.SelectionBg))
                {
                    _selectedAssetIndex = (int)cardId;
                    double now = ui.Time;
                    if (_doubleClickTarget == cardId && (now - _lastClickTime) < 0.3)
                    {
                        _currentBrowserDir = dir;
                        _selectedAssetIndex = -1;
                        _doubleClickTarget = 0;
                    }
                    else
                    {
                        _doubleClickTarget = cardId;
                        _lastClickTime = now;
                    }
                }

                float ix = cx + (itemW - 36) / 2;
                float iy = drawCy + 22;
                ui.RoundedPanel(ix, iy + 4, 36, 24, EditorTheme.FolderFront, 4f);
                ui.Panel(ix + 4, iy, 14, 5, EditorTheme.FolderBack);

                string name = Path.GetFileName(dir);
                if (name.Length > 12) name = name[..10] + "..";
                ui.TextCentered(cx, drawCy + itemH - 26, itemW, 18, name, isCardSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                cx += itemW + gap;
                folderIdx++;
            }

            // Files
            int fileIdx = 0;
            foreach (var file in files)
            {
                if (cx + itemW > contentX + contentW - 20)
                { cx = contentX + 20; cy += itemH + gap; }

                float drawCy = cy - scrollOffset;
                if (drawCy + itemH < contentY || drawCy > contentY + contentH)
                {
                    cx += itemW + gap;
                    fileIdx++;
                    continue;
                }

                string ext = Path.GetExtension(file).ToLower();
                bool isBlueAsset = ext == ".blueskyasset";
                bool isTeaScript = ext == ".tea";
                bool isBsphy = ext == ".bsphy";
                bool isStratamat = ext == ".stratamat";
                bool isEchoSource = ext == ".echosource";
                bool isEchoSuper = ext == ".echosuper";
                bool isAudio = ext == ".wav" || ext == ".ogg" || ext == ".mp3" || ext == ".flac" ||
                               isEchoSource || isEchoSuper;

                uint cardId = 6000u + (uint)fileIdx;
                bool isCardSel = _selectedAssetIndex == (int)cardId;

                if (ui.ClickableCard(cx, drawCy, itemW, itemH, cardId,
                    isCardSel ? EditorTheme.SelectionBg : EditorTheme.Bg2,
                    EditorTheme.Bg3,
                    EditorTheme.SelectionBg))
                {
                    _selectedAssetIndex = (int)cardId;
                    _dragPos = ui.MousePosition;

                    double now = ui.Time;
                    if (_doubleClickTarget == cardId && (now - _lastClickTime) < 0.3)
                    {
                        if (isTeaScript) OpenScriptEditor(file);
                        else if (isStratamat) OpenStrataMaterial(file);
                        else if (isEchoSource || isEchoSuper) OpenEchoAsset(file);
                        else if (isAudio) PreviewAudioFile(file);
                        else if (isBlueAsset)
                        {
                            var header = BlueSky.Core.Assets.BlueAsset.LoadHeader(file);
                            if (header != null)
                            {
                                if (header.Type == BlueSky.Core.Assets.AssetType.StaticMesh || header.Type == BlueSky.Core.Assets.AssetType.Mesh)
                                    OpenStaticMeshEditor(file);
                                else if (header.Type == BlueSky.Core.Assets.AssetType.SkeletalMesh)
                                    OpenSkeletalMeshEditor(file);
                            }
                        }
                    }
                    else
                    {
                        _doubleClickTarget = cardId;
                        _lastClickTime = now;
                    }
                }

                if (ui.IsHovering(cx, drawCy, itemW, itemH) && ui.IsMouseDown)
                {
                    if (_selectedAssetIndex != (int)cardId && !_isDraggingAsset)
                    {
                        _selectedAssetIndex = (int)cardId;
                        _dragPos = ui.MousePosition;
                    }

                    if (Vector2.Distance(ui.MousePosition, _dragPos) > 5 && !_isDraggingAsset)
                    {
                        bool isPrefab = ext == ".bseprefab";
                        if (isBlueAsset || isTeaScript || isPrefab || isBsphy || isStratamat || isAudio)
                        {
                            _isDraggingAsset = true;
                            _draggedAssetPath = file;
                        }
                    }
                }

                string displayLabel = Path.GetFileNameWithoutExtension(file);
                Vector4 dotColor = EditorTheme.Accent;
                if (isTeaScript) dotColor = EditorTheme.Green;
                else if (isStratamat || isBsphy) dotColor = EditorTheme.Orange;
                else if (isEchoSuper) dotColor = EditorTheme.AccentCyan;
                else if (isAudio) dotColor = EditorTheme.Yellow;
                else if (ext == ".png" || ext == ".jpg") dotColor = EditorTheme.Purple;

                if (isBlueAsset)
                {
                    var header = Assets.GetAssetHeader(file);
                    if (header != null && !string.IsNullOrEmpty(header.AssetName))
                        displayLabel = header.AssetName;
                }

                // Minimal centered icon glyph
                float midX = cx + itemW * 0.5f;
                float midY = drawCy + 36f;
                ui.Circle(midX, midY, 16f, EditorTheme.WithAlpha(dotColor, 0.14f), true);
                ui.Circle(midX, midY, 5f, dotColor, true);

                if (displayLabel.Length > 12) displayLabel = displayLabel[..10] + "..";
                ui.TextCentered(cx, drawCy + itemH - 26, itemW, 18, displayLabel, isCardSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                cx += itemW + gap;
                fileIdx++;
            }

            if (dirs.Length == 0 && files.Length == 0)
            {
                ui.TextCentered(contentX, contentY + contentH * 0.45f, contentW, 20, "Drop files or press Cmd+I to import", EditorTheme.TextMuted);
            }

            ui.EndScrollArea("ContentBrowserGrid");
        }

        ui.InputEnabled = true;
        if (_input!.IsMouseButtonDown(MouseButton.Right) &&
            ui.IsHovering(contentX, contentY, contentW, contentH))
        {
            _showContextMenu = true;
            _contextMenuX = ui.MousePosition.X;
            _contextMenuY = ui.MousePosition.Y;
            _contextMenuPath = _currentBrowserDir ?? "";
        }

        if (_showContextMenu)
            DrawContextMenu(ui, _contextMenuX, _contextMenuY);
    }

    private static void DrawTerrainBrushButton(EditorUI ui, float x, float y, string label, BrushMode mode, uint id)
    {
        bool active = _terrainBrushMode == mode;
        if (ui.ButtonEx(x, y, 60, 22, label,
            active ? EditorTheme.WithAlpha(EditorTheme.Green, 0.25f) : EditorTheme.Bg2,
            EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), active ? EditorTheme.Green : EditorTheme.TextSecondary, id))
        {
            _terrainBrushMode = mode;
        }
    }

    private static void DrawConsolePanel(EditorUI ui, DockRect rect)
    {
        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);
        EditorChrome.Header(ui, rect.X, rect.Y, rect.W, EditorTheme.HeaderH, "Output Log");

        if (ui.ButtonEx(rect.X + rect.W - 72, rect.Y + 7, 56, 22, "Clear",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextSecondary, 9001))
        {
            _consoleLogs.Clear();
        }

        float y = rect.Y + EditorTheme.HeaderH + 10;
        float maxY = rect.Y + rect.H - 12;
        int lineHeight = 22;
        int maxLines = Math.Max(1, (int)((maxY - y) / lineHeight));

        int startIdx = Math.Max(0, _consoleLogs.Count - maxLines);
        for (int i = startIdx; i < _consoleLogs.Count && y < maxY; i++)
        {
            string log = _consoleLogs[i];
            var color = EditorTheme.TextSecondary;
            var dotColor = EditorTheme.TextDisabled;

            if (log.Contains("Error") || log.Contains("Failed") || log.Contains("\u2717"))
            {
                color = EditorTheme.Red;
                dotColor = EditorTheme.Red;
            }
            else if (log.Contains("Warning") || log.Contains("\u26a0"))
            {
                color = EditorTheme.Yellow;
                dotColor = EditorTheme.Yellow;
            }
            else if (log.Contains("Success") || log.Contains("\u2713") || log.Contains("Imported") || log.Contains("PASSED"))
            {
                color = EditorTheme.Green;
                dotColor = EditorTheme.Green;
            }

            ui.Circle(rect.X + 18, y + 7, 2.5f, dotColor, true);

            string display = log;
            int maxChars = Math.Max(12, (int)(rect.W - 48) / 7);
            if (display.Length > maxChars)
                display = display[..(maxChars - 3)] + "...";

            ui.SetCursor(rect.X + 30, y);
            ui.Text(display, color);
            y += lineHeight;
        }
    }
}
