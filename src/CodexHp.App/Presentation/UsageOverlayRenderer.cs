using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

public readonly record struct LayoutRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

public enum OverlayDrawKind
{
    Rectangle,
    Text,
}

public enum OverlayElementRole
{
    Background,
    StatusStripe,
    ManaTrack,
    ManaFill,
    ManaText,
    ManaRefreshTrack,
    ManaRefreshFill,
    HpTrack,
    HpFill,
    HpText,
    HpRefreshTrack,
    HpRefreshFill,
    GraphGridDot,
    GraphBaseline,
    TokenBar,
    OverlayPositionOutline,
    ProviderLabel,
    ProviderShortTrack,
    ProviderShortFill,
    ProviderShortText,
    ProviderWeeklyTrack,
    ProviderWeeklyFill,
    ProviderWeeklyText,
}

public sealed record OverlayDrawCommand(
    OverlayDrawKind Kind,
    OverlayElementRole Role,
    LayoutRect Bounds,
    ColorValue Color,
    double Opacity = 1,
    string? Text = null,
    int FontSize = 0);

public sealed record UsageOverlayLayout(
    int Width,
    int Height,
    IReadOnlyList<OverlayDrawCommand> Commands);

public sealed record OverlayPresentationSettings(
    ColorSettings Colors,
    EffectiveAppearanceSettings Appearance)
{
    public static OverlayPresentationSettings FromUnscaled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var appearance = settings.Appearance;
        return new OverlayPresentationSettings(
            settings.Colors,
            new EffectiveAppearanceSettings(
                appearance.OverlayWidth,
                appearance.OverlayHeight,
                appearance.GaugePaneWidth,
                appearance.GraphBarWidth,
                appearance.GraphBarGap,
                appearance.StatusStripeWidth));
    }
}

public static class UsageOverlayRenderer
{
    private const int RefreshHeight = 2;
    private const int RowGap = 2;
    private const double StaleOpacity = 0.55;
    private static readonly ColorValue BackgroundColor = ColorValue.Parse("#18181C");
    private static readonly ColorValue GaugeTrackColor = ColorValue.Parse("#3E3E44");
    private static readonly ColorValue RefreshTrackColor = ColorValue.Parse("#44464E");
    private static readonly ColorValue White = ColorValue.Parse("#FFFFFF");
    private static readonly ColorValue GridColor = ColorValue.Parse("#808080");
    private static readonly ColorValue HealthyColor = ColorValue.Parse("#55C878");
    private static readonly ColorValue WarningColor = ColorValue.Parse("#E4B84A");
    private static readonly ColorValue CriticalColor = ColorValue.Parse("#E45B5B");
    private static readonly ColorValue MutedTextColor = ColorValue.Parse("#9AA0AE");
    private static readonly IReadOnlyList<ColorValue> ProviderAccents =
    [
        ColorValue.Parse("#7C9CFF"),
        ColorValue.Parse("#D8935C"),
        ColorValue.Parse("#67C2A6"),
    ];

    public static UsageOverlayLayout CreateLayout(
        UsageOverlayState state,
        AppSettings settings,
        bool isOverlayPositionChangeMode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CreateLayout(state, OverlayPresentationSettings.FromUnscaled(settings), isOverlayPositionChangeMode);
    }

    public static UsageOverlayLayout CreateLayout(
        UsageOverlayState state,
        OverlayPresentationSettings settings,
        bool isOverlayPositionChangeMode)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        var appearance = settings.Appearance;
        var width = appearance.OverlayWidth;
        var height = appearance.OverlayHeight;
        var commands = new List<OverlayDrawCommand>
        {
            Rectangle(OverlayElementRole.Background, new LayoutRect(0, 0, width, height), BackgroundColor),
        };

        if (state.ProviderRows.Count > 0)
        {
            AddProviderColumns(commands, state.ProviderRows, width, height);
            if (isOverlayPositionChangeMode)
            {
                AddOverlayPositionOutline(commands, width, height);
            }

            return new UsageOverlayLayout(width, height, commands);
        }

