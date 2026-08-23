using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class AnimationCommandQueueTests
{
    [Fact]
    public void Commands_AreValueObjects()
    {
        Assert.Equal(new PlayEmotionCommand(EmotionId.Happy), new PlayEmotionCommand(EmotionId.Happy));
        Assert.NotEqual(new PlayEmotionCommand(EmotionId.Happy), new PlayEmotionCommand(EmotionId.Sad));
        Assert.Equal(new NextEmotionCommand(), new NextEmotionCommand());
        Assert.Equal(new SetEmotionAmountCommand(0.65f), new SetEmotionAmountCommand(0.65f));
        Assert.Equal(
            new SetSceneEnabledCommand(SceneId.Looking, true),
            new SetSceneEnabledCommand(SceneId.Looking, true));
        Assert.Equal(new SetAutoplayCommand(true), new SetAutoplayCommand(true));
        Assert.Equal(new StopCommand(), new StopCommand());

        var calibration = ProjectionCalibration.Default with { Brightness = 1.25f };
        Assert.Equal(
            new ApplyCalibrationCommand(calibration),
            new ApplyCalibrationCommand(calibration with { }));
    }

    [Fact]
    public void Queue_PreservesFirstInFirstOutOrdering()
    {
        var queue = new BoundedAnimationCommandQueue(capacity: 4);
        AnimationCommand[] commands =
        [
            new PlayEmotionCommand(EmotionId.Happy),
            new SetAutoplayCommand(false),
            new NextEmotionCommand(),
            new StopCommand(),
        ];

        foreach (var command in commands)
        {
            Assert.True(queue.TryPost(command));
        }

        Assert.Equal(commands.Length, queue.Count);
        foreach (var expected in commands)
        {
            Assert.True(queue.TryDequeue(out var actual));
            Assert.Same(expected, actual);
        }

        Assert.False(queue.TryDequeue(out var empty));
        Assert.Null(empty);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Queue_RejectsNewestCommandWhenAtCapacity()
    {
        var queue = new BoundedAnimationCommandQueue(capacity: 2);
        var first = new PlayEmotionCommand(EmotionId.Happy);
        var second = new PlayEmotionCommand(EmotionId.Frightened);
        var rejected = new StopCommand();

        Assert.True(queue.TryPost(first));
        Assert.True(queue.TryEnqueue(second));
        Assert.False(queue.TryPost(rejected));

        Assert.Equal(2, queue.Count);
        Assert.Equal(1, queue.RejectedCount);
        Assert.True(queue.TryDequeue(out var firstActual));
        Assert.Same(first, firstActual);
        Assert.True(queue.TryDequeue(out var secondActual));
        Assert.Same(second, secondActual);
    }

    [Fact]
    public void DrainTo_RespectsLimitAndOrdering()
    {
        var queue = new BoundedAnimationCommandQueue(capacity: 4);
        var first = new NumberedCommand(1);
        var second = new NumberedCommand(2);
        var third = new NumberedCommand(3);
        queue.TryPost(first);
        queue.TryPost(second);
        queue.TryPost(third);
        var destination = new List<AnimationCommand>();

        var drained = queue.DrainTo(destination, maximumCount: 2);

        Assert.Equal(2, drained);
        Assert.Equal([first, second], destination);
        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var remainder));
        Assert.Same(third, remainder);
    }

    [Fact]
    public void Queue_IsSafeForConcurrentProducers()
    {
        const int commandCount = 1_000;
        var queue = new BoundedAnimationCommandQueue(commandCount);

        Parallel.For(0, commandCount, index =>
        {
            Assert.True(queue.TryPost(new NumberedCommand(index)));
        });

        var drained = new List<AnimationCommand>();
        Assert.Equal(commandCount, queue.DrainTo(drained));
        Assert.Equal(commandCount, drained.Count);
        Assert.Equal(
            Enumerable.Range(0, commandCount),
            drained.Cast<NumberedCommand>().Select(command => command.Value).Order());
    }

    [Fact]
    public void Queue_ValidatesArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedAnimationCommandQueue(0));

        var queue = new BoundedAnimationCommandQueue();
        Assert.Throws<ArgumentNullException>(() => queue.TryPost(null!));
        Assert.Throws<ArgumentNullException>(() => queue.DrainTo(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.DrainTo([], -1));
        Assert.Throws<ArgumentNullException>(() => new ApplyCalibrationCommand(null!));
    }

    private sealed record NumberedCommand(int Value) : AnimationCommand;
}
