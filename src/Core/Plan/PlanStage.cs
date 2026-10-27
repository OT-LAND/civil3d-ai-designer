using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Brief;
using CivilAI.Core.Claude;
using CivilAI.Core.Intent;
using CivilAI.Core.Metrics;
using CivilAI.Core.Rules;
using CivilAI.Core.Style;

namespace CivilAI.Core.Plan
{
    /// <summary>What the plan stage works from (CONCEPT.md §3.3, stage 3).</summary>
    public sealed class PlanRequest
    {
        /// <summary>Zones with sliders, tagged curves and points. Should pass <see cref="DesignBriefValidator"/>.</summary>
        public DesignBrief Brief { get; init; } = new DesignBrief();

        /// <summary>Material palette, planting character and form language (B.4). Optional.</summary>
        public StyleProfile? Style { get; init; }

        /// <summary>Lot border, buildings, FFLs and entrances. Kept exactly as given in the result.</summary>
        public Site? Site { get; init; }

        /// <summary>Rule set for the rule report; the default starter set when null.</summary>
        public RuleSet? Rules { get; init; }
    }

    /// <summary>"Target vs achieved" rows for one zone (or the whole design when the brief has no zones).</summary>
    public sealed class ZoneTargets
    {
        /// <summary>Brief zone id; null for the whole design.</summary>
        public string? ZoneId { get; init; }

        public string? ZoneName { get; init; }

        public IReadOnlyList<TargetResult> Targets { get; init; } = Array.Empty<TargetResult>();
    }

    /// <summary>What Core found on one plan: hard problems block it, open issues are repairable misses.</summary>
    public sealed class PlanReview
    {
        /// <summary>Wrong references to the brief; the plan cannot be used until they are fixed.</summary>
        public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

        /// <summary>Rule failures and slider misses, one designer-readable line each.</summary>
        public IReadOnlyList<string> OpenIssues { get; init; } = Array.Empty<string>();

        public RuleReport RuleReport { get; init; } = new RuleReport();

        public IReadOnlyList<ZoneTargets> Zones { get; init; } = Array.Empty<ZoneTargets>();
    }

    /// <summary>One usable plan, with its check results.</summary>
    public sealed class PlanResult
    {
        public DesignIntent Intent { get; init; } = new DesignIntent();

        public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();

        /// <summary>Rule report and target-vs-achieved of the returned plan.</summary>
        public PlanReview Review { get; init; } = new PlanReview();

        /// <summary>True when the plan has no rule failures and no slider misses.</summary>
        public bool AllTargetsMet => Review.OpenIssues.Count == 0;

        /// <summary>Claude calls used.</summary>
        public int Attempts { get; init; }

        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
    }

    /// <summary>Claude did not produce a usable plan within the allowed attempts.</summary>
    public sealed class PlanStageException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public string? LastJson { get; }

