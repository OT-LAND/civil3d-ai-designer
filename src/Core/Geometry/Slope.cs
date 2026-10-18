using System;
using System.Globalization;

namespace CivilAI.Core.Geometry
{
    /// <summary>
    /// A slope (gradient) as rise over run. Landscape designers state slopes as a percent
    /// ("2%") or as a ratio ("1:15" = 1 m rise per 15 m run); both map to the same value.
    /// Positive = rising in the direction of travel, negative = falling.
    /// </summary>
    public readonly struct Slope : IEquatable<Slope>
    {
        private const double Tolerance = 1e-9;

        private Slope(double riseOverRun) => RiseOverRun = riseOverRun;

        /// <summary>Rise divided by run (0.05 = 5% = 1:20).</summary>
        public double RiseOverRun { get; }

        /// <summary>Slope in percent (5.0 = 5%).</summary>
        public double Percent => RiseOverRun * 100.0;

        /// <summary>The n in "1:n", or +infinity for a flat slope. Always positive.</summary>
        public double RatioRun => Math.Abs(RiseOverRun) < Tolerance ? double.PositiveInfinity : 1.0 / Math.Abs(RiseOverRun);

        public static Slope FromRiseOverRun(double riseOverRun)
        {
            if (double.IsNaN(riseOverRun) || double.IsInfinity(riseOverRun))
                throw new ArgumentOutOfRangeException(nameof(riseOverRun), "Slope must be a finite number.");
            return new Slope(riseOverRun);
        }

        public static Slope FromPercent(double percent) => FromRiseOverRun(percent / 100.0);

        /// <summary>Slope "1:n" (e.g. n = 15 for a 1:15 ramp). n must be positive.</summary>
        public static Slope FromRatio(double run)
        {
            if (!(run > 0) || double.IsInfinity(run))
                throw new ArgumentOutOfRangeException(nameof(run), "Ratio run must be a positive finite number.");
            return new Slope(1.0 / run);
        }

        /// <summary>Slope between two levels over a horizontal distance (metres).</summary>
        public static Slope Between(double startLevel, double endLevel, double horizontalDistance)
        {
            if (!(horizontalDistance > 0))
                throw new ArgumentOutOfRangeException(nameof(horizontalDistance), "Horizontal distance must be positive.");
            return FromRiseOverRun((endLevel - startLevel) / horizontalDistance);
        }

        /// <summary>
        /// Parses "5%", "-2.5 %", "1:15" or "1/15". A ratio is always read as a positive
        /// (unsigned) steepness.
        /// </summary>
        public static bool TryParse(string? text, out Slope slope)
        {
            slope = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text!.Trim();

            if (s.EndsWith("%", StringComparison.Ordinal))
            {
                if (double.TryParse(s.Substring(0, s.Length - 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
                    && !double.IsNaN(pct) && !double.IsInfinity(pct))
                {
                    slope = FromPercent(pct);
                    return true;
                }
                return false;
            }

            var sep = s.IndexOfAny(new[] { ':', '/' });
            if (sep > 0
                && double.TryParse(s.Substring(0, sep).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rise)
                && double.TryParse(s.Substring(sep + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var run)
                && rise > 0 && run > 0 && !double.IsInfinity(rise) && !double.IsInfinity(run))
            {
                slope = FromRiseOverRun(rise / run);
                return true;
            }
            return false;
        }

        /// <summary>True when this slope is no steeper than <paramref name="limit"/> (direction ignored).</summary>
        public bool IsWithin(Slope limit) => Math.Abs(RiseOverRun) <= Math.Abs(limit.RiseOverRun) + Tolerance;

        public bool Equals(Slope other) => Math.Abs(RiseOverRun - other.RiseOverRun) < Tolerance;
        public override bool Equals(object? obj) => obj is Slope other && Equals(other);
        public override int GetHashCode() => Math.Round(RiseOverRun, 9).GetHashCode();
        public static bool operator ==(Slope a, Slope b) => a.Equals(b);
        public static bool operator !=(Slope a, Slope b) => !a.Equals(b);

        public override string ToString() => Percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
}
