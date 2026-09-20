using Langu.Core;

namespace Langu.Ocr;

public static class OcrModelInstaller
{
    private static readonly OcrFile[] Files =
    [
        new(
            "ch_PP-OCRv5_rec_mobile.onnx",
            "OCR CJK/giapponese (riconoscimento)",
            1_000_000,
            [
                "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx",
                "https://huggingface.co/SWHL/RapidOCR/resolve/main/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx"
            ]),
        new(
            "ppocrv5_dict.txt",
            "OCR CJK dizionario",
            5_000,
            [
                "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt",
                "https://huggingface.co/SWHL/RapidOCR/resolve/main/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt"
            ]),
        new(
            "japan_PP-OCRv4_rec_mobile.onnx",
            "OCR giapponese (specialistico)",
            500_000,
            [
                "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile.onnx",
                "https://huggingface.co/SWHL/RapidOCR/resolve/main/onnx/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile.onnx"
            ]),
        new(
            "japan_dict.txt",
            "OCR giapponese dizionario",
            1_000,
            [
                "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile/japan_dict.txt",
                "https://huggingface.co/SWHL/RapidOCR/resolve/main/paddle/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile/japan_dict.txt"
            ])
    ];

    public static bool CjkReady =>
        Exists("ch_PP-OCRv5_rec_mobile.onnx", 1_000_000) && Exists("ppocrv5_dict.txt", 5_000);

    public static async Task EnsureAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        Directory.CreateDirectory(AppPaths.OcrModelDir);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Langu/1.0");

        foreach (var file in Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest = Path.Combine(AppPaths.OcrModelDir, file.Name);
            if (File.Exists(dest) && new FileInfo(dest).Length >= file.MinBytes)
            {
                CopyToApp(file.Name);
                continue;
            }

            Exception? last = null;
            foreach (var url in file.Urls)
            {
                try
                {
                    progress?.Report(new DownloadProgress { Stage = $"Download {file.Label}" });
                    await DownloadAsync(client, url, dest, progress, file.Label, cancellationToken);
                    if (File.Exists(dest) && new FileInfo(dest).Length >= file.MinBytes)
                    {
                        CopyToApp(file.Name);
                        last = null;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            if (last is not null && !CjkReady && file.Name.StartsWith("ch_", StringComparison.Ordinal))
                throw last;
        }
    }

    public static IEnumerable<string> ModelDirectories()
    {
        yield return AppPaths.OcrModelDir;
        yield return Path.Combine(AppContext.BaseDirectory, "models", "v5");
    }

    public static string? Find(params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var dir in ModelDirectories())
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path) && new FileInfo(path).Length > 500)
                    return path;
            }
        }

        return null;
    }

    private static bool Exists(string name, long minBytes)
    {
        var path = Find(name);
        return path is not null && new FileInfo(path).Length >= minBytes;
    }

    private static void CopyToApp(string name)
    {
        var source = Path.Combine(AppPaths.OcrModelDir, name);
        if (!File.Exists(source))
            return;
        try
        {
            var destDir = Path.Combine(AppContext.BaseDirectory, "models", "v5");
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, name);
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(source).Length)
                File.Copy(source, dest, overwrite: true);
        }
        catch
        {
            // cartella dell'exe non scrivibile
        }
    }

    private static async Task DownloadAsync(
        HttpClient client,
        string url,
        string destination,
        IProgress<DownloadProgress>? progress,
        string stage,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".partial";
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress?.Report(new DownloadProgress
                {
                    Stage = stage,
                    BytesReceived = received,
                    TotalBytes = total
                });
            }
        }

        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(partial, destination);
    }

    private sealed record OcrFile(string Name, string Label, long MinBytes, string[] Urls);
}
