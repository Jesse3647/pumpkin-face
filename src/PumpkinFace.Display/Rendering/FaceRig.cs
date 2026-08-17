using Godot;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Rendering;

/// <summary>
/// Parameterized, resolution-independent jack-o-lantern face geometry.
/// The rig contains no scene scheduling logic; callers can blend poses and feed
/// the result here, which also leaves the mouth controls available to a future
/// speech/viseme layer.
/// </summary>
public sealed partial class FaceRig : Node2D
{
    public static readonly Vector2 DesignSize = new(1600f, 900f);

    private const string InteriorShaderPath = "res://Shaders/carved_interior.gdshader";
    private const string GlowShaderPath = "res://Shaders/carved_glow.gdshader";

    private readonly List<ShaderMaterial> _animatedMaterials = [];
    private readonly List<ShaderMaterial> _interiorMaterials = [];
    private readonly List<ShaderMaterial> _glowMaterials = [];

    private Node2D? _glowLayer;
    private Node2D? _edgeLayer;
    private Node2D? _interiorLayer;
    private Node2D? _detailLayer;
    private CarvedFeature? _leftEye;
    private CarvedFeature? _rightEye;
    private CarvedFeature? _nose;
    private CarvedFeature? _mouth;
    private MouthTeethFeature? _mouthTeeth;
    private PupilFeature? _leftPupil;
    private PupilFeature? _rightPupil;
    private FacePose _pose = FacePose.Neutral;
    private ProjectionCalibration _calibration = ProjectionCalibration.Default;
    private double _animationTime;
    private bool _initialized;

    public FacePose Pose => _pose;

    public ProjectionCalibration Calibration => _calibration;

    /// <summary>
    /// A deterministic time source for procedural candle movement. Setting this
    /// value does not advance it; FaceStage normally advances it once per frame.
    /// </summary>
    public double AnimationTime
    {
        get => _animationTime;
        set
        {
            _animationTime = Math.Max(0d, value);
            UpdateShaderTime();
        }
    }

    /// <summary>
    /// Current high-frequency shake in design-space pixels. FaceStage applies it
    /// after calibration so the whole carved face trembles together.
    /// </summary>
    public Vector2 TrembleOffset
    {
        get
        {
            float amount = _pose.Tremble * 12f;
            float time = (float)_animationTime;
            return new Vector2(
                (Mathf.Sin(time * 39.1f) + Mathf.Sin(time * 23.7f + 1.2f) * 0.42f) * amount,
                (Mathf.Sin(time * 35.3f + 0.7f) + Mathf.Sin(time * 19.9f) * 0.35f) * amount * 0.62f);
        }
    }

    /// <summary>
    /// Restrained secondary motion gives broad mouth poses a little weight while
    /// keeping the projection registered to the physical pumpkin.
    /// </summary>
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

    public override void _Ready()
    {
        EnsureInitialized();
    }

    public void SetPose(FacePose pose)
    {
        _pose = pose.Clamp();
        EnsureInitialized();
        RebuildGeometry();
        UpdateLighting();
    }

    public void SetCalibration(ProjectionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _calibration = calibration.Normalize();
        EnsureInitialized();
        RebuildGeometry();
        UpdateLighting();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _glowLayer = CreateLayer("Glow", -30);
        _edgeLayer = CreateLayer("CharredEdges", -20);
        _interiorLayer = CreateLayer("CarvedInteriors", -10);
        _detailLayer = CreateLayer("PupilsAndEmbers", 0);

        Shader? interiorShader = GD.Load<Shader>(InteriorShaderPath);
        Shader? glowShader = GD.Load<Shader>(GlowShaderPath);
        if (interiorShader is null)
        {
            GD.PushWarning($"Unable to load face shader: {InteriorShaderPath}");
        }

        if (glowShader is null)
        {
            GD.PushWarning($"Unable to load face shader: {GlowShaderPath}");
        }

        _leftEye = CreateCarvedFeature(
            "LeftEye", new Vector2(0.17f, 0.63f), new Vector2(10f, 12f), interiorShader, glowShader);
        _rightEye = CreateCarvedFeature(
            "RightEye", new Vector2(0.73f, 0.24f), new Vector2(9f, 13f), interiorShader, glowShader);
        _nose = CreateCarvedFeature(
            "Nose", new Vector2(0.58f, 0.47f), new Vector2(7f, 11f), interiorShader, glowShader);
        _mouth = CreateCarvedFeature(
            "Mouth", new Vector2(0.41f, 0.88f), new Vector2(12f, 15f), interiorShader, glowShader);
        _mouthTeeth = new MouthTeethFeature("MouthTeeth", _detailLayer!);

        _leftPupil = new PupilFeature("LeftPupil", _detailLayer);
        _rightPupil = new PupilFeature("RightPupil", _detailLayer);

        RebuildGeometry();
        UpdateLighting();
        UpdateShaderTime();
    }

