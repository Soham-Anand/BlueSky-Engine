using System;
using System.Collections.Generic;

namespace BlueSky.Animation.FBX;

public enum FbxPropertyType : byte
{
    Int16 = (byte)'Y',
    Bool = (byte)'C',
    Int32 = (byte)'I',
    Float = (byte)'F',
    Double = (byte)'D',
    Int64 = (byte)'L',
    String = (byte)'S',
    Binary = (byte)'R',
    FloatArray = (byte)'f',
    DoubleArray = (byte)'d',
    Int64Array = (byte)'l',
    Int32Array = (byte)'i',
    BoolArray = (byte)'b',
}

public abstract class FbxProperty
{
    public FbxPropertyType Type { get; protected set; }

    public static FbxProperty CreateScalar(short value) => new ScalarProperty<short>(FbxPropertyType.Int16, value);
    public static FbxProperty CreateScalar(bool value) => new ScalarProperty<bool>(FbxPropertyType.Bool, value);
    public static FbxProperty CreateScalar(int value) => new ScalarProperty<int>(FbxPropertyType.Int32, value);
    public static FbxProperty CreateScalar(float value) => new ScalarProperty<float>(FbxPropertyType.Float, value);
    public static FbxProperty CreateScalar(double value) => new ScalarProperty<double>(FbxPropertyType.Double, value);
    public static FbxProperty CreateScalar(long value) => new ScalarProperty<long>(FbxPropertyType.Int64, value);
    public static FbxProperty CreateString(string value) => new StringProperty(value);
    public static FbxProperty CreateBinary(byte[] value) => new BinaryProperty(value);
    public static FbxProperty CreateArray(float[] value) => new ArrayProperty<float>(FbxPropertyType.FloatArray, value);
    public static FbxProperty CreateArray(double[] value) => new ArrayProperty<double>(FbxPropertyType.DoubleArray, value);
    public static FbxProperty CreateArray(long[] value) => new ArrayProperty<long>(FbxPropertyType.Int64Array, value);
    public static FbxProperty CreateArray(int[] value) => new ArrayProperty<int>(FbxPropertyType.Int32Array, value);
    public static FbxProperty CreateArray(bool[] value) => new ArrayProperty<bool>(FbxPropertyType.BoolArray, value);

    public abstract object? AsObject();
}

public class ScalarProperty<T> : FbxProperty where T : unmanaged
{
    public T Value { get; }

    public ScalarProperty(FbxPropertyType type, T value)
    {
        Type = type;
        Value = value;
    }

    public override object? AsObject() => Value;
}

public class StringProperty : FbxProperty
{
    public string Value { get; }

    public StringProperty(string value)
    {
        Type = FbxPropertyType.String;
        Value = value;
    }

    public override object? AsObject() => Value;
}

public class BinaryProperty : FbxProperty
{
    public byte[] Value { get; }

    public BinaryProperty(byte[] value)
    {
        Type = FbxPropertyType.Binary;
        Value = value;
    }

    public override object? AsObject() => Value;
}

public class ArrayProperty<T> : FbxProperty where T : unmanaged
{
    public T[] Value { get; }

    public ArrayProperty(FbxPropertyType type, T[] value)
    {
        Type = type;
        Value = value;
    }

    public override object? AsObject() => Value;
}
