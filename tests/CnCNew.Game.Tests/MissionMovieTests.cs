using CnCNew.Data.World;
using CnCNew.Game.Data;
using CnCNew.Game.Movies;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Mission;
using CnCNew.Tests;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Video;
using static CnCNew.Game.Tests.MissionPresentationTests;

namespace CnCNew.Game.Tests;

/// <summary>
/// The movies above the decoder (docs/CLIENT-VIDEO.md): the director's clips on the real clock, the intro, a script's movie, the load screen's movie. The movies are synthetic; the
/// tests with real data check the catalogue and the load screen on the retail campaign. Registered by <see cref="MissionTextTests"/>: the script's movie is one of its overlay outputs.
/// </summary>
internal static class MissionMovieTests
{
    public static void Register(TestHarness harness)
    {
        harness.Run("Movies: a clip runs on the real clock and its sound ends with it; what cannot be played is counted", DirectorClips);
        harness.Run("Movies: the intro is the logo, a second of black, the sizzle; Escape ends a stage, a missing movie is passed over", IntroStages);
        harness.Run("Movies: a script's movie goes to the host and the scripts hear of its end after its length in game time; without a host it stays the placeholder", ScriptMovie);
    }

    public static void RegisterRealData(TestHarness harness, GameData data)
    {
        harness.Run("Real data: every movie of Video.ini is found the way the original looks for it", () => RealCatalog(data));
        harness.Run("Real data: a mission's intro movie plays in the load screen, Escape ends it and the mission starts; without it the screen holds as before", () => RealBriefing(data));
    }

    private sealed class SoundProbe : IMovieSound
    {
        public int Started, Stopped;
        public long PlayMovieSound(BinkSound sound) => ++Started;
        public void StopMovieSound(long handle) => Stopped++;
    }

    private static MovieDirector Director(int frames, Action<string>? skipped = null, IMovieSound? sound = null, Action<string>? opened = null, string? missing = null)
        => new(name =>
        {
            opened?.Invoke(name);
            return name == missing ? null : new MemoryStream(BinkFixtures.FilledMovie(frames));
        }, sound, skipped);

    private static void DirectorClips()
    {
        var skipped = new List<string>();
        var sound = new SoundProbe();
        var movies = new MovieDirector(name => name == "Clip" ? new MemoryStream(WithSound()) : name == "Bad" ? new MemoryStream(new byte[100]) : null, sound, skipped.Add);

        TestHarness.Assert(movies.Open("Gone") is null && movies.PlayFullScreen("Bad", modal: true) is null && !movies.FullScreenIsModal, "a missing and a damaged movie play nothing");
        TestHarness.Assert(skipped.SequenceEqual(["Gone", "Bad"]) && movies.Failed.ContainsKey("Gone") && movies.Failed.ContainsKey("Bad"), "both are counted with a reason, nothing throws");

        var clip = movies.PlayFullScreen("Clip", modal: true)!;
        TestHarness.Assert(movies.FullScreen == clip && movies.FullScreenIsModal && sound.Started == 1 && clip.Version == 1, "the movie stands alone, with its sound");
        movies.Update(0.04);
        TestHarness.Assert(clip.Frame == 2 && clip.Version == 2, "the second frame comes after a frame's time");
        movies.Update(1);
        TestHarness.Assert(movies.FullScreen is null && !movies.FullScreenIsModal && clip.IsFinished && sound.Stopped == 1, "at its end the movie and its sound are gone");

        var radar = movies.PlayRadar("Clip")!;
        var replaced = movies.PlayRadar("Clip")!;
        TestHarness.Assert(radar.IsFinished && !replaced.IsFinished && movies.Radar == replaced && sound.Stopped == 2, "a new radar movie replaces the one that runs");
        movies.StopAll();
        TestHarness.Assert(movies.Radar is null && replaced.IsFinished && sound.Stopped == 3, "a game's end stops what plays");
        TestHarness.Assert(movies.Played["Clip"] == 3, "starts are counted");
        TestHarness.Assert(movies.Report().Any(line => line.StartsWith("movies played: Clip 3x", StringComparison.Ordinal)) && movies.Report().Count(line => line.StartsWith("movie not played", StringComparison.Ordinal)) == 2, "the report names both");
    }

