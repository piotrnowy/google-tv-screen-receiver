using System.Diagnostics;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using ScreenSender.Windows.Configuration;

namespace ScreenSender.Windows.Capture;

public sealed class ScreenCaptureService : IScreenCapture, IAsyncDisposable
{
    private readonly WindowsMonitor _monitor;
    private readonly SenderOptions _options;
    private readonly object _frameGate = new();
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FFmpegScreenSource? _source;
    private FFmpegVideoEncoder? _encoder;
    private RawFrameScaler? _scaler;
    private bool _captureLogged;
    private bool _encoderLogged;
    private bool _encodedSizeVerified;
    private bool _stopping;
    private long _lastKeyFrame;
    private long _rtpRemainder;
    private int _forceKeyFrame = 1;
    private int _disposed;

    public event Action<uint, byte[]>? EncodedFrame;
    public VideoSize Size { get; private set; }
    public VideoFormat Format => H264Settings.Format(Size, _options.Fps);
    public Task<string> Failure => _failure.Task;

    public ScreenCaptureService(WindowsMonitor monitor, SenderOptions options)
    {
        _monitor = monitor;
        _options = options;
        Size = VideoSize.Fit(monitor.Bounds.Width, monitor.Bounds.Height, options.Width, options.Height);
    }

    public Task InitializeAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        InitializeFFmpeg();
        Console.WriteLine($"[Capture] {_monitor.Description} {_monitor.Bounds.Width}x{_monitor.Bounds.Height} at ({_monitor.Bounds.X},{_monitor.Bounds.Y})");
        // Capture the whole physical monitor. Scaling the output must never crop the desktop.
        _source = new FFmpegScreenSource("desktop", _monitor.Bounds, _options.Fps);
        if (!_source.InitialiseDecoder())
            throw new InvalidOperationException("FFmpeg gdigrab could not initialize desktop capture. Check the interactive session and monitor bounds.");
        _source.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
        _source.SetVideoSourceFormat(_source.GetVideoSourceFormats().First());
        _source.OnVideoSourceRawSampleFaster += OnRawFrame;
        _source.OnVideoSourceError += OnSourceError;
        InitializeEncoder(Size);
        Console.WriteLine($"[Capture] Selected output {Size} @ {_options.Fps} fps (aspect fit, H264)");
    }, token);

    public Task ConfigureAnswerAsync(string? parameters, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (H264Settings.Parameter(parameters, "packetization-mode") != "1")
            throw new InvalidDataException("Receiver must accept H.264 packetization-mode=1.");
        // Absent profile-level-id means baseline level 3.1 under RFC 6184.
        string fmtp = H264Settings.Parameter(parameters, "profile-level-id") is null
            ? parameters + ";profile-level-id=42e01f" : parameters!;
        var selected = H264Settings.ConstrainToAnswer(Size, _options.Fps, fmtp);
        if (selected != Size)
        {
            Console.WriteLine($"[Capture] Receiver's H264 level limits output to {selected}; requested {Size}.");
            InitializeEncoder(selected);
        }
    }, token);

    public async Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await (_source ?? throw new InvalidOperationException("Screen capture is not initialized.")).StartVideo();
        await _firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        Console.WriteLine($"[Video] Streaming started: {Size} @ {_options.Fps} fps");
    }

    public void RequestKeyFrame() => Interlocked.Exchange(ref _forceKeyFrame, 1);

    public async Task StopAsync()
    {
        lock (_frameGate) { _stopping = true; }
        if (_source is not null) await _source.CloseVideo();
    }

    private static void InitializeFFmpeg()
    {
        string root = FFmpegRuntime.ResolveSharedLibraryDirectory();
        try
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_ERROR, root);
            Console.WriteLine($"[Capture] FFmpeg shared libraries: {root}");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"FFmpeg shared libraries in '{root}' could not be loaded. Check the FFmpeg 8.1 x64 build, its DLL dependencies, gdigrab and libx264. {error.Message}", error);
        }
    }

    private void InitializeEncoder(VideoSize requested)
    {
        FFmpegVideoEncoder? encoder = null;
        try
        {
            encoder = new FFmpegVideoEncoder(H264Encoding.Options(requested, _options.Fps));
            if (!encoder.SetCodec(AVCodecID.AV_CODEC_ID_H264, "libx264"))
                throw new InvalidOperationException("The installed FFmpeg build does not include the libx264 H.264 encoder.");
            // Fixed 6 Mbps CBR for this quality milestone; no adaptive bitrate changes.
            encoder.SetBitrate(H264Encoding.Bitrate, 600_000, H264Encoding.Bitrate, H264Encoding.Bitrate);
            encoder.InitialiseEncoder(AVCodecID.AV_CODEC_ID_H264, requested.Width, requested.Height, _options.Fps);
            lock (_frameGate)
            {
                _encoder?.Dispose();
                _encoder = encoder;
                _scaler?.Dispose();
                _scaler = null;
                Size = requested;
            }
        }
        catch (Exception error)
        {
            encoder?.Dispose();
            throw new InvalidOperationException($"Could not initialize H264 {requested}; resolution fallback is disabled for quality testing.", error);
        }
    }

    private unsafe void OnRawFrame(uint sourceDuration, RawImage image)
    {
        lock (_frameGate)
        {
            if (_stopping || _encoder is null || _failure.Task.IsCompleted) return;
            try
            {
                var layout = RawFrameLayout.FromRawImage(image);
                if (!_captureLogged)
                {
                    Console.WriteLine($"[Capture] Input format: {layout.PixelFormat}");
                    Console.WriteLine($"[Capture] Input: {layout.Width}x{layout.Height}");
                    Console.WriteLine($"[Capture] Source: {layout.Width}x{layout.Height} {layout.PixelFormat}, stride={layout.Stride}, buffer size={layout.BufferSize}");
                    Console.WriteLine($"[Capture] Output: {Size}");
                    Console.WriteLine("[Capture] Encoder: H264");
                    _captureLogged = true;
                }
                if (_scaler is null || _scaler.SourceWidth != layout.Width || _scaler.SourceHeight != layout.Height ||
                    _scaler.SourceFormat != layout.PixelFormat)
                {
                    _scaler?.Dispose();
                    _scaler = null;
                    _scaler = new RawFrameScaler(layout, Size);
                }
                // Consume the source-owned pointer synchronously inside its callback.
                var frame = _scaler.Convert(image.Sample, layout);
                var encoderInput = H264Encoding.PrepareFrame(frame, Size);
                if (!_encoderLogged)
                {
                    Console.WriteLine($"[Scaler] {layout.Width}x{layout.Height} {layout.FFmpegPixelFormat} -> {encoderInput.Width}x{encoderInput.Height} YUV420P");
                    Console.WriteLine("[Encoder] H264");
                    Console.WriteLine($"[Encoder] Resolution: {encoderInput.Width}x{encoderInput.Height}");
                    Console.WriteLine($"[Encoder] FPS: {_options.Fps}");
                    Console.WriteLine($"[Encoder] H264 bitrate: {H264Encoding.Bitrate / 1000} kbps (CBR)");
                    Console.WriteLine("[Encoder] Pixel format: YUV420P");
                    Console.WriteLine($"[Encoder] Input strides Y/U/V: {encoderInput.YStride}/{encoderInput.UStride}/{encoderInput.VStride}, plane bytes={encoderInput.PlaneBytes}, allocated buffer bytes={encoderInput.AllocationBytes}");
                    Console.WriteLine($"[Encoder] preset=veryfast, tune=zerolatency, GOP={_options.Fps * 2}, B-frames=0");
                    _encoderLogged = true;
                }
                bool keyFrame = Interlocked.Exchange(ref _forceKeyFrame, 0) != 0 ||
                    Stopwatch.GetElapsedTime(_lastKeyFrame) >= TimeSpan.FromSeconds(2);
                if (keyFrame) _lastKeyFrame = Stopwatch.GetTimestamp();
                byte[]? sample = _encoder.Encode(AVCodecID.AV_CODEC_ID_H264, frame, _options.Fps, keyFrame);
                if (sample is { Length: > 0 })
                {
                    if (!_encodedSizeVerified)
                    {
                        var encodedSize = H264Encoding.ReadEncodedSize(sample);
                        if (encodedSize != Size)
                            throw new InvalidDataException($"Encoded H264 resolution {encodedSize} does not match selected output {Size}.");
                        Console.WriteLine($"[Video] Actual encoded resolution: {encodedSize} (verified from first H264 frame)");
                        _encodedSizeVerified = true;
                    }
                    _rtpRemainder += 90000;
                    uint duration = (uint)(_rtpRemainder / _options.Fps);
                    _rtpRemainder %= _options.Fps;
                    EncodedFrame?.Invoke(duration, sample);
                    _firstFrame.TrySetResult();
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[Capture] Failed raw frame: {RawFrameLayout.Describe(image.PixelFormat, image.Width, image.Height, image.Stride, image.BufferSize)}");
                _failure.TrySetResult($"Capture/encoding failed: {error.Message}");
            }
        }
    }

    private void OnSourceError(string error) => _failure.TrySetResult($"FFmpeg screen capture: {error}");

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await StopAsync(); }
        finally
        {
            if (_source is not null)
            {
                _source.OnVideoSourceRawSampleFaster -= OnRawFrame;
                _source.OnVideoSourceError -= OnSourceError;
                _source.Dispose();
            }
            lock (_frameGate) { _scaler?.Dispose(); _encoder?.Dispose(); _encoder = null; }
        }
    }

    /// <summary>Scale/convert strided BGR24 or RGB24 using swscale; reuse the YUV420P output frame.</summary>
    private sealed unsafe class RawFrameScaler : IDisposable
    {
        private SwsContext* _context;
        private AVFrame* _frame;
        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public VideoPixelFormatsEnum SourceFormat { get; }

        public RawFrameScaler(RawFrameLayout source, VideoSize target)
        {
            SourceWidth = source.Width;
            SourceHeight = source.Height;
            SourceFormat = source.PixelFormat;
            try
            {
                _context = ffmpeg.sws_getContext(source.Width, source.Height, source.FFmpegPixelFormat,
                    target.Width, target.Height, AVPixelFormat.AV_PIX_FMT_YUV420P, (int)SwsFlags.SWS_BILINEAR, null, null, null);
                if (_context == null) throw new InvalidOperationException("Could not initialize FFmpeg video scaling.");
                _frame = ffmpeg.av_frame_alloc();
                if (_frame == null) throw new OutOfMemoryException("Could not allocate an FFmpeg frame.");
                _frame->width = target.Width;
                _frame->height = target.Height;
                _frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                Check(ffmpeg.av_frame_get_buffer(_frame, 32));
            }
            catch { Dispose(); throw; }
        }

        public AVFrame* Convert(IntPtr sample, RawFrameLayout source)
        {
            Check(ffmpeg.av_frame_make_writable(_frame));
            var data = new byte_ptrArray8();
            var stride = new int_array8();
            data[0] = (byte*)sample;
            stride[0] = source.Stride;
            int rows = ffmpeg.sws_scale(_context, data, stride, 0, source.Height, _frame->data, _frame->linesize);
            Check(rows);
            if (rows != _frame->height)
                throw new InvalidOperationException($"FFmpeg scaling returned {rows} rows; expected {_frame->height}.");
            return _frame;
        }

        private static void Check(int result)
        {
            if (result < 0) throw new InvalidOperationException($"FFmpeg scaling failed: {FFmpegInit.av_strerror(result)}");
        }

        public void Dispose()
        {
            if (_frame != null) { var frame = _frame; ffmpeg.av_frame_free(&frame); _frame = null; }
            if (_context != null) { ffmpeg.sws_freeContext(_context); _context = null; }
        }
    }
}
