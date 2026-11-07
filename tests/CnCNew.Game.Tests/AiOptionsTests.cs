using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Tests;

/// <summary>
/// The AI options of the skirmish setup (the owner's request of 7 November 2026, docs/GAME-SHELL.md "AI options"): per computer slot the engine (New or Classic), the tactics and
/// the strength, as a screen of this project beside the "Debug options" button, kept in step with the slot's own box; what is chosen reaches the match setup, its description and the
/// session that plays it. The screen's pictures are pinned with the shell walk of <see cref="ShellTests"/> (<c>SkirmishAiOptions</c>) and the room's in <see cref="NetScreenTests"/>.
/// </summary>
internal static class AiOptionsTests
{
    private const int NavWidth = 1280, NavHeight = 720;
    private const string EightPlayers = "Maps/Twilight Flame/Twilight Flame.map";
    private const string Setup = "SkirmishGameOptionsMenu.wnd:";

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var harness = new TestHarness(nameof(AiOptionsTests));
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP AiOptionsTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The notes under the table say what applies to the rows", Notes);
        harness.Run("The AI options screen lists the computer slots, and its boxes and the slot's own box show the same values", () => TwoViewsOfOneValue(data));
        harness.Run("What the AI options choose is the match setup and the match description the slot's own box gives; a setup left alone is the default match", () => SameAsTheOriginalBox(data));
        harness.Run("A New slot is the built-in session, a Classic slot the scripted one, a mix is the scripted one with the built-in AI in the New slots", () => Sessions(data));
        harness.Run("The strength and tactics of a New slot reach the built-in AI inside the mixed session", () => MixedSessionPlaysTheProfile(data));
        harness.Run("On a map with scripts only Classic is offered, in a room only New, a member only looks; the debug options refuse Classic", () => EnginesAndRefusals(data));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------- helpers

    private static GameShell SetupShell(GameData data, string? map = null, CnCNew.Simulation.Skirmish.Original.SkirmishAiKind? ai = null)
    {
        var shell = GameShell.Create(data, new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-ai-options-tests-" + Guid.NewGuid().ToString("N"))), viewport: new UiViewport(NavWidth, NavHeight),
            skin: SkinPins.Current);
        if (ai is { } kind) shell.SkirmishAi = kind;
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
        if (map is not null) ((SkirmishSetupScreen)shell.CurrentScreen).SetSelectedMap(map);
        return shell;
    }

    private static void Post(GameShell shell, string window, int value)
    {
        var tree = shell.CurrentScreen.Tree;
        tree.Post(UiEventKind.ComboSelected, tree.FindByName(window) ?? throw new InvalidOperationException($"No window {window} on {shell.CurrentScreen.GetType().Name}."), value: value);
        tree.Dispatch();
    }

    private static int Selected(GameShell shell, string window) => shell.CurrentScreen.Tree.FindByName(window)!.ComboBox!.SelectedIndex;
    private static string[] Entries(GameShell shell, string window) => [.. shell.CurrentScreen.Tree.FindByName(window)!.ComboBox!.Entries];
    private static bool Visible(GameShell shell, string window) => shell.CurrentScreen.Tree.FindByName(window)!.IsVisible;
    private static bool Enabled(GameShell shell, string window) => shell.CurrentScreen.Tree.FindByName(window)!.IsEffectivelyEnabled;

    private static string Slot(int slot) => $"{Setup}ComboBoxPlayer{slot}";

    private static void OpenAi(GameShell shell)
    {
        ShellTestInput.Click(shell, AiOptionsScreen.Button);
        TestHarness.Assert(shell.CurrentScreen is AiOptionsScreen, "the button opens the AI options");
    }

    private static MatchSetup Start(GameShell shell)
    {
        ShellTestInput.Click(shell, Setup + "ButtonStart");
        shell.BuildDrawList();
        shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
        return shell.DrainOutputs().OfType<StartMatchOutput>().Single().Setup;
    }

    private static LiveMatch Create(GameData data, MatchSetup setup, long ticks = 120)
        => LiveMatch.Create(data, setup with { Options = setup.Options with { MaxTicks = ticks } }, new Vector2(NavWidth, NavHeight));

    // ------------------------------------------------------------------------------------------------- the tests

