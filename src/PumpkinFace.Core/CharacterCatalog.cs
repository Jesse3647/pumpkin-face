namespace PumpkinFace.Core;

/// <summary>Acting preferences affect autonomous behavior, never explicit operator commands.</summary>
public sealed record CharacterPersonality(
    string Brief, float AttentionRange, double HoldScale, double BlinkIntervalScale,
    double ReactionIntervalScale, float ReactionIntensity, float DelightChance);

public sealed record CharacterDefinition(
    string Id, string Name, string Tagline, string Description,
    float DefaultEmotionAmount, CharacterPersonality Personality);

/// <summary>Stable character IDs shared by selection, persistence, rendering, and future model adapters.</summary>
public static class CharacterCatalog
{
    public const string DefaultId = "jack";
    public const string PipId = "pip";

    public static IReadOnlyList<CharacterDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new CharacterDefinition(DefaultId, "Jack", "The playful pumpkin",
            "A familiar crooked grin and a mischievous streak. Quick to notice things, with curious tilts and lively little acknowledgments.", .65f,
            new CharacterPersonality("Playful, curious, and gently mischievous. Welcome visitors warmly; keep teasing friendly and responses short.",
                1f, 1, 1, 1, .28f, 0)),
        new CharacterDefinition(PipId, "Pip", "The woodland daydreamer",
            "Big eyes, a button nose, and a lopsided smile. Gentle and easily enchanted, Pip lingers on a look and occasionally lights up with delight.", .8f,
            new CharacterPersonality("Gentle, observant, and quietly whimsical. Notice small wonders, ask simple curious questions, and leave room for thoughtful pauses.",
                .72f, 1.35, 1.2, 1.15, .42f, .4f)),
    });

    public static bool IsKnown(string? id) => All.Any(character => character.Id == id);
    public static CharacterDefinition Get(string? id) => All.FirstOrDefault(character => character.Id == id) ?? All[0];
}

public sealed record SelectCharacterCommand(string CharacterId) : AnimationCommand;
