using BlueSky.Ramanujan;


namespace BlueSky.SkyCore.Builtin
{
    /// <summary>
    /// Attaches an entity to a parent entity in the SkyCore hierarchy.
    /// When combined with <see cref="LocalTransformComponent"/> and <see cref="TransformComponent"/>,
    /// <see cref="HierarchySystem"/> automatically computes the entity's world transform from its parent.
    /// </summary>
    public struct ParentComponent
    {
        public Entity Parent;

        public ParentComponent(Entity parent)
        {
            Parent = parent;
        }
    }

    /// <summary>
    /// Local-space transform relative to an entity's <see cref="ParentComponent"/>.
    /// </summary>
    public struct LocalTransformComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public LocalTransformComponent(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public LocalTransformComponent(Vector3 position) : this(position, Quaternion.Identity, Vector3.One) { }
        public LocalTransformComponent() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

        public static LocalTransformComponent Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

        public readonly Matrix4x4 LocalMatrix =>
            Matrix4x4.CreateScale(Scale) *
            Matrix4x4.CreateRotation(Rotation) *
            Matrix4x4.CreateTranslation(Position);
    }
}
