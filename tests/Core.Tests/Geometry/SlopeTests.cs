using CivilAI.Core.Geometry;
using Xunit;

namespace CivilAI.Core.Tests.Geometry
{
    public class SlopeTests
    {
        [Fact]
        public void Ratio_and_percent_describe_the_same_slope()
        {
            Assert.Equal(Slope.FromPercent(5), Slope.FromRatio(20));
            Assert.Equal(20.0, Slope.FromPercent(5).RatioRun, 6);
        }

        [Fact]
        public void Between_two_levels_gives_signed_slope()
        {
            var down = Slope.Between(startLevel: 10.30, endLevel: 10.00, horizontalDistance: 15.0);
            Assert.Equal(-2.0, down.Percent, 6);
            Assert.Equal(50.0, down.RatioRun, 6);
        }

        [Theory]
        [InlineData("5%", 5.0)]
        [InlineData(" -2.5 % ", -2.5)]
        [InlineData("1:15", 6.6666666667)]
        [InlineData("1/12", 8.3333333333)]
        public void Parses_designer_notation(string text, double expectedPercent)
        {
            Assert.True(Slope.TryParse(text, out var slope));
            Assert.Equal(expectedPercent, slope.Percent, 6);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("steep")]
        [InlineData("1:0")]
        [InlineData("0:15")]
        [InlineData("%")]
        public void Rejects_invalid_text(string? text)
        {
            Assert.False(Slope.TryParse(text, out _));
        }

        [Fact]
        public void IsWithin_ignores_direction_and_includes_the_limit()
        {
            var limit = Slope.FromRatio(15);
            Assert.True(Slope.FromRatio(15).IsWithin(limit));
            Assert.True(Slope.FromPercent(-6).IsWithin(limit));
            Assert.False(Slope.FromRatio(12).IsWithin(limit));
        }

        [Fact]
        public void Flat_slope_has_infinite_ratio_run()
        {
            Assert.True(double.IsPositiveInfinity(Slope.FromPercent(0).RatioRun));
        }

        [Fact]
        public void Invalid_inputs_throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Slope.FromRatio(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Slope.Between(0, 1, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Slope.FromRiseOverRun(double.NaN));
        }

        [Fact]
        public void Formats_as_percent()
        {
            Assert.Equal("6.67%", Slope.FromRatio(15).ToString());
        }
    }
}
