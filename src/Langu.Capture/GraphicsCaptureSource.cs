using System.Runtime.InteropServices;
using Langu.Core;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace Langu.Capture;

public sealed class GraphicsCaptureSource : IFrameCapture
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid InteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private CapturedFrame? _latest;
    private readonly object _lock = new();
    private SizeInt32 _lastSize;
    private bool _disposed;

    private GraphicsCaptureSource(IDirect3DDevice device, GraphicsCaptureItem item, ScreenRect bounds)
    {
        _device = device;
        _item = item;
        CurrentScreenBounds = bounds;
        _lastSize = item.Size;
        item.Closed += OnClosed;
        Start();
    }

    public ScreenRect CurrentScreenBounds { get; private set; }
    public string EngineName => "GraphicsCapture";

    public static GraphicsCaptureSource? TryCreateForWindow(IntPtr hwnd, ScreenRect bounds)
    {
        try
        {
            var item = CreateItem(hwnd, monitor: false);
            if (item is null)
                return null;
            var d3d = Direct3DHelpers.CreateD3DDevice();
            var winrt = Direct3DHelpers.CreateWinRtDevice(d3d);
            d3d.Dispose();
            return new GraphicsCaptureSource(winrt, item, bounds);
        }
        catch
        {
            return null;
        }
    }

    public static GraphicsCaptureSource? TryCreateForMonitor(IntPtr hMonitor, ScreenRect bounds)
    {
        try
        {
            var item = CreateItem(hMonitor, monitor: true);
            if (item is null)
                return null;
            var d3d = Direct3DHelpers.CreateD3DDevice();
            var winrt = Direct3DHelpers.CreateWinRtDevice(d3d);
            d3d.Dispose();
            return new GraphicsCaptureSource(winrt, item, bounds);
        }
        catch
        {
            return null;
        }
    }

    public Task<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_latest);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _item.Closed -= OnClosed;
        _session?.Dispose();
        _pool?.Dispose();
        await Task.CompletedTask;
    }

    private void Start()
    {
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _lastSize);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);
        try { _session.IsCursorCaptureEnabled = false; } catch { /* older OS */ }
        try { _session.GetType().GetProperty("IsBorderRequired")?.SetValue(_session, false); } catch { /* Win10 */ }
        _session.StartCapture();
    }

    private void OnClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_lock)
            _latest = null;
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null)
            return;

        if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
        {
            _lastSize = frame.ContentSize;
            sender.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _lastSize);
        }

        try
        {
            using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            using var bgra = SoftwareBitmap.Convert(software, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var captured = CopySoftwareBitmap(bgra, CurrentScreenBounds);
            lock (_lock)
                _latest = captured;
        }
        catch
        {
            // frame drop
        }
    }

    private static unsafe CapturedFrame? CopySoftwareBitmap(SoftwareBitmap bitmap, ScreenRect bounds)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var access = reference.As<IMemoryBufferByteAccess>();
        access.GetBuffer(out var ptr, out var capacity);
        var desc = buffer.GetPlaneDescription(0);
        var packed = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        var destStride = bitmap.PixelWidth * 4;
        unsafe
        {
            var src = (byte*)ptr;
            for (var y = 0; y < bitmap.PixelHeight; y++)
            {
                Marshal.Copy((IntPtr)(src + y * desc.Stride), packed, y * destStride, destStride);
            }
        }

        _ = capacity;
        return FrameBuffer.FromPackedBgra(packed, bitmap.PixelWidth, bitmap.PixelHeight, bounds);
    }

    private static GraphicsCaptureItem? CreateItem(IntPtr handle, bool monitor)
    {
        var factoryIid = InteropIid;
        var hr = RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref factoryIid, out var factoryPtr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var interop = (IGraphicsCaptureItemInterop)Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IGraphicsCaptureItemInterop));
            var itemIid = GraphicsCaptureItemIid;
            var itemPtr = monitor
                ? interop.CreateForMonitor(handle, ref itemIid)
                : interop.CreateForWindow(handle, ref itemIid);
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        finally
        {
            Marshal.Release(factoryPtr);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        ref Guid iid,
        out IntPtr factory);
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}
