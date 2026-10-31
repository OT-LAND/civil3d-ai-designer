using System;
using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Grading;
using CivilAI.Core.Intent;
using CivilAI.Core.Rules;

namespace CivilAI.Core.Plan
{
    /// <summary>
    /// B.6d: the grading solvers run on one plan, on the site's existing ground. Path levels (B.6a),
    /// area space planes (B.6b), then cut/fill balanced toward the cut↔fill slider (B.6c) per zone
    /// (or once for the whole design when the brief has no zones). The plan review turns
    /// <see cref="Issues"/> and <see cref="BalanceIssues"/> into open issues for the repair loop and
    /// measures the cut↔fill and max slope rows from <see cref="Earthworks"/> and <see cref="Paths"/>.
    /// </summary>
    public sealed class PlanGrading
    {
        public PathLevelResult Paths { get; init; } = new PathLevelResult();

        /// <summary>Area space planes after the balance shift.</summary>
        public AreaLevelResult Areas { get; init; } = new AreaLevelResult();

        /// <summary>Cut and fill of the balanced levels, every space.</summary>
        public EarthworkResult Earthworks { get; init; } = new EarthworkResult();

        /// <summary>Balance shift per zone id ("" for the whole design or spaces with no zone), metres.</summary>
        public IReadOnlyDictionary<string, double> Shifts { get; init; } = new Dictionary<string, double>();

        /// <summary>Path and area level problems, as the solvers found them.</summary>
        public IReadOnlyList<GradingIssue> Issues { get; init; } = Array.Empty<GradingIssue>();

        /// <summary>Earthworks that could not reach the cut↔fill slider, one line per zone.</summary>
        public IReadOnlyList<string> BalanceIssues { get; init; } = Array.Empty<string>();

        public bool AllPass => Issues.Count == 0 && BalanceIssues.Count == 0;

        /// <summary>
        /// Solves the levels of <paramref name="intent"/> on <paramref name="ground"/>. Each zone is
        /// balanced on its own cut↔fill slider (zone override, else global, D15) with only its own
        /// spaces (by <see cref="Space.ZoneId"/>); spaces with no zone use the global slider.
        /// </summary>
        public static PlanGrading Solve(DesignIntent intent, DesignBrief brief, RuleSet rules, IGroundModel ground,
            EarthworkOptions? options = null)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (brief == null) throw new ArgumentNullException(nameof(brief));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (ground == null) throw new ArgumentNullException(nameof(ground));

            var paths = PathLevelSolver.Solve(intent, brief, rules, ground);
            var areas = AreaLevelSolver.Solve(intent, brief, rules, ground);

            var zones = brief.Zones.ToDictionary(z => z.Id, StringComparer.Ordinal);
            var groups = intent.Spaces
                .GroupBy(s => s.ZoneId is string z && zones.ContainsKey(z) ? z : "", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            var moved = new Dictionary<string, AreaLevel>(StringComparer.Ordinal);
            var earth = new EarthworkResult();
            var shifts = new Dictionary<string, double>(StringComparer.Ordinal);
            var balanceIssues = new List<string>();

            foreach (var group in groups)
            {
                var zone = group.Key.Length > 0 ? zones[group.Key] : null;
                var slider = brief.CharacterFor(zone ?? new Zone()).CutFill;
                var sub = new DesignIntent { Site = intent.Site, Spaces = group.ToList() };
                // Earthworks samples only the spaces of `sub`, so the full results can be passed in.
                var bal = Grading.Earthworks.Balance(sub, areas, paths, ground, slider, options);

                var ids = new HashSet<string>(group.Select(s => s.Id), StringComparer.Ordinal);
                foreach (var a in bal.Areas.Areas.Where(a => ids.Contains(a.SpaceId)))
                    moved[a.SpaceId] = a;
                earth.Spaces.AddRange(bal.Earthworks.Spaces);
                shifts[group.Key] = bal.Shift;
                if (bal.Issue != null)
                    balanceIssues.Add((zone == null ? (brief.Zones.Count == 0 ? "the design" : "spaces with no zone") : Label(zone)) + ": " + bal.Issue);
            }

            var balanced = new AreaLevelResult { Issues = areas.Issues.ToList() };
            foreach (var a in areas.Areas)
                balanced.Areas.Add(moved.TryGetValue(a.SpaceId, out var m) && m != null ? m : a);

            return new PlanGrading
            {
                Paths = paths,
                Areas = balanced,
                Earthworks = earth,
                Shifts = shifts,
                Issues = paths.Issues.Concat(areas.Issues).ToList(),
                BalanceIssues = balanceIssues,
            };
        }

        internal static string Label(Zone zone) =>
            string.IsNullOrWhiteSpace(zone.Name) ? $"zone {zone.Id}" : $"zone {zone.Id} ({zone.Name})";
    }
}
