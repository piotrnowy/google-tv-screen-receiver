using ScreenSender.Windows.Configuration;

namespace ScreenSender.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        SenderOptions options;
        string? configurationError = null;
        try { options = SenderOptions.Parse(args, requireHost: false); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException)
        {
            options = new SenderOptions();
            configurationError = error.Message;
        }
        Application.Run(new ConnectionWindow(options, configurationError));
    }
}
