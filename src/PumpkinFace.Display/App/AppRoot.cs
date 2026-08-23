using Godot;
using PumpkinFace.Core;
using PumpkinFace.Display.Animation;
using PumpkinFace.Display.Capture;
using PumpkinFace.Display.Persistence;
using PumpkinFace.Display.Rendering;
using PumpkinFace.Display.UI;

namespace PumpkinFace.Display.App;

/// <summary>
/// Composition root. It is the only place where operator input, persistence,
/// scheduling, animation, and Godot rendering meet.
/// </summary>
public sealed partial class AppRoot : Node
{
    private readonly BoundedAnimationCommandQueue _commands = new(64);

    private CalibrationProfileStore? _profiles;
    private SceneDirector? _director;
    private SceneAnimationController? _animations;
    private ActionSceneController? _actionScenes;
    private FaceStage? _stage;
    private ProjectorHost? _projector;
    private OperatorPanel? _operator;
    private AudioStreamPlayer? _speechPlayer;
    private Task<SpeechSynthesisResult>? _speechSynthesisTask;
    private KokoroSpeechSynthesizer? _kokoroSpeech;
    private string _speechVoice = "kokoro:af_heart";
    private bool _speechWasActive;
    private double _fpsRefreshElapsed;
    private bool _shuttingDown;
    private string? _pendingPersistenceError;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        SetProcessUnhandledKeyInput(true);

        if (TryGetCommandLineValue("--capture-dir=", out string? captureDirectory))
        {
            StartCaptureMode(captureDirectory!);
            return;
        }

        _profiles = new CalibrationProfileStore();
        PersistenceLoadResult load = _profiles.Load();
        _profiles.PersistenceFailed += (_, args) =>
            Interlocked.Exchange(ref _pendingPersistenceError, args.Exception.Message);

        _stage = new FaceStage { Name = "FaceStage" };
        _animations = new SceneAnimationController { Name = "SceneAnimations" };
        _actionScenes = new ActionSceneController { ExternalSpeechCompletion = true };
        _actionScenes.SetAutoplay(load.State.AutoplayEnabled);
        _projector = new ProjectorHost { Name = "ProjectorHost" };
        _operator = new OperatorPanel { Name = "OperatorPanel" };
        _speechPlayer = new AudioStreamPlayer
        {
            Name = "SpeechVoice",
            VolumeDb = -1.5f,
        };
        _speechPlayer.Finished += OnSpeechFinished;
        _kokoroSpeech = new KokoroSpeechSynthesizer(
            ProjectSettings.GlobalizePath("user://models"));
        AddChild(_stage);
        AddChild(_animations);
        AddChild(_projector);
        AddChild(_operator);
        AddChild(_speechPlayer);

        _director = new SceneDirector(
            seed: 0x504B4E,
            autoplayEnabled: false);
        _director.Transitioned += transition => _animations.ApplyTransition(transition);

        WireOperatorEvents();
        WireProjectorEvents();

        ProjectionCalibration calibration = load.State.SelectedProfile.Calibration.Normalize();
        _stage.SetCalibration(calibration);
        _stage.ShowGuides = false;
        _operator.SetPreviewTexture(_stage.Texture);
        _operator.SetAutoplay(load.State.AutoplayEnabled);
        _operator.SetSpeechVoices(KokoroSpeechSynthesizer.Voices, _speechVoice);
        _operator.SetGuides(false);
        _projector.SetTexture(_stage.Texture);
        RefreshProfilesAndCalibration();

        IReadOnlyList<DisplayChoice> displays = _projector.GetDisplays();
        bool savedDisplayExists = load.State.LastDisplayIndex is { } savedScreen &&
                                  displays.Any(display => display.Index == savedScreen);
        int requestedScreen = ResolveStartupScreen(load.State.LastDisplayIndex, displays);
        _operator.SetDisplays(displays, requestedScreen);
        bool fullscreen = _projector.OpenSafely(
            requestedScreen,
            preferFullscreen: savedDisplayExists);
        _stage.Resize(_projector.OutputSize);
        _operator.SetOutputState(_projector.IsVisible, fullscreen);

