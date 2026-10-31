using System.Collections.Generic;
using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// D-15: the rank of a general as the original has it (<c>Player::addSkillPoints</c>, <c>setRankLevel</c>, <c>resetRank</c>; <c>docs/SIMULATION-CORE.md</c> §67): skill points from
/// kills, abilities and scripts raise the level, a level grants its sciences and purchase points, and a step down loses everything.
/// </summary>
static partial class Tests
{
    private const string RankIni = """
        Science SCIENCE_RankOne
          IsGrantable = No
        End
        Science SCIENCE_RankTwo
          IsGrantable = No
        End
        Science SCIENCE_RankThree
          IsGrantable = No
        End
        Science SCIENCE_BornWith
          IsGrantable = No
        End

        Rank 1
          RankName = GUI:Rank1
          SkillPointsNeeded = 0
          SciencesGranted = SCIENCE_RankOne
          SciencePurchasePointsGranted = 1
        End
        Rank 2
          RankName = GUI:Rank2
          SkillPointsNeeded = 800
          SciencesGranted = SCIENCE_RankTwo
          SciencePurchasePointsGranted = 1
        End
        Rank 3
          RankName = GUI:Rank3
          SkillPointsNeeded = 1500
          SciencesGranted = SCIENCE_RankThree
          SciencePurchasePointsGranted = 3
        End

        PlayerTemplate RankedFaction
          Side = Ranked
          PlayableSide = Yes
          IntrinsicSciences = SCIENCE_BornWith
          IntrinsicSciencePurchasePoints = 2
        End
        PlayerTemplate CivilianFaction
          Side = Civilian
        End

        Object RankVictim
          KindOf = SELECTABLE INFANTRY
          ExperienceValue = 40 50 60 70
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object RankPrize
          KindOf = SELECTABLE VEHICLE
          ExperienceValue = 40 50 60 70
          SkillPointValue = 900 0 0 0
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> RankFiles() => new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/RankFixture.ini"] = RankIni };

    private static SimulationWorld RankWorld(SyntheticFixture fixture, string victimFaction = "RankedFaction")
    {
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", "RankedFaction"), new(new PlayerId(2), "Beta", victimFaction, 1), new(new PlayerId(3), "Gamma")];
        return new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
    }

    private static bool Holds(SimulationWorld world, PlayerId player, string name) => world.Sciences.TryGet(name, out var science) && world.PlayerSciences(player).Has(science.Index);

    public static void RankStartAndLevels()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RankFiles());
        Check.That(fixture.Templates.Ranks.LevelCount == 3 && fixture.Templates.Ranks.At(0) is null && fixture.Templates.Ranks.At(4) is null, "The levels are read from the Rank blocks.");
        var world = RankWorld(fixture);
        var alpha = new PlayerId(1);
        Check.That(world.RankLevelOf(alpha) == 1 && world.SkillPointsOf(alpha) == 0, "A player starts at level 1.");
        Check.That(world.SciencePointsOf(alpha) == 3 && Holds(world, alpha, "SCIENCE_BornWith") && Holds(world, alpha, "SCIENCE_RankOne"), "Level 1 adds its point to the intrinsic ones, its science to the intrinsic one.");
        Check.That(world.RankLevelOf(new PlayerId(3)) == 0 && !world.AddSkillPoints(new PlayerId(3), 5000), "Without a faction there is no rank.");

        Check.That(!world.AddSkillPoints(alpha, 799) && world.RankLevelOf(alpha) == 1 && world.NextLevelPointsOf(alpha) == 800, "799 points are not yet level 2.");
        Check.That(world.AddSkillPoints(alpha, 1) && world.RankLevelOf(alpha) == 2, "The 800th point is level 2.");
        Check.That(world.SciencePointsOf(alpha) == 4 && Holds(world, alpha, "SCIENCE_RankTwo"), "A level grants its point and its science.");
        Check.That(world.AddSkillPoints(alpha, 100_000) && world.RankLevelOf(alpha) == 3 && world.SkillPointsOf(alpha) == 1500 && world.NextLevelPointsOf(alpha) == -1,
            $"The points stop at the lowest of the last level (were {world.SkillPointsOf(alpha)}).");
        Check.That(world.SciencePointsOf(alpha) == 7, "Level 3 grants three.");

        Check.That(world.SetRankLevel(alpha, 1) && world.RankLevelOf(alpha) == 1 && world.SkillPointsOf(alpha) == 0, "A step down starts again from level 1.");
        Check.That(world.SciencePointsOf(alpha) == 3 && !Holds(world, alpha, "SCIENCE_RankTwo") && Holds(world, alpha, "SCIENCE_BornWith") && Holds(world, alpha, "SCIENCE_RankOne"), "A step down loses everything above level 1.");
        Check.That(world.SetRankLevel(alpha, 9) && world.RankLevelOf(alpha) == 3 && world.SkillPointsOf(alpha) == 1500 && !world.SetRankLevel(alpha, 3), "A level beyond the last is the last, and no change is no change.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash && restored.IsPlayableSide(alpha), "A save keeps the faction of a player.");
        Check.That(restored.SetRankLevel(alpha, 1) && restored.SciencePointsOf(alpha) == 3 && Holds(restored, alpha, "SCIENCE_BornWith"), "A step down after a save returns to the intrinsic values as well.");
    }

    public static void RankLimitAndModifier()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RankFiles());
        var world = RankWorld(fixture);
        var alpha = new PlayerId(1);
        Check.That(world.RankLevelLimit == -1, "Nothing limits the rank at first.");
        world.SetRankLevelLimit(2);
        Check.That(world.AddSkillPoints(alpha, 100_000) && world.RankLevelOf(alpha) == 2 && world.SkillPointsOf(alpha) == 800, $"A limit of 2 holds the points at the lowest of level 2 (were {world.SkillPointsOf(alpha)}).");
        Check.That(!world.SetRankLevel(alpha, 3) && world.RankLevelOf(alpha) == 2, "A limit holds a level set by hand as well.");

        var scaled = RankWorld(fixture);
        scaled.SetSkillPointsModifier(alpha, Fixed.FromDecimal(0.5m));
        scaled.AddSkillPoints(alpha, 3);
        Check.That(scaled.SkillPointsOf(alpha) == 2, $"The scale rounds up (1.5 is 2, was {scaled.SkillPointsOf(alpha)}).");
        scaled.SetSkillPointsModifier(alpha, Fixed.Zero);
        Check.That(!scaled.AddSkillPoints(alpha, 900) && scaled.SkillPointsOf(alpha) == 2, "A scale of zero stops the points.");

        var bytes = WorldSnapshot.Write(scaled);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == scaled.StateHash && restored.SkillPointsOf(alpha) == 2 && restored.SkillPointsModifierOf(alpha) == Fixed.Zero, "Level, points and scale cross a save.");
        world.AddSkillPoints(alpha, 1);
        var limited = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(limited.RankLevelLimit == 2 && limited.RankLevelOf(alpha) == 2 && limited.StateHash == world.StateHash && limited.SciencePointsOf(alpha) == world.SciencePointsOf(alpha), "The limit crosses a save.");
    }

    private static int KillFor(SyntheticFixture fixture, string victimTemplate, string victimFaction = "RankedFaction", bool underConstruction = false)
    {
        var world = RankWorld(fixture, victimFaction);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "RankVictim", x: 10, y: 10);
        feeder.Spawn(2, victimTemplate, owner: 2, x: 40, y: 10);
        world.Step();
        var victim = world.FindEntity(new EntityId(2))!;
        if (underConstruction) world.SetStatus(victim.Id, 1UL << ObjectStatusBits.UnderConstruction);
        world.Damage(victim.Id, new DamageInfo(new EntityId(1), Fixed.FromInt(100), 0, 0, false));
        for (var step = 0; step < 4; step++) world.Step();
        return world.SkillPointsOf(new PlayerId(1));
    }

    public static void RankPointsOfAKill()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RankFiles());
        Check.That(KillFor(fixture, "RankVictim") == 40, "A kill without SkillPointValue gives the victim's ExperienceValue.");
        Check.That(KillFor(fixture, "RankPrize") == 900, "SkillPointValue goes before it.");
        Check.That(KillFor(fixture, "RankVictim", "CivilianFaction") == 0, "A side that is not playable gives nothing.");
        Check.That(KillFor(fixture, "RankVictim", underConstruction: true) == 0, "Something under construction gives nothing.");
    }

    public static void RankScriptActions()
    {
        var session = StartMission([
            Scr.Script("All", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("PLAYER_ADD_SKILLPOINTS", Scr.Side(PlayerA), Scr.Int(300)),
                Scr.Do("PLAYER_ADD_SKILLPOINTS", Scr.Side(PlayerA), Scr.Int(500)),
                Scr.Do("PLAYER_ADD_RANKLEVEL", Scr.Side(PlayerA), Scr.Int(1)),
                Scr.Do("PLAYER_SET_RANKLEVELLIMIT", Scr.Int(2)),
                Scr.Do("PLAYER_SET_RANKLEVEL", Scr.Side(PlayerA), Scr.Int(1)),
                Scr.Do("PLAYER_ADD_RANKLEVEL", Scr.Side(PlayerA), Scr.Int(5)),
                Scr.Do("PLAYER_AFFECT_RECEIVING_EXPERIENCE", Scr.Side(PlayerA), Scr.Real(2m))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Soldier")], options: MissionOptions.Standard with { HumanFaction = "RankedFaction" },
            extraFiles: new Dictionary<string, string> { ["Data/INI/Object/RankFixture.ini"] = RankIni });
        Run(session, session.Frames(3));
        var owner = NamedIn(session, "Soldier")!.Owner;
        Check.That(session.World.RankLevelOf(owner) == 2, $"The script's limit of 2 holds the player's level ({session.World.RankLevelOf(owner)}).");
        Check.That(session.World.RankLevelLimit == 2 && session.World.SkillPointsModifierOf(owner) == Fixed.FromInt(2), "Limit and scale reach the world.");
        Check.That(session.Engine.RecordedActions == 0 && session.Engine.MissingActionHits.Count == 0, "The actions are executed, not recorded.");

        var carried = StartMission([], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Soldier")], options: MissionOptions.Standard with { HumanFaction = "RankedFaction", RankPoints = 900 },
            extraFiles: new Dictionary<string, string> { ["Data/INI/Object/RankFixture.ini"] = RankIni });
        var human = NamedIn(carried, "Soldier")!.Owner;
        Check.That(carried.World.SkillPointsOf(human) == 900 && carried.World.RankLevelOf(human) == 2, "A mission begins with the points of the last one, and the level they make.");
    }
}
