using System.Reflection;
using System.Text.Json;

namespace CropQc.Api.Tests;

// Metadata integrity only. Actual behavior comes from the referenced runtime tests.
public sealed class GovernanceArchitectureTests
{
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CropQc.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository source is required.");
    }

    [Fact]
    [Trait("BusinessRule", "GOV-001")]
    public void Rule_test_references_resolve_to_real_executable_methods()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs/governance/traceability.json")));
        foreach (var rule in manifest.RootElement.GetProperty("rules").EnumerateArray())
            foreach (var entry in rule.GetProperty("tests").EnumerateArray())
            {
                var type = typeof(GovernanceArchitectureTests).Assembly.GetType(entry.GetProperty("type").GetString()!);
                Assert.NotNull(type);
                var method = type.GetMethod(entry.GetProperty("member").GetString()!, BindingFlags.Public | BindingFlags.Instance);
                Assert.NotNull(method);
                Assert.Contains(method.GetCustomAttributes(), x => x is FactAttribute);
            }
    }

    [Fact]
    [Trait("BusinessRule", "GOV-001")]
    public void Every_tagged_business_contract_has_a_traceability_entry()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs/governance/traceability.json")));
        var rules = manifest.RootElement.GetProperty("rules").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        foreach (var type in new[] { typeof(BusinessRuleContractTests), typeof(GovernanceArchitectureTests) })
            foreach (var method in type.GetMethods())
                foreach (var trait in method.GetCustomAttributesData().Where(x => x.AttributeType == typeof(TraitAttribute)
                    && x.ConstructorArguments[0].Value?.ToString() == "BusinessRule"))
                {
                    var id = trait.ConstructorArguments[1].Value!.ToString()!;
                    Assert.True(rules.TryGetValue(id, out var rule), id);
                    Assert.Contains(rule.GetProperty("tests").EnumerateArray(),
                        x => x.GetProperty("type").GetString() == type.FullName && x.GetProperty("member").GetString() == method.Name);
                }
    }
}
