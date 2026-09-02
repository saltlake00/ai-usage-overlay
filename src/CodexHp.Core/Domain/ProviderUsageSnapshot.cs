namespace CodexHp.Core.Domain;

public sealed record UsageWindow(
    double RemainingPercent,
    DateTimeOffset? ResetsAt,
    TimeSpan? Duration)
{
    public static UsageWindow FromUsedPercent(
        double usedPercent,
        DateTimeOffset? resetsAt,
        TimeSpan? duration)
    {
        var normalizedUsed = double.IsFinite(usedPercent)
            ? Math.Clamp(usedPercent, 0, 100)
            : 0;
        return new UsageWindow(100 - normalizedUsed, resetsAt, duration);
    }
}

public sealed record ProviderUsageSnapshot(
    string Id,
    string Label,
    UsageWindow ShortWindow,
    UsageWindow WeeklyWindow,
    DateTimeOffset FetchedAt);
