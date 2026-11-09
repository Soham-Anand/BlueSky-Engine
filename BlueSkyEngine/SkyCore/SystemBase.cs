namespace BlueSky.SkyCore
{
    public abstract class SystemBase
    {
        protected World? World { get; private set; }
        protected uint LastSystemVersion { get; set; }

        public void Initialize(World world)
        {
            World = world;
            LastSystemVersion = world.Version;
            OnInitialize();
        }

        protected virtual void OnInitialize() { }
        public abstract void Update(float dt);
    }
}
