using System.Runtime.InteropServices;
using ScreenSender.Desktop;
using ScreenSender.Desktop.Controls;
using ScreenSender.Desktop.Presentation;
using ScreenSender.Windows;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Discovery;

internal static class Program
{
    private static int _checks;
    private static int _failures;
    private static string? _captures;
    private static readonly List<string> Failures = [];

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        _captures = args.Length == 2 && args[0] == "--captures" ? Path.GetFullPath(args[1]) : null;
        if (_captures is not null) Directory.CreateDirectory(_captures);
        var backend = new TestBackend();
        using var window = new ConnectionWindow(new SenderOptions(), null, backend.Services) { Text = "Projector Connect - UI verification" };
        window.Shown += async (_, _) => {
            try { await Verify(window, backend); }
            catch (Exception error) { _failures++; _checks++; Failures.Add("UI verification: " + error); }
            finally { window.Close(); }
        };
        Application.Run(window);
        // The window owns Console while open; report after it restores the original writers.
        Console.WriteLine($"{_checks - _failures}/{_checks} desktop UI checks passed.");
        foreach (var failure in Failures) Console.Error.WriteLine("FAIL " + failure);
        return _failures == 0 ? 0 : 1;
    }

    private static async Task Verify(ConnectionWindow window, TestBackend backend)
    {
        await backend.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var list = Find<ReceiverList>(window, "deviceList");
        var primary = Find<ActionButton>(window, "primaryAction");
        var manual = Find<DisclosureSection>(window, "manualSection");
        var technical = Find<DisclosureSection>(window, "technicalSection");
        var audio = All(window).OfType<CheckBox>().Single(c => c.Text == "System audio");
        var mirror = All(window).OfType<CheckBox>().Single(c => c.Text == "Play audio on this PC");
        var keys = All(window).OfType<CheckBox>().Single(c => c.Text == "Media keys control TV");
        await Settle();
        Check(!manual.Expanded && !technical.Expanded, "Advanced sections must start collapsed");
        Check(!primary.Enabled && primary.Text == "Share screen", "No sharing without a selected device");
        Check(All(window).OfType<Label>().Any(l => l.Text.Contains("Looking for nearby")), "Discovery starts with a useful searching state");
        var one = new ReceiverDevice("one", "Living room projector", "192.0.2.10", 53530, false);
        backend.Publish(one); await Settle();
        if (_captures is not null) File.WriteAllLines(Path.Combine(_captures, "initial-layout.txt"), All(window).Select(c => $"{c.GetType().Name}: {c.Text} bounds={c.Bounds} preferred={c.GetPreferredSize(new Size(c.Width, 0))} min={c.MinimumSize} max={c.MaximumSize}"));
        Check(list.SelectedItem is ReceiverDevice { Id: "one" } && primary.Enabled, "One device is preselected without automatically sharing");
        Check(backend.StartCount == 0, "Discovery must never start sharing");
        Check(!technical.Visible || !Find<TextBox>(window, "technicalLogs").Visible, "Raw logs are hidden by default");

        audio.Checked = false; await Settle();
        Check(!mirror.Enabled && !keys.Enabled, "System-audio dependencies are enforced");
        audio.Checked = true; mirror.Checked = true; await Settle();
        Check(!keys.Enabled, "Local playback disables remote media-key control");
        mirror.Checked = false; await Settle();
        Check(keys.Enabled, "Projector-only audio enables media keys");

        foreach (int dpi in new[] { 96, 120, 144, 192 })
        {
            ChangeDpi(window, dpi);
            await Settle();
            foreach (var size in new[] { new Size(900, 620), new Size(800, 550), new Size(1200, 800), new Size(760, 520) })
            {
                window.Size = new Size(size.Width * dpi / 96, size.Height * dpi / 96);
                await Settle();
                Check(window.DeviceDpi == dpi, $"WinForms receives {dpi} DPI change");
                Check(All(window).All(c => c.DeviceDpi == dpi), $"Child controls receive {dpi} DPI change");
                Check(window.Width == Math.Min(size.Width * dpi / 96, SystemInformation.MaxWindowTrackSize.Width) && window.Height == Math.Min(size.Height * dpi / 96, SystemInformation.MaxWindowTrackSize.Height),
                    $"Requested {size.Width}x{size.Height}@{dpi}, got {window.Size}; minimum {window.MinimumSize}");
                AssertLayout(window, $"{size.Width}x{size.Height}@{dpi}");
                if (size.Width == 900) Capture(window, $"ready-{dpi}.png");
            }
        }
        ChangeDpi(window, 96); await Settle(); window.Size = new Size(900, 620); await Settle();

        manual.Expanded = true; technical.Expanded = true; await Settle();
        Check(Find<TextBox>(window, "technicalLogs").Width > window.ClientSize.Width / 2, "Expanded logs use the available width");
        foreach (var size in new[] { new Size(800, 550), new Size(900, 620), new Size(1200, 800) }) {
            window.Size = size; await Settle(); AssertLayout(window, "expanded " + size); Capture(window, $"expanded-{size.Width}.png");
        }
        var address = Find<TextBox>(window, "manualAddress");
        address.Text = "http://invalid/path";
        All(manual).OfType<ActionButton>().Single(b => b.Text == "Connect").PerformClick();
        await Settle();
        Check(backend.StartCount == 0 && All(manual).OfType<Label>().Any(l => l.Visible && l.Text.StartsWith("Enter a valid")), "Invalid manual addresses show a nearby error without connecting");
        technical.Expanded = false; manual.Expanded = false;
        window.Size = new Size(800, 550); await Settle(); manual.Expanded = true; await Settle();
        var bodyViewport = Find<Panel>(window, "scrollingContent");
        Check(bodyViewport.RectangleToScreen(bodyViewport.ClientRectangle).Contains(address.RectangleToScreen(address.ClientRectangle)), "Expanding manual connection reveals the address at small sizes");
        Capture(window, "manual-small.png"); manual.Expanded = false;

        list.Focus(); Key(list, Keys.Tab); Check(audio.Focused, "Tab moves from the device list to sharing options");
        Key(audio, Keys.Tab); Check(mirror.Focused, "Tab visits local audio");
        Key(mirror, Keys.Tab); Check(keys.Focused, "Tab visits media keys");
        Key(keys, Keys.Tab); Check(primary.Focused, "Tab reaches the primary action before advanced sections");
        audio.Focus();
        SendMessage(audio.Handle, 0x0100, (IntPtr)Keys.Space, IntPtr.Zero); SendMessage(audio.Handle, 0x0101, (IntPtr)Keys.Space, IntPtr.Zero);
        await Settle(); Check(!audio.Checked, "Space toggles the native checkbox behavior");
        audio.Checked = true; primary.Focus();
        Key(primary, Keys.Escape); Check(!window.IsDisposed && backend.StartCount == 0, "Escape does not connect or close the window");

        primary.PerformClick(); await Settle();
        Check(primary.Text == "Connecting..." && !primary.Enabled, "Connecting disables duplicate sharing actions");
        Check(backend.StartCount == 1 && backend.LastOptions?.Host == one.Host, "UI routes the selected endpoint to the existing sharing adapter");
        backend.Connected(); await Settle();
        Check(primary.Text == "Stop sharing" && primary.Enabled, "Connected has one obvious stop action");
        Check(All(window).OfType<Label>().Any(l => l.Text == "Sharing with Living room projector"), "Connected state clearly identifies the selected device");
        Key(primary, Keys.Escape); Check(!backend.Cancelled, "Escape cannot terminate an active connection");
        Capture(window, "connected.png");
        primary.PerformClick(); await Settle();
        Check(backend.Cancelled && primary.Text == "Share screen", "Stop uses the normal cancellation and cleanup path");

        primary.PerformClick(); await Settle();
        const string rawError = "FFmpeg screen capture: End of file. C:\\PrivatePath\\technical-only";
        backend.Fail(rawError); await Settle();
        Check(primary.Text == "Try again" && primary.Enabled, "A failed connection offers retry");
        Check(All(window).OfType<Label>().Any(l => l.Text == "Screen sharing couldn't start"), "Errors have a friendly title");
        Check(!All(window).OfType<Label>().Any(l => l.Text.Contains("PrivatePath")), "Raw exceptions do not appear in primary labels");
        Check(Find<TextBox>(window, "technicalLogs").Text.Contains(rawError), "The original exception is retained in technical logs");
        technical.Expanded = true; await Settle();
        All(technical).OfType<ActionButton>().Single(b => b.Text == "Copy logs").PerformClick();
        Check(backend.CopiedLogs?.Contains(rawError) == true, "Copy logs copies diagnostics without changing connection state");
        Capture(window, "error-details.png");
        technical.Expanded = false;
        primary.PerformClick(); await Settle();
        Check(backend.StartCount == 3 && backend.LastOptions?.Host == one.Host, "Retry uses the same endpoint");
        backend.Finish(); await Settle();

        var longDevice = one with { Name = "A very long family room television and home cinema projector with a descriptive receiver name that must wrap safely" };
        backend.Publish(longDevice, new("two", "Second room", "192.0.2.11", 53530, true), new("three", "Office", "192.0.2.12", 53530, false), new("four", "Spare receiver", "192.0.2.13", 53530, false));
        window.Size = new Size(800, 550); await Settle(); AssertLayout(window, "long names / many devices"); Capture(window, "many-long-names.png");
        list.SelectedIndex = 1; await Settle(); Check(!primary.Enabled, "An unavailable receiver cannot be started by the main action");
        backend.Publish(); await Settle();
        Check(list.Items.Count == 0 && !primary.Enabled, "An expired receiver cannot remain selected for sharing");
        Capture(window, "empty.png");
    }

    private static void AssertLayout(ConnectionWindow window, string scenario)
    {
        var primary = Find<ActionButton>(window, "primaryAction");
        var client = window.RectangleToScreen(window.ClientRectangle);
        var primaryBounds = primary.RectangleToScreen(primary.ClientRectangle);
        Check(client.Contains(primaryBounds) && primaryBounds.Height >= 44 * window.DeviceDpi / 96, "Primary action stays visible: " + scenario);
        var body = Find<Panel>(window, "scrollingContent");
        var action = Find<SurfaceCard>(window, "sharingStatusCard");
        Check(body.RectangleToScreen(body.ClientRectangle).Bottom <= action.RectangleToScreen(action.ClientRectangle).Top, "Scrolling content cannot cover the action: " + scenario);
        Check(!body.HorizontalScroll.Visible, "No horizontal overflow: " + scenario);
        foreach (var option in All(window).OfType<SharingOption>()) {
            Check(option.Height >= option.Toggle.Height + option.Description.Margin.Top + option.Description.Height, "Option descriptions aren't clipped: " + scenario);
        }
    }

    private static async Task Settle() { await Task.Delay(80); Application.DoEvents(); }
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) { _failures++; Failures.Add(message); }
    }
    private static IEnumerable<Control> All(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
    private static T Find<T>(Control parent, string name) where T : Control => All(parent).OfType<T>().Single(c => c.Name == name);
    private static void Key(Control control, Keys key)
    {
        var message = Message.Create(control.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
        control.PreProcessMessage(ref message);
    }
    private static void Capture(Form window, string name)
    {
        if (_captures is null) return;
        using var bitmap = new Bitmap(window.Width, window.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try { if (!PrintWindow(window.Handle, dc, 2)) throw new InvalidOperationException("Own-window capture failed"); }
        finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(Path.Combine(_captures, name), System.Drawing.Imaging.ImageFormat.Png);
    }
    private static void ChangeDpi(Form window, int dpi)
    {
        // Match the child-before-parent/top-level/child-after-parent WinForms DPI sequence.
        // The framework supports an explicit test DPI in WM_DPICHANGED_BEFOREPARENT.wParam.
        var children = All(window).ToArray();
        foreach (var child in children.Reverse()) SendMessage(child.Handle, 0x02E2, (IntPtr)dpi, IntPtr.Zero);
        var rect = new NativeRect { Left = 20, Top = 20, Right = 20 + 900 * dpi / 96, Bottom = 20 + 620 * dpi / 96 };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rect, pointer, false); SendMessage(window.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), pointer); }
        finally { Marshal.FreeHGlobal(pointer); }
        foreach (var child in children) SendMessage(child.Handle, 0x02E3, IntPtr.Zero, IntPtr.Zero);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);

    private sealed class TestBackend
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DesktopServices Services { get; }
        private IProgress<IReadOnlyList<ReceiverDevice>>? _devices;
        private IProgress<StreamingUpdate>? _progress;
        private TaskCompletionSource<StreamingResult>? _completion;
        public int StartCount; public bool Cancelled; public SenderOptions? LastOptions; public string? CopiedLogs;
        public TestBackend() { Services = new(Browse, () => { }, Share, text => CopiedLogs = text); }
        private async Task Browse(IProgress<IReadOnlyList<ReceiverDevice>> progress, CancellationToken token) { _devices = progress; Ready.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
        public void Publish(params ReceiverDevice[] devices) => _devices!.Report(devices);
        private async Task<StreamingResult> Share(SenderOptions options, CancellationToken token, IProgress<StreamingUpdate>? progress)
        {
            LastOptions = options; StartCount++; Cancelled = false; _progress = progress;
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => { Cancelled = true; _completion.TrySetResult(new(0, null)); });
            return await _completion.Task;
        }
        public void Connected() => _progress!.Report(new(StreamingPhase.Streaming, "test-only connected state"));
        public void Fail(string error) => _completion!.TrySetResult(new(1, error));
        public void Finish() => _completion!.TrySetResult(new(0, null));
    }
}
