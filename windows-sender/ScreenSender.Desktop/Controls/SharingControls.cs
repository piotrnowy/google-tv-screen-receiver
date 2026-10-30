using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenSender.Windows.Discovery;

namespace ScreenSender.Desktop.Controls;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(14, 21, 31), Surface = Color.FromArgb(23, 33, 46),
        Border = Color.FromArgb(47, 63, 79), Text = Color.FromArgb(237, 243, 249), Muted = Color.FromArgb(164, 181, 197),
        Accent = Color.FromArgb(94, 212, 192), Selected = Color.FromArgb(29, 63, 65), Error = Color.FromArgb(255, 174, 163);

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        int d = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Label(string text, float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty,
        ForeColor = bold ? Text : Muted, UseMnemonic = false,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
    };
}

/// <summary>A single full-width Auto column, measured with its real wrapping width.</summary>
internal sealed class ContentStack : TableLayoutPanel
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        width = Math.Max(width, Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.MinimumSize.Width + c.Margin.Horizontal).DefaultIfEmpty(0).Max() + Padding.Horizontal);
        int height = Padding.Vertical;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            int available = Math.Max(1, width - Padding.Horizontal - child.Margin.Horizontal);
            height += child.GetPreferredSize(new Size(available, 0)).Height + child.Margin.Vertical;
        }
        return new Size(width, height);
    }
}

internal sealed class ActionButton : Button
{
    private bool _hover;
    [DefaultValue(false)]
    public bool Primary { get; init; }
    public ActionButton()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Padding = new Padding(14, 4, 14, 4); Margin = Padding.Empty;
        BackColor = Theme.Surface; ForeColor = Theme.Text; Cursor = Cursors.Hand;
        MinimumSize = new Size(84, 32);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var shape = Theme.Rounded(bounds, Math.Max(5, DeviceDpi / 12));
        Color fill = !Enabled ? Theme.Surface : Primary ? Theme.Accent : _hover ? Theme.Border : Theme.Surface;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, shape);
        using var pen = new Pen(Focused ? Theme.Text : Primary && Enabled ? Theme.Accent : Theme.Border, Focused ? 2 : 1);
        e.Graphics.DrawPath(pen, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            !Enabled ? Theme.Muted : Primary ? Theme.Background : Theme.Text,
            (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Theme.Text, fill);
    }
}

internal sealed class SurfaceCard : Panel
{
    public SurfaceCard()
    {
        BackColor = Theme.Surface; Padding = new Padding(14); Margin = Padding.Empty;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int contentWidth = Math.Max(1, width - Padding.Horizontal);
        int height = Controls.Cast<Control>().Select(c => c.GetPreferredSize(new Size(contentWidth, 0)).Height + c.Margin.Vertical).DefaultIfEmpty(0).Max();
        return new Size(width, height + Padding.Vertical);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(bounds, Math.Max(6, DeviceDpi / 12));
        using var brush = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape);
    }
}

internal sealed class DisclosureSection : UserControl
{
    public ActionButton Header { get; } = new();
    public TableLayoutPanel Body { get; } = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(0, 10, 0, 10), Margin = Padding.Empty };
    private readonly string _title;
    private bool _expanded;
    public event EventHandler? ExpandedChanged;
    [DefaultValue(false)]
    public bool Expanded
    {
        get => _expanded;
        set {
            _expanded = value; Body.Visible = value;
            Header.Text = (value ? "−  " : "+  ") + _title;
            Header.AccessibleDescription = value ? "Expanded" : "Collapsed";
            PerformLayout(); Parent?.PerformLayout();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public DisclosureSection(string title)
    {
        _title = title;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        BackColor = Theme.Background; Margin = new Padding(0, 0, 0, 4);
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Header.AccessibleName = title; Header.Dock = DockStyle.Top; Header.TextAlign = ContentAlignment.MiddleLeft; Header.TabIndex = 0;
        Header.Click += (_, _) => Expanded = !Expanded;
        Body.TabIndex = 1; Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Header, 0, 0); layout.Controls.Add(Body, 0, 1); Controls.Add(layout);
        Expanded = false;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Header is null || Body is null) return base.GetPreferredSize(proposedSize);
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Header.GetPreferredSize(new Size(width, 0)).Height + (Expanded ? Body.GetPreferredSize(new Size(width, 0)).Height : 0));
    }
}

internal sealed class SharingOption : UserControl
{
    public CheckBox Toggle { get; } = new SharingCheckBox { AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Text, Margin = Padding.Empty, UseMnemonic = false };
    public Label Description { get; }
    public SharingOption(string title, string description, bool value)
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        BackColor = Theme.Surface; Margin = Padding.Empty; Padding = new Padding(0, 0, 10, 0);
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Toggle.Text = title; Toggle.AccessibleName = title; Toggle.Checked = value;
        Toggle.Font = new Font("Segoe UI", 10, FontStyle.Bold); Toggle.TabIndex = 0;
        Description = Theme.Label(description, 9); Description.Margin = new Padding(0, 5, 0, 0);
        Toggle.AccessibleDescription = description;
        layout.Controls.Add(Toggle, 0, 0); layout.Controls.Add(Description, 0, 1); Controls.Add(layout);
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Description is null) return;
        int width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        if (Description.MaximumSize.Width != width) { Description.MaximumSize = new Size(width, 0); Parent?.PerformLayout(); }
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Description is null) return base.GetPreferredSize(proposedSize);
        int width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(100, Width);
        int textWidth = Math.Max(1, width - Padding.Horizontal);
        int descriptionHeight = TextRenderer.MeasureText(Description.Text, Description.Font, new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        return new Size(width, Toggle.GetPreferredSize(new Size(textWidth, 0)).Height + Description.Margin.Top + descriptionHeight + Padding.Vertical);
    }
}

