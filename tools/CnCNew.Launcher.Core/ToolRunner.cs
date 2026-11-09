using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CnCNew.Launcher;

public sealed record ToolResult(int ExitCode, IReadOnlyList<string> Lines, bool Cancelled = false)
{
    public bool Succeeded => ExitCode == 0 && !Cancelled;
}

/// <summary>Starts a program of the installed package and hands back its lines of output as they arrive. The seam that lets tests run the launcher's flow without the real patcher.</summary>
public interface IToolRunner
{
    Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onLine, CancellationToken cancellation);
}

public sealed class ProcessToolRunner : IToolRunner
{
    public async Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onLine, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var lines = new List<string>();
        var gate = new object();
        void Receive(string? line)
        {
            if (line is null) return;
            lock (gate) lines.Add(line);
            onLine(line);
        }
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Receive(e.Data);
        process.ErrorDataReceived += (_, e) => Receive(e.Data);
        try { process.Start(); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ToolResult(-1, [$"error: {Path.GetFileName(executable)} could not be started: {exception.Message}"]);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            // The asynchronous readers finish after the exit; this waits for the last lines.
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return new ToolResult(-1, lines.ToArray(), Cancelled: true);
        }
        lock (gate) return new ToolResult(process.ExitCode, lines.ToArray());
    }
}

public enum PatcherEventKind { Info, Progress, Error }

/// <summary>One line of the patcher's output, understood. <see cref="Step"/> is the patcher's own word ("check", "patch", "verify" or an import step).</summary>
public sealed record PatcherEvent(PatcherEventKind Kind, string Step, double Fraction, string Text);

/// <summary>Reads what <c>CnCNew.AssetTool install</c> prints: progress lines <c>step: 40% (n/m)</c>, <c>error: ...</c> and plain sentences.</summary>
public static partial class PatcherOutput
{
    public static PatcherEvent Parse(string line)
    {
        var match = ProgressLine().Match(line);
        if (match.Success)
        {
            var step = match.Groups[1].Value.Trim();
            return new PatcherEvent(PatcherEventKind.Progress, step, int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0, Describe(step));
        }
        return new PatcherEvent(line.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ? PatcherEventKind.Error : PatcherEventKind.Info, "", 0, line.Trim());
    }

    /// <summary>The patcher's step word in the words of the window.</summary>
    public static string Describe(string step)
    {
        var word = step.Trim().ToLowerInvariant();
        if (word.StartsWith("check", StringComparison.Ordinal) || word.StartsWith("hash", StringComparison.Ordinal)) return "Checking your Steam copy of the game";
        if (word.StartsWith("patch", StringComparison.Ordinal)) return "Fetching the community patch";
        if (word.StartsWith("verify", StringComparison.Ordinal)) return "Verifying the game files";
        return "Making the game files";
    }

    /// <summary>The failure of a patcher run in plain words, with the lines that explain it.</summary>
    public static string Explain(ToolResult result)
    {
        if (result.Cancelled) return "Cancelled.";
        var text = string.Join('\n', result.Lines);
        if (text.Contains("No Steam installation", StringComparison.OrdinalIgnoreCase))
            return "The launcher cannot find Command & Conquer Generals - Zero Hour in your Steam libraries. Install the game in Steam (the English version), let it finish, and start the launcher again.";
        if (result.ExitCode == 1)
            return "Your Steam copy of the game is not the version this project supports (the English Steam release of Zero Hour, fully updated). " +
                   "In Steam, set the game's language to English and use Properties > Installed Files > Verify integrity, then try again." + Details(result);
        if (text.Contains("lies in a Steam library", StringComparison.OrdinalIgnoreCase))
            return "That install folder is inside a Steam library. Choose a folder elsewhere; Steam's files are never written." + Details(result);
        if (text.Contains("could not be started", StringComparison.OrdinalIgnoreCase))
            return "The patcher of the package could not be started. A virus scanner may have removed it; repair the installation to fetch it again." + Details(result);
        return "Making the game files did not work." + Details(result);
    }

    private static string Details(ToolResult result)
    {
        var useful = result.Lines.Where(line => line.StartsWith("error:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("  ", StringComparison.Ordinal) || line.Contains("differs", StringComparison.OrdinalIgnoreCase) || line.Contains("refus", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim()).Where(line => line.Length > 0).Take(6).ToArray();
        return useful.Length == 0 ? "" : "\n\n" + string.Join('\n', useful);
    }

    [GeneratedRegex(@"^\s*([A-Za-z][\w ().-]*?):\s+(\d{1,3})% \((\d+)/(\d+)\)\s*$")]
    private static partial Regex ProgressLine();
}
