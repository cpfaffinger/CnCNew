using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CnCNew.Launcher;

static class Tests
{
    private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();

    public static void Versions()
    {
        Check.Equal(new SemanticVersion(1, 2, 3), SemanticVersion.Parse("v1.2.3"), "a leading v");
        Check.Equal("1.2.3-rc.1", SemanticVersion.Parse("1.2.3-rc.1+build5").ToString(), "build metadata is dropped");
        Check.That(SemanticVersion.TryParse("1.2") is null && SemanticVersion.TryParse("x") is null && SemanticVersion.TryParse("") is null && SemanticVersion.TryParse(null) is null, "not versions");
        Check.That(SemanticVersion.Parse("0.10.0") > SemanticVersion.Parse("0.9.9"), "numbers compare as numbers, not text");
        Check.That(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.1"), "a release is newer than its candidate");
        Check.That(SemanticVersion.Parse("1.0.0-rc.2") > SemanticVersion.Parse("1.0.0-rc.1"), "rc.2 after rc.1");
        Check.That(SemanticVersion.Parse("1.0.0-rc.10") > SemanticVersion.Parse("1.0.0-rc.9"), "numeric parts compare as numbers");
        Check.That(SemanticVersion.Parse("1.0.0-alpha") < SemanticVersion.Parse("1.0.0-beta"), "alpha before beta");
        Check.That(SemanticVersion.Parse("2.0.0") > SemanticVersion.Parse("1.99.99"), "major wins");
    }

    public static void ReleaseJson()
    {
        var check = ReleaseClient.Parse(Fixtures.ReleaseJson("0.2.0"));
        Check.That(check.Succeeded && check.Release is not null, "parsed");
        var release = check.Release!;
        Check.Equal("v0.2.0", release.Tag, "tag");
        Check.Equal(new SemanticVersion(0, 2, 0), release.Version, "version");
        Check.Equal("Notes of 0.2.0", release.Notes, "notes");
        Check.Equal("CnCNew-Client-0.2.0-win-x64.zip", release.ClientPackage?.Name, "client package");
        Check.Equal("CnCNew-Client-0.2.0-win-x64.zip.sha256", release.ClientHash?.Name, "its checksum");
        Check.Equal("CnCNew-Launcher-0.2.0.exe", release.Launcher?.Name, "launcher");
        Check.Equal("CnCNew-Launcher-0.2.0.exe.sha256", release.LauncherHash?.Name, "launcher checksum");
        Check.That(release.PublishedAt is { Year: 2026, Month: 11 }, "published date");

        var stem = ReleaseClient.Parse("{\"tag_name\":\"1.0.0\",\"assets\":[{\"name\":\"CnCNew-Client-1.0.0-win-x64.zip\",\"browser_download_url\":\"https://x/a.zip\",\"size\":5},{\"name\":\"CnCNew-Client-1.0.0-win-x64.sha256\",\"browser_download_url\":\"https://x/a.sha\",\"size\":1}]}").Release!;
        Check.Equal("CnCNew-Client-1.0.0-win-x64.sha256", stem.ClientHash?.Name, "a checksum file named without .zip");
        Check.That(stem.Launcher is null, "no launcher asset");
    }

    public static void ReleaseFailures()
    {
        ReleaseCheck Ask(Action<FakeServer> arrange, string? tag = null)
        {
            var server = new FakeServer();
            arrange(server);
            return Run(new ReleaseClient(new HttpClient(server), apiBase: FakeServer.Base).GetLatestAsync());
        }
        var path = $"/repos/{ReleaseClient.DefaultRepository}/releases/latest";
        var none = Ask(_ => { });
        Check.Equal(ReleaseCheckStatus.NoRelease, none.Status, "404 is no release yet");
        Check.Contains(none.Message, "No release", "said plainly");

        var reset = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var limited = Ask(server => server.Route(path, () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", reset.ToString());
            return response;
        }));
        Check.Equal(ReleaseCheckStatus.RateLimited, limited.Status, "403 with an exhausted limit");
        Check.That(limited.RetryAfter is not null && limited.RetryAfter.Value.ToUnixTimeSeconds() == reset, "the reset time is carried");
        Check.Contains(limited.Message, "too many requests", "said plainly");

        Check.Equal(ReleaseCheckStatus.RateLimited, Ask(server => server.Route(path, () => new HttpResponseMessage((HttpStatusCode)429))).Status, "429");
        Check.Equal(ReleaseCheckStatus.Failed, Ask(server => server.Route(path, () => new HttpResponseMessage(HttpStatusCode.InternalServerError))).Status, "500");
        Check.Equal(ReleaseCheckStatus.Failed, Ask(server => server.ServeText(path, "<html>not json</html>")).Status, "nonsense");
        Check.Equal(ReleaseCheckStatus.Failed, Ask(server => server.ServeText(path, "{\"tag_name\":\"nightly\"}")).Status, "a tag that is no version");
        Check.Equal(ReleaseCheckStatus.Failed, Ask(server => server.ServeText(path, "{\"name\":\"x\"}")).Status, "no tag at all");

        var offline = Run(new ReleaseClient(new HttpClient(new ThrowingHandler(new HttpRequestException("no network"))), apiBase: FakeServer.Base).GetLatestAsync());
        Check.Equal(ReleaseCheckStatus.Offline, offline.Status, "no network");
        Check.Contains(offline.Message, "internet connection", "said plainly");
        Check.Equal(ReleaseCheckStatus.Offline, Run(new ReleaseClient(new HttpClient(new ThrowingHandler(new TaskCanceledException("timeout"))), apiBase: FakeServer.Base).GetLatestAsync()).Status, "a timeout");
    }

