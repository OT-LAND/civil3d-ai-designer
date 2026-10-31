using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Brief;
using CivilAI.Core.Claude;
using CivilAI.Core.Grading;
using CivilAI.Core.Intent;
using CivilAI.Core.Metrics;
using CivilAI.Core.Plan;
using CivilAI.Core.Rules;
using Xunit;

namespace CivilAI.Core.Tests.Plan
{
    // B.6d: the plan review levels the plan on existing ground; level problems and earthworks
    // misses join the repair loop, and the cut↔fill and max slope rows are measured.
    public class PlanGradingTests
    {
        private static readonly RuleSet Rules = RuleSetJson.LoadDefault();

        // Flat existing ground at 100.00.
        private static SpotGround Flat() => new SpotGround(new[]
        {
            new Point3(-50, -50, 100), new Point3(150, -50, 100), new Point3(-50, 150, 100), new Point3(150, 150, 100),
        });

        private static List<Point2> Square(double x0, double y0, double size) => new List<Point2>
        {
            new Point2(x0, y0), new Point2(x0 + size, y0), new Point2(x0 + size, y0 + size), new Point2(x0, y0 + size),
        };

        private static Space Lawn(string id, double x0, string? zone = null) =>
            new Space { Id = id, Kind = SpaceKind.Lawn, ZoneId = zone, Outline = Square(x0, 0, 10) };

        private static Space FixedPlaza(string id, double x0, double level, string? zone = null) => new Space
        {
            Id = id, Kind = SpaceKind.Plaza, ZoneId = zone, Outline = Square(x0, 0, 10),
            Level = new LevelIntent { Elevation = level, Fall = new Fall { Percent = 1.5, DirectionDegrees = 0 } },
        };

        // 20 m path along y = 50 with spot levels at both ends.
        private static Space Path(string id, double z0, double z1, string? zone = null) => new Space
        {
            Id = id, Kind = SpaceKind.Path, ZoneId = zone, Width = 2,
            Centerline = new List<Point2> { new Point2(0, 50), new Point2(20, 50) },
            Level = new LevelIntent { Spots = new List<SpotLevel> { new SpotLevel { X = 0, Y = 50, Z = z0 }, new SpotLevel { X = 20, Y = 50, Z = z1 } } },
        };

        private static DesignIntent Intent(params Space[] spaces) => new DesignIntent { Spaces = spaces.ToList() };

        private static DesignBrief Brief(int? cutFill = null) =>
            new DesignBrief { Character = new Character { CutFill = cutFill } };

        // Zone A (x 0–30, exports) and zone B (x 30–60, imports).
        private static DesignBrief TwoZones() => new DesignBrief
        {
            Zones = new List<Zone>
            {
                new Zone { Id = "A", Name = "Upper", Outline = Square(0, -10, 30), Character = new Character { CutFill = 20 } },
                new Zone { Id = "B", Name = "Lower", Outline = Square(30, -10, 30), Character = new Character { CutFill = 80 } },
            },
        };

        private static TargetResult Row(PlanReview review, string slider, int zone = 0) =>
            review.Zones[zone].Targets.Single(t => t.Slider == slider);

        [Fact]
        public void Without_ground_the_review_has_no_levels_and_grading_rows_stay_unmeasured()
        {
            var review = PlanStage.Review(Intent(Path("P1", 100, 102)), Brief(), Rules);

            Assert.Null(review.Grading);
            Assert.DoesNotContain(review.OpenIssues, i => i.StartsWith("levels"));
            Assert.Equal(TargetStatus.NotMeasured, Row(review, "cutFill").Status);
            Assert.Equal(TargetStatus.NotMeasured, Row(review, "maxSlope").Status);
        }

        [Fact]
        public void Too_steep_accessible_path_becomes_an_open_issue()
        {
            // 2 m over 20 m = 10 %, accessible limit 5 % (D10).
            var review = PlanStage.Review(Intent(Path("P1", 100, 102)), Brief(), Rules, null, Flat());

            Assert.NotNull(review.Grading);
            Assert.Contains(review.Grading!.Issues, g => g.Kind == GradingIssueKind.NeedsRamp && g.SpaceId == "P1");
            Assert.Contains(review.OpenIssues, i => i.StartsWith("levels on P1:"));
        }

