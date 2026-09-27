using CnCNew.Data.Ini;

const string source = """
; comment
Object DemoTank
  Side = America
  Draw = W3DModelDraw ModuleTag_01
    DefaultConditionState
      Model = AVTank
    End
  End
  Behavior = ActiveBody ModuleTag_02
    MaxHealth = 100.0 ; inline comment
  End
End
""";

var document = SageIniParser.Parse(source, "fixture.ini");
Assert(document.Blocks.Count() == 1, "Expected one top-level block.");
Assert(document.Blocks.Single().Type == "Object", "Top-level type mismatch.");
Assert(document.DescendantBlocks().Count() == 4, "Nested block count mismatch.");

var side = document.Blocks.Single().Children.OfType<IniProperty>().Single(property => property.Name == "Side");
Assert(side.Value == "America", "Property value mismatch.");

var body = document.DescendantBlocks().Single(block => block.Declaration.StartsWith("Behavior", StringComparison.Ordinal));
var health = body.Children.OfType<IniProperty>().Single();
Assert(health.Value == "100.0", "Inline comment was not removed.");
Console.WriteLine("All INI parser tests passed.");
return 0;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

