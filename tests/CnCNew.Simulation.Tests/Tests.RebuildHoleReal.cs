using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #383 (the owner's playtest of 15 November 2026): a destroyed GLA building never came back - its hole stood and burned, and nothing happened. The rule that a hole sends out its
    /// worker was switched on only for missions and the original AI's session (<see cref="SimulationWorld.HolesRebuild"/>); the original's <c>RebuildHoleBehavior</c> works in every game.
    /// On real data, in a skirmish on Alpine Assault with a human GLA player against a computer: a Stinger Site and a barracks of the human die into their holes
    /// (<c>RebuildHoleExposeDie</c>, <c>HoleMaxHealth</c>), each hole sends its <c>GLAInfantryWorker</c> after <c>WorkerRespawnDelay</c> (20 s) from the hole, the worker cannot be
    /// selected and raises the structure where the hole lies, reconstructing and free of charge, the hole is masked meanwhile; a worker that is killed is replaced after the delay and
    /// resumes the same site; the finished building takes hole and worker away. At 30, 60 and 120 Hz, the same course in game time.
    /// </summary>
    public static void RealGlaHolesRebuildInASkirmish(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        string? course30 = null;
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"), IsHuman: true), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var world = session.World;
            Check.That(world.HolesRebuild, $"{rate}: a hole rebuilds in a skirmish, as in every game of the original.");
            for (var tick = 0; tick < rate.Time.OriginalFrames(30); tick++) session.Step();
            var player = session.Players[0].Id;
            var center = world.Entities.First(entity => entity.Owner == player && entity.Template.Name == "GLACommandCenter");

            // A Stinger Site and a barracks of the human, standing complete beside its command center.
            var built = new List<Entity>();
            foreach (var name in new[] { "GLAStingerSite", "GLABarracks" })
            {
                Check.That(catalog.TryGet(name, out var template), $"{name} is a template.");
                (Fixed X, Fixed Y)? place = null;
                for (var distance = 160; distance <= 400 && place is null; distance += 20)
                    for (var step = 0; step < 16 && place is null; step++)
                    {
                        var angle = Fixed.FromInt(step) * FixedTrig.Pi / Fixed.FromInt(8);
                        var (x, y) = (center.X + Fixed.FromInt(distance) * FixedTrig.Cos(angle), center.Y + Fixed.FromInt(distance) * FixedTrig.Sin(angle));
                        if (world.CheckBuildGround(template!, x, y, Fixed.Zero) == LegalBuildCode.Ok
                            && built.All(other => (other.X - x).Abs() + (other.Y - y).Abs() > Fixed.FromInt(200))) place = (x, y);
                    }
                Check.That(place is not null, $"{rate}: a legal place for {name}.");
                var id = world.RequestSpawn(template!.Index, player, place!.Value.X, place.Value.Y, world.GroundHeightAt(place.Value.X, place.Value.Y), Fixed.Zero);
                session.Step();
                built.Add(world.FindEntity(id)!);
            }
            Check.That(built.All(entity => !entity.HasStatus(ObjectStatusBits.UnderConstruction)), $"{rate}: the two buildings stand complete.");
            var money = world.MoneyOf(player);
            // The kill at the start of the same frame of the original at every rate.
            while (world.Tick < rate.Time.OriginalFrames(40)) session.Step();

            foreach (var building in built) world.Kill(building.Id);
            session.Step();
            var holes = built.Select(building => world.Entities.Single(entity => entity.Template.IsKindOf("REBUILD_HOLE") && entity.Owner == player
                && entity.X == building.X && entity.Y == building.Y)).ToArray();
            Check.That(holes[0].Template.Name == "GLAHoleStingerSite", $"{rate}: the Stinger Site left its hole ({holes[0].Template.Name}).");
            Check.That(holes[0].Body!.MaxHealth == Fixed.FromInt(500), $"{rate}: the hole has HoleMaxHealth ({holes[0].Body!.MaxHealth}).");
            var killedAt = world.Tick;

            // Nothing before WorkerRespawnDelay; then a worker of the human, unselectable, at the hole, and a site of the destroyed building under construction where the hole lies.
            var course = new List<string>();
            // A count of ticks as the frame of the original it lies in, counted from the frame of the event (a share of a frame above 30 Hz belongs to its frame).
            long Frames(long ticks) => (ticks + rate.Time.TicksPerOriginalFrame - 1) / rate.Time.TicksPerOriginalFrame;
            Entity? Worker(Entity hole) => hole.Modules.OfType<RebuildHoleBehavior>().Single() is { } behavior && world.FindEntity(behavior.Reconstructed) is { } site
                ? world.FindEntity(site.BuilderId) : null;
            Entity? Site(Entity hole) => world.FindEntity(hole.Modules.OfType<RebuildHoleBehavior>().Single().Reconstructed);
            var rises = new long[holes.Length];
            var completes = new long[holes.Length];
            var sites = new EntityId[holes.Length];
            var done = new long[holes.Length];
            void Step()
            {
                session.Step();
                for (var index = 0; index < holes.Length; index++)
                {
                    if (done[index] == 0 && world.FindEntity(holes[index].Id) is null) done[index] = world.Tick;
                    if (world.FindEntity(holes[index].Id) is { } hole && hole.Modules.OfType<RebuildHoleBehavior>().Single().Reconstructed is { IsValid: true } id) sites[index] = id;
                    if (world.FindEntity(sites[index]) is not { } site) continue;
                    if (rises[index] == 0 && site.ConstructionProgress > 0) rises[index] = world.Tick;
                    if (completes[index] == 0 && !site.HasStatus(ObjectStatusBits.UnderConstruction)) completes[index] = world.Tick;
                }
            }
            while (holes.Any(hole => Site(hole) is null) && world.Tick < killedAt + rate.TicksPerSecond * 25) Step();
            var waited = (world.Tick - killedAt) * 1000 / rate.TicksPerSecond;
            Check.That(waited >= 19_900 && waited <= 20_200, $"{rate}: the workers come after WorkerRespawnDelay ({waited} ms).");
            for (var index = 0; index < holes.Length; index++)
            {
                var site = Site(holes[index]);
                var worker = Worker(holes[index]);
                Check.That(site is not null && site.Template == built[index].Template && site.Owner == player, $"{rate}: hole {index} raises {built[index].Template.Name} for its player.");
                Check.That(site!.HasStatus(ObjectStatusBits.UnderConstruction) && site.HasStatus(ObjectStatusBits.Reconstructing), $"{rate}: the site is reconstructing.");
                Check.That(site.X == holes[index].X && site.Y == holes[index].Y, $"{rate}: the site stands where the hole lies.");
                Check.That(worker is not null && worker.Template.Name == "GLAInfantryWorker" && worker.Owner == player && worker.HasStatus(ObjectStatusBits.Index("UNSELECTABLE")),
                    $"{rate}: hole {index}'s worker is an unselectable GLA worker of the player.");
                Check.That(holes[index].HasStatus(ObjectStatusBits.Index("MASKED")), $"{rate}: the hole is masked while its building rises.");
            }
            Check.That(world.MoneyOf(player) == money, $"{rate}: the rebuild is free of charge ({money} before, {world.MoneyOf(player)} now).");
            course.Add($"workers in frame {Frames(world.Tick - killedAt)}");

            // The Stinger Site's worker is killed half way: the hole sends another after the delay, which resumes the same site.
            var stingerSite = Site(holes[0])!;
            while (stingerSite.ConstructionShare < Fixed.FromInt(3) / Fixed.FromInt(10) && world.Tick < killedAt + rate.TicksPerSecond * 120) Step();
            while (world.Tick % rate.Time.TicksPerOriginalFrame != 0) Step();
            Check.That(stingerSite.HasStatus(ObjectStatusBits.UnderConstruction), $"{rate}: the Stinger Site is still rising when its worker dies.");
            var first = Worker(holes[0])!;
            world.Kill(first.Id);
            Step();
            var progress = stingerSite.ConstructionShare;
            // The hole sees its worker gone when the body is removed after its slow death (findObjectByID), and the wait begins then.
            while (world.FindEntity(first.Id) is not null && world.Tick < killedAt + rate.TicksPerSecond * 150) Step();
            var lostAt = world.Tick;
            Check.That(world.FindEntity(first.Id) is null, $"{rate}: the killed worker's body is gone.");
            Entity? second = null;
            while (world.Tick < lostAt + rate.TicksPerSecond * 25)
            {
                Step();
                second = world.Entities.FirstOrDefault(entity => entity.Template.Name == "GLAInfantryWorker" && entity.Owner == player && entity.Id != first.Id && stingerSite.BuilderId == entity.Id);
                if (second is not null) break;
            }
            var replaced = (world.Tick - lostAt) * 1000 / rate.TicksPerSecond;
            Check.That(second is not null && replaced >= 19_900 && replaced <= 20_200, $"{rate}: a new worker resumes the same site after the delay ({replaced} ms).");
            Check.That(stingerSite.ConstructionShare >= progress && world.FindEntity(stingerSite.Id) is not null, $"{rate}: the site waited with its progress ({progress}).");
            course.Add($"replaced in frame {Frames(world.Tick - lostAt)}");

            // The finished buildings take hole and worker away.
            var deadline = world.Tick + rate.TicksPerSecond * 180;
            while (world.Tick < deadline && done.Any(value => value == 0)) Step();
            for (var index = 0; index < holes.Length; index++)
            {
                Check.That(done[index] > 0, $"{rate}: hole {index} is gone once its building is complete.");
                var standing = world.Entities.Where(entity => entity.Template == built[index].Template && entity.Owner == player && !entity.EffectivelyDead).ToArray();
                Check.That(standing.Length == 1 && !standing[0].HasStatus(ObjectStatusBits.UnderConstruction) && !standing[0].HasStatus(ObjectStatusBits.Reconstructing),
                    $"{rate}: {built[index].Template.Name} stands complete again.");
                course.Add($"site {index} rises in frame {Frames(rises[index] - killedAt)}, complete in frame {Frames(completes[index] - killedAt)}, hole gone in frame {Frames(done[index] - killedAt)}");
            }
            for (var tick = 0; tick < rate.Time.OriginalFrames(2); tick++) session.Step();
            Check.That(!world.Entities.Any(entity => entity.Owner == player && entity.Template.Name == "GLAInfantryWorker" && entity.HasStatus(ObjectStatusBits.Index("UNSELECTABLE"))),
                $"{rate}: the holes' workers are gone.");
            Check.That(world.MoneyOf(player) == money, $"{rate}: the whole rebuild cost nothing.");
            var text = string.Join(", ", course);
            Console.WriteLine($"       {rate}: {text} (frames of the original after the kill)");
            course30 ??= text;
            Check.That(text == course30, $"{rate}: the same course in game time as at 30 Hz ({course30}).");
        }
    }
}
