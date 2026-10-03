// Name tables transcribed mechanically from the EA Zero Hour reference sources (GeneralsMD/Code/GameEngine).
// Ordinal positions equal the reference bit/enum indices. Do not reorder.
namespace CnCNew.Data.Definitions;

/// <summary>Additions of the structures-and-world module group (kept in a file of their own, like <see cref="NameTablesPhase5"/>).</summary>
public static class NameTablesStructures
{
    /// <summary>GameCommon.cpp TheRelationshipNames (3 entries; the <c>Relationship</c> enum order: ENEMIES, NEUTRAL, ALLIES).</summary>
    public static NameTable Relationship { get; } = new("Relationship", "GameCommon.cpp TheRelationshipNames", "ENEMIES", "NEUTRAL", "ALLIES");
}
