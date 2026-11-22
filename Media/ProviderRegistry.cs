using System.Collections.Generic;
using MusicOverlay.Media.Providers;
using MusicOverlay.Settings;

namespace MusicOverlay.Media;

/// <summary>
/// The one place that decides which integrations run. To add an integration:
///   1. Create a class in Media/Providers implementing <see cref="IMediaProvider"/>.
///   2. Yield it from <see cref="Create"/> below (optionally gated by a setting).
/// The <see cref="MediaHub"/> automatically picks whichever provider is actively playing.
/// </summary>
public static class ProviderRegistry
{
    public static IEnumerable<IMediaProvider> Create(AppSettings settings)
    {
        // Spotify desktop, browsers, Apple Music, Tidal, VLC... anything that reports to Windows media controls.
        yield return new WindowsMediaProvider(settings);

        // yield return new MyNewProvider(settings);
    }
}
