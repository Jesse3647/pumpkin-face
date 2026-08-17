using Godot;

namespace PumpkinFace.Display.App;

/// <summary>
/// Owns the clean native output window and handles safe display fallback.
/// </summary>
public sealed partial class ProjectorHost : Node
{
    private const int WindowedWidth = 960;
    private const int WindowedHeight = 540;

    private Window? _window;
    private TextureRect? _surface;
    private int _selectedScreen;

    public event Action<Vector2I>? OutputSizeChanged;
    public event Action<bool>? FullscreenChanged;
    public event Action? OutputClosed;

    public bool IsFullscreen => _window?.Mode == Window.ModeEnum.Fullscreen;
    public bool IsVisible => _window?.Visible ?? false;
    public int SelectedScreen => _selectedScreen;
    public Vector2I OutputSize => _window?.Size ?? new Vector2I(1920, 1080);

    public override void _Ready()
    {
        EnsureWindow();
    }

    public IReadOnlyList<DisplayChoice> GetDisplays()
    {
        int count = DisplayServer.GetScreenCount();
        List<DisplayChoice> choices = new(count);
        for (int screen = 0; screen < count; screen++)
        {
            Vector2I size = DisplayServer.ScreenGetSize(screen);
            string designation = screen == DisplayServer.GetPrimaryScreen() ? "Primary" : "External";
            choices.Add(new DisplayChoice(screen, $"Display {screen + 1} — {size.X}×{size.Y} ({designation})", size));
        }

        return choices;
    }

    public void SetTexture(Texture2D? texture)
    {
        EnsureWindow();
        if (_surface is not null)
        {
            _surface.Texture = texture;
        }
    }

    public bool OpenSafely(int requestedScreen, bool preferFullscreen)
    {
        EnsureWindow();
        int count = DisplayServer.GetScreenCount();
        bool requestedAvailable = requestedScreen >= 0 && requestedScreen < count;
        bool hasExternal = count > 1;

        _selectedScreen = requestedAvailable ? requestedScreen : DisplayServer.GetPrimaryScreen();
        bool canFullscreen = preferFullscreen && requestedAvailable && hasExternal &&
                             _selectedScreen != DisplayServer.GetPrimaryScreen();

        if (canFullscreen)
        {
            SetScreen(_selectedScreen);
            SetFullscreen(true);
        }
        else
        {
            SetFullscreen(false);
            PlaceWindowed(_selectedScreen);
        }

        _window!.Show();
        OutputSizeChanged?.Invoke(_window.Size);
        return canFullscreen;
    }

    public void SelectScreen(int screen, bool fullscreen)
    {
        int count = DisplayServer.GetScreenCount();
        _selectedScreen = Mathf.Clamp(screen, 0, Mathf.Max(0, count - 1));
        SetScreen(_selectedScreen);

        if (fullscreen)
        {
            SetFullscreen(true);
        }
        else
        {
            SetFullscreen(false);
            PlaceWindowed(_selectedScreen);
        }

        _window?.Show();
    }

    public void ToggleFullscreen()
    {
        SetFullscreen(!IsFullscreen);
        if (!IsFullscreen)
        {
            PlaceWindowed(_selectedScreen);
        }
    }

    public void LeaveFullscreen()
    {
        if (!IsFullscreen)
        {
            return;
        }

        SetFullscreen(false);
        PlaceWindowed(_selectedScreen);
    }

    public void HideOutput()
    {
        _window?.Hide();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new Window
        {
            Name = "ProjectorOutput",
            Title = "Pumpkin Face — Projector Output",
            Size = new Vector2I(WindowedWidth, WindowedHeight),
            MinSize = new Vector2I(640, 360),
            Borderless = true,
            Unresizable = false,
            Exclusive = false,
            AlwaysOnTop = false,
            Visible = false,
        };

        ColorRect background = new()
        {
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _surface = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _window.AddChild(background);
        _window.AddChild(_surface);
        AddChild(_window);

        _window.CloseRequested += () =>
        {
            HideOutput();
            OutputClosed?.Invoke();
        };
        _window.SizeChanged += () => OutputSizeChanged?.Invoke(_window.Size);
        _window.MouseEntered += () =>
        {
            if (IsFullscreen)
            {
                Input.MouseMode = Input.MouseModeEnum.Hidden;
            }
        };
        _window.MouseExited += () => Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void SetScreen(int screen)
    {
        EnsureWindow();
        _window!.CurrentScreen = screen;
    }

    private void SetFullscreen(bool fullscreen)
    {
        EnsureWindow();
        _window!.Mode = fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
        _window.Borderless = fullscreen;
        FullscreenChanged?.Invoke(fullscreen);
    }

    private void PlaceWindowed(int screen)
    {
        EnsureWindow();
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        Vector2I size = new(
            Mathf.Min(WindowedWidth, usable.Size.X),
            Mathf.Min(WindowedHeight, usable.Size.Y));
        _window!.Size = size;
        _window.Position = usable.Position + (usable.Size - size) / 2;
    }
}

public readonly record struct DisplayChoice(int Index, string Label, Vector2I Size);
