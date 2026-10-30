using CivilAI.Core.Ui;
using Xunit;

namespace CivilAI.Core.Tests.Ui
{
    public class PaletteThemeTests
    {
        [Theory]
        [InlineData((short)0)]
        [InlineData(0)]
        [InlineData("0")]
        public void Colortheme_0_is_dark_with_light_text(object value)
        {
            var c = PaletteTheme.ForColorTheme(value);
            Assert.True(c.IsDark);
            Assert.Same(PaletteTheme.Dark, c);
        }

        [Theory]
        [InlineData((short)1)]
        [InlineData(1)]
        [InlineData(" 1 ")]
        public void Colortheme_1_is_light_with_dark_text(object value)
        {
            var c = PaletteTheme.ForColorTheme(value);
            Assert.False(c.IsDark);
            Assert.Same(PaletteTheme.Light, c);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("dark")]
        [InlineData(7)]
        public void Missing_or_unknown_value_falls_back_to_dark(object? value)
        {
            Assert.Same(PaletteTheme.Dark, PaletteTheme.ForColorTheme(value));
        }

        [Fact]
        public void Text_is_readable_in_both_themes()
        {
            foreach (var c in new[] { PaletteTheme.Dark, PaletteTheme.Light })
            {
                // WCAG AA: 4.5 for normal text. Hints too, since they are real instructions.
                Assert.True(PaletteTheme.ContrastRatio(c.Foreground, c.Background) >= 7, $"{c} main text");
                Assert.True(PaletteTheme.ContrastRatio(c.Muted, c.Background) >= 4.5, $"{c} hint text");
            }
        }

        [Fact]
        public void Bug_0_3a_black_on_dark_background_fails_the_contrast_check()
        {
            Assert.True(PaletteTheme.ContrastRatio("#000000", PaletteTheme.Dark.Background) < 3);
            Assert.Equal(21, PaletteTheme.ContrastRatio("#000000", "#FFFFFF"), 6);
        }
    }
}
