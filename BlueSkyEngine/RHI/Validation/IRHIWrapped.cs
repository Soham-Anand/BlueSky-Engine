namespace NotBSRenderer;

public interface IRHIWrapped<out T>
{
    T Inner { get; }
}