        var gaugePaneWidth = Math.Clamp(appearance.GaugePaneWidth, 20, Math.Max(20, width - 20));
        var stripeOffset = 0;
        if (state.StatusStripeColor is { } stripeColor && appearance.StatusStripeWidth > 0)
        {
            var stripeBounds = new LayoutRect(4, 2, appearance.StatusStripeWidth, Math.Max(1, height - 4));
            commands.Add(Rectangle(OverlayElementRole.StatusStripe, stripeBounds, stripeColor));
            stripeOffset = appearance.StatusStripeWidth + 2;
        }

        var gaugeLeft = 4 + stripeOffset;
        var gaugeRight = gaugePaneWidth - 3;
        var gaugeTop = 2;
        var gaugeBottom = height - 2;
        var gaugeHeight = Math.Max(1, gaugeBottom - gaugeTop);
        var quotaHeight = Math.Max(
            1,
            (gaugeHeight - (RefreshHeight * 2) - (RowGap * 3)) / 2);
        var gaugeWidth = Math.Max(1, gaugeRight - gaugeLeft);
        var manaBounds = new LayoutRect(gaugeLeft, gaugeTop, gaugeWidth, quotaHeight);
        var manaRefreshBounds = new LayoutRect(
            gaugeLeft,
            manaBounds.Bottom + RowGap,
            gaugeWidth,
            RefreshHeight);
        var hpBounds = new LayoutRect(
            gaugeLeft,
            manaRefreshBounds.Bottom + RowGap,
            gaugeWidth,
            quotaHeight);
        var hpRefreshTop = hpBounds.Bottom + RowGap;
        var hpRefreshBounds = new LayoutRect(
            gaugeLeft,
            hpRefreshTop,
            gaugeWidth,
            Math.Max(1, Math.Min(RefreshHeight, gaugeBottom - hpRefreshTop)));
        var fontSize = Math.Clamp(quotaHeight + 5, 10, 16);

        AddGauge(
            commands,
            state.ManaBar,
            manaBounds,
            manaRefreshBounds,
            settings.Colors.ManaBar,
            settings.Colors.RefreshGauge,
            OverlayElementRole.ManaTrack,
            OverlayElementRole.ManaFill,
            OverlayElementRole.ManaText,
            OverlayElementRole.ManaRefreshTrack,
            OverlayElementRole.ManaRefreshFill,
            fontSize);
        AddGauge(
            commands,
            state.HpBar,
            hpBounds,
            hpRefreshBounds,
            settings.Colors.HpBar,
            settings.Colors.RefreshGauge,
            OverlayElementRole.HpTrack,
            OverlayElementRole.HpFill,
            OverlayElementRole.HpText,
            OverlayElementRole.HpRefreshTrack,
            OverlayElementRole.HpRefreshFill,
            fontSize);

        AddGraph(commands, state.TokenBuckets, settings, height);

        if (isOverlayPositionChangeMode)
        {
            AddOverlayPositionOutline(commands, width, height);
        }

