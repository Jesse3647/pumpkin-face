namespace PumpkinFace.Core;

/// <summary>
/// Seeded, fixed-step character performance. No engine clock, renderer, inference, or I/O.
/// Public methods are synchronized so command producers can read immutable status snapshots.
/// </summary>
public sealed class PerformanceController : IPerformanceStatusSource
{
    private const double Step = 1d / 240;
    private const PerformanceChannels AllChannels = (PerformanceChannels)127;
    private readonly object _gate = new();
    private Random _random;
    private Random _attentionRandom;
    private readonly List<Performance> _active = [];
    private readonly Dictionary<Guid, PerformanceStatus> _status = [];
    private double _remainder;
    private double _blinkIn = 1.4;
    private double _lookIn = .7;
    private double _reactionIn = 5;
    private double? _demoTime;
    private int _demoIndex;
    private bool _looking;
    private bool _blinking;

    public PerformanceController(int seed = 0x504B4E)
    {
        _random = new Random(seed);
        _attentionRandom = new Random(seed ^ 0x455945);
    }
    public BehaviorState? State { get; private set; }
    public CharacterDefinition Character { get; private set; } = CharacterCatalog.Get(CharacterCatalog.DefaultId);
    public float MotionAmount { get; private set; } = .65f;
    public FacePose Frame { get; private set; }
    public PerformanceSnapshot Snapshot
    {
        get { lock (_gate) return new(State, MotionAmount, Array.AsReadOnly(_status.Values.ToArray()), Character.Id); }
    }

    public void Reject(Guid id, string reason)
    {
        lock (_gate)
        {
            if (!_status.ContainsKey(id)) SetStatus(id, PerformanceOutcome.Rejected, reason);
        }
    }

    public bool Handle(AnimationCommand command)
    {
        lock (_gate)
        {
            switch (command)
            {
                case SelectCharacterCommand selection:
                    if (!CharacterCatalog.IsKnown(selection.CharacterId) || selection.CharacterId == Character.Id) break;
                    Character = CharacterCatalog.Get(selection.CharacterId);
                    // Keep the operator's behavior/scene choices, but retire the previous character's acting.
                    _demoTime = null;
                    foreach (Performance item in _active.ToArray()) Release(item);
                    _lookIn = .6;
                    _blinkIn = 1.4;
                    _reactionIn = 5;
                    break;
                case PlayGestureCommand gesture:
                    if (_status.ContainsKey(gesture.RequestId)) return true;
                    if (gesture.RequestId == Guid.Empty || !Enum.IsDefined(gesture.Gesture) ||
                        !float.IsFinite(gesture.Intensity) || gesture.Intensity is < 0 or > 1)
                        Reject(gesture.RequestId, "Gesture and intensity must be valid (0..1).");
                    else StartGesture(gesture.Gesture, gesture.Intensity, gesture.RequestId);
                    break;
                case SetGazeTargetCommand gaze:
                    if (_status.ContainsKey(gaze.RequestId)) return true;
                    if (gaze.RequestId == Guid.Empty || !float.IsFinite(gaze.X) || !float.IsFinite(gaze.Y) ||
                        Math.Abs(gaze.X) > 1 || Math.Abs(gaze.Y) > 1 ||
                        !double.IsFinite(gaze.HoldSeconds) || gaze.HoldSeconds is < .1 or > 30)
                        Reject(gaze.RequestId, "Gaze requires coordinates -1..1 and a hold of 0.1..30 seconds.");
                    else StartGaze(gaze.X, gaze.Y, gaze.HoldSeconds, gaze.RequestId);
                    break;
                case SetBehaviorStateCommand behavior:
                    if (behavior.State is { } value && !Enum.IsDefined(value)) return true;
                    State = behavior.State;
                    _lookIn = .6;
                    _blinkIn = 1.4;
                    _reactionIn = 5;
                    _demoTime = null;
                    foreach (Performance item in _active.Where(item => item.Autonomous).ToArray()) Release(item);
                    break;
                case SetMotionAmountCommand motion:
                    if (float.IsFinite(motion.Amount)) MotionAmount = Math.Clamp(motion.Amount, 0, 1);
                    break;
                case CancelPerformanceCommand cancel:
                    foreach (Performance item in _active.Where(item => item.Id == cancel.RequestId).ToArray()) Release(item);
                    break;
                case PlayPerformanceDemoCommand:
                    Stop();
                    _random = new Random(0x44454D4F);
                    _attentionRandom = new Random(0x44454D4F ^ 0x455945);
                    _blinkIn = 1.4;
                    _lookIn = .7;
                    _reactionIn = 5;
                    _demoTime = 0;
                    _demoIndex = 0;
                    break;
                case StopCommand:
                    Stop();
                    break;
                default: return false;
            }
            Compose();
            return true;
        }
    }

