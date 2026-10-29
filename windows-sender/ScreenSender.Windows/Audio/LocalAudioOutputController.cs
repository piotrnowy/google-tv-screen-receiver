using System.Globalization;
using NAudio.CoreAudioApi;

namespace ScreenSender.Windows.Audio;

public enum AudioOutputMode { Mirror, Projector }
public readonly record struct LocalAudioOutputState(string EndpointId, bool Muted, float Volume, float VolumeDb);
public readonly record struct AudioOutputActivity(long CapturedSignal, long ConvertedSignal, long OpusFrames, uint RtpPackets);

public interface ILocalAudioEndpoint : IDisposable
{
    string Id { get; }
    bool Muted { get; set; }
    float Volume { get; }
    float VolumeDb { get; set; }
    float RenderPeak { get; }
}

/// <summary>Owns only endpoint output policy, never the WASAPI capture session or per-app volume.</summary>
public sealed class LocalAudioOutputController : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<string, ILocalAudioEndpoint> _open;
    private readonly Action<string> _log;
    private readonly TimeProvider _clock;
    private ILocalAudioEndpoint? _endpoint;
    private LocalAudioOutputState? _saved;
    private AudioOutputActivity _previous;
    private long _mutedAt;
    private long? _lostSignalAt;
    private bool _restorePending, _disposed, _confirmed;
    public AudioOutputMode Mode { get; }
    public bool FellBackToMirror { get; private set; }
    public LocalAudioOutputState? SavedState { get { lock (_gate) return _saved; } }
    public bool IsMutedForProjector { get { lock (_gate) return _restorePending && !FellBackToMirror; } }

    public LocalAudioOutputController(AudioOutputMode mode) : this(mode, id => new WindowsEndpoint(id)) { }

    public LocalAudioOutputController(AudioOutputMode mode, Func<string, ILocalAudioEndpoint> open,
        Action<string>? log = null, TimeProvider? clock = null)
    {
        Mode = mode;
        _open = open;
        _log = log ?? Console.WriteLine;
        _clock = clock ?? TimeProvider.System;
        _log($"[Audio Output] Mode: {(mode == AudioOutputMode.Projector ? "Projector only" : "Mirror")}");
    }

    // The caller must have negotiated/connected audio, started capture, and sent an audio RTP packet.
    public void Activate(string captureEndpointId, AudioOutputActivity activity)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Mode == AudioOutputMode.Mirror || _saved is not null) return;
            if (activity.OpusFrames == 0 || activity.RtpPackets == 0)
                throw new InvalidOperationException("Projector mode cannot mute local output before capture/encoding and audio RTP start.");
            _endpoint = _open(captureEndpointId);
            if (_endpoint.Id != captureEndpointId) throw new InvalidOperationException("Output endpoint does not match the active WASAPI capture endpoint.");
            _log("[Audio Output] Saving local state");
            _saved = new(_endpoint.Id, _endpoint.Muted, _endpoint.Volume, _endpoint.VolumeDb);
            _restorePending = true; // Also restore if a native setter throws after changing state.
            AppDomain.CurrentDomain.ProcessExit += EmergencyRestore;
            AppDomain.CurrentDomain.UnhandledException += EmergencyRestore;
            try
            {
                _endpoint.Muted = true;
                if (!_endpoint.Muted) throw new InvalidOperationException("Windows did not mute the render endpoint.");
                _previous = activity;
                _mutedAt = _clock.GetTimestamp();
                _log("[Audio Output] Local endpoint muted");
                _log("[Audio Output] Windows local output muted");
                _log("[Audio Output] Google TV is now primary audio output");
            }
            catch { RestoreCore(); throw; }
        }
    }

    public void Observe(AudioOutputActivity activity)
    {
        lock (_gate)
        {
            if (!_restorePending || _disposed || FellBackToMirror) return;
            bool active = activity.CapturedSignal > _previous.CapturedSignal && activity.ConvertedSignal > _previous.ConvertedSignal &&
                activity.OpusFrames > _previous.OpusFrames && activity.RtpPackets > _previous.RtpPackets;
            bool hasGrace = _clock.GetElapsedTime(_mutedAt) >= TimeSpan.FromSeconds(1);
            if (active)
            {
                _lostSignalAt = null;
                if (!_confirmed && hasGrace)
                {
                    _confirmed = true;
                    _log("[Audio Output] Remote audio remains active");
                    _log("[Audio Output] Google TV audio active");
                }
            }
            else if (hasGrace && _endpoint!.RenderPeak > 0.00001f)
            {
                // Endpoint peak metering is before endpoint volume/mute. If playback
                // is active but capture/PCM/RTP stalls after mute, protect remote audio.
                _lostSignalAt ??= _clock.GetTimestamp();
                if (_clock.GetElapsedTime(_lostSignalAt.Value) >= TimeSpan.FromSeconds(2))
                {
                    RestoreCore();
                    FellBackToMirror = true;
                    _log("[Audio Output] Muted loopback lost active audio; automatically falling back to mirror mode");
                }
            }
            else _lostSignalAt = null; // Silence/paused playback is not evidence of broken capture.
            _previous = activity;
        }
    }

    public void Restore() { lock (_gate) RestoreCore(); }

    private void RestoreCore()
    {
        if (!_restorePending || _saved is not { } state || _endpoint is null) return;
        _log("[Audio Output] Restoring Windows audio state");
        // Preserve the original hardware dB step exactly; the scalar is saved for diagnostics.
        // Always attempt mute restoration, even if restoring volume fails.
        try { if (_endpoint.VolumeDb != state.VolumeDb) _endpoint.VolumeDb = state.VolumeDb; }
        finally { _endpoint.Muted = state.Muted; }
        _restorePending = false;
        _log($"[Audio Output] Restored mute={state.Muted.ToString().ToLowerInvariant()}, volume={state.Volume.ToString("R", CultureInfo.InvariantCulture)}");
    }

    private void EmergencyRestore(object? sender, EventArgs args)
    {
        try { Restore(); }
        catch (Exception error) { Console.Error.WriteLine($"[Audio Output] Emergency restoration failed: {error}"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { RestoreCore(); }
            finally
            {
                AppDomain.CurrentDomain.ProcessExit -= EmergencyRestore;
                AppDomain.CurrentDomain.UnhandledException -= EmergencyRestore;
                _disposed = true;
                _endpoint?.Dispose();
            }
        }
    }

    private sealed class WindowsEndpoint : ILocalAudioEndpoint
    {
        private readonly MMDevice _device;
        public WindowsEndpoint(string id)
        {
            using var devices = new MMDeviceEnumerator();
            _device = devices.GetDevice(id); // Retain the captured endpoint, even if the default later changes.
        }
        public string Id => _device.ID;
        public bool Muted { get => _device.AudioEndpointVolume.Mute; set => _device.AudioEndpointVolume.Mute = value; }
        public float Volume => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        public float VolumeDb { get => _device.AudioEndpointVolume.MasterVolumeLevel; set => _device.AudioEndpointVolume.MasterVolumeLevel = value; }
        public float RenderPeak => _device.AudioMeterInformation.MasterPeakValue;
        public void Dispose() => _device.Dispose();
    }
}
