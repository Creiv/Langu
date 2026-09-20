using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Langu.App;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? OpenSettings;
    public event Action? TogglePause;
    public event Action? ExitRequested;

    public TrayService()
    {
        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Langu — hold-key translator",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => OpenSettings?.Invoke();
    }

    public void SetRunning(bool running)
    {
        _icon.Text = running ? "Langu — running" : "Langu — paused";
        if (_icon.ContextMenuStrip?.Items[0] is ToolStripMenuItem item)
            item.Text = running ? "Pause" : "Start";
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start / Pause", null, (_, _) => TogglePause?.Invoke());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        return menu;
    }

    private static Icon CreateIcon()
    {
        var packed = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/langu.ico"));
        if (packed is not null)
            return new Icon(packed.Stream);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "langu.ico");
        if (File.Exists(path))
            return new Icon(path);
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
