using System.Diagnostics.CodeAnalysis;
using ActualLab.CommandR.Internal;

namespace ActualLab.CommandR;

public static class CommandExt
{
    private static readonly ConcurrentDictionary<Type, Type> ResultTypeCache = new();
    private static readonly Type CommandWithResultType = typeof(ICommand<>);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type GetResultType(this ICommand command)
        => GetResultType(command.GetType());

    public static Type GetResultType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type commandType)
    {
        if (commandType == null)
            throw new ArgumentNullException(nameof(commandType));

        var result = ResultTypeCache.GetOrAdd(commandType, static tCommand => {
#pragma warning disable IL2070
            foreach (var tInterface in tCommand.GetInterfaces()) {
#pragma warning restore IL2070
                if (!tInterface.IsConstructedGenericType)
                    continue;
                var gInterface = tInterface.GetGenericTypeDefinition();
                if (gInterface != CommandWithResultType)
                    continue;

                return tInterface.GetGenericArguments()[0];
            }
            return null!;
        });
        return result ?? throw Errors.CommandMustImplementICommandOfTResult(commandType);
    }
}
