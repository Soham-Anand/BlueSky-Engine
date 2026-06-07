using System.Numerics;
using BlueSky.Platform;
using BlueSky.Platform.Input;

namespace BlueSky.Core.Gameplay;

/// <summary>
/// Interface for entities that can be possessed and controlled by a player
/// </summary>
public interface IPossessable
{
    /// <summary>
    /// Called when this entity is possessed by a player
    /// </summary>
    /// <param name="controller">The player controller taking possession</param>
    void OnPossessed(PlayerController controller);
    
    /// <summary>
    /// Called when this entity is unpossessed
    /// </summary>
    void OnUnpossessed();
    
    /// <summary>
    /// Process input while possessed
    /// </summary>
    /// <param name="input">Input system reference</param>
    /// <param name="deltaTime">Time since last frame</param>
    void ProcessInput(IInputContext input, float deltaTime);
    
    /// <summary>
    /// Get the ideal camera position for this possessed entity
    /// </summary>
    /// <returns>World position for camera</returns>
    Vector3 GetCameraPosition();
    
    /// <summary>
    /// Get the ideal camera target for this possessed entity
    /// </summary>
    /// <returns>World position to look at</returns>
    Vector3 GetCameraTarget();
    
    /// <summary>
    /// Whether this entity can currently be possessed
    /// </summary>
    bool CanBePossessed { get; }
    
    /// <summary>
    /// Display name for this possessable entity
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Advertise this entity for automatic possession by a specific player.
    /// The engine will automatically possess this entity when called.
    /// Example: car.AdvertisePossession("Player1");
    /// </summary>
    /// <param name="playerId">The player ID to possess this entity (default: "Player1")</param>
    void AdvertisePossession(string playerId = "Player1");
}
