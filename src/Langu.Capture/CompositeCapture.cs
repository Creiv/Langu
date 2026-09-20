using Langu.Core;

namespace Langu.Capture;

public sealed class CompositeCapture : IFrameCapture
{
    private readonly IFrameCapture _inner;
    private readonly ScreenRect? _crop;
    private readonly Func<ScreenRect>? _liveBounds;

    private CompositeCapture(IFrameCapture inner, ScreenRect? crop, Func<ScreenRect>? liveBounds, string engineName)
    {
        _inner = inner;
        _crop = crop;
        _liveBounds = liveBounds;
        EngineName = engineName;
    }

    public ScreenRect CurrentScreenBounds =>
        _liveBounds?.Invoke() ?? _crop ?? _inner.CurrentScreenBounds;

    public string EngineName { get; }

    public static async Task<IFrameCapture> CreateAsync(AppSettings settings)
    {
        return settings.CaptureMode switch
        {
            CaptureMode.Window => await CreateWindowAsync(settings),
            CaptureMode.Region => await CreateRegionAsync(settings),
            _ => await CreateMonitorAsync(settings)
        };
    }

    public async Task<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken)
    {
        if (_inner is GdiCapture gdi && _liveBounds is not null)
            gdi.UpdateBounds(_liveBounds());

        var frame = await _inner.CaptureAsync(cancellationToken);
        if (frame is null)
            return null;

        var live = CurrentScreenBounds;
        if (live.IsEmpty)
            return null;

        if (_crop is null)
            return frame.WithBounds(live);

        var host = _inner.CurrentScreenBounds;
        if (!host.IsEmpty)
            frame = frame.WithBounds(host);

        if (frame.ScreenBounds.X == live.X
            && frame.ScreenBounds.Y == live.Y
            && frame.ScreenBounds.Width == live.Width
            && frame.ScreenBounds.Height == live.Height)
            return frame.WithBounds(live);

        return FrameBuffer.Crop(frame, live);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private static Task<IFrameCapture> CreateWindowAsync(AppSettings settings)
    {
        var info = WindowEnumeration.TryGetWindow(settings.TargetWindowHandle);
        if (info is null)
            return Task.FromResult<IFrameCapture>(new GdiCapture(ScreenRect.Empty));

        var hwnd = new IntPtr(info.Handle);
        var wgc = GraphicsCaptureSource.TryCreateForWindow(hwnd, info.Bounds);
        if (wgc is not null)
        {
            return Task.FromResult<IFrameCapture>(new CompositeCapture(
                wgc,
                null,
                () => WindowEnumeration.TryGetWindow(settings.TargetWindowHandle)?.Bounds ?? ScreenRect.Empty,
                wgc.EngineName));
        }

        var gdi = new GdiCapture(info.Bounds);
        return Task.FromResult<IFrameCapture>(new CompositeCapture(
            gdi,
            null,
            () => WindowEnumeration.TryGetWindow(settings.TargetWindowHandle)?.Bounds ?? ScreenRect.Empty,
            gdi.EngineName));
    }

    private static Task<IFrameCapture> CreateMonitorAsync(AppSettings settings)
    {
        var monitors = WindowEnumeration.GetMonitors();
        var monitor = monitors.ElementAtOrDefault(settings.TargetMonitorIndex) ?? monitors.FirstOrDefault();
        if (monitor is null)
            return Task.FromResult<IFrameCapture>(new GdiCapture(ScreenRect.Empty));

        var wgc = GraphicsCaptureSource.TryCreateForMonitor(new IntPtr(monitor.Handle), monitor.Bounds);
        if (wgc is not null)
            return Task.FromResult<IFrameCapture>(new CompositeCapture(wgc, monitor.Bounds, null, wgc.EngineName));

        var dxgi = DxgiMonitorCapture.TryCreate(monitor.Bounds);
        if (dxgi is not null)
            return Task.FromResult<IFrameCapture>(new CompositeCapture(dxgi, monitor.Bounds, null, dxgi.EngineName));

        var gdi = new GdiCapture(monitor.Bounds);
        return Task.FromResult<IFrameCapture>(new CompositeCapture(gdi, monitor.Bounds, null, gdi.EngineName));
    }

    private static async Task<IFrameCapture> CreateRegionAsync(AppSettings settings)
    {
        var region = settings.RegionBounds;
        var monitors = WindowEnumeration.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => !m.Bounds.Intersect(region).IsEmpty) ?? monitors.FirstOrDefault();
        var host = monitor is null
            ? await Task.FromResult<IFrameCapture>(new GdiCapture(region))
            : await CreateMonitorCapture(monitor);

        return new CompositeCapture(host, region, () => settings.RegionBounds, host.EngineName + "+regione");
    }

    private static Task<IFrameCapture> CreateMonitorCapture(MonitorInfo monitor)
    {
        var wgc = GraphicsCaptureSource.TryCreateForMonitor(new IntPtr(monitor.Handle), monitor.Bounds);
        if (wgc is not null)
            return Task.FromResult<IFrameCapture>(wgc);

        var dxgi = DxgiMonitorCapture.TryCreate(monitor.Bounds);
        if (dxgi is not null)
            return Task.FromResult<IFrameCapture>(dxgi);

        return Task.FromResult<IFrameCapture>(new GdiCapture(monitor.Bounds));
    }
}
