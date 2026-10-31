using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

/// <summary>Object presentation: model conditions, draw specs, state selection, condition derivation, frame projection and scenery.</summary>
static partial class ObjectTests
{
    public static int Run(string[] args)
    {
        var run = new ObjectRunner();
        run.Test("Model condition table, parsing and set algebra", ConditionTable);
        run.Test("Best state selection follows findBestInfo", Selection);
        run.Test("Transitions and wait-to-finish in the state tracker", Tracker);
        run.Test("Draw spec compilation: defaults, aliases, transitions, bones", DrawSpecs);
        run.Test("Draw spec diagnostics: unknown keys, conditions and rule violations", DrawDiagnostics);
        run.Test("Quoted ascii strings join like INI::getNextAsciiString", QuotedTokens);
        run.Test("Condition derivation from simulation state", Derivation);
        run.Test("Frame projector interpolation, spawn and removal", Interpolation);
        run.Test("Frame projector fog of war and structure ghosts", Ghosts);
        run.Test("Static scenery and the claim rule", Scenery);
        run.Test("Static scenery follows the shroud: nothing in unexplored ground, kept once explored", SceneryFollowsTheShroud);
        run.Test("Turrets are the simulation's: they turn toward the victim of the fire order at their turn rate, the shot waits, they hold and recentre", TurretFollowsOrders);
        run.Test("Wheeled chassis: acceleration tilts the body, straight driving keeps the wheels straight, no spring extends past its limit", ChassisFollowsMotion);
        run.Test("Team colors", Colors);
        RegisterTimeScale(run.Test);
        var contentDir = ContentDir(args);
        if (contentDir is not null) run.Test("Real data: draw specs, audit, projection and scenery", () => RealData(contentDir));
        return run.Finish();
    }

    private static string? ContentDir(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--content-dir") return args[i + 1];
        return null;
    }

    // ------------------------------------------------------------------------------------------------ fixtures

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/PresentationFixture.ini"] = """
        Weapon TestGun
          PrimaryDamage = 5
          AttackRange = 100.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 1000
          DelayBetweenShots = 300
        End
        Weapon BigGun
          PrimaryDamage = 5
          AttackRange = 100.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Upgrade Upgrade_Paint
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_Gun
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Locomotor TestLocomotor
          Speed = 30.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 3600.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object Rover
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          Scale = 1.5
          Shadow = SHADOW_VOLUME
          Geometry = BOX
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 2.0
          GeometryIsSmall = Yes
          VisionRange = 50.0
          BuildTime = 1
          Locomotor = SET_NORMAL TestLocomotor
          Draw = W3DTankDraw ModuleTag_Draw
            IgnoreConditionStates = SNOW
            DefaultConditionState
              Model = RoverBody
              Turret = TurretBone
              TurretPitch = PitchBone
              TurretArtAngle = 90
              WeaponFireFXBone = PRIMARY Muzzle
              WeaponRecoilBone = PRIMARY Barrel
              ParticleSysBone = Smoke01 SmokeSystem
              HideSubObject = Flag Lamp
              Animation = RoverSkl.RoverIdle
              AnimationMode = LOOP
              TransitionKey = Rest
            End
            ConditionState = REALLYDAMAGED
              Model = RoverBody_D
              ShowSubObject = Flag
              ParticleSysBone = Smoke02 FireSystem
            End
            AliasConditionState = RUBBLE
            ConditionState = MOVING
              Animation = RoverSkl.RoverDrive 10 2
              AnimationMode = LOOP
              Flags = RANDOMSTART START_FRAME_FIRST
              TransitionKey = Drive
            End
            ConditionState = FIRING_A
              AnimationMode = ONCE
              WaitForStateToFinishIfPossible = Drive
            End
            ConditionState = USER_1
              Model = RoverPainted
            End
            TransitionState = Rest Drive
              Animation = RoverSkl.RoverStart
              AnimationMode = ONCE
            End
            TreadAnimationRate = 2.0
            TrackMarks = Track.tga
            OkToChangeModelColor = Yes
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TestGun
          End
          WeaponSet
            Conditions = PLAYER_UPGRADE
            Weapon = PRIMARY BigGun
          End
          Behavior = WeaponSetUpgrade WeaponUp01
            TriggeredBy = Upgrade_Gun
          End
          Behavior = ModelConditionUpgrade Paint01
            TriggeredBy = Upgrade_Paint
            ConditionFlag = USER_1
          End
        End

        Object Bunker
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 5.0
          GeometryIsSmall = No
          BuildTime = 2
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = Bunker
            End
            ConditionState = PARTIALLY_CONSTRUCTED
              Model = Bunker_Build
            End
            ConditionState = DAMAGED
              Model = Bunker_D
            End
          End
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Gunner
          KindOf = SELECTABLE INFANTRY
          VisionRange = 250.0
          Draw = W3DModelDraw ModuleTag_Draw
            DefaultConditionState
              Model = Scout
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY BigGun
          End
        End

        Object Scout
          KindOf = SELECTABLE INFANTRY
          VisionRange = 40.0
          Draw = W3DModelDraw ModuleTag_Draw
            DefaultConditionState
              Model = Scout
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Pine
          KindOf = SHRUBBERY
          Draw = W3DTreeDraw ModuleTag_Draw
            ModelName = PineTree
            TextureName = Pine.tga
            DoTopple = Yes
          End
        End

        Object Crate
          Draw = W3DModelDraw ModuleTag_Draw
            DefaultConditionState
              Model = CrateProp
            End
          End
        End

