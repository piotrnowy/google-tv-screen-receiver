using ScreenSender.Windows.Configuration;

namespace ScreenSender.Windows;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"]) { Console.WriteLine(SenderOptions.Usage); return 0; }
        using var lifetime = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += cancel;
        try { return (await StreamingSession.RunAsync(SenderOptions.Parse(args), lifetime.Token)).ExitCode; }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[Error] {error.Message}");
            if (error is ArgumentException) Console.Error.WriteLine(SenderOptions.Usage);
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
