using System;
using System.IO;
using System.Linq;
using BlueSky.Aegis;
using BlueSky.Aegis.Input;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.Meridian;
using BlueSky.Editor;
using BlueSky.LHO;
using BlueSky.BSR;
using BlueSky.Airborne;
using BlueSky.BSR.RHI;
using BlueSky.Editor.UI;

namespace BlueSky.Runtime;

/// <summary>
/// Game runtime that initializes the shared platform, rendering, physics,
/// scripting, and multiplayer systems and runs the game loop.
/// </summary>
public class GameRuntime
{
    private readonly RuntimeConfig _config;
    private bool _isRunning;
    
    // Core systems
    private IWindow? _window;
    private IInputContext? _input;
    private IRHIDevice? _rhi;
    private IRHISwapchain? _swapchain;
    private IRHITexture? _depthTexture;
    // The swapchain and depth buffer are measured in PIXELS (window points x backing scale); the HUD is laid out in
    // window POINTS. On a Retina display the two differ by 2x, so they must never share one number.
    private uint _depthW, _depthH;
    private int _uiLogicalW, _uiLogicalH;
    
    // Game systems
    private World? _world;
    private SceneData? _scene;
    private ViewportRenderer? _viewportRenderer;
    private TerrainSystem? _terrainSystem;
    private BlueSky.Airborne.IPhysicsWorld? _physicsWorld;
    private BlueSky.Airborne.CarControllerSystem? _carControllerSystem;
    private IMultiplayerService? _multiplayer;
    private BlueSky.LHO.NetworkReplicationSystem? _replication;
    private readonly BlueSky.BSR.Vectra.VectraSystem _vectraSystem = new();
    private BlueSky.Echo.Echo? _echo;
    private BlueSky.Echo.EchoSystem? _echoSystem;
    private bool _showDiagnosticsHud;

    // ── Convenience helpers ────────────────────────────────────────────────────
    bool IsHost => _replication?.IsHost ?? (_config.Multiplayer.Mode == MultiplayerMode.Host);
    bool IsClient => _replication?.IsClient ?? (_config.Multiplayer.Mode == MultiplayerMode.Join);
    
    // UI system for HUD
    private EditorUI? _ui;
    private EditorUIRenderer? _uiRenderer;
    
    // Camera state — per-car camera pulled from CarController's ChaseCameraController
    private System.Numerics.Vector3 _cameraPos = new(0, 2, 0);
    private System.Numerics.Vector3 _cameraTarget = new(0, 0.5f, -5);
    private Entity _cameraFollowTarget = default; // Entity to follow (car)
    
    // Fixed timestep physics
    private const float FixedTimeStep = 1.0f / 60.0f; // 60Hz physics
    private double _physicsAccumulator = 0.0;
    private int _physicsStepsThisFrame = 0;
    private long _physicsFrame = 0;
    private bool _physicsStateInitialized = false;

    // Runtime UI / menu input state
    // Scene transitions requested by runtime UI scripts are applied after script updates.
    private bool _pendingSceneTransition;
    private string _pendingScenePath = "";
    private MultiplayerMode _pendingMultiplayerMode = MultiplayerMode.Offline;
    private string _pendingSessionName = "";
    private string _pendingJoinSessionId = "";
    
    // The lobby key (UserId or DisplayName) that identifies THIS player in the lobby.
    // Set when hosting or joining. Used to match replicated entities to the local player.
    private string _localPlayerKey = "";
    
    public GameRuntime(RuntimeConfig config)
    {
        _config = config;
        _showDiagnosticsHud = config.ShowDiagnosticsHud;
    }
    
    public void Run()
    {
        // Initialize core systems
        InitializeCore();
        
        // Initialize platform (window management)
        InitializePlatform();
        
        // Initialize rendering
        InitializeRendering();
        
        // Initialize physics
        InitializePhysics();
        
        // Initialize input
        InitializeInput();

        // Initialize multiplayer / EOS session coordination
        InitializeMultiplayer();
        
        // Load scene
        LoadScene(_config.ScenePath);
        
        // Initialize gameplay systems (CarControllerSystem, etc.) AFTER scene load
        InitializeGameplaySystems();
        
        // Run game loop
        _isRunning = true;
        RunGameLoop();
        
        // Cleanup
        Shutdown();
    }
    
    void InitializeCore()
    {
        // Initialize ECS world
        _world = new World();
        _world.AddSystem(new BlueSky.SkyCore.HierarchySystem());
        _terrainSystem = new TerrainSystem(_world);

        // Initialize Echo audio (miniaudio backend; silent no-op if no device)
        try
        {
            _echo = new BlueSky.Echo.Echo(new BlueSky.Echo.MiniAudioBackend());
            _echoSystem = new BlueSky.Echo.EchoSystem(_echo, _world);
            BlueSky.Echo.EchoTeaScriptBridge.Active = _echo;
        }
        catch (Exception ex) { Console.WriteLine($"[Echo] Failed to start audio: {ex.Message}"); }
    }
    
    void InitializeGameplaySystems()
    {
        if (_world == null || _input == null)
        {
            return;
        }
        
        // Initialize TeaScriptSystem (handles all entity TeaScripts using the unified engine API)
        BlueSky.Core.Scripting.TeaScriptSystem.VectraParticles = _vectraSystem;
        _teaScriptSystem = new BlueSky.Core.Scripting.TeaScriptSystem(_world);
        _teaScriptSystem.SetInputProviders(
            IsTeaScriptKeyDown,
            IsTeaScriptMouseButtonDown,
            () => _input?.MousePosition ?? System.Numerics.Vector2.Zero);

        // Initialize CarControllerSystem (handles car input, physics, and wheel animation)
        _carControllerSystem = new BlueSky.Airborne.CarControllerSystem();
        _carControllerSystem.Initialize(_world, _input, viewport: null);
        
        // Force an immediate update to initialize car controllers BEFORE the main game loop
        _carControllerSystem.Update(0.016f);
        
    }
    
    void InitializePlatform()
    {
        // Create window
        var options = WindowOptions.Default;
        options.Title = !string.IsNullOrWhiteSpace(_config.AppTitle) ? _config.AppTitle : $"BlueSky Game - {_config.Mode}";
        options.Width = _config.WindowWidth;
        options.Height = _config.WindowHeight;
        options.Resizable = true;
        options.Fullscreen = _config.Fullscreen;
        
        _window = WindowFactory.Create(options);
        
        // Handle window close
        _window.Closing += () => _isRunning = false;
        
    }
    