    /// <summary>Compatibility controls share the same attention and blink scheduler.</summary>
    public void SetLegacyScene(SceneId scene, bool enabled)
    {
        lock (_gate)
        {
            if (scene == SceneId.Looking) _looking = enabled;
            if (scene == SceneId.Blinking) _blinking = enabled;
            if (!enabled)
            {
                PerformanceChannels mask = scene == SceneId.Looking ? PerformanceChannels.Gaze :
                    scene == SceneId.Blinking ? PerformanceChannels.LeftLid | PerformanceChannels.RightLid : PerformanceChannels.None;
                foreach (Performance item in _active.Where(item => item.Autonomous && (item.Channels & mask) != 0).ToArray()) Release(item);
            }
        }
    }

    public void Update(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        lock (_gate)
        {
            _remainder += seconds;
            while (_remainder + 1e-10 >= Step)
            {
                _remainder = Math.Max(0, _remainder - Step);
                foreach (Performance item in _active.ToArray())
                {
                    item.Time += Step;
                    if (item.Gesture is null && !item.Releasing && item.Time >= item.CorrectionAt + .27)
                        ScheduleCorrection(item, item.Time + AttentionNext(1.3, 2.5));
                    if (item.Time + 1e-10 >= item.Duration)
                    {
                        _active.Remove(item);
                        if (!item.Releasing && item.Id != Guid.Empty) SetStatus(item.Id, PerformanceOutcome.Completed);
                    }
                }
                AdvanceDemo();
                AdvanceBehavior();
                Compose();
            }
        }
    }

    public FacePose ComposePose(FacePose expression)
    {
        lock (_gate) return PoseCompositor.Compose(expression with { Tremble = expression.Tremble * MotionAmount }, Frame, MotionAmount);
    }

    private void Stop()
    {
        State = null;
        _looking = _blinking = false;
        _demoTime = null;
        foreach (Performance item in _active.ToArray()) Release(item);
    }

    private void AdvanceBehavior()
    {
        if (State is null && !_looking && !_blinking) return;
        CharacterPersonality personality = Character.Personality;
        if (State is not null || _blinking)
        {
            _blinkIn -= Step;
            if (_blinkIn <= 0)
            {
                StartGesture(GestureId.Blink, .65f, Guid.Empty);
                _blinkIn = _random.NextDouble() < .12 ? .4 : Next(2.8, 6.2) * personality.BlinkIntervalScale;
            }
        }
        if (State is not null || _looking)
        {
            _lookIn -= Step;
            if (_lookIn <= 0)
            {
                float x = State == BehaviorState.Listening ? (float)Next(-.08, .08) : (float)Next(-.7, .7);
                float y = State == BehaviorState.Thinking ? -.45f : (float)Next(-.2, .25);
                x *= personality.AttentionRange;
                y *= personality.AttentionRange;
                double hold = (State == BehaviorState.Thinking ? Next(3, 5) : Next(1.5, 3.2)) * personality.HoldScale;
                StartGaze(x, y, hold, Guid.Empty);
                _lookIn = hold + Next(1.2, 3);
            }
        }
        if (State is not null)
        {
            _reactionIn -= Step;
            if (_reactionIn <= 0)
            {
                GestureId gesture = State == BehaviorState.Listening ? GestureId.Nod : GestureId.CuriousTilt;
                if (State == BehaviorState.Idle && personality.DelightChance > 0 && _random.NextDouble() < personality.DelightChance)
                    gesture = GestureId.Delight;
                StartGesture(gesture, personality.ReactionIntensity, Guid.Empty);
                _reactionIn = Next(6, 11) * personality.ReactionIntervalScale;
            }
        }
    }

    private void AdvanceDemo()
    {
        if (_demoTime is not { } time) return;
        time += Step;
        _demoTime = time;
        double[] beats = [0, 1.4, 2.6, 3.5, 6.2, 8.8, 11.5, 13.4];
        while (_demoIndex < beats.Length && time + 1e-10 >= beats[_demoIndex])
        {
            switch (_demoIndex++)
            {
                case 0: StartGaze(.65f, -.15f, 5.5, Guid.Empty, false); break;
                case 1: StartGesture(GestureId.CuriousTilt, .65f, Guid.Empty, false); break;
                case 2: StartGesture(GestureId.Blink, .65f, Guid.Empty, false); break;
                case 3: StartGesture(GestureId.Nod, .65f, Guid.Empty, false); break;
                case 4: State = BehaviorState.Listening; _lookIn = 0; break;
                case 5: State = BehaviorState.Thinking; _lookIn = 0; break;
                case 6: State = null; StartGesture(GestureId.Surprise, .65f, Guid.Empty, false); break;
                case 7: StartGesture(GestureId.Delight, .65f, Guid.Empty, false); _demoTime = null; break;
            }
        }
    }

