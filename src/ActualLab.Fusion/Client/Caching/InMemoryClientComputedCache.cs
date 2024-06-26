using ActualLab.Rpc.Caching;

namespace ActualLab.Fusion.Client.Caching;

public sealed class InMemoryClientComputedCache(
    InMemoryClientComputedCache.Options settings,
    IServiceProvider services
    ) : FlushingClientComputedCache(settings, services)
{
    public new sealed record Options : FlushingClientComputedCache.Options
    {
        public static new Options Default { get; set; } = new();
    }

    private static readonly ValueTask<TextOrBytes?> MissValueTask = new((TextOrBytes?)null);

    private readonly ConcurrentDictionary<RpcCacheKey, TextOrBytes> _cache = new();

    protected override ValueTask<TextOrBytes?> Fetch(RpcCacheKey key, CancellationToken cancellationToken)
        => _cache.TryGetValue(key, out var result)
            ? new ValueTask<TextOrBytes?>(result)
            : MissValueTask;

    protected override Task Flush(Dictionary<RpcCacheKey, TextOrBytes?> flushingQueue)
    {
        DefaultLog?.Log(Settings.LogLevel, "Flushing {Count} item(s)", flushingQueue.Count);
        foreach (var (key, result) in flushingQueue) {
            if (result is { } vResult)
                _cache[key] = vResult;
            else
                _cache.Remove(key, out _);
        }
        return Task.CompletedTask;
    }

    public override async Task Clear(CancellationToken cancellationToken = default)
    {
        await Flush().ConfigureAwait(false);
        _cache.Clear();
    }
}