internal sealed class SharingCheckBox : CheckBox
{
    private int Unit(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + Unit(24), Math.Max(text.Height, Unit(16)) + Unit(4));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new Rectangle(Unit(1), (Height - Unit(14)) / 2, Unit(14), Unit(14));
        using var path = Theme.Rounded(box, Unit(3));
        using var fill = new SolidBrush(Checked && Enabled ? Theme.Accent : Theme.Surface);
        using var border = new Pen(Enabled ? Theme.Accent : Theme.Muted, Math.Max(1, Unit(1)));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        if (Checked) {
            using var check = new Pen(Enabled ? Theme.Background : Theme.Muted, Math.Max(1.5f, Unit(2)));
            e.Graphics.DrawLines(check, [new Point(box.Left + Unit(3), box.Top + Unit(7)), new Point(box.Left + Unit(6), box.Top + Unit(10)), new Point(box.Left + Unit(11), box.Top + Unit(4))]);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Unit(22), 0, Math.Max(1, Width - Unit(22)), Height), Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), Theme.Text, BackColor);
    }
}

/// <summary>Native list selection/keyboard accessibility with compact, variable-height device cards.</summary>
internal sealed class ReceiverList : ListBox
{
    private readonly Func<ReceiverDevice, string> _status;
    private Font? _nameFont;
    private bool _measuring;
    public ReceiverList(Func<ReceiverDevice, string> status)
    {
        _status = status; DrawMode = DrawMode.OwnerDrawVariable; IntegralHeight = false;
        BorderStyle = BorderStyle.None; BackColor = Theme.Background; ForeColor = Theme.Text;
        DisplayMember = nameof(ReceiverDevice.Name); SelectionMode = SelectionMode.One;
        FormattingEnabled = true;
        Format += (_, e) => { if (e.ListItem is ReceiverDevice device) e.Value = $"{device.Name}, {_status(device)}, {device.Host}"; };
        AccessibleName = "Available devices"; AccessibleDescription = "Use the arrow keys to choose a receiver. Sharing starts only after Share screen.";
        Dock = DockStyle.Top; Margin = Padding.Empty; HorizontalScrollbar = false;
        Font = new Font("Segoe UI", 10); RefreshNameFont();
    }
    private int Unit(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private void RefreshNameFont() { _nameFont?.Dispose(); _nameFont = new Font(Font, FontStyle.Bold); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); RefreshNameFont(); UpdateHeight(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateHeight(); }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RefreshItems(); UpdateHeight();
    }
    private int CardHeight(int index)
    {
        string name = index >= 0 && index < Items.Count && Items[index] is ReceiverDevice d ? d.Name : "Receiver";
        int nameHeight = TextRenderer.MeasureText(name, _nameFont ?? Font, new Size(Math.Max(Unit(120), ClientSize.Width - Unit(64)), 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        return Math.Max(Unit(72), nameHeight + Font.Height + Unit(30));
    }
    public void UpdateHeight()
    {
        if (IsDisposed || _measuring) return;
        _measuring = true;
        try {
            int total = Enumerable.Range(0, Math.Min(Items.Count, 3)).Sum(CardHeight);
            int desired = Math.Max(Unit(1), total);
            if (Height != desired) Height = desired;
            if (IsHandleCreated) for (int i = 0; i < Items.Count; i++) SendMessage(Handle, 0x01A0, (IntPtr)i, (IntPtr)CardHeight(i));
            Invalidate();
        }
        finally { _measuring = false; }
    }
    protected override void OnMeasureItem(MeasureItemEventArgs e) { e.ItemHeight = CardHeight(e.Index); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not ReceiverDevice device) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var rect = new Rectangle(e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - 2, e.Bounds.Height - Unit(6));
        using var background = new SolidBrush(Theme.Background);
        e.Graphics.FillRectangle(background, e.Bounds);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(rect, Unit(8));
        using var fill = new SolidBrush(selected ? Theme.Selected : Theme.Surface);
        using var border = new Pen(selected ? Theme.Accent : Theme.Border, selected ? 2 : 1);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        int dot = Unit(8), left = rect.Left + Unit(32), width = Math.Max(1, rect.Width - Unit(48));
        using var indicator = new SolidBrush(device.Busy && _status(device) == "Unavailable" ? Theme.Muted : Theme.Accent);
        e.Graphics.FillEllipse(indicator, rect.Left + Unit(14), rect.Top + Unit(17), dot, dot);
        int nameHeight = TextRenderer.MeasureText(device.Name, _nameFont ?? Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        TextRenderer.DrawText(e.Graphics, device.Name, _nameFont ?? Font, new Rectangle(left, rect.Top + Unit(10), width, nameHeight), Theme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        int bottom = rect.Top + Unit(12) + nameHeight;
        var hostSize = TextRenderer.MeasureText(device.Host, Font);
        int hostWidth = Math.Min(hostSize.Width, width / 2);
        TextRenderer.DrawText(e.Graphics, _status(device), Font, new Rectangle(left, bottom, Math.Max(1, width - hostWidth - Unit(12)), Font.Height + Unit(4)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, device.Host, Font, new Rectangle(left + width - hostWidth, bottom, hostWidth, Font.Height + Unit(4)), Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && selected) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -Unit(5), -Unit(5)), Theme.Text, Theme.Selected);
    }
    protected override void Dispose(bool disposing) { if (disposing) _nameFont?.Dispose(); base.Dispose(disposing); }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wparam, IntPtr lparam);
}
