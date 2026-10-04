using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The game data behind a drawable's selection flash: <c>GameData/SelectionFlashSaturationFactor</c> (<c>GlobalData::m_selectionFlashSaturationFactor</c>).
/// <c>SelectionFlashHouseColor</c> is not read: it only chooses the colour of the flash of a freshly selected object, the capture flash always uses the capturer's house colour.
/// </summary>
public sealed record SelectionFlashSettings(float SaturationFactor)
{
    /// <summary>Strict: throws when the key is missing or not a number.</summary>
    public static SelectionFlashSettings FromDefinitions(CompiledDefinitionGraph graph)
        => new(WorldDefinitionReader.Real(WorldDefinitionReader.GameData(graph), "SelectionFlashSaturationFactor"));
}

/// <summary>
/// The flash of a building that is being captured (<c>SpecialAbilityUpdate::continuePreparation</c>, <c>DoCaptureFX</c>): on every <see cref="SimEventKind.CaptureFlash"/> the target
/// plays its selection flash envelope in the house colour of the <em>capturer</em>, as <c>Drawable::flashAsSelected(&amp;color)</c> does after <c>Drawable::saturateRGB</c>.
/// The tint is the value <c>Drawable::getSelectionColor</c> hands to <c>RTS3DScene</c>, which adds it to the ambient and to every global light of that drawable; components can
/// be negative (the saturation removes the other channels).
/// </summary>
public sealed class SelectionFlashTracker
{
    /// <summary><c>flashAsSelected</c> plays the envelope with 0 attack frames and 4 decay frames (<c>TintEnvelope::play(color, 0, 4)</c>).</summary>
    public const int DecayTicks = 4;

    /// <summary>The last tick after a flash on which <see cref="Envelope"/> is above zero.</summary>
    public const int LastVisibleAge = DecayTicks;

    private readonly Dictionary<uint, (long Tick, Vector3 Peak)> _flashes = [];

    /// <summary>
    /// <c>Drawable::saturateRGB</c>: <c>color * factor - factor / 2</c> per channel of the 0..1 colour. With the retail factor 0.5 white becomes (0.25, 0.25, 0.25) and pure red
    /// (0.25, -0.25, -0.25).
    /// </summary>
    public static Vector3 Saturate(ColorValue color, float factor)
    {
        var half = factor * 0.5f;
        return new Vector3(color.Red / 255f * factor - half, color.Green / 255f * factor - half, color.Blue / 255f * factor - half);
    }

    /// <summary>
    /// The share of the peak colour <paramref name="age"/> ticks after the flash. <c>TintEnvelope::update</c> runs once per frame of the 30 Hz clock: the first update of the
    /// attack state adds the whole way to the peak (zero attack frames count as one), the second finds the peak reached and switches to decay without changing the colour, the
    /// following ones subtract a quarter of the peak each until nothing is left and the envelope rests. So the peak shows for two ticks, then 3/4, 1/2 and 1/4.
    /// </summary>
    public static float Envelope(long age) => age switch
    {
        < 0 => 0f,
        <= 1 => 1f,
        <= LastVisibleAge => (DecayTicks + 1 - age) / (float)DecayTicks,
        _ => 0f,
    };

    /// <summary>
    /// Starts (or restarts, as <c>TintEnvelope::play</c> does while a flash is still decaying) the flash of the event's building. The colour is the house colour of the owner of
    /// the capturing unit (<c>getObject()-&gt;getIndicatorColor()</c> of the ability's object); an event whose capturer is gone starts nothing.
    /// </summary>
    public void Observe(SimEvent simEvent, SimulationWorld world, TeamColors? colors, SelectionFlashSettings settings)
    {
        if (simEvent.Kind != SimEventKind.CaptureFlash) return;
        if (world.FindEntity(simEvent.Other) is not { } capturer) return;
        var color = colors?.Of(capturer.Owner) ?? TeamColors.Neutral;
        _flashes[simEvent.Entity.Value] = (simEvent.Tick, Saturate(color, settings.SaturationFactor));
    }

    /// <summary>The tint of the entity at <paramref name="tick"/>; zero when it does not flash.</summary>
    public Vector3 TintAt(uint entity, long tick)
        => _flashes.TryGetValue(entity, out var flash) ? flash.Peak * Envelope(tick - flash.Tick) : Vector3.Zero;

    /// <summary>Forgets finished flashes and those of entities that are gone.</summary>
    public void Prune(long tick, Func<uint, bool> alive)
    {
        foreach (var id in _flashes.Where(pair => tick - pair.Value.Tick > LastVisibleAge || !alive(pair.Key)).Select(pair => pair.Key).ToArray()) _flashes.Remove(id);
    }
}
