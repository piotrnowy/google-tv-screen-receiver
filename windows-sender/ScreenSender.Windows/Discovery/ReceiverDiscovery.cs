using Makaretu.Dns;

namespace ScreenSender.Windows.Discovery;

public sealed class ReceiverDiscovery
{
    private readonly object _gate = new();
    private readonly ReceiverDirectory _directory = new();
    private int _refresh = 1;
    public void Refresh() => Interlocked.Exchange(ref _refresh, 1);

    public async Task RunAsync(IProgress<IReadOnlyList<ReceiverDevice>> progress, CancellationToken token)
    {
        using var mdns = new MulticastService { UseIpv4 = true, UseIpv6 = false };
        mdns.AnswerReceived += (_, args) => { lock (_gate) _directory.Apply(args.Message, DateTimeOffset.UtcNow); };
        mdns.Start();
        try
        {
            IReadOnlyList<ReceiverDevice> previous = [];
            int ticks = 0;
            while (!token.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref _refresh, 0) != 0 || ticks++ % 5 == 0)
                {
                    mdns.SendQuery(ReceiverDirectory.ServiceType, DnsClass.IN, DnsType.PTR);
                    IReadOnlyList<(string Name, DnsType Type)> questions;
                    lock (_gate) questions = _directory.Questions(DateTimeOffset.UtcNow);
                    foreach (var question in questions) mdns.SendQuery(question.Name, DnsClass.IN, question.Type);
                }
                IReadOnlyList<ReceiverDevice> devices;
                lock (_gate) devices = _directory.Snapshot(DateTimeOffset.UtcNow);
                if (!devices.SequenceEqual(previous)) { previous = devices; progress.Report(devices); }
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        finally { mdns.Stop(); }
    }
}
