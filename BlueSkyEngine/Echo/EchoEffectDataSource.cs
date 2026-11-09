using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Miniaudio;

namespace BlueSky.Echo;

/// <summary>
/// A custom miniaudio data source that reads an Echo Source's canonical PCM
/// and runs the Super's live effect chain in the audio callback.
///
/// miniaudio's stock ma_sound path can play files, but it cannot apply a
/// per-voice C# effect rack. Registering this data source keeps the existing
/// engine/device/mixer while giving each Super voice its own DSP.
/// </summary>
internal sealed unsafe class EchoEffectDataSource : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Context
    {
        public ma_data_source_base Base;
        public IntPtr GcHandle;
    }

    private const int ScratchFrames = 2048;

    private readonly float[] _pcm;
    private readonly long _frameCount;
    private readonly int _sampleRate;
    private readonly EchoEffectChain _chain;
    private readonly float[] _scratch = new float[ScratchFrames * 2];

    private IntPtr _context;
    private IntPtr _vtable;
    private GCHandle _self;
    private long _cursor;
    private long _playStart;
    private long _playEnd;
    private long _loopStart;
    private long _loopEnd;
    private bool _looping;
    private bool _disposed;

    public IntPtr NativePointer => _context;

    public EchoEffectDataSource(EchoSourceAsset source, EchoSuperAsset? super, bool loop)
    {
        _pcm = source.Pcm ?? Array.Empty<float>();
        _frameCount = _pcm.Length / 2;
        _sampleRate = Math.Max(8000, source.SampleRate);

        _playStart = Math.Clamp(source.EffectiveTrimStart, 0, _frameCount);
        _playEnd = Math.Clamp(source.EffectiveTrimEnd, _playStart, _frameCount);

        _loopStart = Math.Clamp(source.EffectiveLoopStart, _playStart, _playEnd);
        _loopEnd = Math.Clamp(source.EffectiveLoopEnd, _loopStart, _playEnd);
        if (_loopEnd <= _loopStart)
        {
            _loopStart = _playStart;
            _loopEnd = _playEnd;
        }

        _cursor = _playStart;
        _looping = loop;
        _chain = new EchoEffectChain(_sampleRate, 2);
        _chain.PostSuper(super);

        _self = GCHandle.Alloc(this, GCHandleType.Normal);
        AllocateNative();
    }

    public void PostSuper(EchoSuperAsset? super) => _chain.PostSuper(super);

    private void AllocateNative()
    {
        _vtable = Marshal.AllocHGlobal(Marshal.SizeOf<ma_data_source_vtable>());
        var vtable = default(ma_data_source_vtable);
        vtable.onRead = &OnRead;
        vtable.onSeek = &OnSeek;
        vtable.onGetDataFormat = &OnGetDataFormat;
        vtable.onGetCursor = &OnGetCursor;
        vtable.onGetLength = &OnGetLength;
        vtable.onSetLooping = &OnSetLooping;
        vtable.flags = 0;
        Marshal.StructureToPtr(vtable, _vtable, false);

        _context = Marshal.AllocHGlobal(Marshal.SizeOf<Context>());
        var context = (Context*)_context;
        context->Base = default;
        context->Base.vtable = (ma_data_source_vtable*)_vtable;
        context->Base.rangeBegInFrames = (ulong)Math.Max(0, _playStart);
        context->Base.rangeEndInFrames = (ulong)Math.Max(0, _playEnd);
        context->Base.loopBegInFrames = (ulong)Math.Max(0, _loopStart);
        context->Base.loopEndInFrames = (ulong)Math.Max(0, _loopEnd);
        context->Base.isLooping = _looping ? 1u : 0u;
        context->GcHandle = GCHandle.ToIntPtr(_self);
    }

    private static EchoEffectDataSource? FromUserData(void* pUserData)
    {
        if (pUserData == null)
            return null;

        var context = (Context*)pUserData;
        if (context->GcHandle == IntPtr.Zero)
            return null;

        return GCHandle.FromIntPtr(context->GcHandle).Target as EchoEffectDataSource;
    }

    private int Read(float* destination, ulong requestedFrames)
    {
        if (_frameCount <= 0 || requestedFrames == 0)
            return 0;

        int frames = (int)Math.Min(requestedFrames, (ulong)(int.MaxValue / 4));
        int written = 0;

        while (written < frames)
        {
            int chunk = Math.Min(frames - written, ScratchFrames);
            int copied = CopyFrames(_scratch, chunk);
            if (copied <= 0)
                break;

            _chain.Process(_scratch, copied, 2);

            for (int i = 0; i < copied * 2; i++)
                destination[written * 2 + i] = _scratch[i];

            written += copied;
            if (copied < chunk && !_looping)
                break;
        }

        return written;
    }

    private int CopyFrames(float[] destination, int frameCount)
    {
        int copied = 0;
        long end = _looping ? _loopEnd : _playEnd;

        while (copied < frameCount)
        {
            if (_cursor >= end)
            {
                if (_looping)
                    _cursor = _loopStart;
                else
                    break;
            }

            int available = (int)Math.Min(frameCount - copied, end - _cursor);
            if (available <= 0)
                break;

            Array.Copy(_pcm, _cursor * 2, destination, copied * 2, available * 2);
            _cursor += available;
            copied += available;

            if (_cursor >= end && !_looping)
                break;
        }

        return copied;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ma_result OnRead(void* pDataSource, void* pFramesOut, ulong frameCount, ulong* pFramesRead)
    {
        try
        {
            var source = FromUserData(pDataSource);
            if (source == null || pFramesOut == null)
            {
                if (pFramesRead != null) *pFramesRead = 0;
                return ma_result.MA_ERROR;
            }

            int read = source.Read((float*)pFramesOut, frameCount);
            if (pFramesRead != null) *pFramesRead = (ulong)read;
            return ma_result.MA_SUCCESS;
        }
        catch
        {
            if (pFramesRead != null) *pFramesRead = 0;
            return ma_result.MA_ERROR;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ma_result OnSeek(void* pDataSource, ulong frameIndex)
    {
        var source = FromUserData(pDataSource);
        if (source == null)
            return ma_result.MA_ERROR;

        source._cursor = (long)Math.Min(frameIndex, (ulong)Math.Max(0, source._frameCount));
        source._chain.Reset();
        return ma_result.MA_SUCCESS;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ma_result OnGetDataFormat(void* pDataSource, ma_format* pFormat, uint* pChannels,
        uint* pSampleRate, byte* pChannelMap, nuint channelMapCap)
    {
        var source = FromUserData(pDataSource);
        if (source == null)
            return ma_result.MA_ERROR;

        if (pFormat != null) *pFormat = ma_format.ma_format_f32;
        if (pChannels != null) *pChannels = 2;
        if (pSampleRate != null) *pSampleRate = (uint)source._sampleRate;
        return ma_result.MA_SUCCESS;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ma_result OnGetCursor(void* pDataSource, ulong* pCursor)
    {
        var source = FromUserData(pDataSource);
        if (source == null || pCursor == null)
            return ma_result.MA_ERROR;

        *pCursor = (ulong)Math.Max(0, source._cursor);
        return ma_result.MA_SUCCESS;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ma_result OnGetLength(void* pDataSource, ulong* pLength)
    {
        var source = FromUserData(pDataSource);
        if (source == null || pLength == null)
            return ma_result.MA_ERROR;

        *pLength = (ulong)Math.Max(0, source._frameCount);
        return ma_result.MA_SUCCESS;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ma_result OnSetLooping(void* pDataSource, uint isLooping)
    {
        var source = FromUserData(pDataSource);
        if (source == null)
            return ma_result.MA_ERROR;

        source._looping = isLooping != 0;
        return ma_result.MA_SUCCESS;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_context != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_context);
            _context = IntPtr.Zero;
        }
        if (_vtable != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_vtable);
            _vtable = IntPtr.Zero;
        }
        if (_self.IsAllocated)
            _self.Free();
    }
}
