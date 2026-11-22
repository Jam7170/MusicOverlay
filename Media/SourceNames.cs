using System;
using System.Collections.Generic;

namespace MusicOverlay.Media;

/// <summary>Turns raw media app ids ("Spotify.exe", "308046B0AF4A39CB", "...!App") into readable names.</summary>
public static class SourceNames
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["308046B0AF4A39CB"] = "Firefox",
        ["6F193CCC56814779"] = "Firefox Nightly",
        ["Chrome"] = "Google Chrome",
        ["MSEdge"] = "Microsoft Edge",
        ["Brave"] = "Brave",
        ["Opera"] = "Opera",
        ["Spotify.exe"] = "Spotify",
    };

    private static Dictionary<string, string>? _startApps;

    public static string Friendly(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "Unknown";
        if (Known.TryGetValue(appId, out var name)) return name;
        if (LookupStartApp(appId) is { } startName) return startName;

        var s = appId;
        int bang = s.LastIndexOf('!');          // packaged apps: "Publisher.App_hash!AppName"
        if (bang >= 0 && bang < s.Length - 1) s = s[(bang + 1)..];
        int slash = s.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) s = s[(slash + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        if (Known.TryGetValue(s, out name)) return name;
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : appId;
    }

    /// <summary>
    /// Browsers like Firefox/LibreWolf/Zen report a hashed id ("83C1C0F3FA8524B1"). The Start menu's app list
    /// (shell:AppsFolder, same data as Get-StartApps) maps those ids to names. Loaded once, on the UI (STA) thread.
    /// </summary>
    private static string? LookupStartApp(string appId)
    {
        _startApps ??= LoadStartApps();
        if (_startApps.TryGetValue(appId, out var exact)) return exact;

        // e.g. only "83C1C0F3FA8524B1;PrivateBrowsingAUMID" -> "LibreWolf Private Browsing" is registered.
        foreach (var (id, name) in _startApps)
            if (id.StartsWith(appId + ";", StringComparison.OrdinalIgnoreCase))
                return name.Replace(" Private Browsing", "", StringComparison.OrdinalIgnoreCase);
        return null;
    }

    private static Dictionary<string, string> LoadStartApps()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return map;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
            foreach (dynamic item in folder.Items())
            {
                string id = item.Path, name = item.Name;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) map.TryAdd(id, name);
            }
        }
        catch
        {
            // Shell unavailable: fall back to heuristics.
        }
        return map;
    }
}