    /// <summary>Two frames with a sound of one block on the first.</summary>
    private static byte[] WithSound()
    {
        var frame = BinkFixtures.FilledFrame([100, 100, 100, 100], 128, 128);
        return BinkFixtures.File(16, 16, [frame, frame], [BinkFixtures.AudioPacket(2, 2048, [(1000f, 0f)]), new byte[4]]);
    }

    private static void IntroStages()
    {
        var opened = new List<string>();
        var movies = Director(3, opened: opened.Add);
        var intro = new MovieIntro(movies);
        intro.Update(0.01);
        TestHarness.Assert(opened.SequenceEqual([MovieIntro.LogoMovie]) && movies.FullScreenIsModal, "the logo first, alone");
        movies.Update(0.2);
        intro.Update(0.01);
        intro.Update(0.9);
        intro.Update(0.05);
        TestHarness.Assert(opened.Count == 1 && !intro.IsDone, "a second of black follows");
        intro.Update(0.1);
        intro.Update(0.01);
        TestHarness.Assert(opened.SequenceEqual([MovieIntro.LogoMovie, MovieIntro.SizzleMovie]), "then the sizzle");
        intro.Skip();
        TestHarness.Assert(movies.FullScreen is null, "Escape ends the movie");
        intro.Update(0.01);
        TestHarness.Assert(intro.IsDone, "and the shell can come");

        var alone = new MovieIntro(Director(3, missing: MovieIntro.LogoMovie));
        alone.Update(0.01);
        alone.Skip();
        alone.Update(0.01);
        alone.Update(0.01);
        TestHarness.Assert(!alone.IsDone, "a missing logo is passed over, the sizzle still plays");
        var skipped = new MovieIntro(Director(3));
        for (var step = 0; step < 8; step++)
        {
            skipped.Update(0.01);
            skipped.Skip();
        }
        TestHarness.Assert(skipped.IsDone, "Escape after Escape reaches the shell");
    }

    private static void ScriptMovie()
    {
        var movie = Script("Movie", If("CONDITION_TRUE"), [Do("MOVIE_PLAY_FULLSCREEN", Named(MapScriptParameterType.ObjectType, "Clip"))]);
        var radar = Script("Radar", If("CONDITION_TRUE"), [Do("MOVIE_PLAY_RADAR", Named(MapScriptParameterType.ObjectType, "Small"))]);
        var options = MissionOptions.Standard with { PresentationLatencyTicks = 1_000_000 };
        var session = Start([movie, radar], null, options);
        var asked = new List<(string Name, bool Radar)>();
        var director = new MissionPresentationDirector(session)
        {
            Movies = (name, toRadar) =>
            {
                asked.Add((name, toRadar));
                return name == "Clip" ? 2.0 : null;
            },
        };
        void StepOnce()
        {
            director.BeforeStep();
            session.Step();
            director.AfterStep();
        }

        var start = session.World.Tick;
        StepOnce();
        TestHarness.Assert(asked.SequenceEqual([("Clip", false), ("Small", true)]), "the host is asked for both, the radar's as such");
        TestHarness.Assert(director.MoviePlaceholder == "Small", "the one the host cannot play stays the placeholder; the other shows none");
        TestHarness.Assert(session.Engine.PendingPresentation.Contains("Clip"), "the movie counts as running for the scripts");
        var ticks = CnCNew.Tests.TestTime.Frames(session.World, 60);
        while (session.World.Tick < start + ticks - 1) StepOnce();
        TestHarness.Assert(session.Engine.PendingPresentation.Contains("Clip"), "until its length is over");
        StepOnce();
        StepOnce();
        TestHarness.Assert(!session.Engine.PendingPresentation.Contains("Clip"), "two seconds of game time later the scripts hear of its end, however fast the client draws");

        var headless = Start([movie], null, options);
        var plain = new MissionPresentationDirector(headless);
        plain.BeforeStep();
        headless.Step();
        plain.AfterStep();
        TestHarness.Assert(plain.MoviePlaceholder == "Clip", "without a host the movie is its placeholder");
    }

