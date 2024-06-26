using ActualLab.Fusion.Internal;

namespace ActualLab.Fusion;

#pragma warning disable CA1721

public static class Computed
{
    public static TimeSpan PreciseInvalidationDelayThreshold { get; set; } = TimeSpan.FromSeconds(1);

    // Current & GetCurrent

    public static IComputed? Current {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ComputeContext.Current.Computed;
    }

    public static IComputed GetCurrent()
        => Current ?? throw Errors.CurrentComputedIsNull();
    public static Computed<T> GetCurrent<T>()
        => (Computed<T>)(Current ?? throw Errors.CurrentComputedIsNull());

    public static ComputeContextScope BeginCompute(IComputed computed)
        => new(new(computed));

    public static ComputeContextScope BeginIsolation()
        => new(ComputeContext.None);

    public static ComputeContextScope BeginCapture()
        => new(new ComputeContext(CallOptions.Capture));

    public static ComputeContextScope BeginCaptureExisting()
        => new(new ComputeContext(CallOptions.Capture | CallOptions.GetExisting));

    // TryCapture

    public static async ValueTask<Option<IComputed>> TryCapture(
        Func<Task> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.TryGetCaptured();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured();
            if (result.IsSome(out var computed) && computed.HasError)
                return result; // Return the original error, if possible
            throw;
        }
    }

    public static async ValueTask<Option<Computed<T>>> TryCapture<T>(
        Func<Task<T>> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.TryGetCaptured<T>();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured<T>();
            if (result.IsSome(out var computed) && computed.HasError)
                return result; // Return the original error, if possible
            throw;
        }
    }

    public static async ValueTask<Option<IComputed>> TryCapture(
        Func<ValueTask> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.TryGetCaptured();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured();
            if (result.IsSome(out var computed) && computed.HasError)
                return result; // Return the original error, if possible
            throw;
        }
    }

    public static async ValueTask<Option<Computed<T>>> TryCapture<T>(
        Func<ValueTask<T>> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.TryGetCaptured<T>();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured<T>();
            if (result.IsSome(out var computed) && computed.HasError)
                return result; // Return the original error, if possible
            throw;
        }
    }

    // Capture

    public static async ValueTask<IComputed> Capture(
        Func<Task> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.GetCaptured();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured();
            if (result.IsSome(out var computed) && computed.HasError)
                return computed; // Return the original error, if possible
            throw;
        }
    }

    public static async ValueTask<Computed<T>> Capture<T>(
        Func<Task<T>> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.GetCaptured<T>();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured<T>();
            if (result.IsSome(out var computed) && computed.HasError)
                return computed; // Return the original error, if possible
            throw;
        }
    }

    public static async ValueTask<IComputed> Capture(
        Func<ValueTask> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.GetCaptured();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured();
            if (result.IsSome(out var computed) && computed.HasError)
                return computed; // Return the original error, if possible
            throw;
        }
    }

    public static async ValueTask<Computed<T>> Capture<T>(
        Func<ValueTask<T>> producer,
        CancellationToken cancellationToken = default)
    {
        using var ccs = BeginCapture();
        try {
            await producer.Invoke().ConfigureAwait(false);
            return ccs.Context.GetCaptured<T>();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var result = ccs.Context.TryGetCaptured<T>();
            if (result.IsSome(out var computed) && computed.HasError)
                return computed; // Return the original error, if possible
            throw;
        }
    }

    // GetExisting

    public static Computed<T>? GetExisting<T>(Func<Task<T>> producer)
    {
        using var ccs = BeginCaptureExisting();
        var task = producer.Invoke();
        _ = task.AssertCompleted(); // The must be always synchronous in this case
        return ccs.Context.TryGetCaptured<T>().ValueOrDefault;
    }

    public static Computed<T>? GetExisting<T>(Func<ValueTask<T>> producer)
    {
        using var ccs = BeginCaptureExisting();
        var task = producer.Invoke();
        _ = task.AssertCompleted(); // The must be always synchronous in this case
        return ccs.Context.TryGetCaptured<T>().ValueOrDefault;
    }
}
