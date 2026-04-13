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

public enum EditorState { ProjectBrowser, Workspace }

class Program
{
    // ── top-level state ────────────────────────────────────────────────────
    private static IWindow?          _window;
    private static IInputContext?    _input;
    private static IRHIDevice?       _rhi;
    private static IRHISwapchain?    _swapchain;
    private static NotBSUI?          _ui;
    private static NotBSUIRenderer?  _uiRenderer;

    // ── Editor state ──────────────────────────────────────────────────────
    private static EditorState _state = EditorState.ProjectBrowser;
    private static string _projectPathInput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MyBlueSkyProject");
    private static string _projectNameInput = "MyGame";
    private static string _openProjectPathInput = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    private static int _projectBrowserTab = 1; // 0 = Projects, 1 = New Project
    private static int _selectedRecentProject = -1;
    private static int _selectedTemplate = 0;
    private static int _selectedCategory = 0; // Blueprint vs C++
    private static string _errorMsg = "";
    private static World? _world;
    private static DockingSystem? _dockingSystem;
    private static BlueSky.Core.Scripting.TeaScriptSystem? _teaScriptSystem;

    // ── Interactive Selection State ─────────────────────────────────────
    private static uint _selectedEntityId = 0;
    private static int _selectedSourceIndex = 0;
    private static int _selectedAssetIndex = -1;
    private static List<string> _consoleLogs = new();
    private static uint _buttonIdCounter = 1000;

    // ── Content Browser State ──────────────────────────────────────────
    private static string _currentBrowserDir = "";
    private static string? _draggedAssetPath = null;
    private static System.Numerics.Vector2 _dragPos;
    private static bool _isDraggingAsset = false;
    private static uint _doubleClickTarget = 0;
    private static double _lastClickTime = 0;
    
    // ── Context Menu State ────────────────────────────────────────────
    private static bool _showContextMenu = false;
    private static float _contextMenuX = 0;
    private static float _contextMenuY = 0;
    private static string _contextMenuPath = "";
    
    // ── Script Editor State ───────────────────────────────────────────
    private static bool _showScriptEditor = false;
    private static string _editingScriptPath = "";
    private static string _editingScriptContent = "";
    private static string _editingScriptName = "";
    private static int _scriptCursorLine = 0;
    private static int _scriptCursorCol = 0;
    private static bool _showRenameDialog = false;
    private static string _renameTarget = "";
    private static string _renameNewName = "";
    
    // ── Play State ────────────────────────────────────────────────────
    private static bool _isPlaying = false;
    private static bool _isPaused = false;
    private static SceneSnapshot? _playModeSnapshot = null;

    // ── Import Dialog State ──────────────────────────────────────────────
    private static bool _showImportDialog = false;
    private static string[] _pendingImportFiles = Array.Empty<string>();
    private static float _importScale = 1.0f;
    private static bool _importGenerateCollider = true;
    private static bool _importImportMaterials = true;
    private static int _importSelectedMeshIndex = 0;
    private static string[] _importMeshPreviewNames = Array.Empty<string>();

    // ── Viewport 3D rendering ─────────────────────────────────────────
    private static ViewportRenderer? _viewportRenderer;
    private static BlueSky.Rendering.Viewport? _viewport;
    private static IRHITexture? _depthTexture;
    private static DockRect _viewportPanelRect;
    private static uint _depthW, _depthH;
    private static DockRect _lastViewportRect;
    private static bool _viewportNeedsRender;

    // ── Timing ────────────────────────────────────────────────────────
    private static Stopwatch _stopwatch = new();
    private static float _deltaTime;
    private static float _lastFrameTime;

    private static string _frameTypedText = "";
    private static bool _frameBackspacePressed = false;

