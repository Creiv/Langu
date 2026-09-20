using System.Runtime.InteropServices;
using System.Windows.Threading;
using Langu.Core;
using Langu.Core.Native;

namespace Langu.App;

public sealed class HoldInputService : IDisposable
{
    private readonly NativeHookProc _keyboardProc;
    private readonly NativeHookProc _mouseProc;
    private readonly Dispatcher _dispatcher;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _held;
    private bool _eatLeft;
    private bool _eatRight;
    private bool _middleDrag;
    private bool _movePosted;
    private int _lastMx;
    private int _lastMy;
    private long _downTick;
    private long _lastShortUpTick;
    private bool _suppressHold;

    public int ProbeVk { get; set; } = ProbeKeys.DefaultVk;
    public bool CaptureNextKey { get; set; }
    public bool Armed { get; set; }

    public event Action<bool>? ProbeHeldChanged;
    public event Action? AbortRequested;
    public Func<int, int, bool>? CanHit { get; set; }
    public Action<int, int, bool>? MouseClicked { get; set; }
    public Action<int, int>? FocusPickStarted { get; set; }
    public Action<int, int>? FocusPickMoved { get; set; }
    public Action<int, int>? FocusPickEnded { get; set; }
    public Action<int, int>? PointerMoved { get; set; }
    public event Action<int>? KeyCaptured;

    public HoldInputService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public void Start()
    {
        EnsureKeyboardHook();
    }

    public void SetArmed(bool armed)
    {
        Armed = armed;
        if (armed)
        {
            EnsureKeyboardHook();
            EnsureMouseHook();
        }
        else
        {
            _held = false;
            _suppressHold = false;
            _lastShortUpTick = 0;
        }
    }

    public void SyncFromPhysicalKey()
    {
        if (_suppressHold)
            return;
        if (!Armed)
        {
            if (_held)
            {
                _held = false;
                ProbeHeldChanged?.Invoke(false);
            }
            return;
        }

        var down = (User32.GetAsyncKeyState(ProbeVk) & 0x8000) != 0
                   || ExtraDown(ProbeVk);
        if (down == _held)
            return;
        _held = down;
        ProbeHeldChanged?.Invoke(down);
    }

