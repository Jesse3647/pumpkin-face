using Godot;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Rendering;

/// <summary>
/// A calibrated 2D surface backed by a real 3D pumpkin render. The public node
/// remains Node2D so the existing projector transform and direct-manipulation
/// controls continue to operate in stable 1600x900 design coordinates.
/// </summary>
public sealed partial class FaceRig : Node2D
{
    public static readonly Vector2 DesignSize = new(1600f, 900f);

    private const string ShellShaderPath = "res://Shaders/pumpkin_shell_3d.gdshader";
    private const string InteriorShaderPath = "res://Shaders/pumpkin_interior_3d.gdshader";
    private const string FlameShaderPath = "res://Shaders/candle_flame_3d.gdshader";
    private const string HaloShaderPath = "res://Shaders/carved_halo_3d.gdshader";
    private const float DesignToWorldScale = 150f;
    private const float PumpkinRadiusX = 5.58f;
    private const float PumpkinRadiusY = 3.74f;
    private const float PumpkinRadiusZ = 3.38f;

    private readonly List<ShaderMaterial> _animatedMaterials = [];
    private SubViewport? _modelViewport;
    private Node3D? _modelRoot;
    private Camera3D? _camera;
    private Sprite2D? _modelSurface;
    private MeshInstance3D? _pumpkinShell;
    private MeshInstance3D? _innerWall;
    private OmniLight3D? _candleLight;
    private Node3D? _flameRoot;
    private ShaderMaterial? _shellMaterial;
    private ShaderMaterial? _interiorMaterial;
    private StandardMaterial3D? _charMaterial;
    private StandardMaterial3D? _wallMaterial;
    private StandardMaterial3D? _pupilMaterial;
    private StandardMaterial3D? _catchlightMaterial;
    private CarvedFeature3D? _leftEye;
    private CarvedFeature3D? _rightEye;
    private CarvedFeature3D? _nose;
    private CarvedFeature3D? _mouth;
    private FlatFeature3D? _leftPupil;
    private FlatFeature3D? _rightPupil;
    private FlatFeature3D? _leftCatchlight;
    private FlatFeature3D? _rightCatchlight;
    private FacePose _pose = FacePose.Neutral;
    private ProjectionCalibration _calibration = ProjectionCalibration.Default;
    private float _emotionAmount = 1f;
    private Vector2 _cameraOrbitDegrees;
    private double _animationTime;
    private bool _initialized;

    public FacePose Pose => _pose;

    public ProjectionCalibration Calibration => _calibration;

    public float EmotionAmount
    {
        get => _emotionAmount;
        set
        {
            _emotionAmount = float.IsFinite(value) ? Mathf.Clamp(value, 0f, 1f) : 1f;
            if (_initialized)
            {
                RebuildExpressionGeometry();
                UpdateLightingAndTime();
            }
        }
    }

    public Vector2 CameraOrbitDegrees
    {
        get => _cameraOrbitDegrees;
        set
        {
            _cameraOrbitDegrees = new Vector2(
                Mathf.Clamp(value.X, -60f, 60f),
                Mathf.Clamp(value.Y, -38f, 38f));
            ApplyCameraOrbit();
        }
    }

    public double AnimationTime
    {
        get => _animationTime;
        set
        {
            _animationTime = Math.Max(0d, value);
            UpdateLightingAndTime();
        }
    }

    public Vector2 TrembleOffset
    {
        get
        {
            float amount = _pose.Tremble * _emotionAmount * 12f;
            float time = (float)_animationTime;
            return new Vector2(
                (Mathf.Sin(time * 39.1f) + Mathf.Sin(time * 23.7f + 1.2f) * 0.42f) * amount,
                (Mathf.Sin(time * 35.3f + 0.7f) + Mathf.Sin(time * 19.9f) * 0.35f) * amount * 0.62f);
        }
    }

    public Vector2 PerformanceOffset
    {
        get
        {
            float activity = Mathf.Clamp((_pose.JawOpen - 0.16f) * 1.35f + _pose.Tremble * 0.7f, 0f, 1f);
            float time = (float)_animationTime;
            return TrembleOffset + new Vector2(
                Mathf.Sin(time * 6.1f + 0.4f) * activity * 2.2f,
                Mathf.Sin(time * 4.7f) * activity * 1.6f);
        }
    }

    public float PerformanceRotationRadians
    {
        get
        {
            float cornerSkew = _pose.RightMouthCorner - _pose.LeftMouthCorner;
            float trembleRock = Mathf.Sin((float)_animationTime * 16.3f) * _pose.Tremble;
            return cornerSkew * 0.0075f + trembleRock * 0.0055f;
        }
    }

    public Vector2 PerformanceScale
    {
        get
        {
            float activity = Mathf.Clamp((_pose.JawOpen - 0.18f) * 1.15f, 0f, 1f);
            float pulse = Mathf.Sin((float)_animationTime * 4.7f + 0.8f) * activity;
            return new Vector2(1f + pulse * 0.0035f, 1f - pulse * 0.005f);
        }
    }

    public override void _Ready() => EnsureInitialized();

    public void SetPose(FacePose pose)
    {
        _pose = pose.Clamp();
        EnsureInitialized();
        RebuildExpressionGeometry();
        UpdateLightingAndTime();
    }