    private static void RealCatalog(GameData data)
    {
        TestHarness.Assert(data.Movies.Count == 41, $"Video.ini names 41 movies, read {data.Movies.Count}");
        TestHarness.Assert(data.Movies.FileOf("EALogoMovie") == "EA_LOGO" && data.Movies.FileOf("ealogomovie") == "EA_LOGO", "names are found whatever their case");
        var english = data.Movies.Find("Sizzle", data.Content, "English");
        TestHarness.Assert(english == "Data/English/Movies/sizzle_review.bik", $"a movie of the language folder, got {english}");
        var general = data.Movies.Find("VSSmall", data.Content, "English");
        TestHarness.Assert(general == "Data/Movies/VS_small.bik", $"a movie of the general folder, got {general}");
        TestHarness.Assert(data.Movies.Find("NoSuchMovie", data.Content, "English") is null, "an unknown name is none");
        var unknown = CnCNew.Game.Campaign.CampaignCatalog.Parse(data.Content.OpenRead("Data/INI/Campaign.ini")).Campaigns.SelectMany(campaign => campaign.Missions)
            .Select(mission => mission.IntroMovie).Where(movie => movie.Length > 0 && data.Movies.Find(movie, data.Content, "English") is null).Distinct().ToArray();
        // The retail file names one movie Video.ini does not know: the training mission has none in the original either (BinkVideoPlayer::open answers null).
        TestHarness.Assert(unknown.SequenceEqual(["TrainingCampaign"]), $"every intro movie of Campaign.ini is there but the training's, got {string.Join(", ", unknown)}");
    }

    private static void RealBriefing(GameData data)
    {
        var opened = new List<string>();
        var movies = Director(300, opened: opened.Add);
        var shell = StartCampaign(data, movies);
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing && opened.SequenceEqual(["MD_USA01"]), $"the first mission opens its intro movie, opened {string.Join(",", opened)}");
        TestHarness.Assert(shell.DrainOutputs().OfType<StopMusicOutput>().Any(), "the menu theme ends for the movie's sound");
        var list = shell.BuildDrawList();
        var video = list.Commands.OfType<UiVideoDraw>().SingleOrDefault();
        TestHarness.Assert(video is not null && video.Destination.Width >= 799 && video.Destination.Height >= 599, $"the movie fills the load screen ({video})");
        var videoAt = Array.FindIndex(list.Commands.ToArray(), command => command is UiVideoDraw);
        TestHarness.Assert(videoAt == 1 && list.Commands[0] is UiImageDraw, "over the screen's picture, under its children");

        movies.Update(1);
        shell.Update(1);
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing, "the screen waits for the movie, not for the voice length");
        shell.HandleInput(new KeyEvent(0, KeyCode.Escape, true));
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing, "Escape counts when it comes up");
        shell.HandleInput(new KeyEvent(0, KeyCode.Escape, false));
        shell.Update(0.01);
        TestHarness.Assert(shell.Phase == ShellPhase.InMission && shell.DrainOutputs().OfType<StartMissionOutput>().Any(), "then the mission starts");

        var ending = StartCampaign(data, Director(3, opened: _ => { }));
        ending.Movies!.Update(1);
        ending.Update(0.01);
        TestHarness.Assert(ending.Phase == ShellPhase.InMission, "a movie that ends starts the mission");

        var skipped = new List<string>();
        var without = StartCampaign(data, Director(3, skipped.Add, missing: "MD_USA01"));
        TestHarness.Assert(skipped.SequenceEqual(["MD_USA01"]) && !without.BuildDrawList().Commands.OfType<UiVideoDraw>().Any(), "a missing movie is a counted note");
        without.Update(1);
        TestHarness.Assert(without.Phase == ShellPhase.MissionBriefing, "and the screen holds as it did before");
        for (var step = 0; step < 400 && without.Phase == ShellPhase.MissionBriefing; step++) without.Update(0.1);
        TestHarness.Assert(without.Phase == ShellPhase.InMission, "until the voice length is over");
    }

    private static GameShell StartCampaign(GameData data, MovieDirector movies)
    {
        var environment = new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-movie-tests-" + Guid.NewGuid().ToString("N")));
        var shell = GameShell.Create(data, environment, viewport: new UiViewport(800, 600), skin: CnCNew.Ui.Skins.UiSkin.Original);
        shell.Movies = movies;
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonUSA");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonHard");
        return shell;
    }
}
