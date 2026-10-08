using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    /// <summary>
    /// One skirmish of the group <c>ai</c> at <paramref name="rate"/>, not yet stepped: for diagnoses that step two rates side by side and look at more than positions
    /// (<c>SimTool rate-compare --ai-diff … --fields</c>). Exactly the match the suite's case measures.
    /// </summary>
    public static SkirmishMatchSession SkirmishAiSession(RateCatalogs catalogs, SimulationRate rate, string firstFaction, string secondFaction)
        => StartSkirmishAi(new RateScene(catalogs, rate), firstFaction, secondFaction);
}
