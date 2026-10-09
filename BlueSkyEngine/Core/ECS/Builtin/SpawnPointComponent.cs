namespace BlueSky.Core.ECS.Builtin
{
    /// <summary>
    /// Component for spawn point entities. These are empty entities that define
    /// where player entities should be spawned. The NetworkManager references
    /// spawn points by name and uses their transform positions.
    /// </summary>
    public struct SpawnPointComponent
    {
        /// <summary>Spawn point index (0, 1, 2...). Determines spawn order.</summary>
        public int Index;
        
        /// <summary>Whether this spawn point is currently occupied.</summary>
        public bool IsOccupied;
        
        /// <summary>Entity ID of the player occupying this spawn point.</summary>
        public int OccupantEntityId;
        
        public static SpawnPointComponent Default => new SpawnPointComponent
        {
            Index = 0,
            IsOccupied = false,
            OccupantEntityId = -1
        };
    }
}