using System.Collections.Immutable;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// What an object's <c>SubObjectsUpgrade</c> modules did to its model. <c>SubObjectsUpgrade::upgradeImplementation</c> calls <c>Drawable::showSubObject</c> for every name of <c>ShowSubObjects</c> and then of
/// <c>HideSubObjects</c>; the drawable keeps the answer by name (a later call for the same name replaces the earlier) and re-applies it after every change of model state, so it outlasts the state's own
/// <c>ShowSubObject</c>/<c>HideSubObject</c> lines. The module knows by itself whether its upgrade ran (the conflicts, the required triggers, the player's and the object's upgrades), so the presentation asks it.
/// </summary>
public static class SubObjectOverrides
{
    /// <summary>The entries to apply after the state's, in module order; default when the object has none.</summary>
    public static ImmutableArray<SubObjectVisibility> Of(Entity entity, ObjectVisual visual)
    {
        if (visual.SubObjectUpgrades.IsEmpty) return default;
        var merged = new List<SubObjectVisibility>();
        foreach (var module in entity.Modules)
        {
            if (module is not SubObjectsUpgrade { Executed: true }) continue;
            var spec = visual.SubObjectUpgrades.FirstOrDefault(candidate => candidate.Tag.Equals(module.Tag, StringComparison.OrdinalIgnoreCase));
            if (spec is null) continue;
            foreach (var name in spec.Show) Put(merged, name, hide: false);
            foreach (var name in spec.Hide) Put(merged, name, hide: true);
        }
        return merged.Count == 0 ? default : [..merged];
    }

    /// <summary>
    /// Whether the part named <paramref name="fullName"/> is drawn: its own default, then the state's <c>ShowSubObject</c>/<c>HideSubObject</c> lines, then <paramref name="overrides"/>
    /// (<c>W3DModelDraw::updateSubObjects</c> re-applies them last: "It will override any animation subobjects states"). A name matches the part's full name or the suffix after its container dot; later rules win.
    /// </summary>
    public static bool IsVisible(string fullName, bool defaultVisible, ImmutableArray<SubObjectVisibility> rules, ImmutableArray<SubObjectVisibility> overrides)
    {
        var visible = defaultVisible;
        foreach (var rule in rules)
            if (Matches(fullName, rule.SubObject)) visible = !rule.Hide;
        if (!overrides.IsDefaultOrEmpty)
            foreach (var rule in overrides)
                if (Matches(fullName, rule.SubObject)) visible = !rule.Hide;
        return visible;
    }

    private static bool Matches(string fullName, string ruleName)
    {
        if (fullName.Equals(ruleName, StringComparison.OrdinalIgnoreCase)) return true;
        var dot = fullName.IndexOf('.');
        var suffix = dot < 0 ? fullName : fullName[(dot + 1)..];
        return suffix.Equals(ruleName, StringComparison.OrdinalIgnoreCase);
    }

    private static void Put(List<SubObjectVisibility> list, string name, bool hide)
    {
        var lower = name.ToLowerInvariant();
        var index = list.FindIndex(entry => entry.SubObject.Equals(lower, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) list[index] = list[index] with { Hide = hide };
        else list.Add(new SubObjectVisibility(lower, hide));
    }
}
