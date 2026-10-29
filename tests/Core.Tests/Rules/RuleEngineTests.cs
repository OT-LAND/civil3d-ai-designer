using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivilAI.Core.Intent;
using CivilAI.Core.Rules;
using Xunit;

namespace CivilAI.Core.Tests.Rules
{
    public class RuleSetTests
    {
        [Fact]
        public void Default_rule_set_loads_with_every_known_rule()
        {
            var set = RuleSetJson.LoadDefault();
            Assert.Equal("default-il", set.Id);
            foreach (var id in RuleIds.All)
            {
                var rule = set.Find(id);
                Assert.NotNull(rule);
                Assert.False(string.IsNullOrWhiteSpace(rule!.Source), $"{id} needs a source note");
                if (id != RuleIds.PathNotAccessibleMaxSlopePercent) // provisional until D22
                    Assert.True(rule.Verified, $"{id} was confirmed by the owner (D10)");
            }
            Assert.Equal(5.0, set.Find(RuleIds.PathMaxLongitudinalSlopePercent)!.Value);
        }

        [Fact]
        public void Unknown_rule_id_is_rejected_with_the_known_list()
        {
            const string json = """
            { "ruleSetVersion": "0", "id": "x", "name": "X",
              "rules": [ { "id": "path.maxSlop", "value": 5 } ] }
            """;
            Assert.False(RuleSetJson.TryParse(json, out _, out var errors));
            Assert.Contains(errors, e => e.Contains("path.maxSlop") && e.Contains(RuleIds.PathMaxCrossFallPercent));
        }

        [Fact]
        public void Duplicate_negative_and_version_problems_are_all_reported()
        {
            const string json = """
            { "ruleSetVersion": "9", "id": "", "name": "X",
              "rules": [ { "id": "lawn.maxSlopePercent", "value": 25 },
                         { "id": "lawn.maxSlopePercent", "value": -1 } ] }
            """;
            Assert.False(RuleSetJson.TryParse(json, out _, out var errors));
            Assert.Contains(errors, e => e.Contains("ruleSetVersion"));
            Assert.Contains(errors, e => e.StartsWith("id is required"));
            Assert.Contains(errors, e => e.Contains("more than once"));
            Assert.Contains(errors, e => e.Contains("zero or more"));
        }

        [Fact]
        public void Unknown_field_and_bad_json_fail_cleanly()
        {
            Assert.False(RuleSetJson.TryParse("""{ "id": "x", "name": "X", "colour": 1 }""", out _, out var e1));
            Assert.Single(e1);
            Assert.False(RuleSetJson.TryParse("{ not json", out _, out var e2));
            Assert.Single(e2);
            Assert.Throws<FormatException>(() => RuleSetJson.Parse("null"));
        }
    }

    public class RuleEngineTests
    {
        private static readonly RuleSet Rules = RuleSetJson.LoadDefault();

