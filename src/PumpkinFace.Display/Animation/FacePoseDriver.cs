using Godot;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Animation;

/// <summary>
/// Godot-animatable property surface for the engine-independent FacePose.
/// Animation tracks target these properties; the renderer consumes snapshots.
/// </summary>
public sealed partial class FacePoseDriver : Node
{
	[Export] public float LeftGazeX { get; set; }
	[Export] public float LeftGazeY { get; set; }
	[Export] public float RightGazeX { get; set; }
	[Export] public float RightGazeY { get; set; }
	[Export] public float PupilSize { get; set; } = FacePose.Neutral.PupilSize;
	[Export] public float LeftEyelidOpen { get; set; } = FacePose.Neutral.LeftEyelidOpen;
	[Export] public float RightEyelidOpen { get; set; } = FacePose.Neutral.RightEyelidOpen;
	[Export] public float LeftBrowTension { get; set; }
	[Export] public float RightBrowTension { get; set; }
	[Export] public float JawOpen { get; set; } = 0.18f;
	[Export] public float MouthWidth { get; set; } = FacePose.Neutral.MouthWidth;
	[Export] public float MouthRoundness { get; set; } = 0.15f;
	[Export] public float LeftMouthCorner { get; set; }
	[Export] public float RightMouthCorner { get; set; }
	[Export] public float Tremble { get; set; }
	[Export] public float LightingIntensity { get; set; } = FacePose.Neutral.LightingIntensity;

	public void ApplyPose(FacePose pose)
	{
		FacePose value = pose.Clamp();
		LeftGazeX = value.LeftGazeX;
		LeftGazeY = value.LeftGazeY;
		RightGazeX = value.RightGazeX;
		RightGazeY = value.RightGazeY;
		PupilSize = value.PupilSize;
		LeftEyelidOpen = value.LeftEyelidOpen;
		RightEyelidOpen = value.RightEyelidOpen;
		LeftBrowTension = value.LeftBrowTension;
		RightBrowTension = value.RightBrowTension;
		JawOpen = value.JawOpen;
		MouthWidth = value.MouthWidth;
		MouthRoundness = value.MouthRoundness;
		LeftMouthCorner = value.LeftMouthCorner;
		RightMouthCorner = value.RightMouthCorner;
		Tremble = value.Tremble;
		LightingIntensity = value.LightingIntensity;
	}

	public FacePose ToPose() => new FacePose
	{
		LeftGazeX = LeftGazeX,
		LeftGazeY = LeftGazeY,
		RightGazeX = RightGazeX,
		RightGazeY = RightGazeY,
		PupilSize = PupilSize,
		LeftEyelidOpen = LeftEyelidOpen,
		RightEyelidOpen = RightEyelidOpen,
		LeftBrowTension = LeftBrowTension,
		RightBrowTension = RightBrowTension,
		JawOpen = JawOpen,
		MouthWidth = MouthWidth,
		MouthRoundness = MouthRoundness,
		LeftMouthCorner = LeftMouthCorner,
		RightMouthCorner = RightMouthCorner,
		Tremble = Tremble,
		LightingIntensity = LightingIntensity,
	}.Clamp();
}
