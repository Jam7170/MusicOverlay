using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicOverlay.Settings;

namespace MusicOverlay.Media;

/// <summary>
/// Aggregates all providers and exposes a single "current" track. Selection rule:
/// stick with the active provider while it is playing; otherwise switch to any provider
/// that is playing; otherwise keep showing the last provider that has something.
/// </summary>
public sealed class MediaHub : IAsyncDisposable
{
    private readonly List<IMediaProvider> _providers;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AppSettings _settings;
    private IMediaProvider? _active;

    public MediaHub(IEnumerable<IMediaProvider> providers, AppSettings settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    /// <summary>Every source id any provider can currently see (blocked or not).</summary>
    public IReadOnlyList<string> AvailableSources =>
        _providers.SelectMany(p => p.AvailableSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A provider's state, or null if it is blocked (safety net for providers that don't filter).</summary>
    private MediaState? Visible(IMediaProvider p) =>
        p.Current is { } s && _settings.IsSourceAllowed(s.SourceApp) ? s : null;

    public IReadOnlyList<IMediaProvider> Providers => _providers;

    public MediaState? Current { get; private set; }

    /// <summary>Raised on arbitrary threads.</summary>
    public event EventHandler<MediaState?>? Changed;

    public async Task StartAsync()
    {
        foreach (var p in _providers)
        {
            p.StateChanged += OnProviderChanged;
            try { await p.StartAsync(_cts.Token); }
            catch (Exception ex) { Debug.WriteLine($"Provider {p.Id} failed to start: {ex}"); }
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        foreach (var p in _providers) p.ApplySettings(settings);
        OnProviderChanged(this, null);
    }

    private void OnProviderChanged(object? sender, MediaState? _)
    {
        MediaState? next;
        lock (_lock)
        {
            IMediaProvider? pick = null;
            if (_active != null && Visible(_active) is { IsPlaying: true }) pick = _active;
            pick ??= _providers.FirstOrDefault(p => Visible(p) is { IsPlaying: true });
            if (pick == null && _active != null && Visible(_active) != null) pick = _active;
            pick ??= _providers.FirstOrDefault(p => Visible(p) != null);

            _active = pick;
            next = pick == null ? null : Visible(pick);
            if (Equals(next, Current)) return;
            Current = next;
        }
        Changed?.Invoke(this, next);
    }

    public Task TogglePlayPauseAsync() => _active?.TogglePlayPauseAsync() ?? Task.CompletedTask;
    public Task NextAsync() => _active?.NextAsync() ?? Task.CompletedTask;
    public Task PreviousAsync() => _active?.PreviousAsync() ?? Task.CompletedTask;
    public Task SeekAsync(TimeSpan position) => _active?.SeekAsync(position) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var p in _providers)
        {
            p.StateChanged -= OnProviderChanged;
            try { await p.DisposeAsync(); } catch { /* shutting down */ }
        }
        _cts.Dispose();
    }
}
