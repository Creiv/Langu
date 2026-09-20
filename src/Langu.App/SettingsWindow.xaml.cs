using System.Windows;
using System.Windows.Controls;
using Langu.Core;
using Langu.Ocr;

namespace Langu.App;

public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private bool _loading;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Loaded += (_, _) => Reload();
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void Reload()
    {
        _loading = true;
        var s = _host.Settings;
        TargetLanguageBox.SelectedIndex = s.TargetLanguage == TargetLanguage.Italian ? 0 : 1;
        FillSourceLanguages(s.SourceLanguage);
        ModeMonitor.IsChecked = s.CaptureMode == CaptureMode.Monitor;
        ModeWindow.IsChecked = s.CaptureMode == CaptureMode.Window;
        ModeRegion.IsChecked = s.CaptureMode == CaptureMode.Region;
        OcrBox.SelectedIndex = s.OcrEngine switch
        {
            OcrEngineKind.Windows => 1,
            OcrEngineKind.RapidOcr => 2,
            _ => 0
        };
        FpsSlider.Value = s.FramesPerSecond;
        OverlayCheck.IsChecked = s.OverlayEnabled;
        FillProbeKeys(s.ProbeKeyVk);
        RefreshMonitors();
        RefreshWindows();
        UpdateCapturePanels();
        UpdateOcrHint();
        UpdateRegionSummary();
        UpdateStatus();
        _loading = false;
    }

    public void UpdateStatus()
    {
        var status = _host.Pipeline.Status;
        StatusText.Text = status.Message;
        WarningText.Text = status.Warning ?? "";
        if (ModelsStatusText is not null)
        {
            ModelsStatusText.Text = status.ModelsReady
                ? "OCR and translation models ready (offline)."
                : OcrModelInstaller.CjkReady
                    ? "OCR ready. To translate: Download models."
                    : "Windows OCR is ready. Download models for CJK and translation.";
        }
        StartButton.IsEnabled = !status.Running;
        PauseButton.IsEnabled = status.Running;
        if (StatusDot is not null)
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource(status.Running ? "SuccessBrush" : "MutedBrush");
    }

    public void SetDownload(string text, double? fraction)
    {
        DownloadText.Text = text;
        DownloadProgressBar.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        if (fraction is { } value)
            DownloadProgressBar.Value = value;
    }

    private void RefreshMonitors()
    {
        var monitors = WindowEnumeration.GetMonitors();
        MonitorBox.ItemsSource = monitors;
        MonitorBox.DisplayMemberPath = nameof(MonitorInfo.DisplayName);
        if (monitors.Count == 0)
            return;
        var index = Math.Clamp(_host.Settings.TargetMonitorIndex, 0, monitors.Count - 1);
        MonitorBox.SelectedIndex = index;
    }

    private void RefreshWindows()
    {
        var windows = WindowEnumeration.GetVisibleWindows();
        WindowBox.ItemsSource = windows;
        var current = windows.FirstOrDefault(w => w.Handle == _host.Settings.TargetWindowHandle);
        WindowBox.SelectedItem = current;
    }

    private void FillSourceLanguages(string? iso)
    {
        SourceLanguageBox.Items.Clear();
        ComboBoxItem? selected = null;
        foreach (var (code, name) in SourceLanguages.Choices)
        {
            var item = new ComboBoxItem { Content = name, Tag = code ?? "" };
            SourceLanguageBox.Items.Add(item);
            if (string.Equals(code, iso, StringComparison.OrdinalIgnoreCase) ||
                (code is null && string.IsNullOrWhiteSpace(iso)))
                selected = item;
        }

        SourceLanguageBox.SelectedItem = selected ?? SourceLanguageBox.Items[0];
    }

    private void Persist()
    {
        if (_loading || !IsLoaded || TargetLanguageBox is null || MonitorBox is null || WindowBox is null ||
            ProbeKeyBox is null || SourceLanguageBox is null || OcrBox is null)
            return;

        var s = _host.Settings;
        s.TargetLanguage = TargetLanguageBox.SelectedIndex == 1 ? TargetLanguage.English : TargetLanguage.Italian;
        s.SourceLanguage = SourceLanguageBox.SelectedItem is ComboBoxItem source &&
                           source.Tag is string tag && !string.IsNullOrWhiteSpace(tag)
            ? tag
            : null;
        s.CaptureMode = ModeWindow.IsChecked == true
            ? CaptureMode.Window
            : ModeRegion.IsChecked == true
                ? CaptureMode.Region
                : CaptureMode.Monitor;
        s.OcrEngine = OcrBox.SelectedIndex switch
        {
            1 => OcrEngineKind.Windows,
            2 => OcrEngineKind.RapidOcr,
            _ => OcrEngineKind.Auto
        };
        s.FramesPerSecond = (int)FpsSlider.Value;
        s.OverlayEnabled = OverlayCheck.IsChecked == true;
        if (ProbeKeyBox.SelectedItem is ComboBoxItem keyItem && keyItem.Tag is int vk)
        {
            s.ProbeKeyVk = vk;
            _host.Input.ProbeVk = vk;
        }
        if (MonitorBox.SelectedItem is MonitorInfo monitor)
            s.TargetMonitorIndex = monitor.Index;
        if (WindowBox.SelectedItem is WindowInfo window)
            s.TargetWindowHandle = window.Handle;
        SettingsStore.Save(s);
        _host.Pipeline.ApplyLiveSettings();
        UpdateCapturePanels();
        UpdateOcrHint();
        UpdateRegionSummary();
    }

    private void UpdateCapturePanels()
    {
        if (MonitorPanel is null)
            return;
        MonitorPanel.Visibility = ModeMonitor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WindowPanel.Visibility = ModeWindow.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RegionPanel.Visibility = ModeRegion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateOcrHint()
    {
        if (OcrHintText is null)
            return;
        OcrHintText.Text = OcrBox.SelectedIndex switch
        {
            1 => "Best for YouTube, browsers, and normal Latin text. Weak on stylized kanji and hiragana.",
            2 => "Slower, but reads Japanese, Chinese, and Korean. Set the on-screen language if you can.",
            _ => "Windows reads normal text. Rapid only fills gaps for Japanese, Chinese, and Korean."
        };
    }

    private void UpdateRegionSummary()
    {
        if (RegionSummary is null)
            return;
        var s = _host.Settings;
        RegionSummary.Text = s.RegionBounds.IsEmpty
            ? "No region selected"
            : $"{s.RegionWidth}×{s.RegionHeight}  ·  {s.RegionX},{s.RegionY}";
    }

    private void OnSettingsChanged(object sender, RoutedEventArgs e) => Persist();

    private void OnCaptureModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            UpdateCapturePanels();
            return;
        }

        Persist();
    }

    private void OnOcrChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOcrHint();
        Persist();
    }

    private void OnFpsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FpsLabel is not null)
            FpsLabel.Text = $"Hold-key responsiveness: {(int)e.NewValue}";
        if (IsLoaded)
            Persist();
    }

    private void OnRefreshWindows(object sender, RoutedEventArgs e) => RefreshWindows();

    private async void OnPickWindow(object sender, RoutedEventArgs e)
    {
        Hide();
        var window = await WindowPickOverlay.PickAsync();
        Show();
        Activate();
        if (window is null)
            return;
        _host.Settings.TargetWindowHandle = window.Handle;
        _host.Settings.CaptureMode = CaptureMode.Window;
        SettingsStore.Save(_host.Settings);
        Reload();
    }

    private async void OnPickRegion(object sender, RoutedEventArgs e)
    {
        Hide();
        var region = await RegionPickOverlay.PickAsync();
        Show();
        Activate();
        if (region is null)
            return;
        _host.Settings.RegionX = region.Value.X;
        _host.Settings.RegionY = region.Value.Y;
        _host.Settings.RegionWidth = region.Value.Width;
        _host.Settings.RegionHeight = region.Value.Height;
        _host.Settings.CaptureMode = CaptureMode.Region;
        SettingsStore.Save(_host.Settings);
        Reload();
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        Persist();
        await _host.StartPipelineAsync();
    }

    private async void OnPause(object sender, RoutedEventArgs e) => await _host.StopPipelineAsync();

    private async void OnDownload(object sender, RoutedEventArgs e) => await _host.DownloadModelsAsync();

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    private void OnCaptureKey(object sender, RoutedEventArgs e)
    {
        CaptureKeyButton.Content = "Press now…";
        _host.Input.CaptureNextKey = true;
    }

    private void OnClearPins(object sender, RoutedEventArgs e) => _host.Pipeline.ClearPins();

    private void FillProbeKeys(int selectedVk)
    {
        ProbeKeyBox.Items.Clear();
        var found = false;
        foreach (var (vk, name) in ProbeKeys.Choices)
        {
            var item = new ComboBoxItem { Content = name, Tag = vk };
            ProbeKeyBox.Items.Add(item);
            if (vk == selectedVk)
            {
                ProbeKeyBox.SelectedItem = item;
                found = true;
            }
        }

        if (!found)
        {
            var extra = new ComboBoxItem { Content = ProbeKeys.NameOf(selectedVk), Tag = selectedVk };
            ProbeKeyBox.Items.Add(extra);
            ProbeKeyBox.SelectedItem = extra;
        }

        CaptureKeyButton.Content = "Press a key…";
    }
}