    void InitializeRendering()
    {
        if (_window == null)
        {
            throw new InvalidOperationException("Window must be initialized before rendering");
        }
        
        // Initialize RHI (auto-detect Metal/Vulkan/DX11)
        _rhi = RHIDevice.CreateDefault(_window, Array.Empty<string>());
        
        // Create swapchain
        var presentMode = _config.VSync ? PresentMode.Vsync : PresentMode.Immediate;
        _swapchain = _rhi.CreateSwapchain(_window, presentMode);
        
        // Create depth texture at the framebuffer's pixel size (it must match the swapchain's colour target).
        var framebuffer = _window.FramebufferSize;
        _depthW = (uint)MathF.Max(1, framebuffer.X);
        _depthH = (uint)MathF.Max(1, framebuffer.Y);
        _depthTexture = CreateDepthTexture(_depthW, _depthH);
        Console.WriteLine($"[Runtime] Render targets: swapchain {_swapchain.Width}x{_swapchain.Height} px, depth {_depthW}x{_depthH} px, window {_window.Size.X:0}x{_window.Size.Y:0} pt");
        
        // Initialize viewport renderer (reuses editor rendering code!)
        if (_world != null)
        {
            _viewportRenderer = new ViewportRenderer(_rhi, _world, terrainSystem: _terrainSystem, _swapchain.Format, showEditorGizmos: false, vectraSystem: _vectraSystem);
        }

        // Initialize UI system for HUD rendering (laid out in window points)
        _uiLogicalW = (int)MathF.Max(1, _window.Size.X);
        _uiLogicalH = (int)MathF.Max(1, _window.Size.Y);
        _ui = new EditorUI((uint)_uiLogicalW, (uint)_uiLogicalH);
        _uiRenderer = new EditorUIRenderer(_rhi);
        
        // Initialize font atlas for text rendering
        string fontPath = System.IO.Path.Combine(AppContext.BaseDirectory, "roboto.ttf");
        
        if (System.IO.File.Exists(fontPath))
        {
            _uiRenderer.FontAtlas = new FontAtlas(_rhi, fontPath);
            _ui.MeasureTextWidth = text => _uiRenderer.FontAtlas.MeasureWidth(text.AsSpan());
            _ui.TextLineHeight = _uiRenderer.FontAtlas.LineHeight;
        }
        // The UI projection is in window points, matching the editor.
        _uiRenderer.Resize(_uiLogicalW, _uiLogicalH);
        
    }

    IRHITexture CreateDepthTexture(uint width, uint height)
    {
        return _rhi!.CreateTexture(new TextureDesc
        {
            Width = width,
            Height = height,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil,
            DebugName = "Runtime.DepthBuffer"
        });
    }

    /// <summary>
    /// Keeps the swapchain, depth buffer and HUD matched to the window's current size, every frame, so fullscreen and
    /// dragging the window edge work as well as the first frame does. Returns false when the window has no drawable
    /// area (minimized), in which case the frame is skipped.
    /// </summary>
    bool EnsureRenderTargetSizes(out uint pixelW, out uint pixelH)
    {
        pixelW = 0;
        pixelH = 0;
        if (_window == null || _swapchain == null || _rhi == null)
            return false;

        var framebuffer = _window.FramebufferSize;
        pixelW = (uint)MathF.Max(0, framebuffer.X);
        pixelH = (uint)MathF.Max(0, framebuffer.Y);
        if (pixelW == 0 || pixelH == 0)
            return false;

        if (_swapchain.Width != pixelW || _swapchain.Height != pixelH)
            _swapchain.Resize(pixelW, pixelH);

        if (_depthTexture == null || _depthW != pixelW || _depthH != pixelH)
        {
            // The old depth buffer may still be read by a frame in flight.
            _rhi.WaitIdle();
            _depthTexture?.Dispose();
            _depthTexture = CreateDepthTexture(pixelW, pixelH);
            _depthW = pixelW;
            _depthH = pixelH;
            Console.WriteLine($"[Runtime] Render size {pixelW}x{pixelH} px (window {_window.Size.X:0}x{_window.Size.Y:0} pt)");
        }

        int logicalW = (int)MathF.Max(1, _window.Size.X);
        int logicalH = (int)MathF.Max(1, _window.Size.Y);
        if (logicalW != _uiLogicalW || logicalH != _uiLogicalH)
        {
            _uiLogicalW = logicalW;
            _uiLogicalH = logicalH;
            _ui?.Resize((uint)logicalW, (uint)logicalH);
            _uiRenderer?.Resize(logicalW, logicalH);
        }

        return true;
    }
    
    void InitializePhysics()
    {
        // Backend selection is centralized in the modular vehicle-capable
        // world. Callers depend only on IPhysicsWorld.
        _physicsWorld = new BlueSky.Airborne.VehicleWorldPhysics(preferJolt: true);
        _physicsAccumulator = 0.0;
        _physicsFrame = 0;
        _physicsStateInitialized = false;
        _prevPhysicsState.Clear();
        _currentPhysicsState.Clear();
        
        // Initialize physics bridge for TeaScript
        if (_physicsWorld != null)
        {
            BlueSky.Airborne.PhysicsTeaScriptBridge.Initialize(_physicsWorld);
        }
    }
    
    void InitializeInput()
    {
        if (_window == null)
        {
            throw new InvalidOperationException("Window must be initialized before input");
        }
        
        _input = _window.CreateInput();
        
        // ESC to quit, F3 to toggle diagnostics HUD
        _input.KeyDown += (key, mods) =>
        {
            if (key == KeyCode.Escape)
            {
                _isRunning = false;
            }
            else if (key == KeyCode.F3)
            {
                _showDiagnosticsHud = !_showDiagnosticsHud;
            }
        };
        
    }
    
