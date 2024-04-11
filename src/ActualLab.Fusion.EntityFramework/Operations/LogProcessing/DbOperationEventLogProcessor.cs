using ActualLab.Fusion.EntityFramework.LogProcessing;
using ActualLab.Resilience;
using Microsoft.EntityFrameworkCore;

namespace ActualLab.Fusion.EntityFramework.Operations.LogProcessing;

public class DbOperationEventLogProcessor<TDbContext>
    : DbIndexedLogProcessor<TDbContext, DbOperationEvent, DbOperationEventLogProcessor<TDbContext>.Options>
    where TDbContext : DbContext
{
    public record Options : DbEventLogProcessorOptions
    {
        public static Options Default { get; set; } = new();

#if false // Debug helper
        static Options()
        {
            var options = new Options();
            Default = options with {
                ReprocessPolicy = (RetryPolicy)options.ReprocessPolicy with { Timeout = TimeSpan.FromSeconds(0.5) }
            };
        }
#endif
    }

    protected DbOperationEventHandler OperationEventHandler { get;  }
    protected override IState<ImmutableHashSet<DbShard>> WorkerShards => DbHub.ShardRegistry.EventProcessorShards;

    // ReSharper disable once ConvertToPrimaryConstructor
    public DbOperationEventLogProcessor(Options settings, IServiceProvider services)
        : base(settings, services)
        => OperationEventHandler = services.GetRequiredService<DbOperationEventHandler>();

    protected override Task Process(DbShard shard, DbOperationEvent entry, CancellationToken cancellationToken)
    {
        var operationEvent = entry.ToModel();
        return OperationEventHandler.Handle(operationEvent, cancellationToken);
    }
}