    public void SetCalibration(ProjectionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _calibration = calibration.Normalize();
        EnsureInitialized();
        UpdateShellThickness();
        RebuildExpressionGeometry();
        UpdateLightingAndTime();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _modelViewport = new SubViewport
        {
            Name = "Pumpkin3DViewport",
            Size = new Vector2I((int)DesignSize.X, (int)DesignSize.Y),
            Disable3D = false,
            TransparentBg = true,
            HandleInputLocally = false,
            OwnWorld3D = true,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_modelViewport);

        _modelRoot = new Node3D { Name = "TransparentPumpkinModel" };
        _modelViewport.AddChild(_modelRoot);

        Godot.Environment environment = new()
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.25f, 0.045f, 0.004f),
            AmbientLightEnergy = 0.10f,
        };
        WorldEnvironment worldEnvironment = new()
        {
            Name = "CandleEnvironment",
            Environment = environment,
        };
        _modelRoot.AddChild(worldEnvironment);

        _camera = new Camera3D
        {
            Name = "ProjectionCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 7.55f,
            Position = new Vector3(0f, 0f, 13f),
            Current = true,
        };
        _modelRoot.AddChild(_camera);
        ApplyCameraOrbit();

        _candleLight = new OmniLight3D
        {
            Name = "InternalCandle",
            Position = new Vector3(0f, -1.22f, 0.12f),
            LightColor = new Color(1f, 0.48f, 0.095f),
            LightEnergy = 34f,
            OmniRange = 7.2f,
            OmniAttenuation = 1.52f,
            ShadowEnabled = false,
        };
        _modelRoot.AddChild(_candleLight);

        CreateMaterials();
        CreatePumpkinShell();
        CreateCandle();
        CreateFaceFeatures();

        _modelSurface = new Sprite2D
        {
            Name = "Pumpkin3DRender",
            Texture = _modelViewport.GetTexture(),
            Centered = true,
        };
        AddChild(_modelSurface);

        RebuildExpressionGeometry();
        UpdateLightingAndTime();
    }

    private void CreateMaterials()
    {
        Shader? shellShader = GD.Load<Shader>(ShellShaderPath);
        Shader? interiorShader = GD.Load<Shader>(InteriorShaderPath);
        Shader? flameShader = GD.Load<Shader>(FlameShaderPath);
        Shader? haloShader = GD.Load<Shader>(HaloShaderPath);
        if (shellShader is null || interiorShader is null || flameShader is null || haloShader is null)
        {
            GD.PushError("The 3D pumpkin shaders could not be loaded.");
        }

        _shellMaterial = new ShaderMaterial { Shader = shellShader };
        _animatedMaterials.Add(_shellMaterial);
        _interiorMaterial = new ShaderMaterial { Shader = interiorShader };
        _animatedMaterials.Add(_interiorMaterial);

        _charMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.010f, 0.002f, 0.0005f),
            Roughness = 0.97f,
            Metallic = 0f,
        };
        _wallMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.27f, 0.052f, 0.004f),
            Roughness = 0.92f,
            Metallic = 0f,
        };
        _pupilMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.002f, 0.001f, 0.0004f),
            Roughness = 0.72f,
        };
        _catchlightMaterial = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Roughness = 0.35f,
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = 2.4f,
        };

        ShaderMaterial NewHalo(float strength)
        {
            ShaderMaterial material = new() { Shader = haloShader };
            material.SetShaderParameter("halo_strength", strength);
            _animatedMaterials.Add(material);
            return material;
        }

        _leftEye = NewCarvedFeature(
            "LeftEye",
            NewHalo(0.11f),
            innerScale: 0.955f,
            charScale: 1.022f,
            haloScale: 1.13f);
        _rightEye = NewCarvedFeature(
            "RightEye",
            NewHalo(0.11f),
            innerScale: 0.955f,
            charScale: 1.022f,
            haloScale: 1.13f);
        _nose = NewCarvedFeature(
            "Nose",
            NewHalo(0.075f),
            haloScale: 1.18f);
        _mouth = NewCarvedFeature(
            "Mouth",
            NewHalo(0.14f),
            innerScale: 0.970f,
            charScale: 1.016f,
            haloScale: 1.10f);
    }

    private void ApplyCameraOrbit()
    {
        if (_camera is null)
        {
            return;
        }

        const float distance = 13f;
        float yaw = Mathf.DegToRad(_cameraOrbitDegrees.X);
        float pitch = Mathf.DegToRad(_cameraOrbitDegrees.Y);
        float horizontal = Mathf.Cos(pitch) * distance;
        _camera.Position = new Vector3(
            Mathf.Sin(yaw) * horizontal,
            Mathf.Sin(pitch) * distance,
            Mathf.Cos(yaw) * horizontal);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private CarvedFeature3D NewCarvedFeature(
        string name,
        ShaderMaterial haloMaterial,
        float innerScale = 0.86f,
        float charScale = 1.055f,
        float haloScale = 1.14f) =>
        new(
            name,
            _modelRoot!,
            _charMaterial!,
            _wallMaterial!,
            haloMaterial,
            innerScale,
            charScale,
            haloScale);

    private void CreatePumpkinShell()
    {
        _pumpkinShell = new MeshInstance3D
        {
            Name = "InvisibleProjectionOccluder",
            Mesh = BuildPumpkinMesh(50, 144),
            MaterialOverride = _shellMaterial,
        };
        _modelRoot!.AddChild(_pumpkinShell);

        _innerWall = new MeshInstance3D
        {
            Name = "CandleLitInnerWall",
            Mesh = BuildPumpkinMesh(30, 80, InnerWallScale(), inward: true),
            MaterialOverride = _interiorMaterial,
        };
        _modelRoot.AddChild(_innerWall);
    }

    private void CreateCandle()
    {
        StandardMaterial3D waxMaterial = new()
        {
            AlbedoColor = new Color(0.93f, 0.66f, 0.34f),
            Roughness = 0.86f,
            Metallic = 0f,
            EmissionEnabled = true,
            Emission = new Color(0.46f, 0.19f, 0.045f),
            EmissionEnergyMultiplier = 0.48f,
        };
        StandardMaterial3D wickMaterial = new()
        {
            AlbedoColor = new Color(0.018f, 0.009f, 0.004f),
            Roughness = 1f,
        };

        Node3D candle = new() { Name = "VisibleCandle" };
        _modelRoot!.AddChild(candle);
        MeshInstance3D body = new()
        {
            Name = "WaxBody",
            Position = new Vector3(0f, -2.46f, -0.62f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.32f,
                BottomRadius = 0.35f,
                Height = 1.12f,
                RadialSegments = 32,
                Rings = 4,
            },
            MaterialOverride = waxMaterial,
        };
        candle.AddChild(body);
        MeshInstance3D waxTop = new()
        {
            Name = "MeltedWaxTop",
            Position = new Vector3(0f, -1.88f, -0.62f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.28f,
                BottomRadius = 0.32f,
                Height = 0.065f,
                RadialSegments = 32,
            },
            MaterialOverride = waxMaterial,
        };
        candle.AddChild(waxTop);
        MeshInstance3D wick = new()
        {
            Name = "CharredWick",
            Position = new Vector3(0f, -1.78f, -0.62f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.022f,
                BottomRadius = 0.028f,
                Height = 0.18f,
                RadialSegments = 12,
            },
            MaterialOverride = wickMaterial,
        };
        candle.AddChild(wick);

        Shader? flameShader = GD.Load<Shader>(FlameShaderPath);
        ShaderMaterial outerFlame = new() { Shader = flameShader };
        outerFlame.SetShaderParameter("flame_layer", 0f);
        ShaderMaterial innerFlame = new() { Shader = flameShader };
        innerFlame.SetShaderParameter("flame_layer", 1f);
        _animatedMaterials.Add(outerFlame);
        _animatedMaterials.Add(innerFlame);

        _flameRoot = new Node3D
        {
            Name = "FlickeringFlame",
            Position = new Vector3(0f, -1.54f, -0.62f),
        };
        candle.AddChild(_flameRoot);
        _flameRoot.AddChild(new MeshInstance3D
        {
            Name = "OuterFlame",
            Mesh = BuildFlameMesh(0.22f, 0.72f, 0.10f),
            MaterialOverride = outerFlame,
        });
        _flameRoot.AddChild(new MeshInstance3D
        {
            Name = "HotCore",
            Position = new Vector3(0f, -0.10f, 0.005f),
            Mesh = BuildFlameMesh(0.115f, 0.43f, 0.035f),
            MaterialOverride = innerFlame,
        });
    }

    private void CreateFaceFeatures()
    {
        _leftPupil = new FlatFeature3D("LeftPupil", _modelRoot!, _pupilMaterial!);
        _rightPupil = new FlatFeature3D("RightPupil", _modelRoot!, _pupilMaterial!);
        _leftCatchlight = new FlatFeature3D("LeftCatchlight", _modelRoot!, _catchlightMaterial!);
        _rightCatchlight = new FlatFeature3D("RightCatchlight", _modelRoot!, _catchlightMaterial!);
    }

    private void RebuildExpressionGeometry()
    {
        if (!_initialized || _leftEye is null || _rightEye is null || _nose is null || _mouth is null)
        {
            return;
        }

        ReferenceFaceShape shape = ResolveReferenceShape(_pose);
        Vector2[] leftEye = ApplyEyeCalibration(shape.LeftEye);
        Vector2[] rightEye = ApplyEyeCalibration(shape.RightEye);
        Vector2[] nose = shape.Nose;
        Vector2[] mouth = ApplyMouthCalibration(shape.Mouth);
        _leftEye.SetPolygon(leftEye, _calibration.ShellThickness);
        _rightEye.SetPolygon(rightEye, _calibration.ShellThickness);
        _nose.SetPolygon(nose, _calibration.ShellThickness);
        _mouth.SetPolygon(mouth, _calibration.ShellThickness);
        if (_shellMaterial is not null)
        {
            SetShellAperture("left_eye", leftEye);
            SetShellAperture("right_eye", rightEye);
            SetShellAperture("nose", nose);
            SetShellAperture("mouth", mouth);
        }

        SetReferencePupil(_leftPupil!, _leftCatchlight!, shape.LeftPupil,
            shape.LeftCatchlight, shape.PupilRadius, shape.CatchlightRadius,
            new Vector2(_pose.LeftGazeX, _pose.LeftGazeY), _pose.LeftEyelidOpen);
        SetReferencePupil(_rightPupil!, _rightCatchlight!, shape.RightPupil,
            shape.RightCatchlight, shape.PupilRadius, shape.CatchlightRadius,
            new Vector2(_pose.RightGazeX, _pose.RightGazeY), _pose.RightEyelidOpen);
    }

    private ReferenceFaceShape ResolveReferenceShape(FacePose pose)
    {
        float brow = (pose.LeftBrowTension + pose.RightBrowTension) * 0.5f;
        float smile = (pose.LeftMouthCorner + pose.RightMouthCorner) * 0.5f;

        // There is intentionally no fourth, averaged "neutral" face. The
        // three authored expressions form a triangle in brow/smile pose space,
        // so AnimationTree crossfades map directly to barycentric contour
        // blends between the traced endpoints.
        if (Mathf.Abs(brow) < 0.12f && Mathf.Abs(smile) < 0.12f)
        {
            return ApplyEmotionAmount(ReferenceFaceContours.Happy);
        }

        Vector2 frightenedPoint = new(-0.88f, -0.82f);
        Vector2 happyPoint = new(0.04f, 0.94f);
        Vector2 sadPoint = new(0.88f, -0.92f);
        Vector2 point = new(brow, smile);
        float denominator =
            (happyPoint.Y - sadPoint.Y) * (frightenedPoint.X - sadPoint.X) +
            (sadPoint.X - happyPoint.X) * (frightenedPoint.Y - sadPoint.Y);
        float frightenedWeight =
            ((happyPoint.Y - sadPoint.Y) * (point.X - sadPoint.X) +
             (sadPoint.X - happyPoint.X) * (point.Y - sadPoint.Y)) / denominator;
        float happyWeight =
            ((sadPoint.Y - frightenedPoint.Y) * (point.X - sadPoint.X) +
             (frightenedPoint.X - sadPoint.X) * (point.Y - sadPoint.Y)) / denominator;
        float sadWeight = 1f - frightenedWeight - happyWeight;
        frightenedWeight = Mathf.Max(0f, frightenedWeight);
        happyWeight = Mathf.Max(0f, happyWeight);
        sadWeight = Mathf.Max(0f, sadWeight);
        float total = Mathf.Max(0.0001f, frightenedWeight + happyWeight + sadWeight);
        frightenedWeight /= total;
        happyWeight /= total;
        sadWeight /= total;

        ReferenceFaceShape fearToHappy = BlendReferenceShapes(
            ReferenceFaceContours.Frightened,
            ReferenceFaceContours.Happy,
            happyWeight / Mathf.Max(0.0001f, frightenedWeight + happyWeight));
        return ApplyEmotionAmount(BlendReferenceShapes(
            fearToHappy,
            ReferenceFaceContours.Sad,
            sadWeight));
    }

    private ReferenceFaceShape ApplyEmotionAmount(ReferenceFaceShape shape)
    {
        float eyeX = Mathf.Lerp(0.92f, 1f, _emotionAmount);
        float eyeY = Mathf.Lerp(0.72f, 1f, _emotionAmount);
        float mouthX = Mathf.Lerp(0.90f, 1f, _emotionAmount);
        float mouthY = Mathf.Lerp(0.52f, 1f, _emotionAmount) * (1f + _pose.JawOpen * 0.86f);
        Vector2 leftEyeScale = new(
            eyeX,
            eyeY * Mathf.Lerp(0.055f, 1f, _pose.LeftEyelidOpen));
        Vector2 rightEyeScale = new(
            eyeX,
            eyeY * Mathf.Lerp(0.055f, 1f, _pose.RightEyelidOpen));
        Vector2 leftEyeCenter = CenterOf(shape.LeftEye);
        Vector2 rightEyeCenter = CenterOf(shape.RightEye);
        Vector2 mouthCenter = CenterOf(shape.Mouth);
        Vector2[] mouth = ScaleContour(shape.Mouth, mouthCenter, new Vector2(mouthX, mouthY));
        if (_pose.SpeechBlend > 0f)
        {
            float speechWidth = Mathf.Lerp(0.42f, 1.12f, _pose.MouthWidth);
            float speechHeight = Mathf.Lerp(0.07f, 0.90f, _pose.JawOpen);
            Vector2[] speechMouth = ScaleContour(
                shape.Mouth,
                mouthCenter,
                new Vector2(speechWidth, speechHeight));
            float rounding = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.92f, _pose.MouthRoundness));
            speechMouth = RoundContourTowardEllipse(speechMouth, mouthCenter, rounding);
            mouth = BlendContours(mouth, speechMouth, Smooth(_pose.SpeechBlend));
        }

        return shape with
        {
            LeftEye = ScaleContour(shape.LeftEye, leftEyeCenter, leftEyeScale),
            RightEye = ScaleContour(shape.RightEye, rightEyeCenter, rightEyeScale),
            Mouth = mouth,
            LeftPupil = ScalePoint(shape.LeftPupil, leftEyeCenter, leftEyeScale),
            RightPupil = ScalePoint(shape.RightPupil, rightEyeCenter, rightEyeScale),
            LeftCatchlight = ScalePoint(shape.LeftCatchlight, leftEyeCenter, leftEyeScale),
            RightCatchlight = ScalePoint(shape.RightCatchlight, rightEyeCenter, rightEyeScale),
            PupilRadius = shape.PupilRadius * Mathf.Lerp(0.84f, 1f, _emotionAmount),
            CatchlightRadius = shape.CatchlightRadius * Mathf.Lerp(0.90f, 1f, _emotionAmount),
        };
    }

    private static Vector2[] ScaleContour(Vector2[] points, Vector2 center, Vector2 scale) =>
        points.Select(point => ScalePoint(point, center, scale)).ToArray();

    private static Vector2 ScalePoint(Vector2 point, Vector2 center, Vector2 scale) =>
        center + (point - center) * scale;

    private static float Smooth(float amount) => amount * amount * (3f - 2f * amount);

    private static Vector2[] RoundContourTowardEllipse(
        Vector2[] points,
        Vector2 center,
        float amount)
    {
        if (amount <= 0f)
        {
            return points;
        }

        float radiusX = Mathf.Max(1f, points.Max(point => Mathf.Abs(point.X - center.X)));
        float radiusY = Mathf.Max(1f, points.Max(point => Mathf.Abs(point.Y - center.Y)));
        float roundedRadiusX = radiusX * Mathf.Lerp(1f, 0.55f, amount);
        float roundedRadiusY = radiusY * Mathf.Lerp(1f, 1.12f, amount);
        return points.Select(point =>
        {
            Vector2 delta = point - center;
            float angle = Mathf.Atan2(delta.Y / radiusY, delta.X / radiusX);
            Vector2 ellipse = center + new Vector2(
                Mathf.Cos(angle) * roundedRadiusX,
                Mathf.Sin(angle) * roundedRadiusY);
            return point.Lerp(ellipse, amount);
        }).ToArray();
    }

    private static ReferenceFaceShape BlendReferenceShapes(
        ReferenceFaceShape from,
        ReferenceFaceShape to,
        float amount) =>
        new(
            BlendContours(from.LeftEye, to.LeftEye, amount),
            BlendContours(from.RightEye, to.RightEye, amount),
            BlendContours(from.Nose, to.Nose, amount),
            BlendContours(from.Mouth, to.Mouth, amount),
            from.LeftPupil.Lerp(to.LeftPupil, amount),
            from.RightPupil.Lerp(to.RightPupil, amount),
            from.LeftCatchlight.Lerp(to.LeftCatchlight, amount),
            from.RightCatchlight.Lerp(to.RightCatchlight, amount),
            Mathf.Lerp(from.PupilRadius, to.PupilRadius, amount),
            Mathf.Lerp(from.CatchlightRadius, to.CatchlightRadius, amount));

    private static Vector2[] BlendContours(Vector2[] from, Vector2[] to, float amount)
    {
        int count = Math.Min(from.Length, to.Length);
        Vector2[] result = new Vector2[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = from[index].Lerp(to[index], amount);
        }

        return result;
    }

    private Vector2[] ApplyEyeCalibration(Vector2[] points) =>
        points.Select(point => new Vector2(point.X * _calibration.EyeSpacing, point.Y)).ToArray();

    private Vector2[] ApplyMouthCalibration(Vector2[] points)
    {
        Vector2 offset = new(
            _calibration.MouthOffsetX * DesignSize.X * 0.5f,
            _calibration.MouthOffsetY * DesignSize.Y * 0.5f);
        return points.Select(point => new Vector2(
            point.X * _calibration.MouthScaleX,
            20f + (point.Y - 20f) * _calibration.MouthScaleY) + offset).ToArray();
    }

    private void SetReferencePupil(
        FlatFeature3D pupil,
        FlatFeature3D catchlight,
        Vector2 center,
        Vector2 catchlightCenter,
        float radius,
        float catchlightRadius,
        Vector2 gaze,
        float eyelidOpen)
    {
        center.X *= _calibration.EyeSpacing;
        catchlightCenter.X *= _calibration.EyeSpacing;
        Vector2 gazeOffset = new(gaze.X * 44f, gaze.Y * 30f);
        center += gazeOffset;
        catchlightCenter += gazeOffset;
        bool visible = eyelidOpen > 0.16f;
        pupil.Visible = visible;
        catchlight.Visible = visible;
        if (!visible)
        {
            return;
        }
        pupil.SetPolygon(BuildCircle(center, radius, 28));
        catchlight.SetPolygon(BuildCircle(catchlightCenter, catchlightRadius, 18), 0.19f);
    }

    private static Vector2[] BuildCircle(Vector2 center, float radius, int segments)
    {
        Vector2[] points = new Vector2[segments];
        for (int index = 0; index < segments; index++)
        {
            float angle = Mathf.Tau * index / segments;
            points[index] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        return points;
    }

    private static Vector2[] BuildEye(
        Vector2 center,
        float width,
        float fullHeight,
        float eyelidOpen,
        float browTension,
        bool mirrored)
    {
        float open = Mathf.Clamp(eyelidOpen, 0.035f, 1f);
        float easedOpen = open * open * (3f - 2f * open);
        float tension = Mathf.Clamp(browTension, -1f, 1f);
        float innerDirection = mirrored ? -1f : 1f;
        float fear = Mathf.Max(0f, -tension);
        float sadness = Mathf.Max(0f, tension);
        float emotionalTilt = -tension * innerDirection * 0.225f;
        float baseline = Mathf.Lerp(0.13f, 0.075f, Mathf.Max(fear, sadness));
        float upperDepth = 0.65f - fear * 0.09f - sadness * 0.13f;
        float lowerDepth = 0.035f + fear * 0.17f + sadness * 0.14f;
        const int steps = 16;
        List<Vector2> points = new(steps * 2);

        for (int step = 0; step <= steps; step++)
        {
            float x = -1f + 2f * step / steps;
            float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x));
            float tilt = emotionalTilt * x;
            float y = baseline + tilt - upperDepth * Mathf.Pow(dome, 0.76f);
            y += Mathf.Sin(step * 2.17f + (mirrored ? 1.1f : 0.2f)) * 0.0025f;
            points.Add(center + new Vector2(x * width * 0.5f, y * fullHeight * easedOpen));
        }

        for (int step = steps - 1; step >= 1; step--)
        {
            float x = -1f + 2f * step / steps;
            float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x));
            float tilt = emotionalTilt * x;
            float y = baseline + tilt + lowerDepth * Mathf.Pow(dome, 1.08f);
            y += Mathf.Sin(step * 1.91f + (mirrored ? 0.8f : 2f)) * 0.002f;
            points.Add(center + new Vector2(x * width * 0.5f, y * fullHeight * easedOpen));
        }

        return [.. points];
    }

    private MouthGeometry BuildMouth(Vector2 center, Vector2 calibrationScale)
    {
        float roundness = Mathf.Clamp(_pose.MouthRoundness, 0f, 1f);
        float smile = (_pose.LeftMouthCorner + _pose.RightMouthCorner) * 0.5f;
        center.Y += (_pose.JawOpen * 12f + roundness * 20f) * calibrationScale.Y;
        center.Y -= smile * 125f * calibrationScale.Y;
        float widthFactor = Mathf.Lerp(1f, 0.60f, roundness);
        float width = Mathf.Lerp(500f, 1050f, _pose.MouthWidth) * widthFactor * calibrationScale.X;
        float height = (108f + _pose.JawOpen * 230f + roundness * 145f) * calibrationScale.Y;
        width = Mathf.Max(width, 150f);
        height = Mathf.Max(height, 68f);

        float curvature = smile * (86f + Mathf.Abs(smile) * 62f) * calibrationScale.Y * (1f - roundness);
        float leftCorner = -(_pose.LeftMouthCorner - smile) * 52f * calibrationScale.Y;
        float rightCorner = -(_pose.RightMouthCorner - smile) * 52f * calibrationScale.Y;
        float toothPresence = Mathf.Clamp((0.58f - roundness) / 0.18f, 0f, 1f);
        toothPresence *= Mathf.Clamp((_pose.MouthWidth - 0.14f) / 0.20f, 0f, 1f);
        Vector2[] outline = BuildUnifiedMouthContour(
            center,
            width,
            height,
            curvature,
            leftCorner,
            rightCorner,
            roundness,
            toothPresence);

        return new MouthGeometry(
            outline, center, width, height, leftCorner, rightCorner,
            curvature, roundness, _pose.MouthWidth);
    }

    /// <summary>
    /// Builds one continuous carved aperture. Tooth bridges are concave notches
    /// in this contour—not separate meshes—so the cavity, charred rim, and cut
    /// wall all follow the exact same pointy silhouette.
    /// </summary>
    private static Vector2[] BuildUnifiedMouthContour(
        Vector2 center,
        float width,
        float height,
        float curvature,
        float leftCorner,
        float rightCorner,
        float roundness,
        float toothPresence)
    {
        List<Vector2> outline = [];
        MouthContourParameters parameters = new(
            center, width, height, curvature, leftCorner, rightCorner, roundness);
        (float Center, float HalfWidth, float Depth)[] upperNotches = toothPresence > 0.04f
            ?
            [
                (-0.43f, 0.085f, Mathf.Clamp(height * 0.34f, 28f, 68f) * toothPresence),
                (0.31f, 0.085f, Mathf.Clamp(height * 0.31f, 26f, 62f) * toothPresence),
            ]
            : [];
        (float Center, float HalfWidth, float Depth)[] lowerNotches = toothPresence > 0.04f
            ?
            [
                (0.68f, 0.075f, Mathf.Clamp(height * 0.40f, 32f, 78f) * toothPresence),
                (-0.14f, 0.085f, Mathf.Clamp(height * 0.44f, 34f, 84f) * toothPresence),
            ]
            : [];

        float cursor = -1f;
        foreach ((float notchCenter, float halfWidth, float depth) in upperNotches)
        {
            float left = notchCenter - halfWidth;
            float right = notchCenter + halfWidth;
            AppendMouthEdge(outline, parameters, cursor, left, top: true);
            Vector2 leftBase = MouthPoint(
                center, width, height, curvature, leftCorner, rightCorner, roundness, left, top: true);
            Vector2 rightBase = MouthPoint(
                center, width, height, curvature, leftCorner, rightCorner, roundness, right, top: true);
            float squareBottom = Mathf.Max(leftBase.Y, rightBase.Y) + depth;
            AddDistinct(outline, leftBase);
            AddDistinct(outline, new Vector2(leftBase.X + width * halfWidth * 0.08f, squareBottom));
            AddDistinct(outline, new Vector2(rightBase.X - width * halfWidth * 0.08f, squareBottom));
            AddDistinct(outline, rightBase);
            cursor = right;
        }

        AppendMouthEdge(outline, parameters, cursor, 1f, top: true);

        cursor = 1f;
        foreach ((float notchCenter, float halfWidth, float depth) in lowerNotches)
        {
            float right = notchCenter + halfWidth;
            float left = notchCenter - halfWidth;
            AppendMouthEdge(outline, parameters, cursor, right, top: false);
            Vector2 rightBase = MouthPoint(
                center, width, height, curvature, leftCorner, rightCorner, roundness, right, top: false);
            Vector2 leftBase = MouthPoint(
                center, width, height, curvature, leftCorner, rightCorner, roundness, left, top: false);
            float squareTop = Mathf.Min(rightBase.Y, leftBase.Y) - depth;
            AddDistinct(outline, rightBase);
            AddDistinct(outline, new Vector2(rightBase.X - width * halfWidth * 0.08f, squareTop));
            AddDistinct(outline, new Vector2(leftBase.X + width * halfWidth * 0.08f, squareTop));
            AddDistinct(outline, leftBase);
            cursor = left;
        }

        AppendMouthEdge(outline, parameters, cursor, -1f, top: false);
        if (outline.Count > 1 && outline[^1].DistanceSquaredTo(outline[0]) <= 0.01f)
        {
            outline.RemoveAt(outline.Count - 1);
        }

        return [.. outline];
    }

    private static void AppendMouthEdge(
        List<Vector2> outline,
        MouthContourParameters parameters,
        float from,
        float to,
        bool top)
    {
        int segments = Math.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) * 6f));
        for (int step = 0; step <= segments; step++)
        {
            float x = Mathf.Lerp(from, to, step / (float)segments);
            AddDistinct(outline, MouthPoint(parameters, x, top));
        }
    }

    private static Vector2 MouthPoint(MouthContourParameters parameters, float normalizedX, bool top) =>
        MouthPoint(
            parameters.Center,
            parameters.Width,
            parameters.Height,
            parameters.Curvature,
            parameters.LeftCorner,
            parameters.RightCorner,
            parameters.Roundness,
            normalizedX,
            top);

    private static Vector2 MouthPoint(
        Vector2 center,
        float width,
        float height,
        float curvature,
        float leftCorner,
        float rightCorner,
        float roundness,
        float normalizedX,
        bool top)
    {
        (float middle, float half) = MouthEdge(
            normalizedX, height, curvature, leftCorner, rightCorner, roundness);
        return center + new Vector2(normalizedX * width * 0.5f, middle + (top ? -half : half));
    }

    private static void AddDistinct(List<Vector2> points, Vector2 point)
    {
        if (points.Count == 0 || points[^1].DistanceSquaredTo(point) > 0.01f)
        {
            points.Add(point);
        }
    }

    private static (float Midline, float HalfThickness) MouthEdge(
        float normalizedX,
        float height,
        float curvature,
        float leftCorner,
        float rightCorner,
        float roundness)
    {
        float ellipse = Mathf.Sqrt(Mathf.Max(0f, 1f - normalizedX * normalizedX));
        float arch = curvature * (1f - normalizedX * normalizedX);
        float asymmetry = Mathf.Lerp(leftCorner, rightCorner, normalizedX * 0.5f + 0.5f);
        return (
            arch + asymmetry,
            height * 0.5f * Mathf.Pow(ellipse, Mathf.Lerp(0.82f, 1.08f, roundness)));
    }


    private void UpdatePupil(
        FlatFeature3D pupil,
        FlatFeature3D catchlight,
        Vector2 eyeCenter,
        Vector2 gaze,
        float eyelidOpen,
        bool mirrored)
    {
        float open = Mathf.Clamp(eyelidOpen, 0f, 1f);
        bool visible = open >= 0.10f;
        pupil.Visible = visible;
        catchlight.Visible = visible;
        if (!visible)
        {
            return;
        }

        gaze = new Vector2(Mathf.Clamp(gaze.X, -1f, 1f), Mathf.Clamp(gaze.Y, -1f, 1f));
        float openHeight = (mirrored ? 324f : 332f) * (0.11f + 0.89f * open);
        float radius = Mathf.Max(18f, Mathf.Min(94f * _pose.PupilSize, openHeight * 0.31f));
        Vector2 center = eyeCenter
            + gaze * new Vector2(69f, Mathf.Max(6f, openHeight * 0.14f))
            + new Vector2(0f, openHeight * 0.055f);
        pupil.SetPolygon(BuildEllipse(
            center,
            new Vector2(radius * 0.90f, radius * 0.92f),
            24,
            mirrored ? 1.7f : 0.2f));
        catchlight.SetPolygon(BuildEllipse(
            center + new Vector2(radius * 0.24f, -radius * 0.30f),
            new Vector2(radius * 0.19f, radius * 0.21f),
            16,
            mirrored ? 0.8f : 1.4f), 0.19f);
    }

    private static Vector2[] BuildEllipse(Vector2 center, Vector2 radii, int segments, float phase)
    {
        Vector2[] points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            float wobble = 1f + Mathf.Sin(angle * 3f + phase) * 0.025f + Mathf.Sin(angle * 5f - phase) * 0.012f;
            points[i] = center + new Vector2(
                Mathf.Cos(angle) * radii.X,
                Mathf.Sin(angle) * radii.Y) * wobble;
        }

        return points;
    }

    private void UpdateLightingAndTime()
    {
        if (!_initialized)
        {
            return;
        }

        float time = (float)_animationTime;
        foreach (ShaderMaterial material in _animatedMaterials)
        {
            material.SetShaderParameter("animation_time", time);
            material.SetShaderParameter("light_intensity", _pose.LightingIntensity);
            material.SetShaderParameter("brightness", _calibration.Brightness);
            material.SetShaderParameter("gamma", _calibration.Gamma);
            material.SetShaderParameter("candle_brightness", _calibration.CandleBrightness);
        }

        if (_candleLight is not null)
        {
            float slowFlutter = Mathf.Sin(time * 5.7f);
            float quickFlutter = Mathf.Sin(time * 11.9f + 1.3f);
            float flutter = 0.93f + slowFlutter * 0.055f + quickFlutter * 0.027f;
            float emotionalLight = Mathf.Lerp(1f, _pose.LightingIntensity, _emotionAmount);
            Vector3 flamePosition = new(
                Mathf.Sin(time * 1.7f) * 0.018f + quickFlutter * 0.009f,
                -1.54f + slowFlutter * 0.022f,
                -0.62f + Mathf.Sin(time * 2.3f + 0.7f) * 0.008f);
            if (_flameRoot is not null)
            {
                _flameRoot.Position = flamePosition;
                _flameRoot.Rotation = new Vector3(0f, 0f, Mathf.DegToRad(slowFlutter * 2.8f + quickFlutter * 1.3f));
                _flameRoot.Scale = new Vector3(
                    1f - quickFlutter * 0.025f,
                    1f + slowFlutter * 0.055f + quickFlutter * 0.028f,
                    1f - quickFlutter * 0.025f);
            }

            _candleLight.LightEnergy = 34f * emotionalLight * _calibration.Brightness *
                _calibration.CandleBrightness * flutter;
            _candleLight.Position = flamePosition + new Vector3(0f, 0.08f, 0f);
            _interiorMaterial?.SetShaderParameter("candle_position", _candleLight.Position);
            _shellMaterial?.SetShaderParameter("candle_position", _candleLight.Position);
        }
    }

    private float InnerWallScale()
    {
        float thickness = Mathf.Clamp(_calibration.ShellThickness, 0.2f, 2.5f);
        return Mathf.Clamp(1f - 0.035f * Mathf.Pow(thickness, 1.25f), 0.88f, 0.995f);
    }

    private void UpdateShellThickness()
    {
        if (_innerWall is not null)
        {
            _innerWall.Mesh = BuildPumpkinMesh(30, 80, InnerWallScale(), inward: true);
        }
    }

    private static ArrayMesh BuildPumpkinMesh(
        int latitudeSegments,
        int longitudeSegments,
        float radiusScale = 1f,
        bool inward = false)
    {
        List<Vector3> vertices = [];
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        List<int> indices = [];
        for (int latitude = 0; latitude <= latitudeSegments; latitude++)
        {
            float v = latitude / (float)latitudeSegments;
            float theta = v * Mathf.Pi;
            float ring = Mathf.Sin(theta);
                float y = Mathf.Cos(theta) * PumpkinRadiusY * radiusScale;
            for (int longitude = 0; longitude <= longitudeSegments; longitude++)
            {
                float u = longitude / (float)longitudeSegments;
                float phi = u * Mathf.Tau;
                float lobe = 1f + Mathf.Cos(phi * 10f) * 0.075f;
                float x = Mathf.Cos(phi) * ring * PumpkinRadiusX * lobe * radiusScale;
                float z = Mathf.Sin(phi) * ring * PumpkinRadiusZ * lobe * radiusScale;
                vertices.Add(new Vector3(x, y, z));
                Vector3 normal = new Vector3(
                    x / (PumpkinRadiusX * PumpkinRadiusX),
                    y / (PumpkinRadiusY * PumpkinRadiusY),
                    z / (PumpkinRadiusZ * PumpkinRadiusZ)).Normalized();
                normals.Add(inward ? -normal : normal);
                uvs.Add(new Vector2(u, v));
            }
        }

        int stride = longitudeSegments + 1;
        for (int latitude = 0; latitude < latitudeSegments; latitude++)
        {
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
            {
                int a = latitude * stride + longitude;
                int b = a + stride;
                if (inward)
                {
                    indices.Add(a); indices.Add(a + 1); indices.Add(b);
                    indices.Add(a + 1); indices.Add(b + 1); indices.Add(b);
                }
                else
                {
                    indices.Add(a); indices.Add(b); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                }
            }
        }

        return BuildMesh([.. vertices], [.. normals], [.. uvs], [.. indices]);
    }

    private void SetShellAperture(string uniformPrefix, Vector2[] contour)
    {
        const int maximumPoints = 96;
        Vector2[] shaderPoints = new Vector2[maximumPoints];
        int count = Math.Min(contour.Length, maximumPoints);
        for (int index = 0; index < count; index++)
        {
            shaderPoints[index] = contour[index];
        }

        _shellMaterial!.SetShaderParameter($"{uniformPrefix}_points", shaderPoints);
        _shellMaterial.SetShaderParameter($"{uniformPrefix}_count", count);
    }

    private static ArrayMesh BuildFlameMesh(float radius, float height, float lean)
    {
        const int rings = 12;
        const int segments = 24;
        List<Vector3> vertices = [];
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        List<int> indices = [];
        for (int ringIndex = 0; ringIndex <= rings; ringIndex++)
        {
            float v = ringIndex / (float)rings;
            float profile = Mathf.Pow(Mathf.Sin(Mathf.Pi * v), 0.62f) * Mathf.Lerp(1.08f, 0.34f, v);
            float ringRadius = radius * profile;
            float y = (v - 0.5f) * height;
            float xLean = lean * v * v;
            for (int segment = 0; segment <= segments; segment++)
            {
                float u = segment / (float)segments;
                float angle = u * Mathf.Tau;
                Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(new Vector3(xLean + radial.X * ringRadius, y, radial.Z * ringRadius));
                normals.Add((radial + new Vector3(-lean * 0.35f, radius / height, 0f)).Normalized());
                uvs.Add(new Vector2(u, v));
            }
        }

        int stride = segments + 1;
        for (int ringIndex = 0; ringIndex < rings; ringIndex++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int a = ringIndex * stride + segment;
                int b = a + stride;
                indices.Add(a); indices.Add(b); indices.Add(a + 1);
                indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
            }
        }

        return BuildMesh([.. vertices], [.. normals], [.. uvs], [.. indices]);
    }

    private static Vector3 SurfacePoint(Vector2 designPoint, float depthOffset)
    {
        float x = designPoint.X / DesignToWorldScale;
        float y = -designPoint.Y / DesignToWorldScale;
        float normalized = 1f -
            x * x / (PumpkinRadiusX * PumpkinRadiusX) -
            y * y / (PumpkinRadiusY * PumpkinRadiusY);
        float z = PumpkinRadiusZ * Mathf.Sqrt(Mathf.Max(0.055f, normalized));
        float rib = 1f + Mathf.Cos(Mathf.Atan2(z, x) * 10f) * 0.045f;
        return new Vector3(x, y, z * rib + depthOffset);
    }

    private static ArrayMesh BuildFaceMesh(Vector2[] points, float depthOffset)
    {
        Vector2[] cleanPoints = SanitizeContour(points);
        int[] triangles = Geometry2D.TriangulatePolygon(cleanPoints);
        Vector3[] vertices = cleanPoints.Select(point => SurfacePoint(point, depthOffset)).ToArray();
        Vector3[] normals = vertices.Select(vertex => vertex.Normalized()).ToArray();
        return BuildMesh(vertices, normals, BuildUvs(cleanPoints), triangles);
    }

    private static Vector2[] SanitizeContour(Vector2[] points)
    {
        List<Vector2> clean = new(points.Length);
        foreach (Vector2 point in points)
        {
            AddDistinct(clean, point);
        }

        if (clean.Count > 1 && clean[^1].DistanceSquaredTo(clean[0]) <= 0.01f)
        {
            clean.RemoveAt(clean.Count - 1);
        }

        return [.. clean];
    }

    private static ArrayMesh BuildRingMesh(Vector2[] outer, Vector2[] inner, float outerDepth, float innerDepth)
    {
        int count = Math.Min(outer.Length, inner.Length);
        Vector3[] vertices = new Vector3[count * 2];
        Vector3[] normals = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        int[] indices = new int[count * 6];
        for (int i = 0; i < count; i++)
        {
            vertices[i * 2] = SurfacePoint(outer[i], outerDepth);
            vertices[i * 2 + 1] = SurfacePoint(inner[i], innerDepth);
            int next = (i + 1) % count;
            Vector3 tangent = SurfacePoint(outer[next], outerDepth) - vertices[i * 2];
            Vector3 depth = vertices[i * 2 + 1] - vertices[i * 2];
            Vector3 normal = tangent.Cross(depth).Normalized();
            normals[i * 2] = normal;
            normals[i * 2 + 1] = normal;
            uvs[i * 2] = new Vector2(i / (float)count, 0f);
            uvs[i * 2 + 1] = new Vector2(i / (float)count, 1f);
            int offset = i * 6;
            indices[offset] = i * 2;
            indices[offset + 1] = next * 2;
            indices[offset + 2] = i * 2 + 1;
            indices[offset + 3] = i * 2 + 1;
            indices[offset + 4] = next * 2;
            indices[offset + 5] = next * 2 + 1;
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    private static ArrayMesh BuildMesh(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        Godot.Collections.Array arrays = [];
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static Vector2[] ScaleContour(Vector2[] points, float factor)
    {
        Vector2 center = CenterOf(points);
        return points.Select(point => center + (point - center) * factor).ToArray();
    }

    private static Vector2 CenterOf(Vector2[] points)
    {
        Vector2 center = Vector2.Zero;
        foreach (Vector2 point in points)
        {
            center += point;
        }

        return center / Math.Max(1, points.Length);
    }

    private static Vector2[] BuildUvs(Vector2[] points)
    {
        float minimumX = points.Min(point => point.X);
        float minimumY = points.Min(point => point.Y);
        float maximumX = points.Max(point => point.X);
        float maximumY = points.Max(point => point.Y);
        float width = Mathf.Max(1f, maximumX - minimumX);
        float height = Mathf.Max(1f, maximumY - minimumY);
        return points.Select(point => new Vector2(
            (point.X - minimumX) / width,
            (point.Y - minimumY) / height)).ToArray();
    }

    private readonly record struct MouthGeometry(
        Vector2[] Outline,
        Vector2 Center,
        float Width,
        float Height,
        float LeftCorner,
        float RightCorner,
        float Curvature,
        float Roundness,
        float WidthControl);

    private readonly record struct MouthContourParameters(
        Vector2 Center,
        float Width,
        float Height,
        float Curvature,
        float LeftCorner,
        float RightCorner,
        float Roundness);

    private sealed class CarvedFeature3D
    {
        private readonly MeshInstance3D _halo;
        private readonly MeshInstance3D _charredRim;
        private readonly MeshInstance3D _cutWall;
        private readonly float _innerScale;
        private readonly float _charScale;
        private readonly float _haloScale;

        public CarvedFeature3D(
            string name,
            Node3D parent,
            Material charMaterial,
            Material wallMaterial,
            Material haloMaterial,
            float innerScale,
            float charScale,
            float haloScale)
        {
            _innerScale = innerScale;
            _charScale = charScale;
            _haloScale = haloScale;
            _halo = NewMesh($"{name}Halo", haloMaterial, parent);
            _charredRim = NewMesh($"{name}CharredRim", charMaterial, parent);
            _cutWall = NewMesh($"{name}CutWall", wallMaterial, parent);
        }

        public void SetPolygon(Vector2[] points, float shellThickness)
        {
            if (points.Length < 3)
            {
                return;
            }

            float thickness = Mathf.Clamp(shellThickness, 0.2f, 2.5f);
            float baseInset = 1f - _innerScale;
            float visibleInnerScale = Mathf.Clamp(1f - baseInset * thickness, 0.68f, 0.995f);
            Vector2[] inner = ScaleContour(points, visibleInnerScale);
            _halo.Mesh = BuildRingMesh(
                ScaleContour(points, _haloScale),
                points,
                0.175f,
                0.160f);
            _charredRim.Mesh = BuildRingMesh(ScaleContour(points, _charScale), points, 0.105f, 0.075f);
            float innerDepth = -0.115f * thickness;
            _cutWall.Mesh = BuildRingMesh(points, inner, 0.070f, innerDepth);
        }

        private static MeshInstance3D NewMesh(string name, Material material, Node3D parent)
        {
            MeshInstance3D instance = new() { Name = name, MaterialOverride = material };
            parent.AddChild(instance);
            return instance;
        }
    }

    private sealed class FlatFeature3D
    {
        private readonly MeshInstance3D _mesh;

        public FlatFeature3D(string name, Node3D parent, Material material)
        {
            _mesh = new MeshInstance3D { Name = name, MaterialOverride = material };
            parent.AddChild(_mesh);
        }

        public bool Visible
        {
            get => _mesh.Visible;
            set => _mesh.Visible = value;
        }

        public void SetPolygon(Vector2[] points, float depthOffset = 0.145f)
        {
            if (points.Length >= 3)
            {
                _mesh.Mesh = BuildFaceMesh(points, depthOffset);
            }
        }
    }
}