    public static void ChecksumFiles()
    {
        var hash = new string('a', 64);
        Check.Equal(hash, Hashing.ParseExpected($"{hash}  file.zip\n", "file.zip"), "hash and name");
        Check.Equal(hash, Hashing.ParseExpected($"{hash.ToUpperInvariant()} *file.zip", "file.zip"), "upper case, binary marker");
        Check.Equal(hash, Hashing.ParseExpected(hash + "\r\n", "file.zip"), "hash only");
        Check.Equal(new string('b', 64), Hashing.ParseExpected($"{hash}  other.zip\n{new string('b', 64)}  dir\\file.zip\n", "file.zip"), "the line for this file");
        Check.That(Hashing.ParseExpected("not a checksum", "file.zip") is null, "no hash");
        Check.That(Hashing.ParseExpected("", "file.zip") is null, "empty");
    }

    public static void Download()
    {
        var server = new FakeServer();
        var payload = new byte[300_000];
        new Random(5).NextBytes(payload);
        server.Serve("/file", payload);
        var downloader = new Downloader(new HttpClient(server));
        var destination = Fixtures.Temp("download.bin");
        var reports = new List<TransferProgress>();
        Run(Task.Run(async () => { await downloader.DownloadAsync(FakeServer.Base + "/file", destination, new SyncProgress<TransferProgress>(reports.Add), default); return 0; }));
        Check.That(File.ReadAllBytes(destination).SequenceEqual(payload), "the bytes arrived");
        Check.That(reports.Count > 0 && reports[^1].Bytes == payload.Length && reports[^1].Fraction == 1.0, "the last report is complete");
        Check.That(!File.Exists(destination + ".part"), "no partial file left");

        server.Route("/short", () =>
        {
            var content = new ByteArrayContent(new byte[10]);
            content.Headers.ContentLength = 100;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var broken = Fixtures.Temp("broken.bin");
        var failed = false;
        try { Run(Task.Run(async () => { await downloader.DownloadAsync(FakeServer.Base + "/short", broken, null, default); return 0; })); }
        catch (DownloadException) { failed = true; }
        Check.That(failed, "a short transfer is an error");
        Check.That(!File.Exists(broken) && !File.Exists(broken + ".part"), "and leaves nothing behind");
        var refused = false;
        try { Run(Task.Run(async () => { await downloader.DownloadAsync(FakeServer.Base + "/missing", broken, null, default); return 0; })); }
        catch (DownloadException exception) { refused = exception.Message.Contains("404", StringComparison.Ordinal); }
        Check.That(refused, "a refused download names the code");
    }

    public static void Unpack()
    {
        var zipPath = Fixtures.Temp("p.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        File.WriteAllBytes(zipPath, Fixtures.ClientZip("a", rootFolder: "cncnew-win-x64"));
        var staging = Fixtures.Temp("staging");
        PackageInstaller.Unpack(zipPath, staging);
        Check.That(File.Exists(Path.Combine(staging, "bin", "CnCNew.Client.Stride.exe")) && File.Exists(Path.Combine(staging, "LICENSE.md")), "a single top folder is flattened");

        File.WriteAllBytes(zipPath, Fixtures.ClientZip("b"));
        PackageInstaller.Unpack(zipPath, staging);
        Check.That(File.ReadAllText(Path.Combine(staging, "bin", "CnCNew.Client.Stride.exe")) == "client b", "a package rooted directly, and a second unpack replaces the first");

        File.WriteAllBytes(zipPath, Fixtures.ClientZip("c", extra: ("../escape.txt", "x")));
        var unsafeName = false;
        try { PackageInstaller.Unpack(zipPath, staging); } catch (PackageException exception) { unsafeName = exception.Message.Contains("unsafe", StringComparison.OrdinalIgnoreCase); }
        Check.That(unsafeName, "a name that climbs out is refused");
        Check.That(!File.Exists(Path.Combine(Path.GetDirectoryName(staging)!, "escape.txt")), "and nothing was written outside");

        File.WriteAllText(zipPath, "this is not a zip");
        var notZip = false;
        try { PackageInstaller.Unpack(zipPath, staging); } catch (PackageException exception) { notZip = exception.Message.Contains("not a valid package", StringComparison.OrdinalIgnoreCase); }
        Check.That(notZip, "garbage is not a package");

        using (var buffer = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true)) { zip.CreateEntry("readme.txt"); }
            File.WriteAllBytes(zipPath, buffer.ToArray());
        }
        var missing = false;
        try { PackageInstaller.Unpack(zipPath, staging); } catch (PackageException exception) { missing = exception.Message.Contains("does not hold", StringComparison.OrdinalIgnoreCase); }
        Check.That(missing, "a zip without the client is not a client package");
    }

    public static void ApplyAndDamage()
    {
        var folder = Fixtures.Temp("install");
        var layout = new InstallLayout(folder);
        var zipPath = Fixtures.Temp("p.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        File.WriteAllBytes(zipPath, Fixtures.ClientZip("v1", extra: ("licences/pkg.txt", "mit")));
        PackageInstaller.Unpack(zipPath, Path.Combine(folder, ".staging"));
        PackageInstaller.Apply(Path.Combine(folder, ".staging"), layout, new ReleaseRecord("0.1.0", "v0.1.0", DateTimeOffset.UtcNow));
        Check.That(File.ReadAllText(layout.ClientExe) == "client v1" && layout.InstalledVersion == new SemanticVersion(0, 1, 0), "installed, and the record names the version");
        Check.That(!Directory.Exists(Path.Combine(folder, ".staging")), "the staging folder is gone");
        Check.Equal(InstallState.ClientOnly, layout.Inspect(), "client without content");

        Directory.CreateDirectory(Path.Combine(layout.GameDirectory, "content"));
        File.WriteAllText(Path.Combine(layout.GameDirectory, "content", "keep.txt"), "the user's content");
        File.WriteAllText(Path.Combine(folder, "notes-of-the-user.txt"), "x");

        var whole = Run(PackageInstaller.FindDamageAsync(layout));
        Check.That(whole is { Count: 0 }, "a fresh install is whole");

        File.WriteAllText(layout.ClientExe, "tampered");
        File.Delete(Path.Combine(folder, "licences", "pkg.txt"));
        var damaged = Run(PackageInstaller.FindDamageAsync(layout))!;
        Check.That(damaged.Count == 2 && damaged.Contains("bin/CnCNew.Client.Stride.exe") && damaged.Contains("licences/pkg.txt"), "changed and missing files are found: " + string.Join(",", damaged));

        File.WriteAllBytes(zipPath, Fixtures.ClientZip("v2"));
        PackageInstaller.Unpack(zipPath, Path.Combine(folder, ".staging"));
        PackageInstaller.Apply(Path.Combine(folder, ".staging"), layout, new ReleaseRecord("0.2.0", "v0.2.0", DateTimeOffset.UtcNow));
        Check.Equal("client v2", File.ReadAllText(layout.ClientExe), "the program is replaced");
        Check.That(!File.Exists(Path.Combine(folder, "licences", "pkg.txt")), "a file the new package no longer has is gone with the old folder");
        Check.That(File.ReadAllText(Path.Combine(layout.GameDirectory, "content", "keep.txt")) == "the user's content", "the game folder is untouched");
        Check.That(File.Exists(Path.Combine(folder, "notes-of-the-user.txt")), "unknown files stay");
        Check.That(Run(PackageInstaller.FindDamageAsync(layout)) is { Count: 0 }, "the list was written again");
    }

    public static void Folders()
    {
        Check.That(!FolderRules.Judge("").Acceptable, "empty");
        Check.That(!FolderRules.Judge("C:\\").Acceptable, "a drive");
        Check.That(!FolderRules.Judge("C:\\Program Files\\CnCNew").Acceptable, "a protected folder");
        Check.Contains(FolderRules.Judge("C:\\Program Files (x86)\\Steam\\steamapps\\common\\CnCNew").Message, "Steam", "inside a Steam library by name");
        Check.Contains(FolderRules.Judge("D:\\Games\\SteamLibrary\\CnCNew", ["D:\\Games\\SteamLibrary"]).Message, "Steam", "inside a library Steam knows");
        Check.That(!FolderRules.Judge("C:\\" + new string('x', 120)).Acceptable, "too long");
        var good = Fixtures.Temp("good");
        Check.That(FolderRules.Judge(good).Acceptable, "a new folder is fine");
        Directory.CreateDirectory(good);
        Check.That(FolderRules.Judge(good).Acceptable, "an empty folder is fine");
        File.WriteAllText(Path.Combine(good, "photo.jpg"), "x");
        var busy = FolderRules.Judge(good);
        Check.That(!busy.Acceptable && busy.Message.Contains("other files", StringComparison.Ordinal), "a folder with other files is refused");
        new InstallLayout(good).WriteRelease(new ReleaseRecord("0.1.0", "v0.1.0", DateTimeOffset.UtcNow));
        Check.That(FolderRules.Judge(good).Acceptable, "a folder of ours is fine again");
        Check.That(FolderRules.Judge("\"" + good + "\"").Acceptable, "quotes around a pasted path are dropped");
    }

    public static void Settings()
    {
        var path = Fixtures.Temp("launcher.json");
        Check.That(LauncherSettings.Load(path) == new LauncherSettings(), "missing file gives defaults");
        Check.That(!new LauncherSettings().VerifySimulation, "the simulation check is off by default");
        Check.That(new LauncherSettings().CheckForUpdates, "update check is on by default");
        Check.That(new LauncherSettings().EffectiveInstallFolder.EndsWith("CnCNew", StringComparison.Ordinal), "the default folder is %USERPROFILE%\\CnCNew");
        var saved = new LauncherSettings { InstallFolder = "D:\\Games\\CnCNew", VerifySimulation = true, SkippedVersion = "0.3.0" };
        Check.That(saved.Save(path), "saved");
        Check.That(LauncherSettings.Load(path) == saved, "round trip");
        File.WriteAllText(path, "{ this is broken");
        Check.That(LauncherSettings.Load(path) == new LauncherSettings(), "a damaged file gives defaults");
    }

    public static void SteamLocating()
    {
        var steam = Fixtures.Temp("Steam");
        var library = Fixtures.Temp("Library2");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Generals Zero Hour Dir"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
        Check.That(!SteamLocation.Find([steam], null).Found, "no manifest, no game");
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_2732960.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"2732960\"\n\t\"installdir\"\t\t\"Generals Zero Hour Dir\"\n\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"german\"\n\t}\n}\n");
        var found = SteamLocation.Find([steam], null);
        Check.That(found.Found && found.Directory!.EndsWith("Generals Zero Hour Dir", StringComparison.Ordinal), "found through libraryfolders.vdf and the manifest");
        Check.Equal("german", found.Language, "the language Steam holds");
        Check.That(!found.IsEnglishOrUnknown, "German is not English");
        Check.That(found.Libraries.Count >= 2, "the libraries are listed");
    }

    private sealed class FakeProbe(bool is64 = true, int build = 26100, long free = 100L << 30, SteamCopy? steam = null, string[]? adapters = null) : ISystemProbe
    {
        public bool Is64BitWindows => is64;
        public int WindowsBuild => build;
        public long FreeBytes(string path) => free;
        public SteamCopy FindSteamCopy() => steam ?? new SteamCopy("C:\\Games\\Steam\\steamapps\\common\\Zero", ["C:\\Games\\Steam"], "english");
        public IReadOnlyList<string> GraphicsAdapters() => adapters ?? ["NVIDIA GeForce RTX 3060"];
    }

    public static void PreflightChecks()
    {
        var folder = Fixtures.Temp("pf");
        var fine = Preflight.Run(new FakeProbe(), folder);
        Check.That(Preflight.CanInstall(fine) && fine.All(result => result.Status == CheckStatus.Ok), "a good machine passes everything");
        Check.Equal(5, fine.Count, "five rows");

        CheckResult Row(IEnumerable<CheckResult> rows, string id) => rows.Single(row => row.Id == id);
        var noSteam = Preflight.Run(new FakeProbe(steam: new SteamCopy(null, [], null)), folder);
        Check.That(!Preflight.CanInstall(noSteam) && Row(noSteam, "steam").Detail.Contains("install your own copy in Steam", StringComparison.Ordinal), "no game: install it in Steam");
        var german = Preflight.Run(new FakeProbe(steam: new SteamCopy("C:\\x", [], "german")), folder);
        Check.That(!Preflight.CanInstall(german) && Row(german, "steam").Detail.Contains("choose English", StringComparison.Ordinal), "German: how to change the language");
        var small = Preflight.Run(new FakeProbe(free: 2L << 30), folder);
        Check.That(Row(small, "disk").Status == CheckStatus.Failed && Row(small, "disk").Detail.Contains("2.0 GB", StringComparison.Ordinal), "too little disk, with the number");
        Check.That(Row(Preflight.Run(new FakeProbe(is64: false), folder), "windows").Status == CheckStatus.Failed, "32-bit Windows");
        Check.That(Row(Preflight.Run(new FakeProbe(build: 10240), folder), "windows").Status == CheckStatus.Failed, "an old Windows 10");
        var basic = Preflight.Run(new FakeProbe(adapters: ["Microsoft Basic Display Adapter"]), folder);
        Check.That(Row(basic, "graphics").Status == CheckStatus.Warning && Preflight.CanInstall(basic), "a basic driver is a warning, not a stop");
        Check.That(Row(Preflight.Run(new FakeProbe(adapters: []), folder), "graphics").Status == CheckStatus.Warning, "no adapter listed is a warning");
        var inSteam = Preflight.Run(new FakeProbe(), "C:\\Games\\Steam\\steamapps\\common\\CnCNew");
        Check.That(Row(inSteam, "folder").Status == CheckStatus.Failed, "a folder inside Steam is refused by the preflight too");
    }

    public static void PatcherLines()
    {
        var progress = PatcherOutput.Parse("import:  40% (4/10)");
        Check.That(progress.Kind == PatcherEventKind.Progress && Math.Abs(progress.Fraction - 0.4) < 1e-9 && progress.Step == "import", "a progress line");
        Check.Equal("Making the game files", progress.Text, "import in plain words");
        Check.Equal("Checking your Steam copy of the game", PatcherOutput.Describe("check"), "check");
        Check.Equal("Fetching the community patch", PatcherOutput.Describe("patch"), "patch");
        Check.Equal("Verifying the game files", PatcherOutput.Describe("verify"), "verify");
        Check.Equal("Making the game files", PatcherOutput.Describe("export (dry run)"), "any other step");
        Check.That(PatcherOutput.Parse("error: boom").Kind == PatcherEventKind.Error, "an error line");
        Check.That(PatcherOutput.Parse("Found the game through Steam: C:\\x").Kind == PatcherEventKind.Info, "an info line");
        Check.Contains(PatcherOutput.Explain(new ToolResult(1, ["No Steam installation of \"X\" (app 1) was found."])), "cannot find", "no Steam copy");
        var differs = PatcherOutput.Explain(new ToolResult(1, ["The installation differs from the master:", "  missing: INI.big"]));
        Check.That(differs.Contains("not the version", StringComparison.Ordinal) && differs.Contains("missing: INI.big", StringComparison.Ordinal), "a different version, with the lines that say what");
        Check.Contains(PatcherOutput.Explain(new ToolResult(2, ["error: The target 'x' lies in a Steam library."])), "inside a Steam library", "a refused target");
        Check.Equal("Cancelled.", PatcherOutput.Explain(new ToolResult(-1, [], Cancelled: true)), "cancelled");
    }

    public static void ProcessRunner()
    {
        var script = Fixtures.Temp("tool.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "@echo off\r\necho import:  50%% (5/10)\r\necho error: two 1>&2\r\nexit /b 3\r\n");
        var seen = new List<string>();
        var result = Run(new ProcessToolRunner().RunAsync("cmd.exe", ["/c", script], Path.GetDirectoryName(script)!, line => { lock (seen) seen.Add(line); }, default));
        Check.Equal(3, result.ExitCode, "exit code");
        Check.That(!result.Succeeded && result.Lines.Contains("import:  50% (5/10)") && result.Lines.Any(line => line.Trim() == "error: two"), "both output streams arrive: " + string.Join("|", result.Lines));
        Check.That(seen.Count == 2, "and each line was reported");
        var absent = Run(new ProcessToolRunner().RunAsync(Path.Combine(Path.GetDirectoryName(script)!, "nothing.exe"), [], Path.GetDirectoryName(script)!, _ => { }, default));
        Check.That(!absent.Succeeded && absent.Lines[0].Contains("could not be started", StringComparison.Ordinal), "a missing program is a result, not a crash");
        using var cancel = new CancellationTokenSource(200);
        File.WriteAllText(script, "@echo off\r\nping -n 20 127.0.0.1 >nul\r\n");
        var cancelled = Run(new ProcessToolRunner().RunAsync("cmd.exe", ["/c", script], Path.GetDirectoryName(script)!, _ => { }, cancel.Token));
        Check.That(cancelled.Cancelled, "cancelling ends the program");
    }

    public static void FirstInstall()
    {
        var (engine, server, patcher, data) = Fixtures.Engine();
        Fixtures.Publish(server, "0.2.0", "one");
        var latest = Run(engine.CheckLatestAsync());
        var steps = new StepList(includeVerify: false);
        var headlines = new List<string>();
        engine.Progress += progress => { steps.Apply(progress); headlines.Add(progress.Headline); };
        var folder = Fixtures.Temp("game-folder");
        var result = Run(engine.InstallAsync(folder, latest.Release!, verifySimulation: false));
        Check.That(result.Succeeded, "installed: " + result.Message);
        var layout = new InstallLayout(folder);
        Check.Equal(InstallState.Ready, layout.Inspect(), "ready");
        Check.Equal(new SemanticVersion(0, 2, 0), layout.InstalledVersion, "the version is recorded");
        Check.Equal("client one", File.ReadAllText(layout.ClientExe), "the program is there");
        Check.Equal(1, patcher.Calls.Count, "the patcher ran once");
        Check.Equal($"CnCNew.AssetTool.exe install auto {layout.GameDirectory}", patcher.Calls[0], "with 'auto' (Steam is only read) and a target below the install folder");
        Check.That(steps.Steps.All(step => step.State is StepState.Done or StepState.Skipped), "every step ended: " + string.Join(", ", steps.Steps.Select(step => step.Id + "=" + step.State)));
        Check.That(headlines.Contains("Downloading the game package") && headlines.Any(text => text.Contains("community patch", StringComparison.Ordinal)), "the user saw the downloading and the patch");
        Check.That(!Directory.Exists(Path.Combine(folder, ".staging")) && !Directory.EnumerateFiles(Path.Combine(data, "downloads")).Any(), "no staging folder and no downloaded archive is left");
        Check.That(server.Requests.All(url => url.StartsWith(FakeServer.Base, StringComparison.Ordinal)), "no request left the fake server");
    }

    public static void WrongChecksum()
    {
        var (engine, server, patcher, _) = Fixtures.Engine();
        Fixtures.Publish(server, "0.2.0", "one", corruptHash: true);
        var folder = Fixtures.Temp("wrong");
        var result = Run(engine.InstallAsync(folder, Run(engine.CheckLatestAsync()).Release!, false));
        Check.That(!result.Succeeded && result.FailedStep == StepId.Download, "refused");
        Check.Contains(result.Message, "does not match its published checksum", "said in plain words");
        Check.That(!File.Exists(new InstallLayout(folder).ClientExe), "nothing was installed");
        Check.Equal(0, patcher.Calls.Count, "the patcher never ran");
    }

    public static void IncompleteRelease()
    {
        var (engine, server, _, _) = Fixtures.Engine();
        var path = $"/repos/{ReleaseClient.DefaultRepository}/releases/latest";
        server.ServeText(path, Fixtures.ReleaseJson("0.3.0", withHash: false));
        var noHash = Run(engine.InstallAsync(Fixtures.Temp("a"), Run(engine.CheckLatestAsync()).Release!, false));
        Check.That(!noHash.Succeeded && noHash.Message.Contains("checksum", StringComparison.Ordinal), "no checksum, no install");
        server.ServeText(path, Fixtures.ReleaseJson("0.3.0", withClient: false));
        var noPackage = Run(engine.InstallAsync(Fixtures.Temp("b"), Run(engine.CheckLatestAsync()).Release!, false));
        Check.That(!noPackage.Succeeded && noPackage.Message.Contains("no game package", StringComparison.Ordinal), "no package");
        var running = Fixtures.Engine(running: _ => true);
        Fixtures.Publish(running.Server, "0.2.0", "x");
        var blocked = Run(running.Engine.InstallAsync(Fixtures.Temp("c"), Run(running.Engine.CheckLatestAsync()).Release!, false));
        Check.That(!blocked.Succeeded && blocked.Message.Contains("running", StringComparison.Ordinal), "the game is running");
    }

    public static void Update()
    {
        var (engine, server, patcher, _) = Fixtures.Engine();
        Fixtures.Publish(server, "0.1.0", "old");
        var folder = Fixtures.Temp("upd");
        Check.That(Run(engine.InstallAsync(folder, Run(engine.CheckLatestAsync()).Release!, false)).Succeeded, "first install");
        var layout = new InstallLayout(folder);
        File.WriteAllText(Path.Combine(layout.ContentDirectory, "saved-by-user.txt"), "keep");
        Fixtures.Publish(server, "0.2.0", "new");
        var offer = UpdateOffer.Evaluate(layout.InstalledVersion, Run(engine.CheckLatestAsync()).Release);
        Check.That(offer.IsAvailable && offer.Banner == "Version 0.2.0 is available (you have 0.1.0).", "the banner says it as the owner wished: " + offer.Banner);
        Check.That(Run(engine.InstallAsync(folder, offer.Release!, false)).Succeeded, "updated");
        Check.Equal("client new", File.ReadAllText(layout.ClientExe), "the program is the new one");
        Check.Equal(new SemanticVersion(0, 2, 0), layout.InstalledVersion, "the record is new");
        Check.Equal("keep", File.ReadAllText(Path.Combine(layout.ContentDirectory, "saved-by-user.txt")), "content is kept");
        Check.Equal(2, patcher.Calls.Count, "the patcher ran again to check the content against the new version");
        Check.That(!UpdateOffer.Evaluate(layout.InstalledVersion, Run(engine.CheckLatestAsync()).Release).IsAvailable, "no banner once up to date");
    }

    public static void Repair()
    {
        var (engine, server, patcher, _) = Fixtures.Engine();
        Fixtures.Publish(server, "0.1.0", "v1");
        var folder = Fixtures.Temp("rep");
        Run(engine.InstallAsync(folder, Run(engine.CheckLatestAsync()).Release!, false));
        var layout = new InstallLayout(folder);
        patcher.Calls.Clear();

        server.Requests.Clear();
        Check.That(Run(engine.RepairAsync(folder)).Succeeded, "repair of a whole install");
        Check.That(!server.Requests.Any(url => url.Contains("/dl/", StringComparison.Ordinal)), "nothing is downloaded when nothing is damaged");
        Check.Equal(1, patcher.Calls.Count, "the patcher still checks the game files");

        File.WriteAllText(layout.ClientExe, "virus scanner ate it");
        server.Requests.Clear();
        Check.That(Run(engine.RepairAsync(folder)).Succeeded, "repair of a damaged client");
        Check.Equal("client v1", File.ReadAllText(layout.ClientExe), "the same version is fetched again");
        Check.That(server.Requests.Any(url => url.Contains("/releases/tags/v0.1.0", StringComparison.Ordinal)), "by its tag, not the newest release");

        var (broken, _, failing, _) = Fixtures.Engine(new FakePatcher(1, ["The installation differs from the master:", "  changed: Data/INI/Object.ini"]));
        var folder2 = Fixtures.Temp("rep2");
        new InstallLayout(folder2).WriteRelease(new ReleaseRecord("0.1.0", "v0.1.0", DateTimeOffset.UtcNow));
        Directory.CreateDirectory(Path.Combine(folder2, "bin"));
        var gone = Run(broken.RepairAsync(folder2));
        Check.That(!gone.Succeeded && gone.Message.Contains("could not be fetched again", StringComparison.Ordinal), "damage with no way to fetch is said plainly");

        var (engine3, server3, _, _) = Fixtures.Engine(new FakePatcher(1, ["The installation differs from the master:", "  changed: Data/INI/Object.ini"]));
        Fixtures.Publish(server3, "0.1.0", "v1");
        var folder3 = Fixtures.Temp("rep3");
        var install = Run(engine3.InstallAsync(folder3, Run(engine3.CheckLatestAsync()).Release!, false));
        Check.That(!install.Succeeded && install.FailedStep == StepId.MakeGame && install.Message.Contains("not the version this project supports", StringComparison.Ordinal) && install.Message.Contains("changed: Data/INI/Object.ini", StringComparison.Ordinal),
            "a Steam copy that is not the supported one is explained, with the patcher's lines: " + install.Message);
    }

    public static void SimulationCheck()
    {
        var (engine, server, _, _) = Fixtures.Engine();
        Fixtures.Publish(server, "0.1.0", "v1");
        var folder = Fixtures.Temp("sim");
        var events = new List<EngineProgress>();
        engine.Progress += events.Add;
        Check.That(Run(engine.InstallAsync(folder, Run(engine.CheckLatestAsync()).Release!, verifySimulation: false)).Succeeded, "install");
        Check.That(events.Any(e => e.Step == StepId.Verify && e.State == StepState.Skipped && e.Detail.StartsWith("Off", StringComparison.Ordinal)), "off by default: the step is skipped");
        events.Clear();
        var layout = new InstallLayout(folder);
        Check.That(!layout.HasSimulationCheck, "the package has no check tool");
        Check.That(Run(engine.VerifyAsync(layout, requested: true)).Succeeded, "requested without a tool is no failure");
        Check.That(events.Any(e => e.Step == StepId.Verify && e.State == StepState.Skipped && e.Detail.Contains("does not include", StringComparison.Ordinal)), "and says the package lacks it");

        events.Clear();
        Directory.CreateDirectory(layout.SimulationCheckBaselines);
        File.WriteAllText(layout.SimulationCheckExe, "tool");
        var recording = new RecordingRunner(failSecond: true);
        var checker = new LauncherEngine(new HttpClient(), recording, Fixtures.Temp("d"), new ReleaseClient(new HttpClient()));
        checker.Progress += events.Add;
        Check.That(Run(checker.VerifyAsync(layout, requested: true)).Succeeded, "a difference is a finding, not a failure");
        Check.Equal(2, recording.Calls.Count, "both comparisons ran");
        Check.That(recording.Calls[0].Contains("input-manifest check", StringComparison.Ordinal) && recording.Calls[1].Contains("--set quick", StringComparison.Ordinal), "the input manifest, then the quick reference set, as the developer launcher does");
        Check.That(events[^1].Headline == "Simulation differs" && events[^1].Detail.Contains("nine reference matches", StringComparison.Ordinal), "the difference is shown");
    }

    private sealed class RecordingRunner(bool failSecond) : IToolRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onLine, CancellationToken cancellation)
        {
            Calls.Add(string.Join(' ', arguments));
            return Task.FromResult(new ToolResult(failSecond && Calls.Count == 2 ? 1 : 0, []));
        }
    }

