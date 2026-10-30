using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Brief;
using CivilAI.Core.Grading;
using CivilAI.Core.Intent;
using CivilAI.Core.Metrics;
using CivilAI.Core.Rules;
using Xunit;

namespace CivilAI.Core.Tests.Grading
{
    // B.6c: cut/fill of the solved levels against existing ground, balanced toward the
    // cut↔fill slider; cut↔fill and max slope rows on the target-vs-achieved card.
    public class EarthworksTests
    {
        private static readonly RuleSet Rules = RuleSetJson.LoadDefault();

        // Flat existing ground at 100.00.
        private static SpotGround Flat() => new SpotGround(new[]
        {
            new Point3(-50, -50, 100), new Point3(50, -50, 100), new Point3(-50, 50, 100), new Point3(50, 50, 100),
        });

        private sealed class NoGround : IGroundModel
        {
            public double? LevelAt(Point2 at) => null;
        }

        // 10 × 10 m square at (x0, 0).
        private static Space Area(string id, SpaceKind kind, LevelIntent? level = null, double x0 = 0) => new Space
        {
            Id = id, Kind = kind, Level = level,
            Outline = new List<Point2> { new Point2(x0, 0), new Point2(x0 + 10, 0), new Point2(x0 + 10, 10), new Point2(x0, 10) },
        };

        private static LevelIntent Fixed(double elevation, double fall = 0, double dir = 0) =>
            new LevelIntent { Elevation = elevation, Fall = new Fall { Percent = fall, DirectionDegrees = dir } };

        private static DesignIntent Intent(params Space[] spaces) => new DesignIntent { Spaces = spaces.ToList() };

        private static EarthworkResult Measure(DesignIntent intent, IGroundModel ground) =>
            Earthworks.Measure(intent, AreaLevelSolver.Solve(intent, null, Rules, ground),
                PathLevelSolver.Solve(intent, null, Rules, ground), ground);

        // Path along y = 50 from x = 0 to 20, 2 m wide, with spot levels at both ends.
        private static Space Path(string id, double z0, double z1, bool? accessible = null) => new Space
        {
            Id = id, Kind = SpaceKind.Path, Width = 2, Accessible = accessible,
            Centerline = new List<Point2> { new Point2(0, 50), new Point2(20, 50) },
            Level = new LevelIntent { Spots = new List<SpotLevel> { new SpotLevel { X = 0, Y = 50, Z = z0 }, new SpotLevel { X = 20, Y = 50, Z = z1 } } },
        };

        [Fact]
        public void Raised_flat_plaza_is_all_fill()
        {
            var earth = Measure(Intent(Area("PL", SpaceKind.Plaza, Fixed(100.10))), Flat());

            var pl = earth.For("PL")!;
            Assert.Equal(10.0, pl.Fill, 3); // 100 m² × 0.10 m
            Assert.Equal(0.0, pl.Cut, 3);
            Assert.Equal(100.0, pl.MeasuredArea, 3);
            Assert.False(pl.Free); // the designer gave its level
            Assert.Equal(100.0, earth.FillSharePercent!.Value, 3);
            Assert.Equal(10.0, earth.Net, 3);
        }

        [Fact]
        public void Falling_plaza_through_ground_at_its_centre_balances()
        {
            // 2 % east through 100.00 at the centre: ±0.10 m at the edges.
            var earth = Measure(Intent(Area("PL", SpaceKind.Plaza, Fixed(100.00, 2.0, 0))), Flat());

            Assert.Equal(2.5, earth.Cut, 2);
            Assert.Equal(2.5, earth.Fill, 2);
            Assert.Equal(50.0, earth.FillSharePercent!.Value, 1);
        }

        [Fact]
        public void Path_volume_is_profile_times_width()
        {
            var earth = Measure(Intent(Path("P1", 100.20, 100.20)), Flat());

            var p = earth.For("P1")!;
            Assert.Equal(SpaceKind.Path, p.Kind);
            Assert.Equal(8.0, p.Fill, 3); // 20 m × 2 m × 0.20 m
            Assert.Equal(40.0, p.MeasuredArea, 3);
        }

        [Fact]
        public void Ground_without_data_is_counted_as_not_measured()
        {
            var intent = Intent(Area("PL", SpaceKind.Plaza, Fixed(100.10)));
            var areas = AreaLevelSolver.Solve(intent, null, Rules);
            var earth = Earthworks.Measure(intent, areas, null, new NoGround());

            Assert.Equal(100.0, earth.UnmeasuredArea, 3);
            Assert.Null(earth.FillSharePercent);
        }

        [Fact]
        public void Balanced_target_leaves_a_balanced_lawn_where_it_is()
        {
            var intent = Intent(Area("L", SpaceKind.Lawn));
            var areas = AreaLevelSolver.Solve(intent, null, Rules, Flat());
            var bal = Earthworks.Balance(intent, areas, null, Flat(), 50);

            Assert.True(bal.Earthworks.For("L")!.Free);
            Assert.Equal(0.0, bal.Shift, 3);
            Assert.Null(bal.Issue);
            Assert.Equal(50.0, bal.Earthworks.FillSharePercent!.Value, 1);
        }

        [Fact]
        public void Import_target_raises_the_free_lawn_only_as_far_as_needed()
        {
            // Lawn on flat ground takes the 1 % minimum fall: ±0.05 m across 10 m.
            var intent = Intent(Area("L", SpaceKind.Lawn));
            var areas = AreaLevelSolver.Solve(intent, null, Rules, Flat());
            var bal = Earthworks.Balance(intent, areas, null, Flat(), 100);

            Assert.InRange(bal.Shift, 0.045, 0.051);
            Assert.Null(bal.Issue);
            Assert.True(bal.Earthworks.FillSharePercent > 99);
            var before = areas.For("L")!;
            var after = bal.Areas.For("L")!;
            Assert.Equal(before.CentreLevel + bal.Shift, after.CentreLevel, 6);
            Assert.Equal(before.FallPercent, after.FallPercent, 6);
            Assert.Equal(before.Corners[0].Z + bal.Shift, after.Corners[0].Z, 6);
        }

