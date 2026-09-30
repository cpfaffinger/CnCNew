using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Fx;

/// <summary>Result of <see cref="FxAudit.Run"/>; <see cref="Lines"/> is the deterministic report (no paths, no timings).</summary>
public sealed record FxAuditReport(ImmutableArray<string> Lines, int UnexplainedDiagnostics, int MissingTextures)
{
    public bool IsClean => UnexplainedDiagnostics == 0;
}

/// <summary>Statistics of executing every FXList and particle system once (<see cref="FxAudit.ExecuteAll"/>).</summary>
public sealed record FxExecutionSweep(int FxListsExecuted, int ParticleSystemsSpawned, long Frames, ParticleWorldStats Particles,
    int PeakParticles, ImmutableSortedDictionary<string, long> Counters, int SoundCues, ulong Checksum);

/// <summary>The FX inventory audit behind <c>PresentationTool fx-audit</c> and the real-data test.</summary>
public static class FxAudit
{
    /// <summary>Particle textures are resolved like the W3D texture loader and the importer: the name, then <c>.dds</c>, then <c>.tga</c>.</summary>
    public static IEnumerable<string> TextureCandidates(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        yield return "Art/Textures/" + name;
        yield return "Art/Textures/" + stem + ".dds";
        yield return "Art/Textures/" + stem + ".tga";
    }

