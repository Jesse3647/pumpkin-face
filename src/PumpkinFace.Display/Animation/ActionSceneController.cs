using Godot;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Animation;

public readonly record struct ActionSceneFrame(
    Vector2 Gaze,
    float EyelidOpen,
    float JawOpen,
    float LightingMultiplier)
{
    public static ActionSceneFrame Rest { get; } = new(Vector2.Zero, 1f, 0f, 1f);
}

/// <summary>
/// Composes independent, looping action channels over the selected emotion.
/// Manual selections can run concurrently; autoplay creates short randomized
/// combinations without changing the emotion or its intensity.
/// </summary>
public sealed class ActionSceneController
{
    private static readonly SceneId[] Scenes = Enum.GetValues<SceneId>();
    private readonly Random _random;
    private readonly HashSet<SceneId> _selectedScenes = [];
    private readonly Dictionary<SceneId, SceneState> _states = [];
    private double _autoplayDelay;
    private bool _manualSelection;

    public ActionSceneController(int? seed = null) =>
        _random = seed is { } value ? new Random(value) : new Random();

    public SceneId? CurrentScene => _states.Count == 1 ? _states.Keys.First() : null;
    public IReadOnlySet<SceneId> SelectedScenes => _selectedScenes;
    public IReadOnlyCollection<SceneId> ActiveScenes => _states.Keys;
    public bool AutoplayEnabled { get; private set; }
    public ActionSceneFrame Frame { get; private set; } = ActionSceneFrame.Rest;

    public bool IsSelected(SceneId scene) => _selectedScenes.Contains(scene);

    public void SetAutoplay(bool enabled)
    {
        AutoplayEnabled = enabled;
        if (enabled)
        {
            _manualSelection = false;
            _selectedScenes.Clear();
            _states.Clear();
            Frame = ActionSceneFrame.Rest;
            _autoplayDelay = Next(0.75d, 2.25d);
        }
    }

    /// <summary>
    /// Replaces the current manual combination with one scene. New UI code uses
    /// SetSelected to build combinations; this remains useful to simple clients.
    /// </summary>
    public void Play(SceneId scene)
    {
        ValidateScene(scene);
        AutoplayEnabled = false;
        _manualSelection = true;
        _selectedScenes.Clear();
        _states.Clear();
        _selectedScenes.Add(scene);
        _states[scene] = StartState(scene);
        ComposeFrame();
    }

    public void SetSelected(SceneId scene, bool selected)
    {
        ValidateScene(scene);
        if (selected)
        {
            if (!_manualSelection)
            {
                _states.Clear();
            }

            AutoplayEnabled = false;
            _manualSelection = true;
            if (_selectedScenes.Add(scene))
            {
                _states[scene] = StartState(scene);
            }
        }
        else
        {
            _selectedScenes.Remove(scene);
            _states.Remove(scene);
            _manualSelection = _selectedScenes.Count > 0;
        }

        ComposeFrame();
    }

    public void Stop()
    {
        _selectedScenes.Clear();
        _states.Clear();
        _manualSelection = false;
        AutoplayEnabled = false;
        Frame = ActionSceneFrame.Rest;
    }

    public void Update(double elapsedSeconds)
    {
        double remaining = Math.Max(0d, elapsedSeconds);
        while (remaining > 0d)
        {
            double step = Math.Min(remaining, 0.05d);
            remaining -= step;

            if (_states.Count == 0)
            {
                Frame = ActionSceneFrame.Rest;
                if (AutoplayEnabled)
                {
                    _autoplayDelay -= step;
                    if (_autoplayDelay <= 0d)
                    {
                        StartAutoplayCombination();
                    }
                }

                continue;
            }

            foreach (SceneId scene in _states.Keys.ToArray())
            {
                SceneState state = _states[scene];
                state.Elapsed += step;
                UpdateState(scene, state, step);
                if (state.Elapsed >= state.Duration)
                {
                    if (_manualSelection && _selectedScenes.Contains(scene))
                    {
                        _states[scene] = StartState(scene);
                    }
                    else
                    {
                        _states.Remove(scene);
                    }
                }
            }

            ComposeFrame();
            if (_states.Count == 0 && AutoplayEnabled)
            {
                _autoplayDelay = Next(2.0d, 5.5d);
            }
        }
    }

