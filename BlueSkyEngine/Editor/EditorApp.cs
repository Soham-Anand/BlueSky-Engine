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
using BlueSky.Runtime.UI;
using NotBSRenderer;
using BlueSky.Editor.Services;

namespace BlueSky.Editor;

partial class Program
{
    // ── Services (own most mutable editor state) ───────────────────────────
    internal static readonly EditorContext Ctx = new();
    internal static readonly ProjectService Projects = new();
    internal static readonly AssetService Assets = new();
    internal static readonly ViewportService ViewportSvc = new();
    internal static readonly PlayModeService PlayMode = new();

    // ── top-level state ────────────────────────────────────────────────────
    private static IWindow?          _window;
    private static IInputContext?    _input;
    private static IRHIDevice?       _rhi;
    private static IRHISwapchain?    _swapchain;
    private static NotBSUI?          _ui;
    private static NotBSUIRenderer?  _uiRenderer;

    // ── Editor state ──────────────────────────────────────────────────────
    internal static EditorState _state = EditorState.ProjectBrowser;
    private static string _projectPathInput { get => Projects.ProjectPathInput; set => Projects.ProjectPathInput = value; }
    private static string _projectNameInput { get => Projects.ProjectNameInput; set => Projects.ProjectNameInput = value; }
    private static string _openProjectPathInput { get => Projects.OpenProjectPathInput; set => Projects.OpenProjectPathInput = value; }
    private static int _projectBrowserTab { get => Projects.ProjectBrowserTab; set => Projects.ProjectBrowserTab = value; }
    private static int _selectedRecentProject { get => Projects.SelectedRecentProject; set => Projects.SelectedRecentProject = value; }
    private static int _selectedTemplate { get => Projects.SelectedTemplate; set => Projects.SelectedTemplate = value; }
    private static int _selectedCategory { get => Projects.SelectedCategory; set => Projects.SelectedCategory = value; }
    private static string _errorMsg { get => Projects.ErrorMessage; set => Projects.ErrorMessage = value; }
    internal static World? _world;
    private static DockingSystem? _dockingSystem;
    private static BlueSky.Core.Scripting.TeaScriptSystem? _teaScriptSystem;

    // ── Scene Management ────────────────────────────────────────────────
    internal static string? _currentScenePath = null;
    internal static bool _sceneDirty = false;

    // ── Interactive Selection State ─────────────────────────────────────
    internal static uint _selectedEntityId = 0;
    private static int _selectedSourceIndex { get => Assets.SelectedSourceIndex; set => Assets.SelectedSourceIndex = value; }
    internal static int _selectedAssetIndex { get => Assets.SelectedAssetIndex; set => Assets.SelectedAssetIndex = value; }
    internal static List<string> _consoleLogs = new();
    private static uint _buttonIdCounter = 1000;

    // ── Content Browser State ──────────────────────────────────────────
    internal static string _currentBrowserDir { get => Assets.CurrentBrowserDir; set => Assets.CurrentBrowserDir = value; }
    internal static string? _draggedAssetPath { get => Assets.DraggedAssetPath; set => Assets.DraggedAssetPath = value; }
    internal static System.Numerics.Vector2 _dragPos { get => Assets.DragStartMouse; set => Assets.DragStartMouse = value; }
    internal static bool _isDraggingAsset { get => Assets.IsDraggingAsset; set => Assets.IsDraggingAsset = value; }
    private static uint _doubleClickTarget { get => Assets.DoubleClickTarget; set => Assets.DoubleClickTarget = value; }
    private static double _lastClickTime { get => Assets.LastClickTime; set => Assets.LastClickTime = value; }
    
    // ── Context Menu State ────────────────────────────────────────────
    internal static bool _showContextMenu { get => Assets.ShowContextMenu; set => Assets.ShowContextMenu = value; }
    internal static float _contextMenuX { get => Assets.ContextMenuX; set => Assets.ContextMenuX = value; }
    internal static float _contextMenuY { get => Assets.ContextMenuY; set => Assets.ContextMenuY = value; }
    internal static string _contextMenuPath { get => Assets.ContextMenuPath; set => Assets.ContextMenuPath = value; }
    
