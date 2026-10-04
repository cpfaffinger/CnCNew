using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static BonePose Bone(int x, int y, int z, decimal headingDegrees)
        => new(Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromInt(z), Fixed.FromDecimal(headingDegrees * 3.14159265m / 180m));

    /// <summary>A hand-made two-space, one-runway table in the airfield's own frame: the runway lies south of the building (y = -50) and runs from x = 40 to x = 160.</summary>
    private static ModelGeometry AirfieldBones()
    {
        var layout = new AirfieldLayout(2, 1,
            [Bone(-20, 30, 0, -90), Bone(-60, 30, 0, -90)],
            [Bone(-20, 40, 0, -90), Bone(-60, 40, 0, -90)],
            [Bone(-20, -50, 0, 0), Bone(-60, -50, 0, 0)],
            [Bone(40, -50, 0, 0)], [Bone(160, -50, 0, 0)], null);
        return new ModelGeometry([new KeyValuePair<string, AirfieldLayout>("AirTestAirfield", layout)]);
    }

    /// <summary>
    /// An airfield whose points come from the injected model bones: a produced jet appears at its hangar bone, parks on its parking bone facing the bone's heading, taxis to the runway start of the model
    /// and takes off along the model's runway line (not on a computed layout), lands on it again, and the run survives snapshots under the combined world fingerprint; a table that disagrees with the
    /// module or names an unknown airfield is refused.
    /// </summary>
    public static void AirAirfieldUsesTheModelBones()
    {
        var fixture = new SyntheticFixture(files: WingAirfieldFiles);
        var geometry = AirfieldBones();
        Check.That(geometry.Fingerprint.Length == 64 && geometry.Combine("abc") != "abc" && ModelGeometry.None.Combine("abc") == "abc", "The table has a fingerprint and changes the world fingerprint; the empty table does not.");
        Check.That(new ModelGeometry(geometry.Airfields).Fingerprint == geometry.Fingerprint, "The fingerprint depends on the content only.");
        Check.Throws<TemplateBuildException>(() => TemplateCatalog.Build(fixture.Graph, fixture.Bindings, new ModelGeometry([new KeyValuePair<string, AirfieldLayout>("NoSuchAirfield", geometry.Airfields["AirTestAirfield"])])), "NoSuchAirfield");

        SimulationWorld Create(ModelGeometry table)
        {
            var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, table);
            return new SimulationWorld(new WorldConfiguration(new WorldId(1), 9, CombatPlayers, SyntheticFixture.FixtureId, table.Combine(fixture.Fingerprint), string.Empty), templates, null);
        }

        // a table with other dimensions is refused when the airfield first needs it
        var wrong = new ModelGeometry([new KeyValuePair<string, AirfieldLayout>("AirTestAirfield", geometry.Airfields["AirTestAirfield"] with { Rows = 1 })]);
        var broken = Create(wrong);
        var brokenFeeder = new Feeder(broken);
        brokenFeeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        broken.Step();
        brokenFeeder.GiveMoney(1, 1000);
        broken.Step();
        Check.That(brokenFeeder.QueueProduction(1, Get(broken, "AirTestAirfield").Id.Value, "AirTestJet").Accepted, "The jet is queued.");
        Check.Throws<InvalidDataException>(() => { for (var tick = 0; tick < 200; tick++) broken.Step(); }, "spaces");

        var world = Create(geometry);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 1500, y: 500);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var airfield = Get(world, "AirTestAirfield");
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "The jet is queued.");
        StepUntil(world, () => world.Entities.Any(value => value.Template.Name == "AirTestJet"), 200, "The jet came out.");
        var jet = Get(world, "AirTestJet");
        var ai = JetOf(jet);
        Check.That((jet.X - Fixed.FromInt(480)).Abs() < Fixed.One && (jet.Y - Fixed.FromInt(540)).Abs() < Fixed.One, $"The jet appears at the hangar bone, not at a computed point ({jet.X}, {jet.Y}).");
        StepUntil(world, () => ai.Phase == JetAIUpdate.JetPhase.None, 600, "The jet parked.");
        Check.That((jet.X - Fixed.FromInt(480)).Abs() < Fixed.FromDecimal(0.2m) && (jet.Y - Fixed.FromInt(530)).Abs() < Fixed.FromDecimal(0.2m), $"It parks on the parking bone ({jet.X}, {jet.Y}).");
        Check.That(FixedTrig.AngleDifference(jet.Angle, Fixed.FromDecimal(-1.5708m)).Abs() < Fixed.FromDecimal(0.01m), $"It faces the heading of the bone ({jet.Angle}).");

        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1200, 500).Accepted, "The take-off order is accepted.");
        var liftedAt = (X: Fixed.Zero, Y: Fixed.Zero);
        var worstOffLine = Fixed.Zero;
        var restored = false;
        for (var tick = 0; tick < 1500 && liftedAt.X == Fixed.Zero; tick++)
        {
            world.Step();
            // from the runway start on, the roll stays on the runway line y = 450
            if (ai.Phase == JetAIUpdate.JetPhase.TakingOff && jet.X >= Fixed.FromInt(540)) worstOffLine = Fixed.Max(worstOffLine, (jet.Y - Fixed.FromInt(450)).Abs());
            if (ai.Phase == JetAIUpdate.JetPhase.TaxiToTakeoff && jet.X > Fixed.FromInt(500) && !restored)
            {
                restored = true;
                var copy = WorldSnapshot.Restore(WorldSnapshot.Write(world), world.Templates, world.Configuration.DefinitionFingerprint, world.Terrain);
                Check.That(copy.StateHash == world.StateHash, "A snapshot during the taxi to the runway restores to the same hash.");
                for (var follow = 0; follow < 60; follow++) Check.That(copy.Step().StateHash == world.Step().StateHash, $"The restored world follows (step {follow}).");
            }
            if (jet.Z >= Fixed.FromInt(5) && ai.AllowAirLoco) liftedAt = (jet.X, jet.Y);
        }
        Check.That(liftedAt.X != Fixed.Zero, "The jet took off.");
        Check.That((liftedAt.Y - Fixed.FromInt(450)).Abs() < Fixed.FromInt(2) && liftedAt.X > Fixed.FromInt(540) && liftedAt.X < Fixed.FromInt(700), $"It lifted off on the model's runway ({liftedAt.X}, {liftedAt.Y}).");
        Check.That(worstOffLine < Fixed.One, $"The roll stayed on the runway line (worst offset {worstOffLine}).");

        // out of ammunition: it returns and lands on the model's runway
        StepUntil(world, () => !jet.HasMoveTarget, 900, "The jet reached its destination.");
        var dummy = Get(world, "AirDummy");
        for (var shot = 0; shot < 2; shot++)
        {
            Check.That(feeder.FireWeapon(1, jet.Id.Value, dummy.Id.Value).Accepted, "A shot is accepted.");
            for (var i = 0; i < 6; i++) world.Step();
        }
        Check.That(ai.IsOutOfSpecialReloadAmmo(world), "The jet is out of ammunition.");
        var touchdown = (X: Fixed.Zero, Y: Fixed.Zero);
        var phases = new List<JetAIUpdate.JetPhase>();
        for (var tick = 0; tick < 4000 && !(phases.Contains(JetAIUpdate.JetPhase.ReloadAmmo) && ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco); tick++)
        {
            world.Step();
            if (phases.Count == 0 || phases[^1] != ai.Phase) phases.Add(ai.Phase);
            if (ai.Phase == JetAIUpdate.JetPhase.Landing && touchdown.X == Fixed.Zero && jet.Z <= world.GroundHeightAt(jet.X, jet.Y) + Fixed.FromDecimal(0.5m)) touchdown = (jet.X, jet.Y);
        }
        Check.That(!ai.AllowAirLoco && jet.WeaponClip == 2, $"The jet landed, taxied and reloaded: {string.Join(">", phases)}.");
        Check.That(touchdown.X > Fixed.FromInt(600) && (touchdown.Y - Fixed.FromInt(450)).Abs() < Fixed.FromInt(2), $"It touched down at the end of the model's runway ({touchdown.X}, {touchdown.Y}).");
        Check.That((jet.X - Fixed.FromInt(480)).Abs() < Fixed.FromDecimal(0.2m) && (jet.Y - Fixed.FromInt(530)).Abs() < Fixed.FromDecimal(0.2m), $"It stands on its parking bone again ({jet.X}, {jet.Y}).");

        // the computed layout (no table) puts the same airfield's runway elsewhere: the table is what moved it
        var plain = AirWorld(out _, out var plainFeeder, files: WingAirfieldFiles);
        plainFeeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        plain.Step();
        var computed = Get(plain, "AirTestAirfield").Modules.OfType<ParkingPlaceBehavior>().Single().InfoOfSpace(plain, 0);
        Check.That((computed.RunwayStart.Y - Fixed.FromInt(450)).Abs() > Fixed.FromInt(20), $"Without the table the runway is not where the bones put it ({computed.RunwayStart.Y}).");
    }
}
