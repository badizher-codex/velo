using VELO.Core.Containers;
using VELO.Data.Models;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 4.0 chunk D — coverage for the static policy hub that flips
/// fingerprint protection level per container. Pure unit tests; no WPF
/// or runtime state involved.
/// </summary>
public class CouncilContainerPolicyTests
{
    [Theory]
    [InlineData("council-claude",  true)]
    [InlineData("council-chatgpt", true)]
    [InlineData("council-grok",    true)]
    [InlineData("council-ollama",  true)]
    [InlineData("none",            false)]
    [InlineData("banking",         false)]
    [InlineData("work",            false)]
    [InlineData("",                false)]
    [InlineData("council-",        false)]   // prefix only — not a seeded slot
    [InlineData("council-other",   false)]   // seeded set is closed
    [InlineData("Council-Claude",  false)]   // case-sensitive by design (DB IDs are lowercase)
    public void Applies_byContainerId_matchesOnlyTheFourSeededSlots(string id, bool expected)
    {
        Assert.Equal(expected, CouncilContainerPolicy.Applies(id));
    }

    [Fact]
    public void Applies_nullContainer_isFalse()
    {
        Assert.False(CouncilContainerPolicy.Applies((string?)null));
        Assert.False(CouncilContainerPolicy.Applies((Container?)null));
    }

    [Fact]
    public void Applies_containerInstance_delegatesToId()
    {
        var council = new Container { Id = "council-grok",   Name = "Grok",   Color = "#808080" };
        var work    = new Container { Id = "work",           Name = "Work",   Color = "#808080" };
        Assert.True(CouncilContainerPolicy.Applies(council));
        Assert.False(CouncilContainerPolicy.Applies(work));
    }

    [Theory]
    [InlineData("council-claude",  "Aggressive", "Standard")]
    [InlineData("council-chatgpt", "Off",        "Standard")]
    [InlineData("council-grok",    "Standard",   "Standard")]
    [InlineData("council-ollama",  "Custom",     "Standard")]
    public void ResolveFingerprintLevel_councilSlots_downgradeToStandard(
        string id, string global, string expected)
    {
        Assert.Equal(expected, CouncilContainerPolicy.ResolveFingerprintLevel(id, global));
    }

    [Theory]
    [InlineData("none",      "Aggressive")]
    [InlineData("banking",   "Aggressive")]
    [InlineData("work",      "Off")]
    [InlineData("personal",  "Custom")]
    [InlineData(null,        "Aggressive")]
    public void ResolveFingerprintLevel_nonCouncil_keepsGlobalLevel(string? id, string global)
    {
        Assert.Equal(global, CouncilContainerPolicy.ResolveFingerprintLevel(id, global));
    }

    [Fact]
    public void CouncilContainerIds_listIsExactlyFour()
    {
        Assert.Equal(CouncilLayoutControllerExpectedPanelCount, CouncilContainerPolicy.CouncilContainerIds.Count);
    }

    // CouncilLayoutController lives in VELO.App and that assembly is not
    // referenced here. Hardcoding the expected count keeps the assertion
    // sharp without crossing the project boundary; if the panel count ever
    // changes the test will fail loudly and the maintainer updates it once.
    private const int CouncilLayoutControllerExpectedPanelCount = 4;

    [Fact]
    public void CouncilFingerprintLevel_isStandard()
    {
        Assert.Equal("Standard", CouncilContainerPolicy.CouncilFingerprintLevel);
    }
}
