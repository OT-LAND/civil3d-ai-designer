using System;
using System.Collections.Generic;
using CivilAI.Core.Brief;
using CivilAI.Core.Grading;

namespace CivilAI.Core.Metrics
{
    public enum TargetStatus
    {
        /// <summary>Achieved within tolerance of the slider target.</summary>
        Met,
        /// <summary>Measured, but outside tolerance.</summary>
        Missed,
        /// <summary>Cannot be measured on this Design Intent yet (reason in the note).</summary>
        NotMeasured,
    }

    /// <summary>One row of the "target vs achieved" card, in slider units (0–100).</summary>
    public sealed record TargetResult(
        string Slider,
        int Target,
        double? Achieved,
        double Tolerance,
        TargetStatus Status,
        string Note);

    /// <summary>
    /// How close a metric must be to its slider target, in percentage points.
    /// Hard/soft ±5 comes from CONCEPT.md §2; the others are starting values, easy to change.
    /// </summary>
    public sealed record MetricTolerances(
        double HardSoft = 5, double CurvedStraight = 10, double Accessibility = 5,
        double Shading = 10, double TreesElements = 10, double Budget = 15,
        double CutFill = 10, double MaxSlope = 5)
    {
        public static MetricTolerances Default { get; } = new MetricTolerances();
    }

    public static class TargetComparison
    {
        /// <summary>
        /// Compares measured <paramref name="metrics"/> with the sliders that apply to a zone
        /// (<see cref="DesignBrief.CharacterFor"/>). One row per slider, in palette order.
        /// <paramref name="grading"/> (B.6c) measures the cut↔fill and max slope rows; without it they are "not measured".
        /// </summary>
        public static IReadOnlyList<TargetResult> Compare(
            IntentMetrics metrics, ResolvedCharacter character, MetricTolerances? tolerances = null,
            GradingMetrics? grading = null)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            var tol = tolerances ?? MetricTolerances.Default;

            return new List<TargetResult>
            {
                AtLeast("accessibility", character.Accessibility, metrics.AccessibleSharePercent, tol.Accessibility,
                    "accessible share of path length; the legal minimum (D10/D11) is checked by the rule report",
                    "no paths"),
                Within("hardSoft", character.HardSoft, metrics.SoftSharePercent, tol.HardSoft,
                    "planted share of paved + planted area", "no paved or planted area"),
                NotMeasured("maxGrade", character.MaxGrade, "needs walls and terraces from the grading solver (B.6e, D23); steepest ground is listed in the metrics"),
                grading == null
                    ? NotMeasured("cutFill", character.CutFill, "needs the grading solver levels (B.6)")
                    : Within("cutFill", character.CutFill, grading.FillSharePercent, tol.CutFill,
                        "fill share of moved soil: 0 all cut (export), 50 balanced, 100 all fill (import)", "no earthworks measured"),
                grading == null
                    ? NotMeasured("maxSlope", character.MaxSlope, "needs the grading solver levels (B.6); steepest not-accessible path is listed in the metrics")
                    : AtMost("maxSlope", character.MaxSlope, grading.MaxSlopeSlider, tol.MaxSlope,
                        "steepest not-accessible path: 0 = accessible limit, 100 = rule-set maximum (D19/D22)",
                        "no path marked not accessible has levels"),
                Within("shading", character.Shading, metrics.CanopyCoverPercent, tol.Shading,
                    "canopy cover at maturity (trees + shade/pergola footprints) over open ground", "no zone or lot outline"),
                Within("treesElements", character.TreesElements, metrics.ElementsSharePercent, tol.TreesElements,
                    "site elements share of new trees + site elements, by count", "no new trees or site elements"),
                Within("curvedStraight", character.CurvedStraight, metrics.StraightSharePercent, tol.CurvedStraight,
                    "straight share of outline and path length", "no outlines or paths"),
                Within("budget", character.Budget, metrics.CostLevelPercent, tol.Budget,
                    "cost level of materials (by area), new trees and site elements (low 0, mid 50, high 100)",
                    "no material, new tree or site element has a cost class"),
            };
        }

        private static TargetResult Within(string slider, int target, double? achieved, double tolerance, string what, string empty)
        {
            if (achieved is not double a) return new TargetResult(slider, target, null, tolerance, TargetStatus.NotMeasured, empty);
            var status = Math.Abs(a - target) <= tolerance + 1e-9 ? TargetStatus.Met : TargetStatus.Missed;
            return new TargetResult(slider, target, a, tolerance, status, what);
        }

        /// <summary>A target where more is fine (more step-free than asked is never a miss).</summary>
        private static TargetResult AtLeast(string slider, int target, double? achieved, double tolerance, string what, string empty)
        {
            if (achieved is not double a) return new TargetResult(slider, target, null, tolerance, TargetStatus.NotMeasured, empty);
            var status = a >= target - tolerance - 1e-9 ? TargetStatus.Met : TargetStatus.Missed;
            return new TargetResult(slider, target, a, tolerance, status, what);
        }

        /// <summary>A target where less is fine (gentler than the slider allows is never a miss).</summary>
        private static TargetResult AtMost(string slider, int target, double? achieved, double tolerance, string what, string empty)
        {
            if (achieved is not double a) return new TargetResult(slider, target, null, tolerance, TargetStatus.NotMeasured, empty);
            var status = a <= target + tolerance + 1e-9 ? TargetStatus.Met : TargetStatus.Missed;
            return new TargetResult(slider, target, a, tolerance, status, what);
        }

        private static TargetResult NotMeasured(string slider, int target, string why) =>
            new TargetResult(slider, target, null, 0, TargetStatus.NotMeasured, why);
    }
}