        [Fact]
        public void Export_target_lowers_the_free_lawn_and_keeps_fixed_spaces()
        {
            // Fixed plaza adds 10 m³ fill; the free lawn must go down to make the design mostly cut.
            var intent = Intent(Area("PL", SpaceKind.Plaza, Fixed(100.10)), Area("L", SpaceKind.Lawn, null, 20));
            var areas = AreaLevelSolver.Solve(intent, null, Rules, Flat());
            var bal = Earthworks.Balance(intent, areas, null, Flat(), 30);

            Assert.True(bal.Shift < 0);
            Assert.Equal(0.0, bal.Earthworks.For("PL")!.Shift);
            Assert.Equal(areas.For("PL")!.CentreLevel, bal.Areas.For("PL")!.CentreLevel, 6);
            Assert.Equal(30.0, bal.Earthworks.FillSharePercent!.Value, 0.5);
            Assert.Null(bal.Issue);
        }

        [Fact]
        public void Target_out_of_reach_says_how_far_the_free_spaces_moved()
        {
            // 100 m² plaza fixed 0.50 m high (50 m³ fill); the 100 m² lawn can sink at most 0.30 m (30 m³ cut).
            var intent = Intent(Area("PL", SpaceKind.Plaza, Fixed(100.50)), Area("L", SpaceKind.Lawn, null, 20));
            var areas = AreaLevelSolver.Solve(intent, null, Rules, Flat());
            var bal = Earthworks.Balance(intent, areas, null, Flat(), 50);

            Assert.Equal(-0.30, bal.Shift, 3);
            Assert.NotNull(bal.Issue);
            Assert.Contains("0.30 m", bal.Issue);
            Assert.Contains("to bring in", bal.Issue);
        }

        [Fact]
        public void Nothing_free_to_move_is_explained()
        {
            var intent = Intent(Area("PL", SpaceKind.Plaza, Fixed(100.10)));
            var bal = Earthworks.Balance(intent, AreaLevelSolver.Solve(intent, null, Rules, Flat()), null, Flat(), 50);

            Assert.Equal(0.0, bal.Shift);
            Assert.Contains("every space has a fixed level", bal.Issue);
        }

        [Theory]
        [InlineData(5.0, 0.0)]
        [InlineData(12.5, 100.0)]
        [InlineData(8.75, 50.0)]
        [InlineData(3.0, 0.0)]
        [InlineData(15.5, 140.0)]
        public void Max_slope_slider_reads_back_the_path_slope(double slope, double slider)
        {
            Assert.Equal(slider, GradingMetrics.SliderForSlope(slope, Rules)!.Value, 6);
        }

        [Fact]
        public void Card_measures_cut_fill_and_max_slope_rows()
        {
            // Not-accessible path rising 1.5 m over 20 m = 7.5 % → slider 33.3.
            var intent = Intent(Area("PL", SpaceKind.Plaza, Fixed(100.10)), Path("P1", 100.00, 101.50, accessible: false));
            var paths = PathLevelSolver.Solve(intent, null, Rules, Flat());
            var earth = Earthworks.Measure(intent, AreaLevelSolver.Solve(intent, null, Rules, Flat()), paths, Flat());
            var g = GradingMetrics.Measure(intent, earth, paths, Rules);

            Assert.Equal("P1", g.SteepestNotAccessibleSpaceId);
            Assert.Equal(7.5, g.SteepestNotAccessiblePercent!.Value, 6);
            Assert.Equal(100.0 / 3, g.MaxSlopeSlider!.Value, 3);

            var rows = TargetComparison.Compare(new IntentMetrics(), new ResolvedCharacter(50, 50, 50, 100, 30, 50, 50, 50), grading: g);
            var cutFill = rows.Single(r => r.Slider == "cutFill");
            Assert.Equal(TargetStatus.Met, cutFill.Status); // all fill, slider 100 = import
            var maxSlope = rows.Single(r => r.Slider == "maxSlope");
            Assert.Equal(TargetStatus.Met, maxSlope.Status); // 33.3 ≤ 30 + 5
            Assert.Equal(TargetStatus.NotMeasured, rows.Single(r => r.Slider == "maxGrade").Status); // B.6e, D23

            var strict = TargetComparison.Compare(new IntentMetrics(), new ResolvedCharacter(50, 50, 50, 50, 20, 50, 50, 50), grading: g);
            Assert.Equal(TargetStatus.Missed, strict.Single(r => r.Slider == "cutFill").Status);
            Assert.Equal(TargetStatus.Missed, strict.Single(r => r.Slider == "maxSlope").Status);
        }

        [Fact]
        public void Zone_outline_picks_its_spaces()
        {
            var intent = Intent(Area("PL", SpaceKind.Plaza, Fixed(100.10)), Area("L", SpaceKind.Plaza, Fixed(99.90), 20));
            var earth = Earthworks.Measure(intent, AreaLevelSolver.Solve(intent, null, Rules, Flat()), null, Flat());
            var east = new List<Point2> { new Point2(15, -5), new Point2(40, -5), new Point2(40, 15), new Point2(15, 15) };

            var g = GradingMetrics.Measure(intent, earth, null, Rules, east);
            Assert.Equal(10.0, g.Cut, 3);
            Assert.Equal(0.0, g.Fill, 3);
            Assert.Equal(0.0, g.FillSharePercent!.Value, 6);
            Assert.Null(g.MaxSlopeSlider);
        }
    }
}
