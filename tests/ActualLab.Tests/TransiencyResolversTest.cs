using ActualLab.Resilience;
using ActualLab.Versioning;

namespace ActualLab.Tests;

public class TransiencyResolversTest
{
    [Fact]
    public void WellKnownOnlyTest()
    {
        var resolver = TransiencyResolvers.CoreOnly;
        TestWellKnown(resolver);
        resolver.Invoke(new NullReferenceException()).Should().Be(Transiency.Unknown);
        resolver.Invoke(new ObjectDisposedException("Whatever")).Should().Be(Transiency.Unknown);
    }

    [Fact]
    public void PreferTransientTest()
    {
        var resolver = TransiencyResolvers.PreferTransient;
        TestWellKnown(resolver);
        resolver.Invoke(new NullReferenceException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new ObjectDisposedException("Whatever")).Should().Be(Transiency.NonTransient);

        resolver.Invoke(new ServiceException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new VersionMismatchException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new TaskCanceledException()).Should().Be(Transiency.Transient);
        resolver.Invoke(new OperationCanceledException()).Should().Be(Transiency.Transient);
    }

    [Fact]
    public void PreferNonTransientTest()
    {
        var resolver = TransiencyResolvers.PreferNonTransient;
        TestWellKnown(resolver);
        resolver.Invoke(new NullReferenceException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new ObjectDisposedException("Whatever")).Should().Be(Transiency.NonTransient);

        resolver.Invoke(new ServiceException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new VersionMismatchException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new TaskCanceledException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new OperationCanceledException()).Should().Be(Transiency.NonTransient);
    }

    // Private methods

    private void TestWellKnown(TransiencyResolver resolver)
    {
        resolver.Invoke(new RetryRequiredException()).Should().Be(Transiency.SuperTransient);
        resolver.Invoke(new TransientException()).Should().Be(Transiency.Transient);
        resolver.Invoke(new RetryPolicyTimeoutExceededException()).Should().Be(Transiency.NonTransient);
        resolver.Invoke(new TimeoutException()).Should().Be(Transiency.Transient);
        resolver.Invoke(new TerminalException()).Should().Be(Transiency.Terminal);
        resolver.Invoke(new ObjectDisposedException("IServiceProvider")).Should().Be(Transiency.Terminal);

        try {
            var services = new ServiceCollection()
                .AddSingleton<TransiencyResolversTest>()
                .BuildServiceProvider();
            services.Dispose();
            var testService = services.GetRequiredService<TransiencyResolversTest>();
        }
        catch (Exception e) {
            resolver.Invoke(e).Should().Be(Transiency.Terminal);
        }
    }
}