    // ─────────────────────────────────────────────────────────────────────
    public static void Main(string[] args)
    {
        try   { Run(args); }
        catch (Exception ex)
        {
            var msg = $"[CRASH] {ex}";
            Console.Error.WriteLine(msg);
            File.WriteAllText("bluesky_crash.log", msg);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private static void Run(string[] args)
    {
        // ── window ────────────────────────────────────────────────────────
        var options = WindowOptions.Default;
        options.Title = "BlueSky Engine";
        options.Width = 1280;
        options.Height = 720;
        options.Resizable = true; // Ensure it's resizable!
        _window = WindowFactory.Create(options);

        // ── project config ────────────────────────────────────────────────
        ProjectConfig.Load();
        ProjectConfig.ScanDesktopForProjects();

        // ── RHI ───────────────────────────────────────────────────────────
        _input    = _window.CreateInput();
        _rhi      = RHIDevice.CreateDefault(_window);
        _swapchain = _rhi.CreateSwapchain(_window, PresentMode.Vsync);

        // ── UI system ─────────────────────────────────────────────────────
        _ui         = new NotBSUI((uint)_window.Size.X, (uint)_window.Size.Y);
        _uiRenderer = new NotBSUIRenderer(_rhi);
        var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roboto.ttf");
        
        if (!File.Exists(fontPath))
            fontPath = Path.Combine(Directory.GetCurrentDirectory(), "roboto.ttf");
        
        if (!File.Exists(fontPath))
            fontPath = Path.Combine(Directory.GetCurrentDirectory(), "Editor", "roboto.ttf");
        
        // Fallback to system font if roboto.ttf is not found
        if (!File.Exists(fontPath))
        {
            Console.WriteLine("[WARNING] roboto.ttf not found, using system font fallback");
            if (OperatingSystem.IsMacOS())
            {
                fontPath = "/System/Library/Fonts/Helvetica.ttc";
            }
            else if (OperatingSystem.IsWindows())
            {
                fontPath = @"C:\Windows\Fonts\arial.ttf";
            }
            else
            {
                fontPath = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
            }
        }
            
        _uiRenderer.FontAtlas = new FontAtlas(_rhi, fontPath);
        _uiRenderer.Resize((int)_window.Size.X, (int)_window.Size.Y);

        // Immediately resize swapchain to actual pixel dimensions (Retina)
        var fbSize = _window.FramebufferSize;
        _swapchain.Resize((uint)fbSize.X, (uint)fbSize.Y);

        // ── Input binding ─────────────────────────────────────────────────
        _input.CharInput += c => 
        {
            _frameTypedText += c;
            Console.WriteLine($"[CharInput] Received: '{c}' (code: {(int)c}) - frameTypedText now: '{_frameTypedText}'");
        };
        _input.KeyDown += (k, m) =>
        {
            if (k == KeyCode.Backspace)
            {
                _frameBackspacePressed = true;
                Console.WriteLine("[KeyDown] Backspace pressed");
            }
            if (k == KeyCode.I && m.HasFlag(ModifierKeys.Super)) ImportFilesDialog();
        };

        // ── resize handler ────────────────────────────────────────────────
        _window.Resize += size =>
        {
            _ui.Resize((uint)size.X, (uint)size.Y);
            _uiRenderer.Resize((int)size.X, (int)size.Y);
        };
        _window.FramebufferResize += size =>
        {
            _swapchain?.Resize((uint)size.X, (uint)size.Y);
        };

        // ── Drag and drop for asset import ────────────────────────────────
        if (_window is Platform.macOS.CocoaWindow cocoaWindow)
        {
            cocoaWindow.FilesDropped += files =>
            {
                HandleFilesDropped(files);
            };
        }

        _window.Show();
        _stopwatch.Start();

        // ── main loop ────────────────────────────────────────────────────
        while (!_window.IsClosing)
        {
            // Delta time
            float now = (float)_stopwatch.Elapsed.TotalSeconds;
            _deltaTime = now - _lastFrameTime;
            _lastFrameTime = now;

            _input.BeginFrame();
            _window.ProcessEvents();

            // Update viewport camera (only in workspace)
            if (_state == EditorState.Workspace && _viewport != null)
            {
                _viewport.Update(_deltaTime);
            }

            // Update TeaScript system
            if (_state == EditorState.Workspace && _teaScriptSystem != null && _isPlaying && !_isPaused)
            {
                _teaScriptSystem.Update(_deltaTime);
            }

            RenderFrame();

            // Clear input state AFTER rendering (so modals can use it)
            _frameTypedText = "";
            _frameBackspacePressed = false;
        }

        Cleanup();
    }

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
            if (_window is Platform.macOS.CocoaWindow cocoaWindow)
            {
                var files = cocoaWindow.ShowOpenFileDialog();
                if (files != null && files.Length > 0)
                {
                    HandleFilesDropped(files);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Error opening file dialog: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private static void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string logEntry = $"[{timestamp}] {message}";
        _consoleLogs.Add(logEntry);
        Console.WriteLine(logEntry);
        
        // Keep only last 100 messages
        if (_consoleLogs.Count > 100)
            _consoleLogs.RemoveAt(0);
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
            string[] meshExtensions = { ".obj", ".fbx", ".gltf", ".glb" };
            var meshFiles = files.Where(f => meshExtensions.Contains(Path.GetExtension(f).ToLower())).ToArray();
            
            if (meshFiles.Length > 0)
            {
                // Show import dialog for mesh files
                ShowImportDialog(meshFiles);
            }

            // Handle other file types (textures, etc.) immediately
            string[] otherExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".blueskyasset" };
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
                    string destPath = Path.Combine(ProjectManager.AssetsDir!, Path.GetFileName(file));
                    File.Copy(file, destPath, overwrite: true);
                    Log($"✓ Copied: {Path.GetFileName(destPath)}");
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
    private static void EnsureDepthTexture(uint width, uint height)
    {
        if (_depthTexture != null && _depthW == width && _depthH == height)
            return;
        _depthTexture?.Dispose();
        _depthTexture = _rhi!.CreateTexture(new TextureDesc
        {
            Width = width, Height = height, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage  = TextureUsage.DepthStencil,
            DebugName = "MainDepth",
        });
        _depthW = width;
        _depthH = height;
    }

    private static void RenderFrame()
    {
        _swapchain!.AcquireNextImage();

        var cmd = _rhi!.CreateCommandBuffer();

        if (_state == EditorState.Workspace && _viewportRenderer != null)
        {
            var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f, 0.6f, 0.3f));
            _viewportRenderer.PreRender(cmd, sunDir);
        }

        // Use FramebufferSize for actual pixel bounds (crucial for High-DPI/Retina)
        var w = (uint)_window!.FramebufferSize.X;
        var h = (uint)_window.FramebufferSize.Y;
        // Logical size for UI coordinate system
        var logW = _window.Size.X;
        var logH = _window.Size.Y;

        // Always use depth buffer (UI pipeline also declares Depth32Float)
        EnsureDepthTexture(w, h);
        // ── Shadow pass (render shadows to shadow map) ─────────────────────
        if (_viewportRenderer != null && _viewport != null)
        {
            var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f, 0.6f, 0.3f));
            _viewportRenderer.PreRender(cmd, sunDir);
        }

        cmd.BeginRenderPass(
            new[] { _swapchain.CurrentRenderTarget },
            _depthTexture,
            new ClearValue { Color = new System.Numerics.Vector4(0.11f, 0.11f, 0.12f, 1f), Depth = 1.0f }
        );

