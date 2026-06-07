using System;
using System.IO.Compression;

namespace BlueSky.Animation.FBX;

public class FbxPropertyDecoder
{
    private readonly FbxBinaryReader _reader;

    public FbxPropertyDecoder(FbxBinaryReader reader)
    {
        _reader = reader;
    }

    public FbxProperty? DecodeProperty()
    {
        if (_reader.EndOfStream)
            return null;

        try
        {
            byte typeCode = _reader.ReadByte();

            return typeCode switch
            {
                (byte)'Y' => FbxProperty.CreateScalar(_reader.ReadInt16()),
                (byte)'C' => FbxProperty.CreateScalar(_reader.ReadByte() != 0),
                (byte)'I' => FbxProperty.CreateScalar(_reader.ReadInt32()),
                (byte)'F' => FbxProperty.CreateScalar(_reader.ReadSingle()),
                (byte)'D' => FbxProperty.CreateScalar(_reader.ReadDouble()),
                (byte)'L' => FbxProperty.CreateScalar(_reader.ReadInt64()),
                (byte)'S' => DecodeString(),
                (byte)'R' => DecodeBinary(),
                (byte)'f' => DecodeFloatArray(),
                (byte)'d' => DecodeDoubleArray(),
                (byte)'l' => DecodeInt64Array(),
                (byte)'i' => DecodeInt32Array(),
                (byte)'b' => DecodeBoolArray(),
                _ => null,
            };
        }
        catch
        {
            // If property decoding fails, return null and continue
            return null;
        }
    }

    private FbxProperty DecodeString()
    {
        uint length = _reader.ReadUInt32();
        if (length > 1000000)
            return FbxProperty.CreateString("");

        string str = _reader.ReadString((int)length);
        return FbxProperty.CreateString(str);
    }

    private FbxProperty DecodeBinary()
    {
        uint length = _reader.ReadUInt32();
        if (length > 10000000)
            return FbxProperty.CreateBinary(Array.Empty<byte>());

        byte[] data = _reader.ReadBytes((int)length);
        return FbxProperty.CreateBinary(data);
    }

    private FbxProperty DecodeFloatArray()
    {
        uint arrayLength = _reader.ReadUInt32();
        uint encoding = _reader.ReadUInt32();
        uint compressedLength = _reader.ReadUInt32();

        if (arrayLength > 10000000 || compressedLength > 100000000)
            return FbxProperty.CreateArray(Array.Empty<float>());

        float[] array = new float[arrayLength];

        if (encoding == 0)
        {
            for (int i = 0; i < arrayLength; i++)
                array[i] = _reader.ReadSingle();
        }
        else if (encoding == 1)
        {
            byte[] compressed = _reader.ReadBytes((int)compressedLength);
            byte[] decompressed = DecompressZlib(compressed);

            if (decompressed.Length >= arrayLength * 4)
            {
                for (int i = 0; i < arrayLength; i++)
                {
                    int offset = i * 4;
                    array[i] = BitConverter.ToSingle(decompressed, offset);
                }
            }
        }

        return FbxProperty.CreateArray(array);
    }

    private FbxProperty DecodeDoubleArray()
    {
        uint arrayLength = _reader.ReadUInt32();
        uint encoding = _reader.ReadUInt32();
        uint compressedLength = _reader.ReadUInt32();

        if (arrayLength > 10000000 || compressedLength > 100000000)
            return FbxProperty.CreateArray(Array.Empty<double>());

        double[] array = new double[arrayLength];

        if (encoding == 0)
        {
            for (int i = 0; i < arrayLength; i++)
                array[i] = _reader.ReadDouble();
        }
        else if (encoding == 1)
        {
            byte[] compressed = _reader.ReadBytes((int)compressedLength);
            byte[] decompressed = DecompressZlib(compressed);

            if (decompressed.Length >= arrayLength * 8)
            {
                for (int i = 0; i < arrayLength; i++)
                {
                    int offset = i * 8;
                    array[i] = BitConverter.ToDouble(decompressed, offset);
                }
            }
        }

        return FbxProperty.CreateArray(array);
    }