    private static void Notes()
    {
        static string[] Lines(SkirmishAiKind[] kinds, AiEngineMode mode = AiEngineMode.Free, bool readOnly = false, bool debug = false) => [.. AiOptionsScreen.Notes(kinds, mode, readOnly, debug)];
        var any = Lines([SkirmishAiKind.Builtin, SkirmishAiKind.Builtin]);
        TestHarness.Assert(any.Length == 1 && any[0].StartsWith("New:", StringComparison.Ordinal), $"two New slots: only the explanation ({string.Join(" | ", any)})");
        var classic = Lines([SkirmishAiKind.Original]);
        TestHarness.Assert(classic.Length == 3 && classic[1].Contains("greyed", StringComparison.Ordinal) && classic[2].Contains("no replay", StringComparison.Ordinal), $"a Classic slot: why tactics are greyed, no replay ({string.Join(" | ", classic)})");
        var mixed = Lines([SkirmishAiKind.Original, SkirmishAiKind.Builtin]);
        TestHarness.Assert(mixed[^1].Contains("no saving", StringComparison.Ordinal), $"both AIs: the match cannot be saved ({string.Join(" | ", mixed)})");
        var debug = Lines([SkirmishAiKind.Original], debug: true);
        TestHarness.Assert(debug[^1].Contains("Debug options", StringComparison.Ordinal), $"debug options with Classic: said ({string.Join(" | ", debug)})");
        TestHarness.Assert(Lines([SkirmishAiKind.Original], AiEngineMode.ClassicOnly)[1].Contains("scripts", StringComparison.Ordinal), "a map with scripts says so");
        TestHarness.Assert(Lines([SkirmishAiKind.Builtin], AiEngineMode.NewOnly, readOnly: true).Length == 3, "a room: the server's words and the host's");
        TestHarness.Assert(Lines([]).Length == 1 && Lines([])[0].Contains("No computer", StringComparison.Ordinal), "without a computer slot the screen says there is none");
    }