    private Node2D CreateLayer(string name, int zIndex)
    {
        Node2D layer = new()
        {
            Name = name,
            ZIndex = zIndex,
        };
        AddChild(layer);
        return layer;
    }

    private CarvedFeature CreateCarvedFeature(
        string name,
        Vector2 seed,
        Vector2 depthOffset,
        Shader? interiorShader,
        Shader? glowShader)
    {
        CarvedFeature feature = new(
            name,
            seed,
            depthOffset,
            _glowLayer!,
            _edgeLayer!,
            _interiorLayer!,
            _detailLayer!,
            interiorShader,
            glowShader);

        _animatedMaterials.AddRange(feature.Materials);
        _interiorMaterials.Add(feature.InteriorMaterial);
        _glowMaterials.Add(feature.WideGlowMaterial);
        _glowMaterials.Add(feature.TightGlowMaterial);
        return feature;
    }

    private void RebuildGeometry()
    {
        if (!_initialized)
        {
            return;
        }

        float eyeSpacing = 365f * _calibration.EyeSpacing;
        Vector2 leftCenter = new(-eyeSpacing, -145f);
        Vector2 rightCenter = new(eyeSpacing, -137f);

        Vector2[] leftEyePoints = BuildEye(
            leftCenter,
            420f,
            238f,
            _pose.LeftEyelidOpen,
            _pose.LeftBrowTension,
            false);
        Vector2[] rightEyePoints = BuildEye(
            rightCenter,
            405f,
            228f,
            _pose.RightEyelidOpen,
            _pose.RightBrowTension,
            true);

        _leftEye!.SetPolygon(leftEyePoints);
        _rightEye!.SetPolygon(rightEyePoints);

        float noseTension = (_pose.LeftBrowTension + _pose.RightBrowTension) * 0.5f;
        Vector2[] nosePoints =
        [
            new(-52f, -35f - noseTension * 4f),
            new(-14f, -49f - noseTension * 6f),
            new(39f, -28f + noseTension * 2f),
            new(31f, 17f),
            new(4f, 61f),
            new(-29f, 28f),
        ];
        for (int i = 0; i < nosePoints.Length; i++)
        {
            nosePoints[i] += new Vector2(2f, 25f);
        }

        _nose!.SetPolygon(nosePoints);

        Vector2 mouthOffset = new(
            _calibration.MouthOffsetX * DesignSize.X * 0.5f,
            _calibration.MouthOffsetY * DesignSize.Y * 0.5f);
        Vector2 mouthScale = new(_calibration.MouthScaleX, _calibration.MouthScaleY);
        MouthGeometry mouth = BuildMouth(new Vector2(0f, 225f) + mouthOffset, mouthScale);
        _mouth!.SetPolygon(mouth.Outline);
        _mouthTeeth!.SetGeometry(mouth);

        UpdatePupil(
            _leftPupil!,
            leftCenter,
            new Vector2(_pose.LeftGazeX, _pose.LeftGazeY),
            _pose.LeftEyelidOpen,
            false);
        UpdatePupil(
            _rightPupil!,
            rightCenter,
            new Vector2(_pose.RightGazeX, _pose.RightGazeY),
            _pose.RightEyelidOpen,
            true);
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
        // The two sockets are deliberately authored separately. Projection
        // footage looks most convincing when it feels hand-carved rather than
        // like one perfect vector path reflected across the pumpkin.
        Vector2[] normalized = mirrored
            ?
            [
                new(-0.53f, -0.02f),
                new(-0.35f, -0.34f),
                new(-0.07f, -0.47f),
                new(0.24f, -0.51f),
                new(0.46f, -0.27f),
                new(0.53f, 0.11f),
                new(0.31f, 0.31f),
                new(-0.01f, 0.41f),
                new(-0.34f, 0.32f),
            ]
            :
            [
                new(-0.54f, 0.12f),
                new(-0.48f, -0.27f),
                new(-0.27f, -0.51f),
                new(0.03f, -0.46f),
                new(0.32f, -0.29f),
                new(0.52f, -0.04f),
                new(0.34f, 0.30f),
                new(0.06f, 0.42f),
                new(-0.29f, 0.34f),
            ];

        Vector2[] points = new Vector2[normalized.Length];
        for (int i = 0; i < normalized.Length; i++)
        {
            Vector2 source = normalized[i];
            float seam = (mirrored ? -0.015f : 0.018f) + source.X * (mirrored ? -0.055f : 0.045f);
            bool upperEdge = i is >= 1 and <= 4;
            bool lowerEdge = i is >= 5 and <= 8;
            float closedY = seam + (upperEdge ? -0.022f : lowerEdge ? 0.022f : 0f);
            float y = Mathf.Lerp(closedY, source.Y, easedOpen);

            // Brow tension reshapes only the upper cut, like an expressive lid
            // sliding over a fixed socket instead of scaling the entire hole.
            if (upperEdge)
            {
                float innerWeight = mirrored
                    ? Mathf.Clamp(0.62f - source.X, 0f, 1f)
                    : Mathf.Clamp(0.62f + source.X, 0f, 1f);
                y -= tension * (0.035f + innerWeight * 0.075f) * easedOpen;
            }

            points[i] = center + new Vector2(source.X * width, y * fullHeight);
        }

        return points;
    }

