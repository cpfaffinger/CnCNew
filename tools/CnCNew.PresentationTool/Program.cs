const string Usage = """
    Usage: CnCNew.PresentationTool <command> [arguments]
      fx-audit <content-dir>   FX inventory, references, textures and an execution sweep
    """;

switch (args.FirstOrDefault())
{
    case "fx-audit": return FxAuditCommand.Run(args);
    default:
        Console.Error.WriteLine(Usage);
        return 2;
}
