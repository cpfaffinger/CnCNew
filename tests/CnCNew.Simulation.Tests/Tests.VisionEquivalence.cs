using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>A fixed-seed generator for the equivalence tests (SplitMix64): the same sequence on every runtime.</summary>
    private sealed class VisionDice(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            unchecked
            {
                var z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int Below(int bound) => (int)(Next() % (ulong)bound);
        public int Between(int low, int high) => low + Below(high - low + 1);
        public bool Chance(int percent) => Below(100) < percent;

        /// <summary>A coordinate with a fraction, between the two whole numbers.</summary>
        public Fixed Coordinate(int low, int high) => Fixed.FromRaw(low * Fixed.ScaleRaw + (long)(Next() % (ulong)((high - low) * Fixed.ScaleRaw)));
    }

    private static void VisionMapsAgree(VisionMap fast, VisionMap reference, string step)
    {
        for (var slot = 0; slot < fast.PlayerSlots; slot++)
            Check.That(fast.CellBytes(slot).SequenceEqual(reference.CellBytes(slot)), $"{step}: the cell bytes of slot {slot} differ from the reference computation.");
        var expected = reference.Hash();
        Check.That(fast.Hash() == expected, $"{step}: the full hash over the cells differs.");
        Check.That(fast.CurrentHash() == expected, $"{step}: the kept hash differs from the full pass.");
        Check.That(reference.CurrentHash() == expected, $"{step}: the reference mode's hash differs from the full pass.");
    }

    /// <summary>
    /// The reveal, the refresh start and the kept hash of <see cref="VisionMap"/> against the original computation over every cell
    /// (<see cref="VisionMap.ReferenceMode"/>) on random sequences: refreshes with circles on, across and beyond the map edge, the script
    /// operations, loads in the middle, several hash calls without a change, and switching the mode back and forth.
    /// </summary>
    public static void VisionMapMatchesTheReferenceComputation()
    {
        // 64x64 = 4096 bytes: whole hash words and a checkpoint exactly at the end; 67x53 = 3551: a rest of seven bytes; 3x5: smaller than two words;
        // 128x40 = 5120: rows of whole words; 1x9: one column.
        (int X, int Y, int Slots)[] shapes = [(64, 64, 3), (67, 53, 4), (3, 5, 2), (128, 40, 5), (1, 9, 2), (23, 111, 9)];
        for (var shape = 0; shape < shapes.Length; shape++)
        {
            var (cellsX, cellsY, slots) = shapes[shape];
            var dice = new VisionDice(0xC0FFEE00UL + (ulong)shape);
            var fast = new VisionMap(cellsX, cellsY, slots);
            var reference = new VisionMap(cellsX, cellsY, slots) { ReferenceMode = true };
            VisionMapsAgree(fast, reference, $"shape {shape}, new");
            var width = cellsX * TerrainMap.CellSize;
            var height = cellsY * TerrainMap.CellSize;
            // Lookers that stay where they are for a while, as the buildings of a base do: most refreshes repeat most circles.
            var lookers = new List<(int Slot, Fixed X, Fixed Y, Fixed Radius)>();
            // Even shapes: many lookers that seldom move (slots are patched around the few that do); odd shapes: few that move often (slots are redrawn).
            var most = shape % 2 == 0 ? 40 : 12;
            var moves = shape % 2 == 0 ? 6 : 25;
            long unchanged = 0, patched = 0, redrawn = 0;
            for (var step = 0; step < 700; step++)
            {
                var label = $"shape {shape}, step {step}";
                var roll = dice.Below(100);
                if (roll < 55)
                {
                    // A refresh: some lookers move, appear or vanish.
                    for (var index = lookers.Count - 1; index >= 0; index--)
                    {
                        if (dice.Chance(shape % 2 == 0 ? 2 : 5)) lookers.RemoveAt(index);
                        else if (dice.Chance(moves)) lookers[index] = lookers[index] with { X = lookers[index].X + dice.Coordinate(-12, 12), Y = lookers[index].Y + dice.Coordinate(-12, 12) };
                    }
                    while (lookers.Count < most && dice.Chance(shape % 2 == 0 ? 80 : 40))
                    {
                        var radius = dice.Below(10) switch
                        {
                            0 => Fixed.Zero,
                            1 => Fixed.FromInt(-5),
                            2 => Fixed.FromInt(width + height + 50),
                            3 => Fixed.FromRaw(1 + (long)(dice.Next() % (ulong)(3 * Fixed.ScaleRaw))),
                            _ => dice.Coordinate(1, Math.Max(12, width / 3)),
                        };
                        lookers.Add((dice.Below(slots), dice.Coordinate(-60, width + 60), dice.Coordinate(-60, height + 60), radius));
                    }
                    fast.BeginCollectedRefresh();
                    reference.BeginCollectedRefresh();
                    foreach (var looker in lookers)
                    {
                        fast.Reveal(looker.Slot, looker.X, looker.Y, looker.Radius);
                        reference.Reveal(looker.Slot, looker.X, looker.Y, looker.Radius);
                    }
                    fast.EndCollectedRefresh();
                    reference.EndCollectedRefresh();
                }
                else if (roll < 85)
                {
                    var slot = dice.Below(slots);
                    var x = dice.Coordinate(-40, width + 40);
                    var y = dice.Coordinate(-40, height + 40);
                    var radius = dice.Chance(10) ? Fixed.Zero : dice.Coordinate(1, Math.Max(12, width / 2));
                    var revealed = dice.Chance(60);
                    Action<VisionMap> change = dice.Below(9) switch
                    {
                        0 => map => map.SetPermanentRevealAll(slot, revealed),
                        1 => map => map.ExploreAll(slot),
                        2 => map => map.ShroudAll(slot),
                        3 or 4 => map => map.ExploreCircle(slot, x, y, radius),
                        5 or 6 => map => map.ShroudCircle(slot, x, y, radius),
                        _ => map => map.SetPermanentRevealCircle(slot, x, y, radius, revealed),
                    };
                    change(fast);
                    change(reference);
                }
                else if (roll < 91)
                {
                    // A load in the middle: into fresh maps (a restore) or over the running ones.
                    var fresh = dice.Chance(50);
                    var nextFast = fresh ? new VisionMap(cellsX, cellsY, slots) : fast;
                    var nextReference = fresh ? new VisionMap(cellsX, cellsY, slots) { ReferenceMode = true } : reference;
                    for (var slot = 0; slot < slots; slot++)
                    {
                        var source = dice.Chance(80) ? slot : dice.Below(slots);
                        var text = fast.Save(source);
                        Check.That(text == reference.Save(source), $"{label}: the saved slot differs.");
                        nextFast.Load(slot, text);
                        nextReference.Load(slot, text);
                    }
                    if (fresh)
                    {
                        unchanged += fast.Counters.SlotsUnchanged;
                        patched += fast.Counters.SlotsPatched;
                        redrawn += fast.Counters.SlotsRedrawn;
                    }
                    fast = nextFast;
                    reference = nextReference;
                }
                else if (roll < 93)
                {
                    // The plain form, cells written at once: readable after every call, and the next collected refresh has to draw everything again.
                    fast.BeginRefresh();
                    reference.BeginRefresh();
                    VisionMapsAgree(fast, reference, label + " (plain refresh start)");
                    foreach (var looker in lookers)
                    {
                        fast.Reveal(looker.Slot, looker.X, looker.Y, looker.Radius);
                        reference.Reveal(looker.Slot, looker.X, looker.Y, looker.Radius);
                        if (dice.Chance(30)) VisionMapsAgree(fast, reference, label + " (plain reveal)");
                    }
                }
                else if (roll < 95)
                {
                    var slot = dice.Below(slots);
                    var x = dice.Coordinate(-20, width + 20);
                    var y = dice.Coordinate(-20, height + 20);
                    var radius = dice.Coordinate(1, Math.Max(12, width / 3));
                    // A single circle outside any refresh.
                    fast.Reveal(slot, x, y, radius);
                    reference.Reveal(slot, x, y, radius);
                }
                else if (roll < 97)
                {
                    // The fast map computes one refresh the original way and returns to its own.
                    fast.ReferenceMode = true;
                    fast.BeginCollectedRefresh();
                    reference.BeginCollectedRefresh();
                    foreach (var looker in lookers)
                    {
                        fast.Reveal(looker.Slot, looker.X, looker.Y, looker.Radius);
                        reference.Reveal(looker.Slot, looker.X, looker.Y, looker.Radius);
                    }
                    fast.EndCollectedRefresh();
                    reference.EndCollectedRefresh();
                    if (dice.Chance(50)) VisionMapsAgree(fast, reference, label + " (reference mode)");
                    fast.ReferenceMode = false;
                }
                // Not after every step: several changes then meet one hash call, as script actions of one tick do.
                if (dice.Chance(70)) VisionMapsAgree(fast, reference, label);
            }
            VisionMapsAgree(fast, reference, $"shape {shape}, end");
            unchanged += fast.Counters.SlotsUnchanged;
            patched += fast.Counters.SlotsPatched;
            redrawn += fast.Counters.SlotsRedrawn;
            Check.That(unchanged > 20 && patched > 20 && redrawn > 20, $"shape {shape}: the run took all three ways of a refresh ({unchanged} slots left alone, {patched} patched, {redrawn} redrawn).");
        }
    }

    /// <summary>The kept hash does no work when nothing changed and resumes behind the unchanged start of the chain when something did.</summary>
    public static void VisionHashOnlyMixesFromTheFirstChangedByte()
    {
        var plain = new VisionMap(8, 6, 1);
        plain.Reveal(0, Fixed.FromInt(25), Fixed.FromInt(25), Fixed.FromInt(10));
        Check.That(plain.IsVisible(0, 2, 2) && !plain.IsVisible(0, 6, 4), "Outside a collected refresh a reveal writes its cells at once.");
        plain.BeginRefresh();
        Check.That(!plain.IsVisible(0, 2, 2) && plain.IsExplored(0, 2, 2), "The plain refresh start forgets at once.");
        Check.Throws<InvalidOperationException>(() => plain.EndCollectedRefresh(), "No collected vision refresh");
        plain.BeginCollectedRefresh();
        plain.Reveal(0, Fixed.FromInt(65), Fixed.FromInt(45), Fixed.FromInt(5));
        Check.That(!plain.IsVisible(0, 6, 4), "Inside a collected refresh the cells wait for its end.");
        Check.Throws<InvalidOperationException>(() => plain.BeginRefresh(), "in progress");
        Check.Throws<InvalidOperationException>(() => plain.BeginCollectedRefresh(), "already in progress");
        plain.EndCollectedRefresh();
        Check.That(plain.IsVisible(0, 6, 4) && plain.CurrentHash() == plain.Hash(), "The end of the collected refresh writes them.");

        var map = new VisionMap(64, 64, 3);
        var counters = map.Counters;
        var total = 3L * 64 * 64;
        Check.That(counters.BytesHashed == total, "A new map mixes every byte once.");
        Check.That(map.CurrentHash() == map.Hash() && counters.BytesHashed == total && counters.HashUnchanged == 1, "Without a change the kept value stands.");

        map.BeginCollectedRefresh();
        map.Reveal(2, Fixed.FromInt(320), Fixed.FromInt(325), Fixed.FromInt(40));
        map.EndCollectedRefresh();
        Check.That(map.CurrentHash() == map.Hash(), "A reveal in the last slot changes the hash to the full pass's value.");
        var afterFirst = counters.BytesHashed - total;
        Check.That(afterFirst > 0 && afterFirst <= 64 * 64 - 28 * 64 + 512, $"Only the last slot from the checkpoint before the first revealed row is mixed again ({afterFirst} bytes).");

        var before = counters.BytesHashed;
        var cleared = counters.CellsCleared;
        map.BeginCollectedRefresh();
        map.Reveal(2, Fixed.FromInt(320), Fixed.FromInt(325), Fixed.FromInt(40));
        map.EndCollectedRefresh();
        Check.That(map.CurrentHash() == map.Hash() && counters.BytesHashed == before, "A refresh that ends with the same bytes mixes nothing.");
        Check.That(counters.SlotsUnchanged == 2 + 3 && counters.CirclesSkipped == 1 && counters.CellsCleared == cleared, "A refresh with the same circles leaves every slot alone (the two empty slots were left alone the first time too).");

        // A script write in between: the same circles have to be drawn again, as the pass over every cell would.
        map.ShroudCircle(2, Fixed.FromInt(320), Fixed.FromInt(325), Fixed.FromInt(15));
        Check.That(!map.IsVisible(2, 32, 32) && !map.IsExplored(2, 32, 32), "The shroud action darkens the cell under the looker.");
        map.BeginCollectedRefresh();
        map.Reveal(2, Fixed.FromInt(320), Fixed.FromInt(325), Fixed.FromInt(40));
        map.EndCollectedRefresh();
        Check.That(map.IsVisible(2, 32, 32) && counters.SlotsUnchanged == 2 + 3 + 2 && counters.CellsCleared > cleared, "After a script write the slot is drawn again; the two others stay alone.");
        Check.That(map.CurrentHash() == map.Hash(), "The redrawn slot hashes to the full pass's value.");
        before = counters.BytesHashed;

        map.ExploreCircle(1, Fixed.FromInt(5), Fixed.FromInt(635), Fixed.FromInt(3));
        Check.That(map.CurrentHash() == map.Hash(), "A script change in the middle slot is hashed.");
        var afterSecond = counters.BytesHashed - before;
        Check.That(afterSecond > 64 * 64 && afterSecond <= 64 * 64 + 2 * 512, $"The chain resumes in the last rows of slot 1 and runs through slot 2 ({afterSecond} bytes).");

        before = counters.BytesHashed;
        map.ShroudAll(0);
        Check.That(map.CurrentHash() == map.Hash() && counters.BytesHashed == before, "Shrouding a slot that is already dark changes no byte.");
    }

    private static IReadOnlyDictionary<string, string> VisionEquivalenceFiles()
    {
        var files = new Dictionary<string, string>(NavigationFiles());
        foreach (var (path, text) in VisionFiles()) files[path] = text;
        files["Data/INI/Object/VisionRover.ini"] = """
        Object Rover
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL GroundLoco
          VisionRange = 45.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;
        return files;
    }

    private static readonly ImmutableArray<PlayerConfiguration> VisionThreePlayers = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta"), new(new PlayerId(3), "Gamma")];

    private static void VisionWorldsAgree(SimulationWorld fast, SimulationWorld reference, string step)
    {
        Check.That(fast.StateHash == reference.StateHash, $"{step}: the state hashes differ.");
        VisionMapsAgree(fast.Vision!, reference.Vision!, step);
        foreach (var player in VisionThreePlayers)
            Check.That(fast.EntitiesVisibleTo(player.Id).Select(entity => entity.Id).SequenceEqual(reference.EntitiesVisibleTo(player.Id).Select(entity => entity.Id)),
                $"{step}: player {player.Id} sees different entities.");
    }

    /// <summary>
    /// Two worlds with the same orders, one computing its vision the original way: units that move, die and appear, construction sites, a
    /// reveal-to-all object, alliances that change, every map reveal and shroud action, and a snapshot restore in the middle. State hash, cell
    /// bytes, vision hash and the visible-entity views agree in every tick.
    /// </summary>
    public static void VisionWorldMatchesTheReferenceComputation()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: VisionEquivalenceFiles());
        const int cellsX = 61, cellsY = 47;
        var terrain = TerrainMap.Build(SyntheticMaps.Build(cellsX, cellsY, 1, (_, _) => 0, null, null), "vision-equivalence");
        SimulationWorld Create() => new(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, VisionThreePlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var fast = Create();
        var reference = Create();
        reference.Vision!.ReferenceMode = true;
        Check.That(fast.Vision!.CellsX >= cellsX && fast.Vision.CellsX * fast.Vision.CellsY > 2048, "The map is large enough for several hash checkpoints per slot.");
        var width = fast.Vision.CellsX * TerrainMap.CellSize;
        var height = fast.Vision.CellsY * TerrainMap.CellSize;
        var fastFeeder = new Feeder(fast);
        var referenceFeeder = new Feeder(reference);
        var dice = new VisionDice(0x5EED5EEDUL);
        string[] templates = ["Rover", "Rover", "Rover", "Scout", "Watcher", "Tower", "Beacon", "Blind", "Target"];
        Relationship[] relations = [Relationship.Allies, Relationship.Enemies, Relationship.Neutral];
        var underConstruction = 1UL << ObjectStatusBits.UnderConstruction;
        var changedRefreshes = 0;
        for (var tick = 0; tick < 1500; tick++)
        {
            var label = $"tick {tick}";
            void Both(Action<SimulationWorld, Feeder> action)
            {
                action(fast, fastFeeder);
                action(reference, referenceFeeder);
            }
            if (fast.Entities.Count < 40 && dice.Chance(tick < 60 ? 60 : 12))
            {
                var player = dice.Between(1, 3);
                var template = templates[dice.Below(templates.Length)];
                var x = dice.Between(5, width - 5);
                var y = dice.Between(5, height - 5);
                Both((_, feeder) => feeder.Spawn(player, template, x: x, y: y));
            }
            var ids = fast.Entities.Select(entity => entity.Id.Value).Order().ToArray();
            Check.That(ids.SequenceEqual(reference.Entities.Select(entity => entity.Id.Value).Order()), $"{label}: the worlds hold different entities.");
            if (ids.Length > 0)
            {
                if (dice.Chance(30))
                {
                    var id = ids[dice.Below(ids.Length)];
                    var owner = fast.Entities.Single(entity => entity.Id.Value == id).Owner.Value;
                    var x = dice.Between(5, width - 5);
                    var y = dice.Between(5, height - 5);
                    Both((_, feeder) => feeder.SetMoveTarget(owner, id, x, y));
                }
                if (dice.Chance(3))
                {
                    var id = ids[dice.Below(ids.Length)];
                    Both((_, feeder) => feeder.Kill(id));
                }
                if (dice.Chance(3))
                {
                    var id = new EntityId(ids[dice.Below(ids.Length)]);
                    var set = dice.Chance(50);
                    Both((world, _) => world.SetStatus(id, set ? underConstruction : 0, set ? 0 : underConstruction));
                }
            }
            if (dice.Chance(3))
            {
                var from = dice.Between(1, 3);
                var to = dice.Between(1, 3);
                var relation = relations[dice.Below(relations.Length)];
                Both((_, feeder) => feeder.SetRelationship(from, to, relation));
                if (dice.Chance(70)) Both((_, feeder) => feeder.SetRelationship(to, from, relation));
            }
            if (dice.Chance(4))
            {
                var player = new PlayerId(dice.Between(0, 3));
                var x = dice.Coordinate(-20, width + 20);
                var y = dice.Coordinate(-20, height + 20);
                var radius = dice.Coordinate(1, 150);
                var revealed = dice.Chance(60);
                Action<SimulationWorld> change = dice.Below(8) switch
                {
                    0 => world => world.RevealMapPermanently(player, revealed),
                    1 => world => world.ExploreMap(player),
                    2 => world => world.ShroudMap(player),
                    3 => world => world.ExploreArea(player, x, y, radius),
                    4 or 5 => world => world.ShroudArea(player, x, y, radius),
                    _ => world => world.RevealAreaPermanently(player, x, y, radius, revealed),
                };
                Both((world, _) => change(world));
                // Queried between two refreshes, as a script condition of the same tick would.
                VisionWorldsAgree(fast, reference, label + " after a script action");
            }
            if (tick is 400 or 401 or 900)
            {
                // A restore in the middle, also one tick after a refresh; the reference world is restored from the fast world's bytes.
                var bytes = WorldSnapshot.Write(fast);
                Check.That(bytes.AsSpan().SequenceEqual(WorldSnapshot.Write(reference)), $"{label}: the snapshots differ.");
                fast = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain);
                reference = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain);
                reference.Vision!.ReferenceMode = true;
                fastFeeder = fastFeeder.ForkTo(fast);
                referenceFeeder = referenceFeeder.ForkTo(reference);
                VisionWorldsAgree(fast, reference, label + " after the restore");
            }
            var hashedBefore = fast.Vision!.Counters.BytesHashed;
            Check.That(fast.Step().StateHash == reference.Step().StateHash, $"{label}: the tick results differ.");
            VisionWorldsAgree(fast, reference, label);
            if (fast.Vision.Counters.BytesHashed != hashedBefore) changedRefreshes++;
        }
        Check.That(changedRefreshes > 100, $"The run changed the vision often enough to mean something ({changedRefreshes} ticks with a changed vision hash).");
        Check.That(fast.Vision!.Counters.CellsCleared < reference.Vision!.Counters.CellsCleared, "The refresh clears fewer cells than the reference pass over the whole map.");
        Check.That(fast.Vision.Counters.BytesHashed < reference.Vision.Counters.BytesHashed, "The kept hash mixes fewer bytes than a full pass on every call.");
    }
}
