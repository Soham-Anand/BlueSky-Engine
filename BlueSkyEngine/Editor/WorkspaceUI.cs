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
using BlueSky.LHO;
using BlueSky.BSR.RHI;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace BlueSky.Editor;

partial class Program
{
    private static void BuildWorkspaceUI()
    {
        float w = _window!.Size.X;
        float h = _window!.Size.Y;
        float topBarH = EditorTheme.HeaderH; // Single unified 38px top bar

        // ═══════════════════════════════════════════════════════════════
        //  UNIFIED TOP BAR — Minimalist single-row header
        // ═══════════════════════════════════════════════════════════════
        _ui!.Panel(0, 0, w, topBarH, EditorTheme.Bg0);
        _ui.Panel(0, topBarH - 1, w, 1, EditorTheme.Border1);

        // Minimal brand mark (left)
        float crestX = 16f, crestY = topBarH * 0.5f + 4f;
        _ui.FilledTriangle(new Vector2(crestX, crestY), new Vector2(crestX + 6f, crestY - 10f), new Vector2(crestX + 12f, crestY), EditorTheme.Accent);
        _ui.FilledTriangle(new Vector2(crestX + 5f, crestY), new Vector2(crestX + 10f, crestY - 7f), new Vector2(crestX + 15f, crestY), EditorTheme.AccentCyan);
        _ui.SetCursor(38, (topBarH - EditorTheme.CharHeight) * 0.5f);
        _ui.Text("BlueSky", EditorTheme.TextPrimary);

        // Menu items + Cmd+P quick search pill
        DrawMenuBarHeader(_ui, topBarH);

        // Right-side action cluster: Standalone + Package .app + FPS
        float btnH = 24f;
        float btnY = (topBarH - btnH) * 0.5f;

        float fps = _deltaTime > 0 ? 1f / _deltaTime : 0;
        string fpsText = $"{fps:F0} FPS";
        float fpsW = _ui.MeasureText(fpsText) + 16f;
        float rightCursor = w - fpsW - 12f;
        _ui.SetCursor(rightCursor, (topBarH - EditorTheme.CharHeight) * 0.5f);
        _ui.Text(fpsText, fps >= 55 ? EditorTheme.Green : EditorTheme.Yellow);

        // Package .app button
        rightCursor -= 108f;
        if (AnimatedButton.Render(_ui, rightCursor, btnY, 100f, btnH,
            "Package", 616,
            normalColor: EditorTheme.Bg2,
            hoverColor: EditorTheme.Bg3,
            pressColor: EditorTheme.AccentDim,
            textColor: EditorTheme.TextSecondary,
            enabled: true,
            iconKind: EditorUI.UIIcon.Import))
        {
            OpenAvalancheDialogCommand();
        }

        // Play Standalone button
        bool standaloneRunning = _standalonePlay?.IsRunning ?? false;
        rightCursor -= 122f;
        if (AnimatedButton.Render(_ui, rightCursor, btnY, 114f, btnH,
            standaloneRunning ? "Stop App" : "Standalone", 613,
            normalColor: standaloneRunning ? EditorTheme.WithAlpha(EditorTheme.Red, 0.18f) : EditorTheme.Bg2,
            hoverColor: standaloneRunning ? EditorTheme.WithAlpha(EditorTheme.Red, 0.30f) : EditorTheme.Bg3,
            pressColor: standaloneRunning ? EditorTheme.Red : EditorTheme.Green,
            textColor: standaloneRunning ? EditorTheme.Red : EditorTheme.TextSecondary,
            enabled: true,
            iconKind: standaloneRunning ? EditorUI.UIIcon.Stop : EditorUI.UIIcon.Play))
        {
            ToggleStandaloneCommand();
        }

        // ═══════════════════════════════════════════════════════════════
        //  SLIM LEFT RAIL — Minimal, spacious tool column (no clutter)
        // ═══════════════════════════════════════════════════════════════
        const float RailW = 84f;
        DrawLeftRail(topBarH, h, btnH, RailW);

        // ═══════════════════════════════════════════════════════════════
        //  DOCKING SYSTEM — fills everything below topBarH, right of rail
        // ═══════════════════════════════════════════════════════════════
        if (_dockingSystem != null)
        {
            _dockingSystem.Resize(w - RailW, h - topBarH, topBarH, RailW);

            var mousePos = _input!.MousePosition;
            bool mouseDown = _input.IsMouseButtonDown(MouseButton.Left) && !IsMenuOrPaletteOpen;

            _dockingSystem.Update(_ui, mousePos, mouseDown);
        }

        _ui.InputEnabled = true;

        // ── Modal overlays ────────────────────────────────────────────
        if (_showImportDialog)
            DrawImportDialog(_ui, w, h);

        if (_showScriptEditor)
            DrawScriptEditor(_ui, w, h);

        if (_staticMeshEditor is { IsOpen: true })
        {
            float editorW = 1200, editorH = 700;
            float editorX = (w - editorW) / 2;
            float editorY = (h - editorH) / 2;
            _staticMeshEditor.Render(_ui, editorX, editorY, editorW, editorH);

            if (!_staticMeshEditor.IsOpen && _world != null)
                _staticMeshEditor.Close(_world);
        }

        if (_skeletalMeshEditor is { IsOpen: true })
        {
            float editorW = 1200, editorH = 700;
            float editorX = (w - editorW) / 2;
            float editorY = (h - editorH) / 2;
            _skeletalMeshEditor.Render(_ui, editorX, editorY, editorW, editorH);

            if (!_skeletalMeshEditor.IsOpen && _world != null)
                _skeletalMeshEditor.Close(_world);
        }

        if (_showRenameDialog)
            DrawRenameDialog(_ui, w, h);

        if (_showContextMenu)
            DrawContextMenu(_ui, _contextMenuX, _contextMenuY);

        DrawMenuAndPaletteOverlays(_ui, w, h);
    }