    // ── Script Editor State ───────────────────────────────────────────
    internal static bool _showScriptEditor = false;
    internal static string _editingScriptPath = "";
    internal static string _editingScriptContent = "";
    internal static string _editingScriptName = "";
    internal static int _scriptCursorLine = 0;
    internal static int _scriptCursorCol = 0;
    internal static bool _showRenameDialog = false;
    internal static string _renameTarget = "";
    internal static string _renameNewName = "";
    
    // ── Material Editor State ─────────────────────────────────────────
    internal static MaterialEditor? _materialEditor;
    
    // ── Static Mesh Editor State ─────────────────────────────────────
    internal static StaticMeshEditor? _staticMeshEditor;
    
    // ── Play State ────────────────────────────────────────────────────
    internal static bool _isPlaying => PlayMode.IsPlaying;
    internal static bool _isPaused => PlayMode.IsPaused;
    internal static SceneSnapshot? _playModeSnapshot => PlayMode.Snapshot;

    // ── Import Dialog State ──────────────────────────────────────────────
    internal static bool _showImportDialog = false;
    internal static string[] _pendingImportFiles = Array.Empty<string>();
    internal static float _importScale = 1.0f;
    internal static bool _importGenerateCollider = true;
    internal static bool _importImportMaterials = true;
    internal static int _importSelectedMeshIndex = 0;
    internal static string[] _importMeshPreviewNames = Array.Empty<string>();

    // ── Viewport 3D rendering ─────────────────────────────────────────
    internal static BlueSky.Rendering.Viewport? _viewport { get => ViewportSvc.Viewport; set => ViewportSvc.Viewport = value; }
    internal static BlueSky.Editor.ViewportRenderer? _editorViewportRenderer { get => ViewportSvc.EditorViewportRenderer; set => ViewportSvc.EditorViewportRenderer = value; }
    internal static IRHITexture? _depthTexture { get => ViewportSvc.DepthTexture; set => ViewportSvc.DepthTexture = value; }
    private static DockRect _viewportPanelRect;
    internal static uint _depthW { get => ViewportSvc.DepthW; set => ViewportSvc.DepthW = value; }
    internal static uint _depthH { get => ViewportSvc.DepthH; set => ViewportSvc.DepthH = value; }
    internal static DockRect _lastViewportRect { get => ViewportSvc.LastViewportRect; set => ViewportSvc.LastViewportRect = value; }
    internal static bool _viewportNeedsRender { get => ViewportSvc.ViewportNeedsRender; set => ViewportSvc.ViewportNeedsRender = value; }

    // ── Gizmo Interaction State ─────────────────────────────────────────
    internal static bool _isDraggingGizmo = false;
    internal static int  _draggedGizmoAxis = -1; // 0=X, 1=Y, 2=Z, 3=Center
    internal static BlueSky.Core.Math.Vector3 _gizmoDragStartMousePos;
    internal static BlueSky.Core.Math.Vector3 _gizmoDragStartEntityPos;
    internal static BlueSky.Core.Math.Vector3 _gizmoDragAxisDir;
    internal static float _gizmoDragDistanceOffset = 0f;
    internal static BlueSky.Core.Math.Quaternion _gizmoDragStartRot;
    internal static BlueSky.Core.Math.Vector3 _gizmoDragStartScale;
    internal static BlueSky.Core.Math.Vector3 _gizmoDragStartHitVec;

    // ── Timing ────────────────────────────────────────────────────────
    internal static Stopwatch _stopwatch = new();
    internal static float _deltaTime;
    private static float _lastFrameTime;
    internal static int _frameIndex;

    internal static string _frameTypedText = "";
    internal static bool _frameBackspacePressed = false;

