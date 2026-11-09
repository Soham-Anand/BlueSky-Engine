namespace BlueSky.SkyCore
{
    /// <summary>
    /// Base class for editor-visible components.
    /// In runtime hot-paths, SkyCore stores unmanaged structs directly in SOA chunks.
    /// </summary>
    public abstract class Component
    {
        public Entity Entity { get; internal set; }
    }
}
