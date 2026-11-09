using System;
using System.Collections.Generic;

namespace BlueSky.SkyCore.Builtin
{
    public struct NetworkManagerComponent
    {
        public int StorageIndex;
        public int PlayerCount;
        public int MaxPlayers;
        public bool IsHost;
        public bool HasStartedSpawning;
        public float ReplicationInterval;
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
