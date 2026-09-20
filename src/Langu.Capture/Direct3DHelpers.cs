using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Langu.Capture;

internal static class Direct3DHelpers
{
    private static readonly Guid IDirect3DDeviceGuid = new("A37634EF-0B7D-5C6C-9B24-12C1950F0B08");

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static ID3D11Device CreateD3DDevice()
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_0],
            out var device).CheckError();
        return device ?? throw new InvalidOperationException("Could not create the D3D11 device.");
    }

    public static IDirect3DDevice CreateWinRtDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var unk));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(unk);
        }
        finally
        {
            Marshal.Release(unk);
        }
    }
}