    private MouthGeometry BuildMouth(Vector2 center, Vector2 calibrationScale)
    {
        float roundness = Mathf.Clamp(_pose.MouthRoundness, 0f, 1f);
        // Large rounded vowels and gasps should feel like a dropped jaw, not a
        // balloon expanding upward into the nose.
        center.Y += (_pose.JawOpen * 16f + roundness * 27f) * calibrationScale.Y;
        float widthFactor = Mathf.Lerp(1f, 0.62f, roundness);
        float width = Mathf.Lerp(460f, 1120f, _pose.MouthWidth) * widthFactor * calibrationScale.X;
        float height = (125f + _pose.JawOpen * 280f + roundness * 112f) * calibrationScale.Y;
        width = Mathf.Max(width, 150f);
        height = Mathf.Max(height, 68f);

        float leftCorner = -_pose.LeftMouthCorner * 72f * calibrationScale.Y;
        float rightCorner = -_pose.RightMouthCorner * 72f * calibrationScale.Y;
        Vector2[] grin =
        [
            new(-0.51f, 0.01f),
            new(-0.44f, -0.15f),
            new(-0.28f, -0.27f),
            new(-0.08f, -0.23f),
            new(0.10f, -0.26f),
            new(0.29f, -0.21f),
            new(0.45f, -0.12f),
            new(0.51f, 0.02f),
            new(0.45f, 0.25f),
            new(0.31f, 0.42f),
            new(0.13f, 0.49f),
            new(-0.07f, 0.52f),
            new(-0.27f, 0.45f),
            new(-0.44f, 0.27f),
        ];

        Vector2[] outline = new Vector2[grin.Length];
        for (int i = 0; i < grin.Length; i++)
        {
            Vector2 source = grin[i];
            float angle = Mathf.Tau * i / grin.Length + Mathf.Pi;
            Vector2 rounded = new(Mathf.Cos(angle) * 0.48f, Mathf.Sin(angle) * 0.53f + 0.08f);
            Vector2 shape = source.Lerp(rounded, roundness);
            float corner = Mathf.Lerp(leftCorner, rightCorner, shape.X + 0.5f);
            float cornerWeight = Mathf.Pow(Mathf.Clamp(Mathf.Abs(shape.X) * 2f, 0f, 1f), 2.2f);
            shape.Y += corner / height * cornerWeight;

            // Small stable imperfections keep the edge alive without making it
            // shimmer while the mouth moves between phoneme-like shapes.
            float chip = Mathf.Sin(i * 2.43f + 0.7f) * 0.009f + Mathf.Sin(i * 4.17f) * 0.004f;
            outline[i] = center + new Vector2(shape.X * width, (shape.Y + chip) * height);
        }

        return new MouthGeometry(
            outline,
            center,
            width,
            height,
            leftCorner,
            rightCorner,
            roundness,
            _pose.MouthWidth);
    }

    private void UpdatePupil(
        PupilFeature pupil,
        Vector2 eyeCenter,
        Vector2 gaze,
        float eyelidOpen,
        bool mirrored)
    {
        float open = Mathf.Clamp(eyelidOpen, 0f, 1f);
        if (open < 0.10f)
        {
            pupil.Visible = false;
            return;
        }

        pupil.Visible = true;
        gaze = new Vector2(Mathf.Clamp(gaze.X, -1f, 1f), Mathf.Clamp(gaze.Y, -1f, 1f));
        float openHeight = (mirrored ? 228f : 238f) * (0.11f + 0.89f * open);
        float radius = Mathf.Min(72f * _pose.PupilSize, openHeight * 0.34f);
        radius = Mathf.Max(radius, 16f);
        Vector2 travel = new(91f, Mathf.Max(6f, openHeight * 0.16f));
        Vector2 center = eyeCenter + gaze * travel;
        pupil.SetGeometry(center, radius, mirrored);
    }

