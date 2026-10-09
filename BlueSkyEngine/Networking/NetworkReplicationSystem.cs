using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Scene;
using BlueSky.Core.Gameplay;

namespace BlueSky.Networking;

// ── Wire format ────────────────────────────────────────────────────────────
// All messages are JSON-encoded and sent via EOS P2P on channel 1.
// Max P2P packet is 1170 bytes. Entity state is compact (pos+rot per entity).

public class NetworkedEntityState
{
    public int EntityId { get; set; }
    public string PlayerId { get; set; } = "";
    public string PrefabPath { get; set; } = "";
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[4];
    public float[] Velocity { get; set; } = new float[3];
    // Car HUD data (host-authoritative)
    public float Speed { get; set; }
    public float SpeedMPH { get; set; }
    public int Gear { get; set; }
    public float RPM { get; set; }
    public bool[]? WheelGrounded { get; set; }
    public float[]? WheelSuspension { get; set; }
    public float[]? WheelSlipRatio { get; set; }
    public float[]? WheelSlipAngle { get; set; }
}

/// <summary>
/// Client → Host: car input only (tiny packet, fits in one P2P message).
/// </summary>
public class PlayerInputMessage
{
    public string Type { get; set; } = "input";
    public string PlayerId { get; set; } = "";
    public float Throttle { get; set; }
    public float Brake { get; set; }
    public float Steer { get; set; }
    public bool Handbrake { get; set; }
}

/// <summary>
/// Host → Client: authoritative positions for ALL entities.
/// </summary>
public class WorldStateMessage
{
    public string Type { get; set; } = "world_state";
    public List<NetworkedEntityState> Entities { get; set; } = new();
}

/// <summary>
/// Host → Client: tells the client which entity is "theirs" so it can possess it.
/// </summary>
public class OwnershipMessage
{
    public string Type { get; set; } = "ownership";
    public string PlayerId { get; set; } = "";
    public int EntityId { get; set; }
    public string PrefabPath { get; set; } = "";
    public int SpawnPointIndex { get; set; }
}

/// <summary>
/// Handles entity spawning and replication via EOS P2P (channel 1).
///
/// Architecture: HOST-AUTHORITATIVE
///   Host: runs physics for ALL cars. Receives input from clients via P2P.
///   Client: sends input only. Receives positions from host. No physics.
///
/// Transport: EOS P2P packets, channel 1 (lobby uses channel 0).
/// </summary>
public sealed class NetworkReplicationSystem : IDisposable
{
    private World? _world;
    private readonly Action<string>? _log;

    // P2P transport — set by GameRuntime after multiplayer service is ready
    private IMultiplayerService? _service;

    // State
    private readonly Dictionary<int, NetworkedEntityState> _networkedEntities = new();
    private readonly Dictionary<string, int> _playerToEntityId = new();
    private readonly Dictionary<string, string> _playerPrefabPaths = new();
    private readonly Dictionary<int, string> _entityToPlayer = new(); // reverse lookup
    private string _lastPrefabPath = "";
    private bool _isHost;
    private bool _isClient;
    private bool _paused; // Buffer packets during scene transitions
    private const float ReplicationInterval = 0.05f; // 20 Hz replication (smooth for cars)
    private const byte ReplicationChannel = 1; // lobby uses channel 0

    // Client: smooth lerp toward latest received target position
    private readonly Dictionary<int, (float[] pos, float[] rot, float[] vel, float timeSinceUpdate)> _interpTargets = new();
    private const float InterpLerpRate = 14f; // Smooth follow: ~23% per frame at 60fps, avoids snap-jitter

    // Host receives input from clients (ConcurrentDictionary — written from EOS callback thread)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlayerInputMessage> _pendingInputs = new();

    // Events
    public event Action<int, string>? OnEntitySpawned;
    public event Action<int, string>? OnReplicatedEntitySpawned;
    public event Action? OnReset;

    public NetworkReplicationSystem(World? world, Action<string>? log = null)
    {
        _world = world;
        _log = log;
    }

    public void SetWorld(World? world)
    {
        _world = world;
        _log?.Invoke("[Replication] World reference updated");
    }

