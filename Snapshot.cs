using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicOverlay.Media;
using MusicOverlay.Settings;

namespace MusicOverlay;

/// <summary>
/// <c>MusicOverlay.exe --snapshot &lt;dir&gt;</c> renders README screenshots offscreen: transparent PNGs of the card
/// in each theme, using generated artwork and made-up tracks (no real album art, no desktop capture).
/// Never touches the user's settings and doesn't conflict with a running instance.
/// </summary>
public static class Snapshot
{
    private const double Scale = 2.0; // render at 2x for crisp README images

    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var settings = new AppSettings { ReadOnly = true };
        MainWindow.SnapshotMode = true;
        var window = new MainWindow(new MediaHub([], settings), settings);

        var sunset = Artwork(Color.FromRgb(0x2B, 0x10, 0x3F), Color.FromRgb(0xF0, 0x5A, 0x6E), Color.FromRgb(0xFF, 0xB8, 0x6B), seed: 3);
        var harbour = Artwork(Color.FromRgb(0x06, 0x1B, 0x33), Color.FromRgb(0x1E, 0x9E, 0xB0), Color.FromRgb(0x9B, 0x8C, 0xF2), seed: 11);

        var scenes = new List<(string File, MediaState State, OverlayTheme Theme, TextCase Case)>
        {
            ("vivid", Track("Golden Hour Static", "Paper Satellites", sunset, 116, 241, playing: true), OverlayTheme.Vivid, TextCase.AsIs),
            ("vivid-alt", Track("Low Tide Signals", "Glass Harbour", harbour, 74, 198, playing: true), OverlayTheme.Vivid, TextCase.AsIs),
            ("classic", Track("Midnight Arcade", "Neon Postcards", sunset, 137, 241, playing: false), OverlayTheme.Classic, TextCase.Lowercase),
            ("dusk", Track("Midnight Arcade", "Neon Postcards", sunset, 137, 241, playing: true), OverlayTheme.Dusk, TextCase.Lowercase),
        };

        var renders = new List<BitmapSource>();
        foreach (var (file, state, theme, textCase) in scenes)
        {
            var bmp = window.RenderSnapshot(state, theme, textCase, Scale);
            Save(bmp, Path.Combine(outDir, file + ".png"));
            renders.Add(bmp);
        }

        Save(Grid(renders, columns: 2), Path.Combine(outDir, "themes.png"));
        window.Close();
    }

    private static MediaState Track(string title, string artist, byte[] art, int posSec, int durSec, bool playing) =>
        new("demo", "Demo", title, artist, null, art, playing,
            TimeSpan.FromSeconds(posSec), TimeSpan.FromSeconds(durSec), DateTimeOffset.Now);

    /// <summary>Abstract "album art": a gradient sky with soft glowing orbs and a horizon.</summary>
    private static byte[] Artwork(Color deep, Color mid, Color glow, int seed)
    {
        const int size = 480;
        var rng = new Random(seed);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(deep, mid, new Point(0, 0), new Point(0.3, 1)), null, new Rect(0, 0, size, size));

            for (int i = 0; i < 9; i++)
            {
                double r = 40 + rng.NextDouble() * 140;
                var c = i % 3 == 0 ? glow : i % 3 == 1 ? mid : Colors.White;
                var brush = new RadialGradientBrush(Color.FromArgb((byte)(90 + rng.Next(90)), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                dc.DrawEllipse(brush, null, new Point(rng.NextDouble() * size, rng.NextDouble() * size * 0.75), r, r);
            }

            var sun = new RadialGradientBrush(glow, Color.FromArgb(0, glow.R, glow.G, glow.B)) { GradientOrigin = new Point(0.5, 0.5) };
            dc.DrawEllipse(sun, null, new Point(size * 0.62, size * 0.58), 120, 120);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, deep.R, deep.G, deep.B), deep, 90), null,
                new Rect(0, size * 0.62, size, size * 0.38));

            var line = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 2);
            for (int i = 0; i < 6; i++)
            {
                double y = size * 0.66 + i * i * 6;
                dc.DrawLine(line, new Point(0, y), new Point(size, y));
            }
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource Grid(IReadOnlyList<BitmapSource> images, int columns)
    {
        int w = images[0].PixelWidth, h = images[0].PixelHeight;
        int rows = (images.Count + columns - 1) / columns;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int i = 0; i < images.Count; i++)
                dc.DrawImage(images[i], new Rect(i % columns * w / Scale, i / columns * h / Scale, w / Scale, h / Scale));
        }
        var rtb = new RenderTargetBitmap(w * columns, h * rows, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
