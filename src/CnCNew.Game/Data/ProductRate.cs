namespace CnCNew;

/// <summary>
/// The product's default simulation rate: the one definition every default in the repository derives from. The game layer turns it into
/// <c>GameData.DefaultSessionRate</c> (the rate of a session whose host names none); tools take that value, and the test programs that must not
/// reference the game layer compile this file in as a link, so that their default worlds run at the rate the product runs at. The simulation
/// itself has no default: whoever creates a world names its rate. Supported are 30, 60 and 120 Hz; everything recorded at one rate (reference
/// manifest, save games, replays) carries that rate in the file and does not follow this value.
/// </summary>
internal static class ProductRate
{
    public const int TicksPerSecond = 60;
}
