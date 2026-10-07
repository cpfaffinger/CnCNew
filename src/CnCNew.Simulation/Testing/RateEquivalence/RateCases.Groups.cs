namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    // The harness alone: every group is empty until its cases are added.
    private static RateCaseGroup LocomotionCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Locomotion).Done();
    private static RateCaseGroup FlightCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Flight).Done();
    private static RateCaseGroup WeaponCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Weapons).Done();
    private static RateCaseGroup ProjectileCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Projectiles).Done();
    private static RateCaseGroup TurretCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Turrets).Done();
    private static RateCaseGroup OverTimeCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, OverTime).Done();
    private static RateCaseGroup EconomyCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Economy).Done();
    private static RateCaseGroup TimedCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Timed).Done();
    private static RateCaseGroup ScriptCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Scripts).Done();
    private static RateCaseGroup AiCases(RateCatalogs catalogs) => new GroupBuilder(catalogs, Ai).Done();
}