    private static void TwoViewsOfOneValue(GameData data)
    {
        var shell = SetupShell(data, EightPlayers);
        var button = shell.CurrentScreen.Tree.FindByName(AiOptionsScreen.Button) ?? throw new InvalidOperationException("The setup has no AI options button.");
        var debug = shell.CurrentScreen.Tree.FindByName(DebugOptionsScreen.Button)!;
        var start = shell.CurrentScreen.Tree.FindByName(Setup + "ButtonStart")!;
        var back = shell.CurrentScreen.Tree.FindByName(Setup + "ButtonBack")!;
        TestHarness.Assert(button.IsVisible && button.IsEffectivelyEnabled && button.Text!.Value == "AI options" && button.Rect.X > start.Rect.Right && debug.Rect.X > button.Rect.Right && debug.Rect.Right < back.Rect.X
                           && button.Rect.Y == start.Rect.Y, "the AI options button stands beside the debug options between Start and Back, in no one's way");

        // Slots 1 to 3 are computer armies (rush, hard, medium), slot 4 is closed, the rest open.
        Post(shell, Slot(1), 5);
        Post(shell, Slot(2), 4);
        Post(shell, Slot(3), 3);
        Post(shell, Slot(4), 1);
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen setup && setup.AiOf(1) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Normal, AiTactics.Rush) && setup.AiOf(2) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Hard)
                           && setup.AiOf(3) == MatchAi.Builtin, "the slots' own boxes set the AI as they always did");
        var setupScreen = (SkirmishSetupScreen)shell.CurrentScreen;

        OpenAi(shell);
        TestHarness.Assert(Visible(shell, "AiOptions.wnd:Label0") && Visible(shell, "AiOptions.wnd:Label2") && !Visible(shell, "AiOptions.wnd:Label3"), "three computer slots, three rows (the closed one is none)");
        TestHarness.Assert(Entries(shell, "AiOptions.wnd:ComboEngine0").SequenceEqual(["New", "Classic"]) && Entries(shell, "AiOptions.wnd:ComboTactics0").SequenceEqual(["Balanced", "Rush", "Turtle", "Eco"])
                           && Entries(shell, "AiOptions.wnd:ComboStrength0").SequenceEqual(["Easy", "Medium", "Hard"]), "the three boxes offer what the request names");
        int Row(string box, int row) => Selected(shell, $"AiOptions.wnd:Combo{box}{row}");
        TestHarness.Assert(Row("Engine", 0) == 0 && Row("Tactics", 0) == 1 && Row("Strength", 0) == 1 && Row("Engine", 1) == 0 && Row("Tactics", 1) == 0 && Row("Strength", 1) == 2 && Row("Tactics", 2) == 0 && Row("Strength", 2) == 1,
            "the screen shows what the boxes of the setup set: rush at medium, balanced at hard, balanced at medium - all New");
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("AiOptions.wnd:Label0")!.Text!.Value.StartsWith("Player 2", StringComparison.Ordinal), "a row says which slot it is");

        // Row 2 becomes Classic: its tactics grey out, the notes say why; row 1 gets eco at easy.
        Post(shell, "AiOptions.wnd:ComboEngine2", 1);
        TestHarness.Assert(!Enabled(shell, "AiOptions.wnd:ComboTactics2") && Enabled(shell, "AiOptions.wnd:ComboTactics1") && Enabled(shell, "AiOptions.wnd:ComboEngine2"), "Classic greys the tactics of its row only");
        var notes = Enumerable.Range(0, 3).Select(note => shell.CurrentScreen.Tree.FindByName($"AiOptions.wnd:Note{note}")!.Text!.Value).ToArray();
        TestHarness.Assert(notes.Any(text => text.Contains("greyed", StringComparison.Ordinal)) && notes.Any(text => text.Contains("no saving", StringComparison.Ordinal)), $"the lines say why, and that a mix cannot be saved ({string.Join(" | ", notes)})");
        Post(shell, "AiOptions.wnd:ComboTactics1", 3);
        Post(shell, "AiOptions.wnd:ComboStrength1", 0);
        TestHarness.Assert(setupScreen.AiOf(1) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Normal, AiTactics.Rush) && setupScreen.AiOf(2) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Hard), "nothing reaches the setup before Accept");
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonAccept");
        TestHarness.Assert(shell.CurrentScreen == setupScreen && setupScreen.AiOf(2) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Easy, AiTactics.Eco) && setupScreen.AiOf(3).Kind == SkirmishAiKind.Original
                           && setupScreen.AiOf(3).Difficulty == ScriptDifficulty.Normal, $"Accept gives the setup what was chosen ({setupScreen.AiOf(2)}, {setupScreen.AiOf(3)})");
        // The slots' boxes follow: rush at medium keeps its entry, eco at easy shows the army of its strength, a Classic medium slot is the Medium Army.
        TestHarness.Assert(Selected(shell, Slot(1)) == 5 && Selected(shell, Slot(2)) == 2 && Selected(shell, Slot(3)) == 3, $"the slot boxes show it ({Selected(shell, Slot(1))}, {Selected(shell, Slot(2))}, {Selected(shell, Slot(3))})");

        // Back drops what was chosen.
        OpenAi(shell);
        Post(shell, "AiOptions.wnd:ComboEngine0", 1);
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonBack");
        TestHarness.Assert(setupScreen.AiOf(1).Kind == SkirmishAiKind.Builtin && Selected(shell, Slot(1)) == 5, "Back leaves the setup as it was");

        // The slot's box is a shortcut for the same values: a Classic slot shows its strength; Hard Army keeps the engine; Rush Army is the new AI with that profile.
        Post(shell, Slot(3), 4);
        TestHarness.Assert(setupScreen.AiOf(3) == new MatchAi(SkirmishAiKind.Original, ScriptDifficulty.Hard), "Hard Army on a Classic slot stays Classic");
        OpenAi(shell);
        TestHarness.Assert(Row("Engine", 2) == 1 && Row("Strength", 2) == 2, "and the screen shows Classic at hard");
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonBack");
        Post(shell, Slot(3), 6);
        TestHarness.Assert(setupScreen.AiOf(3) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Normal, AiTactics.Turtle), "Turtle Army sets the new AI with the turtle tactics");
        OpenAi(shell);
        TestHarness.Assert(Row("Engine", 2) == 0 && Row("Tactics", 2) == 2 && Row("Strength", 2) == 1, "and the screen shows New, turtle, medium");
        // A strength of a profile that the slot's box has no entry for is shown by its strength there, and kept here.
        Post(shell, "AiOptions.wnd:ComboStrength2", 2);
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonAccept");
        TestHarness.Assert(setupScreen.AiOf(3) == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Hard, AiTactics.Turtle) && Selected(shell, Slot(3)) == 4, "turtle at hard: the box shows Hard Army, the value is kept");
    }

    private static void SameAsTheOriginalBox(GameData data)
    {
        // The setup left alone, and the same setup after the AI options were opened and accepted unchanged, or with Balanced chosen: the one default match.
        var plain = Start(SetupShell(data));
        var alone = SetupShell(data);
        OpenAi(alone);
        ShellTestInput.Click(alone, "AiOptions.wnd:ButtonAccept");
        var accepted = Start(alone);
        var balanced = SetupShell(data);
        OpenAi(balanced);
        Post(balanced, "AiOptions.wnd:ComboTactics0", 1);
        Post(balanced, "AiOptions.wnd:ComboTactics0", 0);
        ShellTestInput.Click(balanced, "AiOptions.wnd:ButtonAccept");
        var chosen = Start(balanced);
        TestHarness.Assert(plain.Ai.SequenceEqual([MatchAi.Builtin, MatchAi.Builtin]), $"the default setup is the new AI at medium, no profile ({string.Join(", ", plain.Ai)})");
        foreach (var setup in new[] { accepted, chosen })
            TestHarness.Assert(setup.Ai.SequenceEqual(plain.Ai) && !setup.UsesOriginalAi, "opening and accepting the AI options changes nothing");
        var reference = Create(data, plain).Description.Encode();
        TestHarness.Assert(Create(data, plain).Description.Encode().SequenceEqual(reference), "a setup describes itself the same way twice (the check below means something)");
        TestHarness.Assert(Create(data, accepted).Description.Encode().SequenceEqual(reference) && Create(data, chosen).Description.Encode().SequenceEqual(reference), "the default match has the same description whichever way it was set");

        // Rush through the slot's box and through the AI options; easy and hard likewise.
        foreach (var (entry, tactics, strength) in new (int, int, int)[] { (5, 1, 1), (6, 2, 1), (7, 3, 1), (2, 0, 0), (4, 0, 2) })
        {
            var viaBox = SetupShell(data);
            Post(viaBox, Slot(1), entry);
            var boxSetup = Start(viaBox);
            var viaScreen = SetupShell(data);
            OpenAi(viaScreen);
            Post(viaScreen, "AiOptions.wnd:ComboTactics0", tactics);
            Post(viaScreen, "AiOptions.wnd:ComboStrength0", strength);
            ShellTestInput.Click(viaScreen, "AiOptions.wnd:ButtonAccept");
            var screenSetup = Start(viaScreen);
            TestHarness.Assert(screenSetup.Ai.SequenceEqual(boxSetup.Ai), $"entry {entry}: the same AI per slot ({string.Join(", ", boxSetup.Ai)} against {string.Join(", ", screenSetup.Ai)})");
            var boxDescription = Create(data, boxSetup).Description;
            var screenDescription = Create(data, screenSetup).Description;
            TestHarness.Assert(boxDescription.Encode().SequenceEqual(screenDescription.Encode()) && boxDescription.Slots[1].AiStrength == strength && boxDescription.Slots[1].AiTactics == (tactics == 0 ? -1 : tactics),
                $"entry {entry}: the same match description, with strength {strength} and tactics {tactics}");
        }
    }

    private static void Sessions(GameData data)
    {
        // All New: the built-in session (replayable, can be saved).
        var shell = SetupShell(data, EightPlayers);
        Post(shell, Slot(2), 3);
        var allNew = Create(data, Start(shell));
        TestHarness.Assert(allNew.Scripted is null && allNew.Session is not null && allNew.Setup.Opponents.Length == 2, "two New slots: the built-in session, no scripted one");

        // All Classic: the scripted session; the debug-free setup starts.
        shell = SetupShell(data, EightPlayers);
        Post(shell, Slot(2), 3);
        OpenAi(shell);
        Post(shell, "AiOptions.wnd:ComboEngine0", 1);
        Post(shell, "AiOptions.wnd:ComboEngine1", 1);
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonAccept");
        var classicSetup = Start(shell);
        var classic = Create(data, classicSetup);
        TestHarness.Assert(classicSetup.UsesOriginalAi && classic.Scripted is { } scripted && scripted.Slots.Skip(1).All(slot => slot.Kind == SkirmishAiKind.Original), "two Classic slots: the scripted session, every computer slot the original AI");

        // Mixed: slot 1 New (rush at hard), slot 2 Classic (easy), slot 3 New (default).
        shell = SetupShell(data, EightPlayers);
        Post(shell, Slot(2), 3);
        Post(shell, Slot(3), 3);
        OpenAi(shell);
        Post(shell, "AiOptions.wnd:ComboTactics0", 1);
        Post(shell, "AiOptions.wnd:ComboStrength0", 2);
        Post(shell, "AiOptions.wnd:ComboEngine1", 1);
        Post(shell, "AiOptions.wnd:ComboStrength1", 0);
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonAccept");
        var mixedSetup = Start(shell);
        TestHarness.Assert(mixedSetup.Ai.Length == 4 && mixedSetup.Ai[1] == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Hard, AiTactics.Rush) && mixedSetup.Ai[2] == new MatchAi(SkirmishAiKind.Original, ScriptDifficulty.Easy)
                           && mixedSetup.Ai[3] == MatchAi.Builtin && mixedSetup.UsesOriginalAi, $"the setup carries each slot's AI ({string.Join(", ", mixedSetup.Ai)})");
        var mixed = Create(data, mixedSetup, ticks: 1_200);
        var slots = mixed.Scripted?.Slots ?? throw new InvalidOperationException("A mixed match is a scripted session.");
        TestHarness.Assert(slots[1].Kind == SkirmishAiKind.Builtin && slots[1].Tactics == AiTactics.Rush && slots[1].Strength == AiStrength.Hard && slots[2].Kind == SkirmishAiKind.Original && slots[2].Difficulty == ScriptDifficulty.Easy
                           && slots[3].Kind == SkirmishAiKind.Builtin && slots[3].Tactics is null && slots[3].Strength == AiStrength.Medium, "slot 1 is the built-in AI with rush at hard, slot 2 the original at easy, slot 3 the built-in AI as it was");
        var description = mixed.Description;
        TestHarness.Assert(description.Slots[1].AiTactics == (int)AiTactics.Rush && description.Slots[1].AiStrength == 2 && description.Slots[2].AiTactics == -1 && description.Slots[2].AiStrength == 0 && description.Slots[3].AiTactics == -1,
            "the description carries them through the fields it already has");
        for (var step = 0; step < 4; step++) mixed.Advance(0.25);
        TestHarness.Assert(mixed.Tick > 0, "the mixed match runs");
        var saveFailure = false;
        try { mixed.Save("mixed"); }
        catch (CnCNew.Game.Save.SaveGameException) { saveFailure = true; }
        TestHarness.Assert(saveFailure, "a mixed match refuses to be saved with the session's own words, not with a crash");
    }

    private static void MixedSessionPlaysTheProfile(GameData data)
    {
        ulong Hash(AiStrength strength, AiTactics? tactics, int seconds)
        {
            var setup = new MatchSetup(EightPlayers, "FactionAmerica", ["FactionChina", "FactionGLA"], SkirmishMatchOptions.StandardAt(data.Rate)) { Seed = 5 }
                with { Ai = [MatchAi.Builtin, new MatchAi(SkirmishAiKind.Builtin, strength == AiStrength.Easy ? ScriptDifficulty.Easy : strength == AiStrength.Hard ? ScriptDifficulty.Hard : ScriptDifficulty.Normal, tactics),
                    new MatchAi(SkirmishAiKind.Original, ScriptDifficulty.Normal)] };
            var live = Create(data, setup, ticks: 600_000);
            var until = data.Rate.Time.WholeSecondsToTicks(seconds);
            while (live.Tick < until) live.Advance(1.0);
            return live.Scripted!.StateHash();
        }
        var medium = Hash(AiStrength.Medium, null, 60);
        TestHarness.Assert(Hash(AiStrength.Medium, null, 60) == medium, "the mixed match is reproducible");
        TestHarness.Assert(Hash(AiStrength.Easy, null, 60) != medium, "the strength of a New slot changes how it plays inside the mixed session");
        TestHarness.Assert(Hash(AiStrength.Medium, AiTactics.Rush, 240) != Hash(AiStrength.Medium, null, 240), "and so do its tactics");
    }

    private static void EnginesAndRefusals(GameData data)
    {
        // A map with scripts: only Classic. The screen is opened as the setup opens it for such a map.
        var shell = SetupShell(data);
        var rows = ImmutableArray.Create(new AiOptionsRow(1, "Player 2: USA", new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Hard, AiTactics.Rush)));
        shell.OpenAiOptions(rows, AiEngineMode.ClassicOnly, readOnly: false, debugOn: false, _ => { });
        TestHarness.Assert(Selected(shell, "AiOptions.wnd:ComboEngine0") == 1 && !Enabled(shell, "AiOptions.wnd:ComboEngine0") && !Enabled(shell, "AiOptions.wnd:ComboTactics0")
                           && shell.CurrentScreen.Tree.FindByName("AiOptions.wnd:Note1")!.Text!.Value.Contains("scripts", StringComparison.Ordinal), "a map with scripts: Classic, shut, tactics greyed, and the line says why");
        Post(shell, "AiOptions.wnd:ComboEngine0", 0);
        TestHarness.Assert(Selected(shell, "AiOptions.wnd:ComboEngine0") == 1, "the engine cannot be changed there");
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonBack");

        // A room: New only, a member only looks.
        ImmutableArray<MatchAi>? told = null;
        shell.OpenAiOptions(rows, AiEngineMode.NewOnly, readOnly: true, debugOn: false, chosen => told = chosen);
        TestHarness.Assert(Selected(shell, "AiOptions.wnd:ComboEngine0") == 0 && Selected(shell, "AiOptions.wnd:ComboTactics0") == 1 && Selected(shell, "AiOptions.wnd:ComboStrength0") == 2
                           && !Enabled(shell, "AiOptions.wnd:ComboEngine0") && !Enabled(shell, "AiOptions.wnd:ComboTactics0") && !Enabled(shell, "AiOptions.wnd:ComboStrength0") && !Visible(shell, "AiOptions.wnd:ButtonAccept"),
            "a room's screen shows New, the tactics and the strength it carries, shut, without Accept");
        Post(shell, "AiOptions.wnd:ComboStrength0", 0);
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonBack");
        TestHarness.Assert(told is null && shell.CurrentScreen is SkirmishSetupScreen, "a member's look changes nothing");

        // --ai original: every slot starts Classic, the boxes list the three armies only; New can be chosen for a slot.
        shell = SetupShell(data, ai: SkirmishAiKind.Original);
        TestHarness.Assert(Entries(shell, Slot(1)).SequenceEqual(["Open", "Closed", "Easy Army", "Medium Army", "Hard Army"]), "with --ai original the boxes are the original's");
        OpenAi(shell);
        TestHarness.Assert(Selected(shell, "AiOptions.wnd:ComboEngine0") == 1, "the slot starts as the client's --ai says");
        Post(shell, "AiOptions.wnd:ComboEngine0", 0);
        Post(shell, "AiOptions.wnd:ComboTactics0", 2);
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonAccept");
        var asked = Start(shell);
        TestHarness.Assert(asked.Ai[1] == new MatchAi(SkirmishAiKind.Builtin, ScriptDifficulty.Normal, AiTactics.Turtle) && !asked.UsesOriginalAi, "and a slot can be given the new AI there");

        // The debug options need the new AI: with a Classic slot Start says so and starts nothing; with New slots it starts.
        shell = SetupShell(data);
        ShellTestInput.Click(shell, DebugOptionsScreen.Button);
        Post(shell, "DebugOptions.wnd:ComboQuickBuild", 1);
        ShellTestInput.Click(shell, "DebugOptions.wnd:ButtonAccept");
        OpenAi(shell);
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("AiOptions.wnd:Note0")!.Text!.Value.Length > 0, "the screen has its lines");
        Post(shell, "AiOptions.wnd:ComboEngine0", 1);
        TestHarness.Assert(Enumerable.Range(0, 3).Any(note => shell.CurrentScreen.Tree.FindByName($"AiOptions.wnd:Note{note}")!.Text!.Value.Contains("Debug options", StringComparison.Ordinal)), "the screen says the debug options need the new AI");
        ShellTestInput.Click(shell, "AiOptions.wnd:ButtonAccept");
        ShellTestInput.Click(shell, Setup + "ButtonStart");
        shell.BuildDrawList();
        shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(shell.Phase != ShellPhase.MatchLoading && !shell.DrainOutputs().OfType<StartMatchOutput>().Any(), "debug options with a Classic slot start no match");
        shell = SetupShell(data);
        ShellTestInput.Click(shell, DebugOptionsScreen.Button);
        Post(shell, "DebugOptions.wnd:ComboQuickBuild", 1);
        ShellTestInput.Click(shell, "DebugOptions.wnd:ButtonAccept");
        TestHarness.Assert(!Start(shell).Debug.IsDefault, "and with New slots the debug options are played");
    }
}
