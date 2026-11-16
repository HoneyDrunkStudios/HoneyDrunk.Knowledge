using System.Xml.Linq;
using Xunit;

namespace HoneyDrunk.Knowledge.Abstractions.Tests;

/// <summary>Dependency direction of the Abstractions package (ADR-0021 D2, D5; invariant 144).</summary>
public sealed class DependencyTests
{
    private const string ProjectPath = "src/HoneyDrunk.Knowledge.Abstractions/HoneyDrunk.Knowledge.Abstractions.csproj";

    /// <summary>
    /// The only HoneyDrunk assembly the contracts reference is AI's Abstractions, and the project's only
    /// HoneyDrunk packages are AI's Abstractions and the build-time Standards analyzers.
    /// </summary>
    [Fact]
    public void Abstractions_references_AI_Abstractions_and_no_other_HoneyDrunk_package()
    {
        var assemblies = typeof(KnowledgeSource).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("HoneyDrunk.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["HoneyDrunk.AI.Abstractions"], assemblies);

        var project = XDocument.Load(Path.Join(RepositoryRoot.Find(), ProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var packages = project.Descendants("PackageReference")
            .Select(reference => (name: (string?)reference.Attribute("Include") ?? string.Empty, privateAssets: (string?)reference.Attribute("PrivateAssets")))
            .Where(reference => reference.name.StartsWith("HoneyDrunk.", StringComparison.Ordinal))
            .OrderBy(reference => reference.name, StringComparer.Ordinal)
            .ToArray();

        (string name, string? privateAssets)[] expected = [("HoneyDrunk.AI.Abstractions", null), ("HoneyDrunk.Standards", "all")];
        Assert.Equal(expected, packages);
        Assert.Empty(project.Descendants("ProjectReference"));
    }
}
