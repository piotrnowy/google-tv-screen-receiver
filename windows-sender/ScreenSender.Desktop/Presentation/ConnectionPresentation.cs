using ScreenSender.Windows;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Discovery;

namespace ScreenSender.Desktop.Presentation;

internal enum ConnectionPhase { Ready, Connecting, Connected, Stopping, Error }

internal sealed record SharingFailure(string Title, string Help)
{
    public static SharingFailure From(string? technicalMessage, bool wasSharing)
    {
        string message = technicalMessage?.ToLowerInvariant() ?? "";
        string title = wasSharing ? "Screen sharing stopped" : "Screen sharing couldn't start";
        if (message.Contains("shared libraries") || message.Contains("ffmpeg_root") || message.Contains("missing: avcodec"))
            return new(title, "A component needed for screen sharing is missing. Open Technical details for setup information.");
        if (new[] { "capture", "ffmpeg", "encoder", "monitor", "desktop" }.Any(message.Contains))
            return new(title, "We couldn't start capturing your screen. Try again or open Technical details for more information.");
        if (new[] { "connect", "timeout", "timed out", "socket", "network", "host" }.Any(message.Contains))
            return new(title, "We couldn't reach the TV or projector. Make sure it's on and connected to the same network, then try again.");
        return new(title, "Make sure the receiver is ready, then try again. Open Technical details if you need more information.");
    }
}

/// <summary>UI adapters retain the existing discovery and streaming implementations.</summary>
internal sealed record DesktopServices(
    Func<IProgress<IReadOnlyList<ReceiverDevice>>, CancellationToken, Task> Browse,
    Action Refresh,
    Func<SenderOptions, CancellationToken, IProgress<StreamingUpdate>?, Task<StreamingResult>> Share,
    Action<string> CopyLogs)
{
    public static DesktopServices Create()
    {
        var discovery = new ReceiverDiscovery();
        return new(discovery.RunAsync, discovery.Refresh, StreamingSession.RunAsync, Clipboard.SetText);
    }
}
