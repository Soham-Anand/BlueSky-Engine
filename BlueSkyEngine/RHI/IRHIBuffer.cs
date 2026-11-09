namespace BlueSky.BSR.RHI;

public interface IRHIBuffer : IDisposable
{
    ulong Size { get; }
    BufferUsage Usage { get; }
    MemoryType MemoryType { get; }
}
