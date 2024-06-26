using System.Diagnostics.CodeAnalysis;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Rpc.Internal;

namespace ActualLab.Rpc;

public sealed class RpcServiceDef
{
    private Dictionary<MethodInfo, RpcMethodDef> _methods = null!;
    private Dictionary<Symbol, RpcMethodDef> _methodByName = null!;
    private object? _server;
    private string? _toStringCached;

    internal Dictionary<Symbol, RpcMethodDef> MethodByName => _methodByName;

    public RpcHub Hub { get; }
    public Type Type { get; }
    public ServiceResolver? ServerResolver { get; init; }
    public Symbol Name { get; init; }
    public bool IsSystem { get; init; }
    public bool IsBackend { get; init; }
    public bool HasServer => ServerResolver != null;
    public object Server => _server ??= ServerResolver.Resolve(Hub.Services);
    public IReadOnlyCollection<RpcMethodDef> Methods => _methodByName.Values;
    public Symbol Scope { get; init; }
    public LegacyNames LegacyNames { get; init; }

    public RpcMethodDef this[MethodInfo method] => Get(method) ?? throw Errors.NoMethod(Type, method);
    public RpcMethodDef this[Symbol methodName] => Get(methodName) ?? throw Errors.NoMethod(Type, methodName);

    public RpcServiceDef(RpcHub hub, RpcServiceBuilder service)
    {
        var name = service.Name;
        if (name.IsEmpty)
            name = service.Type.GetName();

        Hub = hub;
        Name = name;
        Type = service.Type;
        ServerResolver = service.ServerResolver;
        IsSystem = typeof(IRpcSystemService).IsAssignableFrom(Type);
        IsBackend = hub.BackendServiceDetector.Invoke(service.Type);
        Scope = hub.ServiceScopeResolver.Invoke(this);
        LegacyNames = new LegacyNames(Type
            .GetCustomAttributes<LegacyNameAttribute>(false)
            .Select(x => LegacyName.New(x)));
    }

    internal void BuildMethods(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType)
    {
        if (serviceType != Type)
            throw new ArgumentOutOfRangeException(nameof(serviceType));

        _methods = new Dictionary<MethodInfo, RpcMethodDef>();
        _methodByName = new Dictionary<Symbol, RpcMethodDef>();
        var bindingFlags = BindingFlags.Instance | BindingFlags.Public;
#pragma warning disable IL2067, IL2070
        var methods = (Type.IsInterface
                ? serviceType.GetAllInterfaceMethods(bindingFlags)
                : serviceType.GetMethods(bindingFlags)
            ).ToList();
#pragma warning restore IL2067, IL2070
        foreach (var method in methods) {
            if (method.DeclaringType == typeof(object))
                continue;
            if (method.IsGenericMethodDefinition)
                continue;

            var methodDef = Hub.MethodDefBuilder.Invoke(this, method);
            if (!methodDef.IsValid)
                continue;

            if (!_methodByName.TryAdd(methodDef.Name, methodDef))
                throw Errors.MethodNameConflict(methodDef);

            _methods.Add(method, methodDef);
        }
    }

    public override string ToString()
    {
        if (_toStringCached != null)
            return _toStringCached;

        var serverInfo = HasServer  ? $" -> {ServerResolver}" : "";
        var kindInfo = (IsSystem, IsBackend) switch {
            (true, true) => " [System,Backend]",
            (true, false) => " [System]",
            (false, true) => " [Backend]",
            _ => "",
        };
        return _toStringCached = $"'{Name}'{kindInfo}: {Type.GetName()}{serverInfo}, {Methods.Count} method(s)";
    }

    public RpcMethodDef? Get(MethodInfo method) => _methods.GetValueOrDefault(method);
    public RpcMethodDef? Get(Symbol methodName) => _methodByName.GetValueOrDefault(methodName);
}
