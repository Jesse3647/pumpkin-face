namespace PumpkinFace.Core;

public enum SceneDirectorState
{
    Stopped,
    Neutral,
    Playing,
}

public enum SceneTransitionKind
{
    SceneStarted,
    SceneCompleted,
    Stopped,
    AutoplayChanged,
}

public readonly record struct SceneDurationRange(TimeSpan Minimum, TimeSpan Maximum)
{
    public bool Contains(TimeSpan value) => value >= Minimum && value <= Maximum;
}

public static class SceneTimings
{
    public static SceneDurationRange NeutralDelay { get; } = new(
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(8));

    public static SceneDurationRange For(EmotionId emotion) => emotion switch
    {
        EmotionId.Frightened => new SceneDurationRange(TimeSpan.FromSeconds(4.75), TimeSpan.FromSeconds(5.25)),
        EmotionId.Happy => new SceneDurationRange(TimeSpan.FromSeconds(5.75), TimeSpan.FromSeconds(6.25)),
        EmotionId.Sad => new SceneDurationRange(TimeSpan.FromSeconds(5.75), TimeSpan.FromSeconds(6.25)),
        _ => throw new ArgumentOutOfRangeException(nameof(emotion), emotion, "Unknown emotion."),
    };
}

public readonly record struct SceneDirectorSnapshot(
    SceneDirectorState State,
    EmotionId? CurrentScene,
    bool AutoplayEnabled,
    bool IsManuallyTriggered,
    TimeSpan PhaseElapsed,
    TimeSpan PhaseDuration,
    long Revision)
{
    public TimeSpan PhaseRemaining => PhaseDuration <= PhaseElapsed
        ? TimeSpan.Zero
        : PhaseDuration - PhaseElapsed;

    public TimeSpan NeutralDelayRemaining => State == SceneDirectorState.Neutral
        ? PhaseRemaining
        : TimeSpan.Zero;

    public float PhaseProgress => PhaseDuration <= TimeSpan.Zero
        ? 0f
        : (float)Math.Clamp(PhaseElapsed.TotalSeconds / PhaseDuration.TotalSeconds, 0d, 1d);
}

public readonly record struct SceneDirectorTransition(
    SceneTransitionKind Kind,
    EmotionId? PreviousScene,
    EmotionId? Scene,
    bool IsManuallyTriggered,
    TimeSpan CrossfadeDuration,
    SceneDirectorSnapshot Snapshot);

/// <summary>
/// Owns scene selection and timing. Callers must invoke <see cref="Handle"/> and
/// <see cref="Update"/> on the display's main thread.
/// </summary>
public interface ISceneDirector
{
    SceneDirectorSnapshot Snapshot { get; }

    TimeSpan CrossfadeDuration { get; }

    event Action<SceneDirectorTransition>? Transitioned;

    /// <returns>True when the command belongs to the scene director.</returns>
    bool Handle(AnimationCommand command);

    void Update(TimeSpan elapsed);
}

/// <summary>
/// Deterministic autoplay scheduler with a shuffled scene bag and no adjacent
/// automatic repeats.
/// </summary>
public sealed class SceneDirector : ISceneDirector
{
    public static readonly TimeSpan DefaultCrossfadeDuration = TimeSpan.FromMilliseconds(250);

    private const int DefaultSeed = 0x504B4E;
    private readonly DeterministicRandom _random;
    private readonly EmotionId[] _shuffleBag = Enum.GetValues<EmotionId>();
    private int _shuffleIndex;
    private SceneDirectorState _state;
    private EmotionId? _currentScene;
    private EmotionId? _lastScene;
    private bool _autoplayEnabled;
    private bool _isManuallyTriggered;
    private TimeSpan _phaseElapsed;
    private TimeSpan _phaseDuration;
    private long _revision;

