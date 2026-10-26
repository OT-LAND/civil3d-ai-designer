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
    /// Content checks the JSON Schema cannot express: unique ids, references between items
    /// (materials, edge spaces, swale outlets), usable geometry (closed outlines with area,
    /// paths with a centerline and width), finite numbers, v1 parts only under schema "1".
    /// References to the Design Brief (zoneId, interestPointId, flowBasinId) are not checked here. Design rules (slopes, accessibility, tree zones) are not checked here; that is
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

            if (!DesignIntent.SupportedSchemaVersions.Contains(intent.SchemaVersion))
                Add("schemaVersion", $"Expected schema version \"{DesignIntent.CurrentSchemaVersion}\", got \"{intent.SchemaVersion}\".");
            else if (intent.SchemaVersion == "0" && UsesV1(intent))
                Add("schemaVersion", "Trees, drainage, site elements and brief references need schema version \"1\".");

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
                    if (s.Accessible is not null)
                        Add($"{p}.accessible", "Only paths can be marked accessible or not accessible.");
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

            ValidateV1(intent, CheckId, Add);

            return issues;
        }

        private static bool UsesV1(DesignIntent intent)
        {
            var d = intent.Drainage ?? new DrainageIntent();
            return intent.Trees.Count > 0 || intent.Elements.Count > 0
                || d.Swales.Count > 0 || d.Basins.Count > 0 || d.LowPoints.Count > 0
                || intent.Spaces.Any(s => s.ZoneId is not null || s.InterestPointId is not null);
        }

        /// <summary>Trees, drainage and site elements (v1).</summary>
        private static void ValidateV1(DesignIntent intent, Action<string, string> checkId, Action<string, string> add)
        {
            for (int i = 0; i < intent.Trees.Count; i++)
            {
                var t = intent.Trees[i];
                var p = $"trees[{i}]";
                checkId($"{p}.id", t.Id);
                CheckPoint($"{p}.location", t.Location, add);
                CheckPositive($"{p}.canopyDiameter", t.CanopyDiameter, "A tree needs a canopy diameter greater than 0 m.", add);
                if (t.TrunkDiameter is double trunk)
                    CheckPositive($"{p}.trunkDiameter", trunk, "Trunk diameter must be greater than 0 m.", add);
                if (t.Status == TreeStatus.New)
                {
                    if (t.ProtectionRadius is not null)
                        add($"{p}.protectionRadius", "Only existing trees have a root protection zone.");
                }
                else
                {
                    if (t.ProtectionRadius is double r)
                        CheckPositive($"{p}.protectionRadius", r, "Protection radius must be greater than 0 m.", add);
                    if (string.IsNullOrWhiteSpace(t.SourceId))
                        add($"{p}.sourceId", "An existing tree needs the survey tree id it was kept from.");
                }
            }

            var drainage = intent.Drainage ?? new DrainageIntent();
            var outletIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < drainage.Basins.Count; i++)
            {
                var b = drainage.Basins[i];
                var p = $"drainage.basins[{i}]";
                checkId($"{p}.id", b.Id);
                if (!string.IsNullOrWhiteSpace(b.Id)) outletIds.Add(b.Id);
                CheckPolygon($"{p}.outline", b.Outline, "basin outline", add);
                if (b.BottomLevel is double z) CheckFinite($"{p}.bottomLevel", z, "Bottom level", add);
                if (b.Depth is double depth) CheckPositive($"{p}.depth", depth, "Depth must be greater than 0 m.", add);
            }
            for (int i = 0; i < drainage.LowPoints.Count; i++)
            {
                var lp = drainage.LowPoints[i];
                var p = $"drainage.lowPoints[{i}]";
                checkId($"{p}.id", lp.Id);
                if (!string.IsNullOrWhiteSpace(lp.Id)) outletIds.Add(lp.Id);
                CheckPoint($"{p}.location", lp.Location, add);
                if (lp.Level is double z) CheckFinite($"{p}.level", z, "Level", add);
            }
            for (int i = 0; i < drainage.Swales.Count; i++)
            {
                var sw = drainage.Swales[i];
                var p = $"drainage.swales[{i}]";
                checkId($"{p}.id", sw.Id);
                if (sw.Line.Count < 2) add($"{p}.line", "A swale needs a flow line with at least 2 points.");
                else CheckPolyline($"{p}.line", sw.Line, "swale line", add);
                if (sw.Width is double w) CheckPositive($"{p}.width", w, "Width must be greater than 0 m.", add);
                if (sw.Depth is double depth) CheckPositive($"{p}.depth", depth, "Depth must be greater than 0 m.", add);
                if (sw.OutletId is not null && !outletIds.Contains(sw.OutletId))
                    add($"{p}.outletId", $"Outlet \"{sw.OutletId}\" is not a basin or low point.");
            }

            for (int i = 0; i < intent.Elements.Count; i++)
            {
                var e = intent.Elements[i];
                var p = $"elements[{i}]";
                checkId($"{p}.id", e.Id);
                CheckPoint($"{p}.location", e.Location, add);
                if (e.Outline is not null)
                    CheckPolygon($"{p}.outline", e.Outline, $"{e.Kind.ToString().ToLowerInvariant()} footprint", add);
                else if (e.Kind == ElementKind.Shade || e.Kind == ElementKind.Pergola)
                    add($"{p}.outline", $"A {e.Kind.ToString().ToLowerInvariant()} needs a footprint (its cover counts toward shading).");
                if (e.RotationDegrees is double rot) CheckFinite($"{p}.rotationDegrees", rot, "Rotation", add);
            }
        }

        private static void CheckPositive(string path, double value, string message, Action<string, string> add)
        {
            if (!(value > 0) || double.IsInfinity(value)) add(path, message);
        }

        internal static void CheckFinite(string path, double value, string what, Action<string, string> add)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) add(path, $"{what} must be a finite number.");
        }

        internal static bool CheckPoint(string path, Point2 pt, Action<string, string> add)
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

        internal static void CheckPolyline(string path, List<Point2> points, string what, Action<string, string> add)
        {
            if (!AllFinite(path, points, add)) return;
            for (int i = 1; i < points.Count; i++)
                if (Distance(points[i - 1], points[i]) < MinLength)
                    add($"{path}[{i}]", $"The {what} repeats the same point.");
        }

        internal static void CheckPolygon(string path, List<Point2> points, string what, Action<string, string> add)
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