        return new UsageOverlayLayout(width, height, commands);
    }

    // One column per provider instead of one stacked row each. Three stacked rows
    // left an 8px row and a 7px font at the default size; side-by-side columns give
    // the number the full height of the overlay, which is what the row is read for.
    private static void AddProviderColumns(
        ICollection<OverlayDrawCommand> commands,
        IReadOnlyList<ProviderUsageRowState> rows,
        int width,
        int height)
    {
        const int margin = 4;
        const int gap = 4;
        var count = Math.Max(1, rows.Count);
        var available = Math.Max(24, width - (margin * 2) - (gap * (count - 1)));
        var columnWidth = Math.Max(18, available / count);
        var usable = Math.Max(12, height - (margin * 2));

        // The overlay can be as short as the taskbar allows, so the lines are
        // budgeted out of the height that exists rather than sized independently
        // and allowed to spill past the bitmap.
        var showName = usable >= 22;
        var showWeekly = usable >= 40;
        var barHeight = usable >= 52 ? 2 : 0;
        var nameHeight = showName ? Math.Max(9, usable * 22 / 100) : 0;
        var weeklyHeight = showWeekly ? Math.Max(11, usable * 26 / 100) : 0;
        var valueHeight = Math.Max(9, usable - nameHeight - weeklyHeight - barHeight);
        var nameFont = Math.Clamp(nameHeight - 2, 7, 13);
        var valueFont = Math.Clamp(valueHeight - 3, 10, 26);
        var weeklyFont = Math.Clamp(weeklyHeight - 2, 8, 15);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var left = margin + (index * (columnWidth + gap));
            var opacity = row.IsStale ? StaleOpacity : 1;
            var accent = ProviderAccents[index % ProviderAccents.Count];
            var top = margin;

            if (showName)
            {
                commands.Add(new OverlayDrawCommand(
                    OverlayDrawKind.Text,
                    OverlayElementRole.ProviderLabel,
                    new LayoutRect(left, top, columnWidth, nameHeight),
                    accent,
                    opacity,
                    row.ShortLabel,
                    nameFont));
                top += nameHeight;
            }

            var shortText = FormatMeasure(row.ShortRemainingPercent, row.ShortTokens);
            commands.Add(new OverlayDrawCommand(
                OverlayDrawKind.Text,
                OverlayElementRole.ProviderShortText,
                new LayoutRect(left, top, columnWidth, valueHeight),
                row.ShortRemainingPercent is { } percent ? RiskColor(percent) : White,
                opacity,
                shortText,
                valueFont));
            top += valueHeight;

            // The bar only appears when a quota exists to fill it; a token count has
            // no denominator and a full-width track would imply one.
            if (barHeight > 0)
            {
                var barBounds = new LayoutRect(left, top, columnWidth, barHeight);
                commands.Add(Rectangle(OverlayElementRole.ProviderShortTrack, barBounds, GaugeTrackColor, opacity));
                if (row.ShortRemainingPercent is { } remaining)
                {
                    commands.Add(Rectangle(
                        OverlayElementRole.ProviderShortFill,
                        barBounds with { Width = Math.Max(1, columnWidth * Math.Clamp(remaining, 0, 100) / 100) },
                        RiskColor(remaining),
                        opacity));
                }

                top += barHeight;
            }

            if (showWeekly)
            {
                commands.Add(new OverlayDrawCommand(
                    OverlayDrawKind.Text,
                    OverlayElementRole.ProviderWeeklyText,
                    new LayoutRect(left, top, columnWidth, weeklyHeight),
                    // The weekly window is the one that runs out first in practice,
                    // so it carries the same risk colour as the headline number
                    // instead of sitting in muted grey.
                    row.WeeklyRemainingPercent is { } weekly ? RiskColor(weekly) : MutedTextColor,
                    opacity,
                    $"{row.WeeklyWindowLabel} {FormatMeasure(row.WeeklyRemainingPercent, row.WeeklyTokens)}",
                    weeklyFont));
            }
        }
    }

    private static ColorValue RiskColor(int remainingPercent) => remainingPercent <= 15
        ? CriticalColor
        : remainingPercent <= 30
            ? WarningColor
            : HealthyColor;

    private static string FormatMeasure(int? remainingPercent, long? tokens) => remainingPercent switch
    {
        { } percent => $"{Math.Clamp(percent, 0, 100)}%",
        null when tokens is { } count => FormatTokens(count),
        _ => "--",
    };

    // The overlay row is a few characters wide, so counts are abbreviated rather
    // than truncated by the text renderer.
    internal static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000_000 => $"{tokens / 1_000_000_000d:0.#}B",
        >= 1_000_000 => $"{tokens / 1_000_000d:0.#}M",
        >= 1_000 => $"{tokens / 1_000d:0.#}K",
        _ => tokens.ToString(),
    };

    private static void AddGauge(
        ICollection<OverlayDrawCommand> commands,
        GaugeDisplayState gauge,
        LayoutRect quotaBounds,
        LayoutRect refreshBounds,
        ColorValue quotaColor,
        ColorValue refreshColor,
        OverlayElementRole trackRole,
        OverlayElementRole fillRole,
        OverlayElementRole textRole,
        OverlayElementRole refreshTrackRole,
        OverlayElementRole refreshFillRole,
        int fontSize)
    {
        var opacity = gauge.IsStale ? StaleOpacity : 1;
        var remainingPercent = Math.Clamp(gauge.RemainingPercent ?? 0, 0, 100);
        var refreshFraction = Math.Clamp(gauge.RefreshFraction, 0, 1);
        commands.Add(Rectangle(trackRole, quotaBounds, GaugeTrackColor, opacity));
        commands.Add(Rectangle(
            fillRole,
            quotaBounds with { Width = quotaBounds.Width * remainingPercent / 100 },
            quotaColor,
            opacity));
        commands.Add(new OverlayDrawCommand(
            OverlayDrawKind.Text,
            textRole,
            quotaBounds,
            White,
            opacity,
            gauge.RemainingPercent is { } percent ? $"{Math.Clamp(percent, 0, 100)}%" : "--%",
            fontSize));
        commands.Add(Rectangle(refreshTrackRole, refreshBounds, RefreshTrackColor, opacity));
        commands.Add(Rectangle(
            refreshFillRole,
            refreshBounds with { Width = (int)Math.Floor(refreshBounds.Width * refreshFraction) },
            refreshColor,
            opacity));
    }

    private static void AddGraph(
        ICollection<OverlayDrawCommand> commands,
        IReadOnlyList<int> buckets,
        OverlayPresentationSettings settings,
        int overlayHeight)
    {
        var chartAppearance = new AppearanceSettings(
            settings.Appearance.OverlayWidth,
            settings.Appearance.OverlayHeight,
            settings.Appearance.GaugePaneWidth,
            settings.Appearance.GraphBarWidth,
            settings.Appearance.GraphBarGap,
            settings.Appearance.StatusStripeWidth);
        var chartLeft = TokenGraphViewport.ChartLeft(chartAppearance);
        var chartRight = TokenGraphViewport.ChartRight(chartAppearance);
        var chartTop = 4;
        var baselineTop = overlayHeight - 6;
        var chartBottom = baselineTop;
        if (chartRight <= chartLeft || chartBottom <= chartTop)
        {
            return;
        }

        var barWidth = Math.Max(1, settings.Appearance.GraphBarWidth);
        var gap = Math.Max(0, settings.Appearance.GraphBarGap);
        var slotWidth = barWidth + gap;
        const int bucketsPerFiveMinutes = 300 / TokenGraphViewport.BucketSeconds;
        for (var bucket = bucketsPerFiveMinutes; ; bucket += bucketsPerFiveMinutes)
        {
            var x = chartRight - (bucket * slotWidth);
            if (x < chartLeft)
            {
                break;
            }

            for (var y = chartTop; y < chartBottom; y += 4)
            {
                commands.Add(Rectangle(
                    OverlayElementRole.GraphGridDot,
                    new LayoutRect(x, y, 1, Math.Min(2, chartBottom - y)),
                    GridColor));
            }
        }

        commands.Add(Rectangle(
            OverlayElementRole.GraphBaseline,
            new LayoutRect(chartLeft, baselineTop, chartRight - chartLeft, 1),
            White));

        var maximumBucket = buckets.Count == 0 ? 0 : Math.Max(0, buckets.Max());
        var xPosition = chartRight - barWidth;
        if (maximumBucket <= 0)
        {
            return;
        }

        for (var index = buckets.Count - 1; index >= 0 && xPosition >= chartLeft; index--)
        {
            var value = Math.Max(0, buckets[index]);
            if (value > 0)
            {
                var barHeight = TokenGraphHeightScaler.Scale(
                    value,
                    maximumBucket,
                    chartBottom - chartTop);
                commands.Add(Rectangle(
                    OverlayElementRole.TokenBar,
                    new LayoutRect(xPosition, chartBottom - barHeight, barWidth, barHeight),
                    TokenColorInterpolator.Interpolate(
                        value,
                        settings.Colors.TokenLow,
                        settings.Colors.TokenHigh)));
            }

            xPosition -= slotWidth;
        }
    }

    private static void AddOverlayPositionOutline(
        ICollection<OverlayDrawCommand> commands,
        int width,
        int height)
    {
        const int thickness = 4;
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, 0, width, thickness), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, height - thickness, width, thickness), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, thickness, thickness, height - (thickness * 2)), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(width - thickness, thickness, thickness, height - (thickness * 2)), White));
    }

    private static OverlayDrawCommand Rectangle(
        OverlayElementRole role,
        LayoutRect bounds,
        ColorValue color,
        double opacity = 1) =>
        new(OverlayDrawKind.Rectangle, role, bounds, color, opacity);
}
