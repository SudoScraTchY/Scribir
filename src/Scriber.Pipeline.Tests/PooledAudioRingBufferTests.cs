using Scriber.Pipeline.Streaming;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class PooledAudioRingBufferTests
{
    [Fact]
    public void WriteAndRead_ReturnsCorrectSamples()
    {
        using var ring = new PooledAudioRingBuffer(1024);
        var input = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };

        ring.Write(input);
        Assert.Equal(5, ring.Count);

        var output = new float[5];
        int read = ring.Read(output);

        Assert.Equal(5, read);
        Assert.Equal(0, ring.Count);
        Assert.Equal(input, output);
    }

    [Fact]
    public void CircularWrapAround_MaintainsCorrectDataOrder()
    {
        // Small initial capacity to easily trigger wrap-around
        using var ring = new PooledAudioRingBuffer(16);
        int initialCap = ring.Capacity;

        // Fill partially and consume to advance head
        var chunk1 = new float[10];
        for (int i = 0; i < 10; i++) chunk1[i] = i + 1;
        ring.Write(chunk1);

        var drain = new float[8];
        ring.Read(drain);
        Assert.Equal(2, ring.Count); // 2 samples left, head at index 8

        // Now write chunk2 that wraps around the buffer boundary
        var chunk2 = new float[12];
        for (int i = 0; i < 12; i++) chunk2[i] = 100 + i;
        ring.Write(chunk2);

        Assert.Equal(14, ring.Count);

        var allRead = new float[14];
        int readCount = ring.Read(allRead);
        Assert.Equal(14, readCount);
        Assert.Equal(0, ring.Count);

        // First 2 samples should be from chunk1 (9, 10)
        Assert.Equal(9f, allRead[0]);
        Assert.Equal(10f, allRead[1]);

        // Remaining 12 samples should be chunk2 (100..111)
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(100 + i, allRead[2 + i]);
        }
    }

    [Fact]
    public void PeekAndAdvance_WorksWithoutPrematureConsumption()
    {
        using var ring = new PooledAudioRingBuffer(64);
        var input = new float[] { 1f, 2f, 3f, 4f, 5f };
        ring.Write(input);

        var peekBuffer = new float[3];
        int peeked = ring.Peek(peekBuffer);

        Assert.Equal(3, peeked);
        Assert.Equal(5, ring.Count); // Count must remain 5
        Assert.Equal([1f, 2f, 3f], peekBuffer);

        int advanced = ring.Advance(2);
        Assert.Equal(2, advanced);
        Assert.Equal(3, ring.Count);

        var remaining = new float[3];
        ring.Read(remaining);
        Assert.Equal([3f, 4f, 5f], remaining);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void Grow_ExpandsCapacity_WhenWritingMoreThanInitialCapacity()
    {
        using var ring = new PooledAudioRingBuffer(16);
        int initialCapacity = ring.Capacity;

        var largeData = new float[initialCapacity * 3];
        for (int i = 0; i < largeData.Length; i++) largeData[i] = i * 0.5f;

        ring.Write(largeData);
        Assert.True(ring.Capacity >= largeData.Length);
        Assert.Equal(largeData.Length, ring.Count);

        var readBack = new float[largeData.Length];
        int read = ring.Read(readBack);
        Assert.Equal(largeData.Length, read);
        Assert.Equal(largeData, readBack);
    }

    [Fact]
    public void Clear_ResetsCountAndHead()
    {
        using var ring = new PooledAudioRingBuffer(64);
        ring.Write(new float[] { 1f, 2f, 3f });
        Assert.Equal(3, ring.Count);

        ring.Clear();
        Assert.Equal(0, ring.Count);

        var dest = new float[10];
        int read = ring.Read(dest);
        Assert.Equal(0, read);
    }

    [Fact]
    public void SteadyState_ZeroHeapAllocationLoop()
    {
        using var ring = new PooledAudioRingBuffer(64000);
        var writeChunk = new float[1600]; // 100ms at 16kHz
        var readChunk = new float[32000]; // 2000ms at 16kHz

        // Warm up
        for (int i = 0; i < 20; i++)
        {
            ring.Write(writeChunk);
        }
        ring.Read(readChunk);

        // Measure allocations during steady-state push/pop
        long memoryBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int cycle = 0; cycle < 50; cycle++)
        {
            for (int p = 0; p < 20; p++)
            {
                ring.Write(writeChunk);
            }
            ring.Read(readChunk);
        }

        long memoryAfter = GC.GetAllocatedBytesForCurrentThread();
        long allocated = memoryAfter - memoryBefore;

        // Must be zero bytes allocated on heap
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void DisposedOperations_ThrowObjectDisposedException()
    {
        var ring = new PooledAudioRingBuffer(64);
        ring.Dispose();

        Assert.True(ring.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => ring.Clear());
        Assert.Throws<ObjectDisposedException>(() => ring.Write(new float[] { 1f }));
        Assert.Throws<ObjectDisposedException>(() => ring.Read(new float[1]));
        Assert.Throws<ObjectDisposedException>(() => ring.Peek(new float[1]));
        Assert.Throws<ObjectDisposedException>(() => ring.Advance(1));
    }
}
