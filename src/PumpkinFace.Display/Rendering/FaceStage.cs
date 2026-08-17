using Godot;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Rendering;

/// <summary>
/// Owns the single GPU-backed framebuffer used by both the private operator
/// preview and clean projector output.
/// </summary>
public sealed partial class FaceStage : Node
{
    public static readonly Vector2I DefaultOutputSize = new(1920, 1080);

    private SubViewport? _viewport;
    private ColorRect? _background;
    private Node2D? _projectionRoot;
    private Node2D? _trembleRoot;
    private FaceRig? _rig;
    private ProjectionGuides? _projectionGuides;
    private FaceDesignGuides? _faceGuides;
    private FacePose _pose = FacePose.Neutral;
    private ProjectionCalibration _calibration = ProjectionCalibration.Default;
    private Vector2I _outputSize = DefaultOutputSize;
    private double _animationTime;
    private bool _showGuides;
    private bool _initialized;

    public FacePose Pose
    {
        get => _pose;
        set => SetPose(value);
    }

    public ProjectionCalibration Calibration
    {
        get => _calibration;
        set => SetCalibration(value);
    }

    public bool ShowGuides
    {
        get => _showGuides;
        set
        {
            _showGuides = value;
            ApplyGuideVisibility();
        }
    }

    public SubViewport Viewport
    {
        get
        {
            EnsureInitialized();
            return _viewport!;
        }
    }

    public ViewportTexture Texture
    {
        get
        {
            EnsureInitialized();
            return _viewport!.GetTexture();
        }
    }

    public FaceRig Rig
    {
        get
        {
            EnsureInitialized();
            return _rig!;
        }
    }

    public Vector2I OutputSize => _outputSize;

    /// <summary>
    /// Controls the deterministic candle shader clock. Capture tooling can turn
    /// AutoAdvanceAnimationTime off and set exact timestamps here.
    /// </summary>
    public double AnimationTime
    {
        get => _animationTime;
        set
        {
            _animationTime = Math.Max(0d, value);
            if (_rig is not null)
            {
                _rig.AnimationTime = _animationTime;
                ApplyTremble();
            }
        }
    }

    public bool AutoAdvanceAnimationTime { get; set; } = true;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        EnsureInitialized();
    }

    public override void _Process(double delta)
    {
        if (!_initialized)
        {
            return;
        }

        if (AutoAdvanceAnimationTime)
        {
            AnimationTime += Math.Max(0d, delta);
        }
        else
        {
            ApplyTremble();
        }
    }

    public void SetPose(FacePose pose)
    {
        _pose = pose.Clamp();
        EnsureInitialized();
        _rig!.SetPose(_pose);
        ApplyTremble();
    }

    public void SetCalibration(ProjectionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _calibration = calibration.Normalize();
        EnsureInitialized();
        _rig!.SetCalibration(_calibration);
        ApplyProjectionTransform();
    }

    public void Resize(Vector2I outputSize)
    {
        if (outputSize.X <= 0 || outputSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputSize),
                outputSize,
                "Projection output dimensions must be positive.");
        }

        _outputSize = outputSize;
        EnsureInitialized();

        _viewport!.Size = outputSize;
        _background!.Size = outputSize;
        _projectionGuides!.OutputSize = outputSize;
        ApplyProjectionTransform();
    }

    /// <summary>
    /// Converts a point in the normalized face design to output pixels. Useful
    /// for operator-window drag handles and calibration diagnostics.
    /// </summary>
    public Vector2 DesignToViewport(Vector2 designPoint)
    {
        EnsureInitialized();
        return _projectionRoot!.Transform * designPoint;
    }

    /// <summary>
    /// Converts output pixels back into face-design coordinates.
    /// </summary>
    public Vector2 ViewportToDesign(Vector2 viewportPoint)
    {
        EnsureInitialized();
        return _projectionRoot!.Transform.AffineInverse() * viewportPoint;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _viewport = new SubViewport
        {
            Name = "FaceViewport",
            Size = _outputSize,
            Disable3D = true,
            TransparentBg = false,
            HandleInputLocally = false,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };

        _background = new ColorRect
        {
            Name = "BlackProjectionBackground",
            Color = Colors.Black,
            Size = _outputSize,
            ZIndex = -100,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        Node2D stageCanvas = new()
        {
            Name = "StageCanvas",
        };
        _projectionRoot = new Node2D
        {
            Name = "ProjectionCalibration",
        };
        _trembleRoot = new Node2D
        {
            Name = "ExpressionMotion",
        };
        _rig = new FaceRig
        {
            Name = "FaceRig",
        };
        _faceGuides = new FaceDesignGuides
        {
            Name = "FaceDesignGuides",
        };
        _projectionGuides = new ProjectionGuides
        {
            Name = "OutputGuides",
            OutputSize = _outputSize,
        };

        AddChild(_viewport);
        _viewport.AddChild(_background);
        _viewport.AddChild(stageCanvas);
        stageCanvas.AddChild(_projectionRoot);
        _projectionRoot.AddChild(_trembleRoot);
        _trembleRoot.AddChild(_rig);
        _projectionRoot.AddChild(_faceGuides);
        stageCanvas.AddChild(_projectionGuides);

        _rig.SetPose(_pose);
        _rig.SetCalibration(_calibration);
        _rig.AnimationTime = _animationTime;
        ApplyProjectionTransform();
        ApplyGuideVisibility();
    }

    private void ApplyProjectionTransform()
    {
        if (!_initialized)
        {
            return;
        }

        float fitScale = Mathf.Min(
            _outputSize.X / FaceRig.DesignSize.X,
            _outputSize.Y / FaceRig.DesignSize.Y);
        Vector2 output = _outputSize;

        _projectionRoot!.Position = output * 0.5f + new Vector2(
            _calibration.OffsetX * output.X,
            _calibration.OffsetY * output.Y);
        _projectionRoot.Scale = new Vector2(
            fitScale * _calibration.ScaleX,
            fitScale * _calibration.ScaleY);
        _projectionRoot.RotationDegrees = _calibration.RotationDegrees;
        ApplyTremble();
    }

    private void ApplyTremble()
    {
        if (_rig is not null && _trembleRoot is not null)
        {
            _trembleRoot.Position = _rig.PerformanceOffset;
            _trembleRoot.Rotation = _rig.PerformanceRotationRadians;
            _trembleRoot.Scale = _rig.PerformanceScale;
        }
    }

    private void ApplyGuideVisibility()
    {
        if (_projectionGuides is not null)
        {
            _projectionGuides.Visible = _showGuides;
        }

        if (_faceGuides is not null)
        {
            _faceGuides.Visible = _showGuides;
        }
    }
}