    public static FxAuditReport Run(CompiledDefinitionGraph graph, FxCatalog catalog, Func<string, bool> contentExists)
    {
        var lines = new List<string>();
        var nuggets = catalog.FxLists.Values.SelectMany(list => list.Nuggets).ToArray();
        lines.Add($"FXLists: {catalog.FxLists.Count}; nuggets: {nuggets.Length}.");
        foreach (var kind in Enum.GetValues<FxNuggetKind>())
            lines.Add($"  nugget {kind}: {nuggets.Count(nugget => nugget.Kind == kind)}");
        lines.Add($"Particle systems: {catalog.ParticleSystems.Count}.");
        foreach (var group in catalog.ParticleSystems.Values.GroupBy(system => system.Kind).OrderBy(group => group.Key))
            lines.Add($"  type {group.Key}: {group.Count()}");
        foreach (var group in catalog.ParticleSystems.Values.GroupBy(system => system.Shader).OrderBy(group => group.Key))
            lines.Add($"  shader {group.Key}: {group.Count()}");
        foreach (var group in catalog.ParticleSystems.Values.GroupBy(system => system.Priority).OrderBy(group => group.Key))
            lines.Add($"  priority {group.Key}: {group.Count()}");
        lines.Add($"  with SlaveSystem: {catalog.ParticleSystems.Values.Count(system => FxCatalog.IsNamed(system.SlaveSystem))}; "
            + $"with PerParticleAttachedSystem: {catalog.ParticleSystems.Values.Count(system => FxCatalog.IsNamed(system.PerParticleAttachedSystem))}; "
            + $"smudge textures: {catalog.ParticleSystems.Values.Count(system => system.IsSmudgeTexture)}.");

        // Smudge systems (texture name starting with SMUD) go to the heat-haze renderer, which never loads the named texture.
        var textures = catalog.ParticleSystems.Values.Where(system => system.UsesTexture && system.ParticleName.Length > 0 && !system.IsSmudgeTexture)
            .Select(system => system.ParticleName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingTextures = new List<string>();
        var viaSubstitute = 0;
        foreach (var texture in textures)
        {
            var candidates = TextureCandidates(texture).ToArray();
            if (contentExists(candidates[0])) continue;
            if (candidates.Skip(1).Any(contentExists)) viaSubstitute++;
            else missingTextures.Add(texture);
        }
        lines.Add($"Particle textures: {textures.Length} distinct; {viaSubstitute} resolved through the .dds/.tga substitution; {missingTextures.Count} missing.");
        foreach (var texture in missingTextures)
            lines.Add($"  missing texture (explained: WW3D2 missingtexture.cpp placeholder, the particles still render): {texture} used by "
                + string.Join(", ", catalog.ParticleSystems.Values.Where(system => system.ParticleName.Equals(texture, StringComparison.OrdinalIgnoreCase)).Select(system => system.Name)));

        var weaponsWithFx = catalog.Weapons.Values.Count(weapon => weapon.FireFx.Any(name => name is not null) || weapon.ProjectileDetonationFx.Any(name => name is not null));
        lines.Add($"Weapons: {catalog.Weapons.Count}; with FireFX/ProjectileDetonationFX: {weaponsWithFx}; with FireSound: "
            + $"{catalog.Weapons.Values.Count(weapon => FxCatalog.IsAudible(weapon.FireSound))}.");
        lines.Add($"DamageFX tables: {catalog.DamageFx.Count}; objects with a default DamageFX: {catalog.Objects.Values.Count(obj => obj.DamageFx is not null)}.");
        int MissingFrom(string owner) => catalog.Diagnostics.Count(value => value.Code == FxDiagnosticCodes.MissingFxList && value.Subject.StartsWith(owner, StringComparison.Ordinal));
        lines.Add($"Missing FXLists referenced by weapons: {MissingFrom("Weapon ")}; by DamageFX: {MissingFrom("DamageFX ")}; by FXListAtBonePos: {MissingFrom("FXList ")}; "
            + $"missing particle systems: {catalog.Diagnostics.Count(value => value.Code == FxDiagnosticCodes.MissingParticleSystem)}.");

        var unresolved = graph.References.Where(edge => edge.Target.Kind == DefinitionKind.FxList && !edge.Resolved).ToArray();
        lines.Add($"Graph references to FXLists: {graph.References.Count(edge => edge.Target.Kind == DefinitionKind.FxList)}; unresolved: {unresolved.Length}.");
        foreach (var group in unresolved.GroupBy(edge => edge.Target.Name, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.Ordinal))
            lines.Add($"  unresolved FXList '{group.Key}': {group.Count()} reference(s)");

        lines.Add($"Diagnostics: {catalog.Diagnostics.Length}.");
        foreach (var group in catalog.Diagnostics.GroupBy(value => value.Code).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var explained = FxDiagnosticCodes.Explained.TryGetValue(group.Key, out var reason);
            lines.Add($"  {(explained ? "explained" : "UNEXPLAINED")} {group.Key}: {group.Count()}{(explained ? $" ({reason})" : string.Empty)}");
            foreach (var diagnostic in group) lines.Add($"    {diagnostic.Subject}: {diagnostic.Detail}");
        }
        var unexplained = catalog.UnexplainedDiagnostics.Length;
        lines.Add($"Unexplained diagnostics: {unexplained}.");
        return new FxAuditReport([..lines], unexplained, missingTextures.Count);
    }

    /// <summary>
    /// Executes every FXList in both forms (object at the origin, position with a secondary 100 units away) and spawns every particle
    /// system template once, stepping one frame after each, then lets everything run out for <paramref name="settleFrames"/> frames.
    /// </summary>
    public static FxExecutionSweep ExecuteAll(FxCatalog catalog, ulong seed = 1, int settleFrames = 300)
    {
        var executor = new FxExecutor(catalog, seed);
        var view = new SweepView();
        var peak = 0;
        var cues = 0;
        ulong checksum = 14695981039346656037UL;
        void Frame()
        {
            executor.Update(view);
            var frame = executor.Capture();
            peak = Math.Max(peak, frame.Particles.Particles.Length);
            foreach (var particle in frame.Particles.Particles)
            {
                checksum = Mix(checksum, BitConverter.SingleToUInt32Bits(particle.Position.X));
                checksum = Mix(checksum, BitConverter.SingleToUInt32Bits(particle.Position.Y));
                checksum = Mix(checksum, BitConverter.SingleToUInt32Bits(particle.Position.Z));
                checksum = Mix(checksum, BitConverter.SingleToUInt32Bits(particle.Size));
                checksum = Mix(checksum, BitConverter.SingleToUInt32Bits(particle.Color.W));
            }
            cues += executor.DrainSoundCues().Length;
        }
        var lists = 0;
        foreach (var name in catalog.FxLists.Keys)
        {
            executor.ExecuteFxListAtEntity(name, SweepView.Primary, SweepView.Secondary, view);
            executor.ExecuteFxListAtPosition(name, new FxInvocation(new Vector3(0f, 0f, 0f))
            {
                PrimarySpeed = 10f, Secondary = new Vector3(100f, 0f, 0f), OverrideRadius = 20f,
            }, view);
            lists++;
            Frame();
        }
        var spawned = 0;
        foreach (var template in catalog.ParticleSystems.Values)
        {
            if (executor.Particles.Spawn(template, new ParticleSpawn(new Vector3(0f, 0f, 10f))) != 0) spawned++;
            Frame();
        }
        for (var index = 0; index < settleFrames; index++) Frame();
        return new FxExecutionSweep(lists, spawned, executor.Frame, executor.Particles.Stats, peak, executor.Counters, cues, checksum);
    }

    private static ulong Mix(ulong hash, uint value)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            hash ^= (value >> shift) & 0xFF;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private sealed class SweepView : IFxWorldView
    {
        public static readonly EntityId Primary = new(1);
        public static readonly EntityId Secondary = new(2);

        public FxEntityPose? GetPose(EntityId entity)
            => entity == Primary ? new FxEntityPose(Vector3.Zero, 0.5f, "SweepPrimary", new PlayerId(1), 0, 15f)
                : entity == Secondary ? new FxEntityPose(new Vector3(100f, 0f, 0f), 0f, "SweepSecondary", new PlayerId(2), 3, 10f)
                : null;

        public Vector3? GetBonePosition(EntityId entity, string boneName)
            => entity == Primary && !boneName.EndsWith("03", StringComparison.Ordinal) ? new Vector3(boneName.Length, 0f, 5f) : null;

        public Vector3? GetWeaponMuzzle(EntityId entity, string weaponName) => null;
        public string? GetTemplateName(int templateIndex) => null;
        public float GroundHeight(float x, float y) => 0f;
        public bool IsShrouded(Vector3 position) => false;
    }
}