    // ── UI Enhancement Systems ────────────────────────────────────────
    internal static NotificationSystem? _notificationSystem;
    internal static UIPerformanceMonitor? _perfMonitor;
    internal static bool _showPerformanceOverlay = false;
    internal static CommandPalette? _commandPalette;
    internal static UndoRedoSystem? _undoRedoSystem;

    // ── Physics Systems ───────────────────────────────────────────────
    internal static BlueSky.Physics.IPhysicsWorld? _physicsWorld => PlayMode.PhysicsWorld;

    // ── Terrain System ────────────────────────────────────────────────
    internal static BlueSky.Rendering.TerrainSystem? _terrainSystem;
    internal static bool _terrainEditMode;
    internal static BrushMode _terrainBrushMode = BrushMode.Raise;
    internal static float _terrainBrushRadius = 5.0f;
    internal static float _terrainBrushStrength = 0.55f;
    internal static float _terrainFlattenHeight = 0.0f;
    internal static int _terrainPaintLayer = 0;
    internal static bool _useEaseRenderer;

    // ── Car Controller System ─────────────────────────────────────────
    internal static BlueSky.Core.Gameplay.CarControllerSystem? _carControllerSystem;

    // ─────────────────────────────────────────────────────────────────────
    private static void Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--reflect-jolt")
        {
            DumpJoltReflection();
            return;
        }

        // ── Show native splash window FIRST ───────────────────────────────
        using (var splash = new NativeSplashWindow())
        {
            splash.ShowAndWait(2000); // Show for 2 seconds
        }
        
        // ── window ────────────────────────────────────────────────────────
        var options = WindowOptions.Default;
        options.Title = "BlueSky Engine";
        options.Width = 1280;
        options.Height = 720;
        options.Resizable = true; // Ensure it's resizable!
        _window = WindowFactory.Create(options);

        // Service defaults
        Projects.ProjectPathInput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MyBlueSkyProject");
        Projects.OpenProjectPathInput = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // ── project config ────────────────────────────────────────────────
        ProjectConfig.Load();
        ProjectConfig.ScanDesktopForProjects();

        // ── RHI ───────────────────────────────────────────────────────────
        bool forceCompatibility = Array.Exists(args, arg => arg.Equals("-dx10", StringComparison.OrdinalIgnoreCase) || arg.Equals("--dx10", StringComparison.OrdinalIgnoreCase));
        _useEaseRenderer = Array.Exists(args, arg => arg.Equals("--ease", StringComparison.OrdinalIgnoreCase));
        
        _input    = _window.CreateInput();
        _rhi      = RHIDevice.CreateDefault(_window, forceCompatibility, args);
        _swapchain = _rhi.CreateSwapchain(_window, PresentMode.Immediate);

        // ── UI system ─────────────────────────────────────────────────────
        _ui         = new NotBSUI((uint)_window.Size.X, (uint)_window.Size.Y)
        {
            SharpCorners = false
        };
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
        _ui.MeasureTextWidth = text => _uiRenderer.FontAtlas.MeasureWidth(text.AsSpan());
        _ui.TextLineHeight = _uiRenderer.FontAtlas.LineHeight;
        _uiRenderer.Resize((int)_window.Size.X, (int)_window.Size.Y);

        // ── Enhanced UI Systems ───────────────────────────────────────────
        _notificationSystem = new NotificationSystem();
        _perfMonitor = new UIPerformanceMonitor();
        _materialEditor = new MaterialEditor();
        // When a material is saved, evict it from the viewport's cache so the
        // next frame picks up the new version without requiring a restart.
        _materialEditor.OnSaved += (path) =>
        {
            // Evict the saved material from the viewport cache so the next frame
            // picks up the new version without requiring a restart.
            _editorViewportRenderer?.InvalidateMaterial(path);
        };
        _staticMeshEditor = new StaticMeshEditor();
        _commandPalette = new CommandPalette();
        _undoRedoSystem = new UndoRedoSystem();
        Console.WriteLine("[Editor] Enhanced UI systems initialized");

