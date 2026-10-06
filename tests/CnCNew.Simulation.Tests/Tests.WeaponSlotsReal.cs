using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Retail data: every <c>WeaponSet</c> parses strictly, the multi-weapon objects get slot state, and the Gattling Tank (a ground gun in the primary slot, an anti-air gun in the
    /// secondary slot) fires the right gun at a Ranger on the ground and at a Chinook in the air, where before the slot model it fired its ground gun at both.
    /// </summary>
    public static void RealWeaponSlots(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        var sets = catalog.Templates.SelectMany(template => template.WeaponSets).ToArray();
        var multi = catalog.Templates.Where(template => template.HasMultipleWeaponSlots).ToArray();
        var withSecondary = sets.Count(set => set.HasWeapon(WeaponSlot.Secondary));
        var withTertiary = sets.Count(set => set.HasWeapon(WeaponSlot.Tertiary));
        var neverAuto = sets.Sum(set => Enumerable.Range(0, WeaponSlot.Count).Count(slot => set.HasWeapon(slot) && set.AutoChoose[slot] == 0));
        var preferred = sets.Sum(set => Enumerable.Range(0, WeaponSlot.Count).Count(slot => !set.PreferredAgainst[slot].IsEmpty));
        var shared = sets.Count(set => set.ShareReloadTime);
        var lockShared = sets.Count(set => set.LockSharedAcrossSets);
        Console.WriteLine($"       weapon slots: {sets.Length} weapon sets on {catalog.Templates.Count(template => !template.WeaponSets.IsEmpty)} objects; {withSecondary} with a secondary and {withTertiary} with a tertiary weapon; {multi.Length} objects carry per-slot state ({multi.Count(template => template.IsInstantiable)} instantiable)");
        Console.WriteLine($"       weapon slots: {neverAuto} slots never auto-chosen, {preferred} PreferredAgainst entries, {shared} sets share their reload time, {lockShared} share a weapon lock across sets");
        Check.That(withSecondary > 200 && withTertiary > 50 && neverAuto > 50 && preferred > 50, "The retail data carries the multi-slot features the model reads.");

        Check.That(catalog.TryGet("ChinaTankGattling", out var gattling) && gattling.IsInstantiable && gattling.HasMultipleWeaponSlots, "The Gattling Tank is instantiable and multi-weapon.");
        Check.That(catalog.TryGet("AmericaInfantryRanger", out var ranger) && ranger.IsInstantiable, "The Ranger is instantiable.");
        Check.That(catalog.TryGet("AmericaVehicleChinook", out var chinook) && chinook.IsInstantiable, "The Chinook is instantiable.");
        var set = gattling!.DefaultWeaponSet!;
        Check.That(catalog.Weapons[set.Weapons[WeaponSlot.Primary]].AntiMask == WeaponInfo.AntiMaskGround
            && (catalog.Weapons[set.Weapons[WeaponSlot.Secondary]].AntiMask & WeaponInfo.AntiMaskAirborneVehicle) != 0, "The primary gun shoots the ground, the secondary one airborne vehicles.");

        ulong Run()
        {
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-slots", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            void Spawn(int player, string template, int x, int y, int z)
            {
                sequence[player] = sequence.GetValueOrDefault(player) + 1;
                Check.That(world.Submit(new SpawnCommand(new PlayerId(player), sequence[player], world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromInt(z), Fixed.Zero)).Accepted, $"{template} spawns.");
            }
            Spawn(1, "ChinaTankGattling", 1000, 1000, 0);
            Spawn(2, "AmericaInfantryRanger", 1050, 1000, 0);
            Spawn(2, "AmericaVehicleChinook", 1000, 1050, 100);
            Spawn(1, "GLAVehicleScudLauncher", 3000, 1000, 0);
            Spawn(2, "AmericaInfantryRanger", 3300, 1000, 0);
            Spawn(2, "AmericaTankCrusader", 3000, 1300, 0);
            world.Step();
            Entity Find(string name) => world.Entities.First(value => value.Template.Name == name);
            var tank = Find("ChinaTankGattling");
            var infantry = Find("AmericaInfantryRanger");
            var air = Find("AmericaVehicleChinook");
            Check.That(world.ChooseWeapon(tank, infantry, FireOrigin.Player) is { CanFire: true, Slot: WeaponSlot.Primary }, "Against infantry on the ground the Gattling Tank uses its ground gun.");
            Check.That(world.ChooseWeapon(tank, air, FireOrigin.Player) is { CanFire: true, Slot: WeaponSlot.Secondary }, "Against a Chinook in the air it uses the anti-air gun.");
            var fired = new List<string>();
            foreach (var victim in new[] { infantry, air, infantry, air })
            {
                // A standing attack repeats its fire order every tick; the tank's turret first turns toward the victim (docs/SIM-TURRET-AIM.md), the shot follows.
                var shots = fired.Count;
                for (var attempt = 0; attempt < world.Frames(40) && fired.Count == shots; attempt++)
                {
                    sequence[1]++;
                    Check.That(world.Submit(new FireWeaponCommand(tank.Owner, sequence[1], world.Tick, tank.Id, victim.Id)).Accepted, $"The Gattling Tank fires at {victim.Template.Name}.");
                    fired.AddRange(world.Step().Events.Where(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id).Select(value => value.Text));
                }
                for (var i = 0; i < world.Frames(20); i++) world.Step();
            }
            var names = $"{catalog.Weapons[set.Weapons[WeaponSlot.Primary]].Name}/{catalog.Weapons[set.Weapons[WeaponSlot.Secondary]].Name}";
            Check.That(fired.Count == 4 && fired[0] == fired[2] && fired[1] == fired[3] && fired[0] != fired[1], $"The tank alternates between its two guns by target kind ({string.Join(", ", fired)}; weapons {names}).");
            Check.That(tank.ClipOf(WeaponSlot.Primary) != -1 && tank.ClipOf(WeaponSlot.Secondary) != -1 && tank.ClipOf(WeaponSlot.Tertiary) == -1, "Both guns hold ammunition; there is no tertiary weapon.");
            // The Scud Launcher: explosive warhead in the primary slot, toxin in the secondary one (SECONDARY FROM_SCRIPT FROM_AI, PreferredAgainst = SECONDARY INFANTRY,
            // ShareWeaponReloadTime) and a LockWeaponCreate that pins the primary slot for good.
            var scud = Find("GLAVehicleScudLauncher");
            var scudInfantry = world.Entities.First(value => value.Template.Name == "AmericaInfantryRanger" && value.X == Fixed.FromInt(3300));
            var scudTank = world.Entities.First(value => value.Template.Name == "AmericaTankCrusader");
            Check.That(scud.WeaponLock == WeaponLockKind.Permanent && scud.CurrentWeaponSlot == WeaponSlot.Primary, "LockWeaponCreate pinned the Scud Launcher's primary slot permanently.");
            Check.That(world.ChooseWeapon(scud, scudInfantry, FireOrigin.Ai) is { CanFire: true, Slot: WeaponSlot.Primary }, "Pinned, it fires its explosive warhead even at infantry.");
            Check.That(world.ChooseWeapon(scud, scudInfantry, FireOrigin.Ai, WeaponSlot.Secondary) is { CanFire: true, Slot: WeaponSlot.Primary }, "...and a permanent lock beats an explicit slot.");
            world.ReleaseWeaponLock(scud, WeaponLockKind.Permanent);
            Check.That(world.ChooseWeapon(scud, scudInfantry, FireOrigin.Ai) is { CanFire: true, Slot: WeaponSlot.Secondary }, "Unpinned, the Scud Launcher prefers its toxin warhead against infantry.");
            Check.That(world.ChooseWeapon(scud, scudTank, FireOrigin.Ai) is { CanFire: true, Slot: WeaponSlot.Primary }, "...and its explosive warhead against a tank.");
            // The order is repeated every tick like a standing attack: the Scud first raises its launcher to the firing pitch (TurretAI), then the warhead leaves.
            var toxin = false;
            for (var attempt = 0; attempt < world.Frames(120) && !toxin; attempt++)
            {
                sequence[1]++;
                Check.That(world.Submit(new FireWeaponCommand(scud.Owner, sequence[1], world.Tick, scud.Id, scudInfantry.Id, WeaponSlot.Best, FireOrigin.Ai)).Accepted, "The toxin shot is ordered.");
                toxin = world.Step().Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == scud.Id && value.Text.Contains("Toxin", StringComparison.OrdinalIgnoreCase));
            }
            Check.That(toxin, "The Scud fired its toxin warhead.");
            Check.That(scud.ReadyTickOf(WeaponSlot.Primary) == scud.ReadyTickOf(WeaponSlot.Secondary) && scud.ReadyTickOf(WeaponSlot.Primary) > world.Tick - 1, "The shared reload time holds the explosive warhead back as well.");
            return world.StateHash;
        }

        Check.That(Run() == Run(), "The retail scenario is deterministic.");
    }
}
