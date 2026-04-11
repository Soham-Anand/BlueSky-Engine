using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;

namespace BlueSky.Rendering
{
    /// <summary>
    /// Enhanced rendering system with shadow mapping and post-processing.
    /// </summary>
    public class RenderingSystem : SystemBase
    {
        private readonly IRenderer _renderer;
        private readonly Viewport _viewport;
        private int _sceneFramebuffer;
        private int _bloomFbo;

        public bool EnableBloom { get; set; } = true;
        public bool EnableTonemapping { get; set; } = true;
        public bool EnableShadows { get; set; } = true;

        public RenderingSystem(IRenderer renderer, Viewport viewport, int width, int height)
        {
            _renderer = renderer;
            _viewport = viewport;
            
            // Abstract framebuffer creation
            _sceneFramebuffer = _renderer.CreateFramebuffer(width, height);
            _bloomFbo = _renderer.CreateFramebuffer(width, height);
        }

        public void Resize(int width, int height)
        {
            // TODO: Handle resizing of abstraction-managed framebuffers
        }

        public override void Update(float dt)
        {
            _viewport.Update(dt);
            
            // Find main light for shadows
            Vector3 lightPos = new Vector3(10, 20, 10);
            Vector3 lightDir = new Vector3(-0.5f, -1, -0.3f);
            
            if (World != null)
            {
                foreach (var entity in World.GetAllEntities())
                {
                    if (World.HasComponent<LightComponent>(entity))
                    {
                        var light = World.GetComponent<LightComponent>(entity);
                        if (light.Type == LightComponent.LightType.Directional)
                        {
                            if (World.HasComponent<TransformComponent>(entity))
                            {
                                var transform = World.GetComponent<TransformComponent>(entity);
                                lightPos = transform.Position;
                                lightDir = transform.Forward;
                            }
                            break;
                        }
                    }
                }
            }

            // Find camera
            CameraComponent? camera = null;
            TransformComponent? cameraTransform = null;
            
            if (World != null)
            {
                foreach (var entity in World.GetAllEntities())
                {
                    if (World.HasComponent<CameraComponent>(entity) && 
                        World.HasComponent<TransformComponent>(entity))
                    {
                        camera = World.GetComponent<CameraComponent>(entity);
                        cameraTransform = World.GetComponent<TransformComponent>(entity);
                        break;
                    }
                }
            }

            if (camera == null || cameraTransform == null)
                return;

            // Get viewport dimensions
            int viewportWidth = _viewport.Width;
            int viewportHeight = _viewport.Height;

            // Render with post-processing abstraction
            if (EnableBloom || EnableTonemapping)
            {
                _renderer.SetRenderTarget(_sceneFramebuffer);
                _renderer.Clear(0.05f, 0.05f, 0.1f, 1.0f);
                
                if (EnableShadows)
                {
                    _renderer.RenderSceneWithShadows(World!, camera.Value, cameraTransform.Value, lightPos, lightDir);
                }
                else
                {
                    _renderer.BeginFrame(0.05f, 0.05f, 0.1f);
                    _renderer.RenderScene(World!, camera.Value, cameraTransform.Value);
                }
                
                _renderer.SetRenderTarget(0); // Back to screen
                
                // TODO: Implement backend-agnostic post-processing passes
                // For now, we'll just ensure the scene is rendered to the screen
                _renderer.SetViewport(0, 0, viewportWidth, viewportHeight);
            }
            else
            {
                // Direct rendering - Respect set viewport and don't clear color
                if (EnableShadows)
                {
                    _renderer.RenderSceneWithShadows(World!, camera.Value, cameraTransform.Value, lightPos, lightDir);
                }
                else
                {
                    _renderer.ClearDepth();
                    _renderer.RenderScene(World!, camera.Value, cameraTransform.Value);
                }
            }
        }

        public void Dispose()
        {
            _renderer.DeleteResource(ResourceType.Texture, _renderer.GetFramebufferTexture(_sceneFramebuffer));
            _renderer.DeleteResource(ResourceType.Texture, _renderer.GetFramebufferTexture(_bloomFbo));
            _renderer.DeleteResource(ResourceType.Texture, _sceneFramebuffer); // Assuming CreateFramebuffer creates a resource
            _renderer.DeleteResource(ResourceType.Texture, _bloomFbo);
            _renderer.Dispose();
        }
    }

    public class LightSystem : SystemBase
    {
        public override void Update(float dt)
        {
            // Update dynamic lights, animate light properties, etc.
        }
    }

    public class TransformSystem : SystemBase
    {
        public override void Update(float dt)
        {
            var world = World ?? throw new InvalidOperationException("TransformSystem has not been initialized.");
            
            // Use the new ForEach API for cache-efficient iteration
            world.ForEach<TransformComponent, VelocityComponent>((entity, transform, velocity) =>
            {
                transform.Translate(velocity.Linear * dt);
                
                // Apply angular velocity (simplified)
                if (velocity.Angular.LengthSquared > 0)
                {
                    var angularRotation = new Quaternion(
                        velocity.Angular.Normalize(), 
                        velocity.Angular.Length * dt
                    );
                    transform.Rotate(angularRotation);
                }
                
                // Update component back to world
                world.AddComponent(entity, transform);
            });
        }
    }
}