        public PlanStageException(string message, IReadOnlyList<string> problems, string? lastJson)
            : base(message)
        {
            Problems = problems;
            LastJson = lastJson;
        }
    }

    /// <summary>
    /// Roadmap B.5a: brief + style profile + site → one Design Intent (spaces, paths, places,
    /// trees). Each answer is read like 1.2 (shape, validator, given site swapped in), then
    /// checked by Core: references to brief zones and interest points, the rule report (2.2)
    /// and target vs achieved per zone (B.2). Anything wrong goes back as a repair prompt,
    /// up to <see cref="MaxAttempts"/> calls. Broken references are hard problems; rule
    /// failures and slider misses are open issues: if they remain after the last attempt the
    /// best usable plan (fewest open issues) is returned with them listed, so the designer
    /// sees them on the target-vs-achieved card instead of getting nothing.
    /// Three variants per run (D18) are B.5b.
    /// </summary>
    public sealed class PlanStage
    {
        public const string ToolName = "plan";
        public const string PromptVersion = "plan.v1";

        private readonly Func<ClaudeRequest, CancellationToken, Task<ClaudeResponse>> _send;

        /// <summary>Total Claude calls allowed, including repairs. Default 3.</summary>
        public int MaxAttempts { get; init; } = 3;

        /// <summary>Output budget per call; a whole plan is larger than a 1.2 intent.</summary>
        public int MaxTokens { get; init; } = 16000;

        public MetricTolerances Tolerances { get; init; } = MetricTolerances.Default;

        public PlanStage(ClaudeClient client)
            : this((client ?? throw new ArgumentNullException(nameof(client))).SendAsync)
        {
        }

        /// <param name="send">Sends one request; replaced in tests.</param>
        public PlanStage(Func<ClaudeRequest, CancellationToken, Task<ClaudeResponse>> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public async Task<PlanResult> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (request.Brief is null) throw new ArgumentException("The plan needs a Design Brief.", nameof(request));
            if (MaxAttempts < 1) throw new InvalidOperationException("MaxAttempts must be at least 1.");

            var rules = request.Rules ?? RuleSetJson.LoadDefault();
            var system = ReadSystemPrompt();
            var output = new JsonOutputSpec(ToolName,
                "Return the plan as a Design Intent, with the assumptions you made.", TextToIntent.BuildOutputSchema());
            var prompt = BuildUserPrompt(request);

            string? lastJson = null;
            IReadOnlyList<string> problems = Array.Empty<string>();
            PlanResult? best = null;
            int inputTokens = 0, outputTokens = 0;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var text = attempt == 1 ? prompt : TextToIntent.BuildRepairPrompt(prompt, lastJson, problems);
                var response = await _send(new ClaudeRequest
                {
                    System = system,
                    Content = new[] { ClaudeContent.FromText(text) },
                    JsonOutput = output,
                    MaxTokens = MaxTokens,
                }, ct).ConfigureAwait(false);
                inputTokens += response.InputTokens;
                outputTokens += response.OutputTokens;

                if (!TextToIntent.TryReadAnswer(response, request.Site, out var intent, out var assumptions, out lastJson, out problems))
                    continue;

                var review = Review(intent!, request.Brief, rules, Tolerances);
                if (review.Problems.Count > 0)
                {
                    problems = review.Problems;
                    continue;
                }

                // Later answers win ties: they were asked to fix the earlier one.
                if (best is null || review.OpenIssues.Count <= best.Review.OpenIssues.Count)
                    best = new PlanResult { Intent = intent!, Assumptions = assumptions, Review = review, Attempts = attempt };

                if (review.OpenIssues.Count == 0) break;
                problems = review.OpenIssues;
            }

            if (best is null)
                throw new PlanStageException(
                    $"Claude did not produce a usable plan after {MaxAttempts} attempt(s). " +
                    "Check the problems listed, or simplify the brief.", problems, lastJson);

            return new PlanResult
            {
                Intent = best.Intent,
                Assumptions = best.Assumptions,
                Review = best.Review,
                Attempts = best.Attempts,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            };
        }

        /// <summary>
        /// Core's check of one valid plan against the brief: references, rule report, and
        /// target vs achieved per zone. Pure; also used by B.8 to show the card.
        /// </summary>
        public static PlanReview Review(DesignIntent intent, DesignBrief brief, RuleSet rules, MetricTolerances? tolerances = null)
        {
            if (intent is null) throw new ArgumentNullException(nameof(intent));
            if (brief is null) throw new ArgumentNullException(nameof(brief));
            if (rules is null) throw new ArgumentNullException(nameof(rules));

            var problems = CheckReferences(intent, brief);
            var open = new List<string>();

            var report = RuleEngine.Check(intent, rules);
            foreach (var f in report.Failures)
                open.Add($"rule {f.RuleId} fails on {f.SpaceId}: {f.Message}");

            var zones = new List<ZoneTargets>();
            if (brief.Zones.Count == 0)
            {
                var targets = TargetComparison.Compare(IntentMetricsCalculator.Measure(intent),
                    brief.CharacterFor(new Zone()), tolerances);
                zones.Add(new ZoneTargets { Targets = targets });
                open.AddRange(Misses("the design", targets));
            }
            else
            {
                foreach (var zone in brief.Zones)
                {
                    var targets = TargetComparison.Compare(IntentMetricsCalculator.Measure(intent, zone.Outline),
                        brief.CharacterFor(zone), tolerances);
                    zones.Add(new ZoneTargets { ZoneId = zone.Id, ZoneName = zone.Name, Targets = targets });
                    open.AddRange(Misses(ZoneLabel(zone), targets));
                }
            }

            return new PlanReview { Problems = problems, OpenIssues = open, RuleReport = report, Zones = zones };
        }

        private static List<string> CheckReferences(DesignIntent intent, DesignBrief brief)
        {
            var problems = new List<string>();
            var zoneIds = new HashSet<string>(brief.Zones.Select(z => z.Id), StringComparer.Ordinal);
            var interest = brief.Points.Where(p => p.Tag == PointTag.InterestPoint).ToList();
            var interestIds = new HashSet<string>(interest.Select(p => p.Id), StringComparer.Ordinal);

            foreach (var s in intent.Spaces)
            {
                if (s.ZoneId is string z && !zoneIds.Contains(z))
                    problems.Add($"intent.spaces[{s.Id}].zoneId: \"{z}\" is not a zone of the brief" +
                        (zoneIds.Count == 0 ? " (the brief has no zones; leave zoneId out)." : $" (zones: {string.Join(", ", zoneIds)})."));
                if (zoneIds.Count > 0 && s.ZoneId is null)
                    problems.Add($"intent.spaces[{s.Id}].zoneId: missing. Set it to the zone the space lies in.");
                if (s.InterestPointId is string p && !interestIds.Contains(p))
                    problems.Add($"intent.spaces[{s.Id}].interestPointId: \"{p}\" is not an interest point of the brief.");
            }

            foreach (var p in interest)
            {
                var places = intent.Spaces.Count(s => s.InterestPointId == p.Id);
                var label = string.IsNullOrWhiteSpace(p.Name) ? p.Id : $"{p.Id} ({p.Name})";
                if (places == 0)
                    problems.Add($"interest point {label}: no place designed for it. Add a space around it with interestPointId \"{p.Id}\".");
                else if (places > 1)
                    problems.Add($"interest point {label}: {places} spaces claim it. Give it exactly one place.");
            }
            return problems;
        }

        private static IEnumerable<string> Misses(string where, IEnumerable<TargetResult> targets)
        {
            foreach (var t in targets.Where(t => t.Status == TargetStatus.Missed && t.Achieved is double))
            {
                var a = t.Achieved!.Value;
                var way = a < t.Target ? "raise" : "lower";
                yield return string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1} target {2}, achieved {3:0} (allowed ±{4:0}); {5} it. Measured as {6}.",
                    where, t.Slider, t.Target, a, t.Tolerance, way, t.Note);
            }
        }

        private static string ZoneLabel(Zone zone) =>
            string.IsNullOrWhiteSpace(zone.Name) ? $"zone {zone.Id}" : $"zone {zone.Id} ({zone.Name})";

        /// <summary>
        /// The first user message: the sliders per zone in plain words, the brief, the style
        /// profile and the site, each as JSON.
        /// </summary>
        public static string BuildUserPrompt(PlanRequest request)
        {
            var brief = request.Brief;
            var sb = new StringBuilder();
            sb.AppendLine("Plan one design for this Design Brief.");
            sb.AppendLine();
            sb.AppendLine("Character sliders that apply (0-100; zone overrides already applied):");
            if (brief.Zones.Count == 0)
                sb.Append("- whole design: ").AppendLine(Sliders(brief.CharacterFor(new Zone())));
            foreach (var zone in brief.Zones)
                sb.Append("- ").Append(ZoneLabel(zone)).Append(": ").AppendLine(Sliders(brief.CharacterFor(zone)));
            sb.AppendLine();

            sb.AppendLine("Design Brief:");
            sb.AppendLine(DesignBriefJson.Serialize(brief));
            sb.AppendLine();

            if (request.Style is null)
            {
                sb.AppendLine("Style profile: none given. Choose plain materials that suit the brief and say so.");
            }
            else
            {
                sb.AppendLine("Style profile:");
                sb.AppendLine(StyleProfileJson.Serialize(request.Style));
            }
            sb.AppendLine();

            if (request.Site is null)
            {
                sb.AppendLine("Site data: none given. Use the zone outlines as the extent, with no buildings, and say so.");
            }
            else
            {
                sb.AppendLine("Site data (surveyed; copy into intent.site unchanged):");
                sb.AppendLine(JsonSerializer.Serialize(request.Site, DesignIntentJson.Options));
            }
            return sb.ToString();
        }

        private static string Sliders(ResolvedCharacter c) => string.Format(CultureInfo.InvariantCulture,
            "accessibility {0}, hardSoft {1}, maxGrade {2}, cutFill {3}, maxSlope {4}, shading {5}, treesElements {6}, curvedStraight {7}",
            c.Accessibility, c.HardSoft, c.MaxGrade, c.CutFill, c.MaxSlope, c.Shading, c.TreesElements, c.CurvedStraight);

        /// <summary>The system prompt (embedded resource, versioned by <see cref="PromptVersion"/>).</summary>
        public static string ReadSystemPrompt()
        {
            const string name = "CivilAI.Core.Plan.Prompts." + PromptVersion + ".md";
            using var stream = typeof(PlanStage).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
