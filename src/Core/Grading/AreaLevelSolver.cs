using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Geometry;
using CivilAI.Core.Intent;
using CivilAI.Core.Rules;

namespace CivilAI.Core.Grading
{
    // Grading solver v0, second slice (roadmap B.6b, CONCEPT §3.3 stage 4 "Level").
    //
    // Gives every area space (plaza, lawn, bed) one even sloping surface (a plane): a level and a
    // fall with a direction. The plane passes through the hard levels on the space: its own spot
    // levels, building entrances on its edge (threshold, else FFL), brief entrance and elevation
    // points with a level, and brief contours with a level (hard, D17) where they cross it.
    //
    // Where the hard levels leave the fall free, the space takes its preferred fall:
    //   - the designer's fall from the Design Intent, if given;
    //   - plaza: the middle of the rule set's plaza fall range (1-2 % → 1.5 %);
    //   - lawn: existing ground's slope, kept between the lawn minimum fall and the mowing maximum;
    //   - bed: existing ground's slope, up to the bed maximum.
    // The direction is the designer's, else downhill on existing ground, else away from the
    // nearest building. Where nothing fixes the level, the space sits on its nominal elevation,
    // else on existing ground at its middle.
    //
    // The resulting fall is checked against the rule set (plaza min/max fall, lawn min fall and
    // mowing max, bed max) and comes back as designer-facing reasons for the plan repair loop.
    //
    // Later slices: soft contours, walls/terraces from the max grade slider (D16), cut/fill.

    /// <summary>Where an area's fall came from when the hard levels left it free.</summary>
    public enum FallSource
    {
        /// <summary>The designer's fall in the Design Intent.</summary>
        Designer,
        /// <summary>Follows existing ground (lawn, bed).</summary>
        ExistingGround,
        /// <summary>The rule set's design fall for the kind (plaza).</summary>
        RuleSet,
        /// <summary>Fixed by three or more hard levels.</summary>
        HardLevels,
    }

    /// <summary>The level surface of one area space: an even plane through its centre.</summary>
    public sealed class AreaLevel
    {
        public string SpaceId { get; init; } = "";
        public string Name { get; init; } = "";
        public SpaceKind Kind { get; init; }
        public Point2 Centre { get; init; }
        /// <summary>Level at <see cref="Centre"/>, metres.</summary>
        public double CentreLevel { get; init; }
        /// <summary>Fall, percent (positive).</summary>
        public double FallPercent { get; init; }
        /// <summary>Direction the surface falls towards, degrees counter-clockwise from east.</summary>
        public double FallDirectionDegrees { get; init; }
        public FallSource FallSource { get; init; }
        /// <summary>Least fall the rule set asks for; null when none.</summary>
        public double? MinFallPercent { get; init; }
        /// <summary>Steepest fall the rule set allows; null when none.</summary>
        public double? MaxFallPercent { get; init; }
        /// <summary>Hard levels on the space (station is unused, 0).</summary>
        public List<LevelAnchor> Anchors { get; init; } = new List<LevelAnchor>();
        /// <summary>Levels at the outline corners, in outline order.</summary>
        public List<Point3> Corners { get; init; } = new List<Point3>();
        public double LowLevel => Corners.Min(c => c.Z);
        public double HighLevel => Corners.Max(c => c.Z);

        /// <summary>Level of the plane at a plan point.</summary>
        public double LevelAt(Point2 p)
        {
            var g = FallPercent / 100.0;
            var a = FallDirectionDegrees * Math.PI / 180.0;
            return CentreLevel - g * ((p.X - Centre.X) * Math.Cos(a) + (p.Y - Centre.Y) * Math.Sin(a));
        }
    }

    public sealed class AreaLevelResult
    {
        public List<AreaLevel> Areas { get; init; } = new List<AreaLevel>();
        public List<GradingIssue> Issues { get; init; } = new List<GradingIssue>();
        public bool AllPass => Issues.Count == 0;
        public AreaLevel? For(string spaceId) => Areas.FirstOrDefault(a => a.SpaceId == spaceId);
    }