    public SceneDirector(
        int seed = DefaultSeed,
        bool autoplayEnabled = true,
        TimeSpan? crossfadeDuration = null)
    {
        var crossfade = crossfadeDuration ?? DefaultCrossfadeDuration;
        if (crossfade < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(crossfadeDuration),
                crossfade,
                "Crossfade duration cannot be negative.");
        }

        CrossfadeDuration = crossfade;
        _random = new DeterministicRandom(seed);
        _autoplayEnabled = autoplayEnabled;
        _state = autoplayEnabled ? SceneDirectorState.Neutral : SceneDirectorState.Stopped;
        _phaseDuration = autoplayEnabled ? NextDuration(SceneTimings.NeutralDelay) : TimeSpan.Zero;
        _shuffleIndex = _shuffleBag.Length;
    }

    public event Action<SceneDirectorTransition>? Transitioned;

    public TimeSpan CrossfadeDuration { get; }

    public SceneDirectorSnapshot Snapshot => new(
        _state,
        _currentScene,
        _autoplayEnabled,
        _isManuallyTriggered,
        _phaseElapsed,
        _phaseDuration,
        _revision);

    public bool Handle(AnimationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command)
        {
            case PlayEmotionCommand play:
                StartScene(play.Emotion, manuallyTriggered: true);
                return true;

            case NextEmotionCommand:
                StartScene(TakeNextScene(), manuallyTriggered: true);
                return true;

            case SetAutoplayCommand autoplay:
                SetAutoplay(autoplay.Enabled);
                return true;

            case StopCommand:
                Stop();
                return true;

            case ApplyCalibrationCommand:
                return false;

            default:
                return false;
        }
    }

    public void Update(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Elapsed time cannot be negative.");
        }

        var remaining = elapsed;
        var transitionGuard = 0;

        while (true)
        {
            if (++transitionGuard > 10_000)
            {
                throw new InvalidOperationException("Too many scene transitions in a single update.");
            }

            if (_state == SceneDirectorState.Stopped)
            {
                return;
            }

            if (_state == SceneDirectorState.Neutral)
            {
                if (!_autoplayEnabled)
                {
                    return;
                }

                var untilScene = RemainingInPhase();
                if (remaining < untilScene || remaining == TimeSpan.Zero && untilScene > TimeSpan.Zero)
                {
                    _phaseElapsed += remaining;
                    return;
                }

                remaining -= untilScene;
                StartScene(TakeNextScene(), manuallyTriggered: false);
                continue;
            }

            var untilComplete = RemainingInPhase();
            if (remaining < untilComplete || remaining == TimeSpan.Zero && untilComplete > TimeSpan.Zero)
            {
                _phaseElapsed += remaining;
                return;
            }

            remaining -= untilComplete;
            CompleteCurrentScene();
        }
    }

    private void SetAutoplay(bool enabled)
    {
        if (_autoplayEnabled == enabled)
        {
            return;
        }

        _autoplayEnabled = enabled;

        if (_state == SceneDirectorState.Stopped && enabled)
        {
            _state = SceneDirectorState.Neutral;
            _phaseElapsed = TimeSpan.Zero;
            _phaseDuration = NextDuration(SceneTimings.NeutralDelay);
        }
        else if (_state == SceneDirectorState.Neutral)
        {
            _phaseElapsed = TimeSpan.Zero;
            _phaseDuration = enabled ? NextDuration(SceneTimings.NeutralDelay) : TimeSpan.Zero;
        }

        _revision++;
        RaiseTransition(SceneTransitionKind.AutoplayChanged, previousScene: null, scene: _currentScene);
    }

    private void Stop()
    {
        if (_state == SceneDirectorState.Stopped && !_autoplayEnabled)
        {
            return;
        }

        var previousScene = _currentScene;
        var wasManuallyTriggered = _isManuallyTriggered;
        _autoplayEnabled = false;
        _state = SceneDirectorState.Stopped;
        _currentScene = null;
        _isManuallyTriggered = false;
        _phaseElapsed = TimeSpan.Zero;
        _phaseDuration = TimeSpan.Zero;
        _revision++;
        RaiseTransition(
            SceneTransitionKind.Stopped,
            previousScene,
            scene: null,
            isManuallyTriggered: wasManuallyTriggered);
    }

    private void StartScene(EmotionId scene, bool manuallyTriggered)
    {
        // Validates enum values supplied by untrusted remote controllers.
        var timing = SceneTimings.For(scene);
        var previousScene = _currentScene;

        _state = SceneDirectorState.Playing;
        _currentScene = scene;
        _lastScene = scene;
        _isManuallyTriggered = manuallyTriggered;
        _phaseElapsed = TimeSpan.Zero;
        _phaseDuration = NextDuration(timing);
        _revision++;
        RaiseTransition(SceneTransitionKind.SceneStarted, previousScene, scene);
    }

    private void CompleteCurrentScene()
    {
        var completedScene = _currentScene;
        var wasManuallyTriggered = _isManuallyTriggered;
        _state = SceneDirectorState.Neutral;
        _currentScene = null;
        _isManuallyTriggered = false;
        _phaseElapsed = TimeSpan.Zero;
        _phaseDuration = _autoplayEnabled ? NextDuration(SceneTimings.NeutralDelay) : TimeSpan.Zero;
        _revision++;
        RaiseTransition(
            SceneTransitionKind.SceneCompleted,
            completedScene,
            scene: null,
            isManuallyTriggered: wasManuallyTriggered);
    }

    private TimeSpan RemainingInPhase() => _phaseDuration <= _phaseElapsed
        ? TimeSpan.Zero
        : _phaseDuration - _phaseElapsed;

    private EmotionId TakeNextScene()
    {
        if (_shuffleIndex >= _shuffleBag.Length)
        {
            RefillShuffleBag();
        }

        if (_lastScene is { } last && _shuffleBag[_shuffleIndex] == last)
        {
            var replacement = -1;
            for (var index = _shuffleIndex + 1; index < _shuffleBag.Length; index++)
            {
                if (_shuffleBag[index] != last)
                {
                    replacement = index;
                    break;
                }
            }

            if (replacement < 0)
            {
                RefillShuffleBag();
                replacement = Array.FindIndex(_shuffleBag, candidate => candidate != last);
            }

            (_shuffleBag[_shuffleIndex], _shuffleBag[replacement]) =
                (_shuffleBag[replacement], _shuffleBag[_shuffleIndex]);
        }

        return _shuffleBag[_shuffleIndex++];
    }

    private void RefillShuffleBag()
    {
        for (var index = _shuffleBag.Length - 1; index > 0; index--)
        {
            var swapIndex = _random.NextInt(index + 1);
            (_shuffleBag[index], _shuffleBag[swapIndex]) = (_shuffleBag[swapIndex], _shuffleBag[index]);
        }

        _shuffleIndex = 0;
    }

    private TimeSpan NextDuration(SceneDurationRange range)
    {
        var minimumTicks = range.Minimum.Ticks;
        var tickRange = range.Maximum.Ticks - minimumTicks;
        var ticks = minimumTicks + (long)Math.Round(tickRange * _random.NextDouble());
        return TimeSpan.FromTicks(Math.Clamp(ticks, range.Minimum.Ticks, range.Maximum.Ticks));
    }

    private void RaiseTransition(
        SceneTransitionKind kind,
        EmotionId? previousScene,
        EmotionId? scene,
        bool? isManuallyTriggered = null)
    {
        Transitioned?.Invoke(new SceneDirectorTransition(
            kind,
            previousScene,
            scene,
            isManuallyTriggered ?? _isManuallyTriggered,
            CrossfadeDuration,
            Snapshot));
    }

    private sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(int seed)
        {
            _state = unchecked((uint)seed) + 0x9E3779B97F4A7C15UL;
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }

            return (int)(NextUInt64() % (uint)exclusiveMaximum);
        }

        public double NextDouble() => (NextUInt64() >> 11) * (1d / (1UL << 53));

        private ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
