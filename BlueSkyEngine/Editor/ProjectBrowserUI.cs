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
using NotBSRenderer;

namespace BlueSky.Editor;

partial class Program
{
    private static void BuildProjectBrowserUI()
    {
        float w = _window!.Size.X;
        float h = _window!.Size.Y;

        // ── Full-screen background ─────────────────────────────────────
        _ui!.GradientPanel(0, 0, w, h, EditorTheme.LauncherBg, EditorTheme.Bg0);
        _ui.Panel(0, 0, w, 2, EditorTheme.Accent);

        // ═══════════════════════════════════════════════════════════════
        //  SIDEBAR — fixed-width left panel with branding + navigation
        // ═══════════════════════════════════════════════════════════════
        float sideW = 260;
        _ui.GradientPanel(0, 0, sideW, h, EditorTheme.LauncherSidebar, EditorTheme.Bg0, vertical: false);
        _ui.Panel(sideW - 1, 0, 1, h, EditorTheme.Border0);  // right divider

        // ── Branding ──────────────────────────────────────────────────
        _ui.RoundedPanel(22, 24, 54, 54, EditorTheme.WithAlpha(EditorTheme.Accent, 0.16f), EditorTheme.CardRadius);
        EditorChrome.Stroke(_ui, 22, 24, 54, 54, EditorTheme.WithAlpha(EditorTheme.Accent, 0.42f));
        _ui.SetCursor(39, 42);
        _ui.Text("BS", EditorTheme.LauncherBrand);
        _ui.SetCursor(88, 30);
        _ui.Text("BlueSky", EditorTheme.LauncherBrand);
        _ui.SetCursor(88, 50);
        _ui.Text("Engine Editor", EditorTheme.TextMuted);

        // Subtle brand accent line
        _ui.Panel(24, 96, sideW - 48, 1, EditorTheme.Border1);

        // ── Navigation tabs ───────────────────────────────────────────
        float navY = 122;
        string[] navItems = { "New Project", "Open Project" };
        string[] navIcons = { "+", ">" };
        int[] navTabs = { 1, 0 };

        for (int i = 0; i < navItems.Length; i++)
        {
            bool isSel = _projectBrowserTab == navTabs[i];
            float rowH = 40;
            uint navId = 100u + (uint)i;

            // Row background
            var rowBg = isSel ? EditorTheme.SelectionBg : EditorTheme.WithAlpha(EditorTheme.LauncherSidebar, 0f);
            if (_ui.ClickableCard(12, navY, sideW - 24, rowH, navId,
                rowBg,
                EditorTheme.HoverBg,
                EditorTheme.SelectionBg))
            {
                _projectBrowserTab = navTabs[i];
            }

            // Selection indicator
            if (isSel)
                _ui.Panel(12, navY + 7, 3, rowH - 14, EditorTheme.Accent);

            // Icon + text
            _ui.SetCursor(30, navY + 12);
            _ui.Text(navIcons[i], isSel ? EditorTheme.Accent : EditorTheme.TextMuted);
            _ui.SetCursor(54, navY + 12);
            _ui.Text(navItems[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

            navY += rowH + 4;
        }

        // ── Sidebar footer ────────────────────────────────────────────
        _ui.Panel(24, h - 92, sideW - 48, 1, EditorTheme.Border1);
        _ui.SetCursor(24, h - 72);
        _ui.Text("Renderer", EditorTheme.TextDisabled);
        _ui.SetCursor(24, h - 50);
        _ui.Text("Forward+ / Ease+", EditorTheme.TextMuted);
        _ui.SetCursor(24, h - 26);
        _ui.Text("v0.1.0-alpha", EditorTheme.TextDisabled);

        // ═══════════════════════════════════════════════════════════════
        //  MAIN CONTENT AREA
        // ═══════════════════════════════════════════════════════════════
        float cX = sideW + 38;
        float cW = w - sideW - 76;

        if (_projectBrowserTab == 1)
        {
            // ── NEW PROJECT ───────────────────────────────────────────
            float cy = 36;

            // Section header
            _ui.SetCursor(cX, cy);
            _ui.Text("Create New Project", EditorTheme.TextPrimary);
            cy += 12;
            _ui.SetCursor(cX, cy);
            _ui.Text("Choose a template to get started", EditorTheme.TextMuted);
            cy += 36;

            // ── Template grid ─────────────────────────────────────────
            string[] templates = { "Blank", "3D Scene", "2D Game", "First Person", "Top-Down", "Side Scroller" };
            string[] templateIcons = { "BLK", "3D", "2D", "FPS", "TOP", "SIDE" };
            string[] templateDescs = { "Empty project", "Basic 3D setup", "2D starter", "FPS template", "Overhead view", "Platformer" };

            float cardW = 150, cardH = 140, gap = 14;
            int cardsPerRow = Math.Max(1, (int)((cW + gap) / (cardW + gap)));

            for (int i = 0; i < templates.Length; i++)
            {
                int col = i % cardsPerRow;
                int row = i / cardsPerRow;
                float ax = cX + col * (cardW + gap);
                float ay = cy + row * (cardH + gap);

                bool isSel = _selectedTemplate == i;
                uint cardId = 300u + (uint)i;

                var bgNorm = EditorTheme.LauncherCardBg;
                var bgHov = EditorTheme.LauncherCardHover;
                var bgPress = EditorTheme.SelectionBg;

                if (_ui.ClickableCard(ax, ay, cardW, cardH, cardId, isSel ? EditorTheme.SelectionBg : bgNorm, bgHov, bgPress))
                {
                    _selectedTemplate = i;
                }

                // Selection border
                if (isSel)
                {
                    _ui.Panel(ax, ay, cardW, 2, EditorTheme.Accent);
                    _ui.Panel(ax, ay + cardH - 2, cardW, 2, EditorTheme.Accent);
                    _ui.Panel(ax, ay, 2, cardH, EditorTheme.Accent);
                    _ui.Panel(ax + cardW - 2, ay, 2, cardH, EditorTheme.Accent);
                }
                else
                {
                    _ui.Panel(ax, ay, cardW, 1, EditorTheme.Border1);
                }

                // Icon area — centered
                float iconBgW = 56, iconBgH = 48;
                float iconX = ax + (cardW - iconBgW) / 2;
                float iconY = ay + 18;
                _ui.Panel(iconX, iconY, iconBgW, iconBgH, isSel ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.25f) : EditorTheme.Bg0);

                // Icon character — large centered
                float iconTextW = templateIcons[i].Length * 7.2f;
                _ui.SetCursor(iconX + (iconBgW - iconTextW) / 2, iconY + 16);
                _ui.Text(templateIcons[i], isSel ? EditorTheme.Accent : EditorTheme.TextMuted);

                // Template name
                float nameWidth = templates[i].Length * 7.2f;
                _ui.SetCursor(ax + (cardW - nameWidth) / 2, ay + 80);
                _ui.Text(templates[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                // Description
                float descWidth = templateDescs[i].Length * 7.2f;
                _ui.SetCursor(ax + (cardW - descWidth) / 2, ay + 100);
                _ui.Text(templateDescs[i], EditorTheme.TextDisabled);

                // Subtle check indicator
                if (isSel)
                {
                    _ui.Panel(ax + cardW - 18, ay + 8, 10, 10, EditorTheme.Green);
                }
            }

            // ── Project Configuration ──────────────────────────────────
            float formY = cy + (((templates.Length - 1) / cardsPerRow) + 1) * (cardH + gap) + 20;

            // Divider
            _ui.Panel(cX, formY, cW, 1, EditorTheme.Border1);
            formY += 20;

            // Section label
            _ui.SetCursor(cX, formY);
            _ui.Text("PROJECT SETTINGS", EditorTheme.TextMuted);
            formY += 28;

            // Project name
            _ui.SetCursor(cX, formY);
            _ui.Text("Project Name", EditorTheme.TextSecondary);
            formY += 22;
            _ui.SetCursor(cX, formY);
            {
                var tmp = _projectNameInput;
                _ui.TextField(ref tmp, Math.Min(cW * 0.4f, 340), 32);
                _projectNameInput = tmp;
            }

            // Location (same row, right side)
            float locX = cX + Math.Min(cW * 0.4f, 340) + 24;
            float locW = cW - Math.Min(cW * 0.4f, 340) - 24 - 120;
            _ui.SetCursor(locX, formY - 22);
            _ui.Text("Location", EditorTheme.TextSecondary);
            _ui.SetCursor(locX, formY);
            {
                var tmp = _projectPathInput;
                _ui.TextField(ref tmp, Math.Max(locW, 200), 32);
                _projectPathInput = tmp;
            }

            // Create button — right-aligned, prominent
            float createX = cX + cW - 110;
            if (_ui.ButtonEx(createX, formY, 110, 32, "Create Project",
                EditorTheme.Accent,
                EditorTheme.AccentHover,
                EditorTheme.AccentDim,
                new System.Numerics.Vector4(0, 0, 0, 0.4f),
                EditorTheme.TextPrimary, 500))
            {
                string fullPath = Path.Combine(_projectPathInput, _projectNameInput);
                if (ProjectManager.TryCreateProject(fullPath))
                    TransitionToWorkspace();
                else
                    _errorMsg = "Failed to create project.";
            }

            // Error message
            if (!string.IsNullOrEmpty(_errorMsg))
            {
                formY += 44;
                _ui.SetCursor(cX, formY);
                _ui.Text(_errorMsg, EditorTheme.Red);
            }
        }
        else if (_projectBrowserTab == 0)
        {
            // ── OPEN PROJECT ──────────────────────────────────────────
            float cy = 36;

            _ui.SetCursor(cX, cy);
            _ui.Text("Recent Projects", EditorTheme.TextPrimary);
            cy += 36;

            var recent = ProjectConfig.RecentProjects;

            if (recent.Count == 0)
            {
                // Empty state
                float emptyY = cy + 60;
                _ui.SetCursor(cX, emptyY);
                _ui.Text("No recent projects", EditorTheme.TextSecondary);
                _ui.SetCursor(cX, emptyY + 24);
                _ui.Text("Create a new project or browse to an existing one", EditorTheme.TextMuted);
            }
            else
            {
                float cardW = 260, cardH = 80, gap = 12;

                for (int i = 0; i < recent.Count; i++)
                {
                    float ay = cy + i * (cardH + gap);
                    if (ay + cardH > h - 100) break;

                    bool isSel = _selectedRecentProject == i;
                    uint cardId = 400u + (uint)i;

                    // Full-width card
                    float rowW = Math.Min(cW, 600);

                    if (_ui.ClickableCard(cX, ay, rowW, cardH, cardId,
                        isSel ? EditorTheme.SelectionBg : EditorTheme.LauncherCardBg,
                        EditorTheme.LauncherCardHover,
                        EditorTheme.SelectionBg))
                    {
                        _selectedRecentProject = i;
                        _openProjectPathInput = recent[i].Path;
                    }

                    // Left accent
                    if (isSel)
                        _ui.Panel(cX, ay, 3, cardH, EditorTheme.Accent);

                    // Project icon block
                    float iconX = cX + 16;
                    float iconY = ay + (cardH - 44) / 2;
                    _ui.Panel(iconX, iconY, 44, 44, isSel ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.3f) : EditorTheme.Bg0);
                    _ui.SetCursor(iconX + 14, iconY + 14);
                    _ui.Text("◈", isSel ? EditorTheme.Accent : EditorTheme.TextMuted);

                    // Project name
                    string name = recent[i].Name;
                    if (name.Length > 30) name = name[..28] + "..";
                    _ui.SetCursor(cX + 76, ay + 18);
                    _ui.Text(name, isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                    // Path
                    string path = recent[i].Path;
                    if (path.Length > 50) path = "..." + path[^48..];
                    _ui.SetCursor(cX + 76, ay + 40);
                    _ui.Text(path, EditorTheme.TextDisabled);

                    // Date badge
                    string dateStr = recent[i].LastOpened == DateTime.MinValue ? "New" : recent[i].LastOpened.ToString("MMM dd");
                    _ui.SetCursor(cX + rowW - 70, ay + 30);
                    _ui.Text(dateStr, EditorTheme.TextMuted);
                }
            }

            // ── Browse / open from path ─────────────────────────────
            float formY = h - 100;
            _ui.Panel(cX, formY, cW, 1, EditorTheme.Border1);
            formY += 16;

            _ui.SetCursor(cX, formY);
            _ui.Text("Project Path", EditorTheme.TextSecondary);
            formY += 22;

            float pathW = Math.Min(cW - 130, 600);
            _ui.SetCursor(cX, formY);
            {
                var tmp = _openProjectPathInput;
                _ui.TextField(ref tmp, pathW, 32);
                _openProjectPathInput = tmp;
            }

            if (_ui.ButtonEx(cX + pathW + 12, formY, 110, 32, "Open Project",
                EditorTheme.Accent,
                EditorTheme.AccentHover,
                EditorTheme.AccentDim,
                new System.Numerics.Vector4(0, 0, 0, 0.4f),
                EditorTheme.TextPrimary, 501))
            {
                if (ProjectManager.TryOpenProject(_openProjectPathInput))
                    TransitionToWorkspace();
                else
                    _errorMsg = "Failed to open project. Ensure path contains a .BlueSkyProj file.";
            }

            if (!string.IsNullOrEmpty(_errorMsg))
            {
                _ui.SetCursor(cX, formY + 40);
                _ui.Text(_errorMsg, EditorTheme.Red);
            }
        }

        _ui.EndFrame();
    }

    private static void TransitionToWorkspace()
    {
        _state = EditorState.Workspace;
        _world = new World();

        // Initialize TeaScript system
        _teaScriptSystem = new BlueSky.Core.Scripting.TeaScriptSystem(_world);
        _teaScriptSystem.SetInputProviders(IsTeaScriptKeyDown, IsTeaScriptMouseButtonDown);
        Console.WriteLine("[Editor] TeaScriptSystem initialized");
        
        // Initialize Terrain system
        _terrainSystem = new BlueSky.Rendering.TerrainSystem(_world);
        Console.WriteLine("[Editor] TerrainSystem initialized");
        
        // Car Controller system is initialized after the Viewport is created (see below)
        
        // Welcome notification
        string projectName = Path.GetFileName(ProjectManager.CurrentProjectDir) ?? "Project";
        _notificationSystem?.ShowSuccess($"Welcome to {projectName}!", duration: 3f);

        // Create a simple cube entity with TransformComponent
        var entity1 = _world.CreateEntity();
        var transform1 = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(0, 1, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = BlueSky.Core.Math.Vector3.One
        };
        _world.AddComponent(entity1, transform1);
        _world.AddComponent(entity1, new BlueSky.Core.ECS.Builtin.StaticMeshComponent { MeshAssetId = "CorvetteC7" });
        _world.AddComponent(entity1, new BlueSky.Core.ECS.Builtin.RigidbodyComponent { Mass = 1400f, Drag = 0.5f, AngularDrag = 2.0f, UseGravity = true, IsKinematic = false });
        _world.AddComponent(entity1, new BlueSky.Core.ECS.Builtin.ColliderComponent { Type = BlueSky.Core.ECS.Builtin.ColliderType.Box, Size = new System.Numerics.Vector3(2.0f, 1.2f, 4.5f), Friction = 0.8f, Restitution = 0.1f });

        // Create a second cube entity
        var entity2 = _world.CreateEntity();
        var transform2 = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(2, 1, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = BlueSky.Core.Math.Vector3.One
        };
        _world.AddComponent(entity2, transform2);
        _world.AddComponent(entity2, new BlueSky.Core.ECS.Builtin.StaticMeshComponent { MeshAssetId = "CorvetteC7" });
        _world.AddComponent(entity2, new BlueSky.Core.ECS.Builtin.RigidbodyComponent { Mass = 1400f, Drag = 0.5f, AngularDrag = 2.0f, UseGravity = true, IsKinematic = false });
        _world.AddComponent(entity2, new BlueSky.Core.ECS.Builtin.ColliderComponent { Type = BlueSky.Core.ECS.Builtin.ColliderType.Box, Size = new System.Numerics.Vector3(2.0f, 1.2f, 4.5f), Friction = 0.8f, Restitution = 0.1f });

        // Create a third cube entity
        var entity3 = _world.CreateEntity();
        var transform3 = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(-2, 1, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = BlueSky.Core.Math.Vector3.One
        };
        _world.AddComponent(entity3, transform3);
        _world.AddComponent(entity3, new BlueSky.Core.ECS.Builtin.StaticMeshComponent { MeshAssetId = "CorvetteC7" });
        _world.AddComponent(entity3, new BlueSky.Core.ECS.Builtin.RigidbodyComponent { Mass = 1400f, Drag = 0.5f, AngularDrag = 2.0f, UseGravity = true, IsKinematic = false });
        _world.AddComponent(entity3, new BlueSky.Core.ECS.Builtin.ColliderComponent { Type = BlueSky.Core.ECS.Builtin.ColliderType.Box, Size = new System.Numerics.Vector3(2.0f, 1.2f, 4.5f), Friction = 0.8f, Restitution = 0.1f });

        // Initialize UE5-style docking layout FIRST
        float w = _window!.Size.X, h = _window.Size.Y;
        float headerH = EditorTheme.HeaderH + EditorTheme.ToolbarH;
        _dockingSystem = new DockingSystem(w, h - headerH);

        // Register all editor panels
        var vpPanel = _dockingSystem.AddPanel("viewport", "Viewport", DrawViewportPanel);
        vpPanel.Transparent = true; // 3D content rendered by GPU, don't cover with UI background
        _dockingSystem.AddPanel("outliner", "Outliner", DrawOutlinerPanel);
        _dockingSystem.AddPanel("details", "Details", DrawDetailsPanel);
        _dockingSystem.AddPanel("content", "Content Browser", DrawContentBrowserPanel);
        _dockingSystem.AddPanel("console", "Output Log", DrawConsolePanel);

        // Build default UE5-style layout:
        // [Outliner | Viewport | Details]
        // [         Content Browser     ]
        _dockingSystem.DockTo("viewport", DockPosition.Center);
        _dockingSystem.DockTo("outliner", "viewport", DockPosition.Left);
        _dockingSystem.DockTo("details", "viewport", DockPosition.Right);
        _dockingSystem.DockTo("content", DockPosition.Bottom);
        _dockingSystem.DockTo("console", "content", DockPosition.Center); // Tab with content browser

        // ── Viewport 3D rendering ─────────────────────────────────────
        try
        {
            IRenderer mainRenderer;
            
            // Always attach ViewportRenderer for sky, grid, entity, and gizmo rendering fallback
            var viewportRenderer = new BlueSky.Editor.ViewportRenderer(_rhi!, _world, _terrainSystem);
            _editorViewportRenderer = viewportRenderer;

            if (_useEaseRenderer)
            {
                var easeRenderer = new BlueSky.Rendering.EasePlus.EasePlusRenderer(_window!, _rhi!);
                easeRenderer.SetViewportRenderer(viewportRenderer);
                easeRenderer.Initialize();
                mainRenderer = easeRenderer;
                Console.WriteLine("[Editor] Started with Ease+ Ultimate Renderer");
            }
            else
            {
                var ultraRenderer = new UltraRenderer(_window!, _rhi!);
                ultraRenderer.SetViewportRenderer(viewportRenderer);
                ultraRenderer.Initialize();
                
                mainRenderer = ultraRenderer;
                Console.WriteLine("[Editor] Started with UltraRenderer (Forward+)");
            }

            _viewport = new BlueSky.Rendering.Viewport(_window!, _input!, _world, mainRenderer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[Editor] Inner exception: {ex.InnerException.Message}");
                Console.WriteLine($"[Editor] Inner stack trace: {ex.InnerException.StackTrace}");
            }
            _viewport = null;
        }

        // Initialize Car Controller system (must be after Viewport creation)
        _carControllerSystem = new BlueSky.Core.Gameplay.CarControllerSystem();
        if (_input != null && _viewport != null)
        {
            _carControllerSystem.Initialize(_world, _input, _viewport);
            Console.WriteLine("[Editor] CarControllerSystem initialized - Press F to possess cars!");
        }
        else
        {
            Console.WriteLine("[Editor] ⚠️ CarControllerSystem created but not initialized (input or viewport missing)");
        }
    }

}
