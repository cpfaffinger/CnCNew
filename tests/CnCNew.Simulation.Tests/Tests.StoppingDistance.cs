using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;

/// <summary>
/// <c>NAMED_SET_STOPPING_DISTANCE</c>, <c>SET_STOPPING_DISTANCE</c> and the map's <c>objectStoppingDistance</c> (F-22): <c>Locomotor::setCloseEnoughDist</c> as
/// <c>AIInternalMoveToState::update</c> reads it, with its limit of four cells on the ground.
/// </summary>
static partial class Tests
{
    public static void StoppingDistanceEndsAMoveShort()
    {
        var placedStop = Placed("MoodGunner", 100m, 340m, "Squad", "Marked");
        placedStop = placedStop with { Properties = new MapProperties([..placedStop.Properties.Items, new MapProperty("objectStoppingDistance", MapPropertyType.Real, 0, 25m, string.Empty)]) };
        Fixed Left(string name)
        {
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("Set", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                    Scr.Do("NAMED_SET_STOPPING_DISTANCE", Scr.Unit("Short"), Scr.Real(30m)),
                    Scr.Do("NAMED_SET_STOPPING_DISTANCE", Scr.Unit("Limited"), Scr.Real(120m)),
                    Scr.Do("NAMED_SET_STOPPING_DISTANCE", Scr.Unit("Plain"), Scr.Real(0.25m)),
                    Scr.Do("SET_STOPPING_DISTANCE", Scr.Team("teamPlayerA"), Scr.Real(20m)),
                    Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit(name), Scr.Waypoint("Mid"))], oneShot: true),
            ], objects: [Placed("MoodGunner", 100m, 60m, "teamA", "Short"), Placed("MoodGunner", 100m, 120m, "teamA", "Limited"), Placed("MoodGunner", 100m, 180m, "teamA", "Plain"),
                         Placed("MoodGunner", 100m, 260m, "teamPlayerA", "Member"), placedStop], extraFiles: AttitudeFiles());
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Run(session, session.Frames(900));
            var unit = NamedIn(session, name)!;
            var dx = unit.X - Fixed.FromInt(250);
            var dy = unit.Y - Fixed.FromInt(200);
            Check.That(!unit.HasMoveTarget, $"{name} has ended its move.");
            var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
            Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");
            return Fixed.Sqrt(dx * dx + dy * dy);
        }

        var near = Left("Short");
        Check.That(near > Fixed.FromInt(25) && near <= Fixed.FromInt(31), $"A stopping distance of 30 ends the move about 30 from the goal ({near}).");
        var far = Left("Limited");
        Check.That(far > Fixed.FromInt(35) && far <= Fixed.FromInt(41), $"On the ground it is at most four cells ({far}).");
        var member = Left("Member");
        Check.That(member > Fixed.FromInt(15) && member <= Fixed.FromInt(21), $"SET_STOPPING_DISTANCE reaches the team's members ({member}).");
        var marked = Left("Marked");
        Check.That(marked > Fixed.FromInt(20) && marked <= Fixed.FromInt(26), $"The map's objectStoppingDistance acts ({marked}).");
        var plain = Left("Plain");
        Check.That(plain < Fixed.FromInt(2), $"Below half a unit the value is ignored: the unit walks to its goal ({plain}).");
    }
}
