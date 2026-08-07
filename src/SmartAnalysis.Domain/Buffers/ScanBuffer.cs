namespace SmartAnalysis.Domain.Buffers;

/// <summary>
/// Owns a contiguous numeric block for a scan (1D or 2D) with a <b>single, explicit owner</b>.
/// Consumers receive read-only views (<see cref="Memory"/> / <see cref="Slice(int,int)"/>) and must
/// never dispose it. Slicing returns a view over the same storage — <b>no copy</b> — fixing the
/// legacy 3–5× buffer copying (doc 07 H6).
/// <para>
/// <b>Ownership (ADR-011):</b> creating a buffer <b>transfers ownership</b> of the backing array to
/// the <see cref="ScanBuffer{T}"/>. After transfer the caller must not read or write that array —
/// mutate the data only before transfer, or via a fresh array. Use <see cref="TakeOwnership"/> (the
/// name makes the transfer explicit) or <see cref="Allocate"/>.
/// </para>
/// <para>
/// <b>Lifetime:</b> every <see cref="Memory"/>/<see cref="Slice(int,int)"/> view <b>must not outlive
/// the owner</b>. Using a view after <see cref="Dispose"/> is a contract violation. Today the backing
/// is a GC array so a stale view is merely undefined-by-contract; when a pooled backing is adopted
/// later (ADR-011) this lifetime rule becomes a hard requirement (use-after-return). The public API
/// shape is stable across that change, but the lifetime contract must be honoured either way.
/// </para>
/// </summary>
/// <typeparam name="T">Element type (typically <see cref="float"/> or <see cref="double"/>).</typeparam>
public sealed class ScanBuffer<T> : IDisposable
{
    private readonly T[] _data;
    private bool _disposed;

    private ScanBuffer(T[] data, int width, int height)
    {
        _data = data;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Wraps an existing array, <b>transferring its ownership</b> to the buffer. The caller must not
    /// touch <paramref name="data"/> afterwards.
    /// </summary>
    /// <param name="data">The backing array; ownership transfers to the returned buffer.</param>
    /// <param name="width">Column count (fast axis). For 1D use width = length, height = 1.</param>
    /// <param name="height">Row count (slow axis). 1 for 1D data.</param>
    public static ScanBuffer<T> TakeOwnership(T[] data, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if ((long)width * height != data.Length)
        {
            throw new ArgumentException(
                $"width*height ({width}*{height}) must equal data length ({data.Length}).", nameof(data));
        }

        return new ScanBuffer<T>(data, width, height);
    }

    /// <summary>Allocates a zero-initialized buffer of <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static ScanBuffer<T> Allocate(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        return new ScanBuffer<T>(new T[checked(width * height)], width, height);
    }

    /// <summary>Column count (fast axis).</summary>
    public int Width { get; }

    /// <summary>Row count (slow axis); 1 for 1D data.</summary>
    public int Height { get; }

    /// <summary>Total element count (<see cref="Width"/> * <see cref="Height"/>).</summary>
    public int Length => _data.Length;

    /// <summary>A read-only view over the whole buffer. Does not copy. Must not be used after <see cref="Dispose"/>.</summary>
    public ReadOnlyMemory<T> Memory
    {
        get
        {
            ThrowIfDisposed();
            return _data;
        }
    }

    /// <summary>A read-only span over the whole buffer. Does not copy.</summary>
    public ReadOnlySpan<T> Span => Memory.Span;

    /// <summary>A read-only view of <paramref name="length"/> elements from <paramref name="start"/>. Does not copy.</summary>
    public ReadOnlyMemory<T> Slice(int start, int length) => Memory.Slice(start, length);

    /// <summary>A read-only view from <paramref name="start"/> to the end. Does not copy.</summary>
    public ReadOnlyMemory<T> Slice(int start) => Memory.Slice(start);

    /// <summary>
    /// Marks the owner as done with the storage. No-op for the current owned-array backing (the GC
    /// reclaims it); prevents handing out further views. Consumers of a view must never call this, and
    /// must not use any previously obtained view after the owner is disposed.
    /// </summary>
    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ScanBuffer<T>));
        }
    }
}
