using Langu.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Langu.Capture;

public sealed class DxgiMonitorCapture : IFrameCapture
{
    private readonly ScreenRect _monitorBounds;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private ID3D11Texture2D? _staging;
    private SizeI _stagingSize;

    private DxgiMonitorCapture(
        ScreenRect monitorBounds,
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGIOutputDuplication duplication)
    {
        _monitorBounds = monitorBounds;
        CurrentScreenBounds = monitorBounds;
        _device = device;
        _context = context;
        _duplication = duplication;
    }

    public ScreenRect CurrentScreenBounds { get; }
    public string EngineName => "DXGI";

    public static DxgiMonitorCapture? TryCreate(ScreenRect monitorBounds)
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
            {
                using (adapter)
                {
                    for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                    {
                        using (output)
                        {
                            var desc = output.Description;
                            var bounds = new ScreenRect(desc.DesktopCoordinates.Left, desc.DesktopCoordinates.Top,
                                desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                                desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top);
                            if (bounds.X != monitorBounds.X || bounds.Y != monitorBounds.Y)
                                continue;

                            D3D11.D3D11CreateDevice(
                                adapter,
                                DriverType.Unknown,
                                DeviceCreationFlags.BgraSupport,
                                [FeatureLevel.Level_11_0],
                                out var device).CheckError();
                            if (device is null)
                                continue;

                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            var duplication = output1.DuplicateOutput(device);
                            return new DxgiMonitorCapture(monitorBounds, device, device.ImmediateContext, duplication);
                        }
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public Task<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = _duplication.AcquireNextFrame(80, out _, out var resource);
            if (result.Failure || resource is null)
                return Task.FromResult<CapturedFrame?>(null);

            using (resource)
            using (var texture = resource.QueryInterface<ID3D11Texture2D>())
            {
                var desc = texture.Description;
                var width = (int)desc.Width;
                var height = (int)desc.Height;
                EnsureStaging(width, height);
                _context.CopyResource(_staging!, texture);
                _duplication.ReleaseFrame();

                var mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var packed = new byte[width * height * 4];
                    var destStride = width * 4;
                    unsafe
                    {
                        var src = (byte*)mapped.DataPointer;
                        for (var y = 0; y < height; y++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(
                                (IntPtr)(src + y * (int)mapped.RowPitch),
                                packed,
                                y * destStride,
                                destStride);
                        }
                    }
                    return Task.FromResult<CapturedFrame?>(
                        FrameBuffer.FromPackedBgra(packed, width, height, _monitorBounds));
                }
                finally
                {
                    _context.Unmap(_staging, 0);
                }
            }
        }
        catch
        {
            try { _duplication.ReleaseFrame(); } catch { /* ignore */ }
            return Task.FromResult<CapturedFrame?>(null);
        }
    }

    private void EnsureStaging(int width, int height)
    {
        if (_staging is not null && _stagingSize.Width == width && _stagingSize.Height == height)
            return;

        _staging?.Dispose();
        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = SampleDescription.Default,
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read
        };
        _staging = _device.CreateTexture2D(desc);
        _stagingSize = new SizeI(width, height);
    }

    public ValueTask DisposeAsync()
    {
        _staging?.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
        return ValueTask.CompletedTask;
    }
}
