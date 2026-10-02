using CnCNew.Data.Definitions;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>Phase 12: the modules the campaign missions needed to make their named objects creatable.</summary>
static partial class Tests
{
    public static void Phase12ModulesCreateAndBehave()
    {
        var session = StartMission([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)], objects: [
            Placed("AlphaTrap", 100m, 100m, "teamA", "Trap"), Placed("BetaSoldier", 130m, 100m, "teamB", "Victim"),
            Placed("AlphaTech", 300m, 300m, "teamA", "Tech"),
            Placed("AlphaGunner", 100m, 300m, "teamA", "Gunner"), Placed("BetaSoldier", 150m, 300m, "teamB", "Target"),
            Placed("AlphaHorde", 400m, 100m, "teamA", "H1"), Placed("AlphaHorde", 420m, 100m, "teamA", "H2"), Placed("AlphaHorde", 440m, 100m, "teamA", "H3"), Placed("AlphaHorde", 550m, 380m, "teamA", "Loner")], options: MissionOptions.Standard with { CombatIntervalTicks = 0 });
        Check.That(session.SkippedObjects.IsEmpty, "Every module of the new fixture objects is implemented (nothing was skipped): " + string.Join(",", session.SkippedObjects));

        // FireWeaponWhenDamagedBehavior: a damaging hit makes the trap fire its reaction weapon at itself, hurting the enemy next to it.
        var victim = NamedIn(session, "Victim")!;
        var trap = NamedIn(session, "Trap")!;
        Run(session, 2);
        var before = victim.Body!.Health;
        session.World.Damage(trap.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(10), DamageTypes.Explosion, DeathTypes.Normal, false));
        Run(session, 3);
        Check.That(victim.Body.Health < before, $"The damaged trap hurt the enemy standing next to it ({before} -> {victim.Body.Health}).");
        var again = victim.Body.Health;
        session.World.Damage(trap.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(10), DamageTypes.Penalty, DeathTypes.Normal, false));
        Run(session, 3);
        Check.That(victim.Body.Health == again, "A damage type outside DamageTypes does not set the reaction weapon off.");

        // HordeUpdate: three infantry in a cluster form a horde (HORDE weapon bonus condition), the one far away does not.
        Run(session, 70);
        Check.That(NameTablesPhase5.WeaponBonusCondition.TryIndexOf("HORDE", out var hordeBit), "The HORDE condition exists.");
        bool InHorde(string name) => (NamedIn(session, name)!.WeaponBonusConditions & (1u << hordeBit)) != 0;
        Check.That(InHorde("H1") && InHorde("H2") && InHorde("H3") && !InHorde("Loner"), $"The cluster is a horde, the loner is not (H1 {InHorde("H1")}, H2 {InHorde("H2")}, H3 {InHorde("H3")}, Loner {InHorde("Loner")}).");

        // FireWeaponPower: two shots at the target object; BaikonurLaunchPower: a detonation object appears at the target location.
        var gunner = NamedIn(session, "Gunner")!;
        var target = NamedIn(session, "Target")!;
        var owner = gunner.Owner;
        var health = target.Body!.Health;
        Check.That(session.Issue(owner, (sequence, tick) => new UseSpecialPowerCommand(owner, sequence, tick, gunner.Id, "GunPower", SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero)).Accepted, "The gun power is accepted.");
        Run(session, 40);
        Check.That(health - target.Body.Health >= Fixed.FromInt(40) || target.EffectivelyDead, $"The power fired its two shots (health {health} -> {target.Body.Health}).");
        var soldiers = session.World.Entities.Count(entity => entity.Template.Name == "AlphaSoldier");
        Check.That(session.Issue(owner, (sequence, tick) => new UseSpecialPowerCommand(owner, sequence, tick, gunner.Id, "LaunchPower", SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(200), Fixed.FromInt(250))).Accepted, "The launch power is accepted.");
        Run(session, 3);
        var created = session.World.Entities.Where(entity => entity.Template.Name == "AlphaSoldier").ToArray();
        Check.That(created.Length == soldiers + 1 && created.Any(entity => entity.Owner == owner && entity.X == Fixed.FromInt(200) && entity.Y == Fixed.FromInt(250)), "The detonation object stands where the launch hit, owned by the launching player.");

        // TechBuildingBehavior: destroyed, the building belongs to nobody.
        var tech = NamedIn(session, "Tech")!;
        session.World.Kill(tech.Id);
        Run(session, 3);
        Check.That(tech.Owner.IsSystem, "A destroyed tech building is neutral.");

        // All of it survives a save: the restored mission continues identically.
        var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)], objects: [
            Placed("AlphaTrap", 100m, 100m, "teamA", "Trap"), Placed("AlphaHorde", 400m, 100m, "teamA", "H1"), Placed("AlphaHorde", 420m, 100m, "teamA", "H2"), Placed("AlphaHorde", 440m, 100m, "teamA", "H3")]);
        var (original, restored, _) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 400);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, "A mission with the new modules continues identically after save/restore.");
    }
}
