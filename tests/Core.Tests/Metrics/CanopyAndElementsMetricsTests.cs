using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Geometry;
using CivilAI.Core.Intent;
using CivilAI.Core.Metrics;
using Xunit;

namespace CivilAI.Core.Tests.Metrics
{
    public class CanopyAndElementsMetricsTests
    {
        private static DesignIntent SampleV1()
        {
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v1.json"));
            Assert.True(DesignIntentJson.TryParse(json, out var intent, out var error), error);
            return intent!;
        }

        private static List<Point2> Rect(double x0, double y0, double w, double h) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + w, y0), new Point2(x0 + w, y0 + h), new Point2(x0, y0 + h),
        };

        private static TreeIntent Tree(string id, double x, double y, double canopy, TreeStatus status = TreeStatus.New) =>
            new TreeIntent { Id = id, Status = status, Location = new Point2(x, y), CanopyDiameter = canopy };

        private static DesignIntent Lot(List<TreeIntent>? trees = null, List<SiteElement>? elements = null, List<Building>? buildings = null) =>
            new DesignIntent
            {
                Site = new Site { LotBoundary = Rect(0, 0, 20, 20), Buildings = buildings ?? new List<Building>() },
                Trees = trees ?? new List<TreeIntent>(),
                Elements = elements ?? new List<SiteElement>(),
            };

        [Fact]
        public void Courtyard_v1_canopy_cover_and_elements_share()
        {
            var m = IntentMetricsCalculator.Measure(SampleV1());
            // lot 40×25 = 1000 m²; canopies π(4² + 3.5² + 2.5²) = 108.38 m², shade sail 6×6 = 36 m², no overlaps
            double expected = 100 * (Math.PI * 34.5 + 36) / 1000;
            Assert.InRange(m.CanopyCoverPercent!.Value, expected - 0.1, expected + 0.1);
            Assert.Equal(2, m.NewTreeCount);
            Assert.Equal(3, m.ElementCount);
            Assert.Equal(60, m.ElementsSharePercent!.Value, 6);
        }

        [Fact]
        public void Overlapping_canopies_count_once()
        {
            var single = IntentMetricsCalculator.Measure(Lot(new List<TreeIntent> { Tree("a", 10, 10, 6) }));
            var twice = IntentMetricsCalculator.Measure(Lot(new List<TreeIntent> { Tree("a", 10, 10, 6), Tree("b", 10, 10, 6) }));
            Assert.Equal(100 * Math.PI * 9 / 400, single.CanopyCoverPercent!.Value, 1);
            Assert.Equal(single.CanopyCoverPercent.Value, twice.CanopyCoverPercent!.Value, 6);
        }

        [Fact]
        public void Canopy_over_the_lot_border_is_clipped()
        {
            // centre on the corner: a quarter of the circle lies inside the lot
            var m = IntentMetricsCalculator.Measure(Lot(new List<TreeIntent> { Tree("a", 0, 0, 8) }));
            Assert.Equal(100 * Math.PI * 16 / 4 / 400, m.CanopyCoverPercent!.Value, 1);
        }

        [Fact]
        public void Buildings_are_not_open_ground_and_pergola_counts()
        {
            var building = new Building { Id = "b", Footprint = Rect(0, 0, 10, 20), Ffl = 10 };
            var pergola = new SiteElement { Id = "p", Kind = ElementKind.Pergola, Location = new Point2(15, 10), Outline = Rect(10, 0, 5, 20) };
            var seat = new SiteElement { Id = "s", Kind = ElementKind.Seating, Location = new Point2(12, 2), Outline = Rect(11, 1, 2, 2) };
            var m = IntentMetricsCalculator.Measure(Lot(elements: new List<SiteElement> { pergola, seat }, buildings: new List<Building> { building }));
            // open ground 10×20 = 200; pergola 5×20 = 100; a bench footprint gives no shade
            Assert.Equal(50, m.CanopyCoverPercent!.Value, 1);
            Assert.Equal(100, m.ElementsSharePercent!.Value, 6);
        }

        [Fact]
        public void Zone_measures_its_own_outline_and_its_own_trees_and_elements()
        {
            var intent = Lot(
                new List<TreeIntent> { Tree("in", 5, 5, 4), Tree("out", 15, 15, 4), Tree("kept", 5, 5.5, 2, TreeStatus.Existing) },
                new List<SiteElement> { new SiteElement { Id = "e1", Kind = ElementKind.Play, Location = new Point2(15, 5) } });
            var zone = Rect(0, 0, 10, 10);
            var m = IntentMetricsCalculator.Measure(intent, zone);
            Assert.Equal(1, m.NewTreeCount);   // "out" is outside, "kept" is existing
            Assert.Equal(0, m.ElementCount);   // play element is outside the zone
            Assert.Equal(0, m.ElementsSharePercent!.Value, 6);
            // zone 100 m²: canopy of "in" (π·4); "kept" (π·1) lies wholly inside it, "out" lies outside the zone
            Assert.InRange(m.CanopyCoverPercent!.Value, 4 * Math.PI - 0.1, 4 * Math.PI + 0.1);
        }

        [Fact]
        public void No_lot_and_no_zone_is_not_measured()
        {
            var m = IntentMetricsCalculator.Measure(new DesignIntent { Trees = new List<TreeIntent> { Tree("a", 0, 0, 5) } });
            Assert.Null(m.CanopyCoverPercent);
            Assert.Equal(0, m.ElementsSharePercent!.Value, 6);
        }

        [Fact]
        public void Compare_uses_canopy_and_elements_share()
        {
            var m = new IntentMetrics { CanopyCoverPercent = 58, ElementsSharePercent = 45 };
            var rows = TargetComparison.Compare(m, new ResolvedCharacter(50, 50, 50, 50, 50, 65, 30, 50));
            var shading = rows.Single(r => r.Slider == "shading");
            Assert.Equal(TargetStatus.Met, shading.Status);      // 58 vs 65 ±10
            Assert.Equal(58, shading.Achieved!.Value, 6);
            var te = rows.Single(r => r.Slider == "treesElements");
            Assert.Equal(TargetStatus.Missed, te.Status);        // 45 vs 30 ±10
        }
    }
}
