using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Intent;
using CivilAI.Core.Metrics;
using Xunit;

namespace CivilAI.Core.Tests.Metrics
{
    // B.1b: cost level (D20) measured from cost classes and compared with the budget slider.
    public class CostLevelMetricsTests
    {
        private static List<Point2> Rect(double x0, double y0, double w, double h) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + w, y0), new Point2(x0 + w, y0 + h), new Point2(x0, y0 + h),
        };

        private static Space Plaza(string id, double x0, double w, string material) =>
            new Space { Id = id, Kind = SpaceKind.Plaza, Outline = Rect(x0, 0, w, 10), MaterialId = material };

        private static DesignIntent Intent(
            List<Space>? spaces = null, List<Material>? materials = null,
            List<TreeIntent>? trees = null, List<SiteElement>? elements = null) => new DesignIntent
            {
                Site = new Site { LotBoundary = Rect(0, 0, 100, 100) },
                Spaces = spaces ?? new List<Space>(),
                Materials = materials ?? new List<Material>(),
                Trees = trees ?? new List<TreeIntent>(),
                Elements = elements ?? new List<SiteElement>(),
            };

        private static Material Mat(string id, CostClass? c) =>
            new Material { Id = id, Name = id, Category = MaterialCategory.Paving, CostClass = c };

        private static TreeIntent Tree(string id, CostClass? c, TreeStatus status = TreeStatus.New, double x = 50) =>
            new TreeIntent { Id = id, Status = status, Location = new Point2(x, 50), CanopyDiameter = 4, CostClass = c };

        private static SiteElement Element(string id, CostClass? c, double x = 50) =>
            new SiteElement { Id = id, Kind = ElementKind.Seating, Location = new Point2(x, 50), CostClass = c };

        [Fact]
        public void Surfaces_are_weighted_by_area()
        {
            // 30 m wide stone (high, 100) + 10 m wide gravel (low, 0) → 75.
            var m = IntentMetricsCalculator.Measure(Intent(
                new List<Space> { Plaza("a", 0, 30, "stone"), Plaza("b", 30, 10, "gravel") },
                new List<Material> { Mat("stone", CostClass.High), Mat("gravel", CostClass.Low) }));

            Assert.Equal(75, m.SurfaceCostLevel!.Value, 6);
            Assert.Null(m.TreeCostLevel);
            Assert.Null(m.ElementCostLevel);
            Assert.Equal(75, m.CostLevelPercent!.Value, 6);
        }

        [Fact]
        public void Spaces_with_unclassed_material_are_left_out()
        {
            var m = IntentMetricsCalculator.Measure(Intent(
                new List<Space> { Plaza("a", 0, 30, "stone"), Plaza("b", 30, 10, "other"), Plaza("c", 40, 10, "missing") },
                new List<Material> { Mat("stone", CostClass.Mid), Mat("other", null) }));

            Assert.Equal(50, m.SurfaceCostLevel!.Value, 6);
        }

        [Fact]
        public void Parts_are_averaged_and_only_new_trees_count()
        {
            var m = IntentMetricsCalculator.Measure(Intent(
                new List<Space> { Plaza("a", 0, 10, "gravel") },
                new List<Material> { Mat("gravel", CostClass.Low) },
                new List<TreeIntent> { Tree("t1", CostClass.High), Tree("t2", CostClass.Mid), Tree("old", CostClass.Low, TreeStatus.Existing) },
                new List<SiteElement> { Element("e1", CostClass.High), Element("e2", null) }));

            Assert.Equal(0, m.SurfaceCostLevel!.Value, 6);
            Assert.Equal(75, m.TreeCostLevel!.Value, 6);
            Assert.Equal(100, m.ElementCostLevel!.Value, 6);
            Assert.Equal((0 + 75 + 100) / 3.0, m.CostLevelPercent!.Value, 6);
        }

        [Fact]
        public void Zone_counts_only_what_lies_inside()
        {
            var intent = Intent(
                new List<Space> { Plaza("in", 0, 10, "stone"), Plaza("out", 60, 10, "gravel") },
                new List<Material> { Mat("stone", CostClass.High), Mat("gravel", CostClass.Low) },
                new List<TreeIntent> { Tree("t-in", CostClass.High, x: 5), Tree("t-out", CostClass.Low, x: 65) },
                new List<SiteElement> { Element("e-out", CostClass.Low, x: 65) });

            var m = IntentMetricsCalculator.Measure(intent, Rect(0, 0, 20, 100));

            Assert.Equal(100, m.SurfaceCostLevel!.Value, 6);
            Assert.Equal(100, m.TreeCostLevel!.Value, 6);
            Assert.Null(m.ElementCostLevel);
            Assert.Equal(100, m.CostLevelPercent!.Value, 6);
        }

        [Fact]
        public void No_cost_classes_is_not_measured()
        {
            var m = IntentMetricsCalculator.Measure(Intent(
                new List<Space> { Plaza("a", 0, 10, "stone") }, new List<Material> { Mat("stone", null) },
                new List<TreeIntent> { Tree("t", null) }));

            Assert.Null(m.CostLevelPercent);
            var row = TargetComparison.Compare(m, new ResolvedCharacter(50, 50, 50, 50, 50, 50, 50, 50, 50)).Single(r => r.Slider == "budget");
            Assert.Equal(TargetStatus.NotMeasured, row.Status);
        }

        [Theory]
        [InlineData(50, 40, TargetStatus.Met)]    // all mid on a budget of 40
        [InlineData(50, 65, TargetStatus.Met)]    // within 15 points
        [InlineData(50, 70, TargetStatus.Missed)] // premium asked, all mid given
        [InlineData(50, 20, TargetStatus.Missed)]
        public void Budget_tolerance_is_fifteen_points(double achieved, int budget, TargetStatus expected)
        {
            var ch = new ResolvedCharacter(50, 50, 50, 50, 50, 50, 50, 50, budget);
            var row = TargetComparison.Compare(new IntentMetrics { CostLevelPercent = achieved }, ch).Single(r => r.Slider == "budget");
            Assert.Equal(expected, row.Status);
            Assert.Equal(15, row.Tolerance);
        }

        [Fact]
        public void Courtyard_v1_sample_has_a_cost_level()
        {
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v1.json"));
            Assert.True(DesignIntentJson.TryParse(json, out var intent, out var error), error);

            var m = IntentMetricsCalculator.Measure(intent!);

            Assert.Equal(75, m.TreeCostLevel!.Value, 6);         // high + mid
            Assert.Equal(100 / 3.0, m.ElementCostLevel!.Value, 6); // mid, mid, low
            Assert.InRange(m.SurfaceCostLevel!.Value, 66, 68);   // stone high, gravel low, mulch mid, by area
            Assert.InRange(m.CostLevelPercent!.Value, 58, 59);
            Assert.Equal(intent!.Materials[0].CostClass, CostClass.High);
            Assert.Contains("\"costClass\": \"high\"", DesignIntentJson.Serialize(intent));
        }

        [Fact]
        public void Schema_offers_cost_class_on_materials_trees_and_elements()
        {
            var schema = DesignIntentJson.ReadSchema();
            Assert.Contains("\"costClass\"", schema);
            var root = System.Text.Json.Nodes.JsonNode.Parse(schema)!;
            foreach (var def in new[] { "material", "tree", "element" })
                Assert.NotNull(root["$defs"]![def]!["properties"]!["costClass"]);
        }
    }
}
