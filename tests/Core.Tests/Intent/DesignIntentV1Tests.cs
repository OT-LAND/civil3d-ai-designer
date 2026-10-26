using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivilAI.Core.Intent;
using Xunit;

namespace CivilAI.Core.Tests.Intent
{
    // Design Intent v1 (B.3): brief references, trees, drainage, site elements.
    public class DesignIntentV1Tests
    {
        private static DesignIntent Sample()
        {
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v1.json"));
            Assert.True(DesignIntentJson.TryParse(json, out var intent, out var error), error);
            return intent!;
        }

        private static List<Point2> Square(double x0, double y0, double size) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + size, y0), new Point2(x0 + size, y0 + size), new Point2(x0, y0 + size),
        };

        private static bool HasIssue(IReadOnlyList<IntentIssue> issues, string path) =>
            issues.Any(i => i.Path.StartsWith(path, StringComparison.Ordinal));

        [Fact]
        public void V1_sample_parses_round_trips_and_is_valid()
        {
            var intent = Sample();
            Assert.Equal("1", intent.SchemaVersion);
            Assert.Equal(3, intent.Trees.Count);
            Assert.Equal(TreeStatus.Existing, intent.Trees[2].Status);
            Assert.Equal("basin-1", intent.Drainage.Swales[0].OutletId);
            Assert.Equal(BasinKind.Infiltration, intent.Drainage.Basins[0].Kind);
            Assert.True(intent.Drainage.LowPoints[0].Inlet);
            Assert.Equal(ElementKind.Shade, intent.Elements[1].Kind);
            Assert.Equal("P2", intent.Spaces[2].InterestPointId);
            Assert.Equal("Z2", intent.Spaces[3].ZoneId);
            Assert.Empty(DesignIntentValidator.Validate(intent));

            var json = DesignIntentJson.Serialize(intent);
            Assert.Contains("\"status\": \"existing\"", json);
            Assert.Contains("\"kind\": \"infiltration\"", json);
            Assert.True(DesignIntentJson.TryParse(json, out var again, out var error), error);
            Assert.Equal(json, DesignIntentJson.Serialize(again!));
        }

        [Fact]
        public void V0_documents_still_read_and_must_not_use_v1_parts()
        {
            Assert.Empty(DesignIntentValidator.Validate(new DesignIntent { SchemaVersion = "0" }));

            var intent = new DesignIntent
            {
                SchemaVersion = "0",
                Trees = { new TreeIntent { Id = "t", Location = new Point2(1, 1), CanopyDiameter = 5 } },
            };
            Assert.Contains(DesignIntentValidator.Validate(intent), i => i.Path == "schemaVersion");

            var withZone = new DesignIntent
            {
                SchemaVersion = "0",
                Spaces = { new Space { Id = "a", Kind = SpaceKind.Lawn, Outline = Square(0, 0, 5), ZoneId = "Z1" } },
            };
            Assert.Contains(DesignIntentValidator.Validate(withZone), i => i.Path == "schemaVersion");

            Assert.Contains(DesignIntentValidator.Validate(new DesignIntent { SchemaVersion = "2" }),
                i => i.Path == "schemaVersion");
        }

        [Fact]
        public void New_intent_defaults_to_v1()
        {
            Assert.Equal("1", new DesignIntent().SchemaVersion);
        }

        [Fact]
        public void Tree_checks_canopy_protection_zone_and_survey_link()
        {
            var intent = new DesignIntent
            {
                Trees =
                {
                    new TreeIntent { Id = "a", Status = TreeStatus.New, Location = new Point2(0, 0), CanopyDiameter = 0 },
                    new TreeIntent { Id = "b", Status = TreeStatus.New, Location = new Point2(5, 0), CanopyDiameter = 6, ProtectionRadius = 3 },
                    new TreeIntent { Id = "c", Status = TreeStatus.Existing, Location = new Point2(9, 0), CanopyDiameter = 6 },
                    new TreeIntent { Id = "d", Status = TreeStatus.Existing, Location = new Point2(9, 9), CanopyDiameter = 6,
                                     SourceId = "12", ProtectionRadius = -1, TrunkDiameter = double.NaN },
                    new TreeIntent { Id = "e", Status = TreeStatus.Existing, Location = new Point2(20, 9), CanopyDiameter = 6,
                                     SourceId = "13", ProtectionRadius = 4, TrunkDiameter = 0.3 },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.True(HasIssue(issues, "trees[0].canopyDiameter"));
            Assert.True(HasIssue(issues, "trees[1].protectionRadius"));
            Assert.True(HasIssue(issues, "trees[2].sourceId"));
            Assert.True(HasIssue(issues, "trees[3].protectionRadius"));
            Assert.True(HasIssue(issues, "trees[3].trunkDiameter"));
            Assert.False(HasIssue(issues, "trees[4]"));
        }

        [Fact]
        public void Swale_outlet_must_be_a_basin_or_low_point()
        {
            var intent = new DesignIntent
            {
                Spaces = { new Space { Id = "lawn", Kind = SpaceKind.Lawn, Outline = Square(0, 0, 5) } },
                Drainage = new DrainageIntent
                {
                    Basins = { new DrainageBasin { Id = "b", Kind = BasinKind.Retention, Outline = Square(10, 0, 3), Depth = 0 } },
                    LowPoints = { new LowPoint { Id = "lp", Location = new Point2(20, 0), Level = double.PositiveInfinity } },
                    Swales =
                    {
                        new Swale { Id = "s1", Line = { new Point2(0, 0), new Point2(10, 1) }, OutletId = "b" },
                        new Swale { Id = "s2", Line = { new Point2(0, 5), new Point2(20, 0) }, OutletId = "lp", Width = 1 },
                        new Swale { Id = "s3", Line = { new Point2(0, 9), new Point2(5, 9) }, OutletId = "lawn" },
                        new Swale { Id = "s4", Line = { new Point2(0, 0) }, Depth = -0.2 },
                    },
                },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.False(HasIssue(issues, "drainage.swales[0]"));
            Assert.False(HasIssue(issues, "drainage.swales[1]"));
            Assert.Contains(issues, i => i.Path == "drainage.swales[2].outletId" && i.Message.Contains("lawn"));
            Assert.True(HasIssue(issues, "drainage.swales[3].line"));
            Assert.True(HasIssue(issues, "drainage.swales[3].depth"));
            Assert.True(HasIssue(issues, "drainage.basins[0].depth"));
            Assert.True(HasIssue(issues, "drainage.lowPoints[0].level"));
        }

        [Fact]
        public void Ids_are_unique_across_trees_drainage_elements_and_spaces()
        {
            var intent = new DesignIntent
            {
                Spaces = { new Space { Id = "x", Kind = SpaceKind.Lawn, Outline = Square(0, 0, 5) } },
                Trees = { new TreeIntent { Id = "x", Location = new Point2(1, 1), CanopyDiameter = 4 } },
                Elements = { new SiteElement { Id = "x", Kind = ElementKind.Seating, Location = new Point2(2, 2) } },
                Drainage = new DrainageIntent { LowPoints = { new LowPoint { Id = "x", Location = new Point2(3, 3) } } },
            };
            var issues = DesignIntentValidator.Validate(intent);
            Assert.Contains(issues, i => i.Path == "trees[0].id");
            Assert.Contains(issues, i => i.Path == "elements[0].id");
            Assert.Contains(issues, i => i.Path == "drainage.lowPoints[0].id");
        }

        [Theory]
        [InlineData(ElementKind.Shade, false, true)]
        [InlineData(ElementKind.Pergola, false, true)]
        [InlineData(ElementKind.Shade, true, false)]
        [InlineData(ElementKind.Seating, false, false)]
        [InlineData(ElementKind.Play, true, false)]
        public void Shade_and_pergola_need_a_footprint(ElementKind kind, bool withOutline, bool expectIssue)
        {
            var intent = new DesignIntent
            {
                Elements =
                {
                    new SiteElement { Id = "e", Kind = kind, Location = new Point2(2, 2), Outline = withOutline ? Square(0, 0, 4) : null },
                },
            };
            Assert.Equal(expectIssue, HasIssue(DesignIntentValidator.Validate(intent), "elements[0].outline"));
        }

        [Fact]
        public void Unknown_tree_status_or_element_kind_is_a_shape_error()
        {
            Assert.False(DesignIntentJson.TryParse(
                "{ \"schemaVersion\": \"1\", \"trees\": [ { \"id\": \"t\", \"status\": \"dead\", \"location\": { \"x\": 0, \"y\": 0 }, \"canopyDiameter\": 4 } ] }",
                out _, out var e1));
            Assert.False(string.IsNullOrWhiteSpace(e1));
            Assert.False(DesignIntentJson.TryParse(
                "{ \"schemaVersion\": \"1\", \"elements\": [ { \"id\": \"e\", \"kind\": \"fountainOfYouth\", \"location\": { \"x\": 0, \"y\": 0 } } ] }",
                out _, out var e2));
            Assert.False(string.IsNullOrWhiteSpace(e2));
        }

        [Fact]
        public void Text_to_intent_output_schema_offers_the_v1_parts()
        {
            var schema = TextToIntent.BuildOutputSchema();
            Assert.Contains("\"trees\"", schema);
            Assert.Contains("\"drainage\"", schema);
            Assert.Contains("\"elements\"", schema);
        }
    }
}
