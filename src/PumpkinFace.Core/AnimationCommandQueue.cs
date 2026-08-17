using System.Diagnostics.CodeAnalysis;

namespace PumpkinFace.Core;

/// <summary>
/// A producer-facing command endpoint. Posting is non-blocking so a remote or AI
/// controller can never stall the render loop.
/// </summary>
public interface IAnimationCommandSink
{
    bool TryPost(AnimationCommand command);
}

/// <summary>
/// A consumer-facing command endpoint. The display drains this source from the
/// Godot main thread before mutating scene or rendering state.
/// </summary>
public interface IAnimationCommandSource
{
    int Count { get; }

    bool TryDequeue([NotNullWhen(true)] out AnimationCommand? command);
}

/// <summary>
/// A bounded, thread-safe FIFO. When full, the new command is rejected and the
/// caller receives false; already accepted user actions are never silently lost.
/// </summary>
public sealed class BoundedAnimationCommandQueue : IAnimationCommandSink, IAnimationCommandSource
{
    public const int DefaultCapacity = 128;

    private readonly object _gate = new();
    private readonly Queue<AnimationCommand> _commands;
    private long _rejectedCount;

    public BoundedAnimationCommandQueue(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        Capacity = capacity;
        _commands = new Queue<AnimationCommand>(capacity);
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _commands.Count;
            }
        }
    }

    public long RejectedCount
    {
        get
        {
            lock (_gate)
            {
                return _rejectedCount;
            }
        }
    }

    public bool TryPost(AnimationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (_commands.Count >= Capacity)
            {
                _rejectedCount++;
                return false;
            }

            _commands.Enqueue(command);
            return true;
        }
    }

    /// <summary>
    /// Alias for <see cref="TryPost"/> that is convenient for queue-oriented callers.
    /// </summary>
    public bool TryEnqueue(AnimationCommand command) => TryPost(command);

    public bool TryDequeue([NotNullWhen(true)] out AnimationCommand? command)
    {
        lock (_gate)
        {
            if (_commands.Count == 0)
            {
                command = null;
                return false;
            }

            command = _commands.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Drains up to <paramref name="maximumCount"/> commands in FIFO order.
    /// </summary>
    public int DrainTo(ICollection<AnimationCommand> destination, int maximumCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (maximumCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount), maximumCount, "Maximum count cannot be negative.");
        }

        lock (_gate)
        {
            var count = Math.Min(maximumCount, _commands.Count);
            for (var index = 0; index < count; index++)
            {
                destination.Add(_commands.Dequeue());
            }

            return count;
        }
    }
}
