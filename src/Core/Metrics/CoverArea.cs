using System;
using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Geometry;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Metrics
{
    /// <summary>
    /// Share of an open-ground area covered by tree canopies and shade footprints (roadmap B.3a).
    ///
    /// Measured on a regular grid of sample points: each cell centre inside the reference area and
    /// outside every building counts toward the ground; it counts as covered when it is inside any
    /// canopy circle or shade footprint. Overlaps are therefore counted once and canopy that hangs
    /// over the reference border is clipped. Deterministic; accuracy is about one cell along every
    /// edge (cell size is chosen so the reference area has roughly <see cref="TargetCells"/> cells).
    /// </summary>
    public static class CoverArea
    {
        /// <summary>Roughly how many grid cells cover the reference bounding box.</summary>
        public const int TargetCells = 250_000;

        /// <summary>Smallest grid cell, metres.</summary>
        public const double MinCellSize = 0.05;

        /// <summary>A circular canopy.</summary>
        public readonly record struct Circle(Point2 Centre, double Radius);

        /// <summary>
        /// Covered share of <paramref name="reference"/> (minus <paramref name="holes"/>), 0–100.
        /// Null when the reference has no open area.
        /// </summary>
        public static double? CoveredPercent(
            IReadOnlyList<Point2> reference,
            IEnumerable<IReadOnlyList<Point2>>? holes,
            IEnumerable<Circle> circles,
            IEnumerable<IReadOnlyList<Point2>> footprints)
        {
            if (reference == null || reference.Count < 3) return null;
            var holeList = (holes ?? Enumerable.Empty<IReadOnlyList<Point2>>()).Where(h => h != null && h.Count >= 3).ToList();
            var circleList = (circles ?? Enumerable.Empty<Circle>()).Where(c => c.Radius > 0).ToList();
            var footprintList = (footprints ?? Enumerable.Empty<IReadOnlyList<Point2>>()).Where(f => f != null && f.Count >= 3).ToList();

            double minX = reference.Min(p => p.X), maxX = reference.Max(p => p.X);
            double minY = reference.Min(p => p.Y), maxY = reference.Max(p => p.Y);
            double w = maxX - minX, h = maxY - minY;
            if (w <= 0 || h <= 0) return null;

            double cell = Math.Max(MinCellSize, Math.Sqrt(w * h / TargetCells));
            int nx = Math.Max(1, (int)Math.Ceiling(w / cell));
            int ny = Math.Max(1, (int)Math.Ceiling(h / cell));

            long ground = 0, covered = 0;
            for (int j = 0; j < ny; j++)
            {
                double y = minY + (j + 0.5) * cell;
                if (y > maxY) break;
                for (int i = 0; i < nx; i++)
                {
                    double x = minX + (i + 0.5) * cell;
                    if (x > maxX) break;
                    var p = new Point2(x, y);
                    if (!PolygonGeometry.Contains(reference, p)) continue;
                    if (holeList.Any(hole => PolygonGeometry.Contains(hole, p))) continue;
                    ground++;
                    if (circleList.Any(c => Sq(x - c.Centre.X) + Sq(y - c.Centre.Y) <= c.Radius * c.Radius)
                        || footprintList.Any(f => PolygonGeometry.Contains(f, p)))
                        covered++;
                }
            }
            return ground == 0 ? (double?)null : 100.0 * covered / ground;
        }

        private static double Sq(double v) => v * v;
    }
}
