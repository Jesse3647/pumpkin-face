using Godot;
using PumpkinFace.Core;
using PumpkinFace.Display.Animation;
using PumpkinFace.Display.App;

namespace PumpkinFace.Display.UI;

public enum CalibrationField
{
    OffsetX,
    OffsetY,
    ScaleX,
    ScaleY,
    Rotation,
    EyeSpacing,
    MouthOffsetX,
    MouthOffsetY,
    MouthScale,
    Brightness,
    Gamma,
    CandleBrightness,
    ShellThickness,
}

public readonly record struct CalibrationUiValues(
    double OffsetX,
    double OffsetY,
    double ScaleX,
    double ScaleY,
    double Rotation,
    double EyeSpacing,
    double MouthOffsetX,
    double MouthOffsetY,
    double MouthScale,
    double Brightness,
    double Gamma,
    double CandleBrightness,
    double ShellThickness);

public readonly record struct ProfileChoice(Guid Id, string Name);

/// <summary>
/// Code-built operator surface. Keeping the UI in one class makes the native
/// projector window completely independent from operator controls.
/// </summary>
public sealed partial class OperatorPanel : Control
{
    private readonly Dictionary<CalibrationField, (HSlider Slider, SpinBox Spin)> _calibrationControls = [];
    private readonly List<ProfileChoice> _profiles = [];
    private readonly List<DisplayChoice> _displays = [];
    private readonly Dictionary<SceneId, CheckButton> _sceneToggles = [];

    private OptionButton? _profilePicker;
    private OptionButton? _displayPicker;
    private LineEdit? _profileName;
    private Button? _deleteProfileButton;
    private Button? _outputButton;
    private Button? _fullscreenButton;
    private CheckButton? _autoplayToggle;
    private LineEdit? _speechPhrase;
    private Button? _speakButton;
    private OptionButton? _speechVoicePicker;
    private readonly List<SpeechVoiceChoice> _speechVoices = [];
    private HSlider? _emotionAmountSlider;
    private Label? _emotionAmountValue;
    private CheckButton? _guidesToggle;
    private Label? _statusLabel;
    private Label? _fpsLabel;
    private ConfirmationDialog? _deleteConfirmation;
    private Guid _selectedProfileId;
    private bool _updating;

    private OptionButton? _behaviorPicker;
    private OptionButton? _characterPicker;
    private Label? _characterDescription;
    private HSlider? _motionAmountSlider;
    private float _gestureIntensity = .65f;
    public event Action<AnimationCommand>? PerformanceCommandRequested;
    public event Action<bool>? PerformanceSoundChanged;
    public event Action<float>? SongTempoChanged;
    private Label? _nowPlaying;
    private ProgressBar? _performanceProgress;
    private Button? _songButton;
    private FileDialog? _songPicker;
    private bool _hasSong;
    private readonly Dictionary<string, Button> _performanceButtons = [];

    public event Action<EmotionId>? EmotionRequested;
    public event Action? NextEmotionRequested;
    public event Action<double>? EmotionAmountChanged;
    public event Action<SceneId, bool>? SceneSelectionChanged;
    public event Action<string>? SpeakPhraseRequested;
    public event Action<string>? SpeechVoiceChanged;
    public event Action<bool>? AutoplayChanged;
    public event Action<int>? DisplaySelected;
    public event Action? OutputToggleRequested;
    public event Action? FullscreenToggleRequested;
    public event Action<bool>? GuidesChanged;
    public event Action<Guid>? ProfileSelected;
    public event Action<string>? ProfileCreateRequested;
    public event Action<Guid, string>? ProfileRenameRequested;
    public event Action<Guid>? ProfileDuplicateRequested;
    public event Action<Guid>? ProfileDeleteRequested;
    public event Action<Guid>? ProfileResetRequested;
    public event Action<CalibrationField, double>? CalibrationChanged;