        private static DesignIntent Sample()
        {
            var json = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "courtyard.v0.json"));
            Assert.True(DesignIntentJson.TryParse(json, out var intent, out var error), error);
            return intent!;
        }

        private static DesignIntent With(params Space[] spaces) => new DesignIntent { Spaces = spaces.ToList() };

        private static List<Point2> Square(double size) => new List<Point2>
        {
            new Point2(0, 0), new Point2(size, 0), new Point2(size, size), new Point2(0, size),
        };

        private static Space Path(List<SpotLevel> spots, Fall? fall = null, List<Point2>? line = null) => new Space
        {
            Id = "p1", Kind = SpaceKind.Path, Name = "Route",
            Centerline = line ?? new List<Point2> { new Point2(0, 0), new Point2(20, 0) },
            Width = 1.5,
            Level = new LevelIntent { Spots = spots, Fall = fall },
        };

        private static RuleResult Result(RuleReport report, string ruleId) =>
            Assert.Single(report.Results, r => r.RuleId == ruleId);

        [Fact]
        public void Courtyard_sample_passes_or_is_not_yet_checked()
        {
            var report = RuleEngine.Check(Sample(), Rules);
            Assert.True(report.AllPass, string.Join("\n", report.Failures.Select(f => f.Message)));

            var route = Result(report, RuleIds.PathMaxLongitudinalSlopePercent);
            Assert.Equal(RuleStatus.Pass, route.Status);
            Assert.Equal(0.66 / 17 * 100, route.Measured!.Value, 6);

            Assert.Equal(RuleStatus.Pass, Result(report, RuleIds.PlazaMinFallPercent).Status);
            Assert.Equal(RuleStatus.NotChecked, Result(report, RuleIds.LawnMaxSlopePercent).Status);
            Assert.Equal(RuleStatus.NotChecked, Result(report, RuleIds.PathMaxCrossFallPercent).Status);
        }

        [Fact]
        public void Steep_route_fails_at_the_steepest_stretch_regardless_of_spot_order()
        {
            // 0→10 m rises 0.3 (3%), 10→20 m rises 0.8 (8%); spots given out of order.
            var report = RuleEngine.Check(With(Path(new List<SpotLevel>
            {
                new SpotLevel { X = 20, Y = 0.2, Z = 11.1 },
                new SpotLevel { X = 0, Y = 0, Z = 10.0 },
                new SpotLevel { X = 10, Y = -0.3, Z = 10.3 },
            })), Rules);

            var r = Result(report, RuleIds.PathMaxLongitudinalSlopePercent);
            Assert.Equal(RuleStatus.Fail, r.Status);
            Assert.Equal(8.0, r.Measured!.Value, 6);
            Assert.Equal(15.0, r.Location!.Value.X, 6);
            Assert.Contains("steeper than", r.Message);
            Assert.False(report.AllPass);
        }

        [Fact]
        public void Path_marked_not_accessible_skips_the_accessible_route_rules()
        {
            // Same 8% stretch as above, but the designer marked the path not accessible (D11).
            var steep = Path(new List<SpotLevel>
            {
                new SpotLevel { X = 0, Y = 0, Z = 10.0 }, new SpotLevel { X = 20, Y = 0, Z = 11.6 },
            }, new Fall { Percent = 3, DirectionDegrees = 90 });
            var service = new Space
            {
                Id = steep.Id, Kind = steep.Kind, Name = steep.Name, Centerline = steep.Centerline,
                Width = steep.Width, Level = steep.Level, Accessible = false,
            };

            var report = RuleEngine.Check(With(service), Rules);
            Assert.True(report.AllPass);
            foreach (var id in new[] { RuleIds.PathMaxLongitudinalSlopePercent, RuleIds.PathMaxCrossFallPercent })
            {
                var r = Result(report, id);
                Assert.Equal(RuleStatus.NotChecked, r.Status);
                Assert.Contains("not part of the accessible route", r.Message);
            }

            // Omitted or true: still an accessible route, so it fails.
            Assert.False(RuleEngine.Check(With(steep), Rules).AllPass);
        }

        [Fact]
        public void Station_follows_a_bent_centerline()
        {
            var line = new List<Point2> { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) };
            Assert.Equal(5.0, RuleEngine.Station(line, new Point2(5, 1)), 6);
            Assert.Equal(14.0, RuleEngine.Station(line, new Point2(11, 4)), 6);
            Assert.Equal(20.0, RuleEngine.Station(line, new Point2(10, 15)), 6);
        }

        [Fact]
        public void Fall_across_a_path_is_split_into_along_and_cross_parts()
        {
            // Path runs east; 3% fall towards 60° → along 1.5%, across ~2.6%.
            var report = RuleEngine.Check(With(Path(new List<SpotLevel>(), new Fall { Percent = 3, DirectionDegrees = 60 })), Rules);

            var along = Result(report, RuleIds.PathMaxLongitudinalSlopePercent);
            Assert.Equal(RuleStatus.Pass, along.Status);
            Assert.Equal(1.5, along.Measured!.Value, 6);

            var cross = Result(report, RuleIds.PathMaxCrossFallPercent);
            Assert.Equal(RuleStatus.Fail, cross.Status);
            Assert.Equal(3 * Math.Sin(Math.PI / 3), cross.Measured!.Value, 6);
        }

        [Fact]
        public void Flat_plaza_fails_drainage_and_steep_plaza_fails_accessibility()
        {
            Space Plaza(string id, double pct) => new Space
            {
                Id = id, Kind = SpaceKind.Plaza, Outline = Square(10),
                Level = new LevelIntent { Elevation = 10, Fall = new Fall { Percent = pct, DirectionDegrees = 0 } },
            };
            var report = RuleEngine.Check(With(Plaza("flat", 0.5), Plaza("steep", 3)), Rules);

            var flatMin = report.Results.Single(r => r.SpaceId == "flat" && r.RuleId == RuleIds.PlazaMinFallPercent);
            Assert.Equal(RuleStatus.Fail, flatMin.Status);
            Assert.Contains("flatter than", flatMin.Message);
            Assert.Equal(new Point2(5, 5), flatMin.Location);

            Assert.Equal(RuleStatus.Fail, report.Results.Single(r => r.SpaceId == "steep" && r.RuleId == RuleIds.PlazaMaxFallPercent).Status);
            Assert.Equal(RuleStatus.Pass, report.Results.Single(r => r.SpaceId == "steep" && r.RuleId == RuleIds.PlazaMinFallPercent).Status);
        }

        [Fact]
        public void Lawn_and_bed_slopes_are_measured_from_spot_levels()
        {
            var lawn = new Space
            {
                Id = "lawn", Kind = SpaceKind.Lawn, Outline = Square(10),
                Level = new LevelIntent { Spots = new List<SpotLevel>
                {
                    new SpotLevel { X = 0, Y = 0, Z = 10 }, new SpotLevel { X = 10, Y = 0, Z = 13 },
                } },
            };
            var bed = new Space
            {
                Id = "bed", Kind = SpaceKind.Bed, Outline = Square(10),
                Level = new LevelIntent { Fall = new Fall { Percent = 20, DirectionDegrees = 90 } },
            };
            var report = RuleEngine.Check(With(lawn, bed), Rules);

            var lawnMax = report.Results.Single(r => r.SpaceId == "lawn" && r.RuleId == RuleIds.LawnMaxSlopePercent);
            Assert.Equal(RuleStatus.Fail, lawnMax.Status);
            Assert.Equal(30.0, lawnMax.Measured!.Value, 6);
            Assert.Equal(new Point2(5, 0), lawnMax.Location);
            // Spots alone cannot prove a minimum fall.
            Assert.Equal(RuleStatus.NotChecked, report.Results.Single(r => r.SpaceId == "lawn" && r.RuleId == RuleIds.LawnMinFallPercent).Status);

            Assert.Equal(RuleStatus.Pass, Result(report, RuleIds.BedMaxSlopePercent).Status);
        }

        [Fact]
        public void Rule_left_out_of_the_set_is_reported_as_not_checked()
        {
            var set = RuleSetJson.Parse("""
            { "ruleSetVersion": "0", "id": "office", "name": "Office",
              "rules": [ { "id": "path.maxCrossFallPercent", "value": 2.5 } ] }
            """);
            var report = RuleEngine.Check(With(Path(new List<SpotLevel>
            {
                new SpotLevel { X = 0, Y = 0, Z = 0 }, new SpotLevel { X = 20, Y = 0, Z = 4 },
            })), set);

            var r = Result(report, RuleIds.PathMaxLongitudinalSlopePercent);
            Assert.Equal(RuleStatus.NotChecked, r.Status);
            Assert.Contains("no 'path.maxLongitudinalSlopePercent' rule", r.Message);
            Assert.Equal("office", report.RuleSetId);
        }
    }
}
