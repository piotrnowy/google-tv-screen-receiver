using System.Threading.Channels;
using ScreenSender.Windows.Capture;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Signaling;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.WebRtc;

public sealed class WebRtcScreenSender : IAsyncDisposable
{
    private readonly IScreenCapture _capture;
    private readonly ISystemAudioSource? _audio;
    private readonly LocalAudioOutputController? _audioOutput;
    private readonly RemoteVolumeKeyController? _remoteVolume;
    private readonly SignalingClient _signaling;
    private readonly RTCPeerConnection _peer;
    private readonly SemaphoreSlim _peerGate = new(1, 1);
    private readonly Channel<IceMessage> _localIce = Channel.CreateBounded<IceMessage>(128);
    private readonly List<IceMessage> _pendingRemoteIce = [];
    private readonly TaskCompletionSource _offerSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _configured = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task<string> _anyFailure;
    private readonly Task _iceWriter;
    private readonly Task _audioDiagnostics;
    private long _audioSendCalls;
    private long _audioEncodedEvents;
    private long _audioDropped;
    private VideoFormat? _negotiatedFormat;
    private string? _transportMid;
    private ushort _iceMLineIndex;
    private bool _audioNegotiated;
    private bool _answerSet;
    private int _started;
    private int _disposed;
    public Action? StreamingStarted { get; set; }
    public (long SendAudioCalls, uint PacketsSent, uint PayloadBytes) AudioStatistics =>
        (Interlocked.Read(ref _audioSendCalls), _peer.AudioRtcpSession?.PacketsSentCount ?? 0, _peer.AudioRtcpSession?.OctetsSentCount ?? 0);

    public WebRtcScreenSender(IScreenCapture capture, SignalingClient signaling,
        IEnumerable<RTCIceServer>? iceServers = null, ISystemAudioSource? audio = null, LocalAudioOutputController? audioOutput = null, RemoteVolumeKeyController? remoteVolume = null)
    {
        _capture = capture;
        _audio = audio;
        _audioOutput = audioOutput;
        _remoteVolume = remoteVolume;
        if (audioOutput is not null && audio is null) throw new ArgumentException("Audio output mode requires an audio source.");
        _signaling = signaling;
        _anyFailure = Task.WhenAny<string>(audio is null ? [_failure.Task, capture.Failure] : [_failure.Task, capture.Failure, audio.Failure]).Unwrap();
        Console.WriteLine("[WebRTC] Creating peer connection");
        // Deliberately no STUN or TURN. The optional list is for future configuration.
        _peer = new RTCPeerConnection(new RTCConfiguration
        {
            iceServers = iceServers?.ToList() ?? [],
            X_ICEIncludeAllInterfaceAddresses = true,
            X_UseRtpFeedbackProfile = true,
        }, videoAsPrimary: true);
        Console.WriteLine("[WebRTC] NEW");
        _peer.onconnectionstatechange += OnConnectionState;
        _peer.oniceconnectionstatechange += state => Console.WriteLine($"[ICE] {state.ToString().ToUpperInvariant()}");
        _peer.OnVideoFormatsNegotiated += formats =>
        {
            var h264 = formats.Where(format => format.Codec == VideoCodecsEnum.H264).ToList();
            if (h264.Count == 0) _failure.TrySetResult("Receiver did not negotiate an H.264 video track.");
            else _negotiatedFormat = h264[0];
        };
        _peer.OnAudioFormatsNegotiated += formats =>
        {
            if (_audio is null) return;
            Console.WriteLine($"[Audio] Negotiated formats: {string.Join(", ", formats.Select(format => $"{format.Codec}/{format.ClockRate}/{format.ChannelCount} PT={format.FormatID}"))}");
            var opus = formats.Where(format => format.Codec == AudioCodecsEnum.OPUS).ToList();
            if (opus.Count == 0) { _failure.TrySetResult("Receiver did not negotiate Opus system audio."); return; }
            try
            {
                _audio.SetFormat(opus[0]);
                _audioNegotiated = true;
                Console.WriteLine($"[Audio] Selected format: OPUS {opus[0].ClockRate}Hz, channels={opus[0].ChannelCount}, PT={opus[0].FormatID}");
            }
            catch (Exception error) { _failure.TrySetResult($"Could not configure negotiated Opus audio: {error.Message}"); }
        };
        _peer.onicecandidate += candidate =>
        {
            if (_disposed != 0 || string.IsNullOrWhiteSpace(candidate.candidate)) return;
            if (!_localIce.Writer.TryWrite(new IceMessage(candidate.sdpMid, candidate.sdpMLineIndex, candidate.candidate)))
                _failure.TrySetResult("Local ICE queue overflow; reconnect to start a new session.");
        };
        _capture.EncodedFrame += OnEncodedFrame;
        if (_audio is not null) _audio.EncodedFrame += OnEncodedAudio;
        _iceWriter = WriteIceAsync(_lifetime.Token);
        _audioDiagnostics = _audio is null ? Task.CompletedTask : AudioDiagnosticsAsync(_lifetime.Token);
    }

