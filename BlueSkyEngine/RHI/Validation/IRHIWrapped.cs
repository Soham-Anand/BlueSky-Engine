namespace BlueSky.BSR.RHI;

public interface IRHIWrapped<out T>
{
    T Inner { get; }
}
