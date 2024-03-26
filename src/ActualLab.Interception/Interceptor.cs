using ActualLab.Interception.Interceptors;
using ActualLab.Interception.Internal;

namespace ActualLab.Interception;

public class Interceptor
{
    public void BindTo(IRequiresAsyncProxy proxy, object? proxyTarget = null, bool initialize = true)
    {
        proxy.RequireProxy<IProxy>().Interceptor = this;
        if (proxyTarget != null)
            proxy.RequireProxy<InterfaceProxy>().ProxyTarget = proxyTarget;
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (initialize && proxy is INotifyInitialized notifyInitialized)
            notifyInitialized.Initialized();
    }

    public virtual void Intercept(Invocation invocation)
        => invocation.Intercepted();

    public virtual TResult Intercept<TResult>(Invocation invocation)
        => invocation.Intercepted<TResult>();

    public object? ChainIntercept<TUnwrappedResult>(MethodDef methodDef, Invocation invocation)
        => !methodDef.IsAsyncMethod
            ? Intercept<TUnwrappedResult>(invocation)
            : methodDef.ReturnsTask
                ? Intercept<Task<TUnwrappedResult>>(invocation)
                : Intercept<ValueTask<TUnwrappedResult>>(invocation);

    public Func<Invocation, object?> ChainIntercept<TUnwrappedResult>(MethodDef methodDef)
        => !methodDef.IsAsyncMethod
            ? invocation => Intercept<TUnwrappedResult>(invocation)
            : methodDef.ReturnsTask
                ? Intercept<Task<TUnwrappedResult>>
                : invocation => Intercept<ValueTask<TUnwrappedResult>>(invocation);
}
