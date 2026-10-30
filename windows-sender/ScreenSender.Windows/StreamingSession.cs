using ScreenSender.Windows.Capture;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Signaling;
using ScreenSender.Windows.WebRtc;

namespace ScreenSender.Windows;

public static class StreamingSession
{
    public static async Task<StreamingResult> RunAsync(SenderOptions options, CancellationToken token, IProgress<StreamingUpdate>? progress = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        SignalingClient? signaling = null;
        ScreenCaptureService? capture = null;
        SystemAudioLoopbackSource? audio = null;
        LocalAudioOutputController? audioOutput = null;
        RemoteVolumeKeyController? remoteVolume = null;
        WebRtcScreenSender? sender = null;
        Task? sending = null;
        int exitCode = 0;
        string? failure = null;
        try
        {
            progress?.Report(new(StreamingPhase.Connecting, $"Connecting to {options.Host}:{options.Port}"));
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop capture requires Windows 11.");
            var monitor = WindowsMonitor.Select(options.Monitor);
            Console.WriteLine($"""
                Screen Sender

                Google TV:
                {options.Host}:{options.Port}

                Video:
                Up to {options.Width}x{options.Height} @ {options.Fps} fps H264

                Audio:
                {(options.Audio ? "Opus 48 kHz stereo" : "Disabled")}

                Audio output:
                {(options.Audio ? (options.AudioOutput == AudioOutputMode.Projector ? "Projector only" : "Mirror") : "Disabled")}

                Remote volume:
                {(options.Audio && options.AudioOutput == AudioOutputMode.Projector && options.RemoteVolume ? "Enabled" : "Disabled")}

                Connecting...
                """);
            _ = FFmpegRuntime.ResolveSharedLibraryDirectory();
            signaling = new SignalingClient
            {
                Error = message => { Console.Error.WriteLine($"[Signaling] {message}"); failure = message; exitCode = 1; },
                Warning = message => Console.Error.WriteLine($"[Signaling] {message}"),
                ByeReceived = () => Console.WriteLine("[Signaling] Receiver sent bye"),
            };
            await signaling.ConnectAsync(options.Host, options.Port, lifetime.Token);
            await signaling.SendAsync(new HelloMessage(Environment.MachineName), lifetime.Token);
            capture = new ScreenCaptureService(monitor, options);
            await capture.InitializeAsync(lifetime.Token);
            if (options.Audio)
            {
                audio = new SystemAudioLoopbackSource(options.AudioDebug);
                await audio.InitializeAsync(lifetime.Token);
                audioOutput = new LocalAudioOutputController(options.AudioOutput);
                if (options.AudioOutput == AudioOutputMode.Projector && options.RemoteVolume)
                    remoteVolume = new RemoteVolumeKeyController(options.AudioOutput, audioEnabled: true, remoteVolumeEnabled: true);
                else Console.WriteLine("[Volume] Remote Windows media-key control disabled");
            }
            else Console.WriteLine("[Audio] System audio capture disabled");
            sender = new WebRtcScreenSender(capture, signaling, audio: audio, audioOutput: audioOutput, remoteVolume: remoteVolume);
            sender.StreamingStarted = () => progress?.Report(new(StreamingPhase.Streaming, "Your screen is being shared"));
            signaling.ConnectionEnded = () => remoteVolume?.Deactivate();
            bool initialVolumeState = true;
            signaling.VolumeStateReceived = state =>
            {
                if (!state.Available) Console.WriteLine("[Volume] Google TV uses fixed-volume output; remote volume control unavailable");
                else if (initialVolumeState) Console.WriteLine($"[Volume] Google TV initial volume: {state.Current}/{state.Max}");
                else if (state.Muted) Console.WriteLine($"[Volume] Google TV muted=true ({state.Current}/{state.Max})");
                else Console.WriteLine($"[Volume] Google TV: {state.Current}/{state.Max}");
                initialVolumeState = false;
            };
            signaling.AnswerReceived = sender.HandleAnswerAsync;
            signaling.IceReceived = sender.HandleIceAsync;
            var receiving = signaling.ReceiveAsync(lifetime.Token);
            sending = sender.RunAsync(lifetime.Token);
            var finished = await Task.WhenAny(sending, receiving);
            await finished;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            Console.WriteLine("[App] Stopping...");
        }
        catch (Exception error)
        {
            failure = error.Message;
            progress?.Report(new(StreamingPhase.Failed, error.Message));
            Console.Error.WriteLine($"[Error] {error.Message}");
            if (error.InnerException is not null) Console.Error.WriteLine($"[Error] {error.InnerException.Message}");
            if (error is ArgumentException) Console.Error.WriteLine(SenderOptions.Usage);
            exitCode = 1;
        }
        finally
        {
            await lifetime.CancelAsync();
            remoteVolume?.Deactivate();
            if (audioOutput is not null) await Cleanup(() => { audioOutput.Dispose(); return Task.CompletedTask; }, "Audio Output");
            if (sending is not null)
            {
                try { await sending; }
                catch (Exception) { /* Observed above or interrupted by signaling EOF/Ctrl+C. */ }
            }
            if (sender is not null) await Cleanup(() => sender.DisposeAsync().AsTask(), "WebRTC");
            else if (remoteVolume is not null) await Cleanup(() => remoteVolume.DisposeAsync().AsTask(), "Remote Volume");
            if (audio is not null) await Cleanup(() => audio.DisposeAsync().AsTask(), "Audio");
            if (signaling is not null)
            {
                if (signaling.IsConnected)
                {
                    using var byeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await signaling.SendAsync(new ByeMessage(), byeTimeout.Token); }
                    catch (Exception error) { Console.WriteLine($"[Signaling] Could not send bye: {error.Message}"); }
                }
                await Cleanup(() => signaling.DisposeAsync().AsTask(), "Signaling");
            }
            if (capture is not null) await Cleanup(() => capture.DisposeAsync().AsTask(), "Capture");

        }
        return new(exitCode, failure);
    }

    private static async Task Cleanup(Func<Task> dispose, string component)
    {
        try { await dispose(); }
        catch (Exception error) { Console.Error.WriteLine($"[{component}] Cleanup: {error.Message}"); }
    }
}

public enum StreamingPhase { Connecting, Streaming, Failed }
public sealed record StreamingUpdate(StreamingPhase Phase, string Message);
public sealed record StreamingResult(int ExitCode, string? Error);
