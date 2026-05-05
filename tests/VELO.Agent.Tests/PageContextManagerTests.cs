using VELO.Agent;
using Xunit;

namespace VELO.Agent.Tests;

public class PageContextManagerTests
{
    // ── Spec § 7.4 #3 ─────────────────────────────────────────────────────

    [Fact]
    public void PageContext_InjectedAsSystemPrompt_OnFirstMessage()
    {
        var mgr = new PageContextManager();
        mgr.Prime("tab-1", "https://example.com/article",
            "Sample Article", "Body text about widgets.");
        var prompt = mgr.BuildSystemPrompt("tab-1");
        Assert.Contains("https://example.com/article", prompt);
        Assert.Contains("Sample Article", prompt);
        Assert.Contains("widgets", prompt);
        Assert.Contains("VeloAgent", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyForUnprimedTab()
    {
        var mgr = new PageContextManager();
        Assert.Equal("", mgr.BuildSystemPrompt("never-primed"));
    }

    [Fact]
    public void MarkSent_SuppressesPromptOnSubsequentTurns()
    {
        var mgr = new PageContextManager();
        mgr.Prime("tab-1", "u", "t", "c");
        Assert.NotEqual("", mgr.BuildSystemPrompt("tab-1"));
        mgr.MarkSent("tab-1");
        Assert.Equal("", mgr.BuildSystemPrompt("tab-1"));
        // Content cache stays available for inspection.
        Assert.Equal("c", mgr.GetContent("tab-1"));
    }

    [Fact]
    public void Reprime_ResetsPromptDelivery()
    {
        var mgr = new PageContextManager();
        mgr.Prime("tab-1", "u1", "t1", "c1");
        mgr.MarkSent("tab-1");
        Assert.Equal("", mgr.BuildSystemPrompt("tab-1"));

        // Re-priming (e.g. user clicked "Ask about this page" again on a
        // new article in the same tab) makes the prompt land again.
        mgr.Prime("tab-1", "u2", "t2", "c2");
        Assert.Contains("u2", mgr.BuildSystemPrompt("tab-1"));
    }

    [Fact]
    public void Prime_ClipsLongContent_ToMaxChars()
    {
        var mgr = new PageContextManager { MaxContentChars = 100 };
        var bigContent = new string('x', 5000);
        mgr.Prime("tab-1", "u", "t", bigContent);
        Assert.Equal(100, mgr.GetContent("tab-1").Length);
    }

    [Fact]
    public void Forget_DropsTabState()
    {
        var mgr = new PageContextManager();
        mgr.Prime("tab-1", "u", "t", "c");
        mgr.Forget("tab-1");
        Assert.False(mgr.IsPrimed("tab-1"));
        Assert.Equal("", mgr.GetContent("tab-1"));
    }

    [Fact]
    public void IsPrimed_FalseWhenNotPrimed()
    {
        var mgr = new PageContextManager();
        Assert.False(mgr.IsPrimed("nope"));
        mgr.Prime("yes", "u", "t", "c");
        Assert.True(mgr.IsPrimed("yes"));
        Assert.False(mgr.IsPrimed("nope"));
    }

    // ── Spec § 7.4 #4 ─────────────────────────────────────────────────────

    [Fact]
    public void PageContext_MarksSwitchOnTabChange()
    {
        var sep = PageContextManager.BuildTabSwitchSeparator("https://news.com/article");
        Assert.Contains("Contexto cambiado", sep);
        Assert.Contains("https://news.com/article", sep);
    }

    [Fact]
    public void TabSwitchSeparator_HandlesEmptyUrl()
    {
        var sep = PageContextManager.BuildTabSwitchSeparator("");
        Assert.Contains("(sin URL)", sep);
    }

    [Fact]
    public void EmptyTabId_IgnoresPrime()
    {
        var mgr = new PageContextManager();
        mgr.Prime("", "u", "t", "c"); // should silently no-op
        Assert.False(mgr.IsPrimed(""));
    }
}
