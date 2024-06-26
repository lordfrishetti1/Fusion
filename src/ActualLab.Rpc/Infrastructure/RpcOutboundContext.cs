using System.Diagnostics.CodeAnalysis;
using ActualLab.Interception;
using ActualLab.Rpc.Caching;
using ActualLab.Rpc.Internal;

namespace ActualLab.Rpc.Infrastructure;

public sealed class RpcOutboundContext(byte callTypeId, List<RpcHeader>? headers = null)
{
    [ThreadStatic] private static RpcOutboundContext? _current;

#pragma warning disable CA1721
    public static RpcOutboundContext? Current => _current;
#pragma warning restore CA1721

    public byte CallTypeId = callTypeId;
    public List<RpcHeader>? Headers = headers;
    public RpcPeer? PreSelectedPeer;
    public RpcMethodDef? MethodDef;
    public ArgumentList? Arguments;
    public CancellationToken CancellationToken;
    public RpcOutboundCall? Call;
    public RpcPeer Peer = null!;
    public long RelatedId;
    public RpcCacheInfoCapture? CacheInfoCapture;

    public static RpcOutboundContext GetCurrent()
        => Current ?? throw Errors.NoCurrentRpcOutboundContext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RpcOutboundContext(List<RpcHeader>? headers = null)
        : this(0, headers)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Scope Activate()
        => new(this, _current);

    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcOutboundCall? PrepareCall(RpcMethodDef methodDef, ArgumentList arguments)
    {
        if (MethodDef != null)
            throw ActualLab.Internal.Errors.AlreadyInvoked(nameof(PrepareCall));

        // MethodDef, Arguments, CancellationToken
        MethodDef = methodDef;
        Arguments = arguments;
        var ctIndex = methodDef.CancellationTokenIndex;
        CancellationToken = ctIndex >= 0 ? arguments.GetCancellationToken(ctIndex) : default;

        // Peer
        var hub = MethodDef.Hub;
        Peer = PreSelectedPeer ?? hub.CallRouter.Invoke(methodDef, arguments);
        if (Peer.ConnectionKind == RpcPeerConnectionKind.LocalCall) {
            Call = null;
            return null;
        }

        // Call
        Call = RpcOutboundCall.New(this);
        if (!Call.NoWait)
            hub.OutboundMiddlewares.NullIfEmpty()?.PrepareCall(this);
        return Call;
    }

    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcOutboundCall? PrepareReroutedCall()
    {
        if (MethodDef == null || Arguments == null)
            throw ActualLab.Internal.Errors.NotInvoked(nameof(PrepareCall));
        if (PreSelectedPeer != null)
            throw ActualLab.Internal.Errors.InternalError("This call cannot be rerouted (StaticPeer != null).");

        // Peer
        var hub = MethodDef.Hub;
        Peer = hub.CallRouter.Invoke(MethodDef, Arguments);
        if (Peer.ConnectionKind == RpcPeerConnectionKind.LocalCall) {
            Call = null;
            return null;
        }

        // Call
        Call = RpcOutboundCall.New(this);
        if (!Call.NoWait)
            hub.OutboundMiddlewares.NullIfEmpty()?.PrepareCall(this);
        return Call;
    }

    public bool IsPeerChanged()
        => PreSelectedPeer == null && Peer != MethodDef!.Hub.CallRouter.Invoke(MethodDef, Arguments!);

    public bool MustCaptureCacheKey(RpcMessage? message, out bool keyOnly)
    {
        keyOnly = false;
        if (CacheInfoCapture is not { } cacheInfoCapture)
            return false;

        keyOnly = cacheInfoCapture.CaptureMode == RpcCacheInfoCaptureMode.KeyOnly;
        cacheInfoCapture.Key ??= message is { Arguments: null }
            ? new RpcCacheKey(MethodDef!.Service.Name, MethodDef.Name, message.ArgumentData)
            : RpcCacheKey.Invalid;
        return true;
    }

    public bool MustCaptureCacheData([NotNullWhen(true)] out TaskCompletionSource<TextOrBytes>? dataSource)
    {
        dataSource = CacheInfoCapture?.DataSource;
        return dataSource != null;
    }

    // Nested types

    public readonly struct Scope : IDisposable
    {
        private readonly RpcOutboundContext? _oldContext;

        public readonly RpcOutboundContext Context;

        internal Scope(RpcOutboundContext context, RpcOutboundContext? oldContext)
        {
            Context = context;
            _oldContext = oldContext;
            if (Context != _oldContext)
                _current = context;
        }

        public void Dispose()
        {
            if (Context != _current)
                throw Errors.RpcOutboundContextChanged();

            if (Context != _oldContext)
                _current = _oldContext;
        }
    }
}
