using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Audio.Catalog;

/// <summary>A counted deviation found while typing the audio INI (unknown key, invalid value, reset redefinition, …).</summary>
public sealed record AudioCatalogDiagnostic(string Code, string Event, string Key, string Detail);

/// <summary>A sound name for which neither the localized nor the generic path exists.</summary>
public sealed record UnresolvedAudioFile(string Event, string Key, string Name, string ExpectedPath);

/// <summary>Deterministic audit of an <see cref="AudioCatalog"/> (all lists in canonical order).</summary>
public sealed record AudioCatalogReport(
    int SoundEffects,
    int MusicTracks,
    int DialogEvents,
    int Templates,
    int FileReferences,
    int ResolvedFiles,
    int LocalizedFiles,
    int DistinctFiles,
    int SilentEvents,
    ImmutableArray<UnresolvedAudioFile> UnresolvedFiles,
    ImmutableSortedDictionary<string, int> DiagnosticCounts,
    ImmutableArray<AudioCatalogDiagnostic> Diagnostics,
    int MiscAudioHooks,
    ImmutableArray<string> UnresolvedMiscAudioHooks)
{
    public IEnumerable<string> ToLines()
    {
        var culture = CultureInfo.InvariantCulture;
        yield return string.Create(culture, $"audio events: {SoundEffects} sound effects, {MusicTracks} music tracks, {DialogEvents} dialog events ({Templates} templates)");
        yield return string.Create(culture, $"file references: {FileReferences} ({ResolvedFiles} resolved, {LocalizedFiles} localized, {UnresolvedFiles.Length} unresolved), {DistinctFiles} distinct files");
        yield return string.Create(culture, $"silent events (no sound to play): {SilentEvents}");
        yield return string.Create(culture, $"misc audio hooks: {MiscAudioHooks} ({UnresolvedMiscAudioHooks.Length} unresolved)");
        foreach (var hook in UnresolvedMiscAudioHooks)
            yield return $"  unresolved misc audio hook {hook}";
        foreach (var (code, count) in DiagnosticCounts)
            yield return string.Create(culture, $"  diagnostic {code}: {count}");
        foreach (var file in UnresolvedFiles)
            yield return $"  unresolved {file.Event}.{file.Key} '{file.Name}' -> {file.ExpectedPath}";
    }
}
