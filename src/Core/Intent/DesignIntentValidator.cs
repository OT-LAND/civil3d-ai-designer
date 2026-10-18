using System;
using System.Collections.Generic;
using System.Linq;

namespace CivilAI.Core.Intent
{
    /// <summary>One problem found in a Design Intent; <see cref="Path"/> is a JSON-style path.</summary>
    public readonly record struct IntentIssue(string Path, string Message)
    {
        public override string ToString() => $"{Path}: {Message}";
    }

    /// <summary>
    /// Content checks the JSON Schema cannot express: unique ids, references between items,
    /// usable geometry (closed outlines with area, paths with a centerline and width), finite
    /// numbers. Design rules (slopes, accessibility, tree zones) are not checked here; that is
    /// the rule engine (2.2).
    /// </summary>
    public static class DesignIntentValidator
    {
        private const double MinArea = 1e-6;   // m²
        private const double MinLength = 1e-6; // m
        private const double MaxFallPercent = 100.0;

        public static IReadOnlyList<IntentIssue> Validate(DesignIntent intent)
        {
            if (intent is null) throw new ArgumentNullException(nameof(intent));
            var issues = new List<IntentIssue>();
            void Add(string path, string message) => issues.Add(new IntentIssue(path, message));

            if (intent.SchemaVersion != DesignIntent.CurrentSchemaVersion)
                Add("schemaVersion", $"Expected schema version \"{DesignIntent.CurrentSchemaVersion}\", got \"{intent.SchemaVersion}\".");

            // Ids are unique across the whole document so edits can refer to any item by id.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void CheckId(string path, string id)
            {
                if (string.IsNullOrWhiteSpace(id)) Add(path, "Id is missing.");
                else if (!ids.Add(id)) Add(path, $"Id \"{id}\" is used more than once.");
            }

            var site = intent.Site ?? new Site();
            if (site.LotBoundary is not null)
                CheckPolygon("site.lotBoundary", site.LotBoundary, "lot border", Add);

            for (int i = 0; i < site.Buildings.Count; i++)
            {
                var b = site.Buildings[i];
                var p = $"site.buildings[{i}]";
                CheckId($"{p}.id", b.Id);
                CheckPolygon($"{p}.footprint", b.Footprint, "building footprint", Add);
                CheckFinite($"{p}.ffl", b.Ffl, "Finished floor level", Add);
                for (int e = 0; e < b.Entrances.Count; e++)
                {
                    var en = b.Entrances[e];
                    var ep = $"{p}.entrances[{e}]";
                    CheckId($"{ep}.id", en.Id);
                    CheckPoint($"{ep}.location", en.Location, Add);
                    if (en.ThresholdLevel is double t) CheckFinite($"{ep}.thresholdLevel", t, "Threshold level", Add);
                }
            }

            var materialIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < intent.Materials.Count; i++)
            {
                var m = intent.Materials[i];
                CheckId($"materials[{i}].id", m.Id);
                if (!string.IsNullOrWhiteSpace(m.Id)) materialIds.Add(m.Id);
                if (string.IsNullOrWhiteSpace(m.Name)) Add($"materials[{i}].name", "Material name is missing.");
            }

