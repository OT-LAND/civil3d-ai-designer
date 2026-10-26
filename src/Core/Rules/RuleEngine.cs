using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Rules
{
    public enum RuleStatus
    {
        Pass,
        Fail,
        /// <summary>The space has no level data for this rule yet, or the rule set leaves the rule out.</summary>
        NotChecked,
    }

    /// <summary>
    /// One line of the constraint report: one rule on one space, with the worst value found
    /// and where it is (for zoom-to-problem in the 2.3 palette).
    /// </summary>
    public sealed class RuleResult
    {
        public string RuleId { get; init; } = "";

        public string SpaceId { get; init; } = "";

        public RuleStatus Status { get; init; }

        /// <summary>Measured value in the rule's unit (percent), when one was measured.</summary>
        public double? Measured { get; init; }

        /// <summary>The rule's limit, when the rule set has it.</summary>
        public double? Limit { get; init; }

        /// <summary>Plan point of the worst place found (zoom target), when known.</summary>
        public Point2? Location { get; init; }

        /// <summary>Designer-facing sentence.</summary>
        public string Message { get; init; } = "";
    }

    public sealed class RuleReport
    {
        public string RuleSetId { get; init; } = "";

        public List<RuleResult> Results { get; init; } = new List<RuleResult>();

        public IEnumerable<RuleResult> Failures => Results.Where(r => r.Status == RuleStatus.Fail);

        public bool AllPass => Results.All(r => r.Status != RuleStatus.Fail);
    }

    /// <summary>
    /// Checks a Design Intent against a rule set (2.2, first slice: slopes and falls of
    /// paths, plazas, lawns and beds). Pure geometry on the intent; no Civil 3D needed.
    /// Every path is part of the accessible route unless the designer marked it
    /// <c>accessible: false</c> (D11); such a path is reported "not checked" for the
    /// accessible-route rules.
    /// </summary>
    public static class RuleEngine
    {
        private const double Eps = 1e-9;

        public static RuleReport Check(DesignIntent intent, RuleSet rules)
        {
            if (intent is null) throw new ArgumentNullException(nameof(intent));
            if (rules is null) throw new ArgumentNullException(nameof(rules));

            var results = new List<RuleResult>();
            foreach (var space in intent.Spaces)
            {
                switch (space.Kind)
                {
                    case SpaceKind.Path:
                        CheckPath(space, rules, results);
                        break;
                    case SpaceKind.Plaza:
                        CheckMin(space, rules, RuleIds.PlazaMinFallPercent, "fall", results);
                        CheckMax(space, rules, RuleIds.PlazaMaxFallPercent, "fall", results);
                        break;
                    case SpaceKind.Lawn:
                        CheckMin(space, rules, RuleIds.LawnMinFallPercent, "fall", results);
                        CheckMax(space, rules, RuleIds.LawnMaxSlopePercent, "slope", results);
                        break;
                    case SpaceKind.Bed:
                        CheckMax(space, rules, RuleIds.BedMaxSlopePercent, "slope", results);
                        break;
                }
            }
            return new RuleReport { RuleSetId = rules.Id, Results = results };
        }

        // ---------- paths ----------

        private static void CheckPath(Space space, RuleSet rules, List<RuleResult> results)
        {
            var line = space.Centerline ?? new List<Point2>();
            var spots = space.Level?.Spots ?? new List<SpotLevel>();
            var fall = space.Level?.Fall;

            // D11: a path marked not accessible skips the accessible-route rules (both path rules are).
            if (space.Accessible == false)
            {
                foreach (var id in new[] { RuleIds.PathMaxLongitudinalSlopePercent, RuleIds.PathMaxCrossFallPercent })
                {
                    var rule = rules.Find(id);
                    results.Add(rule is null
                        ? NotInSet(space, id)
                        : NotChecked(space, rule, "it is marked as not part of the accessible route"));
                }
                return;
            }

            // Longitudinal: from spot levels in order along the centerline; else from the fall.
            var longRule = rules.Find(RuleIds.PathMaxLongitudinalSlopePercent);
            if (longRule is null)
                results.Add(NotInSet(space, RuleIds.PathMaxLongitudinalSlopePercent));
            else if (line.Count >= 2 && spots.Count >= 2)
            {
                var ordered = spots.Select(s => (spot: s, station: Station(line, new Point2(s.X, s.Y))))
                                   .OrderBy(t => t.station).ToList();
                double worst = -1;
                Point2 at = default;
                for (var i = 1; i < ordered.Count; i++)
                {
                    var run = ordered[i].station - ordered[i - 1].station;
                    if (run < Eps) continue;
                    var pct = Math.Abs(ordered[i].spot.Z - ordered[i - 1].spot.Z) / run * 100.0;
                    if (pct > worst)
                    {
                        worst = pct;
                        at = Mid(ordered[i - 1].spot, ordered[i].spot);
                    }
                }
                if (worst < 0)
                    results.Add(NotChecked(space, longRule, "its spot levels are all at the same place along the path"));
                else
                    results.Add(Compare(space, longRule, worst, at, isMax: true, what: "slope along the path"));
            }
            else if (line.Count >= 2 && fall is not null)
            {
                var (along, _, at) = WorstComponents(line, fall);
                results.Add(Compare(space, longRule, along, at, isMax: true, what: "slope along the path"));
            }
            else
                results.Add(NotChecked(space, longRule, "it has no levels along the path yet"));

            // Cross fall: from the fall's component across each centerline segment.
            var crossRule = rules.Find(RuleIds.PathMaxCrossFallPercent);
            if (crossRule is null)
                results.Add(NotInSet(space, RuleIds.PathMaxCrossFallPercent));
            else if (line.Count >= 2 && fall is not null)
            {
                var (_, cross, at) = WorstComponents(line, fall, worstCross: true);
                results.Add(Compare(space, crossRule, cross, at, isMax: true, what: "cross fall"));
            }
            else
                results.Add(NotChecked(space, crossRule, "it has no cross fall set yet"));
        }

        /// <summary>Distance along the polyline to the point's closest position on it.</summary>
        public static double Station(IReadOnlyList<Point2> line, Point2 p)
        {
            double best = double.PositiveInfinity, bestStation = 0, cumulative = 0;
            for (var i = 1; i < line.Count; i++)
            {
                var a = line[i - 1];
                var b = line[i];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                var len2 = dx * dx + dy * dy;
                var len = Math.Sqrt(len2);
                var t = len2 < Eps ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
                double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
                var d2 = cx * cx + cy * cy;
                if (d2 < best - Eps)
                {
                    best = d2;
                    bestStation = cumulative + t * len;
                }
                cumulative += len;
            }
            return bestStation;
        }

        /// <summary>Along/across components of a planar fall over each centerline segment; worst segment wins.</summary>
        private static (double along, double cross, Point2 at) WorstComponents(IReadOnlyList<Point2> line, Fall fall, bool worstCross = false)
        {
            var phi = fall.DirectionDegrees * Math.PI / 180.0;
            double bestAlong = 0, bestCross = 0;
            Point2 at = Mid(line[0], line[1]);
            var bestKey = -1.0;
            for (var i = 1; i < line.Count; i++)
            {
                var a = line[i - 1];
                var b = line[i];
                if (Math.Abs(b.X - a.X) < Eps && Math.Abs(b.Y - a.Y) < Eps) continue;
                var theta = Math.Atan2(b.Y - a.Y, b.X - a.X);
                var along = Math.Abs(fall.Percent * Math.Cos(phi - theta));
                var cross = Math.Abs(fall.Percent * Math.Sin(phi - theta));
                var key = worstCross ? cross : along;
                if (key > bestKey)
                {
                    bestKey = key;
                    bestAlong = along;
                    bestCross = cross;
                    at = Mid(a, b);
                }
            }
            return (bestAlong, bestCross, at);
        }

        // ---------- area spaces ----------

        private static void CheckMin(Space space, RuleSet rules, string ruleId, string what, List<RuleResult> results)
        {
            var rule = rules.Find(ruleId);
            if (rule is null) { results.Add(NotInSet(space, ruleId)); return; }

            // Only a stated fall proves a minimum; spot levels give a lower bound only.
            var fall = space.Level?.Fall;
            if (fall is null)
                results.Add(NotChecked(space, rule, "it has no fall set yet"));
            else
                results.Add(Compare(space, rule, Math.Abs(fall.Percent), Centroid(space), isMax: false, what: what));
        }

        private static void CheckMax(Space space, RuleSet rules, string ruleId, string what, List<RuleResult> results)
        {
            var rule = rules.Find(ruleId);
            if (rule is null) { results.Add(NotInSet(space, ruleId)); return; }

            var fall = space.Level?.Fall;
            var spots = space.Level?.Spots ?? new List<SpotLevel>();
            double worst = -1;
            Point2? at = Centroid(space);
            if (fall is not null) worst = Math.Abs(fall.Percent);

            // Steepest pair of spot levels: the surface is at least this steep somewhere.
            for (var i = 0; i < spots.Count; i++)
                for (var j = i + 1; j < spots.Count; j++)
                {
                    var run = Math.Sqrt(Sq(spots[j].X - spots[i].X) + Sq(spots[j].Y - spots[i].Y));
                    if (run < Eps) continue;
                    var pct = Math.Abs(spots[j].Z - spots[i].Z) / run * 100.0;
                    if (pct > worst)
                    {
                        worst = pct;
                        at = Mid(spots[i], spots[j]);
                    }
                }

            if (worst < 0)
                results.Add(NotChecked(space, rule, "it has no fall or spot levels yet"));
            else
                results.Add(Compare(space, rule, worst, at, isMax: true, what: what));
        }

        // ---------- helpers ----------

        private static RuleResult Compare(Space space, RuleValue rule, double measured, Point2? at, bool isMax, string what)
        {
            var pass = isMax ? measured <= rule.Value + Eps : measured >= rule.Value - Eps;
            var name = SpaceName(space);
            var bound = isMax ? "maximum" : "minimum";
            var message = pass
                ? $"{name}: {what} {Pct(measured)} is within the {bound} of {Pct(rule.Value)}."
                : $"{name}: {what} {Pct(measured)} {(isMax ? "is steeper than" : "is flatter than")} the {bound} of {Pct(rule.Value)}"
                  + (rule.Label is null ? "." : $" ({rule.Label}).");
            return new RuleResult
            {
                RuleId = rule.Id,
                SpaceId = space.Id,
                Status = pass ? RuleStatus.Pass : RuleStatus.Fail,
                Measured = measured,
                Limit = rule.Value,
                Location = at,
                Message = message,
            };
        }

        private static RuleResult NotChecked(Space space, RuleValue rule, string why) => new RuleResult
        {
            RuleId = rule.Id,
            SpaceId = space.Id,
            Status = RuleStatus.NotChecked,
            Limit = rule.Value,
            Location = Centroid(space),
            Message = $"{SpaceName(space)}: not checked, {why}.",
        };

        private static RuleResult NotInSet(Space space, string ruleId) => new RuleResult
        {
            RuleId = ruleId,
            SpaceId = space.Id,
            Status = RuleStatus.NotChecked,
            Location = Centroid(space),
            Message = $"{SpaceName(space)}: not checked, the rule set has no '{ruleId}' rule.",
        };

        private static string SpaceName(Space s) => string.IsNullOrWhiteSpace(s.Name) ? s.Id : $"{s.Name} ({s.Id})";

        private static string Pct(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "%";

        private static Point2? Centroid(Space s)
        {
            var pts = s.Outline is { Count: > 0 } ? s.Outline : s.Centerline;
            if (pts is null || pts.Count == 0) return null;
            return new Point2(pts.Average(p => p.X), pts.Average(p => p.Y));
        }

        private static Point2 Mid(SpotLevel a, SpotLevel b) => new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private static Point2 Mid(Point2 a, Point2 b) => new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private static double Sq(double v) => v * v;
    }
}
