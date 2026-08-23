namespace PumpkinFace.Core;

/// <summary>
/// Converts written text into a compact, deterministic viseme sequence. The
/// sequence is stretched over the measured audio duration by the caller.
/// </summary>
public static class SpeechPhrasePlanner
{
    public static IReadOnlyList<VisemeFrame> Plan(string phrase, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        List<(Viseme Shape, double Weight)> shapes = [(Viseme.Silence, 0.18d)];
        string text = phrase.ToLowerInvariant();
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            char next = index + 1 < text.Length ? text[index + 1] : '\0';
            (Viseme Shape, double Weight) cue;

            if (char.IsWhiteSpace(current))
            {
                cue = (Viseme.Silence, 0.34d);
            }
            else if (char.IsPunctuation(current))
            {
                cue = (Viseme.Silence, current is '.' or '!' or '?' ? 0.72d : 0.42d);
            }
            else if (current == 't' && next == 'h')
            {
                cue = (Viseme.Th, 0.62d);
                index++;
            }
            else if ((current == 's' || current == 'c') && next == 'h')
            {
                cue = (Viseme.ChJSh, 0.72d);
                index++;
            }
            else if (current == 'o' && next == 'o')
            {
                cue = (Viseme.Ooh, 1.45d);
                index++;
            }
            else if ((current == 'e' && next == 'e') || (current == 'e' && next == 'a'))
            {
                cue = (Viseme.Ih, 1.40d);
                index++;
            }
            else if (current == 'p' && next == 'h')
            {
                cue = (Viseme.Fv, 0.68d);
                index++;
            }
            else
            {
                cue = MapCharacter(current);
            }

            AddOrExtend(shapes, cue);
        }

        AddOrExtend(shapes, (Viseme.Silence, 0.38d));
        double totalWeight = shapes.Sum(cue => cue.Weight);
        double elapsedWeight = 0d;
        List<VisemeFrame> frames = new(shapes.Count + 1);
        foreach ((Viseme shape, double weight) in shapes)
        {
            frames.Add(new VisemeFrame(
                TimeSpan.FromTicks((long)(duration.Ticks * elapsedWeight / totalWeight)),
                shape,
                1f));
            elapsedWeight += weight;
        }

        frames.Add(new VisemeFrame(duration, Viseme.Silence, 1f));
        return frames;
    }

    private static (Viseme Shape, double Weight) MapCharacter(char value) => value switch
    {
        'a' => (Viseme.Ah, 1.25d),
        'e' => (Viseme.Eh, 1.10d),
        'i' or 'y' => (Viseme.Ih, 1.15d),
        'o' => (Viseme.Oh, 1.30d),
        'u' => (Viseme.Ooh, 1.20d),
        'f' or 'v' => (Viseme.Fv, 0.62d),
        'l' => (Viseme.L, 0.60d),
        'm' or 'b' or 'p' => (Viseme.Mbp, 0.55d),
        'w' or 'q' => (Viseme.Wq, 0.65d),
        'd' or 'n' or 's' or 't' or 'z' or 'r' => (Viseme.DnSt, 0.55d),
        'k' or 'g' or 'c' or 'x' => (Viseme.Kg, 0.62d),
        'j' => (Viseme.ChJSh, 0.70d),
        'h' => (Viseme.Neutral, 0.42d),
        _ => (Viseme.Neutral, 0.38d),
    };

    private static void AddOrExtend(
        List<(Viseme Shape, double Weight)> shapes,
        (Viseme Shape, double Weight) cue)
    {
        if (shapes.Count > 0 && shapes[^1].Shape == cue.Shape)
        {
            (Viseme shape, double weight) = shapes[^1];
            shapes[^1] = (shape, weight + cue.Weight);
        }
        else
        {
            shapes.Add(cue);
        }
    }
}
