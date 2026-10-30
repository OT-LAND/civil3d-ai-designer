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
    // B.6b: area spaces (plaza, lawn, bed) get one even surface through their hard levels,
    // with the preferred fall where the hard levels leave it free, checked against the rule set.
    public class AreaLevelSolverTests
    {
        private static readonly RuleSet Rules = RuleSetJson.LoadDefault();

        // 10 × 10 m square, centre (5, 5).
        private static Space Area(string id, SpaceKind kind, LevelIntent? level = null) => new Space
        {
            Id = id, Kind = kind,
            Outline = new List<Point2> { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) },
            Level = level,
        };

        private static DesignIntent Intent(Space space, params Building[] buildings) => new DesignIntent
        {
            Site = new Site { Buildings = buildings.ToList() },
            Spaces = new List<Space> { space },
        };

        // Building west of the square, entrance on its west edge at (0, 5).
        private static Building House(double ffl) => new Building
        {
            Id = "B1", Ffl = ffl,
            Footprint = new List<Point2> { new Point2(-10, 0), new Point2(-1, 0), new Point2(-1, 10), new Point2(-10, 10) },
            Entrances = new List<Entrance> { new Entrance { Id = "E1", Location = new Point2(-0.5, 5) } },
        };

        private static DesignBrief Brief(params TaggedPoint[] points) => new DesignBrief { Points = points.ToList() };

        private static TaggedPoint Elevation(string id, double x, double y, double level) =>
            new TaggedPoint { Id = id, Tag = PointTag.Elevation, At = new Point2(x, y), Level = level };

        // Ground falling 4% to the east: 100.00 at x = 0, 99.60 at x = 10.
        private static SpotGround EastFall() => new SpotGround(new[]
        {
            new Point3(-20, -20, 100.80), new Point3(30, -20, 98.80), new Point3(-20, 30, 100.80), new Point3(30, 30, 98.80),
            new Point3(0, 0, 100.00), new Point3(10, 0, 99.60), new Point3(0, 10, 100.00), new Point3(10, 10, 99.60),
            new Point3(5, 5, 99.80),
        });

        [Fact]
        public void Plaza_at_an_entrance_falls_away_from_the_building_at_the_design_fall()
        {
            var result = AreaLevelSolver.Solve(Intent(Area("PL", SpaceKind.Plaza), House(101.00)), null, Rules);

            Assert.True(result.AllPass);
            var plaza = result.For("PL")!;
            Assert.Equal(LevelSource.Entrance, Assert.Single(plaza.Anchors).Source);
            Assert.Equal(FallSource.RuleSet, plaza.FallSource);
            Assert.Equal(1.5, plaza.FallPercent, 3);
            Assert.Equal(0.0, plaza.FallDirectionDegrees, 3); // east, away from the house
            Assert.Equal(101.00, plaza.LevelAt(new Point2(-0.5, 5)), 3);
            Assert.Equal(100.85, plaza.LevelAt(new Point2(9.5, 5)), 3);
            Assert.Equal(4, plaza.Corners.Count);
        }

        [Fact]
        public void Designer_fall_is_kept()
        {
            var level = new LevelIntent { Elevation = 50, Fall = new Fall { Percent = 2.0, DirectionDegrees = 90 } };
            var plaza = AreaLevelSolver.Solve(Intent(Area("PL", SpaceKind.Plaza, level)), null, Rules).For("PL")!;

            Assert.Equal(FallSource.Designer, plaza.FallSource);
            Assert.Equal(50.0, plaza.CentreLevel, 3);
            Assert.Equal(2.0, plaza.FallPercent, 3);
            Assert.Equal(90.0, plaza.FallDirectionDegrees, 3);
            Assert.Equal(50.10, plaza.LevelAt(new Point2(5, 0)), 3); // south is uphill
        }

        [Fact]
        public void Lawn_follows_existing_ground()
        {
            var result = AreaLevelSolver.Solve(Intent(Area("L1", SpaceKind.Lawn)), null, Rules, EastFall());

            Assert.True(result.AllPass);
            var lawn = result.For("L1")!;
            Assert.Equal(FallSource.ExistingGround, lawn.FallSource);
            Assert.Equal(99.80, lawn.CentreLevel, 2);
            Assert.Equal(4.0, lawn.FallPercent, 1);
            Assert.Equal(0.0, lawn.FallDirectionDegrees, 0);
        }

        [Fact]
        public void Flat_ground_lawn_gets_the_minimum_fall()
        {
            var flat = new SpotGround(new[] { new Point3(0, 0, 20), new Point3(10, 0, 20), new Point3(10, 10, 20), new Point3(0, 10, 20) });
            var lawn = AreaLevelSolver.Solve(Intent(Area("L1", SpaceKind.Lawn)), null, Rules, flat).For("L1")!;
            Assert.Equal(1.0, lawn.FallPercent, 3);
            Assert.Equal(20.0, lawn.CentreLevel, 3);
        }

        [Fact]
        public void Two_hard_levels_fix_the_fall_along_them()
        {
            // Entrance 101.00 at the west edge, elevation point 100.90 at the east edge: 1% east.
            var result = AreaLevelSolver.Solve(
                Intent(Area("PL", SpaceKind.Plaza), House(101.00)), Brief(Elevation("P1", 9.5, 5, 100.90)), Rules);

            var plaza = result.For("PL")!;
            Assert.Equal(2, plaza.Anchors.Count);
            Assert.Equal(101.00, plaza.LevelAt(new Point2(-0.5, 5)), 3);
            Assert.Equal(100.90, plaza.LevelAt(new Point2(9.5, 5)), 3);
            Assert.Equal(1.0, plaza.FallPercent, 2);
            Assert.True(result.AllPass);
        }

        [Fact]
        public void Plaza_held_flat_by_its_levels_is_too_flat_to_drain()
        {
            var result = AreaLevelSolver.Solve(
                Intent(Area("PL", SpaceKind.Plaza), House(101.00)), Brief(Elevation("P1", 9.5, 5, 101.00)), Rules);

            var issue = Assert.Single(result.Issues);
            Assert.Equal(GradingIssueKind.FallTooFlat, issue.Kind);
            Assert.Equal(1.0, issue.LimitPercent);
            Assert.Contains("needs at least 1.0% to drain", issue.Message);
        }

        [Fact]
        public void Plaza_forced_steeper_than_its_max_needs_steps()
        {
            // 0.50 m over 10 m = 5%.
            var result = AreaLevelSolver.Solve(
                Intent(Area("PL", SpaceKind.Plaza), House(101.00)), Brief(Elevation("P1", 9.5, 5, 100.50)), Rules);

            var issue = Assert.Single(result.Issues);
            Assert.Equal(GradingIssueKind.TooSteep, issue.Kind);
            Assert.Equal(5.0, issue.MeasuredPercent!.Value, 2);
            Assert.Contains("steps or terraces", issue.Message);
        }

        [Fact]
        public void Lawn_steeper_than_mowing_limit_needs_a_wall_or_terraces()
        {
            // 3 m over 10 m = 30% > 25%.
            var brief = Brief(Elevation("A", 0, 5, 103), Elevation("B", 10, 5, 100));
            var result = AreaLevelSolver.Solve(Intent(Area("L1", SpaceKind.Lawn)), brief, Rules);

            var issue = Assert.Single(result.Issues);
            Assert.Equal(GradingIssueKind.TooSteep, issue.Kind);
            Assert.Equal(25.0, issue.LimitPercent);
            Assert.Contains("retaining wall or terraces", issue.Message);
        }

        [Fact]
        public void Bed_has_no_minimum_fall()
        {
            var brief = Brief(Elevation("A", 0, 5, 10), Elevation("B", 10, 5, 10));
            var result = AreaLevelSolver.Solve(Intent(Area("BD", SpaceKind.Bed)), brief, Rules);
            Assert.True(result.AllPass);
            Assert.Null(result.For("BD")!.MinFallPercent);
        }

        [Fact]
        public void Levels_that_do_not_share_one_surface_conflict()
        {
            // Three corners on a 1% plane, the fourth 0.30 m off it.
            var brief = Brief(
                Elevation("A", 0, 0, 10.00), Elevation("B", 10, 0, 9.90),
                Elevation("C", 0, 10, 10.00), Elevation("D", 10, 10, 10.20));
            var result = AreaLevelSolver.Solve(Intent(Area("PL", SpaceKind.Plaza)), brief, Rules);

            var issue = result.Issues.Single(i => i.Kind == GradingIssueKind.ConflictingLevels);
            Assert.Contains("do not lie on one even surface", issue.Message);
            Assert.Equal(FallSource.HardLevels, result.For("PL")!.FallSource);
        }

        [Fact]
        public void Contour_with_a_level_crossing_the_space_is_a_hard_level()
        {
            var brief = new DesignBrief
            {
                Curves = new List<TaggedCurve>
                {
                    new TaggedCurve
                    {
                        Id = "C1", Tag = CurveTag.Contour, Level = 30.00,
                        Points = new List<Point2> { new Point2(5, -5), new Point2(5, 15) },
                    },
                },
            };
            var plaza = AreaLevelSolver.Solve(Intent(Area("PL", SpaceKind.Plaza)), brief, Rules).For("PL")!;

            Assert.All(plaza.Anchors, a => Assert.Equal(LevelSource.Contour, a.Source));
            Assert.Equal(2, plaza.Anchors.Count); // crossings of the south and north edges
            Assert.Equal(30.00, plaza.LevelAt(new Point2(5, 0)), 3);
            Assert.Equal(30.00, plaza.LevelAt(new Point2(5, 10)), 3);
            Assert.Equal(1.5, plaza.FallPercent, 2); // free across the contour
        }

        [Fact]
        public void Points_away_from_the_space_are_ignored()
        {
            var level = new LevelIntent { Elevation = 50 };
            var plaza = AreaLevelSolver.Solve(
                Intent(Area("PL", SpaceKind.Plaza, level)), Brief(Elevation("FAR", 30, 30, 70)), Rules).For("PL")!;
            Assert.Empty(plaza.Anchors);
            Assert.Equal(50.0, plaza.CentreLevel, 3);
        }

        [Fact]
        public void Nothing_to_sit_on_is_reported()
        {
            var result = AreaLevelSolver.Solve(Intent(Area("L1", SpaceKind.Lawn)), null, Rules);
            Assert.Empty(result.Areas);
            Assert.Equal(GradingIssueKind.NoLevel, Assert.Single(result.Issues).Kind);
        }

        [Fact]
        public void Paths_are_left_to_the_path_solver()
        {
            var path = new Space { Id = "P", Kind = SpaceKind.Path, Centerline = new List<Point2> { new Point2(0, 0), new Point2(10, 0) } };
            Assert.Empty(AreaLevelSolver.Solve(Intent(path), null, Rules, EastFall()).Areas);
        }

        [Fact]
        public void Sample_courtyard_solves_every_area_space()
        {
            var root = FindRepoRoot();
            Assert.True(DesignIntentJson.TryParse(File.ReadAllText(Path.Combine(root, "samples/intent/courtyard.v1.json")), out var intent, out var e1), e1);
            Assert.True(DesignBriefJson.TryParse(File.ReadAllText(Path.Combine(root, "samples/brief/courtyard.brief.v1.json")), out var brief, out var e2), e2);

            var result = AreaLevelSolver.Solve(intent!, brief, Rules);

            var areas = intent!.Spaces.Count(s => s.Kind != SpaceKind.Path && s.Outline != null && s.Outline.Count >= 3);
            Assert.Equal(areas, result.Areas.Count + result.Issues.Count(i => i.Kind == GradingIssueKind.NoLevel));
            Assert.All(result.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Message)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CivilAI.sln"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }
}
