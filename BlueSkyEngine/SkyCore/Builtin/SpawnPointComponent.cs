namespace BlueSky.SkyCore.Builtin
{
    public struct SpawnPointComponent
    {
        public int Index;
        public bool IsOccupied;
        public int OccupantEntityId;
        
        public static SpawnPointComponent Default => new SpawnPointComponent
        {
            Index = 0,
            IsOccupied = false,
            OccupantEntityId = -1
        };
    }
}
