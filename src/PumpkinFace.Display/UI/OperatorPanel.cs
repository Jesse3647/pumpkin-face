using Godot;
using PumpkinFace.Core;
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
    private HSlider? _emotionAmountSlider;
    private Label? _emotionAmountValue;
    private CheckButton? _guidesToggle;
    private Label? _statusLabel;
    private Label? _fpsLabel;
    private ConfirmationDialog? _deleteConfirmation;
    private Guid _selectedProfileId;
    private bool _updating;

    public event Action<EmotionId>? EmotionRequested;
    public event Action? NextEmotionRequested;
    public event Action<double>? EmotionAmountChanged;
    public event Action<SceneId, bool>? SceneSelectionChanged;
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
        inspector.AddChild(BuildOutputCard());
        inspector.AddChild(BuildEmotionsCard());
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

    private Control BuildActionScenesCard()
    {
        VBoxContainer content = CreateCardContent("Scenes");
        _autoplayToggle = new CheckButton { Text = "Autoplay scenes", ButtonPressed = true };
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
            Text = "Select any combination. Actions loop over the current emotion.",
            Modulate = new Color("8d8d98"),
        });
        GridContainer scenes = new() { Columns = 1 };
        AddActionSceneButton(scenes, "Looking  [L]", SceneId.Looking);
        AddActionSceneButton(scenes, "Blinking  [B]", SceneId.Blinking);
        AddActionSceneButton(scenes, "Talking  [T]", SceneId.Talking);
        AddActionSceneButton(scenes, "Candle sputter  [C]", SceneId.CandleSputter);
        content.AddChild(scenes);
        return WrapCard(content);
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
