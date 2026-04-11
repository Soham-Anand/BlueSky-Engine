using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Scripting
{
    /// <summary>
    /// Represents a node type in the BlueScript visual scripting system.
    /// Inspired by Blender's Geometry Nodes / Shader Nodes style.
    /// </summary>
    public enum NodeCategory
    {
        Event,      // OnBeginPlay, OnTick, OnCollision
        Flow,       // Branch, Sequence, ForEach
        Math,       // Add, Subtract, Multiply, Clamp, Lerp
        Vector,     // MakeVector, BreakVector, Normalize, CrossProduct
        Transform,  // GetPosition, SetPosition, Translate, Rotate
        Logic,      // And, Or, Not, Compare
        Variable,   // Get, Set
        Output,     // Print, Debug
    }

    /// <summary>
    /// A single pin (input or output) on a node.
    /// </summary>
    public class NodePin
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "";
        public PinType Type { get; set; } = PinType.Flow;
        public bool IsInput { get; set; }
        public string? DefaultValue { get; set; }
        public string? ConnectedToNodeId { get; set; }
        public string? ConnectedToPinId { get; set; }
    }

    public enum PinType
    {
        Flow,       // White — execution flow
        Boolean,    // Red
        Integer,    // Cyan
        Float,      // Green
        String,     // Magenta/Pink
        Vector3,    // Yellow
        Object,     // Blue
    }

    /// <summary>
    /// A single node in the BlueScript graph.
    /// </summary>
    public class ScriptNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Title { get; set; } = "Node";
        public NodeCategory Category { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 180;
        public List<NodePin> Inputs { get; set; } = new();
        public List<NodePin> Outputs { get; set; } = new();

        /// <summary>
        /// Header color derived from category.
        /// </summary>
        [JsonIgnore]
        public string HeaderColor => Category switch
        {
            NodeCategory.Event => "#B33B3B",      // Red
            NodeCategory.Flow => "#5B5B5B",        // Gray
            NodeCategory.Math => "#4B7B4B",        // Green
            NodeCategory.Vector => "#7B7B2B",      // Yellow-olive
            NodeCategory.Transform => "#3B5B9B",   // Blue
            NodeCategory.Logic => "#7B3B7B",       // Purple
            NodeCategory.Variable => "#2B8B8B",    // Teal
            NodeCategory.Output => "#8B6B2B",      // Gold
            _ => "#5B5B5B"
        };
    }

    /// <summary>
    /// A connection (wire) between two pins.
    /// </summary>
    public class NodeConnection
    {
        public string FromNodeId { get; set; } = "";
        public string FromPinId { get; set; } = "";
        public string ToNodeId { get; set; } = "";
        public string ToPinId { get; set; } = "";
    }

    /// <summary>
    /// Represents a complete BlueScript graph (stored in .BlueAsset files).
    /// </summary>
    public class BlueScriptGraph
    {
        public string Name { get; set; } = "New BlueScript";
        public List<ScriptNode> Nodes { get; set; } = new();
        public List<NodeConnection> Connections { get; set; } = new();

        /// <summary>
        /// Creates a default graph with an Event BeginPlay node.
        /// </summary>
        public static BlueScriptGraph CreateDefault()
        {
            var graph = new BlueScriptGraph();

            var beginPlay = new ScriptNode
            {
                Title = "Event BeginPlay",
                Category = NodeCategory.Event,
                X = 100, Y = 200,
                Outputs = new List<NodePin>
                {
                    new() { Name = "Exec", Type = PinType.Flow, IsInput = false },
                }
            };
            graph.Nodes.Add(beginPlay);

            var tick = new ScriptNode
            {
                Title = "Event Tick",
                Category = NodeCategory.Event,
                X = 100, Y = 400,
                Outputs = new List<NodePin>
                {
                    new() { Name = "Exec", Type = PinType.Flow, IsInput = false },
                    new() { Name = "Delta Time", Type = PinType.Float, IsInput = false },
                }
            };
            graph.Nodes.Add(tick);

            var printNode = new ScriptNode
            {
                Title = "Print String",
                Category = NodeCategory.Output,
                X = 400, Y = 200,
                Inputs = new List<NodePin>
                {
                    new() { Name = "Exec", Type = PinType.Flow, IsInput = true },
                    new() { Name = "Text", Type = PinType.String, IsInput = true, DefaultValue = "Hello from BlueSky!" },
                },
                Outputs = new List<NodePin>
                {
                    new() { Name = "Exec", Type = PinType.Flow, IsInput = false },
                }
            };
            graph.Nodes.Add(printNode);

            return graph;
        }
    }

    /// <summary>
    /// Factory for creating commonly used nodes.
    /// </summary>
    public static class NodeFactory
    {
        public static ScriptNode MathAdd(double x = 0, double y = 0) => new()
        {
            Title = "Add",
            Category = NodeCategory.Math,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "A", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
                new() { Name = "B", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
            },
            Outputs = new()
            {
                new() { Name = "Result", Type = PinType.Float, IsInput = false },
            }
        };

        public static ScriptNode MathMultiply(double x = 0, double y = 0) => new()
        {
            Title = "Multiply",
            Category = NodeCategory.Math,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "A", Type = PinType.Float, IsInput = true, DefaultValue = "1" },
                new() { Name = "B", Type = PinType.Float, IsInput = true, DefaultValue = "1" },
            },
            Outputs = new()
            {
                new() { Name = "Result", Type = PinType.Float, IsInput = false },
            }
        };

        public static ScriptNode MathClamp(double x = 0, double y = 0) => new()
        {
            Title = "Clamp",
            Category = NodeCategory.Math,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "Value", Type = PinType.Float, IsInput = true },
                new() { Name = "Min", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
                new() { Name = "Max", Type = PinType.Float, IsInput = true, DefaultValue = "1" },
            },
            Outputs = new()
            {
                new() { Name = "Result", Type = PinType.Float, IsInput = false },
            }
        };

        public static ScriptNode MathLerp(double x = 0, double y = 0) => new()
        {
            Title = "Lerp",
            Category = NodeCategory.Math,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "A", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
                new() { Name = "B", Type = PinType.Float, IsInput = true, DefaultValue = "1" },
                new() { Name = "Alpha", Type = PinType.Float, IsInput = true, DefaultValue = "0.5" },
            },
            Outputs = new()
            {
                new() { Name = "Result", Type = PinType.Float, IsInput = false },
            }
        };

        public static ScriptNode Branch(double x = 0, double y = 0) => new()
        {
            Title = "Branch",
            Category = NodeCategory.Flow,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "Exec", Type = PinType.Flow, IsInput = true },
                new() { Name = "Condition", Type = PinType.Boolean, IsInput = true },
            },
            Outputs = new()
            {
                new() { Name = "True", Type = PinType.Flow, IsInput = false },
                new() { Name = "False", Type = PinType.Flow, IsInput = false },
            }
        };

        public static ScriptNode GetPosition(double x = 0, double y = 0) => new()
        {
            Title = "Get Position",
            Category = NodeCategory.Transform,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "Target", Type = PinType.Object, IsInput = true },
            },
            Outputs = new()
            {
                new() { Name = "Position", Type = PinType.Vector3, IsInput = false },
            }
        };

        public static ScriptNode SetPosition(double x = 0, double y = 0) => new()
        {
            Title = "Set Position",
            Category = NodeCategory.Transform,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "Exec", Type = PinType.Flow, IsInput = true },
                new() { Name = "Target", Type = PinType.Object, IsInput = true },
                new() { Name = "Position", Type = PinType.Vector3, IsInput = true },
            },
            Outputs = new()
            {
                new() { Name = "Exec", Type = PinType.Flow, IsInput = false },
            }
        };

        public static ScriptNode MakeVector(double x = 0, double y = 0) => new()
        {
            Title = "Make Vector",
            Category = NodeCategory.Vector,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "X", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
                new() { Name = "Y", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
                new() { Name = "Z", Type = PinType.Float, IsInput = true, DefaultValue = "0" },
            },
            Outputs = new()
            {
                new() { Name = "Vector", Type = PinType.Vector3, IsInput = false },
            }
        };

        public static ScriptNode Compare(double x = 0, double y = 0) => new()
        {
            Title = "Compare (>)",
            Category = NodeCategory.Logic,
            X = x, Y = y,
            Inputs = new()
            {
                new() { Name = "A", Type = PinType.Float, IsInput = true },
                new() { Name = "B", Type = PinType.Float, IsInput = true },
            },
            Outputs = new()
            {
                new() { Name = "Result", Type = PinType.Boolean, IsInput = false },
            }
        };
    }
}
