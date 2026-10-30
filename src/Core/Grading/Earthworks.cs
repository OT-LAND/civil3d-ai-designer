using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Geometry;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Grading
{
    // Grading solver v0, third slice (roadmap B.6c, CONCEPT §3.3 stage 4 "Level").
    //
    // Cut and fill of the proposed levels against existing ground:
    //   - area spaces (plaza, lawn, bed): the level plane from B.6b, sampled on a grid of cells
    //     inside the outline;
    //   - paths: the profile from B.6a, sampled along the centerline times the path width.
    // Where existing ground has no data the cell is left out and counted as "not measured".
    //
    // Balance toward the cut↔fill slider (0 export soil … 50 balanced … 100 import soil): the
    // slider is read as the fill share of all moved soil, fill / (cut + fill). Area spaces whose
    // level nothing fixes (no hard level, no nominal level from the designer) are "free": they
    // move up or down together, by at most MaxShift, until the fill share meets the target.
    // Their falls stay as they are, so the B.6b fall checks still hold.
    //
    // v0 limits, easy to change: paths and spaces with a hard or nominal level never move;
    // all free spaces move by the same amount; where a path crosses an area both are counted.

    /// <summary>Cut and fill of one space, cubic metres.</summary>
    public sealed class SpaceEarthwork
    {
        public string SpaceId { get; init; } = "";
        public string Name { get; init; } = "";
        public SpaceKind Kind { get; init; }
        /// <summary>A point on the space (area centre, path halfway point) for zone filtering and zoom.</summary>
        public Point2 At { get; init; }
        public double Cut { get; init; }
        public double Fill { get; init; }
        /// <summary>Plan area that was measured, m².</summary>
        public double MeasuredArea { get; init; }
        /// <summary>Plan area with no existing ground under it, m².</summary>
        public double UnmeasuredArea { get; init; }
        /// <summary>Nothing fixes the level, so balancing may move it.</summary>
        public bool Free { get; init; }
        /// <summary>How far balancing moved the level, metres (0 when not moved).</summary>
        public double Shift { get; init; }
    }

    public sealed class EarthworkResult
    {
        public List<SpaceEarthwork> Spaces { get; init; } = new List<SpaceEarthwork>();
        public double Cut => Spaces.Sum(s => s.Cut);
        public double Fill => Spaces.Sum(s => s.Fill);
        /// <summary>Fill minus cut: positive = soil to bring in, negative = soil to take away.</summary>
        public double Net => Fill - Cut;
        public double UnmeasuredArea => Spaces.Sum(s => s.UnmeasuredArea);

        /// <summary>Fill share of all moved soil, percent (the cut↔fill slider's units); null when nothing moves.</summary>
        public double? FillSharePercent => Earthworks.FillShare(Cut, Fill);

        public SpaceEarthwork? For(string spaceId) => Spaces.FirstOrDefault(s => s.SpaceId == spaceId);
    }

    /// <summary>Area levels after balancing, with their earthworks.</summary>
    public sealed class BalanceResult
    {
        /// <summary>The area levels, free spaces moved by <see cref="Shift"/>; same issues as before.</summary>
        public AreaLevelResult Areas { get; init; } = new AreaLevelResult();
        public EarthworkResult Earthworks { get; init; } = new EarthworkResult();
        /// <summary>Common move of the free spaces, metres (+ up, − down).</summary>
        public double Shift { get; init; }
        public int TargetFillSharePercent { get; init; }
        /// <summary>Designer-facing reason when the target could not be met; null when met.</summary>
        public string? Issue { get; init; }
    }

    public sealed class EarthworkOptions
    {
        /// <summary>Grid cell / step along paths, metres. Grows on large spaces to stay within <see cref="MaxSamplesPerSpace"/>.</summary>
        public double CellSize { get; init; } = 0.5;
        public int MaxSamplesPerSpace { get; init; } = 4000;
        /// <summary>Width used for paths that have none (as in <see cref="PathLevelOptions.DefaultWidth"/>).</summary>
        public double DefaultPathWidth { get; init; } = 1.5;
        /// <summary>Furthest balancing may move a free space up or down, metres.</summary>
        public double MaxShift { get; init; } = 0.30;
        /// <summary>Fill share within this many points of the target counts as met (same as the card's tolerance).</summary>
        public double Tolerance { get; init; } = 10;
    }

    public static class Earthworks
    {
        /// <summary>Fill / (cut + fill) as a percent; null when both are (near) zero.</summary>
        public static double? FillShare(double cut, double fill)
        {
            var moved = cut + fill;
            return moved < 1e-6 ? null : fill / moved * 100.0;
        }

        /// <summary>Cut and fill of the solved levels against existing ground.</summary>
        public static EarthworkResult Measure(
            DesignIntent intent, AreaLevelResult? areas, PathLevelResult? paths, IGroundModel ground, EarthworkOptions? options = null)
        {
            var samples = Sample(intent, areas, paths, ground, options ?? new EarthworkOptions());
            return Result(samples, 0);
        }

        /// <summary>
        /// Moves the free area spaces up or down together so the fill share approaches the
        /// cut↔fill slider (0 export … 50 balanced … 100 import), within <see cref="EarthworkOptions.MaxShift"/>.
        /// </summary>
        public static BalanceResult Balance(
            DesignIntent intent, AreaLevelResult areas, PathLevelResult? paths, IGroundModel ground,
            int cutFillSlider, EarthworkOptions? options = null)
        {
            if (areas == null) throw new ArgumentNullException(nameof(areas));
            options ??= new EarthworkOptions();
            var target = Math.Max(Character.Min, Math.Min(Character.Max, cutFillSlider));
            var samples = Sample(intent, areas, paths, ground, options);

            double ShareAt(double shift) => Result(samples, shift).FillSharePercent ?? target;

            var shift = 0.0;
            var hasFree = samples.Any(s => s.Free && s.Cells.Count > 0);
            if (hasFree && Math.Abs(ShareAt(0) - target) > 1e-6)
            {
                // Fill share never drops as the free spaces rise. Move as little as reaches the
                // target: raise when there is too little fill, lower when there is too much.
                var up = ShareAt(0) < target;
                var limit = up ? options.MaxShift : -options.MaxShift;
                bool Reached(double d) => up ? ShareAt(d) >= target - 1e-6 : ShareAt(d) <= target + 1e-6;
                if (!Reached(limit)) shift = limit;
                else
                {
                    double near = 0, far = limit; // Reached(far) holds, Reached(near) does not
                    for (var i = 0; i < 40 && Math.Abs(far - near) > 1e-4; i++)
                    {
                        var mid = (near + far) / 2;
                        if (Reached(mid)) far = mid; else near = mid;
                    }
                    shift = far;
                }
                shift = Math.Round(shift, 3);
            }

            var earth = Result(samples, shift);
            var moved = new AreaLevelResult { Issues = areas.Issues.ToList() };
            foreach (var a in areas.Areas)
                moved.Areas.Add(samples.Any(s => s.Free && s.SpaceId == a.SpaceId) && Math.Abs(shift) > 0 ? Shifted(a, shift) : a);

            string? issue = null;
            var share = earth.FillSharePercent;
            if (share != null && Math.Abs(share.Value - target) > options.Tolerance + 1e-9)
                issue = MissText(target, share.Value, earth, hasFree, shift, options);

            return new BalanceResult { Areas = moved, Earthworks = earth, Shift = shift, TargetFillSharePercent = target, Issue = issue };
        }

        private static string MissText(int target, double share, EarthworkResult earth, bool hasFree, double shift, EarthworkOptions options)
        {
            var wants = target < 50 ? "more soil taken away" : target > 50 ? "more soil brought in" : "a balance on site";
            var now = string.Format(CultureInfo.InvariantCulture, "cut {0} m³, fill {1} m³ ({2})",
                F0(earth.Cut), F0(earth.Fill), earth.Net >= 0 ? F0(earth.Net) + " m³ to bring in" : F0(-earth.Net) + " m³ to take away");
            var why = !hasFree
                ? "every space has a fixed level, so nothing can move up or down"
                : string.Format(CultureInfo.InvariantCulture, "the spaces without a fixed level already moved {0} m, the most allowed", F2(Math.Abs(shift)));
            return string.Format(CultureInfo.InvariantCulture,
                "Earthworks: {0}; the cut↔fill slider asks for {1}, but {2}. Change a fixed level, add terraces or walls, or move the slider.",
                now, wants, why);
        }

        private static AreaLevel Shifted(AreaLevel a, double shift)
        {
            var b = new AreaLevel
            {
                SpaceId = a.SpaceId, Name = a.Name, Kind = a.Kind, Centre = a.Centre, CentreLevel = a.CentreLevel + shift,
                FallPercent = a.FallPercent, FallDirectionDegrees = a.FallDirectionDegrees, FallSource = a.FallSource,
                MinFallPercent = a.MinFallPercent, MaxFallPercent = a.MaxFallPercent, Anchors = a.Anchors,
            };
            foreach (var c in a.Corners) b.Corners.Add(new Point3(c.X, c.Y, c.Z + shift));
            return b;
        }

        // ---- sampling -------------------------------------------------------------------

        private readonly record struct Cell(double DesignMinusGround, double Area);

        private sealed class SpaceSamples
        {
            public string SpaceId = "";
            public string Name = "";
            public SpaceKind Kind;
            public Point2 At;
            public bool Free;
            public double Unmeasured;
            public List<Cell> Cells = new List<Cell>();
        }

        private static EarthworkResult Result(List<SpaceSamples> samples, double shift)
        {
            var result = new EarthworkResult();
            foreach (var s in samples)
            {
                var d = s.Free ? shift : 0;
                double cut = 0, fill = 0, area = 0;
                foreach (var c in s.Cells)
                {
                    var dz = c.DesignMinusGround + d;
                    if (dz > 0) fill += dz * c.Area; else cut -= dz * c.Area;
                    area += c.Area;
                }
                result.Spaces.Add(new SpaceEarthwork
                {
                    SpaceId = s.SpaceId, Name = s.Name, Kind = s.Kind, At = s.At, Cut = cut, Fill = fill,
                    MeasuredArea = area, UnmeasuredArea = s.Unmeasured, Free = s.Free, Shift = d,
                });
            }
            return result;
        }

        private static List<SpaceSamples> Sample(
            DesignIntent intent, AreaLevelResult? areas, PathLevelResult? paths, IGroundModel ground, EarthworkOptions options)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (!(options.CellSize > 0)) throw new ArgumentOutOfRangeException(nameof(options), "Cell size must be greater than 0.");
            var list = new List<SpaceSamples>();
            var spaces = intent.Spaces.ToDictionary(s => s.Id, StringComparer.Ordinal);

            foreach (var a in areas?.Areas ?? new List<AreaLevel>())
            {
                if (!spaces.TryGetValue(a.SpaceId, out var space) || space.Outline == null || space.Outline.Count < 3) continue;
                var s = new SpaceSamples
                {
                    SpaceId = a.SpaceId, Name = a.Name, Kind = a.Kind, At = a.Centre,
                    Free = a.Anchors.Count == 0 && space.Level?.Elevation == null,
                };
                SampleArea(space.Outline, a, ground, options, s);
                list.Add(s);
            }

            foreach (var p in paths?.Profiles ?? new List<PathProfile>())
            {
                if (!p.HasLevels || !spaces.TryGetValue(p.SpaceId, out var space) || space.Centerline == null || space.Centerline.Count < 2) continue;
                var s = new SpaceSamples
                {
                    SpaceId = p.SpaceId, Name = p.Name, Kind = SpaceKind.Path,
                    At = PolygonGeometry.PointAtHalfLength(space.Centerline), Free = false,
                };
                SamplePath(space.Centerline, space.Width ?? options.DefaultPathWidth, p, ground, options, s);
                list.Add(s);
            }
            return list;
        }

        private static void SampleArea(List<Point2> outline, AreaLevel level, IGroundModel ground, EarthworkOptions options, SpaceSamples s)
        {
            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            double minY = outline.Min(p => p.Y), maxY = outline.Max(p => p.Y);
            var area = Math.Abs(PolygonGeometry.SignedArea(outline));
            var cell = Math.Max(options.CellSize, Math.Sqrt(area / Math.Max(1, options.MaxSamplesPerSpace)));
            var nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cell));
            var ny = Math.Max(1, (int)Math.Ceiling((maxY - minY) / cell));
            double cw = (maxX - minX) / nx, ch = (maxY - minY) / ny;
            var inside = new List<Point2>();
            for (var i = 0; i < nx; i++)
                for (var j = 0; j < ny; j++)
                {
                    var p = new Point2(minX + (i + 0.5) * cw, minY + (j + 0.5) * ch);
                    if (PolygonGeometry.Contains(outline, p)) inside.Add(p);
                }
            if (inside.Count == 0) return;
            // Scale cells so their total is the true outline area (edges cut cells unevenly).
            var a = area / inside.Count;
            foreach (var p in inside)
            {
                var g = ground.LevelAt(p);
                if (g == null) { s.Unmeasured += a; continue; }
                s.Cells.Add(new Cell(level.LevelAt(p) - g.Value, a));
            }
        }

        private static void SamplePath(List<Point2> line, double width, PathProfile profile, IGroundModel ground, EarthworkOptions options, SpaceSamples s)
        {
            var length = PolygonGeometry.Length(line, closed: false);
            if (length < 1e-9) return;
            var step = Math.Max(options.CellSize, length * width / Math.Max(1, options.MaxSamplesPerSpace));
            var n = Math.Max(1, (int)Math.Ceiling(length / step));
            var ds = length / n;
            var pts = profile.Points.OrderBy(p => p.Station).ToList();
            for (var k = 0; k < n; k++)
            {
                var st = (k + 0.5) * ds;
                var at = PointAt(line, st);
                var g = ground.LevelAt(at);
                if (g == null) { s.Unmeasured += ds * width; continue; }
                s.Cells.Add(new Cell(ProfileLevel(pts, st) - g.Value, ds * width));
            }
        }

        private static double ProfileLevel(List<ProfilePoint> pts, double st)
        {
            if (st <= pts[0].Station) return pts[0].Level;
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                if (st > b.Station) continue;
                var len = b.Station - a.Station;
                return len < 1e-9 ? b.Level : a.Level + (b.Level - a.Level) * (st - a.Station) / len;
            }
            return pts[pts.Count - 1].Level;
        }

        private static Point2 PointAt(List<Point2> line, double st)
        {
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var d = PolygonGeometry.Distance(line[i], line[i + 1]);
                if (st <= d && d > 0)
                {
                    var t = st / d;
                    return new Point2(line[i].X + t * (line[i + 1].X - line[i].X), line[i].Y + t * (line[i + 1].Y - line[i].Y));
                }
                st -= d;
            }
            return line[line.Count - 1];
        }

        private static string F0(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
