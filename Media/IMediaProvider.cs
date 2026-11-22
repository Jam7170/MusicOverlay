using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MusicOverlay.Settings;

namespace MusicOverlay.Media;

/// <summary>
/// A source of "now playing" information. Implement this to add a new integration
/// (e.g. Spotify Web API, Last.fm, foobar2000, MPD...) and register it in <see cref="ProviderRegistry"/>.
///
/// Contract:
///  - Raise <see cref="StateChanged"/> (from any thread) whenever <see cref="Current"/> changes.
///  - <see cref="Current"/> is null when the provider has nothing to show.
///  - Commands should be no-ops if unsupported; never throw for "nothing playing".
/// </summary>
public interface IMediaProvider : IAsyncDisposable
{
    /// <summary>Stable identifier, e.g. "windows-media".</summary>
    string Id { get; }

    string DisplayName { get; }

    MediaState? Current { get; }

    event EventHandler<MediaState?>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task TogglePlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);

    /// <summary>
    /// Raw ids of the apps/sources this provider can currently see, so the user can block them.
    /// Matched against <see cref="MediaState.SourceApp"/>. Optional.
    /// </summary>
    IReadOnlyList<string> AvailableSources => [];

    /// <summary>
    /// Called after the user changes settings. Providers should honour
    /// <see cref="AppSettings.IsSourceAllowed"/> when choosing what to report. Optional.
    /// </summary>
    void ApplySettings(AppSettings settings) { }
}
