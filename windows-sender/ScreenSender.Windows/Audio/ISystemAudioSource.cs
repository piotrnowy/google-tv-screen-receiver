using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.Audio;

public interface ISystemAudioSource
{
    event Action<uint, byte[]>? EncodedFrame;
    AudioFormat Format { get; }
    Task<string> Failure { get; }
    string RenderEndpointId { get; }
    AudioOutputActivity OutputActivity { get; }
    void SetFormat(AudioFormat format);
    Task StartAsync(CancellationToken token);
    Task StopAsync();
}
