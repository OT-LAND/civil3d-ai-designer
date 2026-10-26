using System;
using System.Collections.Generic;
using CivilAI.Core.Brief;

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
    public sealed record MetricTolerances(double HardSoft = 5, double CurvedStraight = 10, double Accessibility = 5)
    {
        public static MetricTolerances Default { get; } = new MetricTolerances();
    }

    public static class TargetComparison
    {
        /// <summary>
        /// Compares measured <paramref name="metrics"/> with the sliders that apply to a zone
        /// (<see cref="DesignBrief.CharacterFor"/>). One row per slider, in palette order.
        /// </summary>
        public static IReadOnlyList<TargetResult> Compare(
            IntentMetrics metrics, ResolvedCharacter character, MetricTolerances? tolerances = null)
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
                NotMeasured("maxGrade", character.MaxGrade, "needs the grading solver (B.6); steepest ground is listed in the metrics"),
                NotMeasured("cutFill", character.CutFill, "needs the grading solver (B.6)"),
                NotMeasured("maxSlope", character.MaxSlope, "needs the grading solver (B.6); steepest not-accessible path is listed in the metrics"),
                NotMeasured("shading", character.Shading, "needs trees and shade elements in the Design Intent (B.3)"),
                NotMeasured("treesElements", character.TreesElements, "needs trees and site elements in the Design Intent (B.3)"),
                Within("curvedStraight", character.CurvedStraight, metrics.StraightSharePercent, tol.CurvedStraight,
                    "straight share of outline and path length", "no outlines or paths"),
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

        private static TargetResult NotMeasured(string slider, int target, string why) =>
            new TargetResult(slider, target, null, 0, TargetStatus.NotMeasured, why);
    }
}
