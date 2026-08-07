using System.Runtime.InteropServices;
using SmartAnalysis.Domain.Buffers;
using Xunit;

namespace SmartAnalysis.Tests.Buffers;

public sealed class ScanBufferTests
{
    [Fact]
    public void Allocate_sets_dimensions_and_length()
    {
        using var buffer = ScanBuffer<float>.Allocate(width: 4, height: 3);

        Assert.Equal(4, buffer.Width);
        Assert.Equal(3, buffer.Height);
        Assert.Equal(12, buffer.Length);
    }

    [Fact]
    public void Constructor_rejects_mismatched_dimensions()
    {
        Assert.Throws<ArgumentException>(() => new ScanBuffer<float>(new float[10], width: 3, height: 3));
    }

    [Fact]
    public void Slice_is_copy_free_and_shares_backing_storage()
    {
        var data = new double[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        using var buffer = new ScanBuffer<double>(data, width: 8, height: 1);

        var slice = buffer.Slice(2, 4);

        Assert.Equal(4, slice.Length);
        Assert.True(MemoryMarshal.TryGetArray(slice, out ArraySegment<double> segment));
        Assert.Same(data, segment.Array);   // same backing array → no copy
        Assert.Equal(2, segment.Offset);
        Assert.Equal(2.0, slice.Span[0]);
        Assert.Equal(5.0, slice.Span[3]);
    }

    [Fact]
    public void Full_memory_view_is_copy_free()
    {
        var data = new float[] { 1f, 2f, 3f };
        using var buffer = new ScanBuffer<float>(data, width: 3, height: 1);

        Assert.True(MemoryMarshal.TryGetArray(buffer.Memory, out ArraySegment<float> segment));
        Assert.Same(data, segment.Array);
    }

    [Fact]
    public void Accessing_after_dispose_throws()
    {
        var buffer = ScanBuffer<float>.Allocate(2, 2);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = buffer.Memory);
    }

    [Fact]
    public void One_dimensional_buffer_uses_height_one()
    {
        using var buffer = ScanBuffer<double>.Allocate(width: 256, height: 1);
        Assert.Equal(256, buffer.Length);
    }
}
