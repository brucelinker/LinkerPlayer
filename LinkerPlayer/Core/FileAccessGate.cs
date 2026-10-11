using System.Collections.Concurrent;

namespace LinkerPlayer.Core;

/// <summary>
/// Serializes in-process file reads and writes for one path.
/// The next operation waits until the current one releases the file.
/// </summary>
public static class FileAccessGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static Lease Enter(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return default;

        SemaphoreSlim gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        return new Lease(gate);
    }

    public static async ValueTask<Lease> EnterAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return default;

        SemaphoreSlim gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    public readonly struct Lease : IDisposable
    {
        private readonly SemaphoreSlim? _gate;

        internal Lease(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => _gate?.Release();
    }
}