    public static void Uninstall()
    {
        var (engine, server, _, _) = Fixtures.Engine();
        Fixtures.Publish(server, "0.1.0", "v1");
        var folder = Fixtures.Temp("un");
        Run(engine.InstallAsync(folder, Run(engine.CheckLatestAsync()).Release!, false));
        var stranger = Fixtures.Temp("stranger");
        Directory.CreateDirectory(stranger);
        File.WriteAllText(Path.Combine(stranger, "mine.txt"), "x");
        var refused = engine.Uninstall(stranger);
        Check.That(!refused.Succeeded && File.Exists(Path.Combine(stranger, "mine.txt")), "a folder the launcher did not make is not removed");
        var removed = engine.Uninstall(folder);
        Check.That(removed.Succeeded && !Directory.Exists(folder), "ours is removed");
        Check.Contains(removed.Message, "saved games", "and says what stays");
        var running = Fixtures.Engine(running: _ => true);
        new InstallLayout(stranger).WriteRelease(new ReleaseRecord("0.1.0", "v0.1.0", DateTimeOffset.UtcNow));
        Check.That(!running.Engine.Uninstall(stranger).Succeeded && Directory.Exists(stranger), "not while the game runs");
    }

    public static void UpdateOffers()
    {
        ReleaseInfo Release(string version) => ReleaseClient.Parse(Fixtures.ReleaseJson(version)).Release!;
        var v1 = SemanticVersion.Parse("1.0.0");
        Check.That(UpdateOffer.Evaluate(v1, Release("1.1.0")).IsAvailable, "newer");
        Check.That(!UpdateOffer.Evaluate(v1, Release("1.0.0")).IsAvailable, "equal");
        Check.That(!UpdateOffer.Evaluate(v1, Release("0.9.0")).IsAvailable, "older");
        Check.That(!UpdateOffer.Evaluate(v1, Release("1.1.0"), skipped: "1.1.0").IsAvailable, "skipped");
        Check.That(UpdateOffer.Evaluate(v1, Release("1.2.0"), skipped: "1.1.0").IsAvailable, "a skip does not cover later versions");
        Check.That(!UpdateOffer.Evaluate(v1, Release("1.1.0-rc.1")).IsAvailable, "release candidates are not offered to a release");
        Check.That(UpdateOffer.Evaluate(SemanticVersion.Parse("1.1.0-rc.1"), Release("1.1.0-rc.2")).IsAvailable, "but are to a candidate");
        Check.That(!UpdateOffer.Evaluate(null, Release("1.1.0")).IsAvailable && !UpdateOffer.Evaluate(v1, null).IsAvailable, "nothing installed or no answer: no banner");
    }