        // Immediately resize swapchain to actual pixel dimensions (Retina)
        var fbSize = _window.FramebufferSize;
        _swapchain.Resize((uint)fbSize.X, (uint)fbSize.Y);

        // ── Input binding ─────────────────────────────────────────────────
        _input.CharInput += c => 
        {
            _frameTypedText += c;
        };
        _input.KeyDown += (k, m) =>
        {
            if (k == KeyCode.Backspace)
            {
                _frameBackspacePressed = true;
            }
            
            // Command Palette (Cmd+P)
            if (k == KeyCode.P && m.HasFlag(ModifierKeys.Super))
            {
                _commandPalette?.Toggle();
                return;
            }
            
            // Undo (Cmd+Z)
            if (k == KeyCode.Z && m.HasFlag(ModifierKeys.Super) && !m.HasFlag(ModifierKeys.Shift))
            {
                _undoRedoSystem?.Undo();
                _notificationSystem?.ShowInfo($"Undo: {_undoRedoSystem?.GetUndoDescription()}", 1.5f);
                return;
            }
            
            // Redo (Cmd+Shift+Z)
            if (k == KeyCode.Z && m.HasFlag(ModifierKeys.Super) && m.HasFlag(ModifierKeys.Shift))
            {
                _undoRedoSystem?.Redo();
                _notificationSystem?.ShowInfo($"Redo: {_undoRedoSystem?.GetRedoDescription()}", 1.5f);
                return;
            }
            
            if (k == KeyCode.W) { if (_editorViewportRenderer != null) _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Translate; }
            if (k == KeyCode.E) { if (_editorViewportRenderer != null) _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Rotate; }
            if (k == KeyCode.R) { if (_editorViewportRenderer != null) _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Scale; }

            if (k == KeyCode.I && m.HasFlag(ModifierKeys.Super)) ImportFilesDialog();
            if (k == KeyCode.F3) _showPerformanceOverlay = !_showPerformanceOverlay;

            if (k == KeyCode.F10)
            {
                _editorViewportRenderer?.RequestMaterialDebugDump();
                _notificationSystem?.ShowInfo("Material debug: next frame prints [MatDbg] lines to the console.", 2.8f);
            }
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
        else if (_window is Platform.Windows.Win32Window win32Window)
        {
            win32Window.FilesDropped += files =>
            {
                HandleFilesDropped(files);
            };
        }

        // ── Auto-open project from command line argument ─────────────────
        string? projectToOpen = null;
        foreach (var arg in args)
        {
            if (!arg.StartsWith("-") && Directory.Exists(arg))
            {
                if (Directory.GetFiles(arg, "*.BlueSkyProj").Length > 0)
                {
                    projectToOpen = arg;
                    break;
                }
            }
        }

        if (projectToOpen != null)
        {
            if (ProjectManager.TryOpenProject(projectToOpen))
            {
                TransitionToWorkspace();
            }
        }

        _window.Show();
        _stopwatch.Start();

        // Wire context for services
        Ctx.Window = _window;
        Ctx.Input = _input;
        Ctx.Rhi = _rhi;
        Ctx.Swapchain = _swapchain;
        Ctx.Ui = _ui;
        Ctx.UiRenderer = _uiRenderer;
        Ctx.World = _world;
        Ctx.Notifications = _notificationSystem;
        Ctx.Log = Log;

        // ── main loop ────────────────────────────────────────────────────
        while (!_window.IsClosing)
        {
            // Delta time
            float now = (float)_stopwatch.Elapsed.TotalSeconds;
            _deltaTime = now - _lastFrameTime;
            _lastFrameTime = now;
            _frameIndex++;

            _input.BeginFrame();
            _window.ProcessEvents();
            RuntimeUI.BeginFrame();

            // Update viewport camera (only in workspace)
            if (_state == EditorState.Workspace && _viewport != null)
            {
                _viewport.Update(_deltaTime);
                
                // Sync gizmo state to the viewport renderer
                if (_editorViewportRenderer != null)
                {
                    _editorViewportRenderer.SelectedEntityId = _selectedEntityId;
                }
            }

            HandleTerrainSculpting();

            // Update TeaScript system
            if (_state == EditorState.Workspace && _teaScriptSystem != null && PlayMode.IsPlaying && !PlayMode.IsPaused)
            {
                _teaScriptSystem.Update(_deltaTime);
            }

            // Update Physics (independent of TeaScript — required for car driving, general physics)
            if (_state == EditorState.Workspace && PlayMode.IsPlaying && !PlayMode.IsPaused)
            {
                if (PlayMode.PhysicsWorld != null)
                {
                    PlayMode.PhysicsWorld.Step(_deltaTime);

                    // Sync physics transforms back to ECS
                    SyncPhysicsToTransforms();
                }
            }

            // Update Car Controller system AFTER physics step (so forces are integrated same frame)
            if (_state == EditorState.Workspace && PlayMode.IsPlaying && !PlayMode.IsPaused && _carControllerSystem != null)
            {
                _carControllerSystem.Update(_deltaTime);
            }

            // Update Terrain system (always, not just in play mode)
            if (_state == EditorState.Workspace && _terrainSystem != null)
            {
                _terrainSystem.Update();
            }
            
            // Update Static Mesh Editor
            if (_staticMeshEditor != null && _staticMeshEditor.IsOpen)
            {
                _staticMeshEditor.Update(_deltaTime);
            }

            RenderFrame();

            // Clear input state AFTER rendering (so modals can use it)
            _frameTypedText = "";
            _frameBackspacePressed = false;
        }

        Cleanup();
    }

