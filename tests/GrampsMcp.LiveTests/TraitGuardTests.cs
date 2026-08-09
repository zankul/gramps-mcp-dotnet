using System.Reflection;

namespace GrampsMcp.LiveTests;

/// <summary>
/// xunit v2 does not inherit class-level <c>[Trait]</c> attributes onto individual test methods,
/// so nothing enforces that every test class in this project actually carries
/// <c>[Trait("Tier", ...)]</c> — a class that forgets it would silently run under a bare
/// <c>dotnet test</c> at the repo root, defeating the whole point of this project being
/// excluded-by-default. This test makes that mechanical instead of relying on review.
/// </summary>
[Trait("Tier", "Live")]
public class TraitGuardTests
{
    [Fact]
    public void EveryPublicTestClass_CarriesATierTrait()
    {
        var assembly = typeof(TraitGuardTests).Assembly;

        var testClasses = assembly.GetTypes()
            .Where(type => type.IsPublic && !type.IsAbstract && HasTestMethod(type))
            .ToList();

        Assert.NotEmpty(testClasses);

        var missing = testClasses.Where(type => !HasTierTrait(type)).Select(type => type.FullName).ToList();

        Assert.True(
            missing.Count == 0,
            $"These test classes are missing [Trait(\"Tier\", ...)]: {string.Join(", ", missing)}");
    }

    private static bool HasTestMethod(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method => method.GetCustomAttributes(inherit: true)
                .Any(attribute => attribute is FactAttribute));

    private static bool HasTierTrait(Type type) =>
        type.GetCustomAttributesData()
            .Where(data => data.AttributeType == typeof(TraitAttribute))
            .Any(data => data.ConstructorArguments.Count == 2 &&
                string.Equals(data.ConstructorArguments[0].Value as string, "Tier", StringComparison.Ordinal));
}
