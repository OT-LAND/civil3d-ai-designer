using System;
using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Geometry;
using CivilAI.Core.Intent;
using CivilAI.Core.Rules;

namespace CivilAI.Core.Grading
{
    /// <summary>
    /// What the grading solver achieved, for the target-vs-achieved card (B.6c): earthworks and
    /// the steepest walking surface on paths marked not accessible, in the units of their sliders.
    /// </summary>
    public sealed class GradingMetrics
    {
        public double Cut { get; init; }
        public double Fill { get; init; }
        /// <summary>Fill share of moved soil, percent = cut↔fill slider units (0 export … 100 import); null when nothing moves.</summary>
        public double? FillSharePercent { get; init; }

        /// <summary>Steepest stretch on paths marked not accessible, percent; null when there are none with levels.</summary>
        public double? SteepestNotAccessiblePercent { get; init; }
        public string? SteepestNotAccessibleSpaceId { get; init; }

        /// <summary>
        /// <see cref="SteepestNotAccessiblePercent"/> in max slope slider units (D19/D22): 0 = the
        /// accessible limit, 100 = the rule set's not-accessible maximum. Gentler than the accessible
        /// limit reads 0; steeper than the maximum reads above 100. Null when not measured.
        /// </summary>
        public double? MaxSlopeSlider { get; init; }

        /// <summary>
        /// Measures the whole design, or only the spaces inside <paramref name="zoneOutline"/>
        /// (area centre / path halfway point inside it).
        /// </summary>
        public static GradingMetrics Measure(
            DesignIntent intent, EarthworkResult? earthworks, PathLevelResult? paths, RuleSet rules,
            IReadOnlyList<Point2>? zoneOutline = null)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            bool InZone(Point2 p) => zoneOutline == null || PolygonGeometry.Contains(zoneOutline, p);

            var earth = (earthworks?.Spaces ?? new List<SpaceEarthwork>()).Where(s => InZone(s.At)).ToList();
            double cut = earth.Sum(s => s.Cut), fill = earth.Sum(s => s.Fill);

            var spaces = intent.Spaces.ToDictionary(s => s.Id, StringComparer.Ordinal);
            var steep = (paths?.Profiles ?? new List<PathProfile>())
                .Where(p => !p.Accessible && p.SteepestPercent != null)
                .Where(p => spaces.TryGetValue(p.SpaceId, out var s) && s.Centerline != null && s.Centerline.Count >= 2
                            && InZone(PolygonGeometry.PointAtHalfLength(s.Centerline)))
                .OrderByDescending(p => p.SteepestPercent)
                .FirstOrDefault();

            return new GradingMetrics
            {
                Cut = cut, Fill = fill, FillSharePercent = Earthworks.FillShare(cut, fill),
                SteepestNotAccessiblePercent = steep?.SteepestPercent,
                SteepestNotAccessibleSpaceId = steep?.SpaceId,
                MaxSlopeSlider = steep?.SteepestPercent is double g ? SliderForSlope(g, rules) : null,
            };
        }

        /// <summary>Inverse of <see cref="PathLevelSolver.MaxSlopePercent"/>, not capped at 100.</summary>
        public static double? SliderForSlope(double slopePercent, RuleSet rules)
        {
            var at0 = PathLevelSolver.MaxSlopePercent(0, rules);
            var at100 = PathLevelSolver.MaxSlopePercent(100, rules);
            if (at0 == null || at100 == null || at100.Value - at0.Value < 1e-9) return null;
            return Math.Max(0, (slopePercent - at0.Value) / (at100.Value - at0.Value) * 100.0);
        }
    }
}
