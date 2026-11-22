using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicOverlay.Theming;

public sealed record OverlayPalette(
    Color PanelTop,
    Color PanelBottom,
    Color Title,
    Color Artist,
    Color Time,
    Color ProgressFill,
    Color ProgressTrack,
    Color ButtonFill,
    Color ButtonBorder,
    Color ButtonGlyph,
    Color CardBorder,
    Color ArtTint,
    double ArtTintOpacity,
    bool DesaturateArt);

/// <summary>Colours sampled from the album art.</summary>
public sealed record ArtAnalysis(Color Accent, Color Secondary, Color Average)
{
    public static readonly ArtAnalysis Default = new(
        Color.FromRgb(0xE0, 0x4E, 0x5C), Color.FromRgb(0xE8, 0xA2, 0x7A), Color.FromRgb(0x4A, 0x45, 0x55));
}

public static class Palettes
{
    public static readonly OverlayPalette Dusk = new(
        PanelTop: Rgb(0x21, 0x1D, 0x45),
        PanelBottom: Rgb(0x19, 0x16, 0x37),
        Title: Rgb(0x6F, 0x69, 0xA8),
        Artist: Rgb(0x60, 0x5A, 0x98),
        Time: Rgb(0x6F, 0x69, 0xA8),
        ProgressFill: Rgb(0x7C, 0x76, 0xB6),
        ProgressTrack: Rgb(0x2C, 0x27, 0x57),
        ButtonFill: Color.FromArgb(0xE6, 0x1D, 0x19, 0x3F),
        ButtonBorder: Rgb(0x8E, 0x88, 0xCC),
        ButtonGlyph: Rgb(0xCC, 0xC8, 0xF2),
        CardBorder: Color.FromArgb(0x70, 0xC8, 0xC4, 0xE0),
        ArtTint: Rgb(0x3A, 0x31, 0x86),
        ArtTintOpacity: 0.62,
        DesaturateArt: true);

    /// <summary>The original reference look: full-colour art, peach title, lavender artist, red-pink bar.</summary>
    public static readonly OverlayPalette Classic = new(
        PanelTop: Rgb(0x36, 0x30, 0x43),
        PanelBottom: Rgb(0x38, 0x33, 0x4A),
        Title: Rgb(0xDE, 0xAA, 0x8E),
        Artist: Rgb(0xA3, 0x9F, 0xC0),
        Time: Rgb(0xCB, 0xBC, 0xBA),
        ProgressFill: Rgb(0xDF, 0x57, 0x6C),
        ProgressTrack: Rgb(0x2E, 0x2C, 0x3A),
        ButtonFill: Color.FromArgb(0xB4, 0x3B, 0x33, 0x45),
        ButtonBorder: Rgb(0xBC, 0xBD, 0xCF),
        ButtonGlyph: Rgb(0xC0, 0xC4, 0xD8),
        CardBorder: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
        ArtTint: Colors.Black,
        ArtTintOpacity: 0.0,
        DesaturateArt: false);

    public static OverlayPalette Vivid(ArtAnalysis a)
    {
        var (ah, asat, _) = ToHsl(a.Accent);
        var (sh, ssat, _) = ToHsl(a.Secondary);
        var (bh, bsat, _) = ToHsl(a.Average);
        double panelSat = Math.Min(bsat, 0.14);

        var panelTop = FromHsl(bh, panelSat, 0.30);
        return new OverlayPalette(
            PanelTop: panelTop,
            PanelBottom: FromHsl(bh, panelSat, 0.23),
            Title: FromHsl(sh, Math.Clamp(ssat, 0.45, 0.80), 0.72),
            Artist: FromHsl(bh, 0.28, 0.74),
            Time: Rgb(0xD8, 0xD6, 0xE2),
            ProgressFill: FromHsl(ah, Math.Clamp(asat, 0.55, 0.85), 0.58),
            ProgressTrack: FromHsl(bh, Math.Min(bsat, 0.16), 0.16),
            ButtonFill: Color.FromArgb(0xC0, panelTop.R, panelTop.G, panelTop.B),
            ButtonBorder: Color.FromArgb(0xC8, 0xD6, 0xD0, 0xDE),
            ButtonGlyph: Rgb(0xDA, 0xD4, 0xE4),
            CardBorder: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            ArtTint: Colors.Black,
            ArtTintOpacity: 0.0,
            DesaturateArt: false);
    }

    /// <summary>Finds the dominant vivid hue, a contrasting secondary hue, and the overall average.</summary>
    public static ArtAnalysis Analyze(BitmapSource src)
    {
        try
        {
            const int size = 32;
            var small = new TransformedBitmap(src, new ScaleTransform((double)size / src.PixelWidth, (double)size / src.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);

            const int buckets = 12;
            var weight = new double[buckets];
            var sumR = new double[buckets];
            var sumG = new double[buckets];
            var sumB = new double[buckets];
            double avgR = 0, avgG = 0, avgB = 0;
            int n = w * h;

            for (int i = 0; i < px.Length; i += 4)
            {
                byte b = px[i], g = px[i + 1], r = px[i + 2];
                avgR += r; avgG += g; avgB += b;

                var (hue, s, v) = ToHsv(r, g, b);
                if (v < 0.18 || s < 0.22) continue;
                int k = (int)(hue / 360.0 * buckets) % buckets;
                double wgt = s * s * v;
                weight[k] += wgt; sumR[k] += r * wgt; sumG[k] += g * wgt; sumB[k] += b * wgt;
            }

            var average = Rgb((byte)(avgR / n), (byte)(avgG / n), (byte)(avgB / n));

            int top = -1;
            for (int k = 0; k < buckets; k++) if (weight[k] > 0 && (top < 0 || weight[k] > weight[top])) top = k;
            if (top < 0) return ArtAnalysis.Default with { Average = average };

            Color BucketColor(int k) => Rgb((byte)(sumR[k] / weight[k]), (byte)(sumG[k] / weight[k]), (byte)(sumB[k] / weight[k]));
            var accent = BucketColor(top);

            int second = -1;
            for (int k = 0; k < buckets; k++)
            {
                int dist = Math.Min(Math.Abs(k - top), buckets - Math.Abs(k - top));
                if (dist < 1 || weight[k] < weight[top] * 0.12) continue;
                if (second < 0 || weight[k] > weight[second]) second = k;
            }

            Color secondary;
            if (second >= 0) secondary = BucketColor(second);
            else
            {
                var (h0, s0, l0) = ToHsl(accent);
                secondary = FromHsl(h0 + 25, s0, l0);
            }
            return new ArtAnalysis(accent, secondary, average);
        }
        catch
        {
            return ArtAnalysis.Default;
        }
    }

    // ---- colour maths ----

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static (double h, double s, double v) ToHsv(byte rb, byte gb, byte bb)
    {
        double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static (double h, double s, double l) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double l = (max + min) / 2, h = 0, s = 0;
        if (d > 0)
        {
            s = d / (1 - Math.Abs(2 * l - 1));
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
