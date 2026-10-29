using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.Capture;

public interface IScreenCapture
{
    event Action<uint, byte[]>? EncodedFrame;
    VideoFormat Format { get; }
    Task<string> Failure { get; }
    Task ConfigureAnswerAsync(string? parameters, CancellationToken token);
    Task StartAsync(CancellationToken token);
    Task StopAsync();
    void RequestKeyFrame();
}
