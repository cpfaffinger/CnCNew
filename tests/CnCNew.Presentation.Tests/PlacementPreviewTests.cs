using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The model that follows the cursor while a structure is placed (<c>PlacementPreview</c>, <c>InGameUI::handleBuildPlacements</c>): the drawable is built from the ghost alone,
/// translucent at the original's 0.45, red where the spot is refused, absent where there is nothing to show; <c>PlacementViewAngle</c> reaches the visual catalog.
/// </summary>
internal static class PlacementPreviewTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha")];
    private static readonly ColorValue Gold = new(240, 200, 30);

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/PlacementPreviewFixture.ini"] = """
        Object PreviewBarracks
          KindOf = STRUCTURE
          PlacementViewAngle = -45
          Scale = 1.25
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = PreviewBarracks
            End
            ConditionState = DAMAGED
              Model = PreviewBarracks_D
            End
          End
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object PreviewShed
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = PreviewShed
            End
          End
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object PreviewInvisible
          KindOf = STRUCTURE
          PlacementViewAngle = -135
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("PlacementPreview");
        t.Run("PlacementViewAngle reaches the visual catalog in radians (0 when the template has none)", ViewAngle);
        t.Run("a valid ghost becomes a translucent drawable of the template at the ghost's place, turned by its angle, in the owner's colour", ValidGhost);
        t.Run("a refused ghost is tinted red, the translucency stays", RefusedGhost);
        t.Run("no ground under the pointer, an unknown template or a template without a model show nothing", NothingToShow);
        t.Run("the drawable carries the model of the template's normal state, and solid drawables stay solid", Selections);
        return t.Failures;
    }

    private static ObjectVisualCatalog Catalog() => ObjectVisualCatalog.Build(new SyntheticFixture(files: Files()).Graph);

    private static PlacementGhost Ghost(string template, bool valid = true, float angle = 0.5f, PlacementIssue reason = PlacementIssue.None)
        => new(template, new Vector3(120f, 80f, 7f), angle, valid, valid ? PlacementIssue.None : reason == PlacementIssue.None ? PlacementIssue.BlockedFootprint : reason, false);

    private static void ViewAngle()
    {
        var catalog = Catalog();
        Near(-Math.PI / 4, catalog["PreviewBarracks"].PlacementViewAngle, 1e-6, "-45 degrees");
        Near(-3 * Math.PI / 4, catalog["PreviewInvisible"].PlacementViewAngle, 1e-6, "-135 degrees");
        Assert(catalog["PreviewShed"].PlacementViewAngle == 0f, "no PlacementViewAngle is 0");
    }

    private static void ValidGhost()
    {
        var colors = TeamColors.Resolve(Players, null, new Dictionary<int, ColorValue> { [1] = Gold });
        var drawable = PlacementPreview.Create(Catalog(), Ghost("PreviewBarracks"), new PlayerId(1), colors)
                       ?? throw new InvalidOperationException("a valid ghost with a model has a preview");
        Assert(PlacementPreview.Opacity == 0.45f, "InGameUI.cpp placementOpacity is 0.45");
        Assert(drawable.Opacity == 0.45f, "translucent at the original opacity");
        Assert(drawable.IsPlacementPreview && drawable.SceneryIndex == DrawableInstance.PlacementPreviewIndex && !drawable.Entity.IsValid, "it has a key of its own and no entity");
        Assert(drawable.Position == new Vector3(120f, 80f, 7f) && drawable.Yaw == 0.5f, "at the ghost's place and angle");
        Assert(drawable.TeamColor == Gold && drawable.Owner == new PlayerId(1), "in the owner's house colour");
        Assert(drawable.FlashTint == Vector3.Zero && !drawable.HasFlashTint, "a legal spot is not tinted");
        Assert(drawable.TemplateName == "PreviewBarracks" && drawable.Scale == 1.25f, "the template's visual and scale");
        Assert(!drawable.IsStatic && !drawable.IsGhost && !drawable.Fogged && drawable.Health01 == 1f && drawable.ConstructionProgress01 == 1f, "a finished, visible, non-fog drawable");
        Assert(drawable.Conditions.Equals(ModelConditionSet.Empty), "default model conditions");
    }

    private static void RefusedGhost()
    {
        var drawable = PlacementPreview.Create(Catalog(), Ghost("PreviewBarracks", valid: false), new PlayerId(1), null)
                       ?? throw new InvalidOperationException("a refused ghost still has a preview");
        Assert(drawable.Opacity == 0.45f, "still translucent");
        Assert(PlacementPreview.IllegalTint == new Vector3(1f, 0f, 0f), "illegalBuildColor is red");
        Assert(drawable.FlashTint == PlacementPreview.IllegalTint && drawable.HasFlashTint, "tinted with the illegal colour through the flash tint");
        Assert(drawable.TeamColor == TeamColors.Neutral, "without house colours the neutral colour");
    }

    private static void NothingToShow()
    {
        var catalog = Catalog();
        Assert(PlacementPreview.Create(catalog, new PlacementGhost("PreviewBarracks", Vector3.Zero, 0f, false, PlacementIssue.NoGround, false), new PlayerId(1), null) is null,
            "the pointer is off the terrain");
        Assert(PlacementPreview.Create(catalog, Ghost("NoSuchBuilding"), new PlayerId(1), null) is null, "unknown template");
        Assert(PlacementPreview.Create(catalog, Ghost("PreviewInvisible"), new PlayerId(1), null) is null, "a template without a model has nothing to draw");
    }

    private static void Selections()
    {
        var catalog = Catalog();
        var drawable = PlacementPreview.Create(catalog, Ghost("PreviewBarracks"), new PlayerId(1), null)!;
        Assert(string.Equals(drawable.PrimaryModel, "PreviewBarracks", StringComparison.OrdinalIgnoreCase), $"the NONE state's model, not the damaged one ({drawable.PrimaryModel})");
        Assert(drawable.Selections.Length == catalog["PreviewBarracks"].Draws.Length, "one selection per draw module");
        var solid = new DrawableInstance(new EntityId(5), -1, catalog["PreviewShed"], Vector3.Zero, 0f, new PlayerId(1), TeamColors.Neutral, 1f, 1f, ModelConditionSet.Empty,
            [], null, null, false, false);
        Assert(solid.Opacity == 1f && !solid.IsPlacementPreview, "entities and scenery are solid and are no preview");
    }
}
