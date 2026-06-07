using System;
using System.Collections.Generic;

namespace BlueSky.Animation.FBX;

public class FbxConnection
{
    public long SourceId { get; set; }
    public long DestinationId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
}

public class FbxConnectionResolver
{
    private readonly Dictionary<long, FbxNode> _objectMap = new();
    private readonly List<FbxConnection> _connections = new();

    /// <summary>
    /// Build object map from a single root node (legacy API, searches children recursively).
    /// </summary>
    public void BuildObjectMap(FbxNode rootNode)
    {
        _objectMap.Clear();

        var objectsNode = FindNode(rootNode, "Objects");
        if (objectsNode == null)
            return;

        AddObjectsFromNode(objectsNode);
    }

    /// <summary>
    /// Build object map from the full FBX document (searches all root nodes).
    /// </summary>
    public void BuildObjectMap(FbxDocument document)
    {
        _objectMap.Clear();

        // In FBX, "Objects" is typically a top-level root node
        var objectsNodes = document.FindAllNodes("Objects");
        foreach (var objectsNode in objectsNodes)
        {
            AddObjectsFromNode(objectsNode);
        }
    }

    private void AddObjectsFromNode(FbxNode objectsNode)
    {
        foreach (var child in objectsNode.Children)
        {
            if (child.Properties.Count > 0)
            {
                long? idVal = child.GetPropertyValue<long>(0);
                if (idVal.HasValue && idVal.Value != 0)
                    _objectMap[idVal.Value] = child;
            }
        }
    }

    /// <summary>
    /// Build connection map from a single root node (legacy API).
    /// </summary>
    public void BuildConnectionMap(FbxNode rootNode)
    {
        _connections.Clear();

        var connectionsNode = FindNode(rootNode, "Connections");
        if (connectionsNode == null)
            return;

        AddConnectionsFromNode(connectionsNode);
    }

    /// <summary>
    /// Build connection map from the full FBX document.
    /// </summary>
    public void BuildConnectionMap(FbxDocument document)
    {
        _connections.Clear();

        var connectionsNodes = document.FindAllNodes("Connections");
        foreach (var connectionsNode in connectionsNodes)
        {
            AddConnectionsFromNode(connectionsNode);
        }
    }

    private void AddConnectionsFromNode(FbxNode connectionsNode)
    {
        foreach (var conn in connectionsNode.Children)
        {
            if (conn.Name != "C" || conn.Properties.Count < 3)
                continue;

            long? childIdVal = conn.GetPropertyValue<long>(1);
            long? parentIdVal = conn.GetPropertyValue<long>(2);
            string? propName = conn.GetProperty<string>(3);

            if (childIdVal.HasValue && parentIdVal.HasValue)
            {
                _connections.Add(new FbxConnection
                {
                    SourceId = childIdVal.Value,
                    DestinationId = parentIdVal.Value,
                    PropertyName = propName ?? ""
                });
            }
        }
    }

    public FbxNode? GetObjectById(long id)
    {
        _objectMap.TryGetValue(id, out var node);
        return node;
    }

    public List<FbxConnection> GetConnectionsFrom(long sourceId)
    {
        var result = new List<FbxConnection>();
        foreach (var conn in _connections)
        {
            if (conn.SourceId == sourceId)
                result.Add(conn);
        }
        return result;
    }

    public List<FbxConnection> GetConnectionsTo(long destinationId)
    {
        var result = new List<FbxConnection>();
        foreach (var conn in _connections)
        {
            if (conn.DestinationId == destinationId)
                result.Add(conn);
        }
        return result;
    }

    private FbxNode? FindNode(FbxNode root, string name)
    {
        if (root.Name == name)
            return root;

        foreach (var child in root.Children)
        {
            var found = FindNode(child, name);
            if (found != null)
                return found;
        }

        return null;
    }
}
