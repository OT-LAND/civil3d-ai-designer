using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Grading;
using CivilAI.Core.Intent;
using CivilAI.Core.Rules;
using Xunit;

namespace CivilAI.Core.Tests.Grading
{
    // B.6a: path levels from hard constraints, checked against the accessible limit (D10/D11)
    // and the max slope slider for not-accessible paths (D19).
    public class PathLevelSolverTests
    {
        private static readonly RuleSet Rules = RuleSetJson.LoadDefault();

        private static Space Path(string id, double length, bool? accessible = null, string? zone = null, List<SpotLevel>? spots = null) =>
            new Space
            {
                Id = id, Kind = SpaceKind.Path, Width = 2,
                Centerline = new List<Point2> { new Point2(0, 0), new Point2(length / 2, 0), new Point2(length, 0) },
                Accessible = accessible, ZoneId = zone,
                Level = spots == null ? null : new LevelIntent { Spots = spots },
            };

        private static DesignIntent Intent(Space path, params Building[] buildings) => new DesignIntent
        {
            Site = new Site { Buildings = buildings.ToList() },
            Spaces = new List<Space> { path },
        };

        private static Building House(double ffl, double x, double y, double? threshold = null) => new Building
        {
            Id = "B1", Ffl = ffl,
            Footprint = new List<Point2> { new Point2(x, y + 1), new Point2(x + 5, y + 1), new Point2(x + 5, y + 6), new Point2(x, y + 6) },
            Entrances = new List<Entrance> { new Entrance { Id = "E1", Location = new Point2(x, y + 0.5), ThresholdLevel = threshold } },
        };

        private static DesignBrief Brief(List<TaggedPoint>? points = null, List<TaggedCurve>? curves = null, int maxSlope = 50, List<Zone>? zones = null) =>
            new DesignBrief
            {
                Character = new Character { MaxSlope = maxSlope },
                Points = points ?? new List<TaggedPoint>(),
                Curves = curves ?? new List<TaggedCurve>(),
                Zones = zones ?? new List<Zone>(),
            };

        private static TaggedPoint Elevation(string id, double x, double y, double level) =>
            new TaggedPoint { Id = id, Tag = PointTag.Elevation, At = new Point2(x, y), Level = level };

        [Fact]
        public void Entrance_and_elevation_point_give_an_even_grade_that_passes()
        {
            // Entrance at 101.00 near the start, elevation point 100.40 at the end, 20 m: 3%.
            var intent = Intent(Path("P1", 20), House(101.00, 0, 0));
            var brief = Brief(new List<TaggedPoint> { Elevation("P3", 20, 0.5, 100.40) });

            var result = PathLevelSolver.Solve(intent, brief, Rules);

            Assert.True(result.AllPass);
            var profile = result.For("P1")!;
            Assert.True(profile.Accessible);
            Assert.Equal(5.0, profile.LimitPercent);
            Assert.Equal(new[] { LevelSource.Entrance, LevelSource.ElevationPoint }, profile.Anchors.Select(a => a.Source));
            var stretch = Assert.Single(profile.Stretches);
            Assert.Equal(-3.0, stretch.GradePercent, 6);
            Assert.Equal(100.70, profile.Points.Single(p => p.Station == 10).Level, 6);
            Assert.Equal(3.0, profile.SteepestPercent!.Value, 6);
        }

        [Fact]
        public void Threshold_level_wins_over_the_ffl()
        {
            var intent = Intent(Path("P1", 20), House(101.00, 0, 0, threshold: 100.98));
            var result = PathLevelSolver.Solve(intent, Brief(new List<TaggedPoint> { Elevation("P3", 20, 0, 100.98) }), Rules);
            Assert.Equal(100.98, result.For("P1")!.Anchors[0].Level, 6);
            Assert.Equal(0.0, result.For("P1")!.Stretches.Single().GradePercent, 6);
        }

