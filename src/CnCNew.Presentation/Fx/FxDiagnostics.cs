using System.Collections.Immutable;

namespace CnCNew.Presentation.Fx;

/// <summary>One FX parse or reference finding. <see cref="Subject"/> names the definition (and nugget/key) it belongs to.</summary>
public sealed record FxDiagnostic(string Code, string Subject, string Detail)
{
    public bool IsExplained => FxDiagnosticCodes.Explained.ContainsKey(Code);
}

/// <summary>
/// The allow-list of diagnostic codes that are understood and handled exactly like the reference does. Every other code is an
/// unexplained finding: <c>PresentationTool fx-audit</c> exits non-zero and the real-data test fails until it is reviewed.
/// </summary>
public static class FxDiagnosticCodes
{
    public const string UnknownNugget = "unknown-nugget";
    public const string UnknownKey = "unknown-key";
    public const string InvalidValue = "invalid-value";
    public const string UnknownEnum = "unknown-enum";
    public const string UnsupportedDistribution = "unsupported-distribution";
    public const string IgnoredTokens = "ignored-tokens";

    public const string FxListRedefined = "fxlist-redefined";
    public const string ShaderNone = "particle-shader-none";
    public const string VelocityNone = "particle-velocity-none";
    public const string VolumeNone = "particle-volume-none";
    public const string DrawableParticles = "particle-drawable-type";
    public const string VelocityAlias = "particle-velocity-alias";
    public const string MissingParticleSystem = "missing-particle-system";
    public const string MissingFxList = "missing-fxlist";
    public const string MissingSlaveSystem = "missing-slave-system";
    public const string MissingAttachedSystem = "missing-attached-system";
    public const string MissingTracerTemplate = "missing-tracer-template";
    public const string MissingDamageFx = "missing-damagefx";
    public const string CaseMismatch = "case-mismatch";

    public static ImmutableSortedDictionary<string, string> Explained { get; } = new Dictionary<string, string>
    {
        [IgnoredTokens] = "INI::initFromINI moves to the next line after the field parser; tokens it did not read are ignored.",
        [FxListRedefined] ="FXListStore::parseFXListDefinition clears the list before parsing, so only the last block's nuggets survive.",
        [ShaderNone] = "Shader NONE is INVALID_SHADER; Particle::isInvisible returns true, every particle dies in its first update.",
        [VelocityNone] = "VelocityType NONE takes the default branch of ParticleSystem::computeParticleVelocity (zero velocity).",
        [VolumeNone] = "VolumeType NONE takes the default branch of ParticleSystem::computeParticlePosition (the system origin).",
        [DrawableParticles] = "Type DRAWABLE: W3DParticleSystemManager::doParticles skips drawable systems; instances keep ParticleKind.Drawable.",
        [VelocityAlias] = "Velocity keys of another VelocityType share the ParticleSystemInfo::m_emissionVelocity union; last write wins.",
        [MissingParticleSystem] = "ParticleSystemFXNugget::reallyDoFX does nothing when ParticleSystemManager::findTemplate fails (release build).",
        [MissingFxList] = "INI::parseFXList stores NULL for an unknown name in release builds; FXList::doFXPos/doFXObj on NULL is a no-op.",
        [MissingSlaveSystem] = "ParticleSystemTemplate::createSlaveSystem returns NULL for an unknown SlaveSystem; the master emits alone.",
        [MissingAttachedSystem] = "ParticleSystem::update skips PerParticleAttachedSystem when findTemplate fails.",
        [MissingTracerTemplate] = "TracerFXNugget::doFXPos returns when ThingFactory::newDrawable finds no tracer template.",
        [MissingDamageFx] = "INI::parseDamageFX stores NULL for an unknown DamageFX name; ActiveBody::doDamageFX then does nothing.",
        [CaseMismatch] = "Resolved ignoring case like every CnCNew.Data name; the reference NameKeyGenerator::nameToKey and the particle "
            + "template map compare case-sensitively, so the original would miss this reference (deliberate, documented deviation).",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

internal sealed class FxDiagnosticBag
{
    private readonly List<FxDiagnostic> _items = [];

    public void Add(string code, string subject, string detail) => _items.Add(new FxDiagnostic(code, subject, detail));

    public ImmutableArray<FxDiagnostic> ToSortedArray()
        => [.._items.OrderBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => value.Subject, StringComparer.Ordinal)
            .ThenBy(value => value.Detail, StringComparer.Ordinal)];
}
