using CnCNew.PresentationTool;

const string Usage = """
    Usage: CnCNew.PresentationTool <command> [arguments]
      terrain-audit <content-dir> [--map <virtual map path>]
    """;

// One line per command; each handler returns the process exit code.
return args switch
{
    ["terrain-audit", ..] => TerrainAudit.Run(args[1..]),
    _ => Fail(Usage),
};

static int Fail(string usage)
{
    Console.Error.WriteLine(usage);
    return 2;
}
