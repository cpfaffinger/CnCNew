using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>One line of the superweapon list: the object that carries the power, whose it is, the translated name and the time left as <c>m:ss</c>.</summary>
public readonly record struct SuperweaponLine(PlayerId Player, EntityId Object, string Power, string Name, string Time, int SecondsLeft, bool Ready);

/// <summary>
/// The superweapon timers of <c>InGameUI::draw</c> (<c>drawSuperweaponTimers</c> part): every object with a special power that has a <c>PublicTimer</c> shows "Name: m:ss" at the top right,
/// whoever owns it (an enemy's nuke counts down on your screen too), in its owner's colour; a ready power is bold and flashes between that colour and white. The rules follow the reference:
/// players in index order, powers by name, objects in creation order; an object under construction shows nothing; a power with a shared timer shows once per player (the first object); a power whose
/// science the owner lacks is left out; the list stops with "..." above the control bar (<c>0.82</c> of the view height). A mission script can hide the whole list
/// (<c>DISABLE_SPECIAL_POWER_DISPLAY</c>, <see cref="DisplayEnabled"/>) or single objects (<c>NAMED_HIDE_SPECIAL_POWER_DISPLAY</c>, <see cref="HiddenByScript"/>).
/// <para>
/// The position (<c>SuperweaponCountdownPosition</c>), the flash time and color and the fonts (<c>Language.ini</c>, ready or normal) come from the typed <see cref="InGameUiStyle"/>, not from constants;
/// a ready power uses the ready font and flashes between the owner's color and the flash color every flash period (<c>m_superweaponUsedFlashColor</c> starts true, so the flash color shows in
/// the odd periods).
/// </para>
/// </summary>
public sealed class SuperweaponTimers
{
    /// <summary>The list stops at this share of the view height (<c>bottomMargin = view height * 0.82</c>).</summary>
    public const float BottomMargin = 0.82f;

    /// <summary><see langword="false"/> after <c>DISABLE_SPECIAL_POWER_DISPLAY</c> (<c>InGameUI::m_superweaponHiddenByScript</c>).</summary>
    public bool DisplayEnabled { get; set; } = true;

    /// <summary>Objects a script hid (<c>NAMED_HIDE_SPECIAL_POWER_DISPLAY</c>); <see langword="null"/> hides none.</summary>
    public Func<EntityId, bool>? HiddenByScript { get; set; }

    /// <summary>Name labels (<c>GUI:&lt;power&gt;</c>) the string table does not know, by label: shown as the label itself, counted here.</summary>
    public ImmutableSortedDictionary<string, int> MissingLabels => _missing.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private readonly SortedDictionary<string, int> _missing = new(StringComparer.Ordinal);