    public static string[]? ShowOpenFileDialog()
    {
        if (_window is Platform.macOS.CocoaWindow cocoaWindow)
        {
            return cocoaWindow.ShowOpenFileDialog();
        }
        else if (_window is Platform.Windows.Win32Window win32Window)
        {
            return win32Window.ShowOpenFileDialog();
        }
        else if (OperatingSystem.IsLinux())
        {
            var selected = NativeFilePicker.OpenFile("Select File");
            return string.IsNullOrEmpty(selected) ? null : new[] { selected };
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    internal static void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string logEntry = $"[{timestamp}] {message}";
        _consoleLogs.Add(logEntry);
        Console.WriteLine(logEntry);
        
        // Show notification based on message content
        if (message.Contains("✓") || message.Contains("Success") || message.Contains("Imported") || message.Contains("Created") || message.Contains("Saved"))
            _notificationSystem?.ShowSuccess(message, duration: 2.5f);
        else if (message.Contains("✗") || message.Contains("Failed") || message.Contains("Error"))
            _notificationSystem?.ShowError(message, duration: 4f);
        else if (message.Contains("⚠") || message.Contains("Warning"))
            _notificationSystem?.ShowWarning(message, duration: 3f);
        else if (message.Contains("Selected") || message.Contains("Opened"))
            _notificationSystem?.ShowInfo(message, duration: 2f);
        
        // Keep only last 100 messages
        if (_consoleLogs.Count > 100)
            _consoleLogs.RemoveAt(0);
    }

    private static bool IsTeaScriptKeyDown(string keyName)
    {
        if (_input == null || string.IsNullOrWhiteSpace(keyName))
            return false;

        string normalized = keyName.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        normalized = normalized.ToLowerInvariant() switch
        {
            "leftshift" or "shift" => nameof(KeyCode.LeftShift),
            "rightshift" => nameof(KeyCode.RightShift),
            "ctrl" or "control" or "leftcontrol" => nameof(KeyCode.LeftControl),
            "rightcontrol" => nameof(KeyCode.RightControl),
            "alt" or "leftalt" => nameof(KeyCode.LeftAlt),
            "rightalt" => nameof(KeyCode.RightAlt),
            "cmd" or "super" or "windows" or "leftsuper" => nameof(KeyCode.LeftSuper),
            "rightsuper" => nameof(KeyCode.RightSuper),
            "spacebar" => nameof(KeyCode.Space),
            "return" => nameof(KeyCode.Enter),
            "esc" => nameof(KeyCode.Escape),
            "uparrow" => nameof(KeyCode.Up),
            "downarrow" => nameof(KeyCode.Down),
            "leftarrow" => nameof(KeyCode.Left),
            "rightarrow" => nameof(KeyCode.Right),
            "0" => nameof(KeyCode.D0),
            "1" => nameof(KeyCode.D1),
            "2" => nameof(KeyCode.D2),
            "3" => nameof(KeyCode.D3),
            "4" => nameof(KeyCode.D4),
            "5" => nameof(KeyCode.D5),
            "6" => nameof(KeyCode.D6),
            "7" => nameof(KeyCode.D7),
            "8" => nameof(KeyCode.D8),
            "9" => nameof(KeyCode.D9),
            _ => normalized.Length == 1 && char.IsLetter(normalized[0])
                ? normalized.ToUpperInvariant()
                : keyName.Trim()
        };

        return Enum.TryParse<KeyCode>(normalized, ignoreCase: true, out var key)
            && _input.IsKeyDown(key);
    }

    private static bool IsTeaScriptMouseButtonDown(int button)
    {
        if (_input == null || button < 0 || button > (int)MouseButton.X2)
            return false;

        return _input.IsMouseButtonDown((MouseButton)button);
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
        _perfMonitor?.BeginFrame();
        
        _swapchain!.AcquireNextImage();

        var cmd = _rhi!.CreateCommandBuffer();

        if (_state == EditorState.Workspace && _viewport != null)
        {
            // UltraRenderer handles PreRender internally
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
        if (_viewport != null)
        {
            // UltraRenderer handles shadow rendering internally
        }

        // Static Mesh Editor is NOT a fullscreen modal - it needs viewport rendering for preview
        bool fullscreenModalOpen = (_materialEditor?.IsOpen ?? false) || _showScriptEditor || _showImportDialog || _showRenameDialog;

        var mousePos  = _input!.MousePosition;
        var mouseDown = _input.IsMouseButtonDown(MouseButton.Left);
        
        _ui!.Time = _stopwatch!.Elapsed.TotalSeconds;
        
        // Update animation systems
        AnimatedButton.UpdateGlobalTime(_deltaTime);
        _notificationSystem?.Update(_deltaTime);
        _commandPalette?.Update(_deltaTime);
        
        // Don't pass input to UI system if a modal is open (script editor, rename dialog, command palette)
        string uiTypedText = (_showScriptEditor || _showRenameDialog || _commandPalette?.IsOpen == true) ? "" : _frameTypedText;
        bool uiBackspace = (_showScriptEditor || _showRenameDialog || _commandPalette?.IsOpen == true) ? false : _frameBackspacePressed;
        
        // Command palette input (highest priority)
        if (_commandPalette?.IsOpen == true)
        {
            bool upArrow = _input!.IsKeyDown(KeyCode.Up);
            bool downArrow = _input!.IsKeyDown(KeyCode.Down);
            bool enter = _input!.IsKeyDown(KeyCode.Enter);
            bool escape = _input!.IsKeyDown(KeyCode.Escape);
            
            _commandPalette.HandleInput(_frameTypedText, _frameBackspacePressed, upArrow, downArrow, enter, escape);
            
            // Override UI input when command palette is open
            uiTypedText = "";
            uiBackspace = false;
        }
        
        _ui!.BeginFrame(mousePos, mouseDown, uiTypedText, uiBackspace, _input.ScrollDelta.Y);
        
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
        _viewportNeedsRender = (_state == EditorState.Workspace && _viewport != null);

        bool isStaticMeshEditorOpen = _staticMeshEditor != null && _staticMeshEditor.IsOpen;
        
        // Disable input for the background UI if a modal is open
        _ui!.InputEnabled = !(fullscreenModalOpen || isStaticMeshEditorOpen);

        if (_state == EditorState.ProjectBrowser)
        {
            BuildProjectBrowserUI();
        }
        else
        {
            BuildWorkspaceUI();
        }

        // ── Execute 3D Viewport Rendering (UltraRenderer) AFTER UI layout is calculated ──────────────────────────────
        
        if ((_state == EditorState.Workspace && _viewportNeedsRender && _viewport != null && !fullscreenModalOpen) || isStaticMeshEditorOpen)
        {
            float vpX = isStaticMeshEditorOpen ? _staticMeshEditor!.PreviewRect.X : _lastViewportRect.X;
            float vpY = isStaticMeshEditorOpen ? _staticMeshEditor!.PreviewRect.Y : _lastViewportRect.Y;
            float vpW = isStaticMeshEditorOpen ? _staticMeshEditor!.PreviewRect.Z : _lastViewportRect.W;
            float vpH = isStaticMeshEditorOpen ? _staticMeshEditor!.PreviewRect.W : _lastViewportRect.H;

            if (vpW > 1 && vpH > 1)
            {
                _viewport.SetViewportRect(vpX, vpY, vpW, vpH);
                // NOTE: UltraRenderer creates its OWN cmd buffer + render pass internally.
                _viewport.Render();
            }
        }

        cmd.BeginRenderPass(
            new[] { _swapchain.CurrentRenderTarget },
            _depthTexture,
            new ClearValue { Color = EditorTheme.Bg0, Depth = 1.0f }
        );

        cmd.SetViewport(new NotBSRenderer.Viewport { X = 0, Y = 0, Width = w, Height = h, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new Scissor  { X = 0, Y = 0, Width = w, Height = h });

        // ── Composite 3D viewport content first so editor chrome draws above it ───────────
        bool isStaticMeshEditorOpen2 = _staticMeshEditor != null && _staticMeshEditor.IsOpen;
        
        if ((_state == EditorState.Workspace && _viewportNeedsRender && _viewport != null && !fullscreenModalOpen) || isStaticMeshEditorOpen2)
        {
            float vpX = isStaticMeshEditorOpen2 ? _staticMeshEditor!.PreviewRect.X : _lastViewportRect.X;
            float vpY = isStaticMeshEditorOpen2 ? _staticMeshEditor!.PreviewRect.Y : _lastViewportRect.Y;
            float vpW = isStaticMeshEditorOpen2 ? _staticMeshEditor!.PreviewRect.Z : _lastViewportRect.W;
            float vpH = isStaticMeshEditorOpen2 ? _staticMeshEditor!.PreviewRect.W : _lastViewportRect.H;

            if (vpW > 1 && vpH > 1)
            {
                IRHITexture? finalTarget = null;
                if (_viewport.Renderer is UltraRenderer ultra)
                {
                    finalTarget = ultra.FinalTarget;
                }
                else if (_viewport.Renderer is BlueSky.Rendering.EasePlus.EasePlusRenderer easePlus)
                {
                    finalTarget = easePlus.FinalTarget;
                }

                if (finalTarget != null)
                {
                    // Pass logical coordinates — the projection matrix in NotBSUIRenderer
                    // is already set up in logical space (window.Size, not FramebufferSize),
                    // so do NOT scale to pixels here. Scaling was doubling coords on Retina.
                    _uiRenderer.RenderTexture(cmd, finalTarget, vpX, vpY, vpW, vpH);
                }
            }
        }

        // ── Render notifications and performance overlay ──────────────────
        _notificationSystem?.Render(_ui!, logW, logH);
        
        if (_showPerformanceOverlay && _perfMonitor != null)
        {
            // Performance overlay in top-right corner
            float overlayW = 400f;
            float overlayH = 120f;
            float overlayX = logW - overlayW - 10f;
            float overlayY = 10f;
            
            _ui.Panel(overlayX, overlayY, overlayW, overlayH, ModernTheme.WithAlpha(ModernTheme.Bg2, 0.9f));
            _ui.Panel(overlayX, overlayY, overlayW, 2, ModernTheme.Accent);
            
            _ui.SetCursor(overlayX + 10, overlayY + 10);
            _ui.Text($"FPS: {_perfMonitor.FPS:F1}", ModernTheme.Green);
            
            _ui.SetCursor(overlayX + 10, overlayY + 30);
            _ui.Text($"Frame: {_perfMonitor.CurrentFrameTime:F2}ms (avg: {_perfMonitor.AverageFrameTime:F2}ms)", ModernTheme.TextSecondary);
            
            _ui.SetCursor(overlayX + 10, overlayY + 50);
            _ui.Text($"Draw Calls: {_perfMonitor.DrawCallCount}", ModernTheme.TextSecondary);
            
            _ui.SetCursor(overlayX + 10, overlayY + 70);
            _ui.Text($"Panels: {_perfMonitor.PanelCount} | Text: {_perfMonitor.TextCount}", ModernTheme.TextMuted);
            
            _ui.SetCursor(overlayX + 10, overlayY + 90);
            _ui.Text("Press F3 to hide", ModernTheme.TextDisabled);
        }

        _uiRenderer!.Render(cmd, _ui!);

        // Runtime HUD overlay, rendered above the game viewport and below editor overlays.
        if (_state == EditorState.Workspace && PlayMode.IsPlaying)
        {
            _ui!.BeginFrame(mousePos, mouseDown, "", false, _input.ScrollDelta.Y);
            _ui.InputEnabled = !fullscreenModalOpen;
            RuntimeUI.Render(_ui, logW, logH, _deltaTime);
            _uiRenderer!.Render(cmd, _ui);
        }
        
        // ── Render Command Palette (AFTER viewport composite, on top of everything) ─────────────────
        if (_commandPalette?.IsOpen == true)
        {
            _ui!.BeginFrame(mousePos, mouseDown, "", false, _input.ScrollDelta.Y); // Start fresh UI batch for overlay
            _commandPalette.Render(_ui!, logW, logH);
            _uiRenderer!.Render(cmd, _ui!); // Render command palette on top
        }

        // ── Handle Gizmo Interaction ──────────────────────────────────────
        if (_state == EditorState.Workspace && _viewport != null && !fullscreenModalOpen && !_terrainEditMode)
        {
            UpdateGizmoInteraction();
        }

        cmd.EndRenderPass();
        _rhi.Submit(cmd, _swapchain);
        _swapchain.Present();
        cmd.Dispose();
        
        _perfMonitor?.EndFrame();
    }

    private static void DumpJoltReflection()
    {
        string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "jolt_reflection.txt");
        Console.WriteLine($"DUMPING JOLT REFLECTION TO {outputPath}...");
        try
        {
            using (var sw = new StreamWriter(outputPath))
            {
                var types = typeof(JoltPhysicsSharp.PhysicsSystem).Assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.Namespace != null && type.Namespace.Contains("JoltPhysicsSharp"))
                    {
                        sw.WriteLine($"TYPE: {type.FullName}");
                            foreach (var constructor in type.GetConstructors())
                            {
                                sw.WriteLine($"  CONSTRUCTOR: ({string.Join(", ", constructor.GetParameters().Select(p => p.ParameterType.Name))})");
                            }
                            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
                            {
                                sw.WriteLine($"  METHOD: {method.Name} ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}) -> {method.ReturnType.Name}");
                            }
                            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
                            {
                                sw.WriteLine($"  FIELD: {field.Name} ({field.FieldType.Name})");
                            }
                            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
                            {
                                sw.WriteLine($"  PROPERTY: {prop.Name} ({prop.PropertyType.Name})");
                            }
                    }
                }
            }
            Console.WriteLine("Done reflection!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Reflection failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private static void Cleanup()
    {
        AnimatedButton.ClearStates();
        _notificationSystem?.Clear();
        _viewport?.Dispose();
        _depthTexture?.Dispose();
        _world?.Dispose();
        _uiRenderer?.FontAtlas?.Dispose();
        _uiRenderer?.Dispose();
        _swapchain?.Dispose();
        _rhi?.Dispose();
        _input?.Dispose();
    }
}
