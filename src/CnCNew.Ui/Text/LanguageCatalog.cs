using System.Collections.Immutable;

namespace CnCNew.Ui.Text;

/// <param name="StringFile">The string table the game would load, or <c>null</c> when none exists.</param>
/// <param name="LanguageIni">The language's <c>Language.ini</c>, or <c>null</c> when it does not exist.</param>
/// <param name="Candidates">Every path that was probed, in probe order.</param>
public sealed record LanguageFiles(
    string Language,
    string? StringFile,
    StringTableFormat? StringFormat,
    string? LanguageIni,
    ImmutableArray<string> Candidates)
{
    public bool HasStringTable => StringFile is not null;
}

/// <summary>
/// Maps a language name to its files without touching a file system; existence comes from the caller.
/// Paths follow <c>WinMain.cpp</c> (<c>g_strFile = "data\Generals.str"</c>, <c>g_csfFile = "data\%s\Generals.csf"</c>),
/// <c>GameTextManager::init</c> (the textual file is tried first because release builds set <c>m_useStringFile</c>)
/// and <c>GlobalLanguage::init</c> (<c>Data\%s\Language</c>). Lookups in the original are case-insensitive, so the
/// delegate must be as well.
/// </summary>
public static class LanguageCatalog
{
    public const string StrFile = "Data/Generals.str";

    /// <summary>The <c>Data/&lt;Language&gt;</c> folders of a Zero Hour installation, ordinal order.</summary>
    public static ImmutableArray<string> ShippedLanguages { get; } =
        ["Brazilian", "Chinese", "English", "French", "German", "Italian", "Korean", "Polish", "Spanish"];

    public static string CsfFile(string language) => $"Data/{Validate(language)}/Generals.csf";
    public static string LanguageIniFile(string language) => $"Data/{Validate(language)}/Language.ini";

    public static LanguageFiles Resolve(string language, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var csf = CsfFile(language);
        var ini = LanguageIniFile(language);
        string? stringFile = null;
        StringTableFormat? format = null;
        if (exists(StrFile)) { stringFile = StrFile; format = StringTableFormat.Str; }
        else if (exists(csf)) { stringFile = csf; format = StringTableFormat.Csf; }
        return new LanguageFiles(language, stringFile, format, exists(ini) ? ini : null, [StrFile, csf, ini]);
    }

    /// <summary>Resolves every given language; the result is in ordinal order of the language names.</summary>
    public static ImmutableArray<LanguageFiles> Discover(IEnumerable<string> languages, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(languages);
        return [..languages.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => Resolve(name, exists))];
    }

    private static string Validate(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (!language.All(char.IsAsciiLetter))
            throw new ArgumentException($"Language name '{language}' must consist of ASCII letters only.", nameof(language));
        return language;
    }
}
