using System;
using System.Threading;

namespace DualSound.Audio.Capture;

/// <summary>
/// A bounded, thread-safe, pre-allocated circular ring buffer for interleaved 32-bit float audio samples.
/// Designed for low-latency real-time audio streaming with zero heap allocations during read/write.
/// Automatically handles overflow by advancing the read position and tracking dropped samples.
/// </summary>
public sealed class BoundedAudioBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private readonly object _syncLock = new();

    private int _writeIndex;
    private int _readIndex;
    private int _availableSamples;
    private long _totalSamplesWritten;
    private long _totalSamplesRead;
    private long _overflowCount;

    /// <summary>
    /// Total sample capacity of the ring buffer (e.g. 96,000 floats for 1 second of stereo at 48 kHz).
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Current number of samples available to be read from the buffer.
    /// </summary>
    public int AvailableSamples
    {
        get
        {
            lock (_syncLock)
            {
                return _availableSamples;
            }
        }
    }

    /// <summary>
    /// Buffer occupancy percentage [0.0, 1.0].
    /// </summary>
    public float OccupancyPercentage
    {
        get
        {
            lock (_syncLock)
            {
                return _capacity > 0 ? Math.Clamp((float)_availableSamples / _capacity, 0.0f, 1.0f) : 0f;
            }
        }
    }

    /// <summary>
    /// Cumulative count of overflow occurrences (when buffer was full and samples had to be dropped).
    /// </summary>
    public long OverflowCount
    {
        get
        {
            lock (_syncLock)
            {
                return _overflowCount;
            }
        }
    }

    /// <summary>
    /// Cumulative count of total samples written to the buffer.
    /// </summary>
    public long TotalSamplesWritten
    {
        get
        {
            lock (_syncLock)
            {
                return _totalSamplesWritten;
            }
        }
    }

    /// <summary>
    /// Initializes a bounded audio ring buffer with a fixed capacity.
    /// </summary>
    /// <param name="capacity">Capacity in samples (must be greater than 0).</param>
    public BoundedAudioBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _buffer = new float[capacity];
    }

    /// <summary>
    /// Writes a slice of float samples into the circular buffer.
    /// Thread-safe and allocation-free.
    /// If the incoming samples exceed available capacity, older unread samples are dropped to prevent memory growth.
    /// </summary>
    /// <param name="samples">Read-only span of incoming float samples.</param>
    /// <returns>Number of samples written.</returns>
    public int Write(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        lock (_syncLock)
        {
            var count = samples.Length;

            // If the chunk is larger than the whole buffer, write only the most recent 'capacity' samples
            if (count > _capacity)
            {
                samples = samples.Slice(count - _capacity, _capacity);
                count = _capacity;
                _overflowCount++;
            }

            var freeSpace = _capacity - _availableSamples;
            if (count > freeSpace)
            {
                // Overflow: Advance read pointer to discard oldest samples
                var overflow = count - freeSpace;
                _readIndex = (_readIndex + overflow) % _capacity;
                _availableSamples -= overflow;
                _overflowCount++;
            }

            // Copy in up to two contiguous blocks
            var firstChunk = Math.Min(count, _capacity - _writeIndex);
            samples.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(_writeIndex, firstChunk));

            var secondChunk = count - firstChunk;
            if (secondChunk > 0)
            {
                samples.Slice(firstChunk, secondChunk).CopyTo(_buffer.AsSpan(0, secondChunk));
            }

            _writeIndex = (_writeIndex + count) % _capacity;
            _availableSamples += count;
            _totalSamplesWritten += count;

            return count;
        }
    }

    /// <summary>
    /// Reads samples from the buffer into the target destination span.
    /// Thread-safe and allocation-free.
    /// </summary>
    /// <param name="destination">Destination span to receive samples.</param>
    /// <returns>Actual number of samples read (up to destination.Length).</returns>
    public int Read(Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        lock (_syncLock)
        {
            if (_availableSamples == 0)
            {
                return 0;
            }

            var toRead = Math.Min(destination.Length, _availableSamples);

            var firstChunk = Math.Min(toRead, _capacity - _readIndex);
            _buffer.AsSpan(_readIndex, firstChunk).CopyTo(destination.Slice(0, firstChunk));

            var secondChunk = toRead - firstChunk;
            if (secondChunk > 0)
            {
                _buffer.AsSpan(0, secondChunk).CopyTo(destination.Slice(firstChunk, secondChunk));
            }

            _readIndex = (_readIndex + toRead) % _capacity;
            _availableSamples -= toRead;
            _totalSamplesRead += toRead;

            return toRead;
        }
    }

    /// <summary>
    /// Clears all samples and resets read and write pointers without re-allocating memory.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            _writeIndex = 0;
            _readIndex = 0;
            _availableSamples = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }

    /// <summary>
    /// Resets all counters including overflow and sample counts.
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            Clear();
            _totalSamplesWritten = 0;
            _totalSamplesRead = 0;
            _overflowCount = 0;
        }
    }
}

