using System;
using System.Globalization;

namespace CivilAI.Core.Ui
{
    /// <summary>Colours for the designer palette, as "#RRGGBB".</summary>
    /// <param name="IsDark">True for AutoCAD's dark theme.</param>
    /// <param name="Background">Palette background.</param>
    /// <param name="Foreground">Main text.</param>
    /// <param name="Muted">Secondary text (hints).</param>
    public sealed record PaletteColors(bool IsDark, string Background, string Foreground, string Muted);

    /// <summary>
    /// Picks palette colours from AutoCAD's COLORTHEME system variable (0 = dark, 1 = light), so the
    /// palette never relies on WPF defaults (black text), which were unreadable on the dark palette
    /// background (bug 0.3a). Kept in Core so the choice and its contrast are unit-tested.
    /// </summary>
    public static class PaletteTheme
    {
        /// <summary>Dark theme: close to AutoCAD's dark palette grey, light text.</summary>
        public static readonly PaletteColors Dark = new(true, "#3B4453", "#F0F0F0", "#C8CDD5");

        /// <summary>Light theme: near-white background, dark text.</summary>
        public static readonly PaletteColors Light = new(false, "#F5F5F5", "#1E1E1E", "#555555");

        /// <summary>
        /// Colours for a COLORTHEME value as returned by GetSystemVariable (boxed short/int, or text).
        /// 1 → light; 0, missing or unreadable → dark (AutoCAD's default theme).
        /// </summary>
        public static PaletteColors ForColorTheme(object? colorTheme)
        {
            int? value = colorTheme switch
            {
                short s => s,
                int i => i,
                long l => (int)l,
                string t when int.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
                _ => null,
            };
            return value == 1 ? Light : Dark;
        }

        /// <summary>WCAG 2 contrast ratio between two "#RRGGBB" colours (1 to 21).</summary>
        public static double ContrastRatio(string a, string b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(string hex)
        {
            if (hex is null || hex.Length != 7 || hex[0] != '#')
                throw new FormatException($"Expected #RRGGBB, got '{hex}'.");
            double Channel(int i)
            {
                var c = int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }
    }
}
