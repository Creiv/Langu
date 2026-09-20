using System.IO;
using System.Windows;
using System.Windows.Threading;
using Langu.Core;

namespace Langu.App;

public partial class App : System.Windows.Application
{
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("Task", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        try
        {
            if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
            {
                var code = SmokeTest.Run();
                Shutdown(code);
                return;
            }

            var ocrFile = e.Args
                .SkipWhile(a => !a.Equals("--ocr-file", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(ocrFile))
            {
                var code = SmokeTest.OcrFile(ocrFile);
                Shutdown(code);
                return;
            }

            _host = new AppHost();
            _host.ShowSettings();
        }
        catch (Exception ex)
        {
            Log("Startup", ex);
            MessageBox.Show(ex.ToString(), "Langu — errore di avvio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher", e.Exception);
        MessageBox.Show(e.Exception.Message, "Langu", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(string source, Exception? ex)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(
                Path.Combine(AppPaths.Root, "error.log"),
                $"[{DateTime.Now:o}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopPipelineAsync();
            await _host.Pipeline.DisposeAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
