namespace SmartAnalysis.Domain.Buffers;

/// <summary>
/// Owns a contiguous numeric block for a scan (1D or 2D) with a <b>single, explicit owner</b>.
/// Consumers receive read-only views (<see cref="Memory"/> / <see cref="Slice(int,int)"/>) and must
/// never dispose it. Slicing returns a view over the same storage — <b>no copy</b> — fixing the
/// legacy 3–5× buffer copying (doc 07 H6).
/// <para>
/// Backing strategy (plain owned array over <see cref="System.Memory{T}"/>) is decided in
/// <c>ADR-011</c> (OD-1). <see cref="Dispose"/> is a defined no-op today (the GC reclaims the array)
/// and exists so the ownership contract is stable if a pooled backing is adopted later.
/// </para>
/// </summary>
/// <typeparam name="T">Element type (typically <see cref="float"/> or <see cref="double"/>).</typeparam>
public sealed class ScanBuffer<T> : IDisposable
{
    private readonly T[] _data;
    private bool _disposed;

    /// <summary>Wraps an existing array as the buffer's owned storage.</summary>
    /// <param name="data">The backing array (this instance becomes its owner).</param>
    /// <param name="width">Column count (fast axis). Use <paramref name="width"/> = length, height 1 for 1D.</param>
    /// <param name="height">Row count (slow axis). 1 for 1D data.</param>
    public ScanBuffer(T[] data, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if ((long)width * height != data.Length)
        {
            throw new ArgumentException(
                $"width*height ({width}*{height}) must equal data length ({data.Length}).", nameof(data));
        }

        _data = data;
        Width = width;
        Height = height;
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

    /// <summary>A read-only view over the whole buffer. Does not copy.</summary>
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
    /// reclaims it); prevents further access. Consumers of a view must never call this.
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