    public static void Steps()
    {
        var steps = new StepList(includeVerify: true);
        Check.Equal(6, steps.Steps.Count, "six steps with the optional check");
        Check.Equal(5, new StepList(false).Steps.Count, "five without");
        Check.That(steps.Current is null, "nothing running yet");
        steps.Apply(new EngineProgress(StepId.MakeGame, StepState.Running, "Making", "40 %"));
        Check.That(steps[StepId.Checks].State == StepState.Done && steps[StepId.Download].State == StepState.Done, "earlier steps count as done");
        Check.Equal(StepId.MakeGame, steps.Current?.Id, "the current one");
        steps.Fail(StepId.MakeGame, "boom");
        Check.That(steps.Current?.State == StepState.Failed && steps.Current.Detail == "boom", "a failure is the current step");
        var changes = 0;
        steps.Changed += _ => changes++;
        steps.MarkAllDone();
        Check.That(steps[StepId.Play].State == StepState.Done && changes > 0, "all done");
    }

    public static void SelfUpdating()
    {
        var server = new FakeServer();
        var directory = Fixtures.Temp("self");
        Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes("the new launcher");
        Fixtures.Publish(server, "0.2.0", "x", launcher: bytes);
        var http = new HttpClient(server);
        var release = Run(new ReleaseClient(http, apiBase: FakeServer.Base).GetLatestAsync()).Release!;
        var (path, message) = Run(SelfUpdate.FetchAsync(release, directory, http, null, default));
        Check.That(path is not null && File.ReadAllBytes(path).SequenceEqual(bytes) && message == "", "fetched and verified");
        Check.Equal("CnCNew-Launcher-0.2.0.exe", Path.GetFileName(path), "named by the release");

        Fixtures.Publish(server, "0.3.0", "x", launcher: bytes);
        server.ServeText("/dl/0.3.0/launcher.sha256", new string('0', 64) + "  CnCNew-Launcher-0.3.0.exe\n");
        var bad = Run(new ReleaseClient(http, apiBase: FakeServer.Base).GetLatestAsync()).Release!;
        var (badPath, badMessage) = Run(SelfUpdate.FetchAsync(bad, directory, http, null, default));
        Check.That(badPath is null && badMessage.Contains("does not match", StringComparison.Ordinal) && !File.Exists(Path.Combine(directory, "CnCNew-Launcher-0.3.0.exe")), "a wrong hash is deleted");

        var old = Path.Combine(directory, "old.exe");
        File.WriteAllText(old, "old");
        Check.That(SelfUpdate.RemovePredecessor(old, 2, 1) && !File.Exists(old), "the old file is removed");
        using (new FileStream(old, FileMode.Create, FileAccess.Write, FileShare.None))
            Check.That(!SelfUpdate.RemovePredecessor(old, 2, 1), "a file still in use is given up on, not forced");
        Check.That(SelfUpdate.RemovePredecessor(Path.Combine(directory, "never-existed.exe"), 1, 1), "nothing to remove is fine");
    }

