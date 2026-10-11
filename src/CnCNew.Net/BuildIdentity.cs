using System.Reflection;

namespace CnCNew.Net;

/// <summary>
/// Which simulation code this process runs: the commit the tree was built from and whether the tree differed from it (assembly metadata written at build time,
/// <c>CnCNew.Net.csproj</c>). Two clients with the same data and the same format versions but different simulation code are certain to diverge; the identity goes into the
/// handshake and, for a match that leaves its process, into the match description (<c>MatchDescription.SimulationId</c>).
/// </summary>
/// <param name="Commit">The full commit id, or <c>unknown</c> when the build had no git.</param>
/// <param name="Modified">The tree had changes against that commit (or the commit is unknown): a development state.</param>
public sealed record BuildIdentity(string Commit, bool Modified)
{
    public const string UnknownCommit = "unknown";

    /// <summary>The identity of the running build.</summary>
    public static BuildIdentity Current { get; } = Read(typeof(BuildIdentity).Assembly);

    /// <summary>The identity as one text: the commit, with <c>+modified</c> for a development state. This is what is compared.</summary>
    public string Id => Modified ? Commit + "+modified" : Commit;

    public static BuildIdentity Parse(string id)
        => id.EndsWith("+modified", StringComparison.Ordinal) ? new BuildIdentity(id[..^"+modified".Length], true) : new BuildIdentity(id, false);

    private static BuildIdentity Read(Assembly assembly)
    {
        string? commit = null;
        var modified = true;
        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == "CnCNewCommit") commit = attribute.Value;
            else if (attribute.Key == "CnCNewModified") modified = !string.Equals(attribute.Value, "false", StringComparison.OrdinalIgnoreCase);
        }
        return commit is null or "" or UnknownCommit ? new BuildIdentity(UnknownCommit, true) : new BuildIdentity(commit.Trim(), modified);
    }

    public override string ToString() => Id;
}
