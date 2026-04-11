namespace BlueSky.Core.Assets;

/// <summary>
/// Lightweight handle to an asset. Use this instead of direct references.
/// </summary>
public readonly struct AssetHandle : IEquatable<AssetHandle>
{
    public static readonly AssetHandle Invalid = new(0);
    
    public readonly uint Id;
    
    public AssetHandle(uint id) => Id = id;
    
    public bool IsValid => Id != 0;
    
    public bool Equals(AssetHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is AssetHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    
    public static bool operator ==(AssetHandle left, AssetHandle right) => left.Equals(right);
    public static bool operator !=(AssetHandle left, AssetHandle right) => !left.Equals(right);
}