    public ProjectionPreview Preview { get; private set; } = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUi();
    }

    public void SetProfiles(IEnumerable<ProfileChoice> profiles, Guid selectedId)
    {
        _profiles.Clear();
        _profiles.AddRange(profiles);
        _selectedProfileId = selectedId;

        if (_profilePicker is null)
        {
            return;
        }

        _updating = true;
        _profilePicker.Clear();
        int selectedIndex = 0;
        for (int index = 0; index < _profiles.Count; index++)
        {
            _profilePicker.AddItem(_profiles[index].Name);
            if (_profiles[index].Id == selectedId)
            {
                selectedIndex = index;
            }
        }

        if (_profiles.Count > 0)
        {
            _profilePicker.Select(selectedIndex);
            _profileName!.Text = _profiles[selectedIndex].Name;
        }

        _deleteProfileButton!.Disabled = _profiles.Count <= 1;
        _updating = false;
    }

    public void SetDisplays(IEnumerable<DisplayChoice> displays, int selectedScreen)
    {
        _displays.Clear();
        _displays.AddRange(displays);

        if (_displayPicker is null)
        {
            return;
        }

        _updating = true;
        _displayPicker.Clear();
        int selectedIndex = 0;
        for (int index = 0; index < _displays.Count; index++)
        {
            _displayPicker.AddItem(_displays[index].Label);
            if (_displays[index].Index == selectedScreen)
            {
                selectedIndex = index;
            }
        }

        if (_displays.Count > 0)
        {
            _displayPicker.Select(selectedIndex);
        }

        _updating = false;
    }

    public void SetCalibration(CalibrationUiValues values)
    {
        _updating = true;
        SetField(CalibrationField.OffsetX, values.OffsetX);
        SetField(CalibrationField.OffsetY, values.OffsetY);
        SetField(CalibrationField.ScaleX, values.ScaleX);
        SetField(CalibrationField.ScaleY, values.ScaleY);
        SetField(CalibrationField.Rotation, values.Rotation);
        SetField(CalibrationField.EyeSpacing, values.EyeSpacing);
        SetField(CalibrationField.MouthOffsetX, values.MouthOffsetX);
        SetField(CalibrationField.MouthOffsetY, values.MouthOffsetY);
        SetField(CalibrationField.MouthScale, values.MouthScale);
        SetField(CalibrationField.Brightness, values.Brightness);
        SetField(CalibrationField.Gamma, values.Gamma);
        SetField(CalibrationField.CandleBrightness, values.CandleBrightness);
        SetField(CalibrationField.ShellThickness, values.ShellThickness);
        _updating = false;
    }

    public void SetAutoplay(bool enabled)
    {
        _autoplayToggle?.SetPressedNoSignal(enabled);
    }

    public void SetSelectedScenes(IEnumerable<SceneId> scenes)
    {
        HashSet<SceneId> selected = [.. scenes];
        foreach ((SceneId scene, CheckButton toggle) in _sceneToggles)
        {
            toggle.SetPressedNoSignal(selected.Contains(scene));
        }
    }

    public void SetSpeechBusy(bool busy)
    {
        if (_speechPhrase is not null)
        {
            _speechPhrase.Editable = !busy;
        }
        if (_speakButton is not null)
        {
            _speakButton.Disabled = busy;
            _speakButton.Text = busy ? "Preparing voice…" : "Speak phrase";
        }
    }

    public void ClearSpeechPhrase()
    {
        if (_speechPhrase is not null)
        {
            _speechPhrase.Clear();
        }
    }

    public void SetSpeechVoices(IEnumerable<SpeechVoiceChoice> voices, string selectedVoice)
    {
        _speechVoices.Clear();
        _speechVoices.AddRange(voices);
        if (_speechVoicePicker is null)
        {
            return;
        }

        _updating = true;
        _speechVoicePicker.Clear();
        int selectedIndex = 0;
        for (int index = 0; index < _speechVoices.Count; index++)
        {
            _speechVoicePicker.AddItem(_speechVoices[index].Label);
            if (_speechVoices[index].Id == selectedVoice)
            {
                selectedIndex = index;
            }
        }
        _speechVoicePicker.Select(selectedIndex);
        _updating = false;
    }

    public void SetGuides(bool enabled)
    {
        _guidesToggle?.SetPressedNoSignal(enabled);
        Preview.HandlesVisible = enabled;
        Preview.QueueRedraw();
    }

    public void SetOutputState(bool visible, bool fullscreen)
    {
        if (_outputButton is not null)
        {
            _outputButton.Text = visible ? "Hide output" : "Show output";
        }

        if (_fullscreenButton is not null)
        {
            _fullscreenButton.Text = fullscreen ? "Leave fullscreen" : "Fullscreen";
            _fullscreenButton.Disabled = !visible;
        }
    }

    public void SetStatus(string message, bool warning = false)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.Modulate = warning ? new Color("ffb066") : new Color("a8c7b3");
    }

    public void SetFps(double fps, string? sceneName)
    {
        if (_fpsLabel is not null)
        {
            _fpsLabel.Text = $"{fps:0} FPS  •  {sceneName ?? "Holding expression"}";
        }
    }

    private void BuildUi()
    {
        ColorRect background = new() { Color = new Color("101013") };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        VBoxContainer page = new();
        page.AddThemeConstantOverride("separation", 14);
        margin.AddChild(page);

        page.AddChild(BuildHeader());

        HSplitContainer body = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SplitOffsets = [790],
        };
        page.AddChild(body);

        VBoxContainer previewColumn = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        previewColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(previewColumn);

        PanelContainer previewFrame = CreateCard();
        previewFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        Preview = new ProjectionPreview
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        previewFrame.AddChild(Preview);
        previewColumn.AddChild(previewFrame);

        Label previewHint = new()
        {
            Text = "Drag to orbit the 3D pumpkin • right-drag or hide guides while calibrating",
            Modulate = new Color("8d8d98"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        previewColumn.AddChild(previewHint);

        ScrollContainer inspectorScroll = new()
        {
            CustomMinimumSize = new Vector2(390, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(inspectorScroll);

        VBoxContainer inspector = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        inspector.AddThemeConstantOverride("separation", 12);
        inspectorScroll.AddChild(inspector);
        inspector.AddChild(BuildCharacterCard());
        inspector.AddChild(BuildSwitchboardCard());
        inspector.AddChild(BuildOutputCard());
        inspector.AddChild(BuildEmotionsCard());
        inspector.AddChild(BuildPerformanceCard());
        inspector.AddChild(BuildActionScenesCard());
        inspector.AddChild(BuildPumpkinLightingCard());
        inspector.AddChild(BuildProfilesCard());
        inspector.AddChild(BuildCalibrationCard());

        _deleteConfirmation = new ConfirmationDialog
        {
            Title = "Delete calibration profile?",
            DialogText = "This profile will be removed. The remaining profiles are not affected.",
            OkButtonText = "Delete",
        };
        _deleteConfirmation.Confirmed += () => ProfileDeleteRequested?.Invoke(_selectedProfileId);
        AddChild(_deleteConfirmation);
    }

    private Control BuildHeader()
    {
        HBoxContainer header = new();
        Label title = new() { Text = "PUMPKIN FACE" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color("ff9f32"));
        header.AddChild(title);

        Label spacer = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);

        _statusLabel = new()
        {
            Text = "Starting…",
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_statusLabel);

        _fpsLabel = new()
        {
            Text = "— FPS",
            CustomMinimumSize = new Vector2(150, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color("8d8d98"),
        };
        header.AddChild(_fpsLabel);
        return header;
    }

    private Control BuildOutputCard()
    {
        VBoxContainer content = CreateCardContent("Projection output");
        _displayPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _displayPicker.ItemSelected += index =>
        {
            if (!_updating && index >= 0 && index < _displays.Count)
            {
                DisplaySelected?.Invoke(_displays[(int)index].Index);
            }
        };
        content.AddChild(_displayPicker);

        HBoxContainer buttons = new();
        _outputButton = CreateButton("Show output", () => OutputToggleRequested?.Invoke());
        _outputButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(_outputButton);
        _fullscreenButton = CreateButton("Fullscreen", () => FullscreenToggleRequested?.Invoke());
        _fullscreenButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(_fullscreenButton);
        content.AddChild(buttons);

        _guidesToggle = new CheckButton { Text = "Show alignment guides", ButtonPressed = false };
        _guidesToggle.Toggled += enabled =>
        {
            Preview.HandlesVisible = enabled;
            Preview.QueueRedraw();
            GuidesChanged?.Invoke(enabled);
        };
        content.AddChild(_guidesToggle);
        return WrapCard(content);
    }

    private Control BuildEmotionsCard()
    {
        VBoxContainer content = CreateCardContent("Emotions");
        GridContainer grid = new() { Columns = 1 };
        AddEmotionButton(grid, "1  Frightened", EmotionId.Frightened);
        AddEmotionButton(grid, "2  Happy", EmotionId.Happy);
        AddEmotionButton(grid, "3  Sad", EmotionId.Sad);
        content.AddChild(grid);

        VBoxContainer amountRow = new();
        amountRow.AddThemeConstantOverride("separation", 2);
        HBoxContainer amountHeader = new();
        amountHeader.AddChild(new Label
        {
            Text = "Emotion amount",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Modulate = new Color("c8c8cf"),
        });
        _emotionAmountValue = new Label { Text = "100%", Modulate = new Color("a8c7b3") };
        amountHeader.AddChild(_emotionAmountValue);
        amountRow.AddChild(amountHeader);
        _emotionAmountSlider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.01,
            Value = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _emotionAmountSlider.ValueChanged += value =>
        {
            _emotionAmountValue.Text = $"{Math.Round(value * 100):0}%";
            if (!_updating)
            {
                EmotionAmountChanged?.Invoke(value);
            }
        };
        amountRow.AddChild(_emotionAmountSlider);
        content.AddChild(amountRow);

        Button next = CreateButton("Next emotion  [Space]", () => NextEmotionRequested?.Invoke());
        content.AddChild(next);
        return WrapCard(content);
    }

    public void SetEmotionAmount(float amount)
    {
        _emotionAmountSlider?.SetValueNoSignal(amount);
        if (_emotionAmountValue is not null) _emotionAmountValue.Text = $"{amount:P0}";
    }

    public void SetCharacter(string characterId)
    {
        CharacterDefinition character = CharacterCatalog.Get(characterId);
        for (int i = 0; i < CharacterCatalog.All.Count; i++)
            if (CharacterCatalog.All[i].Id == character.Id) _characterPicker?.Select(i);
        if (_characterDescription is not null) _characterDescription.Text = character.Description;
    }

    private Control BuildCharacterCard()
    {
        VBoxContainer content = CreateCardContent("Character");
        _characterPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (CharacterDefinition character in CharacterCatalog.All)
            _characterPicker.AddItem($"{character.Name} — {character.Tagline}");
        _characterPicker.ItemSelected += index => PerformanceCommandRequested?.Invoke(
            new SelectCharacterCommand(CharacterCatalog.All[(int)index].Id));
        content.AddChild(_characterPicker);
        _characterDescription = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(_characterDescription);
        content.AddChild(CreateButton("Meet this character", () => PerformanceCommandRequested?.Invoke(new PlayPerformanceDemoCommand())));
        SetCharacter(CharacterCatalog.DefaultId);
        return WrapCard(content);
    }

    public void SetSongTitle(string title)
    {
        _hasSong = true;
        if (_songButton is not null)
        {
            _songButton.Text = "Play your song";
            _songButton.TooltipText = title;
        }
    }

    public void SetSwitchboardState(string? id, string title, double position, double duration)
    {
        if (_nowPlaying is not null) _nowPlaying.Text = id is null ? "Ready — choose a performance" :
            $"{title}   {position:0.0} / {duration:0.#} s";
        if (_performanceProgress is not null) _performanceProgress.Value = id is null || duration <= 0 ? 0 : position / duration * 100;
        foreach (var (key, button) in _performanceButtons)
            button.SetPressedNoSignal(key == id);
        _songButton?.SetPressedNoSignal(id == PerformanceLibrary.SongId);
    }

    private Control BuildSwitchboardCard()
    {
        VBoxContainer content = CreateCardContent("Performance switchboard");
        content.AddChild(new Label { Text = "Choose a moment. Each plays once, then rests.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        GridContainer board = new() { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        board.AddThemeConstantOverride("h_separation", 8);
        board.AddThemeConstantOverride("v_separation", 8);
        foreach (var clip in PerformanceLibrary.All)
        {
            Button button = CreateButton($"{clip.Title}\n{clip.Duration:0.#} s · {(clip.AudioFile is null ? "silent" : clip.AudioLabel)}",
                () => PerformanceCommandRequested?.Invoke(new PlayCannedPerformanceCommand(clip.Id)));
            button.CustomMinimumSize = new Vector2(0, 62);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.TooltipText = clip.Description;
            StylePerformanceButton(button);
            _performanceButtons.Add(clip.Id, button);
            board.AddChild(button);
        }
        _songButton = CreateButton("Your song…\nChoose audio", () => {
            if (_hasSong) PerformanceCommandRequested?.Invoke(new PlayCannedPerformanceCommand(PerformanceLibrary.SongId));
            else _songPicker!.PopupCentered(new Vector2I(800, 540));
        });
        _songButton.CustomMinimumSize = new Vector2(0, 62);
        _songButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        StylePerformanceButton(_songButton);
        board.AddChild(_songButton);
        content.AddChild(board);
        _nowPlaying = new Label { Text = "Ready — choose a performance", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(_nowPlaying);
        _performanceProgress = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5) };
        _performanceProgress.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color("303039") });
        _performanceProgress.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color("ff9f32") });
        content.AddChild(_performanceProgress);
        HBoxContainer actions = new();
        CheckButton sound = new() { Text = "Sound", ButtonPressed = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sound.Toggled += enabled => PerformanceSoundChanged?.Invoke(enabled);
        actions.AddChild(sound);
        actions.AddChild(CreateButton("Stop", () => PerformanceCommandRequested?.Invoke(new StopCommand())));
        content.AddChild(actions);
        HBoxContainer songSettings = new();
        songSettings.AddChild(CreateButton("Choose song…", () => _songPicker!.PopupCentered(new Vector2I(800, 540))));
        songSettings.AddChild(new Label { Text = "Song tempo", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        SpinBox tempo = new() { MinValue = 40, MaxValue = 200, Step = 1, Value = 120, Suffix = "bpm",
            CustomMinimumSize = new Vector2(120, 32), TooltipText = "For your chosen song. Match the beat by ear." };
        tempo.ValueChanged += value => SongTempoChanged?.Invoke((float)value);
        songSettings.AddChild(tempo);
        content.AddChild(songSettings);
        content.AddChild(new Label { Text = "Try your copy of Ghostbusters. Songs use a dance animation.\nOrgan: InspectorJ · CC BY 4.0",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("8d8d98") });
        _songPicker = new FileDialog {
            Title = "Choose a song to perform", FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Filters = ["*.mp3,*.wav,*.ogg;Audio files;audio/mpeg,audio/wav,audio/ogg"],
        };
        _songPicker.FileSelected += path => PerformanceCommandRequested?.Invoke(new PlaySongFileCommand(path));
        AddChild(_songPicker);
        return WrapCard(content);
    }

    private static void StylePerformanceButton(Button button)
    {
        button.ToggleMode = true;
        foreach (var (state, background, border) in new[] {
            ("normal", "24242c", "3b3b45"), ("hover", "333039", "ba7635"),
            ("pressed", "50331e", "ff9f32"), ("hover_pressed", "604027", "ffb45c"),
            ("focus", "00000000", "ffb45c"),
        })
            button.AddThemeStyleboxOverride(state, new StyleBoxFlat {
                BgColor = new Color(background), BorderColor = new Color(border),
                BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7,
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
            });
    }

    public void SetPerformanceState(BehaviorState? state, float motion)
    {
        _behaviorPicker?.Select(state is { } value ? (int)value + 1 : 0);
        _motionAmountSlider?.SetValueNoSignal(motion);
    }

    private Control BuildPerformanceCard()
    {
        VBoxContainer content = CreateCardContent("Character performance");
        _behaviorPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string label in new[] { "Rest (no autonomous behavior)", "Idle — curious and playful", "Listening — attentive", "Thinking — considering" })
            _behaviorPicker.AddItem(label);
        _behaviorPicker.ItemSelected += index => PerformanceCommandRequested?.Invoke(
            new SetBehaviorStateCommand(index == 0 ? null : (BehaviorState)(index - 1)));
        content.AddChild(_behaviorPicker);
        content.AddChild(new Label { Text = "Gesture intensity" });
        HSlider intensity = new() { MinValue = 0, MaxValue = 1, Step = .05, Value = .65 };
        intensity.ValueChanged += value => _gestureIntensity = (float)value;
        content.AddChild(intensity);
        GridContainer gestures = new() { Columns = 3 };
        foreach (GestureDefinition definition in GestureCatalog.All)
            gestures.AddChild(CreateButton(definition.Label, () => PerformanceCommandRequested?.Invoke(
                new PlayGestureCommand(Guid.NewGuid(), definition.Id, _gestureIntensity))));
        content.AddChild(gestures);
        content.AddChild(new Label { Text = "Look toward (from the audience’s view)" });
        GridContainer targets = new() { Columns = 3 };
        SpinBox hold = new() { MinValue = .1, MaxValue = 30, Step = .1, Value = 2, Suffix = "s hold" };
        string[] labels = ["Upper left", "Up", "Upper right", "Left", "Center", "Right", "Lower left", "Down", "Lower right"];
        for (int i = 0; i < 9; i++)
        {
            float x = (i % 3 - 1) * .7f, y = (i / 3 - 1) * .6f;
            targets.AddChild(CreateButton(labels[i], () => PerformanceCommandRequested?.Invoke(
                new SetGazeTargetCommand(Guid.NewGuid(), x, y, hold.Value))));
        }
        content.AddChild(targets);
        content.AddChild(hold);
        content.AddChild(new Label { Text = "Whole-face motion (off → full expression)" });
        _motionAmountSlider = new HSlider { MinValue = 0, MaxValue = 1, Step = .05, Value = .65 };
        _motionAmountSlider.ValueChanged += value => PerformanceCommandRequested?.Invoke(new SetMotionAmountCommand((float)value));
        content.AddChild(_motionAmountSlider);
        HBoxContainer actions = new();
        actions.AddChild(CreateButton("Play demonstration", () => PerformanceCommandRequested?.Invoke(new PlayPerformanceDemoCommand())));
        actions.AddChild(CreateButton("Stop performance", () => PerformanceCommandRequested?.Invoke(new StopCommand())));
        content.AddChild(actions);
        return WrapCard(content);
    }

    private Control BuildActionScenesCard()
    {
        VBoxContainer content = CreateCardContent("Scenes");
        _autoplayToggle = new CheckButton { Text = "Autoplay curious idle", ButtonPressed = true };
        _autoplayToggle.Toggled += enabled =>
        {
            if (!_updating)
            {
                AutoplayChanged?.Invoke(enabled);
            }
        };
        content.AddChild(_autoplayToggle);
        content.AddChild(new Label
        {
            Text = "Select any combination. Selected scenes repeat until stopped.",
            Modulate = new Color("8d8d98"),
        });
        GridContainer scenes = new() { Columns = 1 };
        AddActionSceneButton(scenes, "Looking  [L]", SceneId.Looking);
        AddActionSceneButton(scenes, "Blinking  [B]", SceneId.Blinking);
        AddActionSceneButton(scenes, "Candle sputter  [C]", SceneId.CandleSputter);
        content.AddChild(scenes);

        content.AddChild(new HSeparator());
        content.AddChild(new Label { Text = "Custom phrase", Modulate = new Color("c8c8cf") });
        _speechVoicePicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _speechVoicePicker.ItemSelected += index =>
        {
            if (!_updating && index >= 0 && index < _speechVoices.Count)
            {
                SpeechVoiceChanged?.Invoke(_speechVoices[(int)index].Id);
            }
        };
        content.AddChild(_speechVoicePicker);
        content.AddChild(new Label
        {
            Text = "Neural voices run locally. First use downloads the model once.",
            Modulate = new Color("8d8d98"),
        });
        _speechPhrase = new LineEdit
        {
            PlaceholderText = "Type what the pumpkin should say",
            MaxLength = KokoroSpeechSynthesizer.MaximumPhraseLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _speechPhrase.TextSubmitted += _ => RequestTypedSpeech();
        content.AddChild(_speechPhrase);
        _speakButton = CreateButton("Speak phrase", RequestTypedSpeech);
        _speakButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(_speakButton);
        return WrapCard(content);
    }

    private void RequestTypedSpeech()
    {
        string phrase = _speechPhrase?.Text.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(phrase))
        {
            SpeakPhraseRequested?.Invoke(phrase);
        }
    }

    private Control BuildPumpkinLightingCard()
    {
        VBoxContainer content = CreateCardContent("Pumpkin lighting");
        AddCalibrationField(
            content,
            CalibrationField.CandleBrightness,
            "Candle brightness",
            ProjectionCalibration.MinimumCandleBrightness,
            ProjectionCalibration.MaximumCandleBrightness,
            0.05);
        AddCalibrationField(
            content,
            CalibrationField.ShellThickness,
            "Shell thickness",
            ProjectionCalibration.MinimumShellThickness,
            ProjectionCalibration.MaximumShellThickness,
            0.05);
        return WrapCard(content);
    }

    private void AddActionSceneButton(Container parent, string text, SceneId scene)
    {
        CheckButton toggle = new()
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        toggle.Toggled += enabled =>
        {
            if (!_updating)
            {
                SceneSelectionChanged?.Invoke(scene, enabled);
            }
        };
        _sceneToggles[scene] = toggle;
        parent.AddChild(toggle);
    }

    private Control BuildProfilesCard()
    {
        VBoxContainer content = CreateCardContent("Calibration profiles");
        _profilePicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _profilePicker.ItemSelected += index =>
        {
            if (_updating || index < 0 || index >= _profiles.Count)
            {
                return;
            }

            ProfileChoice profile = _profiles[(int)index];
            _selectedProfileId = profile.Id;
            _profileName!.Text = profile.Name;
            ProfileSelected?.Invoke(profile.Id);
        };
        content.AddChild(_profilePicker);

        _profileName = new LineEdit
        {
            PlaceholderText = "Profile name",
            MaxLength = 64,
        };
        content.AddChild(_profileName);

        GridContainer buttons = new() { Columns = 2 };
        buttons.AddChild(CreateButton("New", () => ProfileCreateRequested?.Invoke(_profileName!.Text)));
        buttons.AddChild(CreateButton("Rename", () => ProfileRenameRequested?.Invoke(_selectedProfileId, _profileName!.Text)));
        buttons.AddChild(CreateButton("Duplicate", () => ProfileDuplicateRequested?.Invoke(_selectedProfileId)));
        _deleteProfileButton = CreateButton("Delete", () => _deleteConfirmation?.PopupCentered());
        buttons.AddChild(_deleteProfileButton);
        content.AddChild(buttons);
        content.AddChild(CreateButton("Reset selected profile", () => ProfileResetRequested?.Invoke(_selectedProfileId)));
        return WrapCard(content);
    }

    private Control BuildCalibrationCard()
    {
        VBoxContainer content = CreateCardContent("Fine calibration");
        AddCalibrationField(content, CalibrationField.OffsetX, "Horizontal position", -0.5, 0.5, 0.001);
        AddCalibrationField(content, CalibrationField.OffsetY, "Vertical position", -0.5, 0.5, 0.001);
        AddCalibrationField(content, CalibrationField.ScaleX, "Horizontal scale", 0.25, 2.5, 0.01);
        AddCalibrationField(content, CalibrationField.ScaleY, "Vertical scale", 0.25, 2.5, 0.01);
        AddCalibrationField(content, CalibrationField.Rotation, "Rotation", -45, 45, 0.1, "°");
        AddCalibrationField(content, CalibrationField.EyeSpacing, "Eye spacing", 0.65, 1.5, 0.01);
        AddCalibrationField(content, CalibrationField.MouthOffsetX, "Mouth horizontal", -0.3, 0.3, 0.001);
        AddCalibrationField(content, CalibrationField.MouthOffsetY, "Mouth vertical", -0.3, 0.3, 0.001);
        AddCalibrationField(content, CalibrationField.MouthScale, "Mouth scale", 0.5, 1.8, 0.01);
        AddCalibrationField(content, CalibrationField.Brightness, "Brightness", 0.1, 2.0, 0.01);
        AddCalibrationField(content, CalibrationField.Gamma, "Gamma", 0.5, 2.0, 0.01);
        return WrapCard(content);
    }

    private void AddEmotionButton(Container parent, string text, EmotionId emotion)
    {
        Button button = CreateButton(text, () => EmotionRequested?.Invoke(emotion));
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(button);
    }

    private void AddCalibrationField(
        Container parent,
        CalibrationField field,
        string label,
        double minimum,
        double maximum,
        double step,
        string suffix = "")
    {
        VBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 2);
        row.AddChild(new Label { Text = label, Modulate = new Color("c8c8cf") });

        HBoxContainer controls = new();
        HSlider slider = new()
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        SpinBox spin = new()
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            Suffix = suffix,
            CustomMinimumSize = new Vector2(92, 0),
        };

        slider.ValueChanged += value =>
        {
            spin.SetValueNoSignal(value);
            if (!_updating)
            {
                CalibrationChanged?.Invoke(field, value);
            }
        };
        spin.ValueChanged += value =>
        {
            slider.SetValueNoSignal(value);
            if (!_updating)
            {
                CalibrationChanged?.Invoke(field, value);
            }
        };

        controls.AddChild(slider);
        controls.AddChild(spin);
        row.AddChild(controls);
        parent.AddChild(row);
        _calibrationControls[field] = (slider, spin);
    }

    private void SetField(CalibrationField field, double value)
    {
        if (_calibrationControls.TryGetValue(field, out var controls))
        {
            controls.Slider.SetValueNoSignal(value);
            controls.Spin.SetValueNoSignal(value);
        }
    }

    private static PanelContainer CreateCard()
    {
        StyleBoxFlat style = new()
        {
            BgColor = new Color("18181e"),
            BorderColor = new Color("303039"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12,
            ContentMarginTop = 12,
            ContentMarginRight = 12,
            ContentMarginBottom = 12,
        };
        PanelContainer card = new();
        card.AddThemeStyleboxOverride("panel", style);
        return card;
    }

    private static VBoxContainer CreateCardContent(string title)
    {
        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 8);
        Label heading = new() { Text = title.ToUpperInvariant() };
        heading.AddThemeFontSizeOverride("font_size", 13);
        heading.AddThemeColorOverride("font_color", new Color("ff9f32"));
        content.AddChild(heading);
        return content;
    }

    private static PanelContainer WrapCard(Control content)
    {
        PanelContainer card = CreateCard();
        card.AddChild(content);
        return card;
    }

    private static Button CreateButton(string text, Action pressed)
    {
        Button button = new() { Text = text };
        button.Pressed += pressed;
        return button;
    }
}
