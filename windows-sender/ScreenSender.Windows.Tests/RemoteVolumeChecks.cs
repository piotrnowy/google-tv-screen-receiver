using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Signaling;

internal static class RemoteVolumeChecks
{
    public static Task Messages()
    {
        var cases = new (string Json, SignalingMessage Message)[]
        {
            ("{\"type\":\"volume\",\"action\":\"up\"}", new VolumeActionMessage(RemoteVolumeAction.Up)),
            ("{\"type\":\"volume\",\"action\":\"down\"}", new VolumeActionMessage(RemoteVolumeAction.Down)),
            ("{\"type\":\"volume\",\"action\":\"muteToggle\"}", new VolumeActionMessage(RemoteVolumeAction.MuteToggle)),
            ("{\"type\":\"getVolume\"}", new GetVolumeMessage()),
            ("{\"type\":\"volumeState\",\"current\":8,\"max\":15,\"muted\":false}", new VolumeStateMessage(8, 15, false)),
            ("{\"type\":\"volumeState\",\"current\":0,\"max\":0,\"muted\":true,\"available\":false}", new VolumeStateMessage(0, 0, true, false)),
        };
        foreach (var (json, expected) in cases)
            Check(SignalingCodec.TryParse(json, out var actual) && actual == expected, $"Could not parse {json}");
        foreach (var action in new[] { RemoteVolumeAction.Up, RemoteVolumeAction.Down, RemoteVolumeAction.MuteToggle })
            Check(SignalingCodec.TryParse(SignalingCodec.Encode(new VolumeActionMessage(action)), out var parsed) && parsed == new VolumeActionMessage(action), "Volume action did not round-trip");
        Check(!SignalingCodec.TryParse("{\"type\":\"volume\",\"action\":\"reset\"}", out _), "Unsupported action accepted");
        Check(!SignalingCodec.TryParse("{\"type\":\"volume\",\"action\":\"reset\",\"value\":0.5}", out _), "Unsupported action fell back to legacy absolute volume");
        Check(!SignalingCodec.TryParse("{\"type\":\"volumeState\",\"current\":1,\"max\":10,\"muted\":false,\"available\":1}", out _), "Malformed availability accepted");
        using var state = JsonDocument.Parse(SignalingCodec.Encode(new VolumeStateMessage(8, 15, false)));
        Check(state.RootElement.GetProperty("current").GetInt32() == 8, "volumeState serialization missing current value");
        return Task.CompletedTask;
    }

    public static async Task KeysAndLifecycle()
    {
        Check(RemoteVolumeKeyController.ShouldEnable(AudioOutputMode.Projector, true, true), "Projector remote volume did not enable");
        Check(!RemoteVolumeKeyController.ShouldEnable(AudioOutputMode.Mirror, true, true), "Mirror mode intercepted media keys");
        Check(!RemoteVolumeKeyController.ShouldEnable(AudioOutputMode.Projector, false, true), "Video-only mode intercepted media keys");
        Check(!RemoteVolumeKeyController.ShouldEnable(AudioOutputMode.Projector, true, false), "remote-volume=false intercepted media keys");
        Check(RemoteVolumeKeyController.MapVirtualKey(0xAF) == RemoteVolumeAction.Up, "VK_VOLUME_UP mapping failed");
        Check(RemoteVolumeKeyController.MapVirtualKey(0xAE) == RemoteVolumeAction.Down, "VK_VOLUME_DOWN mapping failed");
        Check(RemoteVolumeKeyController.MapVirtualKey(0xAD) == RemoteVolumeAction.MuteToggle, "VK_VOLUME_MUTE mapping failed");
        Check(RemoteVolumeKeyController.MapVirtualKey(0x41) is null, "Non-volume key was mapped");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var signaling = new SignalingClient();
        var hook = new FakeHook();
        var controller = new RemoteVolumeKeyController(AudioOutputMode.Projector, true, true, hook);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await signaling.ConnectAsync("127.0.0.1", port, timeout.Token);
            using var accepted = await accept.WaitAsync(timeout.Token);
            using var reader = new StreamReader(accepted.GetStream());
            using var writer = new StreamWriter(accepted.GetStream()) { AutoFlush = true, NewLine = "\n" };
            await controller.ActivateAsync(signaling, timeout.Token);
            Check(controller.IsActive && hook.Started, "Hook did not activate with a connected signaling session");
            Check(await reader.ReadLineAsync(timeout.Token) == "{\"type\":\"getVolume\"}", "Initial volume state was not requested");
            hook.Emit(RemoteVolumeAction.Up);
            Check(await reader.ReadLineAsync(timeout.Token) == "{\"type\":\"volume\",\"action\":\"up\"}", "Hook action was not sent over the existing signaling socket");
            controller.Deactivate();
            Check(!controller.IsActive && hook.Stopped, "Hook did not stop on disconnect lifecycle");
        }
        finally
        {
            await controller.DisposeAsync();
            await signaling.DisposeAsync();
            listener.Stop();
        }
    }

    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

    private sealed class FakeHook : IRemoteVolumeKeyHook
    {
        private Action<RemoteVolumeAction>? _action;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public void Start(Action<RemoteVolumeAction> onAction) { _action = onAction; Started = true; Stopped = false; }
        public void Emit(RemoteVolumeAction action) => _action?.Invoke(action);
        public void Stop() { Stopped = true; Started = false; }
        public void Dispose() => Stop();
    }
}
