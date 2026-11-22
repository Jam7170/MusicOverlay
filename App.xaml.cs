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

    private static Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Drawing.Color.Transparent);

            using var path = new Drawing.Drawing2D.GraphicsPath();
            const int r = 9;
            var rect = new Drawing.Rectangle(1, 1, 30, 30);
            path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();

            using var fill = new Drawing.Drawing2D.LinearGradientBrush(rect,
                Drawing.Color.FromArgb(0xE0, 0x4E, 0x5C), Drawing.Color.FromArgb(0x6F, 0x69, 0xA8), 45f);
            g.FillPath(fill, path);

            using var font = new Drawing.Font("Segoe UI Symbol", 18, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var fmt = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            g.DrawString("♪", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 1, 32, 32), fmt);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
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