        [Fact]
        public void Accessible_path_too_steep_needs_a_ramp_with_the_length_that_works()
        {
            // 1.20 m over 15 m = 8% > 5%; a 24 m route would do it.
            var intent = Intent(Path("P1", 15), House(101.20, 0, 0));
            var brief = Brief(new List<TaggedPoint> { Elevation("P3", 15, 0, 100.00) });

            var result = PathLevelSolver.Solve(intent, brief, Rules);

            var issue = Assert.Single(result.Issues);
            Assert.Equal(GradingIssueKind.NeedsRamp, issue.Kind);
            Assert.Equal(8.0, issue.MeasuredPercent!.Value, 6);
            Assert.Equal(24.0, issue.RequiredLength!.Value, 6);
            Assert.Contains("needs a ramp", issue.Message);
            Assert.Contains("entrance E1", issue.Message);
            Assert.Contains("elevation point P3", issue.Message);
            Assert.Equal(new Point2(7.5, 0), issue.At);
        }

        [Theory]
        [InlineData(0, 5.0)]
        [InlineData(50, 8.75)]
        [InlineData(100, 12.5)]
        public void Max_slope_slider_runs_from_the_accessible_limit_to_the_rule_set_maximum(int slider, double expected)
        {
            Assert.Equal(expected, PathLevelSolver.MaxSlopePercent(slider, Rules)!.Value, 6);
        }

        [Fact]
        public void Not_accessible_path_uses_the_zone_max_slope_and_needs_steps_when_steeper()
        {
            // 1.0 m over 10 m = 10%: passes at slider 100 (12.5%), fails at the zone's 0 (5%).
            var zones = new List<Zone> { new Zone { Id = "Z1", Character = new Character { MaxSlope = 0 } } };
            var points = new List<TaggedPoint> { Elevation("A", 0, 0, 101.0), Elevation("B", 10, 0, 100.0) };

            var global = PathLevelSolver.Solve(Intent(Path("P1", 10, accessible: false)), Brief(points, maxSlope: 100), Rules);
            Assert.True(global.AllPass);
            Assert.False(global.For("P1")!.Accessible);
            Assert.Equal(12.5, global.For("P1")!.LimitPercent!.Value, 6);

            var zoned = PathLevelSolver.Solve(Intent(Path("P1", 10, accessible: false, zone: "Z1")), Brief(points, maxSlope: 100, zones: zones), Rules);
            var issue = Assert.Single(zoned.Issues);
            Assert.Equal(GradingIssueKind.NeedsSteps, issue.Kind);
            Assert.Contains("needs steps", issue.Message);
            Assert.Contains("max slope slider 0", issue.Message);
        }

        [Fact]
        public void Contour_with_a_level_is_hard_and_one_without_is_ignored()
        {
            var curves = new List<TaggedCurve>
            {
                new TaggedCurve { Id = "C1", Tag = CurveTag.Contour, Level = 100.50, Points = new List<Point2> { new Point2(6, -5), new Point2(6, 5) } },
                new TaggedCurve { Id = "C2", Tag = CurveTag.Contour, Points = new List<Point2> { new Point2(14, -5), new Point2(14, 5) } },
            };
            var points = new List<TaggedPoint> { Elevation("A", 0, 0, 100.20), Elevation("B", 20, 0, 101.20) };

            var profile = PathLevelSolver.Solve(Intent(Path("P1", 20)), Brief(points, curves), Rules).For("P1")!;

            Assert.Equal(3, profile.Anchors.Count);
            var contour = profile.Anchors[1];
            Assert.Equal(LevelSource.Contour, contour.Source);
            Assert.Equal(6.0, contour.Station, 6);
            Assert.Equal(2, profile.Stretches.Count);
        }

