using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.Audio;

public sealed class SystemAudioLoopbackSource(bool debug = false) : ISystemAudioSource, IAsyncDisposable
{
    private readonly TaskCompletionSource<string> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstPacket = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Pcm20MsFramer _framer = new();
    private readonly OpusStereoEncoder _encoder = new();
    private readonly AudioSignalMeter _rawLevels = new();
    private readonly AudioSignalMeter _pcmLevels = new();
    private static readonly WaveFormat PcmFormat = new(48000, 16, 2);
    private MMDevice? _device;
    private WasapiRecorder? _capture;
    private LoopbackPcmConverter? _converter;
    private CancellationTokenSource? _running;
    private Task? _worker;
    private bool _negotiated;
    private volatile bool _stopping;
    private long _capturedBytes;
    private long _captureCallbacks;
    private long _opusFrames;
    private long _nonSilentCallbacks;
    private long _nonSilentPcmFrames;
    private int _lastPacketBytes;
    private int _signalReported;
    private long _lastDiagnostic;
    private int _disposed;

    public event Action<uint, byte[]>? EncodedFrame;
    public AudioFormat Format => OpusStereoEncoder.Format;
    public Task<string> Failure => _failure.Task;
    public string RenderEndpointId => _device?.ID ?? throw new InvalidOperationException("System audio endpoint is not initialized.");
    public AudioOutputActivity OutputActivity => new(Interlocked.Read(ref _nonSilentCallbacks), Interlocked.Read(ref _nonSilentPcmFrames), Interlocked.Read(ref _opusFrames), 0);
    public (long Callbacks, long Bytes, long NonSilentCallbacks, long OpusFrames, long NonSilentPcmFrames) Statistics =>
        (Interlocked.Read(ref _captureCallbacks), Interlocked.Read(ref _capturedBytes), Interlocked.Read(ref _nonSilentCallbacks),
            Interlocked.Read(ref _opusFrames), Interlocked.Read(ref _nonSilentPcmFrames));