    private SceneState StartState(SceneId scene)
    {
        SceneState state = new() { Duration = DurationFor(scene) };
        if (scene == SceneId.Looking)
        {
            BeginGazeTransition(state, RandomGazeTarget());
        }
        else if (scene == SceneId.Blinking)
        {
            state.EventTimer = Next(0.35d, 1.10d);
        }
        else if (scene == SceneId.CandleSputter)
        {
            BeginLightTransition(state);
        }

        return state;
    }

    private void UpdateState(SceneId scene, SceneState state, double step)
    {
        switch (scene)
        {
            case SceneId.Looking:
                UpdateLooking(state, step);
                break;
            case SceneId.Blinking:
                UpdateBlinking(state, step);
                break;
            case SceneId.Talking:
                UpdateTalking(state);
                break;
            case SceneId.CandleSputter:
                UpdateCandleSputter(state, step);
                break;
        }
    }

    private void UpdateLooking(SceneState state, double step)
    {
        double remaining = state.Duration - state.Elapsed;
        if (remaining <= 0.36d && state.GazeTarget != Vector2.Zero)
        {
            BeginGazeTransition(state, Vector2.Zero, Math.Max(0.12d, remaining));
        }

        if (state.TransitionElapsed < state.TransitionDuration)
        {
            state.TransitionElapsed = Math.Min(state.TransitionDuration, state.TransitionElapsed + step);
            float amount = Smooth((float)(state.TransitionElapsed / Math.Max(0.0001d, state.TransitionDuration)));
            state.Frame = state.Frame with { Gaze = state.GazeFrom.Lerp(state.GazeTarget, amount) };
            if (state.TransitionElapsed >= state.TransitionDuration)
            {
                state.Frame = state.Frame with { Gaze = state.GazeTarget };
                state.EventTimer = Next(0.55d, 1.80d);
            }

            return;
        }

        state.EventTimer -= step;
        if (state.EventTimer <= 0d && remaining > 0.50d)
        {
            BeginGazeTransition(state, RandomGazeTarget());
        }
    }

    private void UpdateBlinking(SceneState state, double step)
    {
        if (state.Blinking)
        {
            state.TransitionElapsed += step;
            float progress = (float)Math.Clamp(state.TransitionElapsed / state.TransitionDuration, 0d, 1d);
            float openness = Mathf.Abs(progress * 2f - 1f);
            state.Frame = state.Frame with { EyelidOpen = Mathf.Lerp(0.055f, 1f, openness) };
            if (progress >= 1f)
            {
                state.Blinking = false;
                state.Frame = state.Frame with { EyelidOpen = 1f };
                state.EventTimer = Next(0.65d, 2.20d);
            }

            return;
        }

        state.EventTimer -= step;
        if (state.EventTimer <= 0d && state.Duration - state.Elapsed > 0.35d)
        {
            state.Blinking = true;
            state.TransitionElapsed = 0d;
            state.TransitionDuration = Next(0.14d, 0.25d);
        }
    }

    private static void UpdateTalking(SceneState state)
    {
        double remaining = state.Duration - state.Elapsed;
        float syllable = Mathf.Max(0f, Mathf.Sin((float)state.Elapsed * 10.7f));
        syllable = Mathf.Clamp(
            syllable * 0.78f + Mathf.Sin((float)state.Elapsed * 4.1f + 0.8f) * 0.10f,
            0f,
            1f);
        state.Frame = state.Frame with
        {
            JawOpen = (0.08f + syllable * 0.48f) * FadeOut(remaining, 0.32f),
        };
    }

    private void UpdateCandleSputter(SceneState state, double step)
    {
        state.TransitionElapsed = Math.Min(state.TransitionDuration, state.TransitionElapsed + step);
        float amount = Smooth((float)(state.TransitionElapsed / Math.Max(0.0001d, state.TransitionDuration)));
        float light = Mathf.Lerp(state.LightFrom, state.LightTarget, amount);
        float envelope = FadeOut(state.Duration - state.Elapsed, 0.30f);
        state.Frame = state.Frame with { LightingMultiplier = Mathf.Lerp(1f, light, envelope) };
        if (state.TransitionElapsed >= state.TransitionDuration && state.Duration - state.Elapsed > 0.35d)
        {
            BeginLightTransition(state);
        }
    }