        if (savedDisplayExists)
        {
            DisplayChoice? selected = displays.FirstOrDefault(display => display.Index == requestedScreen);
            _profiles.RememberDisplay(requestedScreen, selected?.Label);
        }

        string status;
        bool warning;
        if (!string.IsNullOrWhiteSpace(load.Warning))
        {
            status = load.Warning;
            warning = true;
        }
        else if (fullscreen)
        {
            status = $"Projecting on display {requestedScreen + 1}";
            warning = false;
        }
        else
        {
            status = "No external display selected — using a safe windowed preview";
            warning = true;
        }

        _operator.SetStatus(status, warning);
        _operator.Preview.SetPreviewTexture(_stage.Texture);
    }

    public override void _Process(double delta)
    {
        if (_director is null || _animations is null || _stage is null || _operator is null)
        {
            return;
        }

        CompleteSpeechSynthesisIfReady();
        DrainCommands();
        _director.Update(TimeSpan.FromSeconds(Math.Max(0d, delta)));
        _animations.Synchronize(_director.Snapshot);
        bool talkingWasSelected = _actionScenes!.IsSelected(SceneId.Talking);
        _actionScenes!.Update(delta);
        if (_speechPlayer?.Playing == true)
        {
            double audiblePosition = Math.Max(
                0d,
                _speechPlayer.GetPlaybackPosition() +
                AudioServer.GetTimeSinceLastMix() -
                AudioServer.GetOutputLatency());
            _actionScenes.SynchronizeSpeech(TimeSpan.FromSeconds(audiblePosition));
        }
        if (talkingWasSelected && !_actionScenes.IsSelected(SceneId.Talking))
        {
            _operator.SetSelectedScenes(_actionScenes.SelectedScenes);
            _operator.SetStatus("Phrase finished");
        }
        ActionSceneFrame action = _actionScenes.Frame;
        FacePose expressionPose = _animations.CurrentPose;
        FacePose displayedPose = _animations.CurrentPose with
        {
            LeftGazeX = action.Gaze.X,
            LeftGazeY = action.Gaze.Y,
            RightGazeX = action.Gaze.X,
            RightGazeY = action.Gaze.Y,
            LeftEyelidOpen = action.EyelidOpen,
            RightEyelidOpen = action.EyelidOpen,
            JawOpen = Mathf.Lerp(0f, action.JawOpen, action.SpeechBlend),
            MouthWidth = Mathf.Lerp(expressionPose.MouthWidth, action.MouthWidth, action.SpeechBlend),
            MouthRoundness = Mathf.Lerp(
                expressionPose.MouthRoundness,
                action.MouthRoundness,
                action.SpeechBlend),
            SpeechBlend = action.SpeechBlend,
            LightingIntensity = _animations.CurrentPose.LightingIntensity * action.LightingMultiplier,
        };
        _stage.SetPose(displayedPose);
        UpdateSpeechAudio(action.SpeechActive);

        string? persistenceError = Interlocked.Exchange(ref _pendingPersistenceError, null);
        if (persistenceError is not null)
        {
            ReportPersistenceFailure(persistenceError);
        }

        _fpsRefreshElapsed += delta;
        if (_fpsRefreshElapsed >= 0.25)
        {
            _fpsRefreshElapsed = 0;
            string activity = _animations.CurrentEmotion.ToString();
            SceneId[] activeScenes = [.. _actionScenes.ActiveScenes.OrderBy(scene => scene)];
            if (activeScenes.Length > 0)
            {
                activity += $"  •  {string.Join(" + ", activeScenes)}";
            }

            _operator.SetFps(Engine.GetFramesPerSecond(), activity);
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        bool handled = true;
        switch (key.Keycode)
        {
            case Key.Space:
                Post(new NextEmotionCommand());
                break;
            case Key.Key1:
                Post(new PlayEmotionCommand(EmotionId.Frightened));
                break;
            case Key.Key2:
                Post(new PlayEmotionCommand(EmotionId.Happy));
                break;
            case Key.Key3:
                Post(new PlayEmotionCommand(EmotionId.Sad));
                break;
            case Key.L:
                ToggleScene(SceneId.Looking);
                break;
            case Key.B:
                ToggleScene(SceneId.Blinking);
                break;
            case Key.C:
                ToggleScene(SceneId.CandleSputter);
                break;
            case Key.A:
                bool autoplay = !(_actionScenes?.AutoplayEnabled ?? true);
                Post(new SetAutoplayCommand(autoplay));
                break;
            case Key.F:
                _projector?.ToggleFullscreen();
                RefreshOutputState();
                break;
            case Key.Escape:
                _projector?.LeaveFullscreen();
                RefreshOutputState();
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        try
        {
            _profiles?.Dispose();
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not save Pumpkin Face settings during shutdown: {exception.Message}");
        }

        // Do not tear down native inference underneath an in-flight worker when
        // the operator closes the app during synthesis.
        if (_speechSynthesisTask is null or { IsCompleted: true })
        {
            try
            {
                _kokoroSpeech?.Dispose();
            }
            catch (Exception exception)
            {
                GD.PushWarning($"Could not release the neural voice cleanly: {exception.Message}");
            }
        }
    }

    private void WireOperatorEvents()
    {
        if (_operator is null)
        {
            return;
        }

        _operator.EmotionRequested += emotion => Post(new PlayEmotionCommand(emotion));
        _operator.NextEmotionRequested += () => Post(new NextEmotionCommand());
        _operator.EmotionAmountChanged += amount => Post(new SetEmotionAmountCommand((float)amount));
        _operator.SceneSelectionChanged += (scene, enabled) =>
            Post(new SetSceneEnabledCommand(scene, enabled));
        _operator.SpeakPhraseRequested += phrase => Post(new SpeakPhraseCommand(phrase));
        _operator.SpeechVoiceChanged += voice => _speechVoice = voice;
        _operator.AutoplayChanged += enabled => Post(new SetAutoplayCommand(enabled));
        _operator.DisplaySelected += SelectDisplay;
        _operator.OutputToggleRequested += ToggleOutput;
        _operator.FullscreenToggleRequested += () =>
        {
            _projector?.ToggleFullscreen();
            RefreshOutputState();
        };
        _operator.GuidesChanged += enabled =>
        {
            if (_stage is not null)
            {
                _stage.ShowGuides = enabled;
            }
        };
        _operator.ProfileSelected += SelectProfile;
        _operator.ProfileCreateRequested += name => RunProfileOperation(() =>
            _profiles!.CreateProfile(name, _profiles.State.SelectedProfile.Calibration));
        _operator.ProfileRenameRequested += (id, name) => RunProfileOperation(() =>
            _profiles!.RenameProfile(id, name));
        _operator.ProfileDuplicateRequested += id => RunProfileOperation(() =>
            _profiles!.DuplicateProfile(id));
        _operator.ProfileDeleteRequested += id => RunProfileOperation(() =>
            _profiles!.DeleteProfile(id));
        _operator.ProfileResetRequested += id => RunProfileOperation(() =>
            _profiles!.ResetProfile(id));
        _operator.CalibrationChanged += ApplyCalibrationField;
        _operator.Preview.TransformEdited += ApplyPreviewEdit;
        _operator.Preview.OrbitDragged += delta => _stage?.AdjustCameraOrbit(delta);
    }

    private void WireProjectorEvents()
    {
        if (_projector is null)
        {
            return;
        }

        _projector.OutputSizeChanged += size =>
        {
            if (size.X > 0 && size.Y > 0)
            {
                _stage?.Resize(size);
            }
        };
        _projector.FullscreenChanged += _ => RefreshOutputState();
        _projector.OutputClosed += RefreshOutputState;
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out AnimationCommand? command))
        {
            if (command is ApplyCalibrationCommand calibration)
            {
                ApplyCalibration(calibration.Calibration);
                continue;
            }

            if (command is SetEmotionAmountCommand emotionAmount)
            {
                _stage!.EmotionAmount = emotionAmount.Amount;
                continue;
            }

            if (command is SetSceneEnabledCommand scene)
            {
                // Talking is an internal channel started only by typed speech;
                // it is no longer exposed as a selectable scene.
                if (scene.Scene == SceneId.Talking)
                {
                    continue;
                }
                _actionScenes!.SetSelected(scene.Scene, scene.Enabled);
                if (scene.Enabled)
                {
                    _profiles!.SetAutoplayEnabled(false);
                    _operator!.SetAutoplay(false);
                }
                _operator!.SetSelectedScenes(_actionScenes.SelectedScenes);
                continue;
            }

            if (command is SpeakPhraseCommand speech)
            {
                BeginSpeechSynthesis(speech.Phrase);
                continue;
            }

            if (command is SetAutoplayCommand autoplay)
            {
                if (autoplay.Enabled)
                {
                    _speechPlayer!.Stop();
                }
                _actionScenes!.SetAutoplay(autoplay.Enabled);
                _profiles!.SetAutoplayEnabled(autoplay.Enabled);
                _operator!.SetAutoplay(autoplay.Enabled);
                _operator.SetSelectedScenes(_actionScenes.SelectedScenes);
                continue;
            }

            if (command is StopCommand)
            {
                _actionScenes!.Stop();
                _profiles!.SetAutoplayEnabled(false);
                _operator!.SetAutoplay(false);
                _operator.SetSelectedScenes([]);
            }

            if (_director!.Handle(command))
            {
                continue;
            }
        }
    }

    private void Post(AnimationCommand command)
    {
        if (!_commands.TryPost(command))
        {
            _operator?.SetStatus("The control queue is busy; try that action again", warning: true);
        }
    }

    private void ToggleScene(SceneId scene)
    {
        bool enabled = !(_actionScenes?.IsSelected(scene) ?? false);
        Post(new SetSceneEnabledCommand(scene, enabled));
    }

    private void UpdateSpeechAudio(bool talkingActive)
    {
        if (_speechPlayer is null)
        {
            return;
        }

        if (talkingActive && !_speechWasActive)
        {
            _speechPlayer.Play();
        }
        else if (!talkingActive && _speechWasActive && _speechPlayer.Playing)
        {
            _speechPlayer.Stop();
        }
        _speechWasActive = talkingActive;
    }

    private void BeginSpeechSynthesis(string phrase)
    {
        if (_speechSynthesisTask is { IsCompleted: false })
        {
            _operator?.SetStatus("A phrase is already being prepared", warning: true);
            return;
        }

        string normalized = phrase.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _actionScenes!.SetAutoplay(false);
        _actionScenes.SetSelected(SceneId.Talking, false);
        _speechPlayer!.Stop();
        _profiles!.SetAutoplayEnabled(false);
        _operator!.SetAutoplay(false);
        _operator.SetSelectedScenes(_actionScenes.SelectedScenes);
        _operator.SetSpeechBusy(true);
        _operator.SetStatus(
            "Preparing local neural speech… The first phrase downloads and loads the model.");
        _speechSynthesisTask = _kokoroSpeech!.SynthesizeAsync(normalized, _speechVoice);
    }

    private void CompleteSpeechSynthesisIfReady()
    {
        if (_speechSynthesisTask is not { IsCompleted: true } completed)
        {
            return;
        }

        _speechSynthesisTask = null;
        _operator!.SetSpeechBusy(false);
        try
        {
            SpeechSynthesisResult result = completed.GetAwaiter().GetResult();
            AudioStreamWav stream = AudioStreamWav.LoadFromFile(result.WavePath);
            TimeSpan duration = TimeSpan.FromSeconds(stream.GetLength());
            IReadOnlyList<VisemeFrame> visemes = result.Visemes ??
                SpeechPhrasePlanner.Plan(result.Phrase, duration);
            _actionScenes!.ConfigureSpeech(visemes, duration);
            _speechPlayer!.Stream = stream;
            _actionScenes.SetSelected(SceneId.Talking, true);
            _operator.SetSelectedScenes(_actionScenes.SelectedScenes);
            _operator.ClearSpeechPhrase();
            _operator.SetStatus($"Speaking: “{result.Phrase}”");
        }
        catch (Exception exception)
        {
            _operator.SetStatus($"Could not prepare speech: {exception.Message}", warning: true);
        }
    }

    private void OnSpeechFinished()
    {
        _actionScenes?.BeginSpeechRelease();
        if (_operator is not null && _actionScenes is not null)
        {
            _operator.SetStatus("Phrase finished — returning to the resting face");
        }
    }

    private void SelectDisplay(int screen)
    {
        if (_projector is null || _profiles is null || _operator is null)
        {
            return;
        }

        bool useFullscreen = DisplayServer.GetScreenCount() > 1 &&
                             screen != DisplayServer.GetPrimaryScreen();
        _projector.SelectScreen(screen, useFullscreen);
        _stage?.Resize(_projector.OutputSize);

        DisplayChoice? choice = _projector.GetDisplays().FirstOrDefault(display => display.Index == screen);
        _profiles.RememberDisplay(screen, choice?.Label);
        _operator.SetStatus(
            useFullscreen
                ? $"Projecting on display {screen + 1}"
                : "Primary display selected — output remains windowed",
            warning: !useFullscreen);
        RefreshOutputState();
    }

    private void ToggleOutput()
    {
        if (_projector is null || _profiles is null)
        {
            return;
        }

        if (_projector.IsVisible)
        {
            _projector.HideOutput();
        }
        else
        {
            bool fullscreen = _projector.OpenSafely(
                _profiles.State.LastDisplayIndex ?? DisplayServer.GetPrimaryScreen(),
                preferFullscreen: true);
            _operator?.SetStatus(
                fullscreen ? "Projection output restored" : "Windowed output restored",
                warning: !fullscreen);
        }

        RefreshOutputState();
    }

    private void SelectProfile(Guid id)
    {
        RunProfileOperation(() => _profiles!.SelectProfile(id));
    }

    private void RunProfileOperation(Action operation)
    {
        try
        {
            operation();
            RefreshProfilesAndCalibration();
            _operator?.SetStatus("Calibration profile updated");
        }
        catch (Exception exception)
        {
            _operator?.SetStatus(exception.Message, warning: true);
        }
    }

    private void ApplyCalibrationField(CalibrationField field, double rawValue)
    {
        if (_profiles is null)
        {
            return;
        }

        float value = (float)rawValue;
        ProjectionCalibration current = _profiles.State.SelectedProfile.Calibration;
        ProjectionCalibration updated = field switch
        {
            CalibrationField.OffsetX => current with { OffsetX = value },
            CalibrationField.OffsetY => current with { OffsetY = value },
            CalibrationField.ScaleX => current with { ScaleX = value },
            CalibrationField.ScaleY => current with { ScaleY = value },
            CalibrationField.Rotation => current with { RotationDegrees = value },
            CalibrationField.EyeSpacing => current with { EyeSpacing = value },
            CalibrationField.MouthOffsetX => current with { MouthOffsetX = value },
            CalibrationField.MouthOffsetY => current with { MouthOffsetY = value },
            CalibrationField.MouthScale => current with { MouthScaleX = value, MouthScaleY = value },
            CalibrationField.Brightness => current with { Brightness = value },
            CalibrationField.Gamma => current with { Gamma = value },
            CalibrationField.CandleBrightness => current with { CandleBrightness = value },
            CalibrationField.ShellThickness => current with { ShellThickness = value },
            _ => current,
        };
        Post(new ApplyCalibrationCommand(updated));
    }

    private void ApplyPreviewEdit(PreviewEdit edit)
    {
        if (_profiles is null)
        {
            return;
        }

        ProjectionCalibration current = _profiles.State.SelectedProfile.Calibration;
        ProjectionCalibration updated = edit.Kind switch
        {
            PreviewEditKind.Move => current with
            {
                OffsetX = current.OffsetX + edit.Delta.X,
                OffsetY = current.OffsetY + edit.Delta.Y,
            },
            PreviewEditKind.Scale => current with
            {
                ScaleX = current.ScaleX * edit.Amount,
                ScaleY = current.ScaleY * edit.Amount,
            },
            PreviewEditKind.Rotate => current with
            {
                RotationDegrees = current.RotationDegrees + edit.Amount,
            },
            _ => current,
        };
        Post(new ApplyCalibrationCommand(updated));
    }

    private void ApplyCalibration(ProjectionCalibration calibration)
    {
        ProjectionCalibration normalized = calibration.Normalize();
        _profiles!.UpdateSelectedCalibration(normalized);
        _stage!.SetCalibration(normalized);
        UpdateCalibrationUi(normalized);
    }

    private void RefreshProfilesAndCalibration()
    {
        if (_profiles is null || _operator is null || _stage is null)
        {
            return;
        }

        ApplicationStateDocument state = _profiles.State;
        _operator.SetProfiles(
            state.Profiles.Select(profile => new ProfileChoice(profile.Id, profile.Name)),
            state.SelectedProfileId);
        ProjectionCalibration calibration = state.SelectedProfile.Calibration.Normalize();
        _stage.SetCalibration(calibration);
        UpdateCalibrationUi(calibration);
    }

    private void UpdateCalibrationUi(ProjectionCalibration calibration)
    {
        _operator!.SetCalibration(new CalibrationUiValues(
            calibration.OffsetX,
            calibration.OffsetY,
            calibration.ScaleX,
            calibration.ScaleY,
            calibration.RotationDegrees,
            calibration.EyeSpacing,
            calibration.MouthOffsetX,
            calibration.MouthOffsetY,
            (calibration.MouthScaleX + calibration.MouthScaleY) * 0.5,
            calibration.Brightness,
            calibration.Gamma,
            calibration.CandleBrightness,
            calibration.ShellThickness));
        _operator.Preview.SetTransformState(new PreviewTransformState(
            new Vector2(calibration.OffsetX, calibration.OffsetY),
            new Vector2(calibration.ScaleX, calibration.ScaleY),
            calibration.RotationDegrees));
    }

    private void RefreshOutputState()
    {
        _operator?.SetOutputState(
            _projector?.IsVisible ?? false,
            _projector?.IsFullscreen ?? false);
    }

    private static int ResolveStartupScreen(int? savedScreen, IReadOnlyList<DisplayChoice> displays)
    {
        if (savedScreen is { } saved && displays.Any(display => display.Index == saved))
        {
            return saved;
        }

        return DisplayServer.GetPrimaryScreen();
    }

    private void StartCaptureMode(string captureDirectory)
    {
        _stage = new FaceStage { Name = "CaptureStage" };
        _animations = new SceneAnimationController { Name = "CaptureAnimations" };
        AddChild(_stage);
        AddChild(_animations);

        TryGetCommandLineValue("--compare-dir=", out string? comparisonDirectory);
        DeterministicCaptureRunner runner = new() { Name = "CaptureRunner" };
        AddChild(runner);
        runner.Configure(_stage, _animations, captureDirectory, comparisonDirectory);
    }

    private static bool TryGetCommandLineValue(string prefix, out string? value)
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = argument[prefix.Length..];
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = null;
        return false;
    }

    private void ReportPersistenceFailure(string message)
    {
        _operator?.SetStatus($"Settings could not be saved: {message}", warning: true);
    }
}

internal static class OperatorPanelExtensions
{
    public static void SetPreviewTexture(this OperatorPanel panel, Texture2D texture) =>
        panel.Preview.SetPreviewTexture(texture);
}
