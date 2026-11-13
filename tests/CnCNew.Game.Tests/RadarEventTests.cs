using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.World;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using static CnCNew.Game.Tests.MissionPresentationTests;

namespace CnCNew.Game.Tests;

/// <summary>
/// F-137, the space bar: <c>Radar::getLastEventLoc</c> is where the last <c>Radar::createEvent</c> happened, and the original makes one for much more than a finished building, an
/// upgrade and an attack - a beacon, a unit lost, a script's event, an infiltration (hijack, capture, hack, sabotage), stealth found or lost, a battle plan, an overcharge that gave out.
/// The synthetic tests hand the message feed the events the simulation raises; with <c>--content-dir</c> the same chain runs on a real skirmish, up to the camera of the space bar.
/// </summary>
internal static class RadarEventTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(RadarEventTests));
        harness.Run("A script's radar event is the last event, a beacon's pulse is not", ScriptEvents);
        harness.Run("A mission's script radar event reaches the feed through the director", DirectorEvent);
        harness.Run("A unit of the local player that is killed is a (fake) radar event, rationed, but not one lost to itself or someone else's", UnitLost);
        harness.Run("An infiltration of the local player's object (hijack, sabotage, capture start, hack) is a radar event with its line; someone else's is not", Infiltration);
        harness.Run("A stealth object found or lost, a battle plan and an overcharge that gave out are radar events", Notices);
        harness.Run("The sounds of a radar event: under attack, infiltration, stealth found or lost, for the local player only", Sounds);
        harness.Run("A drawn radar event plays RadarEvent once (not a beacon's pulse, not a fake one) (the suite runs at 30, 60 and 120 Hz)", DrawnEventSound);
        harness.Run("A radar event is a triangle that shrinks over 1.5 s while it turns, fades over its last half second; a beacon's pulse is smaller and turns the other way (#275)", MarkerShape);
        harness.Run("The radar events to draw: their age in frames of the original at every rate, a script's too and white without a colour of its type, never a fake one (#275)", EventsToDraw);
        var index = Array.IndexOf(args, "--content-dir");
        if (index >= 0 && index + 1 < args.Length)
        {
            var directory = args[index + 1];
            harness.Run("Real data: the space bar looks at the last of the events (a beacon, a captured building, a lost unit)", () => SpaceBar(directory));
            harness.Run("Real data: the space bar walks back through the history of events, Shift+Space forward, a rest of three seconds or a new event starts at the newest", () => SpaceBarHistory(directory));
            harness.Run("Real data: the HUD draws a radar event over the radar as its turning triangle, the same in frames of the original at every rate (#275)", () => HudDrawsTheTriangle(directory));
            harness.Run("Real data: our beacon pulses on the radar every RadarPulseFrequency for RadarPulseDuration, a hidden one does not (#275)", () => BeaconPulses(directory));
            harness.Run("Real data: a defection (capture, car bomb) warns only of an object on the radar - its RadarPriority, or garrisonable or capturable without one (#276)", () => DefectNeedsTheRadar(directory));
        }
        return harness.Failures;
    }

    private static (HudMessageFeed Feed, SimulationWorld World, Entity Tank, Entity Barrel) Fixture()
    {
        var (map, fixture, terrain) = Mission([], [Placed("Tank", 100m, 100m, "teamHero", "Tank"), Placed("Barrel", 300m, 120m, "teamHero", "Barrel")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        session.Step();
        var world = session.World;
        var feed = new HudMessageFeed(new UiMessageLog(TextFixtures.Style.Settings), new LocalizedText(new StringTable("radar-events", StringTableFormat.Csf, [])), fixture.Graph);
        return (feed, world, world.Entities.Single(entity => entity.Template.Name == "Tank"), world.Entities.Single(entity => entity.Template.Name == "Barrel"));
    }

    private static SimEvent Event(SimulationWorld world, SimEventKind kind, EntityId entity, EntityId other = default, string text = "", long value = 0, long tick = -1, PlayerId player = default)
        => new(default, tick < 0 ? world.Tick : tick, kind, entity, other, player, text, value);

    private static void ScriptEvents()
    {
        var (feed, world, _, _) = Fixture();
        TestHarness.Assert(feed.LastRadarEvent is null, "no event before the first");
        feed.NoteRadarEvent(new Vector2(10, 20), 3, 1);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(10, 20), "a script's event is the last one (doRadarCreateEvent -> Radar::createEvent)");
        feed.NoteRadarEvent(new Vector2(50, 60), 5, 2);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(10, 20), "RADAR_EVENT_BEACON_PULSE does not become the last event");
        feed.NoteRadarEvent(new Vector2(70, 80), 0, 3);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(70, 80), "createEvent makes any other ordinal, even one without colours");
        TestHarness.Assert(world.Tick >= 0, "world exists");
    }

    private static void DirectorEvent()
    {
        var radar = Script("Radar", If("CONDITION_TRUE"), [Do("RADAR_CREATE_EVENT", Coord(123m, 456m, 0m), Int(3))]);
        var session = Start([radar], null);
        var director = new MissionPresentationDirector(session) { TextMeasure = new ShellTextMeasure(), TextStyle = TextFixtures.Style, TextScaleOptions = TextFixtures.Options };
        var seen = new List<RadarBlip>();
        director.RadarEventCreated += seen.Add;
        director.BeforeStep();
        session.Step();
        director.AfterStep();
        TestHarness.Assert(seen.Count == 1 && seen[0].X == 123f && seen[0].Y == 456f && seen[0].EventType == 3, $"the director announces the script's radar event ({seen.Count})");
    }

    private static void UnitLost()
    {
        var (feed, world, tank, barrel) = Fixture();
        var human = tank.Owner;
        var enemy = new EntityId(9999);
        feed.Observe(Event(world, SimEventKind.EntityDied, tank.Id, enemy), world, new PlayerId(human.Value + 5));
        TestHarness.Assert(feed.LastRadarEvent is null, "somebody else's unit lost is no event of ours");
        feed.Observe(Event(world, SimEventKind.EntityDied, tank.Id, tank.Id), world, human);
        TestHarness.Assert(feed.LastRadarEvent is null, "a unit that killed itself is not (selfInflicted)");
        feed.Observe(Event(world, SimEventKind.EntityDied, barrel.Id, enemy), world, human);
        TestHarness.Assert(feed.LastRadarEvent is null, "a structure is not a unit (the original has Eva for it, no fake event)");
        feed.Observe(Event(world, SimEventKind.EntityDied, tank.Id, enemy), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(tank), "a unit killed by another is the last event, at its place");
        feed.NoteRadarEvent(new Vector2(1, 2), 1, world.Tick);
        feed.Observe(Event(world, SimEventKind.EntityDied, tank.Id, enemy, tick: world.Tick + 1), world, human);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(1, 2), "a second loss within ten seconds and 250 units is rationed (Radar::tryEvent)");
        feed.Observe(Event(world, SimEventKind.EntityDied, tank.Id, enemy, tick: world.Tick + (long)world.Time.OriginalFrames(310)), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(tank), "and again after ten seconds");
        TestHarness.Assert(feed.Shown == 0, "no line for it (the original only makes the event)");
    }

    private static void Infiltration()
    {
        var (feed, world, tank, barrel) = Fixture(); // the fixture's labels are missing: a line shows its marker, which is enough to count lines
        var human = tank.Owner;
        var other = new PlayerId(human.Value + 5);
        var thief = new EntityId(9999);
        feed.Observe(Event(world, SimEventKind.ObjectConverted, tank.Id, thief, "HIJACKED", other.Value), world, human);
        TestHarness.Assert(feed.LastRadarEvent is null && feed.Shown == 0, "a vehicle hijacked from somebody else is not ours to be warned of");
        feed.Observe(Event(world, SimEventKind.ObjectConverted, tank.Id, thief, "HIJACKED", human.Value), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(tank) && feed.Shown == 1, "our hijacked vehicle: a radar event and RADAR:Infiltration");

        feed.NoteRadarEvent(new Vector2(1, 2), 1, 1);
        feed.Observe(Event(world, SimEventKind.Sabotaged, barrel.Id, thief, "SabotagePowerPlantCrateCollide"), world, new PlayerId(human.Value + 5));
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(1, 2), "a sabotage of somebody else's building is not ours");
        feed.Observe(Event(world, SimEventKind.Sabotaged, barrel.Id, thief, "SabotagePowerPlantCrateCollide"), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(barrel) && feed.Shown == 2, "our sabotaged building: a radar event and a line");

        feed.NoteRadarEvent(new Vector2(3, 4), 1, 1);
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, thief, RadarNotices.Infiltration), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(barrel) && feed.Shown == 3, "the start of a capture or a hack on our building");
        feed.NoteRadarEvent(new Vector2(5, 6), 1, 1);
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, thief, RadarNotices.Infiltration), world, other);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(5, 6) && feed.Shown == 3, "and not on somebody else's");

        // Object::defect needs two real players: a car bomb or a capture between sides without a faction makes no event.
        feed.NoteRadarEvent(new Vector2(7, 8), 1, 1);
        feed.Observe(Event(world, SimEventKind.ObjectConverted, tank.Id, thief, "CARBOMB", human.Value), world, human);
        feed.Observe(Event(world, SimEventKind.StructureCaptured, barrel.Id, thief, value: human.Value, player: other), world, human);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(7, 8) && feed.Shown == 3, "a defection needs two playable sides");
    }

    private static void Notices()
    {
        var (feed, world, tank, barrel) = Fixture();
        var human = tank.Owner;
        var other = new PlayerId(human.Value + 5);
        var spotter = new EntityId(9999);

        // Stealth: lost (our unit revealed, always an event) and found (our detector found one, rationed).
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, spotter, RadarNotices.StealthRevealed), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(tank) && feed.Shown == 1, "our stealth unit revealed: a radar event and a line");
        feed.NoteRadarEvent(new Vector2(1, 2), 1, 1);
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, spotter, RadarNotices.StealthRevealed), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(tank) && feed.Shown == 2, "a unit (not a mine) revealed again makes its event again");
        feed.NoteRadarEvent(new Vector2(3, 4), 1, 1);
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, spotter, RadarNotices.StealthRevealed), world, other);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(3, 4) && feed.Shown == 2, "somebody's stealth unit revealed by somebody that is not us tells us nothing");
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, barrel.Id, RadarNotices.StealthRevealed), world, other);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(3, 4), "(the detector must be ours)");

        // Battle plan: every client's radar, whoever's plan it is.
        feed.NoteRadarEvent(new Vector2(5, 6), 1, 1);
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, text: RadarNotices.BattlePlan), world, other);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(barrel), "a strategy center unpacking a plan is an event for everybody");

        // Overcharge: ours only, with its line.
        var shown = feed.Shown;
        feed.NoteRadarEvent(new Vector2(7, 8), 1, 1);
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, text: RadarNotices.OverchargeExhausted), world, other);
        TestHarness.Assert(feed.LastRadarEvent == new Vector2(7, 8) && feed.Shown == shown, "somebody else's overcharge tells us nothing");
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, text: RadarNotices.OverchargeExhausted), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(barrel) && feed.Shown == shown + 1, "ours: a radar event and GUI:OverchargeExhausted");
    }

    private static List<HudMessageFeed.RadarCue> Cues(HudMessageFeed feed)
    {
        var cues = new List<HudMessageFeed.RadarCue>();
        feed.RadarSound += cues.Add;
        return cues;
    }

    private static void Sounds()
    {
        var (feed, world, tank, barrel) = Fixture();
        var cues = Cues(feed);
        var human = tank.Owner;
        var other = new PlayerId(human.Value + 5);
        var enemy = new EntityId(9999);

        // Radar::tryUnderAttackEvent: the sound is the structure's in every branch but the harvester's; the base of the player is announced by Eva.
        feed.Observe(Event(world, SimEventKind.EntityDamaged, tank.Id, enemy, value: 5), world, human);
        var harvester = tank.Template.IsKindOf("HARVESTER");
        var expectedHook = harvester ? "RadarNotifyHarvesterUnderAttackSound" : "RadarNotifyStructureUnderAttackSound";
        TestHarness.Assert(cues.Count == 1 && cues[0].Hook == expectedHook && cues[0].Eva is null, $"an attacked unit plays its under attack sound ({cues.Count})");
        feed.Observe(Event(world, SimEventKind.EntityDamaged, tank.Id, enemy, value: 5), world, human);
        TestHarness.Assert(cues.Count == 1, "an attack within ten seconds and 250 units is rationed with its sound");
        feed.Observe(Event(world, SimEventKind.EntityDamaged, barrel.Id, enemy, value: 5), world, other);
        TestHarness.Assert(cues.Count == 1, "somebody else's object under attack is silent");
        feed.Observe(Event(world, SimEventKind.EntityDamaged, barrel.Id, enemy, value: 5, tick: world.Tick + (long)world.Time.OriginalFrames(310)), world, human);
        var base_ = barrel.Template.IsKindOf("STRUCTURE") && barrel.Template.IsKindOf("MP_COUNT_FOR_VICTORY");
        TestHarness.Assert(cues.Count == 2 && cues[1].Hook == "RadarNotifyStructureUnderAttackSound" && cues[1].Eva == (base_ ? "BaseUnderAttack" : null),
            $"an attacked building: the structure sound, and Eva's BaseUnderAttack only for a building that counts for victory ({base_})");

        // Radar::tryInfiltrationEvent: only against the local player's own object.
        cues.Clear();
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, enemy, RadarNotices.Infiltration), world, other);
        TestHarness.Assert(cues.Count == 0, "an infiltration of somebody else's building is silent");
        feed.Observe(Event(world, SimEventKind.RadarNotice, barrel.Id, enemy, RadarNotices.Infiltration), world, human);
        feed.Observe(Event(world, SimEventKind.Sabotaged, barrel.Id, enemy, "SabotagePowerPlantCrateCollide"), world, human);
        feed.Observe(Event(world, SimEventKind.ObjectConverted, tank.Id, enemy, "HIJACKED", human.Value), world, human);
        TestHarness.Assert(cues.Count == 3 && cues.All(cue => cue.Hook == "RadarNotifyInfiltrationSound" && cue.Eva is null), "a hack, a sabotage and a hijack of ours play the infiltration sound");

        // StealthDetectorUpdate::update.
        cues.Clear();
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, enemy, RadarNotices.StealthRevealed), world, human);
        TestHarness.Assert(cues.Count == 1 && cues[0].Hook == "StealthNeutralizedSound", "our stealth unit revealed: StealthNeutralizedSound");
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, barrel.Id, RadarNotices.StealthRevealed), world, other);
        TestHarness.Assert(cues.Count == 1, "somebody else's stealth unit revealed by somebody else: nothing");
        feed.Observe(Event(world, SimEventKind.RadarNotice, tank.Id, barrel.Id, RadarNotices.StealthRevealed), world, human);
        // tank.Owner == human, so the unit revealed is ours again; a unit of the other side revealed by our detector is the discovered case.
        var foreign = world.Entities.FirstOrDefault(entity => entity.Owner != human);
        if (foreign is not null)
        {
            feed.Observe(Event(world, SimEventKind.RadarNotice, foreign.Id, barrel.Id, RadarNotices.StealthRevealed), world, human);
            TestHarness.Assert(cues.Any(cue => cue.Hook == "StealthDiscoveredSound"), "a stealth unit of the enemy found by our detector: StealthDiscoveredSound");
        }
    }

    private static void DrawnEventSound()
    {
        {
            var (feed, world, _, _) = Fixture();
            var cues = Cues(feed);
            var perFrame = (long)world.Time.OriginalFrames(1);
            var rate = world.Time.TicksPerSecond;
            feed.NoteRadarEvent(new Vector2(10, 20), 3, 100);
            feed.NoteRadarEvent(new Vector2(30, 40), 5, 100);   // a beacon's pulse
            feed.NoteRadarEvent(new Vector2(50, 60), 10, 100);  // a fake event
            _ = feed.RadarEventsToDraw(new GameClock(100, rate, 0)).ToList();
            TestHarness.Assert(cues.Count == 1 && cues[0].Event == "RadarEvent", $"the first drawing plays RadarEvent once, a pulse and a fake event are silent ({cues.Count})");
            _ = feed.RadarEventsToDraw(new GameClock(100 + perFrame, rate, 0)).ToList();
            TestHarness.Assert(cues.Count == 1, "and not again");
            // An event drawn only after its four seconds is over is never seen, so never heard.
            feed.NoteRadarEvent(new Vector2(1, 1), 4, 200);
            _ = feed.RadarEventsToDraw(new GameClock(200 + (long)world.Time.OriginalFrames(4 * 30 + 1), rate, 0)).ToList();
            TestHarness.Assert(cues.Count == 1, "an event that is over before it is drawn is silent");
        }
    }

    private static void MarkerShape()
    {
        var size = new Vector2(256, 128);
        var place = new Vector2(100, 60);
        var (red, pink) = (new Rgba32(255, 0, 0, 255), new Rgba32(255, 128, 128, 255));
        RadarEventEdge[] At(long age, bool beacon = false, long life = RadarEventMarker.DefaultLifeFrames) => RadarEventMarker.Edges(place, size, age, life, beacon, red, pink);
        static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;
        // A corner at a size in radar cells and an angle on the radar: 256 / 128 pixels a cell across, 128 / 128 down, y up on the radar and down on the screen.
        Vector2 Corner(float cells, float angle) => place + new Vector2(MathF.Cos(angle) * cells * 2f, -MathF.Sin(angle) * cells * 1f);

        // W3DRadar::drawSingleGenericEvent: the first size is half the drawn width (128 px) in radar cells, scaled to the screen; corner 0 at angle 0.
        var first = At(0);
        TestHarness.Assert(first.Length == 3 && first.All(edge => edge.Start == red && edge.End == pink), "three edges from the first colour to the second");
        TestHarness.Assert(Near(first[0].From, Corner(128, 0)) && Near(first[1].From, Corner(128, 2 * MathF.PI / 3)) && Near(first[2].From, Corner(128, -2 * MathF.PI / 3)),
            $"at its start the triangle's corners lie half the radar's width in cells away: {first[0].From} {first[1].From} {first[2].From}");
        TestHarness.Assert(first[0].To == first[1].From && first[2].To == first[0].From, "the edges close the triangle");
        // Shrinking: REAL_TO_INT of the size; the added angle 2 pi age / 45 is subtracted.
        var cells = (int)(128 * (1 - 20 / 45f));
        TestHarness.Assert(Near(At(20)[0].From, Corner(cells, -2 * MathF.PI * 20 / 45f)), $"after 20 frames {cells} cells, turned by 20/45 of a circle: {At(20)[0].From}");
        // After 1.5 s (45 frames) it is at its smallest, six cells, and has turned once round; it keeps turning at that size.
        TestHarness.Assert(Near(At(45)[0].From, Corner(6, 0)), $"after 45 frames the smallest size of six cells, one turn done: {At(45)[0].From}");
        TestHarness.Assert(Near(At(90)[0].From, Corner(6, 0)) && Near(At(60)[0].From, Corner(6, -2 * MathF.PI * 60 / 45f)), "it keeps turning at that size");
        // The generic event turns clockwise on the radar (corner 0 goes down on the screen first), a beacon's pulse the other way, and starts at a tenth of the width.
        TestHarness.Assert(At(5)[0].From.Y > place.Y && At(5, beacon: true)[0].From.Y < place.Y, "the event and a beacon's pulse turn opposite ways");
        TestHarness.Assert(Near(At(0, beacon: true)[0].From, Corner(25, 0)), $"a beacon's pulse starts at a tenth of the width (int 25.6 = 25 cells): {At(0, beacon: true)[0].From}");
        TestHarness.Assert(Near(At(40, beacon: true)[0].From, Corner(6, 2 * MathF.PI * 40 / 45f)), "and shrinks to the same six cells");
        // Radar::internalCreateEvent: fadeFrame is half a second before dieFrame; the alpha is the share left, truncated; the frame after dieFrame it is gone.
        TestHarness.Assert(At(105)[0].Start.A == 255 && At(112)[0].Start.A == (byte)(255 * (1 - 7 / 15f)) && At(112)[0].End.A == At(112)[0].Start.A, "full until 105 frames, then fading");
        TestHarness.Assert(At(120)[0].Start.A == 0 && At(121).Length == 0 && At(-1).Length == 0, "nothing left at 120 frames, gone after it");
        TestHarness.Assert(At(0, true, 15)[0].Start.A == 255 && At(5, true, 15)[0].Start.A == (byte)(255 * (1 - 5 / 15f)), "a pulse of 15 frames fades from its first frame");
    }

    private static void EventsToDraw()
    {
        var (feed, world, _, _) = Fixture();
        var rate = world.Time.TicksPerSecond;
        var perFrame = rate / 30;
        // Made in the last tick of an original frame (at 60 Hz the second), it is of that frame, as at 30 Hz.
        var made = 300L * perFrame + perFrame - 1;
        feed.NoteRadarEvent(new Vector2(10, 20), 3, made);
        feed.NoteRadarEvent(new Vector2(30, 40), 0, made);   // RADAR_EVENT_INVALID: not in the table
        feed.NoteRadarEvent(new Vector2(50, 60), 42, made);  // a script's ordinal past the table
        feed.NoteRadarEvent(new Vector2(70, 80), 10, made);  // fake
        var drawn = feed.RadarEventsToDraw(new GameClock(310L * perFrame, rate, 0.5)).ToList();
        TestHarness.Assert(drawn.Count == 3, $"three of four drawn, not the fake one ({drawn.Count})");
        TestHarness.Assert(drawn.All(item => item.AgeFrames == 10 && item.LifeFrames == 120), $"ten frames of the original old at {rate} Hz: {string.Join(", ", drawn.Select(item => item.AgeFrames))}");
        TestHarness.Assert(drawn[0].Color1 == new Rgba32(255, 0, 0, 255) && drawn[0].Color2 == new Rgba32(255, 128, 128, 255), "under attack: red to pink");
        TestHarness.Assert(drawn.Skip(1).All(item => item.Color1 == new Rgba32(255, 255, 255, 255) && item.Color2 == item.Color1), "a type without colours is white (createEvent's default)");
        TestHarness.Assert(feed.RadarEventsToDraw(new GameClock(420L * perFrame + perFrame - 1, rate, 0)).Count() == 3 && !feed.RadarEventsToDraw(new GameClock(421L * perFrame, rate, 0)).Any(),
            "active to its dieFrame (120 frames on), gone in the frame after");
    }

    private static (LiveMatch Match, MatchController Controller) RadarMatch(string directory)
    {
        var data = GameData.Load(directory, rate: CnCNew.Tests.TestRate.Default);
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var america = data.Factions.First(faction => faction.Faction.Name == "FactionAmerica").Faction.Name;
        var setup = new MatchSetup(mapPath, america, [data.Factions.First(faction => faction.Faction.Name != america).Faction.Name], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        var perFrame = match.World.Time.TicksPerSecond / 30;
        while (!match.RadarOn && match.World.Tick < 30L * 30 * perFrame)
        {
            match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        TestHarness.Assert(match.RadarOn, "premise: America's command center gives a radar");
        // The radar's own upgrade is an event at the command center: let it and every other event of the start die first.
        var quiet = match.World.Tick + 130L * perFrame;
        while (match.World.Tick < quiet) Step(match);
        TestHarness.Assert(!controller.Messages.RadarEventsToDraw(match.Clock).Any(), "premise: no radar event is active");
        return (match, controller);
    }

    private static void Step(LiveMatch match) => match.Advance(1.0 / match.World.Time.TicksPerSecond);

    private static void HudDrawsTheTriangle(string directory)
    {
        var (match, controller) = RadarMatch(directory);
        var perFrame = match.World.Time.TicksPerSecond / 30;
        (List<UiLineDraw> Lines, UiRect Radar) Draw()
        {
            var commands = controller.BuildDrawList(1280, 720).Commands;
            var radar = commands.OfType<UiCustomDraw>().Single(draw => draw.Callback == "W3DLeftHUDDraw");
            var at = commands.IndexOf(radar);
            TestHarness.Assert(commands[at + 1] is UiPushClip, "the radar's clip comes first");
            var end = commands.IndexOf(commands.Skip(at + 1).First(command => command is UiPopClip));
            return (commands.Skip(at + 2).Take(end - at - 2).OfType<UiLineDraw>().ToList(), radar.Rect);
        }
        var outline = Draw().Lines.Count;
        // An event in the middle of the map, made in the first tick of an original frame; drawn 20 frames of the original later.
        while (!match.Clock.IsOriginalFrameBoundary) Step(match);
        var made = match.World.Tick;
        var middle = match.Minimap.WorldExtent / 2;
        controller.Messages.NoteRadarEvent(middle, 3, made);
        while (match.World.Tick < made + 20 * perFrame) Step(match);
        TestHarness.Assert(match.World.Tick == made + 20 * perFrame, $"premise: twenty frames on ({match.World.Tick - made} ticks)");
        var (lines, rect) = Draw();
        TestHarness.Assert(lines.Count == outline + 12, $"the event adds its three edges in four pieces each before the camera's frame ({outline} -> {lines.Count})");
        var scale = Math.Min(rect.Width / (float)match.Minimap.Width, rect.Height / (float)match.Minimap.Height);
        var offset = new Vector2(rect.X + (rect.Width - match.Minimap.Width * scale) / 2f, rect.Y + (rect.Height - match.Minimap.Height * scale) / 2f);
        var edges = RadarEventMarker.Edges(match.Minimap.WorldToPixel(middle) * scale, new Vector2(match.Minimap.Width * scale, match.Minimap.Height * scale), 20, 120, false,
            new Rgba32(255, 0, 0, 255), new Rgba32(255, 128, 128, 255));
        var (first, last) = (lines[0], lines[3]);
        var (from, to) = (offset + edges[0].From, offset + edges[0].To);
        TestHarness.Assert(Vector2.Distance(new Vector2(first.X0, first.Y0), from) < 0.01f && Vector2.Distance(new Vector2(last.X1, last.Y1), to) < 0.01f,
            $"the first edge is the marker's at 20 frames of the original, at {match.World.Time.TicksPerSecond} Hz: ({first.X0},{first.Y0})-({last.X1},{last.Y1}) against {from}-{to}");
        TestHarness.Assert(first.Color.R == 255 && first.Color.G < last.Color.G && first.Color.A == 255, $"shaded from red towards pink ({first.Color} .. {last.Color})");
        TestHarness.Assert(lines.Skip(12).SequenceEqual(Draw().Lines.Skip(12)), "the camera's frame is drawn after it, unchanged");
        // Past its dieFrame it is gone.
        while (match.World.Tick < made + 121 * perFrame) Step(match);
        TestHarness.Assert(Draw().Lines.Count == outline, "gone after its four seconds");
    }

    private static void BeaconPulses(string directory)
    {
        var (match, controller) = RadarMatch(directory);
        var world = match.World;
        var perFrame = world.Time.TicksPerSecond / 30;
        var feed = controller.Messages;
        var place = SimulationQueries.GroundOf(world.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("COMMANDCENTER"))) + new Vector2(60, 60);
        match.Commands.Issue((sequence, tick) => new CnCNew.Simulation.Commands.PlaceBeaconCommand(match.Human, sequence, tick,
            CnCNew.Simulation.Numerics.Fixed.FromInt((int)place.X), CnCNew.Simulation.Numerics.Fixed.FromInt((int)place.Y)));
        Entity? beacon = null;
        for (var i = 0; i < 20 * perFrame && beacon is null; i++)
        {
            Step(match);
            beacon = world.Entities.FirstOrDefault(entity => entity.Owner == match.Human && world.IsBeacon(entity));
        }
        TestHarness.Assert(beacon is not null, "premise: our beacon stands");
        // The pulses made while the HUD steps on, by the frame of the original they were made in.
        var frames = new List<long>();
        var seen = match.Clock.OriginalFrames;
        var history = feed.RadarHistoryVersion;
        for (var frame = 0; frame < 100; frame++)
        {
            for (var tick = 0; tick < perFrame; tick++) Step(match);
            var pulses = feed.RadarEventsToDraw(match.Clock).Where(item => item.IsBeaconPulse).ToList();
            foreach (var pulse in pulses.Where(item => item.AgeFrames == 0)) frames.Add(match.Clock.OriginalFrames - seen);
            TestHarness.Assert(pulses.All(item => item.LifeFrames == 15 && Vector2.Distance(item.Position, SimulationQueries.GroundOf(beacon!)) < 0.01f), "a pulse lives RadarPulseDuration (500 ms) at the beacon");
        }
        TestHarness.Assert(frames.Count == 3 && frames[1] - frames[0] == 31 && frames[2] - frames[1] == 31, $"a pulse every 31 frames (more than RadarPulseFrequency's 30): {string.Join(", ", frames)}");
        TestHarness.Assert(feed.RadarHistoryVersion == history, "a pulse is no event of the space bar");
        match.HideBeacon(beacon!.Id);
        var hiddenFrom = match.Clock.OriginalFrames;
        for (var tick = 0; tick < 40 * perFrame; tick++) Step(match);
        TestHarness.Assert(!feed.RadarEventsToDraw(match.Clock).Any(item => item.IsBeaconPulse && item.AgeFrames < match.Clock.OriginalFrames - hiddenFrom - 15), "a hidden beacon does not pulse");
    }

    private static void DefectNeedsTheRadar(string directory)
    {
        var data = GameData.Load(directory, rate: CnCNew.Tests.TestRate.Default);
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        var world = match.World;
        var human = match.Human;
        var feed = controller.Messages;
        for (var i = 0; i < (int)world.Time.OriginalFrames(4); i++) match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        var enemy = world.Entities.First(entity => entity.Owner != human && !entity.Owner.IsSystem && entity.Template.IsKindOf("COMMANDCENTER"));
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        ulong sequence = 3_100_000;
        Entity Spawn(string name, int dx)
        {
            var known = world.Entities.Select(entity => entity.Id).ToHashSet();
            TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(PlayerId.System, sequence++, world.Tick, name, human, center.X + CnCNew.Simulation.Numerics.Fixed.FromInt(dx),
                center.Y + CnCNew.Simulation.Numerics.Fixed.FromInt(300), CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero)).Accepted, $"{name} is spawned");
            for (var i = 0; i < (int)world.Time.OriginalFrames(2); i++) match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            return world.Entities.First(entity => entity.Template.Name == name && !known.Contains(entity.Id));
        }
        string? Priority(string name) => data.Graph.Definitions.First(definition => definition is ObjectDefinition && definition.OriginalName == name).Fields
            .LastOrDefault(field => field.Name.Equals("RadarPriority", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
        // The premises, read from the data: a plain civilian monument sets no RadarPriority and is neither garrisonable nor capturable; a civilian house sets none either
        // but is garrisonable; a bridge tower is NOT_ON_RADAR; the command center is a STRUCTURE.
        TestHarness.Assert(Priority("BaghdadVictoryMemorial01") is null or "INVALID" && Priority("GermanTownHouse1") is null or "INVALID" && Priority("GenericBridgeTower") == "NOT_ON_RADAR" && Priority(center.Template.Name) == "STRUCTURE",
            $"premise: the four radar priorities of the data (monument {Priority("BaghdadVictoryMemorial01")}, house {Priority("GermanTownHouse1")}, tower {Priority("GenericBridgeTower")}, {center.Template.Name} {Priority(center.Template.Name)})");
        var monument = Spawn("BaghdadVictoryMemorial01", -300);
        var house = Spawn("GermanTownHouse1", 0);
        var tower = Spawn("GenericBridgeTower", 300);
        TestHarness.Assert(!monument.Template.IsKindOf("CAPTURABLE") && house.Modules.OfType<CnCNew.Simulation.Modules.IContainerModule>().FirstOrDefault() is { IsGarrisonable: true },
            "premise: the monument is not capturable, the house is garrisonable");
        SimEvent Captured(Entity entity) => new(default, world.Tick, SimEventKind.StructureCaptured, entity.Id, enemy.Id, enemy.Owner, string.Empty, human.Value);
        SimEvent CarBomb(Entity entity) => new(default, world.Tick, SimEventKind.ObjectConverted, entity.Id, enemy.Id, default, "CARBOMB", human.Value);
        var lines = feed.Shown;
        // Object::defect: "if( friend_getRadarData() && ...)": no radar object, no infiltration event, no line.
        foreach (var off in new[] { monument, tower })
        {
            feed.Observe(Captured(off), world, human);
            feed.Observe(CarBomb(off), world, human);
        }
        TestHarness.Assert(feed.Shown == lines && feed.LastRadarEvent != SimulationQueries.GroundOf(monument) && feed.LastRadarEvent != SimulationQueries.GroundOf(tower),
            $"an object off the radar (no priority, or NOT_ON_RADAR) defects without a warning ({feed.Shown - lines} lines)");
        feed.Observe(Captured(house), world, human);
        TestHarness.Assert(feed.Shown == lines + 1 && feed.LastRadarEvent == SimulationQueries.GroundOf(house), "a garrisonable house without a priority is on the radar as a structure: a warning");
        feed.Observe(Captured(center), world, human);
        TestHarness.Assert(feed.Shown == lines + 2 && feed.LastRadarEvent == SimulationQueries.GroundOf(center), "a STRUCTURE: a warning");
        var vehicle = world.Entities.First(entity => entity.Owner == human && Priority(entity.Template.Name) is "UNIT" or "LOCAL_UNIT_ONLY");
        feed.Observe(CarBomb(vehicle), world, human);
        TestHarness.Assert(feed.Shown == lines + 3 && feed.LastRadarEvent == SimulationQueries.GroundOf(vehicle), "a car bomb of an object on the radar as a unit: a warning");
        // ConvertToHijackedVehicleCrateCollide calls tryInfiltrationEvent itself: no radar test there.
        feed.Observe(new SimEvent(default, world.Tick, SimEventKind.ObjectConverted, monument.Id, enemy.Id, default, "HIJACKED", human.Value), world, human);
        TestHarness.Assert(feed.Shown == lines + 4, "a hijack asks no radar");
    }

    private static void SpaceBar(string directory)
    {
        var data = GameData.Load(directory, rate: CnCNew.Tests.TestRate.Default);
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        for (var i = 0; i < (int)match.World.Time.OriginalFrames(4); i++)
        {
            match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        var world = match.World;
        var human = match.Human;
        var feed = controller.Messages;

        var enemy = world.Entities.First(entity => entity.Owner != human && !entity.Owner.IsSystem && entity.Template.IsKindOf("COMMANDCENTER"));
        var mine = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var unit = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        SimEvent Make(SimEventKind kind, EntityId entity, EntityId other, long value = 0, PlayerId player = default, string text = "") => new(default, world.Tick, kind, entity, other, player, text, value);
        void Press() => controller.Input.DispatchMeta(MetaAction.ViewLastRadarEvent, world, match.Camera);

        // A building of ours captured: the old owner's defect, between two playable sides.
        feed.Observe(Make(SimEventKind.StructureCaptured, mine.Id, enemy.Id, human.Value, enemy.Owner), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(mine), "our building captured by the enemy is the last event");
        TestHarness.Assert(feed.Log.Lines.Any(line => line.Text.Contains("nfiltrat", StringComparison.OrdinalIgnoreCase)), $"with RADAR:Infiltration ({string.Join(" | ", feed.Log.Lines.Select(line => line.Text))})");
        // The same capture seen from the other side (our building the thief's) is not our warning.
        var before = feed.Log.Lines.Length;
        feed.Observe(Make(SimEventKind.StructureCaptured, enemy.Id, mine.Id, enemy.Owner.Value, human), world, human);
        TestHarness.Assert(feed.Log.Lines.Length == before, "a capture we make warns the other side, not us");

        // A unit lost, later: the space bar goes to it.
        feed.Observe(Make(SimEventKind.EntityDied, unit.Id, enemy.Id), world, human);
        TestHarness.Assert(feed.LastRadarEvent == SimulationQueries.GroundOf(unit), "a unit of ours killed by the enemy is the last event");
        Press();
        var seen = match.Camera.Pivot;
        var target = SimulationQueries.GroundOf(unit);
        TestHarness.Assert(Vector2.Distance(seen, target) < 1f || Vector2.Distance(seen, target) < Vector2.Distance(SimulationQueries.GroundOf(mine), target), $"the space bar centres the view on it (pivot {seen}, unit {target})");
    }

    private static void SpaceBarHistory(string directory)
    {
        var data = GameData.Load(directory, rate: CnCNew.Tests.TestRate.Default);
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        // Game time is the controller's own clock, advanced by Update; no real time passes in this test.
        void Wait(double seconds) { for (var rest = seconds; rest > 0; rest -= 0.25) controller.Update(0.25); }
        for (var i = 0; i < (int)match.World.Time.OriginalFrames(4); i++) match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        // Let the camera settle: its limits depend on its height, which eases to the default for a while.
        Wait(5);
        var feed = controller.Messages;
        var world = match.World;
        var places = world.Entities.Select(SimulationQueries.GroundOf)
            .Distinct().OrderBy(place => place.X).ThenBy(place => place.Y).ToList();
        // Spread the places out so that no two are the same camera position.
        var spread = new List<Vector2>();
        foreach (var place in places)
        {
            // The camera keeps its pivot inside the map; only a place it can reach exactly tells which event it looked at.
            match.Camera.CenterOn(place);
            if (Vector2.Distance(match.Camera.Pivot, place) > 0.01f) continue;
            if (spread.All(other => Vector2.Distance(other, place) > 150f)) spread.Add(place);
        }
        TestHarness.Assert(spread.Count >= 5, $"premise: five places apart ({spread.Count})");
        var events = spread.Take(5).ToList();

        long time = 10_000;
        void Tap(params KeyCode[] chord)
        {
            foreach (var key in chord) controller.Process(new KeyEvent(time += 16, key, true));
            foreach (var key in chord.Reverse()) controller.Process(new KeyEvent(time += 16, key, false));
        }
        void Space() { Tap(KeyCode.Space); }
        void ShiftSpace() { Tap(KeyCode.LeftShift, KeyCode.Space); }
        void Expect(Vector2 place, string what)
        {
            // The camera's limits move with its state: compare with where it keeps the pivot when it is sent to the place now.
            var seen = match.Camera.Pivot;
            match.Camera.CenterOn(place);
            var reachable = match.Camera.Pivot;
            TestHarness.Assert(Vector2.Distance(seen, reachable) < 1f, $"{what} (pivot {seen}, wanted {place}, nearest the camera can look {reachable})");
        }

        Space();
        var before = match.Camera.Pivot;
        var already = feed.RadarHistory.Count;
        for (var i = 0; i < events.Count; i++) { feed.NoteRadarEvent(events[i], 3, world.Tick); Wait(0.5); }
        var beforePulse = feed.RadarHistory.Count;
        feed.NoteRadarEvent(new Vector2(1, 1), 5, world.Tick);
        TestHarness.Assert(feed.RadarHistory.Count == beforePulse && feed.RadarHistory[^1] == events[4], $"a beacon's pulse is no history entry ({already} before, {beforePulse} after the five, {feed.RadarHistory.Count} after the pulse)");
        Wait(5);

        Space();
        Expect(events[4], "the first press looks at the newest event");
        Wait(1);
        Space();
        Expect(events[3], "the second press, a second later, goes one event back");
        Wait(1);
        Space();
        Expect(events[2], "the third goes one further back");
        Wait(1);
        ShiftSpace();
        Expect(events[3], "Shift+Space goes one forward again");
        ShiftSpace();
        Expect(events[4], "and again, to the newest");
        ShiftSpace();
        Expect(events[4], "the newest end holds");
        for (var i = 0; i < 4; i++) { Wait(0.5); Space(); }
        Expect(events[0], "older events are reached one by one (four presses from the newest)");
        Wait(3.5);
        Space();
        Expect(events[4], "after three seconds of rest the pointer is back at the newest event");
        Wait(2.5);
        Space();
        Expect(events[3], "a rest of less than three seconds keeps the pointer");
        feed.NoteRadarEvent(events[1], 3, world.Tick);
        Wait(0.5);
        Space();
        Expect(events[1], "a new event puts the pointer on itself at once");
        Space();
        Expect(events[4], "and the next press goes back from it");
        TestHarness.Assert(before != events[4], "premise: the camera had to move");
        for (var i = 0; i < 40; i++) feed.NoteRadarEvent(events[i % events.Count], 3, world.Tick);
        TestHarness.Assert(feed.RadarHistory.Count == HudMessageFeed.MaxRadarHistory, "the history keeps the last thirty-two events");
        Wait(4);
        for (var i = 0; i < HudMessageFeed.MaxRadarHistory; i++) { Wait(0.1); Space(); }
        // The 40 events are events[0..4] cycled; the 32 kept are the last of them, so the oldest kept is the ninth event, events[8 % 5].
        Expect(events[8 % events.Count], "the first press looks at the newest, thirty-one more reach the oldest kept event");
        for (var i = 0; i < 5; i++) { Wait(0.1); Space(); }
        Expect(events[8 % events.Count], "the oldest end holds: more presses stay on it");
    }
}
