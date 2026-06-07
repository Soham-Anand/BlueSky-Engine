using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.Math;

namespace BlueSky.Editor;

public interface IUndoableAction
{
    string Description { get; }
    void Execute();
    void Undo();
}

public class TransformChangeAction : IUndoableAction
{
    private readonly World _world;
    private readonly uint _entityId;
    private readonly Vector3 _oldPosition;
    private readonly Vector3 _newPosition;
    private readonly Quaternion _oldRotation;
    private readonly Quaternion _newRotation;
    private readonly Vector3 _oldScale;
    private readonly Vector3 _newScale;
    
    public string Description => $"Transform Entity {_entityId}";
    
    public TransformChangeAction(World world, uint entityId, 
        Vector3 oldPos, Vector3 newPos,
        Quaternion oldRot, Quaternion newRot,
        Vector3 oldScale, Vector3 newScale)
    {
        _world = world;
        _entityId = entityId;
        _oldPosition = oldPos;
        _newPosition = newPos;
        _oldRotation = oldRot;
        _newRotation = newRot;
        _oldScale = oldScale;
        _newScale = newScale;
    }
    
    public void Execute()
    {
        var entity = new Entity((int)_entityId, 0);
        if (_world.HasComponent<Core.ECS.Builtin.TransformComponent>(entity))
        {
            ref var transform = ref _world.GetComponent<Core.ECS.Builtin.TransformComponent>(entity);
            transform.Position = _newPosition;
            transform.Rotation = _newRotation;
            transform.Scale = _newScale;
        }
    }
    
    public void Undo()
    {
        var entity = new Entity((int)_entityId, 0);
        if (_world.HasComponent<Core.ECS.Builtin.TransformComponent>(entity))
        {
            ref var transform = ref _world.GetComponent<Core.ECS.Builtin.TransformComponent>(entity);
            transform.Position = _oldPosition;
            transform.Rotation = _oldRotation;
            transform.Scale = _oldScale;
        }
    }
}

public class CreateEntityAction : IUndoableAction
{
    private readonly World _world;
    private readonly uint _entityId;
    private readonly string _entityName;
    
    public string Description => $"Create Entity '{_entityName}'";
    
    public CreateEntityAction(World world, uint entityId, string entityName)
    {
        _world = world;
        _entityId = entityId;
        _entityName = entityName;
    }
    
    public void Execute()
    {
        // Entity already created, nothing to do
    }
    
    public void Undo()
    {
        var entity = new Entity((int)_entityId, 0);
        _world.DestroyEntity(entity);
    }
}

public class DeleteEntityAction : IUndoableAction
{
    private readonly World _world;
    private readonly uint _entityId;
    private readonly string _entityName;
    private readonly Vector3 _position;
    private readonly Quaternion _rotation;
    private readonly Vector3 _scale;
    private readonly bool _hadTransform;
    
    public string Description => $"Delete Entity '{_entityName}'";
    
    public DeleteEntityAction(World world, uint entityId, string entityName)
    {
        _world = world;
        _entityId = entityId;
        _entityName = entityName;
        
        var entity = new Entity((int)_entityId, 0);
        _hadTransform = _world.HasComponent<Core.ECS.Builtin.TransformComponent>(entity);
        
        if (_hadTransform)
        {
            ref var transform = ref _world.GetComponent<Core.ECS.Builtin.TransformComponent>(entity);
            _position = transform.Position;
            _rotation = transform.Rotation;
            _scale = transform.Scale;
        }
    }
    
    public void Execute()
    {
        var entity = new Entity((int)_entityId, 0);
        _world.DestroyEntity(entity);
    }
    
    public void Undo()
    {
        // Recreate entity with same ID (simplified - in production you'd store full component data)
        var entity = _world.CreateEntity();
        
        if (_hadTransform)
        {
            _world.AddComponent(entity, new Core.ECS.Builtin.TransformComponent
            {
                Position = _position,
                Rotation = _rotation,
                Scale = _scale
            });
        }
        
        _world.AddComponent(entity, new Core.ECS.Builtin.NameComponent(_entityName));
    }
}

public class UndoRedoSystem
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private const int MaxHistorySize = 100;
    
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;
    
    public void RecordAction(IUndoableAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear(); // Clear redo stack when new action is recorded
        
        // Limit history size
        if (_undoStack.Count > MaxHistorySize)
        {
            var temp = new Stack<IUndoableAction>(_undoStack.Reverse());
            temp.Pop(); // Remove oldest
            _undoStack.Clear();
            foreach (var item in temp.Reverse())
                _undoStack.Push(item);
        }
        
        Console.WriteLine($"[UndoRedo] Recorded: {action.Description} (Undo: {_undoStack.Count}, Redo: {_redoStack.Count})");
    }
    
    public void Undo()
    {
        if (!CanUndo) return;
        
        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
        
        Console.WriteLine($"[UndoRedo] Undid: {action.Description} (Undo: {_undoStack.Count}, Redo: {_redoStack.Count})");
    }
    
    public void Redo()
    {
        if (!CanRedo) return;
        
        var action = _redoStack.Pop();
        action.Execute();
        _undoStack.Push(action);
        
        Console.WriteLine($"[UndoRedo] Redid: {action.Description} (Undo: {_undoStack.Count}, Redo: {_redoStack.Count})");
    }
    
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        Console.WriteLine("[UndoRedo] History cleared");
    }
    
    public string GetUndoDescription()
    {
        return CanUndo ? _undoStack.Peek().Description : "";
    }
    
    public string GetRedoDescription()
    {
        return CanRedo ? _redoStack.Peek().Description : "";
    }
}
