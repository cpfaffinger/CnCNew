namespace CnCNew.W3D;

public sealed record W3dReadResult(
    W3dFile File,
    ReadOnlyMemory<byte> Data,
    bool RepairedLegacyTextMode,
    int RemovedCarriageReturns,
    long SourceLength);