        [Fact]
        public void Ends_tie_into_existing_ground_when_given()
        {
            var ground = new SpotGround(new[]
            {
                new Point3(0, 0, 99.80), new Point3(20, 0, 100.30), new Point3(0, 10, 99.80), new Point3(20, 10, 100.30),
            });
            var points = new List<TaggedPoint> { Elevation("M", 10, 0, 100.00) };

            var profile = PathLevelSolver.Solve(Intent(Path("P1", 20)), Brief(points), Rules, ground).For("P1")!;

            Assert.Equal(new[] { LevelSource.ExistingGround, LevelSource.ElevationPoint, LevelSource.ExistingGround },
                profile.Anchors.Select(a => a.Source));
            Assert.Equal(99.80, profile.Anchors[0].Level, 6);
            Assert.Equal(100.30, profile.Anchors[2].Level, 6);
        }

        [Fact]
        public void Without_ground_the_ends_hold_the_nearest_fixed_level()
        {
            var profile = PathLevelSolver.Solve(Intent(Path("P1", 20)), Brief(new List<TaggedPoint> { Elevation("M", 10, 0, 100.00) }), Rules).For("P1")!;
            Assert.All(profile.Points, p => Assert.Equal(100.00, p.Level, 6));
            Assert.Equal(LevelSource.Held, profile.Anchors[0].Source);
        }

        [Fact]
        public void Path_with_nothing_to_fix_its_levels_is_reported()
        {
            var result = PathLevelSolver.Solve(Intent(Path("P1", 20)), Brief(), Rules);
            var issue = Assert.Single(result.Issues);
            Assert.Equal(GradingIssueKind.NoLevel, issue.Kind);
            Assert.False(result.For("P1")!.HasLevels);
        }

        [Fact]
        public void Points_away_from_the_path_do_not_count()
        {
            // Width 2 + margin 1 → snap 2 m; this point is 3 m off the centerline.
            var result = PathLevelSolver.Solve(Intent(Path("P1", 20)), Brief(new List<TaggedPoint> { Elevation("Far", 10, 3, 100.00) }), Rules);
            Assert.Equal(GradingIssueKind.NoLevel, Assert.Single(result.Issues).Kind);
        }

        [Fact]
        public void Disagreeing_levels_at_the_same_place_are_reported_and_the_spot_level_wins()
        {
            var spots = new List<SpotLevel> { new SpotLevel { X = 0, Y = 0, Z = 100.00 } };
            var brief = Brief(new List<TaggedPoint> { Elevation("A", 0, 0.05, 100.30), Elevation("B", 20, 0, 100.00) });

            var result = PathLevelSolver.Solve(Intent(Path("P1", 20, spots: spots)), brief, Rules);

            var issue = Assert.Single(result.Issues);
            Assert.Equal(GradingIssueKind.ConflictingLevels, issue.Kind);
            Assert.Equal(100.00, result.For("P1")!.Anchors[0].Level, 6);
        }

        [Fact]
        public void Spot_ground_interpolates_and_hits_survey_points_exactly()
        {
            var ground = new SpotGround(new[] { new Point3(0, 0, 100), new Point3(10, 0, 102) });
            Assert.Equal(100, ground.LevelAt(new Point2(0, 0))!.Value, 9);
            Assert.Equal(101, ground.LevelAt(new Point2(5, 0))!.Value, 9);
            Assert.Null(new SpotGround(new Point3[0]).LevelAt(new Point2(0, 0)));
        }

        [Fact]
        public void Sample_courtyard_solves_without_throwing()
        {
            var root = FindRepoRoot();
            Assert.True(DesignIntentJson.TryParse(File.ReadAllText(Path2(root, "samples/intent/courtyard.v1.json")), out var intent, out var e1), e1);
            Assert.True(DesignBriefJson.TryParse(File.ReadAllText(Path2(root, "samples/brief/courtyard.brief.v1.json")), out var brief, out var e2), e2);

            var result = PathLevelSolver.Solve(intent!, brief, Rules);

            Assert.Equal(intent!.Spaces.Count(s => s.Kind == SpaceKind.Path), result.Profiles.Count);
            Assert.All(result.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Message)));
        }

        private static string Path2(string root, string rel) => System.IO.Path.Combine(root, rel);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "CivilAI.sln"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }
}
