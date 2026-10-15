using CnCNew.Simulation.Skirmish;

namespace CnCNew.Simulation.Testing;

/// <summary>How the built-in AI of one slot of a tool's or a test's match plays: its composition rule, its tactics profile and its strength. The default is the AI as it was.</summary>
public sealed record AiSlotChoice(ArmyComposition? Composition = null, AiTactics? Tactics = null, AiStrength Strength = AiStrength.Medium)
{
    public static AiSlotChoice Default { get; } = new();

    /// <summary>The choice of slot <paramref name="index"/>, or <see cref="Default"/> when the list names none for it.</summary>
    public static AiSlotChoice At(IReadOnlyList<AiSlotChoice>? choices, int index) => choices is not null && index < choices.Count ? choices[index] : Default;

    public SkirmishSlot Slot(MatchParticipant participant) => new(participant, Composition: Composition, Tactics: Tactics, Strength: Strength);

    /// <summary><c>balanced</c>, <c>rush</c>, <c>turtle</c>, <c>eco</c>, or <c>default</c>/<c>none</c> for no profile.</summary>
    public static AiTactics? ParseTactics(string text) => text.ToLowerInvariant() switch
    {
        "default" or "none" => null,
        "balanced" => AiTactics.Balanced,
        "rush" => AiTactics.Rush,
        "turtle" or "bunker" => AiTactics.Turtle,
        "eco" => AiTactics.Eco,
        _ => throw new ArgumentException($"Unknown tactics profile '{text}' (balanced, rush, turtle, eco, default)."),
    };

    /// <summary><c>easy</c>, <c>medium</c> (also <c>normal</c>, <c>default</c>), <c>hard</c>.</summary>
    public static AiStrength ParseStrength(string text) => text.ToLowerInvariant() switch
    {
        "easy" => AiStrength.Easy,
        "medium" or "normal" or "default" => AiStrength.Medium,
        "hard" => AiStrength.Hard,
        _ => throw new ArgumentException($"Unknown AI strength '{text}' (easy, medium, hard)."),
    };

    /// <summary>The name the tools print: <c>rush/hard</c>, <c>default/medium</c>.</summary>
    public override string ToString()
        => (Tactics switch { AiTactics.Balanced => "balanced", AiTactics.Rush => "rush", AiTactics.Turtle => "turtle", AiTactics.Eco => "eco", _ => "default" }) + "/"
           + (Strength switch { AiStrength.Easy => "easy", AiStrength.Hard => "hard", _ => "medium" })
           + (Composition switch { ArmyComposition.Uniform => "/uniform", ArmyComposition.ExchangeValue => "/exchange", _ => string.Empty });
}
