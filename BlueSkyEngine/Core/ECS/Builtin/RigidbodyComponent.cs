using System.Numerics;

namespace BlueSky.Core.ECS.Builtin;

/// <summary>
/// Unified physics component: rigidbody + collider.
/// .bsphy asset path is stored externally in PhysicsAssetCache to keep the struct unmanaged (ECS requirement).
/// Replaces the old separate RigidbodyComponent and ColliderComponent.
/// </summary>
public struct PhysicsComponent
{
    // ── Rigidbody fields ──
    public float Mass;
    public float Drag;
    public float AngularDrag;
    public bool UseGravity;
    public bool IsKinematic;
    public bool FreezePositionX;
    public bool FreezePositionY;
    public bool FreezePositionZ;
    public bool FreezeRotationX;
    public bool FreezeRotationY;
    public bool FreezeRotationZ;

    // ── Collider fields ──
    public ColliderType Type;
    public Vector3 Center;
    public Vector3 Size;
    public float Radius;
    public float Height;
    public bool IsTrigger;
    public float Friction;
    public float Restitution;

    public PhysicsComponent()
    {
        Mass = 1.0f;
        Drag = 0.0f;
        AngularDrag = 0.05f;
        UseGravity = true;
        IsKinematic = false;
        FreezePositionX = false;
        FreezePositionY = false;
        FreezePositionZ = false;
        FreezeRotationX = false;
        FreezeRotationY = false;
        FreezeRotationZ = false;
        Type = ColliderType.Box;
        Center = Vector3.Zero;
        Size = Vector3.One;
        Radius = 0.5f;
        Height = 2.0f;
        IsTrigger = false;
        Friction = 0.5f;
        Restitution = 0.0f;
    }
}

public enum ColliderType
{
    Box,
    Sphere,
    Capsule,
    Mesh,
    Convex
}

/// <summary>
/// External cache for .bsphy asset paths + loaded PhysicsAssets.
/// Kept separate from PhysicsComponent to keep the struct unmanaged (ECS requirement).
/// </summary>
public static class PhysicsAssetCache
{
    private static readonly System.Collections.Generic.Dictionary<uint, string> s_bsphyPaths = new();
    private static readonly System.Collections.Generic.Dictionary<uint, Motif.PhysicsAsset?> s_cachedAssets = new();

    public static void SetPath(uint entityId, string path)
    {
        s_bsphyPaths[entityId] = path ?? "";
        if (!s_cachedAssets.ContainsKey(entityId))
            s_cachedAssets[entityId] = null;
    }

    public static string GetPath(uint entityId)
    {
        return s_bsphyPaths.TryGetValue(entityId, out var path) ? path : "";
    }

    public static void SetCachedAsset(uint entityId, Motif.PhysicsAsset? asset)
    {
        s_cachedAssets[entityId] = asset;
    }

    public static Motif.PhysicsAsset? GetCachedAsset(uint entityId)
    {
        return s_cachedAssets.TryGetValue(entityId, out var asset) ? asset : null;
    }

    public static bool HasEntry(uint entityId)
    {
        return s_bsphyPaths.ContainsKey(entityId);
    }

    public static void Clear(uint entityId)
    {
        s_bsphyPaths.Remove(entityId);
        s_cachedAssets.Remove(entityId);
    }

    public static void ClearAll()
    {
        s_bsphyPaths.Clear();
        s_cachedAssets.Clear();
    }
}
