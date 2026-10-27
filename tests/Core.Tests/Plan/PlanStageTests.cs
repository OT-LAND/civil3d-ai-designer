using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Brief;
using CivilAI.Core.Claude;
using CivilAI.Core.Intent;
using CivilAI.Core.Metrics;
using CivilAI.Core.Plan;
using CivilAI.Core.Rules;
using CivilAI.Core.Style;
using Xunit;

namespace CivilAI.Core.Tests.Plan
{
    public class PlanStageTests
    {
        private static string Sample(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "samples" }.Concat(parts).ToArray()));

        /// <summary>The v1 sample intent (laid out for the courtyard brief).</summary>
        private static JsonObject IntentNode() => JsonNode.Parse(Sample("courtyard.v1.json"))!.AsObject();

        private static DesignIntent Intent(JsonObject node)
        {
            Assert.True(DesignIntentJson.TryParse(node.ToJsonString(), out var intent, out var error), error);
            return intent!;
        }

        /// <summary>The sample courtyard brief: its sliders are not met by the sample intent.</summary>
        private static DesignBrief CourtyardBrief() => Brief(JsonNode.Parse(Sample("brief", "courtyard.brief.v1.json"))!.AsObject());

        /// <summary>The same brief with zone sliders set to what the sample intent achieves.</summary>
        private static DesignBrief MetBrief()
        {
            var node = JsonNode.Parse(Sample("brief", "courtyard.brief.v1.json"))!.AsObject();
            var zones = node["zones"]!.AsArray();
            zones[0]!["character"] = new JsonObject { ["hardSoft"] = 0, ["shading"] = 17, ["treesElements"] = 50, ["curvedStraight"] = 100 };
            zones[1]!["character"] = new JsonObject { ["hardSoft"] = 62, ["shading"] = 12, ["treesElements"] = 67, ["curvedStraight"] = 100 };
            return Brief(node);
        }

        private static DesignBrief Brief(JsonObject node)
        {
            Assert.True(DesignBriefJson.TryParse(node.ToJsonString(), out var brief, out var error), error);
            return brief!;
        }

        private static StyleProfile Style()
        {
            Assert.True(StyleProfileJson.TryParse(Sample("style", "courtyard.style.v1.json"), out var style, out var error), error);
            return style!;
        }

        private static string Answer(JsonObject intent, params string[] assumptions) => new JsonObject
        {
            ["assumptions"] = new JsonArray(assumptions.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
            ["intent"] = intent.DeepClone(),
        }.ToJsonString();

        private static ClaudeResponse Reply(string? json, string stop = "tool_use") =>
            new() { Json = json, StopReason = stop, InputTokens = 2000, OutputTokens = 800 };

        private sealed class FakeClaude
        {
            private readonly Queue<ClaudeResponse> _replies;
            public List<ClaudeRequest> Requests { get; } = new();

            public FakeClaude(params ClaudeResponse[] replies) => _replies = new Queue<ClaudeResponse>(replies);

            public Task<ClaudeResponse> Send(ClaudeRequest request, CancellationToken ct)
            {
                Requests.Add(request);
                return Task.FromResult(_replies.Dequeue());
            }

            public string Text(int i) => Requests[i].Content.Single().Text!;
        }

        private static JsonObject Space(JsonObject intent, string id) =>
            intent["spaces"]!.AsArray().Single(s => (string?)s!["id"] == id)!.AsObject();

        [Fact]
        public async Task Plan_that_meets_every_target_is_returned_after_one_call()
        {
            var fake = new FakeClaude(Reply(Answer(IntentNode(), "Gate plaza paved in local stone.")));
            var result = await new PlanStage(fake.Send).GenerateAsync(new PlanRequest { Brief = MetBrief(), Style = Style() });

            Assert.Equal(1, result.Attempts);
            Assert.True(result.AllTargetsMet);
            Assert.Empty(result.Review.Problems);
            Assert.True(result.Review.RuleReport.AllPass);
            Assert.Equal(new[] { "Z1", "Z2" }, result.Review.Zones.Select(z => z.ZoneId));
            Assert.Contains(result.Review.Zones[1].Targets, t => t.Slider == "hardSoft" && t.Status == TargetStatus.Met);
            Assert.Equal(new[] { "Gate plaza paved in local stone." }, result.Assumptions);
            Assert.Equal(2000, result.InputTokens);
        }

        [Fact]
        public async Task Request_uses_plan_prompt_forced_tool_and_lists_sliders_style_and_site()
        {
            var fake = new FakeClaude(Reply(Answer(IntentNode())));
            var site = Intent(IntentNode()).Site;
            await new PlanStage(fake.Send).GenerateAsync(new PlanRequest { Brief = MetBrief(), Style = Style(), Site = site });

            var request = fake.Requests.Single();
            Assert.Contains("plan stage", request.System);
            Assert.Equal(PlanStage.ToolName, request.JsonOutput!.ToolName);
            Assert.Equal(16000, request.MaxTokens);
            var text = fake.Text(0);
            Assert.Contains("zone Z2 (Garden): accessibility 70, hardSoft 62", text);
            Assert.Contains("\"walkingNetwork\"", text);
            Assert.Contains("Style profile:", text);
            Assert.Contains("copy into intent.site unchanged", text);
        }

        [Fact]
        public void Prompt_without_style_or_site_says_so()
        {
            var text = PlanStage.BuildUserPrompt(new PlanRequest { Brief = MetBrief() });
            Assert.Contains("Style profile: none given", text);
            Assert.Contains("Site data: none given", text);
        }

        [Fact]
        public async Task Slider_miss_goes_back_as_repair_and_fixed_plan_is_returned()
        {
            var noBed = IntentNode();
            var spaces = noBed["spaces"]!.AsArray();
            spaces.Remove(spaces.Single(s => (string?)s!["id"] == "bed-garden"));
            var fake = new FakeClaude(Reply(Answer(noBed)), Reply(Answer(IntentNode())));

            var result = await new PlanStage(fake.Send).GenerateAsync(new PlanRequest { Brief = MetBrief() });

            Assert.Equal(2, result.Attempts);
            Assert.True(result.AllTargetsMet);
            var repair = fake.Text(1);
            Assert.Contains("zone Z2 (Garden): hardSoft target 62, achieved 0", repair);
            Assert.Contains("raise it", repair);
            Assert.Contains("Previous intent:", repair);
        }

        [Fact]
        public async Task Misses_left_after_last_attempt_return_the_plan_with_open_issues()
        {
            var fake = new FakeClaude(Reply(Answer(IntentNode())), Reply(Answer(IntentNode())), Reply(Answer(IntentNode())));
            var result = await new PlanStage(fake.Send).GenerateAsync(new PlanRequest { Brief = CourtyardBrief() });

            Assert.Equal(3, fake.Requests.Count);
            Assert.False(result.AllTargetsMet);
            Assert.Equal(3, result.Attempts);
            Assert.Contains(result.Review.OpenIssues, i => i.StartsWith("zone Z1 (Entry court): hardSoft target 30, achieved 0"));
            Assert.Equal(6000, result.InputTokens);
        }

        [Fact]
        public async Task Best_usable_plan_is_kept_when_later_answers_are_broken()
        {
            var fake = new FakeClaude(Reply(Answer(IntentNode())), Reply("{not json"), Reply(null, "max_tokens"));
            var result = await new PlanStage(fake.Send).GenerateAsync(new PlanRequest { Brief = CourtyardBrief() });

            Assert.Equal(1, result.Attempts);
            Assert.NotEmpty(result.Intent.Spaces);
            Assert.Equal(3 * 800, result.OutputTokens);
        }

        [Fact]
        public async Task Unknown_zone_is_a_hard_problem_and_throws_when_never_fixed()
        {
            var bad = IntentNode();
            Space(bad, "bed-garden")["zoneId"] = "Z9";
            var fake = new FakeClaude(Reply(Answer(bad)), Reply(Answer(bad)));

            var ex = await Assert.ThrowsAsync<PlanStageException>(() =>
                new PlanStage(fake.Send) { MaxAttempts = 2 }.GenerateAsync(new PlanRequest { Brief = MetBrief() }));

            Assert.Contains(ex.Problems, p => p.Contains("\"Z9\" is not a zone of the brief"));
            Assert.Contains("\"Z9\" is not a zone of the brief", fake.Text(1));
            Assert.NotNull(ex.LastJson);
        }

        [Fact]
        public void Interest_point_without_a_place_is_a_problem()
        {
            var node = IntentNode();
            Space(node, "plaza-seat").Remove("interestPointId");
            var review = PlanStage.Review(Intent(node), MetBrief(), RuleSetJson.LoadDefault());
            Assert.Contains(review.Problems, p => p.StartsWith("interest point P2 (Seat under the tree): no place"));
        }

        [Fact]
        public void Space_without_zone_is_a_problem_when_the_brief_has_zones()
        {
            var node = IntentNode();
            Space(node, "bed-garden").Remove("zoneId");
            var review = PlanStage.Review(Intent(node), MetBrief(), RuleSetJson.LoadDefault());
            Assert.Contains("intent.spaces[bed-garden].zoneId: missing. Set it to the zone the space lies in.", review.Problems);
        }

        [Fact]
        public void Rule_failure_is_an_open_issue()
        {
            var node = IntentNode();
            Space(node, "plaza-gate")["level"]!["fall"]!["percent"] = 8.0;
            var review = PlanStage.Review(Intent(node), MetBrief(), RuleSetJson.LoadDefault());
            Assert.Empty(review.Problems);
            Assert.Contains(review.OpenIssues, i => i.StartsWith($"rule {RuleIds.PlazaMaxFallPercent} fails on plaza-gate"));
        }

        [Fact]
        public void Brief_without_zones_is_measured_as_one_design()
        {
            var node = JsonNode.Parse(Sample("brief", "courtyard.brief.v1.json"))!.AsObject();
            node["zones"] = new JsonArray();
            var intent = IntentNode();
            foreach (var s in intent["spaces"]!.AsArray()) s!.AsObject().Remove("zoneId");

            var review = PlanStage.Review(Intent(intent), Brief(node), RuleSetJson.LoadDefault());

            Assert.Empty(review.Problems);
            var whole = Assert.Single(review.Zones);
            Assert.Null(whole.ZoneId);
            Assert.All(review.OpenIssues.Where(i => !i.StartsWith("rule ")), i => Assert.StartsWith("the design:", i));
        }
    }
}
