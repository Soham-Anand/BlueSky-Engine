using System;
using System.Collections.Generic;
using System.IO;

namespace BlueSky.Animation.FBX;

public class FbxParser
{
    public List<FbxNode> RootNodes { get; private set; } = new();
    public FbxDocument Document { get; private set; } = new();

    // FBX >= 7500 uses 64-bit node record headers (endOffset, propCount, propListLen are uint64)
    private bool _use64Bit;

    // Size of a null sentinel record (all zeros) that terminates a child list
    private int NullRecordSize => _use64Bit ? 25 : 13;

    public bool Parse(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[FbxParser] File not found: {filePath}");
                return false;
            }

            byte[] data = File.ReadAllBytes(filePath);
            var reader = new FbxBinaryReader(data);

            if (!ParseHeader(reader))
            {
                Console.WriteLine("[FbxParser] Invalid FBX header (not a binary FBX file)");
                return false;
            }

            // Determine if we use 64-bit offsets (FBX >= 7500)
            _use64Bit = Document.Version >= 7500;

            // Parse all top-level nodes until we hit a null sentinel or EOF
            while (reader.Position < reader.Length - NullRecordSize)
            {
                // Peek ahead to check for null sentinel (all zeros)
                if (IsNullSentinel(reader))
                {
                    reader.Skip(NullRecordSize);
                    break;
                }

                var node = ReadNode(reader);
                if (node == null)
                    break;

                RootNodes.Add(node);
            }

            // Populate the Document's RootNodes
            Document.RootNodes = RootNodes;

