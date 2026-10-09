namespace BlueSky.Rendering.RHI;

public interface IRHIWrapped<out T>
{
    T Inner { get; }
}
