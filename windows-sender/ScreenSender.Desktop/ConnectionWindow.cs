using ScreenSender.Windows;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Discovery;

namespace ScreenSender.Desktop;

public sealed class ConnectionWindow : Form
{
    private static readonly Color Background = Color.FromArgb(14, 21, 31), Surface = Color.FromArgb(24, 35, 48),
        TextColor = Color.FromArgb(238, 243, 249), Muted = Color.FromArgb(159, 177, 194), Accent = Color.FromArgb(94, 212, 192);
    private readonly SenderOptions _options;
    private readonly ReceiverDiscovery _discovery = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DataGridView _receivers = new();
    private readonly Label _discoveryStatus = new(), _status = new();
    private readonly Button _connect = new(), _disconnect = new(), _manualConnect = new(), _refresh = new();
    private readonly TextBox _address = new(), _details = new();
    private readonly CheckBox _audio = new(), _mirror = new(), _keys = new();
    private readonly System.Windows.Forms.Timer _emptyHint = new() { Interval = 10_000 };
    private readonly TextWriter _originalOut = Console.Out, _originalError = Console.Error;
    private IReadOnlyList<ReceiverDevice> _devices = [];
    private CancellationTokenSource? _sessionCancellation;
    private Task? _session, _browse;
    private bool _rendering, _hadSelection, _closeRequested, _canClose;
    private string? _selectedId, _sessionFailure;

