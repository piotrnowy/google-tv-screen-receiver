namespace ScreenSender.Windows.Audio;

/// <summary>Accumulate arbitrary converter reads into complete interleaved PCM16 stereo frames.</summary>
public sealed class Pcm20MsFramer
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int PacketMilliseconds = 20;
    public const int SamplesPerChannel = SampleRate * PacketMilliseconds / 1000;
    public const int TotalSamples = SamplesPerChannel * Channels;
    public const int FrameBytes = TotalSamples * sizeof(short);
    private readonly byte[] _frame = new byte[FrameBytes];
    private int _used;

    // The callback must consume this borrowed buffer synchronously before returning.
    public void Append(ReadOnlySpan<byte> pcm, Action<ReadOnlyMemory<byte>> onFrame)
    {
        while (!pcm.IsEmpty)
        {
            int count = Math.Min(pcm.Length, FrameBytes - _used);
            pcm[..count].CopyTo(_frame.AsSpan(_used));
            _used += count;
            pcm = pcm[count..];
            if (_used != FrameBytes) continue;
            _used = 0;
            onFrame(_frame);
        }
    }
}
