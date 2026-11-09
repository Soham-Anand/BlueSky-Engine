using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.Core.Assets;
using BlueSky.Runtime.UI;
using BlueSky.Core.Gameplay;
using BlueSky.Airborne;
using BlueSky.LHO;
using TeaScript.Frontend;
using TeaScript.Runtime;
using TeaScriptExecutionLimitException = TeaScript.Runtime.TeaScriptExecutionLimitException;

namespace BlueSky.Core.Scripting;

/// <summary>
/// ECS System that manages TeaScript execution for all entities with TeaScriptComponent.
/// </summary>
public class TeaScriptSystem : SystemBase
{
    private sealed class ScriptInstance
    {
        private readonly Interpreter _interpreter = new();
        private bool _isInitialized;

        public void RegisterFunction(string name, Func<List<object?>, object?> implementation) =>
            _interpreter.RegisterNativeFunction(name, implementation);

        public void LoadScript(string filePath)
        {
            string source = System.IO.File.ReadAllText(filePath);
            var tokens = new Lexer(source).ScanTokens();
            var program = new Parser(tokens).Parse();
            _interpreter.Execute(program);
            _isInitialized = false;
        }

        public void CallStart()
        {
            if (_isInitialized) return;
            if (_interpreter.HasFunction("start"))
            {
                _interpreter.CallFunction("start");
                _isInitialized = true;
            }
        }

        public void CallUpdate()
        {
            if (_interpreter.HasFunction("update"))
                _interpreter.CallFunction("update");
        }

        public object? CallFunction(string name, params object?[] args) =>
            _interpreter.HasFunction(name) ? _interpreter.CallFunction(name, args) : null;
    }

    private readonly Dictionary<uint, ScriptInstance> _runtimeInstances = new();
    private float _deltaTime = 0.016f;
    private double _elapsedTime;
    private Func<string, bool>? _keyProvider;
    private Func<int, bool>? _mouseButtonProvider;
    private Func<Vector2>? _mousePositionProvider;
    private readonly HashSet<Entity> _pendingDestroyEntities = new();

    private static TeaScriptSystem? _instance;
    public static TeaScriptSystem? Instance => _instance;

    /// <summary>
    /// Particle system reachable from scripts. Set once by the editor at
    /// startup (ProjectBrowserUI); bindings no-op when null (headless/tests).
    /// </summary>
    public static BlueSky.BSR.Vectra.VectraSystem? VectraParticles { get; set; }

    // Script-owned emitters: id → emitter. ids start at 1 (0 = invalid).
    private readonly Dictionary<int, BlueSky.BSR.Vectra.VectraEmitter> _scriptEmitters = new();
    private int _nextEmitterId = 1;

    public TeaScriptSystem(World world)
    {
        _instance = this;
        Initialize(world);
    }

    public static void CallFunctionOnAllScripts(string functionName, params object?[] args)
    {
        if (_instance == null) return;
        
        foreach (var engine in _instance._runtimeInstances.Values)
        {
            try
            {
                engine.CallFunction(functionName, args);
            }
            catch (Exception)
            {
                // Ignore scripts that do not have this function defined
            }
        }
    }

    public void SetInputProviders(
        Func<string, bool>? keyProvider,
        Func<int, bool>? mouseButtonProvider = null,
        Func<Vector2>? mousePositionProvider = null)
    {
        _keyProvider = keyProvider;
        _mouseButtonProvider = mouseButtonProvider;
        _mousePositionProvider = mousePositionProvider;
    }
    
    /// <summary>
    /// Update all TeaScript components.
    /// </summary>
    public override void Update(float deltaTime)
    {
        _deltaTime = deltaTime;
        _elapsedTime += System.Math.Max(0.0f, deltaTime);
        
        if (World == null) return;
        
        // Query for entities with TeaScriptComponent
        var query = World.CreateQuery()
            .All<TeaScriptComponent>()
            .Build();
        
        var chunks = World.GetQueryChunks(query);
        
        int scriptCount = 0;
        foreach (var chunk in chunks)
        {
            int scriptIndex = chunk.GetComponentIndex(typeof(TeaScriptComponent));
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                ref var script = ref chunk.GetComponent<TeaScriptComponent>(i, scriptIndex);
                
                if (!script.IsEnabled) continue;
                
                scriptCount++;
                
                // Initialize script if needed
                if (!script.IsInitialized && !string.IsNullOrEmpty(script.ScriptAssetId))
                {
                    InitializeScript(ref script, entity);
                }
                
                // Call update()
                if (script.IsInitialized && script.RuntimeInstance != 0)
                {
                    if (_runtimeInstances.TryGetValue(script.RuntimeInstance, out var engine))
                    {
                        try
                        {
                            engine.CallUpdate();
                        }
                        catch (TeaScriptExecutionLimitException ex)
                        {
                            script.IsEnabled = false;
                            Console.WriteLine($"[TeaScript] Disabled runaway script on entity {entity.Id}: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TeaScript] Error in update(): {ex.Message}");
                        }
                    }
                }
            }
        }

        FlushPendingEntityDestruction();
        
        // Update lobby state (broadcast, retry joins, countdown)
        LHO.NetworkingTeaScriptBridge.UpdateLobby(_deltaTime);

