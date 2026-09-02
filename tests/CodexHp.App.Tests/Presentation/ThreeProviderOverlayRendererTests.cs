using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class ThreeProviderOverlayRendererTests
{
    [Fact]
    public void A_token_counted_row_shows_the_count_instead_of_a_percent_gauge()
    {
        var layout = UsageOverlayRenderer.CreateLayout(
            BaseState() with
            {
                ProviderRows =
                [
                    new ProviderUsageRowState(
                        "claude",
                        "A",
                        null,
                        null,
                        false,
                        ShortTokens: 460_292,
                        WeeklyTokens: 7_864_494),
                ],
            },
            AppSettings.Default,
            false);

        var texts = layout.Commands
            .Where(command => command.Kind == OverlayDrawKind.Text)
            .Select(command => command.Text)
            .ToArray();

        Assert.Contains(texts, text => text is not null && text.Contains("460.3K", StringComparison.Ordinal));
        Assert.Contains(texts, text => text is not null && text.Contains("7.9M", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text is not null && text.Contains("--%", StringComparison.Ordinal));

        // No fill is drawn: a bar would imply a limit the local count does not have.
        Assert.DoesNotContain(
            layout.Commands,
            command => command.Role is OverlayElementRole.ProviderShortFill or OverlayElementRole.ProviderWeeklyFill);
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1_500, "1.5K")]
    [InlineData(460_292, "460.3K")]
    [InlineData(7_864_494, "7.9M")]
    [InlineData(2_563_375_121, "2.6B")]
    public void FormatTokens_abbreviates_to_fit_the_row(long tokens, string expected) =>
        Assert.Equal(expected, UsageOverlayRenderer.FormatTokens(tokens));

    [Fact]
    public void Default_height_keeps_every_provider_element_inside_the_bitmap()
    {
        var layout = UsageOverlayRenderer.CreateLayout(
            BaseState() with
            {
                ProviderRows =
                [
                    new ProviderUsageRowState("codex", "C", 72, 48, false),
                    new ProviderUsageRowState("claude", "A", 51, 83, false),
                    new ProviderUsageRowState("ollama", "O", 64, 91, false),
                ],
            },
            AppSettings.Default,
            false);

        Assert.All(layout.Commands, command =>
        {
            Assert.InRange(command.Bounds.Top, 0, layout.Height);
            Assert.InRange(command.Bounds.Bottom, 0, layout.Height);
        });
    }

    [Fact]
    public void CreateLayout_renders_C_A_O_rows_with_short_and_weekly_remaining_percent()
    {
        var state = BaseState() with
        {
            ProviderRows =
            [
                new ProviderUsageRowState("codex", "C", 72, 48, false),
                new ProviderUsageRowState("claude", "A", 51, 83, false),
                new ProviderUsageRowState("ollama", "O", 64, 91, false),
            ],
        };

        var commands = UsageOverlayRenderer.CreateLayout(state, Settings(), false).Commands;

        Assert.Equal(["C", "A", "O"], commands
            .Where(command => command.Role == OverlayElementRole.ProviderLabel)
            .Select(command => command.Text));
        Assert.Equal(["5h 72%", "5h 51%", "단기 64%"], commands
            .Where(command => command.Role == OverlayElementRole.ProviderShortText)
            .Select(command => command.Text));
        Assert.Equal(["주간 48%", "주간 83%", "주간 91%"], commands
            .Where(command => command.Role == OverlayElementRole.ProviderWeeklyText)
            .Select(command => command.Text));
    }

    [Fact]
    public void CreateLayout_uses_warning_and_critical_colors_for_low_remaining_usage()
    {
        var state = BaseState() with
        {
            ProviderRows =
            [
                new ProviderUsageRowState("codex", "C", 31, 30, false),
                new ProviderUsageRowState("claude", "A", 30, 14, false),
                new ProviderUsageRowState("ollama", "O", 15, null, true),
            ],
        };

        var fills = UsageOverlayRenderer.CreateLayout(state, Settings(), false).Commands
            .Where(command => command.Role == OverlayElementRole.ProviderShortFill)
            .ToArray();

        Assert.Equal(ColorValue.Parse("#55C878"), fills[0].Color);
        Assert.Equal(ColorValue.Parse("#E4B84A"), fills[1].Color);
        Assert.Equal(ColorValue.Parse("#E45B5B"), fills[2].Color);
    }

    private static UsageOverlayState BaseState() => new(
        true,
        new GaugeDisplayState(null, 0, false),
        new GaugeDisplayState(null, 0, false),
        [],
        null,
        null);

    private static AppSettings Settings() => AppSettings.Default with
    {
        Appearance = new AppearanceSettings(288, 68, 100, 2, 0, 4),
    };
}
