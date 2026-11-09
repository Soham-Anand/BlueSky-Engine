using System;
using BlueSky.Airborne;
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
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace BlueSky.Editor;

partial class Program
{
    private static void BuildProjectBrowserUI()
    {
        ProjectConfig.ApplyCompletedDesktopProjectScan();

        float w = _window!.Size.X;
        float h = _window!.Size.Y;

        _ui!.Panel(0, 0, w, h, EditorTheme.LauncherBg);

        // ═══════════════════════════════════════════════════════════════
        //  SIDEBAR — Clean, minimal navigation
        // ═══════════════════════════════════════════════════════════════
        float sideW = 240;
        _ui.Panel(0, 0, sideW, h, EditorTheme.LauncherSidebar);
        _ui.Panel(sideW - 1, 0, 1, h, EditorTheme.Border1);

        // Brand mark
        float crestX = 28f, crestY = 48f;
        _ui.FilledTriangle(new Vector2(crestX, crestY), new Vector2(crestX + 10f, crestY - 18f), new Vector2(crestX + 20f, crestY), EditorTheme.Accent);
        _ui.FilledTriangle(new Vector2(crestX + 9f, crestY), new Vector2(crestX + 18f, crestY - 13f), new Vector2(crestX + 26f, crestY), EditorTheme.AccentCyan);
        _ui.SetCursor(64, 30);
        _ui.Text("BlueSky", EditorTheme.TextPrimary);
        _ui.SetCursor(64, 48);
        _ui.Text("Engine", EditorTheme.TextMuted);

        // Navigation items
        float navY = 96;
        string[] navItems = { "New Project", "Open Project" };
        EditorUI.UIIcon[] navIcons = { EditorUI.UIIcon.Plus, EditorUI.UIIcon.Folder };
        int[] navTabs = { 1, 0 };

        for (int i = 0; i < navItems.Length; i++)
        {
            bool isSel = _projectBrowserTab == navTabs[i];
            float rowH = 38;
            uint navId = 100u + (uint)i;

            if (_ui.ClickableCard(16, navY, sideW - 32, rowH, navId,
                isSel ? EditorTheme.SelectionBg : new Vector4(0, 0, 0, 0),
                EditorTheme.HoverBg,
                EditorTheme.SelectionBg))
            {
                _projectBrowserTab = navTabs[i];
            }

            if (isSel)
                _ui.RoundedPanel(16, navY + 8, 3, rowH - 16, EditorTheme.Accent, 1.5f);

            _ui.Icon(navIcons[i], 30, navY + 11, 15, isSel ? EditorTheme.Accent : EditorTheme.TextMuted);
            _ui.SetCursor(54, navY + 12);
            _ui.Text(navItems[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

            navY += rowH + 6;
        }

        // Minimal version footer
        _ui.SetCursor(28, h - 36);
        _ui.Text("Metal 3 · Forward+", EditorTheme.TextDisabled);

        // ═══════════════════════════════════════════════════════════════
        //  MAIN CONTENT AREA — Spacious & uncluttered
        // ═══════════════════════════════════════════════════════════════
        float cX = sideW + 48;
        float cW = w - sideW - 96;

        if (_projectBrowserTab == 1)
        {
            float cy = 40;
            _ui.SetCursor(cX, cy);
            _ui.Text("New Project", EditorTheme.TextPrimary);
            _ui.SetCursor(cX, cy + 22);
            _ui.Text("Select a starting template", EditorTheme.TextMuted);
            cy += 58;

            string[] templates = { "Blank", "3D Scene", "2D Game", "First Person", "Top-Down", "Side Scroller" };
            string[] templateDescs = { "Empty world", "Sky & sun light", "2D camera setup", "FPS controller", "Isometric rig", "Platformer" };

            float cardW = 156, cardH = 128, gap = 18;
            int cardsPerRow = Math.Max(1, (int)((cW + gap) / (cardW + gap)));

            for (int i = 0; i < templates.Length; i++)
            {
                int col = i % cardsPerRow;
                int row = i / cardsPerRow;
                float ax = cX + col * (cardW + gap);
                float ay = cy + row * (cardH + gap);

                bool isSel = _selectedTemplate == i;
                uint cardId = 300u + (uint)i;

                if (_ui.ClickableCard(ax, ay, cardW, cardH, cardId,
                    isSel ? EditorTheme.SelectionBg : EditorTheme.LauncherCardBg,
                    EditorTheme.LauncherCardHover,
                    EditorTheme.SelectionBg))
                {
                    _selectedTemplate = i;
                }

                if (isSel)
                    EditorChrome.Stroke(_ui, ax, ay, cardW, cardH, EditorTheme.Accent);

                // Minimal icon mark in center
                float midX = ax + cardW * 0.5f;
                float midY = ay + 42f;
                var iconCol = isSel ? EditorTheme.Accent : EditorTheme.TextMuted;
                _ui.Circle(midX, midY, 16f, EditorTheme.WithAlpha(iconCol, 0.14f), true);
                _ui.Circle(midX, midY, 5f, iconCol, true);

                _ui.TextCentered(ax, ay + 74, cardW, 18, templates[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
                _ui.TextCentered(ax, ay + 94, cardW, 16, templateDescs[i], EditorTheme.TextMuted);
            }

            // Clean bottom form
            float formY = cy + (((templates.Length - 1) / cardsPerRow) + 1) * (cardH + gap) + 32;
            _ui.Panel(cX, formY, cW, 1, EditorTheme.Border1);
            formY += 24;

            float nameW = Math.Min(cW * 0.34f, 280);
            _ui.SetCursor(cX, formY);
            _ui.Text("Name", EditorTheme.TextMuted);
            _ui.SetCursor(cX, formY + 20);
            {
                var tmp = _projectNameInput;
                _ui.TextField(ref tmp, nameW, 32);
                _projectNameInput = tmp;
            }

            float locX = cX + nameW + 20;
            float createBtnW = 136;
            float locW = Math.Max(180, cW - nameW - createBtnW - 40);
            _ui.SetCursor(locX, formY);
            _ui.Text("Location", EditorTheme.TextMuted);
            _ui.SetCursor(locX, formY + 20);
            {
                var tmp = _projectPathInput;
                _ui.TextField(ref tmp, locW, 32);
                _projectPathInput = tmp;
            }

            if (AnimatedButton.Render(_ui, cX + cW - createBtnW, formY + 20, createBtnW, 32, "Create", 500,
                normalColor: EditorTheme.Accent,
                hoverColor: EditorTheme.AccentHover,
                pressColor: EditorTheme.AccentDim,
                textColor: EditorTheme.TextPrimary))
            {
                string fullPath = Path.Combine(_projectPathInput, _projectNameInput);
                if (ProjectManager.TryCreateProject(fullPath))
                    TransitionToWorkspace();
                else
                    _errorMsg = "Failed to create project.";
            }

            if (!string.IsNullOrEmpty(_errorMsg))
            {
                _ui.SetCursor(cX, formY + 62);
                _ui.Text(_errorMsg, EditorTheme.Red);
            }
        }
        else if (_projectBrowserTab == 0)
        {
            float cy = 40;
            _ui.SetCursor(cX, cy);
            _ui.Text("Recent Projects", EditorTheme.TextPrimary);
            cy += 42;

            var recent = ProjectConfig.RecentProjects;
            if (recent.Count == 0)
            {
                _ui.SetCursor(cX, cy + 20);
                _ui.Text("No recent projects yet.", EditorTheme.TextMuted);
            }
            else
            {
                float cardH = 64, gap = 10;
                for (int i = 0; i < recent.Count; i++)
                {
                    float ay = cy + i * (cardH + gap);
                    if (ay + cardH > h - 110) break;

                    bool isSel = _selectedRecentProject == i;
                    uint cardId = 400u + (uint)i;

                    if (_ui.ClickableCard(cX, ay, cW, cardH, cardId,
                        isSel ? EditorTheme.SelectionBg : EditorTheme.LauncherCardBg,
                        EditorTheme.LauncherCardHover,
                        EditorTheme.SelectionBg))
                    {
                        _selectedRecentProject = i;
                        _openProjectPathInput = recent[i].Path;
                    }

                    if (isSel)
                        _ui.RoundedPanel(cX, ay + 10, 3, cardH - 20, EditorTheme.Accent, 1.5f);

                    _ui.Icon(EditorUI.UIIcon.Folder, cX + 20, ay + 24, 16, isSel ? EditorTheme.Accent : EditorTheme.FolderFront);

                    string name = recent[i].Name;
                    _ui.SetCursor(cX + 48, ay + 14);
                    _ui.Text(name, isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                    string path = recent[i].Path;
                    if (path.Length > 64) path = "..." + path[^61..];
                    _ui.SetCursor(cX + 48, ay + 36);
                    _ui.Text(path, EditorTheme.TextMuted);
                }
            }

            float formY = h - 88;
            _ui.Panel(cX, formY - 16, cW, 1, EditorTheme.Border1);
            float openBtnW = 136;
            float pathW = Math.Max(220, cW - openBtnW - 16);
            _ui.SetCursor(cX, formY);
            {
                var tmp = _openProjectPathInput;
                _ui.TextField(ref tmp, pathW, 32);
                _openProjectPathInput = tmp;
            }

            if (AnimatedButton.Render(_ui, cX + cW - openBtnW, formY, openBtnW, 32, "Open", 501,
                normalColor: EditorTheme.Accent,
                hoverColor: EditorTheme.AccentHover,
                pressColor: EditorTheme.AccentDim,
                textColor: EditorTheme.TextPrimary))
            {
                if (ProjectManager.TryOpenProject(_openProjectPathInput))
                    TransitionToWorkspace();
                else
                    _errorMsg = "Failed to open project. Ensure path contains a .BlueSkyProj file.";
            }
        }
    }

    private static void TransitionToWorkspace()
    {
        _state = EditorState.Workspace;
        _world = new World();
        // Moves children with their parents. Ticked by _world.Update during Play mode.
        _world.AddSystem(new BlueSky.SkyCore.HierarchySystem());

        _teaScriptSystem = new BlueSky.Core.Scripting.TeaScriptSystem(_world);
        _teaScriptSystem.SetInputProviders(IsTeaScriptKeyDown, IsTeaScriptMouseButtonDown);
        BlueSky.Core.Scripting.TeaScriptSystem.VectraParticles = _vectraSystem;

        _terrainSystem = new BlueSky.BSR.TerrainSystem(_world);

        float w = _window!.Size.X, h = _window.Size.Y;
        float headerH = EditorTheme.HeaderH + EditorTheme.ToolbarH;
        _dockingSystem = new DockingSystem(w, h - headerH);

        var vpPanel = _dockingSystem.AddPanel("viewport", "Viewport", DrawViewportPanel);
        vpPanel.Transparent = true;
        _dockingSystem.AddPanel("outliner", "Outliner", DrawOutlinerPanel);
        _dockingSystem.AddPanel("details", "Details", DrawDetailsPanel);
        _dockingSystem.AddPanel("content", "Content Browser", DrawContentBrowserPanel);
        _dockingSystem.AddPanel("console", "Output Log", DrawConsolePanel);

        _dockingSystem.DockTo("viewport", DockPosition.Center);
        _dockingSystem.DockTo("outliner", "viewport", DockPosition.Left);
        _dockingSystem.DockTo("details", "viewport", DockPosition.Right);
        _dockingSystem.DockTo("content", DockPosition.Bottom);
        _dockingSystem.DockTo("console", "content", DockPosition.Center);

        RegisterStrataPanel();

        try
        {
            IRenderer mainRenderer;
            var viewportRenderer = new BlueSky.Editor.ViewportRenderer(_rhi!, _world, _terrainSystem, vectraSystem: _vectraSystem);
            _editorViewportRenderer = viewportRenderer;

            var bsrRenderer = new BSRRenderer(_window!, _rhi!);
            bsrRenderer.SetViewportRenderer(viewportRenderer);
            bsrRenderer.Initialize();

            mainRenderer = bsrRenderer;
            _viewport = new BlueSky.BSR.Viewport(_window!, _input!, _world, mainRenderer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Stack trace: {ex.StackTrace}");
            _viewport = null;
        }

        _carControllerSystem = new BlueSky.Airborne.CarControllerSystem();
        if (_input != null && _viewport != null)
        {
            _carControllerSystem.Initialize(_world, _input, _viewport);
        }
    }
}
