using System.Net;
using Makaretu.Dns;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Discovery;

internal static class DiscoveryChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private const string Id = "307934ef-2f79-4079-a208-112b65b54aa0";

    public static Task Discovery()
    {
        var directory = new ReceiverDirectory();
        var records = Announcement("one", Id, "Living room 📺", "192.0.2.10");
        var wireMessage = new Message();
        wireMessage.Read(Message(records).ToByteArray());
        var wireDirectory = new ReceiverDirectory();
        wireDirectory.Apply(wireMessage, Now);
        Check(wireDirectory.Snapshot(Now).Single().Name == "Living room 📺", "Unicode names must survive the installed DNS library's ASCII-only wire parser");
        directory.Apply(Message(records.Take(1)), Now);
        directory.Apply(Message(records.Skip(1).Take(2)), Now);
        Check(directory.Snapshot(Now).Count == 0, "An unresolved service must not appear as available");
        directory.Apply(Message(records.Skip(3)), Now);
        var one = directory.Snapshot(Now).Single();
        Check(one.Name == "Living room 📺" && one.Host == "192.0.2.10" && one.Port == 53530 && !one.Busy, "Fragmented DNS-SD records did not resolve");
        Check(directory.Questions(Now).Any(q => q.Type == DnsType.A), "Missing host refresh query");

        directory.Apply(Message(Announcement("two", "9c68d342-c837-4749-8ffc-29ed168d8345", "Living room 📺", "192.0.2.11")), Now);
        Check(directory.Snapshot(Now).Count == 2, "Same-name receivers must remain independently selectable");
        directory.Apply(Message(Announcement("one", Id, "Bedroom", "192.0.2.10", true)), Now.AddSeconds(1));
        one = directory.Snapshot(Now.AddSeconds(1)).Single(d => d.Id == Id);
        Check(one.Name == "Bedroom" && one.Busy, "Rename/busy updates were lost");
        directory.Apply(Message(Announcement("copy", Id, "Bedroom", "192.0.2.10", true)), Now.AddSeconds(1));
        Check(directory.Snapshot(Now.AddSeconds(1)).Count == 2, "Duplicate announcements of one identity were not deduplicated");

        Check(directory.Snapshot(Now.AddSeconds(32)).Count == 0, "Stale receivers must expire even with a large advertised DNS TTL");
        directory.Apply(Message(records), Now.AddSeconds(33));
        directory.Apply(Message([new PTRRecord { Name = ReceiverDirectory.ServiceType, DomainName = "one." + ReceiverDirectory.ServiceType, TTL = TimeSpan.Zero }]), Now.AddSeconds(34));
        Check(directory.Snapshot(Now.AddSeconds(34)).Count == 0, "Goodbye did not remove receiver");

        var invalid = Announcement("invalid", Id, "Bad\nname", "192.0.2.20");
        directory.Apply(Message(invalid), Now.AddSeconds(35));
        Check(directory.Snapshot(Now.AddSeconds(35)).Count == 0, "Invalid advertised name accepted");
        var badProtocol = Announcement("wrong", Id, "Wrong protocol", "192.0.2.21");
        ((TXTRecord)badProtocol[2]).Strings = ["protocol=99", "id=" + Id, "name64=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Wrong protocol")), "busy=0"];
        directory.Apply(Message(badProtocol), Now.AddSeconds(35));
        Check(directory.Snapshot(Now.AddSeconds(35)).Count == 0, "Incompatible discovery protocol accepted");
        return Task.CompletedTask;
    }

    public static Task ManualAndDefaults()
    {
        Check(ConnectionTarget.Parse(" 192.0.2.10:1234 ") == new ConnectionTarget("192.0.2.10", 1234), "Manual endpoint failed");
        Check(ConnectionTarget.Parse("tv.local") == new ConnectionTarget("tv.local", 53530), "Hostname default port failed");
        Check(ConnectionTarget.Parse("[fd00::1]:53530").Host == "fd00::1", "Manual IPv6 failed");
        foreach (string invalid in new[] { "", "http://tv", "tv/path", "tv?key=x", "user@tv", "tv:0", "tv:65536" })
        {
            bool failed = false;
            try { ConnectionTarget.Parse(invalid); } catch (ArgumentException) { failed = true; }
            Check(failed, "Invalid endpoint accepted");
        }
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var options = SenderOptions.Parse([], missing, requireHost: false);
        Check(options.Host == "" && options.Audio && options.RemoteVolume, "GUI defaults must allow discovery before choosing a host");
        bool required = false;
        try { SenderOptions.Parse([], missing); } catch (ArgumentException) { required = true; }
        Check(required, "CLI host-required behavior must remain intact");
        return Task.CompletedTask;
    }

    public static ResourceRecord[] Announcement(string instance, string id, string name, string address, bool busy = false)
    {
        string full = instance + "." + ReceiverDirectory.ServiceType;
        string host = instance + ".local";
        return [
            new PTRRecord { Name = ReceiverDirectory.ServiceType, DomainName = full, TTL = TimeSpan.FromMinutes(75) },
            new SRVRecord { Name = full, Target = host, Port = 53530, TTL = TimeSpan.FromSeconds(120) },
            new TXTRecord { Name = full, Strings = ["protocol=1", "id=" + id, "name64=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name)), "busy=" + (busy ? "1" : "0")], TTL = TimeSpan.FromMinutes(75) },
            new ARecord { Name = host, Address = IPAddress.Parse(address), TTL = TimeSpan.FromSeconds(120) }
        ];
    }

    private static Message Message(IEnumerable<ResourceRecord> records)
    {
        var message = new Message();
        foreach (var record in records) message.AdditionalRecords.Add(record);
        return message;
    }
    private static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
}
