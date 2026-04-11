namespace BlueSky.Core.ECS
{
    /// <summary>
    /// Base class for editor-visible components. 
    /// In runtime hot-paths, we will extract the struct payload.
    /// </summary>
    public abstract class Component
    {
        public Entity Entity { get; internal set; }
    }
}
