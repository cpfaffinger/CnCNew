using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Diagnostics;

public enum BuffOutcome
{
    Passed,
    Failed,
    /// <summary>The effect could not be brought about in the probe's world (no target the weapon fires at, a module the data name that acts only in a match); said with the reason.</summary>
    NotMeasured,
}

/// <summary>One buff measured on one object: what the data name (<see cref="Expected"/>), what the world did (<see cref="Measured"/>), and how the two compare.</summary>
public sealed record BuffResult(string Faction, string Buff, string Effect, string Object, string Module, BuffOutcome Outcome, string Expected, string Measured, string Detail)
{
    /// <summary>What a list of known failures names: the faction, the upgrade or science (or <c>horde</c>, <c>production</c>), the effect, the object and the module that names it.</summary>
    public string Key => $"{Faction}|{Buff}|{Effect}|{Object}|{Module}";
}

/// <summary>
/// One buff of a faction to measure: <see cref="Buff"/> is an upgrade, a science, <c>horde</c> (the horde of a <c>HordeUpdate</c>) or <c>production</c> (a general's own price
/// or build time of an object its base faction has too), <see cref="Effect"/> what it changes, <see cref="Module"/> the module of <see cref="Object"/> that names it (<c>Name:Tag</c>).
/// </summary>
public sealed record BuffCase(string Faction, string Buff, string Effect, string Object, string Module);

/// <summary>
/// #419: every general's buffs measured where they act. <see cref="CasesOf"/> derives them from the data of a <see cref="FactionRoster"/> - each module of a roster object that an upgrade
/// of the faction switches on (<c>TriggeredBy</c>, or the engine's reading by name: the horde's nationalism and fanaticism, the supply boost of a worker or a Chinook), each object
/// whose veterancy at birth a science of the faction gives (<c>VeterancyGainCreate</c>), each horde, and each object a general builds at another price or time than its base faction.
/// <see cref="Measure"/> builds two worlds alike on flat ground - the control without the buff and the test with it - does the same in both and compares what each shows with what
/// the data name and the original's code does (<c>Player::addUpgrade</c>, the <c>*Upgrade</c> modules, <c>Weapon::computeBonus</c>, <c>ActiveBody::setMaxHealth</c>,
/// <c>AIUpdateInterface::evaluateMoraleBonus</c>, <c>getUpgradedSupplyBoost</c>, <c>VeterancyGainCreate::onCreate</c>, <c>ThingTemplate::calcCostToBuild</c>/<c>calcTimeToBuild</c>):
/// a shot's damage, the damage a body takes per damage type, the health, the top speed, the experience a gain gives, the status, the stealth, the healing, the death weapon, the energy,
/// the time between shots, the money a delivery brings, the veterancy of a new unit, the money and time of a production.
/// </summary>
public sealed class FactionBuffProbe
{
    private static readonly PlayerId Me = new(1);
    private static readonly PlayerId Enemy = new(2);
    private static readonly PlayerId Civilian = new(3);
    private const int Centre = 1280;

    /// <summary>The module types whose effect is measured, with the effect's name.</summary>
    public static readonly ImmutableDictionary<string, string> MeasuredModules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MaxHealthUpgrade"] = "max-health", ["ArmorUpgrade"] = "armor", ["WeaponBonusUpgrade"] = "damage", ["WeaponSetUpgrade"] = "weapon-set",
        ["LocomotorSetUpgrade"] = "speed", ["ExperienceScalarUpgrade"] = "experience", ["StatusBitsUpgrade"] = "status", ["StealthUpgrade"] = "stealth",
        ["AutoHealBehavior"] = "auto-heal", ["FireWeaponWhenDeadBehavior"] = "death-weapon", ["PowerPlantUpgrade"] = "energy", ["HordeUpdate"] = "rate-of-fire",
        ["WorkerAIUpdate"] = "supply-boost", ["ChinookAIUpdate"] = "supply-boost",
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly CompiledDefinitionGraph _graph;
    private readonly TemplateCatalog _catalog;
    private readonly string _fingerprint;
    private readonly TerrainMap _terrain;
    private readonly Dictionary<string, ObjectDefinition> _objects;
    private readonly Dictionary<string, GameDefinition> _locomotors;
    private readonly Func<string, FactionRoster> _rosterOf;