            if (RootNodes.Count == 0)
            {
                Console.WriteLine("[FbxParser] Warning: No nodes parsed from file");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FbxParser] Exception: {ex.Message}");
            Console.WriteLine($"[FbxParser] Stack: {ex.StackTrace}");
            return false;
        }
    }

    private bool ParseHeader(FbxBinaryReader reader)
    {
        // FBX binary header: "Kaydara FBX Binary  \0" (21 bytes) + 0x1A 0x00 (2 bytes) + version (4 bytes) = 27 bytes
        if (reader.Length < 27)
            return false;

        Span<byte> magic = stackalloc byte[21];
        int bytesRead = reader.Read(magic);

        if (bytesRead != 21)
            return false;

        // Check magic: "Kaydara FBX Binary  \0"
        ReadOnlySpan<byte> expected = "Kaydara FBX Binary  \0"u8;
        if (!magic.SequenceEqual(expected))
            return false;

        // Skip 2 bytes (0x1A, 0x00 - reserved padding)
        reader.Skip(2);

        // Read version (e.g. 7400 = FBX 2014, 7500 = FBX 2016)
        uint version = reader.ReadUInt32();
        Document.Version = version;

        return true;
    }

    /// <summary>
    /// Check if the reader is positioned at a null sentinel record (all zero bytes).
    /// The sentinel size depends on whether we use 32-bit or 64-bit record headers.
    /// </summary>
    private bool IsNullSentinel(FbxBinaryReader reader)
    {
        if (reader.Position + NullRecordSize > reader.Length)
            return true; // Not enough data left, treat as end

        reader.PeekBytes(NullRecordSize, out var sentinel);
        for (int i = 0; i < sentinel.Length; i++)
        {
            if (sentinel[i] != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Read a single FBX node record. Returns null if at a null sentinel or on error.
    /// FBX node record format:
    ///   endOffset    (uint32 or uint64) - ABSOLUTE file offset where this node ends
    ///   propCount    (uint32 or uint64) - number of properties
    ///   propListLen  (uint32 or uint64) - total byte length of all properties
    ///   nameLen      (uint8)            - length of node name
    ///   name         (nameLen bytes)    - node name
    ///   [properties] (propListLen bytes)
    ///   [children]   (until endOffset, terminated by null sentinel)
    /// </summary>
    private FbxNode? ReadNode(FbxBinaryReader reader, FbxNode? parent = null)
    {
        int headerSize = _use64Bit ? 25 : 13; // 3 x (4 or 8) + 1 byte nameLen

        if (reader.Position + headerSize > reader.Length)
            return null;

        long startPos = reader.Position;

        // Read endOffset - this is an ABSOLUTE file position (not relative!)
        long endOffset;
        long propCount;
        long propListLen;

        if (_use64Bit)
        {
            endOffset = reader.ReadInt64();
            propCount = reader.ReadInt64();
            propListLen = reader.ReadInt64();
        }
        else
        {
            endOffset = reader.ReadUInt32();
            propCount = reader.ReadUInt32();
            propListLen = reader.ReadUInt32();
        }

        // Null sentinel: endOffset == 0 means end of children
        if (endOffset == 0)
            return null;

        // Validate endOffset (it's absolute, must be within file bounds)
        if (endOffset > reader.Length)
        {
            Console.WriteLine($"[FbxParser] Warning: endOffset {endOffset} (0x{endOffset:X}) exceeds file size {reader.Length} (0x{reader.Length:X}) at pos 0x{startPos:X}");
            // Try to recover by seeking back and skipping
            reader.Position = (int)startPos;
            return null;
        }

        // Read name
        byte nameLen = reader.ReadByte();
        if (reader.Position + nameLen > reader.Length)
        {
            reader.Position = (int)startPos;
            return null;
        }
        string name = reader.ReadString(nameLen);

        // Decode properties
        long propsStartPos = reader.Position;
        var properties = new List<FbxProperty>();
        var decoder = new FbxPropertyDecoder(reader);

        for (long i = 0; i < propCount; i++)
        {
            // Safety: don't read past the property list boundary
            if (reader.Position >= propsStartPos + propListLen || reader.Position >= reader.Length)
                break;

            try
            {
                var prop = decoder.DecodeProperty();
                if (prop != null)
                    properties.Add(prop);
                else
                    break;
            }
            catch (Exception)
            {
                // If property decoding fails, skip to end of property list
                break;
            }
        }

        // Ensure reader is positioned right after the property list
        long expectedPropEnd = propsStartPos + propListLen;
        if (reader.Position != expectedPropEnd && expectedPropEnd <= reader.Length)
        {
            reader.Position = (int)expectedPropEnd;
        }

        var node = new FbxNode
        {
            Name = name,
            Properties = properties,
            Children = new List<FbxNode>(),
            Parent = parent
        };

        // Parse children - they exist between current position and endOffset (absolute)
        // Children are terminated by a null sentinel record
        while (reader.Position < endOffset && reader.Position < reader.Length)
        {
            // Check for null sentinel marking end of children
            if (IsNullSentinel(reader))
            {
                reader.Skip(NullRecordSize);
                break;
            }

            var child = ReadNode(reader, node);
            if (child == null)
                break;
            node.Children.Add(child);
        }

        // Ensure we're positioned at endOffset (absolute) for the next sibling
        if (endOffset <= reader.Length && reader.Position != endOffset)
        {
            reader.Position = (int)endOffset;
        }

        return node;
    }
}

public class FbxDocument
{
    public uint Version { get; set; }
    public List<FbxNode> RootNodes { get; set; } = new();

    public FbxNode? FindNode(string name)
    {
        // First check if any root node has this name directly
        foreach (var root in RootNodes)
        {
            if (root.Name == name)
                return root;
        }

        // Then search recursively
        foreach (var root in RootNodes)
        {
            var found = FindNodeRecursive(root, name);
            if (found != null)
                return found;
        }
        return null;
    }

    public List<FbxNode> FindAllNodes(string name)
    {
        var results = new List<FbxNode>();
        foreach (var root in RootNodes)
        {
            if (root.Name == name)
                results.Add(root);
            FindAllNodesRecursive(root, name, results);
        }
        return results;
    }

    private FbxNode? FindNodeRecursive(FbxNode node, string name)
    {
        foreach (var child in node.Children)
        {
            if (child.Name == name)
                return child;

            var found = FindNodeRecursive(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private void FindAllNodesRecursive(FbxNode node, string name, List<FbxNode> results)
    {
        foreach (var child in node.Children)
        {
            if (child.Name == name)
                results.Add(child);
            FindAllNodesRecursive(child, name, results);
        }
    }
}
