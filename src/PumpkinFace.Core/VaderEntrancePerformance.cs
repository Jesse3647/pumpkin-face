namespace PumpkinFace.Core;

/// <summary>Restrained mask-like acting, synchronized to the recorded respirator effect.</summary>
public static class VaderEntrancePerformance
{
    public const double Duration = 13.5;
    public const double SaberIgnition = 3.25;
    public const double SaberShutdown = 10.4;

    public static FacePose Sample(double time, FacePose expression, float motion = .65f)
    {
        if (!double.IsFinite(time) || time <= 0 || time >= Duration) return expression.Clamp();
        float breath = BreathEnergy(time);
        float approach = Smooth((time - .3) / 1.5);
        float turn = Smooth((time - 3.4) / 1.2);
        float nod = Smooth((time - 9.1) / .65) * (1 - Smooth((time - 9.95) / 1.1));
        float blink = Smooth((time - 11.5) / .18) * (1 - Smooth((time - 11.85) / .4));
        float ignition = Smooth((time - SaberIgnition) / .32);
        float shutdown = 1 - Smooth((time - SaberShutdown) / .55);
        float ignitionFlash = Smooth((time - SaberIgnition) / .07) *
            (1 - Smooth((time - SaberIgnition - .10) / .32));
        float hum = .018f * (float)Math.Sin(time * 19.0) + .012f * (float)Math.Sin(time * 31.0);
        float saberGlow = Math.Clamp((ignition * (.78f + hum) + .22f * ignitionFlash) * shutdown, 0, 1);
        FacePose stern = expression with {
            JawOpen = 0, MouthWidth = .48f, MouthRoundness = 0, SpeechBlend = 0,
            LeftMouthCorner = -.45f, RightMouthCorner = -.45f,
        };
        FacePose pose = PoseCompositor.Compose(stern, new FacePose {
            LeftEyelidOpen = -Math.Max(.40f + nod * .10f, blink),
            RightEyelidOpen = -Math.Max(.36f + nod * .10f, blink),
            LeftBrowTension = .52f, RightBrowTension = .46f,
            PupilSize = -.14f,
            LeftGazeX = -.30f * (1 - turn), RightGazeX = -.30f * (1 - turn),
            LeftGazeY = -.10f * (1 - approach), RightGazeY = -.10f * (1 - approach),
            MotionRoll = -.13f * (1 - turn),
            MotionY = -.025f * approach - .025f * breath + .065f * nod,
        }, motion) with {
            LightingIntensity = expression.LightingIntensity * (.68f + breath * .10f) * (1 - .48f * saberGlow),
            SaberGlow = saberGlow,
            SaberSweep = -.35f + .75f * Smooth((time - 5.0) / 2.4) - .30f * Smooth((time - 8.3) / 1.5),
        };
        float weight = Smooth(time / 1.1) * Smooth((Duration - time) / 1.0);
        return FacePose.Lerp(expression, pose, weight).Clamp();
    }

    private static float BreathEnergy(double time)
    {
        double sample = Math.Clamp(time * VaderBreathTrack.SamplesPerSecond, 0, VaderBreathTrack.Energy.Length - 1);
        int index = (int)sample;
        float a = VaderBreathTrack.Energy[index];
        float b = VaderBreathTrack.Energy[Math.Min(index + 1, VaderBreathTrack.Energy.Length - 1)];
        return a + (b - a) * (float)(sample - index);
    }

    private static float Smooth(double value)
    {
        float t = (float)Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
