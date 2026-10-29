using System;
using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Grading
{
    /// <summary>A surveyed point with an absolute level, metres.</summary>
    public readonly record struct Point3(double X, double Y, double Z);

    /// <summary>
    /// Existing ground the grading solver ties into where the designer fixed no level.
    /// The plugin will supply one read from the Civil 3D existing surface (2.1b); Core has
    /// <see cref="SpotGround"/> for survey points.
    /// </summary>
    public interface IGroundModel
    {
        /// <summary>Existing ground level at a plan point, or null where the model has no data.</summary>
        double? LevelAt(Point2 at);
    }

    /// <summary>
    /// Existing ground from survey spot levels by inverse-distance weighting of the nearest
    /// points (power 2). A query on a survey point returns its level. Good enough for v0
    /// tie-ins; the Civil 3D surface (TIN) replaces it in the plugin.
    /// </summary>
    public sealed class SpotGround : IGroundModel
    {
        private readonly Point3[] _points;
        private readonly int _nearest;

        public SpotGround(IEnumerable<Point3> points, int nearest = 6)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (nearest < 1) throw new ArgumentOutOfRangeException(nameof(nearest), "Use at least one point.");
            _points = points.ToArray();
            _nearest = nearest;
        }

        public int Count => _points.Length;

        public double? LevelAt(Point2 at)
        {
            if (_points.Length == 0) return null;
            var near = _points
                .Select(p => (P: p, D2: (p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y)))
                .OrderBy(t => t.D2)
                .Take(_nearest)
                .ToList();
            if (near[0].D2 < 1e-12) return near[0].P.Z;
            double sw = 0, sz = 0;
            foreach (var (p, d2) in near)
            {
                var w = 1.0 / d2;
                sw += w;
                sz += w * p.Z;
            }
            return sz / sw;
        }
    }
}
