using Microsoft.EntityFrameworkCore;

namespace ActualLab.Fusion.EntityFramework.LogProcessing;

public class FakeDbLogWatcher<TDbContext, TDbEntry>
    : DbServiceBase<TDbContext>, IDbLogWatcher<TDbContext, TDbEntry>
    where TDbContext : DbContext
    where TDbEntry : class, ILogEntry
{
    public FakeDbLogWatcher(IServiceProvider services) : base(services)
    {
        var watcherType = typeof(IDbLogWatcher<TDbContext, TDbEntry>);
        Log.LogWarning(
            "{DbLogWatcherType} is not configured, so no change notifications are sent & received for this log!",
            watcherType.GetName());
    }

    public Task Notify(DbShard shard, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task WhenChanged(DbShard shard, CancellationToken cancellationToken = default)
        => TaskExt.NeverEndingTask.WaitAsync(cancellationToken);
}
