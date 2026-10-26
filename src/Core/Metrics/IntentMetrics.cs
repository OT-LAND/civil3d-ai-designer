using System;
using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Geometry;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Metrics
{
    // Metrics on a Design Intent (roadmap B.2, CONCEPT.md §3 "Metrics + rule report").
    //
    // Numbers measured on a Design Intent so the slider targets of a Design Brief can be
    // checked ("hard/soft 40/60 target → 43/57 achieved"). Pure Core, deterministic.
    //
    // Measured on Design Intent v0: hard/soft share, curved/straight share, accessible
    // share of path length, steepest slopes per kind of surface. Canopy cover, cut/fill and
    // trees vs elements need trees, site elements and the grading solver (B.3, B.6) and are
    // reported as not measured until then.
    //
    // Simplifications (v0): overlapping spaces are not subtracted; a path's area is its
    // centerline length × width; a space belongs to a zone when its centre point (outline
    // vertex average, or centerline midpoint) lies inside the zone outline.

    /// <summary>The steepest slope found on one kind of surface, and where.</summary>
    public readonly record struct SteepestSlope(Slope Slope, string SpaceId);

    /// <summary>Measured values for one Design Intent (whole site or one zone).</summary>
    public sealed class IntentMetrics
    {
        /// <summary>Paved area (plazas, paths, and spaces with a paving/gravel material), m².</summary>
        public double HardArea { get; init; }

        /// <summary>Planted area (lawns, beds, and spaces with a lawn/planting/mulch material), m².</summary>
        public double SoftArea { get; init; }

        /// <summary>Soft share of hard + soft area, 0–100 (same direction as the hard↔soft slider). Null when no area.</summary>
        public double? SoftSharePercent { get; init; }

        /// <summary>Length of space outlines and path centerlines that runs in smooth bends, m.</summary>
        public double CurvedLength { get; init; }

        /// <summary>Length of space outlines and path centerlines that runs straight, m.</summary>
        public double StraightLength { get; init; }

        /// <summary>Straight share of outline + centerline length, 0–100 (same direction as the curved↔straight slider).</summary>
        public double? StraightSharePercent { get; init; }

        /// <summary>Total path centerline length, m.</summary>
        public double PathLength { get; init; }

        /// <summary>Centerline length of paths that are accessible routes (D11: unless marked false), m.</summary>
        public double AccessiblePathLength { get; init; }

        /// <summary>Accessible share of path length, 0–100. Null when there are no paths.</summary>
        public double? AccessibleSharePercent { get; init; }

        /// <summary>Steepest walking slope on accessible paths.</summary>
        public SteepestSlope? SteepestAccessiblePath { get; init; }

        /// <summary>Steepest walking slope on paths marked not accessible (the max slope slider, D19).</summary>
        public SteepestSlope? SteepestOtherPath { get; init; }

        /// <summary>Steepest fall on plazas.</summary>
        public SteepestSlope? SteepestPlaza { get; init; }

        /// <summary>Steepest graded ground on lawns and beds (the max grade slider, D16).</summary>
        public SteepestSlope? SteepestGround { get; init; }

        /// <summary>Canopy cover at maturity, 0–100. Not measured on Design Intent v0 (no trees yet, B.3).</summary>
        public double? CanopyCoverPercent { get; init; }

        /// <summary>Ids of the spaces that were measured.</summary>
        public IReadOnlyList<string> SpaceIds { get; init; } = Array.Empty<string>();
    }

    public static class IntentMetricsCalculator
    {
        /// <summary>
        /// A vertex whose turn is above <see cref="CollinearDegrees"/> and at most this many
        /// degrees counts as a smooth bend (a tessellated arc or spline). Sharper turns are corners.
        /// </summary>
        public const double SmoothBendMaxDegrees = 30.0;

        /// <summary>Turns at or below this are treated as straight (collinear vertices).</summary>
        public const double CollinearDegrees = 0.5;

        private const double MinSpotDistance = 0.01;

        /// <summary>
        /// Measures <paramref name="intent"/>. With <paramref name="zoneOutline"/>, only the spaces
        /// whose centre point lies inside that closed polygon are measured.
        /// </summary>
        public static IntentMetrics Measure(DesignIntent intent, IReadOnlyList<Point2>? zoneOutline = null)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));

            var materials = (intent.Materials ?? new List<Material>())
                .Where(m => m != null && !string.IsNullOrEmpty(m.Id))
                .GroupBy(m => m.Id)
                .ToDictionary(g => g.Key, g => g.First().Category);

            var spaces = (intent.Spaces ?? new List<Space>())
                .Where(s => s != null)
                .Where(s => zoneOutline == null || zoneOutline.Count < 3
                            || (CentrePoint(s) is Point2 c && PolygonGeometry.Contains(zoneOutline, c)))
                .ToList();

            double hard = 0, soft = 0, curved = 0, straight = 0, pathLen = 0, accessibleLen = 0;
            SteepestSlope? accPath = null, otherPath = null, plaza = null, ground = null;

            foreach (var s in spaces)
            {
                double area;
                IReadOnlyList<Point2>? line;
                bool closed;
                if (s.Kind == SpaceKind.Path)
                {
                    line = s.Centerline;
                    closed = false;
                    var len = line == null ? 0 : PolygonGeometry.Length(line, closed: false);
                    area = len * Math.Max(0, s.Width ?? 0);
                    pathLen += len;
                    if (s.Accessible != false) accessibleLen += len;
                }
                else
                {
                    line = s.Outline;
                    closed = true;
                    area = line == null ? 0 : Math.Abs(PolygonGeometry.SignedArea(line));
                }

                if (IsSoft(s, materials)) soft += area; else hard += area;

                if (line != null)
                {
                    var (c, st) = PolygonGeometry.CurvedStraightLength(line, closed, CollinearDegrees, SmoothBendMaxDegrees);
                    curved += c;
                    straight += st;
                }

                var steepest = SteepestOn(s);
                if (steepest is SteepestSlope found)
                {
                    switch (s.Kind)
                    {
                        case SpaceKind.Path when s.Accessible != false: accPath = Steeper(accPath, found); break;
                        case SpaceKind.Path: otherPath = Steeper(otherPath, found); break;
                        case SpaceKind.Plaza: plaza = Steeper(plaza, found); break;
                        default: ground = Steeper(ground, found); break;
                    }
                }
            }

            return new IntentMetrics
            {
                HardArea = hard,
                SoftArea = soft,
                SoftSharePercent = Share(soft, hard + soft),
                CurvedLength = curved,
                StraightLength = straight,
                StraightSharePercent = Share(straight, curved + straight),
                PathLength = pathLen,
                AccessiblePathLength = accessibleLen,
                AccessibleSharePercent = Share(accessibleLen, pathLen),
                SteepestAccessiblePath = accPath,
                SteepestOtherPath = otherPath,
                SteepestPlaza = plaza,
                SteepestGround = ground,
                CanopyCoverPercent = null,
                SpaceIds = spaces.Select(s => s.Id).ToList(),
            };
        }

        private static double? Share(double part, double total) => total > 1e-9 ? 100.0 * part / total : (double?)null;

        /// <summary>Soft = lawn/bed, unless the material says otherwise (a gravel "bed" is hard, a lawn-material plaza soft).</summary>
        private static bool IsSoft(Space s, IReadOnlyDictionary<string, MaterialCategory> materials)
        {
            if (s.MaterialId != null && materials.TryGetValue(s.MaterialId, out var cat))
            {
                switch (cat)
                {
                    case MaterialCategory.Lawn:
                    case MaterialCategory.Planting:
                    case MaterialCategory.Mulch:
                        return true;
                    case MaterialCategory.Paving:
                    case MaterialCategory.Gravel:
                        return false;
                }
            }
            return s.Kind == SpaceKind.Lawn || s.Kind == SpaceKind.Bed;
        }

        private static Point2? CentrePoint(Space s)
        {
            if (s.Kind == SpaceKind.Path)
                return s.Centerline != null && s.Centerline.Count > 0 ? PolygonGeometry.PointAtHalfLength(s.Centerline) : (Point2?)null;
            if (s.Outline == null || s.Outline.Count == 0) return null;
            return new Point2(s.Outline.Average(p => p.X), s.Outline.Average(p => p.Y));
        }

        /// <summary>
        /// Steepest slope stated on a space: its fall, and the slope between every pair of spot
        /// levels (a lower bound of the true steepest slope between them).
        /// </summary>
        private static SteepestSlope? SteepestOn(Space s)
        {
            var level = s.Level;
            if (level == null) return null;
            double best = -1;
            if (level.Fall != null && !double.IsNaN(level.Fall.Percent)) best = Math.Abs(level.Fall.Percent) / 100.0;
            var spots = level.Spots ?? new List<SpotLevel>();
            for (int i = 0; i < spots.Count; i++)
            {
                for (int j = i + 1; j < spots.Count; j++)
                {
                    double d = Math.Sqrt(Sq(spots[j].X - spots[i].X) + Sq(spots[j].Y - spots[i].Y));
                    if (d < MinSpotDistance) continue;
                    best = Math.Max(best, Math.Abs(spots[j].Z - spots[i].Z) / d);
                }
            }
            return best < 0 ? (SteepestSlope?)null : new SteepestSlope(Slope.FromRiseOverRun(best), s.Id);
        }

        private static SteepestSlope Steeper(SteepestSlope? current, SteepestSlope candidate) =>
            current is SteepestSlope c && Math.Abs(c.Slope.RiseOverRun) >= Math.Abs(candidate.Slope.RiseOverRun) ? c : candidate;

        private static double Sq(double v) => v * v;
    }
}
