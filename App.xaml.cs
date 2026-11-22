using System;
using System.IO;
using System.Threading;
using System.Windows;
using MusicOverlay.Media;
using MusicOverlay.Settings;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MusicOverlay;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private MediaHub? _hub;
    private MainWindow? _window;
    private AppSettings _settings = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] == "--snapshot")
        {
            try { Snapshot.Run(e.Args[1]); }
            catch (Exception ex) { LogError(ex); }
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, @"Local\MusicOverlay.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        _settings = AppSettings.Load();
        _hub = new MediaHub(ProviderRegistry.Create(_settings), _settings);
        _window = new MainWindow(_hub, _settings);
        _window.ExitRequested += (_, _) => ExitApp();
        // The window shows itself (respecting "hide when idle").

        CreateTray();
        await _hub.StartAsync();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide", null, (_, _) => _window?.ToggleUserVisibility());
        menu.Items.Add("Reset position", null, (_, _) => _window?.ResetPosition());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Music Overlay",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) _window?.ToggleUserVisibility();
        };
    }

    /// <summary>The embedded app.ico (same as the exe icon) at the system's small-icon size.</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (info != null)
            {
                using var stream = info.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch
        {
            // fall through
        }
        return Drawing.SystemIcons.Application;
    }

    private async void ExitApp()
    {
        _settings.Save();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (_hub != null) await _hub.DisposeAsync();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Directory);
            File.AppendAllText(Path.Combine(AppSettings.Directory, "error.log"), $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}
