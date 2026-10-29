using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Configuration;

internal static class LocalAudioOutputChecks
{
    private static readonly AudioOutputActivity Active = new(10, 10, 10, 10);
    public static Task Parsing()
    {
        Assert(SenderOptions.Parse(["--host", "tv"]).AudioOutput == AudioOutputMode.Projector, "Projector must now be the app default");
        Assert(SenderOptions.Parse(["--host", "tv", "--audio-output", "mirror"]).AudioOutput == AudioOutputMode.Mirror, "CLI mirror override was ignored");
        Assert(SenderOptions.Parse(["--host", "tv", "--audio-output", "projector"]).AudioOutput == AudioOutputMode.Projector, "Projector option not parsed");
        bool invalid = false;
        try { SenderOptions.Parse(["--host", "tv", "--audio-output", "speakers"]); } catch (ArgumentException) { invalid = true; }
        Assert(invalid, "Invalid output mode accepted");
        return Task.CompletedTask;
    }

    public static Task Mirror()
    {
        int opens = 0;
        using var controller = new LocalAudioOutputController(AudioOutputMode.Mirror, _ => { opens++; return new Endpoint(); }, _ => { });
        controller.Activate("capture-endpoint", default);
        controller.Observe(Active);
        controller.Restore();
        Assert(opens == 0 && controller.SavedState is null && !controller.IsMutedForProjector, "Mirror must never open or change endpoint volume/mute");
        return Task.CompletedTask;
    }

    public static Task SavesAndMutes()
    {
        var endpoint = new Endpoint();
        using var controller = New(endpoint);
        controller.Activate(endpoint.Id, Active);
        Assert(endpoint.Muted, "Projector mode did not mute endpoint");
        Assert(controller.SavedState == new LocalAudioOutputState(endpoint.Id, false, 0.62f, -18), "Original endpoint ID/mute/volume was not captured");
        Assert(endpoint.VolumeWrites == 0, "Muting must not change master volume");
        controller.Activate(endpoint.Id, Active); // A repeated callback must not overwrite the original state.
        controller.Restore();
        Assert(!endpoint.Muted && endpoint.Volume == 0.62f, "Repeated activation lost original state");
        return Task.CompletedTask;
    }

    public static Task RestoresPriorMuteAndVolume()
    {
        foreach (bool alreadyMuted in new[] { false, true })
        {
            var endpoint = new Endpoint(alreadyMuted);
            var controller = New(endpoint);
            controller.Activate(endpoint.Id, Active);
            endpoint.VolumeDb = -60;
            controller.Dispose();
            controller.Dispose();
            Assert(endpoint.Muted == alreadyMuted, "Cleanup must restore the previous mute value, never blindly unmute");
            Assert(endpoint.VolumeDb == -18 && endpoint.Volume == 0.62f, "Original hardware volume step was not restored");
            Assert(endpoint.Disposed, "Endpoint handle was not disposed");
        }
        return Task.CompletedTask;
    }

    public static Task ExceptionCleanup()
    {
        var endpoint = new Endpoint();
        try
        {
            using var controller = New(endpoint);
            controller.Activate(endpoint.Id, Active);
            throw new InvalidOperationException("Simulated streaming exception");
        }
        catch (InvalidOperationException) { }
        Assert(!endpoint.Muted && endpoint.Volume == 0.62f && endpoint.Disposed, "Exception cleanup failed");
        var setterFailure = new Endpoint { ThrowAfterNextMute = true };
        using var failedController = New(setterFailure);
        bool failed = false;
        try { failedController.Activate(setterFailure.Id, Active); } catch (InvalidOperationException) { failed = true; }
        Assert(failed && !setterFailure.Muted, "A partially successful mute setter must be rolled back");
        return Task.CompletedTask;
    }

    public static async Task CancellationCleanup()
    {
        var endpoint = new Endpoint();
        using var cancellation = new CancellationTokenSource();
        try
        {
            using var controller = New(endpoint);
            controller.Activate(endpoint.Id, Active);
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        Assert(!endpoint.Muted && endpoint.Volume == 0.62f && endpoint.Disposed, "Cancellation cleanup failed");
    }

    public static Task CaptureSafetyFallback()
    {
        var endpoint = new Endpoint();
        var clock = new Clock();
        var logs = new List<string>();
        using var controller = new LocalAudioOutputController(AudioOutputMode.Projector, _ => endpoint, logs.Add, clock);
        controller.Activate(endpoint.Id, Active);
        clock.Advance(2);
        controller.Observe(new(20, 20, 20, 20));
        Assert(logs.Any(line => line.Contains("Remote audio remains active")), "Muted capture/RTP activity was not confirmed");
        endpoint.RenderPeak = 0;
        clock.Advance(5);
        controller.Observe(new(20, 20, 30, 30));
        Assert(controller.IsMutedForProjector, "Paused/silent playback must not force mirror mode");
        endpoint.RenderPeak = 0.25f;
        clock.Advance(1);
        controller.Observe(new(20, 20, 40, 40));
        clock.Advance(2);
        controller.Observe(new(20, 20, 50, 50));
        Assert(controller.FellBackToMirror && !endpoint.Muted && endpoint.Volume == 0.62f, "Active playback lost in muted loopback must restore mirror mode");
        return Task.CompletedTask;
    }

    public static Task NoPrematureMute()
    {
        var endpoint = new Endpoint();
        using var controller = New(endpoint);
        bool refused = false;
        try { controller.Activate(endpoint.Id, Active with { RtpPackets = 0 }); } catch (InvalidOperationException) { refused = true; }
        Assert(refused && !endpoint.Muted && controller.SavedState is null, "No local mute is allowed before encoded audio/RTP starts");
        return Task.CompletedTask;
    }

    private static LocalAudioOutputController New(Endpoint endpoint) => new(AudioOutputMode.Projector, _ => endpoint, _ => { });
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    internal sealed class Endpoint(bool muted = false) : ILocalAudioEndpoint
    {
        public string Id => "capture-endpoint";
        private bool _muted = muted;
        private float _volumeDb = -18;
        public bool ThrowAfterNextMute { get; set; }
        public bool Muted
        {
            get => _muted;
            set { _muted = value; if (ThrowAfterNextMute) { ThrowAfterNextMute = false; throw new InvalidOperationException("Simulated native setter failure after muting"); } }
        }
        public float Volume => (_volumeDb + 80) / 100;
        public int VolumeWrites { get; private set; }
        public float VolumeDb { get => _volumeDb; set { _volumeDb = value; VolumeWrites++; } }
        public float RenderPeak { get; set; } = 0.25f;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
