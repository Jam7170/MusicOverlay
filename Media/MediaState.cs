using System;

namespace MusicOverlay.Media;

/// <summary>
/// Immutable snapshot of what a provider is playing. Providers should reuse the same
/// <see cref="Artwork"/> array instance while the artwork is unchanged so the UI can skip re-decoding.
/// </summary>
public sealed record MediaState(
    string ProviderId,
    string SourceApp,
    string Title,
    string Artist,
    string? Album,
    byte[]? Artwork,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionTimestamp,
    double PlaybackRate = 1.0)
{
    /// <summary>Position extrapolated from the last reported timestamp (providers only report sporadically).</summary>
    public TimeSpan EstimatedPosition(DateTimeOffset now)
    {
        var p = Position;
        if (IsPlaying)
        {
            var elapsed = now - PositionTimestamp;
            if (elapsed > TimeSpan.Zero)
                p += TimeSpan.FromTicks((long)(elapsed.Ticks * (PlaybackRate <= 0 ? 1.0 : PlaybackRate)));
        }
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
        return p;
    }
}
