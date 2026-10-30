using System.Runtime.InteropServices;
using ScreenSender.Desktop.Controls;
using ScreenSender.Desktop.Presentation;
using ScreenSender.Windows;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Discovery;

namespace ScreenSender.Desktop;

public sealed class ConnectionWindow : Form
{
    private readonly SenderOptions _options;
    private readonly DesktopServices _services;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReceiverList _receivers;
    private readonly Label _deviceCount = Theme.Label("Looking for nearby devices...", 9);
    private readonly Label _emptyTitle = Theme.Label("Looking for nearby devices...", 11, true);
    private readonly Label _emptyHelp = Theme.Label("Make sure the TV or projector is turned on and connected to the same network.");
    private readonly SurfaceCard _empty = new();
    private readonly Label _statusTitle = Theme.Label("Ready to share", 12, true);
    private readonly Label _statusHelp = Theme.Label("Choose a device, then Share screen.", 9);
    private readonly Label _statusBadge = Theme.Label("Ready", 9, true);
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Height = 5, Dock = DockStyle.Top, Visible = false, TabStop = false };
    private readonly ActionButton _primary = new() { Primary = true, Text = "Share screen", MinimumSize = new Size(174, 46), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly ActionButton _cancel = new() { Text = "Cancel", Visible = false };
    private readonly ActionButton _refresh = new() { Text = "Refresh", AccessibleName = "Refresh devices" };
    private readonly ActionButton _manualConnect = new() { Text = "Connect" };
    private readonly ActionButton _copyLogs = new() { Text = "Copy logs" };
    private readonly Label _manualError = Theme.Label("", 9);
    private readonly Label _copyFeedback = Theme.Label("", 9);
    private readonly TextBox _address = new(), _details = new();
    private readonly SharingOption _audio, _mirror, _keys;
    private readonly DisclosureSection _manual = new("Connect by IP address"), _technical = new("Technical details");
    private readonly Panel _body = new() { AutoScroll = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel _content;
    private readonly System.Windows.Forms.Timer _emptyHint = new() { Interval = 10_000 };
    private readonly TextWriter _originalOut = Console.Out, _originalError = Console.Error;
    private IReadOnlyList<ReceiverDevice> _devices = [];
    private CancellationTokenSource? _sessionCancellation;
    private Task? _session, _browse;
    private ConnectionPhase _phase;
    private bool _rendering, _hadSelection, _closeRequested, _canClose, _discoveryFailed, _wasSharing;
    private string? _selectedId;
    private ReceiverDevice? _activeDevice;
    private ConnectionTarget? _lastTarget;
    private string _lastName = "";
    private SharingFailure? _failure;

    public ConnectionWindow(SenderOptions options, string? configurationError = null)
        : this(options, configurationError, DesktopServices.Create()) { }

    internal ConnectionWindow(SenderOptions options, string? configurationError, DesktopServices services)
    {
        _options = options; _services = services;
        Text = "Projector Connect"; AccessibleName = Text;
        using (var stream = typeof(ConnectionWindow).Assembly.GetManifestResourceStream("ScreenSender.Desktop.Brand.ico"))
        {
            if (stream is not null) { using var icon = new Icon(stream); Icon = (Icon)icon.Clone(); }
        }
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
        Size = new Size(900, 620); MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        _receivers = new ReceiverList(DeviceStatus) { TabIndex = 0, Name = "deviceList" };
        _audio = new("System audio", "Share sounds from this computer", options.Audio) { TabIndex = 0 };
        _mirror = new("Play audio on this PC", "Keep audio playing locally while sharing", options.AudioOutput == AudioOutputMode.Mirror) { TabIndex = 1 };
        _keys = new("Media keys control TV", "Volume buttons control the receiver while connected", options.RemoteVolume) { TabIndex = 2 };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24, 20, 24, 12), BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var header = Stack(); header.Margin = new Padding(0, 0, 0, 18); header.TabStop = false;
        Add(header, Theme.Label("Projector Connect", 22, true));
        var subtitle = Theme.Label("Share your screen with a nearby TV or projector"); subtitle.Margin = new Padding(0, 4, 0, 0);
        Add(header, subtitle); root.Controls.Add(header, 0, 0);

        _body.TabIndex = 0; _content = Stack(); _body.Controls.Add(_content); root.Controls.Add(_body, 0, 1);
        _body.SizeChanged += (_, _) => FitContent();
        _content.SizeChanged += (_, _) => FitContent();
        BuildDevices(); BuildOptions(); BuildAdvanced();
        root.Controls.Add(BuildActionArea(), 0, 2);
        var footer = Theme.Label("P&K Applications", 9); footer.Margin = new Padding(0, 10, 0, 0); footer.TabStop = false;
        root.Controls.Add(footer, 0, 3);

        AcceptButton = _primary; // Focused buttons keep their standard Enter behavior; Escape has no CancelButton.
        _primary.TabIndex = 0; _primary.AccessibleName = "Share screen"; _primary.Name = "primaryAction";
        _manual.Name = "manualSection"; _technical.Name = "technicalSection";
        _body.Name = "scrollingContent"; _details.Name = "technicalLogs"; _address.Name = "manualAddress";
        _primary.Click += async (_, _) => {
            if (_phase == ConnectionPhase.Connected) StopSharing();
            else if (_phase == ConnectionPhase.Error && _lastTarget is not null) await ShareAsync(_lastTarget, _lastName);
            else if (_receivers.SelectedItem is ReceiverDevice { Busy: false } device) await ShareAsync(new(device.Host, device.Port), device.Name, device);
        };
        _cancel.Click += (_, _) => StopSharing();
        _refresh.Click += (_, _) => RefreshDevices();
        _receivers.SelectedIndexChanged += (_, _) => {
            if (!_rendering && _receivers.SelectedItem is ReceiverDevice device) {
                _selectedId = device.Id; _hadSelection = true;
                if (_phase == ConnectionPhase.Error) { _phase = ConnectionPhase.Ready; _failure = null; }
            }
            if (!_rendering) UpdateView();
        };
        _audio.Toggle.CheckedChanged += (_, _) => UpdateView(); _mirror.Toggle.CheckedChanged += (_, _) => UpdateView();

        var log = new WindowLogWriter(new Progress<string>(AppendLog));
        Console.SetOut(log); Console.SetError(log);
        _emptyHint.Tick += (_, _) => { _emptyHint.Stop(); UpdateDiscoveryView(searching: false); };
        Load += (_, _) => {
            FitContent(); _receivers.UpdateHeight();
            _browse = BrowseAsync(); _emptyHint.Start();
            if (configurationError is not null) {
                AppendLog("[Configuration] " + configurationError);
                _statusHelp.Text = "Saved preferences couldn't be loaded. Default sharing options are shown; details are available below.";
            }
        };
        Shown += (_, _) => _receivers.Focus();
        DpiChanged += (_, e) => BeginInvoke(() => {
            if (IsDisposed) return;
            MinimumSize = new Size(760 * e.DeviceDpiNew / 96, 520 * e.DeviceDpiNew / 96);
            _receivers.UpdateHeight();
        });
        FormClosing += OnClosing;
        FormClosed += (_, _) => {
            Console.SetOut(_originalOut); Console.SetError(_originalError);
            _emptyHint.Dispose(); _lifetime.Dispose();
        };
        UpdateView();
    }

    private void BuildDevices()
    {
        var section = Stack(); section.TabIndex = 0; section.Margin = new Padding(0, 0, 0, 20);
        var heading = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var caption = Stack(); Add(caption, Theme.Label("Available devices", 11, true)); Add(caption, _deviceCount);
        heading.Controls.Add(caption, 0, 0); heading.Controls.Add(_refresh, 1, 0); _refresh.TabIndex = 1;
        Add(section, heading); Add(section, _receivers);
        var emptyContent = Stack(); emptyContent.BackColor = Theme.Surface;
        Add(emptyContent, _emptyTitle); _emptyHelp.Margin = new Padding(0, 6, 0, 0); Add(emptyContent, _emptyHelp);
        var manualLink = new LinkLabel { Text = "Connect by IP address", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Accent,
            Margin = new Padding(0, 10, 0, 0), AccessibleName = "Connect by IP address instead", TabIndex = 0 };
        manualLink.LinkClicked += (_, _) => { _manual.Expanded = true; _address.Focus(); BeginInvoke(() => Reveal(_manual)); };
        Add(emptyContent, manualLink); _empty.Controls.Add(emptyContent); _empty.Dock = DockStyle.Top; Add(section, _empty);
        Add(_content, section);
    }

    private void BuildOptions()
    {
        var section = Stack(); section.TabIndex = 1; section.Margin = new Padding(0, 0, 0, 18);
        var title = Theme.Label("Sharing options", 11, true); title.Margin = new Padding(0, 0, 0, 8); Add(section, title);
        var card = new SurfaceCard { Dock = DockStyle.Top, TabIndex = 0 };
        var options = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, BackColor = Theme.Surface, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        options.Controls.Add(_audio, 0, 0); options.Controls.Add(_mirror, 1, 0); options.Controls.Add(_keys, 2, 0);
        card.Controls.Add(options); Add(section, card); Add(_content, section);
    }

    private void BuildAdvanced()
    {
        var advanced = Stack(); advanced.TabIndex = 3;
        _manual.TabIndex = 0; _technical.TabIndex = 1;
        _manual.ExpandedChanged += (_, _) => { if (_manual.Expanded && IsHandleCreated) BeginInvoke(() => Reveal(_manual)); };
        _technical.ExpandedChanged += (_, _) => { if (_technical.Expanded && IsHandleCreated) BeginInvoke(() => Reveal(_technical)); };
        Add(_manual.Body, Theme.Label("Address", 10, true));
        var addressRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 7, 0, 0) };
        addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _address.Dock = DockStyle.Fill; _address.BackColor = Theme.Surface; _address.ForeColor = Theme.Text;
        _address.BorderStyle = BorderStyle.FixedSingle; _address.PlaceholderText = "TV address or hostname";
        _address.AccessibleName = "Receiver address"; _address.TabIndex = 0; _address.Margin = new Padding(0, 5, 10, 3);
        _address.Text = string.IsNullOrWhiteSpace(_options.Host) ? "" : $"{(_options.Host.Contains(':') ? "[" + _options.Host + "]" : _options.Host)}:{_options.Port}";
        _manualConnect.AccessibleName = "Connect by IP address"; _manualConnect.TabIndex = 1;
        addressRow.Controls.Add(_address, 0, 0); addressRow.Controls.Add(_manualConnect, 1, 0);
        Add(_manual.Body, addressRow);
        _manualError.ForeColor = Theme.Error; _manualError.Visible = false; _manualError.Margin = new Padding(0, 6, 0, 0); Add(_manual.Body, _manualError);
        _address.TextChanged += (_, _) => { _manualError.Text = ""; _manualError.Visible = false; };
        _address.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (_manualConnect.Enabled) _manualConnect.PerformClick(); } };
        _manualConnect.Click += async (_, _) => {
            ConnectionTarget target;
            try { target = ConnectionTarget.Parse(_address.Text, _options.Port); }
            catch (ArgumentException) {
                _manualError.Text = "Enter a valid TV address or hostname, with an optional port (for example, tv.local:53530).";
                _manualError.Visible = true; _address.Focus(); BeginInvoke(() => Reveal(_manual)); return;
            }
            var device = _devices.FirstOrDefault(d => d.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) && d.Port == target.Port);
            await ShareAsync(target, device?.Name ?? target.Host, device);
        };

        var toolbar = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.Controls.Add(_copyFeedback, 0, 0); toolbar.Controls.Add(_copyLogs, 1, 0);
        _copyLogs.TabIndex = 0; _copyLogs.Click += (_, _) => {
            try { if (_details.TextLength > 0) { _services.CopyLogs(_details.Text); _copyFeedback.Text = "Logs copied"; } }
            catch (Exception error) when (error is ExternalException or ThreadStateException) { _copyFeedback.Text = "Couldn't copy logs. Try again."; AppendLog("[Clipboard] " + error.Message); }
        };
        Add(_technical.Body, toolbar);
        _details.Multiline = true; _details.ReadOnly = true; _details.WordWrap = false; _details.ScrollBars = ScrollBars.Both;
        _details.Dock = DockStyle.Fill; _details.Height = 130; _details.MinimumSize = new Size(0, 80);
        _details.BackColor = Theme.Surface; _details.ForeColor = Theme.Muted; _details.BorderStyle = BorderStyle.FixedSingle;
        _details.Font = new Font("Consolas", 9); _details.AccessibleName = "Connection diagnostics"; _details.TabIndex = 1;
        Add(_technical.Body, _details);
        _technical.Body.RowStyles[^1].SizeType = SizeType.Absolute; _technical.Body.RowStyles[^1].Height = 130;
        Add(advanced, _manual); Add(advanced, _technical); Add(_content, advanced);
    }

    private Control BuildActionArea()
    {
        var card = new SurfaceCard { Dock = DockStyle.Top, Margin = new Padding(0, 16, 0, 0), TabIndex = 1, Name = "sharingStatusCard" };
        var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var status = Stack(); status.BackColor = Theme.Surface; status.Margin = new Padding(0, 0, 20, 0);
        var titleLine = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
        titleLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); titleLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusBadge.ForeColor = Theme.Accent; _statusBadge.Margin = new Padding(8, 3, 0, 0);
        titleLine.Controls.Add(_statusTitle, 0, 0); titleLine.Controls.Add(_statusBadge, 1, 0); Add(status, titleLine);
        _statusHelp.Margin = new Padding(0, 4, 0, 0); Add(status, _statusHelp);
        _progress.Margin = new Padding(0, 8, 0, 0); Add(status, _progress);
        row.Controls.Add(status, 0, 0);
        var actions = Stack(); actions.BackColor = Theme.Surface; actions.Anchor = AnchorStyles.Right; actions.TabIndex = 0;
        _primary.Dock = DockStyle.Top; Add(actions, _primary);
        _cancel.Dock = DockStyle.Top; _cancel.Margin = new Padding(0, 5, 0, 0); _cancel.TabIndex = 1; Add(actions, _cancel);
        row.Controls.Add(actions, 1, 0); card.Controls.Add(row);
        return card;
    }

    private void FitContent()
    {
        if (_content.Width != _body.ClientSize.Width && _body.ClientSize.Width > 0) _content.Width = _body.ClientSize.Width;
        if (_technical.Body.RowStyles.Count > 1) {
            int available = _body.ClientSize.Height - _technical.Header.Height - _copyLogs.Height - _technical.Body.Padding.Vertical - 16 * DeviceDpi / 96;
            int height = Math.Clamp(available, 80 * DeviceDpi / 96, 130 * DeviceDpi / 96);
            if (_technical.Body.RowStyles[^1].Height != height) _technical.Body.RowStyles[^1].Height = height;
        }
    }

    private void Reveal(Control control)
    {
        if (_closeRequested || !control.Visible) return;
        int top = _body.PointToClient(control.PointToScreen(Point.Empty)).Y;
        int bottom = top + control.Height;
        int delta = control.Height > _body.ClientSize.Height || top < 0 ? top : bottom > _body.ClientSize.Height ? bottom - _body.ClientSize.Height : 0;
        if (delta != 0) _body.AutoScrollPosition = new Point(0, Math.Max(0, -_body.AutoScrollPosition.Y + delta));
    }

    private static TableLayoutPanel Stack() => new ContentStack { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 0, Margin = Padding.Empty,
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) } };
    private static void Add(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(control, 0, row);
    }

    private string DeviceStatus(ReceiverDevice device) => _activeDevice?.Id == device.Id && _sessionCancellation is not null
        ? _phase == ConnectionPhase.Connected ? "Connected" : _phase == ConnectionPhase.Stopping ? "Stopping..." : "Connecting..."
        : device.Busy ? "Unavailable" : "Ready to connect";

    private void RefreshDevices()
    {
        _services.Refresh(); _emptyHint.Stop(); _emptyHint.Start();
        if (_devices.Count == 0 && !_discoveryFailed) UpdateDiscoveryView(searching: true);
    }

    private async Task BrowseAsync()
    {
        var progress = new Progress<IReadOnlyList<ReceiverDevice>>(RenderDevices);
        AppendLog("[Discovery] Searching for Screen Receiver on the local network");
        try { await Task.Run(() => _services.Browse(progress, _lifetime.Token)); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) {
            _discoveryFailed = true; _emptyHint.Stop(); AppendLog("[Discovery] " + error);
            UpdateDiscoveryView(searching: false);
        }
    }

    private void RenderDevices(IReadOnlyList<ReceiverDevice> devices)
    {
        if (_closeRequested) return;
        _devices = devices; AppendLog($"[Discovery] {devices.Count} receiver(s) available in the list");
        RebuildDeviceList(); UpdateDiscoveryView(_emptyHint.Enabled); UpdateView();
    }

    private void RebuildDeviceList()
    {
        _rendering = true;
        try {
            var devices = _devices.ToList();
            if (_sessionCancellation is not null && _activeDevice is not null && !devices.Any(d => d.Id == _activeDevice.Id)) devices.Insert(0, _activeDevice);
            _receivers.BeginUpdate(); _receivers.Items.Clear();
            foreach (var device in devices) _receivers.Items.Add(device);
            _receivers.SelectedIndex = devices.FindIndex(d => d.Id == _selectedId);
            if (_receivers.SelectedIndex == -1 && !_hadSelection && devices.Count == 1) { _receivers.SelectedIndex = 0; _selectedId = devices[0].Id; }
            _receivers.Visible = devices.Count > 0; _empty.Visible = devices.Count == 0;
            _receivers.EndUpdate(); _receivers.UpdateHeight();
        }
        finally { _rendering = false; }
    }

    private void UpdateDiscoveryView(bool searching)
    {
        int count = _receivers.Items.Count;
        _deviceCount.Text = count > 0 ? $"{count} device{(count == 1 ? "" : "s")} found" : searching && !_discoveryFailed ? "Looking for nearby devices..." : "No devices found";
        _emptyTitle.Text = searching && !_discoveryFailed ? "Looking for nearby devices..." : "No receivers found";
        _emptyHelp.Text = _discoveryFailed ? "Automatic discovery isn't available. You can refresh or connect by IP address instead. Technical details may help."
            : "Make sure the TV or projector is turned on and connected to the same network.";
    }

    private async Task ShareAsync(ConnectionTarget target, string name, ReceiverDevice? device = null)
    {
        if (_sessionCancellation is not null || _closeRequested) return;
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var sessionCancellation = _sessionCancellation;
        _lastTarget = target; _lastName = name;
        _activeDevice = device ?? _devices.FirstOrDefault(d => d.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) && d.Port == target.Port);
        _failure = null; _wasSharing = false; _phase = ConnectionPhase.Connecting;
        UpdateView();
        var options = _options with { Host = target.Host, Port = target.Port, Audio = _audio.Toggle.Checked,
            AudioOutput = _mirror.Toggle.Checked ? AudioOutputMode.Mirror : AudioOutputMode.Projector, RemoteVolume = _keys.Toggle.Checked };
        var progress = new Progress<StreamingUpdate>(update => {
            if (_closeRequested || !ReferenceEquals(_sessionCancellation, sessionCancellation) || sessionCancellation.IsCancellationRequested) return;
            if (update.Phase == StreamingPhase.Streaming) { _phase = ConnectionPhase.Connected; _wasSharing = true; UpdateView(); }
            // Raw backend errors stay in logs; show an error only after cleanup has finished.
        });
        string? technicalFailure = null;
        try {
            var running = Task.Run(() => _services.Share(options, sessionCancellation.Token, progress));
            _session = running;
            var result = await running; technicalFailure = result.Error;
            if (result.ExitCode != 0 && technicalFailure is null) technicalFailure = "Sharing session ended with an error.";
        }
        catch (Exception error) { technicalFailure = error.Message; AppendLog("[Error] " + error); }
        finally {
            bool cancelled = sessionCancellation.IsCancellationRequested;
            sessionCancellation.Dispose(); _sessionCancellation = null; _session = null;
            if (!cancelled && technicalFailure is not null) {
                AppendLog("[Sharing] " + technicalFailure); _failure = SharingFailure.From(technicalFailure, _wasSharing); _phase = ConnectionPhase.Error;
            } else { _phase = ConnectionPhase.Ready; _failure = null; }
            _activeDevice = null;
            if (!_closeRequested) { RebuildDeviceList(); UpdateView(); _services.Refresh(); }
        }
    }

    private void StopSharing()
    {
        if (_sessionCancellation is null) return;
        _phase = ConnectionPhase.Stopping; UpdateView(); _sessionCancellation.Cancel();
    }

    private void UpdateView()
    {
        bool idle = _sessionCancellation is null && !_closeRequested;
        bool validDevice = _receivers.SelectedItem is ReceiverDevice { Busy: false };
        _primary.Text = _phase switch { ConnectionPhase.Connecting => "Connecting...", ConnectionPhase.Connected => "Stop sharing", ConnectionPhase.Stopping => "Stopping...", ConnectionPhase.Error => "Try again", _ => "Share screen" };
        _primary.AccessibleName = _primary.Text;
        _primary.Enabled = !_closeRequested && (_phase == ConnectionPhase.Connected || idle && (_phase == ConnectionPhase.Error ? _lastTarget is not null : validDevice));
        _cancel.Visible = _phase == ConnectionPhase.Connecting; _cancel.Enabled = !_closeRequested;
        _progress.Visible = _phase is ConnectionPhase.Connecting or ConnectionPhase.Stopping;
        _statusBadge.Text = _phase switch { ConnectionPhase.Connecting => "Connecting", ConnectionPhase.Connected => "Connected", ConnectionPhase.Stopping => "Stopping", ConnectionPhase.Error => "Error", _ => "Ready" };
        _statusBadge.ForeColor = _phase == ConnectionPhase.Error ? Theme.Error : Theme.Accent;
        _statusTitle.Text = _phase switch { ConnectionPhase.Connecting => $"Connecting to {_lastName}...", ConnectionPhase.Connected => $"Sharing with {_lastName}",
            ConnectionPhase.Stopping => "Stopping screen sharing...", ConnectionPhase.Error => _failure?.Title ?? "Screen sharing couldn't start", _ => "Ready to share" };
        _statusHelp.Text = _phase switch {
            ConnectionPhase.Connected => $"System audio: {(_audio.Toggle.Checked ? "On" : "Off")}  ·  TV volume control: {(_audio.Toggle.Checked && !_mirror.Toggle.Checked && _keys.Toggle.Checked ? "On" : "Off")}",
            ConnectionPhase.Connecting => "Your screen will be shared when the receiver connects.",
            ConnectionPhase.Stopping => "Finishing the connection and restoring Windows audio.",
            ConnectionPhase.Error => _failure?.Help ?? "Try again or open Technical details for more information.",
            _ => _receivers.SelectedItem is ReceiverDevice { Busy: true } ? "This device is already in use. Choose another device or wait until it's ready." : "Choose a device, then Share screen."
        };
        _receivers.Enabled = idle; _manualConnect.Enabled = idle; _address.Enabled = idle;
        _audio.Toggle.Enabled = idle; _mirror.Toggle.Enabled = idle && _audio.Toggle.Checked;
        _keys.Toggle.Enabled = idle && _audio.Toggle.Checked && !_mirror.Toggle.Checked;
        _keys.Description.Text = _audio.Toggle.Checked && !_mirror.Toggle.Checked ? "Volume buttons control the receiver while connected" : "Available with system audio on and local playback off";
        _receivers.Invalidate(); _copyLogs.Enabled = _details.TextLength > 0;
    }

    private async void OnClosing(object? sender, FormClosingEventArgs args)
    {
        if (_canClose) return;
        args.Cancel = true; if (_closeRequested) return;
        _closeRequested = true; _emptyHint.Stop(); Enabled = false;
        StopSharing(); await _lifetime.CancelAsync();
        try { if (_session is not null) await _session; if (_browse is not null) await _browse; }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppendLog("[Cleanup] " + error); }
        _canClose = true; Close();
    }

    private void AppendLog(string line)
    {
        if (IsDisposed || _canClose) return;
        if (_details.TextLength > 32_000) _details.Text = _details.Text[^16_000..];
        _details.AppendText(line + Environment.NewLine); _copyLogs.Enabled = _details.TextLength > 0;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) return true;
        if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & (Keys.Alt | Keys.Control)) == 0)
        {
            var candidates = new Control[] { _receivers, _audio.Toggle, _mirror.Toggle, _keys.Toggle, _primary, _cancel,
                _refresh, _manual.Header, _address, _manualConnect, _technical.Header, _copyLogs, _details };
            var available = candidates.Where(c => c.CanSelect && c.Visible && c.Enabled && c.TabStop).ToArray();
            if (available.Length == 0) return true;
            int index = Array.FindIndex(available, c => c.Focused || c.ContainsFocus);
            int direction = (keyData & Keys.Shift) == 0 ? 1 : -1;
            int next = index < 0 ? direction > 0 ? 0 : available.Length - 1 : (index + direction + available.Length) % available.Length;
            available[next].Focus();
            if (_body.Contains(available[next])) Reveal(available[next]);
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}

internal sealed class WindowLogWriter(IProgress<string> progress) : TextWriter
{
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void WriteLine(string? value) => progress.Report(value ?? "");
}
