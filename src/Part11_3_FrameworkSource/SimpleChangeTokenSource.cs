using Microsoft.Extensions.Primitives;

namespace Part11_3_FrameworkSource;

public sealed class SimpleChangeTokenSource : IChangeToken
{
    private readonly List<Action<object?>> _callbacks = [];
    private readonly Lock _gate = new();

    public bool ActiveChangeCallbacks => true;

    public bool HasChanged => false;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        lock (_gate)
        {
            _callbacks.Add(obj => callback(obj));
        }
        return new CallbackDisposable(this, callback);
    }

    public void Trigger()
    {
        List<Action<object?>> snapshot;
        lock (_gate)
        {
            snapshot = [.. _callbacks];
        }
        foreach (var cb in snapshot)
        {
            cb(null);
        }
    }

    private sealed class CallbackDisposable(SimpleChangeTokenSource owner, Action<object?> cb) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._callbacks.Remove(cb);
            }
        }
    }
}
