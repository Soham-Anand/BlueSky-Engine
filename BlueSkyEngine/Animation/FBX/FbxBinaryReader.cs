using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BlueSky.Animation.FBX;

public class FbxBinaryReader
{
    private readonly byte[] _data;
    public byte[] Data => _data;
    private int _position;
    private readonly bool _use64BitOffsets;

    public int Position 
    { 
        get => _position;
        set => _position = value;
    }
    public int Length => _data.Length;
    public bool EndOfStream => _position >= _data.Length;

    public FbxBinaryReader(byte[] data, bool use64BitOffsets = false)
    {
        _data = data;
        _position = 0;
        _use64BitOffsets = use64BitOffsets;
    }

    public int Read(Span<byte> buffer)
    {
        int count = Math.Min(buffer.Length, _data.Length - _position);
        if (count > 0)
        {
            _data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
        }
        return count;
    }

    public void Seek(int offset)
    {
        if (offset < 0 || offset > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _position = offset;
    }

    public void Skip(int count)
    {
        _position += count;
        if (_position > _data.Length)
            throw new EndOfStreamException();
    }

    public byte ReadByte()
    {
        if (_position >= _data.Length)
            throw new EndOfStreamException();
        return _data[_position++];
    }

    public short ReadInt16()
    {
        if (_position + 2 > _data.Length)
            throw new EndOfStreamException();
        short value = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_position));
        _position += 2;
        return value;
    }

    public int ReadInt32()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException();
        int value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    public long ReadInt64()
    {
        if (_position + 8 > _data.Length)
            throw new EndOfStreamException();
        long value = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(_position));
        _position += 8;
        return value;
    }

    public uint ReadUInt32()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException();
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    public float ReadSingle()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException();
        float value = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    public double ReadDouble()
    {
        if (_position + 8 > _data.Length)
            throw new EndOfStreamException();
        double value = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(_position));
        _position += 8;
        return value;
    }

    public uint ReadOffset()
    {
        return _use64BitOffsets ? (uint)ReadInt64() : ReadUInt32();
    }

    public byte[] ReadBytes(int count)
    {
        if (_position + count > _data.Length)
            throw new EndOfStreamException();
        byte[] result = new byte[count];
        Array.Copy(_data, _position, result, 0, count);
        _position += count;
        return result;
    }

    public ReadOnlySpan<byte> ReadBytesSpan(int count)
    {
        if (_position + count > _data.Length)
            throw new EndOfStreamException();
        ReadOnlySpan<byte> result = _data.AsSpan(_position, count);
        _position += count;
        return result;
    }

    public string ReadString(int length)
    {
        if (_position + length > _data.Length)
            throw new EndOfStreamException();
        string result = System.Text.Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        return result;
    }

    public T[] ReadArray<T>(int count) where T : unmanaged
    {
        int elementSize = Marshal.SizeOf<T>();
        int totalBytes = count * elementSize;
        if (_position + totalBytes > _data.Length)
            throw new EndOfStreamException();

        T[] result = new T[count];
        Marshal.Copy(_data, _position, Marshal.UnsafeAddrOfPinnedArrayElement(result, 0), totalBytes);
        _position += totalBytes;
        return result;
    }

    public void PeekBytes(int count, out ReadOnlySpan<byte> span)
    {
        if (_position + count > _data.Length)
            throw new EndOfStreamException();
        span = _data.AsSpan(_position, count);
    }
}
