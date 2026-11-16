using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (issue 396): the built-in AI leaves the slaves of a spawner to their master. An angry mob (the nexus and the members its <c>SpawnBehavior</c> makes) and a
    /// stinger site (a structure whose <c>SlavedUpdate</c> soldiers it makes) of a GLA computer player: the AI leads the nexus and counts it as army, never a member or a
    /// stinger soldier, which the original's AI orders only through their master either. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSkirmishAiLeavesSlavesToTheirMaster(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "ai-slaves");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-ai-slaves", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            var computer = new PlayerId(1);
            void Spawn(string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(computer, ++sequence, world.Tick, template, computer, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("GLAInfantryAngryMobNexus", 300, 300);
            Spawn("GLAStingerSite", 500, 300);
            var aiOptions = new SkirmishAiOptions(computer, 11, Fixed.FromInt(300), Fixed.FromInt(300), "GLA", string.Empty, ImmutableArray<(Fixed X, Fixed Y)>.Empty);
            var ai = new SkirmishAi(world, catalog, aiOptions, world.Submit);
            var end = world.Frames(30 * 20);
            while (world.Tick < end)
            {
                ai.Update();
                world.Step();
            }

            static bool IsSlave(Entity entity)
            {
                foreach (var module in entity.Modules)
                    if (module is ISlavedModule { Slaver.IsValid: true }) return true;
                return false;
            }
            var slaves = world.Entities.Where(entity => entity.Owner == computer && !entity.EffectivelyDead && IsSlave(entity)).ToList();
            var members = slaves.Count(entity => entity.Template.Name.Contains("AngryMob", StringComparison.Ordinal));
            var stingers = slaves.Count(entity => entity.Template.Name.Contains("Stinger", StringComparison.Ordinal));
            Check.That(members > 0 && stingers > 0, $"{rate}: the mob made its members ({members}) and the site its stinger soldiers ({stingers}).");
            var nexus = world.Entities.Single(entity => entity.Template.Name == "GLAInfantryAngryMobNexus");
            Check.That(ai.LeadsUnit(nexus), $"{rate}: the AI leads the mob's nexus.");
            foreach (var slave in slaves)
                Check.That(!ai.LeadsUnit(slave), $"{rate}: the AI does not lead the slave {slave.Template.Name} ({slave.Id.Value}).");
            var state = ai.DescribeState();
            Check.That(state.Contains(", army 1,", StringComparison.Ordinal), $"{rate}: the AI counts the nexus alone as its army ({state}).");
        }
    }
}

static partial class Tests
{
    private const string HiddenBystanderIni = """
        Object HiddenBystander
          KindOf = INFANTRY
          VisionRange = 60.0
          Locomotor = SET_NORMAL RoamWalk
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    /// <summary>
    /// Issue 397: <c>AIAttackSquadState::chooseVictim</c> takes the attacked team's <c>Squad::getLiveObjects</c> on easy and hard, which leaves out every member that is not selectable. The team
    /// holds a selectable bystander (joined first) and one whose template is not <c>SELECTABLE</c> (joined last): hard, which takes the last that joined, and easy, a random one, both take the
    /// selectable one; normal still takes the closest member, selectable or not (the partition's closest object of the squad).
    /// </summary>
    private static (bool Near, bool Hidden) SquadVictimAmongUnselectable(CnCNew.Simulation.Scripting.ScriptDifficulty difficulty, decimal hiddenX)
    {
        var files = new Dictionary<string, string>(RoamFiles()) { ["Data/INI/Object/HiddenBystander.ini"] = HiddenBystanderIni };
        var (map, fixture, terrain) = MissionMap([Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_ATTACK_TEAM", Scr.Unit("Hunter"), Scr.Team("teamB"))], oneShot: true)],
            objects: [Placed("MoodGunner", 100m, 200m, "teamA", "Hunter"), Placed("Bystander", 100m, 330m, "teamB", "Near"), Placed("HiddenBystander", hiddenX, 200m, "teamB", "Hidden")], extraFiles: files);
        var session = CnCNew.Simulation.Mission.MissionSession.Start(CnCNew.Simulation.Mission.MissionOptions.Standard with { Difficulty = difficulty }, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(450));
        return (Hurt(session, "Near"), Hurt(session, "Hidden"));
    }

    public static void SquadVictimSkipsWhatIsNotSelectable()
    {
        Check.That(SquadVictimAmongUnselectable(CnCNew.Simulation.Scripting.ScriptDifficulty.Hard, 320m) is (true, false), "On hard the attacker takes the last selectable member, not the unselectable one that joined after it.");
        Check.That(SquadVictimAmongUnselectable(CnCNew.Simulation.Scripting.ScriptDifficulty.Easy, 320m) is (true, false), "On easy the random member is a selectable one.");
        Check.That(SquadVictimAmongUnselectable(CnCNew.Simulation.Scripting.ScriptDifficulty.Normal, 160m) is (false, true), "On normal the closest member is taken, selectable or not.");
    }

    /// <summary>
    /// Issue 397: <c>Object::isSelectable</c> in the simulation. A spawner's drone carries <c>UNSELECTABLE</c> while its master lives (<c>SlavedUpdate::startSlavedEffects</c>) and is not
    /// selectable; the angry mob's members do not (<c>MobMemberSlavedUpdate</c> leaves the status out), so a member of a <c>SELECTABLE</c> template is; the masters are.
    /// </summary>
    public static void SlavesAreSelectableAsTheOriginalSays()
    {
        var world = SlaveWorld(out _, out var feeder);
        feeder.Spawn(1, "SvMaster", x: 100, y: 100);
        feeder.Spawn(1, "SvNexus", x: 300, y: 300);
        StepN(world, 40);
        var drone = SlaveEntity(world, "SvScoutDrone");
        var member = SlaveEntity(world, "SvMobMember");
        Check.That(SimulationWorld.SlaverOf(drone).IsValid && SimulationWorld.SlaverOf(member).IsValid, "Both are slaves of a living master.");
        Check.That(SimulationWorld.IsSelectable(SlaveEntity(world, "SvMaster")) && SimulationWorld.IsSelectable(SlaveEntity(world, "SvNexus")), "The masters are selectable.");
        Check.That(!SimulationWorld.IsSelectable(drone), "The drone of a living master is not selectable.");
        Check.That(SimulationWorld.IsSelectable(member), "A mob member of a SELECTABLE template is selectable.");
    }
}
