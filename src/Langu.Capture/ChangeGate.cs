using Langu.Core;

namespace Langu.Capture;

public sealed class ChangeGate
{
    private ulong _lastHash;
    private bool _hasLast;

    public bool HasChanged(CapturedFrame frame, int threshold = 20)
    {
        var hash = StableHash(frame);
        if (!_hasLast)
        {
            _lastHash = hash;
            _hasLast = true;
            return true;
        }

        var distance = Hamming(_lastHash, hash);
        if (distance <= threshold)
            return false;

        _lastHash = hash;
        return true;
    }

    public void Reset()
    {
        _hasLast = false;
        _lastHash = 0;
    }

    private static ulong StableHash(CapturedFrame frame)
    {
        const int size = 8;
        Span<int> lum = stackalloc int[size * size];
        Span<int> varAcc = stackalloc int[size * size];
        var cellW = Math.Max(1, frame.Width / size);
        var cellH = Math.Max(1, frame.Height / size);
        var counts = new int[size * size];

        for (var y = 0; y < frame.Height; y += 2)
        {
            var cy = Math.Min(size - 1, y / cellH);
            var row = y * frame.Stride;
            for (var x = 0; x < frame.Width; x += 2)
            {
                var cx = Math.Min(size - 1, x / cellW);
                var i = row + x * 4;
                if (i + 2 >= frame.Bgra.Length)
                    continue;
                var l = (frame.Bgra[i + 2] * 30 + frame.Bgra[i + 1] * 59 + frame.Bgra[i] * 11) / 100;
                var idx = cy * size + cx;
                lum[idx] += l;
                varAcc[idx] += l * l;
                counts[idx]++;
            }
        }

        var variances = new int[size * size];
        long sum = 0;
        var used = 0;
        for (var i = 0; i < lum.Length; i++)
        {
            if (counts[i] == 0)
                continue;
            lum[i] /= counts[i];
            var meanSq = lum[i] * lum[i];
            variances[i] = Math.Max(0, varAcc[i] / counts[i] - meanSq);
            sum += lum[i];
            used++;
        }

        var avg = used > 0 ? (int)(sum / used) : 0;
        var varSorted = variances.Where((_, i) => counts[i] > 0).OrderByDescending(v => v).ToArray();
        var varCut = varSorted.Length > 0 ? varSorted[Math.Min(varSorted.Length - 1, varSorted.Length / 4)] : int.MaxValue;

        ulong hash = 0;
        for (var i = 0; i < lum.Length; i++)
        {
            if (counts[i] == 0 || variances[i] >= varCut && varCut > 80)
                continue;
            if (lum[i] >= avg)
                hash |= 1UL << i;
        }
        return hash;
    }

    private static int Hamming(ulong a, ulong b)
    {
        var x = a ^ b;
        var n = 0;
        while (x != 0)
        {
            n += (int)(x & 1);
            x >>= 1;
        }
        return n;
    }
}
