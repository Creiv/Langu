using System.Drawing;
using Langu.Core;

namespace Langu.Capture;

public sealed class GdiCapture : IFrameCapture
{
    private ScreenRect _bounds;

    public GdiCapture(ScreenRect bounds)
    {
        _bounds = bounds;
    }

    public ScreenRect CurrentScreenBounds => _bounds;
    public string EngineName => "GDI";

    public void UpdateBounds(ScreenRect bounds) => _bounds = bounds;

    public Task<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken)
    {
        if (_bounds.IsEmpty)
            return Task.FromResult<CapturedFrame?>(null);

        try
        {
            using var bitmap = new Bitmap(_bounds.Width, _bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(_bounds.X, _bounds.Y, 0, 0, new Size(_bounds.Width, _bounds.Height));
            return Task.FromResult(FrameBuffer.FromBitmap(bitmap, _bounds));
        }
        catch
        {
            return Task.FromResult<CapturedFrame?>(null);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