    /// <summary>The lines of the moment in drawing order (an empty list when a script switched the display off).</summary>
    public ImmutableArray<SuperweaponLine> Collect(SimulationWorld world, Func<string, string?> labelText)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(labelText);
        if (!DisplayEnabled) return [];
        var byPlayer = new SortedDictionary<int, SortedDictionary<string, List<(Entity Entity, SimModule Module, Simulation.Templates.SpecialPowerInfo Power)>>>();
        foreach (var entity in world.Entities.OrderBy(entity => entity.Id.Value))
        {
            if (entity.DestroyRequested || entity.EffectivelyDead || entity.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
            foreach (var module in entity.Modules.OfType<ISpecialPowerModule>())
            {
                if (module.PowerIndex < 0 || module is not SimModule simModule) continue;
                var power = world.SpecialPowers[module.PowerIndex];
                if (!power.PublicTimer) continue;
                if (power.RequiredScience >= 0 && !world.PlayerSciences(entity.Owner).Has(power.RequiredScience)) continue;
                if (!byPlayer.TryGetValue(entity.Owner.Value, out var powers)) byPlayer[entity.Owner.Value] = powers = new SortedDictionary<string, List<(Entity, SimModule, Simulation.Templates.SpecialPowerInfo)>>(StringComparer.Ordinal);
                if (!powers.TryGetValue(power.Name, out var list)) powers[power.Name] = list = [];
                list.Add((entity, simModule, power));
            }
        }

        var lines = ImmutableArray.CreateBuilder<SuperweaponLine>();
        foreach (var (playerIndex, powers) in byPlayer)
            foreach (var (name, list) in powers)
                foreach (var (entity, module, power) in list)
                {
                    if (HiddenByScript?.Invoke(entity.Id) == true) continue;
                    var readyFrame = CommandBarModel.ReadyFrameOf(world, entity.Owner, module, power);
                    // "Integer math. We can't have float imprecision display 4:01 on a disabled superweapon" (InGameUI.cpp): a power that is ready, or disabled with its time over, shows 0:00.
                    var seconds = readyFrame < world.Tick ? 0 : (int)((readyFrame - world.Tick) / SimulationRate.OriginalFramesPerSecond);
                    var label = "GUI:" + name;
                    var text = labelText(label);
                    if (text is null)
                    {
                        _missing[label] = _missing.GetValueOrDefault(label) + 1;
                        text = label;
                    }
                    var ready = ((ISpecialPowerModule)module).IsReady(world);
                    lines.Add(new SuperweaponLine(new PlayerId(playerIndex), entity.Id, name, text + ": ", string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}"), seconds, ready));
                    if (power.SharedSynced) break;
                }
        return lines.ToImmutable();
    }

    /// <summary>
    /// The draw commands for <paramref name="lines"/> on a <paramref name="width"/> x <paramref name="height"/> view whose control bar starts at <paramref name="controlBarTop"/>: the name right-aligned to
    /// the list's x, the time left-aligned from it (<c>SuperweaponInfo::drawName/drawTime</c>), one line per font height, in the owner's colour, white while a ready power flashes.
    /// </summary>
    public static ImmutableArray<UiDrawCommand> Draw(ImmutableArray<SuperweaponLine> lines, Func<PlayerId, WndColor> colorOf, long tick, int width, int height, int controlBarTop, IUiTextMeasure measure,
        InGameUiStyle style, UiScaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (lines.IsDefaultOrEmpty) return [];
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var settings = style.Settings;
        var viewport = new UiViewport(width, height);
        var normalFont = style.Font(InGameUiFontRole.SuperweaponNormal, viewport, options);
        var readyFont = style.Font(InGameUiFontRole.SuperweaponReady, viewport, options);
        var shadow = new WndColor(0, 0, 0, 255);
        var clip = new UiRect(0, 0, width, height);
        var x = (int)(settings.SuperweaponPosition.X * width);
        var y = (int)(settings.SuperweaponPosition.Y * height);
        var bottom = (int)(Math.Min(height, controlBarTop > 0 ? controlBarTop : height) * BottomMargin);
        var flashFrames = (int)settings.SuperweaponFlashFrames;
        var flash = flashFrames > 0 && tick / flashFrames % 2 == 1;
        foreach (var line in lines)
        {
            var font = line.Ready ? readyFont : normalFont;
            var step = measure.LineHeight(font);
            if (y >= bottom)
            {
                commands.Add(new UiTextDraw("...", null, normalFont, settings.SuperweaponFlashColor, shadow, x, y, UiTextAlignment.Left, clip));
                break;
            }
            var color = line.Ready && flash ? settings.SuperweaponFlashColor : colorOf(line.Player);
            commands.Add(new UiTextDraw(line.Name, null, font, color, shadow, x - measure.MeasureWidth(line.Name, font), y, UiTextAlignment.Left, clip));
            commands.Add(new UiTextDraw(line.Time, null, font, color, shadow, x, y, UiTextAlignment.Left, clip));
            y += step;
        }
        return commands.ToImmutable();
    }
}
