using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Issue #429, the third difference: what <c>Object::isAbleToAttack</c> refuses - <c>OBJECT_STATUS_NO_ATTACK</c>, and a stinger soldier whose site is subdued.</summary>
static partial class Tests
{
    /// <summary>
    /// <c>Object::isAbleToAttack</c> is false first of all for an object with <c>OBJECT_STATUS_NO_ATTACK</c> ("Absolute override to being able to attack"; a flight deck without
    /// aircraft, a prone soldier): such an enemy gunner neither repulses a civilian (<c>PartitionFilterRepulsor::allow</c>) nor picks it as a victim; the same gunner without the status
    /// does both. The port did not look at the status. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AnObjectWithNoAttackIsNotAbleToAttack()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)],
                objects: [Placed("Civilian", 150m, 100m, "teamB", "ByBarred"), Placed("MoodGunner", 200m, 100m, "teamA", "Barred"),
                          Placed("Civilian", 150m, 300m, "teamB", "ByFree"), Placed("MoodGunner", 200m, 300m, "teamA", "Free")], extraFiles: RoamFiles(), rate: rate);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            session.World.SetStatus(NamedIn(session, "Barred")!.Id, 1UL << ObjectStatusBits.Index("NO_ATTACK"));
            Run(session, session.Frames(70));
            var (byBarred, byFree) = (NamedIn(session, "ByBarred")!, NamedIn(session, "ByFree")!);
            Check.That(byFree.Panicking && byFree.Body!.Health < byFree.Body.MaxHealth, $"{rate}: setup - the free gunner repulses its civilian and fires at it ({byFree.Body!.Health}).");
            Check.That(!byBarred.Panicking, $"{rate}: a gunner with NO_ATTACK is not able to attack: it does not repulse.");
            Check.That(byBarred.Body!.Health == byBarred.Body.MaxHealth, $"{rate}: a gunner with NO_ATTACK does not fire ({byBarred.Body.Health}).");
        }
    }

    /// <summary>
    /// Real data: <c>Object::isAbleToAttack</c> is false for a <c>SPAWNS_ARE_THE_WEAPONS</c> (or <c>PORTABLE_STRUCTURE</c>) infantry whose slaver - the first slaved module's
    /// <c>getSlaverID</c> - is <c>DISABLED_SUBDUED</c> ("if my stinger site is subdued, so am I"). A GLA Stinger Site that a microwave keeps subdued (subdual damage beyond its
    /// health; the soldiers themselves are not subdued: its body passes no subdual damage on) has three soldiers that fire at no enemy in their range; the same site, not subdued,
    /// has soldiers that do. The port's soldiers fired from a subdued site. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealStingerSoldiersOfASubduedSiteDoNotAttack(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        // The frame of the first hit on the enemy tank (-1 for none), and whether the site stayed subdued and its soldiers did not.
        (long FirstHit, bool SiteSubdued, bool SoldiersSubdued) Run(SimulationRate rate, bool subdue)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "stinger");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "stinger", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "GLAStingerSite", owner: 1, x: 300, y: 600).Accepted, "The stinger site spawns.");
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            var site = world.Entities.Single(value => value.Template.Name == "GLAStingerSite");
            var soldiers = world.Entities.Where(value => value.Template.Name == "GLAInfantryStingerSoldier").ToArray();
            Check.That(soldiers.Length == 3, $"{rate}: the site has its three soldiers ({soldiers.Length}).");
            // The microwave: subdual damage up to the site's cap, renewed every half second before the site's own drain (100 per 500 ms) takes it under its health.
            void Subdue()
            {
                if (!subdue) return;
                feeder.Damage(site.Id.Value, 1200m, damageType: DamageTypes.SubdualUnresistable);
                world.Step();
            }
            Subdue();
            // An enemy tank within the soldiers' rockets (225).
            Check.That(feeder.Spawn(2, "AmericaTankCrusader", owner: 2, x: 500, y: 600, angle: FixedTrig.Pi).Accepted, "The Crusader spawns.");
            world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "AmericaTankCrusader");
            var (siteSubdued, soldiersSubdued) = (true, false);
            long firstHit = -1;
            var start = world.Tick;
            for (var frame = 0; frame < 300; frame++)
            {
                if (frame % 15 == 0) Subdue();
                for (var step = 0; step < world.Time.TicksPerOriginalFrame; step++) world.Step();
                siteSubdued &= site.Subdued;
                soldiersSubdued |= soldiers.Any(value => value.Subdued);
                if (firstHit < 0 && tank.Body!.Health < tank.Body.MaxHealth) firstHit = (world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            return (firstHit, siteSubdued, soldiersSubdued);
        }

        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var free = Run(rate, subdue: false);
            Check.That(free.FirstHit > 0, $"{rate}: setup - the soldiers of a site that is not subdued fire at the tank (first hit in frame {free.FirstHit}).");
            var subdued = Run(rate, subdue: true);
            Check.That(subdued.SiteSubdued && !subdued.SoldiersSubdued, $"{rate}: setup - the site stays subdued, its soldiers are not (site {subdued.SiteSubdued}, soldiers {subdued.SoldiersSubdued}).");
            Check.That(subdued.FirstHit < 0, $"{rate}: the soldiers of a subdued site are not able to attack: nobody hits the tank (first hit in frame {subdued.FirstHit}).");
        }
    }
}
