using System;
using System.Numerics;
using System.Collections.Generic;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Rendering;

namespace BlueSky.Core.Gameplay;

/// <summary>
/// Manages player possession of entities and input routing.
/// Supports automatic possession via AdvertisePossession() calls.
/// </summary>
public class PlayerController
{
    private static PlayerController? _instance;
    public static PlayerController Instance => _instance ??= new PlayerController();
    
    private IPossessable? _possessedEntity;
    private IInputContext? _input;
    private Viewport? _viewport;
    
    // Camera state
    private Vector3 _freeCameraPosition = new(0, 5, 10);
    private Vector3 _freeCameraTarget = Vector3.Zero;
    private bool _isFreeCameraMode = true;
    
    // Possession advertisement queue (playerId -> entity)
    private Dictionary<string, IPossessable> _possessionRequests = new();
    
    // Events
    public event Action<IPossessable>? OnEntityPossessed;
    public event Action<IPossessable>? OnEntityUnpossessed;
    
    public IPossessable? PossessedEntity => _possessedEntity;
    public bool IsInFreeCameraMode => _isFreeCameraMode;
    
    /// <summary>
    /// Initialize the player controller with required systems
    /// </summary>
    public void Initialize(IInputContext input, Viewport viewport)
    {
        _input = input;
        _viewport = viewport;
        
        Console.WriteLine("[PlayerController] Initialized - Entities can call AdvertisePossession() to be possessed");
        Console.WriteLine("[PlayerController] Press E to unpossess current entity");
    }
    
    /// <summary>
    /// Possess an entity, taking control of it
    /// </summary>
    public bool Possess(IPossessable entity)
    {
        if (entity == null || !entity.CanBePossessed)
        {
            Console.WriteLine("[PlayerController] Cannot possess entity - not available");
            return false;
        }
        
        // Unpossess current entity first
        if (_possessedEntity != null)
        {
            Unpossess();
        }
        
        _possessedEntity = entity;
        _isFreeCameraMode = false;
        
        // Notify the entity it's been possessed
        entity.OnPossessed(this);
        
        Console.WriteLine($"[PlayerController] Possessed: {entity.DisplayName}");
        OnEntityPossessed?.Invoke(entity);
        
        return true;
    }
    
    /// <summary>
    /// Release control of the currently possessed entity
    /// </summary>
    public void Unpossess()
    {
        if (_possessedEntity == null) return;
        
        var entity = _possessedEntity;
        
        // Store current camera position for free camera
        if (_viewport != null)
        {
            _freeCameraPosition = _viewport.GetCameraPositionNumerics();
            _freeCameraTarget = _possessedEntity.GetCameraTarget();
        }
        
        _possessedEntity.OnUnpossessed();
        _possessedEntity = null;
        _isFreeCameraMode = true;
        
        Console.WriteLine($"[PlayerController] Unpossessed: {entity.DisplayName}");
        OnEntityUnpossessed?.Invoke(entity);
    }
    
    /// <summary>
    /// Register a possession request from an entity (called via AdvertisePossession)
    /// </summary>
    internal void RegisterPossessionRequest(IPossessable entity, string playerId)
    {
        if (entity == null) return;
        
        Console.WriteLine($"[PlayerController] 📢 Possession request from {entity.DisplayName} for {playerId}");
        _possessionRequests[playerId] = entity;
    }
    
    /// <summary>
    /// Update the player controller - call this every frame
    /// </summary>
    public void Update(float deltaTime)
    {
        if (_input == null || _viewport == null) return;
        
        // Process possession requests (automatic possession)
        ProcessPossessionRequests();
        
        // Handle unpossession input
        HandleUnpossessionInput();
        
        // Route input to possessed entity or handle free camera
        if (_possessedEntity != null)
        {
            _possessedEntity.ProcessInput(_input, deltaTime);
            UpdatePossessedCamera();
        }
        else
        {
            UpdateFreeCamera(deltaTime);
        }
    }
    
    private void ProcessPossessionRequests()
    {
        // Process Player1 requests (can be extended for multiplayer)
        if (_possessionRequests.TryGetValue("Player1", out var entity))
        {
            _possessionRequests.Remove("Player1");
            
            if (entity.CanBePossessed)
            {
                Possess(entity);
            }
            else
            {
                Console.WriteLine($"[PlayerController] ❌ Cannot possess {entity.DisplayName} - not available");
            }
        }
    }
    
    private void HandleUnpossessionInput()
    {
        if (_input == null) return;
        
        // E key to unpossess
        if (_input.IsKeyPressed(KeyCode.E) && _possessedEntity != null)
        {
            Unpossess();
        }
    }
    
    private void UpdatePossessedCamera()
    {
        if (_possessedEntity == null || _viewport == null) return;
        
        var cameraPos = _possessedEntity.GetCameraPosition();
        var cameraTarget = _possessedEntity.GetCameraTarget();
        
        ref var cameraTransform = ref _viewport.GetCameraTransform();
        
        var pos = new BlueSky.Core.Math.Vector3(cameraPos.X, cameraPos.Y, cameraPos.Z);
        var target = new BlueSky.Core.Math.Vector3(cameraTarget.X, cameraTarget.Y, cameraTarget.Z);
        
        cameraTransform.SetPosition(pos);
        cameraTransform.LookAt(target, BlueSky.Core.Math.Vector3.Up);
    }
    
    private void UpdateFreeCamera(float deltaTime)
    {
        // Free camera is handled by the Viewport's own Update method
        // No need to override it here
    }
    
    /// <summary>
    /// Get the nearest possessable entity to a world position (for click-to-possess)
    /// </summary>
    public IPossessable? FindNearestPossessable(Vector3 worldPosition, float maxDistance = 5.0f)
    {
        // This would need to be implemented with the ECS system
        // For now, return null - entities will register themselves
        return null;
    }
}