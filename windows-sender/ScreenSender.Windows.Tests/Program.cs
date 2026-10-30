using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using ScreenSender.Windows.Capture;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Signaling;
using ScreenSender.Windows.WebRtc;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

// Dependency-free executable checks. No FFmpeg DLLs, desktop capture, or external TV required.
var checks = new (string Name, Func<Task> Run)[]
{
    ("CLI defaults and validation", Options),
    ("Named DNS-SD discovery, fragments, multiple receivers, busy/rename, goodbye and expiry", DiscoveryChecks.Discovery),
    ("Manual endpoint validation and GUI defaults preserve CLI host requirement", DiscoveryChecks.ManualAndDefaults),
    ("Kotlin wire message compatibility", Messages),
    ("Malformed messages", Malformed),
    ("Aspect fit and H264 levels", Scaling),
    ("Packed Bgr 720p and 1080p input formats and sizes", BgrFrames),
    ("Raw frame truncation, invalid metadata and diagnostics", InvalidRawFrames),
    ("Padded Bgr RawImage stride and packed fallback", BgrStride),
    ("Reused encoder frame clears stale forced keyframe state", EncoderFrameChecks.ReusedFrame),
    ("YUV420P encoder dimensions, padded planes and native buffer bounds", EncoderFrameChecks.PlaneValidation),
    ("Stable 6 Mbps H264 screen-sharing settings", EncoderFrameChecks.QualitySettings),
    ("System audio CLI defaults and video-only option", AudioChecks.Options),
    ("appsettings defaults, CLI precedence and malformed JSON diagnostics", ConfigurationChecks.AppSettings),
    ("FFmpeg shared library discovery order and DLL validation", ConfigurationChecks.FFmpegDiscovery),
    ("Audio output CLI modes", LocalAudioOutputChecks.Parsing),
    ("Reserved volume/mute command validation", AudioChecks.FutureVolumeMessages),
    ("Remote volume action wire format and parsing", RemoteVolumeChecks.Messages),
    ("Remote key gating, key mapping and signaling lifecycle", RemoteVolumeChecks.KeysAndLifecycle),
    ("Mirror mode never changes endpoint state", LocalAudioOutputChecks.Mirror),
    ("Projector mode saves state and mutes only once", LocalAudioOutputChecks.SavesAndMutes),
    ("Exact previous mute and master volume restoration", LocalAudioOutputChecks.RestoresPriorMuteAndVolume),
    ("Output state cleanup on streaming/native exceptions", LocalAudioOutputChecks.ExceptionCleanup),
    ("Output state cleanup on cancellation", LocalAudioOutputChecks.CancellationCleanup),
    ("Muted-loopback safety fallback and natural silence", LocalAudioOutputChecks.CaptureSafetyFallback),
    ("Output is never muted before audio RTP starts", LocalAudioOutputChecks.NoPrematureMute),
    ("Audio SDP rejection/direction/Opus validation", AudioChecks.SdpValidation),
    ("Raw WASAPI float32 RMS/peak and silence diagnostics", AudioChecks.RawLevels),
    ("20 ms PCM16 stereo framing and partial reads", AudioChecks.Framing),
    ("20 ms stereo Opus encoding and decoding", AudioChecks.StereoOpus),
    ("Float32/PCM16 conversion and 44.1/48/96 kHz resampling", AudioChecks.Conversion),
    ("Capture overruns retain newest bounded audio without stopping", AudioChecks.CaptureOverrun),
    ("TCP framing, callbacks, concurrent writes, bye and reconnect", TcpProtocol),
    ("Video-only WebRTC offer/answer, host ICE and DTLS", WebRtcNegotiation),
    ("Audio/video WebRTC offer/answer and actual Opus RTP delivery", () => RunWebRtcNegotiation(true)),
    ("Projector mode activates after RTP and restores on peer cleanup", () => RunWebRtcNegotiation(true, true)),
};
int failures = 0;
foreach (var check in checks)
{
    try { await check.Run().WaitAsync(TimeSpan.FromSeconds(20)); Console.WriteLine($"PASS {check.Name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {check.Name}: {error}"); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void Assert(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}

static Task Options()
{
    var defaults = SenderOptions.Parse(["--host", "tv"], Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
    Assert(defaults == new SenderOptions(Host: "tv"), "Unexpected defaults with an explicit host");
    Assert(SenderOptions.Parse(["--host", "tv", "--port", "1234", "--monitor", "2", "--fps", "60"]).Monitor == "2", "Monitor not parsed");
    foreach (var invalid in new[] {
        new[] { "--host" }, new[] { "--host", "tv", "--port", "0" },
        new[] { "--host", "tv", "--width", "1919" }, new[] { "--host", "tv", "--monitor", "0" },
        new[] { "--host", "tv", "--fps", "0" }, new[] { "--host", "tv", "--unknown", "x" },
        new[] { "--host", "tv", "--host", "tv" }, new[] { "--host", "http://tv:1234" },
    })
    {
        try { SenderOptions.Parse(invalid); throw new InvalidOperationException("Accepted invalid CLI"); }
        catch (ArgumentException) { }
    }
    return Task.CompletedTask;
}

static Task Messages()
{
    var fixtures = new (string Json, SignalingMessage Message)[]
    {
        ("""{"type":"hello","deviceName":"Desktop PC"}""", new HelloMessage("Desktop PC")),
        ("""{"type":"offer","sdp":"v=0\r\n"}""", new OfferMessage("v=0\r\n")),
        ("""{"type":"answer","sdp":"v=0\r\n"}""", new AnswerMessage("v=0\r\n")),
        ("""{"type":"ice","sdpMid":"0","sdpMLineIndex":0,"candidate":"candidate:c"}""", new IceMessage("0", 0, "candidate:c")),
        ("""{"type":"ice","sdpMid":null,"sdpMLineIndex":0,"candidate":"c"}""", new IceMessage(null, 0, "c")),
        ("""{"type":"ice","sdpMLineIndex":0,"candidate":"c"}""", new IceMessage(null, 0, "c")),
        ("""{"type":"bye"}""", new ByeMessage()),
    };
    foreach (var fixture in fixtures)
    {
        Assert(SignalingCodec.TryParse(fixture.Json, out var parsed) && parsed == fixture.Message, fixture.Json);
        var encoded = SignalingCodec.Encode(fixture.Message);
        Assert(!encoded.Contains('\n') && !encoded.Contains('\r'), "Framing broken by embedded SDP line breaks");
        Assert(SignalingCodec.TryParse(encoded, out var again) && again == fixture.Message, "Round trip failed");
    }
    Assert(SignalingCodec.Encode(new ByeMessage()) == "{\"type\":\"bye\"}", "Incorrect bye wire format");
    return Task.CompletedTask;
}

static Task Malformed()
{
    foreach (var invalid in new[] { "", "{", "[]", "null", "{}", "{\"type\":\"bye\"} garbage",
        """{"type":"offer","sdp":false}""", """{"type":"answer","sdp":" "}""",
        """{"type":"ice","sdpMid":42,"sdpMLineIndex":0,"candidate":"c"}""",
        """{"type":"ice","sdpMLineIndex":-1,"candidate":"c"}""",
        """{"type":"ice","sdpMLineIndex":"0","candidate":"c"}""",
        """{"type":"ice","sdpMLineIndex":0.5,"candidate":"c"}""",
        """{"type":"ice","sdpMLineIndex":2147483648,"candidate":"c"}""",
        """{"type":"hello","deviceName":null}""", """{"type":"unknown"}""",
        new string(' ', SignalingCodec.MaxLineChars + 1),
    }) Assert(!SignalingCodec.TryParse(invalid, out _), "Accepted invalid JSON or fields");
    return Task.CompletedTask;
}

static Task Scaling()
{
    Assert(VideoSize.Fit(1920, 1080, 1920, 1080) == new VideoSize(1920, 1080), "1080p changed");
    Assert(VideoSize.Fit(1920, 1200, 1920, 1080) == new VideoSize(1728, 1080), "16:10 stretched");
    Assert(VideoSize.Fit(1080, 1920, 1920, 1080) == new VideoSize(606, 1080), "Portrait stretched");
    Assert(VideoSize.Fit(1366, 768, 1920, 1080) == new VideoSize(1366, 768), "Unnecessary upscaling");
    Assert(H264Settings.LevelFor(new VideoSize(1920, 1080), 30) == 40, "1080p30 needs level 4.0");
    Assert(H264Settings.LevelFor(new VideoSize(1280, 720), 30) == 31, "720p30 needs level 3.1");
    var lower = H264Settings.ConstrainToAnswer(new VideoSize(1920, 1080), 30, "packetization-mode=1;profile-level-id=42e01f");
    Assert(lower.Macroblocks <= 3600 && lower.Width % 2 == 0 && lower.Height % 2 == 0, "Receiver level not respected");
    Assert(Math.Abs((double)lower.Width / lower.Height - 16.0 / 9) < 0.01, "Fallback aspect ratio changed");
    var requested = VideoSize.Fit(2560, 1440, 1920, 1080);
    Assert(requested == new VideoSize(1920, 1080), "Full 1440p monitor must fit 1080p without cropping");
    Assert(H264Settings.ConstrainToAnswer(requested, 30, "packetization-mode=1;profile-level-id=42e01f") == new VideoSize(1280, 720),
        "Negotiated level 3.1 must select 1280x720 at 30 fps");
    return Task.CompletedTask;
}

static Task BgrFrames()
{
    foreach (var size in new[] { new VideoSize(1280, 720), new VideoSize(1920, 1080) })
    {
        byte[] sample = new byte[size.Width * size.Height * 3];
        var layout = RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, size.Width, size.Height, size.Width * 3, sample.LongLength);
        Assert(layout.BufferSize == size.Width * size.Height * 3, "Bgr must use exactly three bytes per pixel");
        Assert(layout.Stride == size.Width * 3, "Packed Bgr row size is incorrect");
        Assert(layout.FFmpegPixelFormat == AVPixelFormat.AV_PIX_FMT_BGR24, "Bgr must map to BGR24, not RGB24 or BGRA");
    }
    Assert(1280 * 720 * 3 == 2_764_800, "Incorrect 720p Bgr test fixture");
    Assert(1920 * 1080 * 3 == 6_220_800, "Incorrect 1080p Bgr test fixture");
    var rgb = RawFrameLayout.Validate(VideoPixelFormatsEnum.Rgb, 1280, 720, 3840, 2_764_800);
    Assert(rgb.FFmpegPixelFormat == AVPixelFormat.AV_PIX_FMT_RGB24, "Existing RGB capture compatibility changed");
    return Task.CompletedTask;
}

static Task InvalidRawFrames()
{
    static string Rejected(Action validate)
    {
        try { validate(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return error.Message; }
        throw new InvalidOperationException("Accepted an invalid raw frame");
    }
    byte[] truncated = new byte[1280 * 720 * 3 - 1];
    Assert(Rejected(() => RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, 1280, 720, 3840, truncated.LongLength)).Contains("Truncated"),
        "Truncated Bgr must be rejected before native access");
    foreach (int stride in new[] { 0, -3840, 3839 })
        Rejected(() => RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, 1280, 720, stride, 2_764_800));
    Rejected(() => RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, 0, 720, 3840, 2_764_800));
    Rejected(() => RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, 1280, -1, 3840, 2_764_800));
    Rejected(() => RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, int.MaxValue, 720, null, long.MaxValue));
    Rejected(() => RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, 1280, 720, int.MaxValue, long.MaxValue));
    Rejected(() => RawFrameLayout.FromRawImage(new RawImage {
        Width = 1280, Height = 720, Stride = 3840, PixelFormat = VideoPixelFormatsEnum.Bgr, Sample = IntPtr.Zero,
    }));
    var unsupported = (VideoPixelFormatsEnum)int.MaxValue;
    string diagnostic = Rejected(() => RawFrameLayout.Validate(unsupported, 1280, 720, 3840, 2_764_800));
    foreach (string field in new[] { "2147483647", "width=1280", "height=720", "stride=3840", "sample buffer length=2764800" })
        Assert(diagnostic.Contains(field), $"Unsupported format diagnostic missing {field}");
    return Task.CompletedTask;
}

