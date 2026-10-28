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
    public class IntentMetricsTests
    {
        private static DesignIntent Sample()
        {
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v0.json"));
            Assert.True(DesignIntentJson.TryParse(json, out var intent, out var error), error);
            return intent!;
        }

        private static List<Point2> Rect(double x0, double y0, double w, double h) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + w, y0), new Point2(x0 + w, y0 + h), new Point2(x0, y0 + h),
        };

        private static List<Point2> Circle(double cx, double cy, double r, int n) =>
            Enumerable.Range(0, n).Select(i => new Point2(cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n))).ToList();

        [Fact]
        public void Courtyard_sample_hard_soft_and_paths()
        {
            var m = IntentMetricsCalculator.Measure(Sample());
            // plaza 12×8 = 96, path 17 × 1.8 = 30.6; lawn 16.5×11 = 181.5, bed 12×18 = 216
            Assert.Equal(126.6, m.HardArea, 6);
            Assert.Equal(397.5, m.SoftArea, 6);
            Assert.Equal(100 * 397.5 / 524.1, m.SoftSharePercent!.Value, 6);
            Assert.Equal(17, m.PathLength, 6);
            Assert.Equal(100, m.AccessibleSharePercent!.Value, 6);
            Assert.Equal(100, m.StraightSharePercent!.Value, 6);
            Assert.Equal(0, m.CanopyCoverPercent!.Value, 6); // v0: lot boundary, no trees or shade
            Assert.Null(m.ElementsSharePercent);
            Assert.Equal(4, m.SpaceIds.Count);
        }

        [Fact]
        public void Courtyard_sample_steepest_slopes()
        {
            var m = IntentMetricsCalculator.Measure(Sample());
            Assert.Equal("path-street", m.SteepestAccessiblePath!.Value.SpaceId);
            Assert.Equal(0.66 / 17 * 100, m.SteepestAccessiblePath.Value.Slope.Percent, 6);
            Assert.Equal(1.5, m.SteepestPlaza!.Value.Slope.Percent, 6);
            Assert.Null(m.SteepestOtherPath);
            Assert.Null(m.SteepestGround); // bed has an elevation but no spots or fall
        }

        [Fact]
        public void Material_category_overrides_kind()
        {
            var intent = new DesignIntent
            {
                Spaces =
                {
                    new Space { Id = "gravel-bed", Kind = SpaceKind.Bed, Outline = Rect(0, 0, 10, 10), MaterialId = "gravel" },
                    new Space { Id = "lawn-plaza", Kind = SpaceKind.Plaza, Outline = Rect(20, 0, 10, 10), MaterialId = "turf" },
                    new Space { Id = "plain-lawn", Kind = SpaceKind.Lawn, Outline = Rect(40, 0, 10, 10) },
                },
                Materials =
                {
                    new Material { Id = "gravel", Name = "Gravel", Category = MaterialCategory.Gravel },
                    new Material { Id = "turf", Name = "Turf", Category = MaterialCategory.Lawn },
                },
            };
            var m = IntentMetricsCalculator.Measure(intent);
            Assert.Equal(100, m.HardArea, 6);
            Assert.Equal(200, m.SoftArea, 6);
        }

        [Fact]
        public void Not_accessible_paths_are_measured_separately()
        {
            var intent = new DesignIntent
            {
                Spaces =
                {
                    new Space
                    {
                        Id = "ramp", Kind = SpaceKind.Path, Width = 1.5,
                        Centerline = new List<Point2> { new Point2(0, 0), new Point2(30, 0) },
                        Level = new LevelIntent { Spots = { new SpotLevel { X = 0, Y = 0, Z = 10 }, new SpotLevel { X = 30, Y = 0, Z = 11.5 } } },
                    },
                    new Space
                    {
                        Id = "trail", Kind = SpaceKind.Path, Width = 1.0, Accessible = false,
                        Centerline = new List<Point2> { new Point2(0, 10), new Point2(10, 10) },
                        Level = new LevelIntent { Spots = { new SpotLevel { X = 0, Y = 10, Z = 10 }, new SpotLevel { X = 10, Y = 10, Z = 11 } } },
                    },
                    new Space
                    {
                        Id = "berm", Kind = SpaceKind.Lawn, Outline = Rect(0, 20, 10, 10),
                        Level = new LevelIntent { Fall = new Fall { Percent = 20, DirectionDegrees = 90 } },
                    },
                },
            };
            var m = IntentMetricsCalculator.Measure(intent);
            Assert.Equal(40, m.PathLength, 6);
            Assert.Equal(75, m.AccessibleSharePercent!.Value, 6);
            Assert.Equal(5, m.SteepestAccessiblePath!.Value.Slope.Percent, 6);
            Assert.Equal("trail", m.SteepestOtherPath!.Value.SpaceId);
            Assert.Equal(10, m.SteepestOtherPath.Value.Slope.Percent, 6);
            Assert.Equal(20, m.SteepestGround!.Value.Slope.Percent, 6);
        }

        [Fact]
        public void Tessellated_circle_is_curved_rectangle_is_straight()
        {
            var circle = PolygonGeometry.CurvedStraightLength(Circle(0, 0, 5, 36), true, 0.5, 30);
            Assert.Equal(0, circle.Straight, 6);
            Assert.True(circle.Curved > 31);

            var rect = PolygonGeometry.CurvedStraightLength(Rect(0, 0, 10, 4), true, 0.5, 30);
            Assert.Equal(0, rect.Curved, 6);
            Assert.Equal(28, rect.Straight, 6);
        }

        [Fact]
        public void Single_slight_kink_stays_straight()
        {
            var line = new List<Point2> { new Point2(0, 0), new Point2(10, 0), new Point2(20, 1.5) };
            var r = PolygonGeometry.CurvedStraightLength(line, false, 0.5, 30);
            Assert.Equal(0, r.Curved, 6);
        }

        [Fact]
        public void Zone_outline_limits_measured_spaces()
        {
            var zone = new List<Point2> { new Point2(-10, -25), new Point2(9, -25), new Point2(9, -8.5), new Point2(-10, -8.5) };
            var m = IntentMetricsCalculator.Measure(Sample(), zone);
            // Lawn centre (0.25, -14.5) is inside; path midpoint (10, -16.5) and the others are not.
            Assert.Equal(new[] { "lawn-west" }, m.SpaceIds);
            Assert.Equal(100, m.SoftSharePercent!.Value, 6);
            Assert.Null(m.AccessibleSharePercent);
        }

        [Fact]
        public void Polygon_area_and_contains()
        {
            Assert.Equal(12, PolygonGeometry.SignedArea(Rect(0, 0, 4, 3)), 9);
            Assert.True(PolygonGeometry.Contains(Rect(0, 0, 4, 3), new Point2(2, 1)));
            Assert.False(PolygonGeometry.Contains(Rect(0, 0, 4, 3), new Point2(5, 1)));
        }

        [Fact]
        public void Compare_reports_met_missed_and_not_measured()
        {
            var m = IntentMetricsCalculator.Measure(Sample()); // soft 75.8 %, straight 100 %, accessible 100 %
            var c = Character.AllAt(50);
            var brief = new DesignBrief { Character = new Character { Accessibility = 100, HardSoft = 75, MaxGrade = 50, CutFill = 50, MaxSlope = 50, Shading = 50, TreesElements = 50, CurvedStraight = 50, Budget = 50 } };
            var rows = TargetComparison.Compare(m, brief.CharacterFor(new Zone()));

            Assert.Equal(9, rows.Count);
            Assert.Equal(c.Sliders().Select(s => s.Name), rows.Select(r => r.Slider));
            Assert.Equal(TargetStatus.Met, rows.Single(r => r.Slider == "accessibility").Status);
            Assert.Equal(TargetStatus.Met, rows.Single(r => r.Slider == "hardSoft").Status);
            var curved = rows.Single(r => r.Slider == "curvedStraight");
            Assert.Equal(TargetStatus.Missed, curved.Status);
            Assert.Equal(100, curved.Achieved!.Value, 6);
            Assert.Equal(TargetStatus.Missed, rows.Single(r => r.Slider == "shading").Status); // 0 % canopy
            Assert.Equal(TargetStatus.NotMeasured, rows.Single(r => r.Slider == "treesElements").Status);
            Assert.Equal(TargetStatus.NotMeasured, rows.Single(r => r.Slider == "cutFill").Status);
            Assert.Equal(TargetStatus.NotMeasured, rows.Single(r => r.Slider == "budget").Status); // cost classes come in B.1b
        }

        [Fact]
        public void Accessibility_above_target_is_never_a_miss_but_below_is()
        {
            var full = new IntentMetrics { AccessibleSharePercent = 100 };
            var low = new IntentMetrics { AccessibleSharePercent = 60 };
            var ch = new ResolvedCharacter(80, 50, 50, 50, 50, 50, 50, 50);
            Assert.Equal(TargetStatus.Met, TargetComparison.Compare(full, ch)[0].Status);
            Assert.Equal(TargetStatus.Missed, TargetComparison.Compare(low, ch)[0].Status);
        }

        [Fact]
        public void Hard_soft_tolerance_is_five_points()
        {
            var ch = new ResolvedCharacter(50, 60, 50, 50, 50, 50, 50, 50);
            Assert.Equal(TargetStatus.Met, TargetComparison.Compare(new IntentMetrics { SoftSharePercent = 65 }, ch)[1].Status);
            Assert.Equal(TargetStatus.Missed, TargetComparison.Compare(new IntentMetrics { SoftSharePercent = 65.5 }, ch)[1].Status);
        }
    }
}