    private void UpdateShaderTime()
    {
        if (!_initialized)
        {
            return;
        }

        float shaderTime = (float)_animationTime;
        foreach (ShaderMaterial material in _animatedMaterials)
        {
            material.SetShaderParameter("animation_time", shaderTime);
        }
    }

    private void UpdateLighting()
    {
        if (!_initialized)
        {
            return;
        }

        foreach (ShaderMaterial material in _animatedMaterials)
        {
            material.SetShaderParameter("light_intensity", _pose.LightingIntensity);
            material.SetShaderParameter("brightness", _calibration.Brightness);
            material.SetShaderParameter("gamma", _calibration.Gamma);
        }

        foreach (ShaderMaterial material in _interiorMaterials)
        {
            material.SetShaderParameter("ember_color", new Color(1f, 0.30f, 0.008f, 1f));
            material.SetShaderParameter("flame_color", new Color(1.28f, 1.02f, 0.30f, 1f));
            material.SetShaderParameter("hot_color", new Color(1.48f, 1.38f, 0.92f, 1f));
        }

        foreach (ShaderMaterial material in _glowMaterials)
        {
            material.SetShaderParameter("glow_color", new Color(1f, 0.31f, 0.014f, 1f));
        }

        float detailExposure = _pose.LightingIntensity * _calibration.Brightness;
        _leftEye!.SetDetailLighting(detailExposure, _calibration.Gamma);
        _rightEye!.SetDetailLighting(detailExposure, _calibration.Gamma);
        _nose!.SetDetailLighting(detailExposure, _calibration.Gamma);
        _mouth!.SetDetailLighting(detailExposure, _calibration.Gamma);
        _mouthTeeth!.SetDetailLighting(detailExposure, _calibration.Gamma);
        _leftPupil!.SetDetailLighting(detailExposure, _calibration.Gamma);
        _rightPupil!.SetDetailLighting(detailExposure, _calibration.Gamma);
    }

    private static Color ApplyExposure(Color source, float exposure, float gamma)
    {
        float inverseGamma = 1f / Mathf.Max(0.05f, gamma);
        return new Color(
            Mathf.Pow(Mathf.Max(0f, source.R * exposure), inverseGamma),
            Mathf.Pow(Mathf.Max(0f, source.G * exposure), inverseGamma),
            Mathf.Pow(Mathf.Max(0f, source.B * exposure), inverseGamma),
            source.A);
    }

    private sealed class CarvedFeature
    {
        private const int MaximumWallSegments = 20;

        private static readonly Color DeepCavity = new(0.026f, 0.006f, 0.001f, 1f);
        private static readonly Color CoolCutWall = new(0.105f, 0.021f, 0.002f, 1f);
        private static readonly Color WarmCutWall = new(0.60f, 0.205f, 0.018f, 1f);

        private readonly Vector2 _depthOffset;
        private readonly float _contourPhase;
        private readonly Polygon2D _wideGlow;
        private readonly Polygon2D _tightGlow;
        private readonly Polygon2D _cavityBase;
        private readonly Polygon2D _cavityShadow;
        private readonly Polygon2D _interior;
        private readonly Line2D _charredEdge;
        private readonly Line2D _innerShadowEdge;
        private readonly Line2D _emberEdge;
        private readonly Polygon2D[] _wallSegments = new Polygon2D[MaximumWallSegments];
        private readonly float[] _wallWarmth = new float[MaximumWallSegments];
        private int _activeWallSegments;
        private float _detailExposure = 1f;
        private float _detailGamma = 1f;

