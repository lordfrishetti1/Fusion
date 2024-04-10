using ActualLab.OS;
using ActualLab.Resilience;
using Microsoft.EntityFrameworkCore;

namespace ActualLab.Fusion.EntityFramework.LogProcessing;

public enum DbLogProcessingMode
{
    Cooperative = 0, // Every reader processes each entry - used for operation log / invalidations
    Exclusive = 1, // Just a single reader processes each entry - used for outbox items
}

public abstract record DbLogProcessorOptions
{
    public int BatchSize { get; init; } = 128;
    public RandomTimeSpan GapRetryDelay { get; init; } = TimeSpan.FromSeconds(0.1).ToRandom(0.1);
    public IRetryPolicy GapRetryPolicy { get; init; } = null!;
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(1, 5);
    public RandomTimeSpan ForcedCheckPeriod { get; init; } = TimeSpan.FromSeconds(5).ToRandom(0.1);
    public int ProcessConcurrencyLevel { get; init; } = HardwareInfo.GetProcessorCountFactor(4);
    public LogLevel LogLevel { get; init; } = LogLevel.Information;
}

public abstract record CooperativeDbLogProcessorOptions : DbLogProcessorOptions
{
    public TimeSpan StartOffset { get; init; } = TimeSpan.FromSeconds(3);

    protected CooperativeDbLogProcessorOptions()
    {
        GapRetryPolicy = new RetryPolicy(
            10, TimeSpan.FromSeconds(30),
            RetryDelaySeq.Exp(0.25, 1, 0.1, 2)); // Up to 1 second, 2x longer on each iteration
    }
}

public abstract record ExclusiveDbLogProcessorOptions : DbLogProcessorOptions
{
    protected ExclusiveDbLogProcessorOptions()
    {
        GapRetryPolicy = new RetryPolicy(
            TimeSpan.FromMinutes(5),
            RetryDelaySeq.Exp(0.25, 1, 0.1, 2)); // Up to 1 second, 2x longer on each iteration
    }
}

public static class DbLogProcessor
{
    public static DbHint[] CooperativeReadHints { get; set; } = DbHintSet.Empty;
    public static DbHint[] ExclusiveReadHints { get; set; } =  DbHintSet.UpdateSkipLocked;

    public static DbHint[] GetProcessQueryHints(DbLogProcessingMode mode)
        => mode is DbLogProcessingMode.Cooperative
            ? CooperativeReadHints
            : ExclusiveReadHints;
}

public interface IDbLogProcessor;

