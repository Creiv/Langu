using System.Diagnostics;
using System.Text;
using Langu.Core.Native;

namespace Langu.Core;

public static class WindowEnumeration
{
    public static IReadOnlyList<WindowInfo> GetVisibleWindows()
    {
        var list = new List<WindowInfo>();
        User32.EnumWindows((hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;

            var style = User32.GetWindowLong(hWnd, NativeConstants.GwlStyle);
            if ((style & NativeConstants.WsCaption) == 0 && User32.GetWindowTextLength(hWnd) == 0)
                return true;

            var title = GetTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            User32.GetWindowThreadProcessId(hWnd, out var pid);
            var processName = "";
            try
            {
                processName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                // ignore
            }

            if (processName.Equals("Langu", StringComparison.OrdinalIgnoreCase))
                return true;

            var bounds = GetVisibleBounds(hWnd);
            if (bounds.Width < 80 || bounds.Height < 80)
                return true;

            list.Add(new WindowInfo
            {
                Handle = hWnd.ToInt64(),
                Title = title,
                ProcessName = processName,
                Bounds = bounds,
                IsMinimized = User32.IsIconic(hWnd),
                LooksFullscreen = LooksFullscreen(hWnd, bounds)
            });
            return true;
        }, IntPtr.Zero);

        return list
            .OrderBy(w => w.ProcessName)
            .ThenBy(w => w.Title)
            .ToList();
    }

    public static WindowInfo? TryGetWindow(long handle)
    {
        var hwnd = new IntPtr(handle);
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
            return null;

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        var processName = "";
        try { processName = Process.GetProcessById((int)pid).ProcessName; }
        catch { /* ignore */ }

        var bounds = GetVisibleBounds(hwnd);
        return new WindowInfo
        {
            Handle = handle,
            Title = GetTitle(hwnd),
            ProcessName = processName,
            Bounds = bounds,
            IsMinimized = User32.IsIconic(hwnd),
            LooksFullscreen = LooksFullscreen(hwnd, bounds)
        };
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref WinRect _, IntPtr _) =>
        {
            var info = new MonitorInfoNative { Size = MarshalSize() };
            if (!User32.GetMonitorInfo(hMonitor, ref info))
                return true;

            list.Add(new MonitorInfo
            {
                Index = list.Count,
                Handle = hMonitor.ToInt64(),
                Bounds = info.Monitor.ToScreenRect(),
                IsPrimary = (info.Flags & 1) != 0
            });
            return true;
        }, IntPtr.Zero);

        return list
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.X)
            .Select((m, i) => new MonitorInfo
            {
                Index = i,
                Handle = m.Handle,
                Bounds = m.Bounds,
                IsPrimary = m.IsPrimary
            })
            .ToList();
    }

    public static WindowInfo? FromPoint(int x, int y)
    {
        var hwnd = User32.WindowFromPoint(new WinPoint { X = x, Y = y });
        if (hwnd == IntPtr.Zero)
            return null;
        var root = User32.GetAncestor(hwnd, NativeConstants.GaRoot);
        if (root != IntPtr.Zero)
            hwnd = root;
        return TryGetWindow(hwnd.ToInt64());
    }

    public static bool LooksFullscreen(IntPtr hwnd, ScreenRect bounds)
    {
        var monitor = User32.MonitorFromWindow(hwnd, NativeConstants.MonitorDefaultToNearest);
        var info = new MonitorInfoNative { Size = MarshalSize() };
        if (!User32.GetMonitorInfo(monitor, ref info))
            return false;

        var monitorRect = info.Monitor.ToScreenRect();
        var style = User32.GetWindowLong(hwnd, NativeConstants.GwlStyle);
        var covers = Math.Abs(bounds.X - monitorRect.X) <= 2
                     && Math.Abs(bounds.Y - monitorRect.Y) <= 2
                     && Math.Abs(bounds.Width - monitorRect.Width) <= 4
                     && Math.Abs(bounds.Height - monitorRect.Height) <= 4;
        return covers && (style & NativeConstants.WsCaption) == 0;
    }

    public static ScreenRect GetVisibleBounds(IntPtr hwnd)
    {
        if (DwmApi.TryGetExtendedFrameBounds(hwnd, out var visible) && visible.Width > 40 && visible.Height > 40)
            return visible;
        User32.GetWindowRect(hwnd, out var rect);
        return rect.ToScreenRect();
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var len = User32.GetWindowTextLength(hwnd);
        if (len <= 0)
            return "";
        var sb = new StringBuilder(len + 1);
        User32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static int MarshalSize() => System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfoNative>();
}
