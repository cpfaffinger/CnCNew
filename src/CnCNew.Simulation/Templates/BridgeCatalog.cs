using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Templates;

/// <summary>The model and the texture a terrain bridge is drawn with in one damage state (<c>W3DBridge::load</c>); empty when the block names none.</summary>
public sealed record BridgeVisual(string Model, string Texture);

/// <summary>The effects of one change into a damage state (<c>TransitionToOCL</c>, <c>TransitionToFX</c>): per <c>EffectNum</c> a creation list and an FX list, empty when the block names none.</summary>
public sealed record BridgeTransition(ImmutableArray<string> CreationLists, ImmutableArray<string> Effects);

/// <summary>
/// One <c>Bridge</c> block of <c>Roads.ini</c> (<c>TerrainRoadType</c> with <c>m_isBridge</c>). The arrays are indexed by <c>BodyDamageType</c> (<see cref="Visuals"/>, <see cref="DamagedTo"/>,
/// <see cref="RepairedTo"/>) and by <c>BridgeTowerType</c> (<see cref="Towers"/>: from left, from right, to left, to right). A landmark bridge's block names no model: the object is drawn by
/// its own template.
/// </summary>
public sealed record BridgeTemplate(
    string Name,
    Fixed Scale,
    ColorValue RadarColor,
    Fixed TransitionEffectsHeight,
    int EffectsPerType,
    ImmutableArray<BridgeVisual> Visuals,
    ImmutableArray<string> Towers,
    string ScaffoldObject,
    string ScaffoldSupportObject,
    string DamagedToSound,
    string RepairedToSound,
    ImmutableArray<BridgeTransition> DamagedTo,
    ImmutableArray<BridgeTransition> RepairedTo);

/// <summary><c>TerrainRoadCollection</c>'s bridge list: names compare as in <c>findBridge</c>, exactly.</summary>
public sealed class BridgeCatalog
{
    /// <summary><c>MAX_BRIDGE_BODY_FX</c>.</summary>
    public const int MaxBodyEffects = 3;

    private const int DamageStates = 4;
    private static readonly string[] DamageStateNames = ["PRISTINE", "DAMAGED", "REALLYDAMAGED", "RUBBLE"];
    private readonly ImmutableDictionary<string, BridgeTemplate> _byName;

    private BridgeCatalog(ImmutableArray<BridgeTemplate> bridges)
    {
        Bridges = bridges;
        _byName = bridges.ToImmutableDictionary(value => value.Name, StringComparer.Ordinal);
    }

    public ImmutableArray<BridgeTemplate> Bridges { get; }

    public bool TryGet(string name, out BridgeTemplate bridge) => _byName.TryGetValue(name, out bridge!);

    public static BridgeCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var bridges = ImmutableArray.CreateBuilder<BridgeTemplate>();
        foreach (var definition in graph.Definitions.OfType<BridgeDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
            bridges.Add(Read(definition));
        return new BridgeCatalog(bridges.ToImmutable());
    }

