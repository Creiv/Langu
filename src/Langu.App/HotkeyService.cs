using System.Windows.Interop;
using Langu.Core.Native;

namespace Langu.App;

public sealed class HotkeyService : IDisposable
{
    private HwndSource? _source;

    public event Action? PauseToggled;
    public event Action? WindowPickRequested;
    public event Action? RegionPickRequested;
    public event Action? LanguageToggled;

    public void Start()
    {
        _source = new HwndSource(0, 0, 0, 0, 0, 0, 0, "LanguHotkeys", IntPtr.Zero);
        _source.AddHook(WndProc);
        var hwnd = _source.Handle;
        User32.RegisterHotKey(hwnd, NativeConstants.HotkeyPause, NativeConstants.ModControl | NativeConstants.ModShift, 0x50);
        User32.RegisterHotKey(hwnd, NativeConstants.HotkeyWindow, NativeConstants.ModControl | NativeConstants.ModShift, 0x57);
        User32.RegisterHotKey(hwnd, NativeConstants.HotkeyRegion, NativeConstants.ModControl | NativeConstants.ModShift, 0x52);
        User32.RegisterHotKey(hwnd, NativeConstants.HotkeyLanguage, NativeConstants.ModControl | NativeConstants.ModShift, 0x4C);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeConstants.WmHotkey)
            return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case NativeConstants.HotkeyPause:
                PauseToggled?.Invoke();
                handled = true;
                break;
            case NativeConstants.HotkeyWindow:
                WindowPickRequested?.Invoke();
                handled = true;
                break;
            case NativeConstants.HotkeyRegion:
                RegionPickRequested?.Invoke();
                handled = true;
                break;
            case NativeConstants.HotkeyLanguage:
                LanguageToggled?.Invoke();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
            return;
        var hwnd = _source.Handle;
        User32.UnregisterHotKey(hwnd, NativeConstants.HotkeyPause);
        User32.UnregisterHotKey(hwnd, NativeConstants.HotkeyWindow);
        User32.UnregisterHotKey(hwnd, NativeConstants.HotkeyRegion);
        User32.UnregisterHotKey(hwnd, NativeConstants.HotkeyLanguage);
        _source.Dispose();
        _source = null;
    }
}
