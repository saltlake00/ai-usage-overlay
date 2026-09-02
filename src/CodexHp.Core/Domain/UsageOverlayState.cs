using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public sealed record GaugeDisplayState(
    int? RemainingPercent,
    double RefreshFraction,
    bool IsStale);

public sealed record UsageOverlayState(
    bool IsVisible,
    GaugeDisplayState ManaBar,
    GaugeDisplayState HpBar,
    IReadOnlyList<int> TokenBuckets,
    ColorValue? StatusStripeColor,
    string? StatusStripeTooltip)
{
    public IReadOnlyList<ProviderUsageRowState> ProviderRows { get; init; } = [];
}

public sealed record ProviderUsageRowState(
    string Id,
    string ShortLabel,
    int? ShortRemainingPercent,
    int? WeeklyRemainingPercent,
    bool IsStale,
    long? ShortTokens = null,
    long? WeeklyTokens = null);