static Task BgrStride()
{
    const int width = 1280, height = 720, paddedStride = width * 3 + 16;
    byte[] sample = new byte[paddedStride * height];
    var pinned = GCHandle.Alloc(sample, GCHandleType.Pinned);
    try
    {
        var image = new RawImage {
            Width = width, Height = height, Stride = paddedStride,
            PixelFormat = VideoPixelFormatsEnum.Bgr, Sample = pinned.AddrOfPinnedObject(),
        };
        var layout = RawFrameLayout.FromRawImage(image);
        Assert(layout.Stride == paddedStride && layout.BufferSize == sample.Length, "Source stride/padding was discarded");
        Assert(image.Sample == pinned.AddrOfPinnedObject(), "Validation must preserve source-owned sample storage");
        bool rejected = false;
        try { RawFrameLayout.Validate(image.PixelFormat, width, height, paddedStride, width * height * 3); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Packed-sized buffer cannot hold padded rows");
    }
    finally { pinned.Free(); }
    var packed = RawFrameLayout.Validate(VideoPixelFormatsEnum.Bgr, width, height, null, width * height * 3);
    Assert(packed.Stride == width * 3, "API without stride metadata must fall back to a three-byte packed row");
    return Task.CompletedTask;
}

static async Task TcpProtocol()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var token = timeout.Token;
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    for (int attempt = 0; attempt < 2; attempt++)
    {
        await using var client = new SignalingClient();
        await client.ConnectAsync("127.0.0.1", port, token);
        using var server = await listener.AcceptTcpClientAsync(token);
        using var reader = new StreamReader(server.GetStream(), Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server.GetStream(), new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var answer = new TaskCompletionSource<AnswerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ice = new TaskCompletionSource<IceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        int warnings = 0;
        bool bye = false;
        client.AnswerReceived = (message, _) => { answer.SetResult(message); return Task.CompletedTask; };
        client.IceReceived = (message, _) => { ice.SetResult(message); return Task.CompletedTask; };
        client.Warning = _ => warnings++;
        client.Error = message => throw new InvalidOperationException(message);
        client.ByeReceived = () => bye = true;
        var receiving = client.ReceiveAsync(token);
        var sending = Task.WhenAll(Enumerable.Range(0, 24).Select(index => client.SendAsync(new HelloMessage($"PC {index} 📺"), token)));
        for (int i = 0; i < 24; i++)
        {
            var line = await reader.ReadLineAsync(token);
            Assert(line is not null && SignalingCodec.TryParse(line, out var hello) && hello is HelloMessage, "Concurrent writes interleaved");
        }
        await sending;
        await writer.WriteAsync("malformed\n{\"type\":\"ice\","); // Partial TCP reads must preserve JSON framing.
        await writer.WriteAsync("\"sdpMid\":\"0\",\"sdpMLineIndex\":0,\"candidate\":\"c\"}\r\n");
        await writer.WriteLineAsync(SignalingCodec.Encode(new AnswerMessage("v=0\r\n")));
        Assert((await ice.Task.WaitAsync(token)).SdpMLineIndex == 0, "No ICE callback");
        Assert((await answer.Task.WaitAsync(token)).Sdp == "v=0\r\n", "No answer callback");
        Assert(warnings == 1, "Malformed message was not ignored");
        await writer.WriteLineAsync("{\"type\":\"bye\"}");
        await receiving;
        Assert(bye, "No bye callback");
        await client.SendAsync(new ByeMessage(), token);
        Assert(await reader.ReadLineAsync(token) == "{\"type\":\"bye\"}", "Sender bye missing");
    }
}

static Task WebRtcNegotiation() => RunWebRtcNegotiation(false);

static async Task RunWebRtcNegotiation(bool withAudio, bool projector = false)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var token = timeout.Token;
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    await using var client = new SignalingClient();
    await client.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, token);
    using var server = await listener.AcceptTcpClientAsync(token);
    using var remote = new RTCPeerConnection(new RTCConfiguration { iceServers = [] }, videoAsPrimary: true);
    var remoteConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    remote.onconnectionstatechange += state =>
    {
        if (state == RTCPeerConnectionState.connected) remoteConnected.TrySetResult();
    };
    var capture = new FakeCapture();
    var audio = withAudio ? new FakeSystemAudio(projector) : null;
    var localEndpoint = projector ? new LocalAudioOutputChecks.Endpoint() : null;
    using var localOutput = projector ? new LocalAudioOutputController(AudioOutputMode.Projector, _ =>
    {
        Assert(capture.Started.Task.IsCompleted && audio!.Started.Task.IsCompleted, "Local mute happened before audio/video capture started");
        return localEndpoint!;
    }, _ => { }) : null;
    remote.addTrack(new MediaStreamTrack(capture.Format, MediaStreamStatusEnum.RecvOnly));
    if (audio is not null) remote.addTrack(new MediaStreamTrack(audio.Format, MediaStreamStatusEnum.RecvOnly));
    var audioReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    uint? previousAudioTimestamp = null;
    remote.OnRtpPacketReceived += (_, media, packet) =>
    {
        if (media != SDPMediaTypesEnum.audio || packet.Payload.Length == 0) return;
        if (previousAudioTimestamp is { } previous)
        {
            if (unchecked(packet.Header.Timestamp - previous) == 960) audioReceived.TrySetResult();
            else audioReceived.TrySetException(new InvalidDataException("Audio RTP timestamps must advance by 960 ticks per 20 ms packet"));
        }
        previousAudioTimestamp = packet.Header.Timestamp;
    };
    await using var sender = new WebRtcScreenSender(capture, client, audio: audio, audioOutput: localOutput);
    client.AnswerReceived = sender.HandleAnswerAsync;
    client.IceReceived = sender.HandleIceAsync;
    var receiving = client.ReceiveAsync(token);
    var sending = sender.RunAsync(token);
    var serverTask = Task.Run(async () =>
    {
        using var reader = new StreamReader(server.GetStream(), Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server.GetStream(), new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null) return;
            Assert(SignalingCodec.TryParse(line, out var message), "Sender emitted malformed JSON");
            if (message is OfferMessage offer)
            {
                Assert(offer.Sdp.Contains("m=audio") == withAudio && offer.Sdp.Contains("a=sendonly"), "Offer audio presence does not match selected mode");
                if (withAudio) Assert(offer.Sdp.Contains("opus/48000/2", StringComparison.OrdinalIgnoreCase), "Offer must advertise stereo Opus");
                Assert(offer.Sdp.Contains("H264/90000") && offer.Sdp.Contains("packetization-mode=1"), "Not WebRTC H264");
                Assert(remote.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offer.Sdp }) == SetDescriptionResultEnum.OK, "Offer rejected");
                while (remote.iceGatheringState != RTCIceGatheringState.complete) await Task.Delay(10, token);
                var answer = remote.createAnswer(null);
                await remote.setLocalDescription(answer);
                var candidate = answer.sdp.Split('\n').Select(line => line.TrimEnd('\r'))
                    .First(line => line.StartsWith("a=candidate:", StringComparison.Ordinal))[2..];
                // Exercise the Android behavior: a trickled candidate can precede the answer.
                await writer.WriteLineAsync(SignalingCodec.Encode(new IceMessage("0", 0, candidate)));
                await writer.WriteLineAsync(SignalingCodec.Encode(new AnswerMessage(answer.sdp)));
            }
            else if (message is IceMessage ice)
                remote.addIceCandidate(new RTCIceCandidateInit { candidate = ice.Candidate, sdpMid = ice.SdpMid, sdpMLineIndex = (ushort)ice.SdpMLineIndex });
        }
    }, token);
    try
    {
        var completed = await Task.WhenAny(capture.Started.Task, sending, serverTask).WaitAsync(token);
        if (completed != capture.Started.Task) { await completed; throw new InvalidOperationException("Negotiation ended before capture started"); }
        Assert(capture.Configured, "Video started before answer configuration");
        await remoteConnected.Task.WaitAsync(token);
        if (audio is not null)
        {
            await audio.Started.Task.WaitAsync(token);
            Assert(audio.Configured, "Audio started before Opus format negotiation");
            using var encoder = new OpusStereoEncoder();
            for (int index = 0; index < 10 && !audioReceived.Task.IsCompleted; index++)
            {
                audio.Emit(encoder.Encode(new byte[Pcm20MsFramer.FrameBytes]));
                await Task.Delay(20, token);
            }
            await audioReceived.Task.WaitAsync(token);
            Assert(sender.AudioStatistics.SendAudioCalls >= 2 && sender.AudioStatistics.PacketsSent >= 2 && sender.AudioStatistics.PayloadBytes > 0,
                "Encoded audio callbacks must be backed by actual SIPSorcery audio RTP counters");
            if (projector) Assert(localEndpoint!.Muted, "Projector mode did not activate after capture/negotiation/RTP");
        }
    }
    finally
    {
        await timeout.CancelAsync();
        foreach (var task in new[] { sending, receiving, serverTask })
        {
            try { await task; } catch (OperationCanceledException) { }
        }
        await sender.DisposeAsync();
        if (projector) Assert(!localEndpoint!.Muted && localEndpoint.Volume == 0.62f, "WebRTC cleanup did not restore local output");
        remote.Close("test complete");
    }
}

