namespace BlueSky.Core.ECS
{
    public abstract class SystemBase
    {
        protected World? World { get; private set; }

        public void Initialize(World world)
        {
            World = world;
            OnInitialize();
        }

        protected virtual void OnInitialize() { }
        public abstract void Update(float dt);
    }
}