    /// <summary>
    /// Attach the multiplayer service for P2P communication.
    /// Must be called after the service is initialized.
    /// </summary>
    public void AttachTransport(IMultiplayerService service)
    {
        if (_service != null)
            _service.OnPacketReceived -= HandleP2PPacket;
        _service = service;
        _service.OnPacketReceived += HandleP2PPacket;
        _log?.Invoke("[Replication] Transport attached to multiplayer service (channel 1)");
    }

    public void InitializeAsHost()
    {
        _isHost = true;
        _isClient = false;
        _log?.Invoke("[Replication] Initialized as HOST (authoritative)");
    }

    public void InitializeAsClient()
    {
        _isHost = false;
        _isClient = true;
        _log?.Invoke("[Replication] Initialized as CLIENT (input sender)");
    }

    // Pause/resume: buffer ownership messages during scene transitions
    private readonly List<(string playerId, int hostEntityId, string prefabPath, int spawnPointIndex)> _bufferedOwnership = new();

    public void Pause()
    {
        _paused = true;
        _bufferedOwnership.Clear();
        _log?.Invoke("[Replication] Paused — buffering packets");
    }

    public void Resume()
    {
        _paused = false;
        _log?.Invoke($"[Replication] Resumed — processing {_bufferedOwnership.Count} buffered ownership messages");

        // Replay buffered ownership messages into the new world
        foreach (var (playerId, hostEntityId, prefabPath, spawnPointIndex) in _bufferedOwnership)
        {
            if (!_playerToEntityId.ContainsKey(playerId) && !string.IsNullOrEmpty(prefabPath))
            {
                SpawnReplicatedEntity(playerId, hostEntityId, prefabPath, spawnPointIndex);
            }
        }
        _bufferedOwnership.Clear();
    }

    // ── P2P Packet Handling ──────────────────────────────────────────────

    private void HandleP2PPacket(string peerId, byte[] data, byte channel)
    {
        // Skip handshake packets (single 0x01 byte used for P2P connection establishment)
        if (data.Length == 1 && data[0] == 0x01)
            return;

        // Only handle replication channel (1); lobby uses channel 0
        if (channel != 1) return;

        try
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type", out var typeProp))
                return;

            string type = typeProp.GetString() ?? "";

            switch (type)
            {
                case "world_state":
                    if (!_isHost && !_paused)
                        HandleWorldStateFromHost(root);
                    break;

                case "ownership":
                    if (!_isHost || _paused)
                        HandleOwnershipFromHost(root);
                    break;

                case "input":
                    if (_isHost)
                        HandleInputFromClient(root, peerId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Replication] Packet handling error: {ex.Message}");
        }
    }

    private void HandleWorldStateFromHost(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("Entities", out var entitiesProp))
            return;

        var msg = new WorldStateMessage();
        foreach (var entityJson in entitiesProp.EnumerateArray())
        {
            var state = new NetworkedEntityState
            {
                EntityId = entityJson.GetProperty("EntityId").GetInt32(),
                PlayerId = entityJson.TryGetProperty("PlayerId", out var pid) ? pid.GetString() ?? "" : "",
                Position = ParseFloatArray(entityJson, "Position", 3),
                Rotation = ParseFloatArray(entityJson, "Rotation", 4),
                Velocity = entityJson.TryGetProperty("Velocity", out var vel) ? ParseFloatArray(vel, null, 3) : new float[3],
                Speed = entityJson.TryGetProperty("Speed", out var spd) ? spd.GetSingle() : 0f,
                SpeedMPH = entityJson.TryGetProperty("SpeedMPH", out var mph) ? mph.GetSingle() : 0f,
                Gear = entityJson.TryGetProperty("Gear", out var gr) ? gr.GetInt32() : 1,
                RPM = entityJson.TryGetProperty("RPM", out var rpm) ? rpm.GetSingle() : 800f,
            };
            // Parse car wheel arrays (nullable — may be absent)
            if (entityJson.TryGetProperty("WheelGrounded", out var wg) && wg.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var arr = new bool[4];
                int i = 0;
                foreach (var v in wg.EnumerateArray())
                    if (i < 4) arr[i++] = v.GetBoolean();
                state.WheelGrounded = arr;
            }
            if (entityJson.TryGetProperty("WheelSuspension", out var ws))
                state.WheelSuspension = ParseFloatArray(ws, null, 4);
            if (entityJson.TryGetProperty("WheelSlipRatio", out var sr))
                state.WheelSlipRatio = ParseFloatArray(sr, null, 4);
            if (entityJson.TryGetProperty("WheelSlipAngle", out var sa))
                state.WheelSlipAngle = ParseFloatArray(sa, null, 4);
            msg.Entities.Add(state);
        }

