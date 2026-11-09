using System.Diagnostics;

namespace CnCNew.Launcher;

/// <summary>
/// The launcher replacing itself. A running exe cannot be overwritten, so the new version is downloaded next to the old one, checked against its published hash and
/// started with <c>--replace &lt;old exe&gt;</c>; it waits for the old process to end and deletes the old file.
/// </summary>
public static class SelfUpdate
{
    public const string ReplaceArgument = "--replace";

    /// <summary>Downloads and verifies the release's launcher into <paramref name="directory"/>. Null with a message when the release has none or it does not verify.</summary>
    public static async Task<(string? Path, string Message)> FetchAsync(ReleaseInfo release, string directory, HttpClient http, IProgress<TransferProgress>? progress, CancellationToken cancellation)
    {
        if (release.Launcher is not { } asset || release.LauncherHash is not { } hashAsset) return (null, "This release has no new launcher.");
        var downloader = new Downloader(http);
        var target = System.IO.Path.Combine(directory, asset.Name);
        try
        {
            await downloader.DownloadAsync(asset.DownloadUrl, target, progress, cancellation).ConfigureAwait(false);
            var expected = Hashing.ParseExpected(await downloader.ReadTextAsync(hashAsset.DownloadUrl, cancellation).ConfigureAwait(false), asset.Name);
            if (expected is null || !(await Hashing.Sha256FileAsync(target, cancellation).ConfigureAwait(false)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                return (null, "The new launcher does not match its published checksum and was deleted.");
            }
            return (target, "");
        }
        catch (DownloadException exception) { return (null, exception.Message); }
    }

    /// <summary>Starts the new launcher and tells it which old file to remove once this process has ended. Returns false when it could not be started.</summary>
    public static bool StartSuccessor(string newExe, string oldExe)
    {
        try
        {
            var start = new ProcessStartInfo(newExe) { UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(newExe)! };
            start.ArgumentList.Add(ReplaceArgument);
            start.ArgumentList.Add(oldExe);
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>Deletes the predecessor's file, waiting for it to be released (the old process is still shutting down). True when it is gone.</summary>
    public static bool RemovePredecessor(string oldExe, int attempts = 40, int delayMilliseconds = 250)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (!File.Exists(oldExe)) return true;
                File.Delete(oldExe);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Thread.Sleep(delayMilliseconds); }
        }
        return false;
    }
}
