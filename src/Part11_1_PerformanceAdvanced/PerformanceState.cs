namespace Part11_1_PerformanceAdvanced;

public sealed class PerformanceState
{
    private readonly Lock _gate = new();
    private readonly List<byte[]> _retained = [];
    private const int MaxMegabytes = 64;

    public int RetainedMegabytes
    {
        get
        {
            lock (_gate)
            {
                return _retained.Count;
            }
        }
    }

    public void Retain(int megabytes)
    {
        lock (_gate)
        {
            var remaining = Math.Min(Math.Max(megabytes, 0), MaxMegabytes - _retained.Count);
            for (var i = 0; i < remaining; i++)
            {
                var buffer = GC.AllocateUninitializedArray<byte>(1024 * 1024);
                buffer[0] = 1;
                _retained.Add(buffer);
            }
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _retained.Clear();
        }
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
