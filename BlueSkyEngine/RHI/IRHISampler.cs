namespace BlueSky.BSR.RHI;

public interface IRHISampler : IDisposable
{
    SamplerDesc Desc { get; }
}