    public async Task RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("One offer is supported per sender instance.");
        await _peerGate.WaitAsync(token);
        try
        {
            _peer.addTrack(new MediaStreamTrack(_capture.Format, MediaStreamStatusEnum.SendOnly));
            if (_audio is not null)
            {
                _peer.addTrack(new MediaStreamTrack(_audio.Format, MediaStreamStatusEnum.SendOnly));
                Console.WriteLine("[Audio] Audio MediaStreamTrack added");
                Console.WriteLine("[Audio] Direction: SendOnly; advertised codec: OPUS/48000/2");
            }
            var offer = _peer.createOffer(null);
            if (_audio is not null) AudioSdpDiagnostics.LogAndValidate(offer.sdp, offer: true);
            (_transportMid, _iceMLineIndex) = TransportMid(offer.sdp);
            await _peer.setLocalDescription(offer).WaitAsync(token);
            await _signaling.SendAsync(new OfferMessage(offer.sdp), token);
            Console.WriteLine("[WebRTC] Offer sent");
            _offerSent.TrySetResult();
        }
        finally { _peerGate.Release(); }

        var ready = Task.WhenAll(_connected.Task, _configured.Task);
        Task result;
        try { result = await Task.WhenAny(ready, _anyFailure).WaitAsync(TimeSpan.FromSeconds(30), token); }
        catch (TimeoutException error)
        {
            throw new TimeoutException("WebRTC did not connect within 30 seconds. Check the TV's answer, LAN connectivity, and Windows Firewall UDP access.", error);
        }
        if (result == _anyFailure) throw new InvalidOperationException(await _anyFailure);
        await ready;
        var captureStart = Task.WhenAll(_capture.StartAsync(token), _audio?.StartAsync(token) ?? Task.CompletedTask);
        var started = await Task.WhenAny(captureStart, _anyFailure).WaitAsync(token);
        if (started == _anyFailure) throw new InvalidOperationException(await _anyFailure);
        await captureStart;
        if (_audio is not null && _audioOutput is not null)
            _audioOutput.Activate(_audio.RenderEndpointId, CurrentAudioActivity());
        if (_remoteVolume is not null) await _remoteVolume.ActivateAsync(_signaling, token);
        StreamingStarted?.Invoke();
        throw new InvalidOperationException(await _anyFailure.WaitAsync(token));
    }

    public async Task HandleAnswerAsync(AnswerMessage answer, CancellationToken token)
    {
        await _peerGate.WaitAsync(token);
        try
        {
            if (_disposed != 0) return;
            if (_answerSet) { Console.Error.WriteLine("[Signaling] Ignored duplicate answer"); return; }
            Console.WriteLine("[WebRTC] Answer received");
            if (_audio is not null)
            {
                try { AudioSdpDiagnostics.LogAndValidate(answer.Sdp, offer: false); }
                catch (InvalidDataException error) { _failure.TrySetResult(error.Message); return; }
            }
            SetDescriptionResultEnum result;
            try
            {
                result = _peer.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answer.Sdp });
            }
            catch (Exception error)
            {
                _failure.TrySetResult($"Invalid SDP answer: {error.Message}");
                return;
            }
            if (result != SetDescriptionResultEnum.OK)
            {
                _failure.TrySetResult($"Receiver's SDP answer was rejected: {result}");
                return;
            }
            if (_negotiatedFormat is not { } format)
            {
                _failure.TrySetResult("Receiver rejected the H.264 video track.");
                return;
            }
            if (_audio is not null && !_audioNegotiated)
            {
                _failure.TrySetResult("Receiver rejected Opus audio. Update the TV receiver or use --audio false.");
                return;
            }
            _answerSet = true;
            foreach (var candidate in _pendingRemoteIce) AddRemoteIce(candidate);
            _pendingRemoteIce.Clear();
            try
            {
                // Read answer fmtp explicitly: a negotiated format may retain the local parameters.
                var fmtp = answer.Sdp.Split('\n').Select(line => line.TrimEnd('\r'))
                    .FirstOrDefault(line => line.StartsWith($"a=fmtp:{format.FormatID} ", StringComparison.Ordinal));
                await _capture.ConfigureAnswerAsync(fmtp?[(fmtp.IndexOf(' ') + 1)..], token);
                _configured.TrySetResult();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _failure.TrySetResult($"Could not configure negotiated video: {error.Message}");
            }
        }
        finally { _peerGate.Release(); }
    }

    public async Task HandleIceAsync(IceMessage candidate, CancellationToken token)
    {
        await _peerGate.WaitAsync(token);
        try
        {
            if (_disposed != 0) return;
            if (!_answerSet)
            {
                if (_pendingRemoteIce.Count >= 256) { _failure.TrySetResult("Too many pending remote ICE candidates."); return; }
                _pendingRemoteIce.Add(candidate);
            }
            else AddRemoteIce(candidate);
        }
        finally { _peerGate.Release(); }
    }

    private void AddRemoteIce(IceMessage candidate)
    {
        try
        {
            if (candidate.SdpMLineIndex > ushort.MaxValue) throw new FormatException("ICE m-line index is out of range.");
            _peer.addIceCandidate(new RTCIceCandidateInit
            {
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = (ushort)candidate.SdpMLineIndex,
                candidate = candidate.Candidate,
            });
            Console.WriteLine("[ICE] Remote candidate");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[ICE] Ignored invalid remote candidate: {error.Message}");
        }
    }

    private async Task WriteIceAsync(CancellationToken token)
    {
        try
        {
            await _offerSent.Task.WaitAsync(token);
            await foreach (var candidate in _localIce.Reader.ReadAllAsync(token))
            {
                // Audio/video share one BUNDLE transport. Derive the transport MID/index from the offer.
                await _signaling.SendAsync(candidate with { SdpMid = _transportMid, SdpMLineIndex = _iceMLineIndex }, token);
                Console.WriteLine("[ICE] Local candidate");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (_disposed == 0) _failure.TrySetResult($"Sending local ICE failed: {error.Message}"); }
    }

    private void OnConnectionState(RTCPeerConnectionState state)
    {
        Console.WriteLine($"[WebRTC] {state.ToString().ToUpperInvariant()}");
        if (state == RTCPeerConnectionState.connected)
        {
            _capture.RequestKeyFrame();
            _connected.TrySetResult();
        }
        else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.disconnected or RTCPeerConnectionState.closed)
        {
            _remoteVolume?.Deactivate();
            if (_disposed != 0) return;
            _failure.TrySetResult($"WebRTC {state.ToString().ToUpperInvariant()}; start a new sender session to reconnect.");
        }
    }

    private void OnEncodedFrame(uint duration, byte[] sample)
    {
        if (_disposed == 0 && _peer.connectionState == RTCPeerConnectionState.connected) _peer.SendVideo(duration, sample);
    }

    private void OnEncodedAudio(uint duration, byte[] sample)
    {
        Interlocked.Increment(ref _audioEncodedEvents);
        if (_disposed != 0 || _peer.connectionState != RTCPeerConnectionState.connected)
        {
            Interlocked.Increment(ref _audioDropped);
            return;
        }
        Interlocked.Increment(ref _audioSendCalls);
        _peer.SendAudio(duration, sample);
    }

    private async Task AudioDiagnosticsAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                LogAudioTransport();
                _audioOutput?.Observe(CurrentAudioActivity());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { _failure.TrySetResult($"Audio output monitoring failed: {error.Message}"); }
    }

    private AudioOutputActivity CurrentAudioActivity() => (_audio?.OutputActivity ?? default) with { RtpPackets = AudioStatistics.PacketsSent };

    private void LogAudioTransport()
    {
        var stats = AudioStatistics;
        Console.WriteLine($"[Audio RTP] packets sent={stats.PacketsSent}, payload bytes={stats.PayloadBytes} (SIPSorcery audio RTCP TX counters)");
        Console.WriteLine($"[Audio RTP] encoded events={Interlocked.Read(ref _audioEncodedEvents)}, SendAudio calls={stats.SendAudioCalls}, dropped before SendAudio={Interlocked.Read(ref _audioDropped)}");
        if (stats.SendAudioCalls > 0 && stats.PacketsSent == 0)
            Console.WriteLine($"[Audio RTP] BLOCKED: HasAudio={_peer.HasAudio}, started={_peer.IsAudioStarted}, secure={_peer.IsSecureContextReady()}, local={_peer.AudioLocalTrack?.StreamStatus}, remote={_peer.AudioRemoteTrack?.StreamStatus}");
    }

    private static (string Mid, ushort Index) TransportMid(string sdp)
    {
        var mids = new List<string>();
        string? bundleMid = null;
        foreach (var line in sdp.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("a=group:BUNDLE ", StringComparison.Ordinal)) bundleMid = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            if (line.StartsWith("a=mid:", StringComparison.Ordinal)) mids.Add(line[6..]);
        }
        if (mids.Count == 0) throw new InvalidDataException("Generated offer has no transport MID.");
        int index = bundleMid is null ? 0 : mids.IndexOf(bundleMid);
        if (index < 0) throw new InvalidDataException("Generated offer has an invalid BUNDLE MID.");
        return (mids[index], checked((ushort)index));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        _localIce.Writer.TryComplete();
        await _iceWriter;
        await _audioDiagnostics;
        try
        {
            if (_remoteVolume is not null) await _remoteVolume.DisposeAsync();
            try
            {
                try { _audioOutput?.Restore(); }
                finally { if (_audio is not null) await _audio.StopAsync(); }
            }
            finally { await _capture.StopAsync(); }
        }
        finally
        {
            if (_audio is not null) LogAudioTransport();
            _capture.EncodedFrame -= OnEncodedFrame;
            if (_audio is not null) _audio.EncodedFrame -= OnEncodedAudio;
            await _peerGate.WaitAsync();
            try { _pendingRemoteIce.Clear(); _peer.Close("sender shutdown"); _peer.Dispose(); }
            finally { _peerGate.Release(); _lifetime.Dispose(); }
        }
    }
}