    public sealed class AreaLevelOptions
    {
        /// <summary>A point counts for a space when inside its outline or within this distance of it.</summary>
        public double SnapMargin { get; init; } = 1.0;
        /// <summary>A hard level the plane misses by more than this conflicts.</summary>
        public double LevelTolerance { get; init; } = 0.01;
    }

    public static class AreaLevelSolver
    {
        private const double HardWeight = 1e8;

        public static AreaLevelResult Solve(
            DesignIntent intent, DesignBrief? brief, RuleSet rules, IGroundModel? ground = null, AreaLevelOptions? options = null)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            options ??= new AreaLevelOptions();
            var result = new AreaLevelResult();
            foreach (var space in intent.Spaces)
            {
                if (space.Kind == SpaceKind.Path || space.Outline == null || space.Outline.Count < 3) continue;
                if (Math.Abs(PolygonGeometry.SignedArea(space.Outline)) < 1e-6) continue;
                SolveArea(space, intent, brief, rules, ground, options, result);
            }
            return result;
        }

        private static void SolveArea(
            Space space, DesignIntent intent, DesignBrief? brief, RuleSet rules, IGroundModel? ground,
            AreaLevelOptions options, AreaLevelResult result)
        {
            var outline = space.Outline!;
            var name = string.IsNullOrWhiteSpace(space.Name) ? KindName(space.Kind) + " " + space.Id : space.Name!;
            var centre = Centroid(outline);
            var (minFall, maxFall) = Limits(space.Kind, rules);

            // Hard levels on the space.
            var anchors = new List<LevelAnchor>();
            void AddPoint(Point2 p, double level, LevelSource source, string label, string? refId)
            {
                if (!PolygonGeometry.Contains(outline, p) && DistanceToOutline(outline, p) > options.SnapMargin) return;
                anchors.Add(new LevelAnchor { At = p, Level = level, Source = source, Label = label, RefId = refId });
            }
            foreach (var spot in space.Level?.Spots ?? new List<SpotLevel>())
                AddPoint(new Point2(spot.X, spot.Y), spot.Z, LevelSource.SpotLevel, "spot level " + F2(spot.Z), null);
            foreach (var b in intent.Site.Buildings)
                foreach (var e in b.Entrances)
                    AddPoint(e.Location, e.ThresholdLevel ?? b.Ffl, LevelSource.Entrance, "entrance " + (e.Name ?? e.Id), e.Id);
            if (brief != null)
            {
                foreach (var p in brief.Points.Where(p => p.Level != null))
                {
                    if (p.Tag == PointTag.Entrance)
                        AddPoint(p.At, p.Level!.Value, LevelSource.BriefEntrance, "entrance " + (p.Name ?? p.Id), p.Id);
                    else if (p.Tag == PointTag.Elevation)
                        AddPoint(p.At, p.Level!.Value, LevelSource.ElevationPoint, "elevation point " + (p.Name ?? p.Id), p.Id);
                }
                foreach (var c in brief.Curves.Where(c => c.Tag == CurveTag.Contour && c.Level != null && c.Points.Count >= 2))
                {
                    var label = "contour " + (c.Name ?? c.Id) + " (" + F2(c.Level!.Value) + ")";
                    foreach (var at in ContourPointsOn(outline, c.Points))
                        anchors.Add(new LevelAnchor { At = at, Level = c.Level.Value, Source = LevelSource.Contour, Label = label, RefId = c.Id });
                }
            }

            // Preferred fall and base level, used where the hard levels leave them free.
            var groundPlane = GroundPlane(outline, centre, ground);
            double fall;
            double dir;
            FallSource source;
            if (space.Level?.Fall != null)
            {
                fall = space.Level.Fall.Percent;
                dir = space.Level.Fall.DirectionDegrees;
                source = FallSource.Designer;
            }
            else
            {
                if (space.Kind == SpaceKind.Plaza)
                {
                    fall = minFall != null && maxFall != null ? (minFall.Value + maxFall.Value) / 2 : minFall ?? maxFall ?? 0;
                    source = FallSource.RuleSet;
                }
                else
                {
                    fall = groundPlane?.Fall ?? minFall ?? 0;
                    source = groundPlane != null ? FallSource.ExistingGround : FallSource.RuleSet;
                }
                if (minFall != null) fall = Math.Max(fall, minFall.Value);
                if (maxFall != null) fall = Math.Min(fall, maxFall.Value);
                dir = groundPlane != null && groundPlane.Value.Fall > 1e-3
                    ? groundPlane.Value.Direction
                    : AwayFromBuilding(centre, intent) ?? 0;
            }
            double? baseLevel = space.Level?.Elevation ?? groundPlane?.CentreLevel ?? ground?.LevelAt(centre);

            if (anchors.Count == 0 && baseLevel == null)
            {
                result.Issues.Add(new GradingIssue
                {
                    Kind = GradingIssueKind.NoLevel, SpaceId = space.Id, At = centre,
                    Message = name + ": nothing fixes its level (no nominal level, spot level, entrance, elevation point or contour with a level on it) and there is no existing ground to sit on.",
                });
                return;
            }

            // Weighted least squares for z = z0 + gx·dx + gy·dy (g = uphill gradient):
            // hard levels dominate, the preferred fall and base level fill what they leave free.
            var a = dir * Math.PI / 180.0;
            var tx = -fall / 100.0 * Math.Cos(a);
            var ty = -fall / 100.0 * Math.Sin(a);
            var size = Math.Max(1.0, Math.Sqrt(Math.Abs(PolygonGeometry.SignedArea(outline))));
            var n = new double[3, 3];
            var r = new double[3];
            void Row(double w, double c0, double c1, double c2, double rhs)
            {
                var c = new[] { c0, c1, c2 };
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 3; j++) n[i, j] += w * c[i] * c[j];
                    r[i] += w * c[i] * rhs;
                }
            }
            foreach (var h in anchors) Row(HardWeight, 1, h.At.X - centre.X, h.At.Y - centre.Y, h.Level);
            var fallWeight = size * size;
            Row(fallWeight, 0, 1, 0, tx);
            Row(fallWeight, 0, 0, 1, ty);
            if (baseLevel != null) Row(anchors.Count == 0 ? 1.0 : 1e-6, 1, 0, 0, baseLevel.Value);
            var x = Solve3(n, r);
            double z0 = x[0], gx = x[1], gy = x[2];

            var g = Math.Sqrt(gx * gx + gy * gy);
            var fallOut = g * 100.0;
            var dirOut = g < 1e-9 ? dir : Normalize(Math.Atan2(-gy, -gx) * 180.0 / Math.PI);
            if (anchors.Count >= 3 && source != FallSource.Designer) source = FallSource.HardLevels;

            var area = new AreaLevel
            {
                SpaceId = space.Id, Name = name, Kind = space.Kind, Centre = centre, CentreLevel = z0,
                FallPercent = fallOut, FallDirectionDegrees = dirOut, FallSource = source,
                MinFallPercent = minFall, MaxFallPercent = maxFall, Anchors = anchors,
            };
            foreach (var p in outline) area.Corners.Add(new Point3(p.X, p.Y, area.LevelAt(p)));
            result.Areas.Add(area);

            // Hard levels that cannot share one even surface.
            var worst = anchors
                .Select(h => (H: h, Miss: area.LevelAt(h.At) - h.Level))
                .OrderByDescending(t => Math.Abs(t.Miss))
                .FirstOrDefault();
            if (worst.H != null && Math.Abs(worst.Miss) > options.LevelTolerance)
                result.Issues.Add(new GradingIssue
                {
                    Kind = GradingIssueKind.ConflictingLevels, SpaceId = space.Id, At = worst.H.At,
                    Message = string.Format(CultureInfo.InvariantCulture,
                        "{0}: its fixed levels do not lie on one even surface; the best fit is {1} m {2} {3} ({4}). Split it into terraces or steps, or check the levels.",
                        name, F2(Math.Abs(worst.Miss)), worst.Miss > 0 ? "above" : "below", worst.H.Label, F2(worst.H.Level)),
                });

            // Fall against the rule set.
            var between = anchors.Count >= 2 ? " between its fixed levels" : "";
            if (minFall != null && minFall.Value > 0 && fallOut < minFall.Value - 1e-6)
                result.Issues.Add(new GradingIssue
                {
                    Kind = GradingIssueKind.FallTooFlat, SpaceId = space.Id, At = centre,
                    MeasuredPercent = fallOut, LimitPercent = minFall,
                    Message = string.Format(CultureInfo.InvariantCulture,
                        "{0}: falls only {1}%{2}; a {3} needs at least {4}% to drain. Change a level so it falls, or add a drain/channel.",
                        name, F1(fallOut), between, KindName(space.Kind).ToLowerInvariant(), F1(minFall.Value)),
                });
            if (maxFall != null && fallOut > maxFall.Value + 1e-6)
                result.Issues.Add(new GradingIssue
                {
                    Kind = GradingIssueKind.TooSteep, SpaceId = space.Id, At = centre,
                    MeasuredPercent = fallOut, LimitPercent = maxFall,
                    Message = string.Format(CultureInfo.InvariantCulture,
                        "{0}: falls {1}%{2}; {3} {4}%. {5}",
                        name, F1(fallOut), between, SteepLimitText(space.Kind), F1(maxFall.Value), SteepFixText(space.Kind)),
                });
        }

        /// <summary>Least and steepest fall the rule set gives for an area kind.</summary>
        public static (double? Min, double? Max) Limits(SpaceKind kind, RuleSet rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            double? V(string id) => rules.Find(id)?.Value;
            return kind switch
            {
                SpaceKind.Plaza => (V(RuleIds.PlazaMinFallPercent), V(RuleIds.PlazaMaxFallPercent)),
                SpaceKind.Lawn => (V(RuleIds.LawnMinFallPercent), V(RuleIds.LawnMaxSlopePercent)),
                SpaceKind.Bed => (null, V(RuleIds.BedMaxSlopePercent)),
                _ => (null, null),
            };
        }

        private static string KindName(SpaceKind kind) => kind switch
        {
            SpaceKind.Plaza => "Plaza",
            SpaceKind.Lawn => "Lawn",
            SpaceKind.Bed => "Bed",
            _ => "Space",
        };

        private static string SteepLimitText(SpaceKind kind) => kind switch
        {
            SpaceKind.Plaza => "a paved plaza may fall at most",
            SpaceKind.Lawn => "a lawn can be mown up to",
            _ => "a planting bed without erosion control may be at most",
        };

        private static string SteepFixText(SpaceKind kind) => kind switch
        {
            SpaceKind.Plaza => "It needs steps or terraces between the fixed levels, or a ramp if it is on the accessible route.",
            SpaceKind.Lawn => "It needs a retaining wall or terraces, or planting instead of lawn.",
            _ => "It needs a retaining wall or terraces, or erosion control.",
        };

        /// <summary>Plane fitted to existing ground under the outline: level at the centre, fall and direction.</summary>
        private static (double CentreLevel, double Fall, double Direction)? GroundPlane(List<Point2> outline, Point2 centre, IGroundModel? ground)
        {
            if (ground == null) return null;
            var samples = outline.Concat(new[] { centre })
                .Select(p => (P: p, Z: ground.LevelAt(p)))
                .Where(t => t.Z != null)
                .ToList();
            if (samples.Count < 3) return null;
            var n = new double[3, 3];
            var r = new double[3];
            foreach (var (p, z) in samples)
            {
                var c = new[] { 1.0, p.X - centre.X, p.Y - centre.Y };
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 3; j++) n[i, j] += c[i] * c[j];
                    r[i] += c[i] * z!.Value;
                }
            }
            // Tiny ridge so collinear samples still solve.
            n[1, 1] += 1e-9;
            n[2, 2] += 1e-9;
            var x = Solve3(n, r);
            var g = Math.Sqrt(x[1] * x[1] + x[2] * x[2]);
            return (x[0], g * 100.0, Normalize(Math.Atan2(-x[2], -x[1]) * 180.0 / Math.PI));
        }

        /// <summary>Direction from the nearest building's footprint centre towards <paramref name="p"/>.</summary>
        private static double? AwayFromBuilding(Point2 p, DesignIntent intent)
        {
            var best = intent.Site.Buildings
                .Where(b => b.Footprint.Count >= 3)
                .Select(b => Centroid(b.Footprint))
                .OrderBy(c => PolygonGeometry.Distance(c, p))
                .Select(c => (Point2?)c)
                .FirstOrDefault();
            if (best == null || PolygonGeometry.Distance(best.Value, p) < 1e-6) return null;
            return Normalize(Math.Atan2(p.Y - best.Value.Y, p.X - best.Value.X) * 180.0 / Math.PI);
        }

        /// <summary>Contour vertices inside the outline and where the contour crosses it.</summary>
        private static IEnumerable<Point2> ContourPointsOn(List<Point2> outline, List<Point2> contour)
        {
            foreach (var p in contour)
                if (PolygonGeometry.Contains(outline, p)) yield return p;
            for (var j = 0; j + 1 < contour.Count; j++)
            {
                var c = contour[j];
                var d = contour[j + 1];
                for (var i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
                    var den = rx * sy - ry * sx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double qx = c.X - a.X, qy = c.Y - a.Y;
                    var t = (qx * sy - qy * sx) / den;
                    var u = (qx * ry - qy * rx) / den;
                    if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) continue;
                    yield return new Point2(a.X + t * rx, a.Y + t * ry);
                }
            }
        }

        private static double DistanceToOutline(List<Point2> outline, Point2 p)
        {
            var best = double.MaxValue;
            for (var i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                var t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
                best = Math.Min(best, PolygonGeometry.Distance(p, new Point2(a.X + t * dx, a.Y + t * dy)));
            }
            return best;
        }

        /// <summary>Area centroid of a closed polygon (vertex mean when degenerate).</summary>
        private static Point2 Centroid(IReadOnlyList<Point2> polygon)
        {
            var a = PolygonGeometry.SignedArea(polygon);
            if (Math.Abs(a) < 1e-9)
                return new Point2(polygon.Average(p => p.X), polygon.Average(p => p.Y));
            double cx = 0, cy = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                var cross = p.X * q.Y - q.X * p.Y;
                cx += (p.X + q.X) * cross;
                cy += (p.Y + q.Y) * cross;
            }
            return new Point2(cx / (6 * a), cy / (6 * a));
        }

        /// <summary>Gaussian elimination with partial pivoting on a 3×3 system.</summary>
        private static double[] Solve3(double[,] m, double[] v)
        {
            var a = (double[,])m.Clone();
            var b = (double[])v.Clone();
            for (var c = 0; c < 3; c++)
            {
                var piv = c;
                for (var i = c + 1; i < 3; i++) if (Math.Abs(a[i, c]) > Math.Abs(a[piv, c])) piv = i;
                if (piv != c)
                {
                    for (var j = 0; j < 3; j++) (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
                    (b[c], b[piv]) = (b[piv], b[c]);
                }
                if (Math.Abs(a[c, c]) < 1e-300) continue;
                for (var i = c + 1; i < 3; i++)
                {
                    var f = a[i, c] / a[c, c];
                    for (var j = c; j < 3; j++) a[i, j] -= f * a[c, j];
                    b[i] -= f * b[c];
                }
            }
            var x = new double[3];
            for (var i = 2; i >= 0; i--)
            {
                var s = b[i];
                for (var j = i + 1; j < 3; j++) s -= a[i, j] * x[j];
                x[i] = Math.Abs(a[i, i]) < 1e-300 ? 0 : s / a[i, i];
            }
            return x;
        }

        private static double Normalize(double deg)
        {
            deg %= 360.0;
            if (deg < 0) deg += 360.0;
            return deg;
        }

        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