        [Fact]
        public void Gentle_path_and_balanced_lawn_add_no_level_issues_and_measure_cut_fill()
        {
            var review = PlanStage.Review(Intent(Path("P1", 100, 100.5), Lawn("L", 30)), Brief(50), Rules, null, Flat());

            Assert.True(review.Grading!.AllPass);
            Assert.DoesNotContain(review.OpenIssues, i => i.StartsWith("levels") || i.Contains("Earthworks"));
            var cutFill = Row(review, "cutFill");
            Assert.NotEqual(TargetStatus.NotMeasured, cutFill.Status);
            Assert.NotNull(cutFill.Achieved);
        }

        [Fact]
        public void Not_accessible_path_measures_the_max_slope_row()
        {
            var steep = Path("P1", 100, 101.5); // 7.5 %
            var path = new Space
            {
                Id = steep.Id, Kind = steep.Kind, Width = steep.Width, Centerline = steep.Centerline,
                Level = steep.Level, Accessible = false,
            };
            var brief = new DesignBrief { Character = new Character { MaxSlope = 100 } };
            var review = PlanStage.Review(Intent(path), brief, Rules, null, Flat());

            var row = Row(review, "maxSlope");
            Assert.Equal(TargetStatus.Met, row.Status);
            Assert.InRange(row.Achieved!.Value, 30, 40); // (7.5 − 5) / (12.5 − 5) ≈ 33
        }

        [Fact]
        public void Each_zone_balances_on_its_own_cut_fill_slider()
        {
            var intent = Intent(Lawn("LA", 5, "A"), Lawn("LB", 40, "B"));
            var review = PlanStage.Review(intent, TwoZones(), Rules, null, Flat());

            var g = review.Grading!;
            Assert.True(g.Shifts["A"] < 0); // export: lowered
            Assert.True(g.Shifts["B"] > 0); // import: raised
            Assert.True(g.Areas.For("LA")!.CentreLevel < g.Areas.For("LB")!.CentreLevel);
            Assert.Equal(TargetStatus.Met, Row(review, "cutFill", 0).Status);
            Assert.Equal(TargetStatus.Met, Row(review, "cutFill", 1).Status);
            Assert.Equal(2, g.Earthworks.Spaces.Count);
        }

        [Fact]
        public void Fixed_levels_that_cannot_reach_the_slider_give_an_earthworks_issue()
        {
            // Raised fixed plaza is all fill; the slider asks for export and nothing is free to move.
            var review = PlanStage.Review(Intent(FixedPlaza("PL", 0, 100.30)), Brief(0), Rules, null, Flat());

            Assert.Single(review.Grading!.BalanceIssues);
            Assert.Contains(review.OpenIssues, i => i.StartsWith("the design: Earthworks:"));
            Assert.Equal(TargetStatus.Missed, Row(review, "cutFill").Status);
        }

        [Fact]
        public void Broken_references_block_the_levels()
        {
            var review = PlanStage.Review(Intent(Lawn("L", 0, "nope")), TwoZones(), Rules, null, Flat());

            Assert.NotEmpty(review.Problems);
            Assert.Null(review.Grading);
        }

        // ---- repair loop ------------------------------------------------------------------

        private static ClaudeResponse Reply(DesignIntent intent) => new()
        {
            Json = new JsonObject
            {
                ["concept"] = "Straight walk",
                ["assumptions"] = new JsonArray(),
                ["intent"] = JsonNode.Parse(DesignIntentJson.Serialize(intent)),
            }.ToJsonString(),
            StopReason = "tool_use", InputTokens = 100, OutputTokens = 50,
        };

        [Fact]
        public async Task Level_issues_go_back_to_Claude_and_the_fixed_plan_is_returned()
        {
            var replies = new Queue<ClaudeResponse>(new[] { Reply(Intent(Path("P1", 100, 102))), Reply(Intent(Path("P1", 100, 100.5))) });
            var requests = new List<ClaudeRequest>();
            Task<ClaudeResponse> Send(ClaudeRequest r, CancellationToken ct)
            {
                requests.Add(r);
                return Task.FromResult(replies.Dequeue());
            }

            var result = await new PlanStage(Send) { MaxAttempts = 2 }
                .GenerateAsync(new PlanRequest { Brief = Brief(), Ground = Flat() });

            Assert.Equal(2, requests.Count);
            Assert.Contains("levels on P1:", requests[1].Content.Single().Text);
            Assert.Equal(2, result.Attempts);
            Assert.NotNull(result.Review.Grading);
            Assert.Empty(result.Review.Grading!.Issues);
        }
    }
}
