using System.Runtime.InteropServices;
using System.Text;

namespace Langu.Core.Native;

public static class NativeConstants
{
    public const int GwlExStyle = -20;
    public const int GwlStyle = -16;
    public const int WsExLayered = 0x00080000;
    public const int WsExTransparent = 0x00000020;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTopmost = 0x00000008;
    public const int WsCaption = 0x00C00000;
    public const int WsVisible = 0x10000000;
    public const int SwHide = 0;
    public const int SwShow = 5;
    public const int HwndTopmost = -1;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint WdaExcludeFromCapture = 0x00000011;
    public const uint WdaNone = 0x00000000;
    public const int MonitorDefaultToNearest = 2;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const int WmHotkey = 0x0312;
    public const int HotkeyPause = 1;
    public const int HotkeyWindow = 2;
    public const int HotkeyRegion = 3;
    public const int HotkeyLanguage = 4;
    public const uint GaRoot = 2;
    public const int SmileVisible = 0;
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int WmMouseMove = 0x0200;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDblClk = 0x0203;
    public const int WmRButtonDown = 0x0204;
    public const int WmRButtonUp = 0x0205;
    public const int WmRButtonDblClk = 0x0206;
    public const int WmMButtonDown = 0x0207;
    public const int WmMButtonUp = 0x0208;
    public const int WmMButtonDblClk = 0x0209;
}

public delegate IntPtr NativeHookProc(int nCode, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential)]
public struct KbdLlHookStruct
{
    public uint VkCode;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public UIntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MsLlHookStruct
{
    public WinPoint Pt;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct WinRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public ScreenRect ToScreenRect() => new(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct MonitorInfoNative
{
    public int Size;
    public WinRect Monitor;
    public WinRect Work;
    public uint Flags;
}

public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
public delegate bool EnumMonitorsProc(IntPtr hMonitor, IntPtr hdc, ref WinRect rect, IntPtr dwData);

public static class User32
{
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(long pt, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoNative lpmi);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(WinPoint point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out WinPoint lpPoint);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(int idHook, NativeHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}

public static class Kernel32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}

[StructLayout(LayoutKind.Sequential)]
public struct WinPoint
{
    public int X;
    public int Y;
}

public static class Shcore
{
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}

public static class DwmApi
{
    public const int DwmwaExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out WinRect pvAttribute,
        int cbAttribute);

    public static bool TryGetExtendedFrameBounds(IntPtr hwnd, out ScreenRect bounds)
    {
        bounds = ScreenRect.Empty;
        try
        {
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<WinRect>()) != 0)
                return false;
            bounds = rect.ToScreenRect();
            return !bounds.IsEmpty;
        }
        catch
        {
            return false;
        }
    }
}
