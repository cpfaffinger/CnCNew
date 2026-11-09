namespace CnCNew.Launcher;

public readonly record struct TransferProgress(long Bytes, long? Total)
{
    public double? Fraction => Total is > 0 ? Math.Clamp((double)Bytes / Total.Value, 0, 1) : null;
}

public sealed class DownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Downloads a file to disk with progress. The file appears under its name only when it is complete; a broken transfer leaves nothing behind.</summary>
public sealed class Downloader(HttpClient http)
{
    private const int BufferSize = 64 * 1024;
    public async Task DownloadAsync(string url, string destination, IProgress<TransferProgress>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var partial = destination + ".part";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new DownloadException($"The download was refused ({(int)response.StatusCode}).");
            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                var buffer = new byte[1 << 17];
                long done = 0;
                var lastReport = Environment.TickCount64;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    done += read;
                    if (Environment.TickCount64 - lastReport >= 100) { progress?.Report(new TransferProgress(done, total)); lastReport = Environment.TickCount64; }
                }
                progress?.Report(new TransferProgress(done, total));
                if (total is { } expected && expected != done) throw new DownloadException($"The download stopped early ({done} of {expected} bytes). Try again.");
            }
            File.Move(partial, destination, overwrite: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new DownloadException(exception is HttpRequestException ? "The connection broke during the download. Check the internet connection and try again." : $"The file could not be written: {exception.Message}", exception);
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
        }
    }

    public async Task<string> ReadTextAsync(string url, CancellationToken cancellation)
    {
        try { return await http.GetStringAsync(url, cancellation).ConfigureAwait(false); }
        catch (HttpRequestException exception) { throw new DownloadException("The checksum file could not be fetched.", exception); }
    }
}
