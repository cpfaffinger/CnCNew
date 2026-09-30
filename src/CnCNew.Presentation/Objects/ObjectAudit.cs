using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Objects;

/// <summary>The deterministic result of <see cref="ObjectAudit.Run"/>: report lines in a fixed order and the number of unclassified findings.</summary>
public sealed record ObjectAuditReport(
    int Objects,
    int ObjectsWithDraw,
    int DrawModules,
    int States,
    int Transitions,
    int UnknownConditions,
    int UnknownDrawKeys,
    int RuleErrors,
    int ModelFiles,
    int ModelFilesResolved,
    int AnimationFiles,
    int AnimationFilesResolved,
    int SkeletonFiles,
    int SkeletonFilesResolved,
    ImmutableArray<string> Lines)
{
    /// <summary>Unknown condition names and unknown draw keys/modules that no allow-list entry covers; the audit fails when non-zero.</summary>
    public int Unclassified { get; init; }
}

/// <summary>
/// Audits the draw data of every object of a graph: draw module types, states, condition names, draw keys, and whether the model,
/// animation and skeleton files they name exist (through <c>fileExists</c>, a virtual-path probe such as <c>ContentFileSystem.Exists</c>).
/// </summary>
public static class ObjectAudit
{
    public static ObjectAuditReport Run(CompiledDefinitionGraph graph, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(fileExists);
        var catalog = ObjectVisualCatalog.Build(graph);
        var lines = new List<string>();
        var visuals = catalog.Visuals;
        var draws = visuals.SelectMany(visual => visual.Draws).ToArray();

        lines.Add($"objects {visuals.Length}");
        lines.Add($"objects-with-draw {visuals.Count(visual => !visual.Draws.IsEmpty)}");
        lines.Add($"objects-without-draw {visuals.Count(visual => visual.Draws.IsEmpty)}");
        lines.Add($"objects-with-model {visuals.Count(visual => visual.HasModel)}");
        lines.Add($"objects-with-multiple-draws {visuals.Count(visual => visual.Draws.Length > 1)}");
        lines.Add($"draw-modules {draws.Length}");
        foreach (var group in draws.GroupBy(draw => draw.ModuleType).OrderBy(group => group.Key, StringComparer.Ordinal))
            lines.Add($"draw-module {group.Key} {group.Count()}");
        var states = draws.Sum(draw => draw.States.Length);
        var transitions = draws.Sum(draw => draw.Transitions.Length);
        lines.Add($"states {states}");
        lines.Add($"states-default {draws.Sum(draw => draw.States.Count(state => state.IsDefault))}");
        lines.Add($"alias-alternatives {draws.Sum(draw => draw.States.Sum(state => Math.Max(0, state.Conditions.Length - 1)))}");
        lines.Add($"transitions {transitions}");
        lines.Add($"states-with-animations {draws.Sum(draw => draw.States.Count(state => !state.Animations.IsEmpty))}");
        foreach (var mode in Enum.GetValues<AnimationMode>())
            lines.Add($"animation-mode {mode} {draws.Sum(draw => draw.States.Concat(draw.Transitions).Count(state => state.Mode == mode))}");
        lines.Add($"ignore-condition-modules {draws.Count(draw => draw.IgnoreConditionStates.Any)}");
        lines.Add($"condition-upgrades {visuals.Sum(visual => visual.ConditionUpgrades.Length)}");

        var used = draws.SelectMany(draw => draw.States).SelectMany(state => state.Conditions).SelectMany(set => set.Flags())
            .Concat(visuals.SelectMany(visual => visual.ConditionUpgrades).Select(upgrade => upgrade.Flag))
            .Select(flag => flag.Id).Distinct().Count();
        lines.Add($"condition-flags-used {used} of {ModelConditions.Count}");

        // Diagnostics: counted per code, then the classified detail tables.
        var diagnostics = catalog.Diagnostics;
        foreach (var group in diagnostics.GroupBy(value => (value.Severity, value.Code)).OrderBy(group => group.Key.Severity, StringComparer.Ordinal).ThenBy(group => group.Key.Code, StringComparer.Ordinal))
            lines.Add($"diagnostic {group.Key.Severity} {group.Key.Code} {group.Count()}");
        var unknownConditions = diagnostics.Where(value => value.Code == "unknown-condition").ToArray();
        foreach (var group in unknownConditions.GroupBy(value => value.Key.ToUpperInvariant()).OrderBy(group => group.Key, StringComparer.Ordinal))
            lines.Add($"unknown-condition {group.Key} {group.Count()}");
        var unknownKeys = diagnostics.Where(value => value.Code is "unknown-draw-key" or "unknown-draw-module").ToArray();
        foreach (var group in unknownKeys.GroupBy(value => (value.ModuleType, value.Key)).OrderBy(group => group.Key.ModuleType, StringComparer.Ordinal).ThenBy(group => group.Key.Key, StringComparer.Ordinal))
            lines.Add($"unknown-draw-key {group.Key.ModuleType} {group.Key.Key} {group.Count()}{(DrawKeyCatalog.IsAllowListed(group.Key.ModuleType, group.Key.Key) ? " allow-listed" : string.Empty)}");
        var ruleErrors = diagnostics.Where(value => value.Severity == "error" && value.Code is not ("unknown-condition" or "unknown-draw-key" or "unknown-draw-module")).ToArray();
        foreach (var group in ruleErrors.GroupBy(value => (value.Code, value.Key)).OrderBy(group => group.Key.Code, StringComparer.Ordinal).ThenBy(group => group.Key.Key, StringComparer.Ordinal))
            lines.Add($"error {group.Key.Code} {group.Key.Key} {group.Count()}");
        foreach (var error in ruleErrors.OrderBy(value => value.Location.Source, StringComparer.Ordinal).ThenBy(value => value.Location.Line))
            lines.Add($"error-at {error.Code} {error.Key} {error.Location.Source}:{error.Location.Line}");
        foreach (var group in diagnostics.Where(value => value.Severity == "warning").GroupBy(value => (value.Code, value.ModuleType, value.Key))
                     .OrderBy(group => group.Key.Code, StringComparer.Ordinal).ThenBy(group => group.Key.ModuleType, StringComparer.Ordinal).ThenBy(group => group.Key.Key, StringComparer.Ordinal))
            lines.Add($"warning {group.Key.Code} {group.Key.ModuleType} {group.Key.Key} {group.Count()}");
        foreach (var warning in diagnostics.Where(value => value.Severity == "warning").OrderBy(value => value.Location.Source, StringComparer.Ordinal).ThenBy(value => value.Location.Line))
            lines.Add($"warning-at {warning.Code} {warning.Key} {warning.Location.Source}:{warning.Location.Line} {warning.Detail}");

        // Files: model, animation and skeleton (animation hierarchy) references under Art/W3D.
        var allStates = draws.SelectMany(draw => draw.States.Concat(draw.Transitions)).ToArray();
        var models = allStates.Select(state => state.Model).OfType<string>();
        var animations = allStates.SelectMany(state => state.Animations).Select(animation => animation.File).OfType<string>().Select(file => file[..^4]);
        var skeletons = allStates.SelectMany(state => state.Animations).Select(animation => animation.Hierarchy).OfType<string>();
        var (modelCount, modelResolved) = Files(lines, "model", models, fileExists);
        var (animationCount, animationResolved) = Files(lines, "animation", animations, fileExists);
        var (skeletonCount, skeletonResolved) = Files(lines, "skeleton", skeletons, fileExists);

        // Bones.
        var turretStates = allStates.Count(state => !state.Turret.IsEmpty || !state.AltTurret.IsEmpty);
        lines.Add($"states-with-turret {turretStates}");
        lines.Add($"objects-with-turret {visuals.Count(visual => visual.Draws.Any(draw => !draw.TurretBones.IsEmpty))}");
        lines.Add($"states-with-alt-turret {allStates.Count(state => !state.AltTurret.IsEmpty)}");
        foreach (var group in allStates.SelectMany(state => new[] { state.Turret.AngleBone, state.AltTurret.AngleBone }).OfType<string>()
                     .GroupBy(name => name, StringComparer.Ordinal).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(10))
            lines.Add($"turret-bone {group.Key} {group.Count()}");
        lines.Add($"turret-pitch-bones {allStates.SelectMany(state => new[] { state.Turret.PitchBone, state.AltTurret.PitchBone }).OfType<string>().Distinct(StringComparer.Ordinal).Count()}");
        string[] boneKinds = ["fire-fx", "recoil", "muzzle-flash", "launch", "hide-show"];
        for (var kind = 0; kind < boneKinds.Length; kind++)
        {
            var names = allStates.SelectMany(state => state.WeaponBones).Select(bones => kind switch
            {
                0 => bones.FireFxBone, 1 => bones.RecoilBone, 2 => bones.MuzzleFlash, 3 => bones.LaunchBone, _ => bones.HideShowBone,
            }).OfType<string>().ToArray();
            lines.Add($"weapon-bone {boneKinds[kind]} {names.Length} uses {names.Distinct(StringComparer.Ordinal).Count()} names");
        }
        var particleBones = allStates.SelectMany(state => state.ParticleSysBones).ToArray();
        lines.Add($"particle-sys-bones {particleBones.Length} uses {particleBones.Select(bone => bone.Bone).Distinct(StringComparer.Ordinal).Count()} bones {particleBones.Select(bone => bone.ParticleSystem).Distinct(StringComparer.OrdinalIgnoreCase).Count()} systems");
        var subObjects = allStates.SelectMany(state => state.SubObjects).ToArray();
        lines.Add($"sub-object-entries {subObjects.Length} hidden {subObjects.Count(entry => entry.Hide)}");
        lines.Add($"track-marks {draws.Count(draw => draw.TrackMarks is not null)}");

        var unclassified = unknownConditions.Length + unknownKeys.Count(value => !DrawKeyCatalog.IsAllowListed(value.ModuleType, value.Key));
        lines.Add($"unclassified {unclassified}");
        return new ObjectAuditReport(visuals.Length, visuals.Count(visual => !visual.Draws.IsEmpty), draws.Length, states, transitions,
            unknownConditions.Length, unknownKeys.Length, ruleErrors.Length, modelCount, modelResolved, animationCount, animationResolved,
            skeletonCount, skeletonResolved, [..lines]) { Unclassified = unclassified };
    }

    private static (int Count, int Resolved) Files(List<string> lines, string kind, IEnumerable<string> names, Func<string, bool> fileExists)
    {
        var distinct = names.Select(name => name.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missing = distinct.Where(name => !fileExists(DrawSpec.ModelPath(name))).ToArray();
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{kind}-files {distinct.Length} resolved {distinct.Length - missing.Length} missing {missing.Length}"));
        foreach (var name in missing) lines.Add($"missing-{kind} {name}");
        return (distinct.Length, distinct.Length - missing.Length);
    }
}
