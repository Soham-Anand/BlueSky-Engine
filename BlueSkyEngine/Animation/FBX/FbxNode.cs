using System;
using System.Collections.Generic;

namespace BlueSky.Animation.FBX;

public class FbxNode
{
    public string Name { get; set; } = string.Empty;
    public List<FbxProperty> Properties { get; set; } = new();
    public List<FbxNode> Children { get; set; } = new();
    public FbxNode? Parent { get; set; }

    public FbxNode? FindChild(string name)
    {
        foreach (var child in Children)
        {
            if (child.Name == name)
                return child;
        }
        return null;
    }

    public void FindAllChildren(string name, List<FbxNode> results)
    {
        foreach (var child in Children)
        {
            if (child.Name == name)
                results.Add(child);
            child.FindAllChildren(name, results);
        }
    }

    public T? GetProperty<T>(int index) where T : class
    {
        if (index < 0 || index >= Properties.Count)
            return null;
        return Properties[index].AsObject() as T;
    }

    public T? GetPropertyValue<T>(int index) where T : unmanaged
    {
        if (index < 0 || index >= Properties.Count)
            return null;
        object? obj = Properties[index].AsObject();
        if (obj is T val)
            return val;
        return null;
    }

    public T? As<T>(int index = 0) where T : class
    {
        if (index < 0 || index >= Properties.Count)
            return null;
        return Properties[index].AsObject() as T;
    }
}
