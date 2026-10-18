using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Claude;

namespace CivilAI.Core.Intent
{
    /// <summary>What the designer gives: a free-text brief and, when known, the surveyed site.</summary>
    public sealed class TextToIntentRequest
    {
        /// <summary>The designer's brief, e.g. "a shaded plaza stepping down to a seasonal pond".</summary>
        public string Brief { get; init; } = "";

        /// <summary>Lot border, buildings, FFLs and entrances. Kept exactly as given in the result.</summary>
        public Site? Site { get; init; }
    }

    /// <summary>A validated Design Intent plus the choices Claude made that the brief did not state.</summary>
    public sealed class TextToIntentResult
    {
        public DesignIntent Intent { get; init; } = new DesignIntent();

        /// <summary>One sentence per assumption, for the designer to review before building.</summary>
        public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();

        /// <summary>Claude calls used (1 = first answer was valid).</summary>
        public int Attempts { get; init; }

        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
    }

    /// <summary>Claude could not produce a valid Design Intent within the allowed attempts.</summary>
    public sealed class TextToIntentException : Exception
    {
        /// <summary>Problems with the last answer (shape or content), readable by the designer.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>The last intent JSON Claude returned, if any, for diagnosis.</summary>
        public string? LastJson { get; }

        public TextToIntentException(string message, IReadOnlyList<string> problems, string? lastJson)
            : base(message)
        {
            Problems = problems;
            LastJson = lastJson;
        }
    }

    /// <summary>
    /// Roadmap 1.2: free text (+ site) → validated Design Intent.
    /// Sends the brief with a fixed system prompt and the Design Intent schema as forced tool output,
    /// checks the answer with <see cref="DesignIntentJson.TryParse"/> and
    /// <see cref="DesignIntentValidator"/>, and on failure asks Claude once more with the list of
    /// problems (up to <see cref="MaxAttempts"/> calls). The given site always replaces whatever
    /// site Claude returns, so background data can never be changed by the AI.
    /// </summary>
    public sealed class TextToIntent
    {
        public const string ToolName = "design_intent";
        public const string PromptVersion = "text-to-intent.v0";

        private readonly Func<ClaudeRequest, CancellationToken, Task<ClaudeResponse>> _send;

        /// <summary>Total Claude calls allowed, including repairs. Default 3.</summary>
        public int MaxAttempts { get; init; } = 3;

        public TextToIntent(ClaudeClient client)
            : this((client ?? throw new ArgumentNullException(nameof(client))).SendAsync)
        {
        }

        /// <param name="send">Sends one request; replaced in tests.</param>
        public TextToIntent(Func<ClaudeRequest, CancellationToken, Task<ClaudeResponse>> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public async Task<TextToIntentResult> GenerateAsync(TextToIntentRequest request, CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Brief))
                throw new ArgumentException("The brief is empty. Describe the design you want.", nameof(request));
            if (MaxAttempts < 1) throw new InvalidOperationException("MaxAttempts must be at least 1.");

            var system = ReadSystemPrompt();
            var output = new JsonOutputSpec(ToolName,
                "Return the Design Intent for the brief, with the assumptions you made.", BuildOutputSchema());
            var brief = BuildUserPrompt(request);

            string? lastJson = null;
            IReadOnlyList<string> problems = Array.Empty<string>();
            int inputTokens = 0, outputTokens = 0;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var text = attempt == 1 ? brief : BuildRepairPrompt(brief, lastJson, problems);
                var response = await _send(new ClaudeRequest
                {
                    System = system,
                    Content = new[] { ClaudeContent.FromText(text) },
                    JsonOutput = output,
                }, ct).ConfigureAwait(false);
                inputTokens += response.InputTokens;
                outputTokens += response.OutputTokens;

                if (TryReadAnswer(response, request.Site, out var intent, out var assumptions, out lastJson, out problems))
                    return new TextToIntentResult
                    {
                        Intent = intent!,
                        Assumptions = assumptions,
                        Attempts = attempt,
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                    };
            }

