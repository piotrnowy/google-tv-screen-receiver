using System.Net;
using System.Net.Sockets;
using System.Text;
using Makaretu.Dns;

namespace ScreenSender.Windows.Discovery;

public sealed record ReceiverDevice(string Id, string Name, string Host, int Port, bool Busy)
{
    public string Endpoint => $"{Host}:{Port}";
}

/// <summary>Bounded DNS-SD cache. No TCP probes: browsing must not interrupt a media session.</summary>
public sealed class ReceiverDirectory
{
    public const string ServiceType = "_screenreceiver._tcp.local";
    private const int Limit = 128;
    private readonly Dictionary<string, Service> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Host, string Address), (IPAddress Address, DateTimeOffset Expires)> _addresses = [];
    private sealed class Service
    {
        public DateTimeOffset Seen, PtrUntil, SrvUntil, TxtUntil;
        public string Target = "", Id = "", Name = "";
        public int Port;
        public bool Busy;
    }

    private static string Key(DomainName name) => name.ToString().TrimEnd('.').ToLowerInvariant();
    private static bool Instance(string name) => name.EndsWith("." + ServiceType, StringComparison.OrdinalIgnoreCase);
    private static DateTimeOffset Until(DateTimeOffset now, TimeSpan ttl) =>
        now + TimeSpan.FromSeconds(Math.Clamp(ttl.TotalSeconds, 0, 30));

    public void Apply(Message message, DateTimeOffset now)
    {
        Prune(now);
        foreach (var record in message.Answers.Concat(message.AdditionalRecords).Take(512))
        {
            string key = Key(record.Name);
            if (record is ARecord address)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address.Address) ||
                    address.Address.Equals(IPAddress.Any) || address.Address.GetAddressBytes()[0] >= 224) continue;
                var addressKey = (key, address.Address.ToString());
                if (record.TTL <= TimeSpan.Zero) _addresses.Remove(addressKey);
                else if (_addresses.Count < Limit * 4 || _addresses.ContainsKey(addressKey))
                    _addresses[addressKey] = (address.Address, Until(now, record.TTL));
                continue;
            }
            if (record is PTRRecord ptr)
            {
                if (key != ServiceType) continue;
                key = Key(ptr.DomainName);
            }
            if (!Instance(key) || record is not (PTRRecord or SRVRecord or TXTRecord)) continue;
            if (record.TTL <= TimeSpan.Zero) { _services.Remove(key); continue; }
            if (!_services.TryGetValue(key, out var service))
            {
                if (_services.Count >= Limit) continue;
                _services[key] = service = new Service();
            }
            service.Seen = now;
            switch (record)
            {
                case PTRRecord: service.PtrUntil = Until(now, record.TTL); break;
                case SRVRecord srv:
                    service.Target = Key(srv.Target);
                    service.Port = srv.Port;
                    service.SrvUntil = Until(now, record.TTL);
                    break;
                case TXTRecord txt:
                    var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    bool valid = txt.Strings.Count <= 16;
                    foreach (string text in txt.Strings.Take(16))
                    {
                        int equals = text.IndexOf('=');
                        if (equals <= 0 || text.Length > 255 || !properties.TryAdd(text[..equals], text[(equals + 1)..])) { valid = false; break; }
                    }
                    string name = DecodeName(properties.GetValueOrDefault("name64", ""));
                    string id = properties.GetValueOrDefault("id", "");
                    valid &= properties.GetValueOrDefault("protocol") == "1" && Guid.TryParse(id, out _) &&
                        name.Length is > 0 and <= 96 && !name.Any(char.IsControl) &&
                        properties.GetValueOrDefault("busy") is "0" or "1";
                    service.Id = id;
                    service.Name = name;
                    service.Busy = properties.GetValueOrDefault("busy") == "1";
                    service.TxtUntil = valid ? Until(now, record.TTL) : now;
                    break;
            }
        }
    }

    public IReadOnlyList<ReceiverDevice> Snapshot(DateTimeOffset now)
    {
        Prune(now);
        return _services.Values.Where(s => s.PtrUntil > now && s.SrvUntil > now && s.TxtUntil > now && s.Port is > 0 and <= 65535)
            .Select(s => (Service: s, Addresses: _addresses.Where(a => a.Key.Host == s.Target && a.Value.Expires > now)
                .Select(a => a.Value.Address).OrderByDescending(a => IsPrivate(a)).ThenBy(a => a.ToString(), StringComparer.Ordinal).ToList()))
            .Where(x => x.Addresses.Count > 0)
            .Select(x => new ReceiverDevice(x.Service.Id, x.Service.Name, x.Addresses[0].ToString(), x.Service.Port, x.Service.Busy))
            .DistinctBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Endpoint, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<(string Name, DnsType Type)> Questions(DateTimeOffset now)
    {
        Prune(now);
        return _services.Select(s => (s.Key, DnsType.ANY))
            .Concat(_services.Values.Where(s => s.SrvUntil > now).Select(s => (s.Target, DnsType.A)))
            .Distinct().Take(Limit).ToArray();
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31;
    }

    private static string DecodeName(string encoded)
    {
        if (encoded.Length is 0 or > 240) return "";
        try { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded)).Trim(); }
        catch (Exception error) when (error is FormatException or DecoderFallbackException) { return ""; }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _services.Where(s => now - s.Value.Seen >= TimeSpan.FromSeconds(30)).Select(s => s.Key).ToArray()) _services.Remove(key);
        foreach (var key in _addresses.Where(a => a.Value.Expires <= now).Select(a => a.Key).ToArray()) _addresses.Remove(key);
    }
}
