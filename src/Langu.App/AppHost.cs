using System.Windows;
using System.Windows.Threading;
using Langu.Core;
using Langu.Overlay;

namespace Langu.App;

public sealed class AppHost : IDisposable
{
    public AppSettings Settings { get; }
    public TranslationPipeline Pipeline { get; }
    public OverlayWindow Overlay { get; }
    public SettingsWindow SettingsWindow { get; }

    private readonly TrayService _tray = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly HoldInputService _input;
    private readonly DispatcherTimer _inputWatch;
    private bool _busy;

    public AppHost()
    {
        Settings = SettingsStore.Load();
        Overlay = new OverlayWindow();
        Pipeline = new TranslationPipeline(Settings, Overlay);
        SettingsWindow = new SettingsWindow(this);
        _input = new HoldInputService(SettingsWindow.Dispatcher);

        Pipeline.StatusChanged += () => SettingsWindow.Dispatcher.Invoke(SettingsWindow.UpdateStatus);
        _tray.OpenSettings += ShowSettings;
        _tray.TogglePause += () => _ = ToggleAsync();
        _tray.ExitRequested += Exit;
        _hotkeys.PauseToggled += () => _ = ToggleAsync();
        _hotkeys.WindowPickRequested += () => _ = PickWindowAsync();
        _hotkeys.RegionPickRequested += () => _ = PickRegionAsync();
        _hotkeys.LanguageToggled += ToggleLanguage;
        _hotkeys.Start();

        _input.ProbeVk = Settings.ProbeKeyVk;
        _input.ProbeHeldChanged += OnProbeHeld;
        _input.AbortRequested += () => Pipeline.AbortAll();
        _input.CanHit = Pipeline.HasHit;
        _input.MouseClicked = OnProbeClick;
        _input.FocusPickStarted = Pipeline.BeginFocusPick;
        _input.FocusPickMoved = Pipeline.UpdateFocusPick;
        _input.FocusPickEnded = Pipeline.EndFocusPick;
        _input.PointerMoved = Pipeline.SetPointer;
        _input.KeyCaptured += OnKeyCaptured;
        _input.Start();

        _inputWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _inputWatch.Tick += (_, _) =>
        {
            _input.EnsureKeyboardHook();
            if (_input.Armed)
            {
                _input.EnsureMouseHook();
                _input.SyncFromPhysicalKey();
            }
        };
        _inputWatch.Start();
    }

    public HoldInputService Input => _input;

    private void OnProbeHeld(bool held) => Pipeline.SetProbeHeld(held);

    private void OnProbeClick(int x, int y, bool right) => Pipeline.HandleClick(x, y, right);

    private void OnKeyCaptured(int vk)
    {
        Settings.ProbeKeyVk = vk;
        _input.ProbeVk = vk;
        SettingsStore.Save(Settings);
        SettingsWindow.Dispatcher.Invoke(SettingsWindow.Reload);
    }

    public void ShowSettings()
    {
        SettingsWindow.Show();
        SettingsWindow.Reload();
        SettingsWindow.Activate();
    }

    public async Task DownloadModelsAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                var fraction = p.Fraction;
                var extra = fraction is { } f && p.TotalBytes is { } total
                    ? $" ({FormatBytes(p.BytesReceived)} / {FormatBytes(total)})"
                    : "";
                SettingsWindow.Dispatcher.Invoke(() => SettingsWindow.SetDownload(p.Stage + extra, fraction));
            });
            await Pipeline.PrepareModelsAsync(progress, CancellationToken.None);
            SettingsWindow.Dispatcher.Invoke(() => SettingsWindow.SetDownload("Models ready. You can use Langu offline.", 1));
        }
        catch (Exception ex)
        {
            SettingsWindow.Dispatcher.Invoke(() => SettingsWindow.SetDownload("Error: " + ex.Message, 0));
            MessageBox.Show(ex.Message, "Langu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            SettingsWindow.Dispatcher.Invoke(SettingsWindow.UpdateStatus);
        }
    }

    public async Task StartPipelineAsync()
    {
        SettingsStore.Save(Settings);
        if (!Pipeline.Translator.IsReady)
            await DownloadModelsAsync();
        if (!Pipeline.Translator.IsReady)
            return;
        _input.ProbeVk = Settings.ProbeKeyVk;
        _input.SetArmed(true);
        await Pipeline.StartAsync();
        _tray.SetRunning(true);
        SettingsWindow.UpdateStatus();
    }

    public async Task StopPipelineAsync()
    {
        _input.SetArmed(false);
        await Pipeline.StopAsync();
        _tray.SetRunning(false);
        SettingsWindow.UpdateStatus();
    }

    public async Task ToggleAsync()
    {
        if (Pipeline.Status.Running)
            await StopPipelineAsync();
        else
            await StartPipelineAsync();
    }

    private async Task PickWindowAsync()
    {
        var wasRunning = Pipeline.Status.Running;
        if (wasRunning)
            await StopPipelineAsync();
        var window = await WindowPickOverlay.PickAsync();
        if (window is not null)
        {
            Settings.TargetWindowHandle = window.Handle;
            Settings.CaptureMode = CaptureMode.Window;
            SettingsStore.Save(Settings);
            SettingsWindow.Reload();
        }
        if (wasRunning)
            await StartPipelineAsync();
    }

    private async Task PickRegionAsync()
    {
        var wasRunning = Pipeline.Status.Running;
        if (wasRunning)
            await StopPipelineAsync();
        var region = await RegionPickOverlay.PickAsync();
        if (region is not null)
        {
            Settings.RegionX = region.Value.X;
            Settings.RegionY = region.Value.Y;
            Settings.RegionWidth = region.Value.Width;
            Settings.RegionHeight = region.Value.Height;
            Settings.CaptureMode = CaptureMode.Region;
            SettingsStore.Save(Settings);
            SettingsWindow.Reload();
        }
        if (wasRunning)
            await StartPipelineAsync();
    }

    private void ToggleLanguage()
    {
        Settings.TargetLanguage = Settings.TargetLanguage == TargetLanguage.Italian
            ? TargetLanguage.English
            : TargetLanguage.Italian;
        SettingsStore.Save(Settings);
        SettingsWindow.Reload();
    }

    private async void Exit()
    {
        await StopPipelineAsync();
        System.Windows.Application.Current.Shutdown();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):0.0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.00} GB";
    }

    public void Dispose()
    {
        _inputWatch.Stop();
        _input.Dispose();
        _hotkeys.Dispose();
        _tray.Dispose();
    }
}