    public void EnsureKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;
        var module = Kernel32.GetModuleHandle(null);
        _keyboardHook = User32.SetWindowsHookEx(NativeConstants.WhKeyboardLl, _keyboardProc, module, 0);
    }

    public void EnsureMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
            return;
        var module = Kernel32.GetModuleHandle(null);
        _mouseHook = User32.SetWindowsHookEx(NativeConstants.WhMouseLl, _mouseProc, module, 0);
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var vk = (int)data.VkCode;
            var down = msg is NativeConstants.WmKeyDown or NativeConstants.WmSysKeyDown;
            var up = msg is NativeConstants.WmKeyUp or NativeConstants.WmSysKeyUp;

            if (CaptureNextKey && down && vk is not 0x1B)
            {
                CaptureNextKey = false;
                var normalized = NormalizeVk(vk, data.Flags);
                Post(() => KeyCaptured?.Invoke(normalized));
                return User32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (Armed && ProbeKeys.Matches(ProbeVk, vk, data.Flags))
            {
                var now = Environment.TickCount64;
                if (down && !_held)
                {
                    if (_lastShortUpTick != 0 && now - _lastShortUpTick <= 400)
                    {
                        _held = false;
                        _suppressHold = true;
                        _lastShortUpTick = 0;
                        Post(() => AbortRequested?.Invoke());
                    }
                    else
                    {
                        _downTick = now;
                        _held = true;
                        Post(() => ProbeHeldChanged?.Invoke(true));
                    }
                }
                else if (up && (_held || _suppressHold))
                {
                    var wasHold = _held;
                    var duration = now - _downTick;
                    _held = false;
                    if (_suppressHold)
                    {
                        _suppressHold = false;
                    }
                    else
                    {
                        _lastShortUpTick = duration is > 0 and < 280 ? now : 0;
                        if (wasHold)
                            Post(() => ProbeHeldChanged?.Invoke(false));
                    }
                }
            }
        }

        return User32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        var msg = wParam.ToInt32();

        if (_eatRight && msg is NativeConstants.WmRButtonUp or NativeConstants.WmRButtonDblClk)
        {
            if (msg == NativeConstants.WmRButtonUp)
                _eatRight = false;
            return new IntPtr(1);
        }

        if (_eatLeft && msg is NativeConstants.WmLButtonUp or NativeConstants.WmLButtonDblClk)
        {
            if (msg == NativeConstants.WmLButtonUp)
                _eatLeft = false;
            return new IntPtr(1);
        }

        if (_middleDrag)
        {
            if (msg == NativeConstants.WmMouseMove)
            {
                var move = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                _lastMx = move.Pt.X;
                _lastMy = move.Pt.Y;
                if (!_movePosted)
                {
                    _movePosted = true;
                    Post(() =>
                    {
                        _movePosted = false;
                        FocusPickMoved?.Invoke(_lastMx, _lastMy);
                    });
                }

                return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            if (msg is NativeConstants.WmMButtonUp or NativeConstants.WmMButtonDblClk)
            {
                var end = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                _middleDrag = false;
                if (msg == NativeConstants.WmMButtonUp)
                    Post(() => FocusPickEnded?.Invoke(end.Pt.X, end.Pt.Y));
                return new IntPtr(1);
            }
        }

        if (!Armed || !_held)
            return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        if (msg == NativeConstants.WmMouseMove)
        {
            var move = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            _lastMx = move.Pt.X;
            _lastMy = move.Pt.Y;
            if (!_movePosted)
            {
                _movePosted = true;
                Post(() =>
                {
                    _movePosted = false;
                    PointerMoved?.Invoke(_lastMx, _lastMy);
                });
            }

            return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        if (msg is NativeConstants.WmRButtonDown or NativeConstants.WmRButtonUp or NativeConstants.WmRButtonDblClk)
        {
            if (msg == NativeConstants.WmRButtonDown)
            {
                var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                _eatRight = true;
                Post(() => MouseClicked?.Invoke(data.Pt.X, data.Pt.Y, true));
            }

            return new IntPtr(1);
        }

        if (msg is NativeConstants.WmMButtonDown or NativeConstants.WmMButtonUp or NativeConstants.WmMButtonDblClk)
        {
            if (msg == NativeConstants.WmMButtonDown)
            {
                var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                _middleDrag = true;
                _lastMx = data.Pt.X;
                _lastMy = data.Pt.Y;
                Post(() => FocusPickStarted?.Invoke(data.Pt.X, data.Pt.Y));
            }

            return new IntPtr(1);
        }

        if (msg == NativeConstants.WmLButtonDown)
        {
            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if (CanHit?.Invoke(data.Pt.X, data.Pt.Y) == true)
            {
                _eatLeft = true;
                Post(() => MouseClicked?.Invoke(data.Pt.X, data.Pt.Y, false));
                return new IntPtr(1);
            }
        }

        return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(action, DispatcherPriority.Input);
            return;
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Input);
    }

    private static bool ExtraDown(int vk) => vk switch
    {
        0xA4 => (User32.GetAsyncKeyState(0x12) & 0x8000) != 0,
        0xA5 => (User32.GetAsyncKeyState(0x12) & 0x8000) != 0,
        0xA2 or 0xA3 => (User32.GetAsyncKeyState(0x11) & 0x8000) != 0,
        0x10 => (User32.GetAsyncKeyState(0x10) & 0x8000) != 0,
        _ => false
    };

    private static int NormalizeVk(int vk, uint flags)
    {
        var extended = (flags & 1) != 0;
        return vk switch
        {
            0x12 => extended ? 0xA5 : 0xA4,
            0x11 => extended ? 0xA3 : 0xA2,
            0xA0 or 0xA1 => 0x10,
            _ => vk
        };
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }
}