    private FbxProperty DecodeInt64Array()
    {
        uint arrayLength = _reader.ReadUInt32();
        uint encoding = _reader.ReadUInt32();
        uint compressedLength = _reader.ReadUInt32();

        if (arrayLength > 10000000 || compressedLength > 100000000)
            return FbxProperty.CreateArray(Array.Empty<long>());

        long[] array = new long[arrayLength];

        if (encoding == 0)
        {
            for (int i = 0; i < arrayLength; i++)
                array[i] = _reader.ReadInt64();
        }
        else if (encoding == 1)
        {
            byte[] compressed = _reader.ReadBytes((int)compressedLength);
            byte[] decompressed = DecompressZlib(compressed);

            if (decompressed.Length >= arrayLength * 8)
            {
                for (int i = 0; i < arrayLength; i++)
                {
                    int offset = i * 8;
                    array[i] = BitConverter.ToInt64(decompressed, offset);
                }
            }
        }

        return FbxProperty.CreateArray(array);
    }

    private FbxProperty DecodeInt32Array()
    {
        uint arrayLength = _reader.ReadUInt32();
        uint encoding = _reader.ReadUInt32();
        uint compressedLength = _reader.ReadUInt32();

        if (arrayLength > 10000000 || compressedLength > 100000000)
            return FbxProperty.CreateArray(Array.Empty<int>());

        int[] array = new int[arrayLength];

        if (encoding == 0)
        {
            for (int i = 0; i < arrayLength; i++)
                array[i] = _reader.ReadInt32();
        }
        else if (encoding == 1)
        {
            byte[] compressed = _reader.ReadBytes((int)compressedLength);
            byte[] decompressed = DecompressZlib(compressed);

            if (decompressed.Length >= arrayLength * 4)
            {
                for (int i = 0; i < arrayLength; i++)
                {
                    int offset = i * 4;
                    array[i] = BitConverter.ToInt32(decompressed, offset);
                }
            }
        }

        return FbxProperty.CreateArray(array);
    }

    private FbxProperty DecodeBoolArray()
    {
        uint arrayLength = _reader.ReadUInt32();
        uint encoding = _reader.ReadUInt32();
        uint compressedLength = _reader.ReadUInt32();

        if (arrayLength > 10000000 || compressedLength > 100000000)
            return FbxProperty.CreateArray(Array.Empty<bool>());

        bool[] array = new bool[arrayLength];

        if (encoding == 0)
        {
            for (int i = 0; i < arrayLength; i++)
                array[i] = _reader.ReadByte() != 0;
        }
        else if (encoding == 1)
        {
            byte[] compressed = _reader.ReadBytes((int)compressedLength);
            byte[] decompressed = DecompressZlib(compressed);

            for (int i = 0; i < arrayLength && i < decompressed.Length; i++)
                array[i] = decompressed[i] != 0;
        }

        return FbxProperty.CreateArray(array);
    }

    private byte[] DecompressZlib(byte[] compressed)
    {
        if (compressed.Length < 2)
            return Array.Empty<byte>();

        try
        {
            // FBX uses zlib compression which has a 2-byte header (CMF + FLG)
            // before the raw deflate stream. DeflateStream expects raw deflate,
            // so we skip the zlib header.
            // CMF byte: bits 0-3 = method (8=deflate), bits 4-7 = window size
            // FLG byte: check bits
            int offset = 0;
            if ((compressed[0] & 0x0F) == 8) // zlib header detected (method = deflate)
            {
                offset = 2; // Skip CMF + FLG bytes
            }

            using var input = new System.IO.MemoryStream(compressed, offset, compressed.Length - offset);
            using var output = new System.IO.MemoryStream();
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            // Fallback: try without skipping header (in case it's already raw deflate)
            try
            {
                using var input = new System.IO.MemoryStream(compressed);
                using var output = new System.IO.MemoryStream();
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                deflate.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }
    }
}
