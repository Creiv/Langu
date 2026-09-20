using Langu.Core;

namespace Langu.Translation;

public static class HttpDownloader
{
    public static async Task DownloadAsync(
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
}