        public CarvedFeature(
            string name,
            Vector2 seed,
            Vector2 depthOffset,
            Node2D glowLayer,
            Node2D edgeLayer,
            Node2D interiorLayer,
            Node2D detailLayer,
            Shader? interiorShader,
            Shader? glowShader)
        {
            _depthOffset = depthOffset;
            _contourPhase = seed.X * 17.3f + seed.Y * 29.7f;
            InteriorMaterial = CreateMaterial(interiorShader, seed);
            WideGlowMaterial = CreateMaterial(glowShader, seed + new Vector2(0.11f, 0.07f));
            TightGlowMaterial = CreateMaterial(glowShader, seed + new Vector2(0.03f, 0.13f));
            WideGlowMaterial.SetShaderParameter("halo_strength", 0.085f);
            TightGlowMaterial.SetShaderParameter("halo_strength", 0.18f);

            _wideGlow = new Polygon2D
            {
                Name = $"{name}WideGlow",
                Color = Colors.White,
                Material = WideGlowMaterial,
            };
            _tightGlow = new Polygon2D
            {
                Name = $"{name}TightGlow",
                Color = Colors.White,
                Material = TightGlowMaterial,
            };
            _charredEdge = new Line2D
            {
                Name = $"{name}CharredEdge",
                Width = 27f,
                DefaultColor = new Color(0.018f, 0.004f, 0.001f, 0.995f),
                Antialiased = true,
            };
            _cavityBase = new Polygon2D
            {
                Name = $"{name}CavityBase",
                Color = DeepCavity,
            };
            _cavityShadow = new Polygon2D
            {
                Name = $"{name}CavityShadow",
                Color = DeepCavity,
            };
            _interior = new Polygon2D
            {
                Name = $"{name}Interior",
                // Polygon color multiplies the canvas shader output. Keep it
                // white so the pale candle core is not re-tinted flat orange;
                // retain an amber fallback only when the shader is unavailable.
                Color = interiorShader is null
                    ? new Color(1f, 0.55f, 0.08f, 1f)
                    : Colors.White,
                Material = InteriorMaterial,
            };
            _innerShadowEdge = new Line2D
            {
                Name = $"{name}InnerCavityShadow",
                Width = 15f,
                DefaultColor = new Color(0.022f, 0.004f, 0.001f, 0.96f),
                Antialiased = true,
            };
            _emberEdge = new Line2D
            {
                Name = $"{name}EmberEdge",
                Width = 2.1f,
                DefaultColor = new Color(1f, 0.21f, 0.006f, 0.26f),
                Antialiased = true,
            };

            glowLayer.AddChild(_wideGlow);
            glowLayer.AddChild(_tightGlow);
            edgeLayer.AddChild(_cavityBase);
            edgeLayer.AddChild(_charredEdge);

            for (int i = 0; i < _wallSegments.Length; i++)
            {
                Polygon2D wall = new()
                {
                    Name = $"{name}CutWall{i + 1:00}",
                    Color = CoolCutWall,
                    Visible = false,
                };
                _wallSegments[i] = wall;
                interiorLayer.AddChild(wall);
            }

            interiorLayer.AddChild(_cavityShadow);
            interiorLayer.AddChild(_interior);
            detailLayer.AddChild(_innerShadowEdge);
            detailLayer.AddChild(_emberEdge);
        }

        public ShaderMaterial InteriorMaterial { get; }

        public ShaderMaterial WideGlowMaterial { get; }

        public ShaderMaterial TightGlowMaterial { get; }

        public IEnumerable<ShaderMaterial> Materials
        {
            get
            {
                yield return InteriorMaterial;
                yield return WideGlowMaterial;
                yield return TightGlowMaterial;
            }
        }

