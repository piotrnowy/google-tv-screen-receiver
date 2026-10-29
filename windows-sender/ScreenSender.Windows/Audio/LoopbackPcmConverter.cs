using NAudio.Wave;

namespace ScreenSender.Windows.Audio;

/// <summary>NAudio/Media Foundation conversion from the real endpoint format to PCM16/48k/stereo.</summary>
public sealed class LoopbackPcmConverter : IDisposable
{
    private readonly object _gate = new();
    private readonly BufferedWaveProvider _buffer;
    private readonly MediaFoundationResampler _resampler;
    private readonly byte[] _silence;
    private readonly byte[] _discard;
    private long _droppedInputBytes;
    public int BufferedBytes { get { lock (_gate) return _buffer.BufferedBytes; } }
    public TimeSpan BufferedDuration { get { lock (_gate) return _buffer.BufferedDuration; } }
    public long DroppedInputBytes { get { lock (_gate) return _droppedInputBytes; } }

    public LoopbackPcmConverter(WaveFormat input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _buffer = new BufferedWaveProvider(input, TimeSpan.FromMilliseconds(200))
        {
            ReadFully = true, // Windows loopback can stop delivering callbacks when the endpoint is silent.
            DiscardOnBufferOverflow = false,
        };
        int chunkBytes = Math.Max(input.BlockAlign, input.SampleRate * 20 / 1000 * input.BlockAlign);
        _silence = new byte[chunkBytes];
        _discard = new byte[chunkBytes];
        _resampler = new MediaFoundationResampler(new ChunkedProvider(_buffer, chunkBytes),
            new WaveFormat(Pcm20MsFramer.SampleRate, 16, Pcm20MsFramer.Channels)) { ResamplerQuality = 30 };
    }

    public void AddSamples(ReadOnlySpan<byte> input, bool silent = false)
    {
        if (input.Length % _buffer.WaveFormat.BlockAlign != 0)
            throw new InvalidDataException("WASAPI callback ended inside a PCM sample frame.");
        lock (_gate)
        {
            int capacity = _buffer.BufferLength / _buffer.WaveFormat.BlockAlign * _buffer.WaveFormat.BlockAlign;
            if (input.Length > capacity)
            {
                _droppedInputBytes += input.Length - capacity;
                input = input[^capacity..]; // Keep the newest complete source sample frames.
            }
            int overflow = Math.Max(0, _buffer.BufferedBytes + input.Length - capacity);
            int alignment = _buffer.WaveFormat.BlockAlign;
            int discard = (overflow + alignment - 1) / alignment * alignment;
            while (discard > 0)
            {
                int count = Math.Min(discard, _discard.Length);
                int removed = _buffer.Read(_discard.AsSpan(0, count));
                _droppedInputBytes += removed;
                discard -= removed;
            }
            if (!silent) _buffer.AddSamples(input);
            else for (int remaining = input.Length; remaining > 0;)
            {
                int count = Math.Min(remaining, _silence.Length);
                _buffer.AddSamples(_silence.AsSpan(0, count));
                remaining -= count;
            }
        }
    }

    public int Read(Span<byte> pcm16)
    {
        lock (_gate) return _resampler.Read(pcm16);
    }

    public void Dispose() { lock (_gate) _resampler.Dispose(); }

    // Limit each transform input request to 20 ms. A large read-ahead request must
    // not pre-fill hundreds of milliseconds of synthetic silence ahead of live audio.
    private sealed class ChunkedProvider(BufferedWaveProvider input, int maximumBytes) : IWaveProvider
    {
        public WaveFormat WaveFormat => input.WaveFormat;
        public int Read(Span<byte> buffer) => input.Read(buffer[..Math.Min(buffer.Length, maximumBytes)]);
    }
}