    // ── Minimal Left Rail ─────────────────────────────────────────────
    // Borderless ghost buttons with generous vertical breathing room.
    private static void DrawLeftRail(float topBarH, float h, float btnH, float railW)
    {
        float rowW = railW - 16f, rowH = 34f, rowX = 8f;
        float ry = topBarH + 14f;

        var ui = _ui!;
        ui.Panel(0, topBarH, railW, h - topBarH, EditorTheme.Bg0);
        ui.Panel(railW - 1, topBarH, 1, h - topBarH, EditorTheme.Border1);

        bool RailRow(EditorUI.UIIcon kind, string label, Vector4 tint, bool active, bool enabled, uint id)
        {
            bool hot = enabled && ui.IsHovering(rowX, ry, rowW, rowH);
            if (active || hot)
            {
                ui.RoundedPanel(rowX, ry, rowW, rowH,
                    active ? EditorTheme.WithAlpha(tint, 0.16f) : EditorTheme.HoverBg,
                    EditorTheme.ButtonRadius);
            }

            if (active)
            {
                ui.RoundedPanel(rowX, ry + 7f, 3f, rowH - 14f, tint, 1.5f);
            }

            bool clicked = ui.ClickableCard(rowX, ry, rowW, rowH, id,
                new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0));

            var iconColor = !enabled ? EditorTheme.TextDisabled : (active ? tint : (hot ? EditorTheme.TextPrimary : EditorTheme.TextMuted));
            var textColor = !enabled ? EditorTheme.TextDisabled : (active || hot ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

            ui.Icon(kind, rowX + 8f, ry + (rowH - 14f) * 0.5f, 14f, iconColor);
            ui.SetCursor(rowX + 26f, ry + (rowH - EditorTheme.CharHeight) * 0.5f);
            ui.Text(label, textColor);

            ry += rowH + 8f;
            return enabled && clicked;
        }

        void RailDivider()
        {
            ry += 4f;
            ui.Panel(14, ry, railW - 28, 1, EditorTheme.Border1);
            ry += 12f;
        }

        // Transform tools
        var gizmoMode = _editorViewportRenderer?.CurrentGizmoMode ?? ViewportRenderer.GizmoMode.Translate;

        if (RailRow(EditorUI.UIIcon.Move, "Move", EditorTheme.Accent, gizmoMode == ViewportRenderer.GizmoMode.Translate, true, 701))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Translate;
        }
        if (RailRow(EditorUI.UIIcon.Rotate, "Rot", EditorTheme.Accent, gizmoMode == ViewportRenderer.GizmoMode.Rotate, true, 702))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Rotate;
        }
        if (RailRow(EditorUI.UIIcon.Scale, "Scale", EditorTheme.Accent, gizmoMode == ViewportRenderer.GizmoMode.Scale, true, 703))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Scale;
        }

        RailDivider();

        // Play / Stop transport
        if (!_isPlaying)
        {
            if (RailRow(EditorUI.UIIcon.Play, "Play", EditorTheme.PlayGreen, false, true, 704))
                TogglePlayInEditorCommand();
        }
        else
        {
            if (RailRow(_isPaused ? EditorUI.UIIcon.Play : EditorUI.UIIcon.Pause, _isPaused ? "Res" : "Pause", EditorTheme.Orange, _isPaused, true, 705))
            {
                PlayMode.TogglePause(Log);
                _notificationSystem?.ShowInfo(_isPaused ? "Paused" : "Resumed", duration: 1.5f);
            }
            if (RailRow(EditorUI.UIIcon.Stop, "Stop", EditorTheme.StopRed, true, true, 706))
                TogglePlayInEditorCommand();
        }

        RailDivider();

        // Sculpt toggle
        if (RailRow(EditorUI.UIIcon.Settings, "Sculpt", EditorTheme.Green, _terrainEditMode, true, 708))
        {
            _terrainEditMode = !_terrainEditMode;
            _notificationSystem?.ShowInfo(_terrainEditMode ? "Terrain Sculpt Mode ON" : "Terrain Sculpt Mode OFF", 2f);
        }
    }
}
