using System.IO;
using System.Text.Json;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure;

internal sealed class ProviderUsageCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public ProviderUsageCache(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIUsageOverlay",
            "usage-cache.json");
    }

    public async Task SaveAsync(
        IReadOnlyList<ProviderUsageSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(this.path)
            ?? throw new InvalidOperationException("Usage cache path has no directory.");
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(this.path);
        await JsonSerializer.SerializeAsync(stream, snapshots, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderUsageSnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(this.path))
        {
            return [];
        }

        await using var stream = File.OpenRead(this.path);
        return await JsonSerializer.DeserializeAsync<ProviderUsageSnapshot[]>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];
    }
}
