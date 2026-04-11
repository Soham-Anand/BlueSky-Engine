namespace BlueSky.Core.ECS.Builtin
{
    public unsafe struct StaticMeshComponent
    {
        private fixed char _meshAssetId[128];
        private fixed char _materialAssetId[128];
        
        public string MeshAssetId
        {
            get
            {
                fixed (char* ptr = _meshAssetId)
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
                    _meshAssetId[i] = value[i];
                }
                _meshAssetId[length] = '\0';
            }
        }
        
        public string MaterialAssetId
        {
            get
            {
                fixed (char* ptr = _materialAssetId)
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
                    _materialAssetId[i] = value[i];
                }
                _materialAssetId[length] = '\0';
            }
        }
    }
}
