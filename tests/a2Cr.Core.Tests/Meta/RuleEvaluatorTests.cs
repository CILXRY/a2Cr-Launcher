using System.Text.Json.Nodes;
using a2Cr.Core.Meta;

namespace a2Cr.Core.Tests.Meta;

public sealed class RuleEvaluatorTests
{
    [Fact]
    public void EmptyRules_AreAllowed()
    {
        Assert.True(RuleEvaluator.IsAllowed((JsonArray?)null, "windows", "x86_64"));
    }

    [Fact]
    public void LastMatchingRule_Wins()
    {
        var rules = JsonNode.Parse("[{\"action\":\"allow\"},{\"action\":\"disallow\",\"os\":{\"name\":\"windows\"}}]") as JsonArray;

        Assert.False(RuleEvaluator.IsAllowed(rules, "windows", "x86_64"));
        Assert.True(RuleEvaluator.IsAllowed(rules, "linux", "x86_64"));
    }

    [Fact]
    public void FeatureRule_UsesFalseForMissingFeature()
    {
        var rules = JsonNode.Parse("[{\"action\":\"allow\",\"features\":{\"is_demo_user\":true}}]") as JsonArray;

        Assert.False(RuleEvaluator.IsAllowed(rules, "windows", "x86_64"));
        Assert.True(RuleEvaluator.IsAllowed(rules, "windows", "x86_64",
            new Dictionary<string, bool> { ["is_demo_user"] = true }));
    }
}