        // Debug: Log script count on first frame
        if (scriptCount > 0 && _deltaTime < 0.1f)
        {
            Console.WriteLine($"[TeaScript] Updating {scriptCount} script(s)");
        }
    }

    /// <summary>
    /// Fixed update all TeaScript components in sync with physics steps.
    /// </summary>
    public void FixedUpdate(float fixedDeltaTime)
    {
        float previousDeltaTime = _deltaTime;
        _deltaTime = fixedDeltaTime;
        
        if (World == null) return;
        
        // Query for entities with TeaScriptComponent
        var query = World.CreateQuery()
            .All<TeaScriptComponent>()
            .Build();
        
        var chunks = World.GetQueryChunks(query);
        
        foreach (var chunk in chunks)
        {
            int scriptIndex = chunk.GetComponentIndex(typeof(TeaScriptComponent));
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                ref var script = ref chunk.GetComponent<TeaScriptComponent>(i, scriptIndex);
                
                if (!script.IsEnabled) continue;
                
                // Initialize script if needed
                if (!script.IsInitialized && !string.IsNullOrEmpty(script.ScriptAssetId))
                {
                    InitializeScript(ref script, entity);
                }
                
                // Call fixedUpdate()
                if (script.IsInitialized && script.RuntimeInstance != 0)
                {
                    if (_runtimeInstances.TryGetValue(script.RuntimeInstance, out var engine))
                    {
                        try
                        {
                            engine.CallFunction("fixedUpdate");
                        }
                        catch (TeaScriptExecutionLimitException ex)
                        {
                            script.IsEnabled = false;
                            Console.WriteLine($"[TeaScript] Disabled runaway script on entity {entity.Id}: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            // fixedUpdate() is optional. Only report actual errors inside the function.
                            if (ex.Message.Contains("Undefined variable") || 
                                (ex.InnerException != null && ex.InnerException.Message.Contains("Undefined variable")))
                            {
                                // Ignore
                            }
                            else
                            {
                                Console.WriteLine($"[TeaScript] Error in fixedUpdate(): {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        FlushPendingEntityDestruction();

        _deltaTime = previousDeltaTime;
    }
    
    /// <summary>
    /// Initialize a script instance.
    /// </summary>
    private void InitializeScript(ref TeaScriptComponent script, Entity entity)
    {
        try
        {
            var scriptPath = ResolveScriptPath(script.ScriptAssetId);
            if (string.IsNullOrEmpty(scriptPath))
            {
                Console.WriteLine($"[TeaScript] No script file specified for entity {entity.Id}");
                script.IsEnabled = false;
                return;
            }

            if (!System.IO.File.Exists(scriptPath))
            {
                Console.WriteLine($"[TeaScript] Script not found for entity {entity.Id}: {script.ScriptAssetId}");
                script.IsEnabled = false;
                return;
            }

            var engine = new ScriptInstance();
            
            // Register basic engine functions
            RegisterEngineFunctions(engine, entity);
            
            // Load the actual script file
            Console.WriteLine($"[TeaScript] Loading script: {scriptPath}");
            engine.LoadScript(scriptPath);
            
            // Store instance
            uint instanceId = (uint)_runtimeInstances.Count + 1;
            _runtimeInstances[instanceId] = engine;
            script.RuntimeInstance = instanceId;
            
            // Call start()
            engine.CallStart();
            
            script.IsInitialized = true;
            Console.WriteLine($"[TeaScript] Initialized script for entity {entity.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TeaScript] Failed to initialize script: {ex.Message}");
            Console.WriteLine($"[TeaScript] Stack trace: {ex.StackTrace}");
            script.IsEnabled = false;
        }
    }
    
    /// <summary>
    /// Register all engine functions that scripts can call.
    /// </summary>
    private void RegisterEngineFunctions(ScriptInstance engine, Entity entity)
    {
        if (World == null) return;
        
        // Logging
        engine.RegisterFunction("print", (args) =>
        {
            Console.WriteLine(args.Count > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty);
            return null;
        });

        engine.RegisterFunction("log", (args) =>
        {
            string message = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            Console.WriteLine($"[TeaScript:{entity.Id}] {message}");
            return null;
        });

        engine.RegisterFunction("getTime", (args) => _elapsedTime);

        // Echo audio — fire-and-forget 3D/2D sounds and music from TeaScript.
        engine.RegisterFunction("playSound", (args) =>
        {
            if (args.Count < 1) return false;
            string path = ResolveScriptPath(args[0]?.ToString() ?? "");
            float volume = args.Count > 1 ? Convert.ToSingle(args[1]) : 1.0f;
            bool loop = args.Count > 2 && Convert.ToBoolean(args[2]);
            bool is3D = args.Count <= 3 || Convert.ToBoolean(args[3]);
            float x = args.Count > 4 ? Convert.ToSingle(args[4]) : 0f;
            float y = args.Count > 5 ? Convert.ToSingle(args[5]) : 0f;
            float z = args.Count > 6 ? Convert.ToSingle(args[6]) : 0f;
            return BlueSky.Echo.EchoTeaScriptBridge.PlaySound(path, volume, loop, is3D, x, y, z);
        });

        engine.RegisterFunction("playMusic", (args) =>
        {
            if (args.Count < 1) return false;
            string path = ResolveScriptPath(args[0]?.ToString() ?? "");
            float volume = args.Count > 1 ? Convert.ToSingle(args[1]) : 1.0f;
            bool loop = args.Count <= 2 || Convert.ToBoolean(args[2]);
            return BlueSky.Echo.EchoTeaScriptBridge.PlayMusic(path, volume, loop);
        });

        engine.RegisterFunction("stopAllSounds", (args) =>
        {
            BlueSky.Echo.EchoTeaScriptBridge.StopAllSounds();
            return null;
        });

        engine.RegisterFunction("setAudioVolume", (args) =>
        {
            if (args.Count >= 2)
                BlueSky.Echo.EchoTeaScriptBridge.SetChannelVolume(
                    args[0]?.ToString() ?? "Master",
                    Convert.ToSingle(args[1]));
            return null;
        });

        // Runtime UI - frame-local HUD drawing. These are rendered by the
        // runtime UI overlay during play mode, above the viewport.
        engine.RegisterFunction("uiText", (args) =>
        {
            if (args.Count >= 3)
            {
                RuntimeUI.Label(
                    args[0]?.ToString() ?? "",
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    ParseRuntimeUIAnchor(args, 3),
                    ParseRuntimeUIColor(args, 4, RuntimeUI.TextPrimary));
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
                
                RuntimeUIAnchor anchor = ParseRuntimeUIAnchor(args, 5);
                
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
                
                RuntimeUI.Button(text, x, y, w, h, anchor, buttonId);
                return RuntimeUI.IsButtonClicked(buttonId);
            }
            return false;
        });

        engine.RegisterFunction("loadScene", (args) =>
        {
            if (args.Count >= 1)
            {
                string scenePath  = args[0]?.ToString() ?? "";
                string mode       = args.Count >= 2 ? args[1]?.ToString() ?? "offline" : "offline";
                string session    = args.Count >= 3 ? args[2]?.ToString() ?? ""       : "";
                string joinId     = args.Count >= 4 ? args[3]?.ToString() ?? ""       : "";
                NetworkingTeaScriptBridge.LoadScene(scenePath, mode, session, joinId);
            }
            return null;
        });

        engine.RegisterFunction("setSpectatorMode", (args) =>
        {
            if (args.Count >= 1)
            {
                bool enabled = Convert.ToBoolean(args[0]);
                PlayerController.Instance.SetFreeCameraMode(enabled);
            }
            return null;
        });

        engine.RegisterFunction("login", (args) =>
        {
            string playerName = args.Count > 0 ? args[0]?.ToString() ?? "Player" : "Player";
            Console.WriteLine($"[TeaScript] login() called with playerName='{playerName}'");

            // Lazy-initialize EOS service if not registered yet (editor play mode)
            if (NetworkingTeaScriptBridge.ActiveService == null)
            {
                string projectDir = BlueSky.Editor.ProjectManager.CurrentProjectDir ?? "";
                var eos = EosCredentials.LoadForProject(projectDir);
                if (eos.IsConfigured)
                {
                    Console.WriteLine("[TeaScript] Lazy-initializing EOS multiplayer service");
                    var config = new MultiplayerConfig
                    {
                        Mode = BlueSky.LHO.MultiplayerMode.Host,
                        LocalPlayerName = playerName,
                        Eos = eos,
                    };
                    var service = new EosMultiplayerService(config);
                    service.Initialize(msg => Console.WriteLine($"[TeaScript] {msg}"));
                    NetworkingTeaScriptBridge.Register(service, null);
                    Console.WriteLine("[TeaScript] EOS service registered, calling Login...");
                }
                else
                {
                    Console.WriteLine("[TeaScript] EOS credentials not configured — login will fail");
                    Console.WriteLine($"[TeaScript] Project dir: {projectDir}");
                }
            }

            Console.WriteLine("[TeaScript] Calling NetworkingTeaScriptBridge.Login...");
            NetworkingTeaScriptBridge.Login(playerName, (success, msg) =>
            {
                Console.WriteLine(success
                    ? $"[TeaScript] login succeeded: {msg}"
                    : $"[TeaScript] login failed: {msg}");
            });
            return null;
        });

        engine.RegisterFunction("isLoggedIn", (args) =>
        {
            NetworkingTeaScriptBridge.RefreshAuthStatus();
            return NetworkingTeaScriptBridge.ActiveService?.IsLoggedIn ?? false;
        });

        engine.RegisterFunction("getAuthStatus", (args) =>
        {
            NetworkingTeaScriptBridge.RefreshAuthStatus();
            return NetworkingTeaScriptBridge.AuthStatus;
        });

        engine.RegisterFunction("getDisplayName", (args) =>
        {
            NetworkingTeaScriptBridge.RefreshAuthStatus();
            return NetworkingTeaScriptBridge.DisplayName;
        });

        // ── Session Management ────────────────────────────────────────────────
        // host(sessionName, scenePath, maxPlayers) — creates a session
        engine.RegisterFunction("host", (args) =>
        {
            if (args.Count < 3)
            {
                Console.WriteLine("[TeaScript] host() requires 3 args: host(sessionName, scenePath, maxPlayers)");
                return false;
            }
            string sessionName = Convert.ToString(args[0]) ?? "Session";
            string scenePath = Convert.ToString(args[1]) ?? "";
            int maxPlayers = Convert.ToInt32(args[2]);
            NetworkingTeaScriptBridge.Host(sessionName, scenePath, maxPlayers, (result, msg) =>
            {
                Console.WriteLine(result == "ok"
                    ? $"[TeaScript] Hosted session: {msg}"
                    : $"[TeaScript] Host failed: {msg}");
            });
            return true;
        });

        // search() — discover available sessions
        engine.RegisterFunction("search", (args) =>
        {
            NetworkingTeaScriptBridge.Search();
            Console.WriteLine("[TeaScript] Searching for sessions...");
            return true;
        });

        // join(sessionIndex) — join a discovered session by index
        engine.RegisterFunction("join", (args) =>
        {
            if (args.Count < 1)
            {
                Console.WriteLine("[TeaScript] join() requires 1 arg: join(sessionIndex)");
                return false;
            }
            int index = Convert.ToInt32(args[0]);
            bool success = NetworkingTeaScriptBridge.Join(index);
            if (success)
                Console.WriteLine($"[TeaScript] Joined session at index {index}");
            else
                Console.WriteLine($"[TeaScript] Failed to join session at index {index}");
            return success;
        });

        // getSessionCount() — number of discovered sessions
        engine.RegisterFunction("getSessionCount", (args) =>
        {
            return NetworkingTeaScriptBridge.GetSessionCount();
        });

        // getSessionName(index) — session name at index
        engine.RegisterFunction("getSessionName", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return NetworkingTeaScriptBridge.GetSessionName(index);
        });

        // getSessionHost(index) — host name at index
        engine.RegisterFunction("getSessionHost", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return NetworkingTeaScriptBridge.GetSessionHost(index);
        });

        // getSessionPlayerCount(index) — player count at index
        engine.RegisterFunction("getSessionPlayerCount", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return NetworkingTeaScriptBridge.GetSessionPlayerCount(index);
        });

        // getSessionMaxPlayers(index) — max players at index
        engine.RegisterFunction("getSessionMaxPlayers", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return NetworkingTeaScriptBridge.GetSessionMaxPlayers(index);
        });

        // getSessionId(index) — session ID string at index
        engine.RegisterFunction("getSessionId", (args) =>
        {
            if (args.Count < 1) return "";
            int index = Convert.ToInt32(args[0]);
            return NetworkingTeaScriptBridge.Session?.GetSessionId(index) ?? "";
        });

        // ── Runtime UI ────────────────────────────────────────────────────────
        engine.RegisterFunction("uiPanel", (args) =>
        {
            if (args.Count >= 4)
            {
                RuntimeUI.Panel(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    Convert.ToSingle(args[3]),
                    ParseRuntimeUIAnchor(args, 4),
                    ParseRuntimeUIColor(args, 5, RuntimeUI.PanelColor));
            }
            return null;
        });

        engine.RegisterFunction("uiProgressBar", (args) =>
        {
            if (args.Count >= 5)
            {
                RuntimeUI.ProgressBar(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    Convert.ToSingle(args[3]),
                    Convert.ToSingle(args[4]),
                    ParseRuntimeUIAnchor(args, 5));
            }
            return null;
        });

        // ── Lobby Management ──────────────────────────────────────────────────
        engine.RegisterFunction("hostLobby", (args) =>
        {
            if (args.Count < 3)
            {
                Console.WriteLine("[TeaScript] hostLobby() requires: hostLobby(lobbyName, scenePath, maxPlayers)");
                return false;
            }
            string lobbyName = args[0]?.ToString() ?? "Lobby";
            string scenePath = args[1]?.ToString() ?? "";
            int maxPlayers = Convert.ToInt32(args[2]);
            string hostName = NetworkingTeaScriptBridge.DisplayName;
            string hostUserId = hostName;
            return NetworkingTeaScriptBridge.HostLobby(lobbyName, scenePath, maxPlayers, hostName, hostUserId);
        });

        engine.RegisterFunction("joinLobby", (args) =>
        {
            if (args.Count < 3)
            {
                Console.WriteLine("[TeaScript] joinLobby() requires: joinLobby(lobbyName, scenePath, playerName)");
                return false;
            }
            string lobbyName = args[0]?.ToString() ?? "";
            string scenePath = args[1]?.ToString() ?? "";
            string playerName = args[2]?.ToString() ?? "Player";
            return NetworkingTeaScriptBridge.JoinLobby(lobbyName, scenePath, playerName, playerName);
        });

        engine.RegisterFunction("leaveLobby", (args) =>
        {
            NetworkingTeaScriptBridge.LeaveLobby();
            return null;
        });

        engine.RegisterFunction("startCountdown", (args) =>
        {
            float seconds = args.Count > 0 ? Convert.ToSingle(args[0]) : 10f;
            NetworkingTeaScriptBridge.StartCountdown(seconds);
            return null;
        });

        engine.RegisterFunction("getLobbyPlayerCount", (args) =>
        {
            return NetworkingTeaScriptBridge.GetLobbyPlayerCount();
        });

        engine.RegisterFunction("getLobbyPlayerName", (args) =>
        {
            int index = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return NetworkingTeaScriptBridge.GetLobbyPlayerName(index);
        });

        engine.RegisterFunction("getLobbyCountdown", (args) =>
        {
            return (double)NetworkingTeaScriptBridge.GetLobbyCountdown();
        });

        engine.RegisterFunction("getLobbyCountdownProgress", (args) =>
        {
            return (double)NetworkingTeaScriptBridge.GetLobbyCountdownProgress();
        });

        engine.RegisterFunction("isInLobby", (args) =>
        {
            return NetworkingTeaScriptBridge.IsInLobby();
        });

        engine.RegisterFunction("isLobbyHost", (args) =>
        {
            return NetworkingTeaScriptBridge.IsLobbyHost();
        });

        // Time
        engine.RegisterFunction("getDeltaTime", (args) =>
        {
            return (double)_deltaTime;
        });
        
        // Transform - Get Position
        engine.RegisterFunction("getPositionX", (args) =>
        {
            if (World.HasComponent<PhysicsComponent>(entity))
            {
                return (double)BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(entity).X;
            }

            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.X;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionY", (args) =>
        {
            if (World.HasComponent<PhysicsComponent>(entity))
            {
                return (double)BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(entity).Y;
            }

            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Y;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionZ", (args) =>
        {
            if (World.HasComponent<PhysicsComponent>(entity))
            {
                return (double)BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(entity).Z;
            }

            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Z;
            }
            return 0.0;
        });
        
        // Transform - Set Position
        engine.RegisterFunction("setPositionX", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                var pos = transform.Position;
                transform.Position = new BlueSky.Ramanujan.Vector3(Convert.ToSingle(args[0]), pos.Y, pos.Z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        engine.RegisterFunction("setPositionY", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                var pos = transform.Position;
                transform.Position = new BlueSky.Ramanujan.Vector3(pos.X, Convert.ToSingle(args[0]), pos.Z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        engine.RegisterFunction("setPositionZ", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                var pos = transform.Position;
                transform.Position = new BlueSky.Ramanujan.Vector3(pos.X, pos.Y, Convert.ToSingle(args[0]));
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        // Transform - Move
        engine.RegisterFunction("move", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var pos = transform.Position;
                transform.Position = new BlueSky.Ramanujan.Vector3(pos.X + x, pos.Y + y, pos.Z + z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        // Entity
        engine.RegisterFunction("destroy", (args) =>
        {
            _pendingDestroyEntities.Add(entity);
            return null;
        });
        
        // Input comes from the host editor/runtime through SetInputProviders.
        engine.RegisterFunction("getKey", (args) =>
        {
            string key = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            return _keyProvider?.Invoke(key) ?? false;
        });

        engine.RegisterFunction("getInput", (args) =>
        {
            string key = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            return _keyProvider?.Invoke(key) ?? false;
        });
        
        engine.RegisterFunction("getMouseButton", (args) =>
        {
            int button = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return _mouseButtonProvider?.Invoke(button) ?? false;
        });

        engine.RegisterFunction("getMousePosition", (args) =>
        {
            var pos = _mousePositionProvider?.Invoke() ?? Vector2.Zero;
            return new List<object?> { (double)pos.X, (double)pos.Y };
        });

        engine.RegisterFunction("getPosition", (args) =>
        {
            if (World != null && World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return new List<object?> { (double)transform.Position.X, (double)transform.Position.Y, (double)transform.Position.Z };
            }
            return new List<object?> { 0.0, 0.0, 0.0 };
        });

        engine.RegisterFunction("getRotation", (args) =>
        {
            if (World != null && World.TryGetComponent<TransformComponent>(entity, out var transform))
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
            if (args.Count >= 4 && World != null && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                transform.Rotation = new BlueSky.Ramanujan.Quaternion(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    Convert.ToSingle(args[3]));
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetRotation(entity, new System.Numerics.Quaternion(
                    transform.Rotation.X,
                    transform.Rotation.Y,
                    transform.Rotation.Z,
                    transform.Rotation.W));
            }
            return null;
        });

        engine.RegisterFunction("getVelocity", (args) =>
        {
            var vel = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
            return new List<object?> { (double)vel.X, (double)vel.Y, (double)vel.Z };
        });
        
        // Transform - Set Position (all at once)
        engine.RegisterFunction("setPosition", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                transform.Position = new BlueSky.Ramanujan.Vector3(x, y, z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });

        // Compatibility alias used by the bundled player.tea example.
        // Two args move in X/Y for simple 2D tests; three args move in 3D.
        engine.RegisterFunction("movePlayer", (args) =>
        {
            if (World == null || args.Count < 2 || !World.HasComponent<TransformComponent>(entity))
                return null;

            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            var pos = transform.Position;
            float x = Convert.ToSingle(args[0]);
            float y = Convert.ToSingle(args[1]);
            float z = args.Count >= 3 ? Convert.ToSingle(args[2]) : pos.Z;
            transform.Position = new BlueSky.Ramanujan.Vector3(x, y, z);
            SyncPhysicsPosition(entity, transform.Position);
            return null;
        });
        
        // Math functions
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

        // ══════════════════════════════════════════════════════════════
        //  VECTRA PARTICLES API (driven by the shared VectraSystem)
        //  addEmitter(x,y,z, rate,life,size, r,g,b,alpha, additive) → id
        //  setEmitterPos(id,x,y,z) | setEmitterRate(id,rate) | removeEmitter(id)
        //  burst(x,y,z, count, r,g,b, size,life, additive) → spawned
        //  particleCount() → live particles across all emitters
        // ════════════════════════════════════════════════════════════

        engine.RegisterFunction("particleCount", (args) =>
        {
            var vectra = VectraParticles;
            return vectra != null ? (double)vectra.Pool.LiveCount : 0.0;
        });

        engine.RegisterFunction("clearParticles", (args) =>
        {
            var vectra = VectraParticles;
            if (vectra == null) return 0.0;
            int removed = 0;
            foreach (var emitter in _scriptEmitters.Values)
            {
                if (vectra.RemoveEmitter(emitter)) removed++;
            }
            _scriptEmitters.Clear();
            return (double)removed;
        });

        engine.RegisterFunction("addEmitter", (args) =>
        {
            var vectra = VectraParticles;
            if (vectra == null || args.Count < 12) return 0.0;
            var cfg = new BlueSky.BSR.Vectra.VectraEmitterConfig
            {
                RatePerSec = Convert.ToSingle(args[3]),
                LifeMin = Convert.ToSingle(args[4]),
                LifeMax = Convert.ToSingle(args[4]),
                SpeedMin = 0.45f,
                SpeedMax = 1.05f,
                Direction = new Vector3(0f, 1f, 0f),
                ConeAngleRad = 0.48f,
                Gravity = 0.38f,
                Drag = 0.32f,
                SizeMin = Convert.ToSingle(args[5]) * 0.6f,
                SizeMax = Convert.ToSingle(args[5]),
                GrowRate = 0.48f,
                ColorA = new Vector3(Convert.ToSingle(args[6]), Convert.ToSingle(args[7]), Convert.ToSingle(args[8])),
                ColorB = new Vector3(Convert.ToSingle(args[6]), Convert.ToSingle(args[7]), Convert.ToSingle(args[8])),
                Alpha = Convert.ToSingle(args[9]),
                Additive = Convert.ToDouble(args[10]) >= 0.5,
                SpawnRadius = 0.2f,
                Turbulence = 0.28f,
            };
            var emitter = new BlueSky.BSR.Vectra.VectraEmitter(cfg,
                new Vector3(Convert.ToSingle(args[0]), Convert.ToSingle(args[1]), Convert.ToSingle(args[2])));
            if (!vectra.AddEmitter(emitter)) return 0.0;
            int id = _nextEmitterId++;
            _scriptEmitters[id] = emitter;
            return (double)id;
        });

        engine.RegisterFunction("removeEmitter", (args) =>
        {
            var vectra = VectraParticles;
            if (vectra == null || args.Count < 1) return 0.0;
            int id = Convert.ToInt32(args[0]);
            if (!_scriptEmitters.TryGetValue(id, out var emitter)) return 0.0;
            _scriptEmitters.Remove(id);
            return vectra.RemoveEmitter(emitter) ? 1.0 : 0.0;
        });

        engine.RegisterFunction("setEmitterPos", (args) =>
        {
            if (args.Count < 4) return 0.0;
            int id = Convert.ToInt32(args[0]);
            if (!_scriptEmitters.TryGetValue(id, out var emitter)) return 0.0;
            emitter.Position = new Vector3(Convert.ToSingle(args[1]), Convert.ToSingle(args[2]), Convert.ToSingle(args[3]));
            return 1.0;
        });

        engine.RegisterFunction("setEmitterRate", (args) =>
        {
            if (args.Count < 2) return 0.0;
            int id = Convert.ToInt32(args[0]);
            if (!_scriptEmitters.TryGetValue(id, out var emitter)) return 0.0;
            var cfg = emitter.Config;
            cfg.RatePerSec = System.Math.Max(0f, Convert.ToSingle(args[1]));
            emitter.Config = cfg;
            return 1.0;
        });

        engine.RegisterFunction("burst", (args) =>
        {
            var vectra = VectraParticles;
            if (vectra == null || args.Count < 10) return 0.0;
            float x = Convert.ToSingle(args[0]), y = Convert.ToSingle(args[1]), z = Convert.ToSingle(args[2]);
            int count = System.Math.Max(0, Convert.ToInt32(args[3]));
            float r = Convert.ToSingle(args[4]), g = Convert.ToSingle(args[5]), b = Convert.ToSingle(args[6]);
            float size = System.Math.Max(0.05f, Convert.ToSingle(args[7]));
            float life = System.Math.Max(0.05f, Convert.ToSingle(args[8]));
            float add = Convert.ToDouble(args[9]) >= 0.5 ? 1f : 0f;
            int spawned = 0;
            var pool = vectra.Pool;
            for (int k = 0; k < count; k++)
            {
                float ang = pool.NextFloat() * MathF.PI * 2f;
                float rad = MathF.Sqrt(pool.NextFloat()) * 0.25f;
                float up = 1f + pool.NextFloat() * 1.5f;
                if (pool.Spawn(
                    x + MathF.Cos(ang) * rad, y, z + MathF.Sin(ang) * rad,
                    MathF.Cos(ang) * rad * 4f, up, MathF.Sin(ang) * rad * 4f,
                    life * (0.7f + pool.NextFloat() * 0.6f), size, 0.5f,
                    r, g, b, 0.85f, 0.5f, 0.5f, 0.3f, add))
                    spawned++;
                else
                    break;
            }
            return (double)spawned;
        });
        
        // ══════════════════════════════════════════════════════════════
        //  VEHICLE PHYSICS API (uses static CarControllerSystem lookup)
        // ════════════════════════════════════════════════════════════

        Func<int, CarController?> getController = (entityId) =>
            CarControllerSystem.GetController((uint)entityId);

        Func<int, int, WheelState?> getWheel = (entityId, wheelIndex) =>
        {
            var ctrl = getController(entityId);
            // If entity's own controller has live data (physics on host, network on client), use it
            if (ctrl != null && ctrl.HasActiveData)
            {
                if (wheelIndex < 0 || wheelIndex >= (ctrl._wheelStates?.Length ?? 0)) return null;
                return ctrl._wheelStates![wheelIndex];
            }
            // Fall back to the possessed car (has valid data on both host and client)
            var possessed = CarControllerSystem.PossessedController;
            if (possessed != null && possessed.HasActiveData)
            {
                if (wheelIndex < 0 || wheelIndex >= (possessed._wheelStates?.Length ?? 0)) return null;
                return possessed._wheelStates![wheelIndex];
            }
            if (ctrl == null || ctrl._wheelStates == null) return null;
            if (wheelIndex < 0 || wheelIndex >= ctrl._wheelStates.Length) return null;
            return ctrl._wheelStates[wheelIndex];
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
                return (double)(isLongitudinal ? w.SlipRatio : w.SlipAngle);
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

        engine.RegisterFunction("getWheelSpinAngle", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.SpinAngle ?? 0.0);
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

        // ══════════════════════════════════════════════════════════════
        //  BONE MAPPING API (for skeletal mesh vehicle configuration)
        // ══════════════════════════════════════════════════════════════

        // setWheelBone(slot, boneName)
        // slot: 0=RightFront, 1=LeftFront, 2=LeftRear, 3=RightRear, 4=MainBody
        engine.RegisterFunction("setWheelBone", (args) =>
        {
            if (args.Count >= 2)
            {
                int slot = Convert.ToInt32(args[0]);
                string boneName = args[1]?.ToString() ?? "";
                uint eid = (uint)entity.Id;
                CarController.SetBoneOverride(eid, slot, boneName);
                Console.WriteLine($"[TeaScript:{entity.Id}] setWheelBone({slot}, \"{boneName}\")");
            }
            return null;
        });

        // setBodyBone(boneName) — shorthand for setWheelBone(4, boneName)
        engine.RegisterFunction("setBodyBone", (args) =>
        {
            if (args.Count >= 1)
            {
                string boneName = args[0]?.ToString() ?? "";
                uint eid = (uint)entity.Id;
                CarController.SetBodyBoneOverride(eid, boneName);
                Console.WriteLine($"[TeaScript:{entity.Id}] setBodyBone(\"{boneName}\")");
            }
            return null;
        });

        // refreshBones() — re-resolve bone mapping after setting overrides
        // Must be called after setWheelBone/setBodyBone and after the car controller is initialized
        engine.RegisterFunction("refreshBones", (args) =>
        {
            var ctrl = getController(entity.Id);
            if (ctrl != null)
            {
                ctrl.RefreshBoneMapping();
                Console.WriteLine($"[TeaScript:{entity.Id}] Bone mapping refreshed");
            }
            else
            {
                Console.WriteLine($"[TeaScript:{entity.Id}] refreshBones: car controller not yet initialized");
            }
            return null;
        });

        // debugWheelAnimation() — diagnostic function to check wheel animation status
        engine.RegisterFunction("debugWheelAnimation", (args) =>
        {
            var ctrl = getController(entity.Id);
            if (ctrl == null)
            {
                Console.WriteLine($"[TeaScript:{entity.Id}] ❌ NO CAR CONTROLLER!");
                return null;
            }

            bool hasSkel = ctrl.SkeletalMesh != null;
            int boneCount = ctrl.SkeletalMesh?.Bones?.Length ?? 0;

            Console.WriteLine($"[TeaScript:{entity.Id}] 🔍 Wheel Animation Debug:");
            Console.WriteLine($"  SkeletalMesh: {(hasSkel ? "✅ EXISTS" : "❌ MISSING")}");
            Console.WriteLine($"  Bone count: {boneCount}");

            return null;
        });

        // setWheelPosition(slot, x, y, z) — override wheel local position
        // slot: 0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight
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

        // setDriveWheels(fl, fr, rl, rr) — configure which wheels receive motor torque
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

        // setSteerWheels(fl, fr, rl, rr) — configure which wheels steer
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

        // ═════════════════════════════════════════════════════════════
        //  BONE TRANSFORM + DRIVING DATA API
        // ══════════════════════════════════════════════════════════════

        // setWheelTransform(wheelSlot, spinAngle, steerAngle) — spin/steer in radians
        engine.RegisterFunction("setWheelTransform", (args) =>
        {
            if (args.Count >= 3)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    int slot = Convert.ToInt32(args[0]);
                    float spin = Convert.ToSingle(args[1]);
                    float steer = Convert.ToSingle(args[2]);
                    ctrl.SetWheelSpinAndSteer(slot, spin, steer);
                }
            }
            return null;
        });

        // setBoneTransform(boneIndex, m11, m12, m13, m14, m21..m44) — raw 4×4 matrix
        engine.RegisterFunction("setBoneTransform", (args) =>
        {
            if (args.Count >= 17)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    int boneIdx = Convert.ToInt32(args[0]);
                    var m = new System.Numerics.Matrix4x4(
                        Convert.ToSingle(args[1]),  Convert.ToSingle(args[2]),  Convert.ToSingle(args[3]),  Convert.ToSingle(args[4]),
                        Convert.ToSingle(args[5]),  Convert.ToSingle(args[6]),  Convert.ToSingle(args[7]),  Convert.ToSingle(args[8]),
                        Convert.ToSingle(args[9]),  Convert.ToSingle(args[10]), Convert.ToSingle(args[11]), Convert.ToSingle(args[12]),
                        Convert.ToSingle(args[13]), Convert.ToSingle(args[14]), Convert.ToSingle(args[15]), Convert.ToSingle(args[16]));
                    ctrl.SetBoneTransformOverride(boneIdx, m);
                }
            }
            return null;
        });

        // Driving data: throttle/brake/steer (TeaScript writes, physics reads)
        engine.RegisterFunction("setThrottle", (args) =>
        {
            if (args.Count >= 1)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null) ctrl.ThrottleInput = Convert.ToSingle(args[0]);
            }
            return null;
        });
        engine.RegisterFunction("getThrottle", (args) =>
        {
            var ctrl = getController(entity.Id);
            return ctrl != null ? (double)ctrl.ThrottleInput : 0.0;
        });
        engine.RegisterFunction("setBrake", (args) =>
        {
            if (args.Count >= 1)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null) ctrl.BrakeInput = Convert.ToSingle(args[0]);
            }
            return null;
        });
        engine.RegisterFunction("getBrake", (args) =>
        {
            var ctrl = getController(entity.Id);
            return ctrl != null ? (double)ctrl.BrakeInput : 0.0;
        });
        engine.RegisterFunction("setSteerInput", (args) =>
        {
            if (args.Count >= 1)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null) ctrl.SteerInput = Convert.ToSingle(args[0]);
            }
            return null;
        });
        engine.RegisterFunction("getSteerInput", (args) =>
        {
            var ctrl = getController(entity.Id);
            return ctrl != null ? (double)ctrl.SteerInput : 0.0;
        });

        // ═════════════════════════════════════════════════════════════
        
        // Rigidbody - Velocity
        engine.RegisterFunction("getVelocityX", (args) =>
        {
            if (World.HasComponent<PhysicsComponent>(entity))
            {
                var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.X;
            }
            var ctrl = CarControllerSystem.GetController((uint)entity.Id);
            if (ctrl == null || !ctrl.HasActiveData)
                ctrl = CarControllerSystem.PossessedController;
            if (ctrl != null && ctrl.HasActiveData && World.TryGetComponent<TransformComponent>(entity, out var tf))
            {
                var rot = new System.Numerics.Quaternion(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z, tf.Rotation.W);
                var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, System.Numerics.Matrix4x4.CreateFromQuaternion(rot));
                return (double)(forward.X * ctrl.GetSpeed());
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocityY", (args) =>
        {
            if (World.HasComponent<PhysicsComponent>(entity))
            {
                var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.Y;
            }
            var ctrl = CarControllerSystem.GetController((uint)entity.Id);
            if (ctrl == null || !ctrl.HasActiveData)
                ctrl = CarControllerSystem.PossessedController;
            if (ctrl != null && ctrl.HasActiveData && World.TryGetComponent<TransformComponent>(entity, out var tf))
            {
                var rot = new System.Numerics.Quaternion(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z, tf.Rotation.W);
                var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, System.Numerics.Matrix4x4.CreateFromQuaternion(rot));
                return (double)(forward.Y * ctrl.GetSpeed());
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocityZ", (args) =>
        {
            if (World.HasComponent<PhysicsComponent>(entity))
            {
                var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.Z;
            }
            var ctrl = CarControllerSystem.GetController((uint)entity.Id);
            if (ctrl == null || !ctrl.HasActiveData)
                ctrl = CarControllerSystem.PossessedController;
            if (ctrl != null && ctrl.HasActiveData && World.TryGetComponent<TransformComponent>(entity, out var tf))
            {
                var rot = new System.Numerics.Quaternion(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z, tf.Rotation.W);
                var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, System.Numerics.Matrix4x4.CreateFromQuaternion(rot));
                return (double)(forward.Z * ctrl.GetSpeed());
            }
            return 0.0;
        });
        
        engine.RegisterFunction("setVelocity", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<PhysicsComponent>(entity))
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var velocity = new System.Numerics.Vector3(x, y, z);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetVelocity(entity, velocity);
            }
            return null;
        });
        
        // Rigidbody - Force
        engine.RegisterFunction("addForce", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<PhysicsComponent>(entity))
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var force = new System.Numerics.Vector3(x, y, z);
                BlueSky.Airborne.PhysicsTeaScriptBridge.AddForce(entity, force);
            }
            return null;
        });
        
        engine.RegisterFunction("addImpulse", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<PhysicsComponent>(entity))
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var impulse = new System.Numerics.Vector3(x, y, z);
                BlueSky.Airborne.PhysicsTeaScriptBridge.AddImpulse(entity, impulse);
            }
            return null;
        });
        
        // Rigidbody - Properties
        engine.RegisterFunction("getMass", (args) =>
        {
            if (World.TryGetComponent<PhysicsComponent>(entity, out var phys))
            {
                return (double)phys.Mass;
            }
            return 1.0;
        });
        
        engine.RegisterFunction("setMass", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref World.GetComponent<PhysicsComponent>(entity);
                phys.Mass = Convert.ToSingle(args[0]);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetMass(entity, phys.Mass);
            }
            return null;
        });
        
        engine.RegisterFunction("setGravity", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref World.GetComponent<PhysicsComponent>(entity);
                phys.UseGravity = Convert.ToBoolean(args[0]);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetUseGravity(entity, phys.UseGravity);
            }
            return null;
        });
        
        engine.RegisterFunction("setKinematic", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref World.GetComponent<PhysicsComponent>(entity);
                phys.IsKinematic = Convert.ToBoolean(args[0]);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetKinematic(entity, phys.IsKinematic);
            }
            return null;
        });
        
        // Rotation
        engine.RegisterFunction("rotate", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                
                // Simple euler angle rotation (degrees)
                transform.Rotation = BlueSky.Ramanujan.Quaternion.Euler(x, y, z);
                BlueSky.Airborne.PhysicsTeaScriptBridge.SetRotation(entity, new System.Numerics.Quaternion(
                    transform.Rotation.X,
                    transform.Rotation.Y,
                    transform.Rotation.Z,
                    transform.Rotation.W));
            }
            return null;
        });
        
        engine.RegisterFunction("raycast", (args) =>
        {
            if (args.Count < 6) return new List<object?> { false };

            var origin = new Vector3(
                Convert.ToSingle(args[0]),
                Convert.ToSingle(args[1]),
                Convert.ToSingle(args[2]));
            var direction = new Vector3(
                Convert.ToSingle(args[3]),
                Convert.ToSingle(args[4]),
                Convert.ToSingle(args[5]));
            float maxDistance = args.Count >= 7 ? Convert.ToSingle(args[6]) : 1000.0f;
            if (!float.IsFinite(maxDistance) || maxDistance <= 0 || direction.LengthSquared() < 1e-12f)
                return new List<object?> { false };

            direction = Vector3.Normalize(direction);
            if (!PhysicsTeaScriptBridge.Raycast(origin, direction, maxDistance, out var hit))
                return new List<object?> { false };

            return new List<object?>
            {
                true,
                (double)hit.Point.X, (double)hit.Point.Y, (double)hit.Point.Z,
                (double)hit.Normal.X, (double)hit.Normal.Y, (double)hit.Normal.Z,
                (double)hit.Distance
            };
        });
    }

    private void FlushPendingEntityDestruction()
    {
        if (World == null || _pendingDestroyEntities.Count == 0) return;
        foreach (var entity in _pendingDestroyEntities)
            World.DestroyEntity(entity);
        _pendingDestroyEntities.Clear();
    }

    private static void SyncPhysicsPosition(Entity entity, BlueSky.Ramanujan.Vector3 position)
    {
        BlueSky.Airborne.PhysicsTeaScriptBridge.SetPosition(
            entity,
            new System.Numerics.Vector3(position.X, position.Y, position.Z));
    }

    private static string ResolveScriptPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return BlueSky.Core.Assets.AssetPathResolver.Resolve(path);
    }

    private static RuntimeUIAnchor ParseRuntimeUIAnchor(List<object?> args, int index)
    {
        if (args.Count <= index || args[index] == null)
            return RuntimeUIAnchor.TopLeft;

        string value = args[index]!.ToString() ?? "";
        return Enum.TryParse(value, ignoreCase: true, out RuntimeUIAnchor anchor)
            ? anchor
            : RuntimeUIAnchor.TopLeft;
    }

    private static System.Numerics.Vector4 ParseRuntimeUIColor(List<object?> args, int index, System.Numerics.Vector4 fallback)
    {
        if (args.Count < index + 3)
            return fallback;

        float r = Convert.ToSingle(args[index]);
        float g = Convert.ToSingle(args[index + 1]);
        float b = Convert.ToSingle(args[index + 2]);
        float a = args.Count > index + 3 ? Convert.ToSingle(args[index + 3]) : fallback.W;
        return new System.Numerics.Vector4(r, g, b, a);
    }
    
    /// <summary>
    /// Cleanup all script instances.
    /// </summary>
    public void Cleanup()
    {
        _runtimeInstances.Clear();
    }

    public void ResetRuntimeInstances()
    {
        _runtimeInstances.Clear();
    }
}