    void InitializeMultiplayer()
    {
        // Create the multiplayer service (EOS or offline)
        if (_config.Multiplayer.Eos.IsConfigured && !_config.Multiplayer.Enabled)
        {
            _multiplayer = new EosMultiplayerService(_config.Multiplayer);
            _multiplayer.Initialize(_ => { });
        }
        else
        {
            _multiplayer = MultiplayerServiceFactory.Create(_config.Multiplayer);
            _multiplayer.Initialize(_ => { });
        }

        // Set up the static bridge for TeaScript API
        BlueSky.LHO.NetworkingTeaScriptBridge.Register(_multiplayer, (scenePath, modeStr, sessionName, joinSessionId) =>
        {
            _pendingScenePath = scenePath ?? "";
            _pendingMultiplayerMode = MultiplayerMode.Offline;
            if (!string.IsNullOrEmpty(modeStr))
            {
                Enum.TryParse(modeStr, true, out _pendingMultiplayerMode);
            }
            _pendingSessionName = sessionName ?? "";
            _pendingJoinSessionId = joinSessionId ?? "";
            _pendingSceneTransition = true;
        });

        // Create the replication system and attach P2P transport
        _replication = new BlueSky.LHO.NetworkReplicationSystem(_world, msg => Console.WriteLine(msg));
        _replication.AttachTransport(_multiplayer);

        if (_config.Multiplayer.Mode == BlueSky.LHO.MultiplayerMode.Host ||
            _config.Multiplayer.Mode == BlueSky.LHO.MultiplayerMode.Join)
        {
            if (_config.Multiplayer.Mode == BlueSky.LHO.MultiplayerMode.Host)
            {
                _replication.InitializeAsHost();
                // Set host's own key from config/localname
                _localPlayerKey = _multiplayer.LocalP2PId ?? _config.Multiplayer.LocalPlayerName ?? "Host";
            }
            else
            {
                _replication.InitializeAsClient();
                // Use own P2P ID so the client recognizes ownership messages
                // (the host uses GetStablePlayerKey with the same UserId/P2PId)
                _localPlayerKey = _multiplayer.LocalP2PId ?? "";
            }

            // HOST: when lobby players update, spawn entities for new players, remove departed.
            // Players are sorted so the host is always assigned the first car (spawn point 0),
            // then joined clients in lobby order.
            BlueSky.LHO.NetworkingTeaScriptBridge.Lobby!.OnPlayersUpdated += (players) =>
            {
                if (_replication == null || _world == null) return;
                if (!BlueSky.LHO.NetworkingTeaScriptBridge.IsLobbyHost()) return;

                string prefabPath = FindNetworkManagerPrefabPath();
                if (string.IsNullOrEmpty(prefabPath)) return;

                // Detect departed players and clean up their entities
                var currentKeys = new System.Collections.Generic.HashSet<string>(
                    players.Select(p => GetStablePlayerKey(p)));
                foreach (var existingKey in _replication.PlayerToEntityMapping.Keys.ToList())
                {
                    if (!currentKeys.Contains(existingKey))
                    {
                        int eid = _replication.GetEntityIdForPlayer(existingKey);
                        _replication.RemovePlayerEntity(existingKey);
                    }
                }

                // Sort: host first (priority possession), then joined clients
                var sortedPlayers = players
                    .OrderByDescending(p => GetStablePlayerKey(p) == _localPlayerKey ? 1 : 0)
                    .ThenBy(p => p.JoinedAt)
                    .ToList();

                // Spawn entities for new players
                int spawnIdx = 0;
                foreach (var player in sortedPlayers)
                {
                    string playerKey = GetStablePlayerKey(player);
                    if (!_replication.HasPlayer(playerKey))
                    {
                        int entityId = _replication.SpawnPlayerEntity(playerKey, prefabPath, spawnIdx);
                        spawnIdx++;
                        if (entityId >= 0)
                        {
                            RegisterSpawnedCarPhysics(entityId);

                            if (playerKey == _localPlayerKey)
                            {
                                _cameraFollowTarget = FindEntityById(entityId);
                            }

                            _replication.BroadcastOwnership(playerKey, entityId, prefabPath);
                        }
                    }
                }
            };

            // CLIENT: when ownership received from host, set camera target
            // Do NOT register physics — client uses TransformComponent from interpolation only
            _replication.OnReplicatedEntitySpawned += (entityId, playerName) =>
            {

                // Only set camera + allow input for OUR entity (not for other players' entities)
                if (playerName == _localPlayerKey)
                {
                    _cameraFollowTarget = FindEntityById(entityId);
                }
            };
        }

        // Wire lobby server travel
        BlueSky.LHO.NetworkingTeaScriptBridge.Lobby!.OnServerTravel += () =>
        {
            string scenePath = BlueSky.LHO.NetworkingTeaScriptBridge.Lobby!.ScenePath;
            if (!string.IsNullOrEmpty(scenePath))
            {
                bool isLobbyHost = BlueSky.LHO.NetworkingTeaScriptBridge.Lobby!.IsHost;
                _pendingMultiplayerMode = isLobbyHost
                    ? BlueSky.LHO.MultiplayerMode.Host
                    : BlueSky.LHO.MultiplayerMode.Join;
                _pendingScenePath = scenePath;
                _pendingSceneTransition = true;
                _replication?.Pause(); // Buffer packets — world is about to be replaced
            }
        };
    }
    
    void LoadScene(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            Console.Error.WriteLine("[Runtime] ⚠️ No scene path was provided - loading the built-in test scene.");
            
            // Create a test scene with a simple cube
            CreateTestScene();
            return;
        }
        
        if (!File.Exists(scenePath))
        {
            Console.Error.WriteLine($"[Runtime] ⚠️ Scene file not found: '{scenePath}' (mode: {_config.Mode}) - loading the built-in test scene instead.");
            
            // Create a test scene with a simple cube
            CreateTestScene();
            return;
        }
        
        
        // Load scene data from JSON
        _scene = SceneSerializer.LoadScene(scenePath);
        
        if (_scene == null)
        {
            Console.Error.WriteLine($"[Runtime] ⚠️ Scene file could not be parsed: '{scenePath}' - loading the built-in test scene instead.");
            CreateTestScene();
            return;
        }
        
        
        Console.WriteLine($"[Runtime] Loaded scene '{_scene.Name}' ({_scene.Entities.Count} entities) from {scenePath}");

        // Instantiate entities in the world using SceneConverter
        if (_world != null)
        {
            try
            {
                BlueSky.Meridian.SceneConverter.SceneDataToWorld(_scene, _world);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Runtime] SceneDataToWorld error: {ex.Message}");
            }
            