    public static void LayerRules()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CnCNew.slnx"))) root = Path.GetDirectoryName(root);
        Check.That(root is not null, "the repository root was found");
        var core = Path.Combine(root!, "tools", "CnCNew.Launcher.Core");
        var window = Path.Combine(root!, "tools", "CnCNew.Launcher");
        var coreProject = File.ReadAllText(Path.Combine(core, "CnCNew.Launcher.Core.csproj"));
        Check.That(!coreProject.Contains("ProjectReference", StringComparison.Ordinal) && !coreProject.Contains("PackageReference", StringComparison.Ordinal), "the core references no project and no package");
        var windowProject = File.ReadAllText(Path.Combine(window, "CnCNew.Launcher.csproj"));
        var references = Regex.Matches(windowProject, "<ProjectReference Include=\"([^\"]+)\"").Select(match => match.Groups[1].Value.Replace('\\', '/')).ToArray();
        Check.That(references.Length == 1 && references[0].EndsWith("CnCNew.Launcher.Core/CnCNew.Launcher.Core.csproj", StringComparison.Ordinal), "the window references only the core: " + string.Join(",", references));
        Check.That(!windowProject.Contains("PackageReference", StringComparison.Ordinal), "the window references no package");
        var forbidden = new Regex(@"\b(CnCNew\.(Simulation|Data|Game|Net|Server|Content|Import|Archives|IO|Formats|Images|Presentation|Ui|Audio|Video|W3D|Client)|Stride)\b");
        foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(window, "*.cs", SearchOption.AllDirectories)).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            Check.That(!forbidden.IsMatch(File.ReadAllText(file)), $"{Path.GetFileName(file)} names a game layer");
        foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.TopDirectoryOnly))
            Check.That(!File.ReadAllText(file).Contains("System.Windows", StringComparison.Ordinal), $"{Path.GetFileName(file)} (core) uses WPF");
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