            throw new TextToIntentException(
                $"Claude did not produce a valid Design Intent after {MaxAttempts} attempt(s). " +
                "Try a more specific brief, or check the problems listed.", problems, lastJson);
        }

        /// <summary>
        /// Checks one answer. On success returns the intent (with the given site, if any) and the
        /// assumptions; otherwise the problems to send back to Claude.
        /// </summary>
        internal static bool TryReadAnswer(ClaudeResponse response, Site? givenSite,
            out DesignIntent? intent, out IReadOnlyList<string> assumptions,
            out string? intentJson, out IReadOnlyList<string> problems)
        {
            intent = null;
            intentJson = null;
            assumptions = Array.Empty<string>();

            if (response.WasTruncated)
            {
                problems = new[] { "The answer was cut off (too long). Keep outlines to the points needed and return the whole intent." };
                return false;
            }
            if (string.IsNullOrWhiteSpace(response.Json))
            {
                problems = new[] { $"No call to the {ToolName} tool. Answer only by calling it." };
                return false;
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(response.Json!) as JsonObject
                    ?? throw new JsonException("The tool input is not a JSON object.");
            }
            catch (JsonException ex)
            {
                problems = new[] { $"The tool input is not valid JSON: {ex.Message}" };
                return false;
            }

            var list = new List<string>();
            if (root["assumptions"] is JsonArray items)
                foreach (var item in items)
                    if (item is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                        list.Add(s.Trim());
            assumptions = list;

            if (root["intent"] is not JsonObject intentNode)
            {
                problems = new[] { "\"intent\": missing. Put the Design Intent object in \"intent\"." };
                return false;
            }
            intentJson = intentNode.ToJsonString();

            if (!DesignIntentJson.TryParse(intentJson, out var parsed, out var shapeError))
            {
                problems = new[] { $"intent: {shapeError}" };
                return false;
            }

            if (givenSite is not null)
                parsed = WithSite(parsed!, givenSite);

            var issues = DesignIntentValidator.Validate(parsed!);
            if (issues.Count > 0)
            {
                problems = issues.Select(i => $"intent.{i}").ToList();
                return false;
            }

            intent = parsed;
            problems = Array.Empty<string>();
            return true;
        }

        private static DesignIntent WithSite(DesignIntent intent, Site site) => new DesignIntent
        {
            SchemaVersion = intent.SchemaVersion,
            Site = site,
            Spaces = intent.Spaces,
            Edges = intent.Edges,
            Materials = intent.Materials,
        };

        /// <summary>The first user message: brief, then the site data as JSON (or a note that there is none).</summary>
        public static string BuildUserPrompt(TextToIntentRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Design brief from the landscape architect:");
            sb.AppendLine(request.Brief.Trim());
            sb.AppendLine();
            if (request.Site is null)
            {
                sb.AppendLine("Site data: none given.");
            }
            else
            {
                sb.AppendLine("Site data (surveyed; copy into intent.site unchanged):");
                sb.AppendLine(JsonSerializer.Serialize(request.Site, DesignIntentJson.Options));
            }
            return sb.ToString();
        }

        /// <summary>A follow-up message: the original brief, the previous answer, and what was wrong with it.</summary>
        public static string BuildRepairPrompt(string userPrompt, string? previousJson, IReadOnlyList<string> problems)
        {
            var sb = new StringBuilder(userPrompt);
            sb.AppendLine();
            sb.AppendLine("Your previous Design Intent could not be used:");
            foreach (var p in problems) sb.Append("- ").AppendLine(p);
            if (!string.IsNullOrEmpty(previousJson))
            {
                sb.AppendLine();
                sb.AppendLine("Previous intent:");
                sb.AppendLine(previousJson);
            }
            sb.AppendLine();
            sb.AppendLine("Fix every problem listed and return the complete corrected answer by calling the tool again.");
            return sb.ToString();
        }

        /// <summary>
        /// Tool input schema: { assumptions: string[], intent: Design Intent v0 }. The intent schema's
        /// $defs move to the root so its "#/$defs/..." references still resolve.
        /// </summary>
        public static string BuildOutputSchema()
        {
            var intent = JsonNode.Parse(DesignIntentJson.ReadSchema())!.AsObject();
            var defs = intent["$defs"]?.DeepClone();
            foreach (var key in new[] { "$schema", "$id", "$defs", "title" })
                intent.Remove(key);

            var root = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("assumptions", "intent"),
                ["properties"] = new JsonObject
                {
                    ["assumptions"] = new JsonObject
                    {
                        ["description"] = "Choices made that the brief did not state, one short sentence each.",
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                    },
                    ["intent"] = intent,
                },
            };
            if (defs is not null) root["$defs"] = defs;
            return root.ToJsonString();
        }

        /// <summary>The system prompt (embedded resource, versioned by <see cref="PromptVersion"/>).</summary>
        public static string ReadSystemPrompt()
        {
            const string name = "CivilAI.Core.Intent.Prompts." + PromptVersion + ".md";
            using var stream = typeof(TextToIntent).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