            var spaceIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < intent.Spaces.Count; i++)
            {
                var s = intent.Spaces[i];
                var p = $"spaces[{i}]";
                CheckId($"{p}.id", s.Id);
                if (!string.IsNullOrWhiteSpace(s.Id)) spaceIds.Add(s.Id);

                if (s.Kind == SpaceKind.Path)
                {
                    if (s.Centerline is null || s.Centerline.Count < 2)
                        Add($"{p}.centerline", "A path needs a centerline with at least 2 points.");
                    else
                        CheckPolyline($"{p}.centerline", s.Centerline, "path centerline", Add);
                    if (s.Width is not double w || !(w > 0) || double.IsInfinity(w))
                        Add($"{p}.width", "A path needs a width greater than 0 m.");
                    if (s.Outline is not null)
                        Add($"{p}.outline", "A path is described by its centerline and width, not an outline.");
                }
                else
                {
                    if (s.Outline is null)
                        Add($"{p}.outline", $"A {s.Kind.ToString().ToLowerInvariant()} needs an outline.");
                    else
                        CheckPolygon($"{p}.outline", s.Outline, $"{s.Kind.ToString().ToLowerInvariant()} outline", Add);
                    if (s.Centerline is not null || s.Width is not null)
                        Add(p, "Only paths have a centerline and width.");
                }

                if (s.MaterialId is not null && !materialIds.Contains(s.MaterialId))
                    Add($"{p}.materialId", $"Material \"{s.MaterialId}\" is not in the materials list.");

                if (s.Level is LevelIntent level)
                {
                    if (level.Elevation is double z) CheckFinite($"{p}.level.elevation", z, "Level", Add);
                    for (int k = 0; k < level.Spots.Count; k++)
                    {
                        var spot = level.Spots[k];
                        var sp = $"{p}.level.spots[{k}]";
                        CheckPoint(sp, new Point2(spot.X, spot.Y), Add);
                        CheckFinite($"{sp}.z", spot.Z, "Spot level", Add);
                    }
                    if (level.Fall is Fall fall)
                    {
                        if (!(fall.Percent >= 0 && fall.Percent <= MaxFallPercent))
                            Add($"{p}.level.fall.percent", $"Fall must be between 0% and {MaxFallPercent}%.");
                        CheckFinite($"{p}.level.fall.directionDegrees", fall.DirectionDegrees, "Fall direction", Add);
                    }
                }
            }

            for (int i = 0; i < intent.Edges.Count; i++)
            {
                var e = intent.Edges[i];
                var p = $"edges[{i}]";
                CheckId($"{p}.id", e.Id);
                if (e.Line.Count < 2) Add($"{p}.line", "An edge needs a line with at least 2 points.");
                else CheckPolyline($"{p}.line", e.Line, "edge line", Add);

                if (e.SpaceIds.Count < 1 || e.SpaceIds.Count > 2)
                    Add($"{p}.spaceIds", "An edge bounds one or two spaces.");
                else if (e.SpaceIds.Count == 2 && e.SpaceIds[0] == e.SpaceIds[1])
                    Add($"{p}.spaceIds", "An edge cannot have the same space on both sides.");
                foreach (var id in e.SpaceIds)
                    if (!spaceIds.Contains(id))
                        Add($"{p}.spaceIds", $"Space \"{id}\" does not exist.");

                if (e.Height is double h && (!(h >= 0) || double.IsInfinity(h)))
                    Add($"{p}.height", "Height must be 0 m or more.");
                if ((e.Kind == EdgeKind.Steps || e.Kind == EdgeKind.Wall) && !(e.Height > 0))
                    Add($"{p}.height", $"{e.Kind} need a height (the level difference they take up).");
            }

            return issues;
        }

        private static void CheckFinite(string path, double value, string what, Action<string, string> add)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) add(path, $"{what} must be a finite number.");
        }

        private static bool CheckPoint(string path, Point2 pt, Action<string, string> add)
        {
            if (double.IsFinite(pt.X) && double.IsFinite(pt.Y)) return true;
            add(path, "Point coordinates must be finite numbers.");
            return false;
        }

        private static bool AllFinite(string path, List<Point2> points, Action<string, string> add)
        {
            bool ok = true;
            for (int i = 0; i < points.Count; i++) ok &= CheckPoint($"{path}[{i}]", points[i], add);
            return ok;
        }

        private static void CheckPolyline(string path, List<Point2> points, string what, Action<string, string> add)
        {
            if (!AllFinite(path, points, add)) return;
            for (int i = 1; i < points.Count; i++)
                if (Distance(points[i - 1], points[i]) < MinLength)
                    add($"{path}[{i}]", $"The {what} repeats the same point.");
        }

        private static void CheckPolygon(string path, List<Point2> points, string what, Action<string, string> add)
        {
            if (points.Count < 3)
            {
                add(path, $"The {what} needs at least 3 points.");
                return;
            }
            if (!AllFinite(path, points, add)) return;
            if (Distance(points[0], points[^1]) < MinLength)
                add(path, $"The {what} repeats its first point at the end; leave the closing point out.");
            if (Math.Abs(SignedArea(points)) < MinArea)
                add(path, $"The {what} has no area.");
        }

        private static double Distance(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>Shoelace area; positive for counter-clockwise polygons.</summary>
        internal static double SignedArea(IReadOnlyList<Point2> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2.0;
        }
    }
}