        ApplyWorldState(msg);
    }

    private void HandleOwnershipFromHost(System.Text.Json.JsonElement root)
    {
        string playerId = root.TryGetProperty("PlayerId", out var pid) ? pid.GetString() ?? "" : "";
        int entityId = root.TryGetProperty("EntityId", out var eid) ? eid.GetInt32() : 0;
        string prefabPath = root.TryGetProperty("PrefabPath", out var pp) ? pp.GetString() ?? "" : "";
        int spawnPointIndex = root.TryGetProperty("SpawnPointIndex", out var spi) ? spi.GetInt32() : 0;

        if (string.IsNullOrEmpty(playerId) || entityId == 0) return;

        _log?.Invoke($"[Replication] Received ownership: Entity_{entityId} for player '{playerId}' (spawn {spawnPointIndex})");

        // If paused (scene transitioning), buffer the message
        if (_paused)
        {
            _bufferedOwnership.Add((playerId, entityId, prefabPath, spawnPointIndex));
            _log?.Invoke($"[Replication] Buffered ownership (paused): Entity_{entityId} for '{playerId}'");
            return;
        }

        // If we already have this entity, just re-fire the callback
        // (use ContainsKey, not ID comparison — local vs host IDs are different spaces)
        if (_playerToEntityId.ContainsKey(playerId))
        {
            int localId = _playerToEntityId[playerId];
            OnReplicatedEntitySpawned?.Invoke(localId, playerId);
            return;
        }

        // Spawn the entity if not yet present
        if (!_playerToEntityId.ContainsKey(playerId) && !string.IsNullOrEmpty(prefabPath))
        {
            SpawnReplicatedEntity(playerId, entityId, prefabPath, spawnPointIndex);
        }
    }

    private void HandleInputFromClient(System.Text.Json.JsonElement root, string peerId)
    {
        string playerId = root.TryGetProperty("PlayerId", out var pid) ? pid.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(playerId)) return;

        var input = new PlayerInputMessage
        {
            PlayerId = playerId,
            Throttle = root.TryGetProperty("Throttle", out var t) ? t.GetSingle() : 0f,
            Brake = root.TryGetProperty("Brake", out var b) ? b.GetSingle() : 0f,
            Steer = root.TryGetProperty("Steer", out var s) ? s.GetSingle() : 0f,
            Handbrake = root.TryGetProperty("Handbrake", out var h) ? h.GetBoolean() : false,
        };

        _pendingInputs[playerId] = input;
    }

    // ── Host: Apply client input + broadcast world state ─�────────────────

    /// <summary>
    /// Called by the host's CarControllerSystem to apply client input to their car.
    /// Returns the input for a given player, or null if no input received.
    /// </summary>
    public PlayerInputMessage? GetPendingInput(string playerId)
    {
        return _pendingInputs.TryGetValue(playerId, out var input) ? input : null;
    }

    /// <summary>
    /// Clear processed inputs (call after applying them in CarControllerSystem).
    /// </summary>
    public void ClearPendingInput(string playerId)
    {
        _pendingInputs.TryRemove(playerId, out _);
    }

    /// <summary>
    /// Host: broadcast authoritative world state to all connected clients.
    /// </summary>
    public void BroadcastWorldState()
    {
        if (_world == null || _service == null || !_service.IsLoggedIn) return;
        if (_playerToEntityId.Count == 0) return;

        var msg = new WorldStateMessage();

        foreach (var kvp in _playerToEntityId)
        {
            var entity = FindEntityById(kvp.Value);
            if (entity.Id == 0) continue;

            if (_world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                var state = new NetworkedEntityState
                {
                    EntityId = kvp.Value,
                    PlayerId = kvp.Key,
                    Position = new float[] { transform.Position.X, transform.Position.Y, transform.Position.Z },
                    Rotation = new float[] { transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W },
                    Velocity = new float[3],
                };

                // Include velocity for client-side interpolation
                if (_world.TryGetComponent<PhysicsComponent>(entity, out _))
                {
                    var vel = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(entity);
                    state.Velocity = new float[] { vel.X, vel.Y, vel.Z };
                }

                // Include car HUD data for client-side display
                var carController = CarControllerSystem.GetController((uint)entity.Id);
                if (carController != null)
                {
                    state.Speed = carController.GetSpeed();
                    state.SpeedMPH = carController.GetSpeedMPH();
                    state.Gear = carController.CurrentGear;
                    state.RPM = carController.CurrentRPM;
                    state.WheelGrounded = new bool[4];
                    state.WheelSuspension = new float[4];
                    state.WheelSlipRatio = new float[4];
                    state.WheelSlipAngle = new float[4];
                    for (int i = 0; i < 4 && i < carController.WheelCount; i++)
                    {
                        var ws = carController.GetWheelState(i);
                        if (ws != null)
                        {
                            state.WheelGrounded[i] = ws.IsGrounded;
                            state.WheelSuspension[i] = ws.SuspensionCompression;
                            state.WheelSlipRatio[i] = ws.SlipRatio;
                            state.WheelSlipAngle[i] = ws.SlipAngle;
                        }
                    }
                }

                msg.Entities.Add(state);
            }
        }

        if (msg.Entities.Count == 0) return;

        string json = JsonSerializer.Serialize(msg);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        // Send to all connected clients
        _service.BroadcastPacket(bytes, ReplicationChannel);
    }

    // ── Client: Apply host world state ───────────────────────────────────

    private void ApplyWorldState(WorldStateMessage msg)
    {
        if (_world == null) return;

        foreach (var state in msg.Entities)
        {
            _networkedEntities[state.EntityId] = state;

            // Find the entity locally
            if (!_playerToEntityId.TryGetValue(state.PlayerId, out int localEntityId) || localEntityId == 0)
                continue;

            var entity = FindEntityById(localEntityId);
            if (entity.Id == 0) continue;

            // Store latest target for smooth lerp interpolation with velocity extrapolation
            _interpTargets[localEntityId] = ((float[])state.Position.Clone(), (float[])state.Rotation.Clone(), (float[])(state.Velocity?.Clone() ?? new float[3]), 0f);

            // Apply car HUD data to local controller
            var carController = CarControllerSystem.GetController((uint)localEntityId);
            if (carController != null)
            {
                carController.ApplyNetworkState(state.Speed, state.SpeedMPH, state.Gear, state.RPM);
                if (state.WheelGrounded != null && state.WheelSuspension != null && state.WheelSlipRatio != null && state.WheelSlipAngle != null)
                {
                    for (int i = 0; i < 4 && i < state.WheelGrounded.Length; i++)
                    {
                        var ws = carController.GetWheelState(i);
                        if (ws != null)
                        {
                            ws.IsGrounded = state.WheelGrounded[i];
                            ws.SuspensionCompression = state.WheelSuspension[i];
                            ws.SlipRatio = state.WheelSlipRatio[i];
                            ws.SlipAngle = state.WheelSlipAngle[i];
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Client: smoothly lerp entity transforms toward latest received host positions.
    /// </summary>
    public void InterpolateEntities(float deltaTime, int skipEntityId = -1)
    {
        if (_world == null) return;

        foreach (var key in _interpTargets.Keys.ToList())
        {
            int entityId = key;
            if (entityId == skipEntityId) continue;

            var data = _interpTargets[key];

            // Accumulate time since last host update
            data.timeSinceUpdate += deltaTime;
            _interpTargets[key] = data;

            var entity = FindEntityById(entityId);
            if (entity.Id == 0) continue;
            if (!_world.TryGetComponent<TransformComponent>(entity, out var transform)) continue;

            // Extrapolate target position using host velocity for smooth motion between 20Hz updates
            // Clamp extrapolation to 100ms to prevent overshooting when car stops
            float t = MathF.Min(data.timeSinceUpdate, 0.1f);
            var baseTarget = new System.Numerics.Vector3(data.pos[0], data.pos[1], data.pos[2]);
            var extrapolatedTarget = baseTarget + new System.Numerics.Vector3(data.vel[0], data.vel[1], data.vel[2]) * t;

            float lerpFactor = Math.Clamp(InterpLerpRate * deltaTime, 0f, 1f);

            // Lerp position toward extrapolated target
            var curPos = new System.Numerics.Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
            var desPos = System.Numerics.Vector3.Lerp(curPos, extrapolatedTarget, lerpFactor);
            transform.Position = new BlueSky.Core.Math.Vector3(desPos.X, desPos.Y, desPos.Z);

            // Slerp rotation toward target
            var curRot = new System.Numerics.Quaternion(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
            var desRot = new System.Numerics.Quaternion(data.rot[0], data.rot[1], data.rot[2], data.rot[3]);
            var slerped = System.Numerics.Quaternion.Slerp(curRot, desRot, lerpFactor);
            transform.Rotation = new BlueSky.Core.Math.Quaternion(slerped.X, slerped.Y, slerped.Z, slerped.W);

            _world.AddComponent(entity, transform);
        }
    }

    // ── Client: Send input to host ───────────────────────────────────────

    /// <summary>
    /// Client: send car input to host via P2P.
    /// </summary>
    public void SendInputToHost(string playerId, float throttle, float brake, float steer, bool handbrake)
    {
        if (_service == null || !_service.IsLoggedIn) return;

        var input = new PlayerInputMessage
        {
            PlayerId = playerId,
            Throttle = throttle,
            Brake = brake,
            Steer = steer,
            Handbrake = handbrake,
        };

        string json = JsonSerializer.Serialize(input);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        // Send to host (broadcast works since host is one of the connected peers)
        _service.BroadcastPacket(bytes, ReplicationChannel);
    }

    // ── Spawning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Host: spawn an entity for a player and notify all clients.
    /// </summary>
    public int SpawnPlayerEntity(string playerName, string prefabPath, int spawnPointIndex = -1)
    {
        if (_world == null || string.IsNullOrEmpty(prefabPath))
        {
            _log?.Invoke("[Replication] Cannot spawn — world is null or no prefab path");
            return -1;
        }

        if (_playerToEntityId.ContainsKey(playerName))
        {
            _log?.Invoke($"[Replication] Player '{playerName}' already has entity { _playerToEntityId[playerName]}");
            return _playerToEntityId[playerName];
        }

        if (!System.IO.File.Exists(prefabPath))
        {
            _log?.Invoke($"[Replication] Prefab not found: {prefabPath}");
            return -1;
        }

        var prefabData = SceneSerializer.LoadScene(prefabPath);
        if (prefabData == null)
        {
            _log?.Invoke($"[Replication] Failed to load prefab: {prefabPath}");
            return -1;
        }
        _lastPrefabPath = prefabPath;

        if (spawnPointIndex == -1)
            spawnPointIndex = FindNextSpawnPoint();
        var spawnPos = GetSpawnPointPosition(spawnPointIndex);

        int entityId = SpawnPrefabAtPosition(prefabData, spawnPos);

        if (entityId >= 0)
        {
            _playerToEntityId[playerName] = entityId;
            _entityToPlayer[entityId] = playerName;
            _playerPrefabPaths[playerName] = prefabPath;
            _log?.Invoke($"[Replication] Spawned entity {entityId} for '{playerName}' at spawn {spawnPointIndex}");

            OnEntitySpawned?.Invoke(entityId, playerName);
        }

        return entityId;
    }

    /// <summary>
    /// Host: send ownership notification to all clients about a specific player's entity.
    /// </summary>
    public void BroadcastOwnership(string playerId, int entityId, string prefabPath, int spawnPointIndex = 0)
    {
        if (_service == null || !_service.IsLoggedIn) return;

        var msg = new OwnershipMessage
        {
            PlayerId = playerId,
            EntityId = entityId,
            PrefabPath = prefabPath,
            SpawnPointIndex = spawnPointIndex,
        };

        string json = JsonSerializer.Serialize(msg);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _service.BroadcastPacket(bytes, ReplicationChannel);
        _log?.Invoke($"[Replication] Broadcast ownership: '{playerId}' -> Entity_{entityId}");
    }

    /// <summary>
    /// Client: manually spawn a replicated entity (called from ownership message).
    /// </summary>
    private void SpawnReplicatedEntity(string playerId, int hostEntityId, string prefabPath, int spawnPointIndex = 0)
    {
        if (_world == null) return;

        var prefabData = SceneSerializer.LoadScene(prefabPath);
        if (prefabData == null) return;

        // Spawn at the matching spawn point so the camera/position matches the host
        var spawnPos = GetSpawnPointPosition(spawnPointIndex);
        int entityId = SpawnPrefabAtPosition(prefabData, spawnPos);
        _log?.Invoke($"[Replication] Client spawning entity for '{playerId}' at spawn {spawnPointIndex} pos=({spawnPos.X:F1},{spawnPos.Y:F1},{spawnPos.Z:F1})");

        if (entityId >= 0)
        {
            _playerToEntityId[playerId] = entityId;
            _entityToPlayer[entityId] = playerId;
            _playerPrefabPaths[playerId] = prefabPath;
            _log?.Invoke($"[Replication] Client spawned entity {entityId} for '{playerId}' at spawn point {spawnPointIndex}");

            OnReplicatedEntitySpawned?.Invoke(entityId, playerId);
        }
    }

    // ── Reset ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reset all entity mappings (call on scene transition).
    /// Keeps the transport alive — no need to recreate.
    /// </summary>
    public void Reset()
    {
        _isHost = false;
        _isClient = false;
        _networkedEntities.Clear();
        _playerToEntityId.Clear();
        _entityToPlayer.Clear();
        _playerPrefabPaths.Clear();
        _interpTargets.Clear();
        _pendingInputs.Clear();
        _log?.Invoke("[Replication] State reset (transport alive)");
        OnReset?.Invoke();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public int GetEntityIdForPlayer(string playerName)
        => _playerToEntityId.TryGetValue(playerName, out var id) ? id : -1;

    public string GetPlayerForEntity(int entityId)
        => _entityToPlayer.TryGetValue(entityId, out var key) ? key : "";

    public IReadOnlyDictionary<string, int> PlayerToEntityMapping => _playerToEntityId;

    public bool HasPlayer(string playerName) => _playerToEntityId.ContainsKey(playerName);

    /// <summary>
    /// Remove a player's entity tracking. Does NOT remove the ECS entity itself.
    /// </summary>
    public void RemovePlayerEntity(string playerName)
    {
        _pendingInputs.TryRemove(playerName, out _);
        if (_playerToEntityId.TryGetValue(playerName, out int eid))
        {
            _entityToPlayer.Remove(eid);
            _interpTargets.Remove(eid);
            _networkedEntities.Remove(eid);
        }
        _playerToEntityId.Remove(playerName);
        _playerPrefabPaths.Remove(playerName);
    }

    public bool IsHost => _isHost;
    public bool IsClient => _isClient;

    // ── Internal: Prefab spawning ────────────────────────────────────────

    private int FindNextSpawnPoint()
    {
        if (_world == null) return 0;
        var query = _world.CreateQuery().All<SpawnPointComponent>().All<TransformComponent>().Build();
        int order = 0;
        foreach (var chunk in _world.GetQueryChunks(query))
        {
            int spIdx = chunk.GetComponentIndex(typeof(SpawnPointComponent));
            var entities = chunk.GetEntities();
            for (int i = 0; i < chunk.Count; i++)
            {
                var sp = chunk.GetComponent<SpawnPointComponent>(i, spIdx);
                if (!sp.IsOccupied)
                {
                    sp.IsOccupied = true;
                    chunk.SetComponent(i, spIdx, sp);
                    return order;
                }
                order++;
            }
        }
        return order;
    }

    private System.Numerics.Vector3 GetSpawnPointPosition(int index)
    {
        if (_world == null) return System.Numerics.Vector3.Zero;
        var query = _world.CreateQuery().All<SpawnPointComponent>().All<TransformComponent>().Build();
        int spIdx, transIdx;
        int order = 0;
        foreach (var chunk in _world.GetQueryChunks(query))
        {
            spIdx = chunk.GetComponentIndex(typeof(SpawnPointComponent));
            transIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            for (int i = 0; i < chunk.Count; i++)
            {
                if (order == index)
                {
                    var t = chunk.GetComponent<TransformComponent>(i, transIdx);
                    return new System.Numerics.Vector3(t.Position.X, t.Position.Y, t.Position.Z);
                }
                order++;
            }
        }
        return System.Numerics.Vector3.Zero;
    }

    private int SpawnPrefabAtPosition(SceneData prefabData, System.Numerics.Vector3 position)
    {
        if (_world == null) return -1;

        var existingIds = _world.GetAllEntities().Select(e => e.Id).ToHashSet();
        var rootTransform = prefabData.Entities
            .SelectMany(e => e.Components)
            .OfType<TransformComponentData>()
            .FirstOrDefault();

        float rootX = rootTransform?.Position.Length >= 3 ? rootTransform.Position[0] : 0f;
        float rootY = rootTransform?.Position.Length >= 3 ? rootTransform.Position[1] : 0f;
        float rootZ = rootTransform?.Position.Length >= 3 ? rootTransform.Position[2] : 0f;

        foreach (var transform in prefabData.Entities.SelectMany(e => e.Components).OfType<TransformComponentData>())
        {
            if (transform.Position.Length < 3)
            {
                transform.Position = new float[] { position.X, position.Y, position.Z };
                continue;
            }
            transform.Position = new float[]
            {
                position.X + transform.Position[0] - rootX,
                position.Y + transform.Position[1] - rootY,
                position.Z + transform.Position[2] - rootZ
            };
        }

        SceneConverter.SceneDataToWorld(prefabData, _world, clearWorld: false);

        var spawned = _world.GetAllEntities()
            .Where(e => !existingIds.Contains(e.Id))
            .ToList();

        var playerEntity = spawned.FirstOrDefault(e => _world.HasComponent<CarControllerComponent>(e));
        if (playerEntity.Id == 0)
            playerEntity = spawned.FirstOrDefault(e => _world.HasComponent<TransformComponent>(e));

        if (playerEntity.Id == 0)
        {
            _log?.Invoke("[Replication] Prefab instantiated but no entity identified");
            return -1;
        }

        _world.AddComponent(playerEntity, new NameComponent($"Player_{playerEntity.Id}"));
        _log?.Invoke($"[Replication] Spawned {spawned.Count} entities, root = Entity_{playerEntity.Id}");
        return playerEntity.Id;
    }

    private Entity FindEntityById(int entityId)
    {
        if (_world == null) return default;
        foreach (var chunk in _world.GetQueryChunks(_world.CreateQuery().All<TransformComponent>().Build()))
        {
            foreach (var entity in chunk.GetEntities())
            {
                if (entity.Id == entityId) return entity;
            }
        }
        return default;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float[] ParseFloatArray(System.Text.Json.JsonElement parent, string? propertyName, int count)
    {
        System.Text.Json.JsonElement arr;
        if (propertyName != null)
        {
            if (!parent.TryGetProperty(propertyName, out arr))
                return new float[count];
        }
        else
        {
            arr = parent;
        }

        var result = new float[count];
        int i = 0;
        foreach (var v in arr.EnumerateArray())
        {
            if (i >= count) break;
            result[i++] = v.GetSingle();
        }
        return result;
    }

    public void Dispose()
    {
        if (_service != null)
            _service.OnPacketReceived -= HandleP2PPacket;
        Reset();
    }
}
