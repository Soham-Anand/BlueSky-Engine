namespace BlueSky.Core.ECS.Builtin
{
    /// <summary>
    /// Tracks entity health for the destruction system.
    /// </summary>
    public struct HealthComponent
    {
        public float Current;
        public float Max;
        public bool IsDead => Current <= 0;

        public HealthComponent(float max)
        {
            Max = max;
            Current = max;
        }
    }

    /// <summary>
    /// Tag component for entities that can be fractured into shards.
    /// </summary>
    public unsafe struct FracturableComponent
    {
        private fixed char _shardMeshAssetId[128];
        public int ShardCount;
        public float ExplodeForce;

        public string ShardMeshAssetId
        {
            get
            {
                fixed (char* ptr = _shardMeshAssetId)
                {
                    return new string(ptr).TrimEnd('\0');
                }
            }
            set
            {
                value ??= string.Empty;
                int length = System.Math.Min(127, value.Length);
                for (int i = 0; i < length; i++)
                {
                    _shardMeshAssetId[i] = value[i];
                }
                _shardMeshAssetId[length] = '\0';
            }
        }

        public FracturableComponent(string assetId, int count, float force = 5.0f)
        {
            ShardCount = count;
            ExplodeForce = force;
            ShardMeshAssetId = assetId;
        }
    }

    /// <summary>
    /// Tag for pooled shard entities.
    /// </summary>
    public struct ShardTag { }
}