        public void SetPolygon(Vector2[] points)
        {
            Vector2[] wallInner = TransformIrregular(
                points,
                0.855f,
                _depthOffset * 0.34f,
                _contourPhase,
                0.026f,
                0.16f);
            Vector2[] lightPlane = TransformIrregular(
                points,
                0.685f,
                _depthOffset * 0.68f,
                _contourPhase + 1.7f,
                0.034f,
                0.24f);

            _cavityBase.Polygon = points;
            _charredEdge.Points = Close(points);
            _cavityShadow.Polygon = wallInner;

            _interior.Polygon = lightPlane;
            _interior.UV = BuildUvs(lightPlane);
            _innerShadowEdge.Points = Close(lightPlane);
            _emberEdge.Points = Close(lightPlane);

            _activeWallSegments = Math.Min(points.Length, _wallSegments.Length);
            Vector2 featureCenter = CenterOf(points);
            float halfWidth = Mathf.Max(1f, points.Max(point => point.X) - points.Min(point => point.X)) * 0.5f;
            float halfHeight = Mathf.Max(1f, points.Max(point => point.Y) - points.Min(point => point.Y)) * 0.5f;
            for (int i = 0; i < _wallSegments.Length; i++)
            {
                Polygon2D wall = _wallSegments[i];
                bool active = i < _activeWallSegments;
                wall.Visible = active;
                if (!active)
                {
                    continue;
                }

                int next = (i + 1) % points.Length;
                wall.Polygon =
                [
                    points[i],
                    points[next],
                    wallInner[next],
                    wallInner[i],
                ];

                Vector2 midpoint = (points[i] + points[next]) * 0.5f;
                float normalizedX = (midpoint.X - featureCenter.X) / halfWidth;
                float normalizedY = (midpoint.Y - featureCenter.Y) / halfHeight;
                // The shared virtual candle sits low behind the pumpkin. Upper
                // cut faces catch its amber spill while bottom and far side
                // walls retain irregular soot pockets.
                float sootPocket = Mathf.Sin(i * 2.73f + _contourPhase) * 0.10f;
                _wallWarmth[i] = Mathf.Clamp(
                    0.54f - normalizedY * 0.34f - Mathf.Abs(normalizedX) * 0.08f + sootPocket,
                    0.04f,
                    0.94f);
            }

            ApplyWallLighting();

            Vector2[] tight = TransformIrregular(
                points,
                1.095f,
                new Vector2(0f, 5f),
                _contourPhase + 0.9f,
                0.012f,
                0.08f);
            _tightGlow.Polygon = tight;
            _tightGlow.UV = BuildUvs(tight);

            Vector2[] wide = TransformIrregular(
                points,
                1.245f,
                new Vector2(0f, 13f),
                _contourPhase + 2.4f,
                0.022f,
                0.12f);
            _wideGlow.Polygon = wide;
            _wideGlow.UV = BuildUvs(wide);
        }

        public void SetDetailLighting(float exposure, float gamma)
        {
            _detailExposure = exposure;
            _detailGamma = gamma;
            ApplyWallLighting();
            _emberEdge.DefaultColor = ApplyExposure(
                new Color(1f, 0.21f, 0.006f, 0.26f),
                exposure,
                gamma);
        }

        private void ApplyWallLighting()
        {
            float wallExposure = 0.44f + Mathf.Clamp(_detailExposure, 0f, 1.6f) * 0.44f;
            for (int i = 0; i < _activeWallSegments; i++)
            {
                Color baseColor = CoolCutWall.Lerp(WarmCutWall, _wallWarmth[i]);
                _wallSegments[i].Color = ApplyExposure(baseColor, wallExposure, _detailGamma);
            }
        }

        private static ShaderMaterial CreateMaterial(Shader? shader, Vector2 seed)
        {
            ShaderMaterial material = new()
            {
                Shader = shader,
            };
            material.SetShaderParameter("feature_seed", seed);
            return material;
        }

        private static Vector2[] Close(Vector2[] points)
        {
            Vector2[] closed = new Vector2[points.Length + 1];
            Array.Copy(points, closed, points.Length);
            closed[^1] = points[0];
            return closed;
        }

        private static Vector2[] TransformIrregular(
            Vector2[] points,
            float factor,
            Vector2 offset,
            float phase,
            float radialVariation,
            float depthVariation)
        {
            Vector2 center = CenterOf(points);
            Vector2[] result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float radialJitter = Mathf.Sin(i * 2.11f + phase) * radialVariation;
                radialJitter += Mathf.Sin(i * 4.07f - phase * 0.63f) * radialVariation * 0.34f;
                float depthJitter = 1f + Mathf.Sin(i * 1.77f + phase * 1.31f) * depthVariation;
                result[i] = center + (points[i] - center) * (factor + radialJitter) + offset * depthJitter;
            }

            return result;
        }

        private static Vector2 CenterOf(Vector2[] points)
        {
            Vector2 center = Vector2.Zero;
            foreach (Vector2 point in points)
            {
                center += point;
            }

            return center / points.Length;
        }

        private static Vector2[] BuildUvs(Vector2[] points)
        {
            float minimumX = points.Min(point => point.X);
            float minimumY = points.Min(point => point.Y);
            float maximumX = points.Max(point => point.X);
            float maximumY = points.Max(point => point.Y);
            float width = Mathf.Max(1f, maximumX - minimumX);
            float height = Mathf.Max(1f, maximumY - minimumY);

            Vector2[] result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new Vector2(
                    (points[i].X - minimumX) / width,
                    (points[i].Y - minimumY) / height);
            }

