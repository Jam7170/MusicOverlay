using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicOverlay.Settings;
using Windows.Media.Control;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using GsmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace MusicOverlay.Media.Providers;

/// <summary>
/// Reads "now playing" from Windows' Global System Media Transport Controls — the same source the
/// volume flyout / lock screen uses. Covers the Spotify desktop app (no API keys / login needed),
/// browsers, and most other media apps. Fully event-driven: no polling.
/// </summary>
public sealed class WindowsMediaProvider : IMediaProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GsmtcManager? _manager;
    private GsmtcSession? _session;
    private readonly AppSettings _settings;

    private string _title = "", _artist = "", _album = "";
    private byte[]? _artwork;

    public WindowsMediaProvider(AppSettings settings) => _settings = settings;

    public string Id => "windows-media";
    public string DisplayName => "Windows media controls";
    public MediaState? Current { get; private set; }
    public event EventHandler<MediaState?>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _manager = await GsmtcManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _ = RebindAsync();
        _manager.SessionsChanged += (_, _) => _ = RebindAsync();
        await RebindAsync();
    }

    public void ApplySettings(AppSettings settings) => _ = RebindAsync(force: true);

    public IReadOnlyList<string> AvailableSources
    {
        get
        {
            try { return _manager?.GetSessions().Select(s => s.SourceAppUserModelId).Distinct().ToList() ?? []; }
            catch { return []; }
        }
    }

    private GsmtcSession? PickSession()
    {
        if (_manager == null) return null;
        try
        {
            // Blocked apps (e.g. a browser playing YouTube) are invisible to the overlay.
            var sessions = _manager.GetSessions().Where(s => _settings.IsSourceAllowed(s.SourceAppUserModelId)).ToList();
            if (sessions.Count == 0) return null;

            var preferred = _settings.PreferredSource;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var match = sessions.FirstOrDefault(s =>
                    s.SourceAppUserModelId.Contains(preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            var playing = sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GsmtcStatus.Playing);
            if (playing != null) return playing;

            var current = _manager.GetCurrentSession();
            return current != null && sessions.Any(s => s.SourceAppUserModelId == current.SourceAppUserModelId)
                ? current
                : sessions[0];
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private async Task RebindAsync(bool force = false)
    {
        await _gate.WaitAsync();
        try
        {
            var next = PickSession();
            if (!force && next != null && _session != null &&
                next.SourceAppUserModelId == _session.SourceAppUserModelId)
                return;

            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = next;
            _title = _artist = _album = "";
            _artwork = null;

            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackChanged;
                _session.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }
        finally
        {
            _gate.Release();
        }
        await RefreshMediaAsync();
    }

    private void OnMediaPropertiesChanged(GsmtcSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshMediaAsync();
    private void OnPlaybackChanged(GsmtcSession s, PlaybackInfoChangedEventArgs e) => Publish();
    private void OnTimelineChanged(GsmtcSession s, TimelinePropertiesChangedEventArgs e) => Publish();

    private async Task RefreshMediaAsync()
    {
        var session = _session;
        if (session != null)
        {
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (props != null && ReferenceEquals(session, _session))
                {
                    bool trackChanged = props.Title != _title || props.Artist != _artist;
                    _title = props.Title ?? "";
                    _artist = props.Artist ?? "";
                    _album = props.AlbumTitle ?? "";

                    byte[]? art = null;
                    if (props.Thumbnail != null)
                    {
                        using var ras = await props.Thumbnail.OpenReadAsync();
                        using var stream = ras.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        art = ms.ToArray();
                    }

                    // Keep the same array instance when bytes are unchanged so the UI skips re-decoding.
                    if (art == null)
                    {
                        if (trackChanged) _artwork = null;
                    }
                    else if (_artwork == null || !_artwork.AsSpan().SequenceEqual(art))
                    {
                        _artwork = art;
                    }
                }
            }
            catch (Exception ex)
            {
                // Sessions can vanish mid-call (app closed). Not an error worth surfacing.
                Debug.WriteLine(ex);
            }
        }
        Publish();
    }

    private void Publish()
    {
        var session = _session;
        MediaState? state = null;

        if (session != null && !string.IsNullOrEmpty(_title))
        {
            try
            {
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();
                var now = DateTimeOffset.Now;

                var stamp = timeline.LastUpdatedTime;
                if (stamp.Year < 2000 || stamp > now) stamp = now;

                state = new MediaState(
                    ProviderId: Id,
                    SourceApp: session.SourceAppUserModelId,
                    Title: _title,
                    Artist: _artist,
                    Album: _album,
                    Artwork: _artwork,
                    IsPlaying: playback?.PlaybackStatus == GsmtcStatus.Playing,
                    Position: timeline.Position,
                    Duration: timeline.EndTime - timeline.StartTime,
                    PositionTimestamp: stamp,
                    PlaybackRate: playback?.PlaybackRate ?? 1.0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        Current = state;
        StateChanged?.Invoke(this, state);
    }

    public async Task TogglePlayPauseAsync()
    {
        try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    public async Task NextAsync()
    {
        try { if (_session != null) await _session.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousAsync()
    {
        try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        try { if (_session != null) await _session.TryChangePlaybackPositionAsync(position.Ticks); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _session = null;
        _manager = null;
        return ValueTask.CompletedTask;
    }
}
