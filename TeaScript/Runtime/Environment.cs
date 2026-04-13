using System;
using System.Collections.Generic;

namespace TeaScript.Runtime;

/// <summary>
/// Manages variable scopes and function definitions.
/// </summary>
public class Environment
{
    private readonly Dictionary<string, object?> _variables = new();
    private readonly Environment? _parent;
    
    public Environment(Environment? parent = null)
    {
        _parent = parent;
    }
    
    public void Define(string name, object? value)
    {
        _variables[name] = value;
    }
    
    public object? Get(string name)
    {
        if (_variables.ContainsKey(name))
        {
            return _variables[name];
        }
        
        if (_parent != null)
        {
            return _parent.Get(name);
        }
        
        throw new Exception($"Undefined variable '{name}'");
    }
    
    public void Set(string name, object? value)
    {
        if (_variables.ContainsKey(name))
        {
            _variables[name] = value;
            return;
        }
        
        if (_parent != null)
        {
            _parent.Set(name, value);
            return;
        }
        
        throw new Exception($"Undefined variable '{name}'");
    }
    
    public bool IsDefined(string name)
    {
        if (_variables.ContainsKey(name)) return true;
        if (_parent != null) return _parent.IsDefined(name);
        return false;
    }
}
