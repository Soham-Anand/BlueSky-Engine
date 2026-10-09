namespace BlueSky.Core.Assets;

public enum AssetType
{
    Unknown,
    Mesh, // deprecated
    StaticMesh,
    SkeletalMesh,
    Texture,
    Material, // removed: material system deleted (Strata will own surfaces); kept for serialized numbering
    Shader,
    Scene,
    Terrain,
    Prefab,
    Audio,
    Script
}
