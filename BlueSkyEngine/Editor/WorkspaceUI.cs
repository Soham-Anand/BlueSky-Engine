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
using BlueSky.Networking;
using BlueSky.Rendering.RHI;

namespace BlueSky.Editor;

partial class Program
{
    private static void BuildWorkspaceUI()
    {
        float w = _window!.Size.X;
        float h = _window!.Size.Y;
        float menuH = EditorTheme.HeaderH;
        float toolbarH = EditorTheme.ToolbarH;
        float totalHeaderH = menuH + toolbarH;

        // ═══════════════════════════════════════════════════════════════
        //  MENU BAR - project command center
        // ═══════════════════════════════════════════════════════════════
        _ui!.GradientPanel(0, 0, w, menuH, EditorTheme.Bg1, EditorTheme.Bg0);
        _ui.Panel(0, menuH - 1, w, 1, EditorTheme.Border0);
        _ui.Panel(0, 0, w, 1, EditorTheme.Highlight);

        // Engine branding (left)
        _ui.RoundedPanel(10, 5, 116, menuH - 10, EditorTheme.Bg2, EditorTheme.SmallRadius);
        _ui.Panel(10, 5, 3, menuH - 10, EditorTheme.Accent);
        _ui.SetCursor(22, menuH / 2 - 7);
        _ui.Text("BLUESKY", EditorTheme.LauncherBrand);
        _ui.SetCursor(82, menuH / 2 - 7);
        _ui.Text("EDITOR", EditorTheme.TextDisabled);

        // Menu items with hover underline effect
        string[] menus = { "File", "Edit", "Window", "Tools", "Build", "Help" };
        float menuX = 144;
        foreach (var m in menus)
        {
            float itemW = m.Length * 7.5f + 12;
            bool menuHot = _ui.IsHovering(menuX - 6, 0, itemW, menuH);
            if (menuHot)
            {
                _ui.RoundedPanel(menuX - 8, 5, itemW + 4, menuH - 10, EditorTheme.Bg3, EditorTheme.SmallRadius);
                _ui.Panel(menuX - 6, menuH - 3, itemW, 2, EditorTheme.Accent);
            }

            _ui.SetCursor(menuX, menuH / 2 - 6);
            _ui.Text(m, menuHot ? EditorTheme.TextPrimary : EditorTheme.TextMuted);
            menuX += m.Length * 7.5f + 22;
        }

        // Compact status block (right side)
        string projName = Path.GetFileName(ProjectManager.CurrentProjectDir ?? "Untitled");
        float fps = _deltaTime > 0 ? 1f / _deltaTime : 0;
        string fpsText = $"{fps:F0} FPS";
        float pillH = 20;
        float fpsPillW = MathF.Max(72, _ui.MeasureText(fpsText) + 34);
        float fpsPillX = w - fpsPillW - 12;
        float pillY = (menuH - pillH) / 2;
        EditorChrome.Pill(_ui, fpsPillX, pillY, fpsPillW, pillH, fpsText, fps >= 55 ? EditorTheme.Green : EditorTheme.Yellow);

        float projectW = MathF.Min(240, MathF.Max(104, _ui.MeasureText(projName) + 34));
        EditorChrome.Pill(_ui, fpsPillX - projectW - 8, pillY, projectW, pillH, projName, EditorTheme.AccentCyan);

        // ═══════════════════════════════════════════════════════════════
        //  TOOLBAR - grouped editor tools
        // ═══════════════════════════════════════════════════════════════
        _ui.GradientPanel(0, menuH, w, toolbarH, EditorTheme.ToolbarBg, EditorTheme.Bg1);
        _ui.Panel(0, menuH + toolbarH - 1, w, 1, EditorTheme.Border0);

        float btnH = 26;
        float tlX = 12;
        float tlY = menuH + (toolbarH - btnH) / 2;
        _ui.RoundedPanel(tlX - 6, tlY - 4, 176, btnH + 8, EditorTheme.Bg0, EditorTheme.SmallRadius);

        // Save button
        if (_ui.ButtonEx(tlX, tlY, 48, btnH, "Save",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 600))
        {
            SaveScene();
        }
        tlX += 54;

        // Load button
        if (_ui.ButtonEx(tlX, tlY, 48, btnH, "Load",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 603))
        {
            LoadScene();
        }
        tlX += 54;

        // New button
        if (_ui.ButtonEx(tlX, tlY, 48, btnH, "New",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 604))
        {
            NewScene();
        }
        tlX += 58;

        _ui.Panel(tlX, menuH + 8, 1, toolbarH - 16, EditorTheme.Border1);
        tlX += 8;

        // ── Gizmo Mode Buttons (W/E/R like UE5) ─────────────────────
        _ui.Panel(tlX, menuH + 8, 1, toolbarH - 16, EditorTheme.Border1);
        tlX += 8;
        _ui.RoundedPanel(tlX - 6, tlY - 4, 132, btnH + 8, EditorTheme.Bg0, EditorTheme.SmallRadius);
        
        var gizmoMode = _editorViewportRenderer?.CurrentGizmoMode 
                        ?? BlueSky.Editor.ViewportRenderer.GizmoMode.Translate;
        
        // Move (W)
        bool isMoveActive = gizmoMode == BlueSky.Editor.ViewportRenderer.GizmoMode.Translate;
        if (_ui.ButtonEx(tlX, tlY, 36, btnH, "W",
            isMoveActive ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.35f) : EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            isMoveActive ? EditorTheme.Accent : EditorTheme.TextSecondary, 620))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.CurrentGizmoMode = BlueSky.Editor.ViewportRenderer.GizmoMode.Translate;
        }
        tlX += 40;
        
        // Rotate (E)
        bool isRotActive = gizmoMode == BlueSky.Editor.ViewportRenderer.GizmoMode.Rotate;
        if (_ui.ButtonEx(tlX, tlY, 36, btnH, "E",
            isRotActive ? EditorTheme.WithAlpha(EditorTheme.Green, 0.35f) : EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            isRotActive ? EditorTheme.Green : EditorTheme.TextSecondary, 621))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.CurrentGizmoMode = BlueSky.Editor.ViewportRenderer.GizmoMode.Rotate;
        }
        tlX += 40;
        
        // Scale (R)
        bool isScaleActive = gizmoMode == BlueSky.Editor.ViewportRenderer.GizmoMode.Scale;
        if (_ui.ButtonEx(tlX, tlY, 36, btnH, "R",
            isScaleActive ? EditorTheme.WithAlpha(EditorTheme.Orange, 0.35f) : EditorTheme.ToolbarBtnNormal,
            EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            isScaleActive ? EditorTheme.Orange : EditorTheme.TextSecondary, 622))
        {
            if (_editorViewportRenderer != null)
                _editorViewportRenderer.CurrentGizmoMode = BlueSky.Editor.ViewportRenderer.GizmoMode.Scale;
        }

        // Right: Strata lives in the dock tab now (no toolbar toggle;
        // the old flag gate left the dock tab body empty).

        // Center: Play / Pause / Stop + Standalone
        float tcW = 420; // Wider to fit standalone + instance count field
        float tcX = (w - tcW) / 2;
        float tcY = tlY; // Same Y as left buttons
        _ui.RoundedPanel(tcX - 8, tcY - 5, tcW + 16, btnH + 10, EditorTheme.Bg0, EditorTheme.PillRadius);
        EditorChrome.Stroke(_ui, tcX - 8, tcY - 5, tcW + 16, btnH + 10, EditorTheme.Border1);
        _ui.Panel(tcX + 63, tcY - 1, 1, btnH + 2, EditorTheme.Border1);
        _ui.Panel(tcX + 137, tcY - 1, 1, btnH + 2, EditorTheme.Border1);
        _ui.Panel(tcX + 210, tcY - 1, 1, btnH + 2, EditorTheme.Border1); // Separator for standalone

        // Play button with animated success style
        if (AnimatedButton.RenderSuccess(_ui, tcX, tcY, 58, btnH, "Run", 610,
            enabled: !_isPlaying, icon: ">"))
        {
            if (!_isPlaying)
            {
                if (_world != null)
                {
                    PlayMode.Start(
                        _world,
                        _terrainSystem,
                        hotReloadScripts: HotReloadScripts,
                        resetTeaScriptRuntimeInstances: () => _teaScriptSystem?.ResetRuntimeInstances(),
                        log: Log
                    );
                    _physicsAccumulator = 0.0;
                    _physicsStepsThisFrame = 0;
                    _physicsFrame = 0;
                    ResetEditorPhysicsInterpolation();
                    _notificationSystem?.ShowSuccess("Play mode started", duration: 2f);
                }
            }
        }

        // Pause button with animated warning style
        if (AnimatedButton.Render(_ui, tcX + 68, tcY, 64, btnH, _isPaused ? "Resume" : "Pause", 611,
            normalColor: ModernTheme.WithAlpha(ModernTheme.Orange, 0.2f),
            hoverColor: ModernTheme.WithAlpha(ModernTheme.Orange, 0.4f),
            pressColor: ModernTheme.Orange,
            textColor: ModernTheme.Orange,
            enabled: _isPlaying))
        {
            if (_isPlaying)
            {
                PlayMode.TogglePause(Log);
                _notificationSystem?.ShowInfo(_isPaused ? "Paused" : "Resumed", duration: 1.5f);
            }
        }

        // Stop button with animated danger style
        if (AnimatedButton.RenderDanger(_ui, tcX + 142, tcY, 58, btnH, "Stop", 612,
            enabled: _isPlaying, icon: "x"))
        {
            if (_isPlaying)
            {
                if (_world != null)
                {
                    PlayMode.Stop(_world, hotReloadScripts: HotReloadScripts, log: Log);
                    _physicsAccumulator = 0.0;
                    _physicsStepsThisFrame = 0;
                    _physicsFrame = 0;
                    ResetEditorPhysicsInterpolation();
                    _notificationSystem?.ShowInfo("Stopped - scene restored", duration: 2f);
                }
            }
        }
        
        // Instance count selector (for multi-instance local multiplayer)
        // Arrow buttons + count display — simple, no focus-tracking needed
        float icX = tcX + 216;
        _standaloneInstanceCount = Math.Clamp(_standaloneInstanceCount, 1, 4);
        // "<" button
        if (_ui.ButtonEx(icX, tcY, 14, btnH, "<",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 614) && _standaloneInstanceCount > 1)
        {
            _standaloneInstanceCount--;
        }
        // Count label centered in the selector
        _ui.SetCursor(icX + 14, tcY + 4);
        _ui.Text($"{_standaloneInstanceCount}", EditorTheme.Accent);
        // ">" button
        if (_ui.ButtonEx(icX + 24, tcY, 14, btnH, ">",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 615) && _standaloneInstanceCount < 4)
        {
            _standaloneInstanceCount++;
        }

        // Play Standalone button
        bool standaloneRunning = _standalonePlay?.IsRunning ?? false;
        if (AnimatedButton.Render(_ui, tcX + 255, tcY, 120, btnH,
            standaloneRunning ? "Stop Standalone" : "Play Standalone", 613,
            normalColor: standaloneRunning ? ModernTheme.WithAlpha(ModernTheme.Red, 0.2f) : ModernTheme.WithAlpha(ModernTheme.Blue, 0.2f),
            hoverColor: standaloneRunning ? ModernTheme.WithAlpha(ModernTheme.Red, 0.4f) : ModernTheme.WithAlpha(ModernTheme.Blue, 0.4f),
            pressColor: standaloneRunning ? ModernTheme.Red : ModernTheme.Blue,
            textColor: standaloneRunning ? ModernTheme.Red : ModernTheme.Blue,
            enabled: true))
        {
            if (standaloneRunning)
            {
                _standalonePlay?.Stop();
                _notificationSystem?.ShowInfo("Standalone stopped", duration: 2f);
            }
            else
            {
                if (_world != null && !string.IsNullOrEmpty(_currentScenePath))
                {
                    if (_standaloneInstanceCount > 1)
                    {
                        // Multi-instance launch: all offline — user chooses Host/Join inside each window
                        var baseOpts = new BlueSky.Networking.StandaloneLaunchOptions
                        {
                            ScenePath = _currentScenePath,
                            Width = 1280,
                            Height = 720,
                            Fullscreen = false,
                            VSync = true,
                            Multiplayer = new MultiplayerConfig
                            {
                                Mode = MultiplayerMode.Offline,
                                ScenePath = _currentScenePath,
                            }
                        };

                        bool launched = _standalonePlay?.LaunchMultiple(baseOpts, _standaloneInstanceCount) ?? false;
                        if (launched)
                        {
                            int count = _standaloneInstanceCount;
                            _notificationSystem?.ShowSuccess($"Launched {count} instance(s) for local multiplayer", duration: 3f);
                        }
                        else
                        {
                            _notificationSystem?.ShowError("Failed to launch standalone instances", duration: 3f);
                        }
                    }
                    else
                    {
                        // Single instance (original behavior)
                        bool launchAsHost = _input != null &&
                            (_input.IsKeyDown(KeyCode.LeftShift) || _input.IsKeyDown(KeyCode.RightShift));
                        bool launchAsJoin = _input != null &&
                            (_input.IsKeyDown(KeyCode.LeftControl) || _input.IsKeyDown(KeyCode.RightControl));

                        if (!launchAsHost && !launchAsJoin)
                        {
                            NetworkingTeaScriptBridge.Register(null);
                            bool launched = _standalonePlay?.Launch(_currentScenePath, 1280, 720, false, true) ?? false;
                            if (launched)
                                _notificationSystem?.ShowSuccess($"Standalone launched (PID: {_standalonePlay?.ProcessId})", duration: 3f);
                            else
                                _notificationSystem?.ShowError("Failed to launch standalone", duration: 3f);
                            return;
                        }

                        var multiplayer = new MultiplayerConfig
                        {
                            Mode        = launchAsHost ? MultiplayerMode.Host : MultiplayerMode.Join,
                            SessionName = Path.GetFileNameWithoutExtension(_currentScenePath),
                            LocalPlayerName = Environment.UserName,
                            JoinSessionId   = Environment.GetEnvironmentVariable("EOS_JOIN_SESSION_ID") ?? "",
                            Eos       = EosCredentials.LoadForProject(ProjectManager.CurrentProjectDir ?? ""),
                            ScenePath = _currentScenePath
                        };

                        var liveService = MultiplayerServiceFactory.Create(multiplayer);
                        liveService.Initialize(Log);
                        NetworkingTeaScriptBridge.Register(liveService, (scenePath, mode, sessionName, joinId) =>
                        {
                            Log($"[Net Bridge] loadScene({scenePath}, {mode}, session={sessionName}, join={joinId})");
                            _notificationSystem?.ShowInfo($"Scene load requested: {Path.GetFileName(scenePath)} [{mode}]", duration: 4f);
                        });

                        bool launchedWithNet = _standalonePlay?.Launch(new BlueSky.Networking.StandaloneLaunchOptions
                        {
                            ScenePath = _currentScenePath,
                            Width = 1280,
                            Height = 720,
                            Fullscreen = false,
                            VSync = true,
                            Multiplayer = multiplayer
                        }) ?? false;
                        if (launchedWithNet)
                        {
                            _notificationSystem?.ShowSuccess(
                                launchAsHost ? $"Standalone host launched (PID: {_standalonePlay?.ProcessId})" : $"Standalone client launched (PID: {_standalonePlay?.ProcessId})",
                                duration: 3f);
                        }
                        else
                        {
                            _notificationSystem?.ShowError("Failed to launch standalone", duration: 3f);
                        }
                    }
                }
                else
                {
                    _notificationSystem?.ShowWarning("No scene loaded - save scene first", duration: 3f);
                }
            }
        }

        string rendererName = "Forward+";
        float readyW = MathF.Max(118, _ui.MeasureText(rendererName) + 52);
        EditorChrome.Pill(_ui, w - readyW - 12, tlY + 3, readyW, 20, $"{rendererName} Ready", EditorTheme.Green, filled: _isPlaying);

        // ═══════════════════════════════════════════════════════════════
        //  DOCKING SYSTEM — fills everything below toolbar
        // ═══════════════════════════════════════════════════════════════
        if (_dockingSystem != null)
        {
            // Dock system starts below the toolbar — pass the Y offset
            _dockingSystem.Resize(w, h - totalHeaderH, totalHeaderH);

            var mousePos = _input!.MousePosition;
            bool mouseDown = _input.IsMouseButtonDown(MouseButton.Left);

            _dockingSystem.Update(_ui, mousePos, mouseDown);
        }

        // Re-enable input for the modals so they can be interacted with
        _ui.InputEnabled = true;

        // ── Modal overlays ────────────────────────────────────────────
        if (_showImportDialog)
        {
            DrawImportDialog(_ui, w, h);
        }
        
        if (_showScriptEditor)
        {
            DrawScriptEditor(_ui, w, h);
        }

        if (_staticMeshEditor?.IsOpen ?? false)
        {
            float editorW = 1200, editorH = 700;
            float editorX = (w - editorW) / 2;
            float editorY = (h - editorH) / 2;
            _staticMeshEditor.Render(_ui, editorX, editorY, editorW, editorH);
            
            // Check if editor was closed and clean up
            if (!_staticMeshEditor.IsOpen && _world != null)
            {
                _staticMeshEditor.Close(_world);
            }
        }

        if (_skeletalMeshEditor?.IsOpen ?? false)
        {
            float editorW = 1200, editorH = 700;
            float editorX = (w - editorW) / 2;
            float editorY = (h - editorH) / 2;
            _skeletalMeshEditor.Render(_ui, editorX, editorY, editorW, editorH);

            if (!_skeletalMeshEditor.IsOpen && _world != null)
            {
                _skeletalMeshEditor.Close(_world);
            }
        }

        if (_showRenameDialog)
        {
            DrawRenameDialog(_ui, w, h);
        }
        
        if (_showContextMenu)
        {
            DrawContextMenu(_ui, _contextMenuX, _contextMenuY);
        }

    }

}
