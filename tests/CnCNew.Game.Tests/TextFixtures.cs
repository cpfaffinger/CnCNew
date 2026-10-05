using System.Text;
using CnCNew.Data.Compilation;
using CnCNew.Game.Data;
using CnCNew.Ui.Layout;

namespace CnCNew.Game.Tests;

/// <summary>
/// A small synthetic <c>Language.ini</c> and <c>InGameUI.ini</c> (values of the same kind as the retail files, written for the tests) compiled into an <see cref="InGameUiStyle"/>, so the
/// headless tests of the mission texts need no content directory. The retail files themselves are checked by the tests that run with <c>--content-dir</c>.
/// </summary>
internal static class TextFixtures
{
    public const string LanguageIni = "Language\n  MessageFont = Arial 10 Yes\n  MilitaryCaptionTitleFont = \"Courier New\" 12 Yes\n  MilitaryCaptionFont = \"Courier New\" 12 No\n"
        + "  MilitaryCaptionSpeed = 1\n  MilitaryCaptionDelayMS = 750\n  SuperweaponCountdownNormalFont = Arial 10 No\n  SuperweaponCountdownReadyFont = Arial 10 Yes\n"
        + "  NamedTimerCountdownNormalFont = Arial 10 No\n  NamedTimerCountdownReadyFont = Arial 11 Yes\n  DrawableCaptionFont = Arial 10 No\n  ResolutionFontAdjustment = 0.7\nEnd\n";

    public const string InGameUiIni = "InGameUI\n  MessageColor1 = R:255 G:255 B:255\n  MessageColor2 = R:180 G:180 B:255\n  MessagePosition = X:10 Y:22\n  MessageFont = Arial\n  MessagePointSize = 10\n  MessageBold = Yes\n"
        + "  MessageDelayMS = 75000\n  MilitaryCaptionColor = R:255 G:255 B:255 A:255\n  MilitaryCaptionPosition = X:10 Y:340\n  MilitaryCaptionTitleFont = Courier\n  MilitaryCaptionTitlePointSize = 12\n"
        + "  MilitaryCaptionTitleBold = Yes\n  MilitaryCaptionFont = Courier\n  MilitaryCaptionPointSize = 12\n  MilitaryCaptionBold = No\n"
        + "  SuperweaponCountdownPosition = X:0.90 Y:0.01\n  SuperweaponCountdownFlashDuration = 500\n  SuperweaponCountdownFlashColor = R:255 G:255 B:255\n"
        + "  NamedTimerCountdownPosition = X:0.90 Y:0.666\n  NamedTimerCountdownFlashDuration = 500\n  NamedTimerCountdownFlashColor = R:0 G:255 B:255\n"
        + "  NamedTimerCountdownNormalColor = R:255 G:255 B:0\n  NamedTimerCountdownReadyColor = R:255 G:0 B:255\nEnd\n";

    public static InGameUiStyle Style { get; } = Create(LanguageIni, InGameUiIni);

    public static InGameUiStyle Create(string languageIni, string inGameUiIni)
        => InGameUiStyle.FromGraph(DefinitionCompiler.Compile(new MemorySource(new Dictionary<string, string>
        {
            ["Data/English/Language.ini"] = languageIni,
            ["Data/INI/InGameUI.ini"] = inGameUiIni,
        })));

    /// <summary>The scale options of an unscaled screen: every font at its point size times the language's resolution adjustment for the screen width.</summary>
    public static UiScaleOptions Options { get; } = new();

    private sealed class MemorySource(IReadOnlyDictionary<string, string> files) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => files.Keys;
        public Stream OpenRead(string virtualPath) => new MemoryStream(Encoding.Latin1.GetBytes(files[virtualPath]));
        public bool Exists(string virtualPath) => files.ContainsKey(virtualPath);
    }
}
