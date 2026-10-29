using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ScreenSender.Windows.Capture;

public sealed record WindowsMonitor(string DeviceName, Rectangle Bounds, bool IsPrimary)
{
    public string Description => IsPrimary ? "Primary monitor" : DeviceName;

    public static WindowsMonitor Select(string selection)
    {
        // Physical pixels are necessary: otherwise gdigrab offsets differ at 125%/150% DPI.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var monitors = new List<WindowsMonitor>();
        int monitorError = 0;
        MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) { monitorError = Marshal.GetLastWin32Error(); return false; }
            monitors.Add(new WindowsMonitor(info.DeviceName,
                Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
                (info.Flags & 1) != 0));
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new Win32Exception(monitorError != 0 ? monitorError : Marshal.GetLastWin32Error(), "Could not enumerate Windows monitors.");
        var ordered = monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.DeviceName, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0) throw new InvalidOperationException("No desktop monitor is available. Run in an interactive Windows session.");
        if (selection.Equals("primary", StringComparison.OrdinalIgnoreCase)) return ordered.First(m => m.IsPrimary);
        int index = int.Parse(selection) - 1;
        if (index >= ordered.Count) throw new ArgumentException($"Monitor {index + 1} does not exist; found {ordered.Count} monitor(s).");
        return ordered[index];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