    private static BridgeTemplate Read(BridgeDefinition definition)
    {
        DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        string Text(string name) => Field(name)?.Value.Value as string ?? string.Empty;
        string Object(string name) => Field(name)?.Value.Value is DefinitionReference { IsNone: false } reference ? reference.Name : string.Empty;

        var damagedOcl = Table();
        var damagedFx = Table();
        var repairedOcl = Table();
        var repairedFx = Table();
        foreach (var field in definition.Fields)
        {
            var fx = field.Name.Equals("TransitionToFX", StringComparison.OrdinalIgnoreCase);
            if (!fx && !field.Name.Equals("TransitionToOCL", StringComparison.OrdinalIgnoreCase)) continue;
            var (damage, state, effect, name) = Transition(definition, field, fx ? "FX" : "OCL");
            (fx ? damage ? damagedFx : repairedFx : damage ? damagedOcl : repairedOcl)[state][effect] = name;
        }

        return new BridgeTemplate(definition.OriginalName,
            // TerrainRoadType::TerrainRoadType: m_bridgeScale = 1.0f, no colour, no effects.
            Field("BridgeScale")?.Value.Value is decimal scale ? Fixed.FromDecimal(scale) : Fixed.One,
            Field("RadarColor")?.Value.Value is ColorValue color ? color : default,
            Field("TransitionEffectsHeight")?.Value.Value is decimal height ? Fixed.FromDecimal(height) : Fixed.Zero,
            Field("NumFXPerType")?.Value.Value is long count ? checked((int)count) : 0,
            [new BridgeVisual(Text("BridgeModelName"), Text("Texture")), new BridgeVisual(Text("BridgeModelNameDamaged"), Text("TextureDamaged")),
                new BridgeVisual(Text("BridgeModelNameReallyDamaged"), Text("TextureReallyDamaged")), new BridgeVisual(Text("BridgeModelNameBroken"), Text("TextureBroken"))],
            [Object("TowerObjectNameFromLeft"), Object("TowerObjectNameFromRight"), Object("TowerObjectNameToLeft"), Object("TowerObjectNameToRight")],
            Object("ScaffoldObjectName"), Object("ScaffoldSupportObjectName"), Text("DamagedToSound"), Text("RepairedToSound"),
            Transitions(damagedOcl, damagedFx), Transitions(repairedOcl, repairedFx));
    }

    private static string[][] Table()
    {
        var table = new string[DamageStates][];
        for (var state = 0; state < DamageStates; state++)
        {
            table[state] = new string[MaxBodyEffects];
            Array.Fill(table[state], string.Empty);
        }
        return table;
    }

    private static ImmutableArray<BridgeTransition> Transitions(string[][] lists, string[][] effects)
    {
        var transitions = ImmutableArray.CreateBuilder<BridgeTransition>(DamageStates);
        for (var state = 0; state < DamageStates; state++) transitions.Add(new BridgeTransition([..lists[state]], [..effects[state]]));
        return transitions.MoveToImmutable();
    }

    /// <summary><c>parseTransitionToOCL</c>, <c>parseTransitionToFX</c>: <c>Transition:&lt;Damage|Repair&gt; ToState:&lt;state&gt; EffectNum:&lt;n&gt; &lt;label&gt;:&lt;name&gt;</c>, colons and blanks both separating tokens, the number from one.</summary>
    private static (bool Damage, int State, int Effect, string Name) Transition(BridgeDefinition definition, DefinitionField field, string label)
    {
        var tokens = field.Value.OriginalText.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
        InvalidDataException Bad() => new($"{field.Location.Source}({field.Location.Line}): Bridge '{definition.OriginalName}' {field.Name} expects 'Transition:<Damage|Repair> ToState:<state> EffectNum:<n> {label}:<name>', got '{field.Value.OriginalText.Trim()}'.");
        if (tokens.Length != 8 || !tokens[0].Equals("Transition", StringComparison.OrdinalIgnoreCase) || !tokens[2].Equals("ToState", StringComparison.OrdinalIgnoreCase)
            || !tokens[4].Equals("EffectNum", StringComparison.OrdinalIgnoreCase) || !tokens[6].Equals(label, StringComparison.OrdinalIgnoreCase)) throw Bad();
        var damage = tokens[1].Equals("Damage", StringComparison.OrdinalIgnoreCase);
        if (!damage && !tokens[1].Equals("Repair", StringComparison.OrdinalIgnoreCase)) throw Bad();
        var state = Array.FindIndex(DamageStateNames, name => name.Equals(tokens[3], StringComparison.OrdinalIgnoreCase));
        if (state < 0 || !int.TryParse(tokens[5], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var effect) || effect < 1 || effect > MaxBodyEffects) throw Bad();
        return (damage, state, effect - 1, tokens[7]);
    }
}
