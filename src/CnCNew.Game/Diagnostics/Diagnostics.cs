using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace CnCNew.Game.Diagnostics;

/// <summary>
/// The log of one run of the client (docs/TROUBLESHOOTING.md): everything the program writes to standard output and standard error, also into
/// <c>&lt;directory&gt;/logs/client-&lt;UTC time&gt;-&lt;process&gt;.log</c>, each line as <c>ts=&lt;ms since start&gt; stream=out|err text=...</c>. The last lines stay in memory for a
/// crash report. At most <see cref="KeptFiles"/> files are kept; older ones are deleted when a run starts. Nothing leaves the machine.
/// </summary>
public sealed class RunLog : IDisposable
{
    public const int KeptFiles = 20;
    public const int RememberedLines = 200;
    private readonly object _gate = new();
    private readonly StreamWriter? _file;
    private readonly Queue<string> _recent = new();
    private readonly long _started = Environment.TickCount64;
    private readonly TextWriter _out;
    private readonly TextWriter _err;

    private RunLog(string? path, TextWriter output, TextWriter error)
    {
        Path = path;
        _out = output;
        _err = error;
        if (path is not null) _file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>The file of this run, or null when it could not be created (the run goes on without one).</summary>
    public string? Path { get; }

    /// <summary>The log of this process, once <see cref="Start"/> ran.</summary>
    public static RunLog? Current { get; private set; }

    /// <summary>Starts the log of this run under <paramref name="directory"/> and puts it behind <see cref="Console.Out"/> and <see cref="Console.Error"/>.</summary>
    public static RunLog Start(string directory, string program)
    {
        string? path = null;
        try
        {
            var logs = System.IO.Path.Combine(directory, "logs");
            Directory.CreateDirectory(logs);
            foreach (var old in Directory.GetFiles(logs, program + "-*.log").Order(StringComparer.Ordinal).SkipLast(KeptFiles - 1))
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            path = System.IO.Path.Combine(logs, $"{program}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}.log");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            path = null;
        }
        var log = new RunLog(path, Console.Out, Console.Error);
        Console.SetOut(new Tee(log, "out", log._out));
        Console.SetError(new Tee(log, "err", log._err));
        Current = log;
        return log;
    }

    /// <summary>The last lines written, oldest first.</summary>
    public IReadOnlyList<string> Recent
    {
        get { lock (_gate) return [.. _recent]; }
    }

    private void Record(string stream, string line)
    {
        var entry = $"ts={Environment.TickCount64 - _started} stream={stream} text={line}";
        lock (_gate)
        {
            _recent.Enqueue(entry);
            while (_recent.Count > RememberedLines) _recent.Dequeue();
            try { _file?.WriteLine(entry); } catch (IOException) { }
        }
    }

    public void Dispose()
    {
        Console.SetOut(_out);
        Console.SetError(_err);
        lock (_gate) _file?.Dispose();
        if (Current == this) Current = null;
    }

    private sealed class Tee(RunLog log, string stream, TextWriter inner) : TextWriter
    {
        private readonly StringBuilder _line = new();
        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            inner.Write(value);
            lock (_line)
            {
                if (value == '\n')
                {
                    log.Record(stream, _line.ToString().TrimEnd('\r'));
                    _line.Clear();
                }
                else _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var character in value) Write(character);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        public override void Flush() => inner.Flush();
    }
}

/// <summary>
/// A crash report (docs/TROUBLESHOOTING.md): a text file under <c>&lt;directory&gt;/crashes</c> with what was running, on what, and the exception - and the last lines of
/// the run's log. It holds no file of the game and no state of a world: names of files and options, the build and the system, the error. It stays on the machine;
/// <see cref="SupportBundle"/> packs it on request.
/// </summary>
public static class CrashReport
{
    /// <summary>Writes the report and returns its path, or null when it could not be written (then the caller still has the exception on standard error).</summary>
    /// <param name="context">What the program knows about the moment: build, content, rules, match - one <c>key: value</c> per entry.</param>
    public static string? Write(string directory, string program, Exception exception, IEnumerable<(string Key, string Value)> context, IReadOnlyList<string> arguments)
    {
        try
        {
            var crashes = Path.Combine(directory, "crashes");
            Directory.CreateDirectory(crashes);
            var path = Path.Combine(crashes, $"{program}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}.txt");
            var text = new StringBuilder();
            text.AppendLine($"CnCNew crash report ({program})");
            text.AppendLine($"time (UTC): {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            text.AppendLine($"os: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            text.AppendLine($"runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
            text.AppendLine($"processors: {Environment.ProcessorCount}");
            foreach (var (key, value) in context) text.AppendLine($"{key}: {value}");
            text.AppendLine($"arguments: {string.Join(' ', arguments.Select(Quote))}");
            text.AppendLine();
            text.AppendLine("exception:");
            text.AppendLine(exception.ToString());
            if (RunLog.Current is { } log)
            {
                text.AppendLine();
                text.AppendLine($"log: {log.Path ?? "(not written to a file)"}");
                text.AppendLine($"last {log.Recent.Count} lines:");
                foreach (var line in log.Recent) text.AppendLine(line);
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Quote(string argument) => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
}

/// <summary>
/// The support bundle (docs/TROUBLESHOOTING.md): one ZIP file that a person may hand to somebody who helps - made only when asked (<c>--support-bundle FILE</c>), written
/// where the person says, sent nowhere. It holds the logs, crash reports and desync packages of the program's directory, a description of the system and the build,
/// and a list of every file it holds (<c>CONTENTS.txt</c>). It never holds a file of the game, a key (<c>*.pem</c>) or a saved game; the content directory is described
/// by its record (<c>install.json</c>) and the hash of its manifest, never copied.
/// </summary>
public static class SupportBundle
{
    /// <summary>Files under these directories of <c>directory</c> are taken; anything else there is not.</summary>
    public static readonly IReadOnlyList<string> Taken = ["logs", "crashes", "diagnostics"];

    /// <summary>File names never taken, whatever directory they are in.</summary>
    public static bool IsExcluded(string name)
        => name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".big", StringComparison.OrdinalIgnoreCase);

    public static (string Path, int Files) Write(string output, string directory, IEnumerable<(string Key, string Value)> description, string? contentDirectory)
    {
        var target = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var listing = new StringBuilder();
        var count = 0;
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            var about = new StringBuilder();
            about.AppendLine("CnCNew support bundle");
            about.AppendLine($"made (UTC): {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            about.AppendLine($"os: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            about.AppendLine($"runtime: {RuntimeInformation.FrameworkDescription}");
            foreach (var (key, value) in description) about.AppendLine($"{key}: {value}");
            if (contentDirectory is not null)
            {
                var manifest = Path.Combine(contentDirectory, "content-manifest.json");
                if (File.Exists(manifest))
                {
                    using var stream = File.OpenRead(manifest);
                    about.AppendLine($"content manifest: {new FileInfo(manifest).Length} bytes, SHA-256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant()}");
                }
                var record = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(contentDirectory).TrimEnd(Path.DirectorySeparatorChar)) ?? contentDirectory, "install.json");
                if (File.Exists(record)) { Add(zip, record, "install.json"); listing.AppendLine("install.json"); count++; }
            }
            foreach (var folder in Taken)
            {
                var root = Path.Combine(directory, folder);
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    if (IsExcluded(Path.GetFileName(file))) continue;
                    var name = folder + "/" + Path.GetRelativePath(root, file).Replace('\\', '/');
                    Add(zip, file, name);
                    listing.AppendLine(name);
                    count++;
                }
            }
            WriteText(zip, "ABOUT.txt", about.ToString());
            WriteText(zip, "CONTENTS.txt", "Files in this bundle (besides ABOUT.txt and this list):\n" + listing);
        }
        return (target, count);
    }

    private static void Add(ZipArchive zip, string file, string name)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = entry.Open();
        input.CopyTo(output);
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