    public Task InitializeAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var devices = new MMDeviceEnumerator();
        _device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiRecorderBuilder().WithDevice(_device).WithSharedMode()
            .WithLoopbackCapture().WithBufferLength(20).Build();
        _converter = new LoopbackPcmConverter(_capture.WaveFormat);
        _capture.DataAvailable += OnCaptured;
        _capture.RecordingStopped += OnStopped;
        Console.WriteLine("[Audio] System audio capture enabled");
        Console.WriteLine($"[Audio] Device: {_device.FriendlyName}");
        Console.WriteLine($"[Audio] Render endpoint ID: {_device.ID}");
        Console.WriteLine($"[Audio] Source format: {_capture.WaveFormat} (encoding={_capture.WaveFormat.Encoding}, channels={_capture.WaveFormat.Channels}, bits={_capture.WaveFormat.BitsPerSample})");
        Console.WriteLine("[Audio] WebRTC format: Opus 48000 Hz stereo");
        Console.WriteLine("[Audio] Packet duration: 20 ms");
        Console.WriteLine("[Audio] PCM frame: 960 samples/channel, 2 channels, 20 ms");
        Console.WriteLine("[Audio] Converted frame bytes: 3840");
    }, token);

    public void SetFormat(AudioFormat format)
    {
        if (format.Codec != AudioCodecsEnum.OPUS || format.RtpClockRate != 48000 || format.ChannelCount != 2)
            throw new InvalidDataException("Receiver must negotiate Opus/48000/2 for system audio.");
        _negotiated = true;
    }

    public async Task StartAsync(CancellationToken token)
    {
        await _lifecycle.WaitAsync(token);
        try
        {
            if (_worker is not null) throw new InvalidOperationException("System audio has already started.");
            if (!_negotiated) throw new InvalidOperationException("Opus must be negotiated before starting system audio.");
            if (_stopping) throw new InvalidOperationException("System audio is stopping.");
            if (EncodedFrame is null) throw new InvalidOperationException("Audio source has no encoded-frame subscriber; pc.SendAudio is not wired.");
            var capture = _capture ?? throw new InvalidOperationException("System audio is not initialized.");
            _running = CancellationTokenSource.CreateLinkedTokenSource(token);
            capture.StartRecording();
            _lastDiagnostic = Stopwatch.GetTimestamp();
            _worker = Task.Run(() => EncodeLoopAsync(_running.Token));
        }
        finally { _lifecycle.Release(); }
        await _firstPacket.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        Console.WriteLine("[Audio] Capture/encoder ready; verify capture levels and Audio RTP counters");
    }

    private void OnCaptured(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_stopping) return;
        try
        {
            Interlocked.Increment(ref _captureCallbacks);
            Interlocked.Add(ref _capturedBytes, data.Length);
            if (!_rawLevels.Add(data, _capture!.WaveFormat, (flags & AudioClientBufferFlags.Silent) != 0).Silent)
                Interlocked.Increment(ref _nonSilentCallbacks);
            _converter!.AddSamples(data, (flags & AudioClientBufferFlags.Silent) != 0);
        }
        catch (Exception error) { _failure.TrySetResult($"System audio capture/conversion failed: {error.Message}"); }
    }

    private void OnStopped(object? sender, StoppedEventArgs args)
    {
        if (!_stopping) _failure.TrySetResult($"WASAPI loopback stopped: {args.Exception?.Message ?? "unexpected end of capture"}");
    }

    private async Task EncodeLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Pcm20MsFramer.PacketMilliseconds));
        byte[] pcm = new byte[Pcm20MsFramer.FrameBytes];
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (_failure.Task.IsCompleted) return;
                // PeriodicTimer coalesces missed ticks. Drain a small backlog after a
                // scheduling delay rather than accumulating latency until capture overflows.
                for (int catchUp = 0; catchUp < 4; catchUp++)
                {
                    int count = _converter!.Read(pcm);
                    if (count == 0) throw new InvalidDataException("System audio converter stopped producing PCM.");
                    _framer.Append(pcm.AsSpan(0, count), SendFrame);
                    if (_converter.BufferedDuration < TimeSpan.FromMilliseconds(60)) break;
                }
                if (Stopwatch.GetElapsedTime(_lastDiagnostic) >= TimeSpan.FromSeconds(1)) LogDiagnostics();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { _failure.TrySetResult($"System audio encoding failed: {error.Message}"); }
    }

    private void SendFrame(ReadOnlyMemory<byte> pcm)
    {
        bool hasSignal = !_pcmLevels.Add(pcm.Span, PcmFormat).Silent;
        if (hasSignal) Interlocked.Increment(ref _nonSilentPcmFrames);
        byte[] packet = _encoder.Encode(pcm.Span);
        Interlocked.Increment(ref _opusFrames);
        Volatile.Write(ref _lastPacketBytes, packet.Length);
        EncodedFrame!.Invoke(Pcm20MsFramer.SamplesPerChannel, packet);
        _firstPacket.TrySetResult();
        if (Interlocked.Read(ref _nonSilentCallbacks) > 0 && hasSignal && Interlocked.Exchange(ref _signalReported, 1) == 0)
            Console.WriteLine("[Audio] Non-silent converted PCM entered the Opus pipeline");
    }

    private void LogDiagnostics()
    {
        _lastDiagnostic = Stopwatch.GetTimestamp();
        var raw = _rawLevels.Take();
        var pcm = _pcmLevels.Take();
        Console.WriteLine($"[Audio Capture] callbacks={Interlocked.Read(ref _captureCallbacks)}, bytes={Interlocked.Read(ref _capturedBytes)}, RMS={raw.RmsDb:F1} dBFS, peak={raw.PeakDb:F1} dBFS{(raw.Silent ? " SILENCE" : "")}");
        if (raw.Samples == 0) Console.WriteLine("[Audio Capture] No WASAPI samples this interval; check playback activity and the logged render endpoint");
        Console.WriteLine($"[Audio PCM] RMS={pcm.RmsDb:F1} dBFS, peak={pcm.PeakDb:F1} dBFS{(pcm.Silent ? " SILENCE" : "")}");
        Console.WriteLine($"[Audio Encode] Opus frames={Interlocked.Read(ref _opusFrames)}, last packet={Volatile.Read(ref _lastPacketBytes)} bytes");
        if (debug) Console.WriteLine($"[Audio Capture] queued source bytes={_converter?.BufferedBytes ?? 0}");
        if (_converter?.DroppedInputBytes > 0)
            Console.WriteLine($"[Audio Capture] stale input bytes discarded={_converter.DroppedInputBytes} (bounded real-time queue)");
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_stopping) return;
            _stopping = true;
            if (_running is not null) await _running.CancelAsync();
            try { if (_worker is not null) { await _worker; LogDiagnostics(); } }
            finally
            {
                try
                {
                    if (_capture is not null)
                    {
                        _capture.DataAvailable -= OnCaptured;
                        _capture.RecordingStopped -= OnStopped;
                        await _capture.DisposeAsync(); // Joins WASAPI before converter disposal.
                    }
                }
                finally
                {
                    _capture = null;
                    try { _converter?.Dispose(); }
                    finally
                    {
                        _converter = null;
                        try { _device?.Dispose(); }
                        finally { _device = null; _running?.Dispose(); }
                    }
                }
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await StopAsync(); }
        finally { _encoder.Dispose(); }
    }
}
