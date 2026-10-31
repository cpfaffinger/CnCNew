using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Definitions;

namespace CnCNew.Game.Movies;

/// <summary>
/// <c>Data/INI/Video.ini</c>: an internal name (<c>EALogoMovie</c>) and the name of its file. A movie is found as <c>BinkVideoPlayer::open</c> finds it: first under the language's
/// folder, <c>Data/&lt;language&gt;/Movies</c>, then under <c>Data/Movies</c>.
/// </summary>
public sealed class MovieCatalog
{
    private const string Extension = ".bik";
    private readonly ImmutableSortedDictionary<string, string> _files;

    public MovieCatalog(ImmutableSortedDictionary<string, string> files) => _files = files.WithComparers(StringComparer.OrdinalIgnoreCase);

    public int Count => _files.Count;

    public static MovieCatalog FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var files = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<CompatibilityDefinition>().Where(value => value.Id.Name.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
        {
            var field = definition.Fields.LastOrDefault(value => value.Name.Equals("Filename", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Video {definition.OriginalName} names no file.");
            files[definition.OriginalName] = field.Value.OriginalText.Trim();
        }
        return new MovieCatalog(files.ToImmutable());
    }

    /// <summary>The file of a movie, without folder and extension; <see langword="null"/> when <c>Video.ini</c> does not know it.</summary>
    public string? FileOf(string name) => _files.GetValueOrDefault(name);

    /// <summary>The virtual path of a movie, or <see langword="null"/> when <c>Video.ini</c> does not know it or the file is not there.</summary>
    public string? Find(string name, ContentFileSystem content, string language)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (FileOf(name) is not { } file) return null;
        var localized = $"Data/{language}/Movies/{file}{Extension}";
        if (content.Exists(localized)) return localized;
        var general = $"Data/Movies/{file}{Extension}";
        return content.Exists(general) ? general : null;
    }
}
