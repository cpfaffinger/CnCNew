namespace CnCNew.Content;

public sealed record ContentFileEntry(
    string Id,
    string VirtualPath,
    string PhysicalPath,
    string Type,
    long Bytes,
    string Sha256,
    string SelectedSource,
    IReadOnlyList<RuntimeDependency> Dependencies,
    IReadOnlyList<RuntimeDiagnostic> Diagnostics);
