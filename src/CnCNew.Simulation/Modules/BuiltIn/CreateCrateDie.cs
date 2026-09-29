using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

public sealed record CreateCrateDieData(DieMuxData Mux, ImmutableArray<string> CrateNames) : DieData(Mux)
{
    public static CreateCrateDieData Parse(ModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        [..configuration.Fields.Where(field => field.Name.Equals("CrateData", StringComparison.OrdinalIgnoreCase))
            .SelectMany(field => field.Value.Value switch
            {
                DefinitionReference { IsNone: true } => [],
                DefinitionReference reference => [reference.Name],
                ImmutableArray<DefinitionReference> references => references.Where(value => !value.IsNone).Select(value => value.Name),
                _ => throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): CrateData has an unexpected value kind {field.Value.Kind}."),
            })]);
}

/// <summary>
/// <c>CreateCrateDie</c> (<c>CreateCrateDie.cpp onDie/createCrate/testCreationChance/testVeterancyLevel/testKillerType/testKillerScience</c>).
/// Random draws follow the reference exactly: per <c>CrateData</c> one creation-chance draw, and only after all conditions pass one
/// weighted-pick draw and (when a spot exists) one orientation draw. Differences, all documented in <c>docs/GAME-RULES.md</c>:
/// no terrain/partition model yet, so the crate appears at the victim's position (<c>findPositionAround</c> is phase 6); crate
/// objects whose modules are not implemented (all shipped crates use <c>*CrateCollide</c>) are not spawned, but the request is still
/// reported through <see cref="SimEventKind.CrateRequested"/> so it is never silently lost; the AI <c>notifyCrate</c> hook has no AI yet.
/// </summary>
public sealed class CreateCrateDie(CreateCrateDieData data) : DieModuleBase(data.Mux)
{
    private static readonly Fixed TwoPi = Fixed.FromDecimal(6.283185307m);

    public override string ModuleName => "CreateCrateDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info)) return;
        var killer = world.FindEntity(info.Source);
        if (killer is not null && world.RelationshipOf(killer.Owner, Entity.Owner) == Relationship.Allies) return; // no crate for killing an ally.

        foreach (var name in data.CrateNames)
        {
            if (!world.Crates.TryGet(name, out var crate)) continue; // CrateSystem::findCrateTemplate returns null: the entry is skipped.
            if (!(world.LogicRandom.Range(Fixed.Zero, Fixed.One) < crate.CreationChance)) continue;
            if (crate.VeterancyLevel >= 0 && crate.VeterancyLevel != Entity.VeterancyLevel) continue;
            if (crate.KilledByType.Length > 0 && (killer is null || !crate.KilledByType.All(kind => killer.Template.IsKindOf(kind)))) continue;
            if (crate.KillerScience.Length > 0 && !KillerHasScience(world, killer, crate.KillerScience)) continue;
            CreateCrate(world, crate);
        }
    }

    private static bool KillerHasScience(IWorldServices world, Entity? killer, string science)
        => killer is not null && world.Sciences.TryGet(science, out var info) && world.PlayerSciences(killer.Owner).Has(info.Index);

    private void CreateCrate(IWorldServices world, CrateInfo crate)
    {
        var pick = world.LogicRandom.Range(Fixed.Zero, Fixed.One);
        var running = Fixed.Zero;
        string? chosen = null;
        foreach (var entry in crate.Objects)
        {
            running += entry.Weight;
            if (running > pick) { chosen = entry.ObjectName; break; }
        }
        if (chosen is null) return; // The designer's weights did not add up to 1: no crate (findTemplate("") is null).
        if (!world.Templates.TryGet(chosen, out var template)) return;

        var owner = crate.OwnedByMaker ? Entity.Owner : PlayerId.System;
        var angle = world.LogicRandom.Range(Fixed.Zero, TwoPi);
        world.Emit(SimEventKind.CrateRequested, Entity.Id, EntityId.Invalid, chosen, owner.Value);
        if (template.IsInstantiable)
            world.RequestSpawn(template.Index, owner, Entity.X, Entity.Y, Entity.Z, angle);
    }
}
