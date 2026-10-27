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
using CivilAI.Core.Style;
using Xunit;

namespace CivilAI.Core.Tests.Style
{
    public class StyleReaderTests
    {
        private static string Sample() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "style", "courtyard.style.v1.json"));

        private static ClaudeResponse Reply(string? json, string stop = "tool_use") =>
            new() { Json = json, StopReason = stop, InputTokens = 2000, OutputTokens = 300 };

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
        }

        private static StyleImage Image(string name, byte seed) =>
            new() { Name = name, Data = Enumerable.Range(0, 64).Select(i => (byte)(i + seed)).ToArray(), MediaType = "image/jpeg" };

        private static StyleRequest TwoImages(string prompt = "shaded Mediterranean courtyard, local stone, dry planting") =>
            new() { Prompt = prompt, Images = new[] { Image("ref1.jpg", 1), Image("ref2.jpg", 2) } };

        private static string TempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "civilai-style-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Sample_profile_parses_validates_and_round_trips()
        {
            Assert.True(StyleProfileJson.TryParse(Sample(), out var profile, out var error), error);
            Assert.Empty(StyleProfileValidator.Validate(profile!, imageCount: 2));
            Assert.Equal(WaterUse.Low, profile!.Planting.WaterUse);
            Assert.Equal(FormGeometry.Orthogonal, profile.Form.Geometry);

            Assert.True(StyleProfileJson.TryParse(StyleProfileJson.Serialize(profile), out var again, out error), error);
            Assert.Equal(StyleProfileJson.Serialize(profile), StyleProfileJson.Serialize(again!));
        }

        [Fact]
        public void Palette_becomes_design_intent_materials()
        {
            StyleProfileJson.TryParse(Sample(), out var profile, out _);
            var materials = profile!.ToIntentMaterials();
            Assert.Equal(4, materials.Count);
            Assert.Equal("stone-paving", materials[0].Id);
            Assert.Equal(MaterialCategory.Paving, materials[0].Category);
            Assert.Equal(MaterialCategory.Mulch, materials[3].Category);
        }

        [Fact]
        public void Validator_reports_content_problems()
        {
            var profile = new StyleProfile
            {
                Summary = " ",
                Materials = new List<StyleMaterial>
                {
                    new() { Id = "a", Name = "stone", Category = MaterialCategory.Paving },
                    new() { Id = "a", Name = "", Category = MaterialCategory.Gravel },
                },
                Planting = new PlantingCharacter { Layers = new List<PlantLayer>() },
                Form = new FormLanguage { CurvedShare = 120 },
                ImageNotes = new List<string> { "1", "2" },
            };
            var paths = StyleProfileValidator.Validate(profile, imageCount: 1).Select(i => i.Path).ToList();
            Assert.Contains("summary", paths);
            Assert.Contains("materials[1].id", paths);
            Assert.Contains("materials[1].name", paths);
            Assert.Contains("planting.layers", paths);
            Assert.Contains("form.curvedShare", paths);
            Assert.Contains("imageNotes", paths);
        }

        [Fact]
        public async Task Sends_labelled_images_then_prompt_with_forced_schema()
        {
            var fake = new FakeClaude(Reply(Sample()));
            var result = await new StyleReader(fake.Send).ReadAsync(TwoImages());

            Assert.Equal(1, result.Attempts);
            Assert.False(result.FromCache);
            Assert.Equal("stone-paving", result.Profile.Materials[0].Id);

            var request = fake.Requests.Single();
            Assert.Contains("landscape architects", request.System);
            Assert.Equal(StyleReader.ToolName, request.JsonOutput!.ToolName);
            var schema = JsonNode.Parse(request.JsonOutput.SchemaJson)!;
            Assert.NotNull(schema["properties"]!["planting"]);

            var c = request.Content;
            Assert.Equal(5, c.Count);
            Assert.Equal("Reference image 1: ref1.jpg", c[0].Text);
            Assert.True(c[1].IsImage);
            Assert.Equal("Reference image 2: ref2.jpg", c[2].Text);
            Assert.True(c[3].IsImage);
            Assert.Contains("local stone, dry planting", c[4].Text);
        }

        [Fact]
        public async Task Invalid_answer_is_repaired_once()
        {
            var bad = JsonNode.Parse(Sample())!;
            bad["materials"] = new JsonArray();
            var fake = new FakeClaude(Reply(bad.ToJsonString()), Reply(Sample()));

            var result = await new StyleReader(fake.Send).ReadAsync(TwoImages());

            Assert.Equal(2, result.Attempts);
            Assert.Equal(4000, result.InputTokens);
            var repair = fake.Requests[1].Content.Last().Text!;
            Assert.Contains("could not be used", repair);
            Assert.Contains("materials: Give at least one material.", repair);
            Assert.Equal(5, fake.Requests[1].Content.Count);
        }

        [Fact]
        public async Task Gives_up_after_max_attempts_with_problems()
        {
            var fake = new FakeClaude(Reply(null, "end_turn"), Reply("{\"schemaVersion\":\"1\",\"colour\":1}"));
            var ex = await Assert.ThrowsAsync<StyleReadException>(() => new StyleReader(fake.Send).ReadAsync(TwoImages()));
            Assert.Equal(2, fake.Requests.Count);
            Assert.NotEmpty(ex.Problems);
        }

        [Fact]
        public async Task Empty_or_too_many_inputs_are_refused_without_a_call()
        {
            var fake = new FakeClaude();
            var reader = new StyleReader(fake.Send);
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(new StyleRequest { Prompt = " " }));
            var seven = Enumerable.Range(0, 7).Select(i => Image($"r{i}.jpg", (byte)i)).ToArray();
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(new StyleRequest { Images = seven }));
            Assert.Empty(fake.Requests);
        }

        [Fact]
        public void Cache_key_depends_on_prompt_and_image_bytes_only()
        {
            var a = StyleReader.CacheKey(TwoImages());
            Assert.Equal(a, StyleReader.CacheKey(TwoImages()));
            Assert.Equal(a, StyleReader.CacheKey(TwoImages("  shaded Mediterranean courtyard, local stone, dry planting ")));
            Assert.NotEqual(a, StyleReader.CacheKey(TwoImages("lush tropical garden")));

            var renamed = new StyleRequest { Prompt = TwoImages().Prompt, Images = new[] { Image("x.jpg", 1), Image("y.jpg", 2) } };
            Assert.Equal(a, StyleReader.CacheKey(renamed));
            var swapped = new StyleRequest { Prompt = TwoImages().Prompt, Images = new[] { Image("ref2.jpg", 2), Image("ref1.jpg", 1) } };
            Assert.NotEqual(a, StyleReader.CacheKey(swapped));
        }

        [Fact]
        public async Task Second_read_of_same_inputs_comes_from_cache()
        {
            var dir = TempDir();
            try
            {
                var cache = new StyleProfileCache(dir);
                var fake = new FakeClaude(Reply(Sample()));
                var reader = new StyleReader(fake.Send, cache);

                var first = await reader.ReadAsync(TwoImages());
                var second = await reader.ReadAsync(TwoImages());

                Assert.Single(fake.Requests);
                Assert.True(second.FromCache);
                Assert.Equal(0, second.Attempts);
                Assert.Equal(first.Key, second.Key);
                Assert.Equal(StyleProfileJson.Serialize(first.Profile), StyleProfileJson.Serialize(second.Profile));
                Assert.True(File.Exists(Path.Combine(dir, first.Key + ".json")));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public async Task Corrupt_cache_entry_is_a_miss()
        {
            var dir = TempDir();
            try
            {
                var key = StyleReader.CacheKey(TwoImages());
                File.WriteAllText(Path.Combine(dir, key + ".json"), "{ not json");
                var fake = new FakeClaude(Reply(Sample()));

                var result = await new StyleReader(fake.Send, new StyleProfileCache(dir)).ReadAsync(TwoImages());

                Assert.False(result.FromCache);
                Assert.Single(fake.Requests);
                Assert.True(new StyleProfileCache(dir).TryGet(key, 2, out _));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Request_from_brief_style_reads_relative_image_files()
        {
            var dir = TempDir();
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "ref1.png"), new byte[] { 1, 2, 3 });
                var input = new StyleInput { Prompt = "dry garden", Images = new List<string> { "ref1.png" } };

                var request = StyleRequest.FromInput(input, dir);

                Assert.Equal("dry garden", request.Prompt);
                Assert.Equal("image/png", request.Images.Single().MediaType);
                Assert.Equal(3, request.Images.Single().Data.Length);

                var missing = new StyleInput { Images = new List<string> { "nope.jpg" } };
                Assert.Throws<FileNotFoundException>(() => StyleRequest.FromInput(missing, dir));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