        Object Marker
          Draw = W3DDefaultDraw ModuleTag_Draw
          End
        End
        """,
    };

    private static SyntheticFixture Fixture() => new(CnCNew.Tests.TestRate.Default, files: Files());

    private static SimulationWorld NewWorld(SyntheticFixture fixture, TerrainMap? terrain = null)
        => new(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);

    /// <summary>A flat 20 x 20 cell synthetic map (no original data).</summary>
    private static TerrainMap FlatTerrain()
    {
        const int cells = 20, border = 1, width = cells + 2 * border;
        var stride = (width + 7) / 8;
        var blend = new MapBlendTiles(8, [], [], [], [], [..new byte[stride * width]], stride, 0, 0, [], [], [], []);
        var map = new MapData("synthetic", new MapHeightField(width, width, border, [new MapBoundary(cells, cells)], [..new byte[width * width]]), blend,
            MapProperties.Empty, [], [], [], [], [], [], []);
        return TerrainMap.Build(map, "flat");
    }

    private static ulong _sequence;

    private static void Submit(SimulationWorld world, Func<ulong, SimCommand> command) => world.Submit(command(++_sequence));

    private static EntityId Spawn(SimulationWorld world, string template, int owner, int x, int y)
    {
        Submit(world, sequence => new SpawnCommand(PlayerId.System, sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero));
        world.Step();
        return new EntityId(world.Entities.Where(entity => entity.Template.Name == template).Max(entity => entity.Id.Value));
    }

    private static ModelStateSpec State(int index, string conditions, string? model, string? transitionKey = null, string? waitKey = null, params string[] aliases)
        => new(index, false, [ModelConditionSet.Parse(conditions), ..aliases.Select(ModelConditionSet.Parse)], null, null, model, [], AnimationMode.Once, 1m, 1m,
            AnimationFlags.None, transitionKey, waitKey, TurretSpec.None, TurretSpec.None, [], [], []);

    private static DrawSpec Spec(ModelConditionSet ignore, params ModelStateSpec[] states)
        => new("W3DModelDraw", "Tag", DrawModuleKind.Model, [..states], -1, [], ignore, false, true, false, null, [], null, []);

    // ------------------------------------------------------------------------------------------------ tests

    private static void ConditionTable()
    {
        Check.That(ModelConditions.Count == 117, $"The retail table has 117 conditions ({ModelConditions.Count}).");
        Check.That(ModelConditions.Names[0] == "TOPPLED" && ModelConditions.Names[^1] == "DISGUISED", "Ids follow ModelConditionFlags::s_bitNameList.");
        Check.That(ModelConditions.Parse("reallydamaged") == ModelConditions.ReallyDamaged && ModelConditions.ReallyDamaged.Id == 4, "Lookup is case-insensitive with stable ids.");
        Check.That(!ModelConditions.TryParse("SURRENDER", out _), "SURRENDER only exists with ALLOW_SURRENDER, which retail does not define.");
        Check.That(ModelConditions.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ModelConditions.Count, "Names are unique.");

        var set = ModelConditionSet.Parse("DAMAGED MOVING DISGUISED");
        Check.That(set.Count == 3 && set.Contains(ModelConditions.Disguised) && set.ToString() == "DAMAGED MOVING DISGUISED", "Bits above 64 work and print in id order.");
        Check.That(ModelConditionSet.Parse("MOVING").IsSubsetOf(set) && !set.IsSubsetOf(ModelConditionSet.Parse("MOVING")), "IsSubsetOf.");
        Check.That(set.CountIntersection(ModelConditionSet.Parse("MOVING NIGHT")) == 1 && set.CountInverseIntersection(ModelConditionSet.Parse("MOVING NIGHT")) == 1,
            "countIntersection / countInverseIntersection semantics.");
        Check.That(ModelConditionSet.Parse("NONE") == ModelConditionSet.Empty && ModelConditionSet.Empty.ToString() == "NONE", "NONE is the empty set.");

        var errors = new List<string>();
        var start = ModelConditionSet.Parse("NIGHT");
        Check.That(ModelConditions.ParseList(["DAMAGED"], start, errors) == ModelConditionSet.Parse("DAMAGED"), "A plain list replaces the start set.");
        Check.That(ModelConditions.ParseList(["+DAMAGED", "-NIGHT"], start, errors) == ModelConditionSet.Parse("DAMAGED"), "+/- modify the start set.");
        Check.That(errors.Count == 0, "Valid lists report nothing.");
        ModelConditions.ParseList(["DAMAGED", "+NIGHT", "BOGUS"], start, errors);
        Check.That(errors.Contains("mixed-plus-minus") && errors.Contains("unknown:BOGUS"), "Mixing and unknown names are explicit errors.");
        Check.Throws<FormatException>(() => ModelConditionSet.Parse("NOT_A_FLAG"), "NOT_A_FLAG");
    }

    private static void Selection()
    {
        var damage = Spec(ModelConditionSet.Empty, State(0, "NONE", "a"), State(1, "DAMAGED", "b"), State(2, "REALLYDAMAGED RUBBLE", "c"));
        Check.That(BestStateSelector.FindBest(damage, ModelConditionSet.Empty) == 0, "No bits: the NONE state.");
        Check.That(BestStateSelector.FindBest(damage, ModelConditionSet.Parse("REALLYDAMAGED")) == 2, "More matching bits win even with an extraneous required bit.");
        Check.That(BestStateSelector.FindBest(damage, ModelConditionSet.Parse("MOVING")) == 0, "No match at all: the state with the fewest required bits.");

        var crushed = Spec(ModelConditionSet.Empty, State(0, "NONE", "a"), State(1, "FRONTCRUSHED BACKCRUSHED", "both"), State(2, "FRONTCRUSHED", "front"));
        Check.That(BestStateSelector.FindBest(crushed, ModelConditionSet.Parse("FRONTCRUSHED")) == 2, "Equal matches: fewer extraneous bits win (the reference's FRONTCRUSHED example).");

        var tie = Spec(ModelConditionSet.Empty, State(0, "NONE", "a"), State(1, "MOVING DAMAGED", "b"), State(2, "MOVING NIGHT", "c"));
        Check.That(BestStateSelector.FindBest(tie, ModelConditionSet.Parse("MOVING")) == 1, "A full tie keeps the earlier state.");
        Check.That(BestStateSelector.FindBest(tie, ModelConditionSet.Parse("MOVING NIGHT SNOW")) == 2, "Sparse matching: extra current bits do not disqualify.");

        var alias = Spec(ModelConditionSet.Empty, State(0, "NONE", "a"), State(1, "DAMAGED", "b", null, null, "DAMAGED NIGHT"), State(2, "NIGHT", "c"));
        Check.That(BestStateSelector.FindBest(alias, ModelConditionSet.Parse("DAMAGED NIGHT")) == 1, "An alias alternative matches for its state.");

        var ignoring = Spec(ModelConditionSet.Parse("NIGHT"), State(0, "NONE", "a"), State(1, "DAMAGED", "b"));
        Check.That(BestStateSelector.FindBest(ignoring, ModelConditionSet.Parse("NIGHT")) == 0 && BestStateSelector.FindBest(ignoring, ModelConditionSet.Parse("NIGHT DAMAGED")) == 1,
            "IgnoreConditionStates bits are cleared before matching.");
        Check.That(BestStateSelector.FindBest(Spec(ModelConditionSet.Empty), ModelConditionSet.Parse("DAMAGED")) == -1, "A module without states selects nothing.");
    }

    private static void Tracker()
    {
        var spec = Spec(ModelConditionSet.Empty, State(0, "NONE", "a", "rest"), State(1, "MOVING", "b", "drive"), State(2, "FIRING_A", "c", null, "drive"))
            with { Transitions = [State(0, "NONE", "t") with { TransitionFrom = "rest", TransitionTo = "drive" }] };
        Check.That(BestStateSelector.FindTransition(spec, 0, 1) == 0 && BestStateSelector.FindTransition(spec, 1, 0) == -1, "Transitions are directed by key pair.");
        var tracker = new ModelStateTracker(spec);
        tracker.Request(0, true);
        Check.That(tracker.Current == new ModelStateRef(0, false), "The first request is taken directly.");
        tracker.Request(1, false);
        Check.That(tracker.Current == new ModelStateRef(0, true) && tracker.Pending == 1, "rest -> drive plays the transition first.");
        tracker.Request(1, false);
        Check.That(tracker.Current.IsTransition && tracker.Pending == 1, "Requesting the pending state again changes nothing.");
        tracker.AnimationCompleted();
        Check.That(tracker.Current == new ModelStateRef(1, false) && tracker.Pending == -1, "The pending state follows the transition.");
        tracker.Request(2, false);
        Check.That(tracker.Current == new ModelStateRef(1, false) && tracker.Pending == 2, "WaitForStateToFinishIfPossible defers while the drive animation runs.");
        tracker.Request(2, true);
        Check.That(tracker.Current == new ModelStateRef(1, false) && tracker.Pending == 2, "A request for the pending state is ignored.");
        tracker.AnimationCompleted();
        Check.That(tracker.Current == new ModelStateRef(2, false), "The waiting state takes over when the animation completes.");
    }

    private static void DrawSpecs()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        Check.That(catalog.Diagnostics.IsEmpty, "The synthetic fixture compiles without diagnostics: " + string.Join("; ", catalog.Diagnostics.Select(value => $"{value.Code} {value.Key}")));
        var rover = catalog["rover"];
        Check.That(rover.Scale == 1.5f && rover.Shadow.SequenceEqual(["SHADOW_VOLUME"]) && Math.Abs(rover.SelectionRadius - 5f) < 1e-5f && rover.IsUnit && !rover.IsStructure,
            "Scale, shadow, box selection radius and kind flags come from the object fields.");
        var draw = rover.Draws.Single();
        Check.That(draw.ModuleType == "W3DTankDraw" && draw.Kind == DrawModuleKind.Model && draw.States.Length == 5 && draw.DefaultStateIndex == 0, "Five real states, default first.");
        Check.That(draw.IgnoreConditionStates == ModelConditionSet.Parse("SNOW") && draw.TrackMarks == "track.tga" && draw.OkToChangeModelColor, "Module keys are typed.");
        Check.That(draw.Setting("TreadAnimationRate") == "2.0", "Subclass keys are kept as text in source order.");

        var @default = draw.States[0];
        Check.That(@default.IsDefault && @default.Conditions.Single() == ModelConditionSet.Empty && @default.Model == "roverbody", "The default state has the blank condition set; model names are lower case.");
        Check.That(@default.Turret == new TurretSpec("turretbone", 90m, "pitchbone", 0m) && @default.AltTurret.IsEmpty, "Turret bones and art angle.");
        Check.That(@default.WeaponBones[0] == new WeaponSlotBones("muzzle", "barrel", null, null, null) && @default.WeaponBones[1] == WeaponSlotBones.None, "Weapon bones per slot.");
        Check.That(@default.Animations.Single() == new AnimationSpec("roverskl.roveridle", "roverskl", "roveridle.w3d", 0m, false) && @default.Mode == AnimationMode.Loop,
            "Animation handle, hierarchy and file.");

        var damaged = draw.States[1];
        Check.That(damaged.Conditions.SequenceEqual([ModelConditionSet.Parse("REALLYDAMAGED"), ModelConditionSet.Parse("RUBBLE")]), "AliasConditionState adds an alternative to the previous state.");
        Check.That(damaged.Model == "roverbody_d" && damaged.Turret == @default.Turret && damaged.WeaponBones.SequenceEqual(@default.WeaponBones),
            "A ConditionState starts as a copy of the default state.");
        Check.That(damaged.Animations.SequenceEqual(@default.Animations) && damaged.TransitionKey == "rest", "Untouched animations and the transition key stay copied.");
        Check.That(damaged.SubObjects.SequenceEqual([new SubObjectVisibility("flag", false), new SubObjectVisibility("lamp", true)]), "Show/hide entries merge by name.");
        Check.That(damaged.ParticleSysBones.Select(bone => bone.Bone).SequenceEqual(["smoke01", "smoke02"]), "Particle bones append to the copied ones.");

        var moving = draw.States[2];
        Check.That(moving.Animations.Length == 2 && moving.Animations.All(animation => animation == new AnimationSpec("roverskl.roverdrive", "roverskl", "roverdrive.w3d", 10m, false)),
            "An Animation replaces the copied ones; the repeat count duplicates the entry.");
        Check.That(moving.Flags == (AnimationFlags.RandomStart | AnimationFlags.StartFrameFirst) && moving.TransitionKey == "drive", "Flags and transition key.");
        Check.That(draw.States[3].WaitForStateToFinishKey == "drive" && draw.States[3].Model == "roverbody", "Wait-to-finish key; the model is inherited.");

        var transition = draw.Transitions.Single();
        Check.That(transition.TransitionFrom == "rest" && transition.TransitionTo == "drive" && transition.TransitionKey is null && transition.Model == "roverbody"
                   && transition.Animations.Single().File == "roverstart.w3d", "A TransitionState copies the default without its keys.");
        Check.That(BestStateSelector.FindTransition(draw, 0, 2) == 0, "The rest -> drive transition is found.");
        Check.That(draw.TurretBones.SequenceEqual(["pitchbone", "turretbone"]), "Turret bone summary.");

        var tree = catalog["Pine"].Draws.Single();
        Check.That(tree.Kind == DrawModuleKind.Tree && tree.States.Single().Model == "PineTree" && tree.DefaultStateIndex == 0 && tree.Setting("TextureName") == "Pine.tga",
            "A tree gets one synthesized state with its ModelName.");
        var marker = catalog["Marker"];
        Check.That(marker.Draws.Single().Kind == DrawModuleKind.Default && !marker.HasModel, "W3DDefaultDraw has no model.");
        Check.That(rover.ConditionUpgrades.Single() == rover.ConditionUpgrades[0] && rover.ConditionUpgrades[0].Flag.Name == "USER_1"
                   && rover.ConditionUpgrades[0].TriggeredBy.SequenceEqual(["Upgrade_Paint"]), "ModelConditionUpgrade is read from the graph.");
    }

    private static void DrawDiagnostics()
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/Broken.ini"] = """
            Object Broken
              Draw = W3DModelDraw ModuleTag_Draw
                ConditionState = NONE
                  Model = A
                  Colour = Red
                End
                ConditionState = DAMAGED BOGUS_FLAG
                  Model = B
                  AnimationMode = SIDEWAYS
                  WeaponFireFXBone = QUATERNARY Bone
                End
                ConditionState = DAMAGED
                  Animation = NoDotAnimation
                End
                AliasConditionState = NONE
                Wobble = Yes
                TransitionState = X X
                  AnimationMode = LOOP
                End
                DefaultConditionState
                  Model = C
                End
              End
              Draw = W3DTreeDraw ModuleTag_Tree
                ModelName = T
                Swaying = Yes
              End
            End
            """,
        };
        var graph = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: files).Graph;
        var catalog = ObjectVisualCatalog.Build(graph);
        var codes = catalog.Diagnostics.Select(value => $"{value.Code}:{value.Key}").ToHashSet();
        foreach (var expected in new[]
                 {
                     "unknown-draw-key:ConditionState/Colour", "unknown-draw-key:Wobble", "unknown-draw-key:Swaying", "unknown-condition:BOGUS_FLAG",
                     "unknown-enum:AnimationMode", "unknown-enum:WeaponFireFXBone", "state-rule:duplicate-condition-state", "state-rule:missing-model",
                     "animation-without-dot:Animation", "state-rule:transition-same-states", "state-rule:transition-mode", "state-rule:default-not-first",
                 })
            Check.That(codes.Contains(expected), $"Diagnostic {expected} is reported (got {string.Join(", ", codes.Order())}).");
        Check.That(catalog.Diagnostics.Single(value => value.Key == "Swaying").ModuleType == "W3DTreeDraw", "Unknown keys name their module type.");
        var report = ObjectAudit.Run(graph, _ => true);
        Check.That(report.Unclassified == 4 && report.UnknownConditions == 1 && report.UnknownDrawKeys == 3, $"The audit counts unclassified findings ({report.Unclassified}).");
        Check.That(report.Lines.Contains("unknown-draw-key W3DTreeDraw Swaying 1") && report.Lines.Contains("unknown-condition BOGUS_FLAG 1"), "Per-module key counts are listed.");
    }

    private static void QuotedTokens()
    {
        Check.That(DrawSpecCompiler.Tokens("PRIMARY \"UIMOB03 R HAND\"").SequenceEqual(["PRIMARY", "UIMOB03 R HAND"]), "A quoted string is one token.");
        Check.That(DrawSpecCompiler.Tokens("\"Solo\"").SequenceEqual(["Solo"]), "A lone quoted word loses its quotes.");
        Check.That(DrawSpecCompiler.Tokens("  A =B\tC ").SequenceEqual(["A", "B", "C"]), "Blanks, tabs and '=' separate tokens.");
    }

    private static void Derivation()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var world = NewWorld(fixture);
        var rover = Spawn(world, "Rover", 1, 10, 10);
        // A target that cannot shoot back: since F-58 a human player's idle unit that is hit answers, and the shooter answers that in turn (the player's retaliation).
        var target = Spawn(world, "Bunker", 2, 50, 10);
        var entity = world.FindEntity(rover)!;
        var visual = catalog["Rover"];
        ModelConditionSet Conditions(ConditionContext context = default) => ConditionDeriver.Derive(entity, world, visual, context);

        Check.That(Conditions() == ModelConditionSet.Empty, "A fresh idle unit has no condition.");
        Check.That(Conditions(new ConditionContext(Night: true, Snow: true)) == ModelConditionSet.Parse("NIGHT SNOW"), "Night and snow come from the caller.");

        Submit(world, sequence => new FireWeaponCommand(new PlayerId(1), sequence, world.Tick, rover, target));
        world.Step();
        Check.That(Conditions() == ModelConditionSet.Parse("FIRING_A USING_WEAPON_A"), $"The firing tick shows FIRING_A ({Conditions()}).");
        // FIRING_A is shown for one original frame; the step of the shot is a step, the rest are frames of game time.
        for (var i = 0; i < CnCNew.Tests.TestTime.Frames(world, 1); i++) world.Step();
        Check.That(Conditions() == ModelConditionSet.Parse("BETWEEN_FIRING_SHOTS_A USING_WEAPON_A"), $"Then the shot delay ({Conditions()}).");
        for (var i = 0; i < CnCNew.Tests.TestTime.Frames(world, 12); i++) world.Step();
        Submit(world, sequence => new FireWeaponCommand(new PlayerId(1), sequence, world.Tick, rover, target));
        world.Step();
        for (var i = 0; i < CnCNew.Tests.TestTime.Frames(world, 1); i++) world.Step();
        Check.That(entity.WeaponClip == 0 && Conditions() == ModelConditionSet.Parse("RELOADING_A USING_WEAPON_A"), $"An empty clip reloads ({Conditions()}).");
        for (var i = 0; i < CnCNew.Tests.TestTime.Frames(world, 40); i++) world.Step();
        Check.That(Conditions() == ModelConditionSet.Empty, "Ready again: no weapon bits.");

        Submit(world, sequence => new DamageCommand(PlayerId.System, sequence, world.Tick, rover, Fixed.FromInt(95), 0, 0));
        world.Step();
        Check.That(entity.DamageState() == BodyDamageStates.ReallyDamaged && Conditions().Contains(ModelConditions.ReallyDamaged), "Body damage state maps to REALLYDAMAGED.");
        Check.That(StaticSelection(visual, Conditions()) == "roverbody_d", "and selects the damaged model.");

        Submit(world, sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, world.Tick, rover, Fixed.FromInt(150), Fixed.FromInt(10)));
        world.Step();
        Check.That(Conditions().Contains(ModelConditions.Moving), "A move order shows MOVING.");

        world.SetVeterancy(rover, 2);
        Check.That(Conditions().Contains(ModelConditions.WeaponSetElite) && !Conditions().Contains(ModelConditions.WeaponSetVeteran), "Veterancy maps to WEAPONSET_ELITE.");
        world.CompleteUpgrade(new PlayerId(1), rover, "Upgrade_Gun");
        world.CompleteUpgrade(new PlayerId(1), rover, "Upgrade_Paint");
        world.Step();
        Check.That(Conditions().Contains(ModelConditions.WeaponSetPlayerUpgrade), "WeaponSetUpgrade maps to WEAPONSET_PLAYER_UPGRADE.");
        Check.That(Conditions().Contains(ModelConditions.Parse("USER_1")), "ModelConditionUpgrade sets its flag once triggered.");

        var bunker = Spawn(world, "Bunker", 1, 100, 100);
        var site = world.FindEntity(bunker)!;
        var bunkerVisual = catalog["Bunker"];
        world.SetStatus(bunker, 1UL << ObjectStatusBits.UnderConstruction);
        Check.That(ConditionDeriver.Derive(site, world, bunkerVisual, default) == ModelConditionSet.Parse("AWAITING_CONSTRUCTION"), "A site without work awaits construction.");
        world.AdvanceConstruction(site);
        Check.That(ConditionDeriver.Derive(site, world, bunkerVisual, new ConditionContext(PreviousConstructionWork: 0))
                   == ModelConditionSet.Parse("PARTIALLY_CONSTRUCTED ACTIVELY_BEING_CONSTRUCTED"), "Work added since the last tick: actively being constructed.");
        // "No new work" is the work the site has now (1 at 30 Hz; above it the first work step counts a whole frame of the original, in ticks).
        var worked = site.ConstructionWork;
        Check.That(ConditionDeriver.Derive(site, world, bunkerVisual, new ConditionContext(PreviousConstructionWork: worked)).Contains(ModelConditions.PartiallyConstructed)
                   && !ConditionDeriver.Derive(site, world, bunkerVisual, new ConditionContext(PreviousConstructionWork: worked)).Contains(ModelConditions.ActivelyBeingConstructed),
            "No new work: only partially constructed.");
        Check.That(StaticSelection(bunkerVisual, ModelConditionSet.Parse("PARTIALLY_CONSTRUCTED ACTIVELY_BEING_CONSTRUCTED")) == "bunker_build", "The construction model is selected.");
        world.SetStatus(bunker, 1UL << ObjectStatusBits.Sold, 1UL << ObjectStatusBits.UnderConstruction);
        Check.That(ConditionDeriver.Derive(site, world, bunkerVisual, default).Contains(ModelConditions.Sold), "SOLD mirrors the status bit.");
    }

    private static string? StaticSelection(ObjectVisual visual, ModelConditionSet conditions)
    {
        var draw = visual.Draws[0];
        return draw.States[BestStateSelector.FindBest(draw, conditions)].Model;
    }

    private static void Interpolation()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var world = NewWorld(fixture);
        var projector = new FrameProjector(catalog);
        Check.That(projector.Project(0.5f).Drawables.IsEmpty && projector.Tick == -1, "Nothing before the first capture.");
        var rover = Spawn(world, "Rover", 1, 0, 10);
        projector.Advance(world);
        var first = projector.Project(0f).Drawables.Single();
        Check.That(first.Entity == rover && first.Position == new Vector3(0, 10, 0) && first.PrimaryModel == "roverbody" && first.Health01 == 1f && first.ConstructionProgress01 == 1f,
            "A new entity appears at its current transform without interpolation.");
        Check.That(!first.IsStatic && !first.IsGhost && first.Scale == 1.5f && first.TurretYaw is null, "Entity drawables are dynamic; an object without a turret has no turret angle.");

        Submit(world, sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, world.Tick, rover, Fixed.FromInt(100), Fixed.FromInt(10)));
        for (var i = 0; i < 4; i++) { world.Step(); projector.Advance(world); }
        var entity = world.FindEntity(rover)!;
        var x1 = (float)entity.X.ToDecimal();
        world.Step();
        projector.Advance(world);
        var x2 = (float)entity.X.ToDecimal();
        Check.That(x2 > x1, "The unit moves along +x.");
        var start = projector.Project(0f).Drawables.Single();
        var middle = projector.Project(0.5f).Drawables.Single();
        var end = projector.Project(1f).Drawables.Single();
        Check.That(Math.Abs(start.Position.X - x1) < 1e-3f && Math.Abs(end.Position.X - x2) < 1e-3f && Math.Abs(middle.Position.X - (x1 + x2) / 2) < 1e-3f,
            $"Positions interpolate between the ticks ({start.Position.X}, {middle.Position.X}, {end.Position.X}).");
        Check.That(middle.Conditions.Contains(ModelConditions.Moving) && middle.Selections.Single().Model == "roverbody" && middle.Selections.Single().StateIndex == 2,
            "The capture carries conditions and the selected state.");
        Check.That(projector.Project(7f).Alpha == 1f && projector.Project(-1f).Alpha == 0f, "Alpha is clamped.");
        Check.That(projector.Project(0.25f) == projector.Project(0.25f) || projector.Project(0.25f).Drawables.SequenceEqual(projector.Project(0.25f).Drawables),
            "Projection is a pure function of the captures.");

        Check.That(Math.Abs(FrameProjector.LerpAngle(3f, -3f, 0.5f) - MathF.PI) < 0.01f || Math.Abs(FrameProjector.LerpAngle(3f, -3f, 0.5f) + MathF.PI) < 0.01f,
            "Yaw takes the short way across ±π.");

        Submit(world, sequence => new KillCommand(PlayerId.System, sequence, world.Tick, rover));
        world.Step();
        projector.Advance(world);
        Check.That(projector.Project(0.5f).Drawables.IsEmpty, "A destroyed entity leaves the frame with the next capture.");
    }

    private static void Ghosts()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var terrain = FlatTerrain();
        var world = NewWorld(fixture, terrain);
        var scout = Spawn(world, "Scout", 1, 30, 30);
        var bunker = Spawn(world, "Bunker", 2, 50, 30);
        var enemyScout = Spawn(world, "Scout", 2, 45, 35);
        var hidden = Spawn(world, "Bunker", 2, 180, 180);
        void Settle() { for (var i = 0; i < world.VisionInterval + 1; i++) world.Step(); }
        Settle();
        var projector = new FrameProjector(catalog, new FrameProjectorOptions { Viewer = new PlayerId(1) });
        projector.Advance(world);
        var ids = projector.Project(1f).Drawables.Select(drawable => drawable.Entity).ToArray();
        Check.That(ids.SequenceEqual([scout, bunker, enemyScout]), $"The viewer sees its own and visible enemy objects, not the hidden bunker ({string.Join(",", ids)}).");

        Submit(world, sequence => new KillCommand(PlayerId.System, sequence, world.Tick, scout));
        Settle();
        projector.Advance(world);
        var frame = projector.Project(1f).Drawables;
        Check.That(frame.Length == 1 && frame[0].Entity == bunker && frame[0].IsGhost && frame[0].PrimaryModel == "bunker",
            "Out of sight the enemy structure stays as a ghost; the unit vanishes.");

        Submit(world, sequence => new KillCommand(PlayerId.System, sequence, world.Tick, bunker));
        Settle();
        projector.Advance(world);
        Check.That(projector.Project(1f).Drawables.Single().IsGhost, "The ghost survives the unseen destruction.");

        Spawn(world, "Scout", 1, 45, 30);
        Settle();
        projector.Advance(world);
        frame = projector.Project(1f).Drawables;
        Check.That(frame.All(drawable => drawable.Entity != bunker) && frame.Any(drawable => drawable.Entity == enemyScout) && world.FindEntity(hidden) is not null,
            "Seeing the spot again drops the ghost.");

        var omniscient = new FrameProjector(catalog);
        omniscient.Advance(world);
        Check.That(omniscient.Project(1f).Drawables.Any(drawable => drawable.Entity == hidden), "Without a viewer everything is drawn.");
    }

    private static void Scenery()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var waypoint = new MapProperties([new MapProperty("waypointID", MapPropertyType.Int, 1, 0m, string.Empty)]);
        ImmutableArray<MapObject> objects =
        [
            new("Pine", 10m, 20m, 0m, 1.5m, 0, MapProperties.Empty),
            new("*Waypoints/Waypoint", 5m, 5m, 0m, 0m, 0, waypoint),
            new("Crate", 30m, 40m, 0m, 0m, MapObject.FlagRoadPoint1, MapProperties.Empty),
            new("NoSuchThing", 1m, 1m, 0m, 0m, 0, MapProperties.Empty),
            new("Marker", 2m, 2m, 0m, 0m, 0, MapProperties.Empty),
            new("Bunker", 60m, 60m, 0m, 0m, 0, MapProperties.Empty),
            new("Crate", 70m, 80m, 0m, 0m, 0, MapProperties.Empty),
        ];
        var result = StaticScenery.Build(objects, catalog, (x, y) => x / 10f);
        Check.That(result.Items.Select(item => item.Visual.TemplateName).SequenceEqual(["Pine", "Bunker", "Crate"]), "Trees, props and unspawned structures become scenery.");
        Check.That(result.Items[0].Position == new Vector3(10, 20, 1) && result.Items[0].Yaw == 1.5f && result.Items[0].Selections.Single().Model == "PineTree",
            "Position, ground height, angle and model.");
        Check.That(result.Report is { MapObjects: 7, Waypoints: 1, RoadOrBridgePoints: 1 } && result.Report.UnknownTemplates["NoSuchThing"] == 1
                   && result.Report.WithoutModel["Marker"] == 1, "Skipped objects are counted by reason.");

        var world = NewWorld(fixture);
        var projector = new FrameProjector(catalog, new FrameProjectorOptions { Scenery = result.Items, Night = true });
        projector.Advance(world);
        var frame = projector.Project(0.5f).Drawables;
        Check.That(frame.Length == 3 && frame.All(drawable => drawable.IsStatic && drawable.Entity == EntityId.Invalid) && frame[1].SceneryIndex == 1,
            "Unclaimed scenery is drawn statically in index order.");
        Check.That(frame[0].Conditions == ModelConditionSet.Parse("NIGHT"), "Scenery sees the time of day.");
        Spawn(world, "Bunker", 0, 60, 60);
        projector.Advance(world);
        frame = projector.Project(0.5f).Drawables;
        Check.That(projector.ClaimedScenery == 1 && frame.Count(drawable => drawable.IsStatic) == 2 && frame.Count(drawable => !drawable.IsStatic) == 1,
            "A live entity of the same template at the scenery position claims it.");
    }

    private static void SceneryFollowsTheShroud()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var terrain = FlatTerrain();
        var world = NewWorld(fixture, terrain);
        ImmutableArray<MapObject> objects =
        [
            new("Pine", 36m, 30m, 0m, 0m, 0, MapProperties.Empty),
            new("Bunker", 190m, 190m, 0m, 0m, 0, MapProperties.Empty),
            new("Crate", 150m, 40m, 0m, 0m, 0, MapProperties.Empty),
        ];
        var scenery = StaticScenery.Build(objects, catalog).Items;
        var scout = Spawn(world, "Scout", 1, 30, 30);
        void Settle() { for (var i = 0; i < world.VisionInterval + 1; i++) world.Step(); }
        Settle();
        var projector = new FrameProjector(catalog, new FrameProjectorOptions { Viewer = new PlayerId(1), Scenery = scenery });
        projector.Advance(world);
        var names = () => projector.Project(1f).Drawables.Where(drawable => drawable.IsStatic).Select(drawable => drawable.Visual.TemplateName).ToArray();
        Check.That(names().SequenceEqual(["Pine"]) && projector.ShroudedScenery == 2, $"Only scenery on explored ground is drawn ({string.Join(",", names())}; shrouded {projector.ShroudedScenery}).");
        Check.That(projector.Project(1f).Drawables.Single(drawable => drawable.IsStatic).Fogged == false && projector.FoggedScenery == 0, "Scenery in sight is not dimmed.");

        Submit(world, sequence => new KillCommand(PlayerId.System, sequence, world.Tick, scout));
        Settle();
        projector.Advance(world);
        Check.That(names().SequenceEqual(["Pine"]), "Once explored, the scenery stays drawn when the viewer loses sight of it (fog, not shroud).");
        while (world.Tick % world.Time.OriginalFrames(FrameProjector.SceneryFogIntervalFrames) != 0) world.Step();
        projector.Advance(world);
        Check.That(projector.Project(1f).Drawables.Single(drawable => drawable.IsStatic).Fogged && projector.FoggedScenery == 1, "Out of sight but explored, the scenery is drawn dimmed (fogged).");

        var omniscient = new FrameProjector(catalog, new FrameProjectorOptions { Scenery = scenery });
        omniscient.Advance(world);
        Check.That(omniscient.Project(1f).Drawables.Count(drawable => drawable.IsStatic) == 3 && omniscient.ShroudedScenery == 0, "Without a viewer all scenery is drawn.");
    }

    private static void ChassisFollowsMotion()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var world = NewWorld(fixture);
        var rover = Spawn(world, "Rover", 1, 20, 20);
        var entity = world.FindEntity(rover)!;
        var motion = new SuspensionMotion(SuspensionKind.Wheels, 0.1f, 0.1f, 0.9f, 0.9f, 4f, 4f, 1f, 0.3f, 0f, true, 0.4f, -2.3f);
        var visual = catalog["Rover"] with { Suspension = motion };
        var tracker = new SuspensionTracker();
        Check.That(tracker.Update(entity, catalog["Rover"], world, 50f) is null, "An object without chassis dynamics gets no pose.");

        Submit(world, sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, world.Tick, rover, Fixed.FromInt(170), Fixed.FromInt(20)));
        float maxPitch = 0f, maxSteer = 0f;
        for (var tick = 0; tick < 25; tick++)
        {
            world.Step();
            var pose = tracker.Update(entity, visual, world, 50f)!.Value;
            maxPitch = Math.Max(maxPitch, Math.Abs(pose.Pitch));
            maxSteer = Math.Max(maxSteer, Math.Abs(pose.WheelAngle));
        }

        // The sway overshoots its limit by one step before it is clamped: three steps of 4 * (1/30) are 0.4 exactly, the limit plus 0.1. Since the
        // acceleration per tick is 1/30 to seven digits (24 fraction bits) the peak is that value to the last bit of a single, so the bound allows for it.
        Check.That(maxPitch > 0.005f && maxPitch <= motion.AccelPitchLimit + 0.1f + 0.0001f, $"Setting off tilts the body by acceleration ({maxPitch} rad; at {entity.X},{entity.Y} speed {entity.Speed}).");
        Check.That(maxSteer < 0.05f, $"Driving straight keeps the wheels straight ({maxSteer}).");

        var exact = new SuspensionTracker();
        var other = world.FindEntity(rover)!;
        var again = exact.Update(other, visual, world, 50f)!.Value;
        Check.That(again.FrontLeft >= motion.MaxWheelExtension && again.RearRight >= motion.MaxWheelExtension, "No wheel extends past the maximum extension.");
    }

    private static void TurretFollowsOrders()
    {
        var fixture = Fixture();
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        var world = NewWorld(fixture, FlatTerrain());
        var gunner = Spawn(world, "Gunner", 1, 100, 100);
        var victim = Spawn(world, "Bunker", 2, 100, 170); // due north of the gunner: the turret has to turn +90 degrees from a body facing +X
        var tracker = new TurretTracker();
        var entity = world.FindEntity(gunner)!;
        var step = MathF.PI * (float)CnCNew.Tests.TestTime.TickSeconds(world); // TurretTurnRate = 180 degrees per second: 6 degrees per tick at 30 Hz
        var quarterTurn = CnCNew.Tests.TestTime.Frames(world, 15); // 90 degrees at 180 degrees per second: half a second
        Check.That(tracker.Update(entity, 0) is { Angle: 0f, Pitch: 0f } && !tracker.IsRotating(gunner.Value), "A turret at rest sits at its natural angle.");
        Check.That(tracker.Update(entity, 1) is null, "A missing alternate turret reports nothing.");
        Check.That(tracker.Update(world.FindEntity(victim)!, 0) is null && catalog["Scout"].IsUnit, "An object without a turret has none.");

        // The fire order of a standing attack, once per tick: the turret turns, the weapon waits until it points at the victim. The orders begin with the gunner's first
        // logic frame (the tick after its spawn at 30 Hz): a new turret does nothing before that.
        CnCNew.Presentation.Tests.TestFrames.StepToFrameOf(world, gunner);
        var orderTick = world.Tick;
        long shotTick = -1;
        var angles = new List<float>();
        for (var i = 0; i < CnCNew.Tests.TestTime.Frames(world, 40) && shotTick < 0; i++)
        {
            Submit(world, sequence => new FireWeaponCommand(new PlayerId(1), sequence, world.Tick, gunner, victim));
            var result = world.Step();
            angles.Add(tracker.Update(entity, 0)!.Value.Angle);
            if (result.Events.Any(simEvent => simEvent.Kind == SimEventKind.WeaponFired)) shotTick = result.Tick;
        }

        Check.That(Math.Abs(angles[0] - step) < 1e-3f && tracker.IsRotating(gunner.Value) == (shotTick < 0), $"It turns by its rate per tick ({angles[0]} rad) and is still turning exactly until the shot (shot at tick {shotTick}, rotating {tracker.IsRotating(gunner.Value)}, {world.TurretWaits} orders waited).");
        Check.That(shotTick - orderTick == quarterTurn, $"The shot waits until the turret has turned 90 degrees at 180 degrees per second: tick {shotTick - orderTick} after the order.");
        // TurretWaits counts logic frames of waiting (an order that is held back counts in the tick its frame begins with): fifteen at every rate.
        Check.That(Math.Abs(angles[^1] - MathF.PI / 2) < 0.035f && world.TurretWaits == 15, $"It points at the victim when the shot goes out ({angles[^1]} rad, {world.TurretWaits} orders waited).");

        // No more orders: the attack goes on, so the turret keeps its aim while the victim lives; when the victim is dead it holds for the recentre time, then turns back at half speed.
        var held = 0;
        var holdTicks = CnCNew.Tests.TestTime.Frames(world, 60);
        for (var i = 0; i < holdTicks; i++)
        {
            world.Step();
            if (Math.Abs(tracker.Update(entity, 0)!.Value.Angle - MathF.PI / 2) < 1e-3f) held++;
        }

        Check.That(held >= holdTicks - 2, $"It holds the aim after the last shot ({held} of {holdTicks} ticks).");
        Submit(world, sequence => new KillCommand(PlayerId.System, sequence, world.Tick, victim));
        var angle = 0f;
        for (var i = 0; i < CnCNew.Tests.TestTime.Frames(world, 160); i++)
        {
            world.Step();
            angle = tracker.Update(entity, 0)!.Value.Angle;
        }

        Check.That(Math.Abs(angle) < 1e-3f && !tracker.IsRotating(gunner.Value), $"After the recentre time without an order it has turned back to its natural angle ({angle}).");
    }

    private static void Colors()
    {
        var fixture = Fixture();
        ImmutableArray<PlayerConfiguration> players = [new(PlayerId.System, "System"), new(new PlayerId(1), "A"), new(new PlayerId(2), "B"), new(new PlayerId(3), "C")];
        var colors = TeamColors.Resolve(players, fixture.Graph, new Dictionary<int, ColorValue> { [3] = new(1, 2, 3) });
        Check.That(colors.Of(new PlayerId(1)) == TeamColors.Palette[0] && colors.Of(new PlayerId(2)) == TeamColors.Palette[1], "Without a faction color the palette is used in order.");
        Check.That(colors.Of(new PlayerId(3)) == new ColorValue(1, 2, 3) && colors.Of(PlayerId.System) == TeamColors.Neutral && colors.Of(new PlayerId(9)) == TeamColors.Neutral,
            "Overrides win; system and unknown players are neutral.");
    }

    // ------------------------------------------------------------------------------------------------ real data

    private static void RealData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var catalog = ObjectVisualCatalog.Build(graph);
        Check.That(catalog.Visuals.Length == 2184, $"Every object has a visual ({catalog.Visuals.Length}).");

        var crusader = catalog["AmericaTankCrusader"];
        var tank = crusader.Draws.Single(draw => draw.ModuleType == "W3DTankDraw");
        Check.That(tank.DefaultStateIndex == 0 && tank.States[0].Model == "avleopard" && tank.States[0].Turret.AngleBone == "turret", "Crusader default state: model and turret.");
        Check.That(tank.States[0].WeaponBones[0] is { FireFxBone: "turretms", RecoilBone: "barrel", MuzzleFlash: "turretfx", LaunchBone: "turretms" }, "Crusader weapon bones.");
        Check.That(tank.States[BestStateSelector.FindBest(tank, ModelConditionSet.Parse("REALLYDAMAGED"))].Model == "avleopard_d", "REALLYDAMAGED selects the damaged hull.");
        Check.That(tank.States[BestStateSelector.FindBest(tank, ModelConditionSet.Parse("DAMAGED MOVING"))].Model == "avleopard", "DAMAGED has no state: the default wins.");
        Check.That(tank.States[BestStateSelector.FindBest(tank, ModelConditionSet.Parse("RUBBLE"))].Turret.AngleBone == "turret", "Damaged states keep the default's turret.");
        Check.That(tank.TrackMarks == "extnktrack.tga" && tank.OkToChangeModelColor, "Crusader module keys.");

        // The Turret/AltTurret blocks are typed data now (TurretSchema) and live in the simulation's templates; the presentation reads the simulated turret.
        var turretSections = graph.Definitions.OfType<ObjectDefinition>().SelectMany(definition => definition.Modules).SelectMany(module => module.Sections.IsDefaultOrEmpty ? [] : module.Sections)
            .Where(section => section.Declaration is "Turret" or "AltTurret").ToArray();
        Check.That(turretSections.Length >= 200 && turretSections.All(section => section.Fields.First(field => field.Name == "ControlledWeaponSlots").Value.Kind == DefinitionValueKind.Flags),
            $"The turret blocks of the retail objects are typed ({turretSections.Length}).");

        var barracks = catalog["AmericaBarracks"].Draws[0];
        string? Model(string conditions) => barracks.States[BestStateSelector.FindBest(barracks, ModelConditionSet.Parse(conditions))].Model;
        Check.That(Model("NONE") == "abbarracks" && Model("DAMAGED") == "abbarracks_d" && Model("REALLYDAMAGED") == "abbarracks_e" && Model("DAMAGED NIGHT") == "abbarracks_dn"
                   && Model("NIGHT SNOW") == "abbarracks_ns", "Barracks damage, night and snow models.");
        var building = barracks.States[BestStateSelector.FindBest(barracks, ModelConditionSet.Parse("PARTIALLY_CONSTRUCTED ACTIVELY_BEING_CONSTRUCTED"))];
        Check.That(building.Model == "abbarracks" && building.Flags.HasFlag(AnimationFlags.AdjustHeightByConstructionPercent), "The construction state rises with the build progress.");

        var first = ObjectAudit.Run(graph, content.Exists);
        var second = ObjectAudit.Run(graph, content.Exists);
        Check.That(first.Lines.SequenceEqual(second.Lines), "The audit is deterministic.");
        Check.That(first.Unclassified == 0 && first.UnknownConditions == 0 && first.UnknownDrawKeys == 0, $"Draw keys and condition names are fully classified ({first.Unclassified}).");
        Console.WriteLine($"       real data: {first.Objects} objects, {first.DrawModules} draw modules, {first.States} states, {first.Transitions} transitions, " +
                          $"models {first.ModelFilesResolved}/{first.ModelFiles}, animations {first.AnimationFilesResolved}/{first.AnimationFiles}, " +
                          $"skeletons {first.SkeletonFilesResolved}/{first.SkeletonFiles}, rule errors {first.RuleErrors}");

        var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default, CnCNew.Tests.TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 1, Players, "presentation-real", fingerprint), templates);
        var sequence = 0UL;
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, 0, "AmericaTankCrusader", new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, 0, "AmericaBarracks", new PlayerId(2), Fixed.FromInt(300), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero));
        world.Step();
        var projector = new FrameProjector(catalog, new FrameProjectorOptions { Colors = TeamColors.Resolve(world.Configuration.Players, graph), Night = true });
        projector.Advance(world);
        var frame = projector.Project(1f);
        Check.That(projector.MissingVisuals == 0 && frame.Drawables.Length == 2, "Both spawned objects are projected.");
        Check.That(frame.Drawables[0].Selections.Any(selection => selection.Model == "avleopard") && frame.Drawables[1].PrimaryModel == "abbarracks_n",
            "Real templates select their models (the barracks at night).");

        var wheeled = catalog.Visuals.Count(visual => visual.Suspension is { Kind: SuspensionKind.Wheels });
        var treaded = catalog.Visuals.Count(visual => visual.Suspension is { Kind: SuspensionKind.Treads });
        Check.That(catalog["AmericaVehicleHumvee"].Suspension is { Kind: SuspensionKind.Wheels, HasSuspension: true } humveeMotion && Math.Abs(humveeMotion.WheelTurnAngle - 22f * MathF.PI / 180f) < 1e-4f
                   && catalog["AmericaTankCrusader"].Suspension is { Kind: SuspensionKind.Treads }, "The Humvee's wheels (22 degrees of steering) and the Crusader's treads are read from their locomotors.");
        Console.WriteLine($"       real data: {wheeled} objects with wheeled and {treaded} with treaded chassis dynamics");
        {
            var driveTerrain = FlatTerrain();
            var driveWorld = new SimulationWorld(new WorldConfiguration(new WorldId(2), CnCNew.Tests.TestRate.Default, 1, Players, "presentation-chassis", fingerprint, driveTerrain.Fingerprint), templates, driveTerrain);
            driveWorld.Submit(new SpawnCommand(PlayerId.System, ++sequence, 0, "AmericaVehicleHumvee", new PlayerId(1), Fixed.FromInt(30), Fixed.FromInt(30), Fixed.Zero, Fixed.Zero));
            driveWorld.Step();
            var humvee = driveWorld.Entities.Single(entity => entity.Template.Name == "AmericaVehicleHumvee");
            var humveeTracker = new SuspensionTracker();
            driveWorld.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, driveWorld.Tick, humvee.Id, Fixed.FromInt(150), Fixed.FromInt(30)));
            float steerMax = 0f, steerMin = 0f;
            for (var tick = 0; tick < 40; tick++)
            {
                driveWorld.Step();
                var pose = humveeTracker.Update(humvee, catalog["AmericaVehicleHumvee"], driveWorld, 50f)!.Value;
                steerMax = Math.Max(steerMax, pose.WheelAngle);
                steerMin = Math.Min(steerMin, pose.WheelAngle);
            }

            Check.That(steerMax < 0.05f && steerMin > -0.05f, $"Driving straight leaves the Humvee's wheels straight ({steerMin}..{steerMax}).");
            driveWorld.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, driveWorld.Tick, humvee.Id, Fixed.FromInt(150), Fixed.FromInt(150)));
            for (var tick = 0; tick < 50; tick++)
            {
                driveWorld.Step();
                var pose = humveeTracker.Update(humvee, catalog["AmericaVehicleHumvee"], driveWorld, 50f)!.Value;
                steerMax = Math.Max(steerMax, pose.WheelAngle);
            }

            Check.That(steerMax > 0.02f && steerMax <= 22f * MathF.PI / 180f + 1e-4f, $"A left turn steers the Humvee's front wheels toward the turn, within the 22 degree limit ({steerMax}).");
        }

        var mapPath = "Maps/Alpine Assault/Alpine Assault.map";
        if (content.Exists(mapPath))
        {
            using var stream = content.OpenRead(mapPath);
            var map = SageMapDecoder.Decode(stream);
            var scenery = StaticScenery.Build(map.Objects, catalog);
            Check.That(scenery.Items.Length > 0 && scenery.Report.UnknownTemplates.Count == 0, $"Alpine Assault scenery builds ({scenery.Items.Length} items).");
            Console.WriteLine($"       scenery Alpine Assault: {scenery.Report.MapObjects} map objects, {scenery.Items.Length} scenery items, {scenery.Report.Waypoints} waypoints, " +
                              $"{scenery.Report.RoadOrBridgePoints} road/bridge points, {scenery.Report.WithoutModel.Values.Sum()} without model, {scenery.Report.UnknownTemplates.Values.Sum()} unknown");
        }
    }
}

file sealed class RealSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}

file sealed class ObjectRunner
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        Console.WriteLine(_failed > 0 ? $"object presentation: {_failed} failed, {_passed} passed." : $"object presentation: all {_passed} tests passed.");
        return _failed;
    }
}

file static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Throws<T>(Action action, string messagePart) where T : Exception
    {
        try { action(); }
        catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
    }
}
