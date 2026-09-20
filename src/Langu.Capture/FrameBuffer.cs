using System.Drawing;
using System.Drawing.Imaging;
using Langu.Core;

namespace Langu.Capture;

public static class FrameBuffer
{
    public static CapturedFrame FromPackedBgra(byte[] bgra, int width, int height, ScreenRect screenBounds) =>
        new()
        {
            Bgra = bgra,
            Width = width,
            Height = height,
            Stride = width * 4,
            ScreenBounds = screenBounds
        };

    public static CapturedFrame Crop(CapturedFrame frame, ScreenRect region)
    {
        var clipped = region.Intersect(frame.ScreenBounds);
        if (clipped.IsEmpty)
            return FromPackedBgra([], 0, 0, ScreenRect.Empty);

        var sx = frame.Width / (double)Math.Max(1, frame.ScreenBounds.Width);
        var sy = frame.Height / (double)Math.Max(1, frame.ScreenBounds.Height);
        var srcX = (int)Math.Round((clipped.X - frame.ScreenBounds.X) * sx);
        var srcY = (int)Math.Round((clipped.Y - frame.ScreenBounds.Y) * sy);
        var w = Math.Max(1, (int)Math.Round(clipped.Width * sx));
        var h = Math.Max(1, (int)Math.Round(clipped.Height * sy));
        srcX = Math.Clamp(srcX, 0, Math.Max(0, frame.Width - 1));
        srcY = Math.Clamp(srcY, 0, Math.Max(0, frame.Height - 1));
        w = Math.Min(w, frame.Width - srcX);
        h = Math.Min(h, frame.Height - srcY);
        var dest = new byte[w * h * 4];

        for (var y = 0; y < h; y++)
        {
            var srcOff = (srcY + y) * frame.Stride + srcX * 4;
            var dstOff = y * w * 4;
            var count = w * 4;
            if (srcOff + count <= frame.Bgra.Length && dstOff + count <= dest.Length)
                Buffer.BlockCopy(frame.Bgra, srcOff, dest, dstOff, count);
        }

        return FromPackedBgra(dest, w, h, clipped);
    }

    public static CapturedFrame? FromBitmap(Bitmap bitmap, ScreenRect screenBounds)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var packed = new byte[bitmap.Width * bitmap.Height * 4];
            var srcStride = data.Stride;
            var destStride = bitmap.Width * 4;
            unsafe
            {
                var src = (byte*)data.Scan0;
                for (var y = 0; y < bitmap.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        (IntPtr)(src + y * srcStride),
                        packed,
                        y * destStride,
                        destStride);
                }
            }
            return FromPackedBgra(packed, bitmap.Width, bitmap.Height, screenBounds);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
