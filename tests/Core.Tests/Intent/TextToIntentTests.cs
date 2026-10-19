using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Claude;
using CivilAI.Core.Intent;
using Xunit;

namespace CivilAI.Core.Tests.Intent
{
    public class TextToIntentTests
    {
        private static string Courtyard() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v0.json"));

        private static Site CourtyardSite()
        {
            Assert.True(DesignIntentJson.TryParse(Courtyard(), out var intent, out var error), error);
            return intent!.Site;
        }

        private static string Answer(string intentJson, params string[] assumptions)
        {
            var root = new JsonObject
            {
                ["assumptions"] = new JsonArray(assumptions.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
                ["intent"] = JsonNode.Parse(intentJson),
            };
            return root.ToJsonString();
        }

        private static ClaudeResponse Reply(string? json, string stop = "tool_use") =>
            new() { Json = json, StopReason = stop, InputTokens = 1000, OutputTokens = 400 };

        /// <summary>Returns queued replies in order and records each request.</summary>
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

        private static TextToIntentRequest Brief(Site? site = null) =>
            new() { Brief = "A paved courtyard with a lawn and an accessible path from the street.", Site = site };

        [Fact]
        public async Task Valid_first_answer_returns_intent_and_assumptions()
        {
            var fake = new FakeClaude(Reply(Answer(Courtyard(), "Plaza fall 2% away from the building.")));
            var result = await new TextToIntent(fake.Send).GenerateAsync(Brief());

            Assert.Equal(1, result.Attempts);
            Assert.Equal(new[] { "Plaza fall 2% away from the building." }, result.Assumptions);
            Assert.NotEmpty(result.Intent.Spaces);
            Assert.Empty(DesignIntentValidator.Validate(result.Intent));
            Assert.Equal(1000, result.InputTokens);
            Assert.Equal(400, result.OutputTokens);
        }

        [Fact]
        public async Task Request_uses_system_prompt_and_forced_tool_with_wrapped_schema()
        {
            var fake = new FakeClaude(Reply(Answer(Courtyard())));
            await new TextToIntent(fake.Send).GenerateAsync(Brief());

            var request = fake.Requests.Single();
            Assert.Contains("landscape architects", request.System);
            Assert.Equal(TextToIntent.ToolName, request.JsonOutput!.ToolName);

            var body = ClaudeClient.BuildBody(request, new ClaudeSettings { ApiKey = "k" });
            Assert.Equal(TextToIntent.ToolName, body["tool_choice"]!["name"]!.GetValue<string>());
            var schema = body["tools"]![0]!["input_schema"]!.AsObject();
            Assert.Equal("object", schema["properties"]!["intent"]!["type"]!.GetValue<string>());
            Assert.NotNull(schema["$defs"]!["space"]);
        }

        [Fact]
        public void Output_schema_keeps_every_reference_resolvable()
        {
            var schema = TextToIntent.BuildOutputSchema();
            var defs = JsonNode.Parse(schema)!["$defs"]!.AsObject();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(schema, "\"#/\\$defs/([A-Za-z0-9_]+)\""))
                Assert.True(defs.ContainsKey(m.Groups[1].Value), $"Unresolved $ref {m.Value}");
            Assert.DoesNotContain("\"$id\"", schema);
        }

        [Fact]
        public void User_prompt_contains_brief_and_site_json()
        {
            var withSite = TextToIntent.BuildUserPrompt(Brief(CourtyardSite()));
            Assert.Contains("accessible path from the street", withSite);
            Assert.Contains("\"ffl\"", withSite);
            Assert.Contains("unchanged", withSite);

            var noSite = TextToIntent.BuildUserPrompt(Brief());
            Assert.Contains("Site data: none given.", noSite);
        }