    private void ComposeFrame()
    {
        ActionSceneFrame composed = ActionSceneFrame.Rest;
        if (_states.TryGetValue(SceneId.Looking, out SceneState? looking))
        {
            composed = composed with { Gaze = looking.Frame.Gaze };
        }
        if (_states.TryGetValue(SceneId.Blinking, out SceneState? blinking))
        {
            composed = composed with { EyelidOpen = blinking.Frame.EyelidOpen };
        }
        if (_states.TryGetValue(SceneId.Talking, out SceneState? talking))
        {
            composed = composed with { JawOpen = talking.Frame.JawOpen };
        }
        if (_states.TryGetValue(SceneId.CandleSputter, out SceneState? candle))
        {
            composed = composed with { LightingMultiplier = candle.Frame.LightingMultiplier };
        }
        Frame = composed;
    }

    private void StartAutoplayCombination()
    {
        _states.Clear();
        SceneId[] candidates = [.. Scenes];
        for (int index = candidates.Length - 1; index > 0; index--)
        {
            int swap = _random.Next(index + 1);
            (candidates[index], candidates[swap]) = (candidates[swap], candidates[index]);
        }

        int count = _random.Next(1, Math.Min(3, candidates.Length) + 1);
        for (int index = 0; index < count; index++)
        {
            _states[candidates[index]] = StartState(candidates[index]);
        }
    }

    private void BeginGazeTransition(SceneState state, Vector2 target, double? duration = null)
    {
        state.GazeFrom = state.Frame.Gaze;
        state.GazeTarget = target;
        state.TransitionElapsed = 0d;
        state.TransitionDuration = duration ?? Next(0.14d, 0.34d);
    }

    private void BeginLightTransition(SceneState state)
    {
        state.LightFrom = state.Frame.LightingMultiplier;
        bool deepDip = _random.NextDouble() < 0.22d;
        state.LightTarget = deepDip ? (float)Next(0.16d, 0.42d) : (float)Next(0.62d, 1.18d);
        state.TransitionElapsed = 0d;
        state.TransitionDuration = Next(0.05d, 0.24d);
    }

    private Vector2 RandomGazeTarget()
    {
        Vector2 target = new((float)Next(-0.90d, 0.90d), (float)Next(-0.72d, 0.72d));
        if (Mathf.Abs(target.X) + Mathf.Abs(target.Y) < 0.34f)
        {
            target.X = target.X < 0f ? -0.55f : 0.55f;
        }
        return target;
    }

    private static void ValidateScene(SceneId scene)
    {
        if (!Enum.IsDefined(scene))
        {
            throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown action scene.");
        }
    }

    private static float FadeOut(double remaining, float fadeDuration) =>
        Mathf.Clamp((float)Math.Min(1d, remaining / fadeDuration), 0f, 1f);
    private static float Smooth(float amount) => amount * amount * (3f - 2f * amount);
    private static double DurationFor(SceneId scene) => scene switch
    {
        SceneId.Looking => 12.0d,
        SceneId.Blinking => 10.0d,
        SceneId.Talking => 9.0d,
        SceneId.CandleSputter => 7.0d,
        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown action scene."),
    };
    private double Next(double minimum, double maximum) =>
        minimum + _random.NextDouble() * (maximum - minimum);

    private sealed class SceneState
    {
        public double Elapsed { get; set; }
        public double Duration { get; init; }
        public double EventTimer { get; set; }
        public double TransitionElapsed { get; set; }
        public double TransitionDuration { get; set; }
        public Vector2 GazeFrom { get; set; }
        public Vector2 GazeTarget { get; set; }
        public float LightFrom { get; set; } = 1f;
        public float LightTarget { get; set; } = 1f;
        public bool Blinking { get; set; }
        public ActionSceneFrame Frame { get; set; } = ActionSceneFrame.Rest;
    }
}
