using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivilAI.Core.Intent;
using Xunit;

namespace CivilAI.Core.Tests.Intent
{
    public class DesignIntentTests
    {
        private static string SampleJson() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v0.json"));

        private static DesignIntent Sample()
        {
            Assert.True(DesignIntentJson.TryParse(SampleJson(), out var intent, out var error), error);
            return intent!;
        }

        private static List<Point2> Square(double x0, double y0, double size) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + size, y0), new Point2(x0 + size, y0 + size), new Point2(x0, y0 + size),
        };

        private static bool HasIssue(IReadOnlyList<IntentIssue> issues, string pathStart) =>
            issues.Any(i => i.Path.StartsWith(pathStart, StringComparison.Ordinal));

        [Fact]
        public void Courtyard_sample_parses_and_is_valid()
        {
            var intent = Sample();
            Assert.Equal(4, intent.Spaces.Count);
            Assert.Equal(52.40, intent.Site.Buildings[0].Ffl, 6);
            Assert.Equal(SpaceKind.Path, intent.Spaces[1].Kind);
            Assert.Equal(EdgeKind.Wall, intent.Edges[1].Kind);
            Assert.Empty(DesignIntentValidator.Validate(intent));
        }

        [Fact]
        public void Round_trip_keeps_content_and_writes_camelCase_strings()
        {
            var json = DesignIntentJson.Serialize(Sample());
            Assert.Contains("\"kind\": \"plaza\"", json);
            Assert.Contains("\"materialId\": \"pavers-grey\"", json);
            Assert.DoesNotContain("\"Kind\"", json);

            Assert.True(DesignIntentJson.TryParse(json, out var again, out var error), error);
            Assert.Equal(json, DesignIntentJson.Serialize(again!));
        }

        [Theory]
        [InlineData("{ \"schemaVersion\": \"0\", \"spaces\": [ { \"id\": \"a\", \"kind\": \"pond\" } ] }")]
        [InlineData("{ \"schemaVersion\": \"0\", \"surprise\": 1 }")]
        [InlineData("{ \"schemaVersion\": \"0\", \"spaces\": [ { \"id\": \"a\", \"kind\": 0 } ] }")]
        [InlineData("not json")]
        [InlineData("null")]
        public void Rejects_wrong_shape_with_a_message(string json)
        {
            Assert.False(DesignIntentJson.TryParse(json, out var intent, out var error));
            Assert.Null(intent);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void Duplicate_ids_are_reported()
        {
            var intent = new DesignIntent
            {
                Spaces =
                {
                    new Space { Id = "s1", Kind = SpaceKind.Lawn, Outline = Square(0, 0, 5) },
                    new Space { Id = "s1", Kind = SpaceKind.Bed, Outline = Square(10, 0, 5) },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.Contains(issues, i => i.Path == "spaces[1].id" && i.Message.Contains("more than once"));
        }

        [Fact]
        public void Path_needs_centerline_and_width_and_areas_need_outline()
        {
            var intent = new DesignIntent
            {
                Spaces =
                {
                    new Space { Id = "p", Kind = SpaceKind.Path, Centerline = new List<Point2> { new Point2(0, 0) } },
                    new Space { Id = "pl", Kind = SpaceKind.Plaza },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.True(HasIssue(issues, "spaces[0].centerline"));
            Assert.True(HasIssue(issues, "spaces[0].width"));
            Assert.True(HasIssue(issues, "spaces[1].outline"));
        }

        [Fact]
        public void Degenerate_and_explicitly_closed_outlines_are_reported()
        {
            var flat = new List<Point2> { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0) };
            var closed = Square(0, 0, 5);
            closed.Add(closed[0]);
            var intent = new DesignIntent
            {
                Spaces =
                {
                    new Space { Id = "a", Kind = SpaceKind.Lawn, Outline = flat },
                    new Space { Id = "b", Kind = SpaceKind.Bed, Outline = closed },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.Contains(issues, i => i.Path == "spaces[0].outline" && i.Message.Contains("no area"));
            Assert.Contains(issues, i => i.Path == "spaces[1].outline" && i.Message.Contains("closing point"));
        }

        [Fact]
        public void Broken_references_are_reported()
        {
            var intent = new DesignIntent
            {
                Spaces = { new Space { Id = "a", Kind = SpaceKind.Plaza, Outline = Square(0, 0, 5), MaterialId = "granite" } },
                Edges =
                {
                    new Edge
                    {
                        Id = "e", Kind = EdgeKind.Kerb,
                        Line = new List<Point2> { new Point2(0, 0), new Point2(5, 0) },
                        SpaceIds = new List<string> { "a", "ghost" },
                    },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.Contains(issues, i => i.Path == "spaces[0].materialId");
            Assert.Contains(issues, i => i.Path == "edges[0].spaceIds" && i.Message.Contains("ghost"));
        }

        [Theory]
        [InlineData(EdgeKind.Steps, null, true)]
        [InlineData(EdgeKind.Wall, 0.0, true)]
        [InlineData(EdgeKind.Wall, 0.45, false)]
        [InlineData(EdgeKind.Flush, null, false)]
        [InlineData(EdgeKind.Kerb, -0.1, true)]
        public void Steps_and_walls_need_a_height(EdgeKind kind, double? height, bool expectIssue)
        {
            var intent = new DesignIntent
            {
                Spaces = { new Space { Id = "a", Kind = SpaceKind.Lawn, Outline = Square(0, 0, 5) } },
                Edges =
                {
                    new Edge
                    {
                        Id = "e", Kind = kind, Height = height,
                        Line = new List<Point2> { new Point2(0, 0), new Point2(5, 0) },
                        SpaceIds = new List<string> { "a" },
                    },
                },
            };
            Assert.Equal(expectIssue, HasIssue(DesignIntentValidator.Validate(intent), "edges[0].height"));
        }

        [Fact]
        public void Out_of_range_fall_and_non_finite_levels_are_reported()
        {
            var intent = new DesignIntent
            {
                Spaces =
                {
                    new Space
                    {
                        Id = "a", Kind = SpaceKind.Plaza, Outline = Square(0, 0, 5),
                        Level = new LevelIntent
                        {
                            Elevation = double.NaN,
                            Fall = new Fall { Percent = -2, DirectionDegrees = 90 },
                        },
                    },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.True(HasIssue(issues, "spaces[0].level.elevation"));
            Assert.True(HasIssue(issues, "spaces[0].level.fall.percent"));
        }

        [Fact]
        public void Schema_is_embedded_and_matches_the_model()
        {
            using var doc = JsonDocument.Parse(DesignIntentJson.ReadSchema());
            var defs = doc.RootElement.GetProperty("$defs");

            static string[] EnumValues(JsonElement def, string prop) =>
                def.GetProperty("properties").GetProperty(prop).GetProperty("enum")
                   .EnumerateArray().Select(e => e.GetString()!).ToArray();
            static string[] Camel<T>() where T : struct, Enum =>
                Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName).ToArray();

            Assert.Equal(Camel<SpaceKind>(), EnumValues(defs.GetProperty("space"), "kind"));
            Assert.Equal(Camel<EdgeKind>(), EnumValues(defs.GetProperty("edge"), "kind"));
            Assert.Equal(Camel<MaterialCategory>(), EnumValues(defs.GetProperty("material"), "category"));
            Assert.Equal(DesignIntent.CurrentSchemaVersion,
                doc.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
        }

        [Fact]
        public void Schema_property_names_match_what_the_serializer_writes()
        {
            // Every property the serializer can write for the sample must be declared in the schema.
            using var schema = JsonDocument.Parse(DesignIntentJson.ReadSchema());
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in schema.RootElement.GetProperty("$defs").EnumerateObject())
                if (def.Value.TryGetProperty("properties", out var props))
                    foreach (var p in props.EnumerateObject()) declared.Add(p.Name);
            foreach (var p in schema.RootElement.GetProperty("properties").EnumerateObject()) declared.Add(p.Name);

            using var written = JsonDocument.Parse(DesignIntentJson.Serialize(Sample()));
            var used = new HashSet<string>(StringComparer.Ordinal);
            void Walk(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                    foreach (var p in e.EnumerateObject()) { used.Add(p.Name); Walk(p.Value); }
                else if (e.ValueKind == JsonValueKind.Array)
                    foreach (var item in e.EnumerateArray()) Walk(item);
            }
            Walk(written.RootElement);

            Assert.Empty(used.Except(declared));
        }
    }
}
