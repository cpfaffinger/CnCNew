namespace CnCNew.Content;

public sealed record ContentFileEntry(
    string VirtualPath,
    string PhysicalPath,
    long Bytes,
    string Sha256,
    string SelectedSource);
