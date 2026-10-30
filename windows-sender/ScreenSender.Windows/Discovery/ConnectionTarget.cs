namespace ScreenSender.Windows.Discovery;

public sealed record ConnectionTarget(string Host, int Port)
{
    public static ConnectionTarget Parse(string value, int defaultPort = 53530)
    {
        string text = value.Trim();
        if (text.Contains("://", StringComparison.Ordinal) || !Uri.TryCreate("tcp://" + text, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath is not ("" or "/") || uri.Port == 0 || uri.Port > 65535 ||
            Uri.CheckHostName(uri.Host.Trim('[', ']')) == UriHostNameType.Unknown)
            throw new ArgumentException("Enter the TV's IP address or hostname, optionally followed by :port.");
        int port = uri.Port == -1 ? defaultPort : uri.Port;
        if (port is < 1 or > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
        return new(uri.Host.Trim('[', ']'), port);
    }
}
