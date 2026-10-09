using System;
using System.Collections.Generic;

namespace BlueSky.Core.ECS.Builtin
{
    /// <summary>
    /// Component for the NetworkManager entity. Handles player spawning and possession.
    /// Note: String fields (PrefabPath, SpawnedPlayers) are stored externally in
    /// NetworkManagerStorage because ECS components must be unmanaged value types.
    /// Use NetworkManagerStorage.GetPrefabPath(entityId) to access.
    /// </summary>
    public struct NetworkManagerComponent
    {
        /// <summary>Index of this entity in NetworkManagerStorage (for string fields).</summary>
        public int StorageIndex;
        
        /// <summary>Total number of players connected (including host).</summary>
        public int PlayerCount;
        
        /// <summary>Maximum players allowed.</summary>
        public int MaxPlayers;
        
        /// <summary>Whether this is the host.</summary>
        public bool IsHost;
        
        /// <summary>Whether the network manager has started spawning players.</summary>
        public bool HasStartedSpawning;
        
        /// <summary>Replication interval in seconds (how often to broadcast state).</summary>
        public float ReplicationInterval;
        
        /// <summary>Timer for replication broadcasts.</summary>
        public float ReplicationTimer;
        
        public static NetworkManagerComponent Default => new NetworkManagerComponent
        {
            StorageIndex = -1,
            PlayerCount = 0,
            MaxPlayers = 8,
            IsHost = false,
            HasStartedSpawning = false,
            ReplicationInterval = 0.1f,
            ReplicationTimer = 0f
        };
    }

    /// <summary>
    /// External storage for string fields of NetworkManagerComponent.
    /// Because ECS components must be unmanaged (no reference types),
    /// string data is stored in this static dictionary keyed by storage index.
    /// </summary>
    public static class NetworkManagerStorage
    {
        private static int _nextIndex;
        private static readonly Dictionary<int, string> _prefabPaths = new();
        private static readonly Dictionary<int, string> _spawnedPlayers = new();

        public static int Allocate(string prefabPath = "", string spawnedPlayers = "")
        {
            int idx = _nextIndex++;
            _prefabPaths[idx] = prefabPath;
            _spawnedPlayers[idx] = spawnedPlayers;
            return idx;
        }

        public static string GetPrefabPath(int storageIndex)
            => storageIndex >= 0 && _prefabPaths.ContainsKey(storageIndex) ? _prefabPaths[storageIndex] : "";

        public static void SetPrefabPath(int storageIndex, string path)
        {
            if (storageIndex >= 0) _prefabPaths[storageIndex] = path;
        }

        public static string GetSpawnedPlayers(int storageIndex)
            => storageIndex >= 0 && _spawnedPlayers.ContainsKey(storageIndex) ? _spawnedPlayers[storageIndex] : "";

        public static void SetSpawnedPlayers(int storageIndex, string value)
        {
            if (storageIndex >= 0) _spawnedPlayers[storageIndex] = value;
        }
    }
}