            // Load terrain binary assets (heightmaps + correct dimensions)
            try
            {
                _terrainSystem?.Clear();
                _terrainSystem?.LoadTerrainAssetsForWorld();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Runtime] LoadTerrainAssetsForWorld error: {ex.Message}");
            }
            
            // Register physics bodies and terrain colliders
            RegisterPhysicsBodies();
        }
    }
    
    void RegisterPhysicsBodies()
    {
        
        if (_world == null || _physicsWorld == null)
        {
            return;
        }
        
        
        // Register rigidbodies
        var physicsQuery = _world.CreateQuery()
            .All<BlueSky.SkyCore.Builtin.PhysicsComponent>()
            .All<BlueSky.SkyCore.Builtin.TransformComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(physicsQuery);
        int bodyCount = 0;
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int physIdx = chunk.GetComponentIndex(typeof(BlueSky.SkyCore.Builtin.PhysicsComponent));
            int transIdx = chunk.GetComponentIndex(typeof(BlueSky.SkyCore.Builtin.TransformComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var phys = chunk.GetComponent<BlueSky.SkyCore.Builtin.PhysicsComponent>(i, physIdx);
                var trans = chunk.GetComponent<BlueSky.SkyCore.Builtin.TransformComponent>(i, transIdx);
                
                var pos = new System.Numerics.Vector3(trans.Position.X, trans.Position.Y, trans.Position.Z);
                var rot = new System.Numerics.Quaternion(trans.Rotation.X, trans.Rotation.Y, trans.Rotation.Z, trans.Rotation.W);
                
                try
                {
                    _physicsWorld.AddBody(entity, phys, pos, rot);
                    bodyCount++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Runtime][Physics] Failed to register body for entity {entity.Id}: {ex.Message}");
                }
            }
        }
        
        
        // Register terrain colliders
        if (_terrainSystem != null)
        {
            
            var terrainQuery = _world.CreateQuery()
                .All<BlueSky.SkyCore.Builtin.TerrainComponent>()
                .All<BlueSky.SkyCore.Builtin.TransformComponent>()
                .Build();
            
            var terrainChunks = _world.GetQueryChunks(terrainQuery);
            int terrainCount = 0;
            
            
            foreach (var chunk in terrainChunks)
            {
                var entities = chunk.GetEntities();
                int terrainIdx = chunk.GetComponentIndex(typeof(BlueSky.SkyCore.Builtin.TerrainComponent));
                
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = entities[i];
                    var terrain = chunk.GetComponent<BlueSky.SkyCore.Builtin.TerrainComponent>(i, terrainIdx);
                    
                    
                    if (!terrain.CollisionEnabled)
                    {
                        continue;
                    }
                    
                    uint terrainEntityId = (uint)entity.Id;
                    
                    try
                    {
                        
                        bool heightfieldAdded = false;
                        
                        // Add heightfield terrain collider
                        if (_terrainSystem.TryGetPhysicsHeightField(terrainEntityId, out var hf))
                        {
                            _physicsWorld.AddTerrain(entity, in hf);
                            heightfieldAdded = true;
                        }
                        else
                        {
                            
                            // Create a fallback flat heightfield at Y=0
                            var fallbackHf = new PhysicsTerrainData
                            {
                                Width = 256,
                                Height = 256,
                                WorldWidth = 512f,
                                WorldDepth = 512f,
                                Samples = new float[256 * 256],
                                OriginOffset = new System.Numerics.Vector3(-256f, 0f, -256f)
                            };
                            
                            // Fill with flat height (Y=0)
                            for (int j = 0; j < fallbackHf.Samples.Length; j++)
                            {
                                fallbackHf.Samples[j] = 0f;
                            }
                            
                            _physicsWorld.AddTerrain(entity, in fallbackHf);
                            heightfieldAdded = true;
                        }
                        
                        // Add height sampling callback
                        _physicsWorld.AddTerrain(entity, (System.Numerics.Vector3 worldPosition, out float height, out System.Numerics.Vector3 normal) =>
                            _terrainSystem.TrySampleWorldHeight(terrainEntityId, worldPosition, out height, out normal));
                        
                        if (heightfieldAdded)
                        {
                            terrainCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Runtime][Physics] Failed to register terrain {terrainEntityId}: {ex.Message}");
                    }
                }
            }
            
        }
        
        // Auto-spawn a car from the NetworkManager's prefab if available.
        // This ensures there's a drivable car even before multiplayer logic kicks in.
        SpawnNetworkManagerPrefab();

        // Find car entity to follow with camera
        FindCameraTarget();
    }

    /// <summary>
    /// Spawn the car prefab if in offline/host mode with no lobby, or singleplayer.
    /// In multiplayer, spawning is handled by the OnPlayersUpdated event in InitializeMultiplayer.
    /// </summary>
    void SpawnNetworkManagerPrefab()
    {
        if (_world == null) return;

        // In multiplayer Host/Join, the OnPlayersUpdated handler already spawns entities.
        // Only spawn directly in offline mode or host-with-lobby (handled below).
        if (_config.Multiplayer.Mode == MultiplayerMode.Join)
        {
            return;
        }

        string prefabPath = FindNetworkManagerPrefabPath();
        if (string.IsNullOrEmpty(prefabPath))
        {
            return;
        }

        // If host with lobby players, the OnPlayersUpdated handler will spawn.
        // Only spawn directly if no lobby (offline mode or lobby has no players yet).
        if (_config.Multiplayer.Mode == MultiplayerMode.Host &&
            BlueSky.LHO.NetworkingTeaScriptBridge.Lobby?.Players.Count > 0)
        {
            return;
        }

        // Offline mode: spawn one car for local play

        // Find spawn point
        System.Numerics.Vector3 spawnPos = new(0, 5, 0);
        var spawnQuery = _world.CreateQuery().All<SpawnPointComponent>().All<TransformComponent>().Build();
        foreach (var chunk in _world.GetQueryChunks(spawnQuery))
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            int spIdx = chunk.GetComponentIndex(typeof(SpawnPointComponent));
            if (chunk.Count > 0)
            {
                var t = chunk.GetComponent<TransformComponent>(0, transIdx);
                spawnPos = new System.Numerics.Vector3(t.Position.X, t.Position.Y, t.Position.Z);
            }
            break;
        }

        // Spawn via replication system (works for offline too)
        if (_replication != null)
        {
            _localPlayerKey = "LocalPlayer";
            int entityId = _replication.SpawnPlayerEntity("LocalPlayer", prefabPath, 0);
            if (entityId >= 0)
            {
                RegisterSpawnedCarPhysics(entityId);
                _cameraFollowTarget = FindEntityById(entityId);
                return;
            }
        }

        // Fallback: load prefab directly
        if (!System.IO.File.Exists(prefabPath)) return;
        var prefabData = BlueSky.Meridian.SceneSerializer.LoadScene(prefabPath);
        if (prefabData == null) return;

        BlueSky.Meridian.SceneConverter.SceneDataToWorld(prefabData, _world, clearWorld: false);

        // Position at spawn point
        var carQuery = _world.CreateQuery().All<CarControllerComponent>().All<TransformComponent>().Build();
        foreach (var chunk in _world.GetQueryChunks(carQuery))
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            for (int i = 0; i < chunk.Count; i++)
            {
                ref var t = ref chunk.GetComponent<TransformComponent>(i, transIdx);
                t.Position = new BlueSky.Ramanujan.Vector3(
                    t.Position.X + spawnPos.X, t.Position.Y + spawnPos.Y, t.Position.Z + spawnPos.Z);
                RegisterSpawnedCarPhysics(entities[i].Id);
            }
        }

        _cameraFollowTarget = FindEntityById(1);
    }

    static string GetStablePlayerKey(LobbyPlayer player)
    {
        if (!string.IsNullOrWhiteSpace(player.UserId))
            return player.UserId;
        if (!string.IsNullOrWhiteSpace(player.DisplayName))
            return player.DisplayName;
        return "Player";
    }

    Entity FindEntityById(int entityId)
    {
        if (_world == null) return default;

        var entityQuery = _world.CreateQuery().All<TransformComponent>().Build();
        foreach (var chunk in _world.GetQueryChunks(entityQuery))
        {
            foreach (var entity in chunk.GetEntities())
            {
                if (entity.Id == entityId)
                    return entity;
            }
        }

        return default;
    }

    void RegisterSpawnedCarPhysics(int entityId)
    {
        if (_physicsWorld == null || _world == null) return;
        var carEntity = FindEntityById(entityId);
        if (carEntity.Id == 0) return;
        if (_world.TryGetComponent<PhysicsComponent>(carEntity, out var phys) &&
            _world.TryGetComponent<TransformComponent>(carEntity, out var t))
        {
            var carPos = new System.Numerics.Vector3(t.Position.X, t.Position.Y, t.Position.Z);
            var carRot = new System.Numerics.Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W);
            try
            {
                _physicsWorld.AddBody(carEntity, phys, carPos, carRot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Runtime][Physics] Failed to register spawned car body for entity {entityId}: {ex.Message}");
            }
        }
    }

    void FindCameraTarget()
    {
        if (_world == null)
            return;
        
        // Find the first entity with CarControllerComponent
        var carQuery = _world.CreateQuery()
            .All<BlueSky.SkyCore.Builtin.CarControllerComponent>()
            .All<BlueSky.SkyCore.Builtin.TransformComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(carQuery);
        
        foreach (var chunk in chunks)
        {
            if (chunk.Count > 0)
            {
                var entities = chunk.GetEntities();
                _cameraFollowTarget = entities[0];
                return;
            }
        }
        
        // If no car found, try to find any entity with a mesh (static or skeletal)
        var meshQuery = _world.CreateQuery()
            .Any(typeof(BlueSky.SkyCore.Builtin.StaticMeshComponent), typeof(BlueSky.SkyCore.Builtin.SkeletalMeshComponent))
            .All<BlueSky.SkyCore.Builtin.TransformComponent>()
            .Build();
        
        chunks = _world.GetQueryChunks(meshQuery);
        
        foreach (var chunk in chunks)
        {
            if (chunk.Count > 0)
            {
                var entities = chunk.GetEntities();
                _cameraFollowTarget = entities[0];
                return;
            }
        }
        
    }
    
    void CreateTestScene()
    {
        if (_world == null) return;
        
        // Create a simple cube entity for testing
        var entity = _world.CreateEntity();
        
        // Add Transform component
        var transform = new TransformComponent
        {
            Position = new BlueSky.Ramanujan.Vector3(0, 0, 0),
            Rotation = new BlueSky.Ramanujan.Quaternion(0, 0, 0, 1),
            Scale = new BlueSky.Ramanujan.Vector3(1, 1, 1)
        };
        _world.AddComponent(entity, transform);
        
    }
    
    void RunGameLoop()
    {
        if (_window == null || _swapchain == null || _rhi == null)
        {
            throw new InvalidOperationException("Systems not initialized");
        }
        
        var lastTime = DateTime.Now;
        int frameCount = 0;
        double fpsTimer = 0;
        
        while (_isRunning)
        {
            // Process window events (required for window to stay responsive)
            _window.ProcessEvents();
            
            if (_window.IsClosing)
            {
                _isRunning = false;
                break;
            }
            
            var currentTime = DateTime.Now;
            var deltaTime = (currentTime - lastTime).TotalSeconds;
            lastTime = currentTime;
            
            // Store delta time for rendering
            _lastFrameDeltaTime = deltaTime;
            
            // Update systems
            Update(deltaTime);
            
            if (_pendingSceneTransition)
            {
                ApplySceneTransition();
            }
            
            // Render frame
            Render();
            
            // FPS counter (every second)
            frameCount++;
            fpsTimer += deltaTime;
            if (fpsTimer >= 1.0)
            {
                frameCount = 0;
                fpsTimer = 0;
            }
        }
    }
    
    // ══════════════════════════════════════════════════════════════════════════
    //  GAME SYSTEMS
    // ══════════════════════════════════════════════════════════════════════════
    
    private BlueSky.Core.Scripting.TeaScriptSystem? _teaScriptSystem;
    
    void Update(double deltaTime)
    {
        if (_world == null) return;
        _lastFrameDeltaTime = Math.Max(0.0, deltaTime);
        _elapsedTime += _lastFrameDeltaTime;
        
        // Clear per-frame input state (pressed/released edges, scroll delta)
        _input?.BeginFrame();
        
        // Begin UI frame (clears previous frame's UI elements)
        BlueSky.Runtime.UI.RuntimeUI.BeginFrame();
        
        // Draw Diagnostics HUD (toggleable via F3 or --diagnostics-hud)
        if (_showDiagnosticsHud && _frameCount >= 60)
        {
            BlueSky.Runtime.UI.RuntimeUI.Panel(10, 10, 480, 280);
            BlueSky.Runtime.UI.RuntimeUI.Label("=== BLUESKY ENGINE DIAGNOSTICS ===", 20, 20);
            BlueSky.Runtime.UI.RuntimeUI.Label($"FPS: {(int)(1.0 / deltaTime)} (dt: {deltaTime * 1000:F1}ms)", 20, 45);
            BlueSky.Runtime.UI.RuntimeUI.Label($"Physics Steps / Frame: {_physicsStepsThisFrame}", 20, 70);
            BlueSky.Runtime.UI.RuntimeUI.Label($"Physics Frame: {_physicsFrame}", 20, 95);
            
            // Find active car controller
            BlueSky.Airborne.CarController? activeCar = null;
            if (_carControllerSystem != null)
            {
                foreach (var entity in _world.GetAllEntities())
                {
                    if (_world.HasComponent<BlueSky.SkyCore.Builtin.CarControllerComponent>(entity))
                    {
                        var ctrl = BlueSky.Airborne.CarControllerSystem.GetController((uint)entity.Id);
                        if (ctrl != null && ctrl.IsPossessed)
                        {
                            activeCar = ctrl;
                            break;
                        }
                    }
                }
            }
            
            if (activeCar != null)
            {
                float speedMPH = activeCar.GetSpeedMPH();
                float speedMPS = activeCar.GetSpeed();
                BlueSky.Runtime.UI.RuntimeUI.Label($"Car Speed: {speedMPH:F1} MPH ({speedMPS:F1} m/s) | Gear: {activeCar.CurrentGear} | RPM: {activeCar.CurrentRPM:F0}", 20, 120);
                
                // Bone / Mesh Validation
                var skeletalMesh = activeCar.SkeletalMesh;
                string meshStatus = skeletalMesh is null ? "Static Mesh" : $"Skeletal ({skeletalMesh.Bones.Length} bones)";
                BlueSky.Runtime.UI.RuntimeUI.Label($"Mesh Type: {meshStatus}", 20, 145);
                
                // Wheel contact info
                if (activeCar._wheelStates != null && activeCar._wheelStates.Length >= 4)
                {
                    BlueSky.Runtime.UI.RuntimeUI.Label("Wheel Contact Diagnostics:", 20, 170);
                    string[] wheelNames = { "Front-Left", "Front-Right", "Rear-Left", "Rear-Right" };
                    for (int i = 0; i < 4; i++)
                    {
                        var ws = activeCar._wheelStates[i];
                        string groundedStr = ws.IsGrounded ? "Grounded" : "Airborne";
                        string slipStr = ws.IsGrounded ? $"Slip Ratio: {ws.SlipRatio:F2} | Slip Angle: {ws.SlipAngle * (180f / MathF.PI):F1}°" : "N/A";
                        string suspStr = $"Susp Compression: {ws.SuspensionCompression * 100f:F0}%";
                        BlueSky.Runtime.UI.RuntimeUI.Label($"  {wheelNames[i]}: {groundedStr} | {suspStr} | {slipStr}", 20, 195 + (i * 25));
                    }
                }
            }
            else
            {
                BlueSky.Runtime.UI.RuntimeUI.Label("No possessed vehicle active.", 20, 95);
            }
        }
        
        // ── 0. Process Network Replication (packets arrive via event callback) ──
        
        // ── 1. Update Car Controller System (processes input, updates physics forces) ──
        // IMPORTANT: Use variable deltaTime for input responsiveness
        // ── 0b. Client: interpolate FIRST so CarController reads current positions ──
        if (IsClient && _replication != null)
        {
            _replication.InterpolateEntities((float)deltaTime);
        }

        if (_carControllerSystem != null)
        {
            // Controllers must exist before the standalone input bridge
            // samples input, but their visual update belongs after physics.
            _carControllerSystem.EnsureInitialized();

            // In standalone, directly process car input (bypass PlayerController since it needs viewport)
            UpdateCarInput((float)deltaTime);
        }
        
        // ── 2. Execute TeaScript ─────────────────────────────────────────────
        _teaScriptSystem?.Update((float)deltaTime);

        // ── 3. Update Physics with FIXED TIMESTEP ────────────────────────────
        // Client: skip physics entirely — host runs it and sends positions via replication
        // Host/Offline: run physics locally
        if (_physicsWorld != null && !IsClient)
        {
            _physicsAccumulator += deltaTime;

            if (_physicsAccumulator > 0.25)
                _physicsAccumulator = 0.25;

            // Establish a fixed-step snapshot before the first simulation
            // tick. Snapshots are advanced only after PhysicsWorld.Step(),
            // never once per render frame.
            if (!_physicsStateInitialized)
            {
                CapturePhysicsState();
                _physicsStateInitialized = true;
            }

            _physicsStepsThisFrame = 0;
            while (_physicsAccumulator >= FixedTimeStep)
            {
                _teaScriptSystem?.FixedUpdate(FixedTimeStep);
                if (_carControllerSystem != null)
                    _carControllerSystem.FixedUpdate(FixedTimeStep);
                _physicsWorld.Step(FixedTimeStep);
                _physicsAccumulator -= FixedTimeStep;
                _physicsStepsThisFrame++;
                _physicsFrame++;

                CapturePhysicsState();
            }

            SyncPhysicsToTransforms();
        }

        // Update wheel bones/fallback wheel visuals after the body transform
        // has been synchronized. This prevents a one-fixed-step visual lag.
        _carControllerSystem?.Update((float)deltaTime);

        // ── 3b. World systems (HierarchySystem: children follow parents) ─────
        _world.Update((float)deltaTime);

        // ── 4. Camera (after physics/replication so positions are current) ───
        UpdateCamera(deltaTime);

        // ── 4b. Echo audio (listener follows the game camera) ────────────────
        if (_echo != null)
        {
            var forward = _cameraTarget - _cameraPos;
            if (forward.LengthSquared() > 1e-6f)
                forward = System.Numerics.Vector3.Normalize(forward);
            else
                forward = -System.Numerics.Vector3.UnitZ;

            _echo.SetListenerPosition(_cameraPos, forward, System.Numerics.Vector3.UnitY);
            _echoSystem?.Update((float)deltaTime);
            _echo.Update((float)deltaTime);
        }

        // ── 5. Update Multiplayer / EOS ──────────────────────────────────────
        _multiplayer?.Tick(deltaTime);

        // ── 6. Update Lobby (broadcast state, retry joins) ───────────────────
        BlueSky.LHO.NetworkingTeaScriptBridge.UpdateLobby((float)deltaTime);

        // ── 7. Host: broadcast world state to all clients ────────────────────
        if (IsHost && _replication != null)
        {
            _replication.BroadcastWorldState();
        }

        // ── 8. Update Vectra particles & terrain ─────────────────────────────
        _terrainSystem?.Update();
        _vectraSystem.Update((float)_lastFrameDeltaTime);
        
        _frameCount++;
    }
    
    void UpdateCarInput(float deltaTime)
    {
        if (_world == null || _input == null)
            return;

        var carQuery = _world.CreateQuery()
            .All<BlueSky.SkyCore.Builtin.CarControllerComponent>()
            .Build();

        var chunks = _world.GetQueryChunks(carQuery);

        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var controller = BlueSky.Airborne.CarControllerSystem.GetController((uint)entity.Id);
                if (controller == null) continue;

                // ── HOST: apply client input to remote entities ──────────────
                if (IsHost && _replication != null)
                {
                    string ownerKey = _replication.GetPlayerForEntity(entity.Id);
                    if (!string.IsNullOrEmpty(ownerKey))
                    {
                        var input = _replication.GetPendingInput(ownerKey);
                        if (input != null)
                        {
                            var carInput = new CarInput
                            {
                                Throttle = input.Throttle,
                                Steer = input.Steer,
                                Brake = input.Brake,
                                Handbrake = input.Handbrake ? 1f : 0f
                            };
                            controller.ApplyNetInput(carInput, deltaTime);
                            _replication.ClearPendingInput(ownerKey);
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                // ── OWN ENTITY: process keyboard input (or send to host) ────
                string myKey = _replication?.GetPlayerForEntity(entity.Id) ?? "";
                bool isMyEntity = !string.IsNullOrEmpty(myKey) && myKey == _localPlayerKey;

                if (!isMyEntity) continue;

                if (!controller.IsPossessed)
                {
                    controller.OnPossessed(null!);
                    _cameraFollowTarget = entity;
                    BlueSky.Airborne.CarControllerSystem.PossessedController = controller;
                }

                if (IsClient && _replication != null)
                {
                    float throttle = 0f, steer = 0f, brake = 0f, handbrake = 0f;
                    ReadCarInput(out throttle, out steer, out brake, out handbrake);
                    _replication.SendInputToHost(myKey, throttle, brake, steer, handbrake > 0.5f);
                }
                else
                {
                    controller.ProcessInput(_input, deltaTime);
                }
            }
        }
    }

    void ReadCarInput(out float throttle, out float steer, out float brake, out float handbrake)
    {
        throttle = 0f; steer = 0f; brake = 0f; handbrake = 0f;
        if (_input == null) return;

        if (_input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.W) || _input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.Up))
            throttle = 1f;
        else if (_input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.S) || _input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.Down))
            brake = 1f;
        if (_input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.A) || _input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.Left))
            steer = -1f;
        if (_input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.D) || _input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.Right))
            steer = 1f;
        if (_input.IsKeyDown(BlueSky.Aegis.Input.KeyCode.Space))
            handbrake = 1f;
    }
    
    void UpdateCamera(double deltaTime)
    {
        if (_world == null)
            return;
        
        // Pull camera from the CarController's per-car ChaseCameraController
        if (_cameraFollowTarget.Id != 0)
        {
            var controller = BlueSky.Airborne.CarControllerSystem.GetController((uint)_cameraFollowTarget.Id);
            if (controller != null)
            {
                // ChaseCameraController updates inside CarControllerSystem.Update(),
                // so its GetCameraPosition/Target are already current this frame.
                var camPos = controller.GetCameraPosition();
                var camTarget = controller.GetCameraTarget();
                
                _cameraPos = new System.Numerics.Vector3(camPos.X, camPos.Y, camPos.Z);
                _cameraTarget = new System.Numerics.Vector3(camTarget.X, camTarget.Y, camTarget.Z);
                
                return;
            }
        }
        
        // Fallback: static camera (no car found yet)
    }
    
    // ── PHYSICS INTERPOLATION STATE ────────────────────────────────────────────
    // Store previous physics state for each entity to interpolate between fixed timesteps
    private readonly Dictionary<int, (System.Numerics.Vector3 pos, System.Numerics.Quaternion rot)> _prevPhysicsState = new();
    private readonly Dictionary<int, (System.Numerics.Vector3 pos, System.Numerics.Quaternion rot)> _currentPhysicsState = new();

    private void CapturePhysicsState()
    {
        if (_world == null || _physicsWorld == null)
            return;

        var physicsQuery = _world.CreateQuery()
            .All<BlueSky.SkyCore.Builtin.PhysicsComponent>()
            .All<BlueSky.SkyCore.Builtin.TransformComponent>()
            .Build();

        foreach (var chunk in _world.GetQueryChunks(physicsQuery))
        {
            var entities = chunk.GetEntities();
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                if (!_physicsWorld.HasBody(entity))
                    continue;

                var position = _physicsWorld.GetPosition(entity);
                var rotation = _physicsWorld.GetRotation(entity);
                var state = (position, rotation);

                if (_currentPhysicsState.TryGetValue(entity.Id, out var previous))
                    _prevPhysicsState[entity.Id] = previous;
                else
                    _prevPhysicsState[entity.Id] = state;

                _currentPhysicsState[entity.Id] = state;
            }
        }
    }

    void SyncPhysicsToTransforms()
    {
        if (_world == null || _physicsWorld == null)
            return;
        
        // Query all entities with physics + transform
        var physicsQuery = _world.CreateQuery()
            .All<BlueSky.SkyCore.Builtin.PhysicsComponent>()
            .All<BlueSky.SkyCore.Builtin.TransformComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(physicsQuery);
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(BlueSky.SkyCore.Builtin.TransformComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                
                if (!_physicsWorld.HasBody(entity))
                    continue;

                // Skip per-wheel cars — CarController manages their transforms directly
                var carCtrl = BlueSky.Airborne.CarControllerSystem.GetController((uint)entity.Id);
                if (carCtrl?.UsePerWheelPhysics == true)
                    continue;

                // Read the most recent fixed-step snapshot. A body spawned
                // between snapshots is initialized without interpolation.
                if (!_currentPhysicsState.TryGetValue(entity.Id, out var current))
                {
                    var position = _physicsWorld.GetPosition(entity);
                    var rotation = _physicsWorld.GetRotation(entity);
                    current = (position, rotation);
                    _prevPhysicsState[entity.Id] = current;
                    _currentPhysicsState[entity.Id] = current;
                }

                var physPos = current.pos;
                var physRot = current.rot;
                
                // Calculate interpolation alpha (how far between physics steps we are)
                float alpha = Math.Clamp((float)(_physicsAccumulator / FixedTimeStep), 0.0f, 1.0f);
                
                // Interpolate between previous and current physics state
                System.Numerics.Vector3 interpPos;
                System.Numerics.Quaternion interpRot;
                
                if (_prevPhysicsState.TryGetValue(entity.Id, out var prev))
                {
                    interpPos = System.Numerics.Vector3.Lerp(prev.pos, physPos, alpha);
                    interpRot = System.Numerics.Quaternion.Slerp(prev.rot, physRot, alpha);
                }
                else
                {
                    // First frame - no interpolation
                    interpPos = physPos;
                    interpRot = physRot;
                }
                
                // Update transform with interpolated values
                ref var transform = ref chunk.GetComponent<BlueSky.SkyCore.Builtin.TransformComponent>(i, transIdx);
                transform.Position = new BlueSky.Ramanujan.Vector3(interpPos.X, interpPos.Y, interpPos.Z);
                transform.Rotation = new BlueSky.Ramanujan.Quaternion(interpRot.X, interpRot.Y, interpRot.Z, interpRot.W);
            }
        }
    }
    
    private bool IsTeaScriptKeyDown(string keyName)
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

    private bool IsTeaScriptMouseButtonDown(int button)
    {
        if (_input == null || button < 0 || button > (int)MouseButton.X2)
            return false;

        return _input.IsMouseButtonDown((MouseButton)button);
    }

    
    private ulong _frameCount = 0;
    private double _lastFrameDeltaTime = 0.016;
    private double _elapsedTime;
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    void Render()
    {
        if (_swapchain == null || _rhi == null)
            return;

        if (!EnsureRenderTargetSizes(out uint pixelW, out uint pixelH) || _depthTexture == null)
            return;

        IntPtr autoreleasePool = OperatingSystem.IsMacOS() ? objc_autoreleasePoolPush() : IntPtr.Zero;
        try
        {
            // Acquire next image from swapchain
            _swapchain.AcquireNextImage();
            
            // Get command buffer
            using var cmd = _rhi.CreateCommandBuffer();
            
            // Set up camera matrices
            var view = System.Numerics.Matrix4x4.CreateLookAt(_cameraPos, _cameraTarget, System.Numerics.Vector3.UnitY);
            var proj = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
                60.0f * MathF.PI / 180.0f,  // FOV
                (float)pixelW / pixelH,  // Aspect
                0.1f,  // Near
                1000.0f  // Far
            );

            // Render shadow map pass BEFORE main render pass so standalone runtime has live shadows
            if (_viewportRenderer != null && _world != null)
            {
                _viewportRenderer.PreRender(cmd, BlueSky.Meridian.GlobalEnvironment.SunDirection, _cameraPos);
            }

            // Begin render pass with clear
            var clearColor = ClearValue.FromColor(0.39f, 0.58f, 0.93f, 1.0f);
            cmd.BeginRenderPass(new[] { _swapchain.CurrentRenderTarget }, _depthTexture, clearColor);
            
            // Render scene using ViewportRenderer if available
            if (_viewportRenderer != null && _world != null)
            {
                _viewportRenderer.Render(
                    cmd,
                    view,
                    proj,
                    _cameraPos,
                    viewportX: 0,
                    viewportY: 0,
                    viewportW: (int)pixelW,
                    viewportH: (int)pixelH,
                    deltaTime: (float)Math.Max(0.0001, _lastFrameDeltaTime)
                );
            }
            // Render UI overlay (HUD from TeaScript) BEFORE ending render pass
            if (_ui != null && _uiRenderer != null && _input != null)
            {
                var mousePos = _input.MousePosition;
                bool mouseDown = _input.IsMouseButtonDown(MouseButton.Left);
                
                _ui.BeginFrame(mousePos, mouseDown, "", false, _input.ScrollDelta.Y);
                BlueSky.Runtime.UI.RuntimeUI.Render(_ui, _uiLogicalW, _uiLogicalH, (float)_lastFrameDeltaTime);
                _uiRenderer.Render(cmd, _ui);
            }
            
            cmd.EndRenderPass();
            
            // Submit and present (CRITICAL: must pass swapchain for presentation!)
            _rhi.Submit(cmd, _swapchain);
            _swapchain.Present();
        }
        finally
        {
            if (autoreleasePool != IntPtr.Zero)
                objc_autoreleasePoolPop(autoreleasePool);
        }
    }
    
    void Shutdown()
    {
        
        // Cleanup in reverse order
        _echoSystem?.Dispose();
        _echoSystem = null;
        BlueSky.Echo.EchoTeaScriptBridge.Active = null;
        _echo?.Dispose();
        _echo = null;
        _uiRenderer?.Dispose();
        _carControllerSystem?.Cleanup();
        _viewportRenderer?.Dispose();
        _replication?.Dispose();
        _replication = null;
        _multiplayer?.Shutdown();
        _multiplayer?.Dispose();
        
        // Cleanup physics
        if (_physicsWorld != null)
        {
            BlueSky.Airborne.PhysicsTeaScriptBridge.Shutdown();
            _physicsWorld.Dispose();
            _physicsWorld = null;
        }
        
        _depthTexture?.Dispose();
        _swapchain?.Dispose();
        _rhi?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
        _world?.Dispose();
        
    }

    void ApplySceneTransition()
    {
        _pendingSceneTransition = false;
        ReloadSceneAndMultiplayer(_pendingScenePath, _pendingMultiplayerMode, _pendingSessionName, _pendingJoinSessionId);
    }

    /// <summary>
    /// Search the loaded scene for a NetworkManager entity and return its prefab path.
    /// Returns null/empty if not found.
    /// </summary>
    string FindNetworkManagerPrefabPath()
    {
        if (_world == null) return "";
        
        var query = _world.CreateQuery()
            .All<NetworkManagerComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(query);
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int nmIdx = chunk.GetComponentIndex(typeof(NetworkManagerComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var nm = chunk.GetComponent<NetworkManagerComponent>(i, nmIdx);
                string path = BlueSky.SkyCore.Builtin.NetworkManagerStorage.GetPrefabPath(nm.StorageIndex);
                if (!string.IsNullOrEmpty(path))
                {
                    return path;
                }
            }
        }
        
        return "";
    }

    void ReloadSceneAndMultiplayer(string scenePath, MultiplayerMode mpMode, string sessionName, string joinSessionId)
    {
        var travelPlayers = BlueSky.LHO.NetworkingTeaScriptBridge.Lobby?.Players.ToList() ?? new System.Collections.Generic.List<LobbyPlayer>();

        // 1. Shutdown car system + reset replication (keep multiplayer service alive)
        _carControllerSystem?.Cleanup();
        _carControllerSystem = null;
        _replication?.Reset(); // Clear entity maps, keep EOS transport alive

        // Cleanup physics
        if (_physicsWorld != null)
        {
            BlueSky.Airborne.PhysicsTeaScriptBridge.Shutdown();
            _physicsWorld.Dispose();
            _physicsWorld = null;
        }

        _teaScriptSystem?.Cleanup();
        _teaScriptSystem = null;

        // Dispose old world + recreate
        _world?.Dispose();
        _world = new World();
        _world.AddSystem(new BlueSky.SkyCore.HierarchySystem());
        _replication?.SetWorld(_world);
        _terrainSystem = new TerrainSystem(_world);

        // Dispose old renderer (GPU resources) before creating new one
        _viewportRenderer?.Dispose();
        _viewportRenderer = null;
        if (_swapchain != null && _rhi != null)
            _viewportRenderer = new ViewportRenderer(_rhi, _world, terrainSystem: _terrainSystem, _swapchain.Format, showEditorGizmos: false, vectraSystem: _vectraSystem);

        InitializePhysics();

        // 2. Update multiplayer config (but DON'T recreate multiplayer service)
        _config.Multiplayer.Mode = mpMode;
        _config.Multiplayer.SessionName = sessionName;
        _config.Multiplayer.JoinSessionId = joinSessionId;

        // Reattach transport (re-registers event handlers on existing multiplayer service)
        if (_replication != null && _multiplayer != null)
        {
            _replication.AttachTransport(_multiplayer);
            if (mpMode == MultiplayerMode.Host)
                _replication.InitializeAsHost();
            else if (mpMode == MultiplayerMode.Join)
                _replication.InitializeAsClient();

            // Force reconnect P2P — connections degrade during scene reload
            var peerIds = travelPlayers.Select(p => p.UserId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct();
            _multiplayer.ForceReconnectToAllPeers(peerIds);
            Console.WriteLine($"[Runtime] P2P reconnect: {travelPlayers.Count} lobby players");
        }

        // Re-join lobby players after scene reload
        foreach (var player in travelPlayers)
            BlueSky.LHO.NetworkingTeaScriptBridge.AddLobbyPlayer(player.UserId, player.DisplayName);

        // Reset camera/possession state
        _cameraFollowTarget = default;
        // For Host: set key from P2P ID. For Client: keep the key set during joinLobby()
        // (the client's P2P ID is what the host uses as entity key via GetStablePlayerKey)
        if (_config.Multiplayer.Mode == MultiplayerMode.Host)
        {
            _localPlayerKey = _multiplayer?.LocalP2PId ?? _config.Multiplayer.LocalPlayerName ?? "Host";
        }
        // Client keeps _localPlayerKey from joinLobby — don't reset to ""

        // 3. Load scene + gameplay
        LoadScene(scenePath);
        InitializeGameplaySystems();

        // 4. After scene reload, manually spawn entities for existing lobby players.
        // OnPlayersUpdated only fires when the list CHANGES — nobody joins/leaves
        // during travel, so we must do it here.
        // Players are sorted so the host gets the first car (priority possession),
        // then joined clients in lobby order.
        if (_config.Multiplayer.Mode == MultiplayerMode.Host && _replication != null)
        {
            string prefabPath = FindNetworkManagerPrefabPath();
            if (!string.IsNullOrEmpty(prefabPath))
            {
                var currentPlayers = BlueSky.LHO.NetworkingTeaScriptBridge.Lobby?.Players;
                if (currentPlayers != null)
                {
                    var sortedPlayers = currentPlayers
                        .OrderByDescending(p => GetStablePlayerKey(p) == _localPlayerKey ? 1 : 0)
                        .ThenBy(p => p.JoinedAt)
                        .ToList();

                    int spawnIdx = 0;
                    foreach (var player in sortedPlayers)
                    {
                        string playerKey = GetStablePlayerKey(player);
                        if (!_replication.HasPlayer(playerKey))
                        {
                            int entityId = _replication.SpawnPlayerEntity(playerKey, prefabPath, spawnIdx);
                            spawnIdx++;
                            if (entityId >= 0)
                            {
                                RegisterSpawnedCarPhysics(entityId);
                                if (playerKey == _localPlayerKey)
                                {
                                    _cameraFollowTarget = FindEntityById(entityId);
                                }
                                _replication.BroadcastOwnership(playerKey, entityId, prefabPath);
                            }
                        }
                    }
                }
            }
        }

        // Resume replication — replay any ownership messages that arrived during the transition
        _replication?.Resume();
    }
}
