using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// Hard particle budgets. <see cref="MaxParticles"/>/<see cref="MaxFieldParticles"/>/<see cref="ParticleScale"/> are
/// <c>GlobalData::m_maxParticleCount</c>/<c>m_maxFieldParticleCount</c>/<c>m_particleScale</c>; <see cref="MinPriority"/> is the
/// <c>GameLODManager</c> minimum particle priority (<c>GameLOD.ini MinParticlePriority</c>, the caller picks the detail level);
/// <see cref="MaxParticlesPerSystem"/> is the <c>W3DParticleSystemManager::MAX_POINTS_PER_GROUP</c> render cap (512) applied as a
/// simulation cap; <see cref="MaxSystems"/> is an own safety cap without a reference counterpart.
/// </summary>
public sealed record ParticleBudget(
    int MaxParticles = 2500,
    int MaxFieldParticles = 30,
    int MaxParticlesPerSystem = 512,
    int MaxSystems = 4096,
    ParticlePriority MinPriority = ParticlePriority.WeaponExplosion,
    float ParticleScale = 1f);

/// <summary>
/// The <c>GameData.ini</c> values the FX layer reads. Absent keys keep the <c>GlobalData::GlobalData</c> defaults; present keys that do
/// not parse are reported through <see cref="FxCatalog.Diagnostics"/>.
/// </summary>
public sealed record FxSettings(ParticleBudget Budget, ImmutableArray<float> ShakeIntensities, float MaxShakeIntensity, float MaxShakeRange)
{
    public static FxSettings Default { get; } = new(new ParticleBudget(MaxParticles: 0), [0.5f, 1f, 2.5f, 5f, 8f, 12f], 10f, 150f);

    public float ShakeIntensity(ViewShakeType type) => ShakeIntensities[(int)type];

    private static readonly string[] ShakeKeys =
        ["ShakeSubtleIntensity", "ShakeNormalIntensity", "ShakeStrongIntensity", "ShakeSevereIntensity", "ShakeCineExtremeIntensity", "ShakeCineInsaneIntensity"];

    internal static FxSettings FromGameData(GameDefinition? gameData, FxDiagnosticBag diagnostics)
    {
        var settings = Default;
        if (gameData is null) return settings;
        var read = new FxValueReader(diagnostics, "GameData");
        string? Text(string key) => gameData.Fields.LastOrDefault(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText;
        var budget = settings.Budget;
        if (Text("MaxParticleCount") is { } max) budget = budget with { MaxParticles = read.Int("MaxParticleCount", max, budget.MaxParticles) };
        if (Text("MaxFieldParticleCount") is { } field) budget = budget with { MaxFieldParticles = read.Int("MaxFieldParticleCount", field, budget.MaxFieldParticles) };
        if (Text("ParticleScale") is { } scale) budget = budget with { ParticleScale = read.Real("ParticleScale", scale, budget.ParticleScale) };
        var shakes = settings.ShakeIntensities.ToArray();
        for (var index = 0; index < ShakeKeys.Length; index++)
            if (Text(ShakeKeys[index]) is { } value) shakes[index] = read.Real(ShakeKeys[index], value, shakes[index]);
        var maxShake = Text("MaxShakeIntensity") is { } intensity ? read.Real("MaxShakeIntensity", intensity, settings.MaxShakeIntensity) : settings.MaxShakeIntensity;
        var range = Text("MaxShakeRange") is { } shakeRange ? read.Real("MaxShakeRange", shakeRange, settings.MaxShakeRange) : settings.MaxShakeRange;
        return new FxSettings(budget, [..shakes], maxShake, range);
    }
}
