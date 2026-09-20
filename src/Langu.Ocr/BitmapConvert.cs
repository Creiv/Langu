using System.Runtime.InteropServices;
using Langu.Core;
using SkiaSharp;
using Windows.Graphics.Imaging;
using WinRT;

namespace Langu.Ocr;

internal static class BitmapConvert
{
    public static SKBitmap ToSkia(CapturedFrame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var dest = bitmap.GetPixels();
        if (frame.Stride == frame.Width * 4)
        {
            Marshal.Copy(frame.Bgra, 0, dest, frame.Width * frame.Height * 4);
            return bitmap;
        }

        unsafe
        {
            var ptr = (byte*)dest;
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Bgra, y * frame.Stride, (IntPtr)(ptr + y * frame.Width * 4), frame.Width * 4);
            }
        }
        return bitmap;
    }

    public static unsafe SoftwareBitmap ToSoftwareBitmap(CapturedFrame frame)
    {
        var software = new SoftwareBitmap(BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Premultiplied);
        using var buffer = software.LockBuffer(BitmapBufferAccessMode.Write);
        using var reference = buffer.CreateReference();
        var access = reference.As<IMemoryBufferByteAccess>();
        access.GetBuffer(out var ptr, out _);
        var desc = buffer.GetPlaneDescription(0);
        unsafe
        {
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Bgra, y * frame.Stride, (IntPtr)(ptr + y * desc.Stride), frame.Width * 4);
            }
        }
        return software;
    }
}

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}