            return result;
        }
    }

    private readonly record struct MouthGeometry(
        Vector2[] Outline,
        Vector2 Center,
        float Width,
        float Height,
        float LeftCorner,
        float RightCorner,
        float Roundness,
        float WidthControl);

    /// <summary>
    /// Pumpkin-material bridges left inside the mouth opening. Their chunky,
    /// irregular silhouettes read as traditional teeth while the warm side rims
    /// make them feel cut from the same physical shell as the outer aperture.
    /// </summary>
    private sealed class MouthTeethFeature
    {
        private const int ToothCount = 3;

        private readonly Polygon2D[] _faces = new Polygon2D[ToothCount];
        private readonly Line2D[] _charredRims = new Line2D[ToothCount];
        private readonly Line2D[] _fleshRims = new Line2D[ToothCount];

        public MouthTeethFeature(string name, Node2D layer)
        {
            for (int i = 0; i < ToothCount; i++)
            {
                _faces[i] = new Polygon2D
                {
                    Name = $"{name}{i + 1}Shadow",
                    Color = new Color(0.012f, 0.005f, 0.002f, 1f),
                };
                _charredRims[i] = new Line2D
                {
                    Name = $"{name}{i + 1}Char",
                    Width = 7.5f,
                    DefaultColor = new Color(0.055f, 0.010f, 0.001f, 0.99f),
                    Antialiased = true,
                };
                _fleshRims[i] = new Line2D
                {
                    Name = $"{name}{i + 1}Flesh",
                    Width = 2.2f,
                    DefaultColor = new Color(0.72f, 0.14f, 0.006f, 0.64f),
                    Antialiased = true,
                };

                layer.AddChild(_faces[i]);
                layer.AddChild(_charredRims[i]);
                layer.AddChild(_fleshRims[i]);
            }
        }

        public void SetGeometry(MouthGeometry mouth)
        {
            // Rounded O-shapes are gasps/yawns rather than grins, so their
            // tooth bridges recede early instead of becoming little fangs.
            float presence = Mathf.Clamp((0.58f - mouth.Roundness) / 0.18f, 0f, 1f);
            presence *= Mathf.Clamp((mouth.WidthControl - 0.14f) / 0.20f, 0f, 1f);
            bool visible = presence > 0.04f;
            for (int i = 0; i < ToothCount; i++)
            {
                _faces[i].Visible = visible;
                _charredRims[i].Visible = visible;
                _fleshRims[i].Visible = visible;
            }

            if (!visible)
            {
                return;
            }

            float upperDepth = Mathf.Clamp(mouth.Height * 0.31f, 23f, 59f) * presence;
            float lowerDepth = Mathf.Clamp(mouth.Height * 0.27f, 20f, 51f) * presence;

            SetUpperTooth(0, mouth, -0.235f, 0.104f, upperDepth, 0.82f);
            SetUpperTooth(1, mouth, 0.255f, 0.113f, upperDepth * 0.84f, 1.12f);
            SetLowerTooth(2, mouth, 0.035f, 0.105f, lowerDepth, 0.91f);
        }

        public void SetDetailLighting(float exposure, float gamma)
        {
            float rimExposure = 0.48f + Mathf.Clamp(exposure, 0f, 1.7f) * 0.48f;
            Color charred = ApplyExposure(new Color(0.055f, 0.010f, 0.001f, 0.99f), rimExposure, gamma);
            Color flesh = ApplyExposure(new Color(0.72f, 0.14f, 0.006f, 0.64f), rimExposure, gamma);
            foreach (Line2D rim in _charredRims)
            {
                rim.DefaultColor = charred;
            }

            foreach (Line2D rim in _fleshRims)
            {
                rim.DefaultColor = flesh;
            }
        }

        private void SetUpperTooth(
            int index,
            MouthGeometry mouth,
            float normalizedX,
            float widthFraction,
            float depth,
            float skew)
        {
            float x = mouth.Center.X + mouth.Width * normalizedX;
            float halfWidth = mouth.Width * widthFraction * 0.5f;
            float top = TopEdge(mouth, normalizedX);
            Vector2[] points =
            [
                new(x - halfWidth, top - 4f),
                new(x + halfWidth, top - 4f),
                new(x + halfWidth * 0.82f, top + depth * 0.72f),
                new(x + halfWidth * 0.47f * skew, top + depth),
                new(x - halfWidth * 0.50f, top + depth * 0.91f),
                new(x - halfWidth * 0.84f, top + depth * 0.68f),
            ];
            Vector2[] rim =
            [
                points[0],
                points[5],
                points[4],
                points[3],
                points[2],
                points[1],
            ];
            SetTooth(index, points, rim);
        }

        private void SetLowerTooth(
            int index,
            MouthGeometry mouth,
            float normalizedX,
            float widthFraction,
            float depth,
            float skew)
        {
            float x = mouth.Center.X + mouth.Width * normalizedX;
            float halfWidth = mouth.Width * widthFraction * 0.5f;
            float bottom = BottomEdge(mouth, normalizedX);
            Vector2[] points =
            [
                new(x - halfWidth, bottom + 4f),
                new(x + halfWidth, bottom + 4f),
                new(x + halfWidth * 0.82f, bottom - depth * 0.67f),
                new(x + halfWidth * 0.42f, bottom - depth * 0.94f),
                new(x - halfWidth * 0.46f * skew, bottom - depth),
                new(x - halfWidth * 0.84f, bottom - depth * 0.70f),
            ];
            Vector2[] rim =
            [
                points[0],
                points[5],
                points[4],
                points[3],
                points[2],
                points[1],
            ];
            SetTooth(index, points, rim);
        }

        private void SetTooth(int index, Vector2[] face, Vector2[] rim)
        {
            _faces[index].Polygon = face;
            _charredRims[index].Points = rim;
            _fleshRims[index].Points = rim;
        }

        private static float TopEdge(MouthGeometry mouth, float normalizedX)
        {
            float corner = Mathf.Lerp(
                mouth.LeftCorner,
                mouth.RightCorner,
                normalizedX * 0.5f + 0.5f);
            float arch = -0.22f - 0.075f * (1f - Mathf.Abs(normalizedX));
            return mouth.Center.Y + mouth.Height * arch + corner * 0.68f;
        }

        private static float BottomEdge(MouthGeometry mouth, float normalizedX)
        {
            float corner = Mathf.Lerp(
                mouth.LeftCorner,
                mouth.RightCorner,
                normalizedX * 0.5f + 0.5f);
            float bowl = 0.43f + 0.075f * (1f - Mathf.Abs(normalizedX));
            return mouth.Center.Y + mouth.Height * bowl + corner * 0.30f;
        }
    }

    private sealed class PupilFeature
    {
        private readonly Polygon2D _shadow;
        private readonly Line2D _emberRim;
        private readonly Polygon2D _reflection;

        public PupilFeature(string name, Node2D layer)
        {
            _shadow = new Polygon2D
            {
                Name = $"{name}Shadow",
                Color = new Color(0.006f, 0.003f, 0.001f, 0.995f),
            };
            _emberRim = new Line2D
            {
                Name = $"{name}Rim",
                Width = 2.4f,
                DefaultColor = new Color(0.22f, 0.042f, 0.002f, 0.78f),
                Antialiased = true,
            };
            _reflection = new Polygon2D
            {
                Name = $"{name}Reflection",
                Color = Colors.Transparent,
                Visible = false,
            };

            layer.AddChild(_shadow);
            layer.AddChild(_emberRim);
            layer.AddChild(_reflection);
        }

        public bool Visible
        {
            set
            {
                _shadow.Visible = value;
                _emberRim.Visible = value;
                // A glossy catchlight made the projected cut read like a
                // cartoon eyeball. Atmosphere now comes from a deep, irregular
                // pupil silhouette and a faint ember rim instead.
                _reflection.Visible = false;
            }
        }

        public void SetGeometry(Vector2 center, float radius, bool mirrored)
        {
            Vector2[] pupil = BuildOrganicEllipse(
                center,
                new Vector2(radius * (mirrored ? 0.69f : 0.73f), radius * 1.08f),
                24,
                mirrored ? 1.7f : 0.2f);
            _shadow.Polygon = pupil;
            _emberRim.Points = Close(pupil);
        }

        public void SetDetailLighting(float exposure, float gamma)
        {
            _emberRim.DefaultColor = ApplyExposure(
                new Color(0.22f, 0.042f, 0.002f, 0.78f),
                exposure,
                gamma);
        }

        private static Vector2[] BuildOrganicEllipse(
            Vector2 center,
            Vector2 radii,
            int segments,
            float phase)
        {
            Vector2[] points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.Tau * i / segments;
                float wobble = 1f + Mathf.Sin(angle * 3f + phase) * 0.035f + Mathf.Sin(angle * 5f - phase) * 0.018f;
                points[i] = center + new Vector2(
                    Mathf.Cos(angle) * radii.X,
                    Mathf.Sin(angle) * radii.Y) * wobble;
            }

            return points;
        }

        private static Vector2[] Close(Vector2[] points)
        {
            Vector2[] closed = new Vector2[points.Length + 1];
            Array.Copy(points, closed, points.Length);
            closed[^1] = points[0];
            return closed;
        }
    }
}