sealed class FakeSystemAudio(bool emitOnStart = false) : ISystemAudioSource
{
    private long _emitted;
    public string RenderEndpointId => "capture-endpoint";
    public AudioOutputActivity OutputActivity => new(_emitted, _emitted, _emitted, 0);
    public event Action<uint, byte[]>? EncodedFrame;
    public AudioFormat Format => OpusStereoEncoder.Format;
    public Task<string> Failure { get; } = new TaskCompletionSource<string>().Task;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Configured { get; private set; }
    public void SetFormat(AudioFormat format) { if (format.Codec != AudioCodecsEnum.OPUS) throw new InvalidDataException("Expected Opus"); Configured = true; }
    public Task StartAsync(CancellationToken token)
    {
        Started.TrySetResult();
        if (emitOnStart) { using var encoder = new OpusStereoEncoder(); Emit(encoder.Encode(new byte[Pcm20MsFramer.FrameBytes])); }
        return Task.CompletedTask;
    }
    public Task StopAsync() => Task.CompletedTask;
    public void Emit(byte[] packet) { Interlocked.Increment(ref _emitted); EncodedFrame?.Invoke(960, packet); }
}

sealed class FakeCapture : IScreenCapture
{
    public event Action<uint, byte[]>? EncodedFrame { add { } remove { } }
    public VideoFormat Format => H264Settings.Format(new VideoSize(1920, 1080), 30);
    public Task<string> Failure { get; } = new TaskCompletionSource<string>().Task;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Configured { get; private set; }
    public Task ConfigureAnswerAsync(string? parameters, CancellationToken token) { Configured = true; return Task.CompletedTask; }
    public Task StartAsync(CancellationToken token) { Started.TrySetResult(); return Task.CompletedTask; }
    public Task StopAsync() => Task.CompletedTask;
    public void RequestKeyFrame() { }
}
