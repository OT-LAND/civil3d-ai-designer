using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivilAI.Core.Brief;
using CivilAI.Core.Intent;
using Xunit;

namespace CivilAI.Core.Tests.Brief
{
    public class DesignBriefTests
    {
        private static string SampleJson() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "brief", "courtyard.brief.v1.json"));

        private static DesignBrief Sample()
        {
            Assert.True(DesignBriefJson.TryParse(SampleJson(), out var brief, out var error), error);
            return brief!;
        }

        private static List<Point2> Square(double x0, double y0, double size) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + size, y0), new Point2(x0 + size, y0 + size), new Point2(x0, y0 + size),
        };

        private static DesignBrief Minimal(
            Character? global = null,
            List<Zone>? zones = null,
            List<TaggedCurve>? curves = null,
            List<TaggedPoint>? points = null,
            StyleInput? style = null) => new DesignBrief
        {
            Character = global ?? Character.AllAt(50),
            Zones = zones ?? new List<Zone> { new Zone { Id = "Z1", Outline = Square(0, 0, 10) } },
            Curves = curves ?? new List<TaggedCurve>(),
            Points = points ?? new List<TaggedPoint>(),
            Style = style ?? new StyleInput(),
        };

        private static bool HasIssue(IReadOnlyList<IntentIssue> issues, string pathStart) =>
            issues.Any(i => i.Path.StartsWith(pathStart, StringComparison.Ordinal));

        [Fact]
        public void Sample_parses_and_is_valid()
        {
            var brief = Sample();
            Assert.Empty(DesignBriefValidator.Validate(brief));
            Assert.Equal(2, brief.Zones.Count);
            Assert.Equal(5, brief.Curves.Count);
            Assert.Equal(5, brief.Points.Count);
            Assert.Equal(CurveTag.WalkingNetwork, brief.Curves[0].Tag);
            Assert.Equal(CurveTag.FlowBasin, brief.Curves[3].Tag);
            Assert.Equal(PointTag.InterestPoint, brief.Points[1].Tag);
            Assert.False(brief.Curves[1].Accessible);
            Assert.Equal(100.60, brief.Curves[2].Level);
            Assert.Equal("2F3A", brief.Zones[0].HostId);
        }

        [Fact]
        public void Round_trip_keeps_the_brief()
        {
            var first = Sample();
            var json = DesignBriefJson.Serialize(first);
            Assert.True(DesignBriefJson.TryParse(json, out var second, out var error), error);
            Assert.Equal(json, DesignBriefJson.Serialize(second!));
            Assert.Contains("\"walkingNetwork\"", json);
            Assert.Contains("\"interestPoint\"", json);
        }

        [Fact]
        public void Zone_overrides_single_sliders_and_keeps_the_rest_global()
        {
            // D15: global defaults, any zone overrides single sliders.
            var brief = Sample();
            var z1 = brief.CharacterFor(brief.Zones[0]);
            var z2 = brief.CharacterFor(brief.Zones[1]);
            Assert.Equal(30, z1.HardSoft);   // own
            Assert.Equal(65, z1.Shading);    // global
            Assert.Equal(80, z2.HardSoft);
            Assert.Equal(80, z2.Shading);
            Assert.Equal(70, z2.Accessibility);
            Assert.Equal(35, z2.CurvedStraight);
        }

        [Fact]
        public void Unknown_tag_or_field_is_rejected_with_a_path()
        {
            var badTag = SampleJson().Replace("\"flowBasin\"", "\"river\"");
            Assert.False(DesignBriefJson.TryParse(badTag, out _, out var e1));
            Assert.Contains("curves", e1);

            var badField = SampleJson().Replace("\"hostId\": \"2F3A\"", "\"colour\": \"red\"");
            Assert.False(DesignBriefJson.TryParse(badField, out _, out _));
        }

        [Fact]
        public void Missing_global_slider_and_out_of_range_values_are_reported()
        {
            var global = new Character { Accessibility = 50, HardSoft = 120, MaxGrade = 50, CutFill = 50, MaxSlope = 50, Shading = 50, TreesElements = 50 };
            var zones = new List<Zone> { new Zone { Id = "Z1", Outline = Square(0, 0, 10), Character = new Character { Shading = -5 } } };
            var issues = DesignBriefValidator.Validate(Minimal(global, zones));
            Assert.True(HasIssue(issues, "character.hardSoft"));
            Assert.True(HasIssue(issues, "character.curvedStraight"));
            Assert.True(HasIssue(issues, "zones[0].character.shading"));
            Assert.Equal(3, issues.Count);
        }

        [Fact]
        public void Omitted_character_object_is_not_silently_defaulted()
        {
            var json = "{ \"schemaVersion\": \"1\", \"units\": \"m\", \"zones\": [ { \"id\": \"Z1\", \"outline\": [ {\"x\":0,\"y\":0}, {\"x\":5,\"y\":0}, {\"x\":5,\"y\":5} ] } ] }";
            Assert.True(DesignBriefJson.TryParse(json, out var brief, out var error), error);
            var issues = DesignBriefValidator.Validate(brief!);
            Assert.Equal(8, issues.Count(i => i.Path.StartsWith("character.", StringComparison.Ordinal)));
        }

        [Fact]
        public void Needs_a_zone_with_a_usable_outline_and_unique_ids()
        {
            Assert.True(HasIssue(DesignBriefValidator.Validate(Minimal(zones: new List<Zone>())), "zones"));

            var zones = new List<Zone>
            {
                new Zone { Id = "Z1", Outline = new List<Point2> { new Point2(0, 0), new Point2(1, 1), new Point2(2, 2) } },
            };
            var points = new List<TaggedPoint> { new TaggedPoint { Id = "Z1", Tag = PointTag.Tree, At = new Point2(1, 1) } };
            var issues = DesignBriefValidator.Validate(Minimal(zones: zones, points: points));
            Assert.True(HasIssue(issues, "zones[0].outline"));   // no area
            Assert.True(HasIssue(issues, "points[0].id"));       // duplicate id
        }

        [Fact]
        public void Tag_specific_fields_are_checked()
        {
            var curves = new List<TaggedCurve>
            {
                new TaggedCurve { Id = "C1", Tag = CurveTag.Border, Level = 100, Points = new List<Point2> { new(0, 0), new(5, 0) } },
                new TaggedCurve { Id = "C2", Tag = CurveTag.Contour, Accessible = false, Points = new List<Point2> { new(0, 1), new(5, 1) } },
                new TaggedCurve { Id = "C3", Tag = CurveTag.WalkingNetwork, Points = new List<Point2> { new(0, 2) } },
            };
            var points = new List<TaggedPoint>
            {
                new TaggedPoint { Id = "P1", Tag = PointTag.Elevation, At = new Point2(1, 1) },
                new TaggedPoint { Id = "P2", Tag = PointTag.InterestPoint, At = new Point2(2, 2), Level = 100 },
                new TaggedPoint { Id = "P3", Tag = PointTag.Entrance, At = new Point2(3, 3), Species = "Olea europaea" },
                new TaggedPoint { Id = "P4", Tag = PointTag.Entrance, At = new Point2(4, 4), Level = 101.2 },
            };
            var issues = DesignBriefValidator.Validate(Minimal(curves: curves, points: points));
            Assert.True(HasIssue(issues, "curves[0].level"));
            Assert.True(HasIssue(issues, "curves[1].accessible"));
            Assert.True(HasIssue(issues, "curves[2].points"));
            Assert.True(HasIssue(issues, "points[0].level"));
            Assert.True(HasIssue(issues, "points[1].level"));
            Assert.True(HasIssue(issues, "points[2].species"));
            Assert.False(HasIssue(issues, "points[3]"));
            Assert.Equal(6, issues.Count);
        }

        [Fact]
        public void Style_allows_up_to_six_images_and_units_must_be_metres()
        {
            var style = new StyleInput { Images = Enumerable.Range(1, 7).Select(i => $"ref{i}.jpg").ToList() };
            Assert.True(HasIssue(DesignBriefValidator.Validate(Minimal(style: style)), "style.images"));

            var feet = SampleJson().Replace("\"units\": \"m\"", "\"units\": \"ft\"");
            Assert.True(DesignBriefJson.TryParse(feet, out var brief, out _));
            Assert.True(HasIssue(DesignBriefValidator.Validate(brief!), "units"));
        }

        [Fact]
        public void Schema_is_embedded_and_matches_the_model()
        {
            using var doc = JsonDocument.Parse(DesignBriefJson.ReadSchema());
            var root = doc.RootElement;
            var defs = root.GetProperty("$defs");

            static string[] EnumValues(JsonElement def, string prop) =>
                def.GetProperty("properties").GetProperty(prop).GetProperty("enum")
                   .EnumerateArray().Select(e => e.GetString()!).ToArray();
            static string[] Camel<T>() where T : struct, Enum =>
                Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName).ToArray();

            Assert.Equal(Camel<CurveTag>(), EnumValues(defs.GetProperty("curve"), "tag"));
            Assert.Equal(Camel<PointTag>(), EnumValues(defs.GetProperty("taggedPoint"), "tag"));
            Assert.Equal(DesignBrief.CurrentSchemaVersion,
                root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());

            var sliderNames = new Character().Sliders().Select(s => s.Name).ToArray();
            Assert.Equal(sliderNames,
                defs.GetProperty("characterOverride").GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(sliderNames,
                defs.GetProperty("characterGlobal").GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray());
        }

        [Fact]
        public void Schema_property_names_match_what_the_serializer_writes()
        {
            using var schema = JsonDocument.Parse(DesignBriefJson.ReadSchema());
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in schema.RootElement.GetProperty("$defs").EnumerateObject())
                if (def.Value.TryGetProperty("properties", out var props))
                    foreach (var p in props.EnumerateObject()) declared.Add(p.Name);
            foreach (var p in schema.RootElement.GetProperty("properties").EnumerateObject()) declared.Add(p.Name);

            using var written = JsonDocument.Parse(DesignBriefJson.Serialize(Sample()));
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
