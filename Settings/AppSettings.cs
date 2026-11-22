using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicOverlay.Settings;

public enum OverlayTheme
{
    /// <summary>Full-colour artwork, accents pulled from the album art.</summary>
    Vivid,
    /// <summary>Fixed colours from the original reference design: peach title, red bar, slate panel.</summary>
    Classic,
    /// <summary>Desaturated artwork with a dusky purple tint.</summary>
    Dusk,
    /// <summary>Vivid while playing, Dusk while paused.</summary>
    Auto,
}

public enum TextCase
{
    AsIs,
    Lowercase,
    Uppercase,
}

public enum IdleBehavior
{
    /// <summary>Keep showing the card normally.</summary>
    Show,
    /// <summary>Fade the card to <see cref="AppSettings.IdleOpacity"/>; hovering brings it back.</summary>
    Translucent,
    /// <summary>Fade out and hide the card.</summary>
    Hide,
}

public sealed class AppSettings
{
    /// <summary>
    /// Window position in physical screen pixels: the window corner chosen by AnchorRight/AnchorBottom
    /// (e.g. both true = X/Y is the bottom-right corner), so resizing keeps that corner in place.
    /// </summary>
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool AnchorRight { get; set; }
    public bool AnchorBottom { get; set; }

    /// <summary>Legacy DIP position, migrated to X/Y on first run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Left { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Top { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<OverlayTheme>))]
    public OverlayTheme Theme { get; set; } = OverlayTheme.Vivid;

    [JsonConverter(typeof(JsonStringEnumConverter<TextCase>))]
    public TextCase TextCase { get; set; } = TextCase.AsIs;

    public double Scale { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool Locked { get; set; }
    /// <summary>What to do a few seconds after playback stops/pauses (or only blocked sources are playing).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<IdleBehavior>))]
    public IdleBehavior IdleBehavior { get; set; } = IdleBehavior.Show;

    /// <summary>Card opacity for <see cref="IdleBehavior.Translucent"/>.</summary>
    public double IdleOpacity { get; set; } = 0.35;

    /// <summary>Card opacity while something is playing (1 = solid). Hovering always shows it at full opacity.</summary>
    public double PlayingOpacity { get; set; } = 1.0;

    /// <summary>Legacy, migrated to <see cref="IdleBehavior"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideWhenIdle { get; set; }

    /// <summary>
    /// Substring of the Windows media app id to prefer (e.g. "Spotify", "chrome", "msedge").
    /// Empty = follow whatever Windows considers the current session.
    /// </summary>
    public string PreferredSource { get; set; } = "Spotify";

    /// <summary>Only ever show <see cref="PreferredSource"/>; everything else is ignored.</summary>
    public bool PreferredSourceOnly { get; set; }

    /// <summary>
    /// Media app ids to ignore entirely (case-insensitive substring match), e.g. "Chrome", "MSEdge".
    /// Replace the list instead of mutating it (it's read from background threads).
    /// </summary>
    public List<string> BlockedSources { get; set; } = new();

    public bool IsSourceAllowed(string sourceApp)
    {
        if (string.IsNullOrEmpty(sourceApp)) return true;
        foreach (var blocked in BlockedSources)
            if (!string.IsNullOrWhiteSpace(blocked) && sourceApp.Contains(blocked, StringComparison.OrdinalIgnoreCase))
                return false;
        if (PreferredSourceOnly && !string.IsNullOrWhiteSpace(PreferredSource))
            return sourceApp.Contains(PreferredSource, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>WPF font fallback list. Drop Quicksand .ttf files into a "Fonts" folder for the exact look.</summary>
    public string FontFamily { get; set; } = "Quicksand, Nunito, Segoe UI Variable Display, Segoe UI";

    // ---- persistence ----

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicOverlay");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
                if (settings.HideWhenIdle && settings.IdleBehavior == IdleBehavior.Show)
                {
                    settings.IdleBehavior = IdleBehavior.Hide;
                    settings.HideWhenIdle = false;
                }
                return settings;
            }
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* non-fatal */ }
    }
}