    public ConnectionWindow(SenderOptions options, string? configurationError = null)
    {
        _options = options;
        Text = "Projector Connect";
        AccessibleName = Text;
        Font = new Font("Segoe UI", 10);
        BackColor = Background; ForeColor = TextColor;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 740); MinimumSize = new Size(740, 700);
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10, BackColor = Background };
        foreach (float height in new float[] { 48, 38, 34, 0, 38, 48, 44, 64, 104, 28 })
            root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Projector Connect", Font = new Font("Segoe UI Semibold", 25), ForeColor = TextColor, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "Choose a screen on your local network", ForeColor = Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        var browseBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        browseBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); browseBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        _discoveryStatus.Text = "Searching for receivers…"; _discoveryStatus.Dock = DockStyle.Fill; _discoveryStatus.ForeColor = Muted;
        _discoveryStatus.TextAlign = ContentAlignment.MiddleLeft;
        browseBar.Controls.Add(_discoveryStatus, 0, 0);
        StyleButton(_refresh, "Refresh", false); browseBar.Controls.Add(_refresh, 1, 0);
        _refresh.Click += (_, _) => _discovery.Refresh(); root.Controls.Add(browseBar, 0, 2);
        BuildReceiverList(); root.Controls.Add(_receivers, 0, 3);
        var optionsBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        StyleCheckBox(_audio, "System audio", options.Audio);
        StyleCheckBox(_mirror, "Also play on this PC", options.AudioOutput == AudioOutputMode.Mirror);
        StyleCheckBox(_keys, "Media keys control TV", options.RemoteVolume);
        optionsBar.Controls.AddRange([_audio, _mirror, _keys]); root.Controls.Add(optionsBar, 0, 4);
        _audio.CheckedChanged += (_, _) => UpdateControls(); _mirror.CheckedChanged += (_, _) => UpdateControls();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        StyleButton(_connect, "Connect selected", true); _connect.Width = 180;
        StyleButton(_disconnect, "Disconnect", false); _disconnect.Width = 130;
        _connect.Dock = DockStyle.None; _disconnect.Dock = DockStyle.None;
        actions.Controls.AddRange([_connect, _disconnect]); root.Controls.Add(actions, 0, 5);
        _connect.Click += async (_, _) => {
            if (_receivers.SelectedRows.Count != 1 || _receivers.SelectedRows[0].Tag is not ReceiverDevice receiver || receiver.Busy) return;
            await ShareAsync(new(receiver.Host, receiver.Port), receiver.Name);
        };
        _disconnect.Click += (_, _) => StopSharing();
        var manual = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        manual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); manual.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        _address.Dock = DockStyle.Fill; _address.PlaceholderText = "TV address, if discovery is unavailable";
        _address.AccessibleName = "Manual TV address"; _address.BackColor = Surface; _address.ForeColor = TextColor;
        _address.BorderStyle = BorderStyle.FixedSingle; _address.Margin = new Padding(0, 8, 12, 0);
        _address.Text = string.IsNullOrWhiteSpace(options.Host) ? "" : $"{(options.Host.Contains(':') ? "[" + options.Host + "]" : options.Host)}:{options.Port}";
        manual.Controls.Add(_address, 0, 0); StyleButton(_manualConnect, "Connect by address", false); manual.Controls.Add(_manualConnect, 1, 0);
        _manualConnect.Click += async (_, _) => {
            try { var target = ConnectionTarget.Parse(_address.Text, _options.Port); await ShareAsync(target, target.Host); }
            catch (ArgumentException error) { _status.Text = error.Message; }
        };
        root.Controls.Add(manual, 0, 6);
        _status.Dock = DockStyle.Fill; _status.ForeColor = Accent; _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = configurationError is null ? "Choose a receiver, then Connect to share your screen." : "Saved settings could not be loaded: " + configurationError;
        root.Controls.Add(_status, 0, 7);
        _details.Dock = DockStyle.Fill; _details.Multiline = true; _details.ReadOnly = true; _details.ScrollBars = ScrollBars.Vertical;
        _details.BackColor = Surface; _details.ForeColor = Muted; _details.BorderStyle = BorderStyle.None;
        _details.AccessibleName = "Connection diagnostics"; _details.Font = new Font("Consolas", 9);
        root.Controls.Add(_details, 0, 8);
        root.Controls.Add(new Label { Text = "Your screen is shared only after you choose Connect.", ForeColor = Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 9);
        var log = new WindowLogWriter(new Progress<string>(AppendLog));
        Console.SetOut(log); Console.SetError(log);
        _emptyHint.Tick += (_, _) => {
            if (_devices.Count == 0) _discoveryStatus.Text = "No receivers found. Open Screen Receiver on your TV, or enter its address.";
            _emptyHint.Stop();
        };
        Load += (_, _) => {
            _browse = BrowseAsync(); _emptyHint.Start();
        };
        FormClosing += OnClosing;
        FormClosed += (_, _) => { Console.SetOut(_originalOut); Console.SetError(_originalError); _emptyHint.Dispose(); _lifetime.Dispose(); };
        UpdateControls();
    }

    private void BuildReceiverList()
    {
        _receivers.Dock = DockStyle.Fill; _receivers.BackgroundColor = Surface; _receivers.BorderStyle = BorderStyle.None;
        _receivers.ReadOnly = true; _receivers.AllowUserToAddRows = false; _receivers.AllowUserToDeleteRows = false;
        _receivers.AllowUserToResizeRows = false; _receivers.RowHeadersVisible = false; _receivers.MultiSelect = false;
        _receivers.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _receivers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _receivers.EnableHeadersVisualStyles = false; _receivers.ColumnHeadersHeight = 34; _receivers.RowTemplate.Height = 44;
        _receivers.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 10) };
        _receivers.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextColor, SelectionBackColor = Color.FromArgb(34, 87, 88), SelectionForeColor = Color.White, Padding = new Padding(8, 0, 0, 0) };
        _receivers.GridColor = Background; _receivers.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _receivers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Receiver", HeaderText = "Receiver", FillWeight = 48, SortMode = DataGridViewColumnSortMode.NotSortable });
        _receivers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 20, SortMode = DataGridViewColumnSortMode.NotSortable });
        _receivers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "Address", FillWeight = 32, SortMode = DataGridViewColumnSortMode.NotSortable });
        _receivers.AccessibleName = "Receivers on your network";
        _receivers.SelectionChanged += (_, _) => {
            if (!_rendering && _receivers.SelectedRows.Count == 1 && _receivers.SelectedRows[0].Tag is ReceiverDevice device)
            { _selectedId = device.Id; _hadSelection = true; }
            UpdateControls();
        };
    }

    private async Task BrowseAsync()
    {
        var progress = new Progress<IReadOnlyList<ReceiverDevice>>(RenderDevices);
        AppendLog("[Discovery] Searching for Screen Receiver on the local network");
        try { await Task.Run(() => _discovery.RunAsync(progress, _lifetime.Token)); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) {
            _emptyHint.Stop(); _discoveryStatus.Text = "Discovery unavailable. You can still connect by address.";
            AppendLog("[Discovery] " + error.Message);
        }
    }

    private void RenderDevices(IReadOnlyList<ReceiverDevice> devices)
    {
        if (_closeRequested) return;
        _devices = devices; _rendering = true;
        AppendLog($"[Discovery] {devices.Count} receiver(s) available in the list");
        try {
            _receivers.Rows.Clear();
            foreach (var device in devices) {
                int index = _receivers.Rows.Add(device.Name, device.Busy ? "In use" : "Available", device.Endpoint);
                _receivers.Rows[index].Tag = device;
            }
            _receivers.ClearSelection();
            var selected = _receivers.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((ReceiverDevice)r.Tag!).Id == _selectedId);
            if (selected is not null) selected.Selected = true;
            else if (!_hadSelection && devices.Count == 1) { _receivers.Rows[0].Selected = true; _selectedId = devices[0].Id; }
        }
        finally { _rendering = false; }
        _discoveryStatus.Text = devices.Count == 0 ? "Searching for receivers…" : $"{devices.Count} receiver{(devices.Count == 1 ? "" : "s")} found · updates automatically";
        UpdateControls();
    }

    private async Task ShareAsync(ConnectionTarget target, string name)
    {
        if (_sessionCancellation is not null || _closeRequested) return;
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var sessionCancellation = _sessionCancellation;
        _sessionFailure = null;
        _status.Text = "Connecting to " + name + "…"; _disconnect.Text = "Cancel"; UpdateControls();
        var options = _options with { Host = target.Host, Port = target.Port, Audio = _audio.Checked,
            AudioOutput = _mirror.Checked ? AudioOutputMode.Mirror : AudioOutputMode.Projector, RemoteVolume = _keys.Checked };
        var progress = new Progress<StreamingUpdate>(update => {
            if (_closeRequested || !ReferenceEquals(_sessionCancellation, sessionCancellation)) return;
            if (update.Phase == StreamingPhase.Failed) _sessionFailure = update.Message;
            if (_sessionCancellation.IsCancellationRequested) return;
            _status.Text = update.Phase == StreamingPhase.Streaming ? "Sharing your screen with " + name : update.Message;
            _disconnect.Text = update.Phase == StreamingPhase.Streaming ? "Disconnect" : "Cancel";
        });
        try {
            var running = Task.Run(() => StreamingSession.RunAsync(options, sessionCancellation.Token, progress));
            _session = running;
            var result = await running;
            _sessionFailure = result.Error;
        }
        catch (Exception error) { _sessionFailure = error.Message; AppendLog("[Error] " + error.Message); }
        finally {
            _sessionCancellation.Dispose(); _sessionCancellation = null; _session = null;
            _status.Text = _sessionFailure is null ? "Disconnected. Choose a receiver to share again." : "Connection failed: " + _sessionFailure;
            _disconnect.Text = "Disconnect"; UpdateControls(); _discovery.Refresh();
        }
    }

    private void StopSharing()
    {
        if (_sessionCancellation is null) return;
        _status.Text = "Disconnecting and restoring Windows audio…";
        _sessionCancellation.Cancel(); _disconnect.Enabled = false;
    }

    private async void OnClosing(object? sender, FormClosingEventArgs args)
    {
        if (_canClose) return;
        args.Cancel = true;
        if (_closeRequested) return;
        _closeRequested = true; _emptyHint.Stop(); Enabled = false;
        StopSharing(); await _lifetime.CancelAsync();
        try { if (_session is not null) await _session; if (_browse is not null) await _browse; }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppendLog("[Cleanup] " + error.Message); }
        _canClose = true; Close();
    }

    private void UpdateControls()
    {
        bool idle = _sessionCancellation is null && !_closeRequested;
        _connect.Enabled = idle && _receivers.SelectedRows.Count == 1 && _receivers.SelectedRows[0].Tag is ReceiverDevice { Busy: false };
        _disconnect.Enabled = !idle && !_closeRequested && _sessionCancellation?.IsCancellationRequested == false;
        _manualConnect.Enabled = idle; _address.Enabled = idle; _receivers.Enabled = idle;
        _audio.Enabled = idle; _mirror.Enabled = idle && _audio.Checked; _keys.Enabled = idle && _audio.Checked && !_mirror.Checked;
    }

    private void AppendLog(string line)
    {
        if (IsDisposed || _canClose) return;
        if (_details.TextLength > 32_000) _details.Text = _details.Text[^16_000..];
        _details.AppendText(line + Environment.NewLine);
    }

    private static void StyleCheckBox(CheckBox check, string text, bool value)
    { check.Text = text; check.Checked = value; check.AutoSize = true; check.ForeColor = TextColor; check.Margin = new Padding(0, 9, 22, 0); }
    private static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text; button.AccessibleName = text; button.Dock = DockStyle.Fill; button.Height = 36;
        button.BackColor = primary ? Accent : Surface; button.ForeColor = primary ? Background : TextColor;
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 3, 8, 5); button.Font = new Font("Segoe UI Semibold", 10);
    }
}

internal sealed class WindowLogWriter(IProgress<string> progress) : TextWriter
{
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void WriteLine(string? value) => progress.Report(value ?? "");
}