    public FactionBuffProbe(CompiledDefinitionGraph graph, TemplateCatalog catalog, string fingerprint, Func<string, FactionRoster> rosterOf)
    {
        _graph = graph;
        _catalog = catalog;
        _fingerprint = fingerprint;
        _rosterOf = rosterOf;
        _terrain = FactionProbe.FlatTerrain();
        _objects = new Dictionary<string, ObjectDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>()) _objects.TryAdd(definition.OriginalName, definition);
        _locomotors = new Dictionary<string, GameDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<LocomotorDefinition>()) _locomotors.TryAdd(definition.OriginalName, definition);
    }

    /// <summary>Worlds built so far.</summary>
    public int Worlds { get; private set; }

    /// <summary>Game time stepped by every world so far, in seconds.</summary>
    public double SecondsStepped { get; private set; }

    // ------------------------------------------------------------------------------------------------ the cases, from the data

    /// <summary>Every buff of the roster with a measured effect, in a stable order.</summary>
    public ImmutableArray<BuffCase> CasesOf(FactionRoster roster)
    {
        var cases = new List<BuffCase>();
        var readers = Readers(roster);
        var upgrades = roster.Upgrades.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var upgrade in upgrades.Order(StringComparer.Ordinal))
        {
            var engine = FactionRoster.EngineReaders.GetValueOrDefault(upgrade);
            foreach (var name in readers)
            {
                if (!_objects.TryGetValue(name, out var definition)) continue;
                foreach (var module in definition.Modules)
                {
                    if (!MeasuredModules.TryGetValue(module.Name, out var effect)) continue;
                    var named = Names(module, "TriggeredBy").Contains(upgrade, StringComparer.OrdinalIgnoreCase);
                    // The engine reads the horde's two upgrades and a collector's supply boost by name (FactionRoster.EngineReaders); a boost of 0 boosts nothing.
                    var byEngine = module.Name.Equals(engine, StringComparison.OrdinalIgnoreCase)
                                   && (effect != "supply-boost" || Decimal(module, "UpgradedSupplyBoost") is > 0);
                    if (named || byEngine) cases.Add(new BuffCase(roster.Faction, upgrade, effect, definition.OriginalName, $"{module.Name}:{module.Tag}"));
                }
            }
        }
        // A projectile's weapon bonus (the laser-guided missiles of a jet: WeaponBonusUpgrade on the missile, MissileAIUpdate::detonate fires the launcher's weapon with the missile's
        // bonus): measured on what launches it.
        foreach (var projectile in roster.Made.Order(StringComparer.Ordinal))
        {
            if (!_catalog.TryGet(projectile, out var made) || !made.IsKindOf("PROJECTILE") || !_objects.TryGetValue(projectile, out var definition)) continue;
            foreach (var module in definition.Modules.Where(module => module.Name.Equals("WeaponBonusUpgrade", StringComparison.OrdinalIgnoreCase)))
                foreach (var upgrade in Names(module, "TriggeredBy").Where(upgrades.Contains))
                    foreach (var launcher in readers.Where(name => roster.Find(name) is not null && LaunchedBy(name).Contains(projectile, StringComparer.OrdinalIgnoreCase)))
                        cases.Add(new BuffCase(roster.Faction, upgrade, "damage", launcher, $"{projectile}/{module.Name}:{module.Tag}"));
        }
        // The horde itself (HordeUpdate::update, AIUpdateInterface::evaluateMoraleBonus): a unit among enough of its kind fires faster.
        foreach (var name in readers)
            if (_objects.TryGetValue(name, out var definition) && roster.Find(name) is not null)
                foreach (var module in definition.Modules.Where(module => module.Name.Equals("HordeUpdate", StringComparison.OrdinalIgnoreCase)))
                    cases.Add(new BuffCase(roster.Faction, "horde", "rate-of-fire", definition.OriginalName, $"{module.Name}:{module.Tag}"));
        // Veterancy at birth that a science gives (VeterancyGainCreate::onCreate with ScienceRequired).
        var sciences = roster.Sciences.Select(value => value.Name).Concat(roster.GrantedSciences).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in readers)
            if (_objects.TryGetValue(name, out var definition))
                foreach (var module in definition.Modules.Where(module => module.Name.Equals("VeterancyGainCreate", StringComparison.OrdinalIgnoreCase)))
                    if (Text(module, "ScienceRequired") is { Length: > 0 } science && sciences.Contains(science))
                        cases.Add(new BuffCase(roster.Faction, science, "veterancy-at-birth", definition.OriginalName, $"{module.Name}:{module.Tag}"));
        // A general's own price and build time of a unit its base faction builds too (the Air Force General's cheaper aircraft).
        if (!roster.BaseFaction.Equals(roster.Faction, StringComparison.OrdinalIgnoreCase))
        {
            var baseRoster = _rosterOf(roster.BaseFaction);
            foreach (var entry in roster.Objects.Where(value => value.Origin.StartsWith("produced by ", StringComparison.Ordinal)))
                if (Counterpart(entry.Name, baseRoster) is { } counterpart
                    && (ObjectField(entry.Name, "BuildCost") != ObjectField(counterpart.Name, "BuildCost") || ObjectField(entry.Name, "BuildTime") != ObjectField(counterpart.Name, "BuildTime")))
                    cases.Add(new BuffCase(roster.Faction, "production", "cost-and-time", entry.Name, counterpart.Name));
            // A general's own reload of a special power its base faction has too (the Super Weapon General's faster superweapons).
            foreach (var power in roster.Powers.Where(value => !value.Carriers.IsEmpty))
            {
                var underscore = power.Name.IndexOf('_');
                var basePower = underscore <= 0 ? null : baseRoster.Powers.FirstOrDefault(value => value.Name.Equals(power.Name[(underscore + 1)..], StringComparison.OrdinalIgnoreCase) && !value.Carriers.IsEmpty);
                if (basePower is not null && PowerField(power.Name, "ReloadTime") != PowerField(basePower.Name, "ReloadTime"))
                    cases.Add(new BuffCase(roster.Faction, power.Name, "reload", power.Carriers[0], basePower.Name));
            }
        }
        return [..cases.Distinct().OrderBy(value => value.Buff, StringComparer.Ordinal).ThenBy(value => value.Effect, StringComparer.Ordinal)
            .ThenBy(value => value.Object, StringComparer.Ordinal).ThenBy(value => value.Module, StringComparer.Ordinal)];
    }

    /// <summary>The modules of roster objects that an upgrade of the faction switches on but whose effect no measurement covers (<c>Module</c> by upgrade), for the report.</summary>
    public ImmutableArray<string> Unmeasured(FactionRoster roster)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var upgrade in roster.Upgrades)
            foreach (var reader in upgrade.Modules)
            {
                var module = reader[(reader.IndexOf('/') + 1)..];
                var type = module.Split(':')[0];
                if (!MeasuredModules.ContainsKey(type)) result.Add($"{upgrade.Name}:{type}");
            }
        return [..result];
    }

    /// <summary>The objects a buff can reach: the roster's and what they make (a drone, a portable structure), that the simulation can make.</summary>
    private List<string> Readers(FactionRoster roster)
        => roster.Objects.Select(value => value.Name).Concat(roster.Made)
            .Where(name => _catalog.TryGet(name, out var template) && template.IsInstantiable && !template.IsKindOf("PROJECTILE"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();

    /// <summary>The projectiles the object's weapons launch.</summary>
    private IEnumerable<string> LaunchedBy(string name)
        => _catalog.TryGet(name, out var template)
            ? template.WeaponSets.SelectMany(set => set.Weapons).Where(index => index >= 0).Select(index => _catalog.Weapons[index].ProjectileObject).Where(value => value.Length > 0)
            : [];

    /// <summary>The base faction's object of the same name without the general's prefix (<c>AirF_AmericaJetRaptor</c>, <c>AmericaJetRaptor</c>).</summary>
    private static RosterObject? Counterpart(string name, FactionRoster baseRoster)
    {
        var underscore = name.IndexOf('_');
        return underscore <= 0 ? null : baseRoster.Find(name[(underscore + 1)..]);
    }

    // ------------------------------------------------------------------------------------------------ the measurement

    public BuffResult Measure(FactionRoster roster, BuffCase item)
    {
        try
        {
            return item.Effect switch
            {
                "max-health" => MaxHealth(roster, item),
                "armor" => Armor(roster, item),
                "damage" => Damage(roster, item),
                "weapon-set" => WeaponSet(roster, item),
                "speed" => Speed(roster, item),
                "experience" => Experience(roster, item),
                "status" => Status(roster, item),
                "stealth" => Stealth(roster, item),
                "auto-heal" => AutoHeal(roster, item),
                "death-weapon" => DeathWeapon(roster, item),
                "energy" => Energy(roster, item),
                "rate-of-fire" => RateOfFire(roster, item),
                "supply-boost" => SupplyBoost(roster, item),
                "veterancy-at-birth" => VeterancyAtBirth(roster, item),
                "cost-and-time" => Production(roster, item),
                "reload" => Reload(roster, item),
                _ => Result(item, BuffOutcome.NotMeasured, "", "", $"no measurement for {item.Effect}"),
            };
        }
        catch (ProbeSetupException problem)
        {
            return Result(item, BuffOutcome.Failed, "", "", $"setup: {problem.Message}");
        }
    }

    private sealed class ProbeSetupException(string message) : Exception(message);

    /// <summary>
    /// <c>ActiveBody::setMaxHealth</c>: the body, 20 % hurt in both worlds, gets <c>AddMaxHealth</c> more of the most, and its health follows <c>ChangeType</c> (<c>PRESERVE_RATIO</c>,
    /// <c>ADD_CURRENT_HEALTH_TOO</c>, <c>FULLY_HEAL</c>, by default <c>SAME_CURRENTHEALTH</c>).
    /// </summary>
    private BuffResult MaxHealth(FactionRoster roster, BuffCase item)
    {
        var module = ModuleOf(item);
        var add = Decimal(module, "AddMaxHealth") ?? 0m;
        var change = (Text(module, "ChangeType") ?? "SAME_CURRENTHEALTH").ToUpperInvariant();
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Both(world => world.Damage(reader, new DamageInfo(EntityId.Invalid, world.FindEntity(reader)!.Body!.MaxHealth * Fixed.FromDecimal(0.2m), DamageTypes.Unresistable, DeathTypes.Normal, false)));
        pair.Steps(2);
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        var (maxA, healthA) = HealthOf(pair.A, reader);
        var (maxB, healthB) = HealthOf(pair.B, reader);
        var expectedMax = maxA + add;
        var expectedHealth = change switch
        {
            "PRESERVE_RATIO" => maxA == 0 ? 0 : healthA / maxA * expectedMax,
            "ADD_CURRENT_HEALTH_TOO" => healthA + add,
            "FULLY_HEAL" => expectedMax,
            _ => healthA,
        };
        var passed = Near(maxB, expectedMax, 0.01m) && Near(healthB, Math.Min(expectedHealth, expectedMax), 0.01m);
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"max {expectedMax:0.##}, health {expectedHealth:0.##} ({change})"),
            Invariant($"max {maxB:0.##}, health {healthB:0.##}"), Invariant($"without: max {maxA:0.##}, health {healthA:0.##}"));
    }

    /// <summary>
    /// <c>ArmorUpgrade::upgradeImplementation</c> sets <c>ARMORSET_PLAYER_UPGRADE</c> and the body applies that set's coefficients (<c>ArmorTemplate::adjustDamage</c>): the damage of
    /// every damage type that hurts health is dealt to the body in both worlds, and what it took is compared with the coefficients of the object's two armour sets.
    /// </summary>
    private BuffResult Armor(FactionRoster roster, BuffCase item)
    {
        var template = TemplateOf(item.Object);
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        var amount = Fixed.FromDecimal(0.5m);
        var wrong = new List<string>();
        var changed = new List<string>();
        for (var type = 0; type < NameTables.DamageType.Count; type++)
        {
            if (!HurtsHealth(type)) continue;
            var expectedA = template.DefaultArmor.Adjust(type, amount);
            var expectedB = (template.PlayerUpgradeArmor ?? template.DefaultArmor).Adjust(type, amount);
            var takenA = Taken(pair.A, reader, type, amount);
            var takenB = Taken(pair.B, reader, type, amount);
            var name = NameTables.DamageType.Canonical(type);
            if (expectedA != expectedB) changed.Add(Invariant($"{name} {Ratio(expectedA, amount)}->{Ratio(expectedB, amount)}"));
            if (!Near(takenA.ToDecimal(), expectedA.ToDecimal(), 0.001m) || !Near(takenB.ToDecimal(), expectedB.ToDecimal(), 0.001m))
                wrong.Add(Invariant($"{name} took {Ratio(takenA, amount)}/{Ratio(takenB, amount)} for {Ratio(expectedA, amount)}/{Ratio(expectedB, amount)}"));
        }
        var expected = template.PlayerUpgradeArmor is null ? "no PLAYER_UPGRADE armour set: unchanged" : changed.Count == 0 ? "the same coefficients" : string.Join(" ", changed);
        return Result(item, wrong.Count == 0 ? BuffOutcome.Passed : BuffOutcome.Failed, expected,
            wrong.Count == 0 ? "every damage type as expected" : string.Join("; ", wrong.Take(6)), "without/with the upgrade, as the share of the damage the body takes");
    }

    private Fixed Taken(Side side, EntityId id, int type, Fixed amount)
    {
        var entity = side.World.FindEntity(id) ?? throw new ProbeSetupException("the object is gone");
        var before = entity.Body!.Health;
        side.World.Damage(id, new DamageInfo(EntityId.Invalid, amount, type, DeathTypes.Normal, false));
        return before - entity.Body!.Health;
    }

    /// <summary>The damage types that take health (not healing, subdual, status, the special kinds that capture, disarm or kill a pilot or a garrison).</summary>
    private static bool HurtsHealth(int type)
    {
        var name = NameTables.DamageType.Canonical(type);
        return name is not ("HEALING" or "UNRESISTABLE" or "DEPLOY" or "SURRENDER" or "HACK" or "KILL_PILOT" or "PENALTY" or "DISARM" or "HAZARD_CLEANUP" or "KILL_GARRISONED"
            or "STATUS" or "TOPPLING") && !name.StartsWith("SUBDUAL_", StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>WeaponBonusUpgrade</c> sets <c>WEAPONBONUSCONDITION_PLAYER_UPGRADE</c>, and <c>Weapon::computeBonus</c> adds the condition's <c>DAMAGE</c> delta of <c>GameData</c> and of the
    /// weapon: the first hit on the same target in both worlds, the test's to the control's, against that multiplier.
    /// </summary>
    private BuffResult Damage(FactionRoster roster, BuffCase item)
    {
        var tried = new List<string>();
        foreach (var target in EnemyTargets(roster))
        {
            var pair = NewPair(roster);
            var reader = pair.Spawn(item.Object, Me, Centre, Centre);
            var ahead = FliesOnly(item.Object);
            var victim = ahead ? EntityId.Invalid : pair.Spawn(target, Enemy, Centre + Gap(item.Object, target), Centre);
            pair.Settle();
            pair.Upgrade(item.Buff, reader);
            pair.Steps(2);
            if (ahead) victim = pair.SpawnAhead(target, Enemy, reader, Gap(item.Object, target));
            var hitA = pair.A.FirstHit(victim, reader, 25);
            var hitB = pair.B.FirstHit(victim, reader, 25);
            if (hitA is null) { tried.Add($"{target}: no hit"); continue; }
            if (hitB is null) return Result(item, BuffOutcome.Failed, "a hit", "no hit with the upgrade", $"at {target}");
            var weapon = hitA.Value.Weapon is { Length: > 0 } name && _catalog.Weapons.TryGet(name, out var info) ? info : null;
            if (weapon is null) { tried.Add($"{target}: the hit names no weapon of the object"); continue; }
            // A bonus the projectile carries counts for the weapon that launches it (MissileAIUpdate::detonate, Weapon::computeBonus of the missile): another weapon's hit says nothing.
            if (item.Module.Split('/') is [var projectile, _] && !weapon.ProjectileObject.Equals(projectile, StringComparison.OrdinalIgnoreCase))
            {
                tried.Add($"{target}: hit by {weapon.Name}, which does not launch {projectile}");
                continue;
            }
            var conditions = hitA.Value.Conditions & ~(1u << WeaponBonusTable.PlayerUpgradeCondition);
            var expected = Multiplier(weapon, conditions | 1u << WeaponBonusTable.PlayerUpgradeCondition, WeaponBonusTable.Damage) / Multiplier(weapon, conditions, WeaponBonusTable.Damage);
            // A hit that kills is clipped by the health: then the damage the shot computed (before the armour, the same in both worlds) is what is compared.
            var killed = hitA.Value.Killed || hitB.Value.Killed;
            var (amountA, amountB) = killed ? (hitA.Value.Computed, hitB.Value.Computed) : (hitA.Value.Amount, hitB.Value.Amount);
            if (amountA == 0) { tried.Add($"{target}: {(killed ? "a killing hit without a computed damage" : "no damage")}"); continue; }
            var measured = amountB / amountA;
            return Result(item, Near(measured, expected, 0.005m * expected) ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"damage x{expected:0.###}"),
                Invariant($"damage x{measured:0.###} ({amountA:0.##} -> {amountB:0.##})"), $"{weapon.Name} at {target}{(killed ? ", the shot's computed damage (the hit killed)" : "")}");
        }
        return Result(item, BuffOutcome.NotMeasured, "", "", $"no target the object hit: {string.Join(", ", tried)}");
    }

    private decimal Multiplier(WeaponInfo weapon, uint conditions, int field)
        => 1m + _catalog.GlobalWeaponBonus.Sum(conditions, field).ToDecimal() + weapon.Bonuses.Sum(conditions, field).ToDecimal();

    /// <summary>
    /// <c>WeaponSetUpgrade</c> sets <c>WEAPONSET_PLAYER_UPGRADE</c> and <c>ThingTemplate::findWeaponTemplateSet</c> picks the set that matches best: the weapons of the test's active set
    /// against the set the data name for the control's flags and <c>PLAYER_UPGRADE</c>, and the first slot the upgrade changes fired at a target.
    /// </summary>
    private BuffResult WeaponSet(FactionRoster roster, BuffCase item)
    {
        var template = TemplateOf(item.Object);
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        var entityA = pair.A.World.FindEntity(reader)!;
        var entityB = pair.B.World.FindEntity(reader)!;
        var expectedSet = WeaponSetResolver.Find(template.WeaponSets, SimulationWorld.WeaponSetFlagsOf(entityA) | PlayerUpgradeWeaponSetFlag);
        string Names(WeaponSetInfo? set) => set is null ? "none" : string.Join(" ", set.Weapons.Select(index => index >= 0 ? _catalog.Weapons[index].Name : "-"));
        var activeB = SimulationWorld.ActiveWeaponSet(entityB);
        var activeA = SimulationWorld.ActiveWeaponSet(entityA);
        if (!ReferenceEquals(expectedSet, activeB) && Names(expectedSet) != Names(activeB))
            return Result(item, BuffOutcome.Failed, Names(expectedSet), Names(activeB), $"without: {Names(activeA)}");
        // The slot the upgrade changes, fired at a target where it acts.
        var slot = -1;
        for (var index = 0; expectedSet is not null && index < expectedSet.Weapons.Length; index++)
            if (expectedSet.Weapons[index] >= 0 && (activeA is null || index >= activeA.Weapons.Length || activeA.Weapons[index] != expectedSet.Weapons[index])) { slot = index; break; }
        if (slot < 0) return Result(item, BuffOutcome.Passed, Names(expectedSet), Names(activeB), "the upgrade changes no weapon of the set");
        var weapon = _catalog.Weapons[expectedSet!.Weapons[slot]].Name;
        var tried = new List<string>();
        foreach (var target in EnemyTargets(roster))
        {
            var shot = NewPair(roster);
            var source = shot.Spawn(item.Object, Me, Centre, Centre);
            var ahead = FliesOnly(item.Object);
            var victim = ahead ? EntityId.Invalid : shot.Spawn(target, Enemy, Centre + Gap(item.Object, target), Centre);
            shot.Settle();
            shot.Upgrade(item.Buff, source);
            shot.Steps(2);
            if (ahead) victim = shot.SpawnAhead(target, Enemy, source, Gap(item.Object, target));
            var world = shot.B.World;
            var answer = world.Submit(new FireWeaponCommand(Me, shot.Next(world), world.Tick, source, victim, slot, FireOrigin.Player, FireWeaponCommand.NoMaxShotsLimit));
            if (!answer.Accepted) { tried.Add($"{target}: {answer.Reason}"); continue; }
            if (shot.B.Fired(source, weapon, 20)) return Result(item, BuffOutcome.Passed, Names(expectedSet), Names(activeB), $"fired {weapon} at {target}");
            tried.Add($"{target}: did not fire");
        }
        return Result(item, BuffOutcome.Passed, Names(expectedSet), Names(activeB), $"the set is right; {weapon} fired at no target ({string.Join(", ", tried)})");
    }

    private static readonly uint PlayerUpgradeWeaponSetFlag = NameTablesPhase5.WeaponSetCondition.TryIndexOf("PLAYER_UPGRADE", out var index) ? 1u << index : 0u;

    /// <summary>
    /// <c>LocomotorSetUpgrade</c>: <c>setLocomotorUpgrade(true)</c>, after which <c>SET_NORMAL</c> means <c>SET_NORMAL_UPGRADED</c>. Both worlds drive the object straight ahead; the
    /// most it covers in one second against the <c>Speed</c> of each set's locomotor.
    /// </summary>
    private BuffResult Speed(FactionRoster roster, BuffCase item)
    {
        var normal = LocomotorSpeed(item.Object, "SET_NORMAL");
        var upgraded = LocomotorSpeed(item.Object, "SET_NORMAL_UPGRADED") ?? normal;
        if (normal is null) return Result(item, BuffOutcome.NotMeasured, "", "", "the object names no SET_NORMAL locomotor");
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre - 1000, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        pair.Both(world => world.Submit(new SetMoveTargetCommand(Me, pair.Next(world), world.Tick, reader, Fixed.FromInt(Centre + 1100), Fixed.FromInt(Centre))));
        var speedA = pair.A.TopSpeed(reader, 25);
        var speedB = pair.B.TopSpeed(reader, 25);
        var passed = Near(speedA, normal.Value, 0.02m * normal.Value) && Near(speedB, upgraded!.Value, 0.02m * upgraded.Value);
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"{normal:0.##} -> {upgraded:0.##} per s"), Invariant($"{speedA:0.##} -> {speedB:0.##} per s"),
            "the most covered in one second, without/with the upgrade");
    }

    /// <summary>The <c>Speed</c> of the first locomotor of the object's set (<c>Locomotor = SET_X Name ...</c>).</summary>
    private decimal? LocomotorSpeed(string objectName, string set)
    {
        if (!_objects.TryGetValue(objectName, out var definition)) return null;
        var line = definition.Fields.LastOrDefault(field => field.Name.Equals("Locomotor", StringComparison.OrdinalIgnoreCase)
                                                            && Tokens(field.Value.OriginalText).FirstOrDefault()?.Equals(set, StringComparison.OrdinalIgnoreCase) == true);
        var name = line is null ? null : Tokens(line.Value.OriginalText).Skip(1).FirstOrDefault();
        if (name is null || !_locomotors.TryGetValue(name, out var locomotor)) return null;
        return Number(locomotor.Fields.LastOrDefault(field => field.Name.Equals("Speed", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText);
    }

    /// <summary><c>ExperienceScalarUpgrade</c> adds <c>AddXPScalar</c> to <c>ExperienceTracker::m_experienceScalar</c>; <c>addExperiencePoints</c> multiplies a gain by it (truncated) when the object is trainable.</summary>
    private BuffResult Experience(FactionRoster roster, BuffCase item)
    {
        var add = Decimal(ModuleOf(item), "AddXPScalar") ?? 0m;
        var trainable = TemplateOf(item.Object).IsTrainable;
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        const int gain = 10;
        int Gained(Side side)
        {
            var entity = side.World.FindEntity(reader)!;
            var before = entity.Experience;
            side.World.AddExperiencePoints(entity, gain);
            return entity.Experience - before;
        }
        var gainA = Gained(pair.A);
        var gainB = Gained(pair.B);
        var expectedA = trainable ? gain : 0;
        var expectedB = trainable ? (int)decimal.Floor(gain * (1m + add)) : 0;
        return Result(item, gainA == expectedA && gainB == expectedB ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"{expectedA} -> {expectedB} of a gain of {gain}"),
            Invariant($"{gainA} -> {gainB}"), trainable ? Invariant($"AddXPScalar {add}") : "the object is not trainable: it gains nothing");
    }

    /// <summary><c>StatusBitsUpgrade::upgradeImplementation</c>: <c>setStatus(StatusToSet)</c> and <c>clearStatus(StatusToClear)</c>.</summary>
    private BuffResult Status(FactionRoster roster, BuffCase item)
    {
        var module = ModuleOf(item);
        var set = StatusMask(Names(module, "StatusToSet"));
        var clear = StatusMask(Names(module, "StatusToClear"));
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        var statusA = pair.A.World.FindEntity(reader)!.Status;
        var statusB = pair.B.World.FindEntity(reader)!.Status;
        var passed = (statusB & set) == set && (statusB & clear) == 0;
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, $"set {StatusNames(set)}{(clear != 0 ? $", clear {StatusNames(clear)}" : "")}",
            StatusNames(statusB & (set | clear)), $"without: {StatusNames(statusA & (set | clear))}");
    }

    /// <summary>
    /// <c>StealthUpgrade::upgradeImplementation</c> sets <c>OBJECT_STATUS_CAN_STEALTH</c>; the object's <c>StealthUpdate</c> then hides it after <c>StealthDelay</c> standing still.
    /// </summary>
    private BuffResult Stealth(FactionRoster roster, BuffCase item)
    {
        var stealth = _objects[item.Object].Modules.FirstOrDefault(module => module.Name.Equals("StealthUpdate", StringComparison.OrdinalIgnoreCase));
        var delay = stealth is null ? 0m : Decimal(stealth, "StealthDelay") ?? 0m;
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Seconds((int)decimal.Ceiling(delay / 1000m) + 3);
        var canStealth = StatusMask(["CAN_STEALTH"]);
        var stealthed = StatusMask(["STEALTHED"]);
        var expected = stealth is null ? canStealth : canStealth | stealthed;
        var statusA = pair.A.World.FindEntity(reader)!.Status & (canStealth | stealthed);
        var statusB = pair.B.World.FindEntity(reader)!.Status & (canStealth | stealthed);
        return Result(item, statusB == expected && statusA == 0 ? BuffOutcome.Passed : BuffOutcome.Failed, StatusNames(expected), StatusNames(statusB),
            Invariant($"without: {StatusNames(statusA)}; StealthDelay {delay} ms, standing still"));
    }

    /// <summary>
    /// <c>AutoHealBehavior::update</c> (the self-heal of <c>Radius = 0</c>): after <c>StartHealingDelay</c> from the last damage - without one in the next frame (<c>onDamage</c>) -,
    /// <c>HealingAmount</c> every <c>HealingDelay</c>, through the body's armour (<c>attemptHealing</c>). The body is hurt by half in both worlds, two frames of the original after the
    /// upgrade and where a frame of the object begins, at every rate; a heal counts for the tick that was stepped (#432). The test's heals against those numbers, the control heals nothing.
    /// </summary>
    private BuffResult AutoHeal(FactionRoster roster, BuffCase item)
    {
        var module = ModuleOf(item);
        var radius = Decimal(module, "Radius") ?? 0m;
        var amount = Decimal(module, "HealingAmount") ?? 0m;
        var delay = Decimal(module, "HealingDelay") ?? 0m;
        var start = Decimal(module, "StartHealingDelay") ?? 0m;
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        // The spawn took one tick: the hurt is put where a frame of the object begins, as at 30 Hz.
        var ticksPerFrame = pair.A.World.Time.TicksPerOriginalFrame;
        pair.Steps(checked((int)((ticksPerFrame - (pair.A.World.Tick - pair.A.World.FindEntity(reader)!.SpawnTick) % ticksPerFrame) % ticksPerFrame)));
        pair.Upgrade(item.Buff, reader);
        pair.Steps(checked((int)pair.A.World.Time.OriginalFrames(2)));
        pair.Both(world => world.Damage(reader, new DamageInfo(EntityId.Invalid, world.FindEntity(reader)!.Body!.MaxHealth * Fixed.FromDecimal(0.5m), DamageTypes.Unresistable, DeathTypes.Normal, false)));
        var hurt = pair.A.World.Tick;
        var heals = new List<(long Tick, decimal Amount)>();
        var healedA = 0m;
        var last = (A: HealthOf(pair.A, reader).Health, B: HealthOf(pair.B, reader).Health);
        var until = hurt + pair.A.World.Time.WholeSecondsToTicks(20);
        while (pair.A.World.Tick < until)
        {
            pair.Steps(1);
            var now = (A: HealthOf(pair.A, reader).Health, B: HealthOf(pair.B, reader).Health);
            if (now.A > last.A) healedA += now.A - last.A;
            if (now.B > last.B) heals.Add((pair.B.World.Tick - 1, now.B - last.B));
            last = now;
        }
        var time = pair.A.World.Time;
        var pulse = TemplateOf(item.Object).DefaultArmor.Adjust(DamageTypes.Healing, Fixed.FromDecimal(amount)).ToDecimal();
        var intervalTicks = time.DurationMillisecondsToTicks(delay);
        var startTicks = time.DurationMillisecondsToTicks(start);
        var frame = time.OriginalFrames(1);
        var intervals = heals.Zip(heals.Skip(1), (first, second) => second.Tick - first.Tick).ToList();
        var first = Math.Max(startTicks, frame);
        var expected = Invariant($"{pulse:0.##} every {delay} ms, first {first * 1000m / time.TicksPerSecond:0} ms after the hurt");
        var measured = heals.Count == 0 ? "no heal"
            : Invariant($"{heals[0].Amount:0.##} first after {(heals[0].Tick - hurt) * 1000m / time.TicksPerSecond:0} ms, {heals.Count} heals, every {(intervals.Count == 0 ? 0 : intervals.Min() * 1000m / time.TicksPerSecond):0}-{(intervals.Count == 0 ? 0 : intervals.Max() * 1000m / time.TicksPerSecond):0} ms");
        if (radius > 0) return Result(item, heals.Count > 0 ? BuffOutcome.Passed : BuffOutcome.Failed, $"heals in a radius of {radius}", measured, "the radius heal's pulses are not timed");
        string Life(Side side) => side.World.FindEntity(reader) is { EffectivelyDead: false } ? "alive" : "dead";
        if (Life(pair.A) == "dead" || Life(pair.B) == "dead")
            return Result(item, BuffOutcome.Failed, expected, $"{measured}; the object died (without: {Life(pair.A)}, with: {Life(pair.B)})", "it was hurt by half, not killed");
        var passed = heals.Count >= 2 && healedA == 0
                     && heals.All(value => Near(value.Amount, pulse, 0.01m) || value == heals[^1])
                     && intervals.SkipLast(1).All(value => Math.Abs(value - intervalTicks) <= frame)
                     && heals[0].Tick - hurt == first;
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, expected, measured, Invariant($"without: healed {healedA:0.##}"));
    }

    /// <summary>
    /// <c>FireWeaponWhenDeadBehavior::onDie</c>: once its upgrade is in (<c>TriggeredBy</c>), the death fires <c>DeathWeapon</c>, unless a <c>ConflictsWith</c> upgrade completed. The object is
    /// killed in both worlds; the death weapons each asked for.
    /// </summary>
    private BuffResult DeathWeapon(FactionRoster roster, BuffCase item)
    {
        var module = ModuleOf(item);
        var weapon = Text(module, "DeathWeapon") ?? string.Empty;
        var startsActive = Bool(module, "StartsActive");
        var upgrades = Bool(module, "RequiresAllTriggers") ? Names(module, "TriggeredBy") : (ImmutableArray<string>?)null;
        // DieMuxData: the death must be one of the module's DeathTypes (all when it names none) - the normal death when it may, else the first the module takes.
        var allowed = module.Fields.LastOrDefault(field => field.Name.Equals("DeathTypes", StringComparison.OrdinalIgnoreCase))?.Value.Value is FlagsValue flags
            ? flags.Names.ToHashSet(StringComparer.OrdinalIgnoreCase) : NameTables.DeathType.Names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var death = allowed.Contains("NORMAL") ? "NORMAL" : NameTables.DeathType.Names.FirstOrDefault(allowed.Contains);
        if (death is null || !NameTables.DeathType.TryIndexOf(death, out var deathType)) return Result(item, BuffOutcome.NotMeasured, weapon, "", "the module takes no death");
        var pair = NewPair(roster);
        // What the faction's objects make (a bomb, the dummy that fires a vehicle's death weapon) is made after the upgrade, and dies at once: the test world has it first.
        var made = roster.Find(item.Object) is null;
        if (made) pair.Upgrade(item.Buff, EntityId.Invalid, upgrades);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        // RequiredStatus of a rider (STATUS_RIDERn of RiderChangeContain): the rider of that status gets in, in both worlds.
        foreach (var status in Names(module, "RequiredStatus"))
        {
            var bit = NameTables.ObjectStatus.TryIndexOf(status, out var index) ? index : -1;
            var rider = TemplateOf(item.Object).Modules.Select(value => value.Data).OfType<RiderChangeContainData>().SelectMany(data => data.Riders).FirstOrDefault(value => value.StatusBit == bit);
            if (rider is null) return Result(item, BuffOutcome.NotMeasured, weapon, "", $"the module needs {status}, which no rider of the object gives");
            var seated = pair.Spawn(rider.TemplateName, Me, Centre - 40, Centre);
            var entered = true;
            pair.Both(world => entered &= world.TryEnterContainer(world.FindEntity(seated)!, world.FindEntity(reader)!));
            if (!entered) return Result(item, BuffOutcome.NotMeasured, weapon, "", $"{rider.TemplateName} ({status}) did not get in");
            pair.Steps(2);
        }
        // ExemptStatus of a rider: a dead vehicle keeps its rider's status (RiderChangeContain::onRemoving returns before it clears it), so a vehicle that starts with an exempt
        // rider (the Demolition bike's terrorist) gets a rider of another line first, which throws the old one out.
        var exempt = Names(module, "ExemptStatus").Select(status => NameTables.ObjectStatus.TryIndexOf(status, out var index) ? index : -1).ToHashSet();
        var lines = TemplateOf(item.Object).Modules.Select(value => value.Data).OfType<RiderChangeContainData>().SelectMany(data => data.Riders).ToList();
        if (exempt.Count > 0 && lines.Count > 0 && pair.B.World.FindEntity(reader) is { } vehicle && exempt.Any(bit => bit >= 0 && vehicle.HasStatus(bit)))
        {
            var other = lines.FirstOrDefault(value => !exempt.Contains(value.StatusBit));
            if (other is null) return Result(item, BuffOutcome.NotMeasured, weapon, "", "every rider of the object has a status the module is exempt from");
            var seated = pair.Spawn(other.TemplateName, Me, Centre - 40, Centre);
            var entered = true;
            pair.Both(world => entered &= world.TryEnterContainer(world.FindEntity(seated)!, world.FindEntity(reader)!));
            if (!entered) return Result(item, BuffOutcome.NotMeasured, weapon, "", $"{other.TemplateName} did not get in");
            pair.Steps(2);
        }
        if (!made) pair.Upgrade(item.Buff, reader, upgrades);
        pair.Steps(2);
        pair.Both(world =>
        {
            if (world.FindEntity(reader) is { EffectivelyDead: false }) world.Damage(reader, new DamageInfo(EntityId.Invalid, Fixed.Zero, DamageTypes.Unresistable, deathType, true));
        });
        pair.Seconds(3);
        string[] Asked(Side side) => [..side.Events.Where(value => value.Kind == SimEventKind.WeaponFireRequested && value.Entity == reader).Select(value => value.Text).Distinct().Order(StringComparer.Ordinal)];
        var askedA = Asked(pair.A);
        var askedB = Asked(pair.B);
        var passed = askedB.Contains(weapon, StringComparer.OrdinalIgnoreCase) && (startsActive || !askedA.Contains(weapon, StringComparer.OrdinalIgnoreCase));
        // The weapon in both worlds can be another module's that needs no upgrade (the Demolition bike's crushed terrorist fires CrushedBikeBomb with and without the
        // suicide bomb, from two modules): the weapon's name cannot tell the two apart.
        var twin = _objects.TryGetValue(item.Object, out var owner) ? owner.Modules.FirstOrDefault(other => !other.Tag.Equals(module.Tag, StringComparison.OrdinalIgnoreCase)
            && other.Name.Equals("FireWeaponWhenDeadBehavior", StringComparison.OrdinalIgnoreCase) && string.Equals(Text(other, "DeathWeapon"), weapon, StringComparison.OrdinalIgnoreCase)) : null;
        if (!passed && twin is not null && askedB.Contains(weapon, StringComparer.OrdinalIgnoreCase))
            return Result(item, BuffOutcome.NotMeasured, weapon, string.Join(" ", askedB), $"{twin.Name}:{twin.Tag} fires {weapon} too; without: {(askedA.Length == 0 ? "none" : string.Join(" ", askedA))}; death {death}");
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, weapon, askedB.Length == 0 ? "none" : string.Join(" ", askedB),
            $"without: {(askedA.Length == 0 ? "none" : string.Join(" ", askedA))}; death {death}{(made ? ", made after the upgrade" : "")}{(startsActive ? "; starts active" : "")}");
    }

    /// <summary><c>PowerPlantUpgrade::upgradeImplementation</c>: <c>Player::addPowerBonus</c> adds the object's <c>EnergyBonus</c> to the player's production.</summary>
    private BuffResult Energy(FactionRoster roster, BuffCase item)
    {
        var bonus = (int)(ObjectNumber(item.Object, "EnergyBonus") ?? 0m);
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        var measured = pair.B.World.EnergyProductionOf(Me) - pair.A.World.EnergyProductionOf(Me);
        return Result(item, measured == bonus ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"+{bonus}"), Invariant($"+{measured}"), "the player's energy production, with against without");
    }

    /// <summary>
    /// <c>AIUpdateInterface::evaluateMoraleBonus</c>: <c>HORDE</c> among <c>Count</c> of the kind within <c>Radius</c>, <c>NATIONALISM</c> with <c>Upgrade_Nationalism</c> (cleared for a horde
    /// that <c>AllowedNationalism = No</c>) and <c>FANATICISM</c> with <c>Upgrade_Fanaticism</c> on top; their <c>RATE_OF_FIRE</c> deltas divide the delay between shots
    /// (<c>getDelayBetweenShots</c>, floored in frames). The shortest time between two shots of the object at an enemy building, against that delay - one frame more for an odd delay,
    /// the attack's aim and fire states taking a frame each (F-140).
    /// </summary>
    private BuffResult RateOfFire(FactionRoster roster, BuffCase item)
    {
        var module = ModuleOf(item);
        var count = (int)(Decimal(module, "Count") ?? 0m);
        var allowed = Text(module, "AllowedNationalism") is not { } text || !text.Equals("No", StringComparison.OrdinalIgnoreCase);
        var horde = item.Buff == "horde";
        var fanaticism = item.Buff.Equals("Upgrade_Fanaticism", StringComparison.OrdinalIgnoreCase);
        var target = EnemyTargets(roster).First();
        var pair = NewPair(roster);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        // Close by: a shooter that walks to its victim walks out of its horde.
        var victim = pair.Spawn(target, Enemy, Centre + TemplateOf(item.Object).BoundingCircleRadius.ToDecimal() + TemplateOf(target).BoundingCircleRadius.ToDecimal() + 25m, Centre);
        // The horde: Count more of the same kind around it (HordeUpdate counts the others within Radius); for the horde case only in the test world.
        for (var mate = 0; mate < count; mate++)
        {
            var angle = 2 * Math.PI * mate / Math.Max(1, count);
            var x = Centre + (int)Math.Round(14 * Math.Cos(angle));
            var y = Centre + (int)Math.Round(14 * Math.Sin(angle));
            // The others stand still (they look for no victim of their own): a horde that walks off to fight is no horde to measure.
            if (horde) pair.B.World.SetAttitude(pair.B.World.FindEntity(pair.B.Spawn(item.Object, Me, x, y))!, (int)Attitude.Sleep);
            else
            {
                var id = pair.Spawn(item.Object, Me, x, y);
                pair.Both(world => world.SetAttitude(world.FindEntity(id)!, (int)Attitude.Sleep));
            }
        }
        if (horde) pair.A.Steps(count);
        pair.Settle();
        if (fanaticism) pair.Both(world => world.CompleteUpgrade(Me, null, "Upgrade_Nationalism"));
        if (!horde) pair.Upgrade(item.Buff, reader);
        pair.Seconds(3);
        pair.Both(world => world.Submit(new FireWeaponCommand(Me, pair.Next(world), world.Tick, reader, victim, WeaponSlot.Best, FireOrigin.Player, FireWeaponCommand.NoMaxShotsLimit)));
        var shotsA = pair.A.ShotsOver(reader, 12);
        var shotsB = pair.B.ShotsOver(reader, 12);
        if (shotsA.Count < 2 || shotsB.Count < 2) return Result(item, BuffOutcome.NotMeasured, "", "", Invariant($"too few shots at {target}: {shotsA.Count}/{shotsB.Count}"));
        var weapon = _catalog.Weapons.TryGet(shotsA[0].Weapon, out var info) ? info : null;
        if (weapon is null) return Result(item, BuffOutcome.NotMeasured, "", "", $"the shot names no weapon: {shotsA[0].Weapon}");
        // What the data and the original make of it: the bits each world should carry, and the delay they give.
        var hordeBit = Bit("HORDE");
        var nationalismBit = Bit("NATIONALISM");
        var fanaticismBit = Bit("FANATICISM");
        var morale = hordeBit | nationalismBit | fanaticismBit;
        uint Expected(bool inHorde, bool nationalism, bool fanatic)
        {
            if (inHorde && !allowed) (nationalism, fanatic) = (false, false);
            return (inHorde ? hordeBit : 0) | (nationalism ? nationalismBit : 0) | (nationalism && fanatic ? fanaticismBit : 0);
        }
        // The horde case: alone against a horde; nationalism: a horde without and with it; fanaticism: a horde with nationalism, without and with it.
        var expectedA = Expected(!horde, fanaticism, false);
        var expectedB = Expected(true, !horde, fanaticism);
        var time = pair.A.World.Time;
        var frame = (decimal)time.OriginalFrames(1);
        // The first two shots: the time after a shot is the delay between shots (a clip of one: its reload), scaled by the rate of fire of the conditions the shooter had as it fired -
        // its others as they were, its morale bits as they should be.
        long Frames(uint shotConditions, uint expected)
        {
            var ticks = weapon.ClipSize == 1 ? weapon.ClipReloadTicks : weapon.DelayBetweenShotsTicks;
            var multiplier = Multiplier(weapon, (shotConditions & ~morale) | expected, WeaponBonusTable.RateOfFire);
            return Math.Max(1, (long)decimal.Floor(ticks / frame / multiplier));
        }
        var framesA = Frames(shotsA[0].Conditions, expectedA);
        var framesB = Frames(shotsB[0].Conditions, expectedB);
        var measuredA = (shotsA[1].Tick - shotsA[0].Tick) / frame;
        var measuredB = (shotsB[1].Tick - shotsB[0].Tick) / frame;
        // The attack's aim and fire states take a frame each (F-140): an odd delay waits a frame more.
        bool Fits(decimal measured, long frames) => measured >= frames && measured <= frames + frames % 2;
        var conditionsA = shotsA[0].Conditions & morale;
        var conditionsB = shotsB[0].Conditions & morale;
        var passed = conditionsA == expectedA && conditionsB == expectedB && Fits(measuredA, framesA) && Fits(measuredB, framesB);
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed,
            Invariant($"{BonusNames(expectedA)} {framesA} -> {BonusNames(expectedB)} {framesB} frames"),
            Invariant($"{BonusNames(conditionsA)} {measuredA:0.#} -> {BonusNames(conditionsB)} {measuredB:0.#} frames"),
            $"the time between the first two shots of {weapon.Name} at {target}{(allowed ? "" : "; AllowedNationalism = No")}");
    }

    private static uint Bit(string condition) => NameTablesPhase5.WeaponBonusCondition.TryIndexOf(condition, out var index) ? 1u << index : 0u;

    private static string BonusNames(uint conditions)
    {
        var names = new List<string>();
        for (var index = 0; index < NameTablesPhase5.WeaponBonusCondition.Count; index++)
            if ((conditions & (1u << index)) != 0) names.Add(NameTablesPhase5.WeaponBonusCondition.Canonical(index));
        return names.Count == 0 ? "none" : string.Join("+", names);
    }

    /// <summary>
    /// <c>SupplyCenterDockUpdate::action</c> adds <c>getUpgradedSupplyBoost</c> to a delivery: <c>WorkerAIUpdate</c> with <c>Upgrade_GLAWorkerShoes</c>, <c>ChinookAIUpdate</c> with
    /// <c>Upgrade_AmericaSupplyLines</c> - the module's <c>UpgradedSupplyBoost</c>. The collector is sent to a warehouse beside the faction's supply centre in both worlds; the first delivery's money.
    /// </summary>
    private BuffResult SupplyBoost(FactionRoster roster, BuffCase item)
    {
        var boost = (long)(Decimal(ModuleOf(item), "UpgradedSupplyBoost") ?? 0m);
        var center = roster.Objects.Select(value => value.Name).FirstOrDefault(name => _objects.TryGetValue(name, out var definition)
                                                                                      && definition.Modules.Any(module => module.Name.Equals("SupplyCenterDockUpdate", StringComparison.OrdinalIgnoreCase)));
        var warehouse = _catalog.Templates.Where(value => value.IsInstantiable && value.Modules.Any(module => module.Data is SupplyWarehouseDockData { StartingBoxes: > 0 }))
            .OrderBy(value => value.Name, StringComparer.Ordinal).FirstOrDefault();
        if (center is null || warehouse is null) return Result(item, BuffOutcome.NotMeasured, "", "", "no supply centre in the roster or no warehouse in the data");
        var pair = NewPair(roster);
        pair.Spawn(center, Me, Centre - 250, Centre);
        var dock = pair.Spawn(warehouse.Name, Civilian, Centre + 250, Centre);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre + 60);
        pair.Settle();
        pair.Upgrade(item.Buff, reader);
        pair.Steps(2);
        pair.Events();
        pair.Both(world => world.Submit(new DockCommand(Me, pair.Next(world), world.Tick, reader, dock)));
        var valueA = pair.A.FirstDelivery(reader, 120);
        var valueB = pair.B.FirstDelivery(reader, 120);
        if (valueA is null || valueB is null) return Result(item, BuffOutcome.Failed, Invariant($"+{boost} a delivery"), Invariant($"deliveries {valueA?.ToString(CultureInfo.InvariantCulture) ?? "none"}/{valueB?.ToString(CultureInfo.InvariantCulture) ?? "none"}"), $"at {warehouse.Name}, to {center}");
        return Result(item, valueB - valueA == boost ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"+{boost} a delivery"), Invariant($"+{valueB - valueA} ({valueA} -> {valueB})"), $"the first full delivery at {center}");
    }

    /// <summary><c>VeterancyGainCreate::onCreate</c>: an object made while its player owns <c>ScienceRequired</c> starts at <c>StartingLevel</c>.</summary>
    private BuffResult VeterancyAtBirth(FactionRoster roster, BuffCase item)
    {
        var level = LevelOf(Text(ModuleOf(item), "StartingLevel"));
        // ThingFactory::newObject makes one of the template's BuildVariations when it names any: the object a player gets is that one, with its own create modules.
        var variations = _objects.TryGetValue(item.Object, out var definition)
            ? definition.Fields.Where(field => field.Name.Equals("BuildVariations", StringComparison.OrdinalIgnoreCase)).SelectMany(field => Tokens(field.Value.OriginalText)).ToList() : [];
        // setMinVeterancyLevel of every VeterancyGainCreate whose science the player owns (none named: always), on a trainable object.
        int Expected(string made, ScienceMask owned)
        {
            if (!_catalog.TryGet(made, out var template) || !template.IsTrainable || !_objects.TryGetValue(made, out var madeDefinition)) return 0;
            return madeDefinition.Modules.Where(value => value.Name.Equals("VeterancyGainCreate", StringComparison.OrdinalIgnoreCase))
                .Where(value => Text(value, "ScienceRequired") is not { Length: > 0 } science || _catalog.Sciences.TryGet(science, out var info) && owned.Has(info.Index))
                .Select(value => LevelOf(Text(value, "StartingLevel"))).DefaultIfEmpty(0).Max();
        }
        if (roster.Find(item.Object) is { } entry && entry.Origin.StartsWith("produced by ", StringComparison.Ordinal))
        {
            // Made the way a player makes it: produced, without and with the science.
            var withScience = SciencesFor(item.Buff).ToList();
            var without = Produce(roster, item.Object);
            var with = Produce(roster, item.Object, withScience);
            if (without.Problem is not null || with.Problem is not null || without.Made.Length == 0 || with.Made.Length == 0)
                return Result(item, BuffOutcome.Failed, "", $"{without.Problem ?? without.Made} / {with.Problem ?? with.Made}", "the production made nothing");
            var ownedA = NewSide(roster).World.PlayerSciences(Me);
            var probe = NewSide(roster);
            foreach (var science in withScience) probe.World.GrantScience(Me, science);
            var ownedB = probe.World.PlayerSciences(Me);
            var allowed = variations.Count == 0 ? [item.Object] : variations;
            var rightKind = allowed.Contains(without.Made, StringComparer.OrdinalIgnoreCase) && allowed.Contains(with.Made, StringComparer.OrdinalIgnoreCase);
            var expectedA = Expected(rightKind ? without.Made : allowed[0], ownedA);
            var expectedB = Expected(rightKind ? with.Made : allowed[0], ownedB);
            return Result(item, rightKind && without.Level == expectedA && with.Level == expectedB ? BuffOutcome.Passed : BuffOutcome.Failed,
                Invariant($"{(variations.Count == 0 ? item.Object : "one of " + string.Join(" ", variations))}: {expectedA} -> {expectedB}"),
                Invariant($"{without.Made}: {without.Level} -> {with.Made}: {with.Level}"), Invariant($"the veterancy level of a produced object, without/with the science (StartingLevel {level})"));
        }
        var pair = NewPair(roster);
        foreach (var science in SciencesFor(item.Buff)) pair.B.World.GrantScience(Me, science);
        var reader = pair.Spawn(item.Object, Me, Centre, Centre);
        pair.Steps(2);
        var levelA = pair.A.World.FindEntity(reader)!.VeterancyLevel;
        var levelB = pair.B.World.FindEntity(reader)!.VeterancyLevel;
        var spawnedA = Expected(item.Object, pair.A.World.PlayerSciences(Me));
        var spawnedB = Expected(item.Object, pair.B.World.PlayerSciences(Me));
        return Result(item, levelA == spawnedA && levelB == spawnedB ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"{spawnedA} -> {spawnedB}"), Invariant($"{levelA} -> {levelB}"),
            Invariant($"the veterancy level of a new object, without/with the science (StartingLevel {level})"));
    }

    private static int LevelOf(string? text) => (text ?? string.Empty).ToUpperInvariant() switch { "VETERAN" => 1, "ELITE" => 2, "HEROIC" => 3, _ => 0 };

    private IEnumerable<string> SciencesFor(string science)
    {
        if (!_catalog.Sciences.TryGet(science, out var info)) yield break;
        foreach (var needed in info.PrerequisiteNames.SelectMany(SciencesFor)) yield return needed;
        yield return info.Name;
    }

    /// <summary>
    /// <c>ThingTemplate::calcCostToBuild</c> and <c>calcTimeToBuild</c> (no handicap, no faction modifier, full power, one factory): the money a production takes and the time until it
    /// is done, of the general's object and of its base faction's, each against its own <c>BuildCost</c> and <c>BuildTime</c>.
    /// </summary>
    private BuffResult Production(FactionRoster roster, BuffCase item)
    {
        var general = Produce(roster, item.Object);
        var baseRoster = _rosterOf(roster.BaseFaction);
        var origin = baseRoster.Find(item.Module);
        var counterpart = origin is null ? general with { Problem = "the base faction has no such object" } : Produce(baseRoster, item.Module);
        string Line(Produced value) => value.Problem ?? Invariant($"{value.Cost} in {value.Frames} frames");
        string Want(string name) => Invariant($"{ObjectField(name, "BuildCost")} in {ExpectedFrames(name)} frames");
        // Above 30 Hz the order lies inside a frame of the factory, whose production ends with one of its frames: a frame either way is the rate's grain, not the data's.
        var passed = general.Problem is null && counterpart.Problem is null
                     && general.Cost == (long)(ObjectNumber(item.Object, "BuildCost") ?? 0m) && Math.Abs(general.Frames - ExpectedFrames(item.Object)) <= 1
                     && counterpart.Cost == (long)(ObjectNumber(item.Module, "BuildCost") ?? 0m) && Math.Abs(counterpart.Frames - ExpectedFrames(item.Module)) <= 1;
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, $"{Want(item.Object)} (base {item.Module}: {Want(item.Module)})",
            $"{Line(general)} (base: {Line(counterpart)})", "the money a production takes and the original frames until it is done");
    }

    /// <summary>
    /// <c>SpecialPowerModule::onSpecialPowerCreation</c> starts the power's countdown when its carrier is made: it is ready after the power's <c>ReloadTime</c>. The general's power on its
    /// carrier and the base faction's on its own, each from the moment the carrier stands until the power is ready, against its <c>ReloadTime</c>.
    /// </summary>
    private BuffResult Reload(FactionRoster roster, BuffCase item)
    {
        var baseRoster = _rosterOf(roster.BaseFaction);
        var general = Recharge(roster, item.Buff, item.Object);
        var counterpart = Recharge(baseRoster, item.Module, baseRoster.Powers.First(value => value.Name.Equals(item.Module, StringComparison.OrdinalIgnoreCase)).Carriers[0]);
        long Want(string power) => (long)decimal.Ceiling((Number(PowerField(power, "ReloadTime")) ?? 0m) * OriginalFrame.PerSecond / 1000m);
        string Line((long Frames, string? Problem) value) => value.Problem ?? Invariant($"{value.Frames} frames");
        bool Fits((long Frames, string? Problem) value, long want) => value.Problem is null && Math.Abs(value.Frames - want) <= 1;
        var passed = Fits(general, Want(item.Buff)) && Fits(counterpart, Want(item.Module));
        return Result(item, passed ? BuffOutcome.Passed : BuffOutcome.Failed, Invariant($"{Want(item.Buff)} frames (base {item.Module}: {Want(item.Module)})"),
            $"{Line(general)} (base: {Line(counterpart)})", "from the carrier's appearance until the power is ready, its science owned");
    }

    private (long Frames, string? Problem) Recharge(FactionRoster roster, string power, string carrier)
    {
        if (!_catalog.SpecialPowers.TryGet(power, out var info)) return (0, $"unknown power {power}");
        var side = NewSide(roster);
        if (info.RequiredScience >= 0 && _catalog.Sciences.Sciences.FirstOrDefault(value => value.Index == info.RequiredScience) is { } science)
            foreach (var name in SciencesFor(science.Name)) side.World.GrantScience(Me, name);
        var id = side.Spawn(carrier, Me, Centre, Centre);
        var start = side.World.Tick - 1;
        var module = side.World.FindEntity(id)?.Modules.OfType<ISpecialPowerModule>().FirstOrDefault(value => value.PowerIndex == info.Index);
        if (module is null) return (0, $"{carrier} carries no module of {power}");
        var until = side.World.Tick + info.ReloadTicks + side.World.Time.WholeSecondsToTicks(30);
        while (side.World.Tick < until)
        {
            if (module.IsReady(side.World)) return ((long)decimal.Round((side.World.Tick - start) / (decimal)side.World.Time.OriginalFrames(1)), null);
            side.Steps(1);
        }
        return (0, "not ready");
    }

    private string? PowerField(string power, string field)
        => _graph.Definitions.OfType<SpecialPowerDefinition>().FirstOrDefault(value => value.OriginalName.Equals(power, StringComparison.OrdinalIgnoreCase)) is { } definition
           && definition.Fields.LastOrDefault(value => value.Name.Equals(field, StringComparison.OrdinalIgnoreCase)) is { } found ? Tokens(found.Value.OriginalText).FirstOrDefault() : null;

    /// <param name="Made">The object the production made (its template), empty while none appeared.</param>
    /// <param name="Level">The veterancy level the made object has.</param>
    private sealed record Produced(long Cost, long Frames, string? Problem, string Made = "", int Level = 0);

    /// <summary><c>calcTimeToBuild</c>: <c>BuildTime</c> seconds in frames, truncated to an integer.</summary>
    private long ExpectedFrames(string name) => (long)decimal.Truncate((ObjectNumber(name, "BuildTime") ?? 0m) * OriginalFrame.PerSecond);

    private Produced Produce(FactionRoster roster, string name, IEnumerable<string>? sciences = null)
    {
        var entry = roster.Find(name);
        var producer = entry?.Origin.StartsWith("produced by ", StringComparison.Ordinal) == true ? entry.Origin["produced by ".Length..] : null;
        if (producer is null) return new Produced(0, 0, $"{name} has no producer in the roster");
        var template = TemplateOf(name);
        var side = NewSide(roster);
        foreach (var science in sciences ?? []) side.World.GrantScience(Me, science);
        var factory = side.Spawn(producer, Me, Centre, Centre);
        var row = 0;
        foreach (var line in template.Prerequisites)
        {
            if (line.IsScience)
            {
                foreach (var science in line.Alternatives.Take(1).SelectMany(SciencesFor)) side.World.GrantScience(Me, science);
                continue;
            }
            var chosen = line.Alternatives.FirstOrDefault(value => roster.Find(value) is not null) ?? line.Alternatives.FirstOrDefault();
            if (chosen is not null && !chosen.Equals(producer, StringComparison.OrdinalIgnoreCase)) side.Spawn(chosen, Me, Centre - 600 + 250 * row++, Centre - 600);
        }
        side.Seconds(2);
        var before = side.World.MoneyOf(Me);
        var answer = side.World.Submit(new QueueProductionCommand(Me, side.NextSequence(), side.World.Tick, factory, name));
        if (!answer.Accepted) return new Produced(0, 0, $"the queue refused: {answer.Reason}");
        var queued = side.World.Tick;
        var known = side.World.Entities.Select(entity => entity.Id.Value).ToHashSet();
        side.Steps(1);
        var cost = (before - side.World.MoneyOf(Me)).ToDecimal();
        var until = queued + side.World.Time.WholeSecondsToTicks(120);
        while (side.World.Tick < until)
        {
            side.Steps(1);
            var module = side.World.FindEntity(factory)?.Modules.OfType<ProductionUpdate>().FirstOrDefault();
            var done = module is null || !module.TryGetHead(out var head) || head.RemainingTicks == 0
                       || side.Events.Any(value => value.Kind == SimEventKind.ProductionCompleted);
            if (done)
            {
                // The command takes effect in the tick it was submitted for, and the entry advances in that tick too: the ticks stepped since.
                var ticks = side.World.Tick - queued;
                var frames = (long)decimal.Round(ticks / (decimal)side.World.Time.OriginalFrames(1));
                // What the production made, once it is out of its door.
                var appear = side.World.Tick + side.World.Time.WholeSecondsToTicks(15);
                while (side.World.Tick < appear)
                {
                    var made = side.World.Entities.Where(entity => !known.Contains(entity.Id.Value) && entity.Owner == Me && entity.Id != factory && !entity.Template.IsKindOf("PROJECTILE"))
                        .OrderBy(entity => entity.Id.Value).FirstOrDefault();
                    if (made is not null) return new Produced((long)cost, frames, null, made.Template.Name, made.VeterancyLevel);
                    side.Steps(1);
                }
                return new Produced((long)cost, frames, null);
            }
        }
        return new Produced((long)cost, 0, "not done in 120 s");
    }

    // ------------------------------------------------------------------------------------------------ the worlds

    /// <summary>One world of a probe and what it said.</summary>
    private sealed class Side(FactionBuffProbe probe, SimulationWorld world)
    {
        private ulong _sequence;
        public SimulationWorld World { get; } = world;
        public List<SimEvent> Events { get; } = [];
        public ulong NextSequence() => ++_sequence;

        public EntityId Spawn(string template, PlayerId owner, decimal x, decimal y)
        {
            var known = World.Entities.Select(entity => entity.Id.Value).ToHashSet();
            var answer = World.Submit(new SpawnCommand(PlayerId.System, NextSequence(), World.Tick, template, owner, Fixed.FromDecimal(x), Fixed.FromDecimal(y), Fixed.Zero, Fixed.Zero));
            if (!answer.Accepted) throw new ProbeSetupException($"spawning {template} was refused: {answer.Reason}");
            Steps(1);
            var made = World.Entities.Where(entity => !known.Contains(entity.Id.Value) && entity.Owner == owner
                                                      && (entity.Template.Name.Equals(template, StringComparison.OrdinalIgnoreCase) || World.Templates.IsOfType(entity.Template, template)))
                .OrderBy(entity => entity.Id.Value).FirstOrDefault() ?? throw new ProbeSetupException($"{template} did not appear");
            if (owner != Me) World.SetAttitude(made, (int)Attitude.Sleep);
            return made.Id;
        }

        public void Steps(int count)
        {
            for (var step = 0; step < count; step++)
            {
                Events.AddRange(World.Step().Events);
                probe.SecondsStepped += 1.0 / World.Time.TicksPerSecond;
            }
        }

        public void Seconds(int seconds) => Steps((int)World.Time.WholeSecondsToTicks(seconds));

        /// <summary>
        /// The first damage the victim takes, with the weapon the source fired last before it (or in the same tick), the source's bonus conditions then and the damage that shot
        /// computed (<see cref="SimEventKind.WeaponFired"/>'s value, before the armour); <c>Killed</c> when the hit killed the victim, so its amount is clipped by the health.
        /// </summary>
        public (decimal Amount, string Weapon, uint Conditions, bool Killed, decimal Computed)? FirstHit(EntityId victim, EntityId source, int seconds)
        {
            Events.Clear();
            var weapon = string.Empty;
            var fired = false;
            var computed = 0m;
            var conditions = World.FindEntity(source)?.WeaponBonusConditions ?? 0u;
            var until = World.Tick + World.Time.WholeSecondsToTicks(seconds);
            var second = World.Time.TicksPerSecond;
            var started = World.Tick;
            while (World.Tick < until)
            {
                // The order to attack until told otherwise, given again every second until a shot leaves (an order refused while the weapon reloads is given again, as a player clicks again).
                if (!fired && (World.Tick - started) % second == 0)
                    World.Submit(new FireWeaponCommand(Me, NextSequence(), World.Tick, source, victim, WeaponSlot.Best, FireOrigin.Player, FireWeaponCommand.NoMaxShotsLimit));
                var start = Events.Count;
                var before = World.FindEntity(source)?.WeaponBonusConditions ?? conditions;
                Steps(1);
                for (var index = start; index < Events.Count; index++)
                    if (Events[index] is { Kind: SimEventKind.WeaponFired } shot && shot.Entity == source)
                    {
                        fired = true;
                        weapon = shot.Text;
                        computed = Fixed.FromRaw(shot.Value).ToDecimal();
                        conditions = before;
                    }
                for (var index = start; index < Events.Count; index++)
                    if (Events[index] is { Kind: SimEventKind.EntityDamaged } hit && hit.Entity == victim)
                        return (Fixed.FromRaw(hit.Value).ToDecimal(), weapon, conditions, World.FindEntity(victim) is not { EffectivelyDead: false }, computed);
            }
            return null;
        }

        /// <summary>The shots of the source over the time: the tick, the weapon and the source's bonus conditions as the shot left (those it had before the tick).</summary>
        public List<(long Tick, string Weapon, uint Conditions)> ShotsOver(EntityId source, int seconds)
        {
            Events.Clear();
            var shots = new List<(long Tick, string Weapon, uint Conditions)>();
            var until = World.Tick + World.Time.WholeSecondsToTicks(seconds);
            while (World.Tick < until)
            {
                var start = Events.Count;
                var before = World.FindEntity(source)?.WeaponBonusConditions ?? 0u;
                Steps(1);
                for (var index = start; index < Events.Count; index++)
                    if (Events[index] is { Kind: SimEventKind.WeaponFired } shot && shot.Entity == source) shots.Add((shot.Tick, shot.Text, before));
            }
            return shots;
        }

        /// <summary>Whether the source fires <paramref name="weapon"/> within the time.</summary>
        public bool Fired(EntityId source, string weapon, int seconds)
        {
            Events.Clear();
            var until = World.Tick + World.Time.WholeSecondsToTicks(seconds);
            while (World.Tick < until)
            {
                Steps(1);
                if (Events.Any(item => item.Kind == SimEventKind.WeaponFired && item.Entity == source && item.Text.Equals(weapon, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        /// <summary>The most the object covers in one second while it moves, in units per second.</summary>
        public decimal TopSpeed(EntityId id, int seconds)
        {
            var places = new List<(decimal X, decimal Y)>();
            var second = World.Time.TicksPerSecond;
            var until = World.Tick + World.Time.WholeSecondsToTicks(seconds);
            var best = 0m;
            while (World.Tick < until)
            {
                Steps(1);
                if (World.FindEntity(id) is not { } entity) break;
                places.Add((entity.X.ToDecimal(), entity.Y.ToDecimal()));
                if (places.Count > second)
                {
                    var (x0, y0) = places[^(second + 1)];
                    var (x1, y1) = places[^1];
                    var distance = (decimal)Math.Sqrt((double)((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
                    if (!entity.HasMoveTarget) break;
                    best = Math.Max(best, distance);
                }
            }
            return best;
        }

        /// <summary>The money of the collector's first delivery.</summary>
        public long? FirstDelivery(EntityId collector, int seconds)
        {
            Events.Clear();
            var until = World.Tick + World.Time.WholeSecondsToTicks(seconds);
            while (World.Tick < until)
            {
                Steps(1);
                if (Events.FirstOrDefault(item => item.Kind == SimEventKind.SupplyDeposited && (item.Entity == collector || item.Other == collector)) is { Kind: SimEventKind.SupplyDeposited } delivery)
                    return delivery.Value;
            }
            return null;
        }
    }

    /// <summary>The control world (<see cref="A"/>) and the test world (<see cref="B"/>), built alike and given the same orders.</summary>
    private sealed class Pair(Side a, Side b)
    {
        public Side A { get; } = a;
        public Side B { get; } = b;

        public EntityId Spawn(string template, PlayerId owner, decimal x, decimal y)
        {
            var id = A.Spawn(template, owner, x, y);
            var other = B.Spawn(template, owner, x, y);
            if (id != other) throw new ProbeSetupException($"{template} has the id {id.Value} in one world and {other.Value} in the other");
            return id;
        }

        /// <summary>Spawns <paramref name="template"/> <paramref name="gap"/> ahead of <paramref name="shooter"/> - where the first world's shooter stands and heads - in both worlds.</summary>
        public EntityId SpawnAhead(string template, PlayerId owner, EntityId shooter, decimal gap)
        {
            var source = A.World.FindEntity(shooter) ?? throw new ProbeSetupException("the shooter is gone");
            var distance = Fixed.FromDecimal(gap);
            var x = (source.X + FixedTrig.Cos(source.Angle) * distance).ToDecimal();
            var y = (source.Y + FixedTrig.Sin(source.Angle) * distance).ToDecimal();
            return Spawn(template, owner, x, y);
        }

        public ulong Next(SimulationWorld world) => ReferenceEquals(world, A.World) ? A.NextSequence() : B.NextSequence();

        public void Both(Action<SimulationWorld> action)
        {
            action(A.World);
            action(B.World);
        }

        public void Steps(int count)
        {
            A.Steps(count);
            B.Steps(count);
        }

        public void Seconds(int seconds)
        {
            A.Seconds(seconds);
            B.Seconds(seconds);
        }

        /// <summary>A second for what was made to settle (a payload arriving, a structure's first update).</summary>
        public void Settle() => Seconds(1);

        public void Events()
        {
            A.Events.Clear();
            B.Events.Clear();
        }

        /// <summary>The test world gets the upgrade (and the others a module needs all of): on the object for an object upgrade, on the player for a player upgrade.</summary>
        public void Upgrade(string upgrade, EntityId reader, ImmutableArray<string>? all = null)
        {
            foreach (var name in (all is { IsDefaultOrEmpty: false } names ? names : [upgrade]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!B.World.Templates.Upgrades.TryGet(name, out var info)) throw new ProbeSetupException($"unknown upgrade {name}");
                if (info.Scope == UpgradeScope.Object && !reader.IsValid) throw new ProbeSetupException($"{name} is an object's upgrade: it needs the object first");
                B.World.CompleteUpgrade(Me, info.Scope == UpgradeScope.Object ? reader : null, info.Name);
            }
        }
    }

    private Pair NewPair(FactionRoster roster) => new(NewSide(roster), NewSide(roster));

    private Side NewSide(FactionRoster roster)
    {
        Worlds++;
        var enemy = EnemyOf(roster.BaseFaction);
        var players = ImmutableArray.Create(
            new PlayerConfiguration(Me, roster.Faction, roster.Faction),
            new PlayerConfiguration(Enemy, enemy, enemy, 0, true),
            new PlayerConfiguration(Civilian, "FactionCivilian", "FactionCivilian", 0, true));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), _catalog.Rate, 419u, players, "faction-buffs", _fingerprint, _terrain.Fingerprint), _catalog, _terrain);
        world.ConfigureMatchOptions(buildTimePercent: 100, neutralRespawnSeconds: 0);
        world.SetMoney(Me, Fixed.FromInt(1_000_000));
        world.SetMoney(Enemy, Fixed.FromInt(10_000));
        // Full power for whatever needs it (a ratio above one: no slower production, no structure underpowered).
        world.AddEnergyProduction(Me, 1000);
        foreach (var other in new[] { Me, Enemy })
        {
            world.SetRelationship(other, Civilian, Relationship.Neutral);
            world.SetRelationship(Civilian, other, Relationship.Neutral);
        }
        return new Side(this, world);
    }

    private static string EnemyOf(string baseFaction) => baseFaction.Equals("FactionAmerica", StringComparison.OrdinalIgnoreCase) ? "FactionChina"
        : baseFaction.Equals("FactionChina", StringComparison.OrdinalIgnoreCase) ? "FactionGLA" : "FactionAmerica";

    /// <summary>The enemy's objects a shot is tried at, in this order: its command centre (much health), a vehicle, a soldier, a production building - unarmed ones first.</summary>
    private List<string> EnemyTargets(FactionRoster roster)
    {
        var enemy = _rosterOf(EnemyOf(roster.BaseFaction));
        var templates = enemy.Objects.Select(value => _catalog.TryGet(value.Name, out var template) ? template : null).OfType<ObjectTemplate>()
            .Where(value => value.IsInstantiable)
            // Nothing that hides (an innately stealthed hero cannot be aimed at), unarmed first, then by name.
            .OrderBy(value => value.Modules.Any(module => module.Data is StealthData { InnateStealth: true }) ? 1 : 0)
            .ThenBy(value => value.WeaponSets.Any(set => set.WeaponCount > 0) ? 1 : 0).ThenBy(value => value.Name, StringComparer.Ordinal).ToList();
        var result = new List<string>();
        if (_catalog.Factions.TryGet(enemy.Faction, out var faction) && faction.StartingBuilding.Length > 0) result.Add(faction.StartingBuilding);
        if (templates.FirstOrDefault(value => value.IsKindOf("VEHICLE") && !value.IsKindOf("AIRCRAFT") && value.Locomotor is not null && !value.IsKindOf("HUGE_VEHICLE")) is { } vehicle) result.Add(vehicle.Name);
        // A soldier that is built (a pilot is no target: it heads off to its next vehicle).
        if (templates.FirstOrDefault(value => value.IsKindOf("INFANTRY") && value.BuildCost > Fixed.Zero) is { } soldier) result.Add(soldier.Name);
        if (templates.FirstOrDefault(value => value.IsKindOf("STRUCTURE") && !value.IsKindOf("COMMANDCENTER") && value.Modules.Any(module => module.Data is ProductionData)) is { } building)
            result.Add(building.Name);
        return result;
    }

    /// <summary>How far east of the shooter a target stands: inside the reach of its primary weapon, clear of both footprints.</summary>
    private decimal Gap(string shooter, string target)
    {
        var source = TemplateOf(shooter);
        var victim = TemplateOf(target);
        var range = source.DefaultWeapon >= 0 ? _catalog.Weapons[source.DefaultWeapon].AttackRange.ToDecimal() : 100m;
        var radii = source.BoundingCircleRadius.ToDecimal() + victim.BoundingCircleRadius.ToDecimal();
        return radii + Math.Clamp(range * 0.6m, 20m, 250m);
    }

    /// <summary>
    /// The shooter cannot turn on the spot (its locomotor has a minimum speed: a jet circling where it was made). Such a shooter fires as soon as its target is in reach, and the
    /// target east of it is in reach between the bounding circles (#422) while it flies away from it - the missiles miss (#433). Its target is put as far ahead of it instead, after it settled (<see cref="Pair.SpawnAhead"/>).
    /// </summary>
    private bool FliesOnly(string shooter) => TemplateOf(shooter).Locomotor is { MinSpeed.Raw: > 0 };

    // ------------------------------------------------------------------------------------------------ the data

    private ObjectTemplate TemplateOf(string name) => _catalog.TryGet(name, out var template) ? template : throw new ProbeSetupException($"unknown object {name}");

    private ModuleConfiguration ModuleOf(BuffCase item)
    {
        var tag = item.Module.Split(':') is [_, var value] ? value : string.Empty;
        return _objects.TryGetValue(item.Object, out var definition) && definition.Modules.FirstOrDefault(module => module.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) is { } found
            ? found : throw new ProbeSetupException($"no module {item.Module} on {item.Object}");
    }

    private static string? Text(ModuleConfiguration module, string field)
        => module.Fields.LastOrDefault(value => value.Name.Equals(field, StringComparison.OrdinalIgnoreCase)) is { } found ? Tokens(found.Value.OriginalText).FirstOrDefault() : null;

    private static ImmutableArray<string> Names(ModuleConfiguration module, string field)
        => [..module.Fields.Where(value => value.Name.Equals(field, StringComparison.OrdinalIgnoreCase)).SelectMany(value => Tokens(value.Value.OriginalText))];

    private static decimal? Decimal(ModuleConfiguration module, string field)
        => Number(module.Fields.LastOrDefault(value => value.Name.Equals(field, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText);

    private static bool Bool(ModuleConfiguration module, string field) => Text(module, field) is { } value && value.Equals("Yes", StringComparison.OrdinalIgnoreCase);

    private string? ObjectField(string name, string field)
        => _objects.TryGetValue(name, out var definition) ? definition.Fields.LastOrDefault(value => value.Name.Equals(field, StringComparison.OrdinalIgnoreCase)) is { } found
            ? Tokens(found.Value.OriginalText).FirstOrDefault() : null : null;

    private decimal? ObjectNumber(string name, string field) => Number(ObjectField(name, field));

    /// <summary>A field's tokens, without the comment.</summary>
    private static string[] Tokens(string text) => text.Split(';', 2)[0].Split([' ', '\t', '='], StringSplitOptions.RemoveEmptyEntries);

    private static decimal? Number(string? text)
    {
        var token = text is null ? null : Tokens(text).FirstOrDefault()?.TrimEnd('%');
        return token is not null && decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static ulong StatusMask(IEnumerable<string> names)
    {
        var mask = 0UL;
        foreach (var name in names)
            if (NameTables.ObjectStatus.TryIndexOf(name, out var index)) mask |= 1UL << index;
        return mask;
    }

    private static string StatusNames(ulong status)
    {
        var names = new List<string>();
        for (var index = 0; index < NameTables.ObjectStatus.Count; index++)
            if ((status & (1UL << index)) != 0) names.Add(NameTables.ObjectStatus.Canonical(index));
        return names.Count == 0 ? "none" : string.Join(" ", names);
    }

    private static (decimal Max, decimal Health) HealthOf(Side side, EntityId id)
        => side.World.FindEntity(id)?.Body is { } body ? (body.MaxHealth.ToDecimal(), body.Health.ToDecimal()) : (0m, 0m);

    private static bool Near(decimal measured, decimal expected, decimal tolerance) => Math.Abs(measured - expected) <= tolerance;

    private static string Ratio(Fixed taken, Fixed amount) => Invariant($"{taken.ToDecimal() / amount.ToDecimal():0.###}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static BuffResult Result(BuffCase item, BuffOutcome outcome, string expected, string measured, string detail)
        => new(item.Faction, item.Buff, item.Effect, item.Object, item.Module, outcome, expected, measured, detail);
}
