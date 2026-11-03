using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Game.Hud;

/// <summary>
/// The radius cursor (<c>InGameUI::setRadiusCursor</c>, <c>handleRadiusCursor</c>, <c>RadiusDecal::update</c>): in a mode that asks for a place - guarding, a special power - a decal of the
/// mode's <c>...RadiusCursor</c> block lies on the ground under the pointer, as wide as the guard range or the power's <c>RadiusCursorRadius</c>, its opacity throbbing. The owner's playtest
/// of 3 November 2026: the guard mode showed neither its circle nor its shield.
/// </summary>
public sealed partial class MatchController
{
    /// <summary><c>TheRadiusCursorNames</c> and the <c>...RadiusCursor</c> block each one reads (<c>InGameUI::s_fieldParseTable</c>).</summary>
    private static readonly ImmutableSortedDictionary<string, string> RadiusCursorBlocks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ATTACK_DAMAGE_AREA"] = "AttackDamageAreaRadiusCursor",
        ["ATTACK_SCATTER_AREA"] = "AttackScatterAreaRadiusCursor",
        ["ATTACK_CONTINUE_AREA"] = "AttackContinueAreaRadiusCursor",
        ["GUARD_AREA"] = "GuardAreaRadiusCursor",
        ["EMERGENCY_REPAIR"] = "EmergencyRepairRadiusCursor",
        ["FRIENDLY_SPECIALPOWER"] = "FriendlySpecialPowerRadiusCursor",
        ["OFFENSIVE_SPECIALPOWER"] = "OffensiveSpecialPowerRadiusCursor",
        ["SUPERWEAPON_SCATTER_AREA"] = "SuperweaponScatterAreaRadiusCursor",
        ["PARTICLECANNON"] = "ParticleCannonRadiusCursor",
        ["A10STRIKE"] = "A10StrikeRadiusCursor",
        ["CARPETBOMB"] = "CarpetBombRadiusCursor",
        ["DAISYCUTTER"] = "DaisyCutterRadiusCursor",
        ["PARADROP"] = "ParadropRadiusCursor",
        ["SPYSATELLITE"] = "SpySatelliteRadiusCursor",
        ["SPECTREGUNSHIP"] = "SpectreGunshipRadiusCursor",
        ["HELIX_NAPALM_BOMB"] = "HelixNapalmBombRadiusCursor",
        ["NUCLEARMISSILE"] = "NuclearMissileRadiusCursor",
        ["EMPPULSE"] = "EMPPulseRadiusCursor",
        ["ARTILLERYBARRAGE"] = "ArtilleryRadiusCursor",
        ["NAPALMSTRIKE"] = "NapalmStrikeRadiusCursor",
        ["CLUSTERMINES"] = "ClusterMinesRadiusCursor",
        ["SCUDSTORM"] = "ScudStormRadiusCursor",
        ["ANTHRAXBOMB"] = "AnthraxBombRadiusCursor",
        ["AMBUSH"] = "AmbushRadiusCursor",
        ["RADAR"] = "RadarRadiusCursor",
        ["SPYDRONE"] = "SpyDroneRadiusCursor",
        ["FRENZY"] = "FrenzyRadiusCursor",
        ["CLEARMINES"] = "ClearMinesRadiusCursor",
        ["AMBULANCE"] = "AmbulanceRadiusCursor",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The decal of the radius cursor this frame, or null (<see cref="FxExecutor.CursorDecal"/>).</summary>
    public DecalInstance? RadiusCursorDecal { get; private set; }

    private void UpdateRadiusCursor()
    {
        RadiusCursorDecal = IsMenuOpen || PointerOverWindow || !Input.State.HasMouse ? null : RadiusCursor(Input.State.MousePosition);
        Match.Fx.CursorDecal = RadiusCursorDecal;
    }

    private DecalInstance? RadiusCursor(Vector2 mouse)
    {
        var commands = Input.Commands;
        if (commands.RadiusCursorType is not { } type || !RadiusCursorBlocks.TryGetValue(type.ToUpperInvariant(), out var block)
            || !TextStyle.Settings.RadiusCursors.TryGetValue(block, out var template) || template.Texture.Length == 0) return null;
        if (!Match.Camera.TryScreenToGround(mouse, out var ground)) return null;
        var source = commands.PendingSources.Select(Match.World.FindEntity).FirstOrDefault(entity => entity is not null);
        var radius = Fixed.Zero;
        if (type.Equals("GUARD_AREA", StringComparison.OrdinalIgnoreCase)) radius = source is null ? Fixed.Zero : Match.World.StdGuardRange(source);
        else if (commands.PendingButton is { SpecialPower.Length: > 0 } button && Match.World.SpecialPowers.TryGet(button.SpecialPower, out var power)) radius = power.RadiusCursorRadius;
        if (radius <= Fixed.Zero) return null;
        // RadiusDecal::update: the opacity follows a sine over OpacityThrobTime, counted in logic frames.
        var throb = Math.Max(1L, (long)template.ThrobMilliseconds * CnCNew.Data.OriginalFrame.PerSecond / 1000);
        var theta = 2 * Math.PI * (Match.Clock.OriginalFrames % throb) / throb;
        var opacity = (float)(template.OpacityMin + (decimal)(0.5 * (Math.Sin(theta) + 1)) * (template.OpacityMax - template.OpacityMin));
        var color = new Vector4(template.Color.R / 255f, template.Color.G / 255f, template.Color.B / 255f, template.Color.A / 255f * opacity);
        return new DecalInstance(DecalKind.Fx, template.Texture + ".tga", ground, FixedConvert.ToSingle(radius), 0f, color, new Vector4(0f, 0f, 1f, 1f), Match.Clock.OriginalFrames, null, 0);
    }
}