    private void StartGesture(GestureId id, float intensity, Guid requestId, bool? autonomous = null)
    {
        GestureDefinition definition = GestureCatalog.Get(id);
        Start(new Performance { Id = requestId, Gesture = id, Intensity = intensity,
            Duration = definition.Duration, Channels = definition.Channels,
            Autonomous = autonomous ?? requestId == Guid.Empty });
    }

    private void StartGaze(float x, float y, double hold, Guid requestId, bool? autonomous = null)
    {
        Performance attention = new() { Id = requestId, X = x, Y = y, Duration = hold + .5,
            Channels = PerformanceChannels.Gaze,
            Autonomous = autonomous ?? requestId == Guid.Empty };
        ScheduleCorrection(attention, AttentionNext(1.2, 1.9));
        Start(attention);
    }

    private double AttentionNext(double min, double max) => min + _attentionRandom.NextDouble() * (max - min);

    private void ScheduleCorrection(Performance attention, double at)
    {
        attention.CorrectionAt = at;
        attention.CorrectionX = (float)AttentionNext(-.018, .018);
        attention.CorrectionY = (float)AttentionNext(-.012, .012);
    }

    // Attention's delayed face response is additive and never owns the gesture motion channel.
    private FacePose AttentionMotion()
    {
        Performance? attention = _active.FirstOrDefault(item => item.Channels.HasFlag(PerformanceChannels.Gaze));
        return attention is null ? default : Sample(attention);
    }

    private void Start(Performance next)
    {
        if (next.Autonomous && _active.Any(item => !item.Releasing && (item.Channels & next.Channels) != 0)) return;
        FacePose follow = AttentionMotion();
        next.From = Frame;
        if (next.Gesture is null)
            next.From = next.From with { MotionX = follow.MotionX, MotionY = follow.MotionY, MotionRoll = follow.MotionRoll };
        else if (next.Channels.HasFlag(PerformanceChannels.Motion))
            next.From = next.From with { MotionX = Frame.MotionX - follow.MotionX,
                MotionY = Frame.MotionY - follow.MotionY, MotionRoll = Frame.MotionRoll - follow.MotionRoll };
        foreach (Performance previous in _active.Where(item => (item.Channels & next.Channels) != 0).ToArray())
        {
            // Release any non-overlapping channels; the new action blends the overlapping channels from Frame.
            if (!previous.Releasing && previous.Id != Guid.Empty) SetStatus(previous.Id, PerformanceOutcome.Cancelled, "Replaced by a conflicting action.");
            previous.Channels &= ~next.Channels;
            if (previous.Channels == PerformanceChannels.None) _active.Remove(previous);
            else Release(previous);
        }
        _active.Add(next);
        if (next.Id != Guid.Empty) SetStatus(next.Id, PerformanceOutcome.Running);
        Compose();
    }

    private void Release(Performance item)
    {
        if (item.Releasing) return;
        item.From = Sample(item);
        item.Releasing = true;
        item.Time = 0;
        item.Duration = .2;
        if (item.Id != Guid.Empty) SetStatus(item.Id, PerformanceOutcome.Cancelled);
    }

    private void Compose()
    {
        FacePose result = default;
        foreach (Performance item in _active) result = PoseCompositor.Mix(result, Sample(item), 1, item.Channels);
        FacePose follow = AttentionMotion();
        Frame = result with { MotionX = result.MotionX + follow.MotionX,
            MotionY = result.MotionY + follow.MotionY, MotionRoll = result.MotionRoll + follow.MotionRoll };
    }

