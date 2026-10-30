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
    // Grading solver v0, first slice (roadmap B.6a, CONCEPT §3.3 stage 4 "Level").
    //
    // Levels along every path of a Design Intent from the hard constraints the designer gave:
    // building entrances (threshold, else FFL), brief entrance and elevation points with a level,
    // the path's own spot levels and brief contours that have a level (a contour without a level
    // is soft, D17, and is left to B.6b). Between two fixed levels the path runs at an even grade.
    // A path end with no fixed level ties into existing ground when a ground model is given,
    // otherwise it holds the nearest fixed level.
    //
    // Each even-grade stretch is checked: accessible paths (all paths unless marked otherwise,
    // D11) against the rule set's accessible-route limit (D10); paths marked not accessible
    // against the max slope slider (D19), which runs from the accessible limit (0, gentle) to the
    // rule set's not-accessible maximum (100). Too steep stretches come back as designer-facing
    // reasons ("needs a ramp", "needs steps", with the route length that would work) for the
    // plan repair loop.
    //
    // Area spaces, walls from max grade (D16), soft contours and cut/fill are later slices.

    public enum LevelSource
    {
        /// <summary>The path's own spot level in the Design Intent.</summary>
        SpotLevel,
        /// <summary>A building entrance threshold (or FFL) in the Design Intent.</summary>
        Entrance,
        /// <summary>A brief entrance point with a level.</summary>
        BriefEntrance,
        /// <summary>A brief elevation point (hard spot level).</summary>
        ElevationPoint,
        /// <summary>A brief contour that has a level (hard, D17).</summary>
        Contour,
        /// <summary>Path end tied into existing ground.</summary>
        ExistingGround,
        /// <summary>Path end with nothing to tie into: holds the nearest fixed level.</summary>
        Held,
    }

    /// <summary>A level the path must pass through, at a station along its centerline.</summary>
    public sealed class LevelAnchor
    {
        public double Station { get; init; }
        public Point2 At { get; init; }
        public double Level { get; init; }
        public LevelSource Source { get; init; }
        /// <summary>Designer-facing name ("entrance E1", "elevation point P3").</summary>
        public string Label { get; init; } = "";
        /// <summary>Id of the brief point/curve or intent entrance it came from.</summary>
        public string? RefId { get; init; }
        /// <summary>True for designer-given levels; false for ground tie-ins and held ends.</summary>
        public bool Hard => Source != LevelSource.ExistingGround && Source != LevelSource.Held;
    }

    /// <summary>A computed level on the centerline (at vertices and anchors).</summary>
    public readonly record struct ProfilePoint(double Station, Point2 At, double Level);

    /// <summary>A part of the path between two anchors, at an even grade.</summary>
    public sealed class PathStretch
    {
        public LevelAnchor From { get; init; } = new LevelAnchor();
        public LevelAnchor To { get; init; } = new LevelAnchor();
        public double Length => To.Station - From.Station;
        /// <summary>Signed grade in the path direction, percent.</summary>
        public double GradePercent { get; init; }
        /// <summary>The limit it was checked against; null when the rule set has none.</summary>
        public double? LimitPercent { get; init; }
        public bool Pass => LimitPercent == null || Math.Abs(GradePercent) <= LimitPercent.Value + 1e-9;
    }

    public sealed class PathProfile
    {
        public string SpaceId { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Accessible { get; init; }
        public double Length { get; init; }
        /// <summary>Steepest grade allowed on this path (accessible limit, or from the max slope slider).</summary>
        public double? LimitPercent { get; init; }
        public List<LevelAnchor> Anchors { get; init; } = new List<LevelAnchor>();
        public List<ProfilePoint> Points { get; init; } = new List<ProfilePoint>();
        public List<PathStretch> Stretches { get; init; } = new List<PathStretch>();
        /// <summary>Steepest stretch, percent (absolute); null when the path got no levels.</summary>
        public double? SteepestPercent => Stretches.Count == 0 ? null : Stretches.Max(s => Math.Abs(s.GradePercent));
        public bool HasLevels => Points.Count > 0;
    }

    public enum GradingIssueKind
    {
        /// <summary>Accessible path steeper than the accessible-route limit.</summary>
        NeedsRamp,
        /// <summary>Not-accessible path steeper than its max slope.</summary>
        NeedsSteps,
        /// <summary>Two fixed levels at the same place on the path disagree.</summary>
        ConflictingLevels,
        /// <summary>Nothing fixes the path's levels and there is no existing ground.</summary>
        NoLevel,
        /// <summary>Area space (plaza, lawn) falls less than the rule set's minimum drainage fall (B.6b).</summary>
        FallTooFlat,
        /// <summary>Area space falls more than the rule set allows for its kind (B.6b).</summary>
        TooSteep,
    }

    /// <summary>A reason the levels do not work, for the designer and for the plan repair loop.</summary>
    public sealed class GradingIssue
    {
        public GradingIssueKind Kind { get; init; }
        public string SpaceId { get; init; } = "";
        /// <summary>Zoom target.</summary>
        public Point2 At { get; init; }
        public double? MeasuredPercent { get; init; }
        public double? LimitPercent { get; init; }
        /// <summary>Route length that would take the level difference within the limit.</summary>
        public double? RequiredLength { get; init; }
        public string Message { get; init; } = "";
    }

    public sealed class PathLevelResult
    {
        public List<PathProfile> Profiles { get; init; } = new List<PathProfile>();
        public List<GradingIssue> Issues { get; init; } = new List<GradingIssue>();
        public bool AllPass => Issues.Count == 0;
        public PathProfile? For(string spaceId) => Profiles.FirstOrDefault(p => p.SpaceId == spaceId);
    }

    public sealed class PathLevelOptions
    {
        /// <summary>A point counts for a path when it is within half the path width plus this margin of the centerline.</summary>
        public double SnapMargin { get; init; } = 1.0;
        /// <summary>Width used for paths that have none.</summary>
        public double DefaultWidth { get; init; } = 1.5;
        /// <summary>Fixed levels closer than this along the path are the same place.</summary>
        public double SamePlace { get; init; } = 0.10;
        /// <summary>Fixed levels at the same place that differ by more than this conflict.</summary>
        public double LevelTolerance { get; init; } = 0.01;
    }

    public static class PathLevelSolver
    {
        /// <summary>
        /// Max slope for a path marked not accessible (D19): slider 0 = the accessible limit
        /// (gentle), 100 = the rule set's not-accessible maximum. Null when the rule set lacks
        /// the maximum.
        /// </summary>
        public static double? MaxSlopePercent(int slider, RuleSet rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var max = rules.Find(RuleIds.PathNotAccessibleMaxSlopePercent)?.Value;
            if (max == null) return null;
            var gentle = rules.Find(RuleIds.PathMaxLongitudinalSlopePercent)?.Value ?? max.Value;
            gentle = Math.Min(gentle, max.Value);
            var s = Math.Max(Character.Min, Math.Min(Character.Max, slider)) / (double)Character.Max;
            return gentle + (max.Value - gentle) * s;
        }

        public static PathLevelResult Solve(
            DesignIntent intent, DesignBrief? brief, RuleSet rules, IGroundModel? ground = null, PathLevelOptions? options = null)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            options ??= new PathLevelOptions();
            var result = new PathLevelResult();

            foreach (var space in intent.Spaces)
            {
                if (space.Kind != SpaceKind.Path || space.Centerline == null || space.Centerline.Count < 2) continue;
                if (PolygonGeometry.Length(space.Centerline, closed: false) < 1e-6) continue;
                SolvePath(space, intent, brief, rules, ground, options, result);
            }
            return result;
        }

        private static void SolvePath(
            Space space, DesignIntent intent, DesignBrief? brief, RuleSet rules, IGroundModel? ground,
            PathLevelOptions options, PathLevelResult result)
        {
            var line = space.Centerline!;
            var stations = Stations(line);
            var length = stations[stations.Length - 1];
            var name = string.IsNullOrWhiteSpace(space.Name) ? "Path " + space.Id : space.Name!;
            var accessible = space.Accessible != false;
            var snap = (space.Width ?? options.DefaultWidth) / 2.0 + options.SnapMargin;

            double? limit;
            int slider = Character.DefaultValue;
            if (accessible)
            {
                limit = rules.Find(RuleIds.PathMaxLongitudinalSlopePercent)?.Value;
            }
            else
            {
                var zone = brief?.Zones.FirstOrDefault(z => z.Id == space.ZoneId);
                slider = brief?.CharacterFor(zone!).MaxSlope ?? Character.DefaultValue;
                limit = MaxSlopePercent(slider, rules);
            }

            // Hard levels, in priority order (first wins on a conflict).
            var candidates = new List<(LevelAnchor Anchor, int Priority)>();
            void AddPoint(Point2 p, double level, LevelSource source, string label, string? refId, int priority)
            {
                var (st, dist, at) = Project(line, stations, p);
                if (dist > snap) return;
                candidates.Add((new LevelAnchor { Station = st, At = at, Level = level, Source = source, Label = label, RefId = refId }, priority));
            }

            foreach (var spot in space.Level?.Spots ?? new List<SpotLevel>())
                AddPoint(new Point2(spot.X, spot.Y), spot.Z, LevelSource.SpotLevel,
                    "spot level " + F2(spot.Z), null, 0);

            foreach (var b in intent.Site.Buildings)
                foreach (var e in b.Entrances)
                    AddPoint(e.Location, e.ThresholdLevel ?? b.Ffl, LevelSource.Entrance,
                        "entrance " + (e.Name ?? e.Id), e.Id, 1);

            if (brief != null)
            {
                foreach (var p in brief.Points.Where(p => p.Level != null))
                {
                    if (p.Tag == PointTag.Entrance)
                        AddPoint(p.At, p.Level!.Value, LevelSource.BriefEntrance, "entrance " + (p.Name ?? p.Id), p.Id, 2);
                    else if (p.Tag == PointTag.Elevation)
                        AddPoint(p.At, p.Level!.Value, LevelSource.ElevationPoint, "elevation point " + (p.Name ?? p.Id), p.Id, 3);
                }
                foreach (var c in brief.Curves.Where(c => c.Tag == CurveTag.Contour && c.Level != null && c.Points.Count >= 2))
                    foreach (var (st, at) in Crossings(line, stations, c.Points))
                        candidates.Add((new LevelAnchor
                        {
                            Station = st, At = at, Level = c.Level!.Value, Source = LevelSource.Contour,
                            Label = "contour " + (c.Name ?? c.Id) + " (" + F2(c.Level.Value) + ")", RefId = c.Id,
                        }, 4));
            }

            var anchors = new List<LevelAnchor>();
            foreach (var (a, _) in candidates.OrderBy(c => c.Priority).ThenBy(c => c.Anchor.Station))
            {
                var same = anchors.FirstOrDefault(x => Math.Abs(x.Station - a.Station) < options.SamePlace);
                if (same == null) { anchors.Add(a); continue; }
                if (Math.Abs(same.Level - a.Level) > options.LevelTolerance)
                    result.Issues.Add(new GradingIssue
                    {
                        Kind = GradingIssueKind.ConflictingLevels, SpaceId = space.Id, At = a.At,
                        Message = string.Format(CultureInfo.InvariantCulture,
                            "{0}: {1} ({2}) and {3} ({4}) are at the same place but {5} m apart in level; the levels use {1}.",
                            name, same.Label, F2(same.Level), a.Label, F2(a.Level), F2(Math.Abs(same.Level - a.Level))),
                    });
            }
            anchors.Sort((x, y) => x.Station.CompareTo(y.Station));

            // Ends: existing ground, else hold the nearest fixed level.
            void AddEnd(double st, Point2 at, string where, bool start)
            {
                if (anchors.Any(a => Math.Abs(a.Station - st) < options.SamePlace)) return;
                var g = ground?.LevelAt(at);
                LevelAnchor? end = null;
                if (g != null)
                    end = new LevelAnchor { Station = st, At = at, Level = g.Value, Source = LevelSource.ExistingGround, Label = "existing ground at the path " + where };
                else if (anchors.Count > 0)
                {
                    var near = start ? anchors[0] : anchors[anchors.Count - 1];
                    end = new LevelAnchor { Station = st, At = at, Level = near.Level, Source = LevelSource.Held, Label = "path " + where + " (held at " + F2(near.Level) + ")" };
                }
                if (end == null) return;
                if (start) anchors.Insert(0, end); else anchors.Add(end);
            }
            if (anchors.Count == 0)
            {
                // Without any fixed level, ends tie into ground only.
                AddEnd(0, line[0], "start", true);
                AddEnd(length, line[line.Count - 1], "end", false);
                if (anchors.Count == 1)
                {
                    var only = anchors[0];
                    var st = only.Station < length / 2 ? length : 0;
                    var at = st == 0 ? line[0] : line[line.Count - 1];
                    var held = new LevelAnchor { Station = st, At = at, Level = only.Level, Source = LevelSource.Held, Label = "path " + (st == 0 ? "start" : "end") + " (held at " + F2(only.Level) + ")" };
                    if (st == 0) anchors.Insert(0, held); else anchors.Add(held);
                }
            }
            else
            {
                AddEnd(0, line[0], "start", true);
                AddEnd(length, line[line.Count - 1], "end", false);
            }

            var profile = new PathProfile
            {
                SpaceId = space.Id, Name = name, Accessible = accessible, Length = length, LimitPercent = limit, Anchors = anchors,
            };
            result.Profiles.Add(profile);

            if (anchors.Count == 0)
            {
                result.Issues.Add(new GradingIssue
                {
                    Kind = GradingIssueKind.NoLevel, SpaceId = space.Id, At = PolygonGeometry.PointAtHalfLength(line),
                    Message = name + ": nothing fixes its levels (no entrance, elevation point, spot level or contour with a level on it) and there is no existing ground to tie into.",
                });
                return;
            }

            // Profile points at vertices and anchors.
            var pts = new List<(double St, Point2 At)>();
            for (var i = 0; i < line.Count; i++) pts.Add((stations[i], line[i]));
            pts.AddRange(anchors.Select(a => (a.Station, a.At)));
            foreach (var (st, at) in pts.OrderBy(p => p.St))
            {
                if (profile.Points.Count > 0 && st - profile.Points[profile.Points.Count - 1].Station < 1e-6) continue;
                profile.Points.Add(new ProfilePoint(st, at, LevelAt(anchors, st)));
            }

            // Stretches and their check.
            for (var i = 0; i + 1 < anchors.Count; i++)
            {
                var from = anchors[i];
                var to = anchors[i + 1];
                var len = to.Station - from.Station;
                if (len < 1e-6) continue;
                var dz = to.Level - from.Level;
                var stretch = new PathStretch { From = from, To = to, GradePercent = dz / len * 100.0, LimitPercent = limit };
                profile.Stretches.Add(stretch);
                if (stretch.Pass) continue;

                var grade = Math.Abs(stretch.GradePercent);
                var required = Math.Abs(dz) / limit!.Value * 100.0;
                var head = string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1} m level difference over {2} m between {3} and {4} ({5}%).",
                    name, F2(Math.Abs(dz)), F1(len), from.Label, to.Label, F1(grade));
                var tail = accessible
                    ? string.Format(CultureInfo.InvariantCulture,
                        " An accessible route may be at most {0}%: it needs a ramp with landings, or a route at least {1} m long.",
                        F1(limit.Value), F1(required))
                    : string.Format(CultureInfo.InvariantCulture,
                        " This path is not on the accessible route; its max slope is {0}% (max slope slider {1}): it needs steps, or a route at least {2} m long.",
                        F1(limit.Value), slider, F1(required));
                result.Issues.Add(new GradingIssue
                {
                    Kind = accessible ? GradingIssueKind.NeedsRamp : GradingIssueKind.NeedsSteps,
                    SpaceId = space.Id,
                    At = PointAt(line, stations, (from.Station + to.Station) / 2),
                    MeasuredPercent = grade, LimitPercent = limit, RequiredLength = required,
                    Message = head + tail,
                });
            }
        }

        private static double LevelAt(List<LevelAnchor> anchors, double st)
        {
            if (st <= anchors[0].Station) return anchors[0].Level;
            for (var i = 0; i + 1 < anchors.Count; i++)
            {
                var a = anchors[i];
                var b = anchors[i + 1];
                if (st > b.Station) continue;
                var len = b.Station - a.Station;
                return len < 1e-9 ? b.Level : a.Level + (b.Level - a.Level) * (st - a.Station) / len;
            }
            return anchors[anchors.Count - 1].Level;
        }

        private static double[] Stations(List<Point2> line)
        {
            var s = new double[line.Count];
            for (var i = 1; i < line.Count; i++) s[i] = s[i - 1] + PolygonGeometry.Distance(line[i - 1], line[i]);
            return s;
        }

        /// <summary>Nearest point on the polyline: station, distance to it, and the point.</summary>
        private static (double Station, double Distance, Point2 At) Project(List<Point2> line, double[] stations, Point2 p)
        {
            var best = (Station: 0.0, Distance: double.MaxValue, At: line[0]);
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                var t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
                var q = new Point2(a.X + t * dx, a.Y + t * dy);
                var d = PolygonGeometry.Distance(p, q);
                if (d < best.Distance - 1e-12) best = (stations[i] + t * Math.Sqrt(l2), d, q);
            }
            return best;
        }

        /// <summary>Where a curve crosses the polyline, as stations along it.</summary>
        private static IEnumerable<(double Station, Point2 At)> Crossings(List<Point2> line, double[] stations, List<Point2> curve)
        {
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                for (var j = 0; j + 1 < curve.Count; j++)
                {
                    var c = curve[j];
                    var d = curve[j + 1];
                    double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
                    var den = rx * sy - ry * sx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double qx = c.X - a.X, qy = c.Y - a.Y;
                    var t = (qx * sy - qy * sx) / den;
                    var u = (qx * ry - qy * rx) / den;
                    if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) continue;
                    t = Math.Max(0, Math.Min(1, t));
                    yield return (stations[i] + t * (stations[i + 1] - stations[i]), new Point2(a.X + t * rx, a.Y + t * ry));
                }
            }
        }

        private static Point2 PointAt(List<Point2> line, double[] stations, double st)
        {
            for (var i = 0; i + 1 < line.Count; i++)
            {
                if (st > stations[i + 1] && i + 2 < line.Count) continue;
                var len = stations[i + 1] - stations[i];
                var t = len < 1e-12 ? 0 : Math.Max(0, Math.Min(1, (st - stations[i]) / len));
                return new Point2(line[i].X + t * (line[i + 1].X - line[i].X), line[i].Y + t * (line[i + 1].Y - line[i].Y));
            }
            return line[line.Count - 1];
        }

        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
