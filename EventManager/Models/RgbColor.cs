using System;
using System.Globalization;

namespace EventManager.Models;

/// <summary>
/// Color in the sRGB space.
/// </summary>
public sealed record RgbColor(byte Red, byte Green, byte Blue)
{
    /// <summary>
    /// Pure black.
    /// </summary>
    public static readonly RgbColor Black = new(0, 0, 0);

    /// <summary>
    /// Pure white.
    /// </summary>
    public static readonly RgbColor White = new(255, 255, 255);

    /// <summary>
    /// Parses a color of the hexadecimal format #RGB or #RRGGBB.
    /// </summary>
    public static RgbColor Parse(string cssColor)
    {
        byte ParseHex(char c)
            => byte.Parse(c.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        // "2" == 0x22, etc.
        byte ParseSingleHex(char c)
            => (byte)(ParseHex(c) * 17);

        byte ParseDoubleHex(char high, char low)
            => (byte)((ParseHex(high) << 4) + ParseHex(low));

        if (cssColor is ['#', var singleR, var singleG, var singleB])
        {
            byte r = ParseSingleHex(singleR);
            byte g = ParseSingleHex(singleG);
            byte b = ParseSingleHex(singleB);
            return new(r, g, b);
        }
        if (cssColor is ['#', var highR, var lowR, var highG, var lowG, var highB, var lowB])
        {
            byte r = ParseDoubleHex(highR, lowR);
            byte g = ParseDoubleHex(highG, lowG);
            byte b = ParseDoubleHex(highB, lowB);
            return new(r, g, b);
        }
        throw new ArgumentException("Invalid color code", nameof(cssColor));
    }

    /// <summary>
    /// Decides on black or white for a foreground color using this color as background, based on contrast.
    /// </summary>
    public RgbColor PickForegroundColor()
    {
        // https://www.w3.org/TR/WCAG21/#dfn-relative-luminance
        static double Convert(byte value)
        {
            double normalized = value / 255.0;
            if (normalized <= 0.04045)
            {
                return normalized / 12.92;
            }
            return Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Convert(Red) + 0.7152 * Convert(Green) + 0.0722 * Convert(Blue);

        // https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio
        double whiteRatio = (1 + 0.05) / (luminance + 0.05);
        double blackRatio = (luminance + 0.05) / (0 + 0.05);

        return whiteRatio > blackRatio ? White : Black;
    }

    /// <summary>
    /// Converts this color to the #RRGGBB hexadecimal format.
    /// </summary>
    public override string ToString()
        => $"#{Red:X2}{Green:X2}{Blue:X2}";
}