        [Fact]
        public async Task Shape_error_is_sent_back_and_second_answer_accepted()
        {
            var bad = JsonNode.Parse(Courtyard())!.AsObject();
            bad["spaces"]![0]!["kind"] = "fountain";
            var fake = new FakeClaude(Reply(Answer(bad.ToJsonString())), Reply(Answer(Courtyard())));

            var result = await new TextToIntent(fake.Send).GenerateAsync(Brief());

            Assert.Equal(2, result.Attempts);
            Assert.Equal(2000, result.InputTokens);
            var repair = fake.Text(1);
            Assert.Contains("could not be used", repair);
            Assert.Contains("fountain", repair);
            Assert.Contains("accessible path from the street", repair);
        }

        [Fact]
        public async Task Content_issue_is_listed_in_repair_prompt()
        {
            var bad = JsonNode.Parse(Courtyard())!.AsObject();
            var firstId = bad["spaces"]![0]!["id"]!.GetValue<string>();
            bad["spaces"]![1]!["id"] = firstId; // duplicate id
            var fake = new FakeClaude(Reply(Answer(bad.ToJsonString())), Reply(Answer(Courtyard())));

            await new TextToIntent(fake.Send).GenerateAsync(Brief());

            Assert.Contains("- intent.", fake.Text(1));
            Assert.Contains(firstId, fake.Text(1));
        }

        [Fact]
        public async Task Given_site_replaces_site_from_claude()
        {
            var site = CourtyardSite();
            var changed = JsonNode.Parse(Courtyard())!.AsObject();
            changed["site"]!["buildings"]![0]!["ffl"] = 99.0;
            var fake = new FakeClaude(Reply(Answer(changed.ToJsonString())));

            var result = await new TextToIntent(fake.Send).GenerateAsync(Brief(site));

            Assert.Same(site, result.Intent.Site);
            Assert.NotEqual(99.0, result.Intent.Site.Buildings[0].Ffl);
        }

        [Fact]
        public async Task Truncated_or_missing_tool_answer_triggers_repair()
        {
            var fake = new FakeClaude(Reply(null, "end_turn"), Reply("{\"intent\":{}}", "max_tokens"), Reply(Answer(Courtyard())));
            var result = await new TextToIntent(fake.Send).GenerateAsync(Brief());

            Assert.Equal(3, result.Attempts);
            Assert.Contains("No call to the design_intent tool", fake.Text(1));
            Assert.Contains("cut off", fake.Text(2));
        }

        [Fact]
        public async Task Missing_intent_property_is_reported()
        {
            var fake = new FakeClaude(Reply("{\"assumptions\":[]}"), Reply(Answer(Courtyard())));
            await new TextToIntent(fake.Send).GenerateAsync(Brief());
            Assert.Contains("\"intent\": missing", fake.Text(1));
        }

        [Fact]
        public async Task Gives_up_after_max_attempts_with_problems_and_last_json()
        {
            var bad = JsonNode.Parse(Courtyard())!.AsObject();
            bad["spaces"]![0]!["kind"] = "fountain";
            var badAnswer = Answer(bad.ToJsonString());
            var fake = new FakeClaude(Reply(badAnswer), Reply(badAnswer));

            var ex = await Assert.ThrowsAsync<TextToIntentException>(
                () => new TextToIntent(fake.Send) { MaxAttempts = 2 }.GenerateAsync(Brief()));

            Assert.Equal(2, fake.Requests.Count);
            Assert.Contains(ex.Problems, p => p.Contains("spaces[0].kind"));
            Assert.Contains("fountain", ex.LastJson);
        }

        [Fact]
        public async Task Empty_brief_is_rejected_without_calling_claude()
        {
            var fake = new FakeClaude();
            await Assert.ThrowsAsync<ArgumentException>(
                () => new TextToIntent(fake.Send).GenerateAsync(new TextToIntentRequest { Brief = "  " }));
            Assert.Empty(fake.Requests);
        }

        [Fact]
        public async Task Blank_assumptions_are_dropped()
        {
            var fake = new FakeClaude(Reply(Answer(Courtyard(), " ", "Lawn fall 2.5%. ")));
            var result = await new TextToIntent(fake.Send).GenerateAsync(Brief());
            Assert.Equal(new[] { "Lawn fall 2.5%." }, result.Assumptions);
        }
    }
}
