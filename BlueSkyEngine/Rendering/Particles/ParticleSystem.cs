using System;
using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using NotBSRenderer;
using BlueSky.Rendering.Compute;

namespace BlueSky.Rendering.Particles;

public class ParticleSystem : SystemBase, IDisposable
{
    private readonly IRHIDevice _device;
    private readonly ComputeSystem _computeSystem;
    private ParticleRenderer? _renderer;
    
    private IRHIBuffer? _particleBuffer;
    private ParticleData[]? _cpuParticles; // Used for CPU fallback
    private uint _activeParticleCount = 0;
    private uint _maxParticles;
    
    public Vector3 WindDirection { get; set; } = new Vector3(1, 0, 0);
    public float WindStrength { get; set; } = 0.0f;

    public ParticleSystem(IRHIDevice device, ComputeSystem computeSystem, uint maxParticles = 10000)
    {
        _device = device;
        _computeSystem = computeSystem;
        _maxParticles = maxParticles;
    }

    protected override void OnInitialize()
    {
        _renderer = new ParticleRenderer(_device);
        _renderer.Initialize();
        
        CreateParticleBuffer();
    }

    private void CreateParticleBuffer()
    {
        _particleBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = _maxParticles * 64, // 64 bytes per particle
            Usage = BufferUsage.Storage | BufferUsage.Vertex,
            MemoryType = _computeSystem.IsComputeSupported ? MemoryType.GpuOnly : MemoryType.CpuToGpu,
            DebugName = "ParticleBuffer"
        });
        
        if (!_computeSystem.IsComputeSupported)
        {
            _cpuParticles = new ParticleData[_maxParticles];
        }
    }

    public override void Update(float dt)
    {
        if (World == null) return;
        
        var wind = WindDirection * WindStrength;

        // Process emitters
        World.ForEach<ParticleEmitterComponent, TransformComponent>((entity, emitter, transform) =>
        {
            if (!emitter.IsActive) return;
            
            emitter.EmitAccumulator += emitter.EmissionRate * dt;
            int particlesToEmit = (int)Math.Floor(emitter.EmitAccumulator);
            
            if (particlesToEmit > 0)
            {
                emitter.EmitAccumulator -= particlesToEmit;
                var sysPos = new System.Numerics.Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
                EmitParticles(particlesToEmit, ref emitter, sysPos);
            }
            
            World.AddComponent(entity, emitter); // Save state back
        });

        // Simulate particles
        if (_computeSystem.IsComputeSupported)
        {
            // TODO: dispatch compute shader
        }
        else if (_cpuParticles != null)
        {
            ParticlePhysics.SimulateCPU(_cpuParticles, dt, wind, ref _activeParticleCount);
            
            // Upload to GPU for rendering
            if (_activeParticleCount > 0)
            {
                var span = new ReadOnlySpan<ParticleData>(_cpuParticles, 0, (int)_activeParticleCount);
                _device.UpdateBuffer(_particleBuffer!, MemoryMarshal.AsBytes(span));
            }
        }
    }
    
    private void EmitParticles(int count, ref ParticleEmitterComponent emitter, Vector3 position)
    {
        // Simple CPU emission for now
        if (_cpuParticles == null) return; 

        Random rnd = new Random();
        for (int i = 0; i < count; i++)
        {
            if (_activeParticleCount >= _maxParticles) break;

            float life = emitter.MinLifetime + (float)rnd.NextDouble() * (emitter.MaxLifetime - emitter.MinLifetime);
            
            Vector3 vel = new Vector3(
                emitter.MinStartVelocity.X + (float)rnd.NextDouble() * (emitter.MaxStartVelocity.X - emitter.MinStartVelocity.X),
                emitter.MinStartVelocity.Y + (float)rnd.NextDouble() * (emitter.MaxStartVelocity.Y - emitter.MinStartVelocity.Y),
                emitter.MinStartVelocity.Z + (float)rnd.NextDouble() * (emitter.MaxStartVelocity.Z - emitter.MinStartVelocity.Z)
            );

            _cpuParticles[_activeParticleCount] = new ParticleData
            {
                Position = position, // Handle shape correctly here eventually
                Velocity = vel,
                Size = emitter.StartSize,
                Color = emitter.StartColor,
                Life = life,
                MaxLife = life,
                Rotation = 0
            };
            
            _activeParticleCount++;
        }
    }

    public void Render(IRHICommandBuffer cmd, Matrix4x4 viewProj, Vector3 camPos, Vector3 camUp, Vector3 camRight)
    {
        if (_activeParticleCount > 0 && _renderer != null && _particleBuffer != null)
        {
            _renderer.Render(cmd, _particleBuffer, _activeParticleCount, viewProj, camPos, camUp, camRight);
        }
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _particleBuffer?.Dispose();
    }
}