public abstract class DbLogProcessor<TDbContext, TDbEntry, TOptions>(
    TOptions settings,
    IServiceProvider services)
    : DbShardWorkerBase<TDbContext>(services), IDbLogProcessor
    where TDbContext : DbContext
    where TDbEntry : class, ILogEntry
    where TOptions : DbLogProcessorOptions
{
    protected ConcurrentDictionary<DbShard, long> NextIndexes { get; } = new();
    protected Dictionary<(DbShard Shard, long Index), Task> ProcessGapTasks { get; } = new();

    protected IDbLogWatcher<TDbContext, TDbEntry> LogWatcher { get; } = services.DbLogWatcher<TDbContext, TDbEntry>();
    protected IMomentClock SystemClock { get; init; } = services.Clocks().SystemClock;
    protected ILogger? DefaultLog => Log.IfEnabled(Settings.LogLevel);
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public TOptions Settings { get; } = settings;
    public DbLogProcessingMode Mode { get; } = settings switch {
        CooperativeDbLogProcessorOptions => DbLogProcessingMode.Cooperative,
        ExclusiveDbLogProcessorOptions => DbLogProcessingMode.Exclusive,
        _ => throw new ArgumentOutOfRangeException(nameof(settings))
    };

    protected abstract Task Process(DbShard shard, TDbEntry entry, CancellationToken cancellationToken);

    protected override Task OnRun(DbShard shard, CancellationToken cancellationToken)
        => new AsyncChain($"{nameof(ProcessNewEntries)}[{shard}]", ct => ProcessNewEntries(shard, ct))
            .RetryForever(Settings.RetryDelays, SystemClock, Log)
            .CycleForever()
            .Log(Log)
            .Start(cancellationToken);

    protected virtual Task WhenChanged(DbShard shard, CancellationToken cancellationToken)
        => LogWatcher.WhenChanged(shard, cancellationToken);

    protected virtual async Task ProcessNewEntries(DbShard shard, CancellationToken cancellationToken)
    {
        var timeoutCts = cancellationToken.CreateLinkedTokenSource();
        try {
            var timeoutTask = SystemClock.Delay(Settings.ForcedCheckPeriod.Next(), timeoutCts.Token);
            // WhenEntriesAdded should be invoked before we start reading!
            var whenEntriesAdded = await Task
                .WhenAny(WhenChanged(shard, timeoutCts.Token), timeoutTask)
                .ConfigureAwait(false);
            while (true) { // Reading entries in batches
                var mustContinue = await ProcessBatch(shard, cancellationToken).ConfigureAwait(false);
                if (!mustContinue)
                    break;
            }
            await whenEntriesAdded.ConfigureAwait(false);
        }
        finally {
            // We have to cancel timeoutCts to abort WhenEntriesAdded & timeoutTask
            timeoutCts.CancelAndDisposeSilently();
        }
    }

    protected virtual async Task<bool> ProcessBatch(DbShard shard, CancellationToken cancellationToken)
    {
        var nextIndexOpt = await TryGetNextIndex(shard, cancellationToken).ConfigureAwait(false);
        if (nextIndexOpt is not { } nextIndex)
            return false; // The log is empty

        using var _ = ActivitySource.StartActivity().AddShardTags(shard);
        var dbContext = await DbHub.CreateDbContext(shard, readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var _2 = tx.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        var batchSize = Settings.BatchSize;
        var entries = await dbContext.Set<TDbEntry>(DbLogProcessor.GetProcessQueryHints(Mode))
            // ReSharper disable once AccessToModifiedClosure
            .Where(o => o.Index >= nextIndex)
            .OrderBy(o => o.Index)
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var logLevel = entries.Count == batchSize ? LogLevel.Warning : LogLevel.Debug;
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.IfEnabled(logLevel)?.Log(logLevel,
            $"{nameof(ProcessBatch)}[{{Shard}}]: fetched {{Count}}/{{BatchSize}} log entries with Index >= {{LastIndex}}",
            shard.Value, entries.Count, batchSize, nextIndex);

        if (entries.Count == 0)
            return false;

        await GetProcessTasks(shard, entries, nextIndex, cancellationToken)
            .Collect(Settings.ProcessConcurrencyLevel)
            .ConfigureAwait(false);
        nextIndex = entries[^1].Index + 1;

        if (Mode is DbLogProcessingMode.Exclusive) {
            var dbEntries = dbContext.Set<TDbEntry>();
            foreach (var entry in entries) {
                dbEntries.Attach(entry);
                MarkProcessed(dbEntries, entry);
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        NextIndexes[shard] = nextIndex;
        return entries.Count >= batchSize;
    }

    protected async ValueTask<long?> TryGetNextIndex(DbShard shard, CancellationToken cancellationToken)
    {
        if (NextIndexes.TryGetValue(shard, out var nextIndex))
            return nextIndex;

        var startEntry = await GetStartEntry(shard, cancellationToken).ConfigureAwait(false);
        if (startEntry == null)
            return null;

        nextIndex = NextIndexes.GetOrAdd(shard, startEntry.Index);
        DefaultLog?.Log(Settings.LogLevel,
            $"{nameof(ProcessNewEntries)}[{{Shard}}]: starting from #{{StartIndex}}",
            shard, nextIndex);
        return nextIndex;
    }

    protected virtual async Task<TDbEntry?> GetStartEntry(DbShard shard, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(shard, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        var candidateEntries = dbContext.Set<TDbEntry>().AsQueryable();
        if (Settings is CooperativeDbLogProcessorOptions cooperativeSettings) {
            var minLoggedAt = SystemClock.Now.ToDateTime() - cooperativeSettings.StartOffset;
            candidateEntries = candidateEntries.Where(e => e.LoggedAt >= minLoggedAt);
        }
        else
            candidateEntries = candidateEntries.Where(e => !e.IsProcessed);
        return await candidateEntries
            .OrderBy(e => e.Index)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    protected virtual IEnumerable<Task> GetProcessTasks(
        DbShard shard, List<TDbEntry> entries, long nextIndex, CancellationToken cancellationToken)
    {
        foreach (var entry in entries) {
            while (nextIndex != entry.Index)
                ProcessGapSafe(shard, nextIndex++, entry.LoggedAt, null);
            yield return ProcessSafe(shard, entry, true, cancellationToken);
            nextIndex++;
        }
    }

    protected async Task ProcessSafe(
        DbShard shard, TDbEntry entry, bool fallbackToProcessGap, CancellationToken cancellationToken)
    {
        // This method should never fail (unless cancelled)!
        if (entry.IsProcessed)
            return;

        try {
            await Process(shard, entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var suffix = fallbackToProcessGap ? ", will reprocess it as gap" : "";
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(e,
                $"{nameof(Process)}[{{Shard}}]: failed for log entry #{{Index}}{suffix}",
                shard, entry.Index);
            if (fallbackToProcessGap)
                ProcessGapSafe(shard, entry.Index, entry.LoggedAt, entry);
        }
    }

    protected void ProcessGapSafe(DbShard shard, long index, Moment maxLoggedAt, TDbEntry? foundEntry)
    {
        // This method should never fail!
        lock (ProcessGapTasks) {
            var key = (shard, index);
            if (ProcessGapTasks.ContainsKey(key))
                return;

            var task = Task.Run(() => ProcessGap(shard, index, maxLoggedAt, foundEntry, StopToken));
            ProcessGapTasks[key] = task;
            _ = task.ContinueWith(_ => {
                lock (ProcessGapTasks)
                    ProcessGapTasks.Remove(key);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    protected async Task ProcessGap(
        DbShard shard, long index, Moment loggedAt, TDbEntry? foundEntry, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(Settings.GapRetryDelay.Next(), cancellationToken).ConfigureAwait(false);
            var isProcessed = await Settings.GapRetryPolicy
                .Apply(ct => ProcessGapImpl(shard, index, ct), cancellationToken)
                .ConfigureAwait(false);
            if (isProcessed) {
                DefaultLog?.Log(Settings.LogLevel,
                    $"{nameof(ProcessGap)}[{{Shard}}]: log entry #{{Index}} is processed",
                    shard, index);
                return;
            }

            DebugLog?.LogDebug(
                $"{nameof(ProcessGap)}[{{Shard}}]: log entry #{{Index}} is already processed by another host",
                shard, index);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e,
                $"{nameof(ProcessGap)}[{{Shard}}]: failed to process log entry #{{Index}}",
                shard, index);
        }
    }

    protected async Task<bool> ProcessGapImpl(DbShard shard, long index, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(shard, readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        TDbEntry? entry;
        if (Mode is DbLogProcessingMode.Cooperative) {
            // Cooperative mode flow
            entry = await GetEntry(dbContext, index, cancellationToken).ConfigureAwait(false);
            if (entry == null)
                throw new LogEntryNotFoundException();

            await ProcessSafe(shard, entry, false, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Exclusive mode flow
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var _2 = tx.ConfigureAwait(false);

        entry = await GetEntry(dbContext, index, cancellationToken).ConfigureAwait(false);
        if (entry == null)
            throw new LogEntryNotFoundException();
        if (entry.IsProcessed)
            return false;

        var dbEntries = dbContext.Set<TDbEntry>();
        dbEntries.Attach(entry);
        MarkProcessed(dbEntries, entry);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // If we're here, the entry's row is x-locked due to update above
        await Process(shard, entry, cancellationToken).ConfigureAwait(false);

        if (DbHub.ChaosMaker.IsEnabled)
            await DbHub.ChaosMaker.Act(this, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<TDbEntry?> GetEntry(TDbContext dbContext, long index, CancellationToken cancellationToken)
    {
        var dbEntries = Mode is DbLogProcessingMode.Exclusive
            ? dbContext.Set<TDbEntry>().WithHints(DbLockingHint.Update)
            : dbContext.Set<TDbEntry>();
        return await dbEntries.FirstOrDefaultAsync(x => x.Index == index, cancellationToken).ConfigureAwait(false);
    }

    protected void MarkProcessed(DbSet<TDbEntry> dbEntries, TDbEntry entry)
    {
        entry.Version = DbHub.VersionGenerator.NextVersion(entry.Version);
        entry.IsProcessed = true;
        dbEntries.Update(entry);
    }
}
