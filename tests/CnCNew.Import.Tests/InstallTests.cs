using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Content;
using CnCNew.Import;

/// <summary>
/// The patcher: master description, installation check, install, repair and uninstall on a tiny synthetic installation built in code
/// (a hand-built BIG archive and loose files); with <c>--game-dir</c> additionally the real master against the built-in description (reads only).
/// </summary>
internal static class InstallTests
{
    public static async Task RunAsync(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), $"CnCNew-install-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root, "steam", "Fake Game");
            CreateInstallation(game);
            var master = await MasterDescriptionIsCanonical(game, root);
            await DifferencesAreNamed(game, master, root);
            await RefusedTargetsAreNotWritten(game, master, root);
            await InstallRepairAndUninstall(game, master, root);
            await PatchIsFetchedChecked(game, master, root);
            BuiltInMasterLoads();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        Console.WriteLine("All installation tests passed.");

        var gameDirectory = Option(args, "--game-dir");
        if (gameDirectory is not null) await RealMaster(gameDirectory);
    }

    private static async Task<InstallationMaster> MasterDescriptionIsCanonical(string game, string root)
    {
        var master = await InstallationInspector.DescribeAsync(game, "Fake Game, Test", "fake-master");
        var first = InstallationMasterSerializer.Write(master);
        var second = InstallationMasterSerializer.Write(await InstallationInspector.DescribeAsync(game, "Fake Game, Test", "fake-master"));
        Assert(first.SequenceEqual(second), "Two descriptions of the same directory differ.");
        var text = Encoding.UTF8.GetString(first);
        Assert(first[0] == (byte)'{' && text.EndsWith("}\n", StringComparison.Ordinal) && !text.Contains('\r'), "The description is not UTF-8 without BOM with LF line ends.");
        Assert(!text.Contains('\\') && !text.Contains(root, StringComparison.OrdinalIgnoreCase) && !text.Contains(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase),
            "The description contains a backslash or a machine path.");
        Assert(master.SteamAppId == 4242, "The Steam app id was not read from steam_appid.txt.");
        Assert(master.Files.Select(file => file.Path).SequenceEqual(["Art/Textures/Test.dds", "Fixture.big", "Readme.txt", "alpha/lower.txt", "steam_appid.txt"]),
            "Paths are not relative, forward-slashed and in ordinal order: " + string.Join(", ", master.Files.Select(file => file.Path)));
        Assert(master.FileCount == 5 && master.TotalBytes == master.Files.Sum(file => file.Bytes) && master.HarmlessExtraFiles.Count == 0, "Counts of the description are wrong.");
        Assert(master.Files.All(file => file.Sha256.Length == 64 && file.Sha256 == file.Sha256.ToLowerInvariant()), "Hashes must be lower-case SHA-256.");
        Assert(InstallationMasterSerializer.Write(InstallationMasterSerializer.Read(first)).SequenceEqual(first), "The description does not survive reading and writing.");

        // The recorded export is exactly what a real import publishes.
        var imported = Path.Combine(root, "plain-import");
        await RuntimeContentImporter.ImportAsync(game, imported);
        var manifest = await File.ReadAllBytesAsync(Path.Combine(imported, "content-manifest.json"));
        Assert(master.ExpectedExport.ContentManifestBytes == manifest.LongLength && master.ExpectedExport.ContentManifestSha256 == Hex(manifest) &&
               master.ExpectedExport.RuntimeFileCount == 5, "The recorded export differs from the import (the archive entry and four loose files).");

        Throws<InvalidDataException>(() => InstallationMasterSerializer.Read(Encoding.UTF8.GetBytes(text.Replace("\"id\"", "\"identity\"", StringComparison.Ordinal))), "unknown property");
        Throws<InvalidDataException>(() => InstallationMasterSerializer.Read(Encoding.UTF8.GetBytes(text.Replace("\"fileCount\": 5", "\"fileCount\": 6", StringComparison.Ordinal))), "wrong count");
        var noAppId = Path.Combine(root, "no-app-id");
        Directory.CreateDirectory(noAppId);
        File.WriteAllText(Path.Combine(noAppId, "Readme.txt"), "x");
        await ThrowsAsync<InvalidDataException>(() => InstallationInspector.DescribeAsync(noAppId), "missing steam_appid.txt");
        return master;
    }

    private static async Task DifferencesAreNamed(string game, InstallationMaster master, string root)
    {
        var identical = await InstallationInspector.CheckAsync(game, master);
        Assert(identical.IsIdentical && identical.DifferenceCount == 0 && identical.CheckedFiles == 5, "The described directory is not identical to its own description.");
        Assert(identical.FormatReport().Contains("every size and SHA-256 matches", StringComparison.Ordinal), "The report of an identical directory is wrong.");

        var modded = Path.Combine(root, "modded");
        CopyDirectory(game, modded);
        File.Delete(Path.Combine(modded, "alpha", "lower.txt"));                                       // missing
        File.Move(Path.Combine(modded, "Fixture.big"), Path.Combine(modded, "Fixture.big.bak"));     // renamed
        File.WriteAllText(Path.Combine(modded, "Readme.txt"), "A changed text.");                    // same size, other content
        File.WriteAllBytes(Path.Combine(modded, "Art", "Textures", "Test.dds"), new byte[7]);        // other size
        File.WriteAllBytes(Path.Combine(modded, "900_Mod.big"), [1, 2, 3]);                          // extra
        File.WriteAllText(Path.Combine(modded, "steam.log"), "log");                                 // extra, harmless only by rule

        var result = await InstallationInspector.CheckAsync(modded, master);
        Assert(!result.IsIdentical && result.DifferenceCount == 6, $"Expected six differences, found {result.DifferenceCount}.");
        Assert(result.Missing.Select(file => file.Path).SequenceEqual(["alpha/lower.txt"]), "Missing file not detected.");
        Assert(result.Renamed.SequenceEqual([new RenamedFile("Fixture.big", "Fixture.big.bak")]), "Renamed file not detected.");
        Assert(result.Changed.Select(file => file.Path).SequenceEqual(["Art/Textures/Test.dds", "Readme.txt"]), "Changed files not detected.");
        Assert(result.Changed[0].ActualSha256 is null && result.Changed[0].ActualBytes == 7, "A size difference must be reported as such.");
        Assert(result.Changed[1].ActualSha256 is { Length: 64 } && result.Changed[1].ActualBytes == result.Changed[1].ExpectedBytes, "A content difference at equal size must carry the hash.");
        Assert(result.Extra.Select(file => file.Path).SequenceEqual(["900_Mod.big", "steam.log"]) && result.HarmlessExtra.Count == 0, "Extra files not detected.");
        var report = result.FormatReport();
        foreach (var expected in new[] { "missing: alpha/lower.txt", "renamed: Fixture.big is now Fixture.big.bak", "changed: Readme.txt", "changed: Art/Textures/Test.dds",
                     "extra:   900_Mod.big", "extra:   steam.log", "Verify integrity of game files", "Remove mods", "6 difference(s)" })
            Assert(report.Contains(expected, StringComparison.Ordinal), $"The report does not say '{expected}':\n{report}");

        // Harmless only when the master lists the file explicitly, and then it is counted.
        var lenient = master with { HarmlessExtraFiles = ["steam.log"] };
        var counted = await InstallationInspector.CheckAsync(modded, lenient);
        Assert(counted.DifferenceCount == 5 && counted.HarmlessExtra.Select(file => file.Path).SequenceEqual(["steam.log"]), "The explicit harmless list is not honoured or not counted.");
        File.WriteAllText(Path.Combine(game, "steam.log"), "log");
        var harmless = await InstallationInspector.CheckAsync(game, lenient);
        Assert(harmless.IsIdentical && harmless.HarmlessExtra.Count == 1 && harmless.FormatReport().Contains("harmless: steam.log", StringComparison.Ordinal), "A listed harmless file must not be a difference.");
        Assert(!(await InstallationInspector.CheckAsync(game, master)).IsIdentical, "An unlisted extra file must be a difference.");
        File.Delete(Path.Combine(game, "steam.log"));

        var refusedTarget = Path.Combine(root, "refused-modded");
        var refused = await GameInstaller.InstallAsync(modded, refusedTarget, master);
        Assert(refused.Outcome == InstallOutcome.InstallationDiffers && !refused.Succeeded && refused.Check is { IsIdentical: false } &&
               refused.Message.Contains("Verify integrity", StringComparison.Ordinal), "A different installation must be refused with the report.");
        Assert(!Directory.Exists(refusedTarget), "A refused installation must not create the target.");
    }

    private static async Task RefusedTargetsAreNotWritten(string game, InstallationMaster master, string root)
    {
        var before = Snapshot(game);
        var inside = await GameInstaller.InstallAsync(game, Path.Combine(game, "CnCNew"), master);
        Assert(inside.Outcome == InstallOutcome.TargetRefused && inside.Message.Contains("inside", StringComparison.Ordinal), "A target inside the game directory must be refused.");
        var same = await GameInstaller.InstallAsync(game, game + Path.DirectorySeparatorChar, master);
        Assert(same.Outcome == InstallOutcome.TargetRefused, "The game directory itself must be refused as target.");
        var around = await GameInstaller.InstallAsync(game, Path.Combine(root, "steam"), master);
        Assert(around.Outcome == InstallOutcome.TargetRefused && around.Message.Contains("contains the game directory", StringComparison.Ordinal), "A target containing the game directory must be refused.");
        Assert(Snapshot(game).SequenceEqual(before), "A refused target changed the game directory.");

        var foreign = Path.Combine(root, "foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "holiday.txt"), "mine");
        File.WriteAllText(Path.Combine(foreign, "install.json"), "{ \"product\": \"SomethingElse\" }");
        var notOurs = await GameInstaller.InstallAsync(game, foreign, master);
        Assert(notOurs.Outcome == InstallOutcome.TargetRefused && Directory.GetFileSystemEntries(foreign).Length == 2, "A non-empty directory that is not ours must be refused untouched.");
        var removal = GameInstaller.Uninstall(foreign);
        Assert(!removal.Removed && File.Exists(Path.Combine(foreign, "holiday.txt")), "Uninstall must not touch a directory without our record.");
        Assert(!GameInstaller.Uninstall(game).Removed && Snapshot(game).SequenceEqual(before), "Uninstall must not touch the game directory.");
    }

    private static async Task InstallRepairAndUninstall(string game, InstallationMaster master, string root)
    {
        var before = Snapshot(game);
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target); // an empty directory is acceptable
        var installed = await GameInstaller.InstallAsync(game, target, master);
        Assert(installed.Outcome == InstallOutcome.Installed && installed.Succeeded && installed.CopiedFiles == 5, $"Fresh installation failed: {installed.Outcome} {installed.Message}");
        Assert(Snapshot(game).SequenceEqual(before), "The installation changed the game directory (file list, sizes or timestamps).");

        var content = Path.Combine(target, GameInstaller.ContentDirectoryName);
        await ContentFileSystem.Open(content).VerifyIntegrityAsync();
        Assert(Hex(File.ReadAllBytes(Path.Combine(content, "content-manifest.json"))) == master.ExpectedExport.ContentManifestSha256, "The installed manifest is not the recorded one.");
        Assert(!Directory.EnumerateFiles(target, "*.big", SearchOption.AllDirectories).Any(), "No archive may be taken over.");
        Assert(File.Exists(Path.Combine(content, "Window", "Test.wnd")), "The archive entry does not lie plain in the content.");
        Assert((await GameInstaller.VerifyExportAsync(content, master)).Count == 0, "The installed content is not the known export.");

        var recordBytes = File.ReadAllBytes(Path.Combine(target, InstallRecord.FileName));
        var recordText = Encoding.UTF8.GetString(recordBytes);
        Assert(recordText.EndsWith("}\n", StringComparison.Ordinal) && !recordText.Contains('\r') && !recordText.Contains('\\') &&
               !recordText.Contains(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase), "The install record is not canonical or contains a machine path.");
        var record = InstallRecord.TryRead(target) ?? throw new InvalidOperationException("The install record cannot be read.");
        Assert(record == installed.Record && record.Verified && record.Product == "CnCNew" && record.MasterId == "fake-master" && record.SteamAppId == 4242 &&
               record.RuntimeFileCount == 5 && record.ContentManifestSha256 == master.ExpectedExport.ContentManifestSha256 &&
               record.ProductVersion.Length != 0 && record.ProductCommit.Length != 0, "The install record is wrong: " + recordText);
        Assert(record.Write().SequenceEqual(recordBytes), "The install record does not survive reading and writing.");

        // Idempotent: a second run verifies and writes nothing into the content.
        var contentBefore = Snapshot(content);
        var again = await GameInstaller.InstallAsync(game, target, master);
        Assert(again.Outcome == InstallOutcome.AlreadyComplete && again.CopiedFiles == 0, $"The second run was not idempotent: {again.Outcome}");
        Assert(Snapshot(content).SequenceEqual(contentBefore) && File.ReadAllBytes(Path.Combine(target, InstallRecord.FileName)).SequenceEqual(recordBytes),
            "The second run changed the installation.");

        // Repair: a missing file, a changed file of equal size and a foreign file; an own file beside the content stays.
        File.Delete(Path.Combine(content, "Readme.txt"));
        var texture = Path.Combine(content, "Art", "Textures", "Test.dds");
        var damaged = File.ReadAllBytes(texture);
        damaged[^1] ^= 0xFF;
        File.WriteAllBytes(texture, damaged);
        File.WriteAllText(Path.Combine(content, "stray.tmp"), "stray");
        File.WriteAllText(Path.Combine(target, "notes.txt"), "kept");
        var repaired = await GameInstaller.InstallAsync(game, target, master);
        Assert(repaired.Outcome == InstallOutcome.Repaired && repaired.MissingRuntimeFiles.SequenceEqual(["Readme.txt"]) &&
               repaired.ChangedRuntimeFiles.SequenceEqual(["Art/Textures/Test.dds"]) && repaired.ExtraRuntimeFiles.SequenceEqual(["stray.tmp"]) &&
               repaired.CopiedFiles == 2 && repaired.ReusedFiles == 3, $"Repair reported wrongly: {repaired.Message}");
        Assert((await GameInstaller.VerifyExportAsync(content, master)).Count == 0 && !File.Exists(Path.Combine(content, "stray.tmp")), "The repair did not restore the known export.");
        Assert((await GameInstaller.InstallAsync(game, target, master)).Outcome == InstallOutcome.AlreadyComplete, "A repaired installation must be complete.");

        // An interrupted installation (unverified record, unusable manifest) is ours and is completed.
        File.WriteAllBytes(Path.Combine(target, InstallRecord.FileName), (record with { Verified = false }).Write());
        File.WriteAllText(Path.Combine(content, "content-manifest.json"), "{");
        Assert((await GameInstaller.VerifyExportAsync(content, master)).Count != 0, "A broken manifest must be a problem.");
        var resumed = await GameInstaller.InstallAsync(game, target, master);
        Assert(resumed.Outcome == InstallOutcome.Repaired && resumed.CopiedFiles == 5 && InstallRecord.TryRead(target) is { Verified: true }, "An interrupted installation was not completed.");
        Assert(Snapshot(game).SequenceEqual(before), "Repairing changed the game directory.");

        // A master written for another importer version is not applied.
        var stale = master with { ExpectedExport = master.ExpectedExport with { ImporterVersion = "0.0.0" } };
        await ThrowsAsync<InvalidDataException>(() => GameInstaller.InstallAsync(game, Path.Combine(root, "stale"), stale), "stale importer version");

        Directory.CreateDirectory(Path.Combine(target, "bin"));
        File.WriteAllText(Path.Combine(target, "bin", "client.dll"), "client");
        File.WriteAllText(Path.Combine(target, "Start-CnCNew.cmd"), "start");
        var removed = GameInstaller.Uninstall(target);
        Assert(removed.Removed && removed.Leftovers.SequenceEqual(["notes.txt"]) && Directory.GetFileSystemEntries(target).Length == 1, "Uninstall must remove our entries and keep the rest.");
        Assert(!GameInstaller.Uninstall(target).Removed, "A directory without record must not be uninstalled twice.");

        var clean = Path.Combine(root, "clean");
        Assert((await GameInstaller.InstallAsync(game, clean, master)).Outcome == InstallOutcome.Installed, "Installing into a new directory failed.");
        Assert(GameInstaller.Uninstall(clean).Removed && !Directory.Exists(clean), "Uninstall must remove an installation that holds nothing else.");
        Assert(Snapshot(game).SequenceEqual(before), "The game directory changed.");
    }

    // The patch's files come from a commit or a checkout (docs/PATCH-LAYER.md); whichever way, a file is taken only with its recorded size and hash.
    private static async Task PatchIsFetchedChecked(string game, InstallationMaster master, string root)
    {
        var blob = "the patched text\nits second line\n"u8.ToArray();
        var file = new MasterFile("alpha/lower.txt", blob.LongLength, Hex(blob));
        var fetched = 0;
        Task<byte[]> Serve(Uri address, CancellationToken cancellation)
        {
            Interlocked.Increment(ref fetched);
            Assert(address.AbsoluteUri == "https://raw.githubusercontent.com/owner/patch/abc/edited/alpha/lower.txt", $"The file is asked for at {address}.");
            return Task.FromResult(blob);
        }

        var hollow = new PatchMaster("owner/patch", "abc", "edited", master.ExpectedExport, [file]);
        var layer = await new PatchFetcher(hollow, Serve).FetchAsync(Path.Combine(root, "patch-listing"));
        var described = await RuntimeContentImporter.DescribeAsync(game, layer: layer);
        var bytes = RuntimeManifestSerializer.WriteCanonical(described);
        var patch = hollow with { ExpectedExport = new MasterExport(RuntimeContentImporter.ImporterName, RuntimeContentImporter.ImporterVersion, bytes.LongLength, Hex(bytes), described.Assets.Count) };
        Assert(PatchMaster.Read(patch.Write()).Write().SequenceEqual(patch.Write()), "The patch description does not survive reading and writing.");
        Throws<InvalidDataException>(() => PatchMaster.Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(patch.Write()).Replace("\"commit\"", "\"revision\"", StringComparison.Ordinal))), "a renamed property of the patch description");

        fetched = 0;
        var target = Path.Combine(root, "patched-install");
        var installed = await GameInstaller.InstallAsync(game, target, master, patch: new PatchInstall(patch, Download: Serve));
        Assert(installed.Outcome == InstallOutcome.Installed && fetched == 1, "The installation fetches the patch's file.");
        var content = Path.Combine(target, GameInstaller.ContentDirectoryName);
        Assert(File.ReadAllBytes(Path.Combine(content, "alpha", "lower.txt")).SequenceEqual(blob), "The patch's file is the content.");
        Assert(!Directory.Exists(Path.Combine(target, GameInstaller.PatchDirectoryName)), "The patch's files are gone when the installation is verified.");
        Assert(InstallRecord.TryRead(target)?.PatchSource == "owner/patch abc", "The record names the patch.");
        Assert((await GameInstaller.VerifyExportAsync(content, patch.ExpectedExport)).Count == 0 && (await GameInstaller.VerifyExportAsync(content, master)).Count != 0, "The export is the patched one, not the plain one.");

        var again = await GameInstaller.InstallAsync(game, target, master, patch: new PatchInstall(patch, Download: Serve));
        Assert(again.Outcome == InstallOutcome.AlreadyComplete && fetched == 1, "An intact installation is verified without the network.");

        // A checkout may hold CRLF where the blob has LF, and another text is never taken from it.
        var checkout = Path.Combine(root, "patch-checkout", "alpha");
        Directory.CreateDirectory(checkout);
        File.WriteAllBytes(Path.Combine(checkout, "lower.txt"), Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(blob).Replace("\n", "\r\n", StringComparison.Ordinal)));
        File.Delete(Path.Combine(content, "alpha", "lower.txt"));
        var repaired = await GameInstaller.InstallAsync(game, target, master, patch: new PatchInstall(patch, Path.Combine(root, "patch-checkout"), Serve));
        Assert(repaired.Outcome == InstallOutcome.Repaired && fetched == 1 && File.ReadAllBytes(Path.Combine(content, "alpha", "lower.txt")).SequenceEqual(blob), "A checkout with CRLF gives the blob.");
        File.WriteAllText(Path.Combine(checkout, "lower.txt"), "another text");
        File.Delete(Path.Combine(content, "alpha", "lower.txt"));
        await GameInstaller.InstallAsync(game, target, master, patch: new PatchInstall(patch, Path.Combine(root, "patch-checkout"), Serve));
        Assert(fetched == 2, "A checkout with another text is passed over for the commit.");

        // A file that is not the recorded one ends the installation, and the installation stays unverified.
        var refused = Path.Combine(root, "patched-refused");
        await ThrowsAsync<InvalidDataException>(() => GameInstaller.InstallAsync(game, refused, master, patch: new PatchInstall(patch, Download: (_, _) => Task.FromResult(new byte[] { 1, 2, 3 }))), "a tampered patch file");
        Assert(InstallRecord.TryRead(refused)?.Verified == false, "A refused patch leaves no verified installation.");

        // A directory of the time before the patch is ours: the next installation brings it up to the patched content.
        var before = Path.Combine(root, "patched-schema-1");
        await GameInstaller.InstallAsync(game, before, master);
        var record = File.ReadAllText(Path.Combine(before, InstallRecord.FileName));
        File.WriteAllText(Path.Combine(before, InstallRecord.FileName), record.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal).Replace(",\n  \"patchSource\": \"\"", "", StringComparison.Ordinal));
        Assert(InstallRecord.TryRead(before) is { PatchSource: "" }, "A record of schema 1 is read.");
        Assert((await GameInstaller.InstallAsync(game, before, master, patch: new PatchInstall(patch, Download: Serve))).Outcome == InstallOutcome.Repaired, "It is brought up to the patched content.");
    }

    private static void BuiltInMasterLoads()
    {
        var master = InstallationMaster.LoadBuiltIn();
        Assert(master.FileCount > 0 && master.SteamAppId > 0 && master.Name == InstallationMaster.DefaultName && master.HarmlessExtraFiles.Count == 0,
            "The built-in master description is not the expected one.");
        Assert(master.Files.Any(file => file.Path == InstallationInspector.SteamAppIdFile), "The built-in master lacks steam_appid.txt.");
        var patch = PatchMaster.LoadBuiltIn();
        Assert(patch.Files.Count > 0 && patch.Commit.Length == 40 && patch.ExpectedExport.RuntimeFileCount > master.ExpectedExport.RuntimeFileCount, "The built-in patch description is not the expected one.");
    }

    // Reads the real installation only: every file against the built-in description, and the dry-run export against the recorded manifest.
    private static async Task RealMaster(string gameDirectory)
    {
        var master = InstallationMaster.LoadBuiltIn();
        var before = Snapshot(gameDirectory);
        var check = await InstallationInspector.CheckAsync(gameDirectory, master);
        Assert(check.IsIdentical, "The game directory is not the built-in master:\n" + check.FormatReport());
        var described = await InstallationInspector.DescribeAsync(gameDirectory);
        Assert(described.ExpectedExport == master.ExpectedExport,
            $"The export of the master is {described.ExpectedExport}, recorded is {master.ExpectedExport}.");
        Assert(InstallationMasterSerializer.Write(described).SequenceEqual(InstallationMasterSerializer.Write(master)), "Describing the master does not reproduce baselines/steam-master.json.");
        Assert(Snapshot(gameDirectory).SequenceEqual(before), "Reading the game directory changed it.");
        Console.WriteLine($"Real master: {master.FileCount} files, {master.TotalBytes} bytes identical; export {master.ExpectedExport.RuntimeFileCount} runtime files, " +
                          $"content manifest {master.ExpectedExport.ContentManifestSha256} reproduced.");
    }

    private static void CreateInstallation(string game)
    {
        Directory.CreateDirectory(Path.Combine(game, "Art", "Textures"));
        Directory.CreateDirectory(Path.Combine(game, "alpha"));
        File.WriteAllBytes(Path.Combine(game, "steam_appid.txt"), "4242\n\0"u8.ToArray());
        File.WriteAllText(Path.Combine(game, "Readme.txt"), "A synthetic xx.");
        File.WriteAllText(Path.Combine(game, "alpha", "lower.txt"), "ordinal order puts lower case after upper case");
        File.WriteAllBytes(Path.Combine(game, "Art", "Textures", "Test.dds"), CreateDds(1));
        CreateArchive(Path.Combine(game, "Fixture.big"), "Window/Test.wnd", "FILE_VERSION = 2;\nWINDOW { IMAGE = Art/Textures/Test.dds; }"u8.ToArray());
    }

    private static void CreateArchive(string path, string entryPath, byte[] payload)
    {
        var encodedPath = Encoding.Latin1.GetBytes(entryPath);
        var directoryEnd = 16 + 8 + encodedPath.Length + 1;
        using var stream = File.Create(path);
        stream.Write("BIGF"u8);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(number, checked((uint)(directoryEnd + payload.Length)));
        stream.Write(number);
        BinaryPrimitives.WriteUInt32BigEndian(number, 1);
        stream.Write(number);
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)directoryEnd));
        stream.Write(number);
        stream.Write(number);
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)payload.Length));
        stream.Write(number);
        stream.Write(encodedPath);
        stream.WriteByte(0);
        stream.Write(payload);
    }

    private static byte[] CreateDds(byte fill)
    {
        var data = Enumerable.Repeat(fill, 136).ToArray();
        "DDS "u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 32);
        "DXT1"u8.CopyTo(data.AsSpan(84));
        return data;
    }

    // File list, sizes and write/creation times: what "untouched" means for the source directory.
    private static string[] Snapshot(string directory)
        => Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => File.Exists(path)
                ? $"{Path.GetRelativePath(directory, path)}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}|{File.GetCreationTimeUtc(path).Ticks}|{File.GetAttributes(path)}"
                : $"{Path.GetRelativePath(directory, path)}|dir|{Directory.GetCreationTimeUtc(path).Ticks}")
            .ToArray();

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        return index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"{name} needs a value.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action, string what) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {what}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string what) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {what}.");
    }
}