    private static FacePose Sample(Performance item)
    {
        double t = item.Time;
        if (item.Releasing) return PoseCompositor.Mix(item.From, default, Ease(t / item.Duration), AllChannels);
        FacePose target = default;
        if (item.Gesture is not { } gesture)
        {
            float gaze = Ease(t / .11) * (1 - Ease((t - item.Duration + .32) / .32));
            float follow = Ease((t - .10) / .28) * (1 - Ease((t - item.Duration + .32) / .32));
            float correction = item.CorrectionAt + .27 < item.Duration - .32
                ? Ease((t - item.CorrectionAt) / .06) * (1 - Ease((t - item.CorrectionAt - .18) / .09)) : 0;
            float x = Math.Clamp(item.X + item.CorrectionX * correction, -1, 1);
            float y = Math.Clamp(item.Y + item.CorrectionY * correction, -1, 1);
            target = target with { LeftGazeX = x * gaze, RightGazeX = x * gaze,
                LeftGazeY = y * gaze, RightGazeY = y * gaze,
                MotionX = item.X * .25f * follow, MotionY = item.Y * .18f * follow, MotionRoll = item.X * .12f * follow };
        }
        else
        {
            GestureDefinition d = GestureCatalog.Get(gesture);
            float envelope = Ease(t / Math.Max(.01, d.Anticipation + .2)) * (1 - Ease((t - d.ActionEnd) / (d.SettleEnd - d.ActionEnd)));
            float a = envelope * item.Intensity;
            float wave = (float)Math.Sin(Math.Clamp((t - d.Anticipation) / (d.ActionEnd - d.Anticipation), 0, 1) * Math.PI * 2);
            switch (gesture)
            {
                case GestureId.Blink:
                case GestureId.LeftWink:
                case GestureId.RightWink:
                    double close = gesture == GestureId.Blink ? .065 : .10;
                    double holdEnd = gesture == GestureId.Blink ? .095 : .30;
                    float closure = Ease(t / close) * (1 - Ease((t - holdEnd) / (d.Duration - holdEnd)));
                    closure *= Math.Clamp(item.Intensity / .65f, 0, 1);
                    target = target with { LeftEyelidOpen = -closure, RightEyelidOpen = -closure };
                    break;
                case GestureId.CuriousTilt:
                    target = target with { MotionRoll = a * .8f, MotionY = -a * .12f,
                        LeftBrowTension = -.28f * a, RightBrowTension = .20f * a };
                    break;
                case GestureId.Nod: target = target with { MotionY = wave * a }; break;
                case GestureId.Shake: target = target with { MotionX = wave * a, MotionRoll = wave * a * .16f }; break;
                case GestureId.Surprise:
                    target = target with { LeftBrowTension = -.45f * a, RightBrowTension = -.45f * a,
                        PupilSize = .25f * a, JawOpen = .5f * a, MouthWidth = -.35f * a,
                        MouthRoundness = .75f * a, MotionY = -.55f * a };
                    break;
                case GestureId.Delight:
                    target = target with { LeftBrowTension = -.18f * a, RightBrowTension = -.10f * a,
                        LeftMouthCorner = .10f * a, RightMouthCorner = .22f * a, JawOpen = .16f * a,
                        MouthWidth = .12f * a, MotionY = -.22f * a, MotionRoll = .22f * a };
                    break;
            }
            // A small preparatory counter-movement makes the gesture's main beat readable.
            if (d.Channels.HasFlag(PerformanceChannels.Motion) && t < d.Anticipation)
                target = target with { MotionY = .10f * item.Intensity * (float)Math.Sin(t / d.Anticipation * Math.PI) };
        }
        // Broader travel makes deliberate nods, shakes and curious tilts readable.
        // Convert other motion from its original physical range so ambient attention,
        // surprise and delight retain their established movement.
        if (item.Gesture is not (GestureId.Nod or GestureId.Shake))
        {
            target = target with
            {
                MotionX = target.MotionX * (.01f / PerformanceMotionLimits.HorizontalFraction),
                MotionY = target.MotionY * (.01f / PerformanceMotionLimits.VerticalFraction),
                MotionRoll = item.Gesture == GestureId.CuriousTilt ? target.MotionRoll :
                    target.MotionRoll * (3f / PerformanceMotionLimits.RollDegrees),
            };
        }
        // Blink entry must retain its fast closing phase; interruptions still begin at the current pose.
        double blend = item.Gesture is GestureId.Blink or GestureId.LeftWink or GestureId.RightWink ? .025 : .12;
        PerformanceChannels channels = item.Gesture is null ? item.Channels | PerformanceChannels.Motion : item.Channels;
        return PoseCompositor.Mix(item.From, target, Ease(t / blend), channels);
    }

    public static float Ease(double t)
    {
        float x = (float)Math.Clamp(t, 0, 1);
        return x * x * (3 - 2 * x);
    }
    private double Next(double min, double max) => min + _random.NextDouble() * (max - min);
    private void SetStatus(Guid id, PerformanceOutcome outcome, string? reason = null)
    {
        _status[id] = new(id, outcome, reason);
        // Keep all in-flight requests and a bounded recent terminal history.
        while (_status.Count > 256)
        {
            Guid oldest = _status.First(pair => pair.Value.Outcome != PerformanceOutcome.Running).Key;
            _status.Remove(oldest);
        }
    }
    private sealed class Performance
    {
        public Guid Id;
        public GestureId? Gesture;
        public PerformanceChannels Channels;
        public float Intensity = .65f;
        public float X;
        public float Y;
        public double CorrectionAt;
        public float CorrectionX;
        public float CorrectionY;
        public double Time;
        public double Duration;
        public bool Autonomous;
        public bool Releasing;
        public FacePose From;
    }
}
