using System.Reflection;

namespace CnCNew.Game;

/// <summary>
/// The product version a person sees (the options screen): version.txt as the build wrote it into the assembly (Directory.Build.props), without the commit
/// the SDK appends. Tests that pin pictures set it to a fixed value, so a release moves no picture.
/// </summary>
public static class ProductVersion
{
    public static string Text { get; set; } = Read();

    private static string Read()
    {
        var informational = typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
