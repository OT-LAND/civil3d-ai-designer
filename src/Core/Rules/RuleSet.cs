using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivilAI.Core.Rules
{
    // Design rule sets (roadmap 2.2, decision D3).
    //
    // Rules live in data files, never in code: the plugin ships a default starter set
    // (Rules/RuleSets/default-il.v0.json, embedded) and the designer can pick another file
    // per drawing/project. Each rule is a numeric limit with a source note. A rule missing
    // from a set means "not checked", so an office set can leave rules out on purpose.

    /// <summary>The rule ids Core knows how to check. Rule-set files may only use these.</summary>
    public static class RuleIds
    {
        public const string PathMaxLongitudinalSlopePercent = "path.maxLongitudinalSlopePercent";
        public const string PathMaxCrossFallPercent = "path.maxCrossFallPercent";
        public const string PlazaMinFallPercent = "plaza.minFallPercent";
        public const string PlazaMaxFallPercent = "plaza.maxFallPercent";
        public const string LawnMinFallPercent = "lawn.minFallPercent";
        public const string LawnMaxSlopePercent = "lawn.maxSlopePercent";
        public const string BedMaxSlopePercent = "bed.maxSlopePercent";
        /// <summary>Steepest walking slope on a path marked not accessible, at max slope slider 100 (D19, B.6a).</summary>
        public const string PathNotAccessibleMaxSlopePercent = "path.notAccessibleMaxSlopePercent";

        public static readonly IReadOnlyList<string> All = new[]
        {
            PathMaxLongitudinalSlopePercent, PathMaxCrossFallPercent,
            PlazaMinFallPercent, PlazaMaxFallPercent,
            LawnMinFallPercent, LawnMaxSlopePercent,
            BedMaxSlopePercent,
            PathNotAccessibleMaxSlopePercent,
        };
    }

    /// <summary>One design rule: a numeric limit with where it comes from.</summary>
    public sealed class RuleValue
    {
        public string Id { get; init; } = "";

        public double Value { get; init; }

        /// <summary>Designer-facing description ("Lawn: maximum slope for mowing").</summary>
        public string? Label { get; init; }

        /// <summary>Standard or reference the value comes from.</summary>
        public string? Source { get; init; }

        /// <summary>False until the value has been checked against the published source.</summary>
        public bool Verified { get; init; }
    }

    /// <summary>A selectable set of design rules (one file).</summary>
    public sealed class RuleSet
    {
        public const string CurrentVersion = "0";

        public string RuleSetVersion { get; init; } = CurrentVersion;

        public string Id { get; init; } = "";

        public string Name { get; init; } = "";

        public string? Description { get; init; }

        public List<RuleValue> Rules { get; init; } = new List<RuleValue>();

        /// <summary>The rule with this id, or null when the set leaves it out.</summary>
        public RuleValue? Find(string id) => Rules.FirstOrDefault(r => r.Id == id);
    }

    /// <summary>Reads rule-set files and the embedded default set.</summary>
    public static class RuleSetJson
    {
        public const string DefaultResourceName = "CivilAI.Core.Rules.RuleSets.default-il.v0.json";

        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        /// <summary>
        /// Parses a rule-set file. Returns false with readable errors for bad JSON, an
        /// unsupported version, unknown or duplicate rule ids, or non-finite / negative values.
        /// </summary>
        public static bool TryParse(string json, out RuleSet? ruleSet, out IReadOnlyList<string> errors)
        {
            ruleSet = null;
            var problems = new List<string>();
            errors = problems;

            RuleSet? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<RuleSet>(json, Options);
            }
            catch (JsonException ex)
            {
                problems.Add(ex.Path is null ? ex.Message : $"{ex.Path}: {ex.Message}");
                return false;
            }

            if (parsed is null)
            {
                problems.Add("The rule set is empty (null).");
                return false;
            }

            if (parsed.RuleSetVersion != RuleSet.CurrentVersion)
                problems.Add($"ruleSetVersion '{parsed.RuleSetVersion}' is not supported (expected '{RuleSet.CurrentVersion}').");
            if (string.IsNullOrWhiteSpace(parsed.Id))
                problems.Add("id is required.");
            if (string.IsNullOrWhiteSpace(parsed.Name))
                problems.Add("name is required.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < parsed.Rules.Count; i++)
            {
                var rule = parsed.Rules[i];
                var where = $"rules[{i}]";
                if (!RuleIds.All.Contains(rule.Id))
                    problems.Add($"{where}: unknown rule id '{rule.Id}'. Known ids: {string.Join(", ", RuleIds.All)}.");
                else if (!seen.Add(rule.Id))
                    problems.Add($"{where}: rule '{rule.Id}' appears more than once.");
                if (double.IsNaN(rule.Value) || double.IsInfinity(rule.Value) || rule.Value < 0)
                    problems.Add($"{where}: value must be a finite number, zero or more.");
            }

            if (problems.Count > 0) return false;
            ruleSet = parsed;
            return true;
        }

        /// <summary>Parses or throws <see cref="FormatException"/> listing every problem.</summary>
        public static RuleSet Parse(string json)
        {
            if (TryParse(json, out var set, out var errors)) return set!;
            throw new FormatException("Invalid rule set: " + string.Join(" ", errors));
        }

        /// <summary>The default starter rule set shipped with the plugin (D3).</summary>
        public static RuleSet LoadDefault()
        {
            using var stream = typeof(RuleSetJson).Assembly.GetManifestResourceStream(DefaultResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{DefaultResourceName}' not found.");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
    }
}
