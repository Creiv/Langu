using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Langu.Core;

namespace Langu.Translation;

public sealed class NllbTranslator : ITranslator
{
    private readonly RuntimeInstaller _installer = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _nextId;

    public bool IsReady { get; private set; }
    public string? UnavailableReason { get; private set; } = "Engine not initialized";

    public async Task InitializeAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            progress?.Report(new DownloadProgress { Stage = "Preparing CTranslate2 runtime" });
            await _installer.EnsureReadyAsync(progress, cancellationToken);
            await StartWorkerAsync(cancellationToken);
            IsReady = true;
            UnavailableReason = null;
        }
        catch (Exception ex)
        {
            IsReady = false;
            UnavailableReason = ex.Message;
            throw;
        }
    }

    public async Task<string> TranslateAsync(string text, string sourceNllb, string targetNllb, CancellationToken cancellationToken)
    {
        if (!IsReady || _stdin is null || _stdout is null)
            throw new InvalidOperationException(UnavailableReason ?? "Translator not ready");

        text = text.Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
            return "";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var blocks = text.Contains('\n')
                ? text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray()
                : [text];
            var translatedBlocks = new List<string>(blocks.Length);
            foreach (var block in blocks)
            {
                var parts = SentenceSplitter.Split(block);
                if (parts.Count == 1)
                {
                    translatedBlocks.Add(await SendAsync(parts[0], sourceNllb, targetNllb, cancellationToken));
                    continue;
                }

                var output = new List<string>(parts.Count);
                foreach (var part in parts)
                    output.Add(await SendAsync(part, sourceNllb, targetNllb, cancellationToken));
                translatedBlocks.Add(string.Join(" ", output.Where(p => !string.IsNullOrWhiteSpace(p))));
            }

            return string.Join(blocks.Length > 1 ? "\n" : " ", translatedBlocks.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> SendAsync(string text, string sourceNllb, string targetNllb, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var payload = JsonSerializer.Serialize(new
        {
            cmd = "translate",
            id,
            text,
            src = sourceNllb,
            tgt = targetNllb
        });
        await _stdin!.WriteLineAsync(payload);
        await _stdin.FlushAsync();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await _stdout!.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new InvalidOperationException("The CTranslate2 worker closed.");

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.GetString() == "ready")
                continue;
            if (root.TryGetProperty("id", out var rid) && rid.TryGetInt32(out var got) && got != id)
                continue;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidOperationException(root.TryGetProperty("error", out var err) ? err.GetString() : "Translation error");
            return root.TryGetProperty("text", out var translated) ? translated.GetString() ?? "" : "";
        }
    }

    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        await StopWorkerAsync();

        var psi = new ProcessStartInfo
        {
            FileName = AppPaths.PythonExe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(AppPaths.WorkerScript);
        psi.ArgumentList.Add(AppPaths.NllbModelDir);

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the NLLB worker.");
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _ = DrainErrorAsync(_process);

        var ready = await _stdout.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(ready))
            throw new InvalidOperationException("No response from the NLLB worker.");

        using var doc = JsonDocument.Parse(ready);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var error = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : "Worker not ready";
            throw new InvalidOperationException(error);
        }
    }

    private static async Task DrainErrorAsync(Process process)
    {
        try
        {
            _ = await process.StandardError.ReadToEndAsync();
        }
        catch
        {
            // ignore
        }
    }

    private async Task StopWorkerAsync()
    {
        try
        {
            if (_stdin is not null)
            {
                await _stdin.WriteLineAsync("""{"cmd":"exit"}""");
                await _stdin.FlushAsync();
            }
        }
        catch
        {
            // ignore
        }

        if (_process is { HasExited: false })
        {
            try
            {
                if (!_process.WaitForExit(1500))
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }
        }

        _stdin = null;
        _stdout = null;
        _process?.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopWorkerAsync();
        _gate.Dispose();
    }
}
