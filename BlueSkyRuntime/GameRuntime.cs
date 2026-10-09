using System;
using System.IO;
using System.Linq;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Scene;
using BlueSky.Editor;
using BlueSky.Networking;
using BlueSky.Rendering;
using BlueSky.Airborne;
using BlueSky.Rendering.RHI;
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
    
    // Game systems
    private World? _world;
    private SceneData? _scene;
    private ViewportRenderer? _viewportRenderer;
    private TerrainSystem? _terrainSystem;
    private BlueSky.Airborne.IPhysicsWorld? _physicsWorld;
    private BlueSky.Core.Gameplay.CarControllerSystem? _carControllerSystem;
    private IMultiplayerService? _multiplayer;
    private BlueSky.Networking.NetworkReplicationSystem? _replication;

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
    private string _authStatus = "LoggedOut";
    
    // The lobby key (UserId or DisplayName) that identifies THIS player in the lobby.
    // Set when hosting or joining. Used to match replicated entities to the local player.
    private string _localPlayerKey = "";
    
    public GameRuntime(RuntimeConfig config)
    {
        _config = config;
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
        _terrainSystem = new TerrainSystem(_world);
        
    }
    
    void InitializeGameplaySystems()
    {
        if (_world == null || _input == null)
        {
            return;
        }
        
        // Initialize CarControllerSystem (handles car input, physics, and wheel animation)
        _carControllerSystem = new BlueSky.Core.Gameplay.CarControllerSystem();
        _carControllerSystem.Initialize(_world, _input, viewport: null);
        
        // Force an immediate update to initialize car controllers BEFORE the main game loop
        _carControllerSystem.Update(0.016f);
        
    }
    
    void InitializePlatform()
    {
        // Create window
        var options = WindowOptions.Default;
        options.Title = $"BlueSky Game - {_config.Mode}";
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
        
        // Create depth texture
        _depthTexture = _rhi.CreateTexture(new TextureDesc
        {
            Width = (uint)_config.WindowWidth,
            Height = (uint)_config.WindowHeight,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil,
            DebugName = "Runtime.DepthBuffer"
        });
        
        // Initialize viewport renderer (reuses editor rendering code!)
        if (_world != null)
        {
            _viewportRenderer = new ViewportRenderer(_rhi, _world, terrainSystem: _terrainSystem, _swapchain.Format, showEditorGizmos: false);
        }

        // Initialize UI system for HUD rendering
        _ui = new EditorUI((uint)_config.WindowWidth, (uint)_config.WindowHeight);
        _uiRenderer = new EditorUIRenderer(_rhi);
        
        // Initialize font atlas for text rendering
        string fontPath = System.IO.Path.Combine(AppContext.BaseDirectory, "roboto.ttf");
        
        if (System.IO.File.Exists(fontPath))
        {
            _uiRenderer.FontAtlas = new FontAtlas(_rhi, fontPath);
            _ui.MeasureTextWidth = text => _uiRenderer.FontAtlas.MeasureWidth(text.AsSpan());
            _ui.TextLineHeight = _uiRenderer.FontAtlas.LineHeight;
        }
        // CRITICAL: Resize UI renderer to match window dimensions
        _uiRenderer.Resize(_config.WindowWidth, _config.WindowHeight);
        
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
        
        // ESC to quit
        _input.KeyDown += (key, mods) =>
        {
            if (key == KeyCode.Escape)
            {
                _isRunning = false;
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
        BlueSky.Networking.NetworkingTeaScriptBridge.Register(_multiplayer);

        // Create the replication system and attach P2P transport
        _replication = new BlueSky.Networking.NetworkReplicationSystem(_world, msg => Console.WriteLine(msg));
        _replication.AttachTransport(_multiplayer);

        if (_config.Multiplayer.Mode == BlueSky.Networking.MultiplayerMode.Host ||
            _config.Multiplayer.Mode == BlueSky.Networking.MultiplayerMode.Join)
        {
            if (_config.Multiplayer.Mode == BlueSky.Networking.MultiplayerMode.Host)
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
            BlueSky.Networking.NetworkingTeaScriptBridge.Lobby!.OnPlayersUpdated += (players) =>
            {
                if (_replication == null || _world == null) return;
                if (!BlueSky.Networking.NetworkingTeaScriptBridge.IsLobbyHost()) return;

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
        BlueSky.Networking.NetworkingTeaScriptBridge.Lobby!.OnServerTravel += () =>
        {
            string scenePath = BlueSky.Networking.NetworkingTeaScriptBridge.Lobby!.ScenePath;
            if (!string.IsNullOrEmpty(scenePath))
            {
                bool isLobbyHost = BlueSky.Networking.NetworkingTeaScriptBridge.Lobby!.IsHost;
                _pendingMultiplayerMode = isLobbyHost
                    ? BlueSky.Networking.MultiplayerMode.Host
                    : BlueSky.Networking.MultiplayerMode.Join;
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
            
            // Create a test scene with a simple cube
            CreateTestScene();
            return;
        }
        
        if (!File.Exists(scenePath))
        {
            
            // Create a test scene with a simple cube
            CreateTestScene();
            return;
        }
        
        
        // Load scene data from JSON
        _scene = SceneSerializer.LoadScene(scenePath);
        
        if (_scene == null)
        {
            CreateTestScene();
            return;
        }
        
        
        // Instantiate entities in the world using SceneConverter
        if (_world != null)
        {
            try
            {
                BlueSky.Core.Scene.SceneConverter.SceneDataToWorld(_scene, _world);
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
            .All<BlueSky.Core.ECS.Builtin.PhysicsComponent>()
            .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(physicsQuery);
        int bodyCount = 0;
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int physIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.PhysicsComponent));
            int transIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.TransformComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var phys = chunk.GetComponent<BlueSky.Core.ECS.Builtin.PhysicsComponent>(i, physIdx);
                var trans = chunk.GetComponent<BlueSky.Core.ECS.Builtin.TransformComponent>(i, transIdx);
                
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
                .All<BlueSky.Core.ECS.Builtin.TerrainComponent>()
                .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
                .Build();
            
            var terrainChunks = _world.GetQueryChunks(terrainQuery);
            int terrainCount = 0;
            
            
            foreach (var chunk in terrainChunks)
            {
                var entities = chunk.GetEntities();
                int terrainIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.TerrainComponent));
                
                
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = entities[i];
                    var terrain = chunk.GetComponent<BlueSky.Core.ECS.Builtin.TerrainComponent>(i, terrainIdx);
                    
                    
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
            BlueSky.Networking.NetworkingTeaScriptBridge.Lobby?.Players.Count > 0)
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
        var prefabData = BlueSky.Core.Scene.SceneSerializer.LoadScene(prefabPath);
        if (prefabData == null) return;

        BlueSky.Core.Scene.SceneConverter.SceneDataToWorld(prefabData, _world, clearWorld: false);

        // Position at spawn point
        var carQuery = _world.CreateQuery().All<CarControllerComponent>().All<TransformComponent>().Build();
        foreach (var chunk in _world.GetQueryChunks(carQuery))
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            for (int i = 0; i < chunk.Count; i++)
            {
                ref var t = ref chunk.GetComponent<TransformComponent>(i, transIdx);
                t.Position = new BlueSky.Core.Math.Vector3(
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
            .All<BlueSky.Core.ECS.Builtin.CarControllerComponent>()
            .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
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
            .Any(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent), typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent))
            .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
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
            Position = new BlueSky.Core.Math.Vector3(0, 0, 0),
            Rotation = new BlueSky.Core.Math.Quaternion(0, 0, 0, 1),
            Scale = new BlueSky.Core.Math.Vector3(1, 1, 1)
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
    
    // TeaScript engines per entity (keyed by entity ID)
    private readonly Dictionary<uint, TeaScript.Bridge.TeaScriptEngine> _scriptEngines = new();
    
    void Update(double deltaTime)
    {
        if (_world == null) return;
        _lastFrameDeltaTime = Math.Max(0.0, deltaTime);
        _elapsedTime += _lastFrameDeltaTime;
        
        // Clear per-frame input state (pressed/released edges, scroll delta)
        _input?.BeginFrame();
        
        // Begin UI frame (clears previous frame's UI elements)
        BlueSky.Runtime.UI.RuntimeUI.BeginFrame();
        
        // Draw Diagnostics HUD
        if (_frameCount >= 60)
        {
            BlueSky.Runtime.UI.RuntimeUI.Panel(10, 10, 480, 280);
            BlueSky.Runtime.UI.RuntimeUI.Label("=== BLUESKY ENGINE DIAGNOSTICS ===", 20, 20);
            BlueSky.Runtime.UI.RuntimeUI.Label($"FPS: {(int)(1.0 / deltaTime)} (dt: {deltaTime * 1000:F1}ms)", 20, 45);
            BlueSky.Runtime.UI.RuntimeUI.Label($"Physics Steps / Frame: {_physicsStepsThisFrame}", 20, 70);
            BlueSky.Runtime.UI.RuntimeUI.Label($"Physics Frame: {_physicsFrame}", 20, 95);
            
            // Find active car controller
            BlueSky.Core.Gameplay.CarController? activeCar = null;
            if (_carControllerSystem != null)
            {
                foreach (var entity in _world.GetAllEntities())
                {
                    if (_world.HasComponent<BlueSky.Core.ECS.Builtin.CarControllerComponent>(entity))
                    {
                        var ctrl = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)entity.Id);
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
        UpdateTeaScripts(deltaTime);

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
                FixedUpdateTeaScripts(FixedTimeStep);
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

        // ── 4. Camera (after physics/replication so positions are current) ───
        UpdateCamera(deltaTime);

        // ── 5. Update Multiplayer / EOS ──────────────────────────────────────
        _multiplayer?.Tick(deltaTime);

        // ── 6. Update Lobby (broadcast state, retry joins) ───────────────────
        BlueSky.Networking.NetworkingTeaScriptBridge.UpdateLobby((float)deltaTime);

        // ── 7. Host: broadcast world state to all clients ────────────────────
        if (IsHost && _replication != null)
        {
            _replication.BroadcastWorldState();
        }
        
        _frameCount++;
    }
    
    void UpdateCarInput(float deltaTime)
    {
        if (_world == null || _input == null)
            return;

        var carQuery = _world.CreateQuery()
            .All<BlueSky.Core.ECS.Builtin.CarControllerComponent>()
            .Build();

        var chunks = _world.GetQueryChunks(carQuery);

        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var controller = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)entity.Id);
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
                            var carInput = new BlueSky.Core.Gameplay.CarInput
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
                    BlueSky.Core.Gameplay.CarControllerSystem.PossessedController = controller;
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

        if (_input.IsKeyDown(BlueSky.Platform.Input.KeyCode.W) || _input.IsKeyDown(BlueSky.Platform.Input.KeyCode.Up))
            throttle = 1f;
        else if (_input.IsKeyDown(BlueSky.Platform.Input.KeyCode.S) || _input.IsKeyDown(BlueSky.Platform.Input.KeyCode.Down))
            brake = 1f;
        if (_input.IsKeyDown(BlueSky.Platform.Input.KeyCode.A) || _input.IsKeyDown(BlueSky.Platform.Input.KeyCode.Left))
            steer = -1f;
        if (_input.IsKeyDown(BlueSky.Platform.Input.KeyCode.D) || _input.IsKeyDown(BlueSky.Platform.Input.KeyCode.Right))
            steer = 1f;
        if (_input.IsKeyDown(BlueSky.Platform.Input.KeyCode.Space))
            handbrake = 1f;
    }
    
    void UpdateCamera(double deltaTime)
    {
        if (_world == null)
            return;
        
        // Pull camera from the CarController's per-car ChaseCameraController
        if (_cameraFollowTarget.Id != 0)
        {
            var controller = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)_cameraFollowTarget.Id);
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
            .All<BlueSky.Core.ECS.Builtin.PhysicsComponent>()
            .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
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
            .All<BlueSky.Core.ECS.Builtin.PhysicsComponent>()
            .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(physicsQuery);
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.TransformComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                
                if (!_physicsWorld.HasBody(entity))
                    continue;

                // Skip per-wheel cars — CarController manages their transforms directly
                var carCtrl = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)entity.Id);
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
                ref var transform = ref chunk.GetComponent<BlueSky.Core.ECS.Builtin.TransformComponent>(i, transIdx);
                transform.Position = new BlueSky.Core.Math.Vector3(interpPos.X, interpPos.Y, interpPos.Z);
                transform.Rotation = new BlueSky.Core.Math.Quaternion(interpRot.X, interpRot.Y, interpRot.Z, interpRot.W);
            }
        }
    }
    
    void UpdateTeaScripts(double deltaTime)
    {
        if (_world == null) return;
        
        // Query all entities with TeaScriptComponent
        var query = _world.CreateQuery()
            .All<BlueSky.Core.ECS.Builtin.TeaScriptComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(query);
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int scriptIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.TeaScriptComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                ref var script = ref chunk.GetComponent<BlueSky.Core.ECS.Builtin.TeaScriptComponent>(i, scriptIdx);
                
                if (!script.IsEnabled || string.IsNullOrEmpty(script.ScriptAssetId))
                    continue;
                
                // Initialize script engine if needed
                if (!script.IsInitialized)
                {
                    if (!_scriptEngines.ContainsKey((uint)entity.Id))
                    {
                        try
                        {
                            var engine = new TeaScript.Bridge.TeaScriptEngine();
                            
                            // Register engine API functions that scripts can call
                            RegisterScriptAPI(engine, entity);
                            
                            // Load the script
                            if (System.IO.File.Exists(script.ScriptAssetId))
                            {
                                engine.LoadScript(script.ScriptAssetId);
                                _scriptEngines[(uint)entity.Id] = engine;
                                
                                // Call start()
                                engine.CallStart();
                                script.IsInitialized = true;
                                
                            }
                            else
                            {
                                script.IsEnabled = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[Runtime][TeaScript] start() failed for entity {entity.Id}: {ex.Message}");
                            script.IsEnabled = false;
                        }
                    }
                }
                
                // Call update() every frame
                if (script.IsInitialized && _scriptEngines.TryGetValue((uint)entity.Id, out var scriptEngine))
                {
                    try
                    {
                        scriptEngine.CallUpdate();
                        
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Runtime][TeaScript] update() failed for entity {entity.Id}: {ex.Message}");
                        script.IsEnabled = false;
                    }
                }
            }
        }
    }

    void FixedUpdateTeaScripts(double fixedDeltaTime)
    {
        if (_world == null) return;
        
        // Query all entities with TeaScriptComponent
        var query = _world.CreateQuery()
            .All<BlueSky.Core.ECS.Builtin.TeaScriptComponent>()
            .Build();
        
        var chunks = _world.GetQueryChunks(query);
        
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int scriptIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.TeaScriptComponent));
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                ref var script = ref chunk.GetComponent<BlueSky.Core.ECS.Builtin.TeaScriptComponent>(i, scriptIdx);
                
                if (!script.IsEnabled || string.IsNullOrEmpty(script.ScriptAssetId))
                    continue;
                
                // Call fixedUpdate() in sync with physics step
                if (script.IsInitialized && _scriptEngines.TryGetValue((uint)entity.Id, out var scriptEngine))
                {
                    try
                    {
                        scriptEngine.CallFunction("fixedUpdate");
                    }
                    catch (Exception ex)
                    {
                        // fixedUpdate is optional; only suppress the missing-function error.
                        if (!ex.Message.Contains("Undefined variable", StringComparison.Ordinal) &&
                            !(ex.InnerException?.Message.Contains("Undefined variable", StringComparison.Ordinal) ?? false))
                            Console.Error.WriteLine($"[Runtime][TeaScript] fixedUpdate() failed for entity {entity.Id}: {ex.Message}");
                    }
                }
            }
        }
    }
    
    void RegisterScriptAPI(TeaScript.Bridge.TeaScriptEngine engine, Entity entity)
    {
        // ═══════════════════════════════════════════════════════════════════════
        //  DEBUG / LOGGING
        // ═══════════════════════════════════════════════════════════════════════
        
        engine.RegisterFunction("print", (args) =>
        {
            Console.WriteLine(args.Count > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty);
            return null;
        });
        
        engine.RegisterFunction("log", (args) =>
        {
            string message = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            Console.WriteLine($"[TeaScript] {message}");
            return null;
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  TRANSFORM
        // ═══════════════════════════════════════════════════════════════════════
        
        // Position getters (component-wise)
        engine.RegisterFunction("getPositionX", (args) =>
        {
            if (_world != null && _world.TryGetComponent<PhysicsComponent>(entity, out var _))
            {
                return (double)BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(entity).X;
            }
            if (_world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.X;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionY", (args) =>
        {
            if (_world != null && _world.TryGetComponent<PhysicsComponent>(entity, out var _))
            {
                return (double)BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(entity).Y;
            }
            if (_world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Y;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionZ", (args) =>
        {
            if (_world != null && _world.TryGetComponent<PhysicsComponent>(entity, out var _))
            {
                return (double)BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(entity).Z;
            }
            if (_world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Z;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPosition", (args) =>
        {
            if (_world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                var list = new List<object?> { (double)transform.Position.X, (double)transform.Position.Y, (double)transform.Position.Z };
                return list;
            }
            return new List<object?> { 0.0, 0.0, 0.0 };
        });
        
        engine.RegisterFunction("setPosition", (args) =>
        {
            if (args.Count >= 3 && _world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                transform.Position = new BlueSky.Core.Math.Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                _world.AddComponent(entity, transform);
            }
            return null;
        });
        
        engine.RegisterFunction("getRotation", (args) =>
        {
            if (_world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return new List<object?> { 
                    (double)transform.Rotation.X, 
                    (double)transform.Rotation.Y, 
                    (double)transform.Rotation.Z, 
                    (double)transform.Rotation.W 
                };
            }
            return new List<object?> { 0.0, 0.0, 0.0, 1.0 };
        });
        
        engine.RegisterFunction("setRotation", (args) =>
        {
            if (args.Count >= 4 && _world != null && _world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                transform.Rotation = new BlueSky.Core.Math.Quaternion(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    Convert.ToSingle(args[3])
                );
                _world.AddComponent(entity, transform);
            }
            return null;
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  PHYSICS
        // ═══════════════════════════════════════════════════════════════════════
        
        // Velocity getters (component-wise)
        engine.RegisterFunction("getVelocityX", (args) =>
        {
            if (_world != null && _world.HasComponent<PhysicsComponent>(entity))
            {
                var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.X;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocityY", (args) =>
        {
            if (_world != null && _world.HasComponent<PhysicsComponent>(entity))
            {
                var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.Y;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocityZ", (args) =>
        {
            if (_world != null && _world.HasComponent<PhysicsComponent>(entity))
            {
                var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.Z;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocity", (args) =>
        {
            var vel = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
            return new List<object?> { (double)vel.X, (double)vel.Y, (double)vel.Z };
        });
        
        engine.RegisterFunction("setVelocity", (args) =>
        {
            if (args.Count >= 3)
            {
                var vel = new System.Numerics.Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetVelocity(entity, vel);
            }
            return null;
        });
        
        engine.RegisterFunction("addForce", (args) =>
        {
            if (args.Count >= 3)
            {
                var force = new System.Numerics.Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                BlueSky.Airborne.PhysicsTeaScriptBridge.AddForce(entity, force);
            }
            return null;
        });
        
        engine.RegisterFunction("addImpulse", (args) =>
        {
            if (args.Count >= 3)
            {
                var impulse = new System.Numerics.Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                BlueSky.Airborne.PhysicsTeaScriptBridge.AddImpulse(entity, impulse);
            }
            return null;
        });
        
        engine.RegisterFunction("raycast", (args) =>
        {
            if (args.Count >= 6)
            {
                var origin = new System.Numerics.Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                var direction = new System.Numerics.Vector3(
                    Convert.ToSingle(args[3]),
                    Convert.ToSingle(args[4]),
                    Convert.ToSingle(args[5])
                );
                float maxDistance = args.Count >= 7 ? Convert.ToSingle(args[6]) : 1000.0f;
                
                if (BlueSky.Airborne.PhysicsTeaScriptBridge.Raycast(origin, direction, maxDistance, out var hit))
                {
                    return new List<object?> {
                        true,
                        (double)hit.Point.X, (double)hit.Point.Y, (double)hit.Point.Z,
                        (double)hit.Normal.X, (double)hit.Normal.Y, (double)hit.Normal.Z,
                        (double)hit.Distance
                    };
                }
            }
            return new List<object?> { false };
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  INPUT
        // ═══════════════════════════════════════════════════════════════════════
        
        engine.RegisterFunction("getKey", (args) =>
        {
            if (args.Count > 0 && _input != null)
            {
                string keyName = args[0]?.ToString() ?? "";
                if (Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
                {
                    return _input.IsKeyDown(keyCode);
                }
            }
            return false;
        });
        
        engine.RegisterFunction("getInput", (args) =>
        {
            if (args.Count > 0 && _input != null)
            {
                string keyName = args[0]?.ToString() ?? "";
                
                // Map string to KeyCode
                if (Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
                {
                    return _input.IsKeyDown(keyCode);
                }
            }
            return false;
        });
        
        engine.RegisterFunction("getMouseButton", (args) =>
        {
            if (args.Count > 0 && _input != null)
            {
                string buttonName = args[0]?.ToString() ?? "";
                
                if (Enum.TryParse<MouseButton>(buttonName, true, out var button))
                {
                    return _input.IsMouseButtonDown(button);
                }
            }
            return false;
        });
        
        engine.RegisterFunction("getMousePosition", (args) =>
        {
            if (_input != null)
            {
                var pos = _input.MousePosition;
                return new List<object?> { (double)pos.X, (double)pos.Y };
            }
            return new List<object?> { 0.0, 0.0 };
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  TIME
        // ═══════════════════════════════════════════════════════════════════════
        
        engine.RegisterFunction("getDeltaTime", (args) =>
        {
            return _lastFrameDeltaTime;
        });
        
        engine.RegisterFunction("getTime", (args) =>
        {
            return _elapsedTime;
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  RUNTIME UI (HUD)
        // ═══════════════════════════════════════════════════════════════════════
        
        engine.RegisterFunction("uiText", (args) =>
        {
            if (args.Count >= 3)
            {
                string text = args[0]?.ToString() ?? "";
                float x = Convert.ToSingle(args[1]);
                float y = Convert.ToSingle(args[2]);
                
                // Optional: anchor (args[3]) and color (args[4-7])
                BlueSky.Runtime.UI.RuntimeUIAnchor anchor = BlueSky.Runtime.UI.RuntimeUIAnchor.TopLeft;
                if (args.Count >= 4 && args[3] != null)
                {
                    string anchorStr = args[3]?.ToString() ?? "TopLeft";
                    Enum.TryParse(anchorStr, true, out anchor);
                }
                
                System.Numerics.Vector4? color = null;
                if (args.Count >= 8)
                {
                    color = new System.Numerics.Vector4(
                        Convert.ToSingle(args[4]),
                        Convert.ToSingle(args[5]),
                        Convert.ToSingle(args[6]),
                        Convert.ToSingle(args[7])
                    );
                }
                
                BlueSky.Runtime.UI.RuntimeUI.Label(text, x, y, anchor, color);
                
            }
            return null;
        });

        engine.RegisterFunction("uiButton", (args) =>
        {
            if (args.Count >= 5)
            {
                string text = args[0]?.ToString() ?? "";
                float x = Convert.ToSingle(args[1]);
                float y = Convert.ToSingle(args[2]);
                float w = Convert.ToSingle(args[3]);
                float h = Convert.ToSingle(args[4]);
                
                BlueSky.Runtime.UI.RuntimeUIAnchor anchor = BlueSky.Runtime.UI.RuntimeUIAnchor.TopLeft;
                if (args.Count >= 6 && args[5] != null)
                {
                    string anchorStr = args[5]?.ToString() ?? "TopLeft";
                    Enum.TryParse(anchorStr, true, out anchor);
                }
                
                uint id = 0;
                if (args.Count >= 7 && args[6] != null)
                {
                    id = Convert.ToUInt32(args[6]);
                }
                
                uint buttonId = id;
                if (buttonId == 0 && !string.IsNullOrEmpty(text))
                {
                    buttonId = 5381;
                    foreach (char c in text)
                    {
                        buttonId = ((buttonId << 5) + buttonId) + c;
                    }
                }
                
                BlueSky.Runtime.UI.RuntimeUI.Button(text, x, y, w, h, anchor, buttonId);
                bool clicked = BlueSky.Runtime.UI.RuntimeUI.IsButtonClicked(buttonId);
                return clicked;
            }
            return false;
        });

        engine.RegisterFunction("loadScene", (args) =>
        {
            if (args.Count >= 1)
            {
                _pendingScenePath = args[0]?.ToString() ?? "";
                _pendingMultiplayerMode = MultiplayerMode.Offline;
                _pendingSessionName = "";
                _pendingJoinSessionId = "";

                if (args.Count >= 2 && args[1] != null)
                {
                    string modeStr = args[1]?.ToString() ?? "offline";
                    Enum.TryParse(modeStr, true, out _pendingMultiplayerMode);
                }

                if (args.Count >= 3 && args[2] != null)
                {
                    _pendingSessionName = args[2]?.ToString() ?? "";
                }

                if (args.Count >= 4 && args[3] != null)
                {
                    _pendingJoinSessionId = args[3]?.ToString() ?? "";
                }

                _pendingSceneTransition = true;
            }
            return null;
        });

        engine.RegisterFunction("setSpectatorMode", (args) =>
        {
            if (args.Count >= 1)
            {
                bool enabled = Convert.ToBoolean(args[0]);
            }
            return null;
        });

        engine.RegisterFunction("login", (args) =>
        {
            string playerName = args.Count > 0 ? args[0]?.ToString() ?? "Player" : "Player";
            _authStatus = "LoggingIn";
            
            if (_multiplayer != null)
            {
                _multiplayer.Login(playerName, (success, message) =>
                {
                    if (success)
                    {
                        string displayName = _multiplayer.Config?.LocalPlayerName ?? playerName;
                        _authStatus = $"LoggedIn ({displayName})";
                    }
                    else
                    {
                        _authStatus = "Failed: " + message;
                    }
                });
            }
            else
            {
                _authStatus = "Failed: Multiplayer not initialized";
            }
            return null;
        });

        engine.RegisterFunction("isLoggedIn", (args) =>
        {
            return _multiplayer?.IsLoggedIn ?? false;
        });

        engine.RegisterFunction("getAuthStatus", (args) =>
        {
            if (_multiplayer != null && _multiplayer.IsLoggedIn)
            {
                string displayName = "";
                if (_multiplayer is EosMultiplayerService eosService)
                {
                    displayName = eosService.EosDisplayName;
                }
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = _multiplayer.Config?.LocalPlayerName ?? "Player";
                }
                _authStatus = $"LoggedIn ({displayName})";
            }
            return _authStatus;
        });

        engine.RegisterFunction("getDisplayName", (args) =>
        {
            // Try to get the real display name from the EOS manager
            if (_multiplayer is EosMultiplayerService eosService)
            {
                string displayName = eosService.EosDisplayName;
                if (!string.IsNullOrEmpty(displayName))
                {
                    return displayName;
                }
            }
            // Fallback to config player name
            return _multiplayer?.Config?.LocalPlayerName ?? "";
        });

        // ── Session Management (Host / Search / Join) ────────────────────────
        // These delegate to NetworkingTeaScriptBridge.Session for local registry.
        engine.RegisterFunction("host", (args) =>
        {
            if (args.Count < 3)
            {
                return false;
            }
            string sessionNameArg = args[0]?.ToString() ?? "Session";
            string scenePathArg = args[1]?.ToString() ?? "";
            int maxPlayers = Convert.ToInt32(args[2]);
            _pendingSessionName = sessionNameArg;
            _pendingScenePath = scenePathArg;
            BlueSky.Networking.NetworkingTeaScriptBridge.Host(sessionNameArg, scenePathArg, maxPlayers, (result, msg) =>
            {
            });
            return true;
        });

        engine.RegisterFunction("search", (args) =>
        {
            // Synchronous search: directly query the SessionManager's local registry
            var session = BlueSky.Networking.NetworkingTeaScriptBridge.Session;
            if (session == null)
            {
                return true;
            }
            // If we're hosting, register our own session so we can see it
            session.Search();
            return true;
        });

        engine.RegisterFunction("join", (args) =>
        {
            if (args.Count < 1)
            {
                return false;
            }
            int index = Convert.ToInt32(args[0]);
            bool success = BlueSky.Networking.NetworkingTeaScriptBridge.Join(index);
            return success;
        });

        engine.RegisterFunction("getSessionCount", (args) =>
        {
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetSessionCount();
        });

        engine.RegisterFunction("getSessionName", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetSessionName(index);
        });

        engine.RegisterFunction("getSessionHost", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetSessionHost(index);
        });

        engine.RegisterFunction("getSessionPlayerCount", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetSessionPlayerCount(index);
        });

        engine.RegisterFunction("getSessionMaxPlayers", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetSessionMaxPlayers(index);
        });

        engine.RegisterFunction("getSessionId", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return BlueSky.Networking.NetworkingTeaScriptBridge.Session?.GetSessionId(index) ?? "";
        });

        // ── Lobby Management ──────────────────────────────────────────────────
        engine.RegisterFunction("hostLobby", (args) =>
        {
            if (args.Count < 3)
            {
                return false;
            }
            string lobbyNameArg = args[0]?.ToString() ?? "Lobby";
            string scenePathArg = args[1]?.ToString() ?? "";
            int maxPlayers = Convert.ToInt32(args[2]);
            string hostNameArg = "Host";
            if (_multiplayer is EosMultiplayerService eosService && !string.IsNullOrEmpty(eosService.EosDisplayName))
            {
                hostNameArg = eosService.EosDisplayName;
            }
            else if (_multiplayer?.Config?.LocalPlayerName != null)
            {
                hostNameArg = _multiplayer.Config.LocalPlayerName;
            }

            // Refresh the session's HostUserId with the current P2P ID
            // (may have changed since Host() was called if login completed after hosting)
            var session = BlueSky.Networking.NetworkingTeaScriptBridge.Session;
            string hostP2PId = _multiplayer?.LocalP2PId ?? BlueSky.Networking.NetworkingTeaScriptBridge.Session?.HostedSession?.HostUserId ?? "";

            _pendingSessionName = lobbyNameArg;
            _pendingScenePath = scenePathArg;
            _pendingMultiplayerMode = BlueSky.Networking.MultiplayerMode.Host;
            bool ok = BlueSky.Networking.NetworkingTeaScriptBridge.HostLobby(lobbyNameArg, scenePathArg, maxPlayers, hostNameArg, hostP2PId);
            
            // Store the local player's lobby key so we can match replicated entities later
            _localPlayerKey = !string.IsNullOrEmpty(hostP2PId) ? hostP2PId : hostNameArg;
            
            return ok;
        });

        engine.RegisterFunction("joinLobby", (args) =>
        {
            if (args.Count < 3)
            {
                return false;
            }
            string lobbyNameArg = args[0]?.ToString() ?? "";
            string scenePathArg = args[1]?.ToString() ?? "";
            string playerNameArg = args[2]?.ToString() ?? "Player";

            // Ensure we search for sessions first so JoinLobby can find the host
            BlueSky.Networking.NetworkingTeaScriptBridge.Session?.Search();

            _pendingScenePath = scenePathArg;
            _pendingMultiplayerMode = BlueSky.Networking.MultiplayerMode.Join;
            _pendingJoinSessionId = "";
            bool ok = BlueSky.Networking.NetworkingTeaScriptBridge.JoinLobby(lobbyNameArg, scenePathArg, playerNameArg, playerNameArg);
            
            // Set local player key to OUR P2P ID — this is what the host will use
            // as the key in GetStablePlayerKey(player) when spawning our entity.
            _localPlayerKey = _multiplayer?.LocalP2PId ?? playerNameArg;
            
            return ok;
        });

        engine.RegisterFunction("leaveLobby", (args) =>
        {
            BlueSky.Networking.NetworkingTeaScriptBridge.LeaveLobby();
            return null;
        });

        engine.RegisterFunction("startCountdown", (args) =>
        {
            float seconds = args.Count > 0 ? Convert.ToSingle(args[0]) : 10f;
            BlueSky.Networking.NetworkingTeaScriptBridge.StartCountdown(seconds);
            return null;
        });

        engine.RegisterFunction("getLobbyPlayerCount", (args) =>
        {
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetLobbyPlayerCount();
        });

        engine.RegisterFunction("getLobbyPlayerName", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return BlueSky.Networking.NetworkingTeaScriptBridge.GetLobbyPlayerName(index);
        });

        engine.RegisterFunction("getLobbyCountdown", (args) =>
        {
            return (double)BlueSky.Networking.NetworkingTeaScriptBridge.GetLobbyCountdown();
        });

        engine.RegisterFunction("getLobbyCountdownProgress", (args) =>
        {
            return (double)BlueSky.Networking.NetworkingTeaScriptBridge.GetLobbyCountdownProgress();
        });

        engine.RegisterFunction("isInLobby", (args) =>
        {
            return BlueSky.Networking.NetworkingTeaScriptBridge.IsInLobby();
        });

        engine.RegisterFunction("isLobbyHost", (args) =>
        {
            return BlueSky.Networking.NetworkingTeaScriptBridge.IsLobbyHost();
        });

        engine.RegisterFunction("uiPanel", (args) =>
        {
            if (args.Count >= 4)
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float width = Convert.ToSingle(args[2]);
                float height = Convert.ToSingle(args[3]);
                
                // Optional: anchor (args[4]) and color (args[5-8])
                BlueSky.Runtime.UI.RuntimeUIAnchor anchor = BlueSky.Runtime.UI.RuntimeUIAnchor.TopLeft;
                if (args.Count >= 5 && args[4] != null)
                {
                    string anchorStr = args[4]?.ToString() ?? "TopLeft";
                    Enum.TryParse(anchorStr, true, out anchor);
                }
                
                System.Numerics.Vector4? color = null;
                if (args.Count >= 9)
                {
                    color = new System.Numerics.Vector4(
                        Convert.ToSingle(args[5]),
                        Convert.ToSingle(args[6]),
                        Convert.ToSingle(args[7]),
                        Convert.ToSingle(args[8])
                    );
                }
                
                BlueSky.Runtime.UI.RuntimeUI.Panel(x, y, width, height, anchor, color);
            }
            return null;
        });
        
        engine.RegisterFunction("uiProgressBar", (args) =>
        {
            if (args.Count >= 5)
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float width = Convert.ToSingle(args[2]);
                float height = Convert.ToSingle(args[3]);
                float value = Convert.ToSingle(args[4]);
                
                // Optional: anchor (args[5])
                BlueSky.Runtime.UI.RuntimeUIAnchor anchor = BlueSky.Runtime.UI.RuntimeUIAnchor.TopLeft;
                if (args.Count >= 6 && args[5] != null)
                {
                    string anchorStr = args[5]?.ToString() ?? "TopLeft";
                    Enum.TryParse(anchorStr, true, out anchor);
                }
                
                BlueSky.Runtime.UI.RuntimeUI.ProgressBar(x, y, width, height, value, anchor);
            }
            return null;
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  MATH FUNCTIONS
        // ═══════════════════════════════════════════════════════════════════════
        
        engine.RegisterFunction("sin", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Sin(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("cos", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Cos(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("sqrt", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Sqrt(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("abs", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Abs(value);
            }
            return 0.0;
        });

        engine.RegisterFunction("ceil", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Ceiling(value);
            }
            return 0.0;
        });

        engine.RegisterFunction("floor", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Floor(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("min", (args) =>
        {
            if (args.Count >= 2)
            {
                double a = Convert.ToDouble(args[0]);
                double b = Convert.ToDouble(args[1]);
                return System.Math.Min(a, b);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("max", (args) =>
        {
            if (args.Count >= 2)
            {
                double a = Convert.ToDouble(args[0]);
                double b = Convert.ToDouble(args[1]);
                return System.Math.Max(a, b);
            }
            return 0.0;
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  RIGIDBODY PROPERTY SETTERS
        // ═══════════════════════════════════════════════════════════════════════
        
        engine.RegisterFunction("getMass", (args) =>
        {
            if (_world != null && _world.TryGetComponent<PhysicsComponent>(entity, out var phys))
            {
                return (double)phys.Mass;
            }
            return 1.0;
        });
        
        engine.RegisterFunction("setMass", (args) =>
        {
            if (args.Count >= 1 && _world != null && _world.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref _world.GetComponent<PhysicsComponent>(entity);
                phys.Mass = Convert.ToSingle(args[0]);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetMass(entity, phys.Mass);
            }
            return null;
        });
        
        engine.RegisterFunction("setGravity", (args) =>
        {
            if (args.Count >= 1 && _world != null && _world.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref _world.GetComponent<PhysicsComponent>(entity);
                phys.UseGravity = Convert.ToBoolean(args[0]);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetUseGravity(entity, phys.UseGravity);
            }
            return null;
        });
        
        engine.RegisterFunction("setKinematic", (args) =>
        {
            if (args.Count >= 1 && _world != null && _world.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref _world.GetComponent<PhysicsComponent>(entity);
                phys.IsKinematic = Convert.ToBoolean(args[0]);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetKinematic(entity, phys.IsKinematic);
            }
            return null;
        });
        
        engine.RegisterFunction("rotate", (args) =>
        {
            if (args.Count >= 3 && _world != null && _world.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref _world.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                
                // Simple euler angle rotation (degrees)
                transform.Rotation = BlueSky.Core.Math.Quaternion.Euler(x, y, z);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetRotation(entity, new System.Numerics.Quaternion(
                    transform.Rotation.X,
                    transform.Rotation.Y,
                    transform.Rotation.Z,
                    transform.Rotation.W));
            }
            return null;
        });
        
        // ═══════════════════════════════════════════════════════════════════════
        //  VEHICLE PHYSICS API (Car Controller)
        // ═══════════════════════════════════════════════════════════════════════
        
        Func<int, BlueSky.Core.Gameplay.CarController?> getController = (entityId) =>
            BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)entityId);

        Func<int, int, BlueSky.Core.Gameplay.WheelState?> getWheel = (entityId, wheelIndex) =>
        {
            var ctrl = getController(entityId);
            return ctrl?.GetWheelState(wheelIndex);
        };

        engine.RegisterFunction("getWheelGrounded", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return w?.IsGrounded ?? false;
            }
            return false;
        });

        engine.RegisterFunction("getWheelSuspension", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.SuspensionCompression ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelSlip", (args) =>
        {
            if (args.Count >= 2)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                bool isLongitudinal = Convert.ToBoolean(args[1]);
                var w = getWheel(entity.Id, wheelIndex);
                if (w == null) return 0.0;
                return isLongitudinal ? (double)w.SlipRatio : (double)w.SlipAngle;
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelSteerAngle", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.SteerAngle ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelAngularVelocity", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.AngularVelocity ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelContactNormalX", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.ContactNormal.X ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelContactNormalY", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.ContactNormal.Y ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelContactNormalZ", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.ContactNormal.Z ?? 0.0);
            }
            return 0.0;
        });

        // ═══════════════════════════════════════════════════════════════════════
        //  BONE MAPPING API (for skeletal mesh vehicle configuration)
        // ═══════════════════════════════════════════════════════════════════════

        engine.RegisterFunction("setWheelBone", (args) =>
        {
            if (args.Count >= 2)
            {
                int slot = Convert.ToInt32(args[0]);
                string boneName = args[1]?.ToString() ?? "";
                uint eid = (uint)entity.Id;
                BlueSky.Core.Gameplay.CarController.SetBoneOverride(eid, slot, boneName);
            }
            return null;
        });

        engine.RegisterFunction("setBodyBone", (args) =>
        {
            if (args.Count >= 1)
            {
                string boneName = args[0]?.ToString() ?? "";
                uint eid = (uint)entity.Id;
                BlueSky.Core.Gameplay.CarController.SetBodyBoneOverride(eid, boneName);
            }
            return null;
        });

        engine.RegisterFunction("refreshBones", (args) =>
        {
            var ctrl = getController(entity.Id);
            if (ctrl != null)
            {
                ctrl.RefreshBoneMapping();
            }
            return null;
        });

        engine.RegisterFunction("debugWheelAnimation", (args) =>
        {
            var ctrl = getController(entity.Id);
            if (ctrl == null)
            {
                return null;
            }

            string report = $"SkeletalMesh: {ctrl.SkeletalMesh != null}; " +
                $"Bones: {ctrl.SkeletalMesh?.Bones?.Length ?? 0}";
            Console.WriteLine($"[TeaScript:{entity.Id}] {report}");
            return report;
        });

        engine.RegisterFunction("setWheelPosition", (args) =>
        {
            if (args.Count >= 4)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    int slot = Convert.ToInt32(args[0]);
                    float x = Convert.ToSingle(args[1]);
                    float y = Convert.ToSingle(args[2]);
                    float z = Convert.ToSingle(args[3]);
                    ctrl.SetWheelLocalPosition(slot, x, y, z);
                }
            }
            return null;
        });

        engine.RegisterFunction("setDriveWheels", (args) =>
        {
            if (args.Count >= 4)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    ctrl.SetDriveWheels(
                        Convert.ToBoolean(args[0]),
                        Convert.ToBoolean(args[1]),
                        Convert.ToBoolean(args[2]),
                        Convert.ToBoolean(args[3]));
                }
            }
            return null;
        });

        engine.RegisterFunction("setSteerWheels", (args) =>
        {
            if (args.Count >= 4)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    ctrl.SetSteerWheels(
                        Convert.ToBoolean(args[0]),
                        Convert.ToBoolean(args[1]),
                        Convert.ToBoolean(args[2]),
                        Convert.ToBoolean(args[3]));
                }
            }
            return null;
        });
        
    }
    
    private ulong _frameCount = 0;
    private double _lastFrameDeltaTime = 0.016;
    private double _elapsedTime;
    
    void Render()
    {
        if (_swapchain == null || _rhi == null || _depthTexture == null)
            return;
        
        // Acquire next image from swapchain
        _swapchain.AcquireNextImage();
        
        // Get command buffer
        var cmd = _rhi.CreateCommandBuffer();
        
        // Set up camera matrices
        var view = System.Numerics.Matrix4x4.CreateLookAt(_cameraPos, _cameraTarget, System.Numerics.Vector3.UnitY);
        var proj = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
            60.0f * MathF.PI / 180.0f,  // FOV
            (float)_config.WindowWidth / _config.WindowHeight,  // Aspect
            0.1f,  // Near
            1000.0f  // Far
        );
        
        
        
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
                viewportW: _config.WindowWidth,
                viewportH: _config.WindowHeight,
                deltaTime: 0.016f
            );
        }
        // Render UI overlay (HUD from TeaScript) BEFORE ending render pass
        if (_ui != null && _uiRenderer != null && _input != null)
        {
            var mousePos = _input.MousePosition;
            bool mouseDown = _input.IsMouseButtonDown(Platform.Input.MouseButton.Left);
            
            
            _ui.BeginFrame(mousePos, mouseDown, "", false, _input.ScrollDelta.Y);
            BlueSky.Runtime.UI.RuntimeUI.Render(_ui, _config.WindowWidth, _config.WindowHeight, (float)_lastFrameDeltaTime);
            _uiRenderer.Render(cmd, _ui);
        }
        
        cmd.EndRenderPass();
        
        // Submit and present (CRITICAL: must pass swapchain for presentation!)
        _rhi.Submit(cmd, _swapchain);
        _swapchain.Present();
    }
    
    void Shutdown()
    {
        
        // Cleanup in reverse order
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
                string path = BlueSky.Core.ECS.Builtin.NetworkManagerStorage.GetPrefabPath(nm.StorageIndex);
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
        var travelPlayers = BlueSky.Networking.NetworkingTeaScriptBridge.Lobby?.Players.ToList() ?? new System.Collections.Generic.List<LobbyPlayer>();

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

        _scriptEngines.Clear();

        // Dispose old world + recreate
        _world?.Dispose();
        _world = new World();
        _replication?.SetWorld(_world);
        _terrainSystem = new TerrainSystem(_world);

        // Dispose old renderer (GPU resources) before creating new one
        _viewportRenderer?.Dispose();
        _viewportRenderer = null;
        if (_swapchain != null && _rhi != null)
            _viewportRenderer = new ViewportRenderer(_rhi, _world, terrainSystem: _terrainSystem, _swapchain.Format, showEditorGizmos: false);

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
            BlueSky.Networking.NetworkingTeaScriptBridge.AddLobbyPlayer(player.UserId, player.DisplayName);

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
                var currentPlayers = BlueSky.Networking.NetworkingTeaScriptBridge.Lobby?.Players;
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
