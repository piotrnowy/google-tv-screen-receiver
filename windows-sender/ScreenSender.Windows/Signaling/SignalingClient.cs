using System.Net.Sockets;
using System.Text;

namespace ScreenSender.Windows.Signaling;

/// <summary>A single TCP session; create a fresh instance for each reconnect.</summary>
public sealed class SignalingClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _receiveTask;
    private int _disposed;
    public bool IsConnected => _writer is not null && _disposed == 0;

    public Func<AnswerMessage, CancellationToken, Task>? AnswerReceived { get; set; }
    public Func<IceMessage, CancellationToken, Task>? IceReceived { get; set; }
    public Action? ByeReceived { get; set; }
    public Action<VolumeStateMessage>? VolumeStateReceived { get; set; }
    public Action? ConnectionEnded { get; set; }
    public Action<string>? Error { get; set; }
    public Action<string>? Warning { get; set; }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Console.WriteLine($"[Signaling] Connecting to {host}:{port}...");
        await _tcp.ConnectAsync(host, port, timeout.Token);
        _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        var stream = _tcp.GetStream();
        var utf8 = new UTF8Encoding(false, true);
        _reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, utf8, leaveOpen: true) { NewLine = "\n" };
        Console.WriteLine("[Signaling] Connected");
    }

    public async Task SendAsync(SignalingMessage message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _writeGate.WaitAsync(linked.Token);
        try
        {
            var writer = _writer ?? throw new InvalidOperationException("TCP signaling is not connected.");
            var line = SignalingCodec.Encode(message);
            if (line.Length > SignalingCodec.MaxLineChars) throw new InvalidDataException("Signaling message is too large.");
            await writer.WriteLineAsync(line.AsMemory(), linked.Token);
            await writer.FlushAsync(linked.Token);
        }
        finally { _writeGate.Release(); }
    }

    public Task ReceiveAsync(CancellationToken cancellationToken) =>
        _receiveTask ??= ReceiveLoopAsync(cancellationToken);

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        var reader = _reader ?? throw new InvalidOperationException("TCP signaling is not connected.");
        var buffer = new char[4096];
        var line = new StringBuilder();
        try
        {
            while (true)
            {
                int length = await reader.ReadAsync(buffer.AsMemory(), token);
                if (length == 0) { Console.WriteLine("[Signaling] Receiver closed TCP connection"); return; }
                for (int i = 0; i < length; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        var text = line.ToString().TrimEnd('\r');
                        line.Clear();
                        if (!SignalingCodec.TryParse(text, out var message))
                        {
                            Warning?.Invoke("Ignored malformed or unknown signaling message.");
                            continue;
                        }
                        switch (message)
                        {
                            case AnswerMessage answer when AnswerReceived is not null:
                                await AnswerReceived(answer, token);
                                break;
                            case IceMessage ice when IceReceived is not null:
                                await IceReceived(ice, token);
                                break;
                            case VolumeStateMessage volumeState:
                                VolumeStateReceived?.Invoke(volumeState);
                                break;
                            case ByeMessage:
                                ByeReceived?.Invoke();
                                return;
                            default: Warning?.Invoke($"Ignored unexpected {message!.Type} message from receiver."); break;
                        }
                    }
                    else
                    {
                        if (line.Length >= SignalingCodec.MaxLineChars) throw new InvalidDataException("Signaling line exceeds receiver's size limit.");
                        line.Append(buffer[i]);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or SocketException or DecoderFallbackException)
        {
            if (!token.IsCancellationRequested) Error?.Invoke($"Signaling read failed: {error.Message}");
        }
        finally { ConnectionEnded?.Invoke(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        _tcp.Dispose(); // Also interrupts a pending read/write on network loss or shutdown.
        try
        {
            if (_receiveTask is not null) await _receiveTask;
        }
        finally
        {
            await _writeGate.WaitAsync();
            try
            {
                _reader?.Dispose();
                try { _writer?.Dispose(); }
                catch (Exception error) when (error is IOException or ObjectDisposedException) { /* Socket is already closed. */ }
            }
            finally { _writeGate.Release(); _lifetime.Dispose(); }
        }
    }
}
