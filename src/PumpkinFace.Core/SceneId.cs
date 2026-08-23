namespace PumpkinFace.Core;

/// <summary>
/// The built-in emotional expressions available to the projector.
/// </summary>
public enum EmotionId
{
    Frightened,
    Happy,
    Sad,
}

/// <summary>
/// Actions that can play independently over the selected emotion.
/// </summary>
public enum SceneId
{
    Looking,
    Blinking,
    Talking,
    CandleSputter,
}
