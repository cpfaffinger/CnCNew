namespace CnCNew.Simulation.World;

/// <summary>
/// <c>Relationship</c> (<c>GameCommon.h</c>): the directed view one player has of another. The numeric values are the reference's
/// enum indices (ENEMIES = 0, NEUTRAL = 1, ALLIES = 2) and part of the replay/snapshot encoding.
/// </summary>
public enum Relationship
{
    Enemies = 0,
    Neutral = 1,
    Allies = 2,
}
