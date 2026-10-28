using System.Buffers;

namespace Scriber.Pipeline.Streaming;

/// <summary>
/// High-performance zero-allocation circular audio ring buffer backed by <see cref="ArrayPool{T}.Shared"/>.
/// Eliminates GC heap allocations during continuous 16kHz streaming audio capture.
/// </summary>
public sealed class PooledAudioRingBuffer : IDisposable
{
    private float[]? _buffer;
    private int _head;
    private int _count;
    private int _capacity;
    private bool _disposed;

    public int Count => _count;
    public int Capacity => _capacity;
    public bool IsDisposed => _disposed;

    public PooledAudioRingBuffer(int initialCapacity = 64000)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity must be greater than zero.");
        }

        _buffer = ArrayPool<float>.Shared.Rent(initialCapacity);
        _capacity = _buffer.Length;
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Writes audio samples into the ring buffer, automatically expanding capacity via ArrayPool if needed.
    /// </summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.IsEmpty || _buffer == null) return;

        int requiredCapacity = _count + samples.Length;
        if (requiredCapacity > _capacity)
        {
            Grow(requiredCapacity);
        }

        int tail = (_head + _count) % _capacity;
        int firstChunk = Math.Min(samples.Length, _capacity - tail);
        samples[..firstChunk].CopyTo(_buffer.AsSpan(tail, firstChunk));

        int secondChunk = samples.Length - firstChunk;
        if (secondChunk > 0)
        {
            samples.Slice(firstChunk, secondChunk).CopyTo(_buffer.AsSpan(0, secondChunk));
        }

        _count += samples.Length;
    }

    /// <summary>
    /// Reads and consumes up to destination.Length samples from the ring buffer.
    /// Returns the actual number of samples read.
    /// </summary>
    public int Read(Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.IsEmpty || _count == 0 || _buffer == null) return 0;

        int toRead = Math.Min(destination.Length, _count);
        int firstChunk = Math.Min(toRead, _capacity - _head);
        _buffer.AsSpan(_head, firstChunk).CopyTo(destination[..firstChunk]);

        int secondChunk = toRead - firstChunk;
        if (secondChunk > 0)
        {
            _buffer.AsSpan(0, secondChunk).CopyTo(destination.Slice(firstChunk, secondChunk));
        }

        _head = (_head + toRead) % _capacity;
        _count -= toRead;

        if (_count == 0)
        {
            _head = 0;
        }

        return toRead;
    }

    /// <summary>
    /// Peeks up to destination.Length samples from the ring buffer without consuming them.
    /// Returns the actual number of samples peeked.
    /// </summary>
    public int Peek(Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.IsEmpty || _count == 0 || _buffer == null) return 0;

        int toPeek = Math.Min(destination.Length, _count);
        int firstChunk = Math.Min(toPeek, _capacity - _head);
        _buffer.AsSpan(_head, firstChunk).CopyTo(destination[..firstChunk]);

        int secondChunk = toPeek - firstChunk;
        if (secondChunk > 0)
        {
            _buffer.AsSpan(0, secondChunk).CopyTo(destination.Slice(firstChunk, secondChunk));
        }

        return toPeek;
    }

    /// <summary>
    /// Advances the read head by count samples, discarding them.
    /// </summary>
    public int Advance(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count <= 0 || _count == 0) return 0;

        int toAdvance = Math.Min(count, _count);
        _head = (_head + toAdvance) % _capacity;
        _count -= toAdvance;

        if (_count == 0)
        {
            _head = 0;
        }

        return toAdvance;
    }

    /// <summary>
    /// Clears all buffered samples without deallocating the underlying rented pool memory.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _head = 0;
        _count = 0;
    }

    private void Grow(int minCapacity)
    {
        if (_buffer == null) return;

        int newCapacity = Math.Max(_capacity * 2, minCapacity);
        float[] newBuffer = ArrayPool<float>.Shared.Rent(newCapacity);

        if (_count > 0)
        {
            int firstChunk = Math.Min(_count, _capacity - _head);
            _buffer.AsSpan(_head, firstChunk).CopyTo(newBuffer.AsSpan(0, firstChunk));

            int secondChunk = _count - firstChunk;
            if (secondChunk > 0)
            {
                _buffer.AsSpan(0, secondChunk).CopyTo(newBuffer.AsSpan(firstChunk, secondChunk));
            }
        }

        ArrayPool<float>.Shared.Return(_buffer);
        _buffer = newBuffer;
        _capacity = newBuffer.Length;
        _head = 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            var buf = Interlocked.Exchange(ref _buffer, null);
            if (buf != null)
            {
                ArrayPool<float>.Shared.Return(buf);
            }
            _head = 0;
            _count = 0;
            _capacity = 0;
        }
    }
}
