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

    /// <summary>
    /// The visibility of every part of a model, as <c>W3DModelDraw::doHideShowSubObjs</c> and <c>updateSubObjects</c> leave it (F-159): the part's own default, then each rule of the state and then
    /// each of <paramref name="overrides"/> in turn - and a rule that shows or hides a part also shows or hides every part whose bone hangs below the bone of that part
    /// (<c>doHideShowBoneSubObjs</c>: "hide all other child objects that use this bone as a parent"), so a later rule can bring back a child an earlier one took with its parent.
    /// <paramref name="parents"/> is the model's bone hierarchy (the parent index of each bone, -1 for the root); <paramref name="bones"/> the bone each part hangs on.
    /// </summary>
    public static void Resolve(ReadOnlySpan<string> names, ReadOnlySpan<int> bones, ReadOnlySpan<bool> defaults, ReadOnlySpan<int> parents,
        ImmutableArray<SubObjectVisibility> rules, ImmutableArray<SubObjectVisibility> overrides, Span<bool> visible)
    {
        defaults.CopyTo(visible);
        if (rules.IsDefaultOrEmpty && overrides.IsDefaultOrEmpty) return;
        if (!rules.IsDefaultOrEmpty)
            foreach (var rule in rules) Apply(names, bones, parents, rule, visible);
        if (!overrides.IsDefaultOrEmpty)
            foreach (var rule in overrides) Apply(names, bones, parents, rule, visible);
    }

    private static void Apply(ReadOnlySpan<string> names, ReadOnlySpan<int> bones, ReadOnlySpan<int> parents, SubObjectVisibility rule, Span<bool> visible)
    {
        for (var part = 0; part < names.Length; part++)
        {
            if (!Matches(names[part], rule.SubObject)) continue;
            visible[part] = !rule.Hide;
            var bone = bones[part];
            for (var child = 0; child < names.Length; child++)
                if (child != part && HangsBelow(bones[child], bone, parents)) visible[child] = !rule.Hide;
        }
    }

    /// <summary>
    /// The test of <c>doHideShowBoneSubObjs</c>: walk up from <paramref name="bone"/> while the bone is not the root, and the part is a child when a parent on the way is <paramref name="ancestor"/>
    /// (the root itself counts, so everything below a part on bone 0 that does not sit on bone 0 is its child).
    /// </summary>
    private static bool HangsBelow(int bone, int ancestor, ReadOnlySpan<int> parents)
    {
        var current = bone;
        for (var steps = 0; current != 0 && steps <= parents.Length; steps++)
        {
            current = current < parents.Length ? Math.Max(parents[current], 0) : 0;
            if (current == ancestor) return true;
        }
        return false;
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
