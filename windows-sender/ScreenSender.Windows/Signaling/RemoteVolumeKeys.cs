using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading.Channels;

namespace ScreenSender.Windows.Signaling;

public interface IRemoteVolumeKeyHook : IDisposable
{
    void Start(Action<RemoteVolumeAction> onAction);
    void Stop();
}

/// <summary>Routes key actions through a channel; no signaling IO runs on the native hook thread.</summary>
public sealed class RemoteVolumeKeyController : IAsyncDisposable
{
    private readonly IRemoteVolumeKeyHook _hook;
    private readonly Channel<RemoteVolumeAction> _actions = Channel.CreateUnbounded<RemoteVolumeAction>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private volatile bool _active;
    private int _disposed;

    public RemoteVolumeKeyController(Audio.AudioOutputMode mode, bool audioEnabled, bool remoteVolumeEnabled, IRemoteVolumeKeyHook? hook = null)
    {
        Enabled = audioEnabled && mode == Audio.AudioOutputMode.Projector && remoteVolumeEnabled;
        _hook = hook ?? new WindowsVolumeKeyHook();
    }

    public bool Enabled { get; }
    public bool IsActive => _active;

    public static RemoteVolumeAction? MapVirtualKey(int virtualKey) => virtualKey switch
    {
        0xAF => RemoteVolumeAction.Up,
        0xAE => RemoteVolumeAction.Down,
        0xAD => RemoteVolumeAction.MuteToggle,
        _ => null,
    };

    public static bool ShouldEnable(Audio.AudioOutputMode mode, bool audioEnabled, bool remoteVolumeEnabled) =>
        audioEnabled && mode == Audio.AudioOutputMode.Projector && remoteVolumeEnabled;

    public async Task ActivateAsync(SignalingClient signaling, CancellationToken token)
    {
        if (!Enabled || _active) return;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var lifetimeToken = _lifetime.Token;
        _worker = Task.Run(() => SendLoopAsync(signaling, lifetimeToken), CancellationToken.None);
        _active = true;
        try { _hook.Start(action => _actions.Writer.TryWrite(action)); }
        catch { Deactivate(); throw; }
        Console.WriteLine("[Volume] Windows media keys now control Google TV");
        try { await signaling.SendAsync(new GetVolumeMessage(), token); }
        catch
        {
            Deactivate();
            throw;
        }
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _hook.Stop();
        _lifetime?.Cancel();
        Console.WriteLine("[Volume] Windows media keys restored to local Windows control");
    }

    private async Task SendLoopAsync(SignalingClient signaling, CancellationToken token)
    {
        try
        {
            await foreach (var action in _actions.Reader.ReadAllAsync(token))
            {
                if (!_active) continue;
                await signaling.SendAsync(new VolumeActionMessage(action), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[Volume] Failed to send remote volume action: {error.Message}");
            Deactivate();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Deactivate();
        _actions.Writer.TryComplete();
        if (_worker is not null) { try { await _worker; } catch (OperationCanceledException) { } }
        _lifetime?.Dispose();
        _lifetime = null;
        _hook.Dispose();
    }
}

/// <summary>WH_KEYBOARD_LL is installed on a dedicated message-loop thread. The callback only queues actions.</summary>
internal sealed class WindowsVolumeKeyHook : IRemoteVolumeKeyHook
{
    private const int WhKeyboardLl = 13, WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105, WmQuit = 0x0012;
    private readonly object _gate = new();
    private Thread? _thread;
    private Action<RemoteVolumeAction>? _enqueue;
    private HookProc? _procedure;
    private IntPtr _hook;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new();
    private Exception? _startupError;
    private readonly HashSet<int> _suppressedKeys = [];
    private int _disposed;

    public void Start(Action<RemoteVolumeAction> onAction)
    {
        lock (_gate)
        {
            if (_thread is not null) return;
            _enqueue = onAction;
            _ready.Reset(); _startupError = null;
            _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Remote volume key hook" };
            _thread.Start();
        }
        _ready.Wait();
        if (_startupError is not null) { Stop(); throw new InvalidOperationException("Could not install Windows volume-key hook.", _startupError); }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null) return;
            if (!PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero) && thread.IsAlive && _hook != IntPtr.Zero)
                UnhookWindowsHookEx(_hook);
        }
        if (thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(3)) && _hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            thread.Join(TimeSpan.FromSeconds(1));
        }
        lock (_gate) { if (!thread.IsAlive) { _thread = null; _enqueue = null; _threadId = 0; } }
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        // Force queue creation before another thread posts WM_QUIT.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _procedure = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _procedure, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) _startupError = new Win32Exception(Marshal.GetLastWin32Error());
        _ready.Set();
        if (_hook == IntPtr.Zero) return;
        try { while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); } }
        finally { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; _procedure = null; _suppressedKeys.Clear(); }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = unchecked((int)wParam.ToInt64());
            int key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam).VkCode;
            var action = RemoteVolumeKeyController.MapVirtualKey(key);
            if (action is not null)
            {
                if (message is WmKeyDown or WmSysKeyDown)
                {
                    _suppressedKeys.Add(key); // Repeated key-downs intentionally enqueue repeated volume steps.
                    try { _enqueue?.Invoke(action.Value); } catch { /* Native hook callback must remain responsive. */ }
                    return new IntPtr(1);
                }
                if (message is WmKeyUp or WmSysKeyUp && _suppressedKeys.Remove(key)) return new IntPtr(1);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct KbdLlHookStruct { public int VkCode, ScanCode, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr HWnd; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
}
