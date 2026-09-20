using System.Diagnostics;
using System.IO.Compression;
using Langu.Core;

namespace Langu.Translation;

public sealed class RuntimeInstaller
{
    private const string PythonZipUrl = "https://www.python.org/ftp/python/3.12.9/python-3.12.9-embed-amd64.zip";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
    private const string NllbBase = "https://huggingface.co/mijuanlo/nllb-200-distilled-600M-ct2-int8/resolve/main/";

    private static readonly (string File, string Label)[] ModelFiles =
    [
        ("config.json", "NLLB config"),
        ("shared_vocabulary.json", "NLLB vocabulary"),
        ("sentencepiece.bpe.model", "NLLB tokenizer"),
        ("model.bin", "NLLB model (~600 MB)")
    ];

    public bool ModelsPresent =>
        ModelFiles.All(f => File.Exists(Path.Combine(AppPaths.NllbModelDir, f.File)));

    public bool PythonReady => File.Exists(AppPaths.PythonExe);

    public async Task EnsureReadyAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        using var client = CreateClient();
        await EnsurePythonAsync(client, progress, cancellationToken);
        await EnsurePipAndPackagesAsync(progress, cancellationToken);
        await EnsureWorkerAsync();
        await EnsureModelAsync(client, progress, cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Langu/1.2");
        return client;
    }

    private static async Task EnsurePythonAsync(HttpClient client, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(AppPaths.PythonExe))
            return;

        progress?.Report(new DownloadProgress { Stage = "Download Python embeddable" });
        var zipPath = Path.Combine(AppPaths.RuntimeRoot, "python-embed.zip");
        await HttpDownloader.DownloadAsync(client, PythonZipUrl, zipPath, progress, "Download Python embeddable", cancellationToken);
        ZipFile.ExtractToDirectory(zipPath, AppPaths.PythonDir, overwriteFiles: true);
        EnableSitePackages();
    }

    private static void EnableSitePackages()
    {
        var pth = Directory.GetFiles(AppPaths.PythonDir, "python*._pth").FirstOrDefault();
        if (pth is null)
            return;

        var lines = File.ReadAllLines(pth)
            .Select(l => l.TrimStart().StartsWith("#") && l.Contains("import site") ? "import site" : l)
            .ToList();
        if (!lines.Any(l => l.Trim() == "import site"))
            lines.Add("import site");
        if (!lines.Any(l => l.Contains("Lib\\site-packages", StringComparison.OrdinalIgnoreCase)))
            lines.Add("Lib\\site-packages");
        File.WriteAllLines(pth, lines);
    }

    private static async Task EnsurePipAndPackagesAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.PythonExe))
            throw new InvalidOperationException("Python runtime not found.");

        if (!await CanImportAsync("ctranslate2", cancellationToken))
        {
            progress?.Report(new DownloadProgress { Stage = "Installing pip and CTranslate2" });
            var getPip = Path.Combine(AppPaths.PythonDir, "get-pip.py");
            if (!File.Exists(getPip))
            {
                using var client = CreateClient();
                await HttpDownloader.DownloadAsync(client, GetPipUrl, getPip, progress, "Download get-pip.py", cancellationToken);
            }

            await RunPythonAsync(new[] { getPip }, cancellationToken);
            await RunPythonAsync(new[] { "-m", "pip", "install", "--upgrade", "pip", "ctranslate2", "sentencepiece" }, cancellationToken);
        }

        if (!await CanImportAsync("ctranslate2", cancellationToken))
            throw new InvalidOperationException("Could not import CTranslate2 in the Python runtime.");
    }

    private static Task EnsureWorkerAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "translator_worker.py");
        if (!File.Exists(source))
            source = Path.Combine(AppContext.BaseDirectory, "Runtime", "translator_worker.py");
        if (!File.Exists(source))
            throw new FileNotFoundException("translator_worker.py is missing from the Langu package.");

        File.Copy(source, AppPaths.WorkerScript, overwrite: true);
        return Task.CompletedTask;
    }

    private static async Task EnsureModelAsync(HttpClient client, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (var (file, label) in ModelFiles)
        {
            var dest = Path.Combine(AppPaths.NllbModelDir, file);
            if (File.Exists(dest) && new FileInfo(dest).Length > 32)
                continue;

            var url = NllbBase + file + "?download=true";
            await HttpDownloader.DownloadAsync(client, url, dest, progress, "Download " + label, cancellationToken);
        }
    }

    private static async Task<bool> CanImportAsync(string module, CancellationToken cancellationToken)
    {
        try
        {
            var code = await RunPythonAsync(new[] { "-c", $"import {module}" }, cancellationToken);
            return code == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunPythonAsync(string[] args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = AppPaths.PythonExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Python.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdoutTask;
        var err = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Python failed ({process.ExitCode}): {err} {output}".Trim());
        return process.ExitCode;
    }
}
