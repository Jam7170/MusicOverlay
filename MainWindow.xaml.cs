using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicOverlay.Media;
using MusicOverlay.Settings;
using MusicOverlay.Theming;
using MediaState = MusicOverlay.Media.MediaState;

namespace MusicOverlay;

public partial class MainWindow : Window
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(450));
    private static readonly IEasingFunction FadeEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

    private readonly MediaHub _hub;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _tick;

    private MediaState? _state;
    private byte[]? _artBytes;
    private BitmapSource? _artColor, _artGray;
    private ArtAnalysis _analysis = ArtAnalysis.Default;
    private OverlayPalette? _palette;
    private bool _userHidden;
    private bool _idleExpired;
    private readonly DispatcherTimer _idleTimer;
    private string _elapsedText = "", _durationText = "";
    private double _fillWidth = -1;

    // Animatable brushes shared by the visual tree.
    private readonly SolidColorBrush _title = new(), _artist = new(), _time = new(), _fill = new(), _track = new(),
        _buttonFill = new(), _buttonBorder = new(), _glyph = new(), _border = new(), _tint = new();

    public event EventHandler? ExitRequested;

    public MainWindow(MediaHub hub, AppSettings settings)
    {
        _hub = hub;
        _settings = settings;
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => UpdateProgress();
        // Grace period before "hide when nothing is playing" kicks in, so pauses/track skips don't flicker.
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
        _idleTimer.Tick += (_, _) => { _idleTimer.Stop(); _idleExpired = true; UpdateVisibility(); };
        InitializeComponent();

        TitleText.Foreground = _title;
        ArtistText.Foreground = _artist;
        ElapsedText.Foreground = DurationText.Foreground = _time;
        ProgressFill.Background = _fill;
        ProgressTrack.Background = _track;
        PlayPauseButton.Background = _buttonFill;
        PlayPauseButton.BorderBrush = _buttonBorder;
        PlayPauseGlyph.Fill = PlayPauseGlyph.Stroke = _glyph;
        CardBorder.BorderBrush = _border;
        ArtTint.Fill = _tint;

        ApplyFont();
        ApplyWindowSettings();
        InitPosition();

        ContextMenu = BuildContextMenu();
        _hub.Changed += (_, s) => Dispatcher.BeginInvoke(() => OnState(s));

        OnState(_hub.Current, immediate: true);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Tool window: keeps the overlay out of Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        AttachPositionHandling(hwnd);
    }

    // ---------------- state ----------------

    private void OnState(MediaState? state, bool immediate = false)
    {
        _state = state;

        RenderText();
        if (state == null) SetArtwork(null);
        else if (!ReferenceEquals(state.Artwork, _artBytes)) SetArtwork(state.Artwork);

        bool playing = state?.IsPlaying == true;
        PlayPauseGlyph.Data = (Geometry)FindResource(playing ? "PauseGlyph" : "PlayGlyph");

        ApplyPalette(immediate);
        UpdateVisibility();
        UpdateProgress();

        if (playing && IsVisible) _tick.Start(); else _tick.Stop();
    }

    private void RenderText()
    {
        TitleText.Text = ApplyCase(_state?.Title ?? "Nothing playing");
        ArtistText.Text = ApplyCase(_state?.Artist ?? "");
    }

    private string ApplyCase(string s) => _settings.TextCase switch
    {
        TextCase.Lowercase => s.ToLowerInvariant(),
        TextCase.Uppercase => s.ToUpperInvariant(),
        _ => s,
    };

    private void SetArtwork(byte[]? bytes)
    {
        _artBytes = bytes;
        _artColor = _artGray = null;
        _analysis = ArtAnalysis.Default;

        if (bytes is { Length: > 0 })
        {
            try
            {
                // Peek at the size so huge images are decoded down, small ones aren't upscaled.
                int width;
                using (var peek = new MemoryStream(bytes))
                    width = BitmapFrame.Create(peek, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).PixelWidth;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.StreamSource = new MemoryStream(bytes);
                if (width > 720) bmp.DecodePixelWidth = 720;
                bmp.EndInit();
                bmp.Freeze();
                _artColor = bmp;

                var gray = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
                gray.Freeze();
                _artGray = gray;

                _analysis = Palettes.Analyze(bmp);
            }
            catch
            {
                _artColor = _artGray = null;
            }
        }
    }

    private void ApplyPalette(bool immediate)
    {
        var theme = _settings.Theme;
        if (theme == OverlayTheme.Auto) theme = _state?.IsPlaying == true ? OverlayTheme.Vivid : OverlayTheme.Dusk;
        var p = theme switch
        {
            OverlayTheme.Dusk => Palettes.Dusk,
            OverlayTheme.Classic => Palettes.Classic,
            _ => Palettes.Vivid(_analysis),
        };

        ArtImage.Source = p.DesaturateArt ? _artGray : _artColor;
        if (p == _palette) return;
        _palette = p;

        Animate(PanelTopStop, p.PanelTop, immediate);
        Animate(PanelBottomStop, p.PanelBottom, immediate);
        Animate(FadeStartStop, Color.FromArgb(0, p.PanelTop.R, p.PanelTop.G, p.PanelTop.B), immediate);
        Animate(FadeEndStop, p.PanelTop, immediate);
        Animate(_title, p.Title, immediate);
        Animate(_artist, p.Artist, immediate);
        Animate(_time, p.Time, immediate);
        Animate(_fill, p.ProgressFill, immediate);
        Animate(_track, p.ProgressTrack, immediate);
        Animate(_buttonFill, p.ButtonFill, immediate);
        Animate(_buttonBorder, p.ButtonBorder, immediate);
        Animate(_glyph, p.ButtonGlyph, immediate);
        Animate(_border, p.CardBorder, immediate);
        Animate(_tint, p.ArtTint, immediate);

        if (immediate) ArtTint.Opacity = p.ArtTintOpacity;
        else ArtTint.BeginAnimation(OpacityProperty, new DoubleAnimation(p.ArtTintOpacity, FadeDuration) { EasingFunction = FadeEase });
    }

    private static void Animate(Animatable target, Color to, bool immediate)
    {
        var prop = target is GradientStop ? GradientStop.ColorProperty : SolidColorBrush.ColorProperty;
        if (immediate)
        {
            target.BeginAnimation(prop, null);
            target.SetValue(prop, to);
        }
        else
        {
            target.BeginAnimation(prop, new ColorAnimation(to, FadeDuration) { EasingFunction = FadeEase });
        }
    }

    private void UpdateProgress()
    {
        var s = _state;
        TimeSpan pos = TimeSpan.Zero, dur = TimeSpan.Zero;
        if (s != null)
        {
            pos = s.EstimatedPosition(DateTimeOffset.Now);
            dur = s.Duration;
        }

        var elapsed = Format(pos);
        if (elapsed != _elapsedText) ElapsedText.Text = _elapsedText = elapsed;
        var duration = Format(dur);
        if (duration != _durationText) DurationText.Text = _durationText = duration;

        double frac = dur > TimeSpan.Zero ? Math.Clamp(pos / dur, 0, 1) : 0;
        double width = ProgressHost.ActualWidth * frac;
        if (width > 0 && width < ProgressFill.Height) width = ProgressFill.Height; // keep the rounded cap
        width = Math.Round(width);
        if (width != _fillWidth) ProgressFill.Width = _fillWidth = width;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---------------- visibility ----------------

    public void ToggleUserVisibility()
    {
        _userHidden = !_userHidden;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool idle = _state is not { IsPlaying: true };
        var behavior = _settings.IdleBehavior;
        if (!idle || behavior == IdleBehavior.Show)
        {
            _idleTimer.Stop();
            _idleExpired = false;
        }
        else if (!_idleExpired && !_idleTimer.IsEnabled)
        {
            _idleTimer.Start();
        }

        bool idleActive = idle && _idleExpired;
        bool show = !_userHidden && !(idleActive && behavior == IdleBehavior.Hide);
        double opacity = IsMouseOver ? 1.0
            : idleActive && behavior == IdleBehavior.Translucent ? _settings.IdleOpacity
            : _settings.PlayingOpacity;   // playing, recently paused, or idle set to "Same as playing"
        opacity = Math.Clamp(opacity, 0.1, 1.0);

        if (show) FadeIn(opacity);
        else FadeOutAndHide();

        if (show && _state?.IsPlaying == true) _tick.Start(); else _tick.Stop();
    }

    private bool _hiding;
    private double _targetOpacity = -1;

    private void FadeIn(double opacity)
    {
        _hiding = false;
        if (!IsVisible)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            _targetOpacity = 0;
            Show();
        }
        if (Math.Abs(_targetOpacity - opacity) < 0.001) return;
        _targetOpacity = opacity;
        // Dimming is slow and gentle; coming back is quicker so it feels responsive.
        var duration = opacity < Opacity ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromMilliseconds(250);
        BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = FadeEase });
    }

    private void FadeOutAndHide()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        _targetOpacity = 0;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(450)) { EasingFunction = FadeEase };
        fade.Completed += (_, _) =>
        {
            // A show request may have arrived mid-fade.
            if (!_hiding) return;
            _hiding = false;
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (!_hiding && IsVisible) UpdateVisibility(); // translucent -> full while hovered
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_hiding && IsVisible) UpdateVisibility();
    }

    // ---------------- input ----------------

    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _hub.TogglePlayPauseAsync();

    private async void Progress_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_state is not { Duration.Ticks: > 0 } s || ProgressHost.ActualWidth <= 0) return;
        double frac = Math.Clamp(e.GetPosition(ProgressHost).X / ProgressHost.ActualWidth, 0, 1);
        // Optimistic update so the bar responds immediately.
        _state = s with { Position = s.Duration * frac, PositionTimestamp = DateTimeOffset.Now };
        UpdateProgress();
        await _hub.SeekAsync(s.Duration * frac);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || _settings.Locked) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        SavePosition();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // Ctrl + wheel resizes.
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetScale(_settings.Scale + (e.Delta > 0 ? 0.05 : -0.05));
        e.Handled = true;
    }

    private void Card_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Card.Clip = new RectangleGeometry(new Rect(e.NewSize), 22, 22);

    private void ProgressHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _fillWidth = -1;
        UpdateProgress();
    }

    // ---------------- settings / menu ----------------

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item("Play / pause", async () => await _hub.TogglePlayPauseAsync()));
        menu.Items.Add(Item("Next track", async () => await _hub.NextAsync()));
        menu.Items.Add(Item("Previous track", async () => await _hub.PreviousAsync()));
        menu.Items.Add(Separator());

        var appearance = new MenuItem { Header = "Appearance" };

        var theme = new MenuItem { Header = "Theme" };
        foreach (var t in Enum.GetValues<OverlayTheme>())
            theme.Items.Add(Item(t.ToString(), () => { _settings.Theme = t; _palette = null; ApplyPalette(false); _settings.Save(); }));
        appearance.Items.Add(theme);

        var text = new MenuItem { Header = "Text" };
        foreach (var c in Enum.GetValues<TextCase>())
        {
            var label = c switch
            {
                TextCase.Lowercase => "lowercase",
                TextCase.Uppercase => "UPPERCASE",
                _ => "As is",
            };
            text.Items.Add(Item(label, () => { _settings.TextCase = c; RenderText(); _settings.Save(); }));
        }
        appearance.Items.Add(text);

        var size = new MenuItem { Header = "Size" };
        foreach (var scale in new[] { 0.75, 0.9, 1.0, 1.25, 1.5 })
            size.Items.Add(Item($"{scale * 100:0}%", () => SetScale(scale)));
        appearance.Items.Add(size);

        menu.Items.Add(appearance);

        var sources = new MenuItem { Header = "Sources" };
        sources.Items.Add(new MenuItem()); // placeholder so the arrow shows; rebuilt on open
        menu.Items.Add(sources);
        menu.Items.Add(Separator());

        var topmost = Item("Always on top", () => { _settings.Topmost = !_settings.Topmost; ApplyWindowSettings(); _settings.Save(); });
        var locked = Item("Lock position", () => { _settings.Locked = !_settings.Locked; _settings.Save(); });
        // Opacity > While playing / When paused or nothing playing. Each item knows how to tell if it's selected.
        var checks = new List<(MenuItem Item, Func<bool> IsSelected)>();
        double[] levels = [0.85, 0.7, 0.5, 0.35, 0.2];
        static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

        var opacityMenu = new MenuItem { Header = "Opacity" };

        var playingMenu = new MenuItem { Header = "While playing" };
        foreach (var level in levels.Prepend(1.0))
        {
            var mi = Item($"{level * 100:0}%", () => { _settings.PlayingOpacity = level; UpdateVisibility(); _settings.Save(); });
            playingMenu.Items.Add(mi);
            checks.Add((mi, () => Near(_settings.PlayingOpacity, level)));
        }
        opacityMenu.Items.Add(playingMenu);

        var idleMenu = new MenuItem { Header = "When paused / nothing playing" };
        var same = Item("Same as playing", () => { _settings.IdleBehavior = IdleBehavior.Show; UpdateVisibility(); _settings.Save(); });
        idleMenu.Items.Add(same);
        checks.Add((same, () => _settings.IdleBehavior == IdleBehavior.Show));
        foreach (var level in levels)
        {
            var mi = Item($"{level * 100:0}%", () =>
            {
                _settings.IdleBehavior = IdleBehavior.Translucent;
                _settings.IdleOpacity = level;
                UpdateVisibility();
                _settings.Save();
            });
            idleMenu.Items.Add(mi);
            checks.Add((mi, () => _settings.IdleBehavior == IdleBehavior.Translucent && Near(_settings.IdleOpacity, level)));
        }
        idleMenu.Items.Add(Separator());
        var hide = Item("Hide", () => { _settings.IdleBehavior = IdleBehavior.Hide; UpdateVisibility(); _settings.Save(); });
        idleMenu.Items.Add(hide);
        checks.Add((hide, () => _settings.IdleBehavior == IdleBehavior.Hide));
        opacityMenu.Items.Add(idleMenu);

        var startup = Item("Start with Windows", () =>
        {
            try { StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled); }
            catch { /* registry blocked by policy: leave unchanged */ }
        });

        menu.Items.Add(topmost);
        menu.Items.Add(locked);
        menu.Items.Add(startup);
        menu.Items.Add(opacityMenu);
        menu.Items.Add(Separator());
        menu.Items.Add(Item("Hide overlay", ToggleUserVisibility));
        menu.Items.Add(Item("Exit", () => ExitRequested?.Invoke(this, EventArgs.Empty)));

        // Refresh check marks each time the menu opens.
        menu.Opened += (_, _) =>
        {
            for (int i = 0; i < theme.Items.Count; i++)
                ((MenuItem)theme.Items[i]).IsChecked = (int)_settings.Theme == i;
            for (int i = 0; i < text.Items.Count; i++)
                ((MenuItem)text.Items[i]).IsChecked = (int)_settings.TextCase == i;
            var scales = new[] { 0.75, 0.9, 1.0, 1.25, 1.5 };
            for (int i = 0; i < size.Items.Count; i++)
                ((MenuItem)size.Items[i]).IsChecked = Math.Abs(_settings.Scale - scales[i]) < 0.01;
            RebuildSourcesMenu(sources);
            topmost.IsChecked = _settings.Topmost;
            locked.IsChecked = _settings.Locked;
            foreach (var (item, isSelected) in checks) item.IsChecked = isSelected();
            startup.IsChecked = StartupRegistration.IsEnabled;
        };
        return menu;
    }

    /// <summary>
    /// One entry per app Windows currently sees (ticked = shown), plus any blocked apps that aren't running
    /// so they can be unblocked, plus the Spotify preference toggles.
    /// </summary>
    private void RebuildSourcesMenu(MenuItem sources)
    {
        sources.Items.Clear();

        var live = _hub.AvailableSources;
        var blockedOffline = _settings.BlockedSources
            .Where(b => !live.Any(l => l.Contains(b, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (live.Count == 0 && blockedOffline.Count == 0)
            sources.Items.Add(new MenuItem { Header = "No media apps detected", IsEnabled = false });

        foreach (var id in live)
        {
            bool blocked = IsBlocked(id);
            var mi = Item(SourceNames.Friendly(id), () => SetBlocked(id, !blocked));
            mi.IsChecked = !blocked;
            sources.Items.Add(mi);
        }

        if (blockedOffline.Count > 0)
        {
            sources.Items.Add(Separator());
            foreach (var id in blockedOffline)
                sources.Items.Add(Item($"Unblock {SourceNames.Friendly(id)}", () => SetBlocked(id, false)));
        }

        sources.Items.Add(Separator());
        bool preferSpotify = string.Equals(_settings.PreferredSource, "Spotify", StringComparison.OrdinalIgnoreCase);
        var prefer = Item("Prefer Spotify", () =>
        {
            _settings.PreferredSource = preferSpotify ? "" : "Spotify";
            if (preferSpotify) _settings.PreferredSourceOnly = false;
            ApplySourceSettings();
        });
        prefer.IsChecked = preferSpotify;
        sources.Items.Add(prefer);

        var only = Item("Spotify only", () =>
        {
            _settings.PreferredSourceOnly = !_settings.PreferredSourceOnly;
            if (_settings.PreferredSourceOnly) _settings.PreferredSource = "Spotify";
            ApplySourceSettings();
        });
        only.IsChecked = _settings.PreferredSourceOnly && preferSpotify;
        sources.Items.Add(only);
    }

    private bool IsBlocked(string id) =>
        _settings.BlockedSources.Any(b => id.Contains(b, StringComparison.OrdinalIgnoreCase));

    private void SetBlocked(string id, bool blocked)
    {
        // Copy-on-write: providers read this list from background threads.
        var list = _settings.BlockedSources
            .Where(b => !id.Contains(b, StringComparison.OrdinalIgnoreCase) && !b.Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (blocked) list.Add(id);
        _settings.BlockedSources = list;
        ApplySourceSettings();
    }

    private void ApplySourceSettings()
    {
        _hub.ApplySettings(_settings);
        _settings.Save();
    }

    private static MenuItem Item(string header, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    private static Separator Separator() => new() { Style = (Style)Application.Current.FindResource("MenuSeparator") };

    private void SetScale(double scale)
    {
        _settings.Scale = Math.Round(Math.Clamp(scale, 0.5, 2.5), 2);
        ApplyWindowSettings();
        _settings.Save();
    }

    private void ApplyWindowSettings()
    {
        Topmost = _settings.Topmost;
        RootScale.ScaleX = RootScale.ScaleY = Math.Clamp(_settings.Scale, 0.5, 2.5);
    }

    private void ApplyFont()
    {
        var fontsDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
        FontFamily = Directory.Exists(fontsDir)
            ? new FontFamily(new Uri(AppContext.BaseDirectory), "./Fonts/#Quicksand, " + _settings.FontFamily)
            : new FontFamily(_settings.FontFamily);
    }

    // ---------------- interop ----------------

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLong(nint hWnd, int nIndex, nint dwNewLong);
}