        cmd.SetViewport(new NotBSRenderer.Viewport { X = 0, Y = 0, Width = w, Height = h, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new Scissor  { X = 0, Y = 0, Width = w, Height = h });

        var mousePos  = _input!.MousePosition;
        var mouseDown = _input.IsMouseButtonDown(MouseButton.Left);
        
        _ui!.Time = _stopwatch!.Elapsed.TotalSeconds;
        
        // Don't pass input to UI system if a modal is open (script editor, rename dialog)
        string uiTypedText = (_showScriptEditor || _showRenameDialog) ? "" : _frameTypedText;
        bool uiBackspace = (_showScriptEditor || _showRenameDialog) ? false : _frameBackspacePressed;
        
        _ui!.BeginFrame(mousePos, mouseDown, uiTypedText, uiBackspace);
        
        if (!mouseDown && _isDraggingAsset)
        {
            _isDraggingAsset = false;
            
            // If dropped over viewport, spawn!
            if (_lastViewportRect.W > 0 && 
                mousePos.X >= _lastViewportRect.X && mousePos.X <= _lastViewportRect.X + _lastViewportRect.W &&
                mousePos.Y >= _lastViewportRect.Y && mousePos.Y <= _lastViewportRect.Y + _lastViewportRect.H &&
                _draggedAssetPath != null)
            {
                SpawnDraggedAsset(_draggedAssetPath);
            }
            _draggedAssetPath = null;
        }

        // Mark that viewport hasn't been rendered yet this frame
        _viewportNeedsRender = (_state == EditorState.Workspace && _viewportRenderer != null);

        if (_state == EditorState.ProjectBrowser)
        {
            BuildProjectBrowserUI();
        }
        else
        {
            BuildWorkspaceUI();
        }

        _uiRenderer!.Render(cmd, _ui!);

        // ── Render 3D viewport content AFTER UI layout is calculated ───────
        // The viewport uses scissor testing to only draw within its bounds
        if (_viewportNeedsRender && _viewport != null)
        {
            float vpX = _lastViewportRect.X;
            float vpY = _lastViewportRect.Y; // dock rect already includes header offset
            float vpW = _lastViewportRect.W;
            float vpH = _lastViewportRect.H;

            if (vpW > 1 && vpH > 1)
            {
                _viewport.SetViewportRect(vpX, vpY, vpW, vpH);

                // Need to compute pixel scaled viewport for the 3D renderer since it writes to the high-dpi backbuffer
                float scaleX = w / _window.Size.X;
                float scaleY = h / _window.Size.Y;
                float vpXPx = vpX * scaleX;
                float vpYPx = vpY * scaleY;
                float vpWPx = vpW * scaleX;
                float vpHPx = vpH * scaleY;

                var view = _viewport.GetViewMatrixNumerics();
                var proj = _viewport.GetProjectionMatrixNumerics();
                var camPos = _viewport.GetCameraPositionNumerics();

                // Use depth testing to prevent viewport from drawing over UI
                // The UI renderer should have written depth values
                _viewportRenderer!.Render(cmd, view, proj, camPos,
                    (int)vpXPx, (int)vpYPx, (int)vpWPx, (int)vpHPx, _deltaTime);

                // Reset viewport/scissor back to full window
                cmd.SetViewport(new NotBSRenderer.Viewport { X = 0, Y = 0, Width = w, Height = h, MinDepth = 0, MaxDepth = 1 });
                cmd.SetScissor(new Scissor { X = 0, Y = 0, Width = w, Height = h });
            }
        }

        cmd.EndRenderPass();
        _rhi.Submit(cmd, _swapchain);
        _swapchain.Present();
        cmd.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────
    private static void BuildProjectBrowserUI()
    {
        float w = _window!.Size.X;
        float h = _window!.Size.Y;

        // ── Full-screen background ─────────────────────────────────────
        _ui!.Panel(0, 0, w, h, EditorTheme.LauncherBg);

        // ═══════════════════════════════════════════════════════════════
        //  SIDEBAR — fixed-width left panel with branding + navigation
        // ═══════════════════════════════════════════════════════════════
        float sideW = 240;
        _ui.Panel(0, 0, sideW, h, EditorTheme.LauncherSidebar);
        _ui.Panel(sideW - 1, 0, 1, h, EditorTheme.Border0);  // right divider

        // ── Branding ──────────────────────────────────────────────────
        _ui.SetCursor(24, 28);
        _ui.Text("BlueSky", EditorTheme.LauncherBrand);
        _ui.SetCursor(24, 48);
        _ui.Text("ENGINE", EditorTheme.TextMuted);

        // Subtle brand accent line
        _ui.Panel(24, 72, 48, 2, EditorTheme.Accent);

        // ── Navigation tabs ───────────────────────────────────────────
        float navY = 100;
        string[] navItems = { "New Project", "Open Project" };
        string[] navIcons = { "+", "◈" };
        int[] navTabs = { 1, 0 };

        for (int i = 0; i < navItems.Length; i++)
        {
            bool isSel = _projectBrowserTab == navTabs[i];
            float rowH = 36;
            uint navId = 100u + (uint)i;

            // Row background
            var rowBg = isSel ? EditorTheme.SelectionBg : EditorTheme.WithAlpha(EditorTheme.LauncherSidebar, 0f);
            if (_ui.ClickableCard(8, navY, sideW - 16, rowH, navId,
                rowBg,
                EditorTheme.HoverBg,
                EditorTheme.SelectionBg))
            {
                _projectBrowserTab = navTabs[i];
            }

            // Selection indicator
            if (isSel)
                _ui.Panel(8, navY, 3, rowH, EditorTheme.Accent);

            // Icon + text
            _ui.SetCursor(24, navY + 10);
            _ui.Text(navIcons[i], isSel ? EditorTheme.Accent : EditorTheme.TextMuted);
            _ui.SetCursor(44, navY + 10);
            _ui.Text(navItems[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

            navY += rowH + 4;
        }

        // ── Sidebar footer ────────────────────────────────────────────
        _ui.Panel(24, h - 60, sideW - 48, 1, EditorTheme.Border1);
        _ui.SetCursor(24, h - 42);
        _ui.Text("v0.1.0-alpha", EditorTheme.TextDisabled);

        // ═══════════════════════════════════════════════════════════════
        //  MAIN CONTENT AREA
        // ═══════════════════════════════════════════════════════════════
        float cX = sideW + 32;
        float cW = w - sideW - 64;

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
            string[] templateIcons = { "◇", "△", "▢", "◉", "▽", "◁" };
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
                _ui.SetCursor(iconX + 20, iconY + 16);
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
                    _ui.SetCursor(ax + cardW - 22, ay + 8);
                    _ui.Text("✓", EditorTheme.Green);
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
            _ui.TextField(ref _projectNameInput, Math.Min(cW * 0.4f, 340), 32);

            // Location (same row, right side)
            float locX = cX + Math.Min(cW * 0.4f, 340) + 24;
            float locW = cW - Math.Min(cW * 0.4f, 340) - 24 - 120;
            _ui.SetCursor(locX, formY - 22);
            _ui.Text("Location", EditorTheme.TextSecondary);
            _ui.SetCursor(locX, formY);
            _ui.TextField(ref _projectPathInput, Math.Max(locW, 200), 32);

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
            _ui.TextField(ref _openProjectPathInput, pathW, 32);

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
        Console.WriteLine("[Editor] TeaScriptSystem initialized");

        // Create a simple cube entity with TransformComponent
        var entity1 = _world.CreateEntity();
        var transform1 = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(0, 1, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = BlueSky.Core.Math.Vector3.One
        };
        _world.AddComponent(entity1, transform1);

        // Create a second cube entity
        var entity2 = _world.CreateEntity();
        var transform2 = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(2, 1, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = BlueSky.Core.Math.Vector3.One
        };
        _world.AddComponent(entity2, transform2);

        // Create a third cube entity
        var entity3 = _world.CreateEntity();
        var transform3 = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(-2, 1, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = BlueSky.Core.Math.Vector3.One
        };
        _world.AddComponent(entity3, transform3);

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
            _viewportRenderer = new ViewportRenderer(_rhi!, _world!);
            _viewport = new BlueSky.Rendering.Viewport(_window!, _input!, _world, new StubRenderer());
            Console.WriteLine("[Editor] ViewportRenderer initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] ViewportRenderer init failed: {ex.Message}");
            _viewportRenderer = null;
            _viewport = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private static void BuildWorkspaceUI()
    {
        float w = _window!.Size.X;
        float h = _window!.Size.Y;
        float menuH = EditorTheme.HeaderH;
        float toolbarH = EditorTheme.ToolbarH;
        float totalHeaderH = menuH + toolbarH;

        // ═══════════════════════════════════════════════════════════════
        //  MENU BAR — slim, professional, with proper spacing
        // ═══════════════════════════════════════════════════════════════
        _ui!.Panel(0, 0, w, menuH, EditorTheme.Bg0);
        _ui.Panel(0, menuH - 1, w, 1, EditorTheme.Border0);

        // Engine branding (left)
        _ui.SetCursor(14, menuH / 2 - 6);
        _ui.Text("BlueSky", EditorTheme.LauncherBrand);

        // Menu items with proper spacing
        string[] menus = { "File", "Edit", "Window", "Tools", "Build", "Help" };
        float menuX = 100;
        foreach (var m in menus)
        {
            bool menuHot = _ui.IsHovering(menuX - 6, 0, m.Length * 7.5f + 12, menuH);
            if (menuHot)
                _ui.Panel(menuX - 6, 2, m.Length * 7.5f + 12, menuH - 4, EditorTheme.HoverBg);

            _ui.SetCursor(menuX, menuH / 2 - 6);
            _ui.Text(m, menuHot ? EditorTheme.TextPrimary : EditorTheme.TextMuted);
            menuX += m.Length * 7.5f + 22;
        }

        // Project name + FPS (right side)
        string projName = Path.GetFileName(ProjectManager.CurrentProjectDir ?? "Untitled");
        float fps = _deltaTime > 0 ? 1f / _deltaTime : 0;
        _ui.SetCursor(w - 200, menuH / 2 - 6);
        _ui.Text($"{projName}  |  {fps:F0} FPS", EditorTheme.TextDisabled);

        // ═══════════════════════════════════════════════════════════════
        //  TOOLBAR — slim, functional
        // ═══════════════════════════════════════════════════════════════
        _ui.Panel(0, menuH, w, toolbarH, EditorTheme.ToolbarBg);
        _ui.Panel(0, menuH + toolbarH - 1, w, 1, EditorTheme.Border0);

        float btnH = 20;
        float tlX = 14;
        float tlY = menuH + (toolbarH - btnH) / 2;

        _ui.ButtonEx(tlX, tlY, 40, btnH, "Save",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 600);
        tlX += 46;

        _ui.Panel(tlX, menuH + 8, 1, toolbarH - 16, EditorTheme.Border1);
        tlX += 8;

        _ui.ButtonEx(tlX, tlY, 42, btnH, "Undo",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 601);
        tlX += 48;

        _ui.ButtonEx(tlX, tlY, 42, btnH, "Redo",
            EditorTheme.ToolbarBtnNormal, EditorTheme.ToolbarBtnHover,
            EditorTheme.AccentDim, new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.TextSecondary, 602);

        // Center: Play / Pause / Stop
        float tcW = 150;
        float tcX = (w - tcW) / 2;
        float tcY = tlY; // Same Y as left buttons

        if (_ui.ButtonEx(tcX, tcY, 44, btnH, _isPlaying ? "▶" : "Play",
            _isPlaying ? EditorTheme.PlayGreen : EditorTheme.ToolbarBtnNormal,
            EditorTheme.Lighten(EditorTheme.PlayGreen, 0.15f),
            EditorTheme.PlayGreen,
            new System.Numerics.Vector4(0,0,0,0),
            _isPlaying ? EditorTheme.TextPrimary : EditorTheme.PlayGreen, 610))
        {
            if (!_isPlaying)
            {
                // Capture scene state before entering Play mode
                if (_world != null)
                {
                    _playModeSnapshot = new SceneSnapshot();
                    _playModeSnapshot.Capture(_world);
                }
                
                _isPlaying = true;
                _isPaused = false;
                Log("▶ Play mode started - scripts running");
            }
        }

        if (_ui.ButtonEx(tcX + 50, tcY, 44, btnH, "Pause",
            _isPaused ? EditorTheme.PauseYellow : EditorTheme.ToolbarBtnNormal,
            EditorTheme.Lighten(EditorTheme.PauseYellow, 0.15f),
            EditorTheme.PauseYellow,
            new System.Numerics.Vector4(0,0,0,0),
            _isPaused ? EditorTheme.TextPrimary : EditorTheme.PauseYellow, 611))
        {
            if (_isPlaying)
            {
                _isPaused = !_isPaused;
                Log(_isPaused ? "⏸ Paused" : "▶ Resumed");
            }
        }

        if (_ui.ButtonEx(tcX + 100, tcY, 44, btnH, "Stop",
            EditorTheme.ToolbarBtnNormal,
            EditorTheme.Lighten(EditorTheme.StopRed, 0.15f),
            EditorTheme.StopRed,
            new System.Numerics.Vector4(0,0,0,0),
            EditorTheme.StopRed, 612))
        {
            if (_isPlaying)
            {
                _isPlaying = false;
                _isPaused = false;
                
                // Restore scene state to pre-Play mode
                if (_world != null && _playModeSnapshot != null)
                {
                    _playModeSnapshot.Restore(_world);
                    _playModeSnapshot.Clear();
                    _playModeSnapshot = null;
                }
                
                Log("⏹ Stopped - scene restored to editor state");
                // Reset all scripts
                HotReloadScripts();
            }
        }

        _ui.SetCursor(w - 80, menuH + toolbarH / 2 - 6);
        _ui.Text("Ready", EditorTheme.Green);

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

        // ── Modal overlays ────────────────────────────────────────────
        if (_showImportDialog)
        {
            DrawImportDialog(_ui, w, h);
        }
        
        if (_showScriptEditor)
        {
            DrawScriptEditor(_ui, w, h);
        }
        
        if (_showRenameDialog)
        {
            DrawRenameDialog(_ui, w, h);
        }
        
        if (_showContextMenu)
        {
            DrawContextMenu(_ui, _contextMenuX, _contextMenuY);
        }

        _ui.EndFrame();
    }

    // ── Dockable Panel Content Callbacks ──────────────────────────────

    private static void DrawViewportPanel(NotBSUI ui, DockRect rect)
    {
        _lastViewportRect = rect;

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

        if (_viewportRenderer == null)
        {
            ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg0);
            float cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2;
            ui.SetCursor(cx - 80, cy - 8);
            ui.Text("3D Viewport", EditorTheme.TextDisabled);
            ui.SetCursor(cx - 60, cy + 12);
            ui.Text("No renderer attached", EditorTheme.TextDisabled);
        }

        // ── Viewport toolbar (semi-transparent overlay) ───────────────
        float tbH = 28;
        ui.Panel(rect.X, rect.Y, rect.W, tbH, EditorTheme.WithAlpha(EditorTheme.Bg0, 0.88f));
        ui.Panel(rect.X, rect.Y + tbH - 1, rect.W, 1, EditorTheme.WithAlpha(EditorTheme.Border0, 0.5f));

        // Transform tools
        string[] tools = { "W Move", "E Rotate", "R Scale" };
        float tx = rect.X + 8;
        for (int i = 0; i < tools.Length; i++)
        {
            uint toolId = 700u + (uint)i;
            float tw = tools[i].Length * 7.2f + 16;
            ui.ButtonEx(tx, rect.Y + 3, tw, 22, tools[i],
                EditorTheme.WithAlpha(EditorTheme.ToolbarBtnNormal, 0.6f),
                EditorTheme.WithAlpha(EditorTheme.ToolbarBtnHover, 0.8f),
                EditorTheme.Accent,
                new System.Numerics.Vector4(0, 0, 0, 0),
                EditorTheme.TextSecondary, toolId);
            tx += tw + 4;
        }

        // Separator
        tx += 4;
        ui.Panel(tx, rect.Y + 6, 1, 16, EditorTheme.Border1);
        tx += 8;

        // View modes
        string[] modes = { "Lit", "Wireframe", "Unlit" };
        for (int i = 0; i < modes.Length; i++)
        {
            uint modeId = 710u + (uint)i;
            float mw = modes[i].Length * 7.2f + 12;
            ui.ButtonEx(tx, rect.Y + 3, mw, 22, modes[i],
                EditorTheme.WithAlpha(EditorTheme.ToolbarBtnNormal, 0.6f),
                EditorTheme.WithAlpha(EditorTheme.ToolbarBtnHover, 0.8f),
                EditorTheme.Accent,
                new System.Numerics.Vector4(0, 0, 0, 0),
                i == 0 ? EditorTheme.TextPrimary : EditorTheme.TextMuted, modeId);
            tx += mw + 4;
        }

        // ── Camera info overlay (bottom-left) ────────────────────────
        if (_viewport != null)
        {
            var camPos = _viewport.GetCameraPositionNumerics();
            float infoH = 20;
            ui.Panel(rect.X, rect.Y + rect.H - infoH, 220, infoH,
                EditorTheme.WithAlpha(EditorTheme.Bg0, 0.7f));
            ui.SetCursor(rect.X + 8, rect.Y + rect.H - 16);
            ui.Text($"Cam ({camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1})",
                EditorTheme.WithAlpha(EditorTheme.TextMuted, 0.8f));
        }
    }

    private static void DrawOutlinerPanel(NotBSUI ui, DockRect rect)
    {
        float inset = EditorTheme.Pad;
        float iconCol = rect.X + inset + 6;
        float nameCol = rect.X + inset + 24;

        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        float searchY = rect.Y + inset;
        ui.Panel(rect.X + inset, searchY, rect.W - inset * 2, 26, EditorTheme.Bg0);
        ui.Panel(rect.X + inset, searchY + 25, rect.W - inset * 2, 1, EditorTheme.Border1);
        ui.SetCursor(rect.X + inset + 8, searchY + 6);
        ui.Text("Search actors...", EditorTheme.TextDisabled);

        float listY = searchY + 38;

        if (_world != null)
        {
            ui.SetCursor(iconCol, listY);
            ui.Text("\u25BC  Persistent Level", EditorTheme.TextMuted);
            listY += EditorTheme.RowH + 4;

            ui.Panel(rect.X + inset, listY, rect.W - inset * 2, 1, EditorTheme.Border1);
            listY += 12;

            string[] systemItems = { "DirectionalLight", "SkyAtmosphere", "PostProcessVolume" };
            string[] systemIcons = { "\u2600", "\u2601", "\u25C9" };
            for (int i = 0; i < systemItems.Length; i++)
            {
                uint id = 200u + (uint)i;
                bool isSel = _selectedEntityId == id;

                if (ui.ClickableCard(rect.X + 6, listY, rect.W - 12, EditorTheme.RowH,
                    id,
                    isSel ? EditorTheme.SelectionBg : EditorTheme.Bg1,
                    EditorTheme.HoverBg,
                    EditorTheme.SelectionBg))
                {
                    _selectedEntityId = id;
                    Log($"Selected: {systemItems[i]}");
                }

                if (isSel)
                    ui.Panel(rect.X + 6, listY, 3, EditorTheme.RowH, EditorTheme.Accent);

                ui.SetCursor(iconCol, listY + 6);
                ui.Text(systemIcons[i], EditorTheme.TextMuted);
                ui.SetCursor(nameCol, listY + 6);
                ui.Text(systemItems[i], isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
                listY += EditorTheme.RowH + 2;
            }

            ui.Panel(rect.X + inset, listY + 6, rect.W - inset * 2, 1, EditorTheme.Border1);
            listY += 16;

            var entities = _world.GetAllEntities().ToList();
            for (int i = 0; i < entities.Count && listY < rect.Y + rect.H - 32; i++)
            {
                var entity = entities[i];
                uint id = (uint)entity.Id;
                bool isSel = _selectedEntityId == id;

                if (ui.ClickableCard(rect.X + 6, listY, rect.W - 12, EditorTheme.RowH,
                    id,
                    isSel ? EditorTheme.SelectionBg : EditorTheme.Bg1,
                    EditorTheme.HoverBg,
                    EditorTheme.SelectionBg))
                {
                    _selectedEntityId = id;
                    Log($"Selected Entity_{entity.Id}");
                }

                if (isSel)
                    ui.Panel(rect.X + 6, listY, 3, EditorTheme.RowH, EditorTheme.Accent);

                ui.SetCursor(iconCol, listY + 6);
                ui.Text("\u25A3", EditorTheme.Orange);
                ui.SetCursor(nameCol, listY + 6);
                ui.Text($"Entity_{entity.Id}", isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                listY += EditorTheme.RowH + 2;
            }

            ui.Panel(rect.X + inset, rect.Y + rect.H - 28, rect.W - inset * 2, 1, EditorTheme.Border1);
            ui.SetCursor(iconCol, rect.Y + rect.H - 20);
            ui.Text($"{entities.Count} actors", EditorTheme.TextDisabled);
        }
        else
        {
            ui.SetCursor(iconCol, rect.Y + 80);
            ui.Text("No level loaded", EditorTheme.TextMuted);
        }
    }

    private static void DrawDetailsPanel(NotBSUI ui, DockRect rect)
    {
        float inset = EditorTheme.Pad;
        float labelCol = rect.X + inset + 4;
        float valueCol = rect.X + EditorTheme.PropLabelW + inset;

        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        // Get selected entity info
        string entityName = "None Selected";
        string pos = "0.0, 0.0, 0.0";
        string rot = "0.0, 0.0, 0.0";
        string scale = "1.0, 1.0, 1.0";
        bool hasMesh = false;

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
                var entity = _world.GetAllEntities().FirstOrDefault(e => e.Id == _selectedEntityId);
                if (entity.Id != 0)
                {
                    entityName = $"Entity_{entity.Id}";
                    if (_world.TryGetComponent<TransformComponent>(entity, out var transform))
                    {
                        pos = $"{transform.Position.X:F1}, {transform.Position.Y:F1}, {transform.Position.Z:F1}";
                        scale = $"{transform.Scale.X:F1}, {transform.Scale.Y:F1}, {transform.Scale.Z:F1}";
                    }
                    hasMesh = _world.TryGetComponent<MeshComponent>(entity, out _);
                }
            }
        }

        float y = rect.Y + EditorTheme.PadLg;

        // Entity name header
        ui.Panel(rect.X + inset, y, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg2);
        ui.SetCursor(labelCol + 2, y + 8);
        ui.Text(entityName, EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + 8;

        ui.Panel(rect.X + inset, y, rect.W - inset * 2, 1, EditorTheme.Border1);
        y += EditorTheme.PadLg;

        // Transform Section
        ui.Panel(rect.X + inset, y, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg3);
        ui.Panel(rect.X + inset, y, 3, EditorTheme.SectionH, EditorTheme.Accent);
        ui.SetCursor(labelCol + 6, y + 8);
        ui.Text("▼ Transform", EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + EditorTheme.Pad;

        // Property rows with aligned columns
        string[] labels = { "Location", "Rotation", "Scale" };
        string[] values = { pos, rot, scale };
        for (int i = 0; i < labels.Length; i++)
        {
            if (i % 2 == 0)
                ui.Panel(rect.X + inset, y - 2, rect.W - inset * 2, 24, EditorTheme.WithAlpha(EditorTheme.Bg0, 0.35f));

            ui.SetCursor(labelCol, y + 2);
            ui.Text(labels[i], EditorTheme.TextMuted);
            ui.SetCursor(valueCol, y + 2);
            ui.Text(values[i], EditorTheme.AccentHover);
            y += 26;
        }
        y += EditorTheme.PadLg;

        // Static Mesh Section
        ui.Panel(rect.X + inset, y, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg3);
        ui.Panel(rect.X + inset, y, 3, EditorTheme.SectionH, EditorTheme.Purple);
        ui.SetCursor(labelCol + 6, y + 8);
        ui.Text("▼ Static Mesh", EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + EditorTheme.Pad;

        ui.SetCursor(labelCol, y + 2);
        ui.Text("Mesh", EditorTheme.TextMuted);
        ui.SetCursor(valueCol, y + 2);
        ui.Text(hasMesh ? "Cube.mesh" : "None", hasMesh ? EditorTheme.AccentHover : EditorTheme.TextDisabled);
        y += 26;
        y += EditorTheme.PadLg;
        
        // TeaScript Section
        bool hasScript = false;
        string scriptPath = "None";
        bool scriptEnabled = false;
        
        if (_world != null && _selectedEntityId > 0 && _selectedEntityId < 200)
        {
            var entity = _world.GetAllEntities().FirstOrDefault(e => e.Id == _selectedEntityId);
            if (entity.Id != 0 && _world.TryGetComponent<TeaScriptComponent>(entity, out var scriptComp))
            {
                hasScript = true;
                scriptPath = string.IsNullOrEmpty(scriptComp.ScriptAssetId) ? "None" : Path.GetFileName(scriptComp.ScriptAssetId);
                scriptEnabled = scriptComp.IsEnabled;
            }
        }
        
        ui.Panel(rect.X + inset, y, rect.W - inset * 2, EditorTheme.SectionH, EditorTheme.Bg3);
        ui.Panel(rect.X + inset, y, 3, EditorTheme.SectionH, EditorTheme.Green);
        ui.SetCursor(labelCol + 6, y + 8);
        ui.Text("▼ TeaScript", EditorTheme.TextPrimary);
        y += EditorTheme.SectionH + EditorTheme.Pad;
        
        ui.SetCursor(labelCol, y + 2);
        ui.Text("Script", EditorTheme.TextMuted);
        ui.SetCursor(valueCol, y + 2);
        ui.Text(scriptPath, hasScript ? EditorTheme.Green : EditorTheme.TextDisabled);
        y += 26;
        
        ui.SetCursor(labelCol, y + 2);
        ui.Text("Enabled", EditorTheme.TextMuted);
        ui.SetCursor(valueCol, y + 2);
        ui.Text(scriptEnabled ? "Yes" : "No", scriptEnabled ? EditorTheme.Green : EditorTheme.TextDisabled);

        // Component count pinned to bottom
        ui.Panel(rect.X + inset, rect.Y + rect.H - 28, rect.W - inset * 2, 1, EditorTheme.Border1);
        ui.SetCursor(labelCol, rect.Y + rect.H - 20);
        int compCount = (hasMesh ? 1 : 0) + (hasScript ? 1 : 0) + 1; // +1 for transform
        ui.Text($"{compCount} components", EditorTheme.TextDisabled);
    }


    private static void DrawContentBrowserPanel(NotBSUI ui, DockRect rect)
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
        float sidebarW = 130;
        float toolbarH = 28;
        float viewBarH = 0; // removed view bar
        float statusbarH = EditorTheme.StatusH;

        // ── Background ──────────────────────────────────────────────────────
        ui.Panel(rect.X, rect.Y, rect.W, rect.H, bgBase);

        // ── TOOLBAR ───────────────────────────────────────────────────────────
        ui.Panel(rect.X, rect.Y, rect.W, toolbarH, bgPanel);
        ui.Panel(rect.X, rect.Y + toolbarH - 1, rect.W, 1, borderSubtle);

        // Breadcrumb — vertically centered in toolbar
        float bcY = rect.Y + (toolbarH - 12) / 2;
        float tx = rect.X + 12;
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
        if (ui.ButtonEx(rect.X + rect.W - 90, rect.Y + (toolbarH - 22) / 2, 80, 22, "+ Import",
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

        // Tree items — no header, start immediately
        float treeY = sidebarY + 6;
        string[] sources = { "Content", "Collections", "Shared" };
        string[] icons = { "\u25a3", "\u2605", "\u25c8" };
        for (int i = 0; i < sources.Length; i++)
        {
            uint sourceId = 7000u + (uint)i;
            bool isSel = _selectedSourceIndex == i;
            
            // Better row colors
            var rowBg = isSel ? EditorTheme.SelectionBg : EditorTheme.WithAlpha(EditorTheme.Bg2, 0f);
            var txtCol = isSel ? accentBlueLight : textSecondary;
            float rowH = 26; // Smaller rows

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
            ui.SetCursor(rowX + 10, treeY + 6);
            ui.Text(icons[i], isSel ? EditorTheme.TextPrimary : textSecondary);
            ui.SetCursor(rowX + 28, treeY + 6);
            ui.Text(sources[i], txtCol);
            
            treeY += rowH + 2; // tighter packing
        }
        


        // ── MAIN CONTENT ───────────────────────────────────────────────────
        float contentX = rect.X + sidebarW;
        float contentY = rect.Y + toolbarH;
        float contentW = rect.W - sidebarW;
        float contentH = rect.H - toolbarH - statusbarH;

        // ── ASSET GRID — starts immediately below toolbar ───────────
        float gridY = contentY + 8;
        float itemW = 90, itemH = 100; // Smaller cards
        float gap = 12; // tighter gap
        float thumbSize = 56;

        if (string.IsNullOrEmpty(_currentBrowserDir) || !Directory.Exists(_currentBrowserDir))
        {
            _currentBrowserDir = ProjectManager.AssetsDir ?? "";
        }

        if (!string.IsNullOrEmpty(_currentBrowserDir) && Directory.Exists(_currentBrowserDir))
        {
            string[] dirs = Directory.GetDirectories(_currentBrowserDir);
            string[] files = Directory.GetFiles(_currentBrowserDir);

            float cx = contentX + 16;
            float cy = gridY;

            // Optional: Back Button
            if (_currentBrowserDir != ProjectManager.AssetsDir && ProjectManager.AssetsDir != null)
            {
                if (cx + itemW > contentX + contentW - 16) { cx = contentX + 16; cy += itemH + gap; }
                
                uint backId = 4999u;
                bool isBackSel = _selectedAssetIndex == (int)backId;
                
                if (ui.ClickableCard(cx, cy, itemW, itemH, backId, bgCard, bgCardHover, new System.Numerics.Vector4(0.28f, 0.48f, 0.78f, 0.5f)))
                {
                    _selectedAssetIndex = (int)backId;
                    
                    double now = ui.Time;
                    if (_doubleClickTarget == backId && (now - _lastClickTime) < 0.3)
                    {
                        var parentDir = Directory.GetParent(_currentBrowserDir)?.FullName;
                        if (parentDir != null && parentDir.StartsWith(ProjectManager.AssetsDir))
                        {
                            _currentBrowserDir = parentDir;
                            _selectedAssetIndex = -1;
                        }
                        else
                        {
                            _currentBrowserDir = ProjectManager.AssetsDir;
                            _selectedAssetIndex = -1;
                        }
                    }
                    else
                    {
                        _doubleClickTarget = backId;
                        _lastClickTime = now;
                    }
                }
                
                // Back Icon
                float ix = cx + (itemW - 48) / 2;
                float iy = cy + 16;
                ui.Panel(ix + 12, iy + 6, 24, 24, textSecondary); // Placeholder back icon indicator
                ui.SetCursor(cx + 8, cy + itemH - 24);
                ui.Text("<- Back", textPrimary);
                
                cx += itemW + gap;
            }

            // Folders - INTERACTIVE
            int folderIdx = 0;
            foreach (var dir in dirs)
            {
                if (cx + itemW > contentX + contentW - 16)
                { cx = contentX + 16; cy += itemH + gap; }
                if (cy + itemH > contentY + contentH - 16) break;

                uint cardId = 5000u + (uint)folderIdx;
                bool isCardSel = _selectedAssetIndex == (int)cardId;
                var cardBg = isCardSel ? new System.Numerics.Vector4(0.22f, 0.38f, 0.62f, 0.4f) : bgCard;

                // Interactive card with shadow
                if (ui.ClickableCard(cx, cy, itemW, itemH, cardId,
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
                    ui.Panel(cx, cy, itemW, 2, accentBlueLight);
                    ui.Panel(cx, cy + itemH - 2, itemW, 2, accentBlueLight);
                }
                else
                {
                    ui.Panel(cx, cy, itemW, 2, borderLight);
                }

                // Clean folder icon - 3D style
                float ix = cx + (itemW - 48) / 2; // centered 48px icon
                float iy = cy + 16;
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
                ui.SetCursor(cx + 10, cy + itemH - 24);
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
                if (cy + itemH > contentY + contentH - 16) break;

                string ext = Path.GetExtension(file).ToLower();
                bool isBlueAsset = ext == ".blueskyasset";
                bool isMesh = ext == ".obj" || ext == ".fbx" || ext == ".gltf";
                bool isTexture = ext == ".png" || ext == ".jpg" || ext == ".jpeg";
                bool isCode = ext == ".cs" || ext == ".blueprint";
                bool isTeaScript = ext == ".tea";
                
                // Asset type info for tooltip
                string assetType = "File";
                string assetSubtype = "";
                System.Numerics.Vector4 typeColor = textMuted;

                uint cardId = 6000u + (uint)fileIdx;
                bool isCardSel = _selectedAssetIndex == (int)cardId;
                var cardBg = isBlueAsset ? new System.Numerics.Vector4(0.18f, 0.30f, 0.52f, 1f) :
                             isCardSel ? new System.Numerics.Vector4(0.22f, 0.38f, 0.62f, 0.4f) : bgCard;

                // Interactive card
                if (ui.ClickableCard(cx, cy, itemW, itemH, cardId,
                    cardBg,
                    bgCardHover,
                    new System.Numerics.Vector4(0.28f, 0.48f, 0.78f, 0.5f)))
                {
                    _selectedAssetIndex = (int)cardId;
                    Log($"Selected file: {Path.GetFileName(file)}");
                    
                    // Double-click detection for .tea files
                    double now = ui.Time;
                    if (_doubleClickTarget == cardId && (now - _lastClickTime) < 0.3)
                    {
                        if (isTeaScript)
                        {
                            OpenScriptEditor(file);
                        }
                    }
                    else
                    {
                        _doubleClickTarget = cardId;
                        _lastClickTime = now;
                    }
                }

                // --- Drag and Drop initiation ---
                if (_selectedAssetIndex == (int)cardId && 
                    ui.IsHovering(cx, cy, itemW, itemH) && 
                    ui.IsMouseDown)
                {
                    // If moving distance > 5
                    if (System.Numerics.Vector2.Distance(ui.MousePosition, _dragPos) > 5 && !_isDraggingAsset)
                    {
                        if (isBlueAsset || isTeaScript)
                        {
                            _isDraggingAsset = true;
                            _draggedAssetPath = file;
                        }
                    }
                }

                // Type border (top)
                var typeBorder = borderLight;
                if (isCardSel) typeBorder = accentBlueLight;
                else if (isMesh) typeBorder = accentBlue;
                else if (isTexture) typeBorder = accentPurple;
                else if (isTeaScript) typeBorder = accentGreen;
                else if (isBlueAsset) typeBorder = accentBlueLight;
                ui.Panel(cx, cy, itemW, 2, typeBorder);

                // Selection indicator (bottom border too when selected)
                if (isCardSel)
                    ui.Panel(cx, cy + itemH - 2, itemW, 2, accentBlueLight);

                // Icon position
                float ix = cx + (itemW - 44) / 2;
                float iy = cy + 14;
                
                string badge = ext.TrimStart('.').ToUpper();
                string displayLabel = Path.GetFileNameWithoutExtension(file);
                
                if (isBlueAsset)
                {
                    var header = BlueSky.Core.Assets.BlueAsset.LoadHeader(file);
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
                        else if (header.Type == BlueSky.Core.Assets.AssetType.Material)
                        {
                            assetType = "Material";
                            assetSubtype = "Rendering Material";
                            typeColor = new System.Numerics.Vector4(0.8f, 0.5f, 1.0f, 1f); // Purple
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
                ui.SetCursor(cx + 8, cy + 6);
                ui.Text(badge, isBlueAsset ? accentBlueGlow : textDark);

                // Colored type indicator at bottom (3px thick line)
                ui.Panel(cx, cy + itemH - 3, itemW, 3, typeColor);

                // Filename
                ui.SetCursor(cx + 10, cy + itemH - 24);
                if (displayLabel.Length > 14) displayLabel = displayLabel[..12] + "..";
                ui.Text(displayLabel, (isBlueAsset || isCardSel) ? accentBlueGlow : textSecondary);

                // Hover tooltip
                if (ui.IsHovering(cx, cy, itemW, itemH))
                {
                    // Tooltip background - positioned above the card
                    float tooltipW = 160;
                    float tooltipH = 44;
                    float tooltipX = cx + (itemW - tooltipW) / 2;
                    float tooltipY = cy - tooltipH - 8;
                    
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
        }

        // ── STATUS BAR ─────────────────────────────────────────────────────
        float sy = rect.Y + rect.H - statusbarH;
        ui.Panel(rect.X, sy, rect.W, statusbarH, bgPanel);
        ui.Panel(rect.X, sy, rect.W, 1, borderSubtle);

        ui.SetCursor(rect.X + 16, sy + 6);
        ui.Text("BlueSky Engine  —  A game engine for the ease of Development", textMuted);

        ui.SetCursor(rect.X + rect.W - 90, sy + 6);
        ui.Text("● Ready", accentGreen);
        
        // ── RIGHT-CLICK CONTEXT MENU ───────────────────────────────────────
        // Detect right-click in content area
        if (_input!.IsMouseButtonDown(MouseButton.Right) && 
            ui.IsHovering(contentX, contentY, contentW, contentH))
        {
            _showContextMenu = true;
            _contextMenuX = ui.MousePosition.X;
            _contextMenuY = ui.MousePosition.Y;
            _contextMenuPath = _currentBrowserDir;
        }
        
        // Draw context menu
        if (_showContextMenu)
        {
            DrawContextMenu(ui, _contextMenuX, _contextMenuY);
        }
    }

    private static void DrawConsolePanel(NotBSUI ui, DockRect rect)
    {
        ui.Panel(rect.X, rect.Y, rect.W, rect.H, EditorTheme.Bg1);

        // Toolbar header
        ui.Panel(rect.X, rect.Y, rect.W, EditorTheme.HeaderH, EditorTheme.Bg2);
        ui.Panel(rect.X, rect.Y + EditorTheme.HeaderH - 1, rect.W, 1, EditorTheme.Border0);
        ui.SetCursor(rect.X + 12, rect.Y + 9);
        ui.Text("\u25b8 Output Log", EditorTheme.TextPrimary);

        // Clear button
        if (ui.ButtonEx(rect.X + rect.W - 70, rect.Y + 5, 60, 22, "Clear",
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
        float inputY = rect.Y + rect.H - 28;
        ui.Panel(rect.X + 8, inputY, rect.W - 16, 22, EditorTheme.Bg0);
        ui.Panel(rect.X + 8, inputY + 21, rect.W - 16, 1, EditorTheme.Border1);
        ui.SetCursor(rect.X + 14, inputY + 4);
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

    // ─────────────────────────────────────────────────────────────────────
    private static void Cleanup()
    {
        _viewportRenderer?.Dispose();
        _viewport?.Dispose();
        _depthTexture?.Dispose();
        _world?.Dispose();
        _uiRenderer?.FontAtlas?.Dispose();
        _uiRenderer?.Dispose();
        _swapchain?.Dispose();
        _rhi?.Dispose();
        _input?.Dispose();
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
            f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (_pendingImportFiles.Length == 0)
        {
            Log("No valid mesh files to import");
            return;
        }

        _importMeshPreviewNames = _pendingImportFiles.Select(Path.GetFileNameWithoutExtension).ToArray();
        _importSelectedMeshIndex = 0;
        _importScale = 1.0f;
        _importGenerateCollider = true;
        _importImportMaterials = true;
        _showImportDialog = true;

        Log($"Import dialog opened for {_pendingImportFiles.Length} file(s)");
    }

    private static void DrawImportDialog(NotBSUI ui, float windowW, float windowH)
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

        // Import Materials
        uint matCheckId = 10005;
        var matCheckBg = _importImportMaterials ? accentBlue : new System.Numerics.Vector4(0.15f, 0.16f, 0.18f, 1f);
        if (ui.ButtonEx(dx + 16, contentY, 22, 22, _importImportMaterials ? "✓" : "",
            matCheckBg,
            new System.Numerics.Vector4(0.30f, 0.60f, 1.0f, 1f),
            new System.Numerics.Vector4(0.20f, 0.50f, 0.90f, 1f),
            new System.Numerics.Vector4(0, 0, 0, 0.3f),
            textTitle, matCheckId))
        {
            _importImportMaterials = !_importImportMaterials;
        }
        ui.SetCursor(dx + 44, contentY + 3);
        ui.Text("Import Materials", textNormal);

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
                    ["generateCollider"] = _importGenerateCollider,
                    ["importMaterials"] = _importImportMaterials
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
                        if (_importImportMaterials) options += " +Materials";

                        Log($"✓ Imported: {asset.AssetName} → {asset.AssetName}.blueskyasset (scale: {_importScale:F2}x{options})");
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

    private static void SpawnDraggedAsset(string assetPath)
    {
        if (_world == null) return;
        
        string ext = Path.GetExtension(assetPath).ToLower();
        
        // Handle TeaScript files
        if (ext == ".tea")
        {
            // Find selected entity or create new one
            Entity targetEntity;
            
            if (_selectedEntityId > 0 && _selectedEntityId < 200)
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
                        Position = new BlueSky.Core.Math.Vector3(0, 1, 0),
                        Rotation = BlueSky.Core.Math.Quaternion.Identity,
                        Scale = BlueSky.Core.Math.Vector3.One
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
                    Position = new BlueSky.Core.Math.Vector3(0, 1, 0),
                    Rotation = BlueSky.Core.Math.Quaternion.Identity,
                    Scale = BlueSky.Core.Math.Vector3.One
                };
                _world.AddComponent(targetEntity, transform);
            }
            
            // Add or update TeaScriptComponent
            var scriptComponent = new TeaScriptComponent
            {
                ScriptAssetId = assetPath,
                IsEnabled = true,
                IsInitialized = false,
                RuntimeInstance = 0
            };
            
            if (_world.HasComponent<TeaScriptComponent>(targetEntity))
            {
                // Update existing
                ref var existing = ref _world.GetComponent<TeaScriptComponent>(targetEntity);
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
        
        // Handle mesh assets
        var header = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetPath);
        if (header != null && (header.Type == BlueSky.Core.Assets.AssetType.StaticMesh || header.Type == BlueSky.Core.Assets.AssetType.SkeletalMesh))
        {
            var entity = _world.CreateEntity();
            
            var transform = new TransformComponent
            {
                Position = new BlueSky.Core.Math.Vector3(0, 0, 0), // Spawn at origin
                Rotation = BlueSky.Core.Math.Quaternion.Identity,
                Scale = BlueSky.Core.Math.Vector3.One
            };
            _world.AddComponent(entity, transform);
            
            var staticMesh = new BlueSky.Core.ECS.Builtin.StaticMeshComponent
            {
                MeshAssetId = assetPath,
                MaterialAssetId = ""
            };
            _world.AddComponent(entity, staticMesh);
            
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
    
    private static void DrawContextMenu(NotBSUI ui, float x, float y)
    {
        float menuW = 180;
        float menuH = 160;
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
                var dirs = Directory.GetDirectories(_currentBrowserDir);
                var files = Directory.GetFiles(_currentBrowserDir);
                
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
        }
        
        // Refresh
        uint menuId3 = 9102;
        if (ui.ClickableCard(x + 4, itemY, menuW - 8, itemH, menuId3,
            EditorTheme.Bg2,
            EditorTheme.HoverBg,
            EditorTheme.SelectionBg))
        {
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
        var asset = BlueSky.Core.Assets.TeaScriptAsset.Create(Path.GetFileNameWithoutExtension(scriptPath));
        asset.SaveToFile(scriptPath);
        
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
    
    private static void DrawScriptEditor(NotBSUI ui, float screenW, float screenH)
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
    
    private static void DrawRenameDialog(NotBSUI ui, float screenW, float screenH)
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
}
