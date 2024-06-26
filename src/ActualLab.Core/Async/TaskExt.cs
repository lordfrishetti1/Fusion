using ActualLab.Internal;

namespace ActualLab.Async;

#pragma warning disable MA0004

public static partial class TaskExt
{
    private static readonly MethodInfo FromTypedTaskInternalMethod =
        typeof(TaskExt).GetMethod(nameof(FromTypedTaskInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<Type, Func<Task, IResult>> ToTypedResultCache = new();

    public static readonly Task<Unit> UnitTask = Task.FromResult(Unit.Default);
    public static readonly Task<bool> TrueTask = Task.FromResult(true);
    public static readonly Task<bool> FalseTask = Task.FromResult(false);

    // NewNeverEndingUnreferenced

    // The tasks these methods return aren't referenced,
    // so unless whatever awaits them is referenced,
    // it may simply evaporate on the next GC cycle.
    //
    // Earlier such tasks were stored in a static var, which is actually wrong:
    // if one of them get N dependencies, all of these N dependencies will stay
    // in RAM forever, since there is no way to "unsubscribe" an awaiter.
    //
    // So the best option here is to return a task that won't prevent
    // GC from collecting the awaiter in case nothing else "holds" it -
    // and assuming the task is really never ending, this is the right thing to do.
    public static Task NewNeverEndingUnreferenced()
        => TaskCompletionSourceExt.New<Unit>().Task;
    public static Task<T> NewNeverEndingUnreferenced<T>()
        => TaskCompletionSourceExt.New<T>().Task;

    // ToValueTask

    public static ValueTask<T> ToValueTask<T>(this Task<T> source) => new(source);
    public static ValueTask ToValueTask(this Task source) => new(source);

    // GetBaseException

    public static Exception GetBaseException(this Task task)
        => task.AssertCompleted().Exception?.GetBaseException()
            ?? (task.IsCanceled
                ? new TaskCanceledException(task)
                : throw Errors.TaskIsFaultedButNoExceptionAvailable());

    // AssertXxx

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task AssertCompleted(this Task task)
        => !task.IsCompleted ? throw Errors.TaskIsNotCompleted() : task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> AssertCompleted<T>(this Task<T> task)
        => !task.IsCompleted ? throw Errors.TaskIsNotCompleted() : task;

    // Private methods

    private static IResult FromTypedTaskInternal<T>(Task task)
        // ReSharper disable once HeapView.BoxingAllocation
        => task.IsCompletedSuccessfully()
            ? Result.Value(((Task<T>) task).Result)
            : Result.Error<T>(task.GetBaseException());
